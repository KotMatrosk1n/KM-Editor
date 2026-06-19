// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;

namespace KM.SwSh.Pokemon;

internal static class SwShSpeciesFormLabels
{
    private static readonly IReadOnlyDictionary<(int SpeciesId, int LocalFormIndex), string> RegionalFormLabels =
        new Dictionary<(int SpeciesId, int LocalFormIndex), string>
        {
            [(19, 1)] = "Alolan",
            [(20, 1)] = "Alolan",
            [(26, 1)] = "Alolan",
            [(27, 1)] = "Alolan",
            [(28, 1)] = "Alolan",
            [(37, 1)] = "Alolan",
            [(38, 1)] = "Alolan",
            [(50, 1)] = "Alolan",
            [(51, 1)] = "Alolan",
            [(52, 1)] = "Alolan",
            [(52, 2)] = "Galarian",
            [(53, 1)] = "Alolan",
            [(74, 1)] = "Alolan",
            [(75, 1)] = "Alolan",
            [(76, 1)] = "Alolan",
            [(77, 1)] = "Galarian",
            [(78, 1)] = "Galarian",
            [(79, 1)] = "Galarian",
            [(80, 2)] = "Galarian",
            [(83, 1)] = "Galarian",
            [(88, 1)] = "Alolan",
            [(89, 1)] = "Alolan",
            [(103, 1)] = "Alolan",
            [(105, 1)] = "Alolan",
            [(110, 1)] = "Galarian",
            [(122, 1)] = "Galarian",
            [(144, 1)] = "Galarian",
            [(145, 1)] = "Galarian",
            [(146, 1)] = "Galarian",
            [(199, 1)] = "Galarian",
            [(222, 1)] = "Galarian",
            [(263, 1)] = "Galarian",
            [(264, 1)] = "Galarian",
            [(554, 1)] = "Galarian",
            [(555, 2)] = "Galarian",
            [(555, 3)] = "Galarian",
            [(562, 1)] = "Galarian",
            [(618, 1)] = "Galarian",
        };

    private static readonly IReadOnlyDictionary<int, string> BaseRegionalFormLabels =
        new Dictionary<int, string>
        {
            [19] = "Kanto",
            [20] = "Kanto",
            [26] = "Kanto",
            [27] = "Kanto",
            [28] = "Kanto",
            [37] = "Kanto",
            [38] = "Kanto",
            [50] = "Kanto",
            [51] = "Kanto",
            [52] = "Kanto",
            [53] = "Kanto",
            [74] = "Kanto",
            [75] = "Kanto",
            [76] = "Kanto",
            [77] = "Kanto",
            [78] = "Kanto",
            [79] = "Kanto",
            [80] = "Kanto",
            [83] = "Kanto",
            [88] = "Kanto",
            [89] = "Kanto",
            [103] = "Kanto",
            [105] = "Kanto",
            [110] = "Kanto",
            [122] = "Kanto",
            [144] = "Kanto",
            [145] = "Kanto",
            [146] = "Kanto",
            [199] = "Kanto",
            [222] = "Johto",
            [263] = "Hoenn",
            [264] = "Hoenn",
            [554] = "Unovan",
            [562] = "Unovan",
            [618] = "Unovan",
        };

    private static readonly IReadOnlyDictionary<(int SpeciesId, int LocalFormIndex), string> KnownFormLabels =
        CreateKnownFormLabels();

    internal static string FormatSpeciesFormLabel(string speciesName, int speciesId, int localFormIndex)
    {
        var knownFormLabel = ResolveKnownFormLabel(speciesId, localFormIndex);
        if (localFormIndex == 0)
        {
            var baseRegionalFormLabel = ResolveBaseRegionalFormLabel(speciesId);
            var baseFormLabel = knownFormLabel ?? baseRegionalFormLabel;
            return baseFormLabel is null || SpeciesAlreadyIncludesFormLabel(speciesName, baseFormLabel)
                ? speciesName
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{speciesName} ({baseFormLabel})");
        }

        var formLabel = knownFormLabel
            ?? string.Create(CultureInfo.InvariantCulture, $"Form {localFormIndex}");

        return SpeciesAlreadyIncludesFormLabel(speciesName, formLabel)
            ? speciesName
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{speciesName} ({formLabel})");
    }

    internal static string FormatSpeciesFormOptionLabel(int speciesId, int localFormIndex)
    {
        return ResolveKnownFormLabel(speciesId, localFormIndex)
            ?? (localFormIndex == 0
                ? ResolveBaseRegionalFormLabel(speciesId) ?? "Base"
                : string.Create(CultureInfo.InvariantCulture, $"Form {localFormIndex}"));
    }

