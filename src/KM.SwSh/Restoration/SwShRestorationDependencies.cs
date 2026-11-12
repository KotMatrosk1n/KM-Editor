// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Suspends default gameplay links while another restored identity is absent.</summary>
internal static class SwShRestorationDependencies
{
    internal static HashSet<SwShPokemonIdentity> Enabled(SwShRestorationState state) => state.Selections.Where(s => s.Enabled)
        .SelectMany(s => SwShRestorationDefinitions.Get(s.DefinitionId).Identities).ToHashSet();
    private static readonly HashSet<SwShPokemonIdentity> Restorable = SwShRestorationDefinitions.All.SelectMany(d => d.Identities).ToHashSet();

    internal static IReadOnlyDictionary<string, byte[]> Reconcile(ProjectPaths paths, SwShRestorationState before,
        SwShRestorationState after, Func<string, byte[]?> read)
    {
        var oldEnabled = Enabled(before); var enabled = Enabled(after);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var selection in after.Selections.Where(s => s.Enabled))
        {
            var prepared = SwShRestorationStateStore.ReadPrepared(paths, selection.CustomizationHash ?? selection.PreparedHash);
            foreach (var item in prepared.Contributions.Where(IsDependency))
            {
                var source = result.GetValueOrDefault(item.Path) ?? read(item.Path)
                    ?? throw new InvalidDataException("Restored gameplay dependency source is missing.");
                var identity = SwShRestorationDefinitions.Get(selection.DefinitionId).Identities.Single();
                var offset = item.Writer == SwShRestorationWriter.Personal ? SwShRestorationForms.PersonalRow(identity) * SwShPersonalTable.RecordSize : 0;
                var current = source.AsSpan(offset, item.After.Length).ToArray();
                var expected = oldEnabled.Contains(identity) ? Resolve(item, oldEnabled) : item.After;
                var desired = Resolve(item, enabled);
                foreach (var (at, size) in Fields(item))
                    if (current.AsSpan(at, size).SequenceEqual(expected.AsSpan(at, size))
                        || current.AsSpan(at, size).SequenceEqual(item.After.AsSpan(at, size)))
                        desired.AsSpan(at, size).CopyTo(current.AsSpan(at));
                if (current.AsSpan().SequenceEqual(source.AsSpan(offset, current.Length))) continue;
                var output = source.ToArray(); current.CopyTo(output, offset); result[item.Path] = output;
            }
        }
        return result;
    }

    internal static SwShRestorationContribution Retain(SwShRestorationContribution original,
        SwShRestorationContribution captured, SwShRestorationState state)
    {
        if (!IsDependency(original)) return SwShRestorationForms.NormalizeRetainedEgg(captured);
        if (captured.After.Length != original.After.Length) return captured;
        var expected = Resolve(original, Enabled(state)); var result = captured.After.ToArray();
        if (original.Writer == SwShRestorationWriter.Personal
            && int.TryParse(original.Key, out var row)
            && SwShRestorationForms.EnabledForms(state).Where(i => SwShRestorationSpecies.BaseRow(i.Species) == row).ToArray() is { Length: > 0 } forms)
        {
            var first = forms.Select(i => SwShRestorationForms.PersonalRow(i) - i.Form + 1).Distinct().Single();
            if (BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(30)) == first && result[32] == forms.Max(i => i.Form) + 1)
                original.After.AsSpan(30, 3).CopyTo(result.AsSpan(30));
        }
        foreach (var (at, size) in Fields(original))
            if (!expected.AsSpan(at, size).SequenceEqual(original.After.AsSpan(at, size))
                && result.AsSpan(at, size).SequenceEqual(expected.AsSpan(at, size)))
                original.After.AsSpan(at, size).CopyTo(result.AsSpan(at));
        return captured with { After = result };
    }

    internal static SwShRestorationContribution Effective(SwShRestorationContribution item, SwShRestorationState state) =>
        IsDependency(item) ? item with { After = Resolve(item, Enabled(state)) } : item;

    private static bool IsDependency(SwShRestorationContribution item) => item.Writer == SwShRestorationWriter.Personal
        || item.Writer == SwShRestorationWriter.File && item.Path.StartsWith("romfs/bin/pml/evolution/evo_", StringComparison.Ordinal)
            && SwShRestorationEvolutionPolicy.NativeParentTarget(item) is null;
    private static IEnumerable<(int At, int Size)> Fields(SwShRestorationContribution item)
    {
        if (item.Writer == SwShRestorationWriter.Personal) { yield return (22, 2); yield return (86, 2); }
        else for (var at = 0; at < item.After.Length; at += 8) yield return (at, 8);
    }
    private static byte[] Resolve(SwShRestorationContribution item, IReadOnlySet<SwShPokemonIdentity> enabled)
    {
        var bytes = item.After.ToArray();
        if (item.Writer == SwShRestorationWriter.Personal)
        {
            if (bytes.Length != SwShPersonalTable.RecordSize) throw new InvalidDataException("Restored personal dependency length is invalid.");
            var hatch = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(86));
            var localForm = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(88));
            var regional = (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(90)) & 1) != 0;
            var everstoneForm = regional ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(94)) : localForm;
            bool Missing(int form) => Restorable.Contains(new(hatch, form)) && !enabled.Contains(new(hatch, form));
            if (Missing(localForm) || Missing(everstoneForm))
            { bytes[22] = 15; bytes[23] = 15; bytes.AsSpan(86, 2).Clear(); }
        }
        else
        {
            if (bytes.Length % 8 != 0) throw new InvalidDataException("Restored evolution dependency length is invalid.");
            for (var at = 0; at < bytes.Length; at += 8)
            {
                var destination = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 4));
                var identity = new SwShPokemonIdentity(destination, bytes[at + 6]);
                if (Restorable.Contains(identity) && !enabled.Contains(identity)) bytes.AsSpan(at, 8).Clear();
            }
        }
        return bytes;
    }
}
