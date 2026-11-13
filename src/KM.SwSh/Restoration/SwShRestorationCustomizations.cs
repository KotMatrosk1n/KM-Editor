// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Pokemon;
using KM.Formats.SwSh;
using System.Security.Cryptography;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationCustomizations
{
    internal static ChangePlan AddAnchor(ProjectPaths paths, ChangePlan plan, bool assignmentSourcesBound = false)
    {
        if (plan.Writes.Count == 0 || plan.EffectivePendingEdits?.Any(e => e.Domain == SwShRestorationDefinitions.Domain) == true) return plan;
        if (plan.Writes.All(write => write.TargetRelativePath.StartsWith("romfs/bin/trainer/", StringComparison.Ordinal))) return plan;
        // Only restoration-owned outputs can change a retained customization.
        // Assignment dependencies belong to the assigning editor's plan, rather
        // than every installed Pokemon being a dependency of every editor.
        if (!plan.Writes.Any(write => CanContainContribution(write.TargetRelativePath)))
            return !assignmentSourcesBound && plan.Writes.Any(write => write.Sources.Any(source => source.RelativePath == SwShRestorationComposer.PersonalPath))
                ? BindCatalogSources(paths, plan) : plan;
        var state = SwShRestorationStateStore.Read(paths);
        ChangePlan Complete(ChangePlan result) => assignmentSourcesBound ? result : BindCatalogSources(paths, result);
        if (!state.State.Selections.Any(s => s.Enabled)) return Complete(plan);
        // The registered ownership families determine whether an anchor can be
        // needed. No model payload is needed to make this conservative decision.
        if (plan.Writes.Any(write => write.TargetRelativePath == SwShRestorationComposer.PersonalPath)) return Complete(plan);
        var personal = SwShRestorationComposer.PersonalPath;
        var sources = new List<ProjectFileReference> { new(ProjectFileLayer.Base, personal) };
        if (File.Exists(Path.Combine(paths.OutputRootPath!, personal))) sources.Add(new(ProjectFileLayer.Layered, personal));
        plan = plan with { Writes = plan.Writes.Append(new(personal, sources, sources.Count == 2,
            "Retain restoration settings and Pokemon customizations.")).ToArray() };
        return Complete(plan);
    }

    internal static bool CanContainContribution(string path) => path is
        SwShRestorationComposer.PersonalPath or SwShRestorationComposer.LearnsetPath
        or SwShRestorationComposer.IconsPath or SwShRestorationComposer.CatalogPath
        or SwShRestorationComposer.CampPath or SwShRestorationComposer.VoicesPath
        || path == KM.Formats.SwSh.SwShItemTable.ItemDataRelativePath
        || SwShRestorationMegaItems.IsTextPath(path)
        || SwShRestorationAbilities.IsTextPath(path)
        || SwShRestorationSpeciesText.IsPath(path)
        || path.StartsWith("romfs/bin/archive/pokemon/", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/archive/gpokemon/", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/appli/icon_pokemon/poke_icon_", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/pml/evolution/evo_", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/pml/tamagowaza/tamagowaza_", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/pokemon/pm", StringComparison.Ordinal)
        || SwShRestorationMegaItems.All.Any(item => path == SwShRestorationMegaItems.IconPath(item.ItemId));

    private static ChangePlan BindCatalogSources(ProjectPaths paths, ChangePlan plan)
    {
        // Installed dependencies and customization anchors can introduce personal
        // data. Bind its catalog now so repeated captures retain the same sources.
        // Assignment writers bind their selected resource sources separately.
        if (plan.Writes.Any(w => w.Sources.Any(s => s.RelativePath == SwShRestorationComposer.PersonalPath)))
        {
            var catalog = SwShEffectivePokemonCatalog.LoadForBrowsing(paths);
            if (catalog.Available)
                plan = plan with { Writes = plan.Writes.Select((write, index) => index == 0
                    ? write with { Sources = write.Sources.Concat(catalog.Sources).Distinct().ToArray() } : write).ToArray() };
        }
        return plan;
    }

    internal static SwShRestorationStateSnapshot Capture(ProjectPaths paths, SwShRestorationStateSnapshot state,
        IReadOnlySet<string> changed, Func<string, byte[]?> readOutput, Func<string, byte[]?>? readOriginal = null)
    {
        var output = changed.ToDictionary(path => path, readOutput, StringComparer.Ordinal);
        var original = changed.ToDictionary(path => path, path => readOriginal?.Invoke(path), StringComparer.Ordinal);
        // In particular, an unchanged personal-data anchor does not make every
        // installed family a changed Pokemon.
        changed = changed.Where(path => readOriginal is null || !SameBytes(original[path], output[path])).ToHashSet(StringComparer.Ordinal);
        if (changed.Count == 0) return state;
        readOutput = path => output.GetValueOrDefault(path);
        var changedMegaItems = ChangedMegaItems(changed, original, output);
        var affected = state.State.Selections.Where(selection => selection.Enabled && changed.Any(path =>
            MayAffect(SwShRestorationDefinitions.Get(selection.DefinitionId), path, original.GetValueOrDefault(path), output.GetValueOrDefault(path), changedMegaItems))).ToArray();
        SwShRestorationReadScope.Find(paths)?.PrefetchContributions(affected.Select(s => s.CustomizationHash ?? s.PreparedHash),
            changed.IsSubsetOf(SwShRestorationStateStore.DataPaths) ? SwShRestorationStateStore.DataPaths : changed);
        if (changed.IsSubsetOf(SwShRestorationStateStore.DataPaths) && SwShRestorationReadScope.Find(paths) is { } dataScope)
            // The data index already contains the effective rows. Only the small
            // overlay descriptors are needed to keep repeat edits flat; acquire
            // them together instead of rescanning the store for every Pokemon.
            dataScope.Prefetch(affected.Where(s => s.CustomizationHash is { } hash && !dataScope.IsPreparedBundle(hash))
                .Select(s => s.CustomizationHash!));
        var pendingCustomizations = new List<byte[]>();
        var selections = new List<SwShRestorationSelection>(); var any = false;
        foreach (var selection in state.State.Selections)
        {
            if (!selection.Enabled) { selections.Add(selection); continue; }
            var definition = SwShRestorationDefinitions.Get(selection.DefinitionId);
            if (!changed.Any(path => MayAffect(definition, path, original.GetValueOrDefault(path), output.GetValueOrDefault(path), changedMegaItems)))
            { selections.Add(selection); continue; }
            var looseChanges = changed.Where(p => IsLooseModel(p, definition)).ToArray();
            var previous = SwShRestorationStateStore.ReadPreparedContributions(paths, selection.CustomizationHash ?? selection.PreparedHash, changed);
            var items = previous.Contributions.ToDictionary(c => (c.Path, c.Key));
            var changedItems = items.Values.Where(c => changed.Contains(c.Path)).ToArray();
            // The verified snapshot is immutable. Probe each changed file once,
            // rather than reading a shared bank again for every semantic entry.
            var availablePaths = changedItems.Select(c => c.Path).Distinct(StringComparer.Ordinal)
                .Where(path => readOutput(path) is not null).ToHashSet(StringComparer.Ordinal);
            var touched = changedItems.Where(c => availablePaths.Contains(c.Path)).ToArray();
            if (touched.Length > 0)
                foreach (var item in SwShRestorationComposer.Compose(touched, readOutput, false, captureOnly: true).RetainedCustomizations)
                    items[(item.Path, item.Key)] = SwShRestorationDependencies.Retain(items[(item.Path, item.Key)], item, state.State);
            foreach (var path in looseChanges)
            {
                if (readOutput(path) is { } bytes) items[(path, "")] = new(path, SwShRestorationWriter.File, "", items.GetValueOrDefault((path, ""))?.Before, bytes);
                else items.Remove((path, ""));
            }
            if (touched.Length == 0 && looseChanges.Length == 0) { selections.Add(selection); continue; }
            if (items.Count == previous.Contributions.Count && previous.Contributions.All(before =>
                items.TryGetValue((before.Path, before.Key), out var after) && before.Writer == after.Writer
                && ((before.Before is null && after.Before is null) || (before.Before is not null && after.Before is not null
                    && before.Before.AsSpan().SequenceEqual(after.Before)))
                && before.After.AsSpan().SequenceEqual(after.After)))
            {
                selections.Add(selection);
                continue;
            }
            string hash;
            if (changed.All(path => path is SwShRestorationComposer.PersonalPath or SwShRestorationComposer.LearnsetPath))
            {
                var bytes = SwShRestorationStateStore.CreateDataCustomization(paths, selection.CustomizationHash ?? selection.PreparedHash,
                    selection.CustomizationHash is not null, previous with { Contributions = items.Values.ToArray() });
                hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                pendingCustomizations.Add(bytes);
            }
            else
            {
                var complete = SwShRestorationStateStore.ReadPrepared(paths, selection.CustomizationHash ?? selection.PreparedHash);
                var merged = complete.Contributions.Where(item => !changed.Contains(item.Path)).Concat(items.Values).ToArray();
                var stored = SwShRestorationStateStore.Store(paths, complete with { Contributions = merged });
                using (stored.Lease) { }
                hash = stored.Hash;
            }
            selections.Add(selection with { CustomizationHash = hash,
                AssetHashes = selection.AssetHashes.Concat(new[] { selection.PreparedHash, selection.CustomizationHash, hash }.OfType<string>()).Distinct(StringComparer.Ordinal).ToArray() });
            any |= hash != selection.CustomizationHash;
        }
        if (!any) return state;
        SwShRestorationStateStore.StoreCustomizations(paths, pendingCustomizations);
        var next = state.State with { Selections = selections }; var saved = SwShRestorationStateStore.Store(paths, next);
        using (saved.Lease) { }
        return SwShRestorationStateStore.ReadState(paths, saved.Hash);
    }

    private static bool SameBytes(byte[]? before, byte[]? after) => before is null ? after is null
        : after is not null && before.AsSpan().SequenceEqual(after);

    private static IReadOnlySet<int>? ChangedMegaItems(IReadOnlySet<string> changed,
        IReadOnlyDictionary<string, byte[]?> original, IReadOnlyDictionary<string, byte[]?> output)
    {
        var path = SwShItemTable.ItemDataRelativePath;
        if (!changed.Contains(path)) return new HashSet<int>();
        if (original.GetValueOrDefault(path) is not { } before || output.GetValueOrDefault(path) is not { } after) return null;
        var previous = SwShItemTable.Parse(before, 4096);
        var current = SwShItemTable.Parse(after, 4096);
        // Compare logical item rows so shared-row aliases remain dependencies.
        return SwShRestorationMegaItems.All.Select(item => item.ItemId).Where(id =>
            id >= previous.Records.Count || id >= current.Records.Count
            || !previous.ReadRow(id).AsSpan().SequenceEqual(current.ReadRow(id))).ToHashSet();
    }

    private static bool MayAffect(SwShRestorationDefinition definition, string path, byte[]? before, byte[]? after,
        IReadOnlySet<int>? changedMegaItems)
    {
        if (path is SwShRestorationComposer.PersonalPath or SwShRestorationComposer.LearnsetPath)
        {
            if (before is null || after is null) return true;
            var size = path == SwShRestorationComposer.PersonalPath ? SwShPersonalTable.RecordSize : 260;
            return definition.Identities.Select(SwShRestorationForms.PersonalRow).Any(row =>
                row > before.Length / size - 1 || row > after.Length / size - 1
                || !before.AsSpan(row * size, size).SequenceEqual(after.AsSpan(row * size, size)));
        }
        if (path == SwShItemTable.ItemDataRelativePath)
            return definition.Identities.Any(identity => SwShRestorationMegaItems.For(identity) is { } item
                && (changedMegaItems is null || changedMegaItems.Contains(item.ItemId)));
        if (SwShRestorationMegaItems.IsTextPath(path)
            || SwShRestorationMegaItems.All.Any(item => path == SwShRestorationMegaItems.IconPath(item.ItemId)))
            return definition.Identities.Any(identity => SwShRestorationMegaItems.For(identity) is not null);
        if (path.StartsWith("romfs/bin/archive/pokemon/", StringComparison.Ordinal)
            || path.StartsWith("romfs/bin/archive/gpokemon/", StringComparison.Ordinal)
            || path.StartsWith("romfs/bin/pokemon/", StringComparison.Ordinal))
            return IsLooseModel(path, definition) || SwShRestorationModelResources.Variants(definition)
                .Concat(SwShRestorationModelResources.Variants(definition, legacy: true)).Any(variant =>
                    path == $"romfs/bin/archive/pokemon/{variant.ResourceStem}.gfpak"
                    || path == $"romfs/bin/archive/pokemon/{variant.ResourceStem}_rare.gfpak"
                    || path == $"romfs/bin/archive/gpokemon/{variant.ResourceStem}.gfpak");
        return true;
    }

    internal static SwShPreparedRestoration IncludeLooseModels(ProjectPaths paths, SwShPreparedRestoration prepared, IReadOnlySet<string> owned)
    {
        var items = prepared.Contributions.ToDictionary(c => (c.Path, c.Key));
        long total = 0; var count = 0;
        foreach (var stem in ModelStems(prepared))
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
    internal static IEnumerable<string> ModelStems(SwShPreparedRestoration prepared) => SwShRestorationModelResources.Variants(prepared)
        .Select(variant => variant.ResourceStem).Distinct(StringComparer.Ordinal);
    private static bool IsLooseModel(string path, SwShRestorationDefinition definition) =>
        SwShRestorationModelResources.Variants(definition).Concat(SwShRestorationModelResources.Variants(definition, legacy: true))
            .Any(variant => path.StartsWith($"romfs/bin/pokemon/{variant.ResourceStem}/", StringComparison.Ordinal));
}
