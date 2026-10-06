// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Encounters;
using KM.SwSh.Gifts;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.StaticEncounters;

namespace KM.SwSh.Randomizer;

public sealed partial class SwShRandomizerService
{
    private const int MaximumRandomizerManifestBytes = 128 * 1024 * 1024;

    private static byte[] ReadRandomizerManifestBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumRandomizerManifestBytes) throw new InvalidDataException("Randomizer restore metadata exceeds its supported size.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Randomizer restore metadata changed while reading.");
        return bytes;
    }

    // Persist semantic values, never a byte diff of a compressed archive or AMX script.
    // Coupled identities and variable-length lists are indivisible undo units.
    private sealed record RandomizerValueOwnership(
        string Domain, string Key, string Path, string[] Before, string[] After);

    private sealed record RandomizerCurrentValue(
        string Domain, string Key, string Path, string[] Values,
        Func<string[], IEnumerable<PendingEdit>> MakeEdits);

    private static string ValueKey(string domain, string key) => domain + "\n" + key;
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private Dictionary<string, RandomizerCurrentValue> ReadRandomizerValues(
        ProjectPaths paths, IEnumerable<string> domains, ICollection<ValidationDiagnostic> diagnostics)
    {
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        IReadOnlyList<ProjectFileReference> IdentitySources(ProjectFileReference primary)
        {
            var personal = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
            return personal is null ? [primary] : [primary, new(
                personal.GraphEntry.LayeredFile is null ? ProjectFileLayer.Base : ProjectFileLayer.Layered,
                personal.GraphEntry.RelativePath)];
        }
        var values = new Dictionary<string, RandomizerCurrentValue>(StringComparer.Ordinal);
        foreach (var domain in domains.Distinct(StringComparer.Ordinal))
        {
            void Add(string key, string path, string[] current, Func<string[], IEnumerable<PendingEdit>> makeEdits) =>
                values.Add(ValueKey(domain, key), new(domain, key, path, current, makeEdits));
            void Fields(string key, string editDomain, string recordId, ProjectFileReference source,
                string[] fields, string[] current, IReadOnlyList<ProjectFileReference>? sources = null, string? path = null) =>
                Add(key, path ?? source.RelativePath, current, restored => fields.Select((field, index) =>
                    new PendingEdit(editDomain, "Restore Randomizer value", sources ?? [source], recordId, field, restored[index])));

            switch (domain)
            {
                case "Pokemon":
                {
                    var workflow = pokemonWorkflowService.Load(project);
                    AddWorkflowErrors(workflow.Diagnostics, diagnostics);
                    foreach (var record in workflow.Pokemon)
                    {
                        var id = Number(record.PersonalId);
                        var source = Source(record.Provenance);
                        foreach (var field in workflow.EditableFields)
                        {
                            var current = SwShPokemonEditSessionService.TryGetCurrentEditableValue(record, field.Field);
                            if (current is not null)
                                Fields(id + "/" + field.Field, PokemonEditDomain, id, source, [field.Field], [Number(current.Value)]);
                        }
                        foreach (var group in record.Compatibility)
                        foreach (var entry in group.Entries)
                        {
                            var field = SwShPokemonWorkflowService.CreateCompatibilityFieldId(group.GroupId, entry.Slot);
                            Fields(id + "/" + field, PokemonEditDomain, id, source, [field], [entry.CanLearn ? "1" : "0"]);
                        }
                        foreach (var row in record.Evolutions)
                        {
                            var field = $"evolution:upsert:{row.Slot}";
                            Fields(id + "/" + field, PokemonEditDomain, id, source, [field],
                                [$"{row.Method}:{row.Argument}:{row.Species}:{row.Form}:{row.Level}"],
                                path: SwShPokemonWorkflowService.CreateEvolutionDataPath(record.PersonalId));
                        }
                        var moves = record.Learnset.OrderBy(row => row.Slot).ToArray();
                        Add(id + "/learnset", SwShPokemonWorkflowService.LearnsetDataPath,
                            moves.Select(row => $"{row.MoveId}:{row.Level}").ToArray(), restored =>
                            restored.Select((value, slot) => new PendingEdit(PokemonEditDomain,
                                "Restore Randomizer learnset", [source], id, $"learnset:upsert:{slot}", value))
                            .Concat(Enumerable.Range(restored.Length, Math.Max(0, moves.Length - restored.Length)).Reverse()
                                .Select(slot => new PendingEdit(PokemonEditDomain,
                                    "Remove Randomizer learnset addition", [source], id, $"learnset:remove:{slot}", ""))));
                    }
                    break;
                }
                case "Wild Encounters":
                {
                    var workflow = encountersWorkflowService.Load(project);
                    AddWorkflowErrors(workflow.Diagnostics, diagnostics);
                    foreach (var table in workflow.Tables)
                    {
                        var source = Source(table.Provenance);
                        var slots = table.Slots.OrderBy(slot => slot.Slot).ToArray();
                        foreach (var slot in slots)
                        {
                            var id = SwShEncountersWorkflowService.CreateSlotRecordId(table.TableId, slot.Slot);
                            Fields(id + "/identity", EncountersEditDomain, id, source,
                                [SwShEncountersWorkflowService.SpeciesIdField, SwShEncountersWorkflowService.FormField],
                                [Number(slot.SpeciesId), Number(slot.Form)]);
                        }
                        Add(table.TableId + "/weights", source.RelativePath, slots.Select(slot => Number(slot.Weight)).ToArray(),
                            restored => slots.Select((slot, index) => new PendingEdit(EncountersEditDomain,
                                "Restore Randomizer encounter weights", [source],
                                SwShEncountersWorkflowService.CreateSlotRecordId(table.TableId, slot.Slot),
                                SwShEncountersWorkflowService.ProbabilityField, restored[index])));
                    }
                    break;
                }
                case "Static Encounters":
                {
                    var workflow = staticEncountersWorkflowService.Load(project);
                    AddWorkflowErrors(workflow.Diagnostics, diagnostics);
                    foreach (var row in workflow.Encounters)
                    {
                        var id = SwShStaticEncountersWorkflowService.CreateEncounterRecordId(row.EncounterIndex, row.EncounterKey);
                        Fields(id, SwShStaticEncountersWorkflowService.StaticEncountersEditDomain, id, Source(row.Provenance),
                            [SwShStaticEncountersWorkflowService.SpeciesField, SwShStaticEncountersWorkflowService.FormField,
                                SwShStaticEncountersWorkflowService.CanGigantamaxField],
                            [Number(row.SpeciesId), Number(row.Form), row.CanGigantamax ? "1" : "0"], IdentitySources(Source(row.Provenance)));
                    }
                    break;
                }
                case "Gift Encounters":
                {
                    var workflow = giftPokemonWorkflowService.Load(project);
                    AddWorkflowErrors(workflow.Diagnostics, diagnostics);
                    foreach (var row in workflow.Gifts)
                    {
                        var id = SwShGiftPokemonWorkflowService.CreateGiftRecordId(row.GiftIndex);
                        Fields(id, SwShGiftPokemonWorkflowService.GiftPokemonEditDomain, id, Source(row.Provenance),
                            [SwShGiftPokemonWorkflowService.SpeciesField, SwShGiftPokemonWorkflowService.FormField,
                                SwShGiftPokemonWorkflowService.CanGigantamaxField],
                            [Number(row.SpeciesId), Number(row.Form), row.CanGigantamax ? "1" : "0"], IdentitySources(Source(row.Provenance)));
                    }
                    break;
                }
                case "Raid Rewards":
                case "Raid Bonus Rewards":
                {
                    var bonus = domain == "Raid Bonus Rewards";
                    var workflow = raidRewardsWorkflowService.Load(project, bonus ? SwShRaidRewardWorkflowKind.Bonus : SwShRaidRewardWorkflowKind.Drop);
                    AddWorkflowErrors(workflow.Diagnostics, diagnostics);
                    var itemSources = SwShRaidRewardsWorkflowService.ResolveItemDisplaySourcesForValidation(project)
                        .Select(source => new ProjectFileReference(source.GraphEntry.LayeredFile is null ? ProjectFileLayer.Base : ProjectFileLayer.Layered,
                            source.GraphEntry.RelativePath)).ToArray();
                    foreach (var table in workflow.Tables)
                    foreach (var row in table.Rewards)
                    {
                        var source = Source(table.Provenance);
                        // The public TableId binds the source revision. Persist its stable physical identity instead.
                        var key = $"{table.ArchiveMember}/{table.TableIndex}/{table.SourceTableHash}/{row.Slot}/{row.EntryId}";
                        Fields(key, bonus ? SwShRaidRewardsEditSessionService.RaidBonusRewardsEditDomain : SwShRaidRewardsEditSessionService.RaidRewardsEditDomain,
                            SwShRaidRewardsWorkflowService.CreateRewardRecordId(table.TableId, row.Slot), source,
                            [SwShRaidRewardsWorkflowService.ItemIdField], [Number(row.ItemId)], [source, .. itemSources]);
                    }
                    break;
                }
                case "Type Chart":
                {
                    var workflow = typeChartWorkflowService.Load(project);
                    AddWorkflowErrors(workflow.Diagnostics, diagnostics);
                    foreach (var cell in workflow.Cells.OrderBy(cell => cell.AttackTypeIndex).ThenBy(cell => cell.DefenseTypeIndex))
                        Add($"{cell.AttackTypeIndex}/{cell.DefenseTypeIndex}", "exefs/main", [Number(cell.Effectiveness)], _ => []);
                    break;
                }
                default:
                    throw new InvalidDataException("Unsupported Randomizer value domain.");
            }
        }
        return values;
    }

    private static IReadOnlyList<RandomizerValueOwnership> ValidateRandomizerValues(RandomizerRestoreManifest manifest)
    {
        var values = manifest.Values ?? throw new InvalidDataException("Randomizer value ownership is missing.");
        if (values.Count > 500_000) throw new InvalidDataException("Randomizer value ownership exceeds its supported bound.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null || value.Domain is not ("Pokemon" or "Wild Encounters" or "Static Encounters" or "Gift Encounters" or "Raid Rewards" or "Raid Bonus Rewards" or "Type Chart")
                || string.IsNullOrEmpty(value.Key) || value.Key.Length > 1024 || !IsLayeredFsRelativePath(value.Path)
                || value.Before is null || value.After is null || value.Before.Length > 1024 || value.After.Length > 1024
                || value.Before.Concat(value.After).Any(part => part is null || part.Length > 128)
                || !keys.Add(ValueKey(value.Domain, value.Key)))
                throw new InvalidDataException("Randomizer value ownership is invalid or duplicated.");
        }
        return values;
    }

    private static IReadOnlyList<RandomizerValueOwnership> MergeRandomizerValues(
        IReadOnlyList<RandomizerValueOwnership> existing,
        IReadOnlyDictionary<string, RandomizerCurrentValue> before,
        IReadOnlyDictionary<string, RandomizerCurrentValue> after)
    {
        var result = existing.ToDictionary(value => ValueKey(value.Domain, value.Key), StringComparer.Ordinal);
        foreach (var (key, current) in before)
        {
            if (!after.TryGetValue(key, out var written)) throw new InvalidDataException("Randomizer changed a stable record identity.");
            result.TryGetValue(key, out var owned);
            // An explicit later value becomes the new preimage if Randomizer replaces it again.
            var original = owned is not null && current.Values.SequenceEqual(owned.After) ? owned.Before : current.Values;
            if (original.SequenceEqual(written.Values)) result.Remove(key);
            else result[key] = new(current.Domain, current.Key, current.Path, original, written.Values);
        }
        return result.Values.OrderBy(value => ValueKey(value.Domain, value.Key), StringComparer.Ordinal).ToArray();
    }
}
