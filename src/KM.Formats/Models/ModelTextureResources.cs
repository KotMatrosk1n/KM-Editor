// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

public sealed record ModelTextureResource(string Id, string[] Materials, byte[] Bytes, string? Archive);

public static class ModelTextureResources
{
    public static ModelTextureResource[] Capture(Func<Action<string, byte[], string?>, PreviewScene> load)
    {
        var sources = new Dictionary<string, (byte[] Bytes, string? Archive)>(StringComparer.Ordinal);
        var scene = load((path, bytes, archive) =>
        {
            if (path.EndsWith(".bntx", StringComparison.Ordinal)) sources[path] = (bytes, archive);
        });
        var colorIds = scene.Primitives.SelectMany(p => new[] { p.Material.Texture, p.Material.Underlay }).Where(i => i >= 0).Distinct();
        var masks = scene.Primitives.SelectMany(p => new[] { p.Material.Mask, p.Material.Highlight }).ToHashSet();
        var result = new List<ModelTextureResource>();
        foreach (var index in colorIds)
        {
            if (masks.Contains(index)) continue;
            var path = scene.Textures[index].SourcePath;
            var matches = sources.Where(pair => pair.Key == path || Path.GetFileName(pair.Key) == path).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Color texture source is ambiguous.");
            var source = matches[0];
            result.Add(new(source.Key, scene.Primitives.Where(p => p.Material.Texture == index || p.Material.Underlay == index)
                .Select(p => p.Material.Name).Distinct().ToArray(), source.Value.Bytes, source.Value.Archive));
        }
        return result.ToArray();
    }
}
