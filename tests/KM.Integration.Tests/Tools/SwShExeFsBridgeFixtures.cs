// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.Formats.Executable;
using System.Buffers.Binary;

namespace KM.Integration.Tests.Tools;

internal static class SwShExeFsBridgeFixtures
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const int DynamaxAdventureSummaryOffset = 0x00774054;
    private const int DynamaxAdventureSummaryEntrySize = 0x06;
    private static readonly byte[] TypeChartDependenciesBefore = Convert.FromHexString(
        "E84C74FE0C4D74FE084D74FE0C4D74FE0C4D74FE0C4D74FEF84C74FEE04D74FE" +
        "EC4D74FEF44D74FEEC4D74FE084E74FEEC4D74FEEC4D74FEEC4D74FE004E74FE");
    private static readonly byte[] TypeChartDependenciesAfter = Convert.FromHexString(
        "0000000001000000020000000400000008000000100000002000000040000000" +
        "800000000001000000020000000400000008000000100000F85D74FE105E74FE");
    private static readonly byte[] TypeChartVanillaValues = Convert.FromHexString(
        "040404040402040002040404040404040404080402020408020008040404040208040802040804040402" +
        "080402040408020404040404040404020202040200040408040404040408040400080408020408080402" +
        "080404040404040208040204080402080404040408040404040202020404040202020408040804040802" +
        "000404040404040804040404040804040204040404040408040402020204020408040408040404040402" +
        "080408020208040408020404040404040808040404080202040404020404040402020808020402020802" +
        "040404020404040408040004040404040802020404020404040804080404040402040404040204040004" +
        "040408040804040402020208040402080404040404040404040402040404040404080400040204040404" +
        "040804040404040804040202040804020404040402020404040404080804");

    public static byte[] CreateCompatibleNso()
    {
        return CreateNso(CreateCompatibleText(), [0x10], [0x20], Convert.FromHexString(SwordBuildId));
    }

    public static byte[] CreateTypeChartCompatibleNso()
    {
        var ro = new byte[0x00743600 + (18 * 18) + 0x40];
        Array.Fill(ro, (byte)0xCC);
        TypeChartDependenciesBefore.CopyTo(ro.AsSpan(0x00743600 - TypeChartDependenciesBefore.Length));
        TypeChartVanillaValues.CopyTo(ro.AsSpan(0x00743600));
        TypeChartDependenciesAfter.CopyTo(ro.AsSpan(0x00743600 + TypeChartVanillaValues.Length));
        return CreateNso(CreateCompatibleText(), ro, [0x20], Convert.FromHexString(SwordBuildId));
    }

    public static byte[] CreateDynamaxAdventureBossTargetCompatibleNso(int entryCount)
    {
        var text = new byte[0x015D68AC + sizeof(uint)];
        var ro = new byte[DynamaxAdventureSummaryOffset + (entryCount * DynamaxAdventureSummaryEntrySize)];
        WriteInstruction(text, 0x015D615C, 0x2A1503E1);
        WriteInstruction(text, 0x015D68AC, 0x2A1403E1);
        return CreateNso(text, ro, [0x20], Convert.FromHexString(SwordBuildId));
    }

    private static byte[] CreateCompatibleText()
    {
        var text = new byte[0x0157D000];
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
        WriteInstruction(text, 0x007BAF38, 0x6B36231F);
        WriteInstruction(text, 0x007BAF3C, 0x1A963316);
        WriteInstruction(text, 0x007B1F20, 0x2A0003E2);
        WriteInstruction(text, 0x007DDA8C, EncodeCmpImmediate(8, 0x32));
        WriteInstruction(text, 0x007DDA90, EncodeConditionalBranch(0x007DDA90, 0x007DDAF8, Arm64Condition.HI));
        WriteInstruction(text, 0x01420EF0, 0xF81D0FF5);
        WriteInstruction(text, 0x01421090, 0xA9BE4FF4);
        WriteInstruction(text, 0x0137F634, 0x94001F27);
        WriteInstruction(text, 0x0138F268, 0x9400023E);
        WriteInstruction(text, 0x013872D0, 0xD103C3FF);
        WriteInstruction(text, 0x01385A70, 0xD10143FF);
        WriteInstruction(text, 0x00779070, 0x7100143F);
        WriteInstruction(text, 0x00778E20, 0xA9BF7BFD);
        WriteInstruction(text, 0x007790D0, 0xA9BE4FF4);
        WriteInstruction(text, 0x00779F50, 0xA9BF7BFD);
        WriteInstruction(text, 0x0077AC30, 0x7100143F);
        WriteInstruction(text, 0x0077AC70, 0xF81E0FF3);
        WriteInstruction(text, 0x0077AFD0, 0xF81E0FF3);
        WriteInstruction(text, 0x0138F990, 0xA9BC5FF8);
        WriteInstruction(text, 0x0138FB60, 0xD10243FF);
        WriteInstruction(text, 0x0138A1A0, 0xD10503FF);
        WriteInstruction(text, 0x0138B550, 0xA9457BFD);
        WriteInstruction(text, 0x0138B1E0, 0xD10183FF);
        WriteInstruction(text, 0x0138B1FC, 0x39592408);
        WriteInstruction(text, 0x0138B200, 0x52000108);
        WriteInstruction(text, 0x0139FB60, 0x340000A8);
        WriteInstruction(text, 0x013B2F90, 0xD10143FF);
        WriteInstruction(text, 0x013CA220, 0xF81D0FF5);
        WriteIvScreenCallSiteAnchors(text);
        WriteGymUniformRemovalVanillaAnchors(text);
        WriteFashionUnlockVanillaAnchors(text);
        return text;
    }

    private static void WriteGymUniformRemovalVanillaAnchors(byte[] text)
    {
        WriteInstruction(text, 0x01472600, 0xD0008CE8);
        WriteInstruction(text, 0x01472604, 0xB9400833);
        WriteInstruction(text, 0x01472630, 0xD0008CE8);
        WriteInstruction(text, 0x01472634, 0xB9400833);
    }

    private static void WriteFashionUnlockVanillaAnchors(byte[] text)
    {
        WriteInstruction(text, 0x0143A2B0, 0xAA0003E8);
        WriteInstruction(text, 0x0143A2B4, 0x2A1F03E0);
        WriteInstruction(text, 0x0143A300, 0xD10603FF);
        WriteInstruction(text, 0x0143A304, 0xA9145FFC);
    }

    private static void WriteIvScreenCallSiteAnchors(byte[] text)
    {
        foreach (var (offset, instruction) in new (int Offset, uint Instruction)[]
        {
            (0x0138FBE8, 0x97CFA48E),
            (0x0138FC38, 0x97CFA47A),
            (0x0138FC74, 0x97CFA46B),
            (0x0138FC9C, 0x97CFA461),
            (0x0138FD2C, 0x97CFA43D),
            (0x0138FD5C, 0x97CFA431),
            (0x0138FD84, 0x97CFA427),
            (0x0138FEA0, 0x97CFA3E0),
            (0x0138A2B4, 0x97CFC347),
            (0x0138A3CC, 0x97CFC229),
            (0x0138A47C, 0x97CFC1ED),
            (0x0138A518, 0x97CFC1C6),
            (0x0138A5B4, 0x97CFC19F),
            (0x0138A650, 0x97CFC178),
            (0x0138A6F0, 0x97CFC150),
            (0x0138AA50, 0x97CFBD40),
            (0x0138AA60, 0x97CFC074),
            (0x0138AA90, 0x97CFBD30),
            (0x0138AAA0, 0x97CFC064),
            (0x0138AAD0, 0x97CFBD20),
            (0x0138AAE0, 0x97CFC054),
            (0x0138AB10, 0x97CFBD10),
            (0x0138AB20, 0x97CFC044),
            (0x0138AB50, 0x97CFBD00),
            (0x0138AB60, 0x97CFC034),
            (0x0138AB90, 0x97CFBCF0),
            (0x0138ABA0, 0x97CFC024),
            (0x0138AC88, 0x0B130008),
            (0x0138ACAC, 0x0B130008),
            (0x0138ACD0, 0x0B130008),
            (0x0138ACF8, 0x0B170008),
            (0x0138AD1C, 0x0B130008),
            (0x0138AD40, 0x0B130008),
            (0x0138AE28, 0x97CFBE2E),
            (0x0138AE3C, 0x97CFBE29),
            (0x0138AE50, 0x97CFBE24),
            (0x0138AE64, 0x97CFBE1F),
            (0x0138AE78, 0x97CFBE1A),
            (0x0138AE8C, 0x97CFBE15),
            (0x0138AEAC, 0x2A1F03E8),
            (0x0138AEB0, 0x7103F27F),
            (0x0138AEB4, 0x54000063),
            (0x0138AEB8, 0x39592688),
            (0x0138AEBC, 0x52000108),
            (0x0138AEE0, 0x7103EF1F),
            (0x0138AEE4, 0x54000089),
            (0x0138AEE8, 0x39592688),
            (0x0138AEEC, 0x52000108),
            (0x0138AEF0, 0x14000002),
            (0x0138AEF4, 0x2A1F03E8),
            (0x0138AF18, 0x7103F2FF),
            (0x0138AF1C, 0x54000083),
            (0x0138AF20, 0x39592688),
            (0x0138AF24, 0x52000108),
            (0x0138AF28, 0x14000002),
            (0x0138AF2C, 0x2A1F03E8),
            (0x0138AF54, 0x7103F39F),
            (0x0138AF58, 0x54000083),
            (0x0138AF5C, 0x39592688),
            (0x0138AF60, 0x52000108),
            (0x0138AF64, 0x14000002),
            (0x0138AF68, 0x2A1F03E8),
            (0x0138AF8C, 0x7103F37F),
            (0x0138AF90, 0x54000083),
            (0x0138AF94, 0x39592688),
            (0x0138AF98, 0x52000108),
            (0x0138AF9C, 0x14000002),
            (0x0138AFA0, 0x2A1F03E8),
            (0x0138AFC4, 0x7103F33F),
            (0x0138AFC8, 0x54000083),
            (0x0138AFCC, 0x39592688),
            (0x0138AFD0, 0x52000108),
            (0x0138AFD4, 0x14000002),
            (0x0138AFD8, 0x2A1F03E8),
            (0x0138B230, 0x39592668),
            (0x0138B264, 0x39592668),
            (0x0138B298, 0x39592668),
            (0x0138B2CC, 0x39592668),
            (0x0138B300, 0x39592668),
            (0x0138B334, 0x39592668),
            (0x0138B368, 0x39592668),
            (0x0138B39C, 0x39592668),
            (0x0138B3AC, 0xF942EE60),
            (0x0138B3B0, 0x97FFE9B0),
            (0x0138B3B4, 0x2A1F03E1),
            (0x01392EA8, 0x97FFDCBE),
            (0x01393310, 0x97FFDBA4),
            (0x0139EF4C, 0x97FFAC95),
        })
        {
            WriteInstruction(text, offset, instruction);
        }
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, 4), instruction);
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static uint EncodeConditionalBranch(int sourceOffset, int targetOffset, Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        var imm19 = delta >> 2;
        return (uint)(0x54000000 | ((imm19 & 0x7FFFF) << 5) | ((int)condition & 0xF));
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[]? buildId = null)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Clear();
        (buildId ?? Convert.FromHexString(SwordBuildId)).CopyTo(output.AsSpan(0x40, 0x20));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
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

    private static readonly RareCandyUiCheck[] UiRouteChecks =
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

    private static readonly RareCandyEqualBranchCheck[] EqualBranchChecks =
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

    private sealed record RareCandyUiCheck(
        int CompareOffset,
        int ItemRegister,
        int FailOffset);

    private sealed record RareCandyEqualBranchCheck(
        int CompareOffset,
        int ItemRegister,
        int TargetOffset);
}
