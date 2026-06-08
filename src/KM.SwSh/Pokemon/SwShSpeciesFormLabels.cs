// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

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
            [(80, 1)] = "Galarian",
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
            [(555, 1)] = "Galarian",
            [(562, 1)] = "Galarian",
            [(618, 1)] = "Galarian",
        };

    internal static string FormatSpeciesFormLabel(string speciesName, int speciesId, int localFormIndex)
    {
        if (localFormIndex == 0)
        {
            return speciesName;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{speciesName} ({ResolveKnownRegionalFormLabel(speciesId, localFormIndex) ?? $"Form {localFormIndex}"})");
    }

    internal static string ResolveRegionalFormLabel(int speciesId, int localFormIndex)
    {
        return ResolveKnownRegionalFormLabel(speciesId, localFormIndex)
            ?? (localFormIndex == 0
                ? "Regional Form"
                : string.Create(CultureInfo.InvariantCulture, $"Regional Form {localFormIndex}"));
    }

    private static string? ResolveKnownRegionalFormLabel(int speciesId, int localFormIndex)
    {
        return RegionalFormLabels.TryGetValue((speciesId, localFormIndex), out var label)
            ? label
            : null;
    }
}
