// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShNestHoleReward(
    uint EntryId,
    uint ItemId,
    IReadOnlyList<uint> Values);

public sealed record SwShNestHoleRewardTable(
    ulong TableId,
    IReadOnlyList<SwShNestHoleReward> Rewards);

public enum SwShNestHoleRewardField
{
    ItemId,
    Star1Value,
    Star2Value,
    Star3Value,
    Star4Value,
    Star5Value,
}

public sealed record SwShNestHoleRewardEdit(
    int TableIndex,
    int RewardIndex,
    SwShNestHoleRewardField Field,
    uint Value);

public sealed record SwShNestHoleRewardArchive(IReadOnlyList<SwShNestHoleRewardTable> Tables)
{
    public const uint MaximumItemId = ushort.MaxValue;
    public const uint MaximumDropValue = 100;
    public const uint MaximumBonusQuantity = 999;

    public static SwShNestHoleRewardArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Nest hole reward archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var tableVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);

        return new SwShNestHoleRewardArchive(ReadTableVector(data, tableVectorOffset, ReadRewardTable));
    }

    public byte[] Write()
    {
        var writer = new RewardFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShNestHoleRewardEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var tables = Tables
            .Select(table => table with
            {
                Rewards = table.Rewards
                    .Select(reward => reward with { Values = reward.Values.ToArray() })
                    .ToArray(),
            })
            .ToArray();

        foreach (var edit in edits)
        {
            ApplyEdit(tables, edit);
        }

        return new SwShNestHoleRewardArchive(tables).Write();
    }

    private static void ApplyEdit(IReadOnlyList<SwShNestHoleRewardTable> tables, SwShNestHoleRewardEdit edit)
    {
        if ((uint)edit.TableIndex >= (uint)tables.Count)
        {
            throw new InvalidDataException($"Raid reward table index {edit.TableIndex} is not present.");
        }

        var table = tables[edit.TableIndex];
        if ((uint)edit.RewardIndex >= (uint)table.Rewards.Count)
        {
            throw new InvalidDataException($"Raid reward index {edit.RewardIndex} is not present.");
        }

        if (table.Rewards is not SwShNestHoleReward[] rewards)
        {
            throw new InvalidDataException("Raid reward list is not mutable.");
        }

        var reward = rewards[edit.RewardIndex];
        rewards[edit.RewardIndex] = edit.Field switch
        {
            SwShNestHoleRewardField.ItemId => reward with { ItemId = ValidateRange(edit.Value, 0, MaximumItemId) },
            SwShNestHoleRewardField.Star1Value => ReplaceValue(reward, valueIndex: 0, edit.Value),
            SwShNestHoleRewardField.Star2Value => ReplaceValue(reward, valueIndex: 1, edit.Value),
            SwShNestHoleRewardField.Star3Value => ReplaceValue(reward, valueIndex: 2, edit.Value),
            SwShNestHoleRewardField.Star4Value => ReplaceValue(reward, valueIndex: 3, edit.Value),
            SwShNestHoleRewardField.Star5Value => ReplaceValue(reward, valueIndex: 4, edit.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Raid reward field '{edit.Field}' is not supported."),
        };

        if (tables is SwShNestHoleRewardTable[] mutableTables)
        {
            mutableTables[edit.TableIndex] = table with { Rewards = rewards };
        }
    }

    private static SwShNestHoleReward ReplaceValue(SwShNestHoleReward reward, int valueIndex, uint value)
    {
        if (reward.Values is not uint[] values)
        {
            throw new InvalidDataException("Raid reward values list is not mutable.");
        }

        if ((uint)valueIndex >= (uint)values.Length)
        {
            throw new InvalidDataException($"Raid reward star value {valueIndex + 1} is not present.");
        }

        values[valueIndex] = value;

        return reward with { Values = values };
    }

    private static uint ValidateRange(uint value, uint minimum, uint maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Raid reward value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
    }

    private static SwShNestHoleRewardTable ReadRewardTable(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShNestHoleRewardTable(
            ReadTableUInt64(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableVector(data, ReadTableUOffset(data, tableOffset, fieldIndex: 1, required: true), ReadReward));
    }

    private static SwShNestHoleReward ReadReward(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShNestHoleReward(
            ReadTableUInt32(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableUInt32(data, tableOffset, fieldIndex: 1, required: false),
            ReadUIntVector(data, ReadTableUOffset(data, tableOffset, fieldIndex: 2, required: true)));
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
            throw new InvalidDataException("FlatBuffer offset points outside the nest hole reward archive.");
        }
    }

    private sealed class RewardFlatBufferWriter
    {
        private readonly List<byte> bytes = [];

        public void Write(SwShNestHoleRewardArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var tableVector = WriteTableVector(archive.Tables.Count);
            PatchUOffset(root.Field0Offset, tableVector.VectorOffset);
            for (var index = 0; index < archive.Tables.Count; index++)
            {
                var tableOffset = WriteRewardTable(archive.Tables[index]);
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

        private int WriteRewardTable(SwShNestHoleRewardTable table)
        {
            AlignForTable(vtableLength: 8, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(8);
            WriteUInt16(16);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var entriesFieldOffset = Position;
            WriteUInt32(0);
            WriteUInt64(table.TableId);

            var rewardVector = WriteTableVector(table.Rewards.Count);
            PatchUOffset(entriesFieldOffset, rewardVector.VectorOffset);
            for (var index = 0; index < table.Rewards.Count; index++)
            {
                var rewardOffset = WriteReward(table.Rewards[index]);
                PatchUOffset(rewardVector.ElementOffsets[index], rewardOffset);
            }

            return tableOffset;
        }

        private int WriteReward(SwShNestHoleReward reward)
        {
            AlignForTable(vtableLength: 10, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(10);
            WriteUInt16(16);
            WriteUInt16(8);
            WriteUInt16(12);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var valuesFieldOffset = Position;
            WriteUInt32(0);
            WriteUInt32(reward.EntryId);
            WriteUInt32(reward.ItemId);

            var valuesVector = WriteUIntVector(reward.Values);
            PatchUOffset(valuesFieldOffset, valuesVector);

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
