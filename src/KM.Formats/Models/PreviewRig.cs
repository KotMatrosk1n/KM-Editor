// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace KM.Formats.Models;

public sealed record PreviewBone(string Name, int Parent, int Joint, bool CompensateScale,
    float[] Scale, float[] Rotation, float[] Translation, float[] InverseBind);
public sealed record PreviewKey(float Frame, float[] Value);
public sealed record PreviewBoneTrack(int Bone, PreviewKey[] Scale, PreviewKey[] Rotation, PreviewKey[] Translation);
public sealed record PreviewClipReference(string Id, string? Skeleton, string? Material);
public sealed record PreviewVisibility(string Name, PreviewKey[] Keys);
public sealed record PreviewMaterialTrack(string Material, string Parameter, PreviewKey[][] Channels);
public sealed record PreviewClip(string Id, int Frames, int Rate, bool Loop, PreviewBoneTrack[] Tracks)
{
    public PreviewVisibility[] Visibility { get; init; } = [];
    public PreviewMaterialTrack[] Materials { get; init; } = [];
}
public sealed record PreviewRig(PreviewBone[] Bones, PreviewClipReference[] Clips, PreviewClip? Clip, string[] Warnings);

public static class PreviewRigReader
{
    public static PreviewBone[] Skeleton(ModelBuffer data)
    {
        var nodes = data.Tables(data.Root, 1, 512);
        var joints = data.Tables(data.Root, 2, 512);
        var result = new PreviewBone[nodes.Length];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            int Index(int field) => data.Field(node, field) is var at && at != 0 ? data.I32(at) : -1;
            var parent = Index(4); var joint = Index(5);
            if (parent >= i || parent < -1 || joint < -1 || joint >= joints.Length)
                throw new InvalidDataException("Skeleton hierarchy is unsupported.");
            var transform = data.Table(node, 1);
            var scale = Vector(data, transform, 0, Vector3.One);
            var angles = Vector(data, transform, 1, Vector3.Zero);
            var translation = Vector(data, transform, 2, Vector3.Zero);
            var rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(angles.X)
                * Matrix4x4.CreateRotationY(angles.Y) * Matrix4x4.CreateRotationZ(angles.Z));
            var inverse = Matrix4x4.Identity;
            var compensate = false;
            if (joint >= 0)
            {
                var bone = joints[joint]; var matrix = data.Table(bone, 2);
                var x = Vector(data, matrix, 0, Vector3.UnitX); var y = Vector(data, matrix, 1, Vector3.UnitY);
                var z = Vector(data, matrix, 2, Vector3.UnitZ); var t = Vector(data, matrix, 3, Vector3.Zero);
                inverse = new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, t.X, t.Y, t.Z, 1);
                compensate = data.Field(bone, 0) is var flag && flag != 0 && data.U8(flag) != 0;
            }
            var name = data.Text(node, 0) ?? throw new InvalidDataException("Skeleton node has no identifier.");
            if (!names.Add(name)) throw new InvalidDataException("Skeleton node identifiers are ambiguous.");
            result[i] = new(name, parent, joint, compensate, [scale.X, scale.Y, scale.Z],
                [rotation.X, rotation.Y, rotation.Z, rotation.W], [translation.X, translation.Y, translation.Z],
                [inverse.M11, inverse.M12, inverse.M13, inverse.M14, inverse.M21, inverse.M22, inverse.M23, inverse.M24,
                 inverse.M31, inverse.M32, inverse.M33, inverse.M34, inverse.M41, inverse.M42, inverse.M43, inverse.M44]);
        }
        return result;
    }
    private static Vector3 Vector(ModelBuffer d, int table, int field, Vector3 fallback)
    {
        if (table == 0) return fallback;
        var at = d.Field(table, field);
        if (at == 0) return fallback;
        var value = new Vector3(d.Float(at), d.Float(at + 4), d.Float(at + 8));
        if (!float.IsFinite(value.X + value.Y + value.Z)) throw new InvalidDataException("Invalid skeleton transform.");
        return value;
    }
    public static PreviewClip Animation(ModelBuffer data, string id, PreviewBone[] bones)
    {
        var info = data.Table(data.Root, 0);
        var frames = checked((int)data.Value(info, 1)); var rate = checked((int)data.Value(info, 2));
        if (frames is < 1 or > 18000 || rate is < 1 or > 240) throw new InvalidDataException("Animation timing is unsupported.");
        var tracks = new List<PreviewBoneTrack>();
        var seen = new HashSet<int>();
        var skeletal = data.Table(data.Root, 1);
        foreach (var entry in skeletal == 0 ? [] : data.Tables(skeletal, 0, 512))
        {
            var index = Array.FindIndex(bones, b => b.Name == data.Text(entry, 0));
            if (index < 0) continue;
            if (!seen.Add(index)) throw new InvalidDataException("Animation tracks are ambiguous.");
            tracks.Add(new(index, Keys(1, false), Keys(3, true), Keys(5, false)));
            PreviewKey[] Keys(int field, bool rotation)
            {
                var at = data.Field(entry, field); var kind = at == 0 ? 0 : data.U8(at);
                if (kind == 0) return [];
                if (kind > 4) throw new InvalidDataException("Animation curve encoding is unsupported.");
                var table = data.Table(entry, field + 1); var stride = rotation ? 6 : 12;
                var (start, count) = kind == 1 ? (data.Field(table, 0), 1) : data.Vector(table, kind == 2 ? 0 : 1, stride, 18000);
                var (keys, keyCount) = kind >= 3 ? data.Vector(table, 0, kind == 3 ? 2 : 1, 18000) : (0, count);
                if (count == 0 || keyCount != count) throw new InvalidDataException("Animation keys are missing.");
                var result = new PreviewKey[count];
                for (var k = 0; k < count; k++)
                {
                    var frame = kind == 1 ? 0 : kind == 2 ? k : kind == 3 ? data.U16(keys + k * 2) : data.U8(keys + k);
                    if (frame >= frames || (k > 0 && frame < result[k - 1].Frame)) throw new InvalidDataException("Animation keys are out of order.");
                    var valueAt = start + k * stride;
                    var value = rotation ? Rotation(data, valueAt) : new[] { data.Float(valueAt), data.Float(valueAt + 4), data.Float(valueAt + 8) };
                    if (value.Any(v => !float.IsFinite(v) || Math.Abs(v) > 1_000_000)) throw new InvalidDataException("Invalid animation value.");
                    result[k] = new(frame, value);
                }
                return result;
            }
        }
        if (tracks.Count == 0) throw new InvalidDataException("Animation does not match this skeleton.");
        return new(id, frames, rate, data.Value(info, 0) != 0, tracks.ToArray());
    }
    private static float[] Rotation(ModelBuffer data, int at)
    {
        // Three 15-bit components, a two-bit omitted component index and a sign bit.
        var bits = (ulong)data.U16(at) | ((ulong)data.U16(at + 2) << 16) | ((ulong)data.U16(at + 4) << 32);
        var result = new float[4]; var missing = (int)(bits & 3); var shift = 3; var squared = 0f;
        for (var component = 0; component < 4; component++)
        {
            if (component == missing) continue;
            var value = ((bits >> shift) & 32767) * (MathF.PI / 65534) - MathF.PI / 4;
            result[component] = value; squared += value * value; shift += 15;
        }
        if (squared > 1.001f) throw new InvalidDataException("Invalid compressed rotation.");
        result[missing] = MathF.Sqrt(MathF.Max(0, 1 - squared));
        if ((bits & 4) != 0) for (var i = 0; i < 4; i++) result[i] = -result[i];
        return result;
    }
}
