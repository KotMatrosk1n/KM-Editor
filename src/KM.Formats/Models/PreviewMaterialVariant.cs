// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

public sealed record PreviewMaterialVariant(string[] Materials, IReadOnlyDictionary<string, bool> Visibility)
{
    public static PreviewMaterialVariant? Shiny(ModelBuffer data, string path)
    {
        var matches = data.Tables(data.Root, 2, 64).Where(entry => data.Text(entry, 0) == "rare").ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1) throw new InvalidDataException("Shiny material variant is ambiguous.");
        var entry = matches[0];
        var (start, count) = data.Vector(entry, 1, 4, 16);
        if (count == 0) throw new InvalidDataException("Shiny material variant has no materials.");
        var paths = Enumerable.Range(0, count).Select(i => TrinityPreviewReader.Resolve(path, data.StringAt(start + i * 4))).ToArray();
        if (paths.Any(p => !p.EndsWith(".trmtr", StringComparison.Ordinal))) throw new InvalidDataException("Shiny material dependency is unsupported.");
        var visibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var mesh in data.Tables(entry, 2, 256))
        {
            var name = data.Text(mesh, 0) ?? throw new InvalidDataException("Shiny mesh target is missing.");
            var at = data.Field(mesh, 1); var visible = at == 0 ? 0 : data.U8(at);
            if (visible > 1 || (visibility.TryGetValue(name, out var previous) && previous != (visible != 0)))
                throw new InvalidDataException("Shiny mesh visibility is invalid.");
            visibility[name] = visible != 0;
        }
        return new(paths, visibility);
    }
}
