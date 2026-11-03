using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

public static partial class ZaSwShEyelidMaterial
{
    public sealed record EyeTextures(IReadOnlyDictionary<string, string> Bindings, IReadOnlyDictionary<string, byte[]> Files);

    public static EyeTextures Textures(Contract contract, int index, bool shiny, Func<string, byte[]> read, CancellationToken token,
        ModelTextureMipCache? encodingCache = null)
    {
        if (index is < 0 or > 127) throw new InvalidDataException("Eye texture identity is outside the supported range.");
        var template = read(contract.Textures["BaseColorMap"]);
        var templateName = Path.GetFileNameWithoutExtension(contract.Textures["BaseColorMap"]);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        ZaSwShMaterialBaker.Image Image(string role) => ZaSwShMaterialBaker.Read(read(contract.Textures[role]));
        Vector4 Tint(string name, Vector4 fallback) => contract.Vectors.GetValueOrDefault(name, fallback);
        void Write(string role, char identity, int width, int height, byte[] pixels, bool srgb, bool normals = false, bool exact = false)
        {
            var name = ModelTextureIdentity.MaterialName(templateName, index, identity, shiny ? 's' : 'n', lowerCase: true);
            var allocated = SwShTextureAllocation.Create(template, width, height, srgb, compressed: !exact && role is not ("EmissionMaskTex" or "LyCol0Tex"));
            var document = new ModelTextureDocument(ModelTextureIdentity.Rename(allocated, templateName, name));
            files.Add(name, document.EncodeMipImages(SwShTextureFiltering.Mips(pixels, width, height, document.MipCount, srgb, normals), token, encodingCache));
            bindings.Add(role, name);
        }
        if (contract.LayerEmission || contract.LayerTint)
        {
            var layerMask = Image("LayerMaskMap");
            if (layerMask.Srgb) throw new InvalidDataException("Eye emission mask must be linear.");
            var originalName = Path.GetFileNameWithoutExtension(contract.Textures["LayerMaskMap"]);
            var name = ModelTextureIdentity.MaterialName(originalName, index, 'e', shiny ? 's' : 'n', lowerCase: true);
            files.Add(name, ModelTextureIdentity.Rename(read(contract.Textures["LayerMaskMap"]), originalName, name));
            bindings.Add("EffectTex", name);
        }
        if (contract.IndependentHighlights)
        {
            var highlight = Image("HighlightMaskMap");
            if (highlight.Srgb || highlight.Width > 512 || highlight.Height > 512)
                throw new InvalidDataException("Eye highlight allocation is unsupported.");
            var highlightName = Path.GetFileNameWithoutExtension(contract.Textures["HighlightMaskMap"]);
            var name = ModelTextureIdentity.MaterialName(highlightName, index, 'h', shiny ? 's' : 'n', lowerCase: true);
            files.Add(name, ModelTextureIdentity.Rename(read(contract.Textures["HighlightMaskMap"]),
                highlightName, name));
            bindings.Add("EffectTex", name);
        }
        // Mega Magearna retains a nonempty highlight image, but its fifth
        // emission layer is disabled in both palettes. The layered-emission
        // animation reader rejects fifth-layer intensity/color tracks, so it
        // cannot activate that image after conversion.
        else if (contract.Textures.ContainsKey("HighlightMaskMap")
            && (!contract.LayerEmission || contract.Scalars.GetValueOrDefault("EmissionIntensityLayer5", float.NaN) != 0)
            && Image("HighlightMaskMap").Pixels.Where((_, offset) => offset % 4 == 0).Any(value => value != 0))
            throw new InvalidDataException("Independent eye highlights need another adapter.");
        if (contract.Textures.ContainsKey("OcclusionMap") && Image("OcclusionMap").Pixels.Where((_, offset) => offset % 4 == 0).Any(value => value != 255))
            throw new InvalidDataException("Eye occlusion needs another adapter.");
        var original = Image("BaseColorMap"); var mask = Image("LayerMaskMap");
        var width = Math.Max(original.Width, mask.Width); var height = Math.Max(original.Height, mask.Height);
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            token.ThrowIfCancellationRequested();
            for (var column = 0; column < width; column++)
            {
                var uv = new Vector2((column + .5f) / width, (row + .5f) / height);
                var basis = ZaSwShMaterialBaker.Sample(original, uv, new(1, 1, 0, 0), TrinityTextureAddressing.Preview(contract.Wraps["BaseColorMap"])) * Tint("BaseColor", Vector4.One);
                var weights = ZaSwShMaterialBaker.Sample(mask, uv, new(1, 1, 0, 0), TrinityTextureAddressing.Preview(contract.Wraps["LayerMaskMap"]));
                var color = basis;
                for (var layer = 0; !contract.LayerTint && layer < 4; layer++)
                    color = Vector4.Lerp(color, basis * Tint("BaseColorLayer" + (layer + 1), Vector4.One),
                        Math.Clamp(weights[layer] * contract.Scalars.GetValueOrDefault("LayerMaskScale" + (layer + 1), 1), 0, 1));
                var offset = (row * width + column) * 4;
                for (var component = 0; component < 3; component++) pixels[offset + component] = Encode(color[component], true);
                pixels[offset + 3] = 255;
            }
        }
        Write("Col0Tex", 'z', width, height, pixels, true);
        foreach (var (sourceRole, targetRole, tintName, identity) in new[]
        {
            ("UpperEyelidColorMap", "EmissionMaskTex", "BaseColorLayer7", 'u'),
            ("LowerEyelidColorMap", "LyCol0Tex", "BaseColorLayer8", 'l'),
            ("EyelidShadowMaskMap", "AmbientTex", "BaseColorLayer6", 's')
        })
        {
            var shadow = targetRole == "AmbientTex";
            // An untinted eyelid already contains the required sRGB color and
            // alpha. Retain its compressed blocks and authored mip chain.
            if (!shadow && contract.Textures.TryGetValue(sourceRole, out var originalPath)
                && (Tint(tintName, Vector4.One) == Vector4.One || contract.SourceEyelidTintIdentity))
            {
                var bytes = read(originalPath);
                if (Tint(tintName, Vector4.One) != Vector4.One)
                    RequireWoChienIdentityTint(originalPath, bytes, Tint(tintName, Vector4.One));
                if (PreviewTexture.ReadBaseLevel(bytes).Format is 0x2006 or 0x1a06 or 0x1b06 or 0x1c06)
                {
                    var originalName = Path.GetFileNameWithoutExtension(originalPath);
                    var name = ModelTextureIdentity.MaterialName(originalName, index, identity, shiny ? 's' : 'n', lowerCase: true);
                    files.Add(name, ModelTextureIdentity.Rename(bytes, originalName, name));
                    bindings.Add(targetRole, name);
                    continue;
                }
            }
            var image = contract.Textures.ContainsKey(sourceRole) ? Image(sourceRole) : new ZaSwShMaterialBaker.Image(4, 4, false, new byte[64]);
            var output = new byte[image.Pixels.Length]; var tint = Tint(tintName, Vector4.One);
            for (var pixel = 0; pixel < image.Width * image.Height; pixel++)
            {
                var offset = pixel * 4;
                for (var component = 0; component < 3; component++)
                {
                    var value = shadow ? 1 + (tint[component] - 1) * image.Pixels[offset] / 255f
                        : Decode(image.Pixels[offset + component], image.Srgb) * tint[component];
                    output[offset + component] = Encode(value, !shadow);
                }
                output[offset + 3] = shadow ? (byte)255 : image.Pixels[offset + 3];
            }
            Write(targetRole, identity, image.Width, image.Height, output, !shadow);
        }
        var normal = Image("NormalMap");
        if (contract.NormalAtlasHalf is { } half)
        {
            if (half is not (0 or 1) || normal.Width != 1024 || normal.Height != 512 || normal.Srgb)
                throw new InvalidDataException("Eye normal atlas allocation is unsupported.");
            var cropped = new byte[512 * 512 * 4];
            for (var row = 0; row < 512; row++)
                normal.Pixels.AsSpan((row * 1024 + half * 512) * 4, 512 * 4).CopyTo(cropped.AsSpan(row * 512 * 4));
            normal = new(512, 512, false, cropped);
        }
        if (normal.Width > 512 || normal.Height > 512 || normal.Srgb) throw new InvalidDataException("Eye normal allocation is unsupported.");
        var normalPixels = normal.Pixels.ToArray();
        for (var pixel = 0; pixel < normal.Width * normal.Height; pixel++)
        {
            var offset = pixel * 4; var normalHeight = contract.Scalars.GetValueOrDefault("NormalHeight", 1);
            var horizontal = (normalPixels[offset] / 127.5f - 1) * normalHeight; var vertical = (normalPixels[offset + 1] / 127.5f - 1) * normalHeight;
            normalPixels[offset] = Encode(horizontal * .5f + .5f, false); normalPixels[offset + 1] = Encode(vertical * .5f + .5f, false);
            normalPixels[offset + 2] = Encode((MathF.Sqrt(MathF.Max(0, 1 - horizontal * horizontal - vertical * vertical)) + 1) * .5f, false);
            normalPixels[offset + 3] = 255;
        }
        Write("NormalMapTex", 'n', normal.Width, normal.Height, normalPixels, false, true, exact: contract.NormalAtlasHalf is not null);
        if (contract.Scalars.GetValueOrDefault("NormalHeight") == 0 && contract.Textures.ContainsKey("OpacityMap1"))
        {
            var emission = new byte[width * height * 4];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var uv = new Vector2((x + .5f) / width, (y + .5f) / height);
                var weights = ZaSwShMaterialBaker.Sample(mask, uv, new(1, 1, 0, 0), TrinityTextureAddressing.Preview(contract.Wraps["LayerMaskMap"]));
                var color = Vector3.Zero; float remaining = 1;
                for (var layer = 3; layer >= 0; layer--)
                {
                    var tint = Tint("EmissionColorLayer" + (layer + 1), Vector4.Zero);
                    var weight = Math.Clamp(weights[layer] * contract.Scalars.GetValueOrDefault("LayerMaskScale" + (layer + 1), 1), 0, 1);
                    color += new Vector3(tint.X, tint.Y, tint.Z) * (weight * remaining * contract.Scalars.GetValueOrDefault("EmissionIntensityLayer" + (layer + 1)));
                    remaining *= 1 - weight;
                }
                var at = (y * width + x) * 4;
                for (var c = 0; c < 3; c++) emission[at + c] = Encode(color[c] / 4, false);
                emission[at + 3] = 255;
            }
            Write("SphereMapTex", 'e', width, height, emission, false);
            var detail = Image("OpacityMap1"); Write("EffectTex", 'd', detail.Width, detail.Height, detail.Pixels, false);
        }
        return new(bindings, files);
    }

    private static float Decode(byte value, bool srgb)
    {
        var channel = value / 255f;
        return !srgb ? channel : channel <= .04045f ? channel / 12.92f : MathF.Pow((channel + .055f) / 1.055f, 2.4f);
    }

    private static void RequireWoChienIdentityTint(string path, byte[] bytes, Vector4 tint)
    {
        // The rare palette's exact 1.0000241 tint rounds to the same sRGB
        // byte at every authored mip. Prove identity instead of baking these
        // compressed eyelids into larger uncompressed copies.
        if (path is not ("pm1100_00_00_upper_eyelid_rare_alb.bntx" or "pm1100_00_00_lower_eyelid_rare_alb.bntx")
            || tint != new Vector4(1.0000241f, 1.0000241f, 1.0000241f, 1))
            throw new InvalidDataException("Wo-Chien eyelid tint requires its qualified source palette.");
        var image = new ModelTextureDocument(bytes);
        if (image.Format != 0x2006 || image.Width != 256 || image.Height != 256 || image.MipCount != 9)
            throw new InvalidDataException("Wo-Chien eyelid storage changed.");
        for (var level = 0; level < image.MipCount; level++)
        {
            var pixels = image.Pixels(level);
            for (var at = 0; at < pixels.Length; at += 4) for (var channel = 0; channel < 3; channel++)
                if (Encode(Decode(pixels[at + channel], true) * tint[channel], true) != pixels[at + channel])
                    throw new InvalidDataException("Wo-Chien eyelid tint is no longer byte identical.");
        }
    }

    private static byte Encode(float value, bool srgb)
    {
        if (srgb) value = value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
        return (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    }
}
