// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationAssetUpdate
{
    internal sealed record Result(SwShPreparedRestoration Customized, IReadOnlyDictionary<string, byte[]?> Files);

    // Even unedited gameplay defaults belong to the installation, not an asset update.
    internal static bool IsAsset(SwShRestorationContribution item) => item.Writer is
        SwShRestorationWriter.Icons or SwShRestorationWriter.ModelCatalog or SwShRestorationWriter.Camp or SwShRestorationWriter.Voices or SwShRestorationWriter.BattleCamera
        || item.Writer == SwShRestorationWriter.File && IsAssetPath(item.Path);

    internal static bool IsAssetPath(string path) => path.StartsWith("romfs/bin/archive/pokemon/", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/archive/gpokemon/", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/pokemon/", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/appli/icon_pokemon/", StringComparison.Ordinal)
        || path.StartsWith("romfs/bin/appli/icon_item/", StringComparison.Ordinal)
        || path is SwShRestorationComposer.CampPath or SwShRestorationComposer.VoicesPath or SwShRestorationComposer.BattleCameraPath;

    internal static IEnumerable<SwShRestorationContribution> Inputs(SwShPreparedRestoration package) =>
        package.Contributions.Where(item => IsAsset(item) || item.Writer == SwShRestorationWriter.Personal);

    internal static Result Create(ProjectPaths paths, SwShPreparedRestoration previous, SwShPreparedRestoration next,
        SwShPreparedRestoration customized, SwShRestorationState state, bool enabled,
        IReadOnlySet<string> owned, Func<string, byte[]?> read)
    {
        var captured = new Dictionary<(string Path, string Key), SwShRestorationContribution>();
        if (enabled)
        {
            customized = SwShRestorationCustomizations.IncludeLooseModels(paths, customized, owned);
            var assets = customized.Contributions.Where(IsAsset).ToArray();
            foreach (var path in assets.Concat(next.Contributions.Where(IsAsset)).Select(item => item.Path).Distinct(StringComparer.Ordinal))
                if (File.Exists(Path.Combine(paths.OutputRootPath!, path)) && !owned.Contains(path))
                    throw new InvalidDataException($"Asset update cannot replace externally changed output: {path}.");
            if (assets.Length > 0)
                captured = SwShRestorationComposer.Compose(assets, read, false, captureOnly: true)
                    .RetainedCustomizations.ToDictionary(item => (item.Path, item.Key));
            customized = customized with { Contributions = customized.Contributions.Select(item =>
                captured.TryGetValue((item.Path, item.Key), out var current)
                    ? current : item).ToArray() };
        }
        var merged = Rebase(previous, next, customized);
        if (!enabled)
        {
            var repairs = new Dictionary<string, byte[]?>();
            SwShRestorationIconMigration.Find(previous, next)?.RestoreNative(paths, read, repairs, false, owned);
            return new(merged, repairs);
        }
        var changes = merged.Contributions.Where(IsAsset).Select(item => item with {
            Before = captured.TryGetValue((item.Path, item.Key), out var current) ? current.After : item.Before })
            // Rebuilding an unchanged shared table can reorder unrelated entries.
            // Only compose assets whose effective payload actually changes.
            .Where(item => !Equal(item.Before, item.After)).ToArray();
        var files = changes.Length == 0 ? new Dictionary<string, byte[]?>()
            : SwShRestorationComposer.Compose(changes, read, true).Files.ToDictionary();
        var retainedPaths = merged.Contributions.Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var item in captured.Values.Where(item => SwShRestorationModelOverrides.IsLoose(item) && !retainedPaths.Contains(item.Path)))
            files[item.Path] = item.Before;
        SwShRestorationIconMigration.Find(previous, next)?.RestoreNative(paths, read, files, true, owned);
        foreach (var original in previous.Contributions.Where(item => item.Writer == SwShRestorationWriter.Personal))
        {
            var replacement = merged.Contributions.Single(item => item.Path == original.Path && item.Key == original.Key);
            if (Routing(original.After).SequenceEqual(Routing(replacement.After))) continue;
            var personal = files.GetValueOrDefault(original.Path) ?? read(original.Path)
                ?? throw new InvalidDataException("Personal data is missing for the updated icon route.");
            var result = personal.ToArray(); var row = int.Parse(original.Key, System.Globalization.CultureInfo.InvariantCulture);
            var at = checked(row * SwShPersonalTable.RecordSize + 0x4c);
            if (at + 4 > result.Length) throw new InvalidDataException("The updated icon route is outside personal data.");
            var current = result.AsSpan(at, 4);
            if (!current.SequenceEqual(Routing(original.After)) && !current.SequenceEqual(Routing(replacement.After)))
                throw new InvalidDataException("The updated icon route conflicts with a customized resource identity.");
            Routing(replacement.After).CopyTo(current);
            files[original.Path] = result;
        }
        foreach (var path in files.Keys.ToArray())
            if (Equal(files[path], read(path))) files.Remove(path);
        return new(merged, files);
    }

    internal static SwShPreparedRestoration Rebase(SwShPreparedRestoration previous, SwShPreparedRestoration next,
        SwShPreparedRestoration customized)
    {
        if (previous.DefinitionId != next.DefinitionId || previous.Game != next.Game
            || customized.DefinitionId != previous.DefinitionId || customized.Game != previous.Game
            || previous.DefinitionVersion != next.DefinitionVersion || previous.ConverterVersion != next.ConverterVersion
            || customized.DefinitionVersion != previous.DefinitionVersion || customized.ConverterVersion != previous.ConverterVersion)
            throw new InvalidDataException("Asset updates require matching Pokemon definitions and target games.");
        var originals = previous.Contributions.ToDictionary(item => (item.Path, item.Key));
        var replacements = next.Contributions.ToDictionary(item => (item.Path, item.Key));
        var customizations = customized.Contributions.ToDictionary(item => (item.Path, item.Key));
        var loose = SwShRestorationModelOverrides.Rebase(previous, next, customized);
        var shadowed = customized.Contributions.Where(SwShRestorationModelOverrides.IsLoose).Select(item => item.Path).ToArray();
        var migration = SwShRestorationIconMigration.Find(previous, next);
        if (previous.Contributions.Any(item => IsAsset(item) && !SwShRestorationModelOverrides.IsLoose(item) && !replacements.ContainsKey((item.Path, item.Key))
            && migration?.Retires(item) != true))
            throw new InvalidDataException("This asset update removes an unsupported owned resource. The existing installation is preserved.");
        var merged = new List<SwShRestorationContribution>();
        foreach (var item in next.Contributions.Where(item => IsAsset(item) && !SwShRestorationModelOverrides.IsLoose(item)))
        {
            if (!originals.TryGetValue((item.Path, item.Key), out var original))
            {
                if (item.Before is not null || customizations.ContainsKey((item.Path, item.Key)))
                    throw new InvalidDataException("This asset update conflicts with existing resource ownership.");
                merged.Add(migration?.Transfer(item, customizations) ?? item);
                continue;
            }
            if (item.Writer != original.Writer || !Equal(item.Before, original.Before))
                throw new InvalidDataException("This asset update changes the original resource ownership.");
            var current = customizations.GetValueOrDefault((item.Path, item.Key), original);
            if (current.Writer != original.Writer || !Equal(current.Before, original.Before))
                throw new InvalidDataException("Retained customization ownership does not match the installed asset.");
            merged.Add(item with { After = Merge(original, item, current, shadowed) });
        }
        foreach (var original in previous.Contributions.Where(item => !IsAsset(item)))
        {
            var current = customizations.GetValueOrDefault((original.Path, original.Key), original);
            if (original.Writer == SwShRestorationWriter.Personal
                && replacements.TryGetValue((original.Path, original.Key), out var replacement))
            {
                if (replacement.Writer != original.Writer)
                    throw new InvalidDataException("This asset update changes the personal record ownership.");
                var after = current.After.ToArray();
                if (!Routing(original.After).SequenceEqual(Routing(replacement.After)))
                {
                    if (!Routing(current.After).SequenceEqual(Routing(original.After))
                        && !Routing(current.After).SequenceEqual(Routing(replacement.After)))
                        throw new InvalidDataException("The updated icon route conflicts with a customized resource identity.");
                    Routing(replacement.After).CopyTo(Routing(after));
                }
                current = current with { After = after };
            }
            merged.Add(current);
        }
        merged.AddRange(loose);
        merged.AddRange(customized.Contributions.Where(item => !SwShRestorationModelOverrides.IsLoose(item) && !originals.ContainsKey((item.Path, item.Key))
            && !replacements.ContainsKey((item.Path, item.Key))));
        return next with { Contributions = merged, UnavailableSourceMoves = customized.UnavailableSourceMoves,
            Sources = next.Sources.Where(source => source.Role != "target" || IsAssetPath("romfs/" + source.RelativePath))
                .Concat(previous.Sources.Where(source => source.Role == "target" && !IsAssetPath("romfs/" + source.RelativePath))).ToArray() };
    }

    private static byte[] Merge(SwShRestorationContribution original, SwShRestorationContribution next,
        SwShRestorationContribution current, IReadOnlyList<string> shadowed)
    {
        if (Equal(current.After, original.After) || Equal(current.After, next.After)) return next.After;
        if (Equal(next.After, original.After)) return current.After;
        if (original.Path.StartsWith("romfs/bin/appli/icon_pokemon/", StringComparison.Ordinal)) return current.After;
        if (original.Writer == SwShRestorationWriter.File && original.Path.EndsWith(".gfpak", StringComparison.Ordinal))
            return MergeArchive(original.After, next.After, current.After,
                shadowed.Where(path => SwShRestorationModelOverrides.Overrides(path, original.Path)).ToArray());
        throw new InvalidDataException($"This asset update conflicts with a customized resource: {original.Path} ({original.Key}). Existing edits and Pokemon assignments are preserved.");
    }

    private static Span<byte> Routing(byte[] record)
    {
        if (record.Length != SwShPersonalTable.RecordSize)
            throw new InvalidDataException("Pokemon icon routing has an invalid record size.");
        return record.AsSpan(0x4c, 4);
    }

    private static byte[] MergeArchive(byte[] previous, byte[] next, byte[] current, IReadOnlyList<string> shadowed)
    {
        var baseline = SwShGfPackFile.ParseBoundedReadOnly(previous, 4096, 64L * 1024 * 1024);
        var updated = SwShGfPackFile.ParseBoundedReadOnly(next, 4096, 64L * 1024 * 1024);
        var customized = SwShGfPackFile.ParseBoundedReadOnly(current, 4096, 64L * 1024 * 1024);
        if (baseline.MergeMetadataIdentity != customized.MergeMetadataIdentity
            || baseline.Version != updated.Version || baseline.IsRelocated != updated.IsRelocated)
            throw new InvalidDataException("This asset update changes the structure of a customized model. Reset its model edits before updating; Pokemon assignments are preserved.");
        var updatedMembers = updated.AbsoluteHashes.Select(item => item.HashFnv1aPathFull).ToHashSet();
        // Loose resources take precedence over these packed bytes. Their actual
        // effective edits have already been rebased, so stale hidden copies must
        // not create false conflicts with the new archive.
        var hidden = shadowed.Select(path => baseline.TryGetUniqueFileHash(Path.GetFileName(path), out var hash) ? (ulong?)hash : null)
            .OfType<ulong>().ToHashSet();
        foreach (var member in baseline.AbsoluteHashes)
        {
            if (hidden.Contains(member.HashFnv1aPathFull)) continue;
            var original = baseline.ReadFileByHash(member.HashFnv1aPathFull, 32 * 1024 * 1024);
            var changed = customized.ReadFileByHash(member.HashFnv1aPathFull, 32 * 1024 * 1024);
            if (Equal(original, changed)) continue;
            if (!updatedMembers.Contains(member.HashFnv1aPathFull))
                throw new InvalidDataException("This asset update removes an edited model member. The existing model is preserved.");
            var replacement = updated.ReadFileByHash(member.HashFnv1aPathFull, 32 * 1024 * 1024);
            if (!Equal(original, replacement) && !Equal(changed, replacement))
                throw new InvalidDataException("This asset update conflicts with an edited model member. Reset that model edit before updating; Pokemon assignments are preserved.");
            updated.ReplaceFileByHash(member.HashFnv1aPathFull, changed);
        }
        return updated.Write();
    }

    private static bool Equal(byte[]? left, byte[]? right) => left is null ? right is null
        : right is not null && left.AsSpan().SequenceEqual(right);
}
