// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.SV.Pokemon;
using KM.SV.Shops;
using KM.SV.StaticEncounters;
using KM.SV.Workflows;
using static KM.Core.GameDump.DumpSchemaBuilder;

namespace KM.SV.GameDump;

public sealed partial class SvEditableDumpProvider : IEditableDumpProvider
{
    private readonly ProjectPaths paths;
    private readonly SvWorkflowService workflow;
    private ProjectPaths activePaths;
    private readonly Dictionary<string, EditableDumpDocument> documents = new(StringComparer.Ordinal);

    public SvEditableDumpProvider(ProjectPaths paths, SvWorkflowService workflow)
    {
        this.paths = paths;
        this.workflow = workflow;
        activePaths = paths;
    }

    public EditableDumpDocument Load(string category, string? language = null)
    {
        workflow.ClearMemoryCaches();
        if (paths.SelectedGame is not (ProjectGame.Scarlet or ProjectGame.Violet))
            throw new ArgumentException("This dump requires Scarlet or Violet.");
        activePaths = paths with { GameTextLanguage = language ?? paths.GameTextLanguage ?? "en" };
        using var freshReads = SvWorkflowFileSource.BeginFreshReadScope(activePaths);
        var records = new List<DumpRecord>();
        var sourceFileCount = 0;
        var requestedLanguage = KM.SV.Data.SvGameTextLanguage.Resolve(activePaths);
        IReadOnlyList<ValidationDiagnostic> diagnostics;
        switch (category)
        {
            case "items":
            {
                var data = workflow.LoadItems(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var item in data.Items)
                    records.Add(Simple(item, fields, "itemId", "name", category,
                        fields.ToDictionary(f => f.Field, f => f.Field is "buyPrice" or "sellPrice" or "wattsPrice" ? f.Field : "fieldValues." + f.Field)));
                break;
            }
            case "moves":
            {
                var data = workflow.LoadMoves(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var move in data.Moves)
                {
                    var json = DumpSchemaBuilder.Data(move);
                    var map = MovePaths(json, fields);
                    records.Add(Simple(move, fields, "moveId", "name", category, map));
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
                var rootFields = new HashSet<string>(["battleType", "money", "aiFlags", "isStrong", "changeGem"]);
                foreach (var trainer in data.Trainers)
                {
                    var record = Simple(trainer, fields.Where(f => rootFields.Contains(f.Field)).ToArray(), "trainerId", "name", category,
                        new Dictionary<string, string> { ["battleType"] = "battleTypeValue", ["changeGem"] = "canTerastallize" });
                    record.Children["party"] = trainer.Team.Select(member =>
                    {
                        var child = new DumpRecord(Value(member.Slot + 1), member.Species, "trainers", Value(trainer.TrainerId), member.Slot);
                        Fields(child, DumpSchemaBuilder.Data(member), fields.Where(f => !rootFields.Contains(f.Field)).ToArray(), PokemonInstancePaths(true));
                        return child;
                    }).ToList();
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
                        var child = new DumpRecord(Value(slot.Slot), slot.Species, category, table.TableId, slot.Slot);
                        Fields(child, DumpSchemaBuilder.Data(slot), fields, new Dictionary<string, string> { ["probability"] = "weight" });
                        child.Fields.Add(new("weather", "weather", slot.Weather, "text", Editable: false));
                        child.Fields.Add(new("time_of_day", "timeOfDay", slot.TimeOfDay ?? "Any", "text", Editable: false));
                        return child;
                    }).ToList();
                    records.Add(record);
                }
                break;
            }
            case "giftPokemon":
            {
                var data = workflow.LoadGiftPokemon(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                records.AddRange(data.Gifts.Select(row => Simple(row, fields, "giftIndex", "label", category, PokemonInstancePaths(false))));
                break;
            }
            case "tradePokemon":
            {
                var data = workflow.LoadTradePokemon(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var row in data.Trades)
                {
                    var record = Simple(row, fields, "tradeIndex", "label", category, PokemonInstancePaths(false));
                    if (row.FlawlessIvCount is null && fields.FirstOrDefault(f => f.Field == "flawlessIvCount") is { } preset)
                        record.Fields.Add(Field(preset with { Choices = [new("-1", "Custom IVs"), .. preset.Choices] }, "-1"));
                    records.Add(record);
                }
                break;
            }
            case "staticEncounters":
            {
                var data = workflow.LoadStaticEncounters(activePaths); diagnostics = data.Diagnostics;
                var fields = Definitions(data.EditableFields);
                foreach (var row in data.Encounters)
                {
                    var record = new DumpRecord(row.EncounterId, row.Label, category, Value(row.EncounterIndex));
                    var selected = fields.Where(f => row.SupportedFields.Contains(f.Field))
                        .Select(f => f with { ReadOnly = f.ReadOnly || row.FieldReadOnly.GetValueOrDefault(f.Field) }).ToArray();
                    Fields(record, DumpSchemaBuilder.Data(row), selected, selected.ToDictionary(f => f.Field, f => "fieldValues." + f.Field));
                    records.Add(record);
                }
                break;
            }
            case "teraRaids":
            {
                var data = workflow.LoadTeraRaids(activePaths); diagnostics = data.Diagnostics;
                BuildRaids(data, records);
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
                        var child = new DumpRecord(item.RowId, item.ItemName, category, shop.ShopId, item.Slot);
                        var included = item.SupportedFields.ToHashSet(StringComparer.Ordinal);
                        included.Add("itemId");
                        Fields(child, DumpSchemaBuilder.Data(item), fields, fields.ToDictionary(f => f.Field,
                            f => f.Field == "price" ? "price" : "fieldValues." + f.Field), included);
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
                var fields = Definitions(data.EditableFields).ToDictionary(f => f.Field);
                foreach (var row in data.Objects)
                {
                    var record = new DumpRecord(row.ObjectId, row.Label, category, row.ObjectId);
                    foreach (var item in row.Fields)
                    {
                        var definition = fields.GetValueOrDefault(item.Field);
                        record.Fields.Add(new(Key(item.Field), item.Field, item.Value,
                            item.IsReadOnly ? "text" : definition?.Kind == "float" ? "number" : definition?.Kind ?? "integer",
                            definition?.Minimum, definition?.Maximum,
                            item.Options?.Select(o => new DumpChoice(Value(o.Value), o.Label)).ToArray() ?? definition?.Choices,
                            !item.IsReadOnly && definition?.ReadOnly == false));
                    }
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
            default: throw new ArgumentException("This YAML category is not supported by Scarlet/Violet.");
        }
        var document = new EditableDumpDocument(paths.SelectedGame.Value.ToString().ToLowerInvariant(), category,
            activePaths.GameTextLanguage!, records, diagnostics);
        document = document with { RequestedLanguage = requestedLanguage, SourceFileCount = sourceFileCount };
        documents[category] = document;
        return document;
    }

    private static DumpRecord Simple<T>(T row, IReadOnlyList<DumpFieldDefinition> fields, string id, string name,
        string route, IReadOnlyDictionary<string, string>? map = null)
    {
        var data = DumpSchemaBuilder.Data(row);
        var record = new DumpRecord(Read(data, id)!, Read(data, name)!, route, Read(data, id)!);
        Fields(record, data, fields, map);
        return record;
    }

    private static Dictionary<string, string> PokemonInstancePaths(bool trainer)
    {
        var result = new Dictionary<string, string> { ["species"] = "speciesId", ["requiredSpecies"] = "requiredSpeciesId", ["ballItemId"] = "ballId" };
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

    private static Dictionary<string, string> MovePaths(JsonElement row, IReadOnlyList<DumpFieldDefinition> fields)
    {
        var map = new Dictionary<string, string>();
        if (Element(row, "flags") is { ValueKind: JsonValueKind.Array } flags)
            foreach (var flag in flags.EnumerateArray().Select((value, index) => (value, index)))
                map[Read(flag.value, "field")!] = $"flags.{flag.index}.enabled";
        for (var index = 0; index < 3; index++)
        {
            map[$"stat{index + 1}"] = $"statChanges.{index}.stat";
            map[$"stat{index + 1}Stage"] = $"statChanges.{index}.stage";
            map[$"stat{index + 1}Percent"] = $"statChanges.{index}.percent";
        }
        return map;
    }
}
