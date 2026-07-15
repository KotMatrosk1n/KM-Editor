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
    byte BattlePouch,
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
    BattlePouch,
    CanUseOnPokemon,
    ItemType,
    SortIndex,
    ItemSprite,
    GroupType,
    GroupIndex,
    CureStatusFlags,
    CureSleep,
    CurePoison,
    CureBurn,
    CureFreeze,
    CureParalysis,
    CureConfusion,
    CureInfatuation,
    GuardSpec,
    CanTargetFaintedPokemon,
    RevivesWholeParty,
    LevelUpItem,
    EvolutionItem,
    AttackBoost,
    DefenseBoost,
    SpecialAttackBoost,
    SpecialDefenseBoost,
    SpeedBoost,
    AccuracyBoost,
    CriticalHitBoost,
    PpUpFlag,
    PpMaxFlag,
    UseFlags1,
    UseFlags2,
    RestorePpFlag,
    RestoreAllPpFlag,
    RestoreHpFlag,
    HpEvFlag,
    AttackEvFlag,
    DefenseEvFlag,
    SpeedEvFlag,
    SpecialAttackEvFlag,
    SpecialDefenseEvFlag,
    EvAbove100Flag,
    Friendship1Flag,
    Friendship2Flag,
    Friendship3Flag,
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
    private const int BattlePouchOffset = 0x14;
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
    private readonly IReadOnlyList<MachineTableEntry>? machineEntries;
    private readonly Dictionary<int, SwShItemTableRecord> recordsByItemId;

    private SwShItemTable(
        byte[] data,
        int rowsStart,
        IReadOnlyList<int> rawRowIndexes,
        int machineTableOffset,
        IReadOnlyList<MachineTableEntry>? machineEntries)
    {
        this.data = data;
        this.machineTableOffset = machineTableOffset;
        this.machineEntries = machineEntries;
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
        var entryTableEnd = checked(EntryTableOffset + entryTableLength);
        if (entryTableEnd > data.Length)
        {
            throw new InvalidDataException("Item table index extends past the end of the file.");
        }

        if (rowsStart < entryTableEnd)
        {
            throw new InvalidDataException("Item table row data overlaps the header or item index.");
        }

        var rowsLength = checked(maxRowIndex * RowSize);
        if (rowsLength > data.Length || rowsStart > data.Length - rowsLength)
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

        var (machineTableOffset, machineEntries) = ReadMachineTable(
            data,
            entryTableEnd,
            rowsStart,
            rowsLength);

        return new SwShItemTable(data.ToArray(), rowsStart, rawRowIndexes, machineTableOffset, machineEntries);
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

            if (edit.Field == SwShItemTableField.MachineMove)
            {
                continue;
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
                case SwShItemTableField.BattlePouch:
                    WriteByte(result, rowOffset + BattlePouchOffset, edit.Value);
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
                case SwShItemTableField.CureSleep:
                    WritePackedBit(result, rowOffset + CureStatusFlagsOffset, 0, edit.Value);
                    break;
                case SwShItemTableField.CurePoison:
                    WritePackedBit(result, rowOffset + CureStatusFlagsOffset, 1, edit.Value);
                    break;
                case SwShItemTableField.CureBurn:
                    WritePackedBit(result, rowOffset + CureStatusFlagsOffset, 2, edit.Value);
                    break;
                case SwShItemTableField.CureFreeze:
                    WritePackedBit(result, rowOffset + CureStatusFlagsOffset, 3, edit.Value);
                    break;
                case SwShItemTableField.CureParalysis:
                    WritePackedBit(result, rowOffset + CureStatusFlagsOffset, 4, edit.Value);
                    break;
                case SwShItemTableField.CureConfusion:
                    WritePackedBit(result, rowOffset + CureStatusFlagsOffset, 5, edit.Value);
                    break;
                case SwShItemTableField.CureInfatuation:
                    WritePackedBit(result, rowOffset + CureStatusFlagsOffset, 6, edit.Value);
                    break;
                case SwShItemTableField.GuardSpec:
                    WritePackedBit(result, rowOffset + CureStatusFlagsOffset, 7, edit.Value);
                    break;
                case SwShItemTableField.CanTargetFaintedPokemon:
                    WritePackedBit(result, rowOffset + Boost0Offset, 0, edit.Value);
                    break;
                case SwShItemTableField.RevivesWholeParty:
                    WritePackedBit(result, rowOffset + Boost0Offset, 1, edit.Value);
                    break;
                case SwShItemTableField.LevelUpItem:
                    WritePackedBit(result, rowOffset + Boost0Offset, 2, edit.Value);
                    break;
                case SwShItemTableField.EvolutionItem:
                    WritePackedBit(result, rowOffset + Boost0Offset, 3, edit.Value);
                    break;
                case SwShItemTableField.AttackBoost:
                    WritePackedNibble(result, rowOffset + Boost0Offset, edit.Value, writeHighNibble: true);
                    break;
                case SwShItemTableField.DefenseBoost:
                    WritePackedNibble(result, rowOffset + Boost1Offset, edit.Value, writeHighNibble: false);
                    break;
                case SwShItemTableField.SpecialAttackBoost:
                    WritePackedNibble(result, rowOffset + Boost1Offset, edit.Value, writeHighNibble: true);
                    break;
                case SwShItemTableField.SpecialDefenseBoost:
                    WritePackedNibble(result, rowOffset + Boost2Offset, edit.Value, writeHighNibble: false);
                    break;
                case SwShItemTableField.SpeedBoost:
                    WritePackedNibble(result, rowOffset + Boost2Offset, edit.Value, writeHighNibble: true);
                    break;
                case SwShItemTableField.AccuracyBoost:
                    WritePackedNibble(result, rowOffset + Boost3Offset, edit.Value, writeHighNibble: false);
                    break;
                case SwShItemTableField.CriticalHitBoost:
                    WritePackedBits(result, rowOffset + Boost3Offset, bitOffset: 4, bitCount: 2, edit.Value);
                    break;
                case SwShItemTableField.PpUpFlag:
                    WritePackedBit(result, rowOffset + Boost3Offset, 6, edit.Value);
                    break;
                case SwShItemTableField.PpMaxFlag:
                    WritePackedBit(result, rowOffset + Boost3Offset, 7, edit.Value);
                    break;
                case SwShItemTableField.UseFlags1:
                    WriteByte(result, rowOffset + UseFlags1Offset, edit.Value);
                    break;
                case SwShItemTableField.UseFlags2:
                    WriteByte(result, rowOffset + UseFlags2Offset, edit.Value);
                    break;
                case SwShItemTableField.RestorePpFlag:
                    WritePackedBit(result, rowOffset + UseFlags1Offset, 0, edit.Value);
                    break;
                case SwShItemTableField.RestoreAllPpFlag:
                    WritePackedBit(result, rowOffset + UseFlags1Offset, 1, edit.Value);
                    break;
                case SwShItemTableField.RestoreHpFlag:
                    WritePackedBit(result, rowOffset + UseFlags1Offset, 2, edit.Value);
                    break;
                case SwShItemTableField.HpEvFlag:
                    WritePackedBit(result, rowOffset + UseFlags1Offset, 3, edit.Value);
                    break;
                case SwShItemTableField.AttackEvFlag:
                    WritePackedBit(result, rowOffset + UseFlags1Offset, 4, edit.Value);
                    break;
                case SwShItemTableField.DefenseEvFlag:
                    WritePackedBit(result, rowOffset + UseFlags1Offset, 5, edit.Value);
                    break;
                case SwShItemTableField.SpeedEvFlag:
                    WritePackedBit(result, rowOffset + UseFlags1Offset, 6, edit.Value);
                    break;
                case SwShItemTableField.SpecialAttackEvFlag:
                    WritePackedBit(result, rowOffset + UseFlags1Offset, 7, edit.Value);
                    break;
                case SwShItemTableField.SpecialDefenseEvFlag:
                    WritePackedBit(result, rowOffset + UseFlags2Offset, 0, edit.Value);
                    break;
                case SwShItemTableField.EvAbove100Flag:
                    WritePackedBit(result, rowOffset + UseFlags2Offset, 1, edit.Value);
                    break;
                case SwShItemTableField.Friendship1Flag:
                    WritePackedBit(result, rowOffset + UseFlags2Offset, 2, edit.Value);
                    break;
                case SwShItemTableField.Friendship2Flag:
                    WritePackedBit(result, rowOffset + UseFlags2Offset, 3, edit.Value);
                    break;
                case SwShItemTableField.Friendship3Flag:
                    WritePackedBit(result, rowOffset + UseFlags2Offset, 4, edit.Value);
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
                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), $"Item field '{edit.Field}' is not supported.");
            }
        }

        foreach (var edit in edits)
        {
            if (edit.Field != SwShItemTableField.MachineMove)
            {
                continue;
            }

            WriteMachineMove(result, recordsByItemId[edit.ItemId], edit.Value);
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

    private static void WritePackedBit(byte[] data, int offset, int bitOffset, int value)
    {
        if (value is not 0 and not 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Packed item flag fields must be 0 or 1.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(bitOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitOffset, 7);

        var mask = 1 << bitOffset;
        data[offset] = value == 0
            ? (byte)(data[offset] & ~mask)
            : (byte)(data[offset] | mask);
    }

    private static void WritePackedBits(byte[] data, int offset, int bitOffset, int bitCount, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitOffset + bitCount, 8);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, (1 << bitCount) - 1);

        var valueMask = ((1 << bitCount) - 1) << bitOffset;
        data[offset] = (byte)((data[offset] & ~valueMask) | (value << bitOffset));
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

        var machineTableLength = MachineTableCount * MachineTableEntrySize;
        if (machineTableOffset < 0
            || machineTableLength > data.Length
            || machineTableOffset > data.Length - machineTableLength)
        {
            throw new InvalidDataException("Item machine move table is not available in this item data file.");
        }

        var rowOffset = RowsStart + (record.RawRowIndex * RowSize);
        var groupType = data[rowOffset + GroupTypeOffset];
        var fieldUseType = data[rowOffset + FieldUseTypeOffset];
        var groupIndex = data[rowOffset + GroupIndexOffset];
        if (!TryGetMachineSlot(groupType, fieldUseType, groupIndex, out var machineSlot))
        {
            throw new InvalidDataException($"Item {record.ItemId} is not linked to a TM/TR slot after applying its row edits.");
        }

        var entryOffset = machineTableOffset + (machineSlot * MachineTableEntrySize);
        var ownerItemId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryOffset));
        if (ownerItemId == 0 || ownerItemId != record.ItemId)
        {
            throw new InvalidDataException(
                $"TM/TR slot {machineSlot} is owned by item {ownerItemId}, not item {record.ItemId}.");
        }

        var moveOffset = entryOffset + MachineTableMoveOffset;
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

    public byte[] WriteRoyalCandyRow(
        SwShItemTable baseTable,
        int templateItemId,
        int targetItemId)
    {
        ArgumentNullException.ThrowIfNull(baseTable);
        if (!recordsByItemId.TryGetValue(templateItemId, out var templateRecord))
        {
            throw new ArgumentOutOfRangeException(
                nameof(templateItemId),
                $"Template item {templateItemId} is not present in the item table.");
        }

        if (!recordsByItemId.TryGetValue(targetItemId, out var targetRecord))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetItemId),
                $"Target item {targetItemId} is not present in the item table.");
        }

        if (!baseTable.recordsByItemId.TryGetValue(templateItemId, out var baseTemplateRecord)
            || !baseTable.recordsByItemId.TryGetValue(targetItemId, out var baseTargetRecord))
        {
            throw new InvalidDataException(
                "Royal Candy item generation requires template item 50 and target item 1128 in the base item table.");
        }

        var maxRowIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(MaxRowIndexOffset));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxRowIndex, ushort.MaxValue);

        var targetRowOffset = RowsStart + (targetRecord.RawRowIndex * RowSize);
        var expectedBaseDestinationOwners = baseTargetRecord.SharedItemIds
            .Where(itemId => targetRecord.RawRowIndex == baseTargetRecord.RawRowIndex || itemId != targetItemId)
            .ToHashSet();
        var currentBaseDestinationOwners = RawRowIndexes
            .Select((rowIndex, itemId) => (rowIndex, itemId))
            .Where(entry => entry.rowIndex == baseTargetRecord.RawRowIndex)
            .Select(entry => entry.itemId)
            .ToHashSet();
        if (!currentBaseDestinationOwners.SetEquals(expectedBaseDestinationOwners))
        {
            throw new InvalidDataException(
                "The base item 1128 destination has a different owner set in the layered item table, so Royal Candy will not update a row that cannot be restored safely.");
        }

        if (targetRecord.SharedItemIds.Count == 1
            && (IsExactRoyalCandyRow(targetRowOffset, templateRecord)
                || IsExactRoyalCandyRow(targetRowOffset, baseTemplateRecord, baseTable)))
        {
            var refreshed = data.ToArray();
            var refreshTemplateOffset = RowsStart + (templateRecord.RawRowIndex * RowSize);
            data.AsSpan(refreshTemplateOffset, RowSize).CopyTo(refreshed.AsSpan(targetRowOffset, RowSize));
            WriteRoyalCandyRowShape(refreshed, targetRowOffset, templateRecord);
            return refreshed;
        }

        if (targetRecord.RawRowIndex != baseTargetRecord.RawRowIndex)
        {
            throw new InvalidDataException(
                "Item 1128 points to a non-base row that is not an exact KM Royal Candy row, so Royal Candy will not append another row or overwrite the existing mapping.");
        }

        var templateRowOffset = RowsStart + (templateRecord.RawRowIndex * RowSize);
        var appendedRowOffset = checked(RowsStart + (maxRowIndex * RowSize));
        var result = new byte[data.Length + RowSize];
        data.AsSpan(0, appendedRowOffset).CopyTo(result);
        data.AsSpan(appendedRowOffset).CopyTo(result.AsSpan(appendedRowOffset + RowSize));
        if (machineTableOffset >= appendedRowOffset)
        {
            var shiftedMachineTableOffset = checked(machineTableOffset + RowSize);
            var shiftedPointer = checked((ushort)((shiftedMachineTableOffset - HeaderSize) / sizeof(ushort)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(MachineTablePointerOffset),
                shiftedPointer);
        }

        result.AsSpan(templateRowOffset, RowSize).CopyTo(result.AsSpan(appendedRowOffset, RowSize));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(MaxRowIndexOffset), checked((ushort)(maxRowIndex + 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(EntryTableOffset + (targetItemId * sizeof(ushort))),
            maxRowIndex);

        WriteRoyalCandyRowShape(result, appendedRowOffset, templateRecord);

        return result;
    }

    public byte[] RestoreRoyalCandyRowFromBase(
        SwShItemTable baseTable,
        int templateItemId,
        int targetItemId)
    {
        ArgumentNullException.ThrowIfNull(baseTable);
        if (!recordsByItemId.TryGetValue(templateItemId, out var templateRecord)
            || !recordsByItemId.TryGetValue(targetItemId, out var targetRecord)
            || !baseTable.recordsByItemId.TryGetValue(templateItemId, out var baseTemplateRecord)
            || !baseTable.recordsByItemId.TryGetValue(targetItemId, out var baseTargetRecord))
        {
            throw new InvalidDataException("Royal Candy item restore requires template item 50 and target item 1128 in both current and base tables.");
        }

        var targetRowOffset = RowsStart + (targetRecord.RawRowIndex * RowSize);
        if (!IsExactRoyalCandyRow(targetRowOffset, templateRecord)
            && !IsExactRoyalCandyRow(targetRowOffset, baseTemplateRecord, baseTable))
        {
            throw new InvalidDataException("Item 1128 does not point to an exact KM Royal Candy row, so its mapping will not be restored automatically.");
        }

        var maxRowIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(MaxRowIndexOffset));
        var baseMaxRowIndex = BinaryPrimitives.ReadUInt16LittleEndian(baseTable.data.AsSpan(MaxRowIndexOffset));
        if ((uint)baseTargetRecord.RawRowIndex >= maxRowIndex || baseMaxRowIndex > maxRowIndex)
        {
            throw new InvalidDataException("Base item 1128 points outside the current item row table.");
        }

        var baseTargetOffset = RowsStart + (baseTargetRecord.RawRowIndex * RowSize);
        var sourceBaseTargetOffset = baseTable.RowsStart + (baseTargetRecord.RawRowIndex * RowSize);
        var expectedDestinationOwners = baseTargetRecord.SharedItemIds
            .Where(itemId => itemId != targetItemId)
            .ToHashSet();
        var currentDestinationOwners = RawRowIndexes
            .Select((rowIndex, itemId) => (rowIndex, itemId))
            .Where(entry => entry.rowIndex == baseTargetRecord.RawRowIndex
                && entry.itemId != targetItemId)
            .Select(entry => entry.itemId)
            .ToHashSet();
        var destinationIsExactOwnedRow = baseTargetRecord.SharedItemIds.Count == 1
            && (IsExactRoyalCandyRow(baseTargetOffset, templateRecord)
                || IsExactRoyalCandyRow(baseTargetOffset, baseTemplateRecord, baseTable));
        if (!currentDestinationOwners.SetEquals(expectedDestinationOwners))
        {
            throw new InvalidDataException(
                "The base item 1128 destination row has a different owner set after Royal Candy was installed, so its mapping will not be restored automatically.");
        }

        var result = data.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(EntryTableOffset + (targetItemId * sizeof(ushort))),
            checked((ushort)baseTargetRecord.RawRowIndex));

        if (destinationIsExactOwnedRow)
        {
            baseTable.data.AsSpan(sourceBaseTargetOffset, RowSize)
                .CopyTo(result.AsSpan(baseTargetOffset, RowSize));
        }

        if (targetRecord.RawRowIndex == baseTargetRecord.RawRowIndex)
        {
            return result;
        }

        var rowsEnd = checked(RowsStart + (maxRowIndex * RowSize));
        var removableRows = Enumerable.Range(baseMaxRowIndex, maxRowIndex - baseMaxRowIndex)
            .Where(rowIndex =>
            {
                var ownersAfterRestore = RawRowIndexes.Count(candidate => candidate == rowIndex)
                    - (targetRecord.RawRowIndex == rowIndex ? 1 : 0)
                    + (baseTargetRecord.RawRowIndex == rowIndex ? 1 : 0);
                var rowOffset = RowsStart + (rowIndex * RowSize);
                return ownersAfterRestore == 0
                    && (IsExactRoyalCandyRow(rowOffset, templateRecord)
                        || IsExactRoyalCandyRow(rowOffset, baseTemplateRecord, baseTable));
            })
            .OrderDescending()
            .ToArray();
        if (!removableRows.Contains(targetRecord.RawRowIndex))
        {
            throw new InvalidDataException(
                "The active Royal Candy row is not an unowned appended KM row after restoring item 1128's base mapping.");
        }

        foreach (var removedRowIndex in removableRows)
        {
            var removedRowOffset = checked(RowsStart + (removedRowIndex * RowSize));
            var compacted = new byte[result.Length - RowSize];
            result.AsSpan(0, removedRowOffset).CopyTo(compacted);
            result.AsSpan(removedRowOffset + RowSize).CopyTo(compacted.AsSpan(removedRowOffset));
            for (var itemId = 0; itemId < RawRowIndexes.Count; itemId++)
            {
                var entryOffset = EntryTableOffset + (itemId * sizeof(ushort));
                var rowIndex = BinaryPrimitives.ReadUInt16LittleEndian(compacted.AsSpan(entryOffset));
                if (rowIndex > removedRowIndex)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        compacted.AsSpan(entryOffset),
                        checked((ushort)(rowIndex - 1)));
                }
            }

            result = compacted;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(MaxRowIndexOffset),
            checked((ushort)(maxRowIndex - removableRows.Length)));
        if (machineTableOffset >= rowsEnd)
        {
            var shiftedMachineTableOffset = checked(machineTableOffset - (removableRows.Length * RowSize));
            var shiftedPointer = checked((ushort)((shiftedMachineTableOffset - HeaderSize) / sizeof(ushort)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(MachineTablePointerOffset),
                shiftedPointer);
        }

        return result;
    }

    private bool IsExactRoyalCandyRow(
        int rowOffset,
        SwShItemTableRecord templateRecord,
        SwShItemTable? templateTable = null)
    {
        var owner = templateTable ?? this;
        var templateRowOffset = owner.RowsStart + (templateRecord.RawRowIndex * RowSize);
        var expected = owner.data.AsSpan(templateRowOffset, RowSize).ToArray();
        WriteRoyalCandyRowShape(expected, 0, templateRecord);
        return data.AsSpan(rowOffset, RowSize).SequenceEqual(expected);
    }

    private static void WriteRoyalCandyRowShape(byte[] data, int rowOffset, SwShItemTableRecord templateRecord)
    {
        WriteUInt32(data, rowOffset + BuyPriceOffset, 1);
        WriteUInt32(data, rowOffset + WattsPriceOffset, 0);
        WriteUInt32(data, rowOffset + AlternatePriceOffset, checked((int)templateRecord.AlternatePrice));
        WritePackedNibble(data, rowOffset + PouchOffset, (int)SwShItemPouch.KeyItems, writeHighNibble: false);
        WritePackedNibble(data, rowOffset + PouchOffset, templateRecord.PouchFlags, writeHighNibble: true);
        WriteByte(data, rowOffset + FieldUseTypeOffset, templateRecord.FieldUseType);
        WriteBooleanByte(data, rowOffset + CanUseOnPokemonOffset, 1);
        WriteByte(data, rowOffset + ItemTypeOffset, 9);
        WriteByte(data, rowOffset + SortIndexOffset, templateRecord.SortIndex);
        WriteInt16(data, rowOffset + ItemSpriteOffset, templateRecord.ItemSprite);
        WriteByte(data, rowOffset + GroupTypeOffset, 0);
        WriteByte(data, rowOffset + GroupIndexOffset, 0);
        WritePackedBit(data, rowOffset + Boost0Offset, 2, 1);
    }

    private SwShItemTableRecord ReadRecord(int itemId, int rawRowIndex, IReadOnlyList<int> aliases)
    {
        var rowOffset = RowsStart + (rawRowIndex * RowSize);
        var pouchByte = data[rowOffset + PouchOffset];
        var pouch = (SwShItemPouch)(pouchByte & 0x0F);
        var fieldUseType = data[rowOffset + FieldUseTypeOffset];
        var groupType = data[rowOffset + GroupTypeOffset];
        var groupIndex = data[rowOffset + GroupIndexOffset];
        int? machineSlot = null;
        ushort? machineMove = null;
        if (machineEntries is not null
            && TryGetMachineSlot(groupType, fieldUseType, groupIndex, out var slot)
            && machineEntries[slot].ItemId != 0
            && machineEntries[slot].ItemId == itemId)
        {
            machineSlot = slot;
            machineMove = machineEntries[slot].MoveId;
        }

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
            data[rowOffset + BattlePouchOffset],
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

    private static (int Offset, IReadOnlyList<MachineTableEntry>? Entries) ReadMachineTable(
        ReadOnlySpan<byte> data,
        int entryTableEnd,
        int rowsStart,
        int rowsLength)
    {
        var tablePointer = BinaryPrimitives.ReadUInt16LittleEndian(data[MachineTablePointerOffset..]);
        if (tablePointer == 0)
        {
            return (-1, null);
        }

        var tableOffset = checked(HeaderSize + (tablePointer * sizeof(ushort)));
        var tableLength = MachineTableCount * MachineTableEntrySize;
        if (tableOffset < entryTableEnd)
        {
            throw new InvalidDataException("Item machine table overlaps the header or item index.");
        }

        if (tableLength > data.Length || tableOffset > data.Length - tableLength)
        {
            throw new InvalidDataException("Item machine table extends past the end of the file.");
        }

        if (RangesOverlap(tableOffset, tableLength, rowsStart, rowsLength))
        {
            throw new InvalidDataException("Item machine table overlaps the item row data.");
        }

        var entries = new MachineTableEntry[MachineTableCount];
        var ownerSlots = new Dictionary<ushort, int>();
        for (var slot = 0; slot < entries.Length; slot++)
        {
            var entryOffset = tableOffset + (slot * MachineTableEntrySize);
            var itemId = BinaryPrimitives.ReadUInt16LittleEndian(data[entryOffset..]);
            var moveId = BinaryPrimitives.ReadUInt16LittleEndian(data[(entryOffset + MachineTableMoveOffset)..]);
            if (itemId != 0 && !ownerSlots.TryAdd(itemId, slot))
            {
                throw new InvalidDataException(
                    $"Item machine table assigns item {itemId} to both slots {ownerSlots[itemId]} and {slot}.");
            }

            entries[slot] = new MachineTableEntry(itemId, moveId);
        }

        return (tableOffset, entries);
    }

    private static bool RangesOverlap(int firstStart, int firstLength, int secondStart, int secondLength)
    {
        return firstLength > 0
            && secondLength > 0
            && firstStart < secondStart + secondLength
            && secondStart < firstStart + firstLength;
    }

    private readonly record struct MachineTableEntry(ushort ItemId, ushort MoveId);
}
