// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.GymUniformRemoval;
using Xunit;

namespace KM.SwSh.Tests.GymUniformRemoval;

public sealed class SwShGymUniformRemovalEditSessionServiceSafetyTests
{
    [Theory]
    [InlineData(ProjectGame.Sword, "-sword-")]
    [InlineData(ProjectGame.Shield, "-shield-")]
    public void WorkflowReportsOnlyTheActiveGamesOwnedRangeAndVerifiedSources(
        ProjectGame game,
        string activeGameToken)
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(game);
        var paths = project.Paths with { SelectedGame = game };
        var workspace = new ProjectWorkspaceService();
        var workflowService = new SwShGymUniformRemovalWorkflowService();

        var baseOnly = workflowService.Load(workspace.Open(paths));

        Assert.Equal("available", baseOnly.InstallStatus);
        Assert.False(baseOnly.CanUninstall);
        Assert.Equal(game, baseOnly.DetectedGame);
        Assert.Equal(1, baseOnly.Stats.SourceFileCount);
        Assert.Equal(1, baseOnly.Stats.ReservedMainTextRegionCount);
        Assert.Equal(8, baseOnly.Stats.OwnedByteCount);
        var activeRegion = Assert.Single(baseOnly.ReservedRegions);
        Assert.Contains(activeGameToken, activeRegion.RegionId, StringComparison.Ordinal);

        var baseMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        project.WriteOutputFile(
            SwShGymUniformRemovalMainPatcher.IpsRelativePath(game),
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(baseMain, game));
        workspace.ClearMemoryCache();

        var installed = workflowService.Load(workspace.Open(paths));

