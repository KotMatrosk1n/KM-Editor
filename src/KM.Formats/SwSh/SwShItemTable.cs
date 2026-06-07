// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShItemTableRecord(
    int ItemId,
    int RawRowIndex,
    uint BuyPrice,
    uint WattsPrice,
    uint AlternatePrice,
    SwShItemPouch Pouch,
    IReadOnlyList<int> SharedItemIds);

public sealed record SwShItemTableEdit(
    int ItemId,
    SwShItemTableField Field,
    uint Value);

public enum SwShItemTableField
{
    BuyPrice,
    WattsPrice,
    AlternatePrice,
}

public enum SwShItemPouch : byte
{
    Medicine = 0,
    Balls = 1,
    BattleItems = 2,
    Berries = 3,
    Items = 4,
    TMs = 5,
    Treasures = 6,
    Ingredients = 7,
    KeyItems = 8,
}

public sealed class SwShItemTable
{
    public const string ItemDataRelativePath = "romfs/bin/pml/item/item.dat";

    private const int HeaderSize = 0x44;
    private const int RowSize = 0x30;
    private const int BuyPriceOffset = 0x00;
    private const int WattsPriceOffset = 0x04;
    private const int AlternatePriceOffset = 0x08;
    private const int PouchOffset = 0x11;
    private const int EntryTableOffset = 0x44;
    private const int MaxRowIndexOffset = 0x04;
    private const int RowsStartOffset = 0x40;

    private readonly byte[] data;
    private readonly Dictionary<int, SwShItemTableRecord> recordsByItemId;

    private SwShItemTable(byte[] data, int rowsStart, IReadOnlyList<int> rawRowIndexes)
    {
        this.data = data;
        RowsStart = rowsStart;
        RawRowIndexes = rawRowIndexes;

        var aliasesByRow = rawRowIndexes
            .Select((rowIndex, itemId) => new { rowIndex, itemId })
            .GroupBy(entry => entry.rowIndex)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group.Select(entry => entry.itemId).ToArray());

        Records = rawRowIndexes
            .Select((rowIndex, itemId) => ReadRecord(itemId, rowIndex, aliasesByRow[rowIndex]))
            .ToArray();
        recordsByItemId = Records.ToDictionary(record => record.ItemId);
    }

    public IReadOnlyList<SwShItemTableRecord> Records { get; }

    private int RowsStart { get; }

    private IReadOnlyList<int> RawRowIndexes { get; }

    public static SwShItemTable Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("Item table is too small to contain a Sword/Shield item header.");
        }

        var itemCount = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var maxRowIndex = BinaryPrimitives.ReadUInt16LittleEndian(data[MaxRowIndexOffset..]);
        var rowsStart = BinaryPrimitives.ReadInt32LittleEndian(data[RowsStartOffset..]);

        if (rowsStart < HeaderSize)
        {
            throw new InvalidDataException("Item table row data starts before the header is complete.");
        }

        var entryTableLength = checked(itemCount * sizeof(ushort));
        if (EntryTableOffset + entryTableLength > data.Length)
        {
            throw new InvalidDataException("Item table index extends past the end of the file.");
        }

        var rowsLength = checked(maxRowIndex * RowSize);
        if (rowsStart + rowsLength > data.Length)
        {
            throw new InvalidDataException("Item table row data extends past the end of the file.");
        }

        var rawRowIndexes = new int[itemCount];
        for (var itemId = 0; itemId < rawRowIndexes.Length; itemId++)
        {
            var rowIndex = BinaryPrimitives.ReadUInt16LittleEndian(data[(EntryTableOffset + (itemId * sizeof(ushort)))..]);
            if (rowIndex >= maxRowIndex)
            {
                throw new InvalidDataException($"Item {itemId} points at row {rowIndex}, outside the item row table.");
            }

            rawRowIndexes[itemId] = rowIndex;
        }

        return new SwShItemTable(data.ToArray(), rowsStart, rawRowIndexes);
    }

    public byte[] WriteEdits(IReadOnlyList<SwShItemTableEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var result = data.ToArray();
        foreach (var edit in edits)
        {
            if (!recordsByItemId.TryGetValue(edit.ItemId, out var record))
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"Item {edit.ItemId} is not present in the item table.");
            }

            var rowOffset = RowsStart + (record.RawRowIndex * RowSize);
            switch (edit.Field)
            {
                case SwShItemTableField.BuyPrice:
                    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rowOffset + BuyPriceOffset), edit.Value);
                    break;
                case SwShItemTableField.WattsPrice:
                    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rowOffset + WattsPriceOffset), edit.Value);
                    break;
                case SwShItemTableField.AlternatePrice:
                    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rowOffset + AlternatePriceOffset), edit.Value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), $"Item field '{edit.Field}' is not supported.");
            }
        }

        return result;
    }

    private SwShItemTableRecord ReadRecord(int itemId, int rawRowIndex, IReadOnlyList<int> aliases)
    {
        var rowOffset = RowsStart + (rawRowIndex * RowSize);
        var pouch = (SwShItemPouch)(data[rowOffset + PouchOffset] & 0x0F);

        return new SwShItemTableRecord(
            itemId,
            rawRowIndex,
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + BuyPriceOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + WattsPriceOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + AlternatePriceOffset)),
            pouch,
            aliases);
    }
}
