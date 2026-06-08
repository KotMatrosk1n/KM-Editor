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
    int? MachineSlot,
    ushort? MachineMoveId,
    IReadOnlyList<int> SharedItemIds);

public sealed record SwShItemTableEdit(
    int ItemId,
    SwShItemTableField Field,
    int Value);

public sealed record SwShItemTableCloneEdit(
    int TemplateItemId,
    int TargetItemId);

public enum SwShItemTableField
{
    BuyPrice,
    WattsPrice,
    AlternatePrice,
    Pouch,
    PouchFlags,
    FlingPower,
    FieldUseType,
    FieldFlags,
    CanUseOnPokemon,
    ItemType,
    SortIndex,
    ItemSprite,
    GroupType,
    GroupIndex,
    CureStatusFlags,
    UseFlags1,
    UseFlags2,
    EvHp,
    EvAttack,
    EvDefense,
    EvSpeed,
    EvSpecialAttack,
    EvSpecialDefense,
    HealAmount,
    PpGain,
    FriendshipGain1,
    FriendshipGain2,
    FriendshipGain3,
    MachineMove,
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
    private const int MachineTablePointerOffset = 0x02;
    private const int MachineTableCount = 200;
    private const int MachineTableEntrySize = 4;
    private const int MachineTableMoveOffset = 2;
    private const int MachineTableGroupType = 4;
    private const int MachineTableFieldUseType = 2;

    private readonly byte[] data;
    private readonly int machineTableOffset;
    private readonly IReadOnlyList<ushort>? machineMoves;
    private readonly Dictionary<int, SwShItemTableRecord> recordsByItemId;

