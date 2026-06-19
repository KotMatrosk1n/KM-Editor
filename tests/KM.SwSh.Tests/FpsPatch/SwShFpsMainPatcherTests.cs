// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.FpsPatch;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.FpsPatch;

public sealed class SwShFpsMainPatcherTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const int SwordNvnOffset = 0x018A2C88;
    private const int ShieldNvnOffset = 0x018A2D18;
    private const int SwordSchedulerAdrpOffset = 0x0131677C;
    private const int SwordSchedulerLdrOffset = 0x01316780;
    private const int ShieldSchedulerAdrpOffset = 0x013167AC;
    private const int ShieldSchedulerLdrOffset = 0x013167B0;

    [Theory]
    [InlineData(ProjectGame.Sword, SwordBuildId, SwordNvnOffset, SwordSchedulerAdrpOffset, SwordSchedulerLdrOffset)]
    [InlineData(ProjectGame.Shield, ShieldBuildId, ShieldNvnOffset, ShieldSchedulerAdrpOffset, ShieldSchedulerLdrOffset)]
    public void ApplySupportsSwordAndShieldLayouts(
        ProjectGame game,
        string buildId,
        int nvnOffset,
        int schedulerAdrpOffset,
        int schedulerLdrOffset)
    {
        var main = CreateMain(buildId, nvnOffset, schedulerAdrpOffset, schedulerLdrOffset);

        var before = SwShFpsMainPatcher.Analyze(main, game);
        var patched = SwShFpsMainPatcher.Apply(main, game);
        var after = SwShFpsMainPatcher.Analyze(patched, game);

        Assert.Equal(SwShFpsPatchMainKind.NotInstalled, before.Kind);
        Assert.Equal(game, before.DetectedGame);
        Assert.Equal(SwShFpsPatchMainKind.Installed, after.Kind);
        Assert.Equal(15, after.PatchedSiteCount);
        AssertPatchedBytes(patched, nvnOffset, schedulerAdrpOffset, schedulerLdrOffset);
    }

    [Fact]
    public void ShieldLayoutUsesMovedNvnAndSchedulerSites()
    {
        var main = CreateMain(
            ShieldBuildId,
            ShieldNvnOffset,
            ShieldSchedulerAdrpOffset,
            ShieldSchedulerLdrOffset);
        var text = SwShNsoFile.Parse(main).Text.DecompressedData.ToArray();
        text.AsSpan(SwordNvnOffset, sizeof(uint)).Fill(0xAA);
        text.AsSpan(SwordSchedulerAdrpOffset, sizeof(uint)).Fill(0xBB);
        text.AsSpan(SwordSchedulerLdrOffset, sizeof(uint)).Fill(0xCC);
        main = SwShNsoFile.Parse(main).Write(textDecompressedData: text);

        var patched = SwShFpsMainPatcher.Apply(main, ProjectGame.Shield);
        var patchedText = SwShNsoFile.Parse(patched).Text.DecompressedData;

        Assert.Equal(Enumerable.Repeat((byte)0xAA, sizeof(uint)).ToArray(), patchedText.AsSpan(SwordNvnOffset, sizeof(uint)).ToArray());
        Assert.Equal(Enumerable.Repeat((byte)0xBB, sizeof(uint)).ToArray(), patchedText.AsSpan(SwordSchedulerAdrpOffset, sizeof(uint)).ToArray());
        Assert.Equal(Enumerable.Repeat((byte)0xCC, sizeof(uint)).ToArray(), patchedText.AsSpan(SwordSchedulerLdrOffset, sizeof(uint)).ToArray());
        AssertPatchedBytes(patched, ShieldNvnOffset, ShieldSchedulerAdrpOffset, ShieldSchedulerLdrOffset);
    }

    [Fact]
    public void ApplyRejectsSelectedGameMismatch()
    {
        var shieldMain = CreateMain(
            ShieldBuildId,
            ShieldNvnOffset,
            ShieldSchedulerAdrpOffset,
            ShieldSchedulerLdrOffset);

        var analysis = SwShFpsMainPatcher.Analyze(shieldMain, ProjectGame.Sword);

        Assert.Equal(SwShFpsPatchMainKind.GameMismatch, analysis.Kind);
        Assert.Throws<InvalidDataException>(() => SwShFpsMainPatcher.Apply(shieldMain, ProjectGame.Sword));
    }

    [Fact]
    public void RestoreShieldRestoresOwnedBytesAndPreservesOtherMainEdits()
    {
        var baseMain = CreateMain(
            ShieldBuildId,
            ShieldNvnOffset,
            ShieldSchedulerAdrpOffset,
            ShieldSchedulerLdrOffset);
        var patched = SwShFpsMainPatcher.Apply(baseMain, ProjectGame.Shield);
        var patchedNso = SwShNsoFile.Parse(patched);
        var text = patchedNso.Text.DecompressedData.ToArray();
        const int otherEditOffset = 0x00100000;
        text[otherEditOffset] = 0x5A;
        var patchedWithOtherEdit = patchedNso.Write(textDecompressedData: text);

        var restored = SwShFpsMainPatcher.RestoreFromBase(patchedWithOtherEdit, baseMain, ProjectGame.Shield);
        var restoredAnalysis = SwShFpsMainPatcher.Analyze(restored, ProjectGame.Shield);
        var restoredText = SwShNsoFile.Parse(restored).Text.DecompressedData;

        Assert.Equal(SwShFpsPatchMainKind.NotInstalled, restoredAnalysis.Kind);
        Assert.Equal(0x5A, restoredText[otherEditOffset]);
    }

    private static byte[] CreateMain(
        string buildId,
        int nvnOffset,
        int schedulerAdrpOffset,
        int schedulerLdrOffset)
    {
        var text = new byte[0x018A4000];
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

        return CreateNso(text, [0x01, 0x02, 0x03], [0x04, 0x05, 0x06], Convert.FromHexString(buildId));
    }

    private static void AssertPatchedBytes(
        byte[] main,
        int nvnOffset,
        int schedulerAdrpOffset,
        int schedulerLdrOffset)
    {
        var text = SwShNsoFile.Parse(main).Text.DecompressedData;
        AssertBytes(text, nvnOffset, "E1030032");
        AssertBytes(text, 0x000061F0, "02008052");
        AssertBytes(text, 0x0000620C, "02008052");
        AssertBytes(text, 0x005DE834, "69058A52");
        AssertBytes(text, 0x005DE838, "C91FA072");
        AssertBytes(text, schedulerAdrpOffset, "A94900D0");
        AssertBytes(text, schedulerLdrOffset, "20CD46BD");
        AssertBytes(text, 0x009D17B0, "01902E1E");
        AssertBytes(text, 0x009D17B4, "0008211E");
        AssertBytes(text, 0x009D17B8, "00E804BD");
        AssertBytes(text, 0x009D17BC, "91F0FF17");
        AssertBytes(text, 0x009D05C8, "08F4A752");
        AssertBytes(text, 0x009D0834, "00902C1E");
        AssertBytes(text, 0x009D0838, "01902E1E");
        AssertBytes(text, 0x009D0848, "00902C1E");
    }

    private static void AssertBytes(byte[] data, int offset, string expectedHex)
    {
        var expected = Convert.FromHexString(expectedHex);
        Assert.Equal(expected, data.AsSpan(offset, expected.Length).ToArray());
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
