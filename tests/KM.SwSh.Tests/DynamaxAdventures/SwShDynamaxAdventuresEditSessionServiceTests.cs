// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Editing;
using KM.SwSh.Pokemon;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresEditSessionServiceTests
{
    [Fact]
    public void StageRepairCleansLegacyExecutableStateWithoutWritingAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var archive = CreateRepairableBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseExeFsFile("main", baseMain);

        var legacyMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);
        var marker = Enumerable.Range(1, 24).Select(value => checked((byte)value)).ToArray();
        var legacyNso = NsoFile.Parse(legacyMain);
        temp.WriteOutputFile(
            "exefs/main",
            legacyNso.Write(
                textDecompressedData: legacyNso.Text.DecompressedData.Concat(marker).ToArray()));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var blockedUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "66");
        Assert.True(blockedUpdate.Workflow.HasLegacyBossTargetPatch);
        Assert.Empty(blockedUpdate.Session.PendingEdits);
        Assert.Contains(blockedUpdate.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("legacy final-boss target remap", StringComparison.Ordinal));

        var blockedPreview = service.PreviewDefaults(
            temp.Paths,
            session: null,
            entryIndex: 0,
            species: archive.Entries[0].Species,
            form: 0,
            level: 65);
        Assert.Empty(blockedPreview.Changes);
        Assert.Contains(blockedPreview.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("legacy final-boss target remap", StringComparison.Ordinal));

        var staged = service.StageRepair(temp.Paths, session: null);
        Assert.DoesNotContain(staged.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal("repairable", staged.Workflow.InstallStatus);
        Assert.True(staged.Workflow.HasLegacyBossTargetPatch);
        Assert.Contains("Stage Repair", staged.Workflow.InstallMessage, StringComparison.Ordinal);
        Assert.Contains(staged.Workflow.Encounters, encounter => encounter.IsEditable);
        Assert.Contains(staged.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Warning
            && diagnostic.Message.Contains("destructive cleanup", StringComparison.Ordinal));
        var repair = Assert.Single(staged.Session.PendingEdits);
        Assert.Equal("workflow.dynamaxAdventures", repair.Domain);
        Assert.Equal(SwShDynamaxAdventuresEditSessionService.RepairExecutableProjectionSummary, repair.Summary);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.LevelField, repair.Field);

        var plan = service.CreateChangePlan(temp.Paths, staged.Session);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var write = Assert.Single(plan.Writes);
        Assert.True(plan.CanApply);
        Assert.Equal("exefs/main", write.TargetRelativePath);
        Assert.Contains("legacy final-boss target remap", write.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(
            plan.Writes,
            candidate => candidate.TargetRelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);

        var applied = service.ApplyChangePlan(temp.Paths, staged.Session, plan);

        Assert.DoesNotContain(applied.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var tableOutputPath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.False(File.Exists(tableOutputPath));
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var repairedMain = File.Exists(outputMainPath)
            ? File.ReadAllBytes(outputMainPath)
            : baseMain;
        var repairedText = NsoFile.Parse(repairedMain).Text.DecompressedData;
        Assert.True(repairedText.AsSpan()[^marker.Length..].SequenceEqual(marker));
        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            repairedMain,
            baseMain,
            archive,
            archive,
            Core.Projects.ProjectGame.Sword,
            archive);
        Assert.False(analysis.HasLegacyBossTargetPatch);
        Assert.NotEqual(SwShDynamaxAdventuresMainKind.Stale, analysis.Kind);
    }

    [Fact]
    public void StageRepairCleansExactLegacyBossPatchForShield()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.SelectedGame = Core.Projects.ProjectGame.Shield;
        var archive = CreateRepairableBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(
            archive,
            SwShDynamaxAdventuresMainPatcher.ShieldCommandValidatorOffsetDelta,
            SwShDynamaxAdventuresMainPatcher.ShieldBuildId);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseExeFsFile("main", baseMain);
        temp.WriteOutputFile(
            "exefs/main",
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                baseMain,
                archive,
                fromSpecies: 144,
                toSpecies: 150));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var staged = service.StageRepair(temp.Paths, session: null);
        var plan = service.CreateChangePlan(temp.Paths, staged.Session);
        var applied = service.ApplyChangePlan(temp.Paths, staged.Session, plan);

        Assert.True(staged.Workflow.HasLegacyBossTargetPatch);
        Assert.True(plan.CanApply);
        Assert.DoesNotContain(applied.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var repairedMain = File.Exists(outputMainPath)
            ? File.ReadAllBytes(outputMainPath)
            : baseMain;
        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            repairedMain,
            baseMain,
            archive,
            archive,
            Core.Projects.ProjectGame.Shield,
            archive);
        Assert.False(analysis.HasLegacyBossTargetPatch);
        Assert.NotEqual(SwShDynamaxAdventuresMainKind.Stale, analysis.Kind);
    }

    [Fact]
    public void StageRepairUsesGenericWordingForOrdinaryStaleProjection()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var archive = SwShDynamaxAdventureTestFixtures.CreateArchive();
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(sourceArchive: archive));
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            archive.WriteEdits([new(1, SwShDynamaxAdventureField.Species, 467)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var staged = service.StageRepair(temp.Paths, session: null);
        var plan = service.CreateChangePlan(temp.Paths, staged.Session);

        Assert.Equal("repairable", staged.Workflow.InstallStatus);
        Assert.False(staged.Workflow.HasLegacyBossTargetPatch);
        Assert.DoesNotContain(staged.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("destructive cleanup", StringComparison.Ordinal));
        Assert.True(plan.CanApply);
        var mainWrite = Assert.Single(plan.Writes, write => write.TargetRelativePath == "exefs/main");
        Assert.DoesNotContain("legacy final-boss target remap", mainWrite.Reason, StringComparison.Ordinal);
        Assert.Contains("Synchronize Dynamax Adventures", mainWrite.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void StageRepairSanitizesMalformedSessionBeforeBlockedWorkflowChecks()
    {
        using var temp = TemporarySwShProject.Create();
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var malformed = service.StartSession() with { PendingEdits = null! };

        var staged = service.StageRepair(temp.Paths, malformed);
        var validation = service.Validate(temp.Paths, malformed);
        var plan = service.CreateChangePlan(temp.Paths, malformed);

        Assert.Empty(staged.Session.PendingEdits);
        Assert.Contains(staged.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("missing its pending action list", StringComparison.Ordinal));
        Assert.False(validation.IsValid);
        Assert.Empty(validation.Session.PendingEdits);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void CreateChangePlanRejectsLegacyBossPatchInstalledAfterOrdinaryEditWasStaged()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var archive = CreateRepairableBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseExeFsFile("main", baseMain);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var stagedEdit = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "66");
        Assert.Single(stagedEdit.Session.PendingEdits);

        var legacyMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);
        temp.WriteOutputFile("exefs/main", legacyMain);

        var plan = service.CreateChangePlan(temp.Paths, stagedEdit.Session);

        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("legacy final-boss target remap", StringComparison.Ordinal));
        Assert.Equal(
            legacyMain,
            File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main")));
    }

    [Fact]
    public void StagedLegacyRepairRejectsUnhealthyPathsAndDamagedPatchDrift()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var archive = CreateRepairableBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseExeFsFile("main", baseMain);
        var legacyMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);
        temp.WriteOutputFile("exefs/main", legacyMain);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var staged = service.StageRepair(temp.Paths, session: null);
        Assert.Single(staged.Session.PendingEdits);

        var unhealthy = service.Validate(
            temp.Paths with { OutputRootPath = null },
            staged.Session);
        Assert.False(unhealthy.IsValid);
        Assert.Contains(unhealthy.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var legacyNso = NsoFile.Parse(legacyMain);
        var damagedText = legacyNso.Text.DecompressedData.ToArray();
        var baseTextLength = NsoFile.Parse(baseMain).Text.DecompressedData.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(
            damagedText.AsSpan(baseTextLength + 0x08, sizeof(uint)),
            0xD503201F);
        var damagedMain = legacyNso.Write(textDecompressedData: damagedText);
        temp.WriteOutputFile("exefs/main", damagedMain);
        var driftService = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var driftValidation = driftService.Validate(temp.Paths, staged.Session);
        var driftPlan = driftService.CreateChangePlan(temp.Paths, staged.Session);

        Assert.False(driftValidation.IsValid);
        Assert.False(driftPlan.CanApply);
        Assert.Empty(driftPlan.Writes);
        Assert.Contains(
            driftValidation.Diagnostics.Concat(driftPlan.Diagnostics),
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("partial or damaged historical KM boss-target remap", StringComparison.Ordinal));
        Assert.Equal(
            damagedMain,
            File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main")));
    }

    [Fact]
    public void VanillaTableRestoreExplicitlyCleansLegacyBossPatch()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var archive = CreateRepairableBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseExeFsFile("main", baseMain);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            archive.Write().Concat(new byte[] { 0 }).ToArray());
        temp.WriteOutputFile(
            "exefs/main",
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                baseMain,
                archive,
                fromSpecies: 144,
                toSpecies: 150));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var staged = service.StageVanillaTableRestore(temp.Paths, session: null);
        var plan = service.CreateChangePlan(temp.Paths, staged.Session);
        var applied = service.ApplyChangePlan(temp.Paths, staged.Session, plan);

        Assert.True(staged.Workflow.CanRestoreVanillaTable);
        Assert.True(staged.Workflow.HasLegacyBossTargetPatch);
        Assert.Contains(staged.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Warning
            && diagnostic.Message.Contains("legacy final-boss target remap", StringComparison.Ordinal));
        Assert.True(plan.CanApply);
        var mainWrite = Assert.Single(plan.Writes, write => write.TargetRelativePath == "exefs/main");
        Assert.Contains("while restoring", mainWrite.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(applied.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar))));
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var effectiveMain = File.Exists(outputMainPath) ? File.ReadAllBytes(outputMainPath) : baseMain;
        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            effectiveMain,
            baseMain,
            archive,
            archive,
            Core.Projects.ProjectGame.Sword,
            archive);
        Assert.False(analysis.HasLegacyBossTargetPatch);
    }

    [Fact]
    public void FixedHpIvCannotBeRewrittenThroughGuaranteedPerfectIvControl()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var archive = new SwShDynamaxAdventureArchive(
            SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
                .Select(entry => entry.EntryIndex == 1
                    ? entry with { Ivs = entry.Ivs with { Hp = 17 } }
                    : entry)
                .ToArray());
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(sourceArchive: archive));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
            value: "0");

        var encounter = result.Workflow.Encounters.Single(entry => entry.EntryIndex == 1);
        Assert.Equal(17, encounter.Ivs.Hp);
        Assert.Equal(0, encounter.GuaranteedPerfectIvs);
        Assert.Contains("HP 17", encounter.IvSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
            encounter.LayoutWritableFields);
        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Field == SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField
            && diagnostic.Message.Contains("fixed HP IV of 17", StringComparison.Ordinal));

        var valid = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "61");
        var sourceEdit = Assert.Single(valid.Session.PendingEdits);
        var tampered = valid.Session with
        {
            PendingEdits =
            [
                sourceEdit with
                {
                    Field = SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
                    NewValue = "0",
                    Summary = "Set Dynamax Adventure row 1 Guaranteed perfect IVs to 0.",
                },
            ],
        };

        var validation = service.Validate(temp.Paths, tampered);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot replace a fixed HP IV", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldCreatesPendingDynamaxAdventureIvEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
            value: "6");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.dynamaxAdventures", edit.Domain);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, edit.Field);
        Assert.Equal("dynamaxAdventure:0", edit.RecordId);
        Assert.Equal(6, result.Workflow.Encounters[0].GuaranteedPerfectIvs);
        Assert.Equal(-6, result.Workflow.Encounters[0].Ivs.Hp);
    }

    [Fact]
    public void UpdateFieldRejectsOutOfRangeDynamaxAdventureIvOverrides()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.IvAttackField,
            value: "-2");
        result = service.UpdateField(
            temp.Paths,
            result.Session,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.IvDefenseField,
            value: "80");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(-1, result.Workflow.Encounters[0].Ivs.Attack);
        Assert.Equal(-1, result.Workflow.Encounters[0].Ivs.Defense);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Field == SwShDynamaxAdventuresWorkflowService.IvDefenseField);
    }

    [Fact]
    public void UpdateFieldRejectsAmbiguousGuaranteedPerfectIvCount()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
            value: "1");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("not representable", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField, "0")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.BallItemIdField, "4")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.VersionField, "1")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.ShinyRollField, "1")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField, "1")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.OtGenderField, "1")]
    public void UpdateFieldRejectsUnsafeDynamaxAdventureFields(string field, string value)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: field,
            value: value);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("is not supported", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SwShDynamaxAdventuresWorkflowService.FormField, "0")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.AbilityField, "2")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.GigantamaxStateField, "2")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.Move0Field, "85")]
    public void UpdateFieldCreatesPendingRuntimeVerifiedDynamaxAdventurePokemonEdit(string field, string value)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: field,
            value: value);

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(field, edit.Field);
        Assert.Equal(value, edit.NewValue);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldCreatesPendingSpeciesEditAndResetsUnsafeNormalForm()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");

        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && edit.NewValue == "467");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.FormField
            && edit.NewValue == "0");
        Assert.Equal(467, result.Workflow.Encounters[0].SpeciesId);
        Assert.Equal(0, result.Workflow.Encounters[0].Form);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldRefreshesMoveOptionsForPendingSpecies()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp);
        WriteMoveData(temp, (10, true), (85, true));
        WriteLearnsetData(temp, recordCount: 200, (133, [(85, 50), (10, 70)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "133");

        var encounter = result.Workflow.Encounters.Single(row => row.EntryIndex == 1);
        Assert.Equal(133, encounter.SpeciesId);
        Assert.Contains(encounter.MoveOptions, option => option.Value == 85 && option.Label == "085 Thunderbolt");
        Assert.Contains(encounter.MoveOptions, option => option.Value == 3);
        Assert.DoesNotContain(encounter.MoveOptions, option => option.Value == 10);
    }

    [Fact]
    public void UpdateFieldRefreshesMoveOptionsForPendingLevel()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp);
        WriteMoveData(temp, (10, true), (85, true));
        WriteLearnsetData(temp, recordCount: 200, (25, [(85, 50), (10, 70)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "75");

        var encounter = result.Workflow.Encounters.Single(row => row.EntryIndex == 1);
        Assert.Equal(75, encounter.Level);
        Assert.Contains(encounter.MoveOptions, option => option.Value == 10);
        Assert.Contains(encounter.MoveOptions, option => option.Value == 85);
    }

    [Fact]
    public void PreviewDefaultsReturnsSafeTraitsAndLegalMovesForSpecies()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp);
        WriteMoveData(temp, (1, true), (2, true), (10, true), (85, true));
        WriteLearnsetData(temp, recordCount: 200, (133, [(1, 1), (2, 5), (10, 20), (85, 50)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var preview = service.PreviewDefaults(
            temp.Paths,
            session: null,
            entryIndex: 1,
            species: 133,
            form: 0,
            level: 60);

        Assert.DoesNotContain(preview.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal("0", preview.Changes.Single(change => change.Field == SwShDynamaxAdventuresWorkflowService.FormField).Value);
        Assert.Equal("0", preview.Changes.Single(change => change.Field == SwShDynamaxAdventuresWorkflowService.AbilityField).Value);
        Assert.Equal("1", preview.Changes.Single(change => change.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField).Value);
        Assert.Equal("85", preview.Changes.Single(change => change.Field == SwShDynamaxAdventuresWorkflowService.Move0Field).Value);
        Assert.Equal("10", preview.Changes.Single(change => change.Field == SwShDynamaxAdventuresWorkflowService.Move1Field).Value);
        Assert.Equal("2", preview.Changes.Single(change => change.Field == SwShDynamaxAdventuresWorkflowService.Move2Field).Value);
        Assert.Equal("1", preview.Changes.Single(change => change.Field == SwShDynamaxAdventuresWorkflowService.Move3Field).Value);
        Assert.Equal(new[] { 0, 1, 2 }, preview.AbilityOptions.Select(option => option.Value));
        Assert.Equal(new[] { 1, 2 }, preview.GigantamaxOptions.Select(option => option.Value));
        Assert.Contains(preview.MoveOptions, option => option.Value == 85);
    }

    [Fact]
    public void PreviewDefaultsHidesAbilityOptionsThatWouldRequireDynamaxAdventureTableRebuild()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var table = SwShDynamaxAdventureTestFixtures.CreateArchive().Write();
        SwShDynamaxAdventureTestFixtures.ClearTableField(table, entryIndex: 1, fieldIndex: 19);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            table);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp);
        WriteMoveData(temp, (1, true), (2, true), (10, true), (85, true));
        WriteLearnsetData(temp, recordCount: 200, (133, [(1, 1), (2, 5), (10, 20), (85, 50)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var preview = service.PreviewDefaults(
            temp.Paths,
            session: null,
            entryIndex: 1,
            species: 133,
            form: 0,
            level: 60);

        Assert.DoesNotContain(preview.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(new[] { 0 }, preview.AbilityOptions.Select(option => option.Value));
    }

    [Fact]
    public void PreviewDefaultsOmitsGigantamaxOptionForNonGigantamaxSpecies()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateSpeciesNameTable(467, (25, "Pikachu"), (133, "Eevee"), (467, "Magmortar")));
        WritePersonalData(
            temp,
            count: 468,
            presentSpecies: new HashSet<int> { 25, 133, 467 },
            hatchedSpeciesOverrides: new Dictionary<int, int> { [467] = 240 });
        WriteMoveData(temp, (1, true), (2, true), (10, true), (85, true));
        WriteLearnsetData(temp, recordCount: 468, (467, [(1, 1), (2, 5), (10, 20), (85, 50)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var preview = service.PreviewDefaults(
            temp.Paths,
            session: null,
            entryIndex: 1,
            species: 467,
            form: 0,
            level: 60);

        Assert.DoesNotContain(preview.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(new[] { 1 }, preview.GigantamaxOptions.Select(option => option.Value));
        Assert.Equal("1", preview.Changes.Single(change => change.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField).Value);
    }

    [Theory]
    [InlineData("144")]
    [InlineData("150")]
    [InlineData("151")]
    [InlineData("243")]
    [InlineData("384")]
    [InlineData("493")]
    [InlineData("638")]
    [InlineData("646")]
    [InlineData("716")]
    [InlineData("772")]
    [InlineData("789")]
    [InlineData("790")]
    [InlineData("793")]
    [InlineData("800")]
    [InlineData("803")]
    [InlineData("808")]
    [InlineData("809")]
    [InlineData("888")]
    [InlineData("891")]
    [InlineData("894")]
    [InlineData("896")]
    [InlineData("898")]
    public void ValidateRejectsSpecialSpeciesInNormalDynamaxAdventureRows(string species)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: species);

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains($"cannot use species {species}", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAllowsOrdinaryUniqueNormalDynamaxAdventureSpecies()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WritePersonalData(
            temp,
            count: 468,
            presentSpecies: new HashSet<int> { 25, 133, 467 },
            hatchedSpeciesOverrides: new Dictionary<int, int> { [467] = 240 });
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.True(validation.IsValid);
        Assert.DoesNotContain(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ValidateRejectsMissingSwordShieldSpeciesWhenPersonalDataIsAvailable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WritePersonalData(temp, count: 468, presentSpecies: new HashSet<int> { 25, 133 });
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use species 467", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsCannotDynamaxSpeciesForms()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WritePersonalData(
            temp,
            count: 468,
            presentSpecies: new HashSet<int> { 25, 133, 467 },
            cannotDynamaxSpecies: new HashSet<int> { 467 });
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use species 467", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsNonBasePersonalFormNormalReplacements()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WritePersonalData(
            temp,
            count: 865,
            presentSpecies: new HashSet<int> { 25, 133, 864 },
            personalFormOverrides: new Dictionary<int, int> { [864] = 1 });
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "864");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use species 864", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsNormalReplacementOutsideVerifiedSpeciesRange()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WritePersonalData(temp, count: 902, presentSpecies: new HashSet<int> { 25, 133, 901 });
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "901");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("between 1 and 898", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsIntroducedNormalDynamaxAdventureForms()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.FormField,
            value: "1");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use form 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAllowsPreservedVanillaNormalDynamaxAdventureForms()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var duplicateTargetEntries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 0 ? entry with { Form = 0 } : entry)
            .ToArray();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(duplicateTargetEntries).Write());
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(
                sourceArchive: new SwShDynamaxAdventureArchive(duplicateTargetEntries)));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "70");

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.True(validation.IsValid);
        Assert.DoesNotContain(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Only form 0 normal-route replacements", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldRejectsOutOfBattleRangeDynamaxAdventureLevels()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "0");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("between 1 and 100", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsDuplicateNormalDynamaxAdventureSpeciesForms()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "25");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use species 25", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsBossSpeciesOutsideVanillaDynamaxAdventureBossRoster()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WriteBossOnlyDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 226,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "895");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("hidden from the safe Dynamax Adventures editor", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsBossSpeciesSwapUntilMetadataCopyIsSupported()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WriteBossOnlyDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 226,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "150");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("hidden from the safe Dynamax Adventures editor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SwShDynamaxAdventuresWorkflowService.LevelField, "75")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.AbilityField, "1")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.GigantamaxStateField, "0")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.Move0Field, "94")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, "6")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.IvAttackField, "31")]
    public void ValidateRejectsBossRuntimeFieldEditsUntilMetadataCopyIsSupported(string field, string value)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WriteBossOnlyDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 226,
            field: field,
            value: value);

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("hidden from the safe Dynamax Adventures editor", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldRejectsGigantamaxStateForNonGigantamaxSpecies()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");
        var invalidGigantamax = service.UpdateField(
            temp.Paths,
            update.Session,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
            value: "2");

        Assert.DoesNotContain(invalidGigantamax.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField);
        Assert.Contains(invalidGigantamax.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && edit.NewValue == "467");
        Assert.Contains(invalidGigantamax.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldResetsGigantamaxStateWhenSpeciesCannotGigantamax()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            SwShDynamaxAdventureTestFixtures.CreateArchive().WriteEdits(
            [
                new(0, SwShDynamaxAdventureField.GigantamaxState, 2),
            ]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");

        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && edit.NewValue == "467");
        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            && edit.NewValue == "1");
        Assert.Equal(467, update.Workflow.Encounters[0].SpeciesId);
        Assert.Equal(1, update.Workflow.Encounters[0].GigantamaxState);

        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void UpdateFieldPreservesMovesWhenSpeciesChanges()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");

        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && edit.NewValue == "467");
        Assert.DoesNotContain(update.Session.PendingEdits, edit =>
            edit.Field is SwShDynamaxAdventuresWorkflowService.Move0Field
                or SwShDynamaxAdventuresWorkflowService.Move1Field
                or SwShDynamaxAdventuresWorkflowService.Move2Field
                or SwShDynamaxAdventuresWorkflowService.Move3Field);
        Assert.Equal([1, 2, 10, 20], update.Workflow.Encounters[0].Moves.Select(move => move.MoveId));
    }

    [Fact]
    public void UpdateFieldCreatesPendingDynamaxAdventureLevelEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "70");

        var edit = Assert.Single(update.Session.PendingEdits);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.LevelField, edit.Field);
        Assert.Equal("70", edit.NewValue);
        Assert.Equal(70, update.Workflow.Encounters[0].Level);
        Assert.Empty(update.Diagnostics);
    }

    [Fact]
    public void UpdateFieldRejectsEmptyDynamaxAdventureMoveSlots()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.Move0Field,
            value: "0");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("requires all four move slots", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldAllowsRestoringVanillaDynamaxAdventureZeroMoveSlots()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var vanillaEntries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 1
                ? entry with { Moves = [3, 0, 0, 0] }
                : entry)
            .ToArray();
        var editedEntries = vanillaEntries
            .Select(entry => entry.EntryIndex == 1
                ? entry with { Moves = [3, 4, 0, 0] }
                : entry)
            .ToArray();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(vanillaEntries).Write());
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            new SwShDynamaxAdventureArchive(editedEntries).Write());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.Move1Field,
            value: "0");

        var edit = Assert.Single(update.Session.PendingEdits);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.Move1Field, edit.Field);
        Assert.Equal("0", edit.NewValue);
        Assert.DoesNotContain(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldRejectsVanillaDynamaxAdventureZeroMoveSlotsAfterIdentityChange()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var entries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 1
                ? entry with { Moves = [3, 0, 0, 0] }
                : entry)
            .ToArray();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(entries).Write());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var speciesUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "26");
        var moveUpdate = service.UpdateField(
            temp.Paths,
            speciesUpdate.Session,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.Move1Field,
            value: "0");

        Assert.Equal(speciesUpdate.Session.PendingEdits.Count, moveUpdate.Session.PendingEdits.Count);
        Assert.Contains(moveUpdate.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("requires all four move slots", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyRestoreSequenceCanRemoveLayeredIdentityWithVanillaZeroMoveSlots()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var vanillaEntries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 1
                ? entry with { Moves = [3, 0, 0, 0] }
                : entry)
            .ToArray();
        var editedEntries = vanillaEntries
            .Select(entry => entry.EntryIndex == 1
                ? entry with { Species = 467, Moves = [3, 4, 0, 0] }
                : entry)
            .ToArray();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(vanillaEntries).Write());
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            new SwShDynamaxAdventureArchive(editedEntries).Write());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "25");
        restore = service.UpdateField(
            temp.Paths,
            restore.Session,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.Move1Field,
            value: "0");
        var validation = service.Validate(temp.Paths, restore.Session);
        var plan = service.CreateChangePlan(temp.Paths, restore.Session);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ValidateAllowsPreservedVanillaDynamaxAdventureZeroMoveSlots()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var entries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 1
                ? entry with { Moves = [3, 0, 0, 0] }
                : entry)
            .ToArray();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(entries).Write());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "70");
        var validation = service.Validate(temp.Paths, update.Session);

        Assert.True(validation.IsValid);
        Assert.DoesNotContain(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("empty Dynamax Adventure move slots", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsChangedIdentityWithVanillaDynamaxAdventureZeroMoveSlots()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var entries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 1
                ? entry with { Moves = [3, 0, 0, 0] }
                : entry)
            .ToArray();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(entries).Write());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "26");
        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("empty Dynamax Adventure move slots", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsNewDynamaxAdventureZeroMoveSlots()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var entries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 1
                ? entry with { Moves = [3, 0, 0, 0] }
                : entry)
            .ToArray();
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            new SwShDynamaxAdventureArchive(entries).Write());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "70");
        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("empty Dynamax Adventure move slots", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsUnusableDynamaxAdventureMoveIdsWhenMoveDataIsAvailable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WriteMoveData(
            temp,
            (1, true),
            (2, true),
            (3, true),
            (4, true),
            (5, true),
            (6, true),
            (10, true),
            (20, true),
            (85, false));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.Move0Field,
            value: "85");
        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use move 85", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsDynamaxAdventureMovesThatSpeciesCannotLearn()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WritePersonalData(temp, count: 500, presentSpecies: new HashSet<int> { 25, 133 });
        WriteMoveData(
            temp,
            (1, true),
            (2, true),
            (3, true),
            (4, true),
            (5, true),
            (6, true),
            (10, true),
            (20, true),
            (85, true));
        WriteLearnsetData(
            temp,
            recordCount: 500,
            (133, [(1, 1), (2, 1), (10, 1), (20, 1)]),
            (25, [(3, 1), (4, 1), (5, 1), (6, 1)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.Move0Field,
            value: "85");
        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use move 85", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAllowsPreservedVanillaDynamaxAdventureMoveCompatibilityException()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var entries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 0
                ? entry with { Moves = [85, 2, 10, 20] }
                : entry)
            .ToArray();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(entries).Write());
        WritePersonalData(temp, count: 500, presentSpecies: new HashSet<int> { 25, 133 });
        WriteMoveData(
            temp,
            (1, true),
            (2, true),
            (3, true),
            (4, true),
            (5, true),
            (6, true),
            (10, true),
            (20, true),
            (85, true));
        WriteLearnsetData(
            temp,
            recordCount: 500,
            (133, [(1, 1), (2, 1), (10, 1), (20, 1)]),
            (25, [(3, 1), (4, 1), (5, 1), (6, 1)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "70");
        var validation = service.Validate(temp.Paths, update.Session);

        Assert.True(validation.IsValid);
        Assert.DoesNotContain(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot normally learn", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsVanillaDynamaxAdventureMoveExceptionBelowVanillaLevel()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var entries = SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
            .Select(entry => entry.EntryIndex == 0
                ? entry with { Moves = [85, 2, 10, 20] }
                : entry)
            .ToArray();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(entries).Write());
        WritePersonalData(temp, count: 500, presentSpecies: new HashSet<int> { 25, 133 });
        WriteMoveData(
            temp,
            (1, true),
            (2, true),
            (3, true),
            (4, true),
            (5, true),
            (6, true),
            (10, true),
            (20, true),
            (85, true));
        WriteLearnsetData(
            temp,
            recordCount: 500,
            (133, [(1, 1), (2, 1), (10, 1), (20, 1)]),
            (25, [(3, 1), (4, 1), (5, 1), (6, 1)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "60");
        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot normally learn", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredDynamaxAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.LevelField, "70");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, "6");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.IvAttackField, "31");

        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var planWrite = Assert.Single(plan.Writes);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, planWrite.TargetRelativePath);
        var writtenFile = Assert.Single(apply.WrittenFiles);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, writtenFile.RelativePath);

        var outputPath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(ReadBaseDynamaxAdventureTableLength(temp), new FileInfo(outputPath).Length);
        var output = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(outputPath));
        var entry = output.Entries[0];
        Assert.Equal(133, entry.Species);
        Assert.Equal(70, entry.Level);
        Assert.Equal(1, entry.Moves[0]);
        Assert.Equal(2, entry.Moves[1]);
        Assert.Equal(10, entry.Moves[2]);
        Assert.Equal(20, entry.Moves[3]);
        Assert.Equal(-6, entry.Ivs.Hp);
        Assert.Equal(31, entry.Ivs.Attack);
        Assert.True(entry.IsStoryProgressGated);
        Assert.Equal(0x1122334455667788UL, entry.SingleCaptureFlagBlock);
        Assert.Equal(0x8877665544332211UL, entry.UiMessageId);
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "exefs", "main")));
    }

    [Fact]
    public void ApplyMoveEditWritesOnlyInPlaceDynamaxAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 1, SwShDynamaxAdventuresWorkflowService.Move0Field, "85");
        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var planWrite = Assert.Single(plan.Writes);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, planWrite.TargetRelativePath);
        var writtenFile = Assert.Single(apply.WrittenFiles);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, writtenFile.RelativePath);

        var outputPath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(ReadBaseDynamaxAdventureTableLength(temp), new FileInfo(outputPath).Length);
        var output = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(85, output.Entries[1].Moves[0]);
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "exefs", "main")));
    }

    [Theory]
    [InlineData("exefs/main")]
    [InlineData("romfs/bin/pml/personal/personal_total.bin")]
    [InlineData("romfs/bin/pml/waza_oboe/wazaoboe_total.bin")]
    [InlineData("romfs/bin/pml/waza/waza0085.wazabin")]
    public void ChangePlanGuardsOutputDependencyThatAppearsBeforeApplyScope(
        string dependencyRelativePath)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.Move0Field,
            value: "85");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var tableWrite = Assert.Single(reviewedPlan.Writes);
        Assert.Contains(tableWrite.Sources, source =>
            source.Layer == Core.Files.ProjectFileLayer.Generated
            && source.RelativePath == dependencyRelativePath);

        var basePath = dependencyRelativePath.StartsWith("romfs/", StringComparison.Ordinal)
            ? Path.Combine(
                temp.BaseRomFsPath,
                dependencyRelativePath["romfs/".Length..]
                    .Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(
                temp.BaseExeFsPath,
                dependencyRelativePath["exefs/".Length..]
                    .Replace('/', Path.DirectorySeparatorChar));
        temp.WriteOutputFile(dependencyRelativePath, File.ReadAllBytes(basePath));

        var acquired = SwShChangePlanSourceGuard.TryAcquireApplyScope(
            temp.Paths,
            reviewedPlan,
            out var scope,
            out var diagnostics,
            preserveExplicitSourceLayers: true);

        scope?.Dispose();
        Assert.False(acquired);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("changed", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath
                .Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ApplyAbilityEditWritesOnlyInPlaceDynamaxAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 1, SwShDynamaxAdventuresWorkflowService.AbilityField, "2");
        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var planWrite = Assert.Single(plan.Writes);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, planWrite.TargetRelativePath);
        var writtenFile = Assert.Single(apply.WrittenFiles);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, writtenFile.RelativePath);

        var outputPath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(ReadBaseDynamaxAdventureTableLength(temp), new FileInfo(outputPath).Length);
        var output = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(2, output.Entries[1].Ability);
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "exefs", "main")));
    }

    [Fact]
    public void CreateChangePlanRejectsNonRestoreFromMismatchedDynamaxAdventureLayout()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var expandedTable = SwShDynamaxAdventureTestFixtures.CreateArchive()
            .Write()
            .Concat(new byte[] { 0 })
            .ToArray();
        temp.WriteOutputFile(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, expandedTable);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 1, SwShDynamaxAdventuresWorkflowService.Move0Field, "85");

        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateChangePlanRejectsNonRestoreFromSameLengthDynamaxAdventureLayoutMismatch()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var table = SwShDynamaxAdventureTestFixtures.CreateArchive().Write();
        SwShDynamaxAdventureTestFixtures.ClearTableField(table, entryIndex: 1, fieldIndex: 19);
        temp.WriteOutputFile(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, table);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 1, SwShDynamaxAdventuresWorkflowService.Move0Field, "85");

        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.False(plan.CanApply);
        Assert.Equal(ReadBaseDynamaxAdventureTableLength(temp), table.Length);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyRestoreCanRemoveMismatchedDynamaxAdventureLayout(bool sameLengthMismatch)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var editedTable = sameLengthMismatch
            ? SwShDynamaxAdventureTestFixtures.CreateArchive().Write()
            : SwShDynamaxAdventureTestFixtures.CreateArchive()
                .WriteEdits([new(1, SwShDynamaxAdventureField.Move0, 85)])
                .Concat(new byte[] { 0 })
                .ToArray();
        if (sameLengthMismatch)
        {
            SwShDynamaxAdventureTestFixtures.ClearTableField(editedTable, entryIndex: 1, fieldIndex: 19);
        }
        temp.WriteOutputFile(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, editedTable);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var blockedEdit = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShDynamaxAdventuresWorkflowService.Move0Field,
            "85");
        Assert.Empty(blockedEdit.Session.PendingEdits);
        Assert.True(blockedEdit.Workflow.CanRestoreVanillaTable);
        Assert.Contains(blockedEdit.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);
        Assert.Equal(SwShDynamaxAdventuresEditSessionService.RestoreVanillaTableSummary, Assert.Single(restore.Session.PendingEdits).Summary);
        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(plan.CanApply);
        Assert.Contains(plan.Writes, write =>
            write.TargetRelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath
            && write.Reason.Contains("all layered Dynamax Adventures table changes", StringComparison.Ordinal));

        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Contains(apply.WrittenFiles, file =>
            file.RelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar))));
        var reloaded = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(
            new Core.Projects.ProjectWorkspaceService().Open(temp.Paths));
        Assert.False(reloaded.CanRestoreVanillaTable);
        Assert.All(reloaded.Encounters, encounter => Assert.Equal(Core.Files.ProjectFileLayer.Base, encounter.Provenance.SourceLayer));
    }

    [Theory]
    [InlineData(SwShDynamaxAdventureField.Species, 0)]
    [InlineData(SwShDynamaxAdventureField.Species, 999)]
    [InlineData(SwShDynamaxAdventureField.Level, 101)]
    [InlineData(SwShDynamaxAdventureField.Move0, 827)]
    [InlineData(SwShDynamaxAdventureField.Form, 2)]
    public void ApplyRestoreDiscardsInvalidRowsThroughContractSafeVanillaProjection(
        SwShDynamaxAdventureField field,
        int value)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var basePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var baseArchive = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(basePath));
        var entryIndex = field == SwShDynamaxAdventureField.Form ? 0 : 1;
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            baseArchive.WriteEditsPreservingLayout([new(entryIndex, field, value)]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);

        Assert.True(restore.Workflow.CanRestoreVanillaTable);
        Assert.True(restore.Workflow.UsesVanillaRecoveryProjection);
        Assert.DoesNotContain(restore.Workflow.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));
        Assert.All(restore.Workflow.Encounters, encounter =>
        {
            Assert.False(encounter.IsEditable);
            Assert.Empty(encounter.LayoutWritableFields);
            Assert.InRange(encounter.SpeciesId, 1, 898);
            Assert.InRange(encounter.Level, 1, 100);
            Assert.All(encounter.Moves, move => Assert.InRange(move.MoveId, 0, 826));
        });
        var projected = restore.Workflow.Encounters.Single(encounter => encounter.EntryIndex == entryIndex);
        var vanilla = baseArchive.Entries[entryIndex];
        Assert.Equal(vanilla.Species, projected.SpeciesId);
        Assert.Equal(vanilla.Form, projected.Form);
        Assert.Equal(vanilla.Level, projected.Level);
        Assert.Equal(vanilla.Moves[0], projected.Moves[0].MoveId);
        Assert.Equal(
            SwShDynamaxAdventuresEditSessionService.RestoreVanillaTableSummary,
            Assert.Single(restore.Session.PendingEdits).Summary);

        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(plan.CanApply);
        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void StageRestoreRejectsInvalidLayeredRowsWhenEffectiveMainIsForeign()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var basePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var baseArchive = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(basePath));
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            baseArchive.WriteEditsPreservingLayout([
                new(1, SwShDynamaxAdventureField.Species, 999),
            ]));
        var foreignMain = SwShDynamaxAdventureTestFixtures.CreateCompatibleMain().ToArray();
        foreignMain[0x08] ^= 0x01;
        temp.WriteOutputFile("exefs/main", foreignMain);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);

        Assert.True(restore.Workflow.UsesVanillaRecoveryProjection);
        Assert.False(restore.Workflow.CanRestoreVanillaTable);
        Assert.Equal("blocked", restore.Workflow.InstallStatus);
        Assert.Empty(restore.Session.PendingEdits);
        Assert.Contains(restore.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.File == "exefs/main");
    }

    [Fact]
    public void ApplyRestoreAllowsMalformedLayerWhenPersonalDataIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        File.Delete(Path.Combine(
            temp.BaseRomFsPath,
            SwShPersonalTable.PersonalDataRelativePath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar)));
        var basePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var baseArchive = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(basePath));
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            baseArchive.WriteEditsPreservingLayout([
                new(1, SwShDynamaxAdventureField.Species, 999),
            ]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);

        Assert.True(restore.Workflow.CanRestoreVanillaTable);
        Assert.True(restore.Workflow.UsesVanillaRecoveryProjection);
        Assert.Contains(restore.Workflow.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("personal data is missing or unreadable", StringComparison.Ordinal));
        Assert.DoesNotContain(restore.Workflow.Diagnostics, diagnostic =>
            diagnostic.Message.StartsWith("Verified base Dynamax Adventures row", StringComparison.Ordinal)
            && diagnostic.Message.Contains("form", StringComparison.Ordinal));
        Assert.All(restore.Workflow.Encounters, encounter =>
        {
            Assert.False(encounter.IsEditable);
            Assert.Empty(encounter.LayoutWritableFields);
        });

        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(plan.CanApply);
        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void StageRestoreRetryIsIdempotentForCanonicalRestoreSession()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var first = service.StageVanillaTableRestore(temp.Paths, session: null);

        var retry = service.StageVanillaTableRestore(temp.Paths, first.Session);

        Assert.Equal(first.Session, retry.Session);
        Assert.DoesNotContain(retry.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Contains(retry.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("already staged", StringComparison.Ordinal));
    }

    [Fact]
    public void StageRestoreRetrySanitizesMarkerWhenProjectBecomesReadOnly()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var first = service.StageVanillaTableRestore(temp.Paths, session: null);
        Assert.Equal(
            SwShDynamaxAdventuresEditSessionService.RestoreVanillaTableSummary,
            Assert.Single(first.Session.PendingEdits).Summary);

        var retry = service.StageVanillaTableRestore(
            temp.Paths with { OutputRootPath = null },
            first.Session);

        Assert.Empty(retry.Session.PendingEdits);
        Assert.Contains(retry.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void StageRestoreSanitizesRuntimeNullPendingListWithoutThrowing()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var malformed = service.StartSession() with { PendingEdits = null! };

        var restore = service.StageVanillaTableRestore(temp.Paths, malformed);

        Assert.Empty(restore.Session.PendingEdits);
        Assert.Contains(restore.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("missing its pending action list", StringComparison.Ordinal));
    }

    [Fact]
    public void StageRestoreSanitizesRuntimeNullPendingActionWithoutThrowing()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var malformed = service.StartSession() with { PendingEdits = [null!] };

        var restore = service.StageVanillaTableRestore(temp.Paths, malformed);

        Assert.Empty(restore.Session.PendingEdits);
        Assert.Contains(restore.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("null pending action", StringComparison.Ordinal));
    }

    [Fact]
    public void StageRestoreRejectsAndSanitizesUnrelatedPendingSession()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var editService = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var rowEdit = editService.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.LevelField,
            value: "61");
        Assert.Single(rowEdit.Session.PendingEdits);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var restoreService = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = restoreService.StageVanillaTableRestore(temp.Paths, rowEdit.Session);

        Assert.Empty(restore.Session.PendingEdits);
        Assert.Contains(restore.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("empty edit session", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateChangePlanRejectsSummaryOnlyRestoreMarkerTampering()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);
        var marker = Assert.Single(restore.Session.PendingEdits);
        var tampered = restore.Session with
        {
            PendingEdits =
            [
                marker with
                {
                    Field = SwShDynamaxAdventuresWorkflowService.SpeciesField,
                    NewValue = "133",
                },
            ],
        };

        var validation = service.Validate(temp.Paths, tampered);
        var plan = service.CreateChangePlan(temp.Paths, tampered);

        Assert.False(validation.IsValid);
        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.True(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void CreateChangePlanRejectsRestoreMarkerMixedWithRowEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);
        var marker = Assert.Single(restore.Session.PendingEdits);
        var mixed = restore.Session with
        {
            PendingEdits =
            [
                marker,
                marker with
                {
                    Summary = "Set Dynamax Adventure row 0 Species to 133.",
                    Field = SwShDynamaxAdventuresWorkflowService.SpeciesField,
                    NewValue = "133",
                },
            ],
        };

        var validation = service.Validate(temp.Paths, mixed);
        var plan = service.CreateChangePlan(temp.Paths, mixed);

        Assert.False(validation.IsValid);
        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot be combined", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyRestoreRejectsReviewedPlanAfterRecoverySourceDrift(bool mutateMain)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var tablePath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);
        var reviewedPlan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(reviewedPlan.CanApply);

        if (mutateMain)
        {
            var baseMain = SwShDynamaxAdventureTestFixtures.CreateCompatibleMain();
            var nso = NsoFile.Parse(baseMain);
            temp.WriteOutputFile(
                "exefs/main",
                nso.Write(textDecompressedData: nso.Text.DecompressedData.Concat(new byte[] { 0xA5 }).ToArray()));
        }
        else
        {
            File.WriteAllBytes(tablePath, File.ReadAllBytes(tablePath).Concat(new byte[] { 0xA5 }).ToArray());
        }

        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && (diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase)
                || diagnostic.Message.Contains("changed", StringComparison.OrdinalIgnoreCase)));
        Assert.True(File.Exists(tablePath));
        if (mutateMain)
        {
            Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, "exefs", "main")));
        }
    }

    [Fact]
    public void ApplyRestoreDiscardsSameLayoutHiddenNormalRowChange()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var basePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var baseArchive = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(basePath));
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            baseArchive.WriteEditsPreservingLayout([
                new(1, SwShDynamaxAdventureField.Species, 144),
            ]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);

        Assert.True(restore.Workflow.CanRestoreVanillaTable);
        Assert.True(restore.Workflow.UsesVanillaRecoveryProjection);
        Assert.DoesNotContain(restore.Workflow.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));
        Assert.Contains(restore.Workflow.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("hidden normal or boss row", StringComparison.Ordinal));
        Assert.All(restore.Workflow.Encounters, encounter =>
        {
            Assert.False(encounter.IsEditable);
            Assert.Empty(encounter.LayoutWritableFields);
        });

        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(plan.CanApply);
        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(SwShDynamaxAdventureField.BallItemId, 5, SwShDynamaxAdventureField.Version, 2)]
    [InlineData(SwShDynamaxAdventureField.ShinyRoll, 2, SwShDynamaxAdventureField.IsSingleCapture, 0)]
    public void ApplyRestoreDiscardsFullBossRecordChanges(
        SwShDynamaxAdventureField firstField,
        int firstValue,
        SwShDynamaxAdventureField secondField,
        int secondValue)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var baseArchive = new SwShDynamaxAdventureArchive(
            CreateRepairableBossArchive().Entries
                .Select(entry => entry.EntryIndex == SwShDynamaxAdventureSafetyRules.BossEntryStartIndex
                    ? entry with { Version = 1 }
                    : entry)
                .ToArray());
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            baseArchive.Write());
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(baseArchive));
        var basePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var parsedBase = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(basePath));
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            parsedBase.WriteEditsPreservingLayout([
                new(SwShDynamaxAdventureSafetyRules.BossEntryStartIndex, firstField, firstValue),
                new(SwShDynamaxAdventureSafetyRules.BossEntryStartIndex, secondField, secondValue),
            ]));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);

        Assert.True(restore.Workflow.CanRestoreVanillaTable);
        Assert.True(restore.Workflow.UsesVanillaRecoveryProjection);
        Assert.Contains(restore.Workflow.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("hidden normal or boss row", StringComparison.Ordinal));
        Assert.All(restore.Workflow.Encounters, encounter =>
        {
            Assert.False(encounter.IsEditable);
            Assert.Empty(encounter.LayoutWritableFields);
        });

        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(plan.CanApply);
        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyRestoreReconcilesSynchronizedSourceMainAndPreservesForeignPayload()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var baseArchive = CreateRowCountArchive(SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount);
        var baseBytes = baseArchive.Write();
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            baseBytes);
        var parsedBase = SwShDynamaxAdventureArchive.Parse(baseBytes);
        var sourceBytes = parsedBase.WriteEditsPreservingLayout([
            new(1, SwShDynamaxAdventureField.Species, 144),
        ]);
        var sourceArchive = SwShDynamaxAdventureArchive.Parse(sourceBytes);
        temp.WriteOutputFile(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, sourceBytes);
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(baseArchive);
        temp.WriteBaseExeFsFile("main", baseMain);
        var marker = Enumerable.Range(1, 24).Select(value => checked((byte)value)).ToArray();
        var baseNso = NsoFile.Parse(baseMain);
        var mainWithPayload = baseNso.Write(
            textDecompressedData: baseNso.Text.DecompressedData.Concat(marker).ToArray());
        temp.WriteOutputFile(
            "exefs/main",
            SwShDynamaxAdventuresMainPatcher.Reconcile(
                mainWithPayload,
                baseMain,
                sourceArchive,
                baseArchive,
                Core.Projects.ProjectGame.Sword,
                sourceArchive));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);

        Assert.True(restore.Workflow.CanRestoreVanillaTable);
        Assert.True(restore.Workflow.UsesVanillaRecoveryProjection);
        Assert.Equal("repairable", restore.Workflow.InstallStatus);
        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(plan.CanApply);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "exefs/main");

        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        Assert.True(File.Exists(outputMainPath));
        var restoredMain = File.ReadAllBytes(outputMainPath);
        Assert.True(NsoFile.Parse(restoredMain).Text.DecompressedData.AsSpan()[^marker.Length..].SequenceEqual(marker));
        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            restoredMain,
            baseMain,
            baseArchive,
            baseArchive,
            Core.Projects.ProjectGame.Sword);
        Assert.Equal(SwShDynamaxAdventuresMainKind.Vanilla, analysis.Kind);
    }

    [Theory]
    [InlineData(272)]
    [InlineData(274)]
    public void ApplyRestoreProjectsVerifiedBaseForLayeredRowCountMismatch(int layeredRowCount)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var baseArchive = CreateRowCountArchive(SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            baseArchive.Write());
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(baseArchive));
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            CreateRowCountArchive(layeredRowCount).Write());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);

        Assert.True(restore.Workflow.CanRestoreVanillaTable);
        Assert.True(restore.Workflow.UsesVanillaRecoveryProjection);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount, restore.Workflow.Encounters.Count);
        Assert.All(restore.Workflow.Encounters, encounter =>
        {
            Assert.False(encounter.IsEditable);
            Assert.Empty(encounter.LayoutWritableFields);
        });
        Assert.Contains(restore.Workflow.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));

        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(plan.CanApply);
        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void StageRestoreRejectsLayeredRowCountMismatchWithPartialSourceSummary()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var baseArchive = CreateRowCountArchive(SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            baseArchive.Write());
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(baseArchive);
        temp.WriteBaseExeFsFile("main", baseMain);
        var sourceArchive = new SwShDynamaxAdventureArchive(
            CreateRowCountArchive(272).Entries
                .Select(entry => entry.EntryIndex == 1
                    ? entry with { Species = 144 }
                    : entry)
                .ToArray());
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            sourceArchive.Write());
        temp.WriteOutputFile(
            "exefs/main",
            SwShDynamaxAdventuresMainPatcher.Apply(
                baseMain,
                sourceArchive,
                patchCommandValidatorMirrors: false));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var restore = service.StageVanillaTableRestore(temp.Paths, session: null);

        Assert.True(restore.Workflow.UsesVanillaRecoveryProjection);
        Assert.False(restore.Workflow.CanRestoreVanillaTable);
        Assert.Empty(restore.Session.PendingEdits);
        Assert.Contains(restore.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.File == "exefs/main");
    }

    [Fact]
    public void UpdateFieldRejectsAbilityEditThatRequiresDynamaxAdventureTableRebuild()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var table = SwShDynamaxAdventureTestFixtures.CreateArchive().Write();
        SwShDynamaxAdventureTestFixtures.ClearTableField(table, entryIndex: 1, fieldIndex: 19);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            table);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 1, SwShDynamaxAdventuresWorkflowService.AbilityField, "1");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("omitted FlatBuffer default", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplySpeciesEditWritesCorrectedDynamaxAdventureMainMirror()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateCompatibleMain());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.SpeciesField, "467");
        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "exefs/main");

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == "exefs/main");

        var tablePath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(ReadBaseDynamaxAdventureTableLength(temp), new FileInfo(tablePath).Length);
        var output = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(tablePath));
        Assert.Equal(467, output.Entries[0].Species);

        var mainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var nso = NsoFile.Parse(File.ReadAllBytes(mainPath));
        var ro = nso.Ro.DecompressedData.AsSpan();
        var summaryOffset = SwShDynamaxAdventuresMainPatcher.SummaryOffset;
        Assert.Equal(0x00774054, summaryOffset);
        Assert.Equal(1, ro[summaryOffset]);
        Assert.Equal(467, BinaryPrimitives.ReadInt16LittleEndian(ro.Slice(summaryOffset + 2, sizeof(short))));
        Assert.Equal(1, ro[summaryOffset + 5]);

        var text = nso.Text.DecompressedData.AsSpan();
        Assert.Equal(0xD503201Fu, ReadInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalSpeciesPresentMismatchBranchOffset));
        Assert.Equal(0xD503201Fu, ReadInstruction(text, SwShDynamaxAdventuresMainPatcher.NestSpeciesPresentMismatchBranchOffset));
        Assert.Equal(0xD503201Fu, ReadInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiSpeciesPresentMismatchBranchOffset));
    }

    [Fact]
    public void ApplySpeciesEditWritesShieldDynamaxAdventureMainMirror()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.SelectedGame = Core.Projects.ProjectGame.Shield;
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(
                SwShDynamaxAdventuresMainPatcher.ShieldCommandValidatorOffsetDelta,
                SwShDynamaxAdventuresMainPatcher.ShieldBuildId));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.SpeciesField, "467");
        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var mainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var nso = NsoFile.Parse(File.ReadAllBytes(mainPath));
        var text = nso.Text.DecompressedData.AsSpan();
        Assert.Equal(
            0xD503201Fu,
            ReadInstruction(
                text,
                SwShDynamaxAdventuresMainPatcher.LocalSpeciesPresentMismatchBranchOffset
                    + SwShDynamaxAdventuresMainPatcher.ShieldCommandValidatorOffsetDelta));
        Assert.Equal(
            0xD503201Fu,
            ReadInstruction(
                text,
                SwShDynamaxAdventuresMainPatcher.NestSpeciesPresentMismatchBranchOffset
                    + SwShDynamaxAdventuresMainPatcher.ShieldCommandValidatorOffsetDelta));
        Assert.Equal(
            0xD503201Fu,
            ReadInstruction(
                text,
                SwShDynamaxAdventuresMainPatcher.DaiSpeciesPresentMismatchBranchOffset
                    + SwShDynamaxAdventuresMainPatcher.ShieldCommandValidatorOffsetDelta));
    }

    [Fact]
    public void ApplyGigantamaxEditWritesCommandValidatorMirror()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateCompatibleMain());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var update = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.GigantamaxStateField, "2");
        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "exefs/main");

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var tablePath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        var output = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(tablePath));
        Assert.Equal(2, output.Entries[0].GigantamaxState);

        var mainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var nso = NsoFile.Parse(File.ReadAllBytes(mainPath));
        var text = nso.Text.DecompressedData.AsSpan();
        Assert.Equal(0xD503201Fu, ReadInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalGigantamaxMismatchBranchOffset));
        Assert.Equal(0xD503201Fu, ReadInstruction(text, SwShDynamaxAdventuresMainPatcher.NestGigantamaxMismatchBranchOffset));
        Assert.Equal(0xD503201Fu, ReadInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiGigantamaxMismatchBranchOffset));
    }

    [Fact]
    public void UpdateFieldRejectsRemovedBossTargetField()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var result = service.UpdateField(
            temp.Paths,
            null,
            0,
            "bossTargetSpecies",
            "150");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void OmittedGigantamaxFieldExposesOnlyDefaultAndRejectsNondefaultStage()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var tablePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var bytes = File.ReadAllBytes(tablePath);
        SwShDynamaxAdventureTestFixtures.ClearTableField(bytes, entryIndex: 1, fieldIndex: 4);
        File.WriteAllBytes(tablePath, bytes);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var workflow = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
            "0").Workflow;
        var encounter = workflow.Encounters.Single(candidate => candidate.EntryIndex == 1);
        Assert.Equal([0], encounter.GigantamaxOptions.Select(option => option.Value));
        Assert.DoesNotContain(
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
            encounter.LayoutWritableFields);

        var rejected = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
            "1");
        Assert.Empty(rejected.Session.PendingEdits);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("omitted FlatBuffer default", StringComparison.Ordinal));

        var preview = service.PreviewDefaults(temp.Paths, null, 1, species: 25, form: 0, level: 60);
        Assert.DoesNotContain(preview.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal([0], preview.GigantamaxOptions.Select(option => option.Value));
        Assert.Equal(
            "0",
            preview.Changes.Single(change =>
                change.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField).Value);
    }

    [Fact]
    public void OmittedMoveSlotRejectsMoveStageButKeepsOtherRowFieldsEditable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var tablePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var bytes = File.ReadAllBytes(tablePath);
        SwShDynamaxAdventureTestFixtures.ClearTableField(bytes, entryIndex: 1, fieldIndex: 22);
        File.WriteAllBytes(tablePath, bytes);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var rejected = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShDynamaxAdventuresWorkflowService.Move1Field,
            "85");
        Assert.Empty(rejected.Session.PendingEdits);
        var encounter = rejected.Workflow.Encounters.Single(candidate => candidate.EntryIndex == 1);
        Assert.DoesNotContain(SwShDynamaxAdventuresWorkflowService.Move1Field, encounter.LayoutWritableFields);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("omitted FlatBuffer default", StringComparison.Ordinal));

        var levelUpdate = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShDynamaxAdventuresWorkflowService.LevelField,
            "61");
        Assert.Single(levelUpdate.Session.PendingEdits);

        var preview = service.PreviewDefaults(temp.Paths, null, 1, species: 25, form: 0, level: 60);
        Assert.Empty(preview.Changes);
        Assert.Contains(preview.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Field == SwShDynamaxAdventuresWorkflowService.Move1Field);
    }

    [Fact]
    public void PendingSpeciesCanBeRestoredToVerifiedVanillaIdentity()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var changed = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "467");

        var restored = service.UpdateField(
            temp.Paths,
            changed.Session,
            1,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "25");

        var edit = Assert.Single(
            restored.Session.PendingEdits,
            pending => pending.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField);
        Assert.Equal("25", edit.NewValue);
        Assert.DoesNotContain(restored.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void RestoringVanillaSpeciesRemovesOnlyGeneratedFormReset()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var changed = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "467");

        Assert.Contains(changed.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.FormField
            && edit.NewValue == "0"
            && edit.Summary.StartsWith("Automatically reset", StringComparison.Ordinal));

        var restored = service.UpdateField(
            temp.Paths,
            changed.Session,
            0,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "133");

        var encounter = restored.Workflow.Encounters.Single(candidate => candidate.EntryIndex == 0);
        Assert.Equal(133, encounter.SpeciesId);
        Assert.Equal("Eevee", encounter.Species);
        Assert.Equal(1, encounter.Form);
        Assert.DoesNotContain(
            restored.Session.PendingEdits,
            edit => edit.Field == SwShDynamaxAdventuresWorkflowService.FormField);
        Assert.DoesNotContain(restored.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void RestoringVanillaSpeciesPreservesExplicitFormChoice()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var changed = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "467");
        var explicitForm = service.UpdateField(
            temp.Paths,
            changed.Session,
            0,
            SwShDynamaxAdventuresWorkflowService.FormField,
            "0");
        var restored = service.UpdateField(
            temp.Paths,
            explicitForm.Session,
            0,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "133");

        var formEdit = Assert.Single(
            restored.Session.PendingEdits,
            edit => edit.Field == SwShDynamaxAdventuresWorkflowService.FormField);
        Assert.Equal("0", formEdit.NewValue);
        Assert.StartsWith("Set Dynamax Adventure", formEdit.Summary, StringComparison.Ordinal);
        Assert.Equal(0, restored.Workflow.Encounters.Single(candidate => candidate.EntryIndex == 0).Form);
        Assert.DoesNotContain(restored.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void RestoringVanillaSpeciesRemovesGeneratedGigantamaxReset()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var archive = new SwShDynamaxAdventureArchive(
            SwShDynamaxAdventureTestFixtures.CreateArchive().Entries
                .Select(entry => entry.EntryIndex == 0
                    ? entry with { GigantamaxState = 2 }
                    : entry)
                .ToArray());
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(sourceArchive: archive));
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var changed = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "467");

        Assert.Contains(changed.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            && edit.NewValue == "1"
            && edit.Summary.StartsWith("Automatically reset", StringComparison.Ordinal));

        var restored = service.UpdateField(
            temp.Paths,
            changed.Session,
            0,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "133");

        Assert.Equal(2, restored.Workflow.Encounters.Single(candidate => candidate.EntryIndex == 0).GigantamaxState);
        Assert.DoesNotContain(
            restored.Session.PendingEdits,
            edit => edit.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField);
        Assert.DoesNotContain(restored.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void PendingReplacementSpeciesCannotReuseVanillaAlternateForm()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var changed = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            "467");

        var rejected = service.UpdateField(
            temp.Paths,
            changed.Session,
            0,
            SwShDynamaxAdventuresWorkflowService.FormField,
            "1");

        Assert.Equal(0, rejected.Workflow.Encounters.Single(candidate => candidate.EntryIndex == 0).Form);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Field == SwShDynamaxAdventuresWorkflowService.FormField);
    }

    [Fact]
    public void PendingGuaranteedPerfectIvChangesRefreshWorkflowStats()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var cleared = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
            "0");
        var restored = service.UpdateField(
            temp.Paths,
            cleared.Session,
            0,
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
            "4");

        Assert.Equal(0, cleared.Workflow.Stats.GuaranteedPerfectIvEncounterCount);
        Assert.Equal(1, restored.Workflow.Stats.GuaranteedPerfectIvEncounterCount);
        Assert.Equal(cleared.Workflow.Stats.SourceFileCount, restored.Workflow.Stats.SourceFileCount);
    }

    [Theory]
    [InlineData(SwShDynamaxAdventuresWorkflowService.SpeciesField, "999")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.LevelField, "101")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.Move0Field, "827")]
    [InlineData(SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, "1")]
    public void MalformedPendingSessionNeverOverlaysUnsafeWorkflowValues(string field, string value)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();
        var valid = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShDynamaxAdventuresWorkflowService.LevelField,
            "61");
        var seedEdit = Assert.Single(valid.Session.PendingEdits);
        var tamperedSession = valid.Session with
        {
            PendingEdits =
            [
                seedEdit with
                {
                    Field = field,
                    NewValue = value,
                },
            ],
        };

        var update = service.UpdateField(
            temp.Paths,
            tamperedSession,
            1,
            SwShDynamaxAdventuresWorkflowService.IvAttackField,
            "31");
        var encounter = update.Workflow.Encounters.Single(candidate => candidate.EntryIndex == 1);
        Assert.Equal(25, encounter.SpeciesId);
        Assert.Equal(60, encounter.Level);
        Assert.All(encounter.Moves, move => Assert.InRange(move.MoveId, 0, 826));
        Assert.Contains(update.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var preview = service.PreviewDefaults(
            temp.Paths,
            tamperedSession,
            1,
            species: 25,
            form: 0,
            level: 60);
        Assert.Empty(preview.Changes);
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var validation = service.Validate(temp.Paths, tamperedSession);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(temp.Paths, tamperedSession);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanRemovesLayeredDynamaxAdventureOutputsWhenRestoredToVanilla()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateCompatibleMain());
        var service = SwShDynamaxAdventuresEditSessionService.CreateForSyntheticTests();

        var install = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, "6");
        var installPlan = service.CreateChangePlan(temp.Paths, install.Session);
        Assert.True(installPlan.CanApply);
        var installed = service.ApplyChangePlan(temp.Paths, install.Session, installPlan);
        Assert.DoesNotContain(installed.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var tablePath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        var mainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        Assert.True(File.Exists(tablePath));
        Directory.CreateDirectory(Path.GetDirectoryName(mainPath)!);
        File.WriteAllBytes(mainPath, SwShDynamaxAdventureTestFixtures.CreateCompatibleMain());
        Assert.True(File.Exists(mainPath));

        var restore = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, "4");

        var restorePlan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(restorePlan.CanApply);
        Assert.Contains(restorePlan.Writes, write => write.TargetRelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(restorePlan.Writes, write => write.TargetRelativePath == "exefs/main");
        Assert.Contains(restorePlan.Writes, write => write.Reason.Contains("removing the generated Adventure table", StringComparison.Ordinal));
        Assert.Contains(restorePlan.Writes, write => write.Reason.Contains("Remove redundant generated Dynamax Adventures exefs/main", StringComparison.Ordinal));

        var restored = service.ApplyChangePlan(temp.Paths, restore.Session, restorePlan);

        Assert.DoesNotContain(restored.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.False(File.Exists(tablePath));
        Assert.False(File.Exists(mainPath));
        Assert.Contains(restored.WrittenFiles, file => file.RelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(restored.WrittenFiles, file => file.RelativePath == "exefs/main");
        Assert.Contains(restored.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Info
            && diagnostic.Message.Contains("Restored the verified vanilla Dynamax Adventures table", StringComparison.Ordinal));
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text.Slice(offset, sizeof(uint)));
    }

    private static long ReadBaseDynamaxAdventureTableLength(TemporarySwShProject temp)
    {
        var path = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));

        return new FileInfo(path).Length;
    }

    private static byte[] CreateSpeciesNameTable(int highestIndex, params (int Index, string Value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(_ => new SwShGameTextLine(string.Empty, Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }

    private static void WriteMoveData(TemporarySwShProject temp, params (int MoveId, bool CanUseMove)[] moves)
    {
        foreach (var (moveId, canUseMove) in moves)
        {
            temp.WriteBaseRomFsFile(
                $"bin/pml/waza/waza{moveId:0000}.wazabin",
                SwShMoveDataFile.Write(CreateMoveRecord(moveId, canUseMove)));
        }
    }

    private static SwShMoveDataRecord CreateMoveRecord(int moveId, bool canUseMove)
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: checked((uint)moveId),
            CanUseMove: canUseMove,
            new SwShMoveCoreStats(
                Type: 0,
                Quality: 2,
                Category: 1,
                Power: 40,
                Accuracy: 100,
                PP: 35,
                Priority: 0,
                CritStage: 0,
                GigantamaxPower: 90),
            new SwShMoveTargeting(
                RawTarget: 3,
                HitMin: 1,
                HitMax: 1,
                TurnMin: 0,
                TurnMax: 0),
            new SwShMoveSecondaryEffects(
                Inflict: 0,
                InflictPercent: 0,
                RawInflictCount: 0,
                Flinch: 0,
                EffectSequence: 0,
                Recoil: 0,
                RawHealing: 0),
            [
                new SwShMoveStatChange(1, Stat: 0, Stage: 0, Percent: 0),
                new SwShMoveStatChange(2, Stat: 0, Stage: 0, Percent: 0),
                new SwShMoveStatChange(3, Stat: 0, Stage: 0, Percent: 0),
            ],
            new SwShMoveFlags(
                MakesContact: false,
                Charge: false,
                Recharge: false,
                Protect: true,
                Reflectable: false,
                Snatch: false,
                Mirror: false,
                Punch: false,
                Sound: false,
                Gravity: false,
                Defrost: false,
                DistanceTriple: false,
                Heal: false,
                IgnoreSubstitute: false,
                FailSkyBattle: false,
                AnimateAlly: false,
                Dance: false,
                Metronome: false));
    }

    private static void WriteLearnsetData(
        TemporarySwShProject temp,
        int recordCount,
        params (int PersonalId, (int MoveId, int Level)[] Moves)[] learnsets)
    {
        var data = new byte[recordCount * SwShPokemonLearnsetTable.RecordSize];
        data.AsSpan().Fill(byte.MaxValue);
        foreach (var (personalId, moves) in learnsets)
        {
            SwShPokemonLearnsetTable.WriteRecord(
                new SwShPokemonLearnsetRecord(
                    personalId,
                    moves.Select((move, index) => new SwShPokemonLearnsetMoveRecord(index, move.MoveId, move.Level)).ToArray()),
                data.AsSpan(personalId * SwShPokemonLearnsetTable.RecordSize, SwShPokemonLearnsetTable.RecordSize));
        }

        temp.WriteBaseRomFsFile(
            SwShPokemonLearnsetTable.LearnsetDataRelativePath["romfs/".Length..],
            data);
    }

    private static void WritePersonalData(
        TemporarySwShProject temp,
        int count,
        IReadOnlySet<int>? presentSpecies = null,
        IReadOnlySet<int>? cannotDynamaxSpecies = null,
        IReadOnlyDictionary<int, int>? hatchedSpeciesOverrides = null,
        IReadOnlyDictionary<int, int>? personalFormOverrides = null)
    {
        var includesEeveeFormRecord = count > 133;
        var recordCount = includesEeveeFormRecord ? count + 1 : count;
        temp.WriteBaseRomFsFile(
            SwShPersonalTable.PersonalDataRelativePath["romfs/".Length..],
            SwShDynamaxAdventureTestFixtures.CreatePersonalTable(
                Enumerable.Range(0, recordCount).Select(index =>
                {
                    var speciesIndex = includesEeveeFormRecord && index == count ? 133 : index;
                    var record = CreatePersonalRecord(
                        presentSpecies?.Contains(speciesIndex) == true,
                        cannotDynamaxSpecies?.Contains(speciesIndex) == true,
                        hatchedSpeciesOverrides is not null && hatchedSpeciesOverrides.TryGetValue(speciesIndex, out var hatchedSpecies)
                            ? hatchedSpecies
                            : includesEeveeFormRecord && index == count ? 133 : 0,
                        includesEeveeFormRecord && index == count
                            ? 1
                            : personalFormOverrides is not null && personalFormOverrides.TryGetValue(speciesIndex, out var personalForm)
                                ? personalForm
                                : 0);
                    if (includesEeveeFormRecord && index == 133)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1E), checked((ushort)count));
                        record[0x20] = 2;
                    }

                    return record;
                })));
    }

    private static byte[] CreatePersonalRecord(bool present, bool cannotDynamax, int hatchedSpecies, int personalForm)
    {
        var record = SwShDynamaxAdventureTestFixtures.CreatePersonalRecord(type1: 0, type2: 0);
        if (present)
        {
            record[0x21] |= 0x40;
        }

        if (cannotDynamax)
        {
            record[0x5A] |= 0x04;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x56), checked((ushort)hatchedSpecies));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5E), checked((ushort)personalForm));
        return record;
    }

    private static void WriteBossOnlyDynamaxAdventures(TemporarySwShProject temp)
    {
        var archive = CreateRepairableBossArchive();

        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive));
    }

    private static SwShDynamaxAdventureArchive CreateRowCountArchive(int rowCount)
    {
        var normalSpecies = Enumerable.Range(1, SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies)
            .Where(species => !SwShDynamaxAdventureSafetyRules.IsSpecialNormalRouteSpecies(species))
            .Take(SwShDynamaxAdventureSafetyRules.BossEntryStartIndex)
            .ToArray();
        int[] bossSpecies = [144, 145, 146, 150];
        return new SwShDynamaxAdventureArchive(
            Enumerable.Range(0, rowCount)
                .Select(index => new SwShDynamaxAdventureRecord(
                    index,
                    IsSingleCapture: index >= SwShDynamaxAdventureSafetyRules.BossEntryStartIndex,
                    SingleCaptureFlagBlock: (ulong)(index + 1),
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: index + 1,
                    Level: index >= SwShDynamaxAdventureSafetyRules.BossEntryStartIndex ? 70 : 65,
                    Species: index < SwShDynamaxAdventureSafetyRules.BossEntryStartIndex
                        ? normalSpecies[index]
                        : bossSpecies[(index - SwShDynamaxAdventureSafetyRules.BossEntryStartIndex) % bossSpecies.Length],
                    UiMessageId: (ulong)(index + 1),
                    OtGender: 0,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-2, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [1, 2, 3, 4]))
                .ToArray());
    }

    private static SwShDynamaxAdventureArchive CreateRepairableBossArchive()
    {
        var normalSpecies = Enumerable.Range(1, SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies)
            .Where(species => !SwShDynamaxAdventureSafetyRules.IsSpecialNormalRouteSpecies(species))
            .Take(SwShDynamaxAdventureSafetyRules.BossEntryStartIndex)
            .ToArray();
        return new SwShDynamaxAdventureArchive(
            Enumerable.Range(0, 228)
                .Select(index => new SwShDynamaxAdventureRecord(
                    index,
                    IsSingleCapture: index >= SwShDynamaxAdventureSafetyRules.BossEntryStartIndex,
                    SingleCaptureFlagBlock: (ulong)(index + 1),
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: index + 1,
                    Level: index >= SwShDynamaxAdventureSafetyRules.BossEntryStartIndex ? 70 : 65,
                    Species: index switch { 226 => 144, 227 => 150, _ => normalSpecies[index] },
                    UiMessageId: (ulong)(index + 1),
                    OtGender: 0,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-2, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [1, 2, 3, 4]))
                .ToArray());
    }

}
