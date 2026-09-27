// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Editing;
using KM.Core.GameDump;
using KM.SwSh.Pokemon;
using KM.SwSh.Shops;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Trainers;
using KM.SwSh.Encounters;
using KM.SwSh.Gifts;
using KM.SwSh.Trades;
using KM.SwSh.Rentals;
using KM.SwSh.Raids;
using KM.SwSh.Placement;
using KM.SwSh.Behavior;
using KM.SwSh.Text;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;

namespace KM.SwSh.GameDump;

public sealed partial class SwShEditableDumpProvider
{
    public DumpStageResult Stage(string category, IReadOnlyList<DumpUpdate> updates, EditSession session)
    {
        switch (category)
        {
            case "items":
            {
                var result = workflow.UpdateItemFieldsFreshBounded(activePaths, session, updates.Select(u => new SwShItemFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "moves":
            {
                var result = workflow.UpdateMoveFieldsFreshBounded(activePaths, session, updates.Select(u => new SwShMoveFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "pokemon": return StagePokemon(updates, session);
            case "trainers":
            {
                var result = workflow.UpdateTrainerFieldsFreshBounded(activePaths, session, updates.Select(u => new SwShTrainerFieldUpdate(Id(u), u.Record.Slot, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "encounters":
            {
                var result = workflow.UpdateEncounterSlotFieldsFreshBounded(activePaths, session, updates.Select(u => new SwShEncounterSlotFieldUpdate(u.Record.TargetId, u.Record.Slot!.Value, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "giftPokemon":
            {
                var result = new SwShGiftPokemonEditSessionService().UpdateFields(activePaths, session, updates.Select(u => new SwShGiftPokemonFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "tradePokemon":
            {
                var result = new SwShTradePokemonEditSessionService().UpdateFields(activePaths, session, updates.Select(u => new SwShTradePokemonFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "staticEncounters":
            {
                var result = new SwShStaticEncountersEditSessionService().UpdateFields(activePaths, session, updates.Select(u => new SwShStaticEncounterFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "rentalPokemon":
            {
                var result = new SwShRentalPokemonEditSessionService().UpdateFields(activePaths, session, updates.Select(u => new SwShRentalPokemonFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "raidBattles":
            {
                var result = new SwShRaidBattlesEditSessionService().UpdateSlotFields(activePaths, session, updates.Select(u => new SwShRaidBattleFieldUpdate(u.Record.TargetId, u.Record.Slot!.Value, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "raidRewards":
            case "raidBonusRewards":
            {
                var service = new SwShRaidRewardsEditSessionService();
                var fields = updates.Select(u => new SwShRaidRewardFieldUpdate(u.Record.TargetId, u.Record.Slot!.Value, u.Field.Target, u.Value)).ToArray();
                var result = category == "raidRewards" ? service.UpdateRewardFields(activePaths, session, fields)
                    : service.UpdateBonusRewardFields(activePaths, session, fields);
                return new(result.Session, result.Diagnostics);
            }
            case "behavior":
            {
                var result = new SwShBehaviorEditSessionService().UpdateEntryFields(activePaths, session, updates.Select(u => new SwShBehaviorFieldUpdate(u.Record.TargetId, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "shops":
            {
                var result = new SwShShopsEditSessionService().UpdateInventoryItems(activePaths, session, updates.Select(u => new SwShShopInventoryItemUpdate(u.Record.TargetId, u.Record.Slot!.Value, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "placement":
            {
                var result = new SwShPlacementEditSessionService().UpdateObjectFields(activePaths, session, updates.Select(u => new SwShPlacementObjectFieldUpdate(u.Record.TargetId, u.Field.Target, u.Value)).ToArray());
                return new(result.Session, result.Diagnostics);
            }
            case "text":
            {
                var changes = new List<PendingEdit>();
                var service = new SwShTextEditSessionService();
                foreach (var update in updates)
                {
                    var query = KM.SwSh.Text.SwShTextWorkflowService.TryGetVirtualPathFromTextKey(update.Record.TargetId, out var sourcePath, out var line)
                        ? new KM.SwSh.Text.SwShTextWorkflowQuery(sourcePath, line, 1, Language: sourcePath.Split('/')[3]) : null;
                    var result = service.UpdateEntry(activePaths, null, update.Record.TargetId, update.Value, query);
                    if (result.Diagnostics.Any(d => d.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error)) return new(session, result.Diagnostics);
                    changes.AddRange(result.Session.PendingEdits);
                }
                var selected = updates.Select(update => update.Record.TargetId).ToHashSet(StringComparer.Ordinal);
                return new(session with { PendingEdits = session.PendingEdits
                    .Where(edit => edit.Domain != "workflow.text" || !selected.Contains(edit.RecordId!)).Concat(changes).ToArray() }, []);
            }
            case "heldItemChance":
            {
                var diagnostics = new List<KM.Core.Diagnostics.ValidationDiagnostic>();
                var pending = session.PendingEdits.Where(edit => edit.Domain == KM.SwSh.HeldItemChance.SwShHeldItemChanceService.Domain).ToArray();
                var rates = pending.Length > 0
                    ? KM.SwSh.HeldItemChance.SwShHeldItemChanceService.Decode(session with { PendingEdits = pending }, diagnostics)
                    : documents[category].Records.Single().Fields.Select(field => int.Parse(field.Value, CultureInfo.InvariantCulture)).ToArray();
                if (diagnostics.Count > 0) return new(session, diagnostics);
                foreach (var update in updates) rates[Array.IndexOf(HeldItemRateFields, update.Field.Target)] = int.Parse(update.Value, CultureInfo.InvariantCulture);
                for (var group = 0; group < 2; group++)
                    if (rates.Skip(group * 3).Take(3).Sum() > 100)
                        foreach (var update in updates.Where(update => Array.IndexOf(HeldItemRateFields, update.Field.Target) / 3 == group))
                            diagnostics.Add(new(KM.Core.Diagnostics.DiagnosticSeverity.Error,
                                "These three held item percentages must total 100% or less.", Field: update.Field.Target)
                                { Code = KM.SwSh.HeldItemChance.SwShHeldItemChanceService.RatesCode });
                if (diagnostics.Count > 0) return new(session, diagnostics);
                var result = new KM.SwSh.HeldItemChance.SwShHeldItemChanceService().Stage(activePaths, rates, session);
                return new(result.Session, result.Diagnostics);
            }
            case "typeChart":
            {
                var data = documents[category];
                var changed = updates.ToDictionary(u => u.Record.Id);
                var values = data.Records.Select(row => int.Parse(changed.TryGetValue(row.Id, out var update)
                    ? update.Value : row.Fields[0].Value, CultureInfo.InvariantCulture)).ToArray();
                var result = new SwShTypeChartEditSessionService().StageChart(activePaths, values, session);
                return new(result.Session, result.Diagnostics);
            }
            default: throw new ArgumentException("This YAML category cannot be imported into Sword/Shield.");
        }
    }

    private DumpStageResult StagePokemon(IReadOnlyList<DumpUpdate> updates, EditSession session)
    {
        var fields = updates.Where(u => u.Record.Route == "pokemon").Select(u => new SwShPokemonFieldUpdate(Id(u), u.Field.Target, u.Value)).ToArray();
        var learnsets = updates.Where(u => u.Record.Route == "learnset").GroupBy(u => u.Record).Select(group =>
        {
            int Value(string field) => int.Parse(group.FirstOrDefault(u => u.Field.Target == field)?.Value
                ?? group.Key.Fields.First(f => f.Target == field).Value, CultureInfo.InvariantCulture);
            return new SwShPokemonLearnsetUpdate(int.Parse(group.Key.TargetId), "upsert", group.Key.Slot, Value("moveId"), Value("level"));
        }).ToArray();
        var evolutions = updates.Where(u => u.Record.Route == "evolution").GroupBy(u => u.Record).Select(group =>
        {
            int Value(string field) => int.Parse(group.FirstOrDefault(u => u.Field.Target == field)?.Value
                ?? group.Key.Fields.First(f => f.Target == field).Value, CultureInfo.InvariantCulture);
            return new SwShPokemonEvolutionUpdate(int.Parse(group.Key.TargetId), "upsert", group.Key.Slot,
                Value("method"), Value("argument"), Value("species"), Value("form"), Value("level"));
        }).ToArray();
        var result = new SwShPokemonEditSessionService().UpdateComposite(activePaths, session, fields, evolutions, learnsets);
        return new(result.Session, result.Diagnostics);
    }

    private static int Id(DumpUpdate update) => int.Parse(update.Record.TargetId, CultureInfo.InvariantCulture);
}
