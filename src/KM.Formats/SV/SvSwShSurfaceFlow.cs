// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Retains qualified scrolling surface controls independently from the body rig.</summary>
public static class SvSwShSurfaceFlow
{
    public const string ProgramName = "SurfaceFlow";
    public const string LayerProgramName = "SurfaceLayers";
    public const string SourceLayerProgramName = "SourceSurfaceLayers";
    private static readonly string[] Parameters = ["Layer1UVScaleU", "Layer1UVScaleV", "Layer1UVTranslateU", "Layer1UVTranslateV"];
    private static readonly string[] ColorParameters = ["ColorUVScaleU", "ColorUVScaleV", "ColorUVTranslateU", "ColorUVTranslateV"];
    public sealed record Contract(string Name, string? Noise, float Strength, Vector4 Transform, bool MaskScroll, Vector3[] Colors,
        int WrapU, int WrapV, int BaseWrapU, int BaseWrapV, bool SourceMask = false);
    public static string Program(IReadOnlyDictionary<string, Contract> contracts) => contracts.Values.All(c => c.SourceMask) ? SourceLayerProgramName
        : contracts.Values.All(c => c.MaskScroll) ? LayerProgramName
        : contracts.Values.All(c => !c.MaskScroll) ? ProgramName : throw new InvalidDataException("Mixed surface flow programs are not qualified.");

