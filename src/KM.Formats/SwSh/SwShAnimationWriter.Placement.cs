// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    /// <summary>Animates a constant placement anchor without rewriting other motion or material channels.</summary>
    public static byte[] VerticalOffsets(byte[] animation, string bone, IReadOnlyList<float> offsets)
    {
        var data = new ModelBuffer(animation);
        var frames = data.Value(data.Table(data.Root, 0), 1);
        if (string.IsNullOrWhiteSpace(bone) || offsets.Count != frames || offsets.Count is < 2 or > 18000
            || offsets.Any(value => !float.IsFinite(value)) || offsets[0] != 0 || offsets[^1] != 0)
            throw new InvalidDataException("Animation placement timing or boundaries are invalid.");
        var track = data.Tables(data.Table(data.Root, 1), 0, 4096).Single(t => data.Text(t, 0) == bone);
        if (data.U8(data.Field(track, 5)) != 1)
            throw new InvalidDataException("Animated placement requires a constant source anchor.");
        var at = data.Field(data.Table(track, 6), 0);
        var values = new float[offsets.Count * 3];
        for (var frame = 0; frame < offsets.Count; frame++)
        {
            values[frame * 3] = data.Float(at);
            values[frame * 3 + 1] = data.Float(at + 4) + offsets[frame];
            values[frame * 3 + 2] = data.Float(at + 8);
        }
        var writer = new ModelTableBuilder(animation);
        writer.Int32(data.Field(track, 5), 2);
        var curve = writer.Table(1); writer.Reference(track, 6, curve);
        writer.Blob(curve, 0, ModelTableBuilder.Floats(values), offsets.Count);
        return writer.Finish(data.Root);
    }

    public static byte[] VerticalOffset(byte[] animation, string bone, float offset)
    {
        if (string.IsNullOrWhiteSpace(bone) || !float.IsFinite(offset))
            throw new InvalidDataException("Animation placement is invalid.");
        var bytes = animation.ToArray(); var data = new ModelBuffer(bytes);
        var tracks = data.Tables(data.Table(data.Root, 1), 0, 4096);
        var matches = tracks.Where(t => data.Text(t, 0) == bone).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Animation placement anchor is missing or ambiguous.");
        var kindAt = data.Field(matches[0], 5); var kind = kindAt == 0 ? 0 : data.U8(kindAt);
        if (kind is < 1 or > 4) throw new InvalidDataException("Animation placement curve is unsupported.");
        var curve = data.Table(matches[0], 6);
        var (start, count) = kind == 1 ? (data.Field(curve, 0), 1) : data.Vector(curve, kind == 2 ? 0 : 1, 12, 18000);
        if (start == 0 || count == 0) throw new InvalidDataException("Animation placement curve is empty.");
        for (var i = 0; i < count; i++)
        {
            var at = start + i * 12 + 4; var value = data.Float(at) + offset;
            if (!float.IsFinite(value)) throw new InvalidDataException("Animation placement curve is nonfinite.");
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at, 4), value);
        }
        return bytes;
    }
}
