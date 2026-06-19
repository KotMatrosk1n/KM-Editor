// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using System.Buffers.Binary;

namespace KM.SwSh.Tests.FpsPatch;

internal static class SwShFpsMainTestAnchors
{
    public const int RequiredTextLength = 0x018A4000;
    public const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    public const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    public static byte[] CreateMain(ProjectGame game)
    {
        var text = new byte[RequiredTextLength];
        WriteVanilla(text, game);
        var buildId = Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
        return CreateNso(text, [0x01, 0x02, 0x03], [0x04, 0x05, 0x06], buildId);
    }

    public static void WriteVanilla(byte[] text, ProjectGame game)
    {
        var nvnOffset = game == ProjectGame.Shield ? 0x018A2D18 : 0x018A2C88;
        var schedulerAdrpOffset = game == ProjectGame.Shield ? 0x013167AC : 0x0131677C;
        var schedulerLdrOffset = game == ProjectGame.Shield ? 0x013167B0 : 0x01316780;

        WriteBytes(text, nvnOffset, "E103152A");
        WriteBytes(text, 0x000061F0, "E2030032");
        WriteBytes(text, 0x0000620C, "E2030032");
        WriteBytes(text, 0x005DE834, "C90A9452");
        WriteBytes(text, 0x005DE838, "893FA072");
        WriteBytes(text, schedulerAdrpOffset, "A94900B0");
        WriteBytes(text, schedulerLdrOffset, "20C94FBD");
        WriteBytes(text, 0x009D17B0, "08F044B9");
        WriteBytes(text, 0x009D17B4, "1FE90D71");
        WriteBytes(text, 0x009D17B8, "21010054");
        WriteBytes(text, 0x009D17BC, "080445B9");
        WriteBytes(text, 0x009D05C8, "E81B0932");
        WriteBytes(text, 0x009D0834, "00102C1E");
        WriteBytes(text, 0x009D0838, "01102E1E");
        WriteBytes(text, 0x009D0848, "00102C1E");
    }

    private static void WriteBytes(byte[] data, int offset, string hex)
    {
        Convert.FromHexString(hex).CopyTo(data.AsSpan(offset));
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
    {
        var textOffset = SwShNsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), SwShNsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        buildId.CopyTo(output.AsSpan(0x40));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        SwShNsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        SwShNsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        SwShNsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static void WriteSegmentHeader(
        byte[] output,
        int offset,
        int fileOffset,
        int memoryOffset,
        int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
