// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Pokemon;
using Xunit;

namespace KM.Core.Tests.Pokemon;

public sealed class PokemonFormLabelsTests
{
    [Theory]
    [InlineData(PokemonFormLabelFamily.LegendsZA, 58, "Growlithe", 1, "Hisuian")]
    [InlineData(PokemonFormLabelFamily.ScarletViolet, 58, "Growlithe", 1, "Hisuian")]
    [InlineData(PokemonFormLabelFamily.LegendsZA, 25, "Pikachu", 8, "Starter")]
    [InlineData(PokemonFormLabelFamily.SwordShield, 25, "Pikachu", 8, "World Cap")]
    [InlineData(PokemonFormLabelFamily.SwordShield, 25, "Pikachu", 9, "World Cap")]
    [InlineData(PokemonFormLabelFamily.LegendsZA, 351, "Castform", 2, "Rainy Form")]
    [InlineData(PokemonFormLabelFamily.ScarletViolet, 386, "Deoxys", 2, "Defense Forme")]
    [InlineData(PokemonFormLabelFamily.SwordShield, 351, "Castform", 3, "Snowy Form")]
    [InlineData(PokemonFormLabelFamily.SwordShield, 133, "Eevee", 1, "Partner")]
    [InlineData(PokemonFormLabelFamily.LegendsZA, 952, "Tatsugiri", 1, "Droopy Form")]
    [InlineData(PokemonFormLabelFamily.LegendsZA, 670, "Floette", 5, "Eternal Flower")]
    public void ResolveFormLabelReturnsFriendlyLabels(
        PokemonFormLabelFamily family,
        int speciesId,
        string speciesName,
        int form,
        string expected)
    {
        Assert.Equal(expected, PokemonFormLabels.ResolveFormLabel(speciesId, speciesName, form, family));
    }

    [Fact]
    public void ResolveFormLabelKeepsGameSpecificFormsScoped()
    {
        Assert.Null(PokemonFormLabels.ResolveFormLabel(58, "Growlithe", 1, PokemonFormLabelFamily.SwordShield));
        Assert.Null(PokemonFormLabels.ResolveFormLabel(1011, "Ogerpon", 1, PokemonFormLabelFamily.LegendsZA));
    }

    [Theory]
    [InlineData(PokemonFormLabelFamily.LegendsZA, 58, "Growlithe", "Kantonian")]
    [InlineData(PokemonFormLabelFamily.ScarletViolet, 194, "Wooper", "Johtonian")]
    [InlineData(PokemonFormLabelFamily.SwordShield, 77, "Ponyta", "Kanto")]
    public void ResolveBaseFormLabelReturnsRegionalBaseLabels(
        PokemonFormLabelFamily family,
        int speciesId,
        string speciesName,
        string expected)
    {
        Assert.Equal(expected, PokemonFormLabels.ResolveBaseFormLabel(speciesId, speciesName, family));
    }
}
