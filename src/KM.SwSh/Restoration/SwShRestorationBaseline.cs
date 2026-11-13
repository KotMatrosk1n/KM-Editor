// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;

namespace KM.SwSh.Restoration;

/// <summary>Applies pinned converted defaults without including later project customizations.</summary>
public static class SwShRestorationBaseline
{
    public static byte[] Personal(ProjectPaths paths, byte[] native, IReadOnlySet<int>? personalIds = null)
    {
        using var scope = SwShRestorationReadScope.Begin(paths);
        var state = SwShRestorationStateStore.Read(paths).State;
        var forms = SwShRestorationForms.EnabledForms(state).GroupBy(i => i.Species).Where(group =>
        {
            if (personalIds is null || personalIds.Contains(SwShRestorationSpecies.BaseRow(group.Key))
                || group.Any(i => personalIds.Contains(SwShRestorationForms.PersonalRow(i)))) return true;
            var block = SwShRestorationFormSlots.Blocks.Single(b => b.Species == group.Key);
            return Enumerable.Range(1, block.NativeAlternateCount).Any(form => personalIds.Contains(block.PersonalRow(form)));
        }).SelectMany(group => group).ToArray();
        var formSpecies = forms.Select(i => i.Species).ToHashSet();
        var contributions = Contributions(paths, new HashSet<string>(StringComparer.Ordinal)
            { SwShRestorationComposer.PersonalPath }, definition => personalIds is null
                || definition.Identities.Any(identity => personalIds.Contains(SwShRestorationForms.PersonalRow(identity))
                    || identity.Form != 0 && formSpecies.Contains(identity.Species)), state).Where(c => c.Writer == SwShRestorationWriter.Personal).ToArray();
        var personal = contributions.Length == 0 ? native : SwShRestorationComposer.Compose(contributions,
            path => path == SwShRestorationComposer.PersonalPath ? native : null, enable: true).Files[SwShRestorationComposer.PersonalPath]!;
        return forms.Length == 0 ? personal : SwShRestorationForms.BaselinePersonal(native, personal, forms);
    }

    public static IReadOnlyDictionary<string, byte[]> Models(ProjectPaths paths, string? modelId = null)
    {
        bool Selected(SwShRestorationDefinition definition) => modelId is null
            || SwShRestorationModelResources.Variants(definition)
                .Concat(SwShRestorationModelResources.Variants(definition, legacy: true))
                .Any(variant => modelId.StartsWith($"bin/pokemon/{variant.ResourceStem}/", StringComparison.Ordinal));
        var contributions = Contributions(paths, select: Selected).Where(c => c.Writer == SwShRestorationWriter.ModelCatalog
            || c.Writer == SwShRestorationWriter.File && c.Path.StartsWith("romfs/bin/archive/pokemon/", StringComparison.Ordinal)).ToArray();
        if (contributions.Length == 0) return new Dictionary<string, byte[]>();
        var files = SwShRestorationComposer.Compose(contributions, path =>
        {
            if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath)) throw new InvalidDataException("Restored defaults require the target base catalog.");
            var source = Path.Combine(paths.BaseRomFsPath, path[6..].Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(source);
            if (!info.Exists) return null;
            if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("Restoration baseline source exceeds its size limit.");
            return File.ReadAllBytes(source);
        }, enable: true).Files;
        return files.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value!, StringComparer.Ordinal);
    }

    private static IEnumerable<SwShRestorationContribution> Contributions(ProjectPaths paths,
        IReadOnlySet<string>? contributionPaths = null, Func<SwShRestorationDefinition, bool>? select = null,
        SwShRestorationState? installedState = null)
    {
        var state = installedState ?? SwShRestorationStateStore.Read(paths).State;
        var selections = state.Selections.Where(s => s.Enabled
            && (select is null || select(SwShRestorationDefinitions.Get(s.DefinitionId)))).ToArray();
        if (SwShRestorationReadScope.Find(paths) is { } scope)
        {
            if (contributionPaths is null) scope.Prefetch(selections.Select(s => s.PreparedHash));
            else scope.PrefetchContributions(selections.Select(s => s.PreparedHash), contributionPaths.IsSubsetOf(SwShRestorationStateStore.DataPaths)
                ? SwShRestorationStateStore.DataPaths : contributionPaths);
        }
        foreach (var selection in selections)
        {
            var prepared = contributionPaths is null
                ? SwShRestorationStateStore.ReadPrepared(paths, selection.PreparedHash)
                : SwShRestorationStateStore.ReadPreparedContributions(paths, selection.PreparedHash, contributionPaths);
            if (prepared.DefinitionId != selection.DefinitionId) throw new InvalidDataException("Installed restoration defaults do not match their selection.");
            foreach (var contribution in prepared.Contributions) yield return SwShRestorationDependencies.Effective(contribution, state);
        }
    }
}
