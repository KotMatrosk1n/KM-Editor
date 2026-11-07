// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

/// <summary>Adapts Z-A facial material channels and native expression timing independently of S/V.</summary>
public static partial class ZaSwShAnimationWriter
{
    // Some source clips retain a material from an absent effect mesh. Its
    // curves have no consumer in the authored model; never remove a bound one.
    public static byte[] RemoveUnboundMaterial(byte[] bytes, string name, PreviewScene scene)
    {
        if (name is not ("smog" or "eye") || scene.Primitives.Any(p => p.Material.Name == name))
            throw new InvalidDataException("An unbound source animation material is now bound.");
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var retained = new List<(int Slot, int Target)>();
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4); if (timeline == 0) continue;
            var materials = source.Tables(timeline, 2, 128).Where(m => source.Text(m, 0) != name).ToArray();
            var slots = writer.Vector(timeline, 2, materials.Length);
            for (var i = 0; i < materials.Length; i++) retained.Add((slots[i], materials[i]));
        }
        // Native offsets are unsigned and must point forward. Keep one exact
        // copy of the retained graph after the replacement vectors.
        var copy = writer.Append(bytes);
        foreach (var (slot, target) in retained) writer.Pointer(slot, copy + target);
        return writer.Finish(source.Root);
    }

    public static byte[] Controller(byte[] nativeController, string donorStem, string targetStem,
        IReadOnlyList<string> materialNames, IReadOnlyDictionary<string, string[]> facialBones, bool layeredEyes = false, bool independentHighlights = false, bool layerEmission = false, bool layerTint = false)
    {
        var bytes = SwShAnimationWriter.Controller(nativeController, donorStem, targetStem, materialNames, facialBones, compactMaterials: true);
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            var masks = source.Table(source.Table(layer, 2), 1);
            foreach (var material in source.Tables(masks, 1, 128))
            {
                if (!(source.Text(material, 0) ?? "").Contains("eye", StringComparison.Ordinal)) continue;
                var floats = source.Table(material, 2);
                var names = source.Tables(floats, 1, 256).Select(t => source.Text(t, 0)!).ToArray();
                if (!names.Contains("ColorUVTranslateU", StringComparer.Ordinal)) continue;
                names = names.Concat(new[] { "ColorUVScaleU", "ColorUVScaleV", "ConstantColorVal" })
                    .Concat(layeredEyes ? ZaSwShEyelidMaterial.ScalarNames : [])
                    .Concat(independentHighlights ? ZaSwShEyelidMaterial.HighlightScalarNames : [])
                    .Concat(layerEmission || layerTint ? ZaSwShEyelidMaterial.EmissionScalarNames : [])
                    .Distinct(StringComparer.Ordinal).ToArray();
                var slots = writer.Vector(floats, 1, names.Length);
                for (var i = 0; i < names.Length; i++) { var name = writer.Table(1); writer.Pointer(slots[i], name); writer.Text(name, 0, names[i]); }
                if (layeredEyes)
                {
                    var vectors = source.Table(material, 3);
                    var vectorNames = source.Tables(vectors, 1, 256).Select(parameter => source.Text(parameter, 0)!)
                        .Concat(ZaSwShEyelidMaterial.VectorNames)
                        .Concat(layerEmission || layerTint ? ZaSwShEyelidMaterial.EmissionVectorNames : []).Distinct(StringComparer.Ordinal).ToArray();
                    if (vectors == 0) throw new InvalidDataException("Eye vector controller mask is missing.");
                    var mode = source.Field(vectors, 0);
                    var replacement = writer.Table(2, new Dictionary<int, byte[]> { [0] = [mode == 0 ? (byte)0 : source.U8(mode)] });
                    writer.Reference(material, 3, replacement);
                    var vectorSlots = writer.Vector(replacement, 1, vectorNames.Length);
                    for (var index = 0; index < vectorNames.Length; index++)
                    {
                        var parameter = writer.Table(1); writer.Pointer(vectorSlots[index], parameter); writer.Text(parameter, 0, vectorNames[index]);
                    }
                }
            }
        }
        return writer.Finish(source.Root);
    }

    public static PreviewMaterialTrack[] ReadMaterials(byte[] bytes, PreviewClip skeletal) => ReadClip(bytes, skeletal).Materials;

    public static PreviewClip ReadFlameClip(byte[] bytes, PreviewClip skeletal)
    {
        var decoded = KM.Formats.SV.SvSwShFlameAnimation.Read(bytes, skeletal);
        return decoded with { Materials = MergeBakedHighlights(decoded.Materials) };
    }

    public static PreviewClip ReadFlameClip(byte[] bytes, PreviewClip skeletal, IReadOnlyCollection<string> flameMaterials)
    {
        var decoded = KM.Formats.SV.SvSwShFlameAnimation.ReadMany(bytes, skeletal, flameMaterials);
        return decoded with { Materials = MergeBakedHighlights(decoded.Materials) };
    }

    public static PreviewClip ReadClip(byte[] bytes, PreviewClip skeletal, bool supportVisibility = false, string? emissionMaterial = null,
        IReadOnlyDictionary<string, ZaSwShEyelidMaterial.Contract>? eyes = null)
    {
        var source = new ModelBuffer(bytes); var sanitized = new ModelTableBuilder(bytes);
        var config = source.Table(source.Root, 0);
        var rate = source.Value(config, 2, (uint)skeletal.Rate);
        if (rate == 0) throw new InvalidDataException("Z-A material animation rate is invalid.");
        var ratio = skeletal.Rate / (float)rate;
        var scalars = new List<PreviewMaterialTrack>();
        var eyeSources = new List<PreviewMaterialTrack>();
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4);
            if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var name = source.Text(material, 0) ?? throw new InvalidDataException("Z-A material track is unnamed.");
                if (eyes?.ContainsKey(name) == true)
                {
                    eyeSources.AddRange(ZaSwShEyelidMaterial.ReadTracks(source, material, ratio));
                    sanitized.Vector(material, 1, 0); sanitized.Vector(material, 2, 0);
                    continue;
                }
                foreach (var vector in source.Tables(material, 2, 256))
                {
                    var parameter = source.Text(vector, 0);
                    if (parameter is "UVScaleOffset" or "UVScaleOffset1") continue;
                    throw new InvalidDataException($"Z-A material animation needs another native adapter: {name}/{parameter}.");
                }
                var entries = source.Tables(material, 1, 256);
                if (entries.Length == 0) continue;
                foreach (var entry in entries)
                {
                    var parameter = source.Text(entry, 0);
                    if (parameter != "LayerMaskScale5" && !(parameter == "EmissionIntensityLayer2" && name == emissionMaterial))
                        throw new InvalidDataException($"Z-A scalar material animation needs another adapter: {parameter}");
                    var curve = source.Table(entry, 1);
                    var keys = source.Tables(curve, 0, 18000).Select(key =>
                    {
                        float Value(int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
                        return new PreviewKey(Value(0) * ratio, [Value(1)]);
                    }).ToArray();
                    Validate(keys); scalars.Add(new(name, parameter, [keys]));
                }
                sanitized.Vector(material, 1, 0);
            }
        }
        var warnings = new List<string>();
        var decoded = PreviewMaterialAnimation.Read(new(sanitized.Finish(source.Root)), skeletal, warnings, true);
        if (warnings.Count != 0 || (!supportVisibility && decoded.Visibility.Any(v => v.Keys.Any(k => k.Value.Any(value => value == 0)))))
            throw new InvalidDataException("Z-A visibility or morph animation needs another adapter.");
        if (decoded.Materials.Any(t => t.Parameter is not ("UVScaleOffset" or "UVScaleOffset1")))
            throw new InvalidDataException("Z-A vector material animation needs another adapter.");
        var result = UniqueTracks(decoded.Materials.Concat(scalars));
        result = MergeBakedHighlights(result);
        if (eyes is not null)
            result = result.Concat(eyes.Values.SelectMany(contract => ZaSwShEyelidMaterial.Tracks(contract,
                UniqueTracks(eyeSources.Where(track => track.Material == contract.Name)), skeletal.Frames))).ToArray();
        return decoded with { Materials = result };
    }

    private static PreviewMaterialTrack[] UniqueTracks(IEnumerable<PreviewMaterialTrack> tracks) => tracks
        .GroupBy(track => (track.Material, track.Parameter)).Select(group =>
        {
            var first = group.First();
            if (group.Any(track => track.Channels.Length != first.Channels.Length || track.Channels.Zip(first.Channels)
                .Any(pair => pair.First.Length != pair.Second.Length || pair.First.Zip(pair.Second)
                    .Any(keys => keys.First.Frame != keys.Second.Frame || !keys.First.Value.SequenceEqual(keys.Second.Value)))))
                throw new InvalidDataException("Z-A material animation identity is ambiguous.");
            return first;
        }).ToArray();

    private static PreviewMaterialTrack[] MergeBakedHighlights(PreviewMaterialTrack[] result)
    {
        foreach (var group in result.GroupBy(t => t.Material))
        {
            var color = group.SingleOrDefault(t => t.Parameter == "UVScaleOffset");
            var highlight = group.SingleOrDefault(t => t.Parameter == "UVScaleOffset1");
            if (color is null || highlight is null) continue;
            if (color.Channels.Length != highlight.Channels.Length || color.Channels.Zip(highlight.Channels).Any(c =>
                c.First.Length != c.Second.Length || c.First.Zip(c.Second).Any(k => k.First.Frame != k.Second.Frame || !k.First.Value.SequenceEqual(k.Second.Value))))
                throw new InvalidDataException("Independent Z-A UV channels need another material adapter.");
            // Identical authored color and highlight curves share the baked image.
            result = result.Where(t => t != highlight).ToArray();
        }
        return result;
    }

    public static byte[] Convert(byte[] skeleton, PreviewMaterialTrack[] materials, PreviewBone[] bones,
        IReadOnlyDictionary<string, string> aliases, bool completePose, SwShExpressionCurve expression = SwShExpressionCurve.None,
        bool emptyExpression = false)
    {
        var bytes = SwShAnimationWriter.Convert(skeleton, [], bones, aliases, completePose, expression);
        if (expression != SwShExpressionCurve.None)
            bytes = ExpressionSkeleton(bytes, expression, emptyExpression);
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var frames = checked((int)source.Value(source.Table(source.Root, 0), 1));
        if (frames is < 1 or > 18000) throw new InvalidDataException("Converted Z-A animation timing is invalid.");
        var nativeMaterials = writer.Table(1); writer.Reference(source.Root, 2, nativeMaterials);
        var groups = (emptyExpression ? [] : materials).GroupBy(t => t.Material, StringComparer.Ordinal).ToArray();
        var materialSlots = writer.Vector(nativeMaterials, 0, groups.Length);
        var shared = materials.Any(track => track.Parameter.StartsWith("EyeScalar:", StringComparison.Ordinal));
        var curves = new List<(int Slot, int Components, bool Constant, byte[] Payload)>();
        var strings = new List<(int Slot, string Value)>();
        void Text(int table, int field, string value)
        {
            if (shared) strings.Add((writer.Field(table, field), value));
            else writer.Text(table, field, value);
        }
        void Curve(int table, int components, bool constant, float[] values)
        {
            var payload = ModelTableBuilder.Floats(values);
            if (shared) curves.Add((writer.Field(table, 2), components, constant, payload));
            else
            {
                var curve = writer.Table(1, sizes: constant ? new Dictionary<int, int> { [0] = components * 4 } : null);
                writer.Reference(table, 2, curve);
                if (constant) writer.Write(writer.Field(curve, 0), payload);
                else writer.Blob(curve, 0, payload, frames);
            }
        }
        for (var i = 0; i < groups.Length; i++)
        {
            var native = new List<(string Name, float[] Values)>();
            var vectors = new List<(string Name, float[][] Values)>();
            float Frame(int frame) => expression == SwShExpressionCurve.None ? frame : EyeSample(frame);
            foreach (var track in groups[i])
            {
                if (track.Parameter.StartsWith("EyeScalar:", StringComparison.Ordinal))
                {
                    if (track.Channels.Length != 1) throw new InvalidDataException("Eye scalar channel is invalid.");
                    Validate(track.Channels[0]);
                    native.Add((track.Parameter["EyeScalar:".Length..], Enumerable.Range(0, frames).Select(frame => Sample(track.Channels[0], Frame(frame))).ToArray()));
                }
                else if (track.Parameter.StartsWith("EyeVector:", StringComparison.Ordinal))
                {
                    if (track.Channels.Length != 3) throw new InvalidDataException("Eye vector channels are invalid.");
                    foreach (var channel in track.Channels) Validate(channel);
                    vectors.Add((track.Parameter["EyeVector:".Length..], Enumerable.Range(0, frames)
                        .Select(frame => track.Channels.Select(channel => Sample(channel, Frame(frame))).ToArray()).ToArray()));
                }
                else if (track.Parameter == "SurfaceAlpha")
                {
                    if (track.Channels.Length != 1) throw new InvalidDataException("Surface alpha has invalid channels.");
                    Validate(track.Channels[0]);
                    if (track.Channels[0].Any(k => k.Value[0] is < 0 or > 1)) throw new InvalidDataException("Surface alpha is outside its range.");
                    native.Add(("ConstantAlpha", Enumerable.Range(0, frames).Select(frame => Sample(track.Channels[0], Frame(frame))).ToArray()));
                }
                else if (track.Parameter is "LayerMaskScale5" or "EmissionIntensityLayer2")
                {
                    if (track.Channels.Length != 1) throw new InvalidDataException("Z-A eye override has invalid channels.");
                    Validate(track.Channels[0]);
                    if (track.Channels[0].Any(k => k.Value[0] is < 0 or > 1)) throw new InvalidDataException("Z-A eye override is outside its range.");
                    var emission = track.Parameter == "EmissionIntensityLayer2";
                    native.Add((emission ? "EmissionMaskVal" : "ConstantColorVal", Enumerable.Range(0, frames)
                        .Select(frame => emission ? Sample(track.Channels[0], Frame(frame)) : 1 - Sample(track.Channels[0], Frame(frame))).ToArray()));
                }
                else if (track.Parameter is "UVScaleOffset" or "UVScaleOffset1" or "UnderlayUV")
                {
                    if (track.Channels.Length != 4) throw new InvalidDataException("Z-A highlight UV animation has invalid channels.");
                    foreach (var channel in track.Channels) Validate(channel);
                    string[] names = track.Parameter == "UnderlayUV"
                        ? ["Layer1UVScaleU", "Layer1UVScaleV", "Layer1UVTranslateU", "Layer1UVTranslateV"]
                        : ["ColorUVScaleU", "ColorUVScaleV", "ColorUVTranslateU", "ColorUVTranslateV"];
                    for (var c = 0; c < 4; c++)
                    {
                        var component = c;
                        native.Add((names[c], Enumerable.Range(0, frames).Select(f => component == 3
                            ? 1 - Sample(track.Channels[1], Frame(f)) - Sample(track.Channels[3], Frame(f))
                            : Sample(track.Channels[component], Frame(f))).ToArray()));
                    }
                }
                else throw new InvalidDataException($"Z-A material animation has no native mapping: {track.Material}/{track.Parameter}.");
            }
            var material = writer.Table(4); writer.Pointer(materialSlots[i], material); Text(material, 0, groups[i].Key);
            writer.Vector(material, 1, 0);
            var vectorSlots = writer.Vector(material, 3, vectors.Count);
            for (var index = 0; index < vectors.Count; index++)
            {
                var (name, values) = vectors[index]; var constant = values.All(value => value.SequenceEqual(values[0]));
                var vector = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
                writer.Pointer(vectorSlots[index], vector); Text(vector, 0, name);
                Curve(vector, 3, constant, constant ? values[0] : values.SelectMany(value => value).ToArray());
            }
            var scalarSlots = writer.Vector(material, 2, native.Count);
            for (var j = 0; j < native.Count; j++)
            {
                var (name, values) = native[j]; var constant = values.All(v => v == values[0]);
                var scalar = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
                writer.Pointer(scalarSlots[j], scalar); Text(scalar, 0, name);
                Curve(scalar, 1, constant, constant ? [values[0]] : values);
            }
        }
        foreach (var group in curves.GroupBy(curve => (curve.Components, curve.Constant, Bytes: System.Convert.ToHexString(curve.Payload))))
        {
            var first = group.First();
            var curve = writer.Table(1, sizes: first.Constant ? new Dictionary<int, int> { [0] = first.Components * 4 } : null);
            foreach (var reference in group) writer.Pointer(reference.Slot, curve);
            if (first.Constant) writer.Write(writer.Field(curve, 0), first.Payload);
            else writer.Blob(curve, 0, first.Payload, frames);
        }
        foreach (var group in strings.GroupBy(entry => entry.Value, StringComparer.Ordinal))
        {
            var text = writer.String(group.Key);
            foreach (var reference in group) writer.Pointer(reference.Slot, text);
        }
        return writer.Finish(source.Root);
    }

    public static byte[] Visibility(byte[] animation, IReadOnlyList<PreviewVisibility> tracks, IReadOnlySet<string> meshNames)
    {
        var source = new ModelBuffer(animation);
        var frames = checked((int)source.Value(source.Table(source.Root, 0), 1));
        if (frames is < 1 or > 18000 || tracks.Count > 128 || tracks.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != tracks.Count)
            throw new InvalidDataException("Z-A mesh visibility exceeds native limits.");
        var writer = new ModelTableBuilder(); var root = writer.Table(5, absent: new HashSet<int> { 4 });
        var copy = writer.Append(animation);
        for (var field = 0; field < 3; field++) writer.Reference(root, field, copy + source.Table(source.Root, field));
        var visibility = writer.Table(1); writer.Reference(root, 3, visibility);
        var slots = writer.Vector(visibility, 0, tracks.Count);
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i]; Validate(track.Keys);
            if (!meshNames.Contains(track.Name) || track.Keys[0].Frame != 0 || track.Keys.Any(k => k.Frame >= frames || k.Value[0] is not (0 or 1)))
                throw new InvalidDataException("Z-A mesh visibility has invalid targets or samples.");
            var constant = track.Keys.All(k => k.Value[0] == track.Keys[0].Value[0]);
            var target = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
            writer.Pointer(slots[i], target); writer.Text(target, 0, track.Name);
            var curve = writer.Table(1); writer.Reference(target, 2, curve);
            if (constant) writer.Int32(writer.Field(curve, 0), (int)track.Keys[0].Value[0]);
            else
            {
                var packed = new byte[(frames + 7) / 8]; var key = 0;
                for (var frame = 0; frame < frames; frame++)
                {
                    while (key + 1 < track.Keys.Length && track.Keys[key + 1].Frame <= frame) key++;
                    if (track.Keys[key].Value[0] != 0) packed[frame / 8] |= (byte)(1 << (frame % 8));
                }
                writer.Blob(curve, 0, packed, packed.Length);
            }
        }
        return writer.Finish(root);
    }

    private static void Validate(PreviewKey[] keys)
    {
        if (keys.Length is < 1 or > 18000 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || k.Value.Length != 1 || !float.IsFinite(k.Value[0]))
            || keys.Zip(keys.Skip(1)).Any(pair => pair.First.Frame >= pair.Second.Frame)) throw new InvalidDataException("Z-A material keys are invalid.");
    }
    private static float Sample(PreviewKey[] keys, float frame)
    {
        if (frame <= keys[0].Frame) return keys[0].Value[0];
        for (var i = 1; i < keys.Length; i++) if (frame < keys[i].Frame)
            return keys[i - 1].Step ? keys[i - 1].Value[0]
                : MathF.FusedMultiplyAdd(keys[i].Value[0] - keys[i - 1].Value[0],
                    (frame - keys[i - 1].Frame) / (keys[i].Frame - keys[i - 1].Frame), keys[i - 1].Value[0]);
        return keys[^1].Value[0];
    }
    private static float EyeSample(int frame)
    {
        int[] frames = [0, 10, 20, 30, 40, 50, 60, 80]; int[] samples = [0, 5, 10, 0, 5, 10, 0, 0];
        for (var i = 1; i < frames.Length; i++) if (frame < frames[i])
            return samples[i - 1] + (samples[i] - samples[i - 1]) * (frame - frames[i - 1]) / (float)(frames[i] - frames[i - 1]);
        return 0;
    }
}
