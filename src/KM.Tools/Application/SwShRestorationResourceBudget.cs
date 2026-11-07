// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;

namespace KM.Tools.Application;

internal static class SwShRestorationResourceBudget
{
    internal static void LimitLayeredMegaTextures(Dictionary<string, byte[]> resources, IEnumerable<string> textures, string modelPath)
    {
        if (Path.GetFileName(modelPath) is not ("pm0623_01.gfbmdl" or "pm0623_01_rare.gfbmdl"
            or "pm0652_01.gfbmdl" or "pm0652_01_rare.gfbmdl"
            or "pm0870_01.gfbmdl" or "pm0870_01_rare.gfbmdl"))
            return;

        // Keep the stored BC7 mip tail of each large map to fit the model loader.
        foreach (var path in textures)
            resources[path] = SwShTextureAllocation.LimitBc7MipSize(resources[path], 512);
    }

    internal static void Validate(IReadOnlyDictionary<string, byte[]> resources)
    {
        var textures = resources.Where(p => p.Key.EndsWith(".bntx", StringComparison.Ordinal)).Select(p => p.Value.LongLength).ToArray();
        var total = resources.Values.Sum(bytes => bytes.LongLength);
        if (total > 16L * 1024 * 1024
            || textures.Sum() > 8L * 1024 * 1024 || textures.Any(length => length > 2L * 1024 * 1024))
        {
            var largest = string.Join(", ", resources.Where(p => p.Key.EndsWith(".bntx", StringComparison.Ordinal))
                .OrderByDescending(p => p.Value.Length).Take(12).Select(p => $"{Path.GetFileName(p.Key)}={p.Value.Length}"));
            var resourceSizes = string.Join(", ", resources.GroupBy(p => Path.GetExtension(p.Key), StringComparer.Ordinal)
                .Select(group => (Extension: group.Key, Bytes: group.Sum(p => p.Value.LongLength)))
                .OrderByDescending(group => group.Bytes).Select(group => $"{group.Extension}={group.Bytes}"));
            throw new InvalidDataException($"Converted model resources exceed the supported loading budget: {total} decoded bytes, {textures.Sum()} texture bytes, and {textures.DefaultIfEmpty().Max()} bytes in the largest texture. Resources by type: {resourceSizes}. Largest textures: {largest}");
        }
    }
}
