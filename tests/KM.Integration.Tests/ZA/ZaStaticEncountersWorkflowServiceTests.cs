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
    [InlineData("ect_d03_01_z093_ev", "Dungeon 3 Floor 1")]
    [InlineData("ect_zdm404_sp06_017", "Dimension Dungeon 404 Special Area 6")]
    [InlineData("ect_zdm_random_lv1_445", "Dimension Wild Random Pool, Rank 1")]
    [InlineData("sub_114_goronda", "Side Mission 114")]
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
    [InlineData("spn_d03_01_ev_001", "Dungeon 3 Floor 1 Event Spawn Point 001")]
    [InlineData("spn_subq147_002b", "Side Mission Event 147 Spawn Point 002B")]
    public void RawSpawnerLabelsUseLocationContext(
        string spawnerId,
        string expectedLabel)
    {
        Assert.Equal(expectedLabel, ZaLumioseLocationLabels.FormatRawObjectName(spawnerId));
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
