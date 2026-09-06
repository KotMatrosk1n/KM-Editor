// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SV;

namespace KM.SV.Models;

internal static class SvModelDiscovery
{
    internal static IReadOnlyList<SvModelCatalogEntry> Discover(string root)
    {
        SvTrinityArchiveIndex index;
        try { index = SvTrinityArchive.BuildIndex(root, 64 * 1024 * 1024); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        var hashes = index.Files.Select(file => file.FileHash).ToHashSet();
        var results = new Dictionary<string, SvModelCatalogEntry>(StringComparer.Ordinal);
        // Pack names flatten namespace separators. A reconstructed path is admitted only
        // when its full hash exists in the archive, and ambiguous reconstructions are rejected.
        foreach (var (folder, category) in new[] {
            ("chara/model_ob", "objects"), ("chara/model_tr", "other"),
            ("chara/model_uq", "other"), ("chara/model_vr", "other"),
            ("field_graphic/common_model", "environment"), ("field_graphic/terrain_model", "environment") })
        {
            var prefix = "arc/" + folder.Replace("/", "", StringComparison.Ordinal);
            foreach (var pack in index.Files.Select(file => file.PackName).Distinct(StringComparer.Ordinal)
                .Where(pack => pack.StartsWith(prefix, StringComparison.Ordinal) &&
                    (pack.EndsWith(".trmdl.trpak", StringComparison.Ordinal) || pack.EndsWith(".trmmt.trpak", StringComparison.Ordinal))))
            {
                var flattened = pack[prefix.Length..^12];
                var matches = new List<string>();
                for (var split = 1; split < flattened.Length; split++)
                {
                    var path = folder + "/" + flattened[..split] + "/" + flattened[split..] + ".trmdl";
                    if (hashes.Contains(SvTrinityPathHasher.HashPath(path))) matches.Add(path);
                }
                if (matches.Count != 1) continue;
                var id = matches[0];
                results.TryAdd(id, new(id, 0, 0, 0, Path.GetFileNameWithoutExtension(id), category));
                if (results.Count >= 4096) return results.Values.ToArray();
            }
        }
        return results.Values.OrderBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
    }
}
