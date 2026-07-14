// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace KM.Formats.SwSh;

public sealed record SwShMoveDataFile(SwShMoveDataRecord Record)
{
    public const string MoveDataRelativeDirectory = "romfs/bin/pml/waza";

    private const int FieldCount = 51;
    private const int RootOffset = 0x00;
    private const int VTableStart = 0x04;
    private const int VTableHeaderSize = 0x04;
    private const int TableVTableOffsetSize = 0x04;
    private const int KnownVTableSize = VTableHeaderSize + (FieldCount * sizeof(ushort));

    private static readonly ConditionalWeakTable<SwShMoveDataFile, SourceState> SourceStates = new();

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
        var table = ReadTableLayout(data);
        ValidateTableFields(data, table);
        var statChanges = new[]
        {
            new SwShMoveStatChange(1, ReadByte(data, table, fieldIndex: 23), ReadSByte(data, table, fieldIndex: 26), ReadByte(data, table, fieldIndex: 29)),
            new SwShMoveStatChange(2, ReadByte(data, table, fieldIndex: 24), ReadSByte(data, table, fieldIndex: 27), ReadByte(data, table, fieldIndex: 30)),
            new SwShMoveStatChange(3, ReadByte(data, table, fieldIndex: 25), ReadSByte(data, table, fieldIndex: 28), ReadByte(data, table, fieldIndex: 31)),
        };

        var record = new SwShMoveDataRecord(
                ReadUInt32(data, table, fieldIndex: 0),
                ReadUInt32(data, table, fieldIndex: 1),
                ReadBool(data, table, fieldIndex: 2),
                new SwShMoveCoreStats(
                    ReadByte(data, table, fieldIndex: 3),
                    ReadByte(data, table, fieldIndex: 4),
                    ReadByte(data, table, fieldIndex: 5),
                    ReadByte(data, table, fieldIndex: 6),
                    ReadByte(data, table, fieldIndex: 7),
                    ReadByte(data, table, fieldIndex: 8),
                    ReadSByte(data, table, fieldIndex: 9),
                    ReadSByte(data, table, fieldIndex: 17),
                    ReadByte(data, table, fieldIndex: 32)),
                new SwShMoveTargeting(
                    ReadByte(data, table, fieldIndex: 22),
                    ReadByte(data, table, fieldIndex: 11),
                    ReadByte(data, table, fieldIndex: 10),
                    ReadByte(data, table, fieldIndex: 15),
                    ReadByte(data, table, fieldIndex: 16)),
                new SwShMoveSecondaryEffects(
                    ReadUInt16(data, table, fieldIndex: 12),
                    ReadByte(data, table, fieldIndex: 13),
                    ReadByte(data, table, fieldIndex: 14),
                    ReadByte(data, table, fieldIndex: 18),
                    ReadUInt16(data, table, fieldIndex: 19),
                    ReadSByte(data, table, fieldIndex: 20),
                    ReadSByte(data, table, fieldIndex: 21)),
                statChanges,
                new SwShMoveFlags(
                    ReadBool(data, table, fieldIndex: 33),
                    ReadBool(data, table, fieldIndex: 34),
                    ReadBool(data, table, fieldIndex: 35),
                    ReadBool(data, table, fieldIndex: 36),
                    ReadBool(data, table, fieldIndex: 37),
                    ReadBool(data, table, fieldIndex: 38),
                    ReadBool(data, table, fieldIndex: 39),
                    ReadBool(data, table, fieldIndex: 40),
                    ReadBool(data, table, fieldIndex: 41),
                    ReadBool(data, table, fieldIndex: 42),
                    ReadBool(data, table, fieldIndex: 43),
                    ReadBool(data, table, fieldIndex: 44),
                    ReadBool(data, table, fieldIndex: 45),
                    ReadBool(data, table, fieldIndex: 46),
                    ReadBool(data, table, fieldIndex: 47),
                    ReadBool(data, table, fieldIndex: 48),
                    ReadBool(data, table, fieldIndex: 49),
                    ReadBool(data, table, fieldIndex: 50)));

