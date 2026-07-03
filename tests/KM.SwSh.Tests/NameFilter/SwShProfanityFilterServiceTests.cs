// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.NameFilter;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.NameFilter;

public sealed class SwShProfanityFilterServiceTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const uint SwordVanillaProfanityCheckCall = 0x97E37E86;
    private const uint ShieldVanillaProfanityCheckCall = 0x97E37E7A;
    private const uint KmReturnCleanInstruction = 0x2A1F03E0;

    [Fact]
    public void ApplyPatchesLayeredMainAndPreservesOtherOutputExeFsEdits()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = CreateSyntheticNameFilterMain(ProjectGame.Sword, extraTextSetup: text => text[0x100] = 0x11);
        var layeredMain = CreateSyntheticNameFilterMain(ProjectGame.Sword, extraTextSetup: text => text[0x100] = 0x42);
        temp.WriteBaseExeFsFile("main", baseMain);
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        temp.WriteOutputFile("exefs/main", layeredMain);
        var service = new SwShProfanityFilterService();

        var result = service.Apply(paths);

        Assert.DoesNotContain(result.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("installed", result.Status.Status);
        Assert.Equal("layered", result.Status.SourceLayer);
        Assert.Equal(["exefs/main"], result.ApplyResult.WrittenFiles.Select(file => file.RelativePath).ToArray());
        var outputText = NsoFile.Parse(File.ReadAllBytes(OutputMainPath(temp))).Text.DecompressedData;
        Assert.Equal(KmReturnCleanInstruction, ReadInstruction(outputText, SwShNameFilterMainPatcher.SwordProfanityCheckCallOffset));
        Assert.Equal(0x42, outputText[0x100]);
    }

    [Fact]
    public void RestoreRemovesOnlyOwnedInstructionAndPreservesOtherOutputExeFsEdits()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Shield };
        var baseMain = CreateSyntheticNameFilterMain(ProjectGame.Shield, extraTextSetup: text => text[0x100] = 0x11);
        var layeredMain = SwShNameFilterMainPatcher.Apply(
            CreateSyntheticNameFilterMain(ProjectGame.Shield, extraTextSetup: text => text[0x100] = 0x42),
            ProjectGame.Shield);
        temp.WriteBaseExeFsFile("main", baseMain);
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Shield);
        temp.WriteOutputFile("exefs/main", layeredMain);
        var service = new SwShProfanityFilterService();

        var result = service.Restore(paths);

        Assert.DoesNotContain(result.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("notInstalled", result.Status.Status);
        Assert.True(File.Exists(OutputMainPath(temp)));
        Assert.Equal(["exefs/main"], result.ApplyResult.WrittenFiles.Select(file => file.RelativePath).ToArray());
        var outputText = NsoFile.Parse(File.ReadAllBytes(OutputMainPath(temp))).Text.DecompressedData;
        Assert.Equal(ShieldVanillaProfanityCheckCall, ReadInstruction(outputText, SwShNameFilterMainPatcher.ShieldProfanityCheckCallOffset));
        Assert.Equal(0x42, outputText[0x100]);
    }

    [Fact]
    public void RestoreDeletesOutputMainWhenProfanityFilterWasTheOnlyExeFsChange()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = CreateSyntheticNameFilterMain(ProjectGame.Sword);
        temp.WriteBaseExeFsFile("main", baseMain);
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        temp.WriteOutputFile("exefs/main", SwShNameFilterMainPatcher.Apply(baseMain, ProjectGame.Sword));
        var service = new SwShProfanityFilterService();

        var result = service.Restore(paths);

        Assert.DoesNotContain(result.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("notInstalled", result.Status.Status);
        Assert.False(File.Exists(OutputMainPath(temp)));
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

    private static string OutputMainPath(TemporarySwShProject temp)
    {
        return Path.Combine(temp.OutputRootPath, "exefs", "main");
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
