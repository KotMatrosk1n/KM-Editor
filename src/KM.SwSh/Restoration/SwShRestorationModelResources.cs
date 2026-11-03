// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public static class SwShRestorationModelResources
{
    public static string PrimaryStem(SwShPokemonIdentity identity)
    {
        // Native filenames encode development IDs and sometimes gender rather
        // than National Dex/form IDs. Keep those native resources independently owned.
        var reserved = identity switch
        {
            { Species: 731 or 732 or 733 or 734 or 739 or 740 or 741, Form: 0 } => true,
            { Species: 3 or 65 or 130 or 194 or 208 or 212 or 214 or 215 or 257 or 445 or 460, Form: 1 } => true,
            _ => false,
        };
        return $"pm{identity.Species:0000}_{identity.Form:00}" + (reserved ? "_restored" : "");
    }

    public static IEnumerable<SwShRestorationModelVariant> Variants(SwShRestorationDefinition definition) =>
        definition.Identities.Select(i => new SwShRestorationModelVariant(i, 0, PrimaryStem(i)))
            .Concat(definition.AdditionalModelVariants);

    public static void RequireUnowned(byte[] nativeCatalog, SwShRestorationDefinition definition)
    {
        var stems = Variants(definition).Select(v => v.ResourceStem).ToHashSet(StringComparer.Ordinal);
        var catalog = new SwShPokemonResourceCatalog(nativeCatalog);
        foreach (var identity in catalog.Identities)
        {
            var row = catalog.Read(identity);
            if (stems.Any(stem => row.Archive == $"bin/archive/pokemon/{stem}.gfpak"
                || row.Model.StartsWith($"bin/pokemon/{stem}/", StringComparison.Ordinal)))
                throw new InvalidDataException("Restoration model resources overlap an existing native model.");
        }
    }
}
