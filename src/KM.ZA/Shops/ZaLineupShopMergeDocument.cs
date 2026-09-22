// SPDX-License-Identifier: GPL-3.0-only
using Google.FlatBuffers;
using KM.Core.ModMerging;

namespace KM.ZA.Shops;

public static class ZaLineupShopMergeDocument
{
    public static MergeDocument Read(byte[] bytes)
    {
        var table = KM.Formats.ZA.Generated.GameData.ZaShopLineupArray.GetRootAsZaShopLineupArray(new ByteBuffer(bytes));
        KM.Formats.FlatBufferMergeGuard.Validate(table);
        var rows = ZaShopsWorkflowService.ReadLineupRows(bytes).Select(row => new Lineup(row.SourceIndex, row.Name,
            row.Inventory.Select(item => new Inventory(item.SourceIndex, item.ItemId, item.DisplayIndex, item.RowId,
                item.Conditions.Select(group => new Group(group.Values.Select(holder => new Holder(holder.Values.Select(condition =>
                    new Condition(condition.Condition, condition.Comparison, condition.Arguments.ToArray())).ToArray())).ToArray())).ToArray())).ToArray())).ToArray();
        var document = MergeRowDocument.Create(rows, selected => ZaShopsWorkflowService.WriteLineupRows(selected.Select(row =>
            new ZaShopsWorkflowService.ShopLineupRow(row.SourceIndex, row.Name, row.Inventory.Select(item =>
                new ZaShopsWorkflowService.ShopInventoryRow(item.SourceIndex, item.ItemId, item.DisplayIndex,
                    item.Conditions.Select(group => new ZaShopsWorkflowService.ShopConditionGroup(group.Values.Select(holder =>
                        new ZaShopsWorkflowService.ShopConditionHolder(holder.Values.Select(condition =>
                            new ZaShopsWorkflowService.ShopAppearCondition(condition.Value, condition.Comparison, condition.Arguments)).ToArray())).ToArray())).ToArray(), item.RowId)).ToArray())).ToArray()), "shops");
        KM.Formats.FlatBufferMergeGuard.ValidateRoundTrip(table, document.Write(document.Content.DeepClone()));
        return document;
    }

    private sealed record Lineup(int SourceIndex, string Name, Inventory[] Inventory);
    private sealed record Inventory(int SourceIndex, uint ItemId, uint DisplayIndex, string RowId, Group[] Conditions);
    private sealed record Group(Holder[] Values);
    private sealed record Holder(Condition[] Values);
    private sealed record Condition(string Value, uint Comparison, string[] Arguments);
}
