// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShMoveDataFile(SwShMoveDataRecord Record)
{
    public const string MoveDataRelativeDirectory = "romfs/bin/pml/waza";

    private const int FieldCount = 51;
    private const int RootOffset = 0x00;
    private const int VTableStart = 0x04;
    private const int VTableHeaderSize = 0x04;
    private const int TableVTableOffsetSize = 0x04;

    private static readonly FieldLayout[] FieldLayouts =
    [
        new(sizeof(uint), sizeof(uint)),
        new(sizeof(uint), sizeof(uint)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(sbyte), sizeof(sbyte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(ushort), sizeof(ushort)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(sbyte), sizeof(sbyte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(ushort), sizeof(ushort)),
        new(sizeof(sbyte), sizeof(sbyte)),
        new(sizeof(sbyte), sizeof(sbyte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(sbyte), sizeof(sbyte)),
        new(sizeof(sbyte), sizeof(sbyte)),
        new(sizeof(sbyte), sizeof(sbyte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
        new(sizeof(byte), sizeof(byte)),
    ];

    public static SwShMoveDataFile Parse(ReadOnlySpan<byte> data)
    {
        var tableOffset = ReadRootTableOffset(data);
        var statChanges = new[]
        {
            new SwShMoveStatChange(1, ReadByte(data, tableOffset, fieldIndex: 23), ReadSByte(data, tableOffset, fieldIndex: 26), ReadByte(data, tableOffset, fieldIndex: 29)),
            new SwShMoveStatChange(2, ReadByte(data, tableOffset, fieldIndex: 24), ReadSByte(data, tableOffset, fieldIndex: 27), ReadByte(data, tableOffset, fieldIndex: 30)),
            new SwShMoveStatChange(3, ReadByte(data, tableOffset, fieldIndex: 25), ReadSByte(data, tableOffset, fieldIndex: 28), ReadByte(data, tableOffset, fieldIndex: 31)),
        };

        return new SwShMoveDataFile(
            new SwShMoveDataRecord(
                ReadUInt32(data, tableOffset, fieldIndex: 0),
                ReadUInt32(data, tableOffset, fieldIndex: 1),
                ReadBool(data, tableOffset, fieldIndex: 2),
                new SwShMoveCoreStats(
                    ReadByte(data, tableOffset, fieldIndex: 3),
                    ReadByte(data, tableOffset, fieldIndex: 4),
                    ReadByte(data, tableOffset, fieldIndex: 5),
                    ReadByte(data, tableOffset, fieldIndex: 6),
                    ReadByte(data, tableOffset, fieldIndex: 7),
                    ReadByte(data, tableOffset, fieldIndex: 8),
                    ReadSByte(data, tableOffset, fieldIndex: 9),
                    ReadSByte(data, tableOffset, fieldIndex: 17),
                    ReadByte(data, tableOffset, fieldIndex: 32)),
                new SwShMoveTargeting(
                    ReadByte(data, tableOffset, fieldIndex: 22),
                    ReadByte(data, tableOffset, fieldIndex: 11),
                    ReadByte(data, tableOffset, fieldIndex: 10),
                    ReadByte(data, tableOffset, fieldIndex: 15),
                    ReadByte(data, tableOffset, fieldIndex: 16)),
                new SwShMoveSecondaryEffects(
                    ReadUInt16(data, tableOffset, fieldIndex: 12),
                    ReadByte(data, tableOffset, fieldIndex: 13),
                    ReadByte(data, tableOffset, fieldIndex: 14),
                    ReadByte(data, tableOffset, fieldIndex: 18),
                    ReadUInt16(data, tableOffset, fieldIndex: 19),
                    ReadSByte(data, tableOffset, fieldIndex: 20),
                    ReadSByte(data, tableOffset, fieldIndex: 21)),
                statChanges,
                new SwShMoveFlags(
                    ReadBool(data, tableOffset, fieldIndex: 33),
                    ReadBool(data, tableOffset, fieldIndex: 34),
                    ReadBool(data, tableOffset, fieldIndex: 35),
                    ReadBool(data, tableOffset, fieldIndex: 36),
                    ReadBool(data, tableOffset, fieldIndex: 37),
                    ReadBool(data, tableOffset, fieldIndex: 38),
                    ReadBool(data, tableOffset, fieldIndex: 39),
                    ReadBool(data, tableOffset, fieldIndex: 40),
                    ReadBool(data, tableOffset, fieldIndex: 41),
                    ReadBool(data, tableOffset, fieldIndex: 42),
                    ReadBool(data, tableOffset, fieldIndex: 43),
                    ReadBool(data, tableOffset, fieldIndex: 44),
                    ReadBool(data, tableOffset, fieldIndex: 45),
                    ReadBool(data, tableOffset, fieldIndex: 46),
                    ReadBool(data, tableOffset, fieldIndex: 47),
                    ReadBool(data, tableOffset, fieldIndex: 48),
                    ReadBool(data, tableOffset, fieldIndex: 49),
                    ReadBool(data, tableOffset, fieldIndex: 50))));
    }

    public static byte[] Write(SwShMoveDataRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var fieldOffsets = GetFieldOffsets(out var objectSize);
        var vtableSize = checked((ushort)(VTableHeaderSize + (FieldCount * sizeof(ushort))));
        var tableOffset = Align(VTableStart + vtableSize, sizeof(uint));
        var data = new byte[tableOffset + objectSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(RootOffset, sizeof(uint)), checked((uint)tableOffset));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(VTableStart, sizeof(ushort)), vtableSize);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(VTableStart + sizeof(ushort), sizeof(ushort)), objectSize);

        for (var index = 0; index < fieldOffsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(VTableStart + VTableHeaderSize + (index * sizeof(ushort)), sizeof(ushort)),
                fieldOffsets[index]);
        }

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)), tableOffset - VTableStart);
        WriteUInt32(data, tableOffset, fieldOffsets[0], record.Version);
        WriteUInt32(data, tableOffset, fieldOffsets[1], record.MoveId);
        WriteBool(data, tableOffset, fieldOffsets[2], record.CanUseMove);
        WriteByte(data, tableOffset, fieldOffsets[3], record.Core.Type);
        WriteByte(data, tableOffset, fieldOffsets[4], record.Core.Quality);
        WriteByte(data, tableOffset, fieldOffsets[5], record.Core.Category);
        WriteByte(data, tableOffset, fieldOffsets[6], record.Core.Power);
        WriteByte(data, tableOffset, fieldOffsets[7], record.Core.Accuracy);
        WriteByte(data, tableOffset, fieldOffsets[8], record.Core.PP);
        WriteSByte(data, tableOffset, fieldOffsets[9], record.Core.Priority);
        WriteByte(data, tableOffset, fieldOffsets[10], record.Targeting.HitMax);
        WriteByte(data, tableOffset, fieldOffsets[11], record.Targeting.HitMin);
        WriteUInt16(data, tableOffset, fieldOffsets[12], record.Secondary.Inflict);
        WriteByte(data, tableOffset, fieldOffsets[13], record.Secondary.InflictPercent);
        WriteByte(data, tableOffset, fieldOffsets[14], record.Secondary.RawInflictCount);
        WriteByte(data, tableOffset, fieldOffsets[15], record.Targeting.TurnMin);
        WriteByte(data, tableOffset, fieldOffsets[16], record.Targeting.TurnMax);
        WriteSByte(data, tableOffset, fieldOffsets[17], record.Core.CritStage);
        WriteByte(data, tableOffset, fieldOffsets[18], record.Secondary.Flinch);
        WriteUInt16(data, tableOffset, fieldOffsets[19], record.Secondary.EffectSequence);
        WriteSByte(data, tableOffset, fieldOffsets[20], record.Secondary.Recoil);
        WriteSByte(data, tableOffset, fieldOffsets[21], record.Secondary.RawHealing);
        WriteByte(data, tableOffset, fieldOffsets[22], record.Targeting.RawTarget);

        for (var slot = 1; slot <= 3; slot++)
        {
            var statChange = GetStatChange(record.StatChanges, slot);
            WriteByte(data, tableOffset, fieldOffsets[22 + slot], statChange.Stat);
            WriteSByte(data, tableOffset, fieldOffsets[25 + slot], statChange.Stage);
            WriteByte(data, tableOffset, fieldOffsets[28 + slot], statChange.Percent);
        }

        WriteByte(data, tableOffset, fieldOffsets[32], record.Core.GigantamaxPower);
        WriteBool(data, tableOffset, fieldOffsets[33], record.Flags.MakesContact);
        WriteBool(data, tableOffset, fieldOffsets[34], record.Flags.Charge);
        WriteBool(data, tableOffset, fieldOffsets[35], record.Flags.Recharge);
        WriteBool(data, tableOffset, fieldOffsets[36], record.Flags.Protect);
        WriteBool(data, tableOffset, fieldOffsets[37], record.Flags.Reflectable);
        WriteBool(data, tableOffset, fieldOffsets[38], record.Flags.Snatch);
        WriteBool(data, tableOffset, fieldOffsets[39], record.Flags.Mirror);
        WriteBool(data, tableOffset, fieldOffsets[40], record.Flags.Punch);
        WriteBool(data, tableOffset, fieldOffsets[41], record.Flags.Sound);
        WriteBool(data, tableOffset, fieldOffsets[42], record.Flags.Gravity);
        WriteBool(data, tableOffset, fieldOffsets[43], record.Flags.Defrost);
        WriteBool(data, tableOffset, fieldOffsets[44], record.Flags.DistanceTriple);
        WriteBool(data, tableOffset, fieldOffsets[45], record.Flags.Heal);
        WriteBool(data, tableOffset, fieldOffsets[46], record.Flags.IgnoreSubstitute);
        WriteBool(data, tableOffset, fieldOffsets[47], record.Flags.FailSkyBattle);
        WriteBool(data, tableOffset, fieldOffsets[48], record.Flags.AnimateAlly);
        WriteBool(data, tableOffset, fieldOffsets[49], record.Flags.Dance);
        WriteBool(data, tableOffset, fieldOffsets[50], record.Flags.Metronome);

        return data;
    }

    private static int ReadRootTableOffset(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, RootOffset, sizeof(uint));
        var tableOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[RootOffset..]);
        if (tableOffset > int.MaxValue)
        {
            throw new InvalidDataException("Move FlatBuffer root table offset is too large.");
        }

        EnsureRange(data, (int)tableOffset, sizeof(int));
        return (int)tableOffset;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        return fieldOffset == 0
            ? 0
            : BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(uint)));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        return fieldOffset == 0
            ? (ushort)0
            : BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(ushort)));
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        return fieldOffset == 0 ? (byte)0 : data[tableOffset + fieldOffset];
    }

    private static sbyte ReadSByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        return unchecked((sbyte)ReadByte(data, tableOffset, fieldIndex));
    }

    private static bool ReadBool(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        return ReadByte(data, tableOffset, fieldIndex) != 0;
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        EnsureRange(data, vtableOffset, VTableHeaderSize);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset, sizeof(ushort)));
        var fieldOffset = VTableHeaderSize + (fieldIndex * sizeof(ushort));
        if (fieldOffset + sizeof(ushort) > vtableLength)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset + fieldOffset, sizeof(ushort)));
    }

    private static ushort[] GetFieldOffsets(out ushort objectSize)
    {
        var offsets = new ushort[FieldCount];
        var offset = TableVTableOffsetSize;
        for (var index = 0; index < FieldLayouts.Length; index++)
        {
            offset = Align(offset, FieldLayouts[index].Alignment);
            offsets[index] = checked((ushort)offset);
            offset += FieldLayouts[index].Size;
        }

        objectSize = checked((ushort)Align(offset, sizeof(uint)));
        return offsets;
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    private static SwShMoveStatChange GetStatChange(IReadOnlyList<SwShMoveStatChange> statChanges, int slot)
    {
        return statChanges.FirstOrDefault(statChange => statChange.Slot == slot)
            ?? new SwShMoveStatChange(slot, Stat: 0, Stage: 0, Percent: 0);
    }

    private static void WriteUInt32(byte[] data, int tableOffset, ushort fieldOffset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(tableOffset + fieldOffset, sizeof(uint)), value);
    }

    private static void WriteUInt16(byte[] data, int tableOffset, ushort fieldOffset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(tableOffset + fieldOffset, sizeof(ushort)), value);
    }

    private static void WriteByte(byte[] data, int tableOffset, ushort fieldOffset, byte value)
    {
        data[tableOffset + fieldOffset] = value;
    }

    private static void WriteSByte(byte[] data, int tableOffset, ushort fieldOffset, sbyte value)
    {
        data[tableOffset + fieldOffset] = unchecked((byte)value);
    }

    private static void WriteBool(byte[] data, int tableOffset, ushort fieldOffset, bool value)
    {
        data[tableOffset + fieldOffset] = value ? (byte)1 : (byte)0;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("Move FlatBuffer offset points outside the move data file.");
        }
    }

    private sealed record FieldLayout(int Size, int Alignment);
}