    private SwShItemTable(
        byte[] data,
        int rowsStart,
        IReadOnlyList<int> rawRowIndexes,
        int machineTableOffset,
        IReadOnlyList<ushort>? machineMoves)
    {
        this.data = data;
        this.machineTableOffset = machineTableOffset;
        this.machineMoves = machineMoves;
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

        var machineTableOffset = TryReadMachineMoves(data, out var machineMoves)
            ? ResolveMachineTableOffset(data)
            : -1;

        return new SwShItemTable(data.ToArray(), rowsStart, rawRowIndexes, machineTableOffset, machineMoves);
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
                    WriteUInt32(result, rowOffset + BuyPriceOffset, edit.Value);
                    break;
                case SwShItemTableField.WattsPrice:
                    WriteUInt32(result, rowOffset + WattsPriceOffset, edit.Value);
                    break;
                case SwShItemTableField.AlternatePrice:
                    WriteUInt32(result, rowOffset + AlternatePriceOffset, edit.Value);
                    break;
                case SwShItemTableField.Pouch:
                    WritePackedNibble(result, rowOffset + PouchOffset, edit.Value, writeHighNibble: false);
                    break;
                case SwShItemTableField.PouchFlags:
                    WritePackedNibble(result, rowOffset + PouchOffset, edit.Value, writeHighNibble: true);
                    break;
                case SwShItemTableField.FlingPower:
                    WriteByte(result, rowOffset + FlingPowerOffset, edit.Value);
                    break;
                case SwShItemTableField.FieldUseType:
                    WriteByte(result, rowOffset + FieldUseTypeOffset, edit.Value);
                    break;
                case SwShItemTableField.FieldFlags:
                    WriteByte(result, rowOffset + FieldFlagsOffset, edit.Value);
                    break;
                case SwShItemTableField.CanUseOnPokemon:
                    WriteBooleanByte(result, rowOffset + CanUseOnPokemonOffset, edit.Value);
                    break;
                case SwShItemTableField.ItemType:
                    WriteByte(result, rowOffset + ItemTypeOffset, edit.Value);
                    break;
                case SwShItemTableField.SortIndex:
                    WriteByte(result, rowOffset + SortIndexOffset, edit.Value);
                    break;
                case SwShItemTableField.ItemSprite:
                    WriteInt16(result, rowOffset + ItemSpriteOffset, edit.Value);
                    break;
                case SwShItemTableField.GroupType:
                    WriteByte(result, rowOffset + GroupTypeOffset, edit.Value);
                    break;
                case SwShItemTableField.GroupIndex:
                    WriteByte(result, rowOffset + GroupIndexOffset, edit.Value);
                    break;
                case SwShItemTableField.CureStatusFlags:
                    WriteByte(result, rowOffset + CureStatusFlagsOffset, edit.Value);
                    break;
                case SwShItemTableField.UseFlags1:
                    WriteByte(result, rowOffset + UseFlags1Offset, edit.Value);
                    break;
                case SwShItemTableField.UseFlags2:
                    WriteByte(result, rowOffset + UseFlags2Offset, edit.Value);
                    break;
                case SwShItemTableField.EvHp:
                    WriteSignedByte(result, rowOffset + EvHpOffset, edit.Value);
                    break;
                case SwShItemTableField.EvAttack:
                    WriteSignedByte(result, rowOffset + EvAttackOffset, edit.Value);
                    break;
                case SwShItemTableField.EvDefense:
                    WriteSignedByte(result, rowOffset + EvDefenseOffset, edit.Value);
                    break;
                case SwShItemTableField.EvSpeed:
                    WriteSignedByte(result, rowOffset + EvSpeedOffset, edit.Value);
                    break;
                case SwShItemTableField.EvSpecialAttack:
                    WriteSignedByte(result, rowOffset + EvSpecialAttackOffset, edit.Value);
                    break;
                case SwShItemTableField.EvSpecialDefense:
                    WriteSignedByte(result, rowOffset + EvSpecialDefenseOffset, edit.Value);
                    break;
                case SwShItemTableField.HealAmount:
                    WriteByte(result, rowOffset + HealAmountOffset, edit.Value);
                    break;
                case SwShItemTableField.PpGain:
                    WriteByte(result, rowOffset + PpGainOffset, edit.Value);
                    break;
                case SwShItemTableField.FriendshipGain1:
                    WriteSignedByte(result, rowOffset + FriendshipGain1Offset, edit.Value);
                    break;
                case SwShItemTableField.FriendshipGain2:
                    WriteSignedByte(result, rowOffset + FriendshipGain2Offset, edit.Value);
                    break;
                case SwShItemTableField.FriendshipGain3:
                    WriteSignedByte(result, rowOffset + FriendshipGain3Offset, edit.Value);
                    break;
                case SwShItemTableField.MachineMove:
                    WriteMachineMove(result, record, edit.Value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), $"Item field '{edit.Field}' is not supported.");
            }
        }

        return result;
    }

    private static void WriteByte(byte[] data, int offset, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, byte.MaxValue);

        data[offset] = checked((byte)value);
    }

    private static void WriteUInt32(byte[] data, int offset, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), checked((uint)value));
    }

    private static void WriteSignedByte(byte[] data, int offset, int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, sbyte.MinValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, sbyte.MaxValue);

        data[offset] = unchecked((byte)(sbyte)value);
    }

    private static void WriteBooleanByte(byte[] data, int offset, int value)
    {
        if (value is not 0 and not 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Boolean item metadata fields must be 0 or 1.");
        }

        data[offset] = checked((byte)value);
    }

    private static void WritePackedNibble(byte[] data, int offset, int value, bool writeHighNibble)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 0x0F);

        data[offset] = writeHighNibble
            ? (byte)((data[offset] & 0x0F) | (value << 4))
            : (byte)((data[offset] & 0xF0) | value);
    }

    private static void WriteInt16(byte[] data, int offset, int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, short.MinValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, short.MaxValue);

        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset), checked((short)value));
    }

    private void WriteMachineMove(byte[] data, SwShItemTableRecord record, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);

        if (record.MachineSlot is null)
        {
            throw new ArgumentOutOfRangeException(nameof(record), $"Item {record.ItemId} is not linked to a TM/TR slot.");
        }

        if (machineTableOffset < 0 || machineTableOffset + (MachineTableCount * MachineTableEntrySize) > data.Length)
        {
            throw new InvalidDataException("Item machine move table is not available in this item data file.");
        }

        var moveOffset = machineTableOffset + (record.MachineSlot.Value * MachineTableEntrySize) + MachineTableMoveOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(moveOffset), checked((ushort)value));
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
        var fieldUseType = data[rowOffset + FieldUseTypeOffset];
        var groupType = data[rowOffset + GroupTypeOffset];
        var groupIndex = data[rowOffset + GroupIndexOffset];
        var machineSlot = TryGetMachineSlot(groupType, fieldUseType, groupIndex, out var slot)
            ? slot
            : (int?)null;
        var machineMove = machineSlot is not null && machineMoves is not null
            ? machineMoves[machineSlot.Value]
            : (ushort?)null;

        return new SwShItemTableRecord(
            itemId,
            rawRowIndex,
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + BuyPriceOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + WattsPriceOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + AlternatePriceOffset)),
            pouch,
            (byte)(pouchByte >> 4),
            data[rowOffset + FlingPowerOffset],
            fieldUseType,
            data[rowOffset + FieldFlagsOffset],
            data[rowOffset + CanUseOnPokemonOffset] == 1,
            data[rowOffset + ItemTypeOffset],
            data[rowOffset + SortIndexOffset],
            BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(rowOffset + ItemSpriteOffset)),
            groupType,
            groupIndex,
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
            machineSlot,
            machineMove,
            aliases);
    }

    private static bool TryGetMachineSlot(byte groupType, byte fieldUseType, byte groupIndex, out int slot)
    {
        if (groupType == MachineTableGroupType
            && fieldUseType == MachineTableFieldUseType
            && groupIndex < MachineTableCount)
        {
            slot = groupIndex;
            return true;
        }

        slot = 0;
        return false;
    }

    private static bool TryReadMachineMoves(ReadOnlySpan<byte> data, out IReadOnlyList<ushort>? moves)
    {
        var tableOffset = ResolveMachineTableOffset(data);
        if (tableOffset < 0)
        {
            moves = null;
            return false;
        }

        var result = new ushort[MachineTableCount];
        for (var slot = 0; slot < result.Length; slot++)
        {
            result[slot] = BinaryPrimitives.ReadUInt16LittleEndian(
                data[(tableOffset + (slot * MachineTableEntrySize) + MachineTableMoveOffset)..]);
        }

        moves = result;
        return true;
    }

    private static int ResolveMachineTableOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length < MachineTablePointerOffset + sizeof(ushort))
        {
            return -1;
        }

        var tablePointer = BinaryPrimitives.ReadUInt16LittleEndian(data[MachineTablePointerOffset..]);
        if (tablePointer == 0)
        {
            return -1;
        }

        var tableBase = tablePointer * 2;
        var tableOffset = HeaderSize + tableBase;
        return tableOffset >= HeaderSize
            && tableOffset + (MachineTableCount * MachineTableEntrySize) <= data.Length
            ? tableOffset
            : -1;
    }
}
