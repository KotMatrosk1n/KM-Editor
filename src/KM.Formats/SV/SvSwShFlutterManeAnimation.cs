// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.SV;

/// <summary>Retains Flutter Mane's attack spins while clearing SwSh's idle-aligned ground plane.</summary>
public static class SvSwShFlutterManeAnimation
{
    public static byte[] Apply(byte[] converted, PreviewScene scene, PreviewClip clip, string anchor)
    {
        if (!clip.Id.Contains("_20400_", StringComparison.Ordinal) && !clip.Id.Contains("_20450_", StringComparison.Ordinal))
            return converted;
        if (clip.Loop || clip.Rate != 60 || clip.Frames is < 2 or > 180)
            throw new InvalidDataException("Flutter Mane attack timing changed.");
        var bones = scene.Rig.Bones;
        if (bones.Length == 0 || bones[0].Parent != -1 || bones.Skip(1).Any(b => b.Parent < 0)
            || bones[0].Scale.Any(v => v != 1) || !bones[0].Rotation.SequenceEqual(new float[] { 0, 0, 0, 1 }))
            throw new InvalidDataException("Flutter Mane placement anchor changed.");
        var tracks = clip.Tracks.ToDictionary(t => t.Bone);
        var lows = new float[clip.Frames];
        var world = new Matrix4x4[bones.Length]; var skin = new Matrix4x4[bones.Length];
        var scales = new Vector3[bones.Length];
        static Vector3 V(float[] a) => new(a[0], a[1], a[2]);
        static float[] Sample(PreviewKey[]? keys, float[] fallback, int frame, bool rotation = false)
        {
            if (keys is not { Length: > 0 }) return fallback;
            if (frame <= keys[0].Frame) return keys[0].Value;
            if (frame >= keys[^1].Frame) return keys[^1].Value;
            var upper = Array.FindIndex(keys, k => k.Frame > frame); var a = keys[upper - 1]; var b = keys[upper];
            var t = (frame - a.Frame) / (b.Frame - a.Frame);
            if (rotation)
            {
                var q = Quaternion.Slerp(new(a.Value[0], a.Value[1], a.Value[2], a.Value[3]),
                    new(b.Value[0], b.Value[1], b.Value[2], b.Value[3]), t);
                return [q.X, q.Y, q.Z, q.W];
            }
            return a.Value.Zip(b.Value, (x, y) => x + (y - x) * t).ToArray();
        }
        for (var frame = 0; frame < clip.Frames; frame++)
        {
            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i]; var track = tracks.GetValueOrDefault(i);
                scales[i] = V(Sample(track?.Scale, bone.Scale, frame));
                var q = Sample(track?.Rotation, bone.Rotation, frame, true);
                var local = Matrix4x4.CreateScale(scales[i]) * Matrix4x4.CreateFromQuaternion(new(q[0], q[1], q[2], q[3]));
                if (bone.CompensateScale && bone.Parent >= 0)
                {
                    var parent = scales[bone.Parent];
                    if (MathF.Abs(parent.X * parent.Y * parent.Z) < .000001f)
                        throw new InvalidDataException("Flutter Mane attack scale is degenerate.");
                    local *= Matrix4x4.CreateScale(1 / parent.X, 1 / parent.Y, 1 / parent.Z);
                }
                local *= Matrix4x4.CreateTranslation(V(Sample(track?.Translation, bone.Translation, frame)));
                world[i] = bone.Parent < 0 ? local : local * world[bone.Parent];
                var a = bone.InverseBind;
                skin[i] = new Matrix4x4(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7],
                    a[8], a[9], a[10], a[11], a[12], a[13], a[14], a[15]) * world[i];
            }
            var low = float.PositiveInfinity;
            foreach (var primitive in scene.Primitives)
                for (var vertex = 0; vertex < primitive.Vertices.Length; vertex += 16)
                {
                    var a = primitive.Vertices; var position = new Vector3(a[vertex], a[vertex + 1], a[vertex + 2]);
                    var posed = Vector3.Zero;
                    for (var influence = 0; influence < 4; influence++)
                        if (a[vertex + 12 + influence] > 0)
                            posed += Vector3.Transform(position, skin[(int)a[vertex + 8 + influence]]) * a[vertex + 12 + influence];
                    low = MathF.Min(low, posed.Y);
                }
            if (!float.IsFinite(low)) throw new InvalidDataException("Flutter Mane attack bounds are invalid.");
            lows[frame] = low;
        }
        // The native configuration aligns the idle lower bound to the floor.
        // A bounded envelope eases the lift in/out and keeps every sampled pose above that bound.
        var offsets = lows.Select(low => MathF.Max(0, lows[0] - low + .01f)).ToArray();
        offsets[0] = offsets[^1] = 0;
        const float maximumStep = .08f;
        for (var i = 1; i < offsets.Length; i++) offsets[i] = MathF.Max(offsets[i], offsets[i - 1] - maximumStep);
        for (var i = offsets.Length - 2; i >= 0; i--) offsets[i] = MathF.Max(offsets[i], offsets[i + 1] - maximumStep);
        if (offsets[0] != 0 || offsets[^1] != 0 || offsets.Max() > 1.5f)
            throw new InvalidDataException("Flutter Mane attack clearance exceeds the qualified envelope.");
        return SwShAnimationWriter.VerticalOffsets(converted, anchor, offsets.Select(v => v * 100).ToArray());
    }
}
