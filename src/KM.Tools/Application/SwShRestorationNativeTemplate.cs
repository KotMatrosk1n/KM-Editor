// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

internal static class SwShRestorationNativeTemplate
{
    internal static string Stem(int species, Func<string, byte[]> read)
    {
        // Resource numbers diverge from National Dex numbers in later generations.
        // Resolve the logical camp/personal species through the native model catalog.
        var catalog = new SwShPokemonResourceCatalog(read(SwShRestorationComposer.CatalogPath[6..]));
        var row = catalog.Read(new(species, 0, 0, false));
        var stem = Path.GetFileNameWithoutExtension(row.Configuration);
        if (string.IsNullOrEmpty(stem) || row.Configuration != $"bin/pokemon/{stem}/{stem}.gfbpokecfg"
            || row.Archive != $"bin/archive/pokemon/{stem}.gfpak")
            throw new InvalidDataException("Native restoration template has unsupported resource routing.");
        return stem;
    }
}
