// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;
using Xunit;

namespace KM.Integration.Tests.ZA;

public sealed class ZaMissionLocationLabelTests
{
    [Theory]
    [InlineData("id_rest1", "Full Course of Battles: One Star", "Side Mission 29", 29)]
    [InlineData("id_rest2", "Full Course of Battles: Two Stars", "Side Mission 60", 60)]
    [InlineData("id_rest3", "Full Course of Battles: Three Stars", "Side Mission 94", 94)]
    [InlineData("id_rest4", "Full Course of Battles: High Rolling", "Side Mission 73", 73)]
    [InlineData("id_sub090", "Floette Frolicking with Flowers", "Side Mission 51", 51)]
    [InlineData("id_spn_subq147", "Be a Defenseless Dodger!", "Side Mission 173", 173)]
    [InlineData("id_sub119", "Shine Bright like a Gemstone", "Side Mission EX1", 1001)]
    public void MissionLocationsUseTitlesAndDisplayedMissionMetadata(
        string locationKey,
        string expectedTitle,
        string expectedDetails,
        int expectedSort)
    {
        Assert.Equal(expectedTitle, ZaLumioseLocationLabels.FormatLocation(locationKey));
        Assert.Equal(expectedDetails, ZaLumioseLocationLabels.GetMissionDetails(locationKey));
        Assert.Equal(expectedSort, ZaLumioseLocationLabels.GetLocationSort(locationKey));
    }

    [Fact]
    public void MissionSpawnerLabelsUseLocalizedTitleResolver()
    {
        Assert.Equal(
            "Localized Mission Battle 1 Spawn Point 001",
            ZaLumioseLocationLabels.FormatRawSpawnerId(
                "spn_rest4_01_001",
                missionTitleResolver: _ => "Localized Mission"));
        Assert.Equal(
            "Localized Mission Spawn Point 01",
            ZaLumioseLocationLabels.FormatRawSpawnerId(
                "id_sub090_01",
                missionTitleResolver: _ => "Localized Mission"));
    }

    [Fact]
    public void PlacementMapsUseParentLocationLabels()
    {
        Assert.Equal(
            "Full Course of Battles: High Rolling",
            ZaLumioseLocationLabels.FormatPlacementMap(
                "fallback",
                zoneId: null,
                variationId: null,
                dungeonName: null,
                battleAreaId: null,
                spawnerId: "id_rest4"));
        Assert.Equal(
            "Dimension Wild Pools",
            ZaLumioseLocationLabels.FormatPlacementMap(
                "fallback",
                zoneId: null,
                variationId: null,
                dungeonName: null,
                battleAreaId: null,
                spawnerId: "zdm_random_dimension_wilds"));
    }

    [Theory]
    [InlineData("sub_114_goronda", "Side Mission 117")]
    [InlineData("ect_subq147_p1", "Side Mission 173")]
    [InlineData("sys_rest4_01", "Side Mission 73")]
    [InlineData("Ev_sub_134_010", "Side Mission 147")]
    public void MissionDetailsRecognizeScriptAndEncounterIdShapes(
        string value,
        string expectedDetails)
    {
        Assert.Equal(expectedDetails, ZaLumioseLocationLabels.GetMissionDetails(value));
    }
}
