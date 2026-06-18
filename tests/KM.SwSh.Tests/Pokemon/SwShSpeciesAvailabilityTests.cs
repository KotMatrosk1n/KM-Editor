// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.Pokemon;
using Xunit;

namespace KM.SwSh.Tests.Pokemon;

public sealed class SwShSpeciesAvailabilityTests
{
    [Fact]
    public void CreatePresentSpeciesIdsIncludesSpeciesFromPresentRegionalFormRows()
    {
        var records = Enumerable.Range(0, 54)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        records[53] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 52,
            formStatsIndex: 53,
            formCount: 1,
            localFormIndex: 2,
            form: 2,
            isRegionalForm: true);

        var presentSpeciesIds = SwShSpeciesAvailability.CreatePresentSpeciesIds(
            KM.Formats.SwSh.SwShPersonalTable.Parse(
                SwShPokemonWorkflowServiceTests.CreatePersonalTable(records)).Records);

        Assert.Contains(52, presentSpeciesIds);
        Assert.Contains(53, presentSpeciesIds);
        Assert.DoesNotContain(16, presentSpeciesIds);
    }

    [Fact]
    public void CreatePresentSpeciesIdsKeepsEvolvedSpeciesWhenHatchedSpeciesPointsAtBase()
    {
        var records = new[]
        {
            SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
            SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 1),
            SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 1),
        };

        var presentSpeciesIds = SwShSpeciesAvailability.CreatePresentSpeciesIds(
            KM.Formats.SwSh.SwShPersonalTable.Parse(
                SwShPokemonWorkflowServiceTests.CreatePersonalTable(records)).Records);

        Assert.Contains(1, presentSpeciesIds);
        Assert.Contains(2, presentSpeciesIds);
    }

    [Fact]
    public void CreateSpeciesOptionsExcludesNamedSpeciesThatAreNotPresentInSwordShield()
    {
        var speciesNames = Enumerable.Range(0, 26)
            .Select(index => $"Pokemon {index}")
            .ToArray();
        speciesNames[16] = "Pidgey";
        speciesNames[25] = "Pikachu";
        var presentSpeciesIds = new HashSet<int> { 25 };

        var options = SwShSpeciesAvailability.CreateSpeciesOptions(
            speciesNames,
            presentSpeciesIds,
            (value, label) => (value, label));

        Assert.Contains(options, option => option.value == 25 && option.label == "025 Pikachu");
        Assert.DoesNotContain(options, option => option.value == 16);
    }
}
