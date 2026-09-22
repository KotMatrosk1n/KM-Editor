// SPDX-License-Identifier: GPL-3.0-only
using Google.FlatBuffers;
using KM.Core.ModMerging;

namespace KM.SV.Shops;

public static class SvFriendlyShopMergeDocument
{
    public static MergeDocument Read(byte[] bytes)
    {
        var table = global::LineupDataArray.GetRootAsLineupDataArray(new ByteBuffer(bytes));
        KM.Formats.FlatBufferMergeGuard.Validate(table);
        var rows = Enumerable.Range(0, table.ValuesLength).Select(index => {
            var row = table.Values(index) ?? throw new InvalidDataException("A shop row is missing.");
            return new Row(index, row.Lineupid, row.Sortnum, (int)row.Item, (int)row.ItemCondkind, row.ItemCondvalue, row.GymBadgeNum);
        }).ToArray();
        var document = MergeRowDocument.Create(rows, selected => {
            var builder = new FlatBufferBuilder(1024);
            var offsets = selected.Select(row => global::LineupData.CreateLineupData(builder,
                row.LineupId is null ? default : builder.CreateString(row.LineupId), row.SortNum, (global::ItemID)row.ItemId,
                (global::CondEnum)row.ConditionKind, row.ConditionValue is null ? default : builder.CreateString(row.ConditionValue), row.GymBadgeNum)).ToArray();
            var vector = global::LineupDataArray.CreateValuesVector(builder, offsets);
            global::LineupDataArray.FinishLineupDataArrayBuffer(builder, global::LineupDataArray.CreateLineupDataArray(builder, vector));
            return builder.SizedByteArray();
        }, "shops");
        KM.Formats.FlatBufferMergeGuard.ValidateRoundTrip(table, document.Write(document.Content.DeepClone()));
        return document;
    }
    private sealed record Row(int SourceIndex, string? LineupId, int SortNum, int ItemId, int ConditionKind, string? ConditionValue, int GymBadgeNum);
}
