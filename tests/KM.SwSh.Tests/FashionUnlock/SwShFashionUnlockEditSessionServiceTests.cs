// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.FashionUnlock;
using Xunit;

namespace KM.SwSh.Tests.FashionUnlock;

public sealed class SwShFashionUnlockEditSessionServiceTests
{
    [Theory]
    [InlineData(ProjectGame.Sword, "-sword-")]
    [InlineData(ProjectGame.Shield, "-shield-")]
    public void WorkflowReportsOnlyTheActiveGamesOwnedRangesAndVerifiedSources(
        ProjectGame game,
        string activeGameToken)
    {
        using var project = FashionUnlockTestFixtures.CreateProject(game);
        var paths = project.Paths with { SelectedGame = game };
        var workspace = new ProjectWorkspaceService();
        var workflowService = new SwShFashionUnlockWorkflowService();

        var baseOnly = workflowService.Load(workspace.Open(paths));

        Assert.Equal("available", baseOnly.InstallStatus);
        Assert.False(baseOnly.CanUninstall);
        Assert.Equal(1, baseOnly.Stats.SourceFileCount);
        Assert.Equal(2, baseOnly.Stats.ReservedMainTextRegionCount);
        Assert.Equal(16, baseOnly.Stats.OwnedByteCount);
        Assert.All(baseOnly.ReservedRegions, region =>
            Assert.Contains(activeGameToken, region.RegionId, StringComparison.Ordinal));

        var baseBytes = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        project.WriteOutputFile(
            SwShFashionUnlockWorkflowService.ExeFsMainPath,
            SwShFashionUnlockMainPatcher.Apply(baseBytes, game));
        workspace.ClearMemoryCache();

        var layered = workflowService.Load(workspace.Open(paths));

        Assert.Equal("installed", layered.InstallStatus);
        Assert.True(layered.CanUninstall);
        Assert.Equal(2, layered.Stats.SourceFileCount);
        Assert.Equal(16, layered.Stats.OwnedByteCount);
    }