    public static IReadOnlyDictionary<string, Contract> Contracts(byte[] bytes, PreviewScene scene, Func<string, byte[]> read)
    {
        var zorua = scene.Primitives.Any(p => p.Name.StartsWith("pm0570_00_41_", StringComparison.Ordinal));
        var excadrill = scene.Primitives.Any(p => p.Name.StartsWith("pm0530_51_00_", StringComparison.Ordinal));
        var basculegionStems = new[] { "pm1006_11_41", "pm1006_12_41" }
            .Where(stem => scene.Primitives.Any(p => p.Name.StartsWith(stem + "_", StringComparison.Ordinal))).ToArray();
        if (basculegionStems.Length > 1) throw new InvalidDataException("Surface flow cannot mix Basculegion source forms.");
        var basculegion = basculegionStems.Length == 1;
        var scrollingMask = scene.Primitives.Any(p => p.Name.StartsWith("pm0089_00_11_", StringComparison.Ordinal));
        string[] expected = scene.Primitives.Any(p => p.Name.StartsWith("pm0088_", StringComparison.Ordinal)) ? ["body", "body_00"]
            : scene.Primitives.Any(p => p.Name.StartsWith("pm0089_00_00_", StringComparison.Ordinal)) ? ["body"]
            : scrollingMask ? ["body_a_01"]
            : scene.Primitives.Any(p => p.Name.StartsWith("pm0571_00_41_", StringComparison.Ordinal)) ? ["body_a_00", "body_b"]
            : zorua ? ["body_a_01", "body_b_01"]
            : excadrill ? ["body_a_01"]
            : basculegion ? ["effect"]
            : throw new InvalidDataException("Surface flow requires a qualified source model.");
        var source = new ModelBuffer(bytes); var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            var name = source.Text(material, 0)!;
            if (!expected.Contains(name, StringComparer.Ordinal)) continue;
            if (!scene.Primitives.Any(p => p.Material.Name == name)
                || !source.Tables(material, 1, 16).Select(s => source.Text(s, 0)).SequenceEqual([excadrill ? "IkCharacter" : "Standard"]))
                throw new InvalidDataException("Surface flow material does not match its source geometry.");
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
            {
                var at = source.Field(p, 1);
                return at == 0 ? Vector4.Zero : new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
            });
            var textures = source.Tables(material, 2, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!);
            var maskScroll = scrollingMask || excadrill;
            var uvIndex = source.Tables(material, 9, 128).SingleOrDefault(p => source.Text(p, 0) == "UVIndexLayerMask");
            if (maskScroll && (uvIndex == 0 || source.Value(uvIndex, 1) != 1
                || scene.Primitives.Where(p => p.Material.Name == name).Any(p => p.SecondaryUv?.Length != p.Vertices.Length / 8)))
                throw new InvalidDataException("Surface mask requires the qualified second source texture coordinate set.");
            var transform = vectors.GetValueOrDefault(maskScroll ? "UVScaleOffsetLayerMask" : "UVScaleOffset3", new(1, 1, 0, 0));
            var strength = maskScroll ? 0 : scalars.GetValueOrDefault("DisplacementHeight", float.NaN);
            if (!float.IsFinite(strength) || strength is < 0 or > .15f
                || !float.IsFinite(transform.X + transform.Y + transform.Z + transform.W)
                || transform.X <= 0 || transform.Y <= 0
                || vectors.GetValueOrDefault("UVScaleOffset", new(1, 1, 0, 0)) != new Vector4(1, 1, 0, 0))
                throw new InvalidDataException("Surface flow coordinates require another adapter.");
            var colors = Enumerable.Range(1, 4).Select(i => vectors.GetValueOrDefault("BaseColorLayer" + i, Vector4.One)).ToArray();
            if (maskScroll && (vectors.GetValueOrDefault("BaseColor", Vector4.One) != Vector4.One
                || Enumerable.Range(1, 4).Any(i => scalars.GetValueOrDefault("LayerMaskScale" + i) != 1)
                || colors.Any(c => !float.IsFinite(c.X + c.Y + c.Z + c.W) || c.X is < 0 or > 1 || c.Y is < 0 or > 1 || c.Z is < 0 or > 1)))
                throw new InvalidDataException("Source layer colors require another sampling adapter.");
            if (excadrill && (transform != new Vector4(1, 1, 0, 0)
                || scalars.GetValueOrDefault("EmissionIntensity") != 0
                || Enumerable.Range(1, 4).Any(i => scalars.GetValueOrDefault("EmissionIntensityLayer" + i) != 0)
                || scene.Primitives.Where(p => p.Material.Name == name).Any(p => p.Name is not
                    ("pm0530_51_00_body_a_mesh_shape" or "pm0530_51_00_drill_mesh_shape"))))
                throw new InvalidDataException("Excadrill requires its qualified secondary mask and nonemissive source palette.");
            if (!maskScroll && !textures.ContainsKey("DisplacementMap")) throw new InvalidDataException("Surface flow noise is missing.");
            if ((zorua || basculegion) && (strength != (zorua ? .05f : .036f)
                || transform != (zorua ? new Vector4(1, 1, 0, 0) : new Vector4(1, .5f, 1, 0))
                || textures["DisplacementMap"] != (zorua ? "pm0570_00_41_fire_a_msk.bntx" : basculegionStems[0] + "_eff_msk.bntx")))
                throw new InvalidDataException("Source surface flow requires its qualified displacement contract.");
            var texture = source.Tables(material, 2, 128).Single(t => source.Text(t, 0) == (maskScroll ? "LayerMaskMap" : "DisplacementMap"));
            var samplers = source.Tables(material, 3, 32); var samplerIndex = checked((int)source.Value(texture, 2));
            if ((uint)samplerIndex >= samplers.Length) throw new InvalidDataException("Surface flow sampler is missing.");
            int Address(int field) => TrinityTextureAddressing.SwSh(source.Value(samplers[samplerIndex], field));
            var baseTexture = source.Tables(material, 2, 128).Single(t => source.Text(t, 0) == "BaseColorMap");
            var baseSampler = checked((int)source.Value(baseTexture, 2));
            if ((uint)baseSampler >= samplers.Length) throw new InvalidDataException("Surface base sampler is missing.");
            int BaseAddress(int field) => TrinityTextureAddressing.SwSh(source.Value(samplers[baseSampler], field));
            result.Add(name, new(name, textures[maskScroll ? "LayerMaskMap" : "DisplacementMap"], strength, transform, maskScroll,
                colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray(), Address(9), Address(10), BaseAddress(9), BaseAddress(10), excadrill));
        }
        if (result.Count == 0 || result.Count != expected.Count(name => scene.Primitives.Any(p => p.Material.Name == name))
            || (zorua || basculegion) && result.Count != expected.Length)
            throw new InvalidDataException("Surface flow source contract is incomplete.");
        return result;
    }

    public static PreviewScene RequireSecondaryMaskScene(byte[] bytes, PreviewScene scene, IReadOnlyDictionary<string, Contract> contracts)
    {
        var source = new ModelBuffer(bytes);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            var selector = source.Tables(material, 9, 128).SingleOrDefault(p => source.Text(p, 0) == "UVIndexLayerMask");
            var index = selector == 0 ? uint.MaxValue : source.Value(selector, 1);
            if (index is not (0 or uint.MaxValue)
                && (index != 1 || !contracts.TryGetValue(source.Text(material, 0)!, out var contract) || !contract.SourceMask))
                throw new InvalidDataException("Source mask has an unqualified secondary UV selector.");
        }
        return scene with { Rig = scene.Rig with { Warnings = scene.Rig.Warnings.Where(w => w != "materialUvUnsupported").ToArray() } };
    }

    public static byte[] Bind(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts, IReadOnlyDictionary<string, string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        int ProgramIndex(int field)
        {
            var (at, count) = source.Vector(source.Root, field, 4, 256);
            return Enumerable.Range(0, count).Single(i => source.StringAt(at + i * 4) == Program(contracts));
        }
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            if (!names.TryGetValue(source.Text(material, 0)!, out var original) || !contracts.TryGetValue(original, out var contract)) continue;
            writer.Int32(writer.Field(material, 8), ProgramIndex(3)); writer.Int32(writer.Field(material, 10), ProgramIndex(5));
            var values = new Dictionary<string, float>(StringComparer.Ordinal) { ["ColorLerpValue"] = contract.Strength };
            for (var i = 0; i < 4; i++) values[Parameters[i]] = contract.Transform[i];
            foreach (var parameter in source.Tables(material, 13, 256))
                if (values.TryGetValue(source.Text(parameter, 0)!, out var value)) writer.Single(writer.Field(parameter, 1), value);
            if (contract.MaskScroll) foreach (var parameter in source.Tables(material, 14, 256))
            {
                var index = Array.IndexOf(SvSwShLayerEmission.Vectors, source.Text(parameter, 0));
                if (index >= 0) for (var channel = 0; channel < 3; channel++) writer.Single(source.Field(parameter, 1) + channel * 4, contract.Colors[index][channel]);
            }
            void Sampler(string role, int horizontal, int vertical)
            {
                var texture = source.Tables(material, 11, 64).Single(t => source.Text(t, 0) == role);
                var sampler = source.Table(texture, 2);
                var values = Enumerable.Range(0, 9).ToDictionary(f => f,
                    f => f is 1 or 2 ? ModelTableBuilder.Integer(f == 1 ? horizontal : vertical)
                        : source.Field(sampler, f) is var at && at != 0 ? source.Slice(at, 4).ToArray() : new byte[4]);
                writer.Reference(texture, 2, writer.Table(9, values));
            }
            Sampler("LyCol0Tex", contract.WrapU, contract.WrapV);
            Sampler("Col0Tex", contract.BaseWrapU, contract.BaseWrapV);
        }
        return writer.Finish(source.Root);
    }

    public static PreviewClip Read(byte[] bytes, PreviewClip skeleton, IReadOnlyDictionary<string, Contract> contracts)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4); if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
                foreach (var parameter in source.Tables(material, 2, 256))
                {
                    var name = source.Text(parameter, 0);
                    if (name != "UVScaleOffsetLayerMask") continue;
                    if (!contracts.TryGetValue(source.Text(material, 0)!, out var contract) || !contract.MaskScroll)
                        throw new InvalidDataException("Animated source mask has an undeclared material.");
                    writer.Text(parameter, 0, "UVScaleOffset3");
                }
        }
        var decoded = SvSwShFlameAnimation.ReadMany(writer.Finish(source.Root), skeleton, contracts.Keys.ToArray(), mergeIdenticalTracks: true);
        var tracks = decoded.Materials.GroupBy(t => (t.Material, t.Parameter)).Select(group =>
        {
            var first = group.First();
            if (group.Any(track => !Equal(first, track))) throw new InvalidDataException("Source surface timelines disagree on shared material controls.");
            return first;
        }).ToArray();
        if (contracts.Values.Any(c => c.SourceMask))
        {
            var colors = tracks.Where(t => t.Parameter.StartsWith("BaseColorLayer", StringComparison.Ordinal)).ToArray();
            if (colors.Any(t => !contracts.TryGetValue(t.Material, out var c) || !c.SourceMask || t.Parameter != "BaseColorLayer3"
                || t.Channels.Length != 4 || t.Channels.Any(k => k.Length == 0)
                || t.Channels[3].Any(k => k.Value.Length != 1 || k.Value[0] != 1)))
                throw new InvalidDataException("Secondary mask color animation differs from its source contract.");
            var retained = tracks.Except(colors).ToList();
            foreach (var contract in contracts.Values.Where(c => c.SourceMask))
                for (var layer = 1; layer <= 4; layer++)
                {
                    retained.Add(new(contract.Name, "EmissionIntensityLayer" + layer, [[new(0, [0])]]));
                    var animated = colors.SingleOrDefault(t => t.Material == contract.Name && t.Parameter == "BaseColorLayer" + layer);
                    var color = contract.Colors[layer - 1];
                    retained.Add(animated is not null ? animated with { Parameter = "EmissionColorLayer" + layer }
                        : new(contract.Name, "EmissionColorLayer" + layer, new[] { color.X, color.Y, color.Z, 1 }
                            .Select(v => new[] { new PreviewKey(0, [v]) }).ToArray()));
                }
            tracks = retained.ToArray();
        }
        return decoded with { Materials = tracks };
    }

    private static bool Equal(PreviewMaterialTrack first, PreviewMaterialTrack next) => first.Channels.Length == next.Channels.Length
        && first.Channels.Zip(next.Channels).All(channels => channels.First.Length == channels.Second.Length
            && channels.First.Zip(channels.Second).All(keys => keys.First.Frame == keys.Second.Frame && keys.First.Value.SequenceEqual(keys.Second.Value)));

    public static byte[] Animation(byte[] bytes, IReadOnlyList<PreviewMaterialTrack> tracks, PreviewClip? loop = null, bool includeColor = false)
    {
        var groups = tracks.Where(t => t.Parameter == "UVScaleOffset3" || includeColor && t.Parameter == "UVScaleOffset")
            .GroupBy(t => t.Material).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        if (groups.Count == 0) return bytes;
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder();
        var root = writer.Table(5, absent: Enumerable.Range(0, 5).Where(f => f != 2 && source.Table(source.Root, f) == 0).ToHashSet());
        var parent = writer.Table(1); writer.Reference(root, 2, parent);
        var existing = source.Tables(source.Table(source.Root, 2), 0, 128);
        var names = existing.Select(t => source.Text(t, 0)!).Concat(groups.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var slots = writer.Vector(parent, 0, names.Length);
        var references = new List<(int Offset, int Target)>();
        var frames = loop?.Frames ?? checked((int)source.Value(source.Table(source.Root, 0), 1));
        if (frames is < 1 or > 18000) throw new InvalidDataException("Surface flow timeline exceeds its native limit.");
        if (loop is not null) writer.Reference(root, 0, writer.Table(3, new Dictionary<int, byte[]>
            { [0] = ModelTableBuilder.Integer(1), [1] = ModelTableBuilder.Integer(frames), [2] = ModelTableBuilder.Integer(loop.Rate) }));
        for (var i = 0; i < names.Length; i++)
        {
            var original = existing.SingleOrDefault(t => source.Text(t, 0) == names[i]);
            if (!groups.TryGetValue(names[i], out var track)) { references.Add((slots[i], original)); continue; }
            if (track.Channels.Length != 4) throw new InvalidDataException("Surface flow needs four UV channels.");
            var parameters = track.Parameter == "UVScaleOffset3" ? Parameters : ColorParameters;
            var target = writer.Table(4); writer.Pointer(slots[i], target); writer.Text(target, 0, names[i]);
            foreach (var field in new[] { 1, 3 })
                if (original != 0 && source.Table(original, field) is var table && table != 0) references.Add((writer.Field(target, field), table));
                else writer.Vector(target, field, 0);
            var retained = original == 0 ? [] : source.Tables(original, 2, 256).Where(t => !parameters.Contains(source.Text(t, 0), StringComparer.Ordinal)).ToArray();
            var populated = Enumerable.Range(0, 4).Where(c => track.Channels[c].Length != 0).ToArray();
            var values = writer.Vector(target, 2, retained.Length + populated.Length);
            for (var j = 0; j < retained.Length; j++) references.Add((values[j], retained[j]));
            for (var j = 0; j < populated.Length; j++)
            {
                var channel = populated[j]; var keys = track.Channels[channel];
                if (keys.Length > 18000 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || k.Value.Length != 1 || !float.IsFinite(k.Value[0]))
                    || keys.Zip(keys.Skip(1)).Any(pair => pair.First.Frame >= pair.Second.Frame)) throw new InvalidDataException("Surface flow samples are invalid.");
                var constant = keys.All(k => k.Value[0] == keys[0].Value[0]);
                var value = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
                writer.Pointer(values[retained.Length + j], value); writer.Text(value, 0, parameters[channel]);
                var curve = writer.Table(1); writer.Reference(value, 2, curve);
                float Sample(int frame)
                {
                    if (frame <= keys[0].Frame) return keys[0].Value[0];
                    for (var k = 1; k < keys.Length; k++) if (frame < keys[k].Frame)
                        return keys[k - 1].Value[0] + (keys[k].Value[0] - keys[k - 1].Value[0]) * (frame - keys[k - 1].Frame) / (keys[k].Frame - keys[k - 1].Frame);
                    return keys[^1].Value[0];
                }
                if (constant) writer.Single(writer.Field(curve, 0), keys[0].Value[0]);
                else writer.Blob(curve, 0, ModelTableBuilder.Floats(Enumerable.Range(0, frames).Select(Sample).ToArray()), frames);
            }
        }
        var copy = writer.Append(bytes);
        foreach (var reference in references) writer.Pointer(reference.Offset, copy + reference.Target);
        foreach (var field in new[] { 0, 1, 3, 4 })
            if ((field != 0 || loop is null) && source.Table(source.Root, field) is var table && table != 0)
                writer.Reference(root, field, copy + table);
        return writer.Finish(root);
    }

    public static byte[] GlobalLoop(byte[] bytes, IReadOnlySet<string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var replacements = new List<(int[] Slots, int[] Entries)>();
        foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128).Where(t => names.Contains(source.Text(t, 0)!)))
        {
            var retained = source.Tables(material, 2, 256).Where(t => source.Text(t, 0) != "ColorLerpValue"
                && !Parameters.Contains(source.Text(t, 0), StringComparer.Ordinal)
                && !ColorParameters.Contains(source.Text(t, 0), StringComparer.Ordinal)).ToArray();
            var slots = writer.Vector(material, 2, retained.Length);
            replacements.Add((slots, retained));
        }
        var copy = writer.Append(bytes);
        foreach (var (slots, entries) in replacements) for (var i = 0; i < entries.Length; i++) writer.Pointer(slots[i], copy + entries[i]);
        return writer.Finish(source.Root);
    }

    public static byte[] Controller(byte[] bytes, IReadOnlySet<string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            if (source.Text(layer, 1) is "eye01" or "mouth01") continue;
            var masks = source.Table(source.Table(layer, 2), 1);
            foreach (var material in source.Tables(masks, 1, 128).Where(t => names.Contains(source.Text(t, 0)!)))
            {
                var floats = source.Table(material, 2);
                var parameters = source.Tables(floats, 1, 256).Select(t => source.Text(t, 0)!).Concat(Parameters).Distinct(StringComparer.Ordinal).ToArray();
                var replacement = writer.Table(2, new Dictionary<int, byte[]> { [0] = [source.Field(floats, 0) is var at && at != 0 ? source.U8(at) : (byte)0] });
                writer.Reference(material, 2, replacement);
                var slots = writer.Vector(replacement, 1, parameters.Length);
                for (var i = 0; i < parameters.Length; i++) { var name = writer.Table(1); writer.Pointer(slots[i], name); writer.Text(name, 0, parameters[i]); }
            }
        }
        return writer.Finish(source.Root);
    }
}
