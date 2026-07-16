// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.FpsPatch;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.FpsPatch;
using KM.SwSh.Tests.Items;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.TypeChart;

public sealed class SwShTypeChartWorkflowTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private static readonly byte[] TypeChartDependenciesBefore = Convert.FromHexString(
        "E84C74FE0C4D74FE084D74FE0C4D74FE0C4D74FE0C4D74FEF84C74FEE04D74FE" +
        "EC4D74FEF44D74FEEC4D74FE084E74FEEC4D74FEEC4D74FEEC4D74FE004E74FE");
    private static readonly byte[] TypeChartDependenciesAfter = Convert.FromHexString(
        "0000000001000000020000000400000008000000100000002000000040000000" +
        "800000000001000000020000000400000008000000100000F85D74FE105E74FE");

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

    [Fact]
    public void MissingSelectedGameBlocksStageReviewAndApply()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticTypeChartMain());
        var service = new SwShTypeChartEditSessionService();
        var values = CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4);

        var stage = service.StageChart(temp.Paths, values, session: null);
        var plan = service.CreateChangePlan(temp.Paths, stage.Session);
        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);

        Assert.Equal(SwShWorkflowAvailability.Disabled, stage.Workflow.Summary.Availability);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Sword or Pokemon Shield", StringComparison.Ordinal));
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(OutputMainPath(temp.Paths)));
    }

    [Fact]
    public void LayeredStageUsesCanonicalIdentityBaseEffectiveAndPayloadSources()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var installedValues = CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4);
        temp.WriteOutputFile(
            SwShTypeChartWorkflowService.ExeFsMainPath,
            SwShTypeChartMainPatcher.ApplyChart(
                File.ReadAllBytes(BaseMainPath(paths)),
                SwShTypeChartWorkflowService.ToGameOrder(installedValues),
                ProjectGame.Sword));
        var service = new SwShTypeChartEditSessionService();
        var values = CreateCustomDisplayValues(attackTypeIndex: 2, defenseTypeIndex: 1, effectiveness: 4);

        var stage = service.StageChart(paths, values, session: null);
        var edit = Assert.Single(stage.Session.PendingEdits);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var write = Assert.Single(plan.Writes);

        Assert.Equal("workflow.typeChart", edit.Domain);
        Assert.Equal("type-chart", edit.RecordId);
        Assert.Equal("effectiveness", edit.Field);
        Assert.Equal("Stage Type Chart effectiveness table.", edit.Summary);
        Assert.Equal(SwShTypeChartMainPatcher.ChartLength * 2, edit.NewValue?.Length);
        Assert.Contains(edit.Sources, source => source.Layer == ProjectFileLayer.Base
            && source.RelativePath == SwShTypeChartWorkflowService.ExeFsMainPath);
        Assert.Contains(edit.Sources, source => source.Layer == ProjectFileLayer.Layered
            && source.RelativePath == SwShTypeChartWorkflowService.ExeFsMainPath);
        Assert.Contains(edit.Sources, source => source.Layer == ProjectFileLayer.Pending
            && source.RelativePath.StartsWith("pending/type-chart/effectiveness/", StringComparison.Ordinal)
            && source.RelativePath.Length == "pending/type-chart/effectiveness/".Length + 64);
        Assert.Equal(edit.Sources, write.Sources);
        Assert.False(string.IsNullOrWhiteSpace(write.SourceFingerprint));
        Assert.Equal(1, stage.Workflow.Stats.SourceFileCount);
        Assert.Equal(1, stage.Workflow.Stats.OutputFileCount);
    }

    [Fact]
    public void ValidateRejectsForgedIdentitySummaryPayloadAndSources()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShTypeChartEditSessionService();
        var values = CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4);
        var stage = service.StageChart(paths, values, session: null);
        var edit = Assert.Single(stage.Session.PendingEdits);
        var changedPayload = $"02{edit.NewValue![2..]}";
        var forgedEdits = new[]
        {
            edit with { Domain = "workflow.other" },
            edit with { RecordId = "other" },
            edit with { Field = "other" },
            edit with { Summary = "Forged Type Chart edit." },
            edit with { NewValue = changedPayload },
            edit with { Sources = edit.Sources.Reverse().ToArray() },
            edit with { Sources = edit.Sources.Skip(1).ToArray() },
        };

        foreach (var forged in forgedEdits)
        {
            var session = stage.Session with { PendingEdits = [forged] };
            var validation = service.Validate(paths, session);
            var plan = service.CreateChangePlan(paths, session);

            Assert.False(validation.IsValid);
            Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Empty(plan.Writes);
        }
    }

    [Fact]
    public void ApplyRejectsReviewedPayloadDriftWithSameSessionId()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShTypeChartEditSessionService();
        var first = service.StageChart(
            paths,
            CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4),
            session: null);
        var reviewedPlan = service.CreateChangePlan(paths, first.Session);
        var changed = service.StageChart(
            paths,
            CreateCustomDisplayValues(attackTypeIndex: 2, defenseTypeIndex: 1, effectiveness: 4),
            first.Session);

        var apply = service.ApplyChangePlan(paths, changed.Session, reviewedPlan);

        Assert.Equal(first.Session.Id, changed.Session.Id);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void ApplyRejectsSourceDriftAfterReview()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShTypeChartEditSessionService();
        var stage = service.StageChart(
            paths,
            CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4),
            session: null);
        var reviewedPlan = service.CreateChangePlan(paths, stage.Session);
        File.WriteAllBytes(BaseMainPath(paths), CreateSyntheticTypeChartMainWithOtherExeFsEdits());

        var apply = service.ApplyChangePlan(paths, stage.Session, reviewedPlan);

        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void StageRefreshesCachedWorkspaceAndPreservesLayeredExeFsChanges()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var workspace = new ProjectWorkspaceService();
        var service = new SwShTypeChartEditSessionService(workspace);
        _ = workspace.Open(paths);
        var priorValues = CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4);
        temp.WriteOutputFile(
            SwShTypeChartWorkflowService.ExeFsMainPath,
            SwShTypeChartMainPatcher.ApplyChart(
                CreateSyntheticTypeChartMainWithOtherExeFsEdits(),
                SwShTypeChartWorkflowService.ToGameOrder(priorValues),
                ProjectGame.Sword));
        var values = CreateCustomDisplayValues(attackTypeIndex: 2, defenseTypeIndex: 1, effectiveness: 4);

        var stage = service.StageChart(paths, values, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        var output = File.ReadAllBytes(OutputMainPath(paths));

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOtherExeFsEditsStillPresent(output);
        Assert.Equal(
            SwShTypeChartWorkflowService.ToGameOrder(values),
            SwShTypeChartMainPatcher.Analyze(output, ProjectGame.Sword).EffectivenessValues);
        Assert.Equal(1, stage.Workflow.Stats.OutputFileCount);
    }

    [Fact]
    public void OutputThatAppearsAfterStageRequiresRestagingAndIsNotOverwritten()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShTypeChartEditSessionService();
        var stage = service.StageChart(
            paths,
            CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4),
            session: null);
        var reviewedPlan = service.CreateChangePlan(paths, stage.Session);
        var concurrentValues = CreateCustomDisplayValues(attackTypeIndex: 2, defenseTypeIndex: 1, effectiveness: 4);
        var concurrentOutput = SwShTypeChartMainPatcher.ApplyChart(
            CreateSyntheticTypeChartMainWithOtherExeFsEdits(),
            SwShTypeChartWorkflowService.ToGameOrder(concurrentValues),
            ProjectGame.Sword);
        temp.WriteOutputFile(SwShTypeChartWorkflowService.ExeFsMainPath, concurrentOutput);

        var apply = service.ApplyChangePlan(paths, stage.Session, reviewedPlan);

        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(concurrentOutput, File.ReadAllBytes(OutputMainPath(paths)));
    }

    [Fact]
    public void VanillaRestoreDeletesSemanticallyBaseEquivalentOutput()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var customValues = CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4);
        temp.WriteOutputFile(
            SwShTypeChartWorkflowService.ExeFsMainPath,
            SwShTypeChartMainPatcher.ApplyChart(
                File.ReadAllBytes(BaseMainPath(paths)),
                SwShTypeChartWorkflowService.ToGameOrder(customValues),
                ProjectGame.Sword));
        var service = new SwShTypeChartEditSessionService();
        var vanilla = SwShTypeChartWorkflowService.ToDisplayOrder(SwShTypeChartMainPatcher.VanillaChartValues);
        var stage = service.StageChart(paths, vanilla, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void VanillaRestoreKeepsOutputThatContainsOtherExeFsChanges()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var customValues = CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4);
        temp.WriteOutputFile(
            SwShTypeChartWorkflowService.ExeFsMainPath,
            SwShTypeChartMainPatcher.ApplyChart(
                CreateSyntheticTypeChartMainWithOtherExeFsEdits(),
                SwShTypeChartWorkflowService.ToGameOrder(customValues),
                ProjectGame.Sword));
        var service = new SwShTypeChartEditSessionService();
        var vanilla = SwShTypeChartWorkflowService.ToDisplayOrder(SwShTypeChartMainPatcher.VanillaChartValues);
        var stage = service.StageChart(paths, vanilla, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        var output = File.ReadAllBytes(OutputMainPath(paths));

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOtherExeFsEditsStillPresent(output);
        Assert.Equal(
            SwShTypeChartMainKind.Vanilla,
            SwShTypeChartMainPatcher.Analyze(output, ProjectGame.Sword).Kind);
    }

    [Fact]
    public void VanillaFromBaseDoesNotCreateRedundantOutput()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShTypeChartEditSessionService();
        var vanilla = SwShTypeChartWorkflowService.ToDisplayOrder(SwShTypeChartMainPatcher.VanillaChartValues);
        var stage = service.StageChart(paths, vanilla, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
        Assert.Equal(0, stage.Workflow.Stats.OutputFileCount);
    }

    [Fact]
    public void WorkflowBlocksModifiedBaseEvenWhenEffectiveMainIsSupported()
    {
        using var temp = TemporarySwShProject.Create();
        var modifiedBaseValues = CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4);
        temp.WriteBaseExeFsFile(
            "main",
            SwShTypeChartMainPatcher.ApplyChart(
                CreateSyntheticTypeChartMain(),
                SwShTypeChartWorkflowService.ToGameOrder(modifiedBaseValues),
                ProjectGame.Sword));
        temp.WriteOutputFile(SwShTypeChartWorkflowService.ExeFsMainPath, CreateSyntheticTypeChartMain());
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShTypeChartWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Base exefs/main", StringComparison.Ordinal));
    }

    [Fact]
    public void LatePromotionCollisionPreservesConcurrentOutput()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var concurrentOutput = new byte[] { 0x43, 0x4F, 0x4E, 0x43, 0x55, 0x52, 0x52, 0x45, 0x4E, 0x54 };
        var service = new SwShTypeChartEditSessionService(
            projectWorkspaceService: null,
            typeChartWorkflowService: null,
            beforeVerifiedPromotion: (_, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutputMainPath(paths))!);
                File.WriteAllBytes(OutputMainPath(paths), concurrentOutput);
            });
        var stage = service.StageChart(
            paths,
            CreateCustomDisplayValues(attackTypeIndex: 1, defenseTypeIndex: 4, effectiveness: 4),
            session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("changed before verified promotion", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(concurrentOutput, File.ReadAllBytes(OutputMainPath(paths)));
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

    private static TemporarySwShProject CreateProject(ProjectGame game)
    {
        var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticTypeChartMain(game));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        return temp;
    }

    private static int[] CreateCustomDisplayValues(
        int attackTypeIndex,
        int defenseTypeIndex,
        int effectiveness)
    {
        var values = SwShTypeChartWorkflowService.ToDisplayOrder(SwShTypeChartMainPatcher.VanillaChartValues);
        values[(attackTypeIndex * SwShTypeChartMainPatcher.TypeCount) + defenseTypeIndex] = effectiveness;
        return values;
    }

    private static string BaseMainPath(ProjectPaths paths)
    {
        return Path.Combine(paths.BaseExeFsPath!, "main");
    }

    private static string OutputMainPath(ProjectPaths paths)
    {
        return Path.Combine(paths.OutputRootPath!, "exefs", "main");
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
        TypeChartDependenciesBefore.CopyTo(
            ro.AsSpan(
                SwShTypeChartMainPatcher.SwordRoChartOffset - TypeChartDependenciesBefore.Length,
                TypeChartDependenciesBefore.Length));
        SwShTypeChartMainPatcher.VanillaChartValues
            .Select(value => checked((byte)value))
            .ToArray()
            .CopyTo(ro.AsSpan(SwShTypeChartMainPatcher.SwordRoChartOffset));
        TypeChartDependenciesAfter.CopyTo(
            ro.AsSpan(
                SwShTypeChartMainPatcher.SwordRoChartOffset + SwShTypeChartMainPatcher.ChartLength,
                TypeChartDependenciesAfter.Length));
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
