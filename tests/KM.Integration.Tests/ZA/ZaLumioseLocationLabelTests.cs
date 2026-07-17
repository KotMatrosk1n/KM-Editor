// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;
using Xunit;

namespace KM.Integration.Tests.ZA;

public sealed class ZaLumioseLocationLabelTests
{
    [Fact]
    public void OutzoneLetterGroupsUseOneGrammarFromAToS()
    {
        for (var group = 'A'; group <= 'S'; group++)
        {
            var spawnerId = $"id_spn_outzone_a0102_{group}00";

            Assert.Equal(
                $"Vert District, Sector 2 Outside Wild Zone, Spawn Group {group}, Point 00",
                ZaLumioseLocationLabels.FormatRawSpawnerId(spawnerId));
            Assert.Equal(
                ZaLumioseLocationLabels.SpawnGroupSpawnerCategory,
                ZaLumioseLocationLabels.ClassifyRawSpawnerId(spawnerId));
        }
    }

    [Theory]
    [InlineData(
        "id_spn_outzone_a0102_A459",
        "Vert District, Sector 2 Outside Wild Zone, Spawn Group A, Point 459",
        "spawnGroup")]
    [InlineData(
        "id_spn_outzone_a0102_050_BZ",
        "Vert District, Sector 2 Outside Wild Zone, Spawn Point 050, Battle Zone",
        "spawnPoint")]
    [InlineData(
        "id_spn_outzone_a0102_sp1",
        "Vert District, Sector 2 Outside Wild Zone, Special Encounter 1",
        "specialEncounter")]
    [InlineData(
        "id_spn_outzone_a0102_405_A_BZ_PH",
        "Vert District, Sector 2 Outside Wild Zone, Spawn Point 405, Alpha, Battle Zone, Phase Condition",
        "alpha")]
    [InlineData(
        "id_spn_outzone_a0102_A00_A",
        "Vert District, Sector 2 Outside Wild Zone, Spawn Group A, Point 00, Alpha",
        "alpha")]
    public void OutzoneCategoriesUseStructuredTokens(
        string spawnerId,
        string expectedLabel,
        string expectedCategory)
    {
        Assert.Equal(expectedLabel, ZaLumioseLocationLabels.FormatRawSpawnerId(spawnerId));
        Assert.Equal(expectedCategory, ZaLumioseLocationLabels.ClassifyRawSpawnerId(spawnerId));
    }
}
