// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.FpsPatch;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.FpsPatch;
using KM.SwSh.Tests.Items;
using KM.SwSh.TypeChart;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.TypeChart;

public sealed class SwShTypeChartWorkflowTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void AnalyzeFindsVanillaSelectedGameTypeChart(ProjectGame game)
    {
        var main = CreateSyntheticTypeChartMain(game);

        var analysis = SwShTypeChartMainPatcher.Analyze(main, game);

        Assert.Equal(SwShTypeChartMainKind.Vanilla, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal("main.ro+0x00743600", analysis.ChartOffsetHex);
        Assert.Equal(SwShTypeChartMainPatcher.VanillaChartValues, analysis.EffectivenessValues);
    }

    [Fact]
    public void AnalyzeRejectsSelectedGameMismatch()
    {
        var main = CreateSyntheticTypeChartMain(ProjectGame.Sword);

        var analysis = SwShTypeChartMainPatcher.Analyze(main, ProjectGame.Shield);

        Assert.Equal(SwShTypeChartMainKind.GameMismatch, analysis.Kind);
        Assert.Equal(ProjectGame.Sword, analysis.DetectedGame);
        Assert.Contains("will not patch a different game's executable", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ApplyChartPatchesOnlyReservedRoChartBytes(ProjectGame game)
    {
        var main = CreateSyntheticTypeChartMain(game);
        var before = NsoFile.Parse(main);
        var values = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        values[0] = 0;
        values[(1 * SwShTypeChartMainPatcher.TypeCount) + 4] = 2;

        var patched = SwShTypeChartMainPatcher.ApplyChart(main, values, game);
        var after = NsoFile.Parse(patched);
        var analysis = SwShTypeChartMainPatcher.Analyze(patched, game);

        Assert.Equal(SwShTypeChartMainKind.Modified, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal(values, analysis.EffectivenessValues);
        Assert.Equal(before.Text.DecompressedData, after.Text.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.All(
            ChangedOffsets(before.Ro.DecompressedData, after.Ro.DecompressedData),
            changedOffset => Assert.Contains(
                SwShTypeChartMainPatcher.ReservedMainRoRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ApplyVanillaChartRestoresOnlyTypeChartBytesAndPreservesOtherExeFsEdits(ProjectGame game)
    {
        var mainWithOtherEdits = CreateSyntheticTypeChartMainWithOtherExeFsEdits(game);
        var customValues = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        customValues[0] = 0;
        customValues[(1 * SwShTypeChartMainPatcher.TypeCount) + 4] = 2;

        var installed = SwShTypeChartMainPatcher.ApplyChart(mainWithOtherEdits, customValues, game);
        var restored = SwShTypeChartMainPatcher.ApplyChart(
            installed,
            SwShTypeChartMainPatcher.VanillaChartValues,
            game);
        var restoredAnalysis = SwShTypeChartMainPatcher.Analyze(restored, game);

        Assert.Equal(SwShTypeChartMainKind.Vanilla, restoredAnalysis.Kind);
        Assert.Equal(game, restoredAnalysis.DetectedGame);
        AssertOnlyReservedRoBytesChanged(mainWithOtherEdits, installed);
        AssertOnlyReservedRoBytesChanged(installed, restored);
        AssertOtherExeFsEditsStillPresent(restored);
    }

    [Fact]
    public void ApplyChartRejectsIllegalEffectivenessValues()
    {
        var main = CreateSyntheticTypeChartMain();
        var values = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        values[0] = 3;

        Assert.Throws<InvalidDataException>(() =>
            SwShTypeChartMainPatcher.ApplyChart(main, values, ProjectGame.Sword));
    }

    [Fact]
    public void LoadPresentsVanillaChartInDisplayTypeOrder()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticTypeChartMain());
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShTypeChartWorkflowService().Load(project);

        AssertEffectiveness(workflow, attackTypeIndex: 1, defenseTypeIndex: 4, expected: 8); // Fire -> Grass
        AssertEffectiveness(workflow, attackTypeIndex: 1, defenseTypeIndex: 2, expected: 2); // Fire -> Water
        AssertEffectiveness(workflow, attackTypeIndex: 0, defenseTypeIndex: 13, expected: 0); // Normal -> Ghost
        AssertEffectiveness(workflow, attackTypeIndex: 3, defenseTypeIndex: 8, expected: 0); // Electric -> Ground
        AssertEffectiveness(workflow, attackTypeIndex: 6, defenseTypeIndex: 0, expected: 8); // Fighting -> Normal
        AssertEffectiveness(workflow, attackTypeIndex: 17, defenseTypeIndex: 14, expected: 8); // Fairy -> Dragon
        AssertEffectiveness(workflow, attackTypeIndex: 7, defenseTypeIndex: 16, expected: 0); // Poison -> Steel
        AssertEffectiveness(workflow, attackTypeIndex: 13, defenseTypeIndex: 0, expected: 0); // Ghost -> Normal
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void StageAndApplyTypeChartWritesExeFsMainOutput(ProjectGame game)
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticTypeChartMain(game));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        var paths = temp.Paths with { SelectedGame = game };
        var values = SwShTypeChartWorkflowService.ToDisplayOrder(SwShTypeChartMainPatcher.VanillaChartValues);
        values[0] = 0;
        values[(14 * SwShTypeChartMainPatcher.TypeCount) + 17] = 2;
        var expectedGameOrderValues = SwShTypeChartWorkflowService.ToGameOrder(values);
        var service = new SwShTypeChartEditSessionService();

        var staged = service.StageChart(paths, values, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);
        var outputMain = File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main"));
        var analysis = SwShTypeChartMainPatcher.Analyze(outputMain, game);

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Single(plan.Writes);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShTypeChartWorkflowService.ExeFsMainPath);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShTypeChartWorkflowService.ExeFsMainPath);
        Assert.Equal(SwShTypeChartMainKind.Modified, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal(expectedGameOrderValues, analysis.EffectivenessValues);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void StageAndApplyTypeChartPreservesInstalledFpsPatch(ProjectGame game)
    {
        using var temp = TemporarySwShProject.Create();
        var baseMain = CreateSyntheticTypeChartMainWithFpsAnchors(game);
        var fpsMain = SwShFpsMainPatcher.Apply(baseMain, game);
        temp.WriteBaseExeFsFile("main", baseMain);
        temp.WriteOutputFile("exefs/main", fpsMain);
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        var paths = temp.Paths with { SelectedGame = game };
        var values = SwShTypeChartWorkflowService.ToDisplayOrder(SwShTypeChartMainPatcher.VanillaChartValues);
        values[(14 * SwShTypeChartMainPatcher.TypeCount) + 17] = 2;
        var service = new SwShTypeChartEditSessionService();

        var staged = service.StageChart(paths, values, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);
        var outputMain = File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main"));

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(SwShFpsPatchMainKind.Installed, SwShFpsMainPatcher.Analyze(outputMain, game).Kind);
        Assert.Equal(SwShTypeChartMainKind.Modified, SwShTypeChartMainPatcher.Analyze(outputMain, game).Kind);
    }

    private static void AssertEffectiveness(
        SwShTypeChartWorkflow workflow,
        int attackTypeIndex,
        int defenseTypeIndex,
        int expected)
    {
        var cell = Assert.Single(
            workflow.Cells,
            cell => cell.AttackTypeIndex == attackTypeIndex && cell.DefenseTypeIndex == defenseTypeIndex);
        Assert.Equal(expected, cell.Effectiveness);
    }

    private static byte[] CreateSyntheticTypeChartMain(
        ProjectGame game = ProjectGame.Sword,
        int? minimumTextLength = null,
        Action<byte[]>? extraTextSetup = null)
    {
        var text = new byte[Math.Max(0x40, minimumTextLength ?? 0)];
        for (var index = 0; index < text.Length; index++)
        {
            text[index] = (byte)(0x80 + index);
        }

        var ro = new byte[SwShTypeChartMainPatcher.SwordRoChartOffset + SwShTypeChartMainPatcher.ChartLength + 0x40];
        var data = Enumerable.Range(0, 0x20).Select(index => (byte)(0x20 + index)).ToArray();
        Array.Fill(ro, (byte)0xCC);
        SwShTypeChartMainPatcher.VanillaChartValues
            .Select(value => checked((byte)value))
            .ToArray()
            .CopyTo(ro.AsSpan(SwShTypeChartMainPatcher.SwordRoChartOffset));
        extraTextSetup?.Invoke(text);

        return CreateNso(text, ro, data, BuildIdForGame(game));
    }

    private static byte[] CreateSyntheticTypeChartMainWithFpsAnchors(ProjectGame game)
    {
        return CreateSyntheticTypeChartMain(
            game,
            SwShFpsMainTestAnchors.RequiredTextLength,
            text => SwShFpsMainTestAnchors.WriteVanilla(text, game));
    }

    private static byte[] CreateSyntheticTypeChartMainWithOtherExeFsEdits(ProjectGame game = ProjectGame.Sword)
    {
        var nso = NsoFile.Parse(CreateSyntheticTypeChartMain(game));
        var text = nso.Text.DecompressedData.ToArray();
        var ro = nso.Ro.DecompressedData.ToArray();
        var data = nso.Data.DecompressedData.ToArray();
        text[0x10] = 0x42;
        ro[0x100] = 0x24;
        data[0x08] = 0x66;
        return nso.Write(textDecompressedData: text, roDecompressedData: ro, dataDecompressedData: data);
    }

    private static void AssertOnlyReservedRoBytesChanged(byte[] beforeMain, byte[] afterMain)
    {
        var before = NsoFile.Parse(beforeMain);
        var after = NsoFile.Parse(afterMain);

        Assert.Equal(before.Text.DecompressedData, after.Text.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.All(
            ChangedOffsets(before.Ro.DecompressedData, after.Ro.DecompressedData),
            changedOffset => Assert.Contains(
                SwShTypeChartMainPatcher.ReservedMainRoRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    private static void AssertOtherExeFsEditsStillPresent(byte[] main)
    {
        var nso = NsoFile.Parse(main);
        Assert.Equal(0x42, nso.Text.DecompressedData[0x10]);
        Assert.Equal(0x24, nso.Ro.DecompressedData[0x100]);
        Assert.Equal(0x66, nso.Data.DecompressedData[0x08]);
    }

    private static int[] ChangedOffsets(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        return Enumerable.Range(0, before.Length)
            .Where(index => before[index] != after[index])
            .ToArray();
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
