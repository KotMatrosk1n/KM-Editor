// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using KM.SwSh.ShinyRate;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.ShinyRate;

public sealed class SwShShinyRateWorkflowTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const uint VanillaCompareInstruction = 0x6B17033F;
    private const uint VanillaBreakInstruction = 0x54000062;

    private static readonly byte[] FunctionPrelude =
    [
        0xFF, 0x03, 0x06, 0xD1, 0xFC, 0x6F, 0x12, 0xA9,
        0xFA, 0x67, 0x13, 0xA9, 0xF8, 0x5F, 0x14, 0xA9,
        0xF6, 0x57, 0x15, 0xA9, 0xF4, 0x4F, 0x16, 0xA9,
        0xFD, 0x7B, 0x17, 0xA9, 0xFD, 0xC3, 0x05, 0x91,
        0xFA, 0xC6, 0x00, 0xF0,
    ];

    [Theory]
    [InlineData(ProjectGame.Sword, "main.text+0x00D311C0", "main.text+0x00D31488", "main.text+0x00D3148C")]
    [InlineData(ProjectGame.Shield, "main.text+0x00D311F0", "main.text+0x00D314B8", "main.text+0x00D314BC")]
    public void AnalyzeFindsVanillaSelectedGameShinyRate(
        ProjectGame game,
        string expectedFunctionOffset,
        string expectedCompareOffset,
        string expectedBreakOffset)
    {
        var main = CreateSyntheticShinyRateMain(game);

        var analysis = SwShShinyRateMainPatcher.Analyze(main, game);

        Assert.Equal(SwShShinyRateMainKind.Default, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal(expectedFunctionOffset, analysis.FunctionOffsetHex);
        Assert.Equal(expectedCompareOffset, analysis.CompareOffsetHex);
        Assert.Equal(expectedBreakOffset, analysis.BreakOffsetHex);
        Assert.Equal(1, analysis.RollCount);
        Assert.Equal(4096, analysis.OddsDenominator);
    }

    [Fact]
    public void AnalyzeRejectsSelectedGameMismatch()
    {
        var main = CreateSyntheticShinyRateMain(ProjectGame.Sword);

        var analysis = SwShShinyRateMainPatcher.Analyze(main, ProjectGame.Shield);

        Assert.Equal(SwShShinyRateMainKind.GameMismatch, analysis.Kind);
        Assert.Equal(ProjectGame.Sword, analysis.DetectedGame);
        Assert.Contains("will not patch this file", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ApplyFixedRollsPatchesOnlyReservedTextBytes(ProjectGame game)
    {
        var main = CreateSyntheticShinyRateMain(game, extraTextSetup: text =>
        {
            text[0x100] = 0x42;
            text[0xD00000] = 0x24;
        });
        var before = SwShNsoFile.Parse(main);

        var patched = SwShShinyRateMainPatcher.ApplyRate(main, SwShShinyRateMode.FixedRolls, 8, game);
        var after = SwShNsoFile.Parse(patched);
        var analysis = SwShShinyRateMainPatcher.Analyze(patched, game);

        Assert.Equal(SwShShinyRateMainKind.FixedRolls, analysis.Kind);
        Assert.Equal(8, analysis.RollCount);
        Assert.Equal(512, analysis.OddsDenominator);
        Assert.Equal(before.Ro.DecompressedData, after.Ro.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.Equal(0x42, after.Text.DecompressedData[0x100]);
        Assert.Equal(0x24, after.Text.DecompressedData[0xD00000]);
        Assert.All(
            ChangedOffsets(before.Text.DecompressedData, after.Text.DecompressedData),
            changedOffset => Assert.Contains(
                SwShShinyRateMainPatcher.ReservedMainTextRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    [Fact]
    public void ApplyDefaultRestoresVanillaBytesAndPreservesOtherExeFsEdits()
    {
        var main = CreateSyntheticShinyRateMain(ProjectGame.Sword, extraTextSetup: text =>
        {
            text[0x100] = 0x42;
            text[0xD00000] = 0x24;
        });
        var fixedRolls = SwShShinyRateMainPatcher.ApplyRate(
            main,
            SwShShinyRateMode.FixedRolls,
            6,
            ProjectGame.Sword);

        var restored = SwShShinyRateMainPatcher.ApplyRate(
            fixedRolls,
            SwShShinyRateMode.Default,
            rollCount: null,
            ProjectGame.Sword);
        var text = SwShNsoFile.Parse(restored).Text.DecompressedData;
        var analysis = SwShShinyRateMainPatcher.Analyze(restored, ProjectGame.Sword);

        Assert.Equal(SwShShinyRateMainKind.Default, analysis.Kind);
        Assert.Equal(VanillaCompareInstruction, ReadInstruction(text, SwShShinyRateMainPatcher.SwordCompareOffset));
        Assert.Equal(VanillaBreakInstruction, ReadInstruction(text, SwShShinyRateMainPatcher.SwordBreakOffset));
        Assert.Equal(0x42, text[0x100]);
        Assert.Equal(0x24, text[0xD00000]);
        AssertOnlyReservedTextBytesChanged(main, fixedRolls);
        AssertOnlyReservedTextBytesChanged(fixedRolls, restored);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void StageAndApplyShinyRateWritesExeFsMainOutput(ProjectGame game)
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticShinyRateMain(game));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        var paths = temp.Paths with { SelectedGame = game };
        var service = new SwShShinyRateEditSessionService();

        var staged = service.StageRate(paths, "fixed", rollCount: 6, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);
        var outputMain = File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main"));
        var analysis = SwShShinyRateMainPatcher.Analyze(outputMain, game);

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Single(plan.Writes);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShShinyRateWorkflowService.ExeFsMainPath);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShShinyRateWorkflowService.ExeFsMainPath);
        Assert.Equal(SwShShinyRateMainKind.FixedRolls, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal(6, analysis.RollCount);
        Assert.Equal(683, analysis.OddsDenominator);
    }

    [Theory]
    [InlineData("default", null, "default")]
    [InlineData("fixed", 8, "fixed")]
    [InlineData("always", null, "always")]
    public void StageAndApplyShinyRateUsesExistingOutputMainAndPreservesOtherHooks(
        string mode,
        int? rollCount,
        string expectedKind)
    {
        using var temp = TemporarySwShProject.Create();
        var baseMain = CreateSyntheticShinyRateMain(ProjectGame.Sword, extraTextSetup: text =>
        {
            text[0x100] = 0x11;
            text[0xD00000] = 0x22;
        });
        var outputMainWithOtherHooks = SwShShinyRateMainPatcher.ApplyRate(
            CreateSyntheticShinyRateMain(ProjectGame.Sword, extraTextSetup: text =>
            {
                text[0x100] = 0x42;
                text[0xD00000] = 0x24;
            }),
            SwShShinyRateMode.FixedRolls,
            6,
            ProjectGame.Sword);
        temp.WriteBaseExeFsFile("main", baseMain);
        temp.WriteOutputFile("exefs/main", outputMainWithOtherHooks);
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShShinyRateEditSessionService();

        var staged = service.StageRate(paths, mode, rollCount, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);
        var outputMain = File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main"));
        var outputText = SwShNsoFile.Parse(outputMain).Text.DecompressedData;
        var analysis = SwShShinyRateMainPatcher.Analyze(outputMain, ProjectGame.Sword);

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(ExpectedKind(expectedKind), analysis.Kind);
        if (rollCount is not null)
        {
            Assert.Equal(rollCount, analysis.RollCount);
        }
        Assert.Equal(0x42, outputText[0x100]);
        Assert.Equal(0x24, outputText[0xD00000]);
        AssertOnlyReservedTextBytesChanged(outputMainWithOtherHooks, outputMain);
    }

    [Fact]
    public void CalculateRollsForGenThreeOddsClampsToDefaultOneRoll()
    {
        var rollCount = SwShShinyRateMainPatcher.CalculateRollsForTargetDenominator(8192);

        Assert.Equal(1, rollCount);
        Assert.Equal(4096, SwShShinyRateMainPatcher.CalculateOddsDenominator(
            SwShShinyRateMainPatcher.CalculateChance(rollCount)));
    }

    private static byte[] CreateSyntheticShinyRateMain(
        ProjectGame game = ProjectGame.Sword,
        Action<byte[]>? extraTextSetup = null)
    {
        var shift = game == ProjectGame.Shield ? SwShShinyRateMainPatcher.ShieldOffsetDelta : 0;
        var text = new byte[SwShShinyRateMainPatcher.SwordBreakOffset + shift + 0x40];
        Array.Fill(text, (byte)0xCC);
        FunctionPrelude.CopyTo(text.AsSpan(SwShShinyRateMainPatcher.SwordFunctionOffset + shift));
        WriteInstruction(text, SwShShinyRateMainPatcher.SwordCompareOffset + shift, VanillaCompareInstruction);
        WriteInstruction(text, SwShShinyRateMainPatcher.SwordBreakOffset + shift, VanillaBreakInstruction);
        extraTextSetup?.Invoke(text);

        return CreateNso(text, [0x10], [0x20], BuildIdForGame(game));
    }

    private static SwShShinyRateMainKind ExpectedKind(string value)
    {
        return value switch
        {
            "default" => SwShShinyRateMainKind.Default,
            "fixed" => SwShShinyRateMainKind.FixedRolls,
            "always" => SwShShinyRateMainKind.AlwaysShiny,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
    }

    private static void AssertOnlyReservedTextBytesChanged(byte[] beforeMain, byte[] afterMain)
    {
        var before = SwShNsoFile.Parse(beforeMain);
        var after = SwShNsoFile.Parse(afterMain);

        Assert.Equal(before.Ro.DecompressedData, after.Ro.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.All(
            ChangedOffsets(before.Text.DecompressedData, after.Text.DecompressedData),
            changedOffset => Assert.Contains(
                SwShShinyRateMainPatcher.ReservedMainTextRegions(),
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
        var textOffset = SwShNsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), SwShNsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        buildId.CopyTo(output.AsSpan(0x40, 0x20));
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
