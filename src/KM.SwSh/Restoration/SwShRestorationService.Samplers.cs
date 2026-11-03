// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.ZA;

namespace KM.SwSh.Restoration;

public sealed partial class SwShRestorationService
{
    // Inspect the installed material ABI rather than trusting an optional flag
    // in a prepared manifest. Older bundles and user customizations use the
    // same check, including asset updates that do not change the selection.
    internal static bool RequiresSamplerSupport(SwShPreparedRestoration prepared)
    {
        const int maximumModelBytes = 16 * 1024 * 1024;
        const int maximumArchiveEntries = 16384;
        var variants = SwShRestorationModelResources.Variants(SwShRestorationDefinitions.Get(prepared.DefinitionId))
            .Select(v => v.ResourceStem).Distinct(StringComparer.Ordinal).ToArray();
        bool required = false;
        foreach (var contribution in prepared.Contributions.Where(c => c.Writer == SwShRestorationWriter.File))
        {
            if (contribution.Path.EndsWith(".gfbmdl", StringComparison.Ordinal))
            {
                if (contribution.After.Length > maximumModelBytes)
                    throw new InvalidDataException("Restored sampler model exceeds the supported loading budget.");
                required |= ZaSwShFresnelMaterial.RequiresClampRuntime(contribution.After);
                continue;
            }
            var stem = variants.FirstOrDefault(s => contribution.Path == $"romfs/bin/archive/pokemon/{s}.gfpak"
                || contribution.Path == $"romfs/bin/archive/pokemon/{s}_rare.gfpak"
                || contribution.Path == $"romfs/bin/archive/gpokemon/{s}.gfpak");
            if (stem is null) continue;
            var pack = SwShGfPackFile.ParseBoundedReadOnly(contribution.After, maximumArchiveEntries, contribution.After.LongLength);
            if (pack.TryGetFileByName(stem + ".gfbmdl", maximumModelBytes, out var model))
                required |= ZaSwShFresnelMaterial.RequiresClampRuntime(model);
        }
        return required;
    }

    private static void AddSamplerSupport(ProjectPaths paths, SwShRestorationState state,
        IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources)
    {
        bool wanted = false;
        foreach (var selection in state.Selections.Where(s => s.Enabled))
            wanted |= RequiresSamplerSupport(SwShRestorationStateStore.ReadPrepared(paths, selection.CustomizationHash ?? selection.PreparedHash));
        const string relative = "exefs/main";
        var output = Output(paths, relative, includeExecutable: true);
        byte[]? current = outputs.TryGetValue(relative, out var pending) ? pending : null;
        if (current is null && File.Exists(output))
        {
            current = BoundedRead(output);
            sources.Add(new(ProjectFileLayer.Layered, relative));
        }
        if (!wanted && (current is null || !SwShRestorationSamplerPatcher.HasInstalledPatch(current))) return;
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            throw new InvalidDataException("Select the game's Base ExeFS folder to support the restored source samplers.");
        var baseline = BoundedRead(Path.Combine(paths.BaseExeFsPath, "main"));
        sources.Add(new(ProjectFileLayer.Base, relative));
        current ??= baseline;
        var patched = wanted ? SwShRestorationSamplerPatcher.Apply(baseline, current, paths.SelectedGame!.Value)
            : SwShRestorationSamplerPatcher.RestoreFromBase(baseline, current, paths.SelectedGame!.Value);
        if (!patched.AsSpan().SequenceEqual(current)) outputs[relative] = patched;
    }
}
