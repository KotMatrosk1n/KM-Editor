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
            ("chara/model_ob", "objects"), ("chara/model_tr", "trainers"),
            ("chara/model_uq", "trainers"), ("chara/model_vr", "other"),
            ("field_graphic/common_model", "environment"), ("field_graphic/terrain_model", "environment") })
        {
            var prefix = "arc/" + folder.Replace("/", "", StringComparison.Ordinal);
            foreach (var pack in index.Files.Select(file => file.PackName).Distinct(StringComparer.Ordinal)
                .Where(pack => pack.StartsWith(prefix, StringComparison.Ordinal) &&
                    (pack.EndsWith(".trmdl.trpak", StringComparison.Ordinal) || pack.EndsWith(".trmmt.trpak", StringComparison.Ordinal)
                    || pack.EndsWith("_loc.trskl.trpak", StringComparison.Ordinal))))
            {
                var flattened = pack[prefix.Length..^12];
                if (pack.EndsWith("_loc.trskl.trpak", StringComparison.Ordinal)) flattened = flattened[..^4];
                var matches = new List<string>();
                for (var split = 1; split < flattened.Length; split++)
                {
                    var path = folder + "/" + flattened[..split] + "/" + flattened[split..] + ".trmdl";
                    if (hashes.Contains(SvTrinityPathHasher.HashPath(path))) matches.Add(path);
                }
                if (matches.Count != 1) continue;
                var id = matches[0];
                var name = category == "trainers" ? id.Split('/')[^2].Replace('_', ' ') : Path.GetFileNameWithoutExtension(id);
                results.TryAdd(id, new(id, 0, 0, 0, name, category));
                if (results.Count >= 4096) return results.Values.ToArray();
            }
        }
        return results.Values.OrderBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
    }

    internal static string[] AnimationCatalogs(string root, string model)
    {
        if (!model.StartsWith("chara/model_", StringComparison.Ordinal)) return [model[..^6] + ".tracn"];
        var folder = model[..(model.LastIndexOf('/') + 1)].Replace("/model_", "/motion_", StringComparison.Ordinal);
        var prefix = "arc/" + folder.Replace("/", "", StringComparison.Ordinal);
        var index = SvTrinityArchive.BuildIndex(root, 64 * 1024 * 1024);
        var hashes = index.Files.Select(file => file.FileHash).ToHashSet();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var stem = Path.GetFileNameWithoutExtension(model);
        names.Add(stem);
        foreach (var suffix in new[] { "_base", "_battle", "_field", "_other", "_face", "_head00" }) names.Add(stem + suffix);
        foreach (var pack in index.Files.Select(file => file.PackName).Distinct(StringComparer.Ordinal)
            .Where(pack => pack.StartsWith(prefix, StringComparison.Ordinal) && pack.EndsWith(".trpak", StringComparison.Ordinal) && pack.Length <= 256))
            names.Add(Path.GetFileNameWithoutExtension(pack[prefix.Length..^6]));
        return names.Select(name => folder + name + ".tracn").Where(path => hashes.Contains(SvTrinityPathHasher.HashPath(path)))
            .OrderBy(path => path, StringComparer.Ordinal).Take(64).ToArray();
    }
}
