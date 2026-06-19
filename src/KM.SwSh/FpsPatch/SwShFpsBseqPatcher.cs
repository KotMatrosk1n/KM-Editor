// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.SwSh.FpsPatch;

internal sealed record SwShFpsBseqConversionStats(
    int CommandCount,
    int FieldsChanged);

internal static class SwShFpsBseqPatcher
{
    public const double MoveEffectTimelineScale = 2.25d;
    public const double OpeningDemoTimelineScale = 2.0d;
    public const double DynamaxBallTimelineScale = 2.0d;

    private const uint CommandTerminator = 0xFFFFFFFF;
    private const int OpeningDemoNooneLifetimeCommandIndex = 21;
    private const uint OpeningDemoNooneScaledEndFrame = 346;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SESD");

    public static byte[] Convert(byte[] source, double scale, out SwShFpsBseqConversionStats stats)
    {
        ArgumentNullException.ThrowIfNull(source);
        var data = source.ToArray();
        var layout = ReadLayout(data);

        WriteU32(data, 0x0C, ScaleU32(layout.FrameCount, scale));
        var fieldsChanged = 1;
        foreach (var command in layout.Commands)
        {
            if (command.StartFrame != 0)
            {
                WriteU32(data, command.Offset, ScaleU32(command.StartFrame, scale));
                fieldsChanged++;
            }

            if (command.EndFrame != 0)
            {
                WriteU32(data, command.Offset + sizeof(uint), ScaleU32(command.EndFrame, scale));
                fieldsChanged++;
            }
        }

        stats = new SwShFpsBseqConversionStats(layout.Commands.Count, fieldsChanged);
        return data;
    }

    public static byte[] ConvertOpeningDemoD010(byte[] source, out SwShFpsBseqConversionStats stats)
    {
        var data = Convert(source, OpeningDemoTimelineScale, out stats);
        var layout = ReadLayout(data);
        if (layout.Commands.Count <= OpeningDemoNooneLifetimeCommandIndex)
        {
            throw new InvalidDataException("Opening demo BSEQ does not contain the expected noone startup command.");
        }

        var nooneLifetimeCommand = layout.Commands[OpeningDemoNooneLifetimeCommandIndex];
        if (nooneLifetimeCommand.StartFrame != 0 || nooneLifetimeCommand.EndFrame != OpeningDemoNooneScaledEndFrame)
        {
            throw new InvalidDataException("Opening demo BSEQ noone startup command did not match the expected 60FPS layout.");
        }

        WriteU32(data, nooneLifetimeCommand.Offset + sizeof(uint), 0);
        stats = stats with { FieldsChanged = stats.FieldsChanged + 1 };
        return data;
    }

    private static BseqLayout ReadLayout(byte[] data)
    {
        if (data.Length < 0x18 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Invalid BSEQ SESD header.");
        }

        var frameCount = ReadU32(data, 0x0C);
        var groupOptionCount = ReadU32(data, 0x10);
        var hashCount = ReadU32(data, 0x14);
        var hashTableEnd = 0x18L + (hashCount * 12L);
        if (hashTableEnd > data.Length)
        {
            throw new InvalidDataException("Invalid BSEQ hash table bounds.");
        }

        var sizes = new Dictionary<ulong, int>();
        for (var index = 0; index < hashCount; index++)
        {
            var entryOffset = 0x18 + (index * 12);
            var hash = ReadU64(data, entryOffset);
            var size = ReadU32(data, entryOffset + 8);
            if (size > int.MaxValue)
            {
                throw new InvalidDataException("BSEQ command payload is too large.");
            }

            sizes[hash] = (int)size;
        }

        var commands = new List<BseqCommand>();
        var offset = (int)hashTableEnd;
        var groupOptionBytes = checked((long)groupOptionCount * 12L);
        while (offset + 12L + groupOptionBytes + 8L <= data.Length)
        {
            var startFrame = ReadU32(data, offset);
            if (startFrame == CommandTerminator)
            {
                break;
            }

            var endFrame = ReadU32(data, offset + sizeof(uint));
            var hashOffset = offset + 12 + (int)groupOptionBytes;
            var hash = ReadU64(data, hashOffset);
            if (!sizes.TryGetValue(hash, out var payloadSize))
            {
                throw new InvalidDataException(
                    string.Create(CultureInfo.InvariantCulture, $"Unknown BSEQ command hash 0x{hash:X16}."));
            }

            var nextOffset = hashOffset + sizeof(ulong) + payloadSize;
            if (nextOffset > data.Length)
            {
                throw new InvalidDataException("BSEQ command payload exceeds file length.");
            }

            commands.Add(new BseqCommand(offset, startFrame, endFrame, hash, payloadSize));
            offset = nextOffset;
        }

        return new BseqLayout(frameCount, commands);
    }

    private static uint ScaleU32(uint value, double scale)
    {
        if (scale <= 0)
        {
            throw new InvalidDataException("BSEQ timeline scale must be positive.");
        }

        if (value > uint.MaxValue / scale)
        {
            throw new InvalidDataException(
                string.Create(CultureInfo.InvariantCulture, $"BSEQ frame field overflow while scaling {value} by {scale}."));
        }

        return (uint)Math.Round(value * scale, MidpointRounding.AwayFromZero);
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..(offset + sizeof(uint))]);
    }

    private static ulong ReadU64(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(data[offset..(offset + sizeof(ulong))]);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private sealed record BseqLayout(
        uint FrameCount,
        IReadOnlyList<BseqCommand> Commands);

    private sealed record BseqCommand(
        int Offset,
        uint StartFrame,
        uint EndFrame,
        ulong Hash,
        int Size);
}
