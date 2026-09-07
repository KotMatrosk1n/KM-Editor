// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

/// <summary>Resolves the model's declared asset dependencies, including unused detail levels.</summary>
public static class ModelAssetGraph
{
    public static ModelTextureResource[] Read(IEnumerable<(string Path, string? Archive)> roots, Func<string, string?, byte[]> read)
    {
        var pending = new Queue<(string Path, string? Archive)>(roots);
        var assets = new Dictionary<string, ModelTextureResource>(StringComparer.Ordinal);
        long total = 0;
        while (pending.TryDequeue(out var item))
        {
            if (assets.ContainsKey(item.Path)) continue;
            if (TrinityPreviewReader.Resolve("catalog", item.Path) != item.Path) throw new InvalidDataException("Model asset path is invalid.");
            var bytes = read(item.Path, item.Archive);
            total += bytes.Length;
            if (assets.Count >= 2048 || total > 256L * 1024 * 1024) throw new InvalidDataException("Model asset budget exceeded.");
            assets.Add(item.Path, new(item.Path, [], bytes, item.Archive));
            var extension = Path.GetExtension(item.Path);
            if (extension is ".bntx" or ".trmbf" or ".trskl" or ".tranm" or ".tracm" or ".tracp" or ".tracl" or ".gfbanm") continue;
            if (extension is not (".trmdl" or ".trmsh" or ".trmtr" or ".trmmt" or ".tracn" or ".tracr" or ".gfbanmcfg" or ".gfbmdl")) continue;
            var data = new ModelBuffer(bytes);
            void Add(string? relative) { if (!string.IsNullOrWhiteSpace(relative)) pending.Enqueue((TrinityPreviewReader.Resolve(item.Path, relative), item.Archive)); }
            void Strings(int table, int field)
            {
                var (start, count) = data.Vector(table, field, 4, 256);
                for (var i = 0; i < count; i++) Add(data.StringAt(start + i * 4));
            }
            if (extension == ".trmdl")
            {
                foreach (var mesh in data.Tables(data.Root, 1, 32)) Add(data.Text(mesh, 0));
                var skeleton = data.Table(data.Root, 2); if (skeleton != 0) Add(data.Text(skeleton, 0));
                Strings(data.Root, 3);
            }
            else if (extension == ".trmsh") Add(data.Text(data.Root, 2));
            else if (extension == ".trmtr")
                foreach (var material in data.Tables(data.Root, 1, 256))
                    foreach (var texture in data.Tables(material, 2, 64)) Add(data.Text(texture, 1));
            else if (extension == ".trmmt")
                foreach (var variant in data.Tables(data.Root, 2, 64)) Strings(variant, 1);
            else if (extension == ".tracn")
                foreach (var name in data.Tables(data.Root, 0, 256)) Add(data.Text(name, 1));
            else if (extension == ".tracr")
            {
                var group = data.Table(data.Root, 0);
                if (group != 0) foreach (var track in data.Tables(group, 0, 2048))
                {
                    var resources = data.Table(track, 3); if (resources == 0) continue;
                    for (var field = 0; field < 2; field++)
                    {
                        var reference = data.Table(resources, field); if (reference != 0) Add(data.Text(reference, 0));
                    }
                }
            }
            else if (extension == ".gfbanmcfg")
            {
                var group = data.Table(data.Root, 6);
                if (group != 0) foreach (var track in data.Tables(group, 0, 2048)) Add(data.Text(track, 1));
            }
            else if (extension == ".gfbmdl")
            {
                var (start, count) = data.Vector(data.Root, 2, 4, 256);
                for (var i = 0; i < count; i++) Add("../tex/" + data.StringAt(start + i * 4) + ".bntx");
            }
        }
        return assets.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
    }
}
