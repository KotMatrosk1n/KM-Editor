// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShEvolutionSet(IReadOnlyList<SwShEvolutionRecord> Evolutions)
{
    public const int RecordSize = 8;
    public const int MaxEvolutionCount = 9;
    public const int FileSize = RecordSize * MaxEvolutionCount;
    public const string EvolutionDataRelativeDirectory = "romfs/bin/pml/evolution";

    public static SwShEvolutionSet Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != FileSize)
        {
            throw new InvalidDataException($"Evolution file length must be {FileSize} bytes.");
        }

        var evolutions = new List<SwShEvolutionRecord>();
        for (var offset = 0; offset < FileSize; offset += RecordSize)
        {
            var entry = data.Slice(offset, RecordSize);
            var method = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var argument = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            var species = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
            var form = entry[6];
            var level = entry[7];

            if (method == 0 && argument == 0 && species == 0 && form == 0 && level == 0)
            {
                continue;
            }

            evolutions.Add(new SwShEvolutionRecord(method, argument, species, form, level));
        }

        return new SwShEvolutionSet(evolutions);
    }
}

public sealed record SwShEvolutionRecord(
    int Method,
    int Argument,
    int Species,
    int Form,
    int Level);
