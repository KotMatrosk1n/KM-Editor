// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

public static partial class ZaSwShAnimationWriter
{
    // Copy only the reachable facial tracks from our converted skeletal output.
    // Facial playback previously retained the entire unused body animation twice.
    private static byte[] ExpressionSkeleton(byte[] animation, SwShExpressionCurve expression, bool empty)
    {
        var source = new ModelBuffer(animation);
        var skeleton = source.Table(source.Root, 1);
        var boneName = expression switch
        {
            SwShExpressionCurve.Eyes => "eyelid",
            SwShExpressionCurve.Mouth => "jaw",
            _ => throw new InvalidDataException("Facial skeleton requires an eye or mouth expression."),
        };
        var tracks = source.Tables(skeleton, 0, 4096).Where(t => !empty
            && (source.Text(t, 0) ?? "").Contains(boneName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var writer = new ModelTableBuilder();
        HashSet<int> Absent(int table, int fields)
        {
            var count = (source.U16(table - source.I32(table)) - 4) / 2;
            if (Enumerable.Range(fields, Math.Max(0, count - fields)).Any(f => source.Field(table, f) != 0))
                throw new InvalidDataException("Converted facial skeleton contains an unsupported field.");
            return Enumerable.Range(0, fields).Where(f => source.Field(table, f) == 0).ToHashSet();
        }
        // Preserve the existing representation for skeletons carrying an initial
        // transform, whose complete schema is independent of the facial tracks.
        if (source.Table(skeleton, 1) != 0)
        {
            writer = new ModelTableBuilder(animation);
            var oldSlots = writer.Vector(skeleton, 0, tracks.Length);
            var copy = tracks.Length == 0 ? 0 : writer.Append(animation);
            for (var i = 0; i < tracks.Length; i++) writer.Pointer(oldSlots[i], copy + tracks[i]);
            return writer.Finish(source.Root);
        }
        var rootAbsent = Absent(source.Root, 5);
        if (!rootAbsent.Contains(3) || !rootAbsent.Contains(4)
            || source.Tables(source.Table(source.Root, 2), 0, 128).Length != 0)
            throw new InvalidDataException("Facial skeleton compaction requires unadorned converted motion.");
        var root = writer.Table(5, absent: rootAbsent);
        var info = source.Table(source.Root, 0); var infoAbsent = Absent(info, 3);
        writer.Reference(root, 0, writer.Table(3, Enumerable.Range(0, 3).Where(f => !infoAbsent.Contains(f))
            .ToDictionary(f => f, f => source.Slice(source.Field(info, f), 4).ToArray()), absent: infoAbsent));
        var targetSkeleton = writer.Table(2, absent: Absent(skeleton, 2));
        writer.Reference(root, 1, targetSkeleton);
        var slots = writer.Vector(targetSkeleton, 0, tracks.Length);
        for (var i = 0; i < tracks.Length; i++)
        {
            var track = tracks[i]; var target = writer.Table(7, absent: Absent(track, 7));
            writer.Pointer(slots[i], target); writer.Text(target, 0, source.Text(track, 0)!);
            foreach (var field in new[] { 1, 3, 5 })
            {
                var at = source.Field(track, field); var kind = at == 0 ? 0 : source.U8(at);
                if (kind == 0) continue;
                if (kind is not (1 or 2)) throw new InvalidDataException("Converted facial curve requires another encoding adapter.");
                var curve = source.Table(track, field + 1); var stride = field == 3 ? 6 : 12;
                var absent = Absent(curve, 1);
                if (absent.Contains(0)) throw new InvalidDataException("Converted facial curve has no payload.");
                writer.Int32(writer.Field(target, field), kind);
                if (kind == 1)
                    writer.Reference(target, field + 1, writer.Table(1,
                        new Dictionary<int, byte[]> { [0] = source.Slice(source.Field(curve, 0), stride).ToArray() },
                        new Dictionary<int, int> { [0] = stride }));
                else
                {
                    var (start, count) = source.Vector(curve, 0, stride, 18000);
                    if (count == 0) throw new InvalidDataException("Converted facial curve is empty.");
                    var dense = writer.Table(1); writer.Reference(target, field + 1, dense);
                    writer.Blob(dense, 0, source.Slice(start, checked(count * stride)).ToArray(), count);
                }
            }
        }
        var materials = writer.Table(1); writer.Reference(root, 2, materials); writer.Vector(materials, 0, 0);
        return writer.Finish(root);
    }
}
