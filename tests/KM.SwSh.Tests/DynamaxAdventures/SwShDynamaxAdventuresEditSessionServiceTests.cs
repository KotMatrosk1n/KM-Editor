// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldCreatesPendingDynamaxAdventureIvEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

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
    public void UpdateFieldClampsDynamaxAdventureIvOverrides()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

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

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.IvAttackField
            && edit.NewValue == "0");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.IvDefenseField
            && edit.NewValue == "31");
        Assert.Equal(0, result.Workflow.Encounters[0].Ivs.Attack);
        Assert.Equal(31, result.Workflow.Encounters[0].Ivs.Defense);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateFieldRejectsAmbiguousGuaranteedPerfectIvCount()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: species);

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("normal route row", StringComparison.Ordinal));
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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 1,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "901");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use species 901", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsIntroducedNormalDynamaxAdventureForms()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "25");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("duplicate normal-route species/form 25/0", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsBossSpeciesOutsideVanillaDynamaxAdventureBossRoster()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        WriteBossOnlyDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.Move0Field,
            value: "85");
        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("not marked usable", StringComparison.Ordinal));
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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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

    [Fact]
    public void ApplyAbilityEditWritesOnlyInPlaceDynamaxAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 1, SwShDynamaxAdventuresWorkflowService.Move0Field, "85");

        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.False(plan.CanApply);
        Assert.Equal(ReadBaseDynamaxAdventureTableLength(temp), table.Length);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("source table byte layout differs", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyRestoreCanRemoveMismatchedDynamaxAdventureLayout()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var editedTable = SwShDynamaxAdventureTestFixtures.CreateArchive()
            .WriteEdits([new(1, SwShDynamaxAdventureField.Move0, 85)])
            .Concat(new byte[] { 0 })
            .ToArray();
        temp.WriteOutputFile(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, editedTable);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var restore = service.UpdateField(temp.Paths, null, 1, SwShDynamaxAdventuresWorkflowService.Move0Field, "3");
        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Contains(apply.WrittenFiles, file =>
            file.RelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void CreateChangePlanRejectsAbilityEditThatRequiresDynamaxAdventureTableRebuild()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var table = SwShDynamaxAdventureTestFixtures.CreateArchive().Write();
        SwShDynamaxAdventureTestFixtures.ClearTableField(table, entryIndex: 1, fieldIndex: 19);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            table);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 1, SwShDynamaxAdventuresWorkflowService.AbilityField, "1");

        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("rebuilding the table byte layout", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplySpeciesEditWritesCorrectedDynamaxAdventureMainMirror()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateCompatibleMain());
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        temp.WriteBaseExeFsFile(
            "main",
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(
                SwShDynamaxAdventuresMainPatcher.ShieldCommandValidatorOffsetDelta,
                SwShDynamaxAdventuresMainPatcher.ShieldBuildId));
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        var service = new SwShDynamaxAdventuresEditSessionService();

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
    public void ApplyBossTargetEditWritesOnlyMainTargetRemap()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBossOnlyDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain());
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            null,
            226,
            SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
            "150");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyBossTargetRestoreRemovesGeneratedMainWhenNoOtherMainPatchRemains()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBossOnlyDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateBossTargetAndSummaryCompatibleMain(entryCount: 228));
        var service = new SwShDynamaxAdventuresEditSessionService();
        var install = service.UpdateField(
            temp.Paths,
            null,
            226,
            SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
            "150");

        Assert.Empty(install.Session.PendingEdits);
        Assert.Contains(install.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldRejectsUnsafeBossTargetSpecies()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBossOnlyDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            null,
            226,
            SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
            "484");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyChangePlanRemovesLayeredDynamaxAdventureOutputsWhenRestoredToVanilla()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateCompatibleMain());
        var service = new SwShDynamaxAdventuresEditSessionService();

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
        Assert.Contains(restorePlan.Writes, write => write.Reason.Contains("Restore or remove Dynamax Adventures ExeFS mirrors", StringComparison.Ordinal));

        var restored = service.ApplyChangePlan(temp.Paths, restore.Session, restorePlan);

        Assert.DoesNotContain(restored.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.False(File.Exists(tablePath));
        Assert.False(File.Exists(mainPath));
        Assert.Contains(restored.WrittenFiles, file => file.RelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(restored.WrittenFiles, file => file.RelativePath == "exefs/main");
        Assert.Contains(restored.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Info
            && diagnostic.Message.Contains("Restored vanilla Dynamax Adventures Pokemon", StringComparison.Ordinal));
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
        temp.WriteBaseRomFsFile(
            SwShPersonalTable.PersonalDataRelativePath["romfs/".Length..],
            SwShDynamaxAdventureTestFixtures.CreatePersonalTable(
                Enumerable.Range(0, count).Select(index => CreatePersonalRecord(
                    presentSpecies?.Contains(index) == true,
                    cannotDynamaxSpecies?.Contains(index) == true,
                    hatchedSpeciesOverrides is not null && hatchedSpeciesOverrides.TryGetValue(index, out var hatchedSpecies)
                        ? hatchedSpecies
                        : 0,
                    personalFormOverrides is not null && personalFormOverrides.TryGetValue(index, out var personalForm)
                        ? personalForm
                        : 0))));
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
        var entries = Enumerable.Range(0, 226)
            .Select(index => CreateNormalFiller(index))
            .Concat(
            [
                new SwShDynamaxAdventureRecord(
                    226,
                    IsSingleCapture: true,
                    SingleCaptureFlagBlock: 0xDCA342BDF75E52CFUL,
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: 1003,
                    Level: 70,
                    Species: 144,
                    UiMessageId: 0x8877665544332211UL,
                    OtGender: 1,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-5, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [58, 573, 542, 54]),
                new SwShDynamaxAdventureRecord(
                    227,
                    IsSingleCapture: true,
                    SingleCaptureFlagBlock: 0xDCA33FBDF75E4DB6UL,
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: 1004,
                    Level: 70,
                    Species: 150,
                    UiMessageId: 0x0807060504030201UL,
                    OtGender: 1,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-5, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [94, 50, 105, 59]),
            ])
            .ToArray();

        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(entries).Write());
    }

    private static SwShDynamaxAdventureRecord CreateNormalFiller(int entryIndex)
    {
        return new SwShDynamaxAdventureRecord(
            entryIndex,
            IsSingleCapture: false,
            SingleCaptureFlagBlock: 0xCBF29CE484222645UL,
            Field02: 0,
            Form: 0,
            GigantamaxState: 1,
            BallItemId: 4,
            AdventureIndex: entryIndex + 1,
            Level: 65,
            Species: 1000 + entryIndex,
            UiMessageId: 0,
            OtGender: 1,
            Version: 0,
            ShinyRoll: 1,
            new SwShDynamaxAdventureIvs(-5, -1, -1, -1, -1, -1),
            Ability: 0,
            IsStoryProgressGated: false,
            Moves: [1, 2, 10, 20]);
    }
}
