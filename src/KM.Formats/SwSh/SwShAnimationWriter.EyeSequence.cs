// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    // Layered eyes have independent scalar and vector curves. Concatenate the
    // converted native samples directly, without round-tripping through UV0.
    private static byte[] EyeMaterialSequence(IReadOnlyList<byte[]> segments, PreviewBone[] bones)
    {
        var sources = segments.Select(bytes => new ModelBuffer(bytes)).ToArray();
        var frames = sources.Select(source => PreviewRigReader.Animation(source, "segment", bones).Frames).ToArray();
        var total = frames.Sum();
        if (total is < 1 or > 18000) throw new InvalidDataException("Eye sequence exceeds its frame budget.");
        var allowedScalars = ZaSwShEyelidMaterial.ScalarNames.Concat(ZaSwShEyelidMaterial.HighlightScalarNames)
            .Concat(ZaSwShEyelidMaterial.EmissionScalarNames)
            .Append("ConstantColorVal").ToHashSet(StringComparer.Ordinal);
        var allowedVectors = ZaSwShEyelidMaterial.VectorNames.Concat(ZaSwShEyelidMaterial.EmissionVectorNames).ToHashSet(StringComparer.Ordinal);
        var tracks = sources.Select((source, segment) =>
        {
            var result = new Dictionary<(string Material, int Field, string Parameter), float[]>();
            foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128))
            {
                var name = source.Text(material, 0) ?? throw new InvalidDataException("Eye sequence material is unnamed.");
                if (source.Tables(material, 1, 256).Length != 0)
                    throw new InvalidDataException("Eye sequence sampler animation needs another adapter.");
                foreach (var field in new[] { 2, 3 }) foreach (var track in source.Tables(material, field, 256))
                {
                    var parameter = source.Text(track, 0) ?? throw new InvalidDataException("Eye sequence parameter is unnamed.");
                    if (!(field == 2 ? allowedScalars : allowedVectors).Contains(parameter))
                        throw new InvalidDataException("Eye sequence parameter needs another adapter.");
                    if (!result.TryAdd((name, field, parameter), EyeSequenceSamples(source, track, frames[segment], field == 2 ? 1 : 3)))
                        throw new InvalidDataException("Eye sequence parameter is duplicated.");
                }
            }
            return result;
        }).ToArray();
        if (tracks.Any(part => part.Count != tracks[0].Count || part.Keys.Any(key => !tracks[0].ContainsKey(key))))
            throw new InvalidDataException("Eye sequence material targets differ.");
        var sanitized = segments.Select((bytes, index) =>
        {
            var writer = new ModelTableBuilder(bytes); var material = writer.Table(1);
            writer.Reference(sources[index].Root, 2, material); writer.Vector(material, 0, 0);
            return writer.Finish(sources[index].Root);
        }).ToArray();
        // The established path still checks rates, loops, visibility and bones.
        var sequence = SkeletalSequence(sanitized, bones);
        var data = new ModelBuffer(sequence); var output = new ModelTableBuilder(sequence);
        var root = output.Table(1); output.Reference(data.Root, 2, root);
        var groups = tracks[0].Keys.GroupBy(key => key.Material).ToArray();
        var slots = output.Vector(root, 0, groups.Length);
        var expected = new Dictionary<(string Material, int Field, string Parameter), float[]>();
        for (var index = 0; index < groups.Length; index++)
        {
            var target = output.Table(4); output.Pointer(slots[index], target); output.Text(target, 0, groups[index].Key);
            output.Vector(target, 1, 0);
            foreach (var field in new[] { 2, 3 })
            {
                var keys = groups[index].Where(key => key.Field == field).ToArray();
                var parameters = output.Vector(target, field, keys.Length);
                for (var parameter = 0; parameter < keys.Length; parameter++)
                {
                    var key = keys[parameter]; var components = field == 2 ? 1 : 3;
                    var values = tracks.SelectMany(part => part[key]).ToArray(); expected.Add(key, values);
                    var constant = Enumerable.Range(0, total).All(frame => values.AsSpan(frame * components, components).SequenceEqual(values.AsSpan(0, components)));
                    var entry = output.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
                    output.Pointer(parameters[parameter], entry); output.Text(entry, 0, key.Parameter);
                    var curve = output.Table(1, sizes: new Dictionary<int, int> { [0] = constant ? components * 4 : 4 });
                    output.Reference(entry, 2, curve);
                    if (constant) output.Write(output.Field(curve, 0), ModelTableBuilder.Floats(values[..components]));
                    else output.Blob(curve, 0, ModelTableBuilder.Floats(values), total);
                }
            }
        }
        var result = output.Finish(data.Root); var verified = new ModelBuffer(result); var verifiedCount = 0;
        foreach (var material in verified.Tables(verified.Table(verified.Root, 2), 0, 128))
            foreach (var field in new[] { 2, 3 }) foreach (var track in verified.Tables(material, field, 256))
            {
                var key = (verified.Text(material, 0)!, field, verified.Text(track, 0)!);
                if (!expected.TryGetValue(key, out var values) || !EyeSequenceSamples(verified, track, total, field == 2 ? 1 : 3).SequenceEqual(values))
                    throw new InvalidDataException("Eye sequence failed material verification.");
                verifiedCount++;
            }
        if (verifiedCount != expected.Count) throw new InvalidDataException("Eye sequence material verification is incomplete.");
        return result;
    }

    private static float[] EyeSequenceSamples(ModelBuffer source, int track, int frames, int components)
    {
        var kind = source.Value(track, 1); var curve = source.Table(track, 2);
        if (kind is not (1 or 2)) throw new InvalidDataException("Eye sequence curve encoding needs another adapter.");
        var (start, count) = kind == 1 ? (source.Field(curve, 0), 1) : source.Vector(curve, 0, components * 4, 18000);
        if (start == 0 || count != (kind == 1 ? 1 : frames)) throw new InvalidDataException("Eye sequence samples are incomplete.");
        var values = Enumerable.Range(0, frames * components).Select(index => source.Float(start + (kind == 1 ? index % components : index) * 4)).ToArray();
        if (values.Any(value => !float.IsFinite(value) || Math.Abs(value) > 1_000_000))
            throw new InvalidDataException("Eye sequence sample is invalid.");
        return values;
    }
}
