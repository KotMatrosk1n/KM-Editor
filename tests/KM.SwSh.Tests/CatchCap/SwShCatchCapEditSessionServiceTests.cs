// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.CatchCap;
using KM.SwSh.Tests.ExeFs;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.CatchCap;

public sealed class SwShCatchCapEditSessionServiceTests
{
    private const ulong SwordTitleId = 0x0100ABF008968000;
    private const ulong ShieldTitleId = 0x01008DB008C2C000;
    private const string SelectedGameDiagnostic = "Catch Cap Editor requires Pokemon Sword or Pokemon Shield to be selected before it can load.";

    [Fact]
    public void MissingSelectedGameBlocksCapStagingReviewAndApply()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths;
        var service = new SwShCatchCapEditSessionService();

        var stage = service.StageCaps(
            paths,
            DefaultCaps().Select((cap, badgeCount) => new SwShCatchCapSelection(badgeCount, cap)).ToArray(),
            session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Equal(SwShWorkflowAvailability.Disabled, stage.Workflow.Summary.Availability);
        Assert.Contains(stage.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message == SelectedGameDiagnostic
            && diagnostic.Expected == "Selected Pokemon Sword or Pokemon Shield project");
        Assert.Empty(stage.Session.PendingEdits);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void MissingSelectedGameBlocksUninstallStagingReviewAndApply()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths;
        var baseBytes = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var installed = SwShCatchCapMainPatcher.Apply(baseBytes, DefaultCaps(), ProjectGame.Sword);
        temp.WriteOutputFile(SwShCatchCapWorkflowService.ExeFsMainPath, installed);
        var service = new SwShCatchCapEditSessionService();

        var stage = service.StageUninstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Equal(SwShWorkflowAvailability.Disabled, stage.Workflow.Summary.Availability);
        Assert.Contains(stage.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message == SelectedGameDiagnostic);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(installed, File.ReadAllBytes(OutputMainPath(paths)));
    }

    [Fact]
    public void InvalidPathsRemainThePrimaryDiagnosticWhenSelectedGameIsMissing()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with
        {
            BaseExeFsPath = Path.Combine(temp.RootPath, "missing-exefs"),
            SelectedGame = null,
        };
        var service = new SwShCatchCapEditSessionService();

        var stage = service.StageCaps(
            paths,
            DefaultCaps().Select((cap, badgeCount) => new SwShCatchCapSelection(badgeCount, cap)).ToArray(),
            session: null);

