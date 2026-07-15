// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.Executable;

namespace KM.SwSh.Tests.ExeFs;

internal static class SwShExeFsPatchTestFixtures
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    public static byte[] CreateCompatibleNso(ProjectGame game)
    {
        return CreateNso(
            CreateCompatibleText(game),
            Enumerable.Repeat((byte)0x10, 64).ToArray(),
            Enumerable.Repeat((byte)0x20, 64).ToArray(),
            SupportedBuildId(game));
    }

    public static byte[] CreateCompatibleNsoWithUnsupportedBuild(ProjectGame anchorGame)
    {
        return CreateNso(
            CreateCompatibleText(anchorGame),
            Enumerable.Repeat((byte)0x10, 64).ToArray(),
            Enumerable.Repeat((byte)0x20, 64).ToArray(),
            Enumerable.Repeat((byte)0xAB, 20).ToArray());
    }

    public static byte[] CreateCompatibleNsoWithoutCodeCaves(ProjectGame game)
    {
        return CreateNso(
            CreateCompatibleText(game, fillByte: 0xCC),
            Enumerable.Repeat((byte)0x10, 64).ToArray(),
            Enumerable.Repeat((byte)0x20, 64).ToArray(),
            SupportedBuildId(game));
    }

    public static byte[] ReplaceTextInstruction(byte[] mainBytes, int offset, uint instruction)
    {
        var nso = NsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();
        WriteInstruction(text, offset, instruction);
        return nso.Write(textDecompressedData: text);
    }

    public static byte[] WithHashCheckFlags(byte[] mainBytes, NsoFlags hashCheckFlags)
    {
        var output = mainBytes.ToArray();
        var currentFlags = (NsoFlags)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(0x0C, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(0x0C, 4),
            (uint)(currentFlags | hashCheckFlags));
        return output;
    }

    public static byte[] CorruptHeaderHash(byte[] mainBytes, string segmentName)
    {
        var hashOffset = segmentName switch
        {
            ".text" => 0xA0,
            ".ro" => 0xC0,
            ".data" => 0xE0,
            _ => throw new ArgumentOutOfRangeException(nameof(segmentName)),
        };
        var output = mainBytes.ToArray();
        output[hashOffset] ^= 0xFF;
        return output;
    }

    public static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static byte[] CreateCompatibleText(ProjectGame game, byte fillByte = 0)
    {
        var text = new byte[0x01421100];
        if (fillByte != 0)
        {
            Array.Fill(text, fillByte);
        }

        foreach (var check in UiRouteChecks)
        {
            WriteInstruction(text, check.CompareOffset, EncodeCmpImmediate(check.ItemRegister, 50));
            WriteInstruction(
                text,
                check.CompareOffset + 4,
                EncodeConditionalBranch(check.CompareOffset + 4, check.FailOffset, Arm64Condition.NE));
        }

        foreach (var check in EqualBranchChecks)
        {
            WriteInstruction(text, check.CompareOffset, EncodeCmpImmediate(check.ItemRegister, 50));
            WriteInstruction(
                text,
                check.CompareOffset + 4,
                EncodeConditionalBranch(check.CompareOffset + 4, check.TargetOffset, Arm64Condition.EQ));
        }

        WriteInstruction(text, 0x007BC1BC, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007BC1C4, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007B1F20, 0x2A0003E2);
        WriteInstruction(text, 0x007BAF38, 0x6B36231F);
        WriteInstruction(text, 0x007BAF3C, 0x1A963316);
        WriteInstruction(text, 0x007DDA8C, EncodeCmpImmediate(8, 0x32));
        WriteInstruction(
            text,
            0x007DDA90,
            EncodeConditionalBranch(0x007DDA90, 0x007DDAF8, Arm64Condition.HI));

        var ownershipOffset = game == ProjectGame.Shield ? 0x01420F20 : 0x01420EF0;
        var countOffset = game == ProjectGame.Shield ? 0x014210C0 : 0x01421090;
        WriteInstruction(text, ownershipOffset, 0xF81D0FF5);
        WriteInstruction(text, countOffset, 0xA9BE4FF4);
        return text;
    }

    private static byte[] SupportedBuildId(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, 4), instruction);
    }

    private static uint EncodeConditionalBranch(
        int sourceOffset,
        int targetOffset,
        Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        var immediate = delta >> 2;
        return (uint)(0x54000000 | ((immediate & 0x7FFFF) << 5) | ((int)condition & 0xF));
    }

    private static byte[] CreateNso(
        byte[] text,
        byte[] ro,
        byte[] data,
        byte[] buildId)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x08), 0xA1B2C3D4);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        buildId.CopyTo(output.AsSpan(0x40, 0x20));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        for (var offset = 0x6C; offset < 0xA0; offset++)
        {
            output[offset] = (byte)(offset ^ 0x5A);
        }

        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        var nso = NsoFile.Parse(output);
        var compressedRo = ro.ToArray();
        var compressedData = data.ToArray();
        compressedRo[0] ^= 0xFF;
        compressedData[0] ^= 0xFF;
        return (nso with { Flags = NsoFlags.CompressedRo | NsoFlags.CompressedData }).Write(
            roDecompressedData: compressedRo,
            dataDecompressedData: compressedData);
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

    private static readonly UiRouteCheck[] UiRouteChecks =
    [
        new(0x00747988, 28, 0x00747A80),
        new(0x00747D44, 9, 0x007477E8),
        new(0x0074BA24, 26, 0x0074BAD4),
        new(0x0074BDA8, 9, 0x0074B788),
        new(0x0074DFE4, 9, 0x0074DE78),
        new(0x0074DFF8, 28, 0x0074E16C),
        new(0x0075CEFC, 9, 0x0075CC18),
        new(0x007BB204, 20, 0x007BB26C),
        new(0x007BB3C0, 19, 0x007BB3EC),
        new(0x007BC1F8, 8, 0x007BC2B4),
    ];

    private static readonly EqualBranchCheck[] EqualBranchChecks =
    [
        new(0x00747DE0, 9, 0x00747D4C),
        new(0x0074BE44, 9, 0x0074BDB0),
        new(0x0075CCE8, 27, 0x0075D064),
        new(0x0075D08C, 10, 0x0075D05C),
        new(0x007BBFD4, 23, 0x007BC054),
    ];

    private enum Arm64Condition
    {
        EQ = 0,
        NE = 1,
        HI = 8,
    }

    private sealed record UiRouteCheck(int CompareOffset, int ItemRegister, int FailOffset);

    private sealed record EqualBranchCheck(int CompareOffset, int ItemRegister, int TargetOffset);
}
