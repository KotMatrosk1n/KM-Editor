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
            var slot = offset / RecordSize;
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

            evolutions.Add(new SwShEvolutionRecord(slot, method, argument, species, form, level));
        }

        return new SwShEvolutionSet(evolutions);
    }

    public static byte[] Write(IReadOnlyList<SwShEvolutionRecord> evolutions)
    {
        ArgumentNullException.ThrowIfNull(evolutions);

        if (evolutions.Count > MaxEvolutionCount)
        {
            throw new InvalidDataException($"Evolution files support at most {MaxEvolutionCount} rows.");
        }

        var output = new byte[FileSize];
        var occupiedSlots = new HashSet<int>();
        foreach (var evolution in evolutions)
        {
            if ((uint)evolution.Slot >= MaxEvolutionCount)
            {
                throw new InvalidDataException(
                    $"Evolution slot {evolution.Slot} is outside the supported range 0-{MaxEvolutionCount - 1}.");
            }

            if (!occupiedSlots.Add(evolution.Slot))
            {
                throw new InvalidDataException($"Evolution slot {evolution.Slot} is duplicated.");
            }

            WriteRecord(evolution, output.AsSpan(evolution.Slot * RecordSize, RecordSize));
        }

        return output;
    }

    private static void WriteRecord(SwShEvolutionRecord evolution, Span<byte> destination)
    {
        if (destination.Length != RecordSize)
        {
            throw new InvalidDataException($"Evolution record length must be {RecordSize} bytes.");
        }

        if ((uint)evolution.Method > ushort.MaxValue
            || (uint)evolution.Argument > ushort.MaxValue
            || (uint)evolution.Species > ushort.MaxValue
            || (uint)evolution.Form > byte.MaxValue
            || (uint)evolution.Level > byte.MaxValue)
        {
            throw new InvalidDataException("Evolution row values must fit the Sword/Shield evolution record layout.");
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)evolution.Method));
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], checked((ushort)evolution.Argument));
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], checked((ushort)evolution.Species));
        destination[6] = checked((byte)evolution.Form);
        destination[7] = checked((byte)evolution.Level);
    }
}

public sealed record SwShEvolutionRecord(
    int Slot,
    int Method,
    int Argument,
    int Species,
    int Form,
    int Level);
