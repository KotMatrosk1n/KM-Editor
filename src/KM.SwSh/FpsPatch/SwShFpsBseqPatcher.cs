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
    private const ulong AbsoluteFrameJumpCommand = 0x4FFDD33C6628F5F9;

    public static byte[] Convert(byte[] source, double scale, out SwShFpsBseqConversionStats stats)
        => ConvertTimeline(source, scale, scaleJumpDestinations: true, out stats);

    // Reproduce earlier output exactly for ownership migration and restoration.
    internal static byte[] ConvertLegacyTimeline(byte[] source, double scale)
        => ConvertTimeline(source, scale, scaleJumpDestinations: false, out _);

    private static byte[] ConvertTimeline(
        byte[] source,
        double scale,
        bool scaleJumpDestinations,
        out SwShFpsBseqConversionStats stats)
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

            if (scaleJumpDestinations && command.Hash == AbsoluteFrameJumpCommand)
            {
                if (command.PayloadLength != 8)
                {
                    throw new InvalidDataException("BSEQ frame jump has an unsupported payload layout.");
                }

                var mode = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(command.PayloadOffset, 4));
                var target = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(command.PayloadOffset + 4, 4));
                if (mode > 4 || target < 0 || target > file.FrameCount)
                {
                    throw new InvalidDataException("BSEQ frame jump has an unsupported condition or destination.");
                }

                var scaledTarget = ScaleU32((uint)target, scale);
                if (scaledTarget > int.MaxValue)
                {
                    throw new InvalidDataException("BSEQ frame jump exceeds the supported signed frame range.");
                }

                WriteU32(data, command.PayloadOffset + 4, scaledTarget);
                if (scaledTarget != target)
                {
                    fieldsChanged++;
                }
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
        if (!double.IsFinite(scale) || scale <= 0)
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