    [Fact]
    public void WorkflowBlocksInvalidBaseEvenWhenLayeredSourceIsValid()
    {
        var vanilla = FashionUnlockTestFixtures.CreateMain(ProjectGame.Sword);
        var installedBase = SwShFashionUnlockMainPatcher.Apply(vanilla, ProjectGame.Sword);
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword, installedBase);
        project.WriteOutputFile(SwShFashionUnlockWorkflowService.ExeFsMainPath, vanilla);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var workspace = new ProjectWorkspaceService();
        var workflow = new SwShFashionUnlockWorkflowService().Load(workspace.Open(paths));
        var stage = new SwShFashionUnlockEditSessionService(workspace).StageInstall(paths, session: null);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.False(workflow.CanUninstall);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("vanilla Fashion Unlock source", StringComparison.Ordinal));
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GameMismatchUsesSelectedGamesRangesAndCountsNoVerifiedSource()
    {
        using var project = FashionUnlockTestFixtures.CreateProject(
            ProjectGame.Sword,
            FashionUnlockTestFixtures.CreateMain(ProjectGame.Shield));
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var workflow = new SwShFashionUnlockWorkflowService().Load(
            new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Equal(ProjectGame.Shield, workflow.DetectedGame);
        Assert.Equal(0, workflow.Stats.SourceFileCount);
        Assert.Equal(16, workflow.Stats.OwnedByteCount);
        Assert.All(workflow.ReservedRegions, region =>
            Assert.Contains("-sword-", region.RegionId, StringComparison.Ordinal));
        Assert.DoesNotContain(workflow.ReservedRegions, region =>
            region.RegionId.Contains("-shield-", StringComparison.Ordinal));
    }

    [Fact]
    public void StageRejectsMissingSelectedGame()
    {
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = null };
        var stage = new SwShFashionUnlockEditSessionService().StageInstall(paths, session: null);

        Assert.Equal("disabled", stage.Workflow.InstallStatus);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(stage.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Sword or Pokemon Shield", StringComparison.Ordinal));
    }

    [Fact]
    public void StageCreatesExactCanonicalInstallAndUninstallSources()
    {
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var workspace = new ProjectWorkspaceService();
        var service = new SwShFashionUnlockEditSessionService(workspace);

        var install = service.StageInstall(paths, session: null);
        var installEdit = Assert.Single(install.Session.PendingEdits);
        Assert.Equal("workflow.fashionUnlock", installEdit.Domain);
        Assert.Equal("fashion-unlock-v1-install", installEdit.RecordId);
        Assert.Equal("install", installEdit.Field);
        Assert.Equal("true", installEdit.NewValue);
        Assert.Equal("Stage Fashion Unlock install.", installEdit.Summary);
        Assert.Equal(
            [
                new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                PendingSource("install"),
            ],
            installEdit.Sources);

        var baseBytes = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        project.WriteOutputFile(
            SwShFashionUnlockWorkflowService.ExeFsMainPath,
            SwShFashionUnlockMainPatcher.Apply(baseBytes, ProjectGame.Sword));
        workspace.ClearMemoryCache();
        var uninstall = service.StageUninstall(paths, session: null);
        var uninstallEdit = Assert.Single(uninstall.Session.PendingEdits);
        Assert.Equal("fashion-unlock-v1-uninstall", uninstallEdit.RecordId);
        Assert.Equal("uninstall", uninstallEdit.Field);
        Assert.Equal("true", uninstallEdit.NewValue);
        Assert.Equal("Stage Fashion Unlock uninstall.", uninstallEdit.Summary);
        Assert.Equal(
            [
                new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                new ProjectFileReference(ProjectFileLayer.Layered, "exefs/main"),
                PendingSource("uninstall"),
            ],
            uninstallEdit.Sources);
    }

    [Fact]
    public void ValidateRejectsForgedAndDuplicatePendingEdits()
    {
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShFashionUnlockEditSessionService();
        var staged = service.StageInstall(paths, session: null);
        var canonical = Assert.Single(staged.Session.PendingEdits);
        var forgeries = new[]
        {
            canonical with { Field = "uninstall" },
            canonical with { NewValue = "false" },
            canonical with { Summary = "Forged Fashion Unlock edit." },
            canonical with { Sources = [new ProjectFileReference(ProjectFileLayer.Base, "exefs/main")] },
        };

        foreach (var forged in forgeries)
        {
            var session = staged.Session with { PendingEdits = [forged] };
            var validation = service.Validate(paths, session);
            var plan = service.CreateChangePlan(paths, session);
            Assert.False(validation.IsValid);
            Assert.Empty(plan.Writes);
            Assert.Contains(validation.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        }

        var duplicateSession = staged.Session with { PendingEdits = [canonical, canonical] };
        var duplicateValidation = service.Validate(paths, duplicateSession);
        var duplicatePlan = service.CreateChangePlan(paths, duplicateSession);
        Assert.False(duplicateValidation.IsValid);
        Assert.Empty(duplicatePlan.Writes);
        Assert.Contains(duplicateValidation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("exactly one", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyRejectsEveryForgedReviewedWriteField()
    {
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShFashionUnlockEditSessionService();
        var staged = service.StageInstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var canonical = Assert.Single(plan.Writes);
        var forgedWrites = new[]
        {
            canonical with { Reason = "Different reviewed action." },
            canonical with { ReplacesExistingOutput = !canonical.ReplacesExistingOutput },
            canonical with { Sources = [new ProjectFileReference(ProjectFileLayer.Base, "exefs/main")] },
            canonical with { SourceFingerprint = "FORGED" },
        };

        foreach (var forgedWrite in forgedWrites)
        {
            var result = service.ApplyChangePlan(
                paths,
                staged.Session,
                plan with { Writes = [forgedWrite] });
            Assert.Empty(result.WrittenFiles);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(FashionUnlockTestFixtures.OutputMainPath(paths)));
        }
    }

    [Fact]
    public void ApplyRejectsSourceChangedBeforeVerifiedScopeAcquisition()
    {
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var basePath = Path.Combine(paths.BaseExeFsPath!, "main");
        var service = new SwShFashionUnlockEditSessionService(
            projectWorkspaceService: null,
            fashionUnlockWorkflowService: null,
            beforeAcquireApplyScope: () => File.WriteAllBytes(
                basePath,
                FashionUnlockTestFixtures.MutateUnownedSemanticBytes(File.ReadAllBytes(basePath))));
        var staged = service.StageInstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);

        var result = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.Empty(result.WrittenFiles);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(FashionUnlockTestFixtures.OutputMainPath(paths)));
    }

    [Fact]
    public void LatePromotionCollisionPreservesConcurrentOutput()
    {
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var outputPath = FashionUnlockTestFixtures.OutputMainPath(paths);
        var concurrentOutput = Encoding.UTF8.GetBytes("concurrent-fashion-output");
        var service = new SwShFashionUnlockEditSessionService(
            projectWorkspaceService: null,
            fashionUnlockWorkflowService: null,
            beforeVerifiedPromotion: (_, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, concurrentOutput);
            });
        var staged = service.StageInstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);

        var result = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.Empty(result.WrittenFiles);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(concurrentOutput, File.ReadAllBytes(outputPath));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void InstallThenUninstallRefreshesCacheAndDeletesSoleOutput(ProjectGame game)
    {
        using var project = FashionUnlockTestFixtures.CreateProject(game);
        var paths = project.Paths with { SelectedGame = game };
        var workspace = new ProjectWorkspaceService();
        var service = new SwShFashionUnlockEditSessionService(workspace);

        var installStage = service.StageInstall(paths, session: null);
        var installPlan = service.CreateChangePlan(paths, installStage.Session);
        var install = service.ApplyChangePlan(paths, installStage.Session, installPlan);
        AssertNoErrors(install.Diagnostics);
        Assert.True(File.Exists(FashionUnlockTestFixtures.OutputMainPath(paths)));

        var uninstallStage = service.StageUninstall(paths, session: null);
        var uninstallPlan = service.CreateChangePlan(paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(paths, uninstallStage.Session, uninstallPlan);

        AssertNoErrors(uninstall.Diagnostics);
        Assert.False(File.Exists(FashionUnlockTestFixtures.OutputMainPath(paths)));
        var workflow = new SwShFashionUnlockWorkflowService().Load(workspace.Open(paths));
        Assert.Equal("available", workflow.InstallStatus);
        Assert.False(workflow.CanUninstall);
    }

    [Fact]
    public void UninstallDeletesSemanticallyEquivalentNonidenticalOutput()
    {
        var baseMain = FashionUnlockTestFixtures.CreateSemanticallyStableNoncanonicalBase(ProjectGame.Sword);
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword, baseMain);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShFashionUnlockEditSessionService();
        var installStage = service.StageInstall(paths, session: null);
        var installPlan = service.CreateChangePlan(paths, installStage.Session);
        var install = service.ApplyChangePlan(paths, installStage.Session, installPlan);
        AssertNoErrors(install.Diagnostics);

        var outputPath = FashionUnlockTestFixtures.OutputMainPath(paths);
        Assert.False(File.ReadAllBytes(outputPath).SequenceEqual(baseMain));
        var uninstallStage = service.StageUninstall(paths, session: null);
        var uninstallPlan = service.CreateChangePlan(paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(paths, uninstallStage.Session, uninstallPlan);

        AssertNoErrors(uninstall.Diagnostics);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void UninstallPreservesEveryUnownedEffectiveEdit()
    {
        using var project = FashionUnlockTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShFashionUnlockEditSessionService();
        var installStage = service.StageInstall(paths, session: null);
        var installPlan = service.CreateChangePlan(paths, installStage.Session);
        AssertNoErrors(service.ApplyChangePlan(paths, installStage.Session, installPlan).Diagnostics);
        var outputPath = FashionUnlockTestFixtures.OutputMainPath(paths);
        var modified = FashionUnlockTestFixtures.MutateUnownedSemanticBytes(File.ReadAllBytes(outputPath));
        File.WriteAllBytes(outputPath, modified);

        var uninstallStage = service.StageUninstall(paths, session: null);
        var uninstallPlan = service.CreateChangePlan(paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(paths, uninstallStage.Session, uninstallPlan);

        AssertNoErrors(uninstall.Diagnostics);
        Assert.True(File.Exists(outputPath));
        var restored = NsoFile.Parse(File.ReadAllBytes(outputPath));
        var expected = NsoFile.Parse(modified);
        Assert.Equal(expected.Text.DecompressedData[0x100], restored.Text.DecompressedData[0x100]);
        Assert.Equal(expected.Ro.DecompressedData[0], restored.Ro.DecompressedData[0]);
        Assert.Equal(expected.Data.DecompressedData[0], restored.Data.DecompressedData[0]);
        Assert.Equal(
            SwShFashionUnlockInstallKind.NotInstalled,
            SwShFashionUnlockMainPatcher.Analyze(File.ReadAllBytes(outputPath), ProjectGame.Sword).Kind);
    }

    private static ProjectFileReference PendingSource(string action)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("true")));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/fashion-unlock/{action}/{hash}");
    }

    private static void AssertNoErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