        var file = new SwShMoveDataFile(record);
        SourceStates.Add(file, new SourceState(data.ToArray(), table));
        return file;
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
        WriteRecord(data, tableOffset, fieldOffsets, record);

        return data;
    }

    /// <summary>
    /// Writes an edited record over the parsed source buffer when all non-default fields already have
    /// storage in the source table. If an absent field becomes non-default, a known-schema buffer is
    /// rebuilt only when every unclassified source byte is zero; otherwise the edit fails rather than
    /// discard opaque or extended data.
    /// </summary>
    public byte[] WriteEdited(SwShMoveDataRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!SourceStates.TryGetValue(this, out var source))
        {
            return Write(record);
        }

        var fieldOffsets = GetSourceFieldOffsets(source.Data, source.Layout);
        var values = GetFieldValues(record);
        var sparseFieldIndex = FindActivatedSparseField(fieldOffsets, values);
        if (sparseFieldIndex >= 0)
        {
            if (CanRebuildWithoutDiscardingOpaqueData(source.Data, source.Layout, fieldOffsets))
            {
                return Write(record);
            }

            throw new InvalidDataException(
                $"Move FlatBuffer field {sparseFieldIndex} is absent from the sparse source table and cannot be activated without discarding opaque or extended source data.");
        }

        var data = (byte[])source.Data.Clone();
        WriteFieldValues(data, source.Layout.TableOffset, fieldOffsets, values);
        return data;
    }

    private static TableLayout ReadTableLayout(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, RootOffset, sizeof(uint));
        var rawTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(RootOffset, sizeof(uint)));
        if (rawTableOffset < sizeof(uint))
        {
            throw new InvalidDataException("Move FlatBuffer root must point past the root offset.");
        }

        if (rawTableOffset > int.MaxValue)
        {
            throw new InvalidDataException("Move FlatBuffer root table offset is too large.");
        }

        var tableOffset = (int)rawTableOffset;
        if ((tableOffset & (sizeof(uint) - 1)) != 0)
        {
            throw new InvalidDataException("Move FlatBuffer root table offset is not 4-byte aligned.");
        }

        EnsureRange(data, tableOffset, TableVTableOffsetSize);
        var vtableDistance = BinaryPrimitives.ReadInt32LittleEndian(
            data.Slice(tableOffset, TableVTableOffsetSize));
        if (vtableDistance == 0)
        {
            throw new InvalidDataException("Move FlatBuffer table does not reference a vtable.");
        }

        var rawVTableOffset = (long)tableOffset - vtableDistance;
        if (rawVTableOffset < sizeof(uint) || rawVTableOffset > int.MaxValue)
        {
            throw new InvalidDataException("Move FlatBuffer vtable offset points outside the move data file.");
        }

        var vtableOffset = (int)rawVTableOffset;
        if ((vtableOffset & (sizeof(ushort) - 1)) != 0)
        {
            throw new InvalidDataException("Move FlatBuffer vtable offset is not 2-byte aligned.");
        }

        EnsureRange(data, vtableOffset, VTableHeaderSize);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(vtableOffset, sizeof(ushort)));
        if (vtableLength < VTableHeaderSize || (vtableLength & (sizeof(ushort) - 1)) != 0)
        {
            throw new InvalidDataException("Move FlatBuffer vtable length is invalid.");
        }

        EnsureRange(data, vtableOffset, vtableLength);
        var objectSize = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(vtableOffset + sizeof(ushort), sizeof(ushort)));
        if (objectSize < TableVTableOffsetSize)
        {
            throw new InvalidDataException("Move FlatBuffer table object size is too small.");
        }

        EnsureRange(data, tableOffset, objectSize);
        if (RangesOverlap(vtableOffset, vtableLength, tableOffset, objectSize))
        {
            throw new InvalidDataException("Move FlatBuffer vtable overlaps the table object.");
        }

        return new TableLayout(tableOffset, vtableOffset, vtableLength, objectSize);
    }

    private static void ValidateTableFields(ReadOnlySpan<byte> data, TableLayout table)
    {
        var fieldRanges = new FieldRange[FieldCount];
        var fieldRangeCount = 0;
        for (var fieldIndex = 0; fieldIndex < FieldLayouts.Length; fieldIndex++)
        {
            var fieldOffset = GetTableFieldOffset(data, table, fieldIndex);
            if (fieldOffset == 0)
            {
                continue;
            }

            var fieldLayout = FieldLayouts[fieldIndex];
            if (fieldOffset < TableVTableOffsetSize
                || fieldOffset + fieldLayout.Size > table.ObjectSize)
            {
                throw new InvalidDataException(
                    $"Move FlatBuffer field {fieldIndex} points outside the table object.");
            }

            var absoluteOffset = table.TableOffset + fieldOffset;
            if ((absoluteOffset & (fieldLayout.Alignment - 1)) != 0)
            {
                throw new InvalidDataException(
                    $"Move FlatBuffer field {fieldIndex} is not {fieldLayout.Alignment}-byte aligned.");
            }

            EnsureRange(data, absoluteOffset, fieldLayout.Size);
            for (var rangeIndex = 0; rangeIndex < fieldRangeCount; rangeIndex++)
            {
                var existing = fieldRanges[rangeIndex];
                if (RangesOverlap(fieldOffset, fieldLayout.Size, existing.Offset, existing.Length))
                {
                    throw new InvalidDataException(
                        $"Move FlatBuffer fields {existing.FieldIndex} and {fieldIndex} overlap within the table object.");
                }
            }

            fieldRanges[fieldRangeCount++] = new FieldRange(fieldIndex, fieldOffset, fieldLayout.Size);
            if (IsBooleanField(fieldIndex) && data[absoluteOffset] > 1)
            {
                throw new InvalidDataException(
                    $"Move FlatBuffer Boolean field {fieldIndex} must contain 0 or 1.");
            }
        }

        var unknownFieldStarts = new HashSet<ushort>();
        var vtableFieldCount = (table.VTableLength - VTableHeaderSize) / sizeof(ushort);
        for (var fieldIndex = FieldCount; fieldIndex < vtableFieldCount; fieldIndex++)
        {
            var fieldOffset = GetTableFieldOffset(data, table, fieldIndex);
            if (fieldOffset == 0)
            {
                continue;
            }

            if (fieldOffset < TableVTableOffsetSize || fieldOffset >= table.ObjectSize)
            {
                throw new InvalidDataException(
                    $"Move FlatBuffer unknown field {fieldIndex} points outside the table object.");
            }

            EnsureRange(data, table.TableOffset + fieldOffset, sizeof(byte));
            for (var rangeIndex = 0; rangeIndex < fieldRangeCount; rangeIndex++)
            {
                var known = fieldRanges[rangeIndex];
                if (fieldOffset >= known.Offset && fieldOffset < known.Offset + known.Length)
                {
                    throw new InvalidDataException(
                        $"Move FlatBuffer unknown field {fieldIndex} aliases known field {known.FieldIndex}.");
                }
            }

            if (!unknownFieldStarts.Add(fieldOffset))
            {
                throw new InvalidDataException(
                    $"Move FlatBuffer unknown field {fieldIndex} aliases another unknown field.");
            }
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = GetTableFieldOffset(data, table, fieldIndex);
        return fieldOffset == 0
            ? 0
            : BinaryPrimitives.ReadUInt32LittleEndian(
                data.Slice(table.TableOffset + fieldOffset, sizeof(uint)));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = GetTableFieldOffset(data, table, fieldIndex);
        return fieldOffset == 0
            ? (ushort)0
            : BinaryPrimitives.ReadUInt16LittleEndian(
                data.Slice(table.TableOffset + fieldOffset, sizeof(ushort)));
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = GetTableFieldOffset(data, table, fieldIndex);
        return fieldOffset == 0 ? (byte)0 : data[table.TableOffset + fieldOffset];
    }

    private static sbyte ReadSByte(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        return unchecked((sbyte)ReadByte(data, table, fieldIndex));
    }

    private static bool ReadBool(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        return ReadByte(data, table, fieldIndex) != 0;
    }

    private static ushort GetTableFieldOffset(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var entryOffset = VTableHeaderSize + (fieldIndex * sizeof(ushort));
        if (entryOffset + sizeof(ushort) > table.VTableLength)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(table.VTableOffset + entryOffset, sizeof(ushort)));
    }

    private static ushort[] GetSourceFieldOffsets(ReadOnlySpan<byte> data, TableLayout table)
    {
        var offsets = new ushort[FieldCount];
        for (var fieldIndex = 0; fieldIndex < offsets.Length; fieldIndex++)
        {
            offsets[fieldIndex] = GetTableFieldOffset(data, table, fieldIndex);
        }

        return offsets;
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

    private static void WriteRecord(
        byte[] data,
        int tableOffset,
        IReadOnlyList<ushort> fieldOffsets,
        SwShMoveDataRecord record)
    {
        var values = GetFieldValues(record);
        if (!CanWriteInPlace(fieldOffsets, values))
        {
            throw new InvalidOperationException("Move record contains a value without a table field offset.");
        }

        WriteFieldValues(data, tableOffset, fieldOffsets, values);
    }

    private static long[] GetFieldValues(SwShMoveDataRecord record)
    {
        var values = new long[FieldCount];
        values[0] = record.Version;
        values[1] = record.MoveId;
        values[2] = record.CanUseMove ? 1 : 0;
        values[3] = record.Core.Type;
        values[4] = record.Core.Quality;
        values[5] = record.Core.Category;
        values[6] = record.Core.Power;
        values[7] = record.Core.Accuracy;
        values[8] = record.Core.PP;
        values[9] = record.Core.Priority;
        values[10] = record.Targeting.HitMax;
        values[11] = record.Targeting.HitMin;
        values[12] = record.Secondary.Inflict;
        values[13] = record.Secondary.InflictPercent;
        values[14] = record.Secondary.RawInflictCount;
        values[15] = record.Targeting.TurnMin;
        values[16] = record.Targeting.TurnMax;
        values[17] = record.Core.CritStage;
        values[18] = record.Secondary.Flinch;
        values[19] = record.Secondary.EffectSequence;
        values[20] = record.Secondary.Recoil;
        values[21] = record.Secondary.RawHealing;
        values[22] = record.Targeting.RawTarget;

        for (var slot = 1; slot <= 3; slot++)
        {
            var statChange = GetStatChange(record.StatChanges, slot);
            values[22 + slot] = statChange.Stat;
            values[25 + slot] = statChange.Stage;
            values[28 + slot] = statChange.Percent;
        }

        values[32] = record.Core.GigantamaxPower;
        values[33] = record.Flags.MakesContact ? 1 : 0;
        values[34] = record.Flags.Charge ? 1 : 0;
        values[35] = record.Flags.Recharge ? 1 : 0;
        values[36] = record.Flags.Protect ? 1 : 0;
        values[37] = record.Flags.Reflectable ? 1 : 0;
        values[38] = record.Flags.Snatch ? 1 : 0;
        values[39] = record.Flags.Mirror ? 1 : 0;
        values[40] = record.Flags.Punch ? 1 : 0;
        values[41] = record.Flags.Sound ? 1 : 0;
        values[42] = record.Flags.Gravity ? 1 : 0;
        values[43] = record.Flags.Defrost ? 1 : 0;
        values[44] = record.Flags.DistanceTriple ? 1 : 0;
        values[45] = record.Flags.Heal ? 1 : 0;
        values[46] = record.Flags.IgnoreSubstitute ? 1 : 0;
        values[47] = record.Flags.FailSkyBattle ? 1 : 0;
        values[48] = record.Flags.AnimateAlly ? 1 : 0;
        values[49] = record.Flags.Dance ? 1 : 0;
        values[50] = record.Flags.Metronome ? 1 : 0;
        return values;
    }

    private static bool CanWriteInPlace(IReadOnlyList<ushort> fieldOffsets, IReadOnlyList<long> values)
    {
        if (fieldOffsets.Count != FieldCount || values.Count != FieldCount)
        {
            return false;
        }

        for (var fieldIndex = 0; fieldIndex < FieldCount; fieldIndex++)
        {
            if (fieldOffsets[fieldIndex] == 0 && values[fieldIndex] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int FindActivatedSparseField(
        IReadOnlyList<ushort> fieldOffsets,
        IReadOnlyList<long> values)
    {
        for (var fieldIndex = 0; fieldIndex < FieldCount; fieldIndex++)
        {
            if (fieldOffsets[fieldIndex] == 0 && values[fieldIndex] != 0)
            {
                return fieldIndex;
            }
        }

        return -1;
    }

    private static bool CanRebuildWithoutDiscardingOpaqueData(
        ReadOnlySpan<byte> data,
        TableLayout table,
        IReadOnlyList<ushort> fieldOffsets)
    {
        if (table.VTableLength > KnownVTableSize)
        {
            return false;
        }

        var classifiedRanges = new List<DataRange>(FieldCount + 3)
        {
            new(RootOffset, sizeof(uint)),
            new(table.VTableOffset, table.VTableLength),
            new(table.TableOffset, TableVTableOffsetSize),
        };

        for (var fieldIndex = 0; fieldIndex < FieldCount; fieldIndex++)
        {
            var fieldOffset = fieldOffsets[fieldIndex];
            if (fieldOffset != 0)
            {
                classifiedRanges.Add(new DataRange(
                    table.TableOffset + fieldOffset,
                    FieldLayouts[fieldIndex].Size));
            }
        }

        classifiedRanges.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        var classifiedUntil = 0;
        foreach (var range in classifiedRanges)
        {
            if (range.Offset > classifiedUntil
                && !IsAllZero(data.Slice(classifiedUntil, range.Offset - classifiedUntil)))
            {
                return false;
            }

            classifiedUntil = Math.Max(classifiedUntil, range.Offset + range.Length);
        }

        return IsAllZero(data[classifiedUntil..]);
    }

    private static bool IsAllZero(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteFieldValues(
        byte[] data,
        int tableOffset,
        IReadOnlyList<ushort> fieldOffsets,
        IReadOnlyList<long> values)
    {
        for (var fieldIndex = 0; fieldIndex < FieldCount; fieldIndex++)
        {
            var fieldOffset = fieldOffsets[fieldIndex];
            if (fieldOffset == 0)
            {
                continue;
            }

            var absoluteOffset = tableOffset + fieldOffset;
            switch (FieldLayouts[fieldIndex].Size)
            {
                case sizeof(byte):
                    data[absoluteOffset] = unchecked((byte)values[fieldIndex]);
                    break;
                case sizeof(ushort):
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(absoluteOffset, sizeof(ushort)),
                        unchecked((ushort)values[fieldIndex]));
                    break;
                case sizeof(uint):
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        data.AsSpan(absoluteOffset, sizeof(uint)),
                        unchecked((uint)values[fieldIndex]));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported move field width {FieldLayouts[fieldIndex].Size}.");
            }
        }
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("Move FlatBuffer offset points outside the move data file.");
        }
    }

    private static bool RangesOverlap(int firstOffset, int firstLength, int secondOffset, int secondLength)
    {
        return (long)firstOffset < (long)secondOffset + secondLength
            && (long)secondOffset < (long)firstOffset + firstLength;
    }

    private sealed record FieldLayout(int Size, int Alignment);

    private readonly record struct FieldRange(int FieldIndex, int Offset, int Length);

    private readonly record struct DataRange(int Offset, int Length);

    private sealed record SourceState(byte[] Data, TableLayout Layout);

    private readonly record struct TableLayout(
        int TableOffset,
        int VTableOffset,
        ushort VTableLength,
        ushort ObjectSize);

    private static bool IsBooleanField(int fieldIndex)
    {
        return fieldIndex == 2 || fieldIndex is >= 33 and <= 50;
    }
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