        Assert.Equal("installed", installed.InstallStatus);
        Assert.True(installed.CanUninstall);
        Assert.Equal(2, installed.Stats.SourceFileCount);
        Assert.Equal(8, installed.Stats.OwnedByteCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ProjectGame.Scarlet)]
    public void StageInstallRequiresAnExplicitSupportedGame(ProjectGame? selectedGame)
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = selectedGame };

        var stage = new SwShGymUniformRemovalEditSessionService().StageInstall(paths, session: null);

        Assert.Equal("disabled", stage.Workflow.InstallStatus);
        Assert.Equal("notInspected", stage.Workflow.MainHandlerState);
        Assert.Equal("notInspected", stage.Workflow.IpsArtifactState);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(stage.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StageCreatesExactCanonicalInstallAndUninstallSources()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var workspace = new ProjectWorkspaceService();
        var service = new SwShGymUniformRemovalEditSessionService(workspace);
        var targetRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Sword);

        var install = service.StageInstall(paths, session: null);
        var installEdit = Assert.Single(install.Session.PendingEdits);
        Assert.Equal("workflow.gymUniformRemoval", installEdit.Domain);
        Assert.Equal("gym-uniform-removal-v1-install", installEdit.RecordId);
        Assert.Equal("install", installEdit.Field);
        Assert.Equal("true", installEdit.NewValue);
        Assert.Equal("Stage Gym Uniform Removal install.", installEdit.Summary);
        Assert.Equal(
            [
                new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                PendingSource("install"),
                new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath),
            ],
            installEdit.Sources);

        var baseMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        project.WriteOutputFile(
            targetRelativePath,
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(baseMain, ProjectGame.Sword));
        workspace.ClearMemoryCache();

        var uninstall = service.StageUninstall(paths, session: null);
        var uninstallEdit = Assert.Single(uninstall.Session.PendingEdits);
        Assert.Equal("gym-uniform-removal-v1-uninstall", uninstallEdit.RecordId);
        Assert.Equal("uninstall", uninstallEdit.Field);
        Assert.Equal("true", uninstallEdit.NewValue);
        Assert.Equal("Stage Gym Uniform Removal uninstall.", uninstallEdit.Summary);
        Assert.Equal(
            [
                new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                PendingSource("uninstall"),
                new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath),
            ],
            uninstallEdit.Sources);
        var uninstallPlan = service.CreateChangePlan(paths, uninstall.Session);
        var uninstallWrite = Assert.Single(uninstallPlan.Writes);
        Assert.Equal(uninstallEdit.Sources, uninstallWrite.Sources);
        Assert.False(string.IsNullOrWhiteSpace(uninstallWrite.SourceFingerprint));
        Assert.DoesNotContain(
            uninstallWrite.Sources,
            source => source.Layer == ProjectFileLayer.Layered);
    }

    [Fact]
    public void InstallBindsReadableLayeredMainWhileUninstallDoesNot()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var targetRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Sword);
        project.WriteOutputFile(
            "exefs/main",
            GymUniformRemovalTestFixtures.MutateUnownedSemanticBytes(baseMain));
        var workspace = new ProjectWorkspaceService();
        var service = new SwShGymUniformRemovalEditSessionService(workspace);

        var install = service.StageInstall(paths, session: null);
        var installEdit = Assert.Single(install.Session.PendingEdits);
        Assert.Equal(
            [
                new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                new ProjectFileReference(ProjectFileLayer.Layered, "exefs/main"),
                PendingSource("install"),
                new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath),
            ],
            installEdit.Sources);

        project.WriteOutputFile(
            targetRelativePath,
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(baseMain, ProjectGame.Sword));
        workspace.ClearMemoryCache();
        var uninstall = service.StageUninstall(paths, session: null);
        var uninstallEdit = Assert.Single(uninstall.Session.PendingEdits);
        Assert.Equal(
            [
                new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                PendingSource("uninstall"),
                new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath),
            ],
            uninstallEdit.Sources);
        var uninstallPlan = service.CreateChangePlan(paths, uninstall.Session);
        var uninstallWrite = Assert.Single(uninstallPlan.Writes);
        Assert.Equal(uninstallEdit.Sources, uninstallWrite.Sources);
        Assert.False(string.IsNullOrWhiteSpace(uninstallWrite.SourceFingerprint));
        Assert.DoesNotContain(
            uninstallWrite.Sources,
            source => source.Layer == ProjectFileLayer.Layered);
    }

    [Fact]
    public void ValidateRejectsForgedAndDuplicatePendingEdits()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShGymUniformRemovalEditSessionService();
        var staged = service.StageInstall(paths, session: null);
        var canonical = Assert.Single(staged.Session.PendingEdits);
        var forgeries = new[]
        {
            canonical with { Domain = "workflow.forged" },
            canonical with { RecordId = "gym-uniform-removal-forged" },
            canonical with { Field = "uninstall" },
            canonical with { NewValue = "false" },
            canonical with { Summary = "Forged Gym Uniform Removal edit." },
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
    public void ApplyRejectsEveryForgedReviewedPlanField()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShGymUniformRemovalEditSessionService();
        var staged = service.StageInstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var canonical = Assert.Single(plan.Writes);
        var forgedPlans = new[]
        {
            plan with { SessionId = EditSession.Start().Id },
            plan with { Diagnostics = [new ValidationDiagnostic(DiagnosticSeverity.Error, "Forged review state.")] },
            plan with { Writes = [] },
            plan with { Writes = [canonical, canonical] },
            plan with { Writes = [canonical with { TargetRelativePath = "exefs/forged.ips" }] },
            plan with { Writes = [canonical with { Reason = "Different reviewed action." }] },
            plan with { Writes = [canonical with { ReplacesExistingOutput = !canonical.ReplacesExistingOutput }] },
            plan with { Writes = [canonical with { Sources = [new ProjectFileReference(ProjectFileLayer.Base, "exefs/main")] }] },
            plan with { Writes = [canonical with { SourceFingerprint = "FORGED" }] },
        };

        foreach (var forgedPlan in forgedPlans)
        {
            var result = service.ApplyChangePlan(paths, staged.Session, forgedPlan);

            Assert.Empty(result.WrittenFiles);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(
                GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Sword)));
        }
    }

    [Fact]
    public void ApplyRejectsSourceChangedBeforeVerifiedScopeAcquisition()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var basePath = Path.Combine(paths.BaseExeFsPath!, "main");
        var service = new SwShGymUniformRemovalEditSessionService(
            projectWorkspaceService: null,
            gymUniformRemovalWorkflowService: null,
            beforeAcquireApplyScope: () => File.WriteAllBytes(
                basePath,
                GymUniformRemovalTestFixtures.MutateUnownedSemanticBytes(File.ReadAllBytes(basePath))));
        var staged = service.StageInstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);

        var result = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.Empty(result.WrittenFiles);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(
            GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Sword)));
    }

    [Fact]
    public void UninstallOwnedIpsIgnoresAConflictingEffectiveMain()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var conflictingMain = GymUniformRemovalTestFixtures.CreateConflictingMain(ProjectGame.Sword);
        var targetRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Sword);
        project.WriteOutputFile("exefs/main", conflictingMain);
        project.WriteOutputFile(
            targetRelativePath,
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(baseMain, ProjectGame.Sword));
        var workspace = new ProjectWorkspaceService();
        var workflow = new SwShGymUniformRemovalWorkflowService().Load(workspace.Open(paths));
        var service = new SwShGymUniformRemovalEditSessionService(workspace);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.True(workflow.CanUninstall);
        Assert.Equal(3, workflow.Stats.SourceFileCount);

        var staged = service.StageUninstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var result = service.ApplyChangePlan(paths, staged.Session, plan);

        AssertNoErrors(result.Diagnostics);
        Assert.False(File.Exists(
            GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Sword)));
        Assert.Equal(
            conflictingMain,
            File.ReadAllBytes(Path.Combine(paths.OutputRootPath!, "exefs", "main")));
    }

    [Fact]
    public void ConflictingEffectiveMainWithoutIpsReportsInstallBlocked()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        project.WriteOutputFile(
            "exefs/main",
            GymUniformRemovalTestFixtures.CreateConflictingMain(ProjectGame.Sword));

        var workflow = new SwShGymUniformRemovalWorkflowService().Load(
            new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.False(workflow.CanUninstall);
        Assert.Equal("notPresent", workflow.IpsArtifactState);
        Assert.Contains("install or refresh is blocked", workflow.InstallMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Installing creates", workflow.InstallMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", "foreign", "notPresent", 2)]
    [InlineData("foreign", "foreign", "foreign", 3)]
    [InlineData("invalid", "blocked", "invalid", 3)]
    public void ForeignMainAndIpsArtifactStatesUseTheSafeStatusMatrix(
        string artifactKind,
        string expectedStatus,
        string expectedArtifactState,
        int expectedSourceCount)
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        project.WriteOutputFile(
            "exefs/main",
            GymUniformRemovalTestFixtures.CreateForeignMain(ProjectGame.Sword));
        if (!string.Equals(artifactKind, "missing", StringComparison.Ordinal))
        {
            var ips = SwShGymUniformRemovalMainPatcher.CreateIpsPatch(
                baseMain,
                ProjectGame.Sword);
            if (string.Equals(artifactKind, "foreign", StringComparison.Ordinal))
            {
                ips[11] ^= 0x01;
            }
            else
            {
                ips = Encoding.ASCII.GetBytes("invalid-gym-uniform-ips");
            }

            project.WriteOutputFile(
                SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Sword),
                ips);
        }

        var workflow = new SwShGymUniformRemovalWorkflowService().Load(
            new ProjectWorkspaceService().Open(paths));

        Assert.Equal(expectedStatus, workflow.InstallStatus);
        Assert.Equal("foreign", workflow.MainHandlerState);
        Assert.Equal(expectedArtifactState, workflow.IpsArtifactState);
        Assert.False(workflow.CanUninstall);
        Assert.Equal(expectedSourceCount, workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void UninstallOwnedSwordIpsKeepsSwordIdentityWhenLayeredMainIsShield()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var swordBase = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var shieldLayered = GymUniformRemovalTestFixtures.CreateMain(ProjectGame.Shield);
        var swordIpsRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Sword);
        var shieldIpsRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Shield);
        project.WriteOutputFile("exefs/main", shieldLayered);
        project.WriteOutputFile(
            swordIpsRelativePath,
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(swordBase, ProjectGame.Sword));
        project.WriteOutputFile(
            shieldIpsRelativePath,
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(shieldLayered, ProjectGame.Shield));
        var workspace = new ProjectWorkspaceService();
        var workflow = new SwShGymUniformRemovalWorkflowService().Load(workspace.Open(paths));
        var service = new SwShGymUniformRemovalEditSessionService(workspace);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.True(workflow.CanUninstall);
        Assert.Equal(SwShGymUniformRemovalMainPatcher.SwordBuildId, workflow.BuildId);
        Assert.Equal(
            $"main.text+0x{SwShGymUniformRemovalMainPatcher.SwordPatchOffset:X8}",
            workflow.PatchOffsetHex);
        Assert.Equal(ProjectGame.Sword, workflow.DetectedGame);
        Assert.Equal("conflict", workflow.MainHandlerState);
        var activeRegion = Assert.Single(workflow.ReservedRegions);
        Assert.Contains("-sword-", activeRegion.RegionId, StringComparison.Ordinal);
        Assert.DoesNotContain("-shield-", activeRegion.RegionId, StringComparison.Ordinal);

        var staged = service.StageUninstall(paths, session: null);
        var uninstallEdit = Assert.Single(staged.Session.PendingEdits);
        AssertNoErrors(staged.Diagnostics);
        Assert.Equal(
            [
                new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                PendingSource("uninstall"),
                new ProjectFileReference(ProjectFileLayer.Generated, swordIpsRelativePath),
            ],
            uninstallEdit.Sources);
        var plan = service.CreateChangePlan(paths, staged.Session);
        Assert.True(plan.CanApply);
        var result = service.ApplyChangePlan(paths, staged.Session, plan);

        AssertNoErrors(result.Diagnostics);
        Assert.False(File.Exists(
            GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Sword)));
        Assert.True(File.Exists(
            GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Shield)));
        Assert.Equal(
            shieldLayered,
            File.ReadAllBytes(Path.Combine(paths.OutputRootPath!, "exefs", "main")));
    }

    [Fact]
    public void MissingOptionalLayeredMainStillReportsAndRemovesOwnedIps()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var targetRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Sword);
        var layeredMainPath = Path.Combine(paths.OutputRootPath!, "exefs", "main");
        project.WriteOutputFile("exefs/main", baseMain);
        project.WriteOutputFile(
            targetRelativePath,
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(baseMain, ProjectGame.Sword));
        var workspace = new ProjectWorkspaceService();
        var openedWithLayeredMain = workspace.Open(paths);
        File.Delete(layeredMainPath);

        var staleGraphWorkflow = new SwShGymUniformRemovalWorkflowService().Load(openedWithLayeredMain);

        Assert.Equal("blocked", staleGraphWorkflow.InstallStatus);
        Assert.Equal("unreadable", staleGraphWorkflow.MainHandlerState);
        Assert.Equal("current", staleGraphWorkflow.IpsArtifactState);
        Assert.True(staleGraphWorkflow.CanUninstall);
        Assert.Equal(ProjectFileLayer.Layered, staleGraphWorkflow.Provenance.SourceLayer);
        Assert.Equal(2, staleGraphWorkflow.Stats.SourceFileCount);

        var service = new SwShGymUniformRemovalEditSessionService(workspace);
        var staged = service.StageUninstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var result = service.ApplyChangePlan(paths, staged.Session, plan);

        AssertNoErrors(result.Diagnostics);
        Assert.False(File.Exists(
            GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Sword)));
        Assert.False(File.Exists(layeredMainPath));
    }

    [Fact]
    public void UnreadableOptionalLayeredMainDoesNotBlockLegacyIpsUninstall()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var layeredMain = GymUniformRemovalTestFixtures.MutateUnownedSemanticBytes(baseMain);
        var targetRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Sword);
        var currentIps = SwShGymUniformRemovalMainPatcher.CreateIpsPatch(
            baseMain,
            ProjectGame.Sword);
        var legacyIps = currentIps[..^4]
            .Concat(Encoding.ASCII.GetBytes("EOF"))
            .ToArray();
        project.WriteOutputFile("exefs/main", layeredMain);
        project.WriteOutputFile(targetRelativePath, legacyIps);
        var layeredMainPath = Path.Combine(paths.OutputRootPath!, "exefs", "main");
        var workspace = new ProjectWorkspaceService();
        using var heldMain = new FileStream(
            layeredMainPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var workflow = new SwShGymUniformRemovalWorkflowService().Load(workspace.Open(paths));

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Equal("unreadable", workflow.MainHandlerState);
        Assert.Equal("legacy", workflow.IpsArtifactState);
        Assert.True(workflow.CanUninstall);
        Assert.Equal(2, workflow.Stats.SourceFileCount);

        var service = new SwShGymUniformRemovalEditSessionService(workspace);
        var staged = service.StageUninstall(paths, session: null);
        var uninstallEdit = Assert.Single(staged.Session.PendingEdits);
        Assert.Equal(
            [
                new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                PendingSource("uninstall"),
                new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath),
            ],
            uninstallEdit.Sources);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var result = service.ApplyChangePlan(paths, staged.Session, plan);

        AssertNoErrors(result.Diagnostics);
        Assert.False(File.Exists(
            GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Sword)));
        heldMain.Position = 0;
        var preservedMain = new byte[heldMain.Length];
        Assert.Equal(preservedMain.Length, heldMain.Read(preservedMain));
        Assert.Equal(layeredMain, preservedMain);
    }

    [Fact]
    public void EmptyReadableLayeredMainCountsAsAReadSourceAndDoesNotBlockOwnedIpsUninstall()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var targetRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(ProjectGame.Sword);
        project.WriteOutputFile("exefs/main", []);
        project.WriteOutputFile(
            targetRelativePath,
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(baseMain, ProjectGame.Sword));
        var workspace = new ProjectWorkspaceService();
        var workflow = new SwShGymUniformRemovalWorkflowService().Load(workspace.Open(paths));

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Equal("unreadable", workflow.MainHandlerState);
        Assert.Equal("current", workflow.IpsArtifactState);
        Assert.True(workflow.CanUninstall);
        Assert.Equal(3, workflow.Stats.SourceFileCount);

        var service = new SwShGymUniformRemovalEditSessionService(workspace);
        var staged = service.StageUninstall(paths, session: null);
        var uninstallEdit = Assert.Single(staged.Session.PendingEdits);
        Assert.DoesNotContain(
            uninstallEdit.Sources,
            source => source.Layer == ProjectFileLayer.Layered);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var result = service.ApplyChangePlan(paths, staged.Session, plan);

        AssertNoErrors(result.Diagnostics);
        Assert.False(File.Exists(
            GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Sword)));
        Assert.Empty(File.ReadAllBytes(Path.Combine(paths.OutputRootPath!, "exefs", "main")));
    }

    [Fact]
    public void LatePromotionCollisionPreservesConcurrentOutput()
    {
        using var project = GymUniformRemovalTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = project.Paths with { SelectedGame = ProjectGame.Sword };
        var outputPath = GymUniformRemovalTestFixtures.OutputIpsPath(paths, ProjectGame.Sword);
        var concurrentOutput = Encoding.UTF8.GetBytes("concurrent-gym-uniform-output");
        var service = new SwShGymUniformRemovalEditSessionService(
            projectWorkspaceService: null,
            gymUniformRemovalWorkflowService: null,
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
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(outputPath)!,
            "*.km-verified.tmp",
            SearchOption.TopDirectoryOnly));
    }

    private static ProjectFileReference PendingSource(string action)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("true")));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/gym-uniform-removal/{action}/{hash}");
    }

    private static void AssertNoErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
