// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Core.Files;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using System.Globalization;
using System.Text.Json;

namespace KM.SwSh.Restoration;

public sealed partial class SwShRestorationService
{
    private static IReadOnlyList<SwShRestorationContribution> RetainSharedAbilityText(
        IReadOnlyList<SwShRestorationContribution> contributions,
        IReadOnlySet<(string Path, SwShRestorationWriter Writer, string Key)> shared,
        IReadOnlySet<string> owned, Func<string, byte[]?> readEffective) => contributions.Select(item =>
    {
        if (item.Writer != SwShRestorationWriter.AbilityText || !owned.Contains(item.Path)
            || !shared.Contains((item.Path, item.Writer, item.Key))) return item;
        var bytes = readEffective(item.Path);
        if (bytes is null) return item;
        var table = SwShGameTextFile.Parse(bytes, 512);
        if (!int.TryParse(item.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id < SwShRestorationAbilities.NativeTextCount || id >= table.Lines.Count
            || string.IsNullOrWhiteSpace(table.Lines[id].Text)) return item;
        var text = JsonSerializer.Deserialize<SwShRestoredAbilityText>(item.After)
            ?? throw new InvalidDataException("Restored ability text is missing.");
        // An existing owner may use localized donor text or a user edit while
        // another donor only supplies English. Keep the shared effective line.
        return item with { After = JsonSerializer.SerializeToUtf8Bytes(text with { Line = table.Lines[id] }) };
    }).ToArray();

    private static void AddAbilitySupport(ProjectPaths paths, SwShRestorationState state,
        IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources, IReadOnlySet<string> owned)
    {
        var wanted = false;
        var requiredAbilities = new HashSet<int>();
        var enabled = SwShRestorationDependencies.Enabled(state).ToArray();
        if (enabled.Length != 0)
        {
            var personal = outputs.TryGetValue(SwShRestorationComposer.PersonalPath, out var pending) && pending is not null
                ? pending : SwShEffectivePokemonCatalog.Read(paths, SwShRestorationComposer.PersonalPath, sources: sources);
            foreach (var identity in enabled)
            {
                var row = SwShRestorationForms.PersonalRow(identity) * SwShPersonalTable.RecordSize;
                if (row < 0 || row > personal.Length - SwShPersonalTable.RecordSize)
                    throw new InvalidDataException("The restored ability's personal row is missing.");
                for (var i = 0; i < 3; ++i)
                {
                    var ability = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(personal.AsSpan(row + 24 + i * 2));
                    if (ability <= 267) continue;
                    if (!SwShRestorationAbilities.HasHandler(ability) || !SwShRestorationAbilities.IsAbilityFor(identity, ability))
                        throw new InvalidDataException("This restored source ability requires another native handler.");
                    wanted = true;
                    requiredAbilities.Add(ability);
                }
            }
        }
        const string relative = SwShRestorationAbilityRuntime.ModulePath;
        var physical = Output(paths, relative, includeExecutable: true);
        var current = File.Exists(physical) ? BoundedRead(physical) : null;
        if (current is not null) sources.Add(new(ProjectFileLayer.Layered, relative));
        if (!wanted && (current is null || !owned.Contains(relative) || !SwShRestorationAbilityRuntime.IsModule(current))) return;
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath)) throw new InvalidDataException("Select the game's Base ExeFS folder to support restored abilities.");
        // This module is generated output; the Base must have no occupant.
        // Check the original configured Base even while applying from verified
        // source snapshots, where an unreferenced foreign file is not copied.
        // The change-plan guard separately fingerprints the module's output target.
        var originalBase = KM.SwSh.Editing.SwShChangePlanSourceGuard.OriginalPaths(paths).BaseExeFsPath!;
        var baseModule = Path.Combine(originalBase, "subsdk8");
        if (File.Exists(baseModule) || Directory.Exists(baseModule))
            throw new InvalidDataException("The Base already uses subsdk8, which is required by restoration abilities.");
        if (current is not null && (!owned.Contains(relative) || !SwShRestorationAbilityRuntime.IsModule(current)))
            throw new InvalidDataException("Restoration abilities cannot replace an externally supplied subsdk8.");
        if (!wanted) { outputs[relative] = null; return; }
        foreach (var language in SwShRestorationSpeciesText.Languages)
        foreach (var description in new[] { false, true })
        {
            var path = SwShRestorationAbilities.TextPath(language, description);
            var text = outputs.TryGetValue(path, out var pendingText) ? pendingText : ReadEffective(paths, path, sources);
            if (text is null) throw new InvalidDataException("Restored ability text is missing.");
            var lines = SwShGameTextFile.Parse(text, 512).Lines;
            if (requiredAbilities.Any(id => id >= lines.Count || string.IsNullOrWhiteSpace(lines[id].Text)))
                throw new InvalidDataException("Restored ability names and descriptions are incomplete.");
        }
        const string mainPath = "exefs/main";
        var baseline = BoundedRead(Path.Combine(paths.BaseExeFsPath, "main"));
        sources.Add(new(ProjectFileLayer.Base, mainPath));
        var effective = baseline;
        if (outputs.TryGetValue(mainPath, out var mainPending) && mainPending is not null) effective = mainPending;
        else if (File.Exists(Output(paths, mainPath, includeExecutable: true)))
        {
            effective = BoundedRead(Output(paths, mainPath, includeExecutable: true));
            sources.Add(new(ProjectFileLayer.Layered, mainPath));
        }
        var module = SwShRestorationAbilityRuntime.CreateModule(baseline, effective, paths.SelectedGame!.Value);
        if (current is null || !current.AsSpan().SequenceEqual(module)) outputs[relative] = module;
    }
}
