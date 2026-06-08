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
    byte PouchFlags,
    byte FlingPower,
    byte FieldUseType,
    byte FieldFlags,
    bool CanUseOnPokemon,
    byte ItemType,
    byte SortIndex,
    short ItemSprite,
    byte GroupType,
    byte GroupIndex,
    byte CureStatusFlags,
    byte Boost0,
    byte Boost1,
    byte Boost2,
    byte Boost3,
    byte UseFlags1,
    byte UseFlags2,
    sbyte EvHp,
    sbyte EvAttack,
    sbyte EvDefense,
    sbyte EvSpeed,
    sbyte EvSpecialAttack,
    sbyte EvSpecialDefense,
    byte HealAmount,
    byte PpGain,
    sbyte FriendshipGain1,
    sbyte FriendshipGain2,
    sbyte FriendshipGain3,
    IReadOnlyList<int> SharedItemIds);

public sealed record SwShItemTableEdit(
    int ItemId,
    SwShItemTableField Field,
    uint Value);

public sealed record SwShItemTableCloneEdit(
    int TemplateItemId,
    int TargetItemId);

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
    private const int FlingPowerOffset = 0x12;
    private const int FieldUseTypeOffset = 0x13;
    private const int FieldFlagsOffset = 0x14;
    private const int CanUseOnPokemonOffset = 0x15;
    private const int ItemTypeOffset = 0x16;
    private const int SortIndexOffset = 0x18;
    private const int ItemSpriteOffset = 0x1A;
    private const int GroupTypeOffset = 0x1C;
    private const int GroupIndexOffset = 0x1D;
    private const int CureStatusFlagsOffset = 0x1E;
    private const int Boost0Offset = 0x1F;
    private const int Boost1Offset = 0x20;
    private const int Boost2Offset = 0x21;
    private const int Boost3Offset = 0x22;
    private const int UseFlags1Offset = 0x23;
    private const int UseFlags2Offset = 0x24;
    private const int EvHpOffset = 0x25;
    private const int EvAttackOffset = 0x26;
    private const int EvDefenseOffset = 0x27;
    private const int EvSpeedOffset = 0x28;
    private const int EvSpecialAttackOffset = 0x29;
    private const int EvSpecialDefenseOffset = 0x2A;
    private const int HealAmountOffset = 0x2B;
    private const int PpGainOffset = 0x2C;
    private const int FriendshipGain1Offset = 0x2D;
    private const int FriendshipGain2Offset = 0x2E;
    private const int FriendshipGain3Offset = 0x2F;
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

    public byte[] WriteClonedRows(IReadOnlyList<SwShItemTableCloneEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var result = data.ToArray();
        foreach (var edit in edits)
        {
            if (!recordsByItemId.TryGetValue(edit.TemplateItemId, out var templateRecord))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(edits),
                    $"Template item {edit.TemplateItemId} is not present in the item table.");
            }

            if (!recordsByItemId.TryGetValue(edit.TargetItemId, out var targetRecord))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(edits),
                    $"Target item {edit.TargetItemId} is not present in the item table.");
            }

            var templateRowOffset = RowsStart + (templateRecord.RawRowIndex * RowSize);
            var targetRowOffset = RowsStart + (targetRecord.RawRowIndex * RowSize);
            result.AsSpan(templateRowOffset, RowSize).CopyTo(result.AsSpan(targetRowOffset, RowSize));
        }

        return result;
    }

    private SwShItemTableRecord ReadRecord(int itemId, int rawRowIndex, IReadOnlyList<int> aliases)
    {
        var rowOffset = RowsStart + (rawRowIndex * RowSize);
        var pouchByte = data[rowOffset + PouchOffset];
        var pouch = (SwShItemPouch)(pouchByte & 0x0F);

        return new SwShItemTableRecord(
            itemId,
            rawRowIndex,
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + BuyPriceOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + WattsPriceOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + AlternatePriceOffset)),
            pouch,
            (byte)(pouchByte >> 4),
            data[rowOffset + FlingPowerOffset],
            data[rowOffset + FieldUseTypeOffset],
            data[rowOffset + FieldFlagsOffset],
            data[rowOffset + CanUseOnPokemonOffset] == 1,
            data[rowOffset + ItemTypeOffset],
            data[rowOffset + SortIndexOffset],
            BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(rowOffset + ItemSpriteOffset)),
            data[rowOffset + GroupTypeOffset],
            data[rowOffset + GroupIndexOffset],
            data[rowOffset + CureStatusFlagsOffset],
            data[rowOffset + Boost0Offset],
            data[rowOffset + Boost1Offset],
            data[rowOffset + Boost2Offset],
            data[rowOffset + Boost3Offset],
            data[rowOffset + UseFlags1Offset],
            data[rowOffset + UseFlags2Offset],
            unchecked((sbyte)data[rowOffset + EvHpOffset]),
            unchecked((sbyte)data[rowOffset + EvAttackOffset]),
            unchecked((sbyte)data[rowOffset + EvDefenseOffset]),
            unchecked((sbyte)data[rowOffset + EvSpeedOffset]),
            unchecked((sbyte)data[rowOffset + EvSpecialAttackOffset]),
            unchecked((sbyte)data[rowOffset + EvSpecialDefenseOffset]),
            data[rowOffset + HealAmountOffset],
            data[rowOffset + PpGainOffset],
            unchecked((sbyte)data[rowOffset + FriendshipGain1Offset]),
            unchecked((sbyte)data[rowOffset + FriendshipGain2Offset]),
            unchecked((sbyte)data[rowOffset + FriendshipGain3Offset]),
            aliases);
    }
}
