// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ShinyRate;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.ShinyRate;

public sealed class SwShShinyRateEditSessionSafetyTests
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

    private static readonly byte[] SwordLoopDependenciesBeforePatch =
    [
        0x17, 0x1D, 0x00, 0x72, 0x40, 0x05, 0x00, 0x54,
        0xF8, 0x03, 0x1F, 0x2A, 0xF9, 0x03, 0x00, 0x32,
        0x48, 0x03, 0x40, 0xF9, 0x08, 0x4D, 0x40, 0xF9,
        0x09, 0x29, 0x40, 0xA9, 0x41, 0x01, 0x09, 0x0B,
        0x4A, 0x01, 0x09, 0xCA, 0x49, 0xA1, 0xC9, 0xCA,
        0x29, 0x41, 0x0A, 0xCA, 0x4A, 0x6D, 0xCA, 0x93,
        0x09, 0x29, 0x00, 0xA9, 0xA0, 0xA2, 0x42, 0xB9,
        0xB4, 0x17, 0xE9, 0x97, 0x18, 0x03, 0x00, 0x2A,
    ];

    private static readonly byte[] ShieldLoopDependenciesBeforePatch =
    [
        0x17, 0x1D, 0x00, 0x72, 0x40, 0x05, 0x00, 0x54,
        0xF8, 0x03, 0x1F, 0x2A, 0xF9, 0x03, 0x00, 0x32,
        0x48, 0x03, 0x40, 0xF9, 0x08, 0x4D, 0x40, 0xF9,
        0x09, 0x29, 0x40, 0xA9, 0x41, 0x01, 0x09, 0x0B,
        0x4A, 0x01, 0x09, 0xCA, 0x49, 0xA1, 0xC9, 0xCA,
        0x29, 0x41, 0x0A, 0xCA, 0x4A, 0x6D, 0xCA, 0x93,
        0x09, 0x29, 0x00, 0xA9, 0xA0, 0xA2, 0x42, 0xB9,
        0xA8, 0x17, 0xE9, 0x97, 0x18, 0x03, 0x00, 0x2A,
    ];

    private static readonly byte[] LoopDependenciesAfterPatch =
    [
        0x39, 0x07, 0x00, 0x11,
        0x20, 0xFE, 0x07, 0x36,
        0x1F, 0x03, 0x00, 0x72,
        0xE8, 0x03, 0x00, 0x32,
        0x08, 0x15, 0x88, 0x1A,
        0x88, 0x0A, 0x00, 0xB9,
        0x88, 0x12, 0x40, 0xB9,
        0x88, 0xFA, 0xFF, 0x35,
    ];

    [Fact]
    public void MissingSelectedGameBlocksStageReviewAndApply()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticMain(ProjectGame.Sword));
        var paths = temp.Paths;
        var service = new SwShShinyRateEditSessionService();

        var stage = service.StageRate(paths, "fixed", 6, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Equal(SwShWorkflowAvailability.Disabled, stage.Workflow.Summary.Availability);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Sword or Pokemon Shield", StringComparison.Ordinal));
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void LayeredStageUsesCanonicalIdentityBaseEffectiveAndPayloadSources()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(BaseMainPath(paths));
        temp.WriteOutputFile(
            SwShShinyRateWorkflowService.ExeFsMainPath,
            SwShShinyRateMainPatcher.ApplyRate(
                baseMain,
                SwShShinyRateMode.FixedRolls,
                3,
                ProjectGame.Sword));
        var service = new SwShShinyRateEditSessionService();

        var stage = service.StageRate(paths, "fixed", 6, session: null);
        var edit = Assert.Single(stage.Session.PendingEdits);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var write = Assert.Single(plan.Writes);

        Assert.Equal("workflow.shinyRate", edit.Domain);
        Assert.Equal("shiny-rate", edit.RecordId);
        Assert.Equal("rate", edit.Field);
        Assert.Equal("fixed:6", edit.NewValue);
        Assert.Equal("Stage Shiny Rate fixed 6 rolls.", edit.Summary);
        Assert.Contains(edit.Sources, source => source.Layer == ProjectFileLayer.Base
            && source.RelativePath == SwShShinyRateWorkflowService.ExeFsMainPath);
        Assert.Contains(edit.Sources, source => source.Layer == ProjectFileLayer.Layered
            && source.RelativePath == SwShShinyRateWorkflowService.ExeFsMainPath);
        Assert.Contains(edit.Sources, source => source.Layer == ProjectFileLayer.Pending
            && source.RelativePath.StartsWith("pending/shiny-rate/rate/", StringComparison.Ordinal)
            && source.RelativePath.Length == "pending/shiny-rate/rate/".Length + 64);
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
        var baseMain = File.ReadAllBytes(BaseMainPath(paths));
        temp.WriteOutputFile(
            SwShShinyRateWorkflowService.ExeFsMainPath,
            SwShShinyRateMainPatcher.ApplyRate(
                baseMain,
                SwShShinyRateMode.FixedRolls,
                3,
                ProjectGame.Sword));
        var service = new SwShShinyRateEditSessionService();
        var stage = service.StageRate(paths, "fixed", 6, session: null);
        var edit = Assert.Single(stage.Session.PendingEdits);
        var forgedEdits = new[]
        {
            edit with { Domain = "workflow.other" },
            edit with { RecordId = "other" },
            edit with { Field = "other" },
            edit with { Summary = "Forged Shiny Rate edit." },
            edit with { NewValue = "fixed:06" },
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
        var service = new SwShShinyRateEditSessionService();
        var first = service.StageRate(paths, "fixed", 6, session: null);
        var reviewedPlan = service.CreateChangePlan(paths, first.Session);
        var changed = service.StageRate(paths, "always", null, first.Session);

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
        var service = new SwShShinyRateEditSessionService();
        var stage = service.StageRate(paths, "fixed", 6, session: null);
        var reviewedPlan = service.CreateChangePlan(paths, stage.Session);
        File.WriteAllBytes(BaseMainPath(paths), MutateTextByte(
            File.ReadAllBytes(BaseMainPath(paths)),
            0x100,
            0x42));

        var apply = service.ApplyChangePlan(paths, stage.Session, reviewedPlan);

        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void StageRefreshesOutputCreatedAfterWorkspaceWasCachedAndPreservesIt()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var workspace = new ProjectWorkspaceService();
        var service = new SwShShinyRateEditSessionService(workspace);
        _ = workspace.Open(paths);
        var baseMain = File.ReadAllBytes(BaseMainPath(paths));
        var mainWithOtherHook = MutateTextByte(baseMain, 0x100, 0x42);
        temp.WriteOutputFile(
            SwShShinyRateWorkflowService.ExeFsMainPath,
            SwShShinyRateMainPatcher.ApplyRate(
                mainWithOtherHook,
                SwShShinyRateMode.FixedRolls,
                3,
                ProjectGame.Sword));

        var stage = service.StageRate(paths, "fixed", 6, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        var output = File.ReadAllBytes(OutputMainPath(paths));
        var workflow = new SwShShinyRateWorkflowService().Load(workspace.Open(paths));

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(0x42, NsoFile.Parse(output).Text.DecompressedData[0x100]);
        Assert.Equal(6, SwShShinyRateMainPatcher.Analyze(output, ProjectGame.Sword).RollCount);
        Assert.Equal("fixed", workflow.InstallStatus);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Equal(1, workflow.Stats.OutputFileCount);
    }

    [Fact]
    public void OutputThatAppearsAfterStageRequiresRestagingAndIsNotOverwritten()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShShinyRateEditSessionService();
        var stage = service.StageRate(paths, "fixed", 6, session: null);
        var reviewedPlan = service.CreateChangePlan(paths, stage.Session);
        var concurrentOutput = SwShShinyRateMainPatcher.ApplyRate(
            MutateTextByte(File.ReadAllBytes(BaseMainPath(paths)), 0x100, 0x42),
            SwShShinyRateMode.FixedRolls,
            3,
            ProjectGame.Sword);
        temp.WriteOutputFile(SwShShinyRateWorkflowService.ExeFsMainPath, concurrentOutput);

        var apply = service.ApplyChangePlan(paths, stage.Session, reviewedPlan);

        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(concurrentOutput, File.ReadAllBytes(OutputMainPath(paths)));
    }

    [Fact]
    public void DefaultRestoreDeletesSemanticallyVanillaOutput()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(BaseMainPath(paths));
        temp.WriteOutputFile(
            SwShShinyRateWorkflowService.ExeFsMainPath,
            SwShShinyRateMainPatcher.ApplyRate(
                baseMain,
                SwShShinyRateMode.FixedRolls,
                6,
                ProjectGame.Sword));
        var service = new SwShShinyRateEditSessionService();
        var stage = service.StageRate(paths, "default", null, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(OutputMainPath(paths)));
    }

    [Fact]
    public void DefaultRestoreKeepsOutputThatContainsOtherHooks()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = File.ReadAllBytes(BaseMainPath(paths));
        var mainWithOtherHook = MutateTextByte(baseMain, 0x100, 0x42);
        temp.WriteOutputFile(
            SwShShinyRateWorkflowService.ExeFsMainPath,
            SwShShinyRateMainPatcher.ApplyRate(
                mainWithOtherHook,
                SwShShinyRateMode.FixedRolls,
                6,
                ProjectGame.Sword));
        var service = new SwShShinyRateEditSessionService();
        var stage = service.StageRate(paths, "default", null, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        var output = File.ReadAllBytes(OutputMainPath(paths));

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(0x42, NsoFile.Parse(output).Text.DecompressedData[0x100]);
        Assert.Equal(SwShShinyRateMainKind.Default, SwShShinyRateMainPatcher.Analyze(
            output,
            ProjectGame.Sword).Kind);
    }

    [Fact]
    public void DefaultFromVanillaBaseDoesNotCreateRedundantOutput()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShShinyRateEditSessionService();
        var stage = service.StageRate(paths, "default", null, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(OutputMainPath(paths)));
        Assert.Equal(0, stage.Workflow.Stats.OutputFileCount);
    }

    [Fact]
    public void WorkflowBlocksWrongGameBaseEvenWhenLayeredMainMatchesSelection()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticMain(ProjectGame.Shield));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        temp.WriteOutputFile(
            SwShShinyRateWorkflowService.ExeFsMainPath,
            CreateSyntheticMain(ProjectGame.Sword));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShShinyRateWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Base exefs/main", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingMainUsesBlockedUnknownRuleAndAccurateStats()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShShinyRateWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Equal("blocked", workflow.RateRule.Mode);
        Assert.Null(workflow.RateRule.RollCount);
        Assert.Null(workflow.RateRule.OddsDenominator);
        Assert.Null(workflow.RateRule.ChancePercent);
        Assert.Equal("Unknown", workflow.RateRule.OddsLabel);
        Assert.Equal("Unknown", workflow.RateRule.PercentLabel);
        Assert.Equal(0, workflow.Stats.SourceFileCount);
        Assert.Equal(0, workflow.Stats.OutputFileCount);
    }

    [Fact]
    public void LatePromotionCollisionPreservesConcurrentOutput()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var concurrentOutput = new byte[] { 0x43, 0x4F, 0x4E, 0x43, 0x55, 0x52, 0x52, 0x45, 0x4E, 0x54 };
        var service = new SwShShinyRateEditSessionService(
            projectWorkspaceService: null,
            shinyRateWorkflowService: null,
            beforeVerifiedPromotion: (_, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutputMainPath(paths))!);
                File.WriteAllBytes(OutputMainPath(paths), concurrentOutput);
            });
        var stage = service.StageRate(paths, "fixed", 6, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("changed before verified promotion", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(concurrentOutput, File.ReadAllBytes(OutputMainPath(paths)));
    }

    [Fact]
    public void NullModeReturnsValidationDiagnostic()
    {
        using var temp = CreateProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var stage = new SwShShinyRateEditSessionService().StageRate(
            paths,
            mode: null,
            rollCount: null,
            session: null);

        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message == "Shiny Rate mode is required.");
    }

    private static TemporarySwShProject CreateProject(ProjectGame game)
    {
        var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticMain(game));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        return temp;
    }

    private static byte[] CreateSyntheticMain(ProjectGame game)
    {
        var shift = game == ProjectGame.Shield ? SwShShinyRateMainPatcher.ShieldOffsetDelta : 0;
        var text = new byte[SwShShinyRateMainPatcher.SwordBreakOffset + shift + 0x40];
        Array.Fill(text, (byte)0xCC);
        FunctionPrelude.CopyTo(text.AsSpan(SwShShinyRateMainPatcher.SwordFunctionOffset + shift));
        var dependenciesBefore = game == ProjectGame.Shield
            ? ShieldLoopDependenciesBeforePatch
            : SwordLoopDependenciesBeforePatch;
        dependenciesBefore.CopyTo(text.AsSpan(
            SwShShinyRateMainPatcher.SwordCompareOffset + shift - dependenciesBefore.Length));
        WriteInstruction(text, SwShShinyRateMainPatcher.SwordCompareOffset + shift, VanillaCompareInstruction);
        WriteInstruction(text, SwShShinyRateMainPatcher.SwordBreakOffset + shift, VanillaBreakInstruction);
        LoopDependenciesAfterPatch.CopyTo(text.AsSpan(
            SwShShinyRateMainPatcher.SwordCompareOffset + shift + SwShShinyRateMainPatcher.PatchLength));
        return CreateNso(text, [0x10], [0x20], BuildIdForGame(game));
    }

    private static byte[] MutateTextByte(byte[] main, int offset, byte value)
    {
        var nso = NsoFile.Parse(main);
        var text = nso.Text.DecompressedData.ToArray();
        text[offset] = value;
        return nso.Write(textDecompressedData: text);
    }

    private static string BaseMainPath(ProjectPaths paths)
    {
        return Path.Combine(paths.BaseExeFsPath!, "main");
    }

    private static string OutputMainPath(ProjectPaths paths)
    {
        return Path.Combine(paths.OutputRootPath!, "exefs", "main");
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

    private static void WriteSegmentHeader(
        byte[] output,
        int offset,
        int fileOffset,
        int memoryOffset,
        int decompressedSize)
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
