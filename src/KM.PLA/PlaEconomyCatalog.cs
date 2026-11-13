// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Formats.PLA;

namespace KM.PLA;

internal sealed class PlaEconomyCatalog
{
    internal const string ItemsPath = "bin/pml/item/item.dat";
    private const string MiscPath = "bin/pokemon/data/poke_misc.bin";
    private const string SelectionPath = "bin/event/event_progress/ginkgo/ginkgo_item_table.bin";
    private const ulong EmptyHash = 14695981039346656837;
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["crafting"] = "bin/appli/table/making/makelist.bin",
        ["shops"] = "bin/appli/shop/bin/ha_shop_data.bin",
        ["ginkgo"] = "bin/event/event_progress/ginkgo/ginkgo_item.bin",
        ["drops"] = "bin/pokemon/data/poke_drop_item.bin",
        ["research"] = "bin/appli/pokedex/res_table/pokedex_research_task_table.bin",
        ["ranks"] = "bin/appli/pokedex/res_table/pokedex_rank_table.bin",
    };
    private static readonly Dictionary<string, int[]> Widths = new(StringComparer.Ordinal)
    {
        ["crafting"] = [1, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4],
        ["shops"] = [8, 8, 4], ["ginkgo"] = [8, 8, 2, 4, 4], ["drops"] = [8, 4, 4, 4, 4],
        ["research"] = [4, 4, 4, 4, 4, 4, 8, 8, 8, 4, 4, 4, 4, 4, 4, 4, 1], ["ranks"] = [4, 4],
    };
    private static readonly Dictionary<string, string[]> Keys = new(StringComparer.Ordinal)
    {
        ["items"] = ["price", "meritPrice", "maxQuantity"],
        ["crafting"] = ["resultItem", "resultQuantity", "ingredient1", "quantity1", "ingredient2", "quantity2", "ingredient3", "quantity3", "ingredient4", "quantity4"],
        ["shops"] = ["item"], ["ginkgo"] = ["price", "item", "quantity"],
        ["drops"] = ["regularItem", "regularChance", "rareItem", "rareChance"],
        ["research"] = ["threshold1", "threshold2", "threshold3", "threshold4", "threshold5", "points", "bonusPoints"],
        ["ranks"] = ["requiredPoints"],
    };
    private readonly PlaCatalog catalog;
    private readonly PlaItemTable items;
    private readonly PlaItemTable baseItems;
    private readonly string[] names;
    private readonly Dictionary<string, (PlaFlatBuffer Buffer, Dictionary<string, int> References)> tables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlaFlatBuffer> originals = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Kind, string Id), int> originalRows = [];
    private readonly HashSet<int> supportedItems = [];
    private readonly Dictionary<string, List<PlaRelatedRecord>> dropUsers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> inventoryUses = new(StringComparer.Ordinal);
    internal static bool Supports(string kind) => Keys.ContainsKey(kind);

    internal PlaEconomyCatalog(PlaCatalog catalog)
    {
        this.catalog = catalog;
        items = new(catalog.Scope.Read(ItemsPath));
        baseItems = new(catalog.Scope.Base(ItemsPath));
        if (!items.HasSameIndex(baseItems)) throw new InvalidDataException("The PLA item index has an unsupported structural change.");
        names = catalog.Labels("itemname");
        foreach (var (kind, relative) in Paths)
        {
            var buffer = new PlaFlatBuffer(catalog.Scope.Read(relative));
            var original = new PlaFlatBuffer(catalog.Scope.Base(relative));
            originals.Add(kind, original);
            var refs = buffer.VectorReferences(buffer.Root, 0, 4096);
            var baseRefs = original.VectorReferences(original.Root, 0, 4096);
            if (refs.Length != baseRefs.Length) throw new InvalidDataException("The PLA economy table has an unsupported structural change.");
            var identities = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < refs.Length; i++)
            {
                var row = buffer.TableAtReference(refs[i]);
                var originalRow = original.TableAtReference(baseRefs[i]);
                var id = Identity(kind, buffer, row, i);
                if (id != Identity(kind, original, originalRow, i) || !identities.TryAdd(id, refs[i]))
                    throw new InvalidDataException("The PLA economy identities changed or are ambiguous.");
                originalRows.Add((kind, id), originalRow);
                foreach (var item in ItemReferences(kind, original, originalRow))
                    if (baseItems.IsInventoryItem(item)) supportedItems.Add(item);
            }
            tables.Add(kind, (buffer, identities));
        }
        foreach (var item in catalog.EvolutionItems()) if (baseItems.IsInventoryItem(item)) supportedItems.Add(item);
        ReadDropUsers();
        ReadInventoryUses();
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Hash(PlaFlatBuffer buffer, int row, int field) => buffer.Hash(row, field).ToString("X16", CultureInfo.InvariantCulture);
    private static string Identity(string kind, PlaFlatBuffer buffer, int row, int index) => kind switch
    {
        "crafting" => Number(buffer.Scalar(row, 0, 1)),
        "shops" => Hash(buffer, row, 0) + ":" + Hash(buffer, row, 1) + ":" + Number(index),
        "ginkgo" or "drops" => Hash(buffer, row, 0),
        "research" => Number(buffer.Scalar(row, 0, 4)) + ":" + Number(index),
        "ranks" => Number(buffer.Scalar(row, 0, 4)),
        _ => throw new InvalidDataException("The PLA economy editor is unsupported."),
    };
    internal IEnumerable<string> Ids(string kind) => kind == "items" ? supportedItems.Order().Select(id => Number(id)) : tables[kind].References.Keys;
    internal string PathFor(string kind, string id)
    {
        if (kind == "items") { _ = ItemId(id); return ItemsPath; }
        if (!tables.TryGetValue(kind, out var table) || !table.References.ContainsKey(id)) throw new InvalidDataException("The PLA economy record is unavailable.");
        return Paths[kind];
    }
    private int ItemId(string id) => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && supportedItems.Contains(value)
        && items.IsInventoryItem(value) ? value : throw new InvalidDataException("This PLA item is protected or unavailable.");
    private string ItemLabel(int id) => $"{id}: {(id >= 0 && id < names.Length ? names[id] : Number(id))}";
    private IReadOnlyList<PlaChoice> ItemChoices(bool empty = false) => (empty ? new[] { new PlaChoice("0", "0") } : [])
        .Concat(supportedItems.Order().Where(items.IsInventoryItem).Select(id => new PlaChoice(Number(id), ItemLabel(id)))).ToArray();
    internal PlaField[] Fields(string kind) => Keys[kind].Select(key => key switch
    {
        "item" or "resultItem" => new PlaField(key, key, Choices: ItemChoices()),
        "regularItem" or "rareItem" or "ingredient1" or "ingredient2" or "ingredient3" or "ingredient4" => new PlaField(key, key, Choices: ItemChoices(true)),
        "regularChance" or "rareChance" => new PlaField(key, key, Minimum: 0, Maximum: 100),
        "price" => new PlaField(key, key, Minimum: 0, Maximum: kind == "ginkgo" ? 65535 : 999999),
        "meritPrice" => new PlaField(key, key, Minimum: 0, Maximum: 65535),
        "maxQuantity" or "quantity" or "resultQuantity" => new PlaField(key, key, Minimum: 1, Maximum: 999),
        "quantity1" or "quantity2" or "quantity3" or "quantity4" => new PlaField(key, key, Minimum: 0, Maximum: 999),
        "requiredPoints" => new PlaField(key, key, Minimum: 0, Maximum: 999999),
        "points" or "bonusPoints" => new PlaField(key, key, Minimum: 0, Maximum: 255),
        _ => new PlaField(key, key, Minimum: 0, Maximum: 60000),
    }).ToArray();

    private static IEnumerable<int> ItemReferences(string kind, PlaFlatBuffer buffer, int row) => kind switch
    {
        "crafting" => new[] { 2, 4, 6, 8, 10 }.Select(field => checked((int)buffer.Scalar(row, field, 4))),
        "shops" => [checked((int)buffer.Scalar(row, 2, 4, true))],
        "ginkgo" => buffer.VectorReferences(row, 4, 16).Select(reference => checked((int)buffer.Scalar(buffer.TableAtReference(reference), 0))),
        "drops" => new[] { 1, 3 }.Select(field => checked((int)buffer.Scalar(row, field, 4, true))),
        _ => [],
    };

    private void ReadDropUsers()
    {
        var buffer = new PlaFlatBuffer(catalog.Scope.Read(MiscPath));
        foreach (var reference in buffer.VectorReferences(buffer.Root, 0, 4096))
        {
            var row = buffer.TableAtReference(reference);
            var pokemon = $"{buffer.Scalar(row, 0, 4)}:{buffer.Scalar(row, 1, 4)}";
            if (!catalog.Pokemon.ContainsKey(pokemon)) continue;
            foreach (var field in new[] { 7, 8 })
            {
                var key = Hash(buffer, row, field);
                if (!dropUsers.TryGetValue(key, out var uses)) dropUsers[key] = uses = [];
                var use = new PlaRelatedRecord(field == 7 ? "pokemon" : "alpha", pokemon, catalog.PokemonLabel(pokemon));
                if (!uses.Contains(use)) uses.Add(use);
            }
        }
    }
    private void ReadInventoryUses()
    {
        var buffer = new PlaFlatBuffer(catalog.Scope.Read(SelectionPath));
        foreach (var reference in buffer.VectorReferences(buffer.Root, 0, 64))
            foreach (var entry in buffer.VectorReferences(buffer.TableAtReference(reference), 2, 1024))
            {
                var row = buffer.TableAtReference(entry);
                var key = Hash(buffer, row, 0);
                if (buffer.Scalar(row, 1) > 0 && tables["ginkgo"].References.ContainsKey(key))
                    inventoryUses[key] = inventoryUses.GetValueOrDefault(key) + 1;
            }
    }

    internal PlaRecord Record(string kind, string id, byte[]? edited = null)
    {
        _ = PathFor(kind, id);
        if (kind == "items")
        {
            var item = ItemId(id);
            var table = edited is null ? items : new PlaItemTable(edited);
            return new(id, ItemLabel(item), Keys[kind].ToDictionary(key => key, key => Number(table.Value(item, key))));
        }
        var source = tables[kind];
        var buffer = edited is null ? source.Buffer : new PlaFlatBuffer(edited);
        var reference = source.References[id];
        var row = buffer.TableAtReference(reference);
        var original = originals[kind];
        var originalRow = originalRows[(kind, id)];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var protectedFields = new List<string>();
        var label = id;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (kind)
        {
            case "crafting":
                for (var i = 0; i < Keys[kind].Length; i++) values[Keys[kind][i]] = Number(buffer.Scalar(row, i + 2, 4));
                label = ItemLabel(checked((int)buffer.Scalar(row, 2, 4)));
                metadata["recipeItem"] = ItemLabel(checked((int)buffer.Scalar(row, 1, 4)));
                if (buffer.Scalar(row, 1, 4) != original.Scalar(originalRow, 1, 4)) protectedFields.AddRange(Keys[kind]);
                break;
            case "shops":
                values["item"] = Number(buffer.Scalar(row, 2, 4, true));
                label = ItemLabel(int.Parse(values["item"], CultureInfo.InvariantCulture));
                metadata["inventory"] = Hash(buffer, row, 0); metadata["condition"] = Hash(buffer, row, 1);
                if (!supportedItems.Contains(checked((int)original.Scalar(originalRow, 2, 4, true)))) protectedFields.AddRange(Keys[kind]);
                break;
            case "ginkgo":
                values["price"] = Number(buffer.Scalar(row, 2));
                var details = buffer.VectorReferences(row, 4, 16);
                var baseDetails = original.VectorReferences(originalRow, 4, 16);
                var detail = details.Length == 1 ? buffer.TableAtReference(details[0]) : 0;
                values["item"] = detail == 0 ? "0" : Number(buffer.Scalar(detail, 0));
                values["quantity"] = detail == 0 ? "0" : Number(buffer.Scalar(detail, 1));
                label = detail == 0 ? id : ItemLabel(int.Parse(values["item"], CultureInfo.InvariantCulture));
                var offerName = catalog.MessageLabel("script/icho_rare_shop", buffer.Hash(row, 1));
                if (!string.IsNullOrWhiteSpace(offerName)) label = offerName;
                var reward = buffer.String(row, 3);
                if (reward.Length > 0) metadata["rewardEvent"] = reward;
                else
                {
                    metadata["contents"] = string.Join(", ", details.Select(reference => {
                        var item = buffer.TableAtReference(reference);
                        return $"{ItemLabel(checked((int)buffer.Scalar(item, 0)))} × {buffer.Scalar(item, 1)}";
                    }));
                }
                metadata["inventory"] = id; metadata["selectionCount"] = Number(inventoryUses.GetValueOrDefault(id));
                if (details.Length != 1 || baseDetails.Length != 1 || original.String(originalRow, 3).Length != 0 || buffer.String(row, 3).Length != 0
                    || !supportedItems.Contains(checked((int)original.Scalar(original.TableAtReference(baseDetails[0]), 0))) || !inventoryUses.ContainsKey(id))
                    protectedFields.AddRange(Keys[kind]);
                break;
            case "drops":
                for (var i = 0; i < 4; i++) values[Keys[kind][i]] = Number(buffer.Scalar(row, i + 1, 4, true));
                label = ItemLabel(int.Parse(values["regularItem"], CultureInfo.InvariantCulture)) + " / " + ItemLabel(int.Parse(values["rareItem"], CultureInfo.InvariantCulture));
                if (!dropUsers.ContainsKey(id) || ItemReferences(kind, original, originalRow).All(item => item == 0)) protectedFields.AddRange(Keys[kind]);
                break;
            case "research":
                for (var i = 0; i < 7; i++) values[Keys[kind][i]] = Number(buffer.Scalar(row, i + 9, 4, true));
                var species = Number(buffer.Scalar(row, 0, 4));
                label = catalog.Pokemon.ContainsKey(species + ":0") ? catalog.PokemonLabel(species + ":0") : species;
                var task = buffer.Scalar(row, 1, 4, true);
                metadata["task"] = Number(task);
                if (task == 1) metadata["move"] = catalog.MoveLabel(checked((int)buffer.Scalar(row, 3, 4)));
                if (task == 2) metadata["moveType"] = catalog.TypeLabel(checked((int)buffer.Scalar(row, 4, 4)));
                var move = buffer.Scalar(row, 3, 4);
                var moveType = buffer.Scalar(row, 4, 4);
                var understood = task is 0 or 1 or 2 or 3 or 4 or 5 or 13 or 14 or 15 or 18 or 19
                    && buffer.Scalar(row, 2, 4) == 0 && buffer.Scalar(row, 5, 4) == 0 && buffer.Scalar(row, 16, 1) == 0
                    && (task == 1 ? catalog.Moves.ContainsKey(checked((int)move)) && moveType == 0
                        : task == 2 ? move == 0 && moveType < 18 : move == 0 && moveType == 0);
                if (!understood || !catalog.Pokemon.ContainsKey(species + ":0") || Enumerable.Range(6, 3).Any(field => buffer.Hash(row, field) != EmptyHash)
                    || Enumerable.Range(0, 9).Any(field => field is >= 6 ? buffer.Hash(row, field) != original.Hash(originalRow, field)
                        : buffer.Scalar(row, field, 4) != original.Scalar(originalRow, field, 4))) protectedFields.AddRange(Keys[kind]);
                else for (var i = 0; i < 5; i++) if (original.Scalar(originalRow, i + 9, 4) == 0) protectedFields.Add(Keys[kind][i]);
                break;
            case "ranks":
                values["requiredPoints"] = Number(buffer.Scalar(row, 1, 4, true));
                if (id == "0") protectedFields.AddRange(Keys[kind]);
                break;
        }
        return new(id, label, values) { ReadOnlyFields = protectedFields, Metadata = metadata,
            RelatedRecords = kind == "drops" ? dropUsers.GetValueOrDefault(id) ?? [] : [] };
    }

    internal IReadOnlyList<PlaRecord> Records(string kind, IReadOnlyDictionary<string, byte[]> edited, string? selectedId = null)
    {
        var records = Ids(kind).Where(id => selectedId is null || id == selectedId).Select(id => Record(kind, id, edited.GetValueOrDefault(PathFor(kind, id)))).ToArray();
        if (kind != "items") return records;
        var uses = new Dictionary<int, List<PlaRelatedRecord>>();
        foreach (var relatedKind in new[] { "crafting", "shops", "ginkgo", "drops" })
        {
            var buffer = new PlaFlatBuffer(edited.GetValueOrDefault(Paths[relatedKind]) ?? catalog.Scope.Read(Paths[relatedKind]));
            foreach (var (id, reference) in tables[relatedKind].References)
                foreach (var item in ItemReferences(relatedKind, buffer, buffer.TableAtReference(reference)).Distinct())
                {
                    if (!uses.TryGetValue(item, out var owners)) uses[item] = owners = [];
                    owners.Add(new(relatedKind, id, id));
                }
        }
        return records.Select(record => record with { RelatedRecords = uses.GetValueOrDefault(ItemId(record.Id)) ?? [] }).ToArray();
    }

    internal byte[] Edit(string kind, string id, byte[] bytes, IReadOnlyDictionary<string, string> requested)
    {
        var record = Record(kind, id, bytes);
        if (requested.Keys.Any(key => record.ReadOnlyFields.Contains(key))) throw new InvalidDataException("This PLA economy field is protected.");
        if (requested.All(pair => record.Values[pair.Key] == pair.Value)) return bytes;
        var values = record.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var (key, value) in requested) values[key] = value;
        long Value(string key) => long.Parse(values[key], CultureInfo.InvariantCulture);
        void Item(string key, bool empty = false)
        {
            var value = checked((int)Value(key));
            if (!(empty && value == 0) && (!supportedItems.Contains(value) || !items.IsInventoryItem(value)))
                throw new InvalidDataException("Choose a supported PLA inventory item.");
        }
        if (kind == "items")
        {
            var table = new PlaItemTable(bytes);
            foreach (var (key, value) in requested) table.Set(ItemId(id), key, long.Parse(value, CultureInfo.InvariantCulture));
            return table.ToArray();
        }
        if (kind is "shops" or "ginkgo") Item("item");
        if (kind == "crafting")
        {
            Item("resultItem");
            var ingredients = new List<long>();
            var empty = false;
            for (var i = 1; i <= 4; i++)
            {
                Item($"ingredient{i}", true);
                var item = Value($"ingredient{i}"); var count = Value($"quantity{i}");
                if ((item == 0) != (count == 0) || empty && item != 0 || item != 0 && (item == Value("resultItem") || ingredients.Contains(item)))
                    throw new InvalidDataException("PLA recipes require distinct ingredients in consecutive slots, with positive quantities and a different result item.");
                empty |= item == 0;
                if (item != 0) ingredients.Add(item);
            }
            if (ingredients.Count == 0) throw new InvalidDataException("A PLA recipe needs an ingredient.");
        }
        if (kind == "drops")
            foreach (var prefix in new[] { "regular", "rare" })
            {
                Item(prefix + "Item", true);
                if ((Value(prefix + "Item") == 0) != (Value(prefix + "Chance") == 0))
                    throw new InvalidDataException("A PLA drop requires an item and a positive chance, or an empty item and zero chance.");
            }
        if (kind == "research")
        {
            long previous = 0;
            for (var i = 1; i <= 5; i++)
            {
                var value = Value($"threshold{i}");
                if (record.ReadOnlyFields.Contains($"threshold{i}")) continue;
                if (value <= previous) throw new InvalidDataException("PLA research thresholds must be positive and strictly increasing.");
                previous = value;
            }
            if (Value("points") + Value("bonusPoints") is < 1 or > 255) throw new InvalidDataException("PLA research points and bonus points must total between 1 and 255.");
        }
        var buffer = new PlaFlatBuffer(bytes);
        var reference = tables[kind].References[id];
        if (kind == "ginkgo")
        {
            buffer.SetObjectScalars(reference, Widths[kind], new Dictionary<int, long> { [2] = Value("price") }, [3], new Dictionary<int, int[]>(), new Dictionary<int, int[]> { [4] = [2, 2] });
            var detail = buffer.VectorReferences(buffer.TableAtReference(reference), 4, 16).Single();
            buffer.SetObjectScalars(detail, [2, 2], new Dictionary<int, long> { [0] = Value("item"), [1] = Value("quantity") }, [], new Dictionary<int, int[]>());
        }
        else
        {
            var first = kind switch { "crafting" or "shops" => 2, "research" => 9, _ => 1 };
            var edits = requested.ToDictionary(pair => Array.IndexOf(Keys[kind], pair.Key) + first, pair => long.Parse(pair.Value, CultureInfo.InvariantCulture));
            buffer.SetObjectScalars(reference, Widths[kind], edits, [], new Dictionary<int, int[]>());
        }
        return buffer.ToArray();
    }

    internal void ValidateComposition(IReadOnlyDictionary<string, byte[]> files)
    {
        var effectiveItems = files.TryGetValue(ItemsPath, out var itemBytes) ? new PlaItemTable(itemBytes) : items;
        foreach (var kind in new[] { "crafting", "ginkgo" })
        {
            var buffer = new PlaFlatBuffer(files.GetValueOrDefault(Paths[kind]) ?? catalog.Scope.Read(Paths[kind]));
            foreach (var reference in buffer.VectorReferences(buffer.Root, 0, 4096))
            {
                var row = buffer.TableAtReference(reference);
                var pairs = kind == "crafting" ? new[] { 2, 4, 6, 8, 10 }.Select(field => (Item: buffer.Scalar(row, field, 4), Count: buffer.Scalar(row, field + 1, 4)))
                    : buffer.VectorReferences(row, 4, 16).Select(entry => (Item: buffer.Scalar(buffer.TableAtReference(entry), 0), Count: buffer.Scalar(buffer.TableAtReference(entry), 1)));
                foreach (var pair in pairs)
                    if (pair.Item != 0 && supportedItems.Contains((int)pair.Item) && pair.Count > effectiveItems.Value((int)pair.Item, "maxQuantity"))
                        throw new InvalidDataException("A PLA recipe or offer quantity exceeds the item's stack limit.");
            }
        }
        if (files.TryGetValue(Paths["crafting"], out var recipes))
        {
            var buffer = new PlaFlatBuffer(recipes);
            var graph = buffer.VectorReferences(buffer.Root, 0, 256).Select(buffer.TableAtReference)
                .GroupBy(row => buffer.Scalar(row, 2, 4)).ToDictionary(group => group.Key,
                    group => group.SelectMany(row => new[] { 4, 6, 8, 10 }.Select(field => buffer.Scalar(row, field, 4))).Where(item => item != 0).Distinct().ToArray());
            var complete = new HashSet<long>();
            var visiting = new HashSet<long>();
            void Visit(long item)
            {
                if (complete.Contains(item) || !graph.TryGetValue(item, out var ingredients)) return;
                if (!visiting.Add(item)) throw new InvalidDataException("The PLA recipes create a dependency cycle.");
                foreach (var ingredient in ingredients) Visit(ingredient);
                visiting.Remove(item); complete.Add(item);
            }
            foreach (var item in graph.Keys) Visit(item);
        }
        if (files.TryGetValue(Paths["ranks"], out var ranks))
        {
            var buffer = new PlaFlatBuffer(ranks);
            var rows = buffer.VectorReferences(buffer.Root, 0, 32).Select(buffer.TableAtReference).OrderBy(row => buffer.Scalar(row, 0, 4)).ToArray();
            long previous = -1;
            foreach (var row in rows)
            {
                var points = buffer.Scalar(row, 1, 4, true);
                if (points <= previous) throw new InvalidDataException("PLA rank requirements must increase with each rank.");
                previous = points;
            }
        }
    }
}
