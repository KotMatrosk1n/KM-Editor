// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Diagnostics;
using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresWorkflowServiceTests
{
    [Fact]
    public void ProductionServicesRejectSyntheticCanonicalShapeWhileTestFactoriesAcceptIt()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var syntheticArchive = SwShDynamaxAdventureTestFixtures.CreateRowCountArchive(
            SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            syntheticArchive.Write());
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(syntheticArchive));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var productionWorkflow = new SwShDynamaxAdventuresWorkflowService().Load(project);
        var syntheticWorkflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);
        var productionSeedPlan = new SwShDynamaxAdventureSeedPlanningService()
            .Predict(temp.Paths, seed: 0, npcCount: 0);
        var syntheticSeedPlan = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests()
            .Predict(temp.Paths, seed: 0, npcCount: 0);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, productionWorkflow.Summary.Availability);
        Assert.False(productionWorkflow.CanRestoreVanillaTable);
        Assert.Contains(productionWorkflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("canonical", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount, syntheticWorkflow.Encounters.Count);
        Assert.Empty(productionSeedPlan.Rentals);
        Assert.Empty(productionSeedPlan.Encounters);
        Assert.Contains(productionSeedPlan.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("base Adventure table identity", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(syntheticSeedPlan.Rentals);
        Assert.NotEmpty(syntheticSeedPlan.Encounters);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    public void LoadProjectsVerifiedBaseForFormsThatDoNotExistForTheSpecies(int entryIndex, int form)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().WriteEdits(
            [
                new(entryIndex, SwShDynamaxAdventureField.Form, form),
            ]));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.True(workflow.CanRestoreVanillaTable);
        Assert.True(workflow.UsesVanillaRecoveryProjection);
        Assert.NotEmpty(workflow.Encounters);
        Assert.All(workflow.Encounters, encounter =>
        {
            Assert.False(encounter.IsEditable);
            Assert.Empty(encounter.LayoutWritableFields);
        });
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == SwShDynamaxAdventuresWorkflowService.FormField
            && diagnostic.Message.Contains("does not exist for its species", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadReadsDynamaxAdventureRecordsFromRealSwordShieldTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Equal(2, workflow.Stats.TotalEncounterCount);
        Assert.Equal(1, workflow.Stats.SingleCaptureCount);
        Assert.Equal(1, workflow.Stats.StoryGatedCount);
        Assert.Equal(1, workflow.Stats.GuaranteedPerfectIvEncounterCount);
        Assert.Equal(16, workflow.Stats.SourceFileCount);

        var first = workflow.Encounters[0];
        Assert.Equal(0, first.EntryIndex);
        Assert.Equal("000 / 100 - Eevee (Partner) [Sword]", first.Label);
        Assert.Equal(100, first.AdventureIndex);
        Assert.Equal(133, first.SpeciesId);
        Assert.Equal("Eevee", first.Species);
        Assert.Equal(4, first.BallItemId);
        Assert.Equal("Poke Ball", first.BallItem);
        Assert.Equal("Ability 2 - 002 Ability 2", first.AbilityLabel);
        Assert.Equal("Normal", first.GigantamaxLabel);
        Assert.Equal("Sword", first.VersionLabel);
        Assert.Equal("Enabled", first.ShinyRollLabel);
        Assert.True(first.IsSingleCapture);
        Assert.Equal("0x1122334455667788", first.SingleCaptureFlagBlock);
        Assert.True(first.IsStoryProgressGated);
        Assert.Equal("0x8877665544332211", first.UiMessageId);
        Assert.Equal("Vine Whip", first.Moves[2].Move);
        Assert.Equal(4, first.GuaranteedPerfectIvs);
        Assert.Equal(-1, first.Ivs.Attack);
        Assert.Contains("4 guaranteed perfect", first.IvSummary, StringComparison.Ordinal);
        Assert.Equal(ProjectFileLayer.Base, first.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, first.Provenance.FileState);
        var vanilla = first.VanillaPokemon;
        Assert.NotNull(vanilla);
        Assert.Equal(133, vanilla!.SpeciesId);
        Assert.Equal(65, vanilla.Level);
        Assert.Equal(20, vanilla.Moves[3].MoveId);

        var levelField = workflow.EditableFields.Single(field => field.Field == SwShDynamaxAdventuresWorkflowService.LevelField);
        Assert.Equal(1, levelField.MinimumValue);
        Assert.Equal(100, levelField.MaximumValue);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.IvAttackField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.FormField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.AbilityField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.Move0Field);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.Move1Field);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.Move2Field);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.Move3Field);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.BallItemIdField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.VersionField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.ShinyRollField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.OtGenderField);
    }

    [Fact]
    public void LoadCountsDistinctBaseAndLayeredAdventureAndMainSources()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var layeredBytes = SwShDynamaxAdventureTestFixtures.CreateArchive().WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.IvAttack, 31),
        ]);
        var layeredArchive = SwShDynamaxAdventureArchive.Parse(layeredBytes);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            layeredBytes);
        temp.WriteOutputFile(
            "exefs/main",
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(sourceArchive: layeredArchive));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Equal(ProjectFileLayer.Layered, workflow.Encounters[0].Provenance.SourceLayer);
        Assert.Equal(18, workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void LoadPrefersLayeredDynamaxAdventureDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().WriteEdits(
            [
                new(0, SwShDynamaxAdventureField.IvAttack, 31),
            ]));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        var first = workflow.Encounters[0];
        Assert.Equal(ProjectFileLayer.Layered, first.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, first.Provenance.FileState);
        Assert.Equal(31, first.Ivs.Attack);
        var vanilla = first.VanillaPokemon;
        Assert.NotNull(vanilla);
        Assert.Equal(-1, vanilla!.Ivs.Attack);
    }

    [Fact]
    public void LoadWarnsWhenLayeredDynamaxAdventureTableLayoutDiffersFromVanilla()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().Write().Concat(new byte[] { 0 }).ToArray());
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Equal(2, workflow.Encounters.Count);
        Assert.Equal(ProjectFileLayer.Layered, workflow.Encounters[0].Provenance.SourceLayer);
        Assert.NotNull(workflow.Encounters[0].VanillaPokemon);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadWarnsWhenLayeredDynamaxAdventureTableByteLayoutDiffersAtSameLength()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var table = SwShDynamaxAdventureTestFixtures.CreateArchive().Write();
        SwShDynamaxAdventureTestFixtures.ClearTableField(table, entryIndex: 1, fieldIndex: 19);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            table);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Equal(2, workflow.Encounters.Count);
        Assert.Equal(0, workflow.Encounters[1].Ability);
        Assert.Equal(ReadBaseDynamaxAdventureTableLength(temp), table.Length);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadExposesRuntimeVerifiedDynamaxAdventurePokemonFields()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.FormField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.AbilityField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.Move0Field);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.Move1Field);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.Move2Field);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.Move3Field);
    }

    [Fact]
    public void LoadHidesAbilityOptionsThatWouldRequireDynamaxAdventureTableRebuild()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var table = SwShDynamaxAdventureTestFixtures.CreateArchive().Write();
        SwShDynamaxAdventureTestFixtures.ClearTableField(table, entryIndex: 1, fieldIndex: 19);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            table);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Equal(new[] { 0, 1, 2 }, workflow.Encounters[0].AbilityOptions.Select(option => option.Value));
        Assert.Equal(new[] { 0 }, workflow.Encounters[1].AbilityOptions.Select(option => option.Value));
    }

    [Fact]
    public void LoadFiltersDynamaxAdventureSpeciesOptionsToPokemonPresentInSwordShield()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateSpeciesNameTable(467, (16, "Pidgey"), (25, "Pikachu"), (133, "Eevee"), (467, "Magmortar")));
        var personalRecords = Enumerable.Range(0, 469)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        personalRecords[25] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 25);
        personalRecords[133] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            formStatsIndex: 468,
            formCount: 2);
        personalRecords[467] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 467);
        personalRecords[468] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 133, form: 1);
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(personalRecords));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        var speciesOptions = workflow.EditableFields.Single(field =>
            field.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField).Options;
        Assert.Contains(speciesOptions, option => option.Value == 467 && option.Label == "467 Magmortar");
        Assert.Contains(speciesOptions, option => option.Value == 133 && option.Label == "133 Eevee");
        Assert.DoesNotContain(speciesOptions, option => option.Value == 25);
        Assert.DoesNotContain(speciesOptions, option => option.Value == 16);
    }

    [Fact]
    public void LoadExposesSafeNormalDynamaxAdventureSpeciesOptions()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateSpeciesNameTable(
                901,
                (25, "Pikachu"),
                (133, "Eevee"),
                (150, "Mewtwo"),
                (467, "Magmortar"),
                (484, "Palkia"),
                (772, "Type: Null"),
                (800, "Necrozma"),
                (864, "Cursola"),
                (895, "Regidrago"),
                (901, "Internal")));
        var personalRecords = Enumerable.Range(0, 903)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        personalRecords[25] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 25);
        personalRecords[133] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            formStatsIndex: 902,
            formCount: 2);
        personalRecords[150] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 150);
        personalRecords[467] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 240);
        personalRecords[484] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 484);
        personalRecords[772] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 772);
        personalRecords[800] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 800);
        personalRecords[864] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 222, form: 1);
        personalRecords[895] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 895);
        personalRecords[901] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 901);
        personalRecords[902] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 133, form: 1);
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(personalRecords));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Contains(workflow.SafeNormalSpeciesOptions, option =>
            option.Value == 467 && option.Label == "467 Magmortar");
        Assert.DoesNotContain(workflow.SafeNormalSpeciesOptions, option => option.Value == 25);
        Assert.DoesNotContain(workflow.SafeNormalSpeciesOptions, option => option.Value == 150);
        Assert.DoesNotContain(workflow.SafeNormalSpeciesOptions, option => option.Value == 484);
        Assert.DoesNotContain(workflow.SafeNormalSpeciesOptions, option => option.Value == 772);
        Assert.DoesNotContain(workflow.SafeNormalSpeciesOptions, option => option.Value == 800);
        Assert.DoesNotContain(workflow.SafeNormalSpeciesOptions, option => option.Value == 864);
        Assert.DoesNotContain(workflow.SafeNormalSpeciesOptions, option => option.Value == 895);
        Assert.DoesNotContain(workflow.SafeNormalSpeciesOptions, option => option.Value == 901);
    }

    [Fact]
    public void LoadDoesNotExposeBossTargetOptionsForUniqueSameBucketBosses()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBossTargetOptionArchive(temp, duplicateArticuno: false);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        var normal = workflow.Encounters.Single(encounter => encounter.EntryIndex == 0);
        Assert.True(normal.IsEditable);
        Assert.Empty(normal.BossTargetOptions);

        var articuno = workflow.Encounters.Single(encounter => encounter.EntryIndex == 226);
        Assert.False(articuno.IsEditable);
        Assert.Empty(articuno.BossTargetOptions);
    }

    [Fact]
    public void LoadOmitsBossTargetOptionsWhenBossSpeciesIsDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBossTargetOptionArchive(temp, duplicateArticuno: true);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        var articuno = workflow.Encounters.Single(encounter => encounter.EntryIndex == 226);
        Assert.Empty(articuno.BossTargetOptions);
        var mewtwo = workflow.Encounters.Single(encounter => encounter.EntryIndex == 227);
        Assert.DoesNotContain(mewtwo.BossTargetOptions, option => option.SpeciesId == 144);
    }

    [Fact]
    public void LoadDoesNotExposeActiveBossTargetRemapFromGeneratedMain()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBossTargetOptionArchive(temp, duplicateArticuno: false);
        var tablePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var archive = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(tablePath));
        temp.WriteOutputFile(
            "exefs/main",
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain(),
                archive,
                fromSpecies: 144,
                toSpecies: 150));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        var articuno = workflow.Encounters.Single(encounter => encounter.EntryIndex == 226);
        Assert.Equal(144, articuno.SpeciesId);
        Assert.Equal("Articuno", articuno.Species);
        Assert.Equal(144, articuno.BossTargetSpeciesId);
        Assert.Equal("Articuno", articuno.BossTargetSpecies);
        var mewtwo = workflow.Encounters.Single(encounter => encounter.EntryIndex == 227);
        Assert.Equal(150, mewtwo.BossTargetSpeciesId);
    }

    [Fact]
    public void LoadExposesCompatibleDynamaxAdventureMoveOptionsForCurrentPokemon()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp);
        WriteMoveData(temp, (10, true), (85, true));
        WriteLearnsetData(temp, recordCount: 134, (25, [(85, 50), (10, 70)]));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        var pikachu = workflow.Encounters.Single(encounter => encounter.EntryIndex == 1);
        Assert.Contains(pikachu.MoveOptions, option => option.Value == 85 && option.Label == "085 Thunderbolt");
        Assert.Contains(pikachu.MoveOptions, option => option.Value == 3);
        Assert.DoesNotContain(pikachu.MoveOptions, option => option.Value == 10);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenDynamaxAdventureTableIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/adventures.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Empty(workflow.Encounters);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.dynamaxAdventures");
    }

    [Fact]
    public void LoadBecomesReadOnlyWhenSafeSpeciesOptionsCannotBeMapped()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        File.Delete(Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "message",
            "English",
            "common",
            "monsname.dat"));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Empty(workflow.SafeNormalSpeciesOptions);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && diagnostic.Message.Contains("safe normal-route species options", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SwShDynamaxAdventureField.Species, 899, SwShDynamaxAdventuresWorkflowService.SpeciesField, true)]
    [InlineData(SwShDynamaxAdventureField.Level, 101, SwShDynamaxAdventuresWorkflowService.LevelField, true)]
    [InlineData(SwShDynamaxAdventureField.Move0, 827, SwShDynamaxAdventuresWorkflowService.Move0Field, true)]
    [InlineData(SwShDynamaxAdventureField.BallItemId, 65536, SwShDynamaxAdventuresWorkflowService.BallItemIdField, true)]
    [InlineData(SwShDynamaxAdventureField.OtGender, 2, SwShDynamaxAdventuresWorkflowService.OtGenderField, true)]
    public void LoadKeepsRecordsOutsideStrictApiDomainOutOfEditableProjection(
        SwShDynamaxAdventureField archiveField,
        int value,
        string expectedField,
        bool expectsRestoreProjection)
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
            baseArchive.WriteEditsPreservingLayout([new(1, archiveField, value)]));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(expectsRestoreProjection, workflow.CanRestoreVanillaTable);
        if (expectsRestoreProjection)
        {
            Assert.NotEmpty(workflow.Encounters);
            Assert.All(workflow.Encounters, encounter =>
            {
                Assert.False(encounter.IsEditable);
                Assert.Empty(encounter.LayoutWritableFields);
            });
        }
        else
        {
            Assert.Empty(workflow.Encounters);
        }
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == expectedField
            && diagnostic.Message.Contains("outside the supported API domain", StringComparison.Ordinal));

        var seedPlan = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests()
            .Predict(temp.Paths, seed: 0, npcCount: 0);
        Assert.Empty(seedPlan.Rentals);
        Assert.Empty(seedPlan.Encounters);
        Assert.Contains(seedPlan.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == expectedField
            && diagnostic.Message.Contains("outside the supported domain", StringComparison.Ordinal));
    }

    private static void WriteBossTargetOptionArchive(TemporarySwShProject temp, bool duplicateArticuno)
    {
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var normalEntries = Enumerable.Range(0, 226)
            .Select(index => CreateAdventureRecord(
                index,
                adventureIndex: index,
                species: 25,
                version: 0,
                isBoss: false,
                isStoryProgressGated: false));

        var bossEntries = new[]
        {
            CreateAdventureRecord(
                entryIndex: 226,
                adventureIndex: 1003,
                species: 144,
                version: 0,
                isBoss: true,
                isStoryProgressGated: false),
            CreateAdventureRecord(
                entryIndex: 227,
                adventureIndex: 1004,
                species: 150,
                version: 0,
                isBoss: true,
                isStoryProgressGated: false),
            CreateAdventureRecord(
                entryIndex: 228,
                adventureIndex: 1019,
                species: duplicateArticuno ? 144 : 484,
                version: duplicateArticuno ? 0 : 2,
                isBoss: true,
                isStoryProgressGated: false),
            CreateAdventureRecord(
                entryIndex: 229,
                adventureIndex: 1038,
                species: 800,
                version: 0,
                isBoss: true,
                isStoryProgressGated: true),
        };

        var archive = new SwShDynamaxAdventureArchive(normalEntries.Concat(bossEntries).ToArray());
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            archive.Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateSpeciesNameTable(
                800,
                (25, "Pikachu"),
                (144, "Articuno"),
                (150, "Mewtwo"),
                (484, "Palkia"),
                (800, "Necrozma")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateSpeciesNameTable(4, (4, "Poke Ball")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateSpeciesNameTable(4, (1, "Tackle"), (2, "Growl"), (3, "Water Gun"), (4, "Ember")));
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(sourceArchive: archive));
    }

    private static SwShDynamaxAdventureRecord CreateAdventureRecord(
        int entryIndex,
        int adventureIndex,
        int species,
        int version,
        bool isBoss,
        bool isStoryProgressGated)
    {
        return new SwShDynamaxAdventureRecord(
            entryIndex,
            IsSingleCapture: isBoss,
            SingleCaptureFlagBlock: 0x1000000000000000UL + (uint)entryIndex,
            Field02: 0,
            Form: 0,
            GigantamaxState: isBoss ? 1 : 0,
            BallItemId: 4,
            AdventureIndex: adventureIndex,
            Level: isBoss ? 70 : 65,
            Species: species,
            UiMessageId: 0x2000000000000000UL + (uint)entryIndex,
            OtGender: 1,
            Version: version,
            ShinyRoll: 1,
            new SwShDynamaxAdventureIvs(-5, -1, -1, -1, -1, -1),
            Ability: isBoss ? 0 : 1,
            IsStoryProgressGated: isStoryProgressGated,
            Moves: [1, 2, 3, 4]);
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

    private static long ReadBaseDynamaxAdventureTableLength(TemporarySwShProject temp)
    {
        var path = Path.Combine(
            temp.BaseRomFsPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));

        return new FileInfo(path).Length;
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
}