public sealed record SwShMoveDataRecord(
    uint Version,
    uint MoveId,
    bool CanUseMove,
    SwShMoveCoreStats Core,
    SwShMoveTargeting Targeting,
    SwShMoveSecondaryEffects Secondary,
    IReadOnlyList<SwShMoveStatChange> StatChanges,
    SwShMoveFlags Flags);

public sealed record SwShMoveCoreStats(
    byte Type,
    byte Quality,
    byte Category,
    byte Power,
    byte Accuracy,
    byte PP,
    sbyte Priority,
    sbyte CritStage,
    byte GigantamaxPower);

public sealed record SwShMoveTargeting(
    byte RawTarget,
    byte HitMin,
    byte HitMax,
    byte TurnMin,
    byte TurnMax);

public sealed record SwShMoveSecondaryEffects(
    ushort Inflict,
    byte InflictPercent,
    byte RawInflictCount,
    byte Flinch,
    ushort EffectSequence,
    sbyte Recoil,
    sbyte RawHealing);

public sealed record SwShMoveStatChange(
    int Slot,
    byte Stat,
    sbyte Stage,
    byte Percent);

public sealed record SwShMoveFlags(
    bool MakesContact,
    bool Charge,
    bool Recharge,
    bool Protect,
    bool Reflectable,
    bool Snatch,
    bool Mirror,
    bool Punch,
    bool Sound,
    bool Gravity,
    bool Defrost,
    bool DistanceTriple,
    bool Heal,
    bool IgnoreSubstitute,
    bool FailSkyBattle,
    bool AnimateAlly,
    bool Dance,
    bool Metronome);
