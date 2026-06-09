// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShStaticEncounterStats(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SwShStaticEncounterRecord(
    int Index,
    ulong BackgroundFarTypeId,
    ulong BackgroundNearTypeId,
    SwShStaticEncounterStats Evs,
    int Form,
    int DynamaxLevel,
    int Field0A,
    ulong EncounterId,
    int Field0C,
    bool CanGigantamax,
    int HeldItem,
    int Level,
    int EncounterScenario,
    int Species,
    int ShinyLock,
    int Nature,
    int Gender,
    SwShStaticEncounterStats Ivs,
    int Ability,
    IReadOnlyList<int> Moves);

public enum SwShStaticEncounterField
{
    Form,
    DynamaxLevel,
    CanGigantamax,
    HeldItem,
    Level,
    EncounterScenario,
    Species,
    ShinyLock,
    Nature,
    Gender,
    EvHp,
    EvAttack,
    EvDefense,
    EvSpeed,
    EvSpecialAttack,
    EvSpecialDefense,
    IvHp,
    IvAttack,
    IvDefense,
    IvSpeed,
    IvSpecialAttack,
    IvSpecialDefense,
    Ability,
    Move0,
    Move1,
    Move2,
    Move3,
    FlawlessIvCount,
}

public sealed record SwShStaticEncounterEdit(
    int EncounterIndex,
    SwShStaticEncounterField Field,
    int Value);

public sealed record SwShStaticEncounterArchive(IReadOnlyList<SwShStaticEncounterRecord> Encounters)
{
    public const int RandomIvValue = -1;
    public const int ThreePerfectIvSentinel = -4;
    public const int MinimumFixedIvValue = 0;
    public const int MaximumFixedIvValue = 31;
    public const int MaximumDynamaxLevel = 10;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumIdValue = int.MaxValue;

    public static SwShStaticEncounterArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Static encounter archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var tableVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var encounters = ReadTableVector(data, tableVectorOffset, ReadEncounter);

