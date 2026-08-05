// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.ZA.Data;

/// <summary>
/// Preserves the boss move selector FlatBuffer byte-for-byte while allowing
/// updates to verified, materialized move ID fields.
/// </summary>
internal sealed class ZaBossMoveSelectorDocument
{
    private const int RootGroupsField = 0;
    private const int GroupRowsField = 0;
    private const int ActionIdField = 1;
    private const int LotteryTypeField = 2;
    private const int MoveIdField = 4;
    private const int SelectMoveLotteryType = 2;

    private readonly byte[] originalBytes;
    private readonly Dictionary<int, int> pendingWrites = [];
    private readonly IReadOnlyDictionary<int, ZaBossMoveSelectorRow> rowsByActionId;

    private ZaBossMoveSelectorDocument(
        byte[] originalBytes,
        IReadOnlyList<ZaBossMoveSelectorRow> rows)
    {
        this.originalBytes = originalBytes;
        Rows = rows;
        rowsByActionId = rows
            .Where(row => row.HasUniqueActionId)
            .ToDictionary(row => row.ActionId);
    }

    public IReadOnlyList<ZaBossMoveSelectorRow> Rows { get; }

    public bool HasChanges => pendingWrites.Count > 0;

    public static ZaBossMoveSelectorDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var originalBytes = bytes.ToArray();
        var reader = new ZaPokemonSpawnerFlatBufferReader(originalBytes);
        var rootPosition = reader.GetRootTablePosition();
        var groups = reader.GetTableVector(rootPosition, RootGroupsField)
            ?? throw new InvalidDataException("Boss move selector data has no group vector.");
        var rows = new List<ZaBossMoveSelectorRow>();

        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var groupPosition = reader.GetTableVectorElement(groups, groupIndex);
            if (groupPosition is null)
            {
                continue;
            }

            var groupRows = reader.GetTableVector(groupPosition.Value, GroupRowsField);
            if (groupRows is null)
            {
                continue;
            }

            for (var rowIndex = 0; rowIndex < groupRows.Value.Length; rowIndex++)
            {
                var rowPosition = reader.GetTableVectorElement(groupRows.Value, rowIndex);
                if (rowPosition is null)
                {
                    continue;
                }

                var actionId = reader.GetInt32(rowPosition.Value, ActionIdField);
                var lotteryType = reader.GetInt32(rowPosition.Value, LotteryTypeField);
                var moveId = reader.GetInt32(rowPosition.Value, MoveIdField);
                rows.Add(new ZaBossMoveSelectorRow(
                    groupIndex,
                    rowIndex,
                    actionId.Value,
                    lotteryType.Value,
                    moveId.Value,
                    actionId.Position,
                    moveId.Position));
            }
        }

        if (rows.Count == 0)
        {
            throw new InvalidDataException("Boss move selector data contains no rows.");
        }

        foreach (var group in rows.GroupBy(row => row.ActionId))
        {
            var isUnique = group.Count() == 1;
            foreach (var row in group)
            {
                row.HasUniqueActionId = isUnique;
            }
        }

        foreach (var group in rows
                     .Where(row => row.MoveIdPosition is not null)
                     .GroupBy(row => row.MoveIdPosition!.Value))
        {
            var hasExclusiveStorage = group.Count() == 1;
            foreach (var row in group)
            {
                row.HasExclusiveMoveIdStorage = hasExclusiveStorage;
            }
        }

        return new ZaBossMoveSelectorDocument(originalBytes, rows);
    }

    public bool TryGetRow(int actionId, out ZaBossMoveSelectorRow row)
    {
        return rowsByActionId.TryGetValue(actionId, out row!);
    }

    public bool TrySetRuntimeMoveId(int actionId, int runtimeMoveId, out string? error)
    {
        if (!TryGetRow(actionId, out var row))
        {
            error = "The selector action ID is missing or ambiguous.";
            return false;
        }

        if (!row.CanEdit)
        {
            error = row.LotteryType != SelectMoveLotteryType
                ? "The selector row is not a move-selection action."
                : "The selector move ID does not have exclusive materialized storage.";
            return false;
        }

        pendingWrites[row.MoveIdPosition!.Value] = runtimeMoveId;
        row.RuntimeMoveId = runtimeMoveId;
        error = null;
        return true;
    }

    public byte[] Write()
    {
        var output = originalBytes.ToArray();
        foreach (var (position, value) in pendingWrites)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(position, sizeof(int)),
                value);
        }

        return output;
    }
}

internal sealed class ZaBossMoveSelectorRow
{
    public ZaBossMoveSelectorRow(
        int groupIndex,
        int rowIndex,
        int actionId,
        int lotteryType,
        int runtimeMoveId,
        int? actionIdPosition,
        int? moveIdPosition)
    {
        GroupIndex = groupIndex;
        RowIndex = rowIndex;
        ActionId = actionId;
        LotteryType = lotteryType;
        RuntimeMoveId = runtimeMoveId;
        ActionIdPosition = actionIdPosition;
        MoveIdPosition = moveIdPosition;
    }

    public int GroupIndex { get; }

    public int RowIndex { get; }

    public int ActionId { get; }

    public int LotteryType { get; }

    public int RuntimeMoveId { get; internal set; }

    public bool CanEdit =>
        LotteryType == 2
        && HasUniqueActionId
        && HasExclusiveMoveIdStorage
        && ActionIdPosition is not null
        && MoveIdPosition is not null;

    internal int? ActionIdPosition { get; }

    internal int? MoveIdPosition { get; }

    internal bool HasUniqueActionId { get; set; }

    internal bool HasExclusiveMoveIdStorage { get; set; }
}
