// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationCustomizations
{
    internal static ChangePlan AddAnchor(ProjectPaths paths, ChangePlan plan)
    {
        if (plan.Writes.Count == 0 || plan.EffectivePendingEdits?.Any(e => e.Domain == SwShRestorationDefinitions.Domain) == true) return plan;
        var state = SwShRestorationStateStore.Read(paths);
        if (!state.State.Selections.Any(s => s.Enabled)) return BindCatalogSources(paths, plan);
        var targets = plan.Writes.Select(w => w.TargetRelativePath).ToHashSet(StringComparer.Ordinal);
        var resourcePaths = new HashSet<string>(StringComparer.Ordinal);
        var needsAnchor = false;
        // The installed set can be larger than the operation cache. Consume one
        // package at a time so its size is not an editing limit for the project.
        foreach (var selection in state.State.Selections.Where(s => s.Enabled))
        {
            var prepared = SwShRestorationStateStore.ReadPrepared(paths, selection.PreparedHash);
            foreach (var contribution in prepared.Contributions) resourcePaths.Add(contribution.Path);
            needsAnchor |= prepared.Contributions.Any(c => targets.Contains(c.Path))
                || targets.Any(p => IsLooseModel(p, SwShRestorationDefinitions.Get(prepared.DefinitionId)));
        }
        // Species validation runs again in a verified snapshot. Bind the complete
        // installed resource closure so that snapshot sees the same capabilities.
        var dependencies = resourcePaths
            .Where(p => File.Exists(Path.Combine(paths.OutputRootPath!, p)) || File.Exists(Path.Combine(paths.BaseRomFsPath!, p[6..])))
            .Select(p => new ProjectFileReference(File.Exists(Path.Combine(paths.OutputRootPath!, p)) ? ProjectFileLayer.Layered : ProjectFileLayer.Base, p)).ToArray();
        var first = plan.Writes[0];
        plan = plan with { Writes = plan.Writes.Select((write, index) => index == 0
            ? first with { Sources = first.Sources.Concat(dependencies).Distinct().ToArray() } : write).ToArray() };
        if (!needsAnchor) return BindCatalogSources(paths, plan);
        if (targets.Contains(SwShRestorationComposer.PersonalPath)) return BindCatalogSources(paths, plan);
        var personal = SwShRestorationComposer.PersonalPath;
        var sources = new List<ProjectFileReference> { new(ProjectFileLayer.Base, personal) };
        if (File.Exists(Path.Combine(paths.OutputRootPath!, personal))) sources.Add(new(ProjectFileLayer.Layered, personal));
        plan = plan with { Writes = plan.Writes.Append(new(personal, sources, sources.Count == 2,
            "Retain restoration settings and Pokemon customizations.")).ToArray() };
        return BindCatalogSources(paths, plan);
    }

    private static ChangePlan BindCatalogSources(ProjectPaths paths, ChangePlan plan)
    {
        // Installed dependencies and customization anchors can introduce personal
        // data. Bind its catalog now so repeated captures retain the same sources.
        // This also includes models from restorations without installation records.
        if (plan.Writes.Any(w => w.Sources.Any(s => s.RelativePath == SwShRestorationComposer.PersonalPath)))
        {
            var catalog = SwShEffectivePokemonCatalog.Load(paths);
            if (catalog.Available)
                plan = plan with { Writes = plan.Writes.Select((write, index) => index == 0
                    ? write with { Sources = write.Sources.Concat(catalog.Sources).Distinct().ToArray() } : write).ToArray() };
        }
        return plan;
    }

    internal static SwShRestorationStateSnapshot Capture(ProjectPaths paths, SwShRestorationStateSnapshot state,
        IReadOnlySet<string> changed, Func<string, byte[]?> readOutput)
    {
        var selections = new List<SwShRestorationSelection>(); var any = false;
        SwShRestorationReadScope.Find(paths)?.Prefetch(state.State.Selections.Where(s => s.Enabled)
            .Select(s => s.CustomizationHash ?? s.PreparedHash));
        foreach (var selection in state.State.Selections)
        {
            if (!selection.Enabled) { selections.Add(selection); continue; }
            var previous = SwShRestorationStateStore.ReadPrepared(paths, selection.CustomizationHash ?? selection.PreparedHash);
            var items = previous.Contributions.ToDictionary(c => (c.Path, c.Key));
            var touched = items.Values.Where(c => changed.Contains(c.Path) && readOutput(c.Path) is not null).ToArray();
            if (touched.Length > 0)
                foreach (var item in SwShRestorationComposer.Compose(touched, readOutput, false, captureOnly: true).RetainedCustomizations)
                    items[(item.Path, item.Key)] = SwShRestorationDependencies.Retain(items[(item.Path, item.Key)], item, state.State);
            var definition = SwShRestorationDefinitions.Get(selection.DefinitionId);
            foreach (var path in changed.Where(p => IsLooseModel(p, definition)))
            {
                if (readOutput(path) is { } bytes) items[(path, "")] = new(path, SwShRestorationWriter.File, "", items.GetValueOrDefault((path, ""))?.Before, bytes);
                else items.Remove((path, ""));
            }
            if (touched.Length == 0 && !changed.Any(p => IsLooseModel(p, definition))) { selections.Add(selection); continue; }
            if (items.Count == previous.Contributions.Count && previous.Contributions.All(before =>
                items.TryGetValue((before.Path, before.Key), out var after) && before.Writer == after.Writer
                && ((before.Before is null && after.Before is null) || (before.Before is not null && after.Before is not null
                    && before.Before.AsSpan().SequenceEqual(after.Before)))
                && before.After.AsSpan().SequenceEqual(after.After)))
            {
                selections.Add(selection);
                continue;
            }
            var stored = SwShRestorationStateStore.Store(paths, previous with { Contributions = items.Values.ToArray() });
            using (stored.Lease) { }
            selections.Add(selection with { CustomizationHash = stored.Hash,
                AssetHashes = selection.AssetHashes.Concat(new[] { selection.PreparedHash, selection.CustomizationHash, stored.Hash }.OfType<string>()).Distinct(StringComparer.Ordinal).ToArray() });
            any |= stored.Hash != selection.CustomizationHash;
        }
        if (!any) return state;
        var next = state.State with { Selections = selections }; var saved = SwShRestorationStateStore.Store(paths, next);
        using (saved.Lease) { }
        return new(saved.Hash, next);
    }

    internal static SwShPreparedRestoration IncludeLooseModels(ProjectPaths paths, SwShPreparedRestoration prepared, IReadOnlySet<string> owned)
    {
        var definition = SwShRestorationDefinitions.Get(prepared.DefinitionId);
        var items = prepared.Contributions.ToDictionary(c => (c.Path, c.Key));
        long total = 0; var count = 0;
        foreach (var stem in ModelStems(definition))
        {
            var relative = $"romfs/bin/pokemon/{stem}";
            var directory = Path.Combine(paths.OutputRootPath!, relative);
            if (!Directory.Exists(directory)) continue;
            var pending = new Stack<string>(); pending.Push(directory);
            while (pending.TryPop(out var current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked model override directories are unsupported.");
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    if (++count > 4096) throw new InvalidDataException("Model overrides exceed their entry budget.");
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked model overrides are unsupported.");
                    if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                    var path = Path.GetRelativePath(paths.OutputRootPath!, entry).Replace('\\', '/');
                    if (!owned.Contains(path)) throw new InvalidDataException($"Restoration has an external model override: {path}.");
                    var info = new FileInfo(entry); total += info.Length;
                    if (info.Length > 32 * 1024 * 1024 || total > 128L * 1024 * 1024)
                        throw new InvalidDataException("Model override exceeds its restoration budget.");
                    items[(path, "")] = new(path, SwShRestorationWriter.File, "", items.GetValueOrDefault((path, ""))?.Before, File.ReadAllBytes(entry));
                }
            }
        }
        return prepared with { Contributions = items.Values.ToArray() };
    }
    internal static IEnumerable<string> ModelStems(SwShRestorationDefinition definition) => definition.Identities
        .Select(SwShRestorationModelResources.PrimaryStem)
        .Concat(definition.AdditionalModelVariants.Select(variant => variant.ResourceStem)).Distinct(StringComparer.Ordinal);
    private static bool IsLooseModel(string path, SwShRestorationDefinition definition) => ModelStems(definition).Any(stem =>
        path.StartsWith($"romfs/bin/pokemon/{stem}/", StringComparison.Ordinal));
}
