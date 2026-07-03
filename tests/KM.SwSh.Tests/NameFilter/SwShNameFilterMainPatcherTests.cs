// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.NameFilter;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.NameFilter;

public sealed class SwShNameFilterMainPatcherTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const uint SwordVanillaProfanityCheckCall = 0x97E37E86;
    private const uint ShieldVanillaProfanityCheckCall = 0x97E37E7A;
    private const uint KmReturnCleanInstruction = 0x2A1F03E0;
    private const uint CompatibleReturnCleanInstruction = 0x52800000;

    [Theory]
    [InlineData(ProjectGame.Sword, "main.text+0x00EF1228")]
    [InlineData(ProjectGame.Shield, "main.text+0x00EF1258")]
    public void AnalyzeFindsVanillaNameFilterCall(ProjectGame game, string expectedPatchOffset)
    {
        var main = CreateSyntheticNameFilterMain(game);

        var analysis = SwShNameFilterMainPatcher.Analyze(main, game);

        Assert.Equal(SwShNameFilterMainKind.NotInstalled, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal(expectedPatchOffset, analysis.PatchOffsetHex);
        Assert.Equal("vanilla profanity-filter call", analysis.PatchShape);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ApplyPatchesOnlyReservedTextInstruction(ProjectGame game)
    {
        var main = CreateSyntheticNameFilterMain(game, extraTextSetup: text =>
        {
            text[0x100] = 0x42;
            text[0xD00000] = 0x24;
        });
        var before = NsoFile.Parse(main);

        var patched = SwShNameFilterMainPatcher.Apply(main, game);
        var after = NsoFile.Parse(patched);
        var analysis = SwShNameFilterMainPatcher.Analyze(patched, game);

        Assert.Equal(SwShNameFilterMainKind.Installed, analysis.Kind);
        Assert.True(SwShNameFilterMainPatcher.HasInstalledHook(patched));
        Assert.Equal(before.Ro.DecompressedData, after.Ro.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.Equal(KmReturnCleanInstruction, ReadInstruction(after.Text.DecompressedData, PatchOffset(game)));
        Assert.Equal(0x42, after.Text.DecompressedData[0x100]);
        Assert.Equal(0x24, after.Text.DecompressedData[0xD00000]);
        AssertOnlyReservedTextBytesChanged(main, patched);
    }

    [Fact]
    public void AnalyzeRejectsSelectedGameMismatch()
    {
        var main = CreateSyntheticNameFilterMain(ProjectGame.Sword);

        var analysis = SwShNameFilterMainPatcher.Analyze(main, ProjectGame.Shield);

        Assert.Equal(SwShNameFilterMainKind.GameMismatch, analysis.Kind);
        Assert.Equal(ProjectGame.Sword, analysis.DetectedGame);
        Assert.Contains("will not patch this file", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ApplyRefreshesCompatibleCleanResultInstruction(ProjectGame game)
    {
        var main = CreateSyntheticNameFilterMain(
            game,
            patchInstruction: CompatibleReturnCleanInstruction);

        var compatible = SwShNameFilterMainPatcher.Analyze(main, game);
        var patched = SwShNameFilterMainPatcher.Apply(main, game);
        var analysis = SwShNameFilterMainPatcher.Analyze(patched, game);

        Assert.Equal(SwShNameFilterMainKind.InstalledCompatible, compatible.Kind);
        Assert.Equal(SwShNameFilterMainKind.Installed, analysis.Kind);
        Assert.Equal(KmReturnCleanInstruction, ReadInstruction(NsoFile.Parse(patched).Text.DecompressedData, PatchOffset(game)));
        AssertOnlyReservedTextBytesChanged(main, patched);
    }

    [Fact]
    public void AnalyzeReportsConflictForUnexpectedInstruction()
    {
        var main = CreateSyntheticNameFilterMain(
            ProjectGame.Sword,
            patchInstruction: 0xDEADBEEF);

        var analysis = SwShNameFilterMainPatcher.Analyze(main, ProjectGame.Sword);

        Assert.Equal(SwShNameFilterMainKind.Conflict, analysis.Kind);
        Assert.Contains("expected the vanilla profanity-filter call", analysis.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => SwShNameFilterMainPatcher.Apply(main, ProjectGame.Sword));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RestoreFromBaseRestoresVanillaInstructionAndPreservesOtherEdits(ProjectGame game)
    {
        var baseMain = CreateSyntheticNameFilterMain(game, extraTextSetup: text => text[0x100] = 0x11);
        var currentMain = SwShNameFilterMainPatcher.Apply(
            CreateSyntheticNameFilterMain(game, extraTextSetup: text => text[0x100] = 0x42),
            game);

        var restored = SwShNameFilterMainPatcher.RestoreFromBase(currentMain, baseMain, game);
        var restoredText = NsoFile.Parse(restored).Text.DecompressedData;
        var analysis = SwShNameFilterMainPatcher.Analyze(restored, game);

        Assert.Equal(SwShNameFilterMainKind.NotInstalled, analysis.Kind);
        Assert.Equal(VanillaInstruction(game), ReadInstruction(restoredText, PatchOffset(game)));
        Assert.Equal(0x42, restoredText[0x100]);
        AssertOnlyReservedTextBytesChanged(currentMain, restored);
    }

    private static byte[] CreateSyntheticNameFilterMain(
        ProjectGame game,
        uint? patchInstruction = null,
        Action<byte[]>? extraTextSetup = null)
    {
        var text = new byte[PatchOffset(game) + 0x40];
        Array.Fill(text, (byte)0xCC);
        WriteInstruction(text, PatchOffset(game), patchInstruction ?? VanillaInstruction(game));
        extraTextSetup?.Invoke(text);

        return CreateNso(text, [0x10], [0x20], BuildIdForGame(game));
    }

    private static int PatchOffset(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? SwShNameFilterMainPatcher.ShieldProfanityCheckCallOffset
            : SwShNameFilterMainPatcher.SwordProfanityCheckCallOffset;
    }

    private static uint VanillaInstruction(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? ShieldVanillaProfanityCheckCall
            : SwordVanillaProfanityCheckCall;
    }

    private static void AssertOnlyReservedTextBytesChanged(byte[] beforeMain, byte[] afterMain)
    {
        var before = NsoFile.Parse(beforeMain);
        var after = NsoFile.Parse(afterMain);

        Assert.Equal(before.Ro.DecompressedData, after.Ro.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.All(
            ChangedOffsets(before.Text.DecompressedData, after.Text.DecompressedData),
            changedOffset => Assert.Contains(
                SwShNameFilterMainPatcher.ReservedMainTextRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    private static int[] ChangedOffsets(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        return Enumerable.Range(0, before.Length)
            .Where(index => before[index] != after[index])
            .ToArray();
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text.Slice(offset, sizeof(uint)));
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
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
        buildId.CopyTo(output.AsSpan(0x40, 0x20));
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

    private static void WriteSegmentHeader(byte[] output, int offset, int fileOffset, int memoryOffset, int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static byte[] BuildIdForGame(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
