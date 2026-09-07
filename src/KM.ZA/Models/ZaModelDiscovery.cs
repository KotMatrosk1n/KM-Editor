// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.ZA.Models;

internal static class ZaModelDiscovery
{
    internal static IReadOnlyList<ZaModelCatalogEntry> Discover(OpenedProject project)
    {
        var results = new Dictionary<string, ZaModelCatalogEntry>(StringComparer.Ordinal);
        void Add(string path, string category)
        {
            if (category == "trainers" && !Path.GetFileName(path).StartsWith("tr", StringComparison.Ordinal)) return;
            if (TrinityPreviewReader.Resolve("catalog", path) != path)
                throw new InvalidDataException("Model path is unsupported.");
            var name = category == "trainers" ? path.Split('/')[^2].Replace('_', ' ') : Path.GetFileNameWithoutExtension(path);
            results.TryAdd(path, new(path, 0, 0, 0, name, category));
            if (results.Count > 4096) throw new InvalidDataException("Model catalog exceeds the preview budget.");
        }
        foreach (var file in project.FileGraph.Entries)
        {
            if (!file.RelativePath.StartsWith("romfs/", StringComparison.Ordinal) || !file.RelativePath.EndsWith(".trmdl", StringComparison.Ordinal)) continue;
            var path = file.RelativePath[6..];
            if (path.StartsWith("ik_chara/", StringComparison.Ordinal)) Add(path, "objects");
            else if (path.StartsWith("field/", StringComparison.Ordinal)) Add(path, "environment");
        }
        if (project.Paths.BaseRomFsPath is not { } root) return results.Values.ToArray();
        ZaTrinityArchiveIndex index;
        try { index = ZaTrinityArchive.BuildIndex(root, 64 * 1024 * 1024); }
        catch (FileNotFoundException) { return results.Values.ToArray(); }
        catch (DirectoryNotFoundException) { return results.Values.ToArray(); }
        var hashes = index.Files.Select(file => file.FileHash).ToHashSet();
        var packs = index.Files.Select(file => file.PackName).Distinct(StringComparer.Ordinal).ToArray();
        // Pack names flatten separators. Admit only a unique reconstruction whose
        // full virtual path hash is present in this project's archive descriptor.
        foreach (var (folder, category) in new[] {
            ("ik_chara/model_ob", "objects"), ("ik_chara/model_pc", "other"),
            ("ik_chara/model_uq", "trainers"), ("ik_chara/model_cc_ir", "trainers") })
        {
            var prefix = "arc/" + folder.Replace("/", "", StringComparison.Ordinal);
            foreach (var pack in packs.Where(pack => pack.StartsWith(prefix, StringComparison.Ordinal)
                && (pack.EndsWith(".trmdl.trpak", StringComparison.Ordinal) || pack.EndsWith("_loc.trskl.trpak", StringComparison.Ordinal)) && pack.Length <= 256))
            {
                var flattened = pack[prefix.Length..^12];
                if (pack.EndsWith("_loc.trskl.trpak", StringComparison.Ordinal)) flattened = flattened[..^4];
                var matches = new List<string>();
                for (var split = 1; split < flattened.Length; split++)
                {
                    var path = folder + "/" + flattened[..split] + "/" + flattened[split..] + ".trmdl";
                    if (hashes.Contains(ZaTrinityPathHasher.HashPath(path))) matches.Add(path);
                }
                if (matches.Count == 1) Add(matches[0], category);
            }
        }
        foreach (var pack in packs.Where(pack => pack.StartsWith("arc/fieldmodel", StringComparison.Ordinal)
            && pack.EndsWith(".trmdl.trpak", StringComparison.Ordinal) && pack.Length <= 256))
        {
            var flat = pack[14..^12];
            var matches = new HashSet<string>(StringComparer.Ordinal);
            // Field assets repeat their family prefix in the asset folder and file.
            // This bounds reconstruction without shipping an extracted path catalog.
            for (var a = 1; a <= Math.Min(16, flat.Length - 3); a++)
                for (var b = a + 1; b <= Math.Min(a + 32, flat.Length - 2); b++)
                {
                    var family = flat[a..b];
                    if (!flat.AsSpan(b).StartsWith(family, StringComparison.Ordinal)) continue;
                    for (var c = b + family.Length; c < flat.Length; c++)
                    {
                        if (!flat.AsSpan(c).StartsWith(family, StringComparison.Ordinal)) continue;
                        var path = $"field/model/{flat[..a]}/{family}/{flat[b..c]}/{flat[c..]}.trmdl";
                        if (hashes.Contains(ZaTrinityPathHasher.HashPath(path))) matches.Add(path);
                    }
                }
            if (matches.Count == 1) Add(matches.Single(), "environment");
        }
        return results.Values.OrderBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
    }

    internal static string[] AnimationCatalogs(string root, string model)
    {
        if (!model.StartsWith("ik_chara/model_", StringComparison.Ordinal)) return [model[..^6] + ".tracn"];
        var folder = model[..(model.LastIndexOf('/') + 1)].Replace("/model_", "/motion_", StringComparison.Ordinal);
        var prefix = "arc/" + folder.Replace("/", "", StringComparison.Ordinal);
        var index = ZaTrinityArchive.BuildIndex(root, 64 * 1024 * 1024);
        var hashes = index.Files.Select(file => file.FileHash).ToHashSet();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var stem = Path.GetFileNameWithoutExtension(model);
        names.Add(stem);
        foreach (var suffix in new[] { "_base", "_battle", "_field", "_other", "_face", "_head00" }) names.Add(stem + suffix);
        foreach (var pack in index.Files.Select(file => file.PackName).Distinct(StringComparer.Ordinal)
            .Where(pack => pack.StartsWith(prefix, StringComparison.Ordinal) && pack.EndsWith(".trpak", StringComparison.Ordinal) && pack.Length <= 256))
            names.Add(Path.GetFileNameWithoutExtension(pack[prefix.Length..^6]));
        return names.Select(name => folder + name + ".tracn").Where(path => hashes.Contains(ZaTrinityPathHasher.HashPath(path)))
            .OrderBy(path => path, StringComparer.Ordinal).Take(64).ToArray();
    }
}
