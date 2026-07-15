// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.ExeFs;

public sealed class SwShExeFsPatchEditSessionServiceTests
{
    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void StagePlanAndApplyUseTheVerifiedGameLayoutAndPreserveSourceData(ProjectGame game)
    {
        using var temp = CreateEditableProject(game);
        var sourceBytes = SwShExeFsPatchTestFixtures.ReplaceTextInstruction(
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(game),
            0x100,
            0x11223344);
        temp.WriteBaseExeFsFile("main", sourceBytes);
        var paths = temp.Paths with { SelectedGame = game };
        var service = new SwShExeFsPatchEditSessionService();

        var staged = service.StagePatch(paths, SwShExeFsPatchWorkflowService.MainPatchId, session: null);
        var validation = service.Validate(paths, staged.Session);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        var plannedWrite = Assert.Single(plan.Writes);
        Assert.False(string.IsNullOrWhiteSpace(plannedWrite.SourceFingerprint));
        Assert.Contains("Exp Candy fixed-amount bypass", plannedWrite.Reason, StringComparison.Ordinal);
        Assert.Contains("allowed-consumable routing", plannedWrite.Reason, StringComparison.Ordinal);
        Assert.Contains("virtual inventory", plannedWrite.Reason, StringComparison.Ordinal);
        Assert.Contains("infinite use", plannedWrite.Reason, StringComparison.Ordinal);
        Assert.Contains("UI routing", plannedWrite.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("exefs/main", Assert.Single(apply.WrittenFiles).RelativePath);

        var basePath = Path.Combine(temp.BaseExeFsPath, "main");
        var outputPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        Assert.Equal(sourceBytes, File.ReadAllBytes(basePath));

        var source = NsoFile.Parse(sourceBytes);
        var outputBytes = File.ReadAllBytes(outputPath);
        var output = NsoFile.Parse(outputBytes);
        Assert.True(source.Flags.HasFlag(NsoFlags.CompressedRo));
        Assert.True(source.Flags.HasFlag(NsoFlags.CompressedData));
        Assert.Equal(
            BinaryPrimitives.ReadUInt32LittleEndian(source.Text.DecompressedData.AsSpan(0x100, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(output.Text.DecompressedData.AsSpan(0x100, 4)));
        Assert.Equal(source.Ro.DecompressedData, output.Ro.DecompressedData);
        Assert.Equal(source.Data.DecompressedData, output.Data.DecompressedData);
        Assert.Equal(source.Ro.Hash, output.Ro.Hash);
        Assert.Equal(source.Data.Hash, output.Data.Hash);
        Assert.Equal(source.Ro.CompressedData, output.Ro.CompressedData);
        Assert.Equal(source.Data.CompressedData, output.Data.CompressedData);
        Assert.True(SwShExeFsMainComparison.StableHeaderBytesMatch(source.RawHeader, output.RawHeader));
        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.Unlimited,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(outputBytes, game).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void NullSelectedGameInfersSupportedBuildThroughLoadStagePlanAndApply(ProjectGame game)
    {
        using var temp = CreateEditableProject(game);
        temp.WriteBaseExeFsFile("main", SwShExeFsPatchTestFixtures.CreateCompatibleNso(game));
        var paths = temp.Paths with { SelectedGame = null };
        var service = new SwShExeFsPatchEditSessionService();

        var loaded = new SwShExeFsPatchWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        var staged = service.StagePatch(paths, SwShExeFsPatchWorkflowService.MainPatchId, session: null);
        var validation = service.Validate(paths, staged.Session);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.Equal("available", Assert.Single(loaded.Patches).Status);
        Assert.Contains(
            loaded.Checks,
            check => check.Name == "Selected game route"
                && check.Status == "Pass"
                && check.Notes.Contains("inferred", StringComparison.OrdinalIgnoreCase));
        Assert.Single(staged.Session.PendingEdits);
        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main"));
        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.Unlimited,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(outputBytes, game).Kind);
    }

    [Fact]
    public void ValidateRejectsForgedPendingMetadataAndSourceProvenance()
    {
        using var temp = CreateEditableProject(ProjectGame.Sword);
        temp.WriteBaseExeFsFile("main", SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShExeFsPatchEditSessionService();
        var staged = service.StagePatch(paths, SwShExeFsPatchWorkflowService.MainPatchId, session: null);
        var canonical = Assert.Single(staged.Session.PendingEdits);
        PendingEdit[] forgedEdits =
        [
            canonical with { Domain = "workflow.items" },
            canonical with { Field = "targetFile" },
            canonical with { RecordId = "forged-patch" },
            canonical with { Summary = "Stage an unrelated executable patch." },
            canonical with { NewValue = "exefs/forged" },
            canonical with { Sources = [] },
            canonical with
            {
                Sources =
                [
                    new ProjectFileReference(ProjectFileLayer.Base, "exefs/main"),
                    new ProjectFileReference(ProjectFileLayer.Base, "exefs/other"),
                ],
            },
            canonical with { Sources = [new ProjectFileReference(ProjectFileLayer.Layered, "exefs/main")] },
            canonical with { Sources = [new ProjectFileReference(ProjectFileLayer.Base, "exefs/forged")] },
        ];

        foreach (var forgedEdit in forgedEdits)
        {
            var forgedSession = staged.Session with { PendingEdits = [forgedEdit] };

            var validation = service.Validate(paths, forgedSession);

            Assert.False(validation.IsValid);
            Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
    }

    [Fact]
    public void StageRejectsLayeredOnlyExecutableWhileLeavingItInspectable()
    {
        using var temp = CreateEditableProject(ProjectGame.Sword);
        temp.WriteOutputFile(
            "exefs/main",
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShExeFsPatchEditSessionService();

        var staged = service.StagePatch(paths, SwShExeFsPatchWorkflowService.MainPatchId, session: null);

        Assert.Empty(staged.Session.PendingEdits);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOnly, Assert.Single(staged.Workflow.Patches).Provenance.FileState);
        Assert.Equal("blocked", Assert.Single(staged.Workflow.Patches).Status);
        Assert.Contains(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StageRejectsExistingUnlimitedAndStoryLimitsInstallations(bool storyLimits)
    {
        using var temp = CreateEditableProject(ProjectGame.Sword);
        var source = SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword);
        var installed = storyLimits
            ? SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
                source,
                [new SwShRoyalCandyStoryLevelCap(10, 0x1234567890ABCDEF, "Test milestone")],
                ProjectGame.Sword)
            : SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(source, ProjectGame.Sword);
        temp.WriteBaseExeFsFile("main", installed);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShExeFsPatchEditSessionService();

        var staged = service.StagePatch(paths, SwShExeFsPatchWorkflowService.MainPatchId, session: null);

        Assert.Empty(staged.Session.PendingEdits);
        Assert.Equal("blocked", Assert.Single(staged.Workflow.Patches).Status);
        Assert.Contains(
            staged.Workflow.Checks,
            check => check.Name == "Exact Royal Candy patch preflight" && check.Status == "Fail");
        Assert.Contains(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyRejectsReviewedPlanWhenNonTargetPlanSemanticsAreForged()
    {
        using var temp = CreateEditableProject(ProjectGame.Sword);
        temp.WriteBaseExeFsFile("main", SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShExeFsPatchEditSessionService();
        var staged = service.StagePatch(paths, SwShExeFsPatchWorkflowService.MainPatchId, session: null);
        var currentPlan = service.CreateChangePlan(paths, staged.Session);
        var currentWrite = Assert.Single(currentPlan.Writes);
        var forgedPlan = currentPlan with
        {
            Writes = [currentWrite with { Reason = $"{currentWrite.Reason} Forged." }],
        };

        var apply = service.ApplyChangePlan(paths, staged.Session, forgedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "exefs", "main")));
    }

    [Fact]
    public void LayeredChangePlanFingerprintsBaseAndLayeredSourcesAndRejectsBaseDrift()
    {
        using var temp = CreateEditableProject(ProjectGame.Sword);
        var baseMain = SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword);
        var layeredMain = SwShExeFsPatchTestFixtures.ReplaceTextInstruction(
            baseMain,
            0x013AE3AC,
            0x14000000);
        temp.WriteBaseExeFsFile("main", baseMain);
        temp.WriteOutputFile("exefs/main", layeredMain);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShExeFsPatchEditSessionService();
        var staged = service.StagePatch(paths, SwShExeFsPatchWorkflowService.MainPatchId, session: null);
        var reviewedPlan = service.CreateChangePlan(paths, staged.Session);
        var reviewedWrite = Assert.Single(reviewedPlan.Writes);

        Assert.True(reviewedPlan.CanApply);
        Assert.False(string.IsNullOrWhiteSpace(reviewedWrite.SourceFingerprint));
        Assert.Equal(2, reviewedWrite.Sources.Count);
        Assert.Contains(
            reviewedWrite.Sources,
            source => source.Layer == ProjectFileLayer.Base && source.RelativePath == "exefs/main");
        Assert.Contains(
            reviewedWrite.Sources,
            source => source.Layer == ProjectFileLayer.Layered && source.RelativePath == "exefs/main");

        temp.WriteBaseExeFsFile(
            "main",
            SwShExeFsPatchTestFixtures.ReplaceTextInstruction(baseMain, 0x104, 0x11223344));

        var apply = service.ApplyChangePlan(paths, staged.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(layeredMain, File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main")));
    }

    [Fact]
    public void OutputVerifierRejectsChangedOpaqueHeaderMetadata()
    {
        var source = SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword);
        var output = SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(source, ProjectGame.Sword);
        output[0x70] ^= 0xFF;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShExeFsRoyalCandyMainPatcher.VerifyBasePatchOutput(source, output, ProjectGame.Sword));

        Assert.Contains("header metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyAndOutputVerifierRejectRequiredTextHashCorruption()
    {
        var source = SwShExeFsPatchTestFixtures.WithHashCheckFlags(
            SwShExeFsPatchTestFixtures.CreateCompatibleNso(ProjectGame.Sword),
            NsoFlags.CheckHashText);
        var corruptSource = SwShExeFsPatchTestFixtures.CorruptHeaderHash(source, ".text");

        var applyException = Assert.Throws<InvalidDataException>(() =>
            SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(corruptSource, ProjectGame.Sword));

        Assert.Contains("required NSO header hash", applyException.Message, StringComparison.OrdinalIgnoreCase);

        var output = SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(source, ProjectGame.Sword);
        var corruptOutput = SwShExeFsPatchTestFixtures.CorruptHeaderHash(output, ".text");
        var verifyException = Assert.Throws<InvalidDataException>(() =>
            SwShExeFsRoyalCandyMainPatcher.VerifyBasePatchOutput(source, corruptOutput, ProjectGame.Sword));

        Assert.Contains("required NSO header hash", verifyException.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TemporarySwShProject CreateEditableProject(ProjectGame game)
    {
        var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        return temp;
    }
}
