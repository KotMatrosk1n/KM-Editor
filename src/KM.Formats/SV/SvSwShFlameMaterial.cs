// SPDX-License-Identifier: GPL-3.0-only
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.SV;

public sealed record SvSwShFlameTemplate(byte[] Model, int Core, int Mask);
public sealed record SvSwShFlameTextures(string Layer, string Noise, string Control, string White, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>Fits the S/V unlit flame surface to the native paired flame shader contract.</summary>
public static class SvSwShFlameMaterial
{
    public static bool IsUndisplaced(byte[] sourceMaterials)
    {
        var source = new ModelBuffer(sourceMaterials);
        return Scalar(source, Flame(source), "DisplacementHeight") == 0;
    }

    public static PreviewPrimitive AtlasGeometry(PreviewPrimitive primitive)
    {
        var vertices = (float[])primitive.Vertices.Clone();
        for (var triangle = 0; triangle < primitive.Indices.Length; triangle += 3)
            for (var channel = 6; channel < 8; channel++)
            {
                var tile = MathF.Floor(vertices[checked((int)primitive.Indices[triangle] * 16 + channel)]);
                if (!float.IsFinite(tile) || Enumerable.Range(1, 2).Any(corner =>
                    MathF.Floor(vertices[checked((int)primitive.Indices[triangle + corner] * 16 + channel)]) != tile))
                    throw new InvalidDataException("A flame triangle crosses source UV tiles.");
            }
        // Trinity applies the material transform inside the authored UV tile.
        // Native fire shaders transform raw UVs before addressing the texture.
        foreach (var index in primitive.Indices.Distinct())
            for (var channel = 6; channel < 8; channel++)
            {
                var at = checked((int)index * 16 + channel);
                vertices[at] -= MathF.Floor(vertices[at]);
            }
        return primitive with { Vertices = vertices };
    }

    public static byte[] NormalizeName(byte[] bytes, string sourceName)
    {
        if (sourceName == "fire") return bytes;
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var materials = source.Tables(source.Root, 1, 128);
        if (materials.Any(t => source.Text(t, 0) == "fire")) throw new InvalidDataException("S/V flame material identity is ambiguous.");
        var selected = materials.Where(t => source.Text(t, 0) == sourceName).ToArray();
        if (selected.Length != 1) throw new InvalidDataException("S/V flame material identity is missing or duplicated.");
        writer.Text(selected[0], 0, "fire");
        return writer.Finish(source.Root);
    }

    public static SvSwShFlameTemplate Template(byte[] bodyModel, byte[] flameModel, byte[] sourceMaterials, byte[]? sharedMaterials = null,
        int linkId = 0, float colorExposure = 1)
    {
        if (linkId is < 0 or > 7) throw new InvalidDataException("Native flame pair identity exceeds its supported range.");
        if (!float.IsFinite(colorExposure) || colorExposure is <= 0 or > 1)
            throw new InvalidDataException("Native flame color exposure is invalid.");
        var source = new ModelBuffer(sourceMaterials); var flame = Flame(source);
        if (sharedMaterials is not null)
        {
            var reference = new ModelBuffer(sharedMaterials);
            string[] Bindings(ModelBuffer data, int material) => data.Tables(material, 2, 64)
                .Select(t => data.Text(t, 0) + ":" + data.Text(t, 1)).Order(StringComparer.Ordinal).ToArray();
            if (!Bindings(source, flame).SequenceEqual(Bindings(reference, Flame(reference))))
                throw new InvalidDataException("S/V flame palettes require separate texture conversion.");
        }
        var colors = source.Tables(flame, 7, 128).ToDictionary(t => source.Text(t, 0)!, t =>
        {
            var at = source.Field(t, 1);
            return at == 0 ? new float[4] : Enumerable.Range(0, 4).Select(i => source.Float(at + 4 * i)).ToArray();
        }, StringComparer.Ordinal);
        float[] Color(string name) => colors.TryGetValue(name, out var color) && color.All(float.IsFinite)
            ? color[..3].Select(channel => channel * colorExposure).ToArray() : throw new InvalidDataException("S/V flame palette is incomplete.");
        if (!colors.GetValueOrDefault("BaseColor", [1f, 1, 1, 1]).SequenceEqual([1f, 1, 1, 1])
            || Scalar(source, flame, "LayerMaskScale1") != 1 || Scalar(source, flame, "LayerMaskScale2") != 1)
            throw new InvalidDataException("S/V flame base tint or layer weights require another adapter.");
        var native = new ModelBuffer(bodyModel); var fire = new ModelBuffer(flameModel);
        var originals = native.Tables(native.Root, 6, 128);
        foreach (var material in originals.Where(t => (native.Text(t, 1) ?? "").StartsWith("PokeFire", StringComparison.Ordinal)))
        {
            var links = native.Tables(material, 13, 256).Where(t => native.Text(t, 0) == "MatLinkID").ToArray();
            if (links.Length != 1) throw new InvalidDataException("Native flame pair identity is missing or ambiguous.");
            var at = native.Field(links[0], 1);
            if ((at == 0 ? 0 : native.Float(at)) == linkId)
                throw new InvalidDataException("Native flame pair identity is already assigned.");
        }
        var selected = new[] { "PokeFireCoreShader1", "PokeFireMaskShader1" }.Select(shader =>
            fire.Tables(fire.Root, 6, 128).Single(t => fire.Text(t, 1) == shader)).ToArray();
        var writer = new ModelTableBuilder(bodyModel); var slots = writer.Vector(native.Root, 6, originals.Length + 2);
        var bodyCopy = writer.Append(bodyModel); var fireCopy = writer.Append(flameModel);
        for (var i = 0; i < originals.Length; i++) writer.Pointer(slots[i], bodyCopy + originals[i]);
        for (var i = 0; i < selected.Length; i++) writer.Pointer(slots[originals.Length + i], fireCopy + selected[i]);
        foreach (var field in new[] { 3, 5 })
        {
            string[] Names(ModelBuffer data)
            {
                var (at, count) = data.Vector(data.Root, field, 4, 256);
                return Enumerable.Range(0, count).Select(i => data.StringAt(at + 4 * i)).ToArray();
            }
            var fireNames = Names(fire);
            var names = Names(native).Concat(fireNames).Distinct(StringComparer.Ordinal).ToArray();
            var entries = writer.Vector(native.Root, field, names.Length);
            for (var i = 0; i < names.Length; i++) writer.Pointer(entries[i], writer.String(names[i]));
            // Material program indices address the corresponding model shader list.
            // The flame donor's indices change when its list is merged with the body.
            var programField = field == 3 ? 8 : 10;
            foreach (var material in selected)
            {
                var originalIndex = checked((int)fire.Value(material, programField));
                if ((uint)originalIndex >= fireNames.Length)
                    throw new InvalidDataException("Native flame shader index is invalid.");
                writer.Int32(writer.Field(fireCopy + material, programField), Array.IndexOf(names, fireNames[originalIndex]));
            }
        }
        var palette = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            ["BaseColor0"] = Color("BaseColorLayer1"), ["BaseColor1"] = Color("BaseColorLayer1"),
            ["LayerColor0"] = Color("BaseColorLayer2"), ["LayerColor1"] = Color("BaseColorLayer2"),
            ["OnGameColor"] = [1, 1, 1], ["ConstantColor0"] = [1, 1, 1],
        };
        var uv = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (parameter, prefix) in new[] { ("UVScaleOffset", "Blend0"), ("UVScaleOffset3", "Blend1") })
        {
            var value = colors.GetValueOrDefault(parameter, [1, 1, 0, 0]);
            if (value.Length != 4 || value.Any(v => !float.IsFinite(v))) throw new InvalidDataException("S/V flame UV transform is invalid.");
            if (Scalar(source, flame, "DisplacementHeight") == 0 && (value[0] <= 0 || value[1] <= 0 || value[2] != 0 || value[3] != 0))
                throw new InvalidDataException("S/V flame atlas transform requires another adapter.");
            string[] names = ["UVScaleU", "UVScaleV", "UVTranslateU", "UVTranslateV"];
            for (var i = 0; i < names.Length; i++) uv.Add(prefix + names[i], value[i]);
        }
        foreach (var material in selected)
            foreach (var field in new[] { 13, 14 })
            {
                var parameters = fire.Tables(material, field, 256); var entries = writer.Vector(fireCopy + material, field, parameters.Length);
                var parameterCopy = writer.Append(flameModel);
                for (var i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i]; var name = fire.Text(parameter, 0)!;
                    float[]? value = field == 14 ? palette.GetValueOrDefault(name) : name switch
                    {
                        "MatLinkID" => [linkId],
                        "RimStrength" or "BaseRimVal" or "LayerRimVal" or "ColorLerpValue" or "ConstantColor0Val" => [0],
                        "OnGameEmissionVal" or "OnGameColorVal" or "OnGameAlpha" => [1],
                        _ => uv.TryGetValue(name, out var coordinate) ? [coordinate] : null,
                    };
                    if (value is null) { writer.Pointer(entries[i], parameterCopy + parameter); continue; }
                    var replacement = writer.Table(2, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Floats(value) },
                        new Dictionary<int, int> { [1] = value.Length * 4 });
                    writer.Pointer(entries[i], replacement); writer.Text(replacement, 0, name);
                }
            }
        if (Scalar(source, flame, "DisplacementHeight") == 0)
        {
            var layer = source.Tables(flame, 2, 64).Single(t => source.Text(t, 0) == "LayerMaskMap");
            var samplers = source.Tables(flame, 3, 32);
            var index = checked((int)source.Value(layer, 2));
            if ((uint)index >= samplers.Length) throw new InvalidDataException("S/V flame sampler is missing.");
            int Address(int field) => TrinityTextureAddressing.SwSh(source.Value(samplers[index], field));
            var binding = fire.Tables(selected[0], 11, 64).Single(t => fire.Text(t, 0) == "Blend0Tex");
            var sampler = fire.Table(binding, 2);
            var values = Enumerable.Range(0, 9).ToDictionary(f => f,
                f => f is 1 or 2 ? ModelTableBuilder.Integer(checked((int)Address(f + 8)))
                    : fire.Field(sampler, f) is var at && at != 0 ? fire.Slice(at, 4).ToArray() : new byte[4]);
            writer.Reference(fireCopy + binding, 2, writer.Table(9, values));
        }
        return new(writer.Finish(native.Root), originals.Length, originals.Length + 1);
    }

    public static SvSwShFlameTextures Textures(byte[] sourceMaterials, Func<string, byte[]> read, CancellationToken token = default,
        ModelTextureMipCache? encodingCache = null)
        => Textures(sourceMaterials, read, false, token, encodingCache);

    public static SvSwShFlameTextures ZaTextures(byte[] sourceMaterials, Func<string, byte[]> read, CancellationToken token = default,
        ModelTextureMipCache? encodingCache = null)
        => Textures(sourceMaterials, read, true, token, encodingCache);

    private static SvSwShFlameTextures Textures(byte[] sourceMaterials, Func<string, byte[]> read, bool zaFlame, CancellationToken token, ModelTextureMipCache? encodingCache)
    {
        var source = new ModelBuffer(sourceMaterials); var flame = Flame(source);
        var textures = source.Tables(flame, 2, 64).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!, StringComparer.Ordinal);
        if (textures.Count != 3 || !textures.ContainsKey("BaseColorMap") || !textures.ContainsKey("LayerMaskMap") || !textures.ContainsKey("DisplacementMap"))
            throw new InvalidDataException("S/V flame texture bindings require another adapter.");
        var sourceLayer = read(textures["LayerMaskMap"]); var layer = new ModelTextureDocument(sourceLayer); var pixels = layer.Pixels();
        // Compressed masks can have a few quantization steps in unused channels.
        if (Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 2] > 4 || pixels[i * 4 + 3] > 4))
            throw new InvalidDataException("S/V flame has unsupported additional color layers.");
        var whiteBytes = read(textures["BaseColorMap"]); var white = new ModelTextureDocument(whiteBytes);
        if (white.Pixels().Any(v => v != 255)) throw new InvalidDataException("S/V flame base color is not neutral.");
        var displacement = PreviewTexture.ReadBaseLevel(read(textures["DisplacementMap"]));
        var height = Scalar(source, flame, "DisplacementHeight");
        // The verified Z-A surfaces use .08/.09 for the two Charizard X flames,
        // and .12 for the existing single-flame contract. Keep other contracts closed.
        if (!float.IsFinite(height) || (zaFlame ? height is not (.08f or .09f or .12f) : height is < 0 or > .11f))
            throw new InvalidDataException("Flame displacement needs another native adapter.");
        // An undisplaced surface does not sample its placeholder noise texture.
        if (height != 0 && displacement.Format is not (0x1d01 or 0x0b01))
            throw new InvalidDataException("S/V flame noise requires another texture adapter.");
        var independentNoise = height != 0 && (displacement.Width != layer.Width || displacement.Height != layer.Height);
        // PreviewTexture resolves authored channel mapping before returning RGBA.
        // Sample its red channel. The Sinnoh and Z-A flames use mapped RGBA noise.
        var noise = height == 0 ? [] : displacement.Format == 0x1d01
            ? new BcDecoder().DecodeRaw(displacement.Blocks, displacement.Width, displacement.Height, CompressionFormat.Bc4).Select(pixel => pixel.r).ToArray()
            : Enumerable.Range(0, displacement.Width * displacement.Height).Select(i => displacement.Blocks[i * 4]).ToArray();
        var noiseWidth = independentNoise ? displacement.Width : layer.Width;
        var noiseHeight = independentNoise ? displacement.Height : layer.Height;
        var noisePixels = new byte[checked(noiseWidth * noiseHeight * 4)];
        for (var i = 0; i < pixels.Length / 4; i++)
        {
            var green = pixels[i * 4 + 1];
            for (var c = 0; c < 3; c++) pixels[i * 4 + c] = green;
            pixels[i * 4 + 3] = 255;
        }
        for (var i = 0; i < noisePixels.Length / 4; i++)
        {
            for (var c = 0; c < 3; c++) noisePixels[i * 4 + c] = height == 0 ? (byte)0 : (byte)Math.Clamp((int)MathF.Round(noise[i] * height), 0, 255);
            noisePixels[i * 4 + 3] = 255;
        }
        var original = Path.GetFileNameWithoutExtension(textures["LayerMaskMap"]);
        if (!original.EndsWith("_lym", StringComparison.Ordinal)) throw new InvalidDataException("S/V flame layer identity is unsupported.");
        var layerName = original[..^3] + "col"; var noiseName = original[..^3] + "mix"; var controlName = original[..^3] + "ctl";
        byte[] Encode(string name, byte[] rgba, int imageWidth, int imageHeight)
        {
            var layout = imageWidth == layer.Width && imageHeight == layer.Height ? sourceLayer
                : SwShTextureAllocation.Create(sourceLayer, imageWidth, imageHeight, false);
            var container = new ModelTextureDocument(ModelTextureIdentity.Rename(layout, original, name, linear: true));
            var mips = new byte[container.MipCount][];
            for (var mip = 0; mip < mips.Length; mip++)
            {
                var width = Math.Max(1, imageWidth >> mip); var height = Math.Max(1, imageHeight >> mip); var level = new byte[width * height * 4];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) for (var c = 0; c < 4; c++)
                {
                    var sum = 0; var count = 0;
                    for (var sy = y * imageHeight / height; sy < (y + 1) * imageHeight / height; sy++)
                        for (var sx = x * imageWidth / width; sx < (x + 1) * imageWidth / width; sx++) { sum += rgba[(sy * imageWidth + sx) * 4 + c]; count++; }
                    level[(y * width + x) * 4 + c] = (byte)((sum + count / 2) / count);
                }
                mips[mip] = level;
            }
            return container.EncodeMipImages(mips, token, encodingCache);
        }
        // The native shader computes clamp(control.r + (1-control.a) *
        // (layer0.r + layer1.r), 0, 1). A transparent black control keeps both
        // scrolling inputs active. Source noise perturbs the color boundary rather
        // than displacing vertices; the source silhouette is retained.
        var whiteName = Path.GetFileNameWithoutExtension(textures["BaseColorMap"]);
        return new(layerName, noiseName, controlName, whiteName, new Dictionary<string, byte[]>
        { [layerName] = Encode(layerName, pixels, layer.Width, layer.Height), [noiseName] = Encode(noiseName, noisePixels, noiseWidth, noiseHeight),
            [controlName] = independentNoise ? Encode(controlName, new byte[4 * 4 * 4], 4, 4)
                : Encode(controlName, new byte[pixels.Length], layer.Width, layer.Height), [whiteName] = whiteBytes });
    }

    private static int Flame(ModelBuffer source)
    {
        var materials = source.Tables(source.Root, 1, 128).Where(t => source.Text(t, 0) == "fire").ToArray();
        if (materials.Length != 1 || !source.Tables(materials[0], 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["Unlit"]))
            throw new InvalidDataException("S/V flame material requires another shader adapter.");
        if (Scalar(source, materials[0], "DiscardValue") != 0 || Scalar(source, materials[0], "EmissionIntensity") != 1)
            throw new InvalidDataException("S/V flame opacity or emission requires another adapter.");
        return materials[0];
    }
    private static float Scalar(ModelBuffer source, int material, string name)
    {
        var entry = source.Tables(material, 4, 128).Single(t => source.Text(t, 0) == name);
        var at = source.Field(entry, 1); return at == 0 ? 0 : source.Float(at);
    }
}