    internal static string? ResolveKnownFormLabel(int speciesId, int localFormIndex)
    {
        return KnownFormLabels.TryGetValue((speciesId, localFormIndex), out var label)
            ? label
            : null;
    }

    internal static string ResolveRegionalFormLabel(int speciesId, int localFormIndex)
    {
        if (localFormIndex == 0)
        {
            return ResolveBaseRegionalFormLabel(speciesId) ?? "Original";
        }

        return ResolveKnownFormLabel(speciesId, localFormIndex)
            ?? string.Create(CultureInfo.InvariantCulture, $"Regional Form {localFormIndex}");
    }

    private static Dictionary<(int SpeciesId, int LocalFormIndex), string> CreateKnownFormLabels()
    {
        var labels = new Dictionary<(int SpeciesId, int LocalFormIndex), string>(RegionalFormLabels)
        {
            [(25, 1)] = "Original Cap",
            [(25, 2)] = "Hoenn Cap",
            [(25, 3)] = "Sinnoh Cap",
            [(25, 4)] = "Unova Cap",
            [(25, 5)] = "Kalos Cap",
            [(25, 6)] = "Alola Cap",
            [(25, 7)] = "Partner Cap",
            [(25, 8)] = "World Cap",
            [(201, 26)] = "Question Mark",
            [(201, 27)] = "Exclamation Mark",
            [(421, 0)] = "Overcast Form",
            [(421, 1)] = "Sunshine Form",
            [(422, 0)] = "West Sea",
            [(422, 1)] = "East Sea",
            [(423, 0)] = "West Sea",
            [(423, 1)] = "East Sea",
            [(487, 0)] = "Altered Forme",
            [(487, 1)] = "Origin Forme",
            [(479, 0)] = "Normal",
            [(479, 1)] = "Heat",
            [(479, 2)] = "Wash",
            [(479, 3)] = "Frost",
            [(479, 4)] = "Fan",
            [(479, 5)] = "Mow",
            [(521, 0)] = "Male",
            [(521, 1)] = "Female",
            [(550, 0)] = "Red-Striped",
            [(550, 1)] = "Blue-Striped",
            [(555, 0)] = "Unovan Standard Mode",
            [(555, 1)] = "Unovan Zen Mode",
            [(555, 2)] = "Galarian Standard Mode",
            [(555, 3)] = "Galarian Zen Mode",
            [(592, 0)] = "Male",
            [(592, 1)] = "Female",
            [(593, 0)] = "Male",
            [(593, 1)] = "Female",
            [(649, 0)] = "Normal",
            [(649, 1)] = "Douse Drive",
            [(649, 2)] = "Shock Drive",
            [(649, 3)] = "Burn Drive",
            [(649, 4)] = "Chill Drive",
            [(641, 0)] = "Incarnate Forme",
            [(641, 1)] = "Therian Forme",
            [(642, 0)] = "Incarnate Forme",
            [(642, 1)] = "Therian Forme",
            [(645, 0)] = "Incarnate Forme",
            [(645, 1)] = "Therian Forme",
            [(646, 0)] = "Kyurem",
            [(646, 1)] = "White Kyurem",
            [(646, 2)] = "Black Kyurem",
            [(647, 0)] = "Ordinary Form",
            [(647, 1)] = "Resolute Form",
            [(678, 0)] = "Male",
            [(678, 1)] = "Female",
            [(681, 0)] = "Shield Forme",
            [(681, 1)] = "Blade Forme",
            [(710, 0)] = "Average Size",
            [(710, 1)] = "Small Size",
            [(710, 2)] = "Large Size",
            [(710, 3)] = "Super Size",
            [(711, 0)] = "Average Size",
            [(711, 1)] = "Small Size",
            [(711, 2)] = "Large Size",
            [(711, 3)] = "Super Size",
            [(716, 0)] = "Neutral Mode",
            [(716, 1)] = "Active Mode",
            [(718, 0)] = "50% Forme",
            [(718, 1)] = "10% Forme",
            [(718, 2)] = "Complete Forme",
            [(744, 0)] = "Standard",
            [(744, 1)] = "Own Tempo",
            [(745, 0)] = "Midday Form",
            [(745, 1)] = "Midnight Form",
            [(745, 2)] = "Dusk Form",
            [(746, 0)] = "Solo Form",
            [(746, 1)] = "School Form",
            [(778, 0)] = "Disguised Form",
            [(778, 1)] = "Busted Form",
            [(800, 0)] = "Necrozma",
            [(800, 1)] = "Dusk Mane",
            [(800, 2)] = "Dawn Wings",
            [(800, 3)] = "Ultra Necrozma",
            [(801, 0)] = "Normal",
            [(801, 1)] = "Original Color",
            [(845, 0)] = "Normal",
            [(845, 1)] = "Gulping Form",
            [(845, 2)] = "Gorging Form",
            [(849, 0)] = "Amped Form",
            [(849, 1)] = "Low Key Form",
            [(854, 0)] = "Phony Form",
            [(854, 1)] = "Antique Form",
            [(855, 0)] = "Phony Form",
            [(855, 1)] = "Antique Form",
            [(875, 0)] = "Ice Face",
            [(875, 1)] = "Noice Face",
            [(876, 0)] = "Male",
            [(876, 1)] = "Female",
            [(877, 0)] = "Full Belly Mode",
            [(877, 1)] = "Hangry Mode",
            [(888, 0)] = "Hero of Many Battles",
            [(888, 1)] = "Crowned Sword",
            [(889, 0)] = "Hero of Many Battles",
            [(889, 1)] = "Crowned Shield",
            [(890, 0)] = "Eternatus",
            [(890, 1)] = "Eternamax",
            [(892, 0)] = "Single Strike Style",
            [(892, 1)] = "Rapid Strike Style",
            [(893, 0)] = "Zarude",
            [(893, 1)] = "Dada",
            [(898, 0)] = "Calyrex",
            [(898, 1)] = "Ice Rider",
            [(898, 2)] = "Shadow Rider",
        };

        AddSilvallyFormLabels(labels);

        for (var form = 0; form < 26; form++)
        {
            labels[(201, form)] = ((char)('A' + form)).ToString();
        }

        AddAlcremieFormLabels(labels);

        return labels;
    }

