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
        IEnumerable<int> TextureIds(PreviewMaterial material) => new[] { material.Texture, material.Underlay, material.Mask, material.Highlight }
            .Concat(material.Surface?.Textures ?? []).Where(i => i >= 0);
        var colorIds = scene.Primitives.SelectMany(p => TextureIds(p.Material)).Distinct();
        var result = new List<ModelTextureResource>();
        foreach (var index in colorIds)
        {
            var path = scene.Textures[index].SourcePath;
            var matches = sources.Where(pair => pair.Key == path || Path.GetFileName(pair.Key) == path).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Color texture source is ambiguous.");
            var source = matches[0];
            result.Add(new(source.Key, scene.Primitives.Where(p => TextureIds(p.Material).Contains(index))
                .Select(p => p.Material.Name).Distinct().ToArray(), source.Value.Bytes, source.Value.Archive));
        }
        return result.ToArray();
    }
}
