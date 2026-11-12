// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    /// <summary>Adds a battle stance recovery without changing the authored landing frames.</summary>
    public static byte[]? BattleEntryRecovery(byte[] landing, byte[] idle, PreviewBone[] bones)
    {
        var source = new ModelBuffer(landing);
        var entry = PreviewRigReader.Animation(source, "entry", bones);
        var wait = PreviewRigReader.Animation(new(idle), "idle", bones);
        if (entry.Loop || !wait.Loop || entry.Rate != wait.Rate || bones.Length is < 1 or > 255)
            throw new InvalidDataException("Battle entry recovery needs compatible landing and combat idle timing.");
        var duration = Math.Max(2, checked((int)Math.Ceiling(entry.Rate * .2)));
        var frames = checked(entry.Frames + duration);
        if (frames > 18000) throw new InvalidDataException("Battle entry recovery exceeds its frame budget.");
        var previous = entry.Tracks.ToDictionary(t => t.Bone);
        var target = wait.Tracks.ToDictionary(t => t.Bone);
        float[] Values(int bone, int field, bool next)
        {
            var track = (next ? target : previous).GetValueOrDefault(bone);
            var fallback = field == 1 ? bones[bone].Scale : field == 3 ? bones[bone].Rotation : bones[bone].Translation;
            var keys = field == 1 ? track?.Scale : field == 3 ? track?.Rotation : track?.Translation;
            return keys is not { Length: > 0 } ? fallback : next ? keys[0].Value : keys[^1].Value;
        }
        bool Equal(float[] a, float[] b, int field) => field == 3
            ? MathF.Abs(Quaternion.Dot(Q(a), Q(b))) >= 1 - .000001f
            : a.Zip(b).All(pair => MathF.Abs(pair.First - pair.Second) <= .0001f);
        var changed = Enumerable.Range(0, bones.Length).SelectMany(b => new[] { 1, 3, 5 }
            .Where(f => !Equal(Values(b, f, false), Values(b, f, true), f)).Select(f => (Bone: b, Field: f))).ToHashSet();
        if (changed.Count == 0) return null;

        var oldSkeleton = source.Table(source.Root, 1);
        if (oldSkeleton == 0 || source.U16(oldSkeleton - source.I32(oldSkeleton)) > 8)
            throw new InvalidDataException("Battle entry skeleton metadata needs another adapter.");
        var originals = source.Tables(oldSkeleton, 0, 4096).ToDictionary(t => source.Text(t, 0)
            ?? throw new InvalidDataException("Battle entry track is unnamed."), StringComparer.Ordinal);
        if (originals.Keys.Except(bones.Select(b => b.Name), StringComparer.Ordinal).Any())
            throw new InvalidDataException("Battle entry contains an unrelated skeleton track.");
        var writer = new ModelTableBuilder(landing);
        var info = source.Table(source.Root, 0);
        writer.Int32(source.Field(info, 1), frames);
        var skeleton = writer.Table(2, absent: source.Table(oldSkeleton, 1) == 0 ? new HashSet<int> { 1 } : null);
        writer.Reference(source.Root, 1, skeleton);
        var slots = writer.Vector(skeleton, 0, bones.Length);
        var tracks = slots.Select(_ => writer.Table(7)).ToArray();
        for (var i = 0; i < slots.Length; i++) writer.Pointer(slots[i], tracks[i]);
        var copy = writer.Append(landing);
        if (source.Table(oldSkeleton, 1) is var initial && initial != 0) writer.Reference(skeleton, 1, copy + initial);
        for (var bone = 0; bone < bones.Length; bone++)
        {
            var node = tracks[bone]; writer.Text(node, 0, bones[bone].Name);
            var original = originals.GetValueOrDefault(bones[bone].Name);
            foreach (var field in new[] { 1, 3, 5 })
            {
                var kindAt = original == 0 ? 0 : source.Field(original, field);
                var kind = kindAt == 0 ? 0 : source.U8(kindAt);
                var curve = original == 0 ? 0 : source.Table(original, field + 1);
                if (!changed.Contains((bone, field)))
                {
                    writer.Int32(writer.Field(node, field), kind);
                    // Dense curves are indexed directly by playback frame. They
                    // must cover the recovery even when their final pose already
                    // matches idle, or playback can read past the authored data.
                    if (kind == 2)
                    {
                        var heldStride = field == 3 ? 6 : 12;
                        var (start, count) = source.Vector(curve, 0, heldStride, 18000);
                        if (count < 1 || count > entry.Frames)
                            throw new InvalidDataException("Battle entry dense curve timing is invalid.");
                        var held = new byte[frames * heldStride];
                        source.Slice(start, count * heldStride).CopyTo(held);
                        for (var frame = count; frame < frames; frame++)
                            source.Slice(start + (count - 1) * heldStride, heldStride).CopyTo(held.AsSpan(frame * heldStride));
                        var heldCurve = writer.Table(1); writer.Reference(node, field + 1, heldCurve);
                        writer.Blob(heldCurve, 0, held, frames);
                        continue;
                    }
                    if (kind != 0) writer.Reference(node, field + 1, copy + curve);
                    continue;
                }
                if (kind > 4) throw new InvalidDataException("Battle entry curve encoding is unsupported.");
                var stride = field == 3 ? 6 : 12;
                var values = new List<byte>(); var indices = new List<ushort>();
                if (kind == 0)
                {
                    indices.Add(0); values.AddRange(Encode(Values(bone, field, false), field));
                }
                else
                {
                    var (start, count) = kind == 1 ? (source.Field(curve, 0), 1) : source.Vector(curve, kind == 2 ? 0 : 1, stride, 18000);
                    var (keys, keyCount) = kind >= 3 ? source.Vector(curve, 0, kind == 3 ? 2 : 1, 18000) : (0, count);
                    if (count < 1 || keyCount != count) throw new InvalidDataException("Battle entry keys are incomplete.");
                    values.AddRange(source.Slice(start, count * stride));
                    for (var i = 0; i < count; i++) indices.Add(checked((ushort)(kind == 1 ? 0 : kind == 2 ? i : kind == 3 ? source.U16(keys + i * 2) : source.U8(keys + i))));
                }
                // Retain a held final source value before adding any new motion.
                if (indices[^1] < entry.Frames - 1)
                {
                    indices.Add(checked((ushort)(entry.Frames - 1)));
                    values.AddRange(values.TakeLast(stride).ToArray());
                }
                var from = Values(bone, field, false); var to = Values(bone, field, true);
                for (var step = 1; step <= duration; step++)
                {
                    var t = (float)step / duration; t = t * t * (3 - 2 * t);
                    float[] value;
                    if (field == 3)
                    {
                        var rotation = Quaternion.Slerp(Q(from), Q(to), t);
                        value = [rotation.X, rotation.Y, rotation.Z, rotation.W];
                    }
                    else value = from.Zip(to, (a, b) => a + (b - a) * t).ToArray();
                    indices.Add(checked((ushort)(entry.Frames - 1 + step))); values.AddRange(Encode(value, field));
                }
                writer.Int32(writer.Field(node, field), 3);
                var replacement = writer.Table(2); writer.Reference(node, field + 1, replacement);
                writer.Blob(replacement, 0, indices.SelectMany(BitConverter.GetBytes).ToArray(), indices.Count);
                writer.Blob(replacement, 1, values.ToArray(), indices.Count);
            }
        }
        var result = writer.Finish(source.Root);
        // Dense visibility streams end in a partially filled byte. Extending the
        // clip must hold the last authored sample, rather than playing its zero
        // padding as hidden meshes during the new recovery frames.
        var visibility = SwShPreviewAnimation.ReadVisibility(source, entry.Frames);
        if (visibility.Length > 0)
            result = Visibility(result, visibility, visibility.Select(track => track.Name).ToHashSet(StringComparer.Ordinal));
        var verified = PreviewRigReader.Animation(new(result), "recovered-entry", bones);
        if (verified.Frames != frames || verified.Rate != entry.Rate || verified.Loop)
            throw new InvalidDataException("Battle entry recovery failed timing verification.");
        return result;

        static Quaternion Q(float[] v) => Quaternion.Normalize(new(v[0], v[1], v[2], v[3]));
        static byte[] Encode(float[] value, int field) => field == 3 ? PackRotation(value) : ModelTableBuilder.Floats(value);
    }

    public static byte[] BattleEntryController(byte[] controller)
    {
        var source = new ModelBuffer(controller); var writer = new ModelTableBuilder(controller);
        var bindings = source.Tables(source.Table(source.Root, 6), 0, 2048)
            .Where(t => source.Text(t, 0) == "ba01_landC01").ToArray();
        if (bindings.Length != 1) throw new InvalidDataException("Battle entry binding is ambiguous.");
        var path = source.Text(bindings[0], 1) ?? "";
        const string suffix = "_ba01_landC01.gfbanm";
        if (!path.EndsWith(suffix, StringComparison.Ordinal)) throw new InvalidDataException("Battle entry binding is unsupported.");
        writer.Text(bindings[0], 1, path[..^suffix.Length] + "_battleentry01.gfbanm");
        return writer.Finish(source.Root);
    }
}
