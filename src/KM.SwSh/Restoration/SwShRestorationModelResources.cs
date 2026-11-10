// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public static class SwShRestorationModelResources
{
    public static string PrimaryStem(SwShPokemonIdentity identity, bool legacy = false)
    {
        // Native filenames encode development IDs and sometimes gender rather
        // than National Dex/form IDs. Keep those native resources independently owned.
        var reserved = identity switch
        {
            { Species: 731 or 732 or 733 or 734 or 739 or 740 or 741, Form: 0 } => true,
            { Species: 3 or 65 or 130 or 194 or 208 or 212 or 214 or 215 or 257 or 445 or 460, Form: 1 } => true,
            { Form: 1 } when !legacy && HasFemaleMegaCollision(identity.Species) => true,
            _ => false,
        };
        return $"pm{identity.Species:0000}_{identity.Form:00}" + (reserved ? "_restored" : "");
    }

    private static bool HasFemaleMegaCollision(int species) => species is 154 or 229 or 308 or 323 or 398 or 668;

    public static IEnumerable<SwShRestorationModelVariant> Variants(SwShRestorationDefinition definition, bool legacy = false) =>
        definition.Identities.Select(i => new SwShRestorationModelVariant(i, 0, PrimaryStem(i, legacy)))
            .Concat(definition.AdditionalModelVariants.Select(v =>
                !legacy && v.Identity.Form == 0 && v.Gender == 1 && HasFemaleMegaCollision(v.Identity.Species)
                    ? v with { ResourceStem = v.ResourceStem + "_female" } : v));

    public static IEnumerable<SwShRestorationModelVariant> Variants(SwShPreparedRestoration prepared) =>
        Variants(SwShRestorationDefinitions.Get(prepared.DefinitionId), UsesLegacyLayout(prepared));

    internal static bool UsesLegacyLayout(SwShPreparedRestoration prepared) =>
        LegacyVariants(SwShRestorationDefinitions.Get(prepared.DefinitionId)).Any(variant =>
            prepared.Contributions.Any(item => item.Writer == SwShRestorationWriter.File
                && (item.Path == $"romfs/bin/archive/pokemon/{variant.ResourceStem}.gfpak"
                    || item.Path == $"romfs/bin/archive/gpokemon/{variant.ResourceStem}.gfpak")));

    internal static IEnumerable<SwShRestorationModelVariant> LegacyVariants(SwShRestorationDefinition definition) =>
        Variants(definition, legacy: true).Where(v => HasFemaleMegaCollision(v.Identity.Species)
            && ((v.Identity.Form == 0 && v.Gender == 1) || (v.Identity.Form == 1 && v.Gender == 0)));

    // Both new forms get independent paths so either one can be added alongside an
    // older installation. Preparing an update retains the installed model graph.
    public static bool UseLegacyLayout(ProjectPaths target, SwShRestorationDefinition definition)
    {
        var legacy = LegacyVariants(definition).ToArray();
        if (legacy.Length == 0 || string.IsNullOrWhiteSpace(target.OutputRootPath)) return false;
        var installed = SwShRestorationStateStore.Read(target).State.Selections.SingleOrDefault(s => s.DefinitionId == definition.Id);
        if (installed is not null)
            return installed.RemovalId is null
                && UsesLegacyLayout(SwShRestorationStateStore.ReadPrepared(target, installed.PreparedHash));

        var path = Path.Combine(target.OutputRootPath, SwShRestorationComposer.CatalogPath);
        if (!File.Exists(path)) return false;
        var catalog = new SwShPokemonResourceCatalog(SwShEffectivePokemonCatalog.Read(target, SwShRestorationComposer.CatalogPath));
        return legacy.Any(v =>
        {
            var key = new SwShPokemonResourceIdentity(v.Identity.Species, v.Identity.Form, v.Gender, false);
            var archive = $"bin/archive/pokemon/{v.ResourceStem}.gfpak";
            return catalog.Identities.Contains(key)
                && catalog.Read(key).Archive == archive
                && File.Exists(Path.Combine(target.OutputRootPath, "romfs", archive));
        });
    }

    public static void RequireUnowned(byte[] nativeCatalog, SwShRestorationDefinition definition, bool legacy = false)
    {
        var stems = Variants(definition, legacy).Select(v => v.ResourceStem).ToHashSet(StringComparer.Ordinal);
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