        var diagnostic = Assert.Single(stage.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "Catch Cap Editor requires valid base RomFS and base ExeFS paths before it can load.",
            diagnostic.Message);
        Assert.Empty(stage.Session.PendingEdits);
    }

    [Fact]
    public void ValidateRejectsForgedIdentitySourcesAndMultipleEdits()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var service = new SwShCatchCapEditSessionService();
        var stage = StageCaps(service, temp.Paths with { SelectedGame = ProjectGame.Sword }, DefaultCaps());
        var canonical = Assert.Single(stage.Session.PendingEdits);
        var forged = canonical with
        {
            Summary = "Forged Catch Cap edit.",
            Sources = [new ProjectFileReference(ProjectFileLayer.Base, SwShCatchCapWorkflowService.ExeFsMainPath)],
        };
        var session = stage.Session with { PendingEdits = [forged, canonical] };

        var validation = service.Validate(temp.Paths with { SelectedGame = ProjectGame.Sword }, session);
        var plan = service.CreateChangePlan(temp.Paths with { SelectedGame = ProjectGame.Sword }, session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("exactly one", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void ValidateRejectsForgedSummaryAndSourceOwnership()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShCatchCapEditSessionService();
        var stage = StageCaps(service, paths, DefaultCaps());
        var canonical = Assert.Single(stage.Session.PendingEdits);
        var forged = canonical with
        {
            Summary = "Forged Catch Cap edit.",
            Sources = [new ProjectFileReference(ProjectFileLayer.Base, SwShCatchCapWorkflowService.ExeFsMainPath)],
        };

        var validation = service.Validate(paths, stage.Session with { PendingEdits = [forged] });

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("canonical staged summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("sources do not match", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRejectsDuplicateAndNonCanonicalBadgePayload()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShCatchCapEditSessionService();
        var stage = StageCaps(service, paths, DefaultCaps());
        var edit = Assert.Single(stage.Session.PendingEdits);
        var forged = edit with
        {
            NewValue = "0=20;0=20;1=25;2=30;3=35;4=40;5=45;6=50;7=55;8=100",
        };

        var validation = service.Validate(paths, stage.Session with { PendingEdits = [forged] });

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("more than once", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRejectsForgedUninstallPayload()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseBytes = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        temp.WriteOutputFile(
            SwShCatchCapWorkflowService.ExeFsMainPath,
            SwShCatchCapMainPatcher.Apply(baseBytes, DefaultCaps(), ProjectGame.Sword));
        var service = new SwShCatchCapEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        var edit = Assert.Single(stage.Session.PendingEdits);

        var validation = service.Validate(
            paths,
            stage.Session with { PendingEdits = [edit with { NewValue = "True" }] });

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("exactly true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyRejectsReviewedPayloadDriftWithSameSessionId()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShCatchCapEditSessionService();
        var firstStage = StageCaps(service, paths, DefaultCaps());
        var reviewedPlan = service.CreateChangePlan(paths, firstStage.Session);
        var changedCaps = DefaultCaps();
        changedCaps[0] = 19;
        var secondStage = StageCaps(service, paths, changedCaps);
        var forgedSession = secondStage.Session with { Id = firstStage.Session.Id };

        var apply = service.ApplyChangePlan(paths, forgedSession, reviewedPlan);

        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void ApplyRejectsBaseSourceDriftAfterReview()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShCatchCapEditSessionService();
        var stage = StageCaps(service, paths, DefaultCaps());
        var reviewedPlan = service.CreateChangePlan(paths, stage.Session);
        var basePath = Path.Combine(paths.BaseExeFsPath!, "main");
        var nso = NsoFile.Parse(File.ReadAllBytes(basePath));
        var text = nso.Text.DecompressedData.ToArray();
        text[0x100] ^= 0x5A;
        File.WriteAllBytes(basePath, nso.Write(textDecompressedData: text));

        var apply = service.ApplyChangePlan(paths, stage.Session, reviewedPlan);

        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void ApplyRejectsSourceSwapBetweenValidationAndHandleAcquisition()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var basePath = Path.Combine(paths.BaseExeFsPath!, "main");
        var service = new SwShCatchCapEditSessionService(
            projectWorkspaceService: null,
            catchCapWorkflowService: null,
            beforeAcquireApplyScope: () => MutateUnownedTextByte(basePath));
        var stage = StageCaps(service, paths, DefaultCaps());
        var reviewedPlan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, reviewedPlan);

        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("handles were being acquired", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void LayeredPlanBindsVanillaBaseEffectiveMainAndPayload()
    {
        using var temp = CreateProject(ProjectGame.Shield);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Shield };
        var baseBytes = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        temp.WriteOutputFile(
            SwShCatchCapWorkflowService.ExeFsMainPath,
            SwShCatchCapMainPatcher.Apply(baseBytes, DefaultCaps(), ProjectGame.Shield));
        var service = new SwShCatchCapEditSessionService();
        var stage = StageCaps(service, paths, DefaultCaps());
        var plan = service.CreateChangePlan(paths, stage.Session);
        var write = Assert.Single(plan.Writes);

        Assert.Contains(write.Sources, source => source.Layer == ProjectFileLayer.Base
            && source.RelativePath == SwShCatchCapWorkflowService.ExeFsMainPath);
        Assert.Contains(write.Sources, source => source.Layer == ProjectFileLayer.Layered
            && source.RelativePath == SwShCatchCapWorkflowService.ExeFsMainPath);
        Assert.Contains(write.Sources, source => source.Layer == ProjectFileLayer.Pending
            && source.RelativePath.StartsWith("pending/catch-cap/caps/", StringComparison.Ordinal));
        Assert.Equal(write.Sources.Count, write.Sources.Distinct().Count());
        Assert.False(string.IsNullOrWhiteSpace(write.SourceFingerprint));
    }

    [Fact]
    public void WorkflowBlocksInstalledEffectiveMainWhenBaseIsNotVanilla()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var basePath = Path.Combine(paths.BaseExeFsPath!, "main");
        var installed = SwShCatchCapMainPatcher.Apply(
            File.ReadAllBytes(basePath),
            DefaultCaps(),
            ProjectGame.Sword);
        File.WriteAllBytes(basePath, installed);
        temp.WriteOutputFile(SwShCatchCapWorkflowService.ExeFsMainPath, installed);

        var workflow = new SwShCatchCapWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Base exefs/main", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedOwnedCapByteRemainsVisibleRepairableAndUninstallable()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseBytes = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var installed = NsoFile.Parse(SwShCatchCapMainPatcher.Apply(
            baseBytes,
            DefaultCaps(),
            ProjectGame.Sword));
        var text = installed.Text.DecompressedData.ToArray();
        text[SwShCatchCapMainPatcher.ExeFsTableOffset + 1] = 200;
        temp.WriteOutputFile(
            SwShCatchCapWorkflowService.ExeFsMainPath,
            installed.Write(textDecompressedData: text));

        var workspace = new ProjectWorkspaceService();
        var workflow = new SwShCatchCapWorkflowService().Load(workspace.Open(paths));
        var service = new SwShCatchCapEditSessionService(workspace);
        var repair = StageCaps(service, paths, DefaultCaps());
        var uninstall = service.StageUninstall(paths, session: null);

        Assert.Equal("installed", workflow.InstallStatus);
        Assert.Equal(200, Assert.Single(workflow.Caps, cap => cap.BadgeCount == 1).LevelCap);
        Assert.DoesNotContain(repair.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Single(repair.Session.PendingEdits);
        Assert.Single(uninstall.Session.PendingEdits);
    }

    [Fact]
    public void UninstallDeletesSemanticallyVanillaOutputAndRefreshesWorkflow()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseBytes = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        temp.WriteOutputFile(
            SwShCatchCapWorkflowService.ExeFsMainPath,
            SwShCatchCapMainPatcher.Apply(baseBytes, DefaultCaps(), ProjectGame.Sword));
        var workspace = new ProjectWorkspaceService();
        var service = new SwShCatchCapEditSessionService(workspace);
        var stage = service.StageUninstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        workspace.ClearMemoryCache();
        var workflow = new SwShCatchCapWorkflowService().Load(workspace.Open(paths));

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(OutputMainPath(paths)));
        Assert.Equal("available", workflow.InstallStatus);
    }

    [Fact]
    public void LatePromotionCollisionPreservesConcurrentOutput()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var concurrentOutput = new byte[] { 0x43, 0x4F, 0x4E, 0x43, 0x55, 0x52, 0x52, 0x45, 0x4E, 0x54 };
        var service = new SwShCatchCapEditSessionService(
            projectWorkspaceService: null,
            catchCapWorkflowService: null,
            beforeVerifiedPromotion: (_, _) =>
            {
                var outputPath = OutputMainPath(paths);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, concurrentOutput);
            });
        var stage = StageCaps(service, paths, DefaultCaps());
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("changed before verified promotion", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(concurrentOutput, File.ReadAllBytes(OutputMainPath(paths)));
    }

    private static SwShCatchCapEditResult StageCaps(
        SwShCatchCapEditSessionService service,
        ProjectPaths paths,
        IReadOnlyList<int> caps)
    {
        var stage = service.StageCaps(
            paths,
            caps.Select((cap, badgeCount) => new SwShCatchCapSelection(badgeCount, cap)).ToArray(),
            session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return stage;
    }

    private static int[] DefaultCaps()
    {
        return [20, 25, 30, 35, 40, 45, 50, 55, 100];
    }

    private static TemporarySwShProject CreateProject(ProjectGame game)
    {
        var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateVanillaCatchCapMain(game));
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(game));
        return temp;
    }

    private static byte[] CreateVanillaCatchCapMain(ProjectGame game)
    {
        var nso = NsoFile.Parse(SwShExeFsPatchTestFixtures.CreateCompatibleNso(game));
        var text = nso.Text.DecompressedData.ToArray();
        var hookOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsHookSiteOffset
            : SwShCatchCapMainPatcher.ExeFsHookSiteOffset;
        var tableOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsTableOffset
            : SwShCatchCapMainPatcher.ExeFsTableOffset;
        var returnOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsReturnOffset
            : SwShCatchCapMainPatcher.ExeFsReturnOffset;
        var runtimeOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsRuntimeHookSiteOffset
            : SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset;
        var runtimeReturnOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsRuntimeReturnOffset
            : SwShCatchCapMainPatcher.ExeFsRuntimeReturnOffset;

        WriteInstruction(text, hookOffset, 0x0B000809);
        WriteInstruction(text, tableOffset, 0xA9417BFD);
        WriteInstruction(text, tableOffset + 4, 0x12001C08);
        WriteInstruction(text, tableOffset + 8, 0x71001D1F);
        WriteInstruction(text, tableOffset + 0x0C, 0x52800C88);
        WriteInstruction(text, tableOffset + 0x10, 0x11005129);
        WriteInstruction(text, tableOffset + 0x14, 0x1A898100);
        WriteInstruction(text, returnOffset, 0xA8C24FF4);
        WriteInstruction(text, returnOffset + 4, 0xD65F03C0);
        WriteInstruction(text, runtimeOffset, 0x0B000809);
        WriteInstruction(text, runtimeOffset + 4, 0x12001C08);
        WriteInstruction(text, runtimeOffset + 8, 0x71001D1F);
        WriteInstruction(text, runtimeOffset + 0x0C, 0x52800C88);
        WriteInstruction(text, runtimeOffset + 0x10, 0x11005129);
        WriteInstruction(text, runtimeOffset + 0x14, 0x1A898100);
        WriteInstruction(text, runtimeReturnOffset, 0xA8C17BFD);
        WriteInstruction(text, runtimeReturnOffset + 4, 0xD65F03C0);
        return nso.Write(textDecompressedData: text);
    }

    private static byte[] CreateNpdm(ProjectGame game)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(
            data.AsSpan(0x290, sizeof(ulong)),
            game == ProjectGame.Shield ? ShieldTitleId : SwordTitleId);
        return data;
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static void MutateUnownedTextByte(string mainPath)
    {
        var nso = NsoFile.Parse(File.ReadAllBytes(mainPath));
        var text = nso.Text.DecompressedData.ToArray();
        text[0x100] ^= 0x5A;
        File.WriteAllBytes(mainPath, nso.Write(textDecompressedData: text));
    }

    private static string OutputMainPath(ProjectPaths paths)
    {
        return Path.Combine(paths.OutputRootPath!, "exefs", "main");
    }
}
