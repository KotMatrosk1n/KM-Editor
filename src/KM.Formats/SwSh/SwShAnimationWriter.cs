// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public enum SwShExpressionCurve { None, Eyes, Mouth }

/// <summary>Retains source skeletal curve encodings while adapting native playback and material channels.</summary>
public static partial class SwShAnimationWriter
{
    public static byte[] Convert(byte[] skeletonAnimation, PreviewMaterialTrack[] materialTracks,
        PreviewBone[] sourceBones, IReadOnlyDictionary<string, string> aliases, bool completePose,
        SwShExpressionCurve expression = SwShExpressionCurve.None, float scale = 100)
    {
        if (!float.IsFinite(scale) || scale <= 0 || sourceBones.Length is < 1 or > 255)
            throw new InvalidDataException("Animation conversion inputs are invalid.");
        var source = new ModelBuffer(skeletonAnimation);
        var validated = PreviewRigReader.Animation(source, "conversion", sourceBones);
        var sourceSkeleton = source.Table(source.Root, 1);
        var entries = source.Tables(sourceSkeleton, 0, 4096);
        var byName = sourceBones.ToDictionary(b => b.Name, StringComparer.Ordinal);
        // The reader has validated every repeated track's encoding and exact keys.
        // Retain the first source payload so conversion emits one track per bone.
        var retained = entries.Where(e => byName.ContainsKey(source.Text(e, 0) ?? ""))
            .DistinctBy(e => source.Text(e, 0), StringComparer.Ordinal).ToArray();
        var frames = expression == SwShExpressionCurve.None ? validated.Frames : 81;
        var rate = expression == SwShExpressionCurve.None ? validated.Rate : 30;
        var writer = new ModelTableBuilder(); var root = writer.Table(5, absent: new HashSet<int> { 3, 4 });
        var info = writer.Table(3, new Dictionary<int, byte[]>
        {
            [0] = ModelTableBuilder.Integer(validated.Loop ? 1 : 0), [1] = ModelTableBuilder.Integer(frames), [2] = ModelTableBuilder.Integer(rate),
        }); writer.Reference(root, 0, info);
        var skeleton = writer.Table(2, absent: source.Table(sourceSkeleton, 1) == 0 ? new HashSet<int> { 1 } : null);
        writer.Reference(root, 1, skeleton);
        var missing = completePose ? sourceBones.Where(b => !retained.Any(e => source.Text(e, 0) == b.Name)).ToArray() : [];
        var slots = writer.Vector(skeleton, 0, retained.Length + missing.Length);
        var targets = new int[slots.Length];
        for (var i = 0; i < targets.Length; i++) { targets[i] = writer.Table(7); writer.Pointer(slots[i], targets[i]); }
        var copy = writer.Append(skeletonAnimation);
        var scaled = new HashSet<int>();
        void ScaleVector(int address)
        {
            if (!scaled.Add(address)) return;
            for (var c = 0; c < 3; c++) writer.Single(copy + address + c * 4, source.Float(address + c * 4) * scale);
        }
        var init = source.Table(sourceSkeleton, 1);
        if (init != 0)
        {
            writer.Reference(skeleton, 1, copy + init);
            var transform = source.Field(init, 1);
            if (transform != 0) { source.Slice(transform, 40); ScaleVector(transform + 28); }
        }
        for (var i = 0; i < targets.Length; i++)
        {
            var entry = i < retained.Length ? retained[i] : 0;
            var name = entry == 0 ? missing[i - retained.Length].Name : source.Text(entry, 0)!;
            var bone = byName[name]; var target = targets[i]; writer.Text(target, 0, aliases.GetValueOrDefault(name, name));
            foreach (var field in new[] { 1, 3, 5 })
            {
                var typeAt = entry == 0 ? 0 : source.Field(entry, field); var kind = typeAt == 0 ? 0 : source.U8(typeAt);
                if (kind == 0)
                {
                    if (!completePose) continue;
                    var value = field == 1 ? ModelTableBuilder.Floats(bone.Scale) : field == 3 ? PackRotation(bone.Rotation)
                        : ModelTableBuilder.Floats(bone.Translation.Select(v => v * scale).ToArray());
                    writer.Int32(writer.Field(target, field), 1);
                    writer.Reference(target, field + 1, writer.Table(1, new Dictionary<int, byte[]> { [0] = value }, new Dictionary<int, int> { [0] = value.Length }));
                    continue;
                }
                var curve = source.Table(entry, field + 1); var stride = field == 3 ? 6 : 12;
                var (start, count) = kind == 1 ? (source.Field(curve, 0), 1) : source.Vector(curve, kind == 2 ? 0 : 1, stride, 18000);
                if (count == 0 || start == 0) throw new InvalidDataException("Animation curve has no values.");
                if (expression != SwShExpressionCurve.None && kind >= 2)
                {
                    var (indices, keyCount) = kind >= 3 ? source.Vector(curve, 0, kind == 3 ? 2 : 1, 18000) : (0, count);
                    if (keyCount != count) throw new InvalidDataException("Expression key count does not match its values.");
                    var sourceFrames = Enumerable.Range(0, count).Select(k => kind == 2 ? k : kind == 3 ? source.U16(indices + k * 2) : source.U8(indices + k)).ToArray();
                    var payload = new byte[frames * stride];
                    for (var frame = 0; frame < frames; frame++)
                    {
                        var sample = ExpressionSample(expression, frame);
                        var nearest = Enumerable.Range(0, count).MinBy(k => Math.Abs(sourceFrames[k] - sample));
                        source.Slice(start + nearest * stride, stride).CopyTo(payload.AsSpan(frame * stride));
                        if (field == 5) for (var c = 0; c < 3; c++)
                            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(frame * stride + c * 4), source.Float(start + nearest * stride + c * 4) * scale);
                    }
                    writer.Int32(writer.Field(target, field), 2); var dense = writer.Table(1); writer.Reference(target, field + 1, dense);
                    writer.Blob(dense, 0, payload, frames);
                }
                else
                {
                    writer.Int32(writer.Field(target, field), kind); writer.Reference(target, field + 1, copy + curve);
                    if (field == 5) for (var k = 0; k < count; k++) ScaleVector(start + k * stride);
                }
            }
        }
        Materials(writer, root, materialTracks, frames, expression != SwShExpressionCurve.None);
        var result = writer.Finish(root);
        var convertedBones = sourceBones.Select(b => b with { Name = aliases.GetValueOrDefault(b.Name, b.Name) }).ToArray();
        _ = PreviewRigReader.Animation(new(result), "converted", convertedBones);
        return result;
    }

    private static void Materials(ModelTableBuilder writer, int root, PreviewMaterialTrack[] tracks, int frames, bool expression)
    {
        if (tracks.Length > 256) throw new InvalidDataException("Material animation exceeds the conversion limit.");
        var selected = new List<PreviewMaterialTrack>();
        foreach (var group in tracks.GroupBy(t => t.Material, StringComparer.Ordinal))
        {
            PreviewMaterialTrack? Unique(string parameter)
            {
                var matches = group.Where(t => t.Parameter == parameter).ToArray();
                if (matches.Length == 0) return null;
                if (matches.Skip(1).Any(track => !Equivalent(matches[0], track)))
                    throw new InvalidDataException("Material animation has conflicting tracks for the same target and parameter.");
                return matches[0];
            }
            var primary = Unique("UVScaleOffset");
            var secondary = Unique("UVScaleOffset1");
            if (primary is not null && secondary is not null && !Equivalent(primary, secondary))
                throw new InvalidDataException("Independent secondary UV animation needs a material adapter.");
            if ((primary ?? secondary) is { } chosen) selected.Add(chosen);
            if (group.Any(t => t.Parameter is not ("UVScaleOffset" or "UVScaleOffset1")))
                throw new InvalidDataException("This material animation needs an additional native adapter.");
        }
        var material = writer.Table(1); writer.Reference(root, 2, material); var slots = writer.Vector(material, 0, selected.Count);
        string[] parameters = ["ColorUVScaleU", "ColorUVScaleV", "ColorUVTranslateU", "ColorUVTranslateV"];
        for (var i = 0; i < selected.Count; i++)
        {
            var track = selected[i];
            if (track.Channels.Length != 4) throw new InvalidDataException("UV animation has an invalid channel count.");
            var target = writer.Table(4); writer.Pointer(slots[i], target); writer.Text(target, 0, track.Material);
            writer.Vector(target, 1, 0); writer.Vector(target, 3, 0);
            var populated = Enumerable.Range(0, 4).Where(c => track.Channels[c].Length > 0).ToArray();
            var scalarSlots = writer.Vector(target, 2, populated.Length);
            for (var c = 0; c < populated.Length; c++)
            {
                var keys = track.Channels[populated[c]]; ValidateKeys(keys);
                var constant = keys.All(k => k.Value[0] == keys[0].Value[0]);
                var scalar = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
                writer.Pointer(scalarSlots[c], scalar); writer.Text(scalar, 0, parameters[populated[c]]);
                var curve = writer.Table(1); writer.Reference(scalar, 2, curve);
                if (constant) writer.Single(writer.Field(curve, 0), keys[0].Value[0]);
                else writer.Blob(curve, 0, ModelTableBuilder.Floats(Enumerable.Range(0, frames)
                    .Select(frame => Interpolate(keys, expression ? frame / 2f : frame)).ToArray()), frames);
            }
        }
    }
    private static bool Equivalent(PreviewMaterialTrack a, PreviewMaterialTrack b) => a.Channels.Length == b.Channels.Length
        && a.Channels.Zip(b.Channels).All(pair => pair.First.Length == pair.Second.Length
            && pair.First.Zip(pair.Second).All(keys => keys.First.Frame == keys.Second.Frame && keys.First.Value.SequenceEqual(keys.Second.Value)));
    private static void ValidateKeys(PreviewKey[] keys)
    {
        if (keys.Length > 18000 || keys.Any(k => k.Value.Length != 1 || !float.IsFinite(k.Frame) || k.Frame < 0 || !float.IsFinite(k.Value[0]))
            || keys.Zip(keys.Skip(1)).Any(k => k.First.Frame >= k.Second.Frame)) throw new InvalidDataException("Material animation keys are invalid.");
    }
    private static float Interpolate(PreviewKey[] keys, float frame)
    {
        if (frame <= keys[0].Frame) return keys[0].Value[0];
        for (var i = 1; i < keys.Length; i++) if (frame < keys[i].Frame)
            return keys[i - 1].Value[0] + (keys[i].Value[0] - keys[i - 1].Value[0]) * (frame - keys[i - 1].Frame) / (keys[i].Frame - keys[i - 1].Frame);
        return keys[^1].Value[0];
    }
    private static float ExpressionSample(SwShExpressionCurve expression, int frame)
    {
        int[] frames = [0, 10, 20, 30, 40, 50, 60, 80];
        int[] samples = expression == SwShExpressionCurve.Mouth ? [10, 5, 0, 10, 5, 5, 10, 10] : [0, 5, 10, 0, 5, 10, 0, 0];
        for (var i = 1; i < frames.Length; i++) if (frame < frames[i])
            return samples[i - 1] + (samples[i] - samples[i - 1]) * (frame - frames[i - 1]) / (float)(frames[i] - frames[i - 1]);
        return samples[^1];
    }
    internal static byte[] PackRotation(float[] quaternion)
    {
        if (quaternion.Length != 4 || quaternion.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Bind rotation is invalid.");
        var length = Math.Sqrt(quaternion.Sum(v => (double)v * v));
        if (length < .000001) throw new InvalidDataException("Bind rotation is empty.");
        var q = quaternion.Select(v => v / length).ToArray(); var missing = Enumerable.Range(0, 4).MaxBy(i => Math.Abs(q[i]));
        var negative = q[missing] < 0; if (negative) for (var i = 0; i < 4; i++) q[i] = -q[i];
        ulong bits = (uint)(missing | (negative ? 4 : 0)); var shift = 3;
        for (var i = 0; i < 4; i++)
        {
            if (i == missing) continue;
            var value = (int)Math.Round((q[i] + Math.PI / 4) * 65534 / Math.PI);
            if (value is < 0 or > 32767) throw new InvalidDataException("Bind rotation cannot be represented by the native encoding.");
            bits |= (ulong)value << shift; shift += 15;
        }
        var result = new byte[6]; for (var i = 0; i < 6; i++) result[i] = (byte)(bits >> (8 * i)); return result;
    }
}