        return new SwShStaticEncounterArchive(encounters);
    }

    public byte[] Write()
    {
        var writer = new StaticFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShStaticEncounterEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var encounters = Encounters
            .Select(encounter => encounter with
            {
                Evs = encounter.Evs with { },
                Ivs = encounter.Ivs with { },
                Moves = encounter.Moves.ToArray(),
            })
            .ToArray();

        foreach (var edit in edits)
        {
            ApplyEdit(encounters, edit);
        }

        return new SwShStaticEncounterArchive(encounters).Write();
    }

    public static int? GetFlawlessIvCount(SwShStaticEncounterStats ivs)
    {
        if (ivs.HP == ThreePerfectIvSentinel)
        {
            return 3;
        }

        if (ivs.HP == MaximumFixedIvValue
            && ivs.Attack == MaximumFixedIvValue
            && ivs.Defense == MaximumFixedIvValue
            && ivs.Speed == MaximumFixedIvValue
            && ivs.SpecialAttack == MaximumFixedIvValue
            && ivs.SpecialDefense == MaximumFixedIvValue)
        {
            return 6;
        }

        if (ivs.HP == RandomIvValue
            && ivs.Attack == RandomIvValue
            && ivs.Defense == RandomIvValue
            && ivs.Speed == RandomIvValue
            && ivs.SpecialAttack == RandomIvValue
            && ivs.SpecialDefense == RandomIvValue)
        {
            return 0;
        }

        return null;
    }

    private static void ApplyEdit(IReadOnlyList<SwShStaticEncounterRecord> encounters, SwShStaticEncounterEdit edit)
    {
        if ((uint)edit.EncounterIndex >= (uint)encounters.Count)
        {
            throw new InvalidDataException($"Static encounter index {edit.EncounterIndex} is not present.");
        }

        if (encounters is not SwShStaticEncounterRecord[] mutableEncounters)
        {
            throw new InvalidDataException("Static encounter list is not mutable.");
        }

        var encounter = encounters[edit.EncounterIndex];
        mutableEncounters[edit.EncounterIndex] = edit.Field switch
        {
            SwShStaticEncounterField.Form => encounter with { Form = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShStaticEncounterField.DynamaxLevel => encounter with { DynamaxLevel = ValidateRange(edit.Value, 0, MaximumDynamaxLevel) },
            SwShStaticEncounterField.CanGigantamax => encounter with { CanGigantamax = ValidateBool(edit.Value) },
            SwShStaticEncounterField.HeldItem => encounter with { HeldItem = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShStaticEncounterField.Level => encounter with { Level = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShStaticEncounterField.EncounterScenario => encounter with { EncounterScenario = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShStaticEncounterField.Species => encounter with { Species = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShStaticEncounterField.ShinyLock => encounter with { ShinyLock = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShStaticEncounterField.Nature => encounter with { Nature = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShStaticEncounterField.Gender => encounter with { Gender = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShStaticEncounterField.EvHp => encounter with { Evs = encounter.Evs with { HP = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShStaticEncounterField.EvAttack => encounter with { Evs = encounter.Evs with { Attack = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShStaticEncounterField.EvDefense => encounter with { Evs = encounter.Evs with { Defense = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShStaticEncounterField.EvSpeed => encounter with { Evs = encounter.Evs with { Speed = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShStaticEncounterField.EvSpecialAttack => encounter with { Evs = encounter.Evs with { SpecialAttack = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShStaticEncounterField.EvSpecialDefense => encounter with { Evs = encounter.Evs with { SpecialDefense = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShStaticEncounterField.IvHp => encounter with { Ivs = encounter.Ivs with { HP = ValidateHpIvValue(edit.Value) } },
            SwShStaticEncounterField.IvAttack => encounter with { Ivs = encounter.Ivs with { Attack = ValidateIvValue(edit.Value) } },
            SwShStaticEncounterField.IvDefense => encounter with { Ivs = encounter.Ivs with { Defense = ValidateIvValue(edit.Value) } },
            SwShStaticEncounterField.IvSpeed => encounter with { Ivs = encounter.Ivs with { Speed = ValidateIvValue(edit.Value) } },
            SwShStaticEncounterField.IvSpecialAttack => encounter with { Ivs = encounter.Ivs with { SpecialAttack = ValidateIvValue(edit.Value) } },
            SwShStaticEncounterField.IvSpecialDefense => encounter with { Ivs = encounter.Ivs with { SpecialDefense = ValidateIvValue(edit.Value) } },
            SwShStaticEncounterField.Ability => encounter with { Ability = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShStaticEncounterField.Move0 => encounter with { Moves = SetMove(encounter.Moves, 0, edit.Value) },
            SwShStaticEncounterField.Move1 => encounter with { Moves = SetMove(encounter.Moves, 1, edit.Value) },
            SwShStaticEncounterField.Move2 => encounter with { Moves = SetMove(encounter.Moves, 2, edit.Value) },
            SwShStaticEncounterField.Move3 => encounter with { Moves = SetMove(encounter.Moves, 3, edit.Value) },
            SwShStaticEncounterField.FlawlessIvCount => encounter with { Ivs = CreateIvPreset(edit.Value) },
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Static encounter field '{edit.Field}' is not supported."),
        };
    }

    private static IReadOnlyList<int> SetMove(IReadOnlyList<int> moves, int slot, int value)
    {
        var updatedMoves = moves.ToArray();
        if ((uint)slot >= (uint)updatedMoves.Length)
        {
            throw new InvalidDataException($"Static encounter move slot {slot} is not present.");
        }

        updatedMoves[slot] = ValidateRange(value, 0, MaximumIdValue);

        return updatedMoves;
    }

    private static SwShStaticEncounterStats CreateIvPreset(int flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => new SwShStaticEncounterStats(RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue),
            3 => new SwShStaticEncounterStats(
                ThreePerfectIvSentinel,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue),
            6 => new SwShStaticEncounterStats(
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue),
            _ => throw new ArgumentOutOfRangeException(
                nameof(flawlessIvCount),
                "Static encounter flawless IV count must be 0, 3, or 6."),
        };
    }

    private static bool ValidateBool(int value)
    {
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Static encounter boolean values must be 0 or 1."),
        };
    }

    private static int ValidateIvValue(int value)
    {
        if (value == RandomIvValue || value is >= MinimumFixedIvValue and <= MaximumFixedIvValue)
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Static encounter IV value {value} is outside the supported range {RandomIvValue}, {MinimumFixedIvValue}-{MaximumFixedIvValue}.");
    }

    private static int ValidateHpIvValue(int value)
    {
        if (value == ThreePerfectIvSentinel)
        {
            return value;
        }

        return ValidateIvValue(value);
    }

    private static int ValidateRange(int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Static encounter value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
    }

    private static SwShStaticEncounterRecord ReadEncounter(ReadOnlySpan<byte> data, int tableOffset, int index)
    {
        return new SwShStaticEncounterRecord(
            index,
            ReadTableUInt64(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 1, required: false),
            new SwShStaticEncounterStats(
                ReadTableByte(data, tableOffset, fieldIndex: 5, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 3, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 4, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 6, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 7, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 2, required: false)),
            ReadTableByte(data, tableOffset, fieldIndex: 8, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 9, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 10, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 11, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 12, required: false),
            ReadTableBool(data, tableOffset, fieldIndex: 13, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 14, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 15, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 16, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 17, required: false),
            checked((int)ReadTableUInt32(data, tableOffset, fieldIndex: 18, required: false)),
            checked((int)ReadTableUInt32(data, tableOffset, fieldIndex: 19, required: false)),
            ReadTableSByte(data, tableOffset, fieldIndex: 20, required: false),
            new SwShStaticEncounterStats(
                ReadTableSByte(data, tableOffset, fieldIndex: 24, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 22, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 23, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 25, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 26, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 21, required: false)),
            ReadTableInt32(data, tableOffset, fieldIndex: 27, required: false),
            [
                ReadTableInt32(data, tableOffset, fieldIndex: 28, required: false),
                ReadTableInt32(data, tableOffset, fieldIndex: 29, required: false),
                ReadTableInt32(data, tableOffset, fieldIndex: 30, required: false),
                ReadTableInt32(data, tableOffset, fieldIndex: 31, required: false),
            ]);
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(uint));
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
        var targetOffset = checked(offset + (int)relativeOffset);
        EnsureRange(data, targetOffset, sizeof(int));

        return targetOffset;
    }

    private static int ReadTableUOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        return ReadUOffset(data, tableOffset + fieldOffset);
    }

    private static bool ReadTableBool(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return false;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(byte));

        return data[tableOffset + fieldOffset] != 0;
    }

    private static byte ReadTableByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(byte));

        return data[tableOffset + fieldOffset];
    }

    private static sbyte ReadTableSByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(sbyte));

        return unchecked((sbyte)data[tableOffset + fieldOffset]);
    }

    private static int ReadTableInt32(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(int));

        return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(int)));
    }

    private static uint ReadTableUInt32(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(uint));

        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(uint)));
    }

    private static ulong ReadTableUInt64(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(ulong));

        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(ulong)));
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        EnsureRange(data, tableOffset, sizeof(int));
        var vtableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        var vtableStart = tableOffset - vtableOffset;
        EnsureRange(data, vtableStart, sizeof(ushort) * 2);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableStart, sizeof(ushort)));
        var fieldEntryOffset = sizeof(ushort) * 2 + (fieldIndex * sizeof(ushort));
        if (fieldEntryOffset + sizeof(ushort) > vtableLength)
        {
            return 0;
        }

        EnsureRange(data, vtableStart + fieldEntryOffset, sizeof(ushort));

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableStart + fieldEntryOffset, sizeof(ushort)));
    }

    private static T[] ReadTableVector<T>(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        Func<ReadOnlySpan<byte>, int, int, T> readTable)
    {
        var count = ReadVectorLength(data, vectorOffset);
        var values = new T[count];

        for (var index = 0; index < count; index++)
        {
            var elementOffset = vectorOffset + sizeof(uint) + (index * sizeof(uint));
            values[index] = readTable(data, ReadUOffset(data, elementOffset), index);
        }

        return values;
    }

    private static int ReadVectorLength(ReadOnlySpan<byte> data, int vectorOffset)
    {
        EnsureRange(data, vectorOffset, sizeof(uint));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(vectorOffset, sizeof(uint)));
        if (count > int.MaxValue)
        {
            throw new InvalidDataException("FlatBuffer vector is too large.");
        }

        return (int)count;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new InvalidDataException("FlatBuffer archive contains an invalid offset.");
        }
    }

    private sealed class StaticFlatBufferWriter
    {
        private const int EncounterFieldCount = 32;
        private const int EncounterVtableLength = sizeof(ushort) * 2 + (EncounterFieldCount * sizeof(ushort));
        private readonly List<byte> bytes = [];

        public void Write(SwShStaticEncounterArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var encounterVector = WriteTableVector(archive.Encounters.Count);
            PatchUOffset(root.Field0Offset, encounterVector.VectorOffset);
            for (var index = 0; index < archive.Encounters.Count; index++)
            {
                var encounterOffset = WriteEncounter(archive.Encounters[index]);
                PatchUOffset(encounterVector.ElementOffsets[index], encounterOffset);
            }
        }

        public byte[] ToArray()
        {
            return bytes.ToArray();
        }

        private TableFields WriteArchiveTable()
        {
            AlignForTable(vtableLength: 6, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(6);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var encounterFieldOffset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, encounterFieldOffset);
        }

        private int WriteEncounter(SwShStaticEncounterRecord encounter)
        {
            AlignForTable(vtableLength: EncounterVtableLength, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(EncounterVtableLength);
            WriteUInt16(90);
            WriteEncounterFieldOffsets();

            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            WriteInt32(encounter.Field0A);
            WriteInt32(encounter.HeldItem);
            WriteInt32(encounter.EncounterScenario);
            WriteInt32(encounter.Species);
            WriteUInt32(checked((uint)encounter.ShinyLock));
            WriteUInt32(checked((uint)encounter.Nature));
            WriteInt32(encounter.Ability);
            WriteInt32(encounter.Moves[0]);
            WriteInt32(encounter.Moves[1]);
            WriteInt32(encounter.Moves[2]);
            WriteInt32(encounter.Moves[3]);
            WriteUInt64(encounter.BackgroundFarTypeId);
            WriteUInt64(encounter.BackgroundNearTypeId);
            WriteUInt64(encounter.EncounterId);
            WriteByte(checked((byte)encounter.Evs.Speed));
            WriteByte(checked((byte)encounter.Evs.Attack));
            WriteByte(checked((byte)encounter.Evs.Defense));
            WriteByte(checked((byte)encounter.Evs.HP));
            WriteByte(checked((byte)encounter.Evs.SpecialAttack));
            WriteByte(checked((byte)encounter.Evs.SpecialDefense));
            WriteByte(checked((byte)encounter.Form));
            WriteByte(checked((byte)encounter.DynamaxLevel));
            WriteByte(checked((byte)encounter.Field0C));
            WriteByte(encounter.CanGigantamax ? (byte)1 : (byte)0);
            WriteByte(checked((byte)encounter.Level));
            WriteSByte(checked((sbyte)encounter.Gender));
            WriteSByte(checked((sbyte)encounter.Ivs.Speed));
            WriteSByte(checked((sbyte)encounter.Ivs.Attack));
            WriteSByte(checked((sbyte)encounter.Ivs.Defense));
            WriteSByte(checked((sbyte)encounter.Ivs.HP));
            WriteSByte(checked((sbyte)encounter.Ivs.SpecialAttack));
            WriteSByte(checked((sbyte)encounter.Ivs.SpecialDefense));

            return tableOffset;
        }

        private void WriteEncounterFieldOffsets()
        {
            WriteUInt16(48); // BackgroundFarTypeID
            WriteUInt16(56); // BackgroundNearTypeID
            WriteUInt16(72); // EV_SPE
            WriteUInt16(73); // EV_ATK
            WriteUInt16(74); // EV_DEF
            WriteUInt16(75); // EV_HP
            WriteUInt16(76); // EV_SPA
            WriteUInt16(77); // EV_SPD
            WriteUInt16(78); // Form
            WriteUInt16(79); // DynamaxLevel
            WriteUInt16(4);  // Field_0A
            WriteUInt16(64); // EncounterID
            WriteUInt16(80); // Field_0C
            WriteUInt16(81); // CanGigantamax
            WriteUInt16(8);  // HeldItem
            WriteUInt16(82); // Level
            WriteUInt16(12); // EncounterScenario
            WriteUInt16(16); // Species
            WriteUInt16(20); // ShinyLock
            WriteUInt16(24); // Nature
            WriteUInt16(83); // Gender
            WriteUInt16(84); // IV_SPE
            WriteUInt16(85); // IV_ATK
            WriteUInt16(86); // IV_DEF
            WriteUInt16(87); // IV_HP
            WriteUInt16(88); // IV_SPA
            WriteUInt16(89); // IV_SPD
            WriteUInt16(28); // Ability
            WriteUInt16(32); // Move0
            WriteUInt16(36); // Move1
            WriteUInt16(40); // Move2
            WriteUInt16(44); // Move3
        }

        private VectorFields WriteTableVector(int count)
        {
            Align(4);
            var vectorOffset = Position;
            WriteUInt32(checked((uint)count));
            var elementOffsets = new int[count];
            for (var index = 0; index < count; index++)
            {
                elementOffsets[index] = Position;
                WriteUInt32(0);
            }

            return new VectorFields(vectorOffset, elementOffsets);
        }

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            if (targetOffset <= sourceOffset)
            {
                throw new InvalidOperationException("FlatBuffer target offsets must point forward.");
            }

            WriteUInt32At(sourceOffset, checked((uint)(targetOffset - sourceOffset)));
        }

        private void AlignForTable(int vtableLength, int alignment)
        {
            while (((Position + vtableLength) % alignment) != 0)
            {
                bytes.Add(0);
            }
        }

        private void Align(int alignment)
        {
            while ((Position % alignment) != 0)
            {
                bytes.Add(0);
            }
        }

        private int Position => bytes.Count;

        private void WriteByte(byte value)
        {
            bytes.Add(value);
        }

        private void WriteSByte(sbyte value)
        {
            bytes.Add(unchecked((byte)value));
        }

        private void WriteUInt16(ushort value)
        {
            var start = Grow(sizeof(ushort));
            BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(ushort)), value);
        }

        private void WriteInt32(int value)
        {
            var start = Grow(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(int)), value);
        }

        private void WriteUInt32(uint value)
        {
            var start = Grow(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(uint)), value);
        }

        private void WriteUInt64(ulong value)
        {
            var start = Grow(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(ulong)), value);
        }

        private void WriteUInt32At(int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(uint)), value);
        }

        private int Grow(int count)
        {
            var start = bytes.Count;
            for (var index = 0; index < count; index++)
            {
                bytes.Add(0);
            }

            return start;
        }

        private sealed record TableFields(int TableOffset, int Field0Offset);

        private sealed record VectorFields(
            int VectorOffset,
            IReadOnlyList<int> ElementOffsets);
    }
}
