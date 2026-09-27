// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.ZA.Workflows;
using static KM.Core.GameDump.DumpSchemaBuilder;

namespace KM.ZA.GameDump;

public sealed partial class ZaEditableDumpProvider : IEditableDumpProvider
{
    private readonly ProjectPaths paths;
    private readonly ZaWorkflowService workflow;
    private ProjectPaths activePaths;
    private readonly Dictionary<string, EditableDumpDocument> documents = new(StringComparer.Ordinal);

    public ZaEditableDumpProvider(ProjectPaths paths, ZaWorkflowService workflow)
    {
        this.paths = paths;
        this.workflow = workflow;
        activePaths = paths;
    }

    public EditableDumpDocument Load(string category, string? language = null)
    {
        workflow.ClearMemoryCaches();
        if (paths.SelectedGame != ProjectGame.ZA) throw new ArgumentException("This dump requires Z-A.");
        activePaths = paths with { GameTextLanguage = language ?? paths.GameTextLanguage ?? "en" };
        using var freshReads = ZaWorkflowFileSource.BeginFreshReadScope(activePaths);
        var records = new List<DumpRecord>();
        var sourceFileCount = 0;
        var requestedLanguage = KM.ZA.Data.ZaGameTextLanguage.Resolve(activePaths);
        IReadOnlyList<ValidationDiagnostic> diagnostics;
        switch (category)
        {
            case "items":
            {
                var data = workflow.LoadItems(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                records.AddRange(data.Items.Select(row => Record(row, fields, "itemId", "name", category,
                    fields.ToDictionary(f => f.Field, f => f.Field is "buyPrice" or "sellPrice" ? f.Field : "fieldValues." + f.Field))));
                break;
            }
            case "pokemon":
            {
                var data = workflow.LoadPokemon(activePaths); diagnostics = data.Diagnostics;
                BuildPokemon(data, records);
                break;
            }
            case "moves":
            {
                var data = workflow.LoadMoves(activePaths); diagnostics = data.Diagnostics;
                BuildMoves(data, records);
                break;
            }
            case "trainers":
            {
                var data = workflow.LoadTrainers(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                var root = new HashSet<string>(["rank", "money", "aiFlags", "megaEvolution", "lastHand"]);
                foreach (var trainer in data.Trainers)
                {
                    var record = Record(trainer, fields.Where(f => root.Contains(f.Field)).ToArray(), "trainerId", "name", category);
                    record.Children["party"] = trainer.Team.Select(member => Record(member,
                        fields.Where(f => !root.Contains(f.Field)).ToArray(), "slot", "species", category,
                        InstancePaths(true), Value(trainer.TrainerId), member.Slot)).ToList();
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
                    record.Children["slots"] = table.Slots.Select(slot =>
                    {
                        var map = new Dictionary<string, string> { ["shinyLock"] = "shinyMode" };
                        for (var i = 0; i < 4; i++) map[$"move{i + 1}Id"] = $"moveIds.{i}";
                        var selected = fields.Select(f => f with { ReadOnly = f.ReadOnly || f.Field switch
                        {
                            "weight" => !slot.CanEditWeight,
                            "slotMaxCount" => !slot.CanEditSlotMaxCount,
                            "appearanceMinCount" => !slot.CanEditAppearanceMinCount,
                            "appearanceMaxCount" => !slot.CanEditAppearanceMaxCount,
                            _ => false,
                        }}).ToArray();
                        return Record(slot, selected, "slot", "species", category, map, table.TableId, slot.Slot);
                    }).ToList();
                    records.Add(record);
                }
                break;
            }
            case "scriptedBosses":
            {
                var data = workflow.LoadEncounters(activePaths); diagnostics = data.Diagnostics;
                BuildBosses(data, records);
                break;
            }
            case "giftPokemon":
            {
                var data = workflow.LoadGiftPokemon(activePaths); diagnostics = data.Diagnostics;
                records.AddRange(data.Gifts.Select(row => Record(row, Definitions(data.EditableFields), "giftIndex", "label", category, InstancePaths(false))));
                break;
            }
            case "tradePokemon":
            {
                var data = workflow.LoadTradePokemon(activePaths); diagnostics = data.Diagnostics;
                records.AddRange(data.Trades.Select(row => Record(row, Definitions(data.EditableFields), "tradeIndex", "label", category, InstancePaths(false))));
                break;
            }
            case "shops":
            {
                var data = workflow.LoadShops(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var shop in data.Shops)
                {
                    var record = new DumpRecord(shop.ShopId, shop.Name, category, shop.ShopId);
                    record.Children["inventory"] = shop.Inventory.Select(item =>
                    {
                        var selected = fields.Where(f => item.SupportedFields.Contains(f.Field) || f.Field == "itemId").ToArray();
                        var child = Record(item, selected, "rowId", "itemName", category,
                            selected.ToDictionary(f => f.Field, f => f.Field == "price" ? "price" : "fieldValues." + f.Field), shop.ShopId, item.Slot);
                        child.Fields.Add(new("price", "price", Value(item.Price), Editable: false));
                        return child;
                    }).ToList();
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
                    foreach (var field in row.Fields)
                        record.Fields.Add(Field(new(field.Field, field.ValueKind, field.MinimumValue, field.MaximumValue,
                            field.Options?.Select(o => new DumpChoice(Value(o.Value), o.Label)).ToArray() ?? [], field.IsReadOnly), field.Value));
                    records.Add(record);
                }
                break;
            }
            case "behavior":
            {
                var data = workflow.LoadBehavior(activePaths); diagnostics = data.Diagnostics;
                foreach (var resource in data.Resources)
                {
                    var record = new DumpRecord(resource.EntryId, resource.SpeciesName, category, resource.EntryId);
                    record.Fields.Add(new("profile", "profile", resource.Profile, "text",
                        Choices: data.Profiles.Select(p => new DumpChoice(p.Value, p.Label)).ToArray()));
                    foreach (var field in data.Fields)
                        if (resource.Fields.TryGetValue(field.Field, out var value))
                            record.Fields.Add(new(Key(field.Field), field.Field, value, "number", field.Minimum, field.Maximum));
                    records.Add(record);
                }
                break;
            }
            case "text":
            {
                var data = workflow.LoadTextUnpaged(activePaths, activePaths.GameTextLanguage); diagnostics = data.Diagnostics;
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
            default: throw new ArgumentException("This YAML category is not supported by Z-A.");
        }
        var document = new EditableDumpDocument("za", category, activePaths.GameTextLanguage!, records, diagnostics);
        document = document with { RequestedLanguage = requestedLanguage, SourceFileCount = sourceFileCount };
        documents[category] = document;
        return document;
    }

    private static Dictionary<string, string> InstancePaths(bool trainer)
    {
        var result = new Dictionary<string, string> { ["species"] = "speciesId", ["alphaLevelBonus"] = "alphaAdditionalLevel" };
        foreach (var stat in new[] { "Hp", "Attack", "Defense", "SpecialAttack", "SpecialDefense", "Speed" })
        {
            result["iv" + stat] = "ivs." + stat;
            result["ev" + stat] = "evs." + stat;
        }
        for (var index = 0; index < 4; index++)
        {
            result[$"move{index + 1}Id"] = trainer ? $"moveIds.{index}" : $"moves.{index}.moveId";
            result[$"move{index + 1}PointUps"] = $"moves.{index}.pointUps";
        }
        return result;
    }
}
