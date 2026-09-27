// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Editing;
using KM.Core.GameDump;
using KM.ZA.Pokemon;
using KM.ZA.Shops;
using KM.ZA.Encounters;
using KM.ZA.Gifts;
using KM.ZA.Trades;
using KM.ZA.Trainers;
using KM.ZA.Placement;
using KM.ZA.Workflows;

namespace KM.ZA.GameDump;

public sealed partial class ZaEditableDumpProvider
{
    public DumpStageResult Stage(string category, IReadOnlyList<DumpUpdate> updates, EditSession session)
    {
        using var freshReads = ZaWorkflowFileSource.BeginFreshReadScope(activePaths);
        switch (category)
        {
            case "items":
            {
                var result = workflow.UpdateItemFields(activePaths, session, updates.Select(u => new ZaItemFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "moves":
            {
                var result = workflow.UpdateMoveFields(activePaths, session, updates.Select(u => new ZaMoveFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "pokemon": return StagePokemon(updates, session);
            case "trainers":
            {
                var result = workflow.UpdateTrainerFields(activePaths, session, updates.Select(u => new ZaTrainerFieldUpdate(Id(u), u.Record.Slot, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "encounters":
            {
                var result = workflow.UpdateEncounterSlotFields(activePaths, session, updates.Select(u => new ZaEncounterSlotFieldUpdate(u.Record.TargetId, u.Record.Slot!.Value, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "giftPokemon":
            {
                var result = workflow.UpdateGiftPokemonFields(activePaths, session, updates.Select(u => new ZaGiftPokemonFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "tradePokemon":
            {
                var result = workflow.UpdateTradePokemonFields(activePaths, session, updates.Select(u => new ZaTradePokemonFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "scriptedBosses":
            {
                var result = workflow.UpdateEncounterSlotFields(activePaths, session, updates.Select(u => new ZaEncounterSlotFieldUpdate(u.Record.TargetId, u.Record.Slot!.Value, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "shops":
            {
                var result = workflow.UpdateShopInventoryItems(activePaths, session, updates.Select(u => new ZaShopInventoryItemUpdate(u.Record.TargetId, u.Record.Slot!.Value, u.Field.Target, u.Value, u.Record.Id)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "placement":
            {
                var result = workflow.UpdatePlacementObjectFields(activePaths, session, updates.Select(u => new ZaPlacementObjectFieldUpdate(u.Record.TargetId, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "behavior":
            {
                var result = workflow.UpdateBehavior(activePaths, session, updates.Select(u => new KM.ZA.Behavior.ZaBehaviorUpdate(u.Record.TargetId, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "text":
            {
                var changes = new List<PendingEdit>();
                foreach (var update in updates)
                {
                    var query = KM.ZA.Text.ZaTextWorkflowService.TryGetVirtualPathFromTextKey(update.Record.TargetId, out var sourcePath, out var line)
                        ? new KM.ZA.Text.ZaTextWorkflowQuery($"romfs/{sourcePath}", line, 1, Language: sourcePath.Split('/')[2]) : null;
                    var result = workflow.UpdateTextEntry(activePaths, null, update.Record.TargetId, update.Value, query);
                    if (result.Diagnostics.Any(d => d.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error)) return new(session, result.Diagnostics);
                    changes.AddRange(result.Session.PendingEdits);
                }
                var selected = updates.Select(update => update.Record.TargetId).ToHashSet(StringComparer.Ordinal);
                return new(session with { PendingEdits = session.PendingEdits
                    .Where(edit => edit.Domain != "workflow.text" || !selected.Contains(edit.RecordId!)).Concat(changes).ToArray() }, []);
            }
            case "typeChart":
            {
                var data = documents[category];
                var changed = updates.ToDictionary(u => u.Record.Id);
                var values = data.Records.Select(row => int.Parse(changed.TryGetValue(row.Id, out var update)
                    ? update.Value : row.Fields[0].Value, CultureInfo.InvariantCulture)).ToArray();
                var result = workflow.StageTypeChart(activePaths, values, session);
                return new(result.Session, result.Diagnostics);
            }
            default: throw new ArgumentException("This YAML category cannot be imported into Z-A.");
        }
    }

    private DumpStageResult StagePokemon(IReadOnlyList<DumpUpdate> updates, EditSession session)
    {
        var fields = updates.Where(u => u.Record.Route == "pokemon").Select(u => new ZaPokemonFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray();
        var learnsets = updates.Where(u => u.Record.Route == "learnset").GroupBy(u => u.Record).Select(group =>
        {
            int Value(string field) => int.Parse(group.FirstOrDefault(u => u.Field.Target == field)?.Value
                ?? group.Key.Fields.First(f => f.Target == field).Value, CultureInfo.InvariantCulture);
            return new ZaPokemonLearnsetUpdate(int.Parse(group.Key.TargetId), "upsert", group.Key.Slot, Value("moveId"), Value("level"));
        }).ToArray();
        var evolutions = updates.Where(u => u.Record.Route == "evolution").GroupBy(u => u.Record).Select(group =>
        {
            int Value(string field) => int.Parse(group.FirstOrDefault(u => u.Field.Target == field)?.Value
                ?? group.Key.Fields.First(f => f.Target == field).Value, CultureInfo.InvariantCulture);
            return new ZaPokemonEvolutionOperation(int.Parse(group.Key.TargetId), "upsert", group.Key.Slot,
                Value("method"), Value("argument"), Value("species"), Value("form"), Value("level"));
        }).ToArray();
        var result = workflow.UpdatePokemonComposite(activePaths, session, fields, evolutions, learnsets);
        return new(result.Session, result.Diagnostics);
    }

    private static int Id(DumpUpdate update) => int.Parse(update.Record.TargetId, CultureInfo.InvariantCulture);
}
