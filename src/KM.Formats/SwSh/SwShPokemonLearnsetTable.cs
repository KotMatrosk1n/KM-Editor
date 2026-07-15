// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShPokemonLearnsetTable(IReadOnlyList<SwShPokemonLearnsetRecord> Records)
{
    public const int RecordSize = 0x104;
    public const int MaxMovesPerRecord = RecordSize / 4;
    public const string LearnsetDataRelativePath = "romfs/bin/pml/waza_oboe/wazaoboe_total.bin";

    public static SwShPokemonLearnsetTable Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length % RecordSize != 0)
        {
            throw new InvalidDataException(
                $"Pokemon learnset table length must be a non-empty multiple of {RecordSize} bytes.");
        }

        var records = new SwShPokemonLearnsetRecord[data.Length / RecordSize];
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = ParseRecord(index, data.Slice(index * RecordSize, RecordSize));
        }

        return new SwShPokemonLearnsetTable(records);
    }

    private static SwShPokemonLearnsetRecord ParseRecord(int personalId, ReadOnlySpan<byte> data)
    {
        var entries = new List<SwShPokemonLearnsetMoveRecord>();
        for (var offset = 0; offset < RecordSize; offset += 4)
        {
            var moveId = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            var level = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]);

            if (moveId == ushort.MaxValue && level == ushort.MaxValue)
            {
                for (var tailOffset = offset; tailOffset < RecordSize; tailOffset += 4)
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(data[tailOffset..]) != uint.MaxValue)
                    {
                        throw new InvalidDataException(
                            $"Pokemon learnset record {personalId} contains data after its terminator.");
                    }
                }

                break;
            }

            if (moveId == ushort.MaxValue || (level & 0xFF00) == 0xFF00)
            {
                throw new InvalidDataException(
                    $"Pokemon learnset record {personalId} contains a partial terminator at slot {offset / 4}.");
            }

            entries.Add(new SwShPokemonLearnsetMoveRecord(offset / 4, moveId, level));
        }

        return new SwShPokemonLearnsetRecord(personalId, entries);
    }

    public static byte[] Write(
        IReadOnlyList<SwShPokemonLearnsetRecord> records,
        ReadOnlySpan<byte> originalData)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (originalData.Length == 0 || originalData.Length % RecordSize != 0)
        {
            throw new InvalidDataException(
                $"Pokemon learnset table length must be a non-empty multiple of {RecordSize} bytes.");
        }

        var expectedRecordCount = originalData.Length / RecordSize;
        if (records.Count != expectedRecordCount)
        {
            throw new InvalidDataException(
                $"Pokemon learnset table write expected {expectedRecordCount} records, but received {records.Count}.");
        }

        var output = originalData.ToArray();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            ArgumentNullException.ThrowIfNull(record);
            if (record.PersonalId != index)
            {
                throw new InvalidDataException(
                    $"Pokemon learnset record at physical index {index} has PersonalId {record.PersonalId}.");
            }

            ValidateRecord(record);
            var sourceRecord = ParseRecord(
                index,
                originalData.Slice(index * RecordSize, RecordSize));
            if (RecordsAreSemanticallyEqual(record, sourceRecord))
            {
                continue;
            }

            WriteRecord(record, output.AsSpan(index * RecordSize, RecordSize));
        }

        return output;
    }

    public static void WriteRecord(
        SwShPokemonLearnsetRecord record,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (destination.Length != RecordSize)
        {
            throw new InvalidDataException(
                $"Pokemon learnset record length must be {RecordSize} bytes.");
        }

        ValidateRecord(record);

        destination.Fill(byte.MaxValue);
        for (var index = 0; index < record.Moves.Count; index++)
        {
            var move = record.Moves[index];
            var offset = index * 4;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], checked((ushort)move.MoveId));
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(offset + 2)..], checked((ushort)move.Level));
        }
    }

    private static void ValidateRecord(SwShPokemonLearnsetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record.Moves);

        if (record.Moves.Count > MaxMovesPerRecord)
        {
            throw new InvalidDataException(
                $"Pokemon learnset records support at most {MaxMovesPerRecord} moves.");
        }

        for (var index = 0; index < record.Moves.Count; index++)
        {
            var move = record.Moves[index];
            ArgumentNullException.ThrowIfNull(move);
            if (move.Slot != index)
            {
                throw new InvalidDataException(
                    $"Pokemon learnset move at list index {index} has slot {move.Slot}; slots must be contiguous from zero.");
            }

            if ((uint)move.MoveId > ushort.MaxValue)
            {
                throw new InvalidDataException("Pokemon learnset move IDs must fit in an unsigned 16-bit value.");
            }

            if ((uint)move.Level > ushort.MaxValue)
            {
                throw new InvalidDataException("Pokemon learnset levels must fit in an unsigned 16-bit value.");
            }

            if (move.MoveId == ushort.MaxValue || (move.Level & 0xFF00) == 0xFF00)
            {
                throw new InvalidDataException(
                    $"Pokemon learnset move at slot {index} conflicts with the format terminator.");
            }
        }
    }

    private static bool RecordsAreSemanticallyEqual(
        SwShPokemonLearnsetRecord left,
        SwShPokemonLearnsetRecord right)
    {
        if (left.PersonalId != right.PersonalId || left.Moves.Count != right.Moves.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Moves.Count; index++)
        {
            if (left.Moves[index] != right.Moves[index])
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record SwShPokemonLearnsetRecord(
    int PersonalId,
    IReadOnlyList<SwShPokemonLearnsetMoveRecord> Moves);

public sealed record SwShPokemonLearnsetMoveRecord(int Slot, int MoveId, int Level);
