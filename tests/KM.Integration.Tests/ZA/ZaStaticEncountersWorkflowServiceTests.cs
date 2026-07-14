// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;
using KM.ZA.StaticEncounters;
using Xunit;

namespace KM.Integration.Tests.ZA;

public sealed class ZaStaticEncountersWorkflowServiceTests
{
    [Theory]
    [InlineData("ect_boss_0359_01", "Boss Battle: Pokemon 359")]
    [InlineData("ect_outzone_a0201_z669_01", "Bleu District, Sector 1 Outside Wild Zone")]
    [InlineData("ect_a0202_w02_v01_z504", "Wild Zone 5")]
    [InlineData("ect_d03_01_z093_ev", "Old Building")]
    [InlineData("ect_zdm404_sp06_017", "Dimension Dungeon 404 Special Area 6")]
    [InlineData("ect_zdm_random_lv1_445", "Dimension Wild Random Pool, Rank 1")]
    [InlineData("sub_114_goronda", "Josée’s Training")]
    [InlineData("10rom_poke_encount_rose_dede", "Story Event Rose Dede")]
    [InlineData("chapter5_ect_d02_z707", "Story Chapter Event 5")]
    public void ScenarioLabelsUseEncounterIdContext(string encounterId, string expectedLabel)
    {
        var label = ZaStaticEncountersWorkflowService.FormatScenarioLabel(
            encounterId,
            ZaTextLabelLookup.None());

        Assert.Equal(expectedLabel, label);
    }

    [Theory]
    [InlineData("random_zdm403_v01_381", "Dimension Dungeon 403 Variant 1 Spawn Point 381")]
    [InlineData("random_zdm403_v02_001", "Dimension Dungeon 403 Variant 2 Spawn Point 001")]
    [InlineData("random_zdm404_sp06_017", "Dimension Dungeon 404 Special Area 6 Spawn Point 017")]
    [InlineData("spn_zdm502_v00_001", "Dimension Dungeon 502 Variant 0 Spawn Point 001")]
    [InlineData("spn_outzone_a0201_050", "Bleu District, Sector 1 Outside Wild Zone, Spawn Point 050")]
    [InlineData("spn_a0102_w01_v01_001", "Wild Zone 1 Variant 1 Spawn Point 001")]
    [InlineData("spn_d01_01_001", "Lysandre Labs Spawn Point 001")]
    [InlineData("spn_d02_01_001", "Lumiose Sewers Main Area Spawn Point 001")]
    [InlineData("spn_d02_02_001", "Lumiose Sewers Side Area Spawn Point 001")]
    [InlineData("spn_d03_01_ev_001", "Old Building Event Spawn Point 001")]
    [InlineData("spn_t2_001", "Lysandre Labs Spawn Point 001")]
    [InlineData("spn_t3_001", "Lumiose Sewers Main Area Spawn Point 001")]
    [InlineData("spn_t3_2_001", "Lumiose Sewers Side Area Spawn Point 001")]
    [InlineData("spn_rest4_01_001", "Full Course of Battles: High Rolling Battle 1 Spawn Point 001")]
    [InlineData("spn_rest4_05_003", "Full Course of Battles: High Rolling Battle 5 Spawn Point 003")]
    [InlineData("spn_subq147_002b", "Be a Defenseless Dodger! Spawn Point 002B")]
    public void RawSpawnerLabelsUseLocationContext(
        string spawnerId,
        string expectedLabel)
    {
        Assert.Equal(expectedLabel, ZaLumioseLocationLabels.FormatRawObjectName(spawnerId));
    }

    [Theory]
    [InlineData("d01", "Lysandre Labs")]
    [InlineData("d01_01", "Lysandre Labs")]
    [InlineData("t2", "Lysandre Labs")]
    [InlineData("d02_01", "Lumiose Sewers Main Area")]
    [InlineData("t3", "Lumiose Sewers Main Area")]
    [InlineData("d02_02", "Lumiose Sewers Side Area")]
    [InlineData("t3_2", "Lumiose Sewers Side Area")]
    [InlineData("d03", "Old Building")]
    [InlineData("d03_01", "Old Building")]
    [InlineData("id_rest4", "Full Course of Battles: High Rolling")]
    [InlineData("id_rest4_01", "Full Course of Battles: High Rolling Battle 1")]
    [InlineData("id_rest4_02", "Full Course of Battles: High Rolling Battle 2")]
    [InlineData("id_rest4_03", "Full Course of Battles: High Rolling Battle 3")]
    [InlineData("id_rest4_04", "Full Course of Battles: High Rolling Battle 4")]
    [InlineData("id_rest4_05", "Full Course of Battles: High Rolling Battle 5")]
    [InlineData("id_spn_subq147", "Be a Defenseless Dodger!")]
    public void SharedLocationLabelsUseConfirmedDungeonAndEventMappings(
        string locationKey,
        string expectedLabel)
    {
        Assert.Equal(expectedLabel, ZaLumioseLocationLabels.FormatLocation(locationKey));
    }

    [Fact]
    public void PlacementMapFormatsDungeonAndBattleAreaAliases()
    {
        Assert.Equal(
            "Lysandre Labs",
            ZaLumioseLocationLabels.FormatPlacementMap(
                "fallback",
                zoneId: null,
                variationId: null,
                dungeonName: "t2",
                battleAreaId: null,
                spawnerId: null));
        Assert.Equal(
            "Lumiose Sewers Side Area",
            ZaLumioseLocationLabels.FormatPlacementMap(
                "fallback",
                zoneId: null,
                variationId: null,
                dungeonName: null,
                battleAreaId: "t3_2",
                spawnerId: null));
    }

    [Fact]
    public void BossScenarioLabelsPreferEncounterRowSpecies()
    {
        var label = ZaStaticEncountersWorkflowService.FormatScenarioLabel(
            "ect_boss_0448_01",
            ZaTextLabelLookup.None(),
            "Tyranitar");

        Assert.Equal("Boss Battle: Tyranitar", label);
    }

    [Fact]
    public void BossScenarioLabelsIncludeEncounterRowForm()
    {
        var megaLabel = ZaStaticEncountersWorkflowService.FormatScenarioLabel(
            "btl_ect_boss_0448_01",
            ZaTextLabelLookup.None(),
            speciesId: 248,
            form: 1,
            speciesName: "Tyranitar");
        var zygardeLabel = ZaStaticEncountersWorkflowService.FormatScenarioLabel(
            "ect_boss_0718_02",
            ZaTextLabelLookup.None(),
            speciesId: 718,
            form: 2,
            speciesName: "Zygarde");

        Assert.Equal("Boss Battle: Tyranitar (Mega)", megaLabel);
        Assert.Equal("Boss Battle: Zygarde (10% Forme Power Construct), Phase 2", zygardeLabel);
    }

    [Fact]
    public void StaticPokemonIdsRejectWildAlphaBaseRows()
    {
        var wildEncounterIds = new HashSet<string>(StringComparer.Ordinal);
        ZaEncounterDataIds.AddSpawnerEncounterDataTargets(
            wildEncounterIds,
            "ect_a0201_w01_v01_z460_Alpha");

        Assert.False(ZaStaticEncountersWorkflowService.IsStaticPokemonId(
            "ect_a0201_w01_v01_z460",
            wildEncounterIds));
        Assert.False(ZaStaticEncountersWorkflowService.IsStaticPokemonId(
            "ect_a0201_w01_v01_z460_Alpha",
            wildEncounterIds));
        Assert.True(ZaStaticEncountersWorkflowService.IsStaticPokemonId(
            "ect_boss_0359_01",
            wildEncounterIds));
    }
}
