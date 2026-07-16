// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.HyperTraining;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.HyperTraining;

public sealed class SwShHyperTrainingWorkflowTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    [Fact]
    public void ApplyMinimumLevelPatchesThresholdCellOnly()
    {
        var amx = CreateSyntheticHyperTrainingAmx(100);

        var patched = SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(amx, 42);

        Assert.Equal(42, SwShHyperTrainingAmxPatcher.ReadMinimumLevel(patched));
        Assert.Equal(PackInstruction(188, 100), ReadCodeCell(patched, 2300));
    }

    [Fact]
    public void ApplyMinimumLevelSupportsCompactAmx()
    {
        var amx = CreateSyntheticCompactHyperTrainingAmx(100);

        var patched = SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(amx, 25);

        Assert.Equal(25, SwShHyperTrainingAmxPatcher.ReadMinimumLevel(patched));
        Assert.True((BinaryPrimitives.ReadInt16LittleEndian(patched.AsSpan(0x08)) & 0x0004) != 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ApplyMinimumLevelRejectsOutOfRangeValues(int minimumLevel)
    {
        var amx = CreateSyntheticHyperTrainingAmx(100);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(amx, minimumLevel));
    }

    [Fact]
    public void AnalyzeReportsConflictForUnexpectedLevelCheckShape()
    {
        var amx = CreateSyntheticHyperTrainingAmx(100);
        WriteCodeCell(amx, 2295, 0);

        var analysis = SwShHyperTrainingAmxPatcher.Analyze(amx);

        Assert.Equal(SwShHyperTrainingScriptKind.Conflict, analysis.Kind);
        Assert.Contains("level comparison", analysis.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DialoguePatcherUpdatesLevelLinesAndKeepsVariableToken()
    {
        var table = CreateDialogueTable();
        var source = SwShGameTextFile.Parse(table);

        var patched = SwShHyperTrainingDialoguePatcher.ApplyMinimumLevel(table, 42);
        var parsed = SwShGameTextFile.Parse(patched);

        Assert.Equal(source.Lines[0].Text.Replace("100", "42", StringComparison.Ordinal), parsed.Lines[0].Text);
        Assert.Equal(source.Lines[3].Text.Replace("100", "42", StringComparison.Ordinal), parsed.Lines[3].Text);
        Assert.Equal("Untouched \\[VAR 0102(0001)] line.", parsed.Lines[4].Text);
        Assert.Equal(source.Lines.Select(line => line.Flags), parsed.Lines.Select(line => line.Flags));
    }

    [Fact]
    public void DialoguePatcherRejectsAmbiguousOrDisagreeingLevelTokens()
    {
        var parsed = SwShGameTextFile.Parse(CreateDialogueTable());
        var variants = new[]
        {
            RewriteDialogueLine(
                parsed,
                SwShHyperTrainingDialoguePatcher.LevelFailureLineIndex,
                parsed.Lines[SwShHyperTrainingDialoguePatcher.LevelFailureLineIndex].Text
                    .Replace("Lv. 100", "Lv. 99", StringComparison.Ordinal)),
            RewriteDialogueLine(
                parsed,
                SwShHyperTrainingDialoguePatcher.IntroLineIndex,
                parsed.Lines[SwShHyperTrainingDialoguePatcher.IntroLineIndex].Text + " Lv. 100"),
            RewriteDialogueLine(
                parsed,
                SwShHyperTrainingDialoguePatcher.LevelFailureLineIndex,
                parsed.Lines[SwShHyperTrainingDialoguePatcher.LevelFailureLineIndex].Text
                    .Replace("Hyper Training", "training", StringComparison.Ordinal)),
        };

        foreach (var variant in variants)
        {
            Assert.Equal(SwShHyperTrainingDialogueKind.Conflict, SwShHyperTrainingDialoguePatcher.Analyze(variant).Kind);
            Assert.Throws<InvalidDataException>(() =>
                SwShHyperTrainingDialoguePatcher.ApplyMinimumLevel(variant, 42));
        }
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ApplyMainMinimumLevelPatchesPickerCutoffAndBranchConditions(ProjectGame game)
    {
        var main = CreateSyntheticHyperTrainingMain(game);
        var offsets = HyperTrainingOffsets(game);

        var patched = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(main, 50, game);
        var patchedText = NsoFile.Parse(patched).Text.DecompressedData;
        var analysis = SwShHyperTrainingMainPatcher.Analyze(patched, game);

        Assert.Equal(SwShHyperTrainingMainKind.CustomMinimumLevel, analysis.Kind);
        Assert.Equal(50, analysis.MinimumLevel);
        Assert.Equal(EncodeCmpW0Immediate(50), ReadInstruction(patchedText, offsets.PreflightCompareOffset));
        Assert.Equal(EncodeCmpW0Immediate(50), ReadInstruction(patchedText, offsets.EligibilityCompareOffset));
        Assert.Equal(0x54000063u, ReadInstruction(patchedText, offsets.EligibilityBranchOffset));
        Assert.Equal(EncodeCmpW0Immediate(50), ReadInstruction(patchedText, offsets.GrayOutCompareOffset));
        Assert.Equal(0x540000A3u, ReadInstruction(patchedText, offsets.GrayOutBranchOffset));
        Assert.Equal(EncodeCmpW0Immediate(50), ReadInstruction(patchedText, offsets.DetailCompareOffset));
        Assert.Equal(0x540002C3u, ReadInstruction(patchedText, offsets.DetailBranchOffset));
    }

    [Fact]
    public void ApplyMainMinimumLevelRestoresVanillaBranchShapeAtLevel100()
    {
        var patched = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(
            CreateSyntheticHyperTrainingMain(ProjectGame.Sword),
            50,
            ProjectGame.Sword);

        var restored = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(patched, 100, ProjectGame.Sword);
        var restoredText = NsoFile.Parse(restored).Text.DecompressedData;
        var analysis = SwShHyperTrainingMainPatcher.Analyze(restored, ProjectGame.Sword);

        Assert.Equal(SwShHyperTrainingMainKind.NotInstalled, analysis.Kind);
        Assert.Equal(100, analysis.MinimumLevel);
        Assert.Equal(0x54000061u, ReadInstruction(restoredText, SwShHyperTrainingMainPatcher.SwordEligibilityBranchOffset));
        Assert.Equal(0x540000A1u, ReadInstruction(restoredText, SwShHyperTrainingMainPatcher.SwordGrayOutBranchOffset));
        Assert.Equal(0x540002C1u, ReadInstruction(restoredText, SwShHyperTrainingMainPatcher.SwordDetailBranchOffset));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void AnalyzeMainRejectsNonZeroBuildIdTail(ProjectGame game)
    {
        var main = CreateSyntheticHyperTrainingMain(game);
        main[0x40 + 20] = 0x7F;

        var analysis = SwShHyperTrainingMainPatcher.Analyze(main, game);

        Assert.Equal(SwShHyperTrainingMainKind.UnsupportedBuild, analysis.Kind);
        Assert.Null(analysis.DetectedGame);
        Assert.Throws<InvalidDataException>(() =>
            SwShHyperTrainingMainPatcher.ApplyMinimumLevel(main, 50, game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void AnalyzeMainRejectsCorruptedLevelGetter(ProjectGame game)
    {
        var main = MutateMainText(
            CreateSyntheticHyperTrainingMain(game),
            SwShHyperTrainingMainPatcher.LevelGetterOffset + 0x20);

        var analysis = SwShHyperTrainingMainPatcher.Analyze(main, game);

        Assert.Equal(SwShHyperTrainingMainKind.Conflict, analysis.Kind);
        Assert.Contains("level getter", analysis.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() =>
            SwShHyperTrainingMainPatcher.ApplyMinimumLevel(main, 50, game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void AnalyzeMainRejectsCorruptedSelectedGameDependencies(ProjectGame game)
    {
        var original = CreateSyntheticHyperTrainingMain(game);
        var offsets = HyperTrainingOffsets(game);
        var dependencyOffsets = new[]
        {
            offsets.PreflightCompareOffset - sizeof(uint),
            offsets.PreflightCompareOffset + sizeof(uint),
            offsets.PreflightCompareOffset + (2 * sizeof(uint)),
            offsets.EligibilityCompareOffset - sizeof(uint),
            offsets.GrayOutCompareOffset - sizeof(uint),
            offsets.DetailCompareOffset - sizeof(uint),
        };

        foreach (var dependencyOffset in dependencyOffsets)
        {
            var main = MutateMainText(original, dependencyOffset);
            var analysis = SwShHyperTrainingMainPatcher.Analyze(main, game);

            Assert.True(
                analysis.Kind == SwShHyperTrainingMainKind.Conflict,
                $"Expected dependency at main.text+0x{dependencyOffset:X8} to be rejected; found {analysis.Kind}: {analysis.Message}");
            Assert.Throws<InvalidDataException>(() =>
                SwShHyperTrainingMainPatcher.ApplyMinimumLevel(main, 50, game));
        }
    }

    [Fact]
    public void ApplyMainMinimumLevelRequiresExplicitSupportedGame()
    {
        var main = CreateSyntheticHyperTrainingMain(ProjectGame.Sword);

        Assert.Throws<InvalidDataException>(() =>
            SwShHyperTrainingMainPatcher.ApplyMinimumLevel(main, 50, expectedGame: null));
    }

    [Fact]
    public void ApplyMainMinimumLevelChangesOnlyHyperTrainingReservedTextBytes()
    {
        var main = CreateSyntheticHyperTrainingMain(ProjectGame.Sword, extraTextSetup: text =>
        {
            WriteInstruction(text, 0x00747988, 0xDEADBEEF);
            WriteInstruction(text, 0x013AE3AC, 0xFEEDFACE);
        });
        var baseText = NsoFile.Parse(main).Text.DecompressedData;

        var patched = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(main, 50, ProjectGame.Sword);
        var patchedText = NsoFile.Parse(patched).Text.DecompressedData;

        Assert.Equal(0xDEADBEEFu, ReadInstruction(patchedText, 0x00747988));
        Assert.Equal(0xFEEDFACEu, ReadInstruction(patchedText, 0x013AE3AC));
        Assert.All(
            ChangedTextOffsets(baseText, patchedText),
            changedOffset => Assert.Contains(
                SwShHyperTrainingMainPatcher.ReservedMainTextRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    [Fact]
    public void StageAndApplyMinimumLevelWritesScriptDialogueAndMainOutputs()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/script/amx/hyper_training.amx",
            CreateSyntheticHyperTrainingAmx(100));
        temp.WriteBaseRomFsFile(
            "bin/message/English/script/sub_event_007.dat",
            CreateDialogueTable());
        temp.WriteBaseExeFsFile("main", CreateSyntheticHyperTrainingMain(ProjectGame.Sword));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        var service = new SwShHyperTrainingEditSessionService();

        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var staged = service.StageMinimumLevel(paths, 50, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(3, plan.Writes.Count);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShHyperTrainingWorkflowService.ScriptPath);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShHyperTrainingWorkflowService.ExeFsMainPath);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        Assert.Equal(
            50,
            SwShHyperTrainingAmxPatcher.ReadMinimumLevel(File.ReadAllBytes(Path.Combine(
                temp.OutputRootPath,
                "romfs",
                "bin",
                "script",
                "amx",
                "hyper_training.amx"))));
        var mainAnalysis = SwShHyperTrainingMainPatcher.Analyze(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "exefs",
            "main")));
        Assert.Equal(SwShHyperTrainingMainKind.CustomMinimumLevel, mainAnalysis.Kind);
        Assert.Equal(50, mainAnalysis.MinimumLevel);

        var dialogue = SwShGameTextFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "script",
            "sub_event_007.dat")));
        Assert.Contains("Lv. 50", dialogue.Lines[3].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void StageAndApplyMinimumLevelPreservesExistingLayeredScriptCells()
    {
        using var temp = TemporarySwShProject.Create();
        var baseScript = CreateSyntheticHyperTrainingAmx(100);
        temp.WriteBaseRomFsFile(
            "bin/script/amx/hyper_training.amx",
            baseScript);
        temp.WriteBaseExeFsFile("main", CreateSyntheticHyperTrainingMain(ProjectGame.Sword));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        var layeredScript = baseScript.ToArray();
        WriteCodeCell(layeredScript, 2300, PackInstruction(188, 77));
        temp.WriteOutputFile(SwShHyperTrainingWorkflowService.ScriptPath, layeredScript);
        var service = new SwShHyperTrainingEditSessionService();

        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var staged = service.StageMinimumLevel(paths, 50, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var scriptWrite = Assert.Single(
            plan.Writes,
            write => write.TargetRelativePath == SwShHyperTrainingWorkflowService.ScriptPath);
        Assert.Equal(
            [ProjectFileLayer.Base, ProjectFileLayer.Layered, ProjectFileLayer.Pending],
            scriptWrite.Sources.Select(source => source.Layer).ToArray());
        var output = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script",
            "amx",
            "hyper_training.amx"));
        Assert.Equal(50, SwShHyperTrainingAmxPatcher.ReadMinimumLevel(output));
        Assert.Equal(PackInstruction(188, 77), ReadCodeCell(output, 2300));
    }

    [Fact]
    public void ApplyMinimumLevelPreservesCompactAmxSuffix()
    {
        byte[] suffix = [0xDE, 0xAD, 0xBE, 0xEF, 0x5A];
        var compact = CreateSyntheticCompactHyperTrainingAmx(100);
        var withSuffix = compact.Concat(suffix).ToArray();

        var patched = SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(withSuffix, 42);

        Assert.Equal(42, SwShHyperTrainingAmxPatcher.ReadMinimumLevel(patched));
        Assert.Equal(suffix, patched[^suffix.Length..]);
    }

    [Fact]
    public void StageRequiresExplicitSelectedGame()
    {
        using var temp = TemporarySwShProject.Create();
        WriteEditableProject(temp, ProjectGame.Sword);
        var service = new SwShHyperTrainingEditSessionService();

        var staged = service.StageMinimumLevel(temp.Paths with { SelectedGame = null }, 50, session: null);

        Assert.Empty(staged.Session.PendingEdits);
        Assert.Contains(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StagedEditUsesCanonicalIdentityAndBaseEffectivePayloadSources()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = WriteEditableProject(temp, ProjectGame.Sword);
        var service = new SwShHyperTrainingEditSessionService();
        var staged = service.StageMinimumLevel(paths, 50, session: null);
        var edit = Assert.Single(staged.Session.PendingEdits);

        Assert.Equal(SwShHyperTrainingEditSessionService.HyperTrainingEditDomain, edit.Domain);
        Assert.Equal("hyper-training-minimum-level", edit.RecordId);
        Assert.Equal("minimumLevel", edit.Field);
        Assert.Equal("50", edit.NewValue);
        Assert.Equal("Stage Hyper Training minimum level Lv.50.", edit.Summary);
        Assert.Equal(3, edit.Sources.Count(source => source.Layer == ProjectFileLayer.Base));
        Assert.Single(edit.Sources, source => source.Layer == ProjectFileLayer.Pending);
        Assert.DoesNotContain(edit.Sources, source => source.Layer == ProjectFileLayer.Generated);

        var tamperedEdits = new[]
        {
            edit with { Summary = "tampered" },
            edit with { RecordId = "other" },
            edit with { Field = "other" },
            edit with { NewValue = "050" },
            edit with { Sources = edit.Sources.Reverse().ToArray() },
            edit with { Sources = edit.Sources.Skip(1).ToArray() },
        };
        foreach (var tamperedEdit in tamperedEdits)
        {
            var session = staged.Session with { PendingEdits = [tamperedEdit] };
            Assert.False(service.Validate(paths, session).IsValid);
        }
    }

    [Fact]
    public void ApplyRejectsSourceDriftAfterPlanReviewWithoutWritingOutputs()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = WriteEditableProject(temp, ProjectGame.Sword);
        var service = new SwShHyperTrainingEditSessionService();
        var staged = service.StageMinimumLevel(paths, 50, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var changedScript = CreateSyntheticHyperTrainingAmx(100);
        WriteCodeCell(changedScript, 2300, PackInstruction(188, 77));
        temp.WriteBaseRomFsFile("bin/script/amx/hyper_training.amx", changedScript);

        var apply = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        AssertOutputsAbsent(temp);
    }

    [Fact]
    public void ApplyRejectsPendingPayloadDriftAfterPlanReviewWithoutWritingOutputs()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = WriteEditableProject(temp, ProjectGame.Sword);
        var service = new SwShHyperTrainingEditSessionService();
        var staged = service.StageMinimumLevel(paths, 50, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var edit = Assert.Single(staged.Session.PendingEdits);
        var changedSession = staged.Session with
        {
            PendingEdits = [edit with { NewValue = "51" }],
        };

        var apply = service.ApplyChangePlan(paths, changedSession, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOutputsAbsent(temp);
    }

    [Fact]
    public void ApplyingVanillaLevelDeletesSemanticallyVanillaOutputs()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = WriteEditableProject(temp, ProjectGame.Sword);
        var service = new SwShHyperTrainingEditSessionService();
        ApplyLevel(service, paths, 50);

        var apply = ApplyLevel(service, paths, 100);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOutputsAbsent(temp);
    }

    [Fact]
    public void ApplyingVanillaLevelPreservesUnrelatedLayeredChanges()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = WriteEditableProject(temp, ProjectGame.Sword);
        var service = new SwShHyperTrainingEditSessionService();
        ApplyLevel(service, paths, 50);

        var scriptPath = OutputPath(temp, SwShHyperTrainingWorkflowService.ScriptPath);
        var script = File.ReadAllBytes(scriptPath);
        WriteCodeCell(script, 2300, PackInstruction(188, 77));
        File.WriteAllBytes(scriptPath, script);

        const int unrelatedMainOffset = 0x00747988;
        var mainPath = OutputPath(temp, SwShHyperTrainingWorkflowService.ExeFsMainPath);
        var mainNso = NsoFile.Parse(File.ReadAllBytes(mainPath));
        var mainText = mainNso.Text.DecompressedData.ToArray();
        WriteInstruction(mainText, unrelatedMainOffset, 0xDEADBEEF);
        File.WriteAllBytes(mainPath, mainNso.Write(textDecompressedData: mainText));

        var dialoguePath = OutputPath(temp, SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        var dialogue = SwShGameTextFile.Parse(File.ReadAllBytes(dialoguePath));
        File.WriteAllBytes(
            dialoguePath,
            RewriteDialogueLine(dialogue, 4, "Preserve this unrelated custom line."));

        var apply = ApplyLevel(service, paths, 100);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(100, SwShHyperTrainingAmxPatcher.ReadMinimumLevel(File.ReadAllBytes(scriptPath)));
        Assert.Equal(PackInstruction(188, 77), ReadCodeCell(File.ReadAllBytes(scriptPath), 2300));
        var restoredMain = NsoFile.Parse(File.ReadAllBytes(mainPath));
        Assert.Equal(0xDEADBEEFu, ReadInstruction(restoredMain.Text.DecompressedData, unrelatedMainOffset));
        Assert.Equal(
            SwShHyperTrainingMainKind.NotInstalled,
            SwShHyperTrainingMainPatcher.Analyze(File.ReadAllBytes(mainPath), ProjectGame.Sword).Kind);
        Assert.Equal(
            "Preserve this unrelated custom line.",
            SwShGameTextFile.Parse(File.ReadAllBytes(dialoguePath)).Lines[4].Text);
    }

    [Fact]
    public void MidPromotionFailureRollsBackEveryHyperTrainingOutput()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = WriteEditableProject(temp, ProjectGame.Sword);
        var service = new SwShHyperTrainingEditSessionService(
            projectWorkspaceService: null,
            hyperTrainingWorkflowService: null,
            beforeVerifiedPromotion: (index, _) =>
            {
                if (index == 1)
                {
                    throw new IOException("Injected Hyper Training promotion failure.");
                }
            });
        var staged = service.StageMinimumLevel(paths, 50, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);

        var apply = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("promotion failure", StringComparison.OrdinalIgnoreCase));
        AssertOutputsAbsent(temp);
    }

    private static ProjectPaths WriteEditableProject(TemporarySwShProject temp, ProjectGame game)
    {
        temp.WriteBaseRomFsFile(
            "bin/script/amx/hyper_training.amx",
            CreateSyntheticHyperTrainingAmx(100));
        temp.WriteBaseRomFsFile(
            "bin/message/English/script/sub_event_007.dat",
            CreateDialogueTable());
        temp.WriteBaseExeFsFile("main", CreateSyntheticHyperTrainingMain(game));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        return temp.Paths with { SelectedGame = game };
    }

    private static ApplyResult ApplyLevel(
        SwShHyperTrainingEditSessionService service,
        ProjectPaths paths,
        int minimumLevel)
    {
        var staged = service.StageMinimumLevel(paths, minimumLevel, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var apply = service.ApplyChangePlan(paths, staged.Session, plan);
        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return apply;
    }

    private static string OutputPath(TemporarySwShProject temp, string relativePath)
    {
        return Path.Combine(
            temp.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void AssertOutputsAbsent(TemporarySwShProject temp)
    {
        Assert.False(File.Exists(OutputPath(temp, SwShHyperTrainingWorkflowService.ScriptPath)));
        Assert.False(File.Exists(OutputPath(temp, SwShHyperTrainingWorkflowService.ExeFsMainPath)));
        Assert.False(File.Exists(OutputPath(temp, SwShHyperTrainingWorkflowService.EnglishDialoguePath)));
    }

    private static byte[] RewriteDialogueLine(
        SwShGameTextFile source,
        int lineIndex,
        string text)
    {
        var lines = source.Lines.ToArray();
        lines[lineIndex] = lines[lineIndex] with { Text = text };
        return source.WritePreserving(lines);
    }

    private static byte[] MutateMainText(byte[] main, int offset)
    {
        var nso = NsoFile.Parse(main);
        var text = nso.Text.DecompressedData.ToArray();
        text[offset] ^= 0x01;
        return nso.Write(textDecompressedData: text);
    }

    private static byte[] CreateSyntheticHyperTrainingMain(
        ProjectGame game,
        int minimumLevel = 100,
        Action<byte[]>? extraTextSetup = null)
    {
        var text = new byte[0x0157D000];
        var offsets = HyperTrainingOffsets(game);
        WriteHyperTrainingLevelGetter(text);
        WriteInstruction(text, offsets.PreflightCompareOffset - 4, offsets.PreflightGetterCall);
        WriteInstruction(text, offsets.PreflightCompareOffset, EncodeCmpW0Immediate(minimumLevel));
        WriteInstruction(text, offsets.PreflightCompareOffset + 4, 0x1A9F27E8);
        WriteInstruction(text, offsets.PreflightCompareOffset + 8, 0x54000123);
        WriteInstruction(text, offsets.EligibilityCompareOffset - 4, offsets.EligibilityGetterCall);
        WriteInstruction(text, offsets.EligibilityCompareOffset, EncodeCmpW0Immediate(minimumLevel));
        WriteInstruction(text, offsets.EligibilityBranchOffset, minimumLevel == 100 ? 0x54000061u : 0x54000063u);
        WriteInstruction(text, offsets.GrayOutCompareOffset - 4, offsets.GrayOutGetterCall);
        WriteInstruction(text, offsets.GrayOutCompareOffset, EncodeCmpW0Immediate(minimumLevel));
        WriteInstruction(text, offsets.GrayOutBranchOffset, minimumLevel == 100 ? 0x540000A1u : 0x540000A3u);
        WriteInstruction(text, offsets.DetailCompareOffset - 4, offsets.DetailGetterCall);
        WriteInstruction(text, offsets.DetailCompareOffset, EncodeCmpW0Immediate(minimumLevel));
        WriteInstruction(text, offsets.DetailBranchOffset, minimumLevel == 100 ? 0x540002C1u : 0x540002C3u);
        extraTextSetup?.Invoke(text);
        return CreateNso(text, [0x10], [0x20], BuildIdForGame(game));
    }

    private static HyperTrainingOffsetSet HyperTrainingOffsets(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword => new HyperTrainingOffsetSet(
                0x00F98F18,
                0x00F9A314,
                0x00F9A318,
                0x00F9A334,
                0x00F9A338,
                0x00F9E4C0,
                0x00F9E4C4,
                0x97DF85B7,
                0x97DF80B8,
                0x97DF80B0,
                0x97DF704D),
            ProjectGame.Shield => new HyperTrainingOffsetSet(
                0x00F98F48,
                0x00F9A344,
                0x00F9A348,
                0x00F9A364,
                0x00F9A368,
                0x00F9E4F0,
                0x00F9E4F4,
                0x97DF85AB,
                0x97DF80AC,
                0x97DF80A4,
                0x97DF7041),
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        };
    }

    private static void WriteHyperTrainingLevelGetter(byte[] text)
    {
        uint[] words =
        [
            0xF81D0FF5, 0xA9014FF4, 0xA9027BFD, 0x910083FD, 0xAA0003F3,
            0xF9404C00, 0x97FFB4EA, 0x2A0003E8, 0xF9404E60, 0x360000A8,
            0xA9427BFD, 0xA9414FF4, 0xF84307F5, 0x17FFB8BB, 0x97FFB9A6,
            0x2A0003F4, 0xF9404E60, 0x97FFC34B, 0x2A0003F5, 0xF9404E60,
            0x97FFBA90, 0x2A0003E2, 0x2A1403E0, 0x2A1503E1, 0x97FFF188,
            0xA9427BFD, 0x12001C00, 0xA9414FF4, 0xF84307F5, 0xD65F03C0,
        ];
        for (var index = 0; index < words.Length; index++)
        {
            WriteInstruction(text, 0x0077A5F0 + (index * sizeof(uint)), words[index]);
        }
    }

    private static uint EncodeCmpW0Immediate(int immediate)
    {
        return 0x7100001Fu | (uint)(immediate << 10);
    }

    private static uint ReadInstruction(byte[] text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)));
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static int[] ChangedTextOffsets(byte[] before, byte[] after)
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

    private static byte[] CreateSyntheticHyperTrainingAmx(int minimumLevel)
    {
        const int headerSize = 0x38;
        const int cellSize = 8;
        const int codeCellCount = 2301;
        var cod = headerSize;
        var dat = cod + codeCellCount * cellSize;
        var data = new byte[dat];

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), data.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), 0xF1E1);
        data[0x06] = 0x0A;
        data[0x07] = 0x0A;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x08), 0);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x0A), 12);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), cod);

        WriteCodeCell(data, 2294, PackInstruction(172, minimumLevel));
        WriteCodeCell(data, 2295, 64);
        WriteCodeCell(data, 2296, 176);
        WriteCodeCell(data, 2300, PackInstruction(188, 100));
        return data;
    }

    private static byte[] CreateSyntheticCompactHyperTrainingAmx(int minimumLevel)
    {
        var expanded = CreateSyntheticHyperTrainingAmx(minimumLevel);
        const int cod = 0x38;
        const int cellSize = 8;
        var prefix = expanded[..cod].ToArray();
        BinaryPrimitives.WriteInt16LittleEndian(prefix.AsSpan(0x08), 0x0004);
        var compactBody = CompactAmxMemory(expanded, cod, expanded.Length, cellSize);
        var compact = new byte[cod + compactBody.Length];
        prefix.CopyTo(compact, 0);
        compactBody.CopyTo(compact.AsSpan(cod));
        BinaryPrimitives.WriteInt32LittleEndian(compact.AsSpan(0x00), compact.Length);
        return compact;
    }

    private static byte[] CreateDialogueTable()
    {
        return SwShGameTextFile.Write(
        [
            new SwShGameTextLine(
                "Oho... I see you've become the Champion.\\nThen I can now have your Lv. 100 Pokemon[VAR BE00]\\nundergo Hyper Training.",
                4),
            new SwShGameTextLine("No caps.", 0),
            new SwShGameTextLine("Choose Pokemon.", 0),
            new SwShGameTextLine("If it isn't Lv. 100, it's not hype enough to\\nundergo Hyper Training.", 4),
            new SwShGameTextLine("Untouched \\[VAR 0102(0001)] line.", 0),
        ]);
    }

    private static ulong ReadCodeCell(byte[] amx, int cell)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(amx.AsSpan(0x38 + cell * 8));
    }

    private static void WriteCodeCell(byte[] amx, int cell, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(amx.AsSpan(0x38 + cell * 8), value);
    }

    private static ulong PackInstruction(int opcode, int operand)
    {
        return ((ulong)(uint)operand << 32) | (uint)opcode;
    }

    private static byte[] CompactAmxMemory(byte[] expanded, int cod, int hea, int cellSize)
    {
        var compact = new List<byte>();
        for (var offset = cod; offset < hea; offset += cellSize)
        {
            var signed = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(expanded.AsSpan(offset)));
            var chunks = new List<byte>();
            var value = signed;
            while (true)
            {
                var payload = (byte)(value & 0x7F);
                chunks.Add(payload);
                value >>= 7;
                var signBitSet = (payload & 0x40) != 0;
                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                {
                    break;
                }
            }

            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var current = chunks[i];
                if (i != 0)
                {
                    current |= 0x80;
                }

                compact.Add(current);
            }
        }

        return compact.ToArray();
    }

    private sealed record HyperTrainingOffsetSet(
        int PreflightCompareOffset,
        int EligibilityCompareOffset,
        int EligibilityBranchOffset,
        int GrayOutCompareOffset,
        int GrayOutBranchOffset,
        int DetailCompareOffset,
        int DetailBranchOffset,
        uint PreflightGetterCall,
        uint EligibilityGetterCall,
        uint GrayOutGetterCall,
        uint DetailGetterCall);
}
