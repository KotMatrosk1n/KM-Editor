// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShEncounterNest(
    int EntryIndex,
    int Species,
    int Form,
    ulong LevelTableId,
    int Ability,
    bool IsGigantamax,
    ulong DropTableId,
    ulong BonusTableId,
    IReadOnlyList<uint> Probabilities,
    int Gender,
    int FlawlessIvs);

public sealed record SwShEncounterNestTable(
    ulong TableId,
    int GameVersion,
    IReadOnlyList<SwShEncounterNest> Entries);

public enum SwShEncounterNestField
{
    Species,
    Form,
    Ability,
    IsGigantamax,
    Star1Probability,
    Star2Probability,
    Star3Probability,
    Star4Probability,
    Star5Probability,
    Gender,
    FlawlessIvs,
}

public sealed record SwShEncounterNestEdit(
    int TableIndex,
    int EntryIndex,
    SwShEncounterNestField Field,
    int Value);

public sealed record SwShEncounterNestArchive(IReadOnlyList<SwShEncounterNestTable> Tables)
{
    public const int MaximumSpeciesId = ushort.MaxValue;
    public const int MaximumForm = byte.MaxValue;
    public const int MaximumAbility = 4;
    public const int MaximumGender = 3;
    public const int MaximumFlawlessIvs = 6;
    public const int MaximumProbability = 100;