    private static void AddAlcremieFormLabels(Dictionary<(int SpeciesId, int LocalFormIndex), string> labels)
    {
        var creams = new[]
        {
            "Vanilla Cream",
            "Ruby Cream",
            "Matcha Cream",
            "Mint Cream",
            "Lemon Cream",
            "Salted Cream",
            "Ruby Swirl",
            "Caramel Swirl",
            "Rainbow Swirl",
        };
        var sweets = new[]
        {
            "Strawberry Sweet",
            "Berry Sweet",
            "Love Sweet",
            "Star Sweet",
            "Clover Sweet",
            "Flower Sweet",
            "Ribbon Sweet",
        };

        for (var creamIndex = 0; creamIndex < creams.Length; creamIndex++)
        {
            for (var sweetIndex = 0; sweetIndex < sweets.Length; sweetIndex++)
            {
                var localFormIndex = creamIndex * sweets.Length + sweetIndex;
                labels[(869, localFormIndex)] = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{creams[creamIndex]} / {sweets[sweetIndex]}");
            }
        }
    }

    private static void AddSilvallyFormLabels(Dictionary<(int SpeciesId, int LocalFormIndex), string> labels)
    {
        var types = new[]
        {
            "Normal Type",
            "Fighting Type",
            "Flying Type",
            "Poison Type",
            "Ground Type",
            "Rock Type",
            "Bug Type",
            "Ghost Type",
            "Steel Type",
            "Fire Type",
            "Water Type",
            "Grass Type",
            "Electric Type",
            "Psychic Type",
            "Ice Type",
            "Dragon Type",
            "Dark Type",
            "Fairy Type",
        };

        for (var form = 0; form < types.Length; form++)
        {
            labels[(773, form)] = types[form];
        }
    }

    internal static string? ResolveBaseRegionalFormLabel(int speciesId)
    {
        return BaseRegionalFormLabels.TryGetValue(speciesId, out var label) ? label : null;
    }

    private static bool SpeciesAlreadyIncludesFormLabel(string speciesName, string formLabel)
    {
        var trimmedSpecies = speciesName.TrimEnd();
        if (!trimmedSpecies.EndsWith(')'))
        {
            return false;
        }

        var openParenthesis = trimmedSpecies.LastIndexOf('(');
        if (openParenthesis < 0 || openParenthesis >= trimmedSpecies.Length - 1)
        {
            return false;
        }

        var existingLabel = trimmedSpecies.Substring(
            openParenthesis + 1,
            trimmedSpecies.Length - openParenthesis - 2);
        return NormalizeFormLabel(existingLabel) == NormalizeFormLabel(formLabel);
    }

    private static string NormalizeFormLabel(string label)
    {
        return new string(
            label
                .Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

}
