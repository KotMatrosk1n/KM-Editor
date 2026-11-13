// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json;
using KM.Formats.PLA;

namespace KM.PLA;

internal sealed class PlaEventCatalog
{
    private const string Prefix = "bin/field/encount/";
    private static readonly PlaFlatBuffer.ObjectLayout Condition = Layout([8, 8, 4, 4, 4, 4, 4], [2, 3, 4, 5, 6]);
    private static readonly PlaFlatBuffer.ObjectLayout Wave = Layout([8, 8, 4, 4, 4, 4, 4, 8, 4], [2, 3, 4, 5, 6]);
    private static readonly Dictionary<string, Dictionary<string, int>> FieldIndices = new(StringComparer.Ordinal)
    {
        ["outbreaks"] = new() { ["outbreakChance"] = 1, ["totalMin"] = 5, ["totalMax"] = 6, ["shinyRolls"] = 7 },
        ["massiveOutbreaks"] = new() { ["outbreakChance"] = 2, ["locationsMin"] = 3, ["locationsMax"] = 4,
            ["rareTwoCount"] = 5, ["rareOneCount"] = 6, ["firstWaveMin"] = 7, ["firstWaveMax"] = 8,
            ["secondWaveMin"] = 9, ["secondWaveMax"] = 10, ["extraBonusChance"] = 11, ["starChance"] = 12,
            ["berryChance"] = 13, ["shinyRolls"] = 14 },
        ["outbreakGroups"] = new() { ["selectionWeight"] = 1 },
        ["outbreakWaves"] = new() { ["selectionWeight"] = 8 },
        ["outbreakDurations"] = new() { ["durationSeconds"] = 0, ["selectionWeight"] = 1 },
        ["distortionItems"] = new() { ["item"] = 0, ["selectionWeight"] = 1 },
    };
    private sealed record Entry(string Kind, string Id, string Path, int Top, int Child, int Index, string Label, bool Protected,
        Dictionary<string, string> Metadata, PlaRelatedRecord[] Related)
    {
        internal string Node => Child < 0 ? Top.ToString(CultureInfo.InvariantCulture) : $"{Top}/{Child}/{Index}";
    }
    private sealed record Resource(PlaFlatBuffer Base, PlaFlatBuffer Current, PlaFlatBuffer.ObjectLayout Layout, bool Archive);
    private readonly PlaCatalog catalog;
    private readonly Dictionary<string, Resource> resources = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Kind, string Id), Entry> entries = [];
    private readonly IReadOnlyList<PlaChoice> itemChoices;
    internal static bool Supports(string kind) => FieldIndices.ContainsKey(kind);
    private static PlaFlatBuffer.ObjectLayout Layout(int[] widths, int[]? strings = null, Dictionary<int, PlaFlatBuffer.ObjectLayout>? vectors = null,
        int[]? stringVectors = null, int[]? intVectors = null) => new(widths, strings ?? [], new Dictionary<int, PlaFlatBuffer.ObjectLayout>(), vectors ?? [], stringVectors, intVectors);
    private static int Int(PlaFlatBuffer b, int row, int field) => checked((int)b.Scalar(row, field, 4, true));
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Hex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);
    private static int[] Rows(PlaFlatBuffer b) => b.VectorReferences(b.Root, 0, 4096);
    private static bool EmptyCondition(PlaFlatBuffer b, int row) => b.Hash(row, 0) == PlaEventLinks.EmptyHash && b.Hash(row, 1) == PlaEventLinks.EmptyHash
        && Enumerable.Range(2, 5).All(field => b.String(row, field).Length == 0);

    internal PlaEventCatalog(PlaCatalog catalog)
    {
        this.catalog = catalog;
        catalog.Wild.ValidateEventSources();
        var links = catalog.Wild.EventLinks;
        itemChoices = catalog.Economy.Fields("crafting").Single(field => field.Key == "resultItem").Choices!;
        var ordinary = AddResource(Prefix + "huge_outbreak.bin", Layout([8, 4, 4, 4, 4, 4, 4, 4], [3], new() { [2] = Condition }));
        var massive = AddResource(Prefix + "new_huge_outbreak_lottery.bin", Layout([8, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4], [1], stringVectors: [15], intVectors: [16, 17]));
        var regions = new Dictionary<ulong, string>();
        for (var i = 1; i <= 5; i++) regions[PlaFieldArchive.HashPath("ha_area0" + i)] = "ha_area0" + i;
        void Regions(string kind, string path, PlaFlatBuffer b)
        {
            var rows = Rows(b);
            for (var index = 0; index < rows.Length; index++)
            {
                var row = b.TableAtReference(rows[index]); var hash = b.Hash(row, 0);
                var area = regions.GetValueOrDefault(hash) ?? Hex(hash);
                var known = regions.ContainsKey(hash);
                Add(kind, path, index, -1, 0, area, !known, new() { ["area"] = area, ["areaName"] = catalog.AreaLabel(area) });
            }
        }
        Regions("outbreaks", Prefix + "huge_outbreak.bin", ordinary);
        Regions("massiveOutbreaks", Prefix + "new_huge_outbreak_lottery.bin", massive);

        var groups = AddResource(PlaEventLinks.GroupPath, Layout([8, 4, 8], vectors: new() { [1] = Wave }));
        var groupInfo = new Dictionary<ulong, (string Label, bool Protected, string Initial)>();
        var groupRows = Rows(groups);
        for (var index = 0; index < groupRows.Length; index++)
        {
            var row = groups.TableAtReference(groupRows[index]); var hash = groups.Hash(row, 0); var initial = groups.Hash(row, 2);
            var follows = groups.VectorReferences(row, 1, 64);
            var guarded = !links.Groups.ContainsKey(hash) || follows.Any(reference => !EmptyCondition(groups, groups.TableAtReference(reference)));
            var label = links.Groups.TryGetValue(hash, out var paths)
                ? string.Join("; ", paths.Order(StringComparer.Ordinal).Select(path => catalog.Wild.EncounterLabel(path, initial)).Distinct(StringComparer.Ordinal)) : Hex(hash);
            if (!groupInfo.TryAdd(hash, (label, guarded, Hex(initial)))) throw new InvalidDataException("The PLA outbreak group identity is ambiguous.");
            for (var child = 0; child < follows.Length; child++)
            {
                var detail = groups.TableAtReference(follows[child]); var table = groups.Hash(detail, 7);
                var followLabel = paths is { Count: > 0 }
                    ? string.Join("; ", paths.Order(StringComparer.Ordinal).Select(path => catalog.Wild.EncounterLabel(path, table)).Distinct(StringComparer.Ordinal)) : Hex(table);
                Add("outbreakWaves", PlaEventLinks.GroupPath, index, 1, child, label + " / " + followLabel, guarded,
                    new() { ["group"] = Hex(hash), ["initialTable"] = Hex(initial), ["table"] = Hex(table), ["wave"] = "2" },
                    [new("initial", Hex(initial), label), new("followup", Hex(table), followLabel)]);
            }
        }
        var selection = AddResource(PlaEventLinks.LotteryPath, Layout([8, 4, 4, 4], vectors: new() { [1] = Layout([8, 4]), [2] = Layout([8, 4]), [3] = Layout([8, 4]) }));
        var lotteryRows = Rows(selection);
        for (var index = 0; index < lotteryRows.Length; index++)
        {
            var row = selection.TableAtReference(lotteryRows[index]); var hash = selection.Hash(row, 0);
            for (var category = 1; category <= 3; category++)
            {
                var children = selection.VectorReferences(row, category, 1024);
                for (var child = 0; child < children.Length; child++)
                {
                    var detail = selection.TableAtReference(children[child]); var group = selection.Hash(detail, 0);
                    var empty = group == PlaEventLinks.EmptyHash; var info = groupInfo.GetValueOrDefault(group);
                    Add("outbreakGroups", PlaEventLinks.LotteryPath, index, category, child, empty ? "Empty selection" : info.Label ?? Hex(group),
                        !links.Lotteries.Contains(hash) || !empty && (info.Label is null || info.Protected),
                        new() { ["lottery"] = Hex(hash), ["category"] = Number(category), ["group"] = Hex(group), ["empty"] = empty ? "1" : "0" },
                        empty ? [] : [new("initial", info.Initial ?? "", info.Label ?? Hex(group))]);
                }
            }
        }
        var duration = AddResource(Prefix + "new_huge_outbreak_time_limit.bin", Layout([4, 4, 4, 4], intVectors: [2, 3]));
        for (var index = 0; index < Rows(duration).Length; index++)
            Add("outbreakDurations", Prefix + "new_huge_outbreak_time_limit.bin", index, -1, 0, Number(index + 1), false, []);
        foreach (var (path, tables) in links.Items)
        {
            var items = AddResource(path, Layout([8, 4], vectors: new() { [1] = Layout([4, 4]) }), true);
            var rows = Rows(items);
            for (var index = 0; index < rows.Length; index++)
            {
                var row = items.TableAtReference(rows[index]); var hash = items.Hash(row, 0); var slots = items.VectorReferences(row, 1, 100);
                for (var child = 0; child < slots.Length; child++)
                {
                    var item = Number(Int(items, items.TableAtReference(slots[child]), 0));
                    var label = itemChoices.FirstOrDefault(choice => choice.Value == item)?.Label;
                    Add("distortionItems", path, index, 1, child, label ?? item, !tables.Contains(hash) || label is null,
                        new() { ["area"] = path.Split('/')[4], ["areaName"] = catalog.AreaLabel(path.Split('/')[4]), ["table"] = Hex(hash), ["slot"] = Number(child + 1) });
                }
            }
        }
        foreach (var (path, resource) in resources) ValidatePreservation(path, resource.Current);
    }

    private PlaFlatBuffer AddResource(string path, PlaFlatBuffer.ObjectLayout layout, bool archive = false)
    {
        var original = new PlaFlatBuffer(archive ? catalog.Wild.BaseMember(path) : catalog.Scope.Base(path));
        var current = new PlaFlatBuffer(archive ? catalog.Wild.ReadMember(path) : catalog.Scope.Read(path));
        resources.Add(path, new(original, current, layout, archive));
        return original;
    }
    private void Add(string kind, string path, int top, int child, int index, string label, bool guarded,
        Dictionary<string, string> metadata, PlaRelatedRecord[]? related = null)
    {
        var id = (resources[path].Archive ? path.Split('/')[4] + ":" : "") + $"{top}:{child}:{index}";
        metadata["protected"] = guarded ? "1" : "0";
        if (!entries.TryAdd((kind, id), new(kind, id, path, top, child, index, label, guarded, metadata, related ?? [])))
            throw new InvalidDataException("The PLA event record identity is ambiguous.");
    }
    private Entry Get(string kind, string id) => entries.GetValueOrDefault((kind, id)) ?? throw new InvalidDataException("The PLA event record is unavailable.");
    internal string PathFor(string kind, string id) => resources[Get(kind, id).Path].Archive ? PlaWildCatalog.ArchivePath : Get(kind, id).Path;
    internal PlaField[] Fields(string kind) => FieldIndices[kind].Keys.Select(key => key switch
    {
        "item" => new PlaField(key, key, Choices: itemChoices),
        "selectionWeight" => new PlaField(key, key, Minimum: 0, Maximum: 10000),
        "shinyRolls" => new PlaField(key, key, Minimum: 0, Maximum: 32),
        "durationSeconds" => new PlaField(key, key, Minimum: 60, Maximum: 3600),
        _ when key.EndsWith("Chance", StringComparison.Ordinal) => new PlaField(key, key, Minimum: 0, Maximum: 100),
        _ => new PlaField(key, key, Minimum: key is "rareOneCount" or "rareTwoCount" ? 0 : 1, Maximum: key.StartsWith("locations", StringComparison.Ordinal) || key.StartsWith("rare", StringComparison.Ordinal) ? 32 : 100),
    }).ToArray();
    private static int Reference(Entry entry, PlaFlatBuffer b)
    {
        var rows = Rows(b);
        if (entry.Top >= rows.Length) throw new InvalidDataException("A PLA event record is missing.");
        var reference = rows[entry.Top];
        if (entry.Child < 0) return reference;
        var children = b.VectorReferences(b.TableAtReference(reference), entry.Child, 1024);
        if (entry.Index >= children.Length) throw new InvalidDataException("A PLA event selection is missing.");
        return children[entry.Index];
    }
    private PlaRecord Record(Entry entry, PlaFlatBuffer b)
    {
        var row = b.TableAtReference(Reference(entry, b));
        var values = FieldIndices[entry.Kind].ToDictionary(pair => pair.Key, pair => Number(Int(b, row, pair.Value)), StringComparer.Ordinal);
        var label = entry.Kind == "distortionItems" ? itemChoices.FirstOrDefault(choice => choice.Value == values["item"])?.Label ?? entry.Label : entry.Label;
        return new(entry.Id, label, values) { Metadata = entry.Metadata, RelatedRecords = entry.Related,
            ReadOnlyFields = entry.Protected ? FieldIndices[entry.Kind].Keys.ToArray() : [] };
    }
    internal PlaRecord Record(string kind, string id, byte[]? edited)
    {
        var entry = Get(kind, id); var resource = resources[entry.Path];
        var b = edited is null ? resource.Current : new PlaFlatBuffer(resource.Archive ? new PlaFieldArchive(edited).Read(entry.Path, catalog.Scope.Compression) : edited);
        return Record(entry, b);
    }
    internal IReadOnlyList<PlaRecord> Records(string kind, IReadOnlyDictionary<string, byte[]> files, string? selectedId = null)
    {
        var cache = new Dictionary<string, PlaFlatBuffer>(StringComparer.Ordinal);
        var archive = files.TryGetValue(PlaWildCatalog.ArchivePath, out var pending) ? new PlaFieldArchive(pending) : null;
        var labels = new Dictionary<(string Path, ulong Table), string>();
        string EncounterName(string path, string table)
        {
            var hash = ulong.Parse(table, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (labels.TryGetValue((path, hash), out var label)) return label;
            if (!cache.TryGetValue(path, out var buffer)) cache[path] = buffer = new(archive!.Read(path, catalog.Scope.Compression));
            return labels[(path, hash)] = catalog.Wild.EncounterLabel(buffer, hash);
        }
        return entries.Values.Where(entry => entry.Kind == kind && (selectedId is null || entry.Id == selectedId)).Select(entry =>
        {
            if (!cache.TryGetValue(entry.Path, out var b))
            {
                var resource = resources[entry.Path]; var data = files.GetValueOrDefault(PathFor(kind, entry.Id));
                cache[entry.Path] = b = data is null ? resource.Current : new(resource.Archive ? new PlaFieldArchive(data).Read(entry.Path, catalog.Scope.Compression) : data);
            }
            var record = Record(entry, b);
            if (archive is not null && kind is "outbreakGroups" or "outbreakWaves" && entry.Related.Length > 0
                && catalog.Wild.EventLinks.Groups.TryGetValue(ulong.Parse(entry.Metadata["group"], NumberStyles.HexNumber, CultureInfo.InvariantCulture), out var paths))
            {
                var related = entry.Related.Select(link => link with { Label = string.Join("; ", paths.Order(StringComparer.Ordinal)
                    .Select(path => EncounterName(path, link.Id)).Distinct(StringComparer.Ordinal)) }).ToArray();
                record = record with { Label = string.Join(" / ", related.Select(link => link.Label)), RelatedRecords = related };
            }
            return record;
        }).ToArray();
    }

    private void ValidatePreservation(string path, PlaFlatBuffer current)
    {
        var resource = resources[path];
        var allowed = entries.Values.Where(entry => entry.Path == path && !entry.Protected).ToDictionary(entry => entry.Node, entry => FieldIndices[entry.Kind].Values.ToHashSet());
        string Signature(PlaFlatBuffer b)
        {
            var values = new List<string>();
            void Visit(int reference, PlaFlatBuffer.ObjectLayout layout, string node)
            {
                var row = b.TableAtReference(reference); var editable = allowed.GetValueOrDefault(node) ?? [];
                for (var field = 0; field < layout.Widths.Length; field++)
                {
                    if (editable.Contains(field)) continue;
                    if (layout.Strings.Contains(field)) values.Add(b.String(row, field));
                    else if ((layout.StringVectors ?? []).Contains(field)) values.Add(JsonSerializer.Serialize(b.StringVector(row, field, 4096)));
                    else if ((layout.IntVectors ?? []).Contains(field)) values.Add(JsonSerializer.Serialize(b.Int32Vector(row, field)));
                    else if (layout.Vectors.TryGetValue(field, out var child))
                    {
                        var refs = b.VectorReferences(row, field, 4096); values.Add(Number(refs.Length));
                        for (var i = 0; i < refs.Length; i++) Visit(refs[i], child, node + "/" + field + "/" + i);
                    }
                    else values.Add(layout.Widths[field] == 8 ? Hex(b.Hash(row, field)) : Number(b.Scalar(row, field, layout.Widths[field])));
                }
            }
            var rows = Rows(b); values.Add(Number(rows.Length));
            for (var i = 0; i < rows.Length; i++) Visit(rows[i], resource.Layout, Number(i));
            return JsonSerializer.Serialize(values);
        }
        if (Signature(resource.Base) != Signature(current)) throw new InvalidDataException("PLA event identities, conditions, and protected data must be preserved.");
    }

    internal void Edit(IReadOnlyDictionary<(string Kind, string Id), Dictionary<string, string>> edits,
        Dictionary<string, byte[]> files, Dictionary<string, byte[]> members)
    {
        foreach (var group in edits.GroupBy(pair => Get(pair.Key.Kind, pair.Key.Id).Path))
        {
            var resource = resources[group.Key]; var b = new PlaFlatBuffer(resource.Current.ToArray());
            foreach (var top in group.GroupBy(pair => Get(pair.Key.Kind, pair.Key.Id).Top))
            {
                var changes = new Dictionary<int, IReadOnlyDictionary<int, long>>();
                foreach (var (key, values) in top)
                {
                    var entry = Get(key.Kind, key.Id);
                    if (entry.Protected) throw new InvalidDataException("This PLA event record is protected.");
                    var current = Record(entry, b);
                    var effective = values.Where(pair => current.Values[pair.Key] != pair.Value).ToDictionary(pair => FieldIndices[key.Kind][pair.Key], pair => long.Parse(pair.Value, CultureInfo.InvariantCulture));
                    if (effective.Count > 0) changes.Add(Reference(entry, b), effective);
                }
                if (changes.Count > 0) b.EditObjectTree(Rows(b)[top.Key], resource.Layout, changes);
            }
            ValidatePreservation(group.Key, b);
            ValidateValues(group.Key, b);
            if (resource.Archive) members.Add(group.Key, b.ToArray()); else files[group.Key] = b.ToArray();
        }
    }
    private void ValidateValues(string path, PlaFlatBuffer b)
    {
        foreach (var entry in entries.Values.Where(entry => entry.Path == path && !entry.Protected))
        {
            var record = Record(entry, b); var row = b.TableAtReference(Reference(entry, b));
            int Value(string field) => int.Parse(record.Values[field], CultureInfo.InvariantCulture);
            foreach (var field in Fields(entry.Kind))
            {
                var value = Value(field.Key);
                if (field.Minimum is { } min && value < min || field.Maximum is { } max && value > max || field.Choices is { } choices && !choices.Any(choice => choice.Value == Number(value)))
                    throw new InvalidDataException("A PLA event value is outside its supported range.");
            }
            foreach (var pair in new[] { ("totalMin", "totalMax"), ("locationsMin", "locationsMax"), ("firstWaveMin", "firstWaveMax"), ("secondWaveMin", "secondWaveMax") })
                if (record.Values.ContainsKey(pair.Item1) && Value(pair.Item1) > Value(pair.Item2)) throw new InvalidDataException("PLA event minimum counts cannot exceed maximum counts.");
            if (entry.Kind == "massiveOutbreaks" && (Value("locationsMax") > b.StringVector(row, 15).Length || Value("rareOneCount") + Value("rareTwoCount") > Value("locationsMin")))
                throw new InvalidDataException("PLA outbreak location and rare group counts exceed the available locations.");
            if (entry.Kind == "outbreakDurations" && (b.Int32Vector(row, 2).Length != b.Int32Vector(row, 3).Length || b.Int32Vector(row, 2).Any(time => time < 0 || time >= Value("durationSeconds"))))
                throw new InvalidDataException("PLA outbreak duration must exceed every preserved warning time.");
        }
        foreach (var group in entries.Values.Where(entry => entry.Path == path && FieldIndices[entry.Kind].ContainsKey("selectionWeight"))
            .GroupBy(entry => entry.Child < 0 ? "all" : $"{entry.Top}/{entry.Child}"))
            if (!group.Any(entry => Int(b, b.TableAtReference(Reference(entry, b)), FieldIndices[entry.Kind]["selectionWeight"]) > 0))
                throw new InvalidDataException("A PLA event selection must retain a positive weight.");
    }
}
