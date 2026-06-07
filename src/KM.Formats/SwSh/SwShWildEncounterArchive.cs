// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShWildEncounterSlot(
    byte Probability,
    int Species,
    byte Form);

public sealed record SwShWildEncounterSubTable(
    byte LevelMin,
    byte LevelMax,
    IReadOnlyList<SwShWildEncounterSlot> Slots);

public sealed record SwShWildEncounterTable(
    ulong ZoneId,
    IReadOnlyList<SwShWildEncounterSubTable> SubTables);

public enum SwShWildEncounterField
{
    SpeciesId,
    Form,
    Probability,
    LevelMin,
    LevelMax,
}

public sealed record SwShWildEncounterEdit(
    int TableIndex,
    int SubTableIndex,
    int? SlotIndex,
    SwShWildEncounterField Field,
    int Value);

public sealed record SwShWildEncounterArchive(
    uint Field00,
    IReadOnlyList<SwShWildEncounterTable> Tables)
{
    public const int MaximumSpeciesId = int.MaxValue;
    public const int MinimumLevel = 0;
    public const int MaximumLevel = byte.MaxValue;
    public const int MinimumForm = 0;
    public const int MaximumForm = byte.MaxValue;
    public const int MinimumProbability = 0;
    public const int MaximumProbability = byte.MaxValue;

    public static SwShWildEncounterArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Encounter archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var field00 = ReadTableUInt32(data, rootTableOffset, fieldIndex: 0, required: false);
        var tablesVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 1, required: true);

        return new SwShWildEncounterArchive(
            field00,
            ReadTableVector(data, tablesVectorOffset, ReadEncounterTable));
    }

    public byte[] Write()
    {
        var writer = new EncounterFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShWildEncounterEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var tables = Tables
            .Select(table => table with
            {
                SubTables = table.SubTables
                    .Select(subTable => subTable with
                    {
                        Slots = subTable.Slots.ToArray(),
                    })
                    .ToArray(),
            })
            .ToArray();

        foreach (var edit in edits)
        {
            ApplyEdit(tables, edit);
        }

        return new SwShWildEncounterArchive(Field00, tables).Write();
    }

    private static void ApplyEdit(IReadOnlyList<SwShWildEncounterTable> tables, SwShWildEncounterEdit edit)
    {
        if ((uint)edit.TableIndex >= (uint)tables.Count)
        {
            throw new InvalidDataException($"Encounter table index {edit.TableIndex} is not present.");
        }

        var table = tables[edit.TableIndex];
        if ((uint)edit.SubTableIndex >= (uint)table.SubTables.Count)
        {
            throw new InvalidDataException($"Encounter subtable index {edit.SubTableIndex} is not present.");
        }

        if (table.SubTables is not SwShWildEncounterSubTable[] subTables)
        {
            throw new InvalidDataException("Encounter subtable list is not mutable.");
        }

        var subTable = table.SubTables[edit.SubTableIndex];
        switch (edit.Field)
        {
            case SwShWildEncounterField.LevelMin:
                ValidateRange(edit.Value, MinimumLevel, MaximumLevel, nameof(edit));
                subTables[edit.SubTableIndex] = subTable with { LevelMin = checked((byte)edit.Value) };
                break;
            case SwShWildEncounterField.LevelMax:
                ValidateRange(edit.Value, MinimumLevel, MaximumLevel, nameof(edit));
                subTables[edit.SubTableIndex] = subTable with { LevelMax = checked((byte)edit.Value) };
                break;
            case SwShWildEncounterField.SpeciesId:
            case SwShWildEncounterField.Form:
            case SwShWildEncounterField.Probability:
                ApplySlotEdit(subTables, edit.SubTableIndex, subTable, edit);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), $"Encounter field '{edit.Field}' is not supported.");
        }
    }

    private static void ApplySlotEdit(
        IReadOnlyList<SwShWildEncounterSubTable> mutableSubTables,
        int subTableIndex,
        SwShWildEncounterSubTable subTable,
        SwShWildEncounterEdit edit)
    {
        if (edit.SlotIndex is null || (uint)edit.SlotIndex.Value >= (uint)subTable.Slots.Count)
        {
            throw new InvalidDataException("Encounter slot edit targets a slot that is not present.");
        }

        if (subTable.Slots is not SwShWildEncounterSlot[] slots)
        {
            throw new InvalidDataException("Encounter slot list is not mutable.");
        }

        var slot = slots[edit.SlotIndex.Value];
        slots[edit.SlotIndex.Value] = edit.Field switch
        {
            SwShWildEncounterField.SpeciesId => slot with
            {
                Species = ValidateRange(edit.Value, 0, MaximumSpeciesId, nameof(edit)),
            },
            SwShWildEncounterField.Form => slot with
            {
                Form = checked((byte)ValidateRange(edit.Value, MinimumForm, MaximumForm, nameof(edit))),
            },
            SwShWildEncounterField.Probability => slot with
            {
                Probability = checked((byte)ValidateRange(edit.Value, MinimumProbability, MaximumProbability, nameof(edit))),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Encounter slot field '{edit.Field}' is not supported."),
        };

        if (mutableSubTables is SwShWildEncounterSubTable[] subTables)
        {
            subTables[subTableIndex] = subTable with { Slots = slots };
        }
    }

    private static int ValidateRange(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Encounter value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
    }

    private static SwShWildEncounterTable ReadEncounterTable(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShWildEncounterTable(
            ReadTableUInt64(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableVector(data, ReadTableUOffset(data, tableOffset, fieldIndex: 1, required: true), ReadSubTable));
    }

    private static SwShWildEncounterSubTable ReadSubTable(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShWildEncounterSubTable(
            ReadTableByte(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 1, required: false),
            ReadTableVector(data, ReadTableUOffset(data, tableOffset, fieldIndex: 2, required: true), ReadSlot));
    }

    private static SwShWildEncounterSlot ReadSlot(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShWildEncounterSlot(
            ReadTableByte(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 1, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 2, required: false));
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
            throw new InvalidDataException("FlatBuffer offset points outside the encounter archive.");
        }
    }

    private sealed class EncounterFlatBufferWriter
    {
        private readonly List<byte> bytes = [];

        public void Write(SwShWildEncounterArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable(archive.Field00);
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var tableVector = WriteTableVector(archive.Tables.Count);
            PatchUOffset(root.Field1Offset, tableVector.VectorOffset);
            for (var index = 0; index < archive.Tables.Count; index++)
            {
                var tableOffset = WriteEncounterTable(archive.Tables[index]);
                PatchUOffset(tableVector.ElementOffsets[index], tableOffset);
            }
        }

        public byte[] ToArray()
        {
            return bytes.ToArray();
        }

        private TableFields WriteArchiveTable(uint field00)
        {
            AlignForTable(vtableLength: 8, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(8);
            WriteUInt16(12);
            WriteUInt16(4);
            WriteUInt16(8);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            WriteUInt32(field00);
            var tablesFieldOffset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, Field0Offset: -1, tablesFieldOffset);
        }

        private int WriteEncounterTable(SwShWildEncounterTable table)
        {
            AlignForTable(vtableLength: 8, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(8);
            WriteUInt16(16);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var subTablesFieldOffset = Position;
            WriteUInt32(0);
            WriteUInt64(table.ZoneId);

            var subTableVector = WriteTableVector(table.SubTables.Count);
            PatchUOffset(subTablesFieldOffset, subTableVector.VectorOffset);
            for (var index = 0; index < table.SubTables.Count; index++)
            {
                var subTableOffset = WriteSubTable(table.SubTables[index]);
                PatchUOffset(subTableVector.ElementOffsets[index], subTableOffset);
            }

            return tableOffset;
        }

        private int WriteSubTable(SwShWildEncounterSubTable subTable)
        {
            AlignForTable(vtableLength: 10, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(10);
            WriteUInt16(10);
            WriteUInt16(8);
            WriteUInt16(9);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var slotsFieldOffset = Position;
            WriteUInt32(0);
            WriteByte(subTable.LevelMin);
            WriteByte(subTable.LevelMax);

            var slotVector = WriteTableVector(subTable.Slots.Count);
            PatchUOffset(slotsFieldOffset, slotVector.VectorOffset);
            for (var index = 0; index < subTable.Slots.Count; index++)
            {
                var slotOffset = WriteSlot(subTable.Slots[index]);
                PatchUOffset(slotVector.ElementOffsets[index], slotOffset);
            }

            return tableOffset;
        }

        private int WriteSlot(SwShWildEncounterSlot slot)
        {
            AlignForTable(vtableLength: 10, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(10);
            WriteUInt16(10);
            WriteUInt16(8);
            WriteUInt16(4);
            WriteUInt16(9);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            WriteInt32(slot.Species);
            WriteByte(slot.Probability);
            WriteByte(slot.Form);

            return tableOffset;
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

        private sealed record TableFields(
            int TableOffset,
            int Field0Offset,
            int Field1Offset);

        private sealed record VectorFields(
            int VectorOffset,
            IReadOnlyList<int> ElementOffsets);
    }
}
