// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Tools.Bridge;
using KM.ZA.Data;
using KM.ZA.StaticEncounters;
using KM.ZA.Workflows;
using Xunit;

namespace KM.Integration.Tests.ZA;

public sealed class ZaStaticEncounterMissionScenarioTests
{
    [Theory]
    [InlineData("sub_114_goronda", "Josée’s Training", "Side Mission 117")]
    [InlineData("ect_subq147_p2", "Be a Defenseless Dodger!", "Side Mission 173")]
    [InlineData("sub_119_diancie", "Shine Bright like a Gemstone", "Side Mission EX1")]
    [InlineData("sys_rest1_02_01", "Full Course of Battles: One Star Battle 2", "Side Mission 29")]
    [InlineData("rest2", "Full Course of Battles: Two Stars", "Side Mission 60")]
    [InlineData("id_rest3_03", "Full Course of Battles: Three Stars Battle 3", "Side Mission 94")]
    [InlineData("sys_rest4_05_03", "Full Course of Battles: High Rolling Battle 5", "Side Mission 73")]
    public void MissionScenariosResolveInternalIdsToTitlesAndDisplayedMissionNumbers(
        string encounterId,
        string expectedTitle,
        string expectedDetails)
    {
        var labels = ZaTextLabelLookup.None();

        Assert.Equal(
            expectedTitle,
            ZaStaticEncountersWorkflowService.FormatScenarioLabel(encounterId, labels));
        Assert.Equal(
            expectedDetails,
            ZaStaticEncountersWorkflowService.FormatScenarioDetails(encounterId, labels));
    }

    [Theory]
    [InlineData("sub_999_unknown")]
    [InlineData("ect_subq999_p1")]
    [InlineData("sys_rest5_01_01")]
    public void UnknownInternalMissionIdsRemainGeneric(string encounterId)
    {
        var labels = ZaTextLabelLookup.None();

        Assert.Equal(
            "Scripted Pokemon",
            ZaStaticEncountersWorkflowService.FormatScenarioLabel(encounterId, labels));
        Assert.Null(ZaStaticEncountersWorkflowService.FormatScenarioDetails(encounterId, labels));
    }

    [Fact]
    public void BridgeCarriesScenarioDetailsSeparatelyFromTheTechnicalEncounterLabel()
    {
        var stats = new ZaStaticEncounterStatsRecord(0, 0, 0, 0, 0, 0);
        var encounter = new ZaStaticEncounterEntry(
            EncounterIndex: 0,
            SourceIndex: 0,
            CategoryId: "encounterData",
            CategoryLabel: "Encounter Data",
            Label: "Static 001: Pikachu Lv. 50 | sys_rest4_05_03",
            EncounterId: "sys_rest4_05_03",
            SpeciesId: 25,
            Species: "Pikachu",
            Form: 0,
            Level: 50,
            HeldItemId: 0,
            HeldItem: null,
            Ability: 0,
            AbilityLabel: "Random",
            Nature: 0,
            NatureLabel: "Random",
            Gender: 0,
            GenderLabel: "Random",
            ShinyLock: 0,
            ShinyLockLabel: "Random",
            EncounterScenario: 0,
            EncounterScenarioLabel: "Full Course of Battles: High Rolling Battle 5",
            Evs: stats,
            Ivs: stats,
            FlawlessIvCount: null,
            IvSummary: "Random IVs",
            Moves: [],
            Provenance: new ZaStaticEncounterProvenance(
                "avalon/data/pokemon_data_array.bin",
                ProjectFileLayer.Base,
                ProjectFileGraphEntryState.BaseOnly),
            SupportedFields: [],
            FieldValues: new Dictionary<string, string>(StringComparer.Ordinal),
            FieldDisplayValues: new Dictionary<string, string>(StringComparer.Ordinal),
            FieldReadOnly: new Dictionary<string, bool>(StringComparer.Ordinal),
            AbilityOptions: [])
        {
            ScenarioDetails = "Side Mission 73",
        };
        var workflow = new ZaStaticEncountersWorkflow(
            new ZaWorkflowSummary(
                ZaWorkflowIds.StaticEncounters,
                "Static Encounters",
                "Test workflow",
                ZaWorkflowAvailability.Available,
                []),
            [encounter],
            [],
            new ZaStaticEncountersWorkflowStats(1, 0, 1, 1),
            []);

        var mapped = Assert.Single(ZaBridgeMapper.ToDto(workflow).Workflow.Encounters);

        Assert.Equal(encounter.Label, mapped.Label);
        Assert.Equal(encounter.EncounterScenarioLabel, mapped.EncounterScenarioLabel);
        Assert.Equal("Side Mission 73", mapped.ScenarioDetails);
    }
}
