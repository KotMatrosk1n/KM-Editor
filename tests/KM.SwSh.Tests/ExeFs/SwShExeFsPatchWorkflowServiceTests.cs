// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.ExeFs;

public sealed class SwShExeFsPatchWorkflowServiceTests
{
    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void LoadReadsSupportedBuildAndReportsExactPatchReadiness(ProjectGame game)
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        temp.WriteBaseExeFsFile("main", SwShExeFsPatchTestFixtures.CreateCompatibleNso(game));
        var paths = temp.Paths with { OutputRootPath = null, SelectedGame = game };
        var project = new ProjectWorkspaceService().Open(paths);

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var patch = Assert.Single(workflow.Patches);
        Assert.Equal(SwShExeFsPatchWorkflowService.MainPatchId, patch.PatchId);
        Assert.Equal("Royal Candy executable patch", patch.Name);
        Assert.Equal("exefs/main", patch.TargetFile);
        Assert.Equal("Executable patch", patch.PatchKind);
        Assert.Equal("available", patch.Status);
        Assert.Contains("only the Royal Candy executable portion", patch.Description, StringComparison.Ordinal);
        Assert.Contains("complete data, script, and shop install lifecycle", patch.Description, StringComparison.Ordinal);
        Assert.Contains(patch.Details, detail => detail.StartsWith("Build ID:", StringComparison.Ordinal));
        Assert.Contains(patch.Details, detail => detail == $"Detected game: {game}");
        Assert.Equal(ProjectFileLayer.Base, patch.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, patch.Provenance.FileState);
        Assert.Equal("exefs/main", patch.Provenance.SourceFile);
        Assert.Equal(3, workflow.Segments.Count);
        Assert.All(workflow.Segments, segment => Assert.Equal("Pass", segment.HashStatus));
        Assert.Contains(workflow.Checks, check => check.Name == "Supported game build" && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.Name == "Selected game route" && check.Status == "Pass");
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Base executable source"
                && check.Status == "Pass"
                && check.Actual == "Base only");
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight"
                && check.Status == "Pass"
                && check.Actual == "Ready");
        Assert.Contains(workflow.Checks, check => check.Name == "Patch code cave" && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.Name == "Allowed consumable upper bound" && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.Name == "Royal Candy immediate scan" && check.Status == "Info");
        Assert.Equal(1, workflow.Stats.TotalPatchCount);
        Assert.Equal(30, workflow.Stats.TotalCheckCount);
        Assert.Equal(28, workflow.Stats.PassCount);
        Assert.Equal(0, workflow.Stats.WarningCount);
        Assert.Equal(0, workflow.Stats.FailCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenExeFsMainIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Empty(workflow.Patches);
        Assert.Empty(workflow.Segments);
        Assert.Empty(workflow.Checks);
        Assert.Equal(0, workflow.Stats.SourceFileCount);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.exefsPatches"
                && diagnostic.Expected == "exefs/main");
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenExeFsMainIsNotNso()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "not-an-nso");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Empty(workflow.Patches);
        Assert.Empty(workflow.Segments);
        Assert.Empty(workflow.Checks);
        Assert.Equal(0, workflow.Stats.SourceFileCount);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.exefsPatches"
                && diagnostic.File == "exefs/main");
    }

    [Fact]
    public void LoadBlocksPatchWhenKnownAnchorDoesNotMatch()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        var main = SwShExeFsPatchTestFixtures.ReplaceTextInstruction(
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword),
            0x00747988,
            0xD503201F);
        temp.WriteBaseExeFsFile("main", main);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        var patch = Assert.Single(workflow.Patches);
        Assert.Equal("blocked", patch.Status);
        Assert.Equal(2, workflow.Stats.FailCount);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "UI check A"
                && check.Status == "Fail"
                && check.Actual == "0xD503201F");
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight"
                && check.Status == "Fail");
    }

    [Fact]
    public void LoadBlocksUnsupportedBuildEvenWhenAllKnownAnchorsMatch()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        temp.WriteBaseExeFsFile(
            "main",
            SwShExeFsPatchTestFixtures.CreateCompatibleNsoWithUnsupportedBuild(ProjectGame.Sword));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", Assert.Single(workflow.Patches).Status);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Supported game build" && check.Status == "Fail");
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight" && check.Status == "Fail");
    }

    [Fact]
    public void LoadBlocksBuildThatDoesNotMatchSelectedGame()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        temp.WriteBaseExeFsFile(
            "main",
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Shield));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", Assert.Single(workflow.Patches).Status);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Supported game build" && check.Status == "Pass");
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Selected game route" && check.Status == "Fail");
    }

    [Fact]
    public void LoadBlocksBadAdjacentBranchThatLegacyChecksDidNotCover()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        var main = SwShExeFsPatchTestFixtures.ReplaceTextInstruction(
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword),
            0x0074798C,
            0xD503201F);
        temp.WriteBaseExeFsFile("main", main);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", Assert.Single(workflow.Patches).Status);
        Assert.Contains(workflow.Checks, check => check.Name == "UI check A" && check.Status == "Pass");
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight"
                && check.Status == "Fail"
                && !string.IsNullOrWhiteSpace(check.Notes));
    }

    [Fact]
    public void LoadBlocksWhenAggregateCodeCaveCapacityIsInsufficient()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        temp.WriteBaseExeFsFile(
            "main",
            SwShExeFsPatchTestFixtures.CreateCompatibleNsoWithoutCodeCaves(ProjectGame.Sword));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", Assert.Single(workflow.Patches).Status);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight"
                && check.Status == "Fail"
                && check.Notes.Contains("code cave", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadRebindsCachedFactsToCurrentLayeredOnlyProvenance()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        var main = SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword);
        temp.WriteBaseExeFsFile("main", main);
        temp.WriteOutputFile("exefs/main", main);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShExeFsPatchWorkflowService();

        var overrideWorkflow = service.Load(new ProjectWorkspaceService().Open(paths));
        File.Delete(Path.Combine(temp.BaseExeFsPath, "main"));
        var layeredOnlyWorkflow = service.Load(new ProjectWorkspaceService().Open(paths));

        var overridePatch = Assert.Single(overrideWorkflow.Patches);
        Assert.Equal(ProjectFileLayer.Layered, overridePatch.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, overridePatch.Provenance.FileState);
        var layeredOnlyPatch = Assert.Single(layeredOnlyWorkflow.Patches);
        Assert.Equal(ProjectFileLayer.Layered, layeredOnlyPatch.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOnly, layeredOnlyPatch.Provenance.FileState);
        Assert.Equal("blocked", layeredOnlyPatch.Status);
        Assert.Contains(
            layeredOnlyWorkflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("staging requires", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadAllowsCompatibleLayeredTextThatDiffersFromVanillaBase()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        var baseMain = SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword);
        var layeredMain = SwShExeFsPatchTestFixtures.ReplaceTextInstruction(
            baseMain,
            0x013AE3AC,
            0x14000000);
        temp.WriteBaseExeFsFile("main", baseMain);
        temp.WriteOutputFile("exefs/main", layeredMain);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("available", Assert.Single(workflow.Patches).Status);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Base executable source"
                && check.Status == "Pass"
                && check.Actual == "Layered override");
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight"
                && check.Status == "Pass"
                && check.Actual == "Ready");
        Assert.Equal(2, workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void LoadBlocksLayeredOverrideWhenBaseIdentityDoesNotMatch()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        temp.WriteBaseExeFsFile(
            "main",
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword));
        temp.WriteOutputFile(
            "exefs/main",
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Shield));
        var paths = temp.Paths with { SelectedGame = null };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", Assert.Single(workflow.Patches).Status);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Base executable source"
                && check.Status == "Fail"
                && check.Actual == "Layered override"
                && check.Notes.Contains("build IDs differ", StringComparison.Ordinal));
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight" && check.Status == "Pass");
    }

    [Fact]
    public void LoadBlocksLayeredOverrideWhenStableHeaderMetadataDoesNotMatch()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        var baseMain = SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword);
        var layeredMain = baseMain.ToArray();
        layeredMain[0x70] ^= 0xFF;
        temp.WriteBaseExeFsFile("main", baseMain);
        temp.WriteOutputFile("exefs/main", layeredMain);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", Assert.Single(workflow.Patches).Status);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Base executable source"
                && check.Status == "Fail"
                && check.Notes.Contains("stable NSO header metadata differs", StringComparison.Ordinal));
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight" && check.Status == "Pass");
    }

    [Fact]
    public void LoadBlocksLayeredOverrideWhenBaseIsNotSafeVanilla()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        var vanilla = SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword);
        temp.WriteBaseExeFsFile(
            "main",
            SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(vanilla, ProjectGame.Sword));
        temp.WriteOutputFile("exefs/main", vanilla);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", Assert.Single(workflow.Patches).Status);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Base executable source"
                && check.Status == "Fail"
                && check.Notes.Contains("safe vanilla", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight" && check.Status == "Pass");
    }

    [Theory]
    [InlineData(NsoFlags.CheckHashText, ".text")]
    [InlineData(NsoFlags.CheckHashRo, ".ro")]
    [InlineData(NsoFlags.CheckHashData, ".data")]
    public void LoadBlocksRequiredSegmentHashMismatch(NsoFlags requiredFlag, string segmentName)
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        var main = SwShExeFsPatchTestFixtures.WithHashCheckFlags(
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword),
            requiredFlag);
        temp.WriteBaseExeFsFile(
            "main",
            SwShExeFsPatchTestFixtures.CorruptHeaderHash(main, segmentName));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", Assert.Single(workflow.Patches).Status);
        Assert.Equal("Fail", workflow.Segments.Single(segment => segment.Name == segmentName).HashStatus);
        Assert.Contains(
            workflow.Checks,
            check => check.Area == segmentName
                && check.Name == "Segment hash"
                && check.Status == "Fail"
                && check.Notes.Contains("enabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight"
                && check.Status == "Fail"
                && check.Notes.Contains("hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadKeepsDisabledSegmentHashMismatchAsWarning()
    {
        using var temp = CreateSupportedProject(ProjectGame.Sword);
        var main = SwShExeFsPatchTestFixtures.CorruptHeaderHash(
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword),
            ".text");
        temp.WriteBaseExeFsFile("main", main);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("warning", Assert.Single(workflow.Patches).Status);
        Assert.Equal("Warning", workflow.Segments.Single(segment => segment.Name == ".text").HashStatus);
        Assert.Contains(
            workflow.Checks,
            check => check.Area == ".text"
                && check.Name == "Segment hash"
                && check.Status == "Warning"
                && check.Notes.Contains("disabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight"
                && check.Status == "Pass"
                && check.Actual == "Ready");
    }

    private static TemporarySwShProject CreateSupportedProject(ProjectGame game)
    {
        var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        return temp;
    }
}
