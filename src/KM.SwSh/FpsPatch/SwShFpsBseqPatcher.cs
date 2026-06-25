// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.FpsPatch;

internal sealed record SwShFpsBseqConversionStats(
    int CommandCount,
    int FieldsChanged);

internal static class SwShFpsBseqPatcher
{
    public const double MoveEffectTimelineScale = 2.25d;
    public const double OpeningDemoTimelineScale = 2.0d;
    public const double DynamaxBallTimelineScale = 2.0d;

    private const int OpeningDemoNooneLifetimeCommandIndex = 21;
    private const uint OpeningDemoNooneScaledEndFrame = 346;

    public static byte[] Convert(byte[] source, double scale, out SwShFpsBseqConversionStats stats)
    {
        ArgumentNullException.ThrowIfNull(source);
        var data = source.ToArray();
        var file = SwShBseqFile.Parse(data);

        WriteU32(data, SwShBseqFile.FrameCountOffset, ScaleU32(file.FrameCount, scale));
        var fieldsChanged = 1;
        foreach (var command in file.Commands)
        {
            if (command.StartFrame != 0)
            {
                WriteU32(data, command.StartFrameOffset, ScaleU32(command.StartFrame, scale));
                fieldsChanged++;
            }

            if (command.EndFrame != 0)
            {
                WriteU32(data, command.EndFrameOffset, ScaleU32(command.EndFrame, scale));
                fieldsChanged++;
            }
        }

        stats = new SwShFpsBseqConversionStats(file.Commands.Count, fieldsChanged);
        return data;
    }

    public static byte[] ConvertOpeningDemoD010(byte[] source, out SwShFpsBseqConversionStats stats)
    {
        var data = Convert(source, OpeningDemoTimelineScale, out stats);
        var file = SwShBseqFile.Parse(data);
        if (file.Commands.Count <= OpeningDemoNooneLifetimeCommandIndex)
        {
            throw new InvalidDataException("Opening demo BSEQ does not contain the expected noone startup command.");
        }

        var nooneLifetimeCommand = file.Commands[OpeningDemoNooneLifetimeCommandIndex];
        if (nooneLifetimeCommand.StartFrame != 0 || nooneLifetimeCommand.EndFrame != OpeningDemoNooneScaledEndFrame)
        {
            throw new InvalidDataException("Opening demo BSEQ noone startup command did not match the expected 60FPS layout.");
        }

        WriteU32(data, nooneLifetimeCommand.Offset + sizeof(uint), 0);
        stats = stats with { FieldsChanged = stats.FieldsChanged + 1 };
        return data;
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

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }
}
