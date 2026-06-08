// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShPokemonLearnsetTable(IReadOnlyList<SwShPokemonLearnsetRecord> Records)
{
    public const int RecordSize = 0x104;
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
                break;
            }

            entries.Add(new SwShPokemonLearnsetMoveRecord(moveId, level));
        }

        return new SwShPokemonLearnsetRecord(personalId, entries);
    }
}

public sealed record SwShPokemonLearnsetRecord(
    int PersonalId,
    IReadOnlyList<SwShPokemonLearnsetMoveRecord> Moves);

public sealed record SwShPokemonLearnsetMoveRecord(int MoveId, int Level);