    public static SwShEncounterNestArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Encounter nest archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var tableVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);

        return new SwShEncounterNestArchive(ReadTableVector(data, tableVectorOffset, ReadNestTable));
    }

    public byte[] Write()
    {
        var writer = new EncounterNestFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShEncounterNestEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var tables = Tables
            .Select(table => table with
            {
                Entries = table.Entries
                    .Select(entry => entry with { Probabilities = entry.Probabilities.ToArray() })
                    .ToArray(),
            })
            .ToArray();

        foreach (var edit in edits)
        {
            ApplyEdit(tables, edit);
        }

        return new SwShEncounterNestArchive(tables).Write();
    }

    private static void ApplyEdit(IReadOnlyList<SwShEncounterNestTable> tables, SwShEncounterNestEdit edit)
    {
        if ((uint)edit.TableIndex >= (uint)tables.Count)
        {
            throw new InvalidDataException($"Raid battle table index {edit.TableIndex} is not present.");
        }

        var table = tables[edit.TableIndex];
        if ((uint)edit.EntryIndex >= (uint)table.Entries.Count)
        {
            throw new InvalidDataException($"Raid battle entry index {edit.EntryIndex} is not present.");
        }

        if (table.Entries is not SwShEncounterNest[] entries)
        {
            throw new InvalidDataException("Raid battle entry list is not mutable.");
        }

        var entry = entries[edit.EntryIndex];
        entries[edit.EntryIndex] = edit.Field switch
        {
            SwShEncounterNestField.Species => entry with { Species = ValidateRange(edit.Value, 0, MaximumSpeciesId) },
            SwShEncounterNestField.Form => entry with { Form = ValidateRange(edit.Value, 0, MaximumForm) },
            SwShEncounterNestField.Ability => entry with { Ability = ValidateRange(edit.Value, 0, MaximumAbility) },
            SwShEncounterNestField.IsGigantamax => entry with { IsGigantamax = ValidateBoolean(edit.Value) },
            SwShEncounterNestField.Star1Probability => ReplaceProbability(entry, probabilityIndex: 0, edit.Value),
            SwShEncounterNestField.Star2Probability => ReplaceProbability(entry, probabilityIndex: 1, edit.Value),
            SwShEncounterNestField.Star3Probability => ReplaceProbability(entry, probabilityIndex: 2, edit.Value),
            SwShEncounterNestField.Star4Probability => ReplaceProbability(entry, probabilityIndex: 3, edit.Value),
            SwShEncounterNestField.Star5Probability => ReplaceProbability(entry, probabilityIndex: 4, edit.Value),
            SwShEncounterNestField.Gender => entry with { Gender = ValidateRange(edit.Value, 0, MaximumGender) },
            SwShEncounterNestField.FlawlessIvs => entry with { FlawlessIvs = ValidateRange(edit.Value, 0, MaximumFlawlessIvs) },
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Raid battle field '{edit.Field}' is not supported."),
        };

        if (tables is SwShEncounterNestTable[] mutableTables)
        {
            mutableTables[edit.TableIndex] = table with { Entries = entries };
        }
    }

    private static SwShEncounterNest ReplaceProbability(
        SwShEncounterNest entry,
        int probabilityIndex,
        int value)
    {
        if (entry.Probabilities is not uint[] probabilities)
        {
            throw new InvalidDataException("Raid battle probability list is not mutable.");
        }

        if ((uint)probabilityIndex >= (uint)probabilities.Length)
        {
            throw new InvalidDataException($"Raid battle probability slot {probabilityIndex + 1} is not present.");
        }

        probabilities[probabilityIndex] = checked((uint)ValidateRange(value, 0, MaximumProbability));

        return entry with { Probabilities = probabilities };
    }

    private static bool ValidateBoolean(int value)
    {
        return ValidateRange(value, 0, 1) != 0;
    }

    private static int ValidateRange(int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Raid battle value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
    }

    private static SwShEncounterNestTable ReadNestTable(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShEncounterNestTable(
            ReadTableUInt64(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 1, required: false),
            ReadTableVector(data, ReadTableUOffset(data, tableOffset, fieldIndex: 2, required: true), ReadNestEntry));
    }

    private static SwShEncounterNest ReadNestEntry(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShEncounterNest(
            ReadTableInt32(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 1, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 2, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 3, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 4, required: false),
            ReadTableBool(data, tableOffset, fieldIndex: 5, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 6, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 7, required: false),
            ReadUIntVector(data, ReadTableUOffset(data, tableOffset, fieldIndex: 8, required: true)),
            ReadTableSByte(data, tableOffset, fieldIndex: 9, required: false),
            ReadTableSByte(data, tableOffset, fieldIndex: 10, required: false));
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

    private static int ReadTableByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
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

    private static int ReadTableSByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var value = ReadTableByte(data, tableOffset, fieldIndex, required);
        return unchecked((sbyte)(byte)value);
    }

    private static bool ReadTableBool(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        return ReadTableByte(data, tableOffset, fieldIndex, required) != 0;
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
        Func<ReadOnlySpan<byte>, int, T> readTable)
    {
        var count = ReadVectorLength(data, vectorOffset);
        var values = new T[count];

        for (var index = 0; index < count; index++)
        {
            var elementOffset = vectorOffset + sizeof(uint) + (index * sizeof(uint));
            values[index] = readTable(data, ReadUOffset(data, elementOffset));
        }

        return values;
    }

    private static uint[] ReadUIntVector(ReadOnlySpan<byte> data, int vectorOffset)
    {
        var count = ReadVectorLength(data, vectorOffset);
        EnsureRange(data, vectorOffset + sizeof(uint), checked(count * sizeof(uint)));
        var values = new uint[count];

        for (var index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                data.Slice(vectorOffset + sizeof(uint) + (index * sizeof(uint)), sizeof(uint)));
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

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("FlatBuffer offset points outside the encounter nest archive.");
        }
    }

    private sealed class EncounterNestFlatBufferWriter
    {
        private readonly List<byte> bytes = [];

        public void Write(SwShEncounterNestArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var tableVector = WriteTableVector(archive.Tables.Count);
            PatchUOffset(root.Field0Offset, tableVector.VectorOffset);
            for (var index = 0; index < archive.Tables.Count; index++)
            {
                var tableOffset = WriteNestTable(archive.Tables[index]);
                PatchUOffset(tableVector.ElementOffsets[index], tableOffset);
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
            var tableFieldOffset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, tableFieldOffset);
        }

        private int WriteNestTable(SwShEncounterNestTable table)
        {
            AlignForTable(vtableLength: 10, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(10);
            WriteUInt16(24);
            WriteUInt16(16);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var entriesFieldOffset = Position;
            WriteUInt32(0);
            WriteInt32(table.GameVersion);
            WriteUInt32(0);
            WriteUInt64(table.TableId);

            var entriesVector = WriteTableVector(table.Entries.Count);
            PatchUOffset(entriesFieldOffset, entriesVector.VectorOffset);
            for (var index = 0; index < table.Entries.Count; index++)
            {
                var entryOffset = WriteNestEntry(table.Entries[index]);
                PatchUOffset(entriesVector.ElementOffsets[index], entryOffset);
            }

            return tableOffset;
        }

        private int WriteNestEntry(SwShEncounterNest entry)
        {
            AlignForTable(vtableLength: 26, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(26);
            WriteUInt16(48);
            WriteUInt16(8);
            WriteUInt16(12);
            WriteUInt16(16);
            WriteUInt16(24);
            WriteUInt16(20);
            WriteUInt16(21);
            WriteUInt16(32);
            WriteUInt16(40);
            WriteUInt16(4);
            WriteUInt16(22);
            WriteUInt16(23);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var probabilitiesFieldOffset = Position;
            WriteUInt32(0);
            WriteInt32(entry.EntryIndex);
            WriteInt32(entry.Species);
            WriteInt32(entry.Form);
            WriteByte(checked((byte)entry.Ability));
            WriteByte(entry.IsGigantamax ? (byte)1 : (byte)0);
            WriteByte(unchecked((byte)(sbyte)entry.Gender));
            WriteByte(unchecked((byte)(sbyte)entry.FlawlessIvs));
            WriteUInt64(entry.LevelTableId);
            WriteUInt64(entry.DropTableId);
            WriteUInt64(entry.BonusTableId);

            var probabilitiesVector = WriteUIntVector(entry.Probabilities);
            PatchUOffset(probabilitiesFieldOffset, probabilitiesVector);

            return tableOffset;
        }

        private int WriteUIntVector(IReadOnlyList<uint> values)
        {
            Align(4);
            var vectorOffset = Position;
            WriteUInt32(checked((uint)values.Count));
            foreach (var value in values)
            {
                WriteUInt32(value);
            }

            return vectorOffset;
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
