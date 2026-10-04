// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using static KM.Core.GameDump.DumpSchemaBuilder;

namespace KM.SwSh.GameDump;

public sealed partial class SwShEditableDumpProvider : IEditableDumpProvider
{
    private static readonly string[] HeldItemRateFields = ["normal_slot_1", "normal_slot_2", "normal_slot_3", "boosted_slot_1", "boosted_slot_2", "boosted_slot_3"];
    private readonly ProjectPaths paths;
    private readonly SwShWorkflowService workflow;
    private ProjectPaths activePaths;
    private readonly Dictionary<string, EditableDumpDocument> documents = new(StringComparer.Ordinal);

    public SwShEditableDumpProvider(ProjectPaths paths, SwShWorkflowService workflow)
    {
        this.paths = paths;
        this.workflow = workflow;
        activePaths = paths;
    }

    public EditableDumpDocument Load(string category, string? language = null)
    {
        workflow.ClearMemoryCaches();
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)) throw new ArgumentException("This dump requires Sword or Shield.");
        activePaths = paths with { GameTextLanguage = language ?? paths.GameTextLanguage ?? "en" };
        var records = new List<DumpRecord>();
        var sourceFileCount = 0;
        var requestedLanguage = SwShGameTextLanguage.Resolve(activePaths);
        IReadOnlyList<ValidationDiagnostic> diagnostics;
        switch (category)
        {
            case "items":
            {
                var data = workflow.LoadItems(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                records.AddRange(data.Items.Select(row => Record(row, fields, "itemId", "name", category,
                    fields.ToDictionary(f => f.Field, f => f.Field is "buyPrice" or "sellPrice" or "wattsPrice" ? f.Field : "fieldValues." + f.Field))));
                break;
            }
            case "moves":
            {
                var data = workflow.LoadMoves(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var move in data.Moves)
                {
                    var map = new Dictionary<string, string>();
                    for (var i = 0; i < move.Flags.Count; i++) map[move.Flags[i].Field] = $"flags.{i}.enabled";
                    for (var i = 0; i < 3; i++)
                    {
                        map[$"stat{i + 1}"] = $"statChanges.{i}.stat";
                        map[$"stat{i + 1}Stage"] = $"statChanges.{i}.stage";
                        map[$"stat{i + 1}Percent"] = $"statChanges.{i}.percent";
                    }
                    records.Add(Record(move, fields, "moveId", "name", category, map));
                }
                break;
            }
            case "pokemon":
            {
                var data = workflow.LoadPokemon(activePaths); diagnostics = data.Diagnostics;
                BuildPokemon(data, records);
                break;
            }
            case "trainers":
            {
                var data = workflow.LoadTrainers(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                var root = new HashSet<string>(["trainerClassId", "battleType", "aiFlags", "heal", "money", "gift", "classBallId", "trainerItem1Id", "trainerItem2Id", "trainerItem3Id", "trainerItem4Id"]);
                var map = new Dictionary<string, string> { ["battleType"] = "battleTypeValue" };
                for (var i = 0; i < 4; i++) map[$"trainerItem{i + 1}Id"] = $"itemIds.{i}";
                foreach (var trainer in data.Trainers)
                {
                    var selected = fields.Where(f => root.Contains(f.Field)).Select(f => f with { ReadOnly = f.Field == "classBallId" && !trainer.CanEditClassBall }).ToArray();
                    var record = Record(trainer, selected, "trainerId", "name", category, map);
                    record.Children["party"] = trainer.Team.Select(member => Record(member,
                        fields.Where(f => !root.Contains(f.Field)).ToArray(), "slot", "species", category,
                        InstancePaths(true), Value(trainer.TrainerId), member.Slot)).ToList();
                    records.Add(record);
                }
                break;
            }
            case "giftPokemon":
            {
                var data = workflow.LoadGiftPokemon(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                records.AddRange(data.Gifts.Select(row => Record(row, fields, "giftIndex", "label", category, InstancePaths(false))));
                break;
            }
            case "tradePokemon":
            {
                var data = workflow.LoadTradePokemon(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                records.AddRange(data.Trades.Select(row => Record(row, fields, "tradeIndex", "label", category, InstancePaths(false))));
                break;
            }
            case "staticEncounters":
            {
                var data = workflow.LoadStaticEncounters(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                records.AddRange(data.Encounters.Select(row => Record(row, fields, "encounterIndex", "label", category, InstancePaths(false))));
                break;
            }
            case "rentalPokemon":
            {
                var data = workflow.LoadRentalPokemon(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var row in data.Rentals)
                {
                    var record = Record(row, fields, "rentalIndex", "label", category, InstancePaths(false));
                    var stats = new[] { row.Ivs.HP, row.Ivs.Attack, row.Ivs.Defense, row.Ivs.SpecialAttack, row.Ivs.SpecialDefense, row.Ivs.Speed };
                    if (fields.FirstOrDefault(f => f.Field == "fixedIvPreset") is { } preset)
                        record.Fields.Add(Field(preset with { Choices = [new("-1", "Custom IVs"), .. preset.Choices] },
                            stats.Distinct().Count() == 1 ? Value(stats[0]) : "-1"));
                    records.Add(record);
                }
                break;
            }
            case "encounters":
            {
                var data = workflow.LoadEncounters(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var table in data.Tables)
                {
                    var record = new DumpRecord(table.TableId, table.Location, category, table.TableId);
                    record.Children["slots"] = table.Slots.Select(slot => Record(slot, fields, "slot", "species", category,
                        new Dictionary<string, string> { ["probability"] = "weight" }, table.TableId, slot.Slot)).ToList();
                    records.Add(record);
                }
                break;
            }
            case "raidBattles":
            {
                var data = workflow.LoadRaidBattles(activePaths); diagnostics = data.Diagnostics;
                BuildRaids(data, records);
                break;
            }
            case "raidRewards":
            case "raidBonusRewards":
            {
                var data = category == "raidRewards" ? workflow.LoadRaidRewards(activePaths) : workflow.LoadRaidBonusRewards(activePaths);
                diagnostics = data.Diagnostics;
                BuildRewards(data, records, category);
                break;
            }
            case "shops":
            {
                var data = workflow.LoadShops(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var shop in data.Shops)
                {
                    var record = new DumpRecord(shop.ShopId, shop.Name, category, shop.ShopId);
                    record.Children["inventory"] = shop.Inventory.Select(item => Record(item, fields, "slot", "itemName", category, targetId: shop.ShopId, slot: item.Slot)).ToList();
                    records.Add(record);
                }
                break;
            }
            case "placement":
            {
                var data = workflow.LoadPlacement(activePaths); diagnostics = data.Diagnostics;
                foreach (var row in data.Objects)
                {
                    var record = new DumpRecord(row.ObjectId, row.Label, category, row.ObjectId);
                    if (row.Fields is { Count: > 0 })
                        foreach (var field in row.Fields)
                            record.Fields.Add(Field(new(field.Field, field.ValueKind, field.MinimumValue, field.MaximumValue,
                                field.Options?.Select(o => new DumpChoice(Value(o.Value), o.Label)).ToArray() ?? [], field.IsReadOnly), field.Value));
                    else Fields(record, DumpSchemaBuilder.Data(row), Definitions(data.EditableFields));
                    records.Add(record);
                }
                break;
            }
            case "behavior":
            {
                var data = workflow.LoadBehavior(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.Fields).ToDictionary(f => f.Field);
                foreach (var row in data.Entries)
                {
                    var record = new DumpRecord(row.EntryId, row.Label, category, row.EntryId);
                    foreach (var field in row.Fields)
                        if (fields.TryGetValue(field.Field, out var definition)) record.Fields.Add(Field(definition, field.Value));
                    records.Add(record);
                }
                break;
            }
            case "text":
            {
                var data = workflow.LoadText(activePaths); diagnostics = data.Diagnostics;
                sourceFileCount = data.Stats.SourceFileCount;
                activePaths = activePaths with { GameTextLanguage = data.SelectedLanguage };
                foreach (var text in data.Entries)
                {
                    var record = new DumpRecord(text.TextKey, text.Label, category, text.TextKey);
                    record.Fields.Add(new("text", "value", text.Value, "text", Editable: text.CanEdit));
                    records.Add(record);
                }
                break;
            }
            case "heldItemChance":
            {
                var data = new KM.SwSh.HeldItemChance.SwShHeldItemChanceService().Load(activePaths);
                diagnostics = data.Diagnostics;
                if (data.Rates.Count == 6)
                {
                    var record = new DumpRecord("global-held-items", "Global held item percentages", category, "global-held-items");
                    for (var index = 0; index < 6; index++)
                        record.Fields.Add(new(HeldItemRateFields[index], HeldItemRateFields[index], Value(data.Rates[index]), Minimum: 0, Maximum: 100));
                    records.Add(record);
                    foreach (var pokemon in data.Pokemon)
                    {
                        var id = pokemon.PersonalId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var label = string.IsNullOrWhiteSpace(pokemon.FormLabel) ? pokemon.Name : $"{pokemon.Name} ({pokemon.FormLabel})";
                        var entry = new DumpRecord(id, label, category, id);
                        entry.Fields.Add(new("custom_rates", "customRates", pokemon.CustomRates ? "1" : "0", Minimum: 0, Maximum: 1));
                        for (var index = 0; index < 6; index++)
                            entry.Fields.Add(new(HeldItemRateFields[index], HeldItemRateFields[index], Value(pokemon.Rates[index]), Minimum: 0, Maximum: 100));
                        records.Add(entry);
                    }
                    sourceFileCount = 2;
                }
                break;
            }
            case "typeChart":
            {
                var data = workflow.LoadTypeChart(activePaths); diagnostics = data.Diagnostics;
                foreach (var cell in data.Cells)
                {
                    var id = $"{cell.AttackTypeIndex}:{cell.DefenseTypeIndex}";
                    var record = new DumpRecord(id, $"{data.Types[cell.AttackTypeIndex].Label} / {data.Types[cell.DefenseTypeIndex].Label}", category, id);
                    record.Fields.Add(new("effectiveness", "effectiveness", Value(cell.Effectiveness), Choices:
                        [new("0", "Immune"), new("2", "Half damage"), new("4", "Normal damage"), new("8", "Double damage")]));
                    records.Add(record);
                }
                break;
            }
            default: throw new ArgumentException("This YAML category is not supported by Sword/Shield.");
        }
        var document = new EditableDumpDocument(paths.SelectedGame.Value.ToString().ToLowerInvariant(), category, activePaths.GameTextLanguage!, records, diagnostics);
        document = document with { RequestedLanguage = requestedLanguage, SourceFileCount = sourceFileCount };
        documents[category] = document;
        return document;
    }

    private static Dictionary<string, string> InstancePaths(bool trainer)
    {
        var result = new Dictionary<string, string> { ["species"] = "speciesId", ["requiredSpecies"] = "requiredSpeciesId" };
        foreach (var stat in new[] { "Hp", "Attack", "Defense", "SpecialAttack", "SpecialDefense", "Speed" })
        {
            result["iv" + stat] = "ivs." + stat;
            result["ev" + stat] = "evs." + stat;
        }
        for (var index = 0; index < 4; index++)
        {
            result[$"move{(trainer ? index + 1 : index)}Id"] = trainer ? $"moveIds.{index}" : $"moves.{index}.moveId";
            result[$"relearnMove{index}"] = $"relearnMoves.{index}.moveId";
            result[$"move{index + 1}PointUps"] = $"moves.{index}.pointUps";
        }
        return result;
    }
}
