// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Formats.PLA;

namespace KM.PLA;

internal sealed class PlaWildCatalog
{
    internal const string ArchivePath = "bin/archive/field/resident_release.gfpak";
    private const string AreaPath = "bin/field/resident/AreaSettings.bin";
    private const ulong EmptyHash = 0xCBF29CE484222645;
    private static readonly string[] Efforts = ["effortHp", "effortAttack", "effortDefense", "effortSpecialAttack", "effortSpecialDefense", "effortSpeed"];
    private static readonly string[] Multipliers = ["dawn", "day", "dusk", "night", "sunny", "cloudy", "rain", "snow", "drought", "fog", "rainstorm", "snowstorm"];
    private static readonly HashSet<int> SpecialSpecies = [201, 480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493, 641, 642, 645, 905];
    private static readonly HashSet<string> BossForms = ["900:1", "101:2", "59:2", "549:2", "713:2"];
    private static readonly int[] SlotWidths = Enumerable.Range(0, 47).Select(i => i == 1 ? 8 : i is 9 or 10 or 11 or 12 or 28 or 33 or 38 or 39 or 40 or 41 or 44 ? 1 : 4).ToArray();
    private static readonly int[] SpawnWidths = [8, 8, 4, 4, 4, 4, 4, 4, 4, 4, 1, 1, 1, 8, 4, 4, 4, 4, 4, 4, 4, 4, 4];
    private static readonly PlaFlatBuffer.ObjectLayout EligibilityLayout = new(
        Enumerable.Range(0, 19).Select(i => i < 2 ? 8 : 4).ToArray(), [2, 3, 4, 5, 6], new Dictionary<int, PlaFlatBuffer.ObjectLayout>(), new Dictionary<int, PlaFlatBuffer.ObjectLayout>());
    private static readonly PlaFlatBuffer.ObjectLayout SlotLayout = new(SlotWidths, [26, 27, 42],
        new Dictionary<int, PlaFlatBuffer.ObjectLayout> { [45] = EligibilityLayout, [46] = new([1, 1, 1, 1], [], new Dictionary<int, PlaFlatBuffer.ObjectLayout>(), new Dictionary<int, PlaFlatBuffer.ObjectLayout>()) },
        new Dictionary<int, PlaFlatBuffer.ObjectLayout>());
    private static readonly PlaFlatBuffer.ObjectLayout TableLayout = new([8, 4, 4, 4], [], new Dictionary<int, PlaFlatBuffer.ObjectLayout>(),
        new Dictionary<int, PlaFlatBuffer.ObjectLayout> { [3] = SlotLayout });
    private sealed record Entry(string Path, string Area, ulong Table, int Slot, ulong SlotId, bool Protected, string Alpha, string[] Consumers);
    private readonly PlaCatalog catalog;
    private readonly PlaFieldArchive original;
    private readonly PlaFieldArchive current;
    private readonly Dictionary<string, PlaFlatBuffer> buffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> encounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> outbreakEncounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> distortionEncounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> distortionSpawns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> spawns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float[]> defaults = new(StringComparer.Ordinal);
    private readonly Dictionary<PlaFlatBuffer, Dictionary<ulong, int>> tableCache = [];
    private readonly Dictionary<(string Area, string Table), string> encounterPaths = [];
    internal static bool IsEncounter(string kind) => kind is "encounters" or "outbreakEncounters" or "distortionEncounters";
    internal static bool IsSpawn(string kind) => kind is "spawns" or "distortionSpawns";
    internal static bool Supports(string kind) => IsEncounter(kind) || IsSpawn(kind);
    internal static string PhysicalKind(string kind) => IsEncounter(kind) ? "encounters" : kind;
    internal PlaEventLinks EventLinks { get; }
    private PlaCompressionRuntime Runtime => catalog.Scope.Compression;
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Hex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);
    private static int Int(PlaFlatBuffer b, int row, int index) => checked((int)b.Scalar(row, index, 4, true));
    private static int Child(PlaFlatBuffer b, int row, int index) => b.TableAtReference(b.Field(row, index, 4));
    private static string Identity(PlaFlatBuffer b, int row) => $"{Int(b, row, 0)}:{Int(b, row, 3)}";
    private static bool OrdinaryCondition(PlaFlatBuffer b, int row, int hashIndex, int argIndex) =>
        b.Hash(row, hashIndex) == EmptyHash && b.Hash(row, hashIndex + 1) == EmptyHash
        && Enumerable.Range(argIndex, 5).All(i => b.String(row, i).Length == 0);
    private static bool QualifiedCondition(PlaFlatBuffer b, int row) => OrdinaryCondition(b, row, 0, 2)
        || b.Hash(row, 0) == 0x3DDD5FEB4A68A149 && b.Hash(row, 1) == 0xB763CBE88B6F844F
        && Enumerable.Range(1, 5).Any(i => b.String(row, 2) == $"FSYS_NS_0{i}_CLEARED")
        && Enumerable.Range(3, 4).All(i => b.String(row, i).Length == 0);
    private static bool AllowedIdentity(string identity) => !SpecialSpecies.Contains(int.Parse(identity.Split(':')[0], CultureInfo.InvariantCulture)) && !BossForms.Contains(identity);

    internal PlaWildCatalog(PlaCatalog catalog)
    {
        this.catalog = catalog;
        _ = Runtime;
        original = new(catalog.Scope.Base(ArchivePath));
        current = new(catalog.Scope.Read(ArchivePath));
        if (!original.HasSameDirectory(current)) throw new InvalidDataException("PLA field archive membership and directory identities must be preserved.");
        EventLinks = new(catalog, original);
        var areas = new PlaFlatBuffer(original.Read(AreaPath, Runtime));
        if (!original.Read(AreaPath, Runtime).AsSpan().SequenceEqual(current.Read(AreaPath, Runtime)))
            throw new InvalidDataException("The PLA area resource links are unsupported. Restore the original area directory.");
        var areaRows = areas.VectorReferences(areas.Root, 0, 256).Select(areas.TableAtReference).ToArray();
        var consumers = new Dictionary<(string Path, ulong Table), HashSet<string>>();
        var excluded = new HashSet<(string Path, ulong Table)>();
        var spawnSources = new Dictionary<string, (string Area, string Encounter)>(StringComparer.Ordinal);
        foreach (var row in areaRows)
        {
            var area = areas.String(row, 0);
            var encounter = areas.String(row, 18);
            var spawn = areas.String(row, 15);
            if (encounter.Length == 0 || spawn.Length == 0 || !original.Contains(encounter) || !original.Contains(spawn)) continue;
            if (!spawnSources.TryAdd(spawn, (area, encounter)) && spawnSources[spawn].Encounter != encounter)
                throw new InvalidDataException("The PLA spawner resource has ambiguous encounter links.");
            foreach (var field in new[] { 15, 35 })
            {
                var path = areas.String(row, field);
                if (path.Length == 0 || !original.Contains(path)) continue;
                var b = new PlaFlatBuffer(original.Read(path, Runtime));
                foreach (var reference in b.VectorReferences(b.Root, 0, 4096))
                {
                    var spawner = b.TableAtReference(reference);
                    var isOrdinary = field == 15 && b.Scalar(spawner, 10, 1) == 0;
                    foreach (var link in b.VectorReferences(spawner, 20, 64).Select(b.TableAtReference))
                    {
                        var key = (encounter, b.Hash(link, 0));
                        if (!isOrdinary) { excluded.Add(key); continue; }
                        if (!consumers.TryGetValue(key, out var names)) consumers[key] = names = new(StringComparer.Ordinal);
                        names.Add(area + "/" + Hex(b.Hash(spawner, 0)));
                    }
                }
            }
        }
        foreach (var path in spawnSources.Values.Select(value => value.Encounter).Distinct(StringComparer.Ordinal))
        {
            var b = new PlaFlatBuffer(original.Read(path, Runtime));
            var now = Buffer(path);
            var baseRows = Tables(b); var currentRows = Tables(now);
            if (!baseRows.Keys.SequenceEqual(currentRows.Keys)) throw new InvalidDataException("PLA encounter table order and identities must be preserved.");
            var area = path.Split('/')[2];
            foreach (var (hash, reference) in baseRows)
            {
                encounterPaths.Add((area, Hex(hash)), path);
                var table = b.TableAtReference(reference);
                var slots = b.VectorReferences(table, 3, 100);
                var currentSlots = now.VectorReferences(now.TableAtReference(currentRows[hash]), 3, 100);
                if (slots.Length != currentSlots.Length) throw new InvalidDataException("PLA encounter slot membership must be preserved.");
                for (var i = 0; i < slots.Length; i++)
                {
                    var slot = b.TableAtReference(slots[i]); var nowSlot = now.TableAtReference(currentSlots[i]);
                    if (b.Hash(slot, 1) != now.Hash(nowSlot, 1)) throw new InvalidDataException("PLA encounter slot order and identities must be preserved.");
                    var traits = Child(b, slot, 46);
                    var alphaFlags = Enumerable.Range(0, 4).Select(f => b.Scalar(traits, f, 1)).ToArray();
                    var alpha = alphaFlags.SequenceEqual(new long[] { 0, 0, 0, 0 }) ? "ordinary"
                        : alphaFlags.SequenceEqual(new long[] { 1, 0, 1, 1 }) ? "alpha"
                        : alphaFlags.SequenceEqual(new long[] { 1, 1, 1, 1 }) ? "fixedAlpha" : "protected";
                    var identity = Identity(b, slot);
                    var baseMin = Int(b, slot, 30) == 0 ? Int(b, table, 1) : Int(b, slot, 30);
                    var baseMax = Int(b, slot, 31) == 0 ? Int(b, table, 2) : Int(b, slot, 31);
                    var traitsProtected = alpha == "protected"
                        || baseMin < 1 || baseMax > 100 || baseMin > baseMax
                        || !catalog.Pokemon.ContainsKey(identity) || !AllowedIdentity(identity) || !QualifiedCondition(b, Child(b, slot, 45))
                        || b.Scalar(slot, 33, 1) != 0 || b.Scalar(slot, 44, 1) != 0 || b.Scalar(slot, 11, 1) != 0
                        || b.Scalar(slot, 4, 4) != 0 || !PreservedTraits(b, slot, now, nowSlot);
                    var id = area + ":" + Hex(hash) + ":" + i;
                    var entry = new Entry(path, area, hash, i, b.Hash(slot, 1), traitsProtected || !consumers.ContainsKey((path, hash)) || excluded.Contains((path, hash)), alpha,
                        consumers.GetValueOrDefault((path, hash))?.Order(StringComparer.Ordinal).ToArray() ?? []);
                    encounters.Add(id, entry);
                    foreach (var (links, records) in new[] { (EventLinks.Outbreaks, outbreakEncounters), (EventLinks.Distortions, distortionEncounters) })
                        if (links.TryGetValue((path, hash), out var names))
                            records.Add(id, entry with { Protected = traitsProtected, Consumers = names.Union(entry.Consumers).Order(StringComparer.Ordinal).ToArray() });
                }
            }
        }
        foreach (var (path, source) in spawnSources.Concat(EventLinks.DistortionSpawns))
        {
            var b = new PlaFlatBuffer(original.Read(path, Runtime)); var now = Buffer(path);
            var baseRows = b.VectorReferences(b.Root, 0, 4096); var currentRows = now.VectorReferences(now.Root, 0, 4096);
            if (!baseRows.Select(r => b.Hash(b.TableAtReference(r), 0)).SequenceEqual(currentRows.Select(r => now.Hash(now.TableAtReference(r), 0))))
                throw new InvalidDataException("PLA spawner order and identities must be preserved.");
            var aliases = baseRows.GroupBy(b.TableAtReference).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            var duplicateIds = baseRows.GroupBy(r => b.Hash(b.TableAtReference(r), 0)).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            var compatible = CountOnlyCompatible(b, now, baseRows, currentRows);
            for (var index = 0; index < baseRows.Length; index++)
            {
                var reference = baseRows[index]; var hash = b.Hash(b.TableAtReference(reference), 0);
                var row = b.TableAtReference(reference);
                var links = b.VectorReferences(row, 20, 64).Select(b.TableAtReference).ToArray();
                var keys = links.Select(link => b.Hash(link, 0)).ToArray();
                var records = EventLinks.DistortionSpawns.ContainsKey(path) ? distortionSpawns : spawns;
                var slots = records == distortionSpawns ? distortionEncounters : encounters;
                var isProtected = b.Scalar(row, 10, 1) != 0 || Int(b, row, 16) != 0 || !EmptySpawnLinks(b, row)
                    || b.StringVector(row, 22).Any(value => value.Length != 0) || aliases.Contains(row) || duplicateIds.Contains(hash) || keys.Length == 0
                    || links.Any(link => !OrdinaryCondition(b, link, 1, 3))
                    || keys.Any(key => !slots.Values.Any(entry => entry.Path == source.Encounter && entry.Table == key && !entry.Protected))
                    || b.Field(row, 7, 4) == 0 || b.Field(row, 8, 4) == 0 || Int(b, row, 7) < 1 || Int(b, row, 8) > 4 || !compatible;
                records.Add(source.Area + ":" + Hex(hash) + ":" + index, new(path, source.Area, hash, index, 0, isProtected, "", keys.Select(Hex).ToArray()));
            }
        }
        var multipliers = new PlaFlatBuffer(catalog.Scope.Read("bin/pokemon/data/poke_encount.bin"));
        foreach (var row in multipliers.VectorReferences(multipliers.Root, 0, 4096).Select(multipliers.TableAtReference))
        {
            var key = $"{Int(multipliers, row, 0)}:{Int(multipliers, row, 1)}";
            if (!defaults.TryAdd(key, Enumerable.Range(2, 12).Select(i => multipliers.Float(row, i)).ToArray()))
                throw new InvalidDataException("The PLA species eligibility identity is ambiguous.");
        }
    }

    private static bool EmptySpawnLinks(PlaFlatBuffer b, int row)
    {
        return b.VectorReferences(row, 21, 64).Select(b.TableAtReference).All(link =>
            Enumerable.Range(0, 6).All(i => b.Hash(link, i) == EmptyHash)
            && b.String(link, 7).Length == 0 && b.Float(link, 8) == 0 && Int(b, link, 11) == 0
            && new[] { 6, 9, 10 }.All(i => Enumerable.Range(0, 3).All(axis => b.Float(Child(b, link, i), axis) == 0)));
    }
    private static bool PreservedTraits(PlaFlatBuffer source, int row, PlaFlatBuffer current, int now)
    {
        var editable = new HashSet<int> { 0, 3, 13, 14, 15, 16, 17, 18, 25, 29, 30, 31 };
        for (var i = 0; i < 45; i++)
        {
            if (editable.Contains(i) || i == 1) continue;
            if (i is 26 or 27 or 42)
            {
                if (source.String(row, i) != current.String(now, i)) return false;
            }
            else if (source.Scalar(row, i, SlotWidths[i]) != current.Scalar(now, i, SlotWidths[i])) return false;
        }
        var eligibility = Child(source, row, 45); var currentEligibility = Child(current, now, 45);
        if (Enumerable.Range(0, 2).Any(i => source.Hash(eligibility, i) != current.Hash(currentEligibility, i))
            || Enumerable.Range(2, 5).Any(i => source.String(eligibility, i) != current.String(currentEligibility, i))) return false;
        var alpha = Child(source, row, 46); var currentAlpha = Child(current, now, 46);
        return Enumerable.Range(0, 4).All(i => source.Scalar(alpha, i, 1) == current.Scalar(currentAlpha, i, 1));
    }
    private static bool CountOnlyCompatible(PlaFlatBuffer source, PlaFlatBuffer current, int[] rows, int[] currentRows)
    {
        var left = source.ToArray(); var right = current.ToArray();
        if (left.Length != right.Length || !rows.SequenceEqual(currentRows)) return false;
        foreach (var reference in rows)
            foreach (var field in new[] { 7, 8 })
            {
                var a = source.Field(source.TableAtReference(reference), field, 4);
                var b = current.Field(current.TableAtReference(reference), field, 4);
                if (a != b) return false;
                if (a != 0) { left.AsSpan(a, 4).Clear(); right.AsSpan(b, 4).Clear(); }
            }
        return left.AsSpan().SequenceEqual(right);
    }
    private PlaFlatBuffer Buffer(string path) => buffers.TryGetValue(path, out var b) ? b : buffers[path] = new(current.Read(path, Runtime));
    private Dictionary<ulong, int> Tables(PlaFlatBuffer b)
    {
        if (tableCache.TryGetValue(b, out var cached)) return cached;
        var result = new Dictionary<ulong, int>();
        foreach (var reference in b.VectorReferences(b.Root, 0, 4096))
            if (!result.TryAdd(b.Hash(b.TableAtReference(reference), 0), reference)) throw new InvalidDataException("The PLA field record identity is ambiguous.");
        return tableCache[b] = result;
    }
    private Dictionary<string, Entry> Entries(string kind) => kind switch
    {
        "encounters" => encounters, "spawns" => spawns, "outbreakEncounters" => outbreakEncounters,
        "distortionEncounters" => distortionEncounters, "distortionSpawns" => distortionSpawns,
        _ => throw new InvalidDataException("The PLA field editor is unsupported."),
    };
    private Entry Get(string kind, string id) => Entries(kind).GetValueOrDefault(id)
        ?? throw new InvalidDataException("The PLA wild encounter identity is unavailable.");
    internal string PathFor(string kind, string id) { _ = Get(kind, id); return ArchivePath; }
    internal PlaField[] Fields(string kind) => IsSpawn(kind)
        ? [new("spawnMin", "spawnMin", Minimum: 1, Maximum: 4), new("spawnMax", "spawnMax", Minimum: 1, Maximum: 4)]
        : [
            new("species", "species", Choices: catalog.SpeciesChoices.Where(choice => !SpecialSpecies.Contains(int.Parse(choice.Value, CultureInfo.InvariantCulture))).ToArray()),
            catalog.FormField(AllowedIdentity),
            new("wildMin", "wildMin", Minimum: 0, Maximum: 100), new("wildMax", "wildMax", Minimum: 0, Maximum: 100),
            new("wildWeight", "wildWeight", Minimum: 0, Maximum: 10000),
            new("perfectIvs", "perfectIvs", Minimum: 0, Maximum: 6),
            .. Efforts.Select(key => new PlaField(key, key, Minimum: -1, Maximum: 10)),
            .. Multipliers.Select(key => new PlaField(key, key, "decimal", -1, 100)),
        ];
    internal IReadOnlyList<PlaRecord> Records(string kind, IReadOnlyDictionary<string, byte[]> edited, string? selectedId = null)
    {
        var archive = edited.TryGetValue(ArchivePath, out var data) ? new PlaFieldArchive(data) : current;
        var members = new Dictionary<string, PlaFlatBuffer>(StringComparer.Ordinal);
        PlaFlatBuffer Read(string path) => members.TryGetValue(path, out var buffer) ? buffer : members[path] = new(archive.Read(path, Runtime));
        var protectedFields = Fields(kind).Select(field => field.Key).ToArray();
        return Entries(kind).Where(pair => selectedId is null || pair.Key == selectedId).Select(pair =>
        {
            return Record(kind, pair.Key, pair.Value, Read(pair.Value.Path), Read, protectedFields);
        }).ToArray();
    }
    internal PlaRecord Record(string kind, string id, byte[]? edited)
    {
        var entry = Get(kind, id);
        var archive = edited is null ? current : new PlaFieldArchive(edited);
        PlaFlatBuffer Read(string path) => edited is null ? Buffer(path) : new(archive.Read(path, Runtime));
        return Record(kind, id, entry, Read(entry.Path), Read);
    }
    private PlaRecord Record(string kind, string id, Entry entry, PlaFlatBuffer b, Func<string, PlaFlatBuffer>? read = null,
        string[]? protectedFields = null)
    {
        var row = b.TableAtReference(IsSpawn(kind) ? b.VectorReferences(b.Root, 0, 4096)[entry.Slot] : Tables(b)[entry.Table]);
        if (b.Hash(row, 0) != entry.Table) throw new InvalidDataException("The PLA field identity changed.");
        var metadata = new Dictionary<string, string> { ["area"] = entry.Area, ["table"] = Hex(entry.Table), ["alpha"] = entry.Alpha,
            ["areaName"] = catalog.AreaLabel(entry.Area),
            ["consumers"] = Number(entry.Consumers.Length), ["protected"] = entry.Protected ? "1" : "0" };
        var values = new Dictionary<string, string>();
        var label = entry.Area + " / " + Hex(entry.Table);
        var related = entry.Consumers.Select(key => new PlaRelatedRecord(IsSpawn(kind) ? "table" : "spawner", key, key)).ToArray();
        if (IsSpawn(kind))
        {
            values["spawnMin"] = Number(Int(b, row, 7)); values["spawnMax"] = Number(Int(b, row, 8));
            related = entry.Consumers.Select(key => new PlaRelatedRecord("table", key,
                encounterPaths.TryGetValue((entry.Area, key), out var path)
                    ? EncounterLabel((read ?? Buffer)(path), ulong.Parse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture)) : key)).ToArray();
            metadata["pokemon"] = string.Join("; ", related.Select(record => record.Label).Distinct(StringComparer.Ordinal));
        }
        else
        {
            metadata["defaultMin"] = Number(Int(b, row, 1)); metadata["defaultMax"] = Number(Int(b, row, 2));
            var slots = b.VectorReferences(row, 3, 100);
            if (entry.Slot >= slots.Length) throw new InvalidDataException("The PLA encounter slot is missing.");
            row = b.TableAtReference(slots[entry.Slot]);
            if (b.Hash(row, 1) != entry.SlotId) throw new InvalidDataException("The PLA encounter slot identity changed.");
            values["species"] = Number(Int(b, row, 0)); values["form"] = Number(Int(b, row, 3));
            values["wildWeight"] = Number(Int(b, row, 29)); values["wildMin"] = Number(Int(b, row, 30)); values["wildMax"] = Number(Int(b, row, 31));
            values["perfectIvs"] = Number(Int(b, row, 25));
            for (var i = 0; i < Efforts.Length; i++) values[Efforts[i]] = Number(Int(b, row, 13 + i));
            var eligibility = Child(b, row, 45);
            var inherited = defaults.GetValueOrDefault(Identity(b, row));
            for (var i = 0; i < Multipliers.Length; i++)
            {
                values[Multipliers[i]] = b.Float(eligibility, 7 + i).ToString("R", CultureInfo.InvariantCulture);
                metadata["default." + Multipliers[i]] = inherited?[i].ToString("R", CultureInfo.InvariantCulture) ?? "?";
            }
            metadata["slot"] = Number(entry.Slot + 1);
            metadata["pokemon"] = catalog.PokemonLabel(Identity(b, row));
            metadata["speciesName"] = catalog.SpeciesLabel(Int(b, row, 0));
            metadata["formName"] = catalog.FormLabel(Identity(b, row));
            metadata["condition"] = OrdinaryCondition(b, eligibility, 0, 2) ? "none" : QualifiedCondition(b, eligibility) ? "progression" : "protected";
            label = entry.Area + " / " + Hex(entry.Table) + " / " + catalog.PokemonLabel(Identity(b, row));
        }
        return new(id, label, values) { Metadata = metadata, ReadOnlyFields = entry.Protected
            ? protectedFields?.ToArray() ?? Fields(kind).Select(field => field.Key).ToArray() : [],
            RelatedRecords = related };
    }

    internal byte[] Compose(IReadOnlyDictionary<string, byte[]> members) => current.Compose(members, Runtime);
    internal byte[] ReadMember(string path) => current.Read(path, Runtime);
    internal byte[] BaseMember(string path) => original.Read(path, Runtime);
    internal void ValidateEventSources()
    {
        foreach (var path in EventLinks.PlacementPaths)
            if (!BaseMember(path).AsSpan().SequenceEqual(ReadMember(path))) throw new InvalidDataException("PLA distortion placement data must be preserved.");
        foreach (var path in EventLinks.SpawnPaths)
        {
            var source = new PlaFlatBuffer(BaseMember(path)); var now = Buffer(path);
            if (!CountOnlyCompatible(source, now, source.VectorReferences(source.Root, 0, 4096), now.VectorReferences(now.Root, 0, 4096)))
                throw new InvalidDataException("PLA event spawner links and placement data must be preserved.");
        }
    }
    internal string EncounterLabel(string path, ulong hash)
    {
        return EncounterLabel(Buffer(path), hash);
    }
    internal string EncounterLabel(PlaFlatBuffer b, ulong hash)
    {
        if (!Tables(b).TryGetValue(hash, out var reference)) throw new InvalidDataException("The PLA event encounter table is missing.");
        var slots = b.VectorReferences(b.TableAtReference(reference), 3, 100);
        return slots.Length == 0 ? Hex(hash) : string.Join(", ", slots.Select(slot => catalog.PokemonLabel(Identity(b, b.TableAtReference(slot)))).Distinct(StringComparer.Ordinal));
    }
    internal Dictionary<string, byte[]> EditMembers(IReadOnlyDictionary<(string Kind, string Id), Dictionary<string, string>> edits)
    {
        // Different editor tabs can address the same physical encounter slot.
        // Merge those fields before validating the complete record and cloning its tree.
        var combined = new Dictionary<(string Kind, string Id), Dictionary<string, string>>();
        foreach (var group in edits.GroupBy(pair => (Kind: PhysicalKind(pair.Key.Kind), pair.Key.Id)))
        {
            var key = group.First().Key; var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in group)
            {
                if (Get(pair.Key.Kind, pair.Key.Id).Protected) throw new InvalidDataException("This PLA encounter record is protected.");
                foreach (var field in pair.Value)
                    if (!values.TryAdd(field.Key, field.Value)) throw new InvalidDataException("The PLA session contains conflicting encounter fields.");
            }
            combined.Add(key, values);
        }
        var members = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var member in combined.GroupBy(pair => Get(pair.Key.Kind, pair.Key.Id).Path))
        {
            var b = new PlaFlatBuffer(current.Read(member.Key, Runtime));
            foreach (var group in member.GroupBy(pair => Get(pair.Key.Kind, pair.Key.Id).Table))
            {
                var first = group.First(); var firstEntry = Get(first.Key.Kind, first.Key.Id);
                var reference = IsSpawn(first.Key.Kind) ? b.VectorReferences(b.Root, 0, 4096)[firstEntry.Slot] : Tables(b)[group.Key];
                var table = b.TableAtReference(reference);
                var treeEdits = new Dictionary<int, IReadOnlyDictionary<int, long>>();
                foreach (var (key, changes) in group)
                {
                    var entry = Get(key.Kind, key.Id); var record = Record(key.Kind, key.Id, entry, b);
                    if (entry.Protected || changes.Keys.Any(field => !record.Values.ContainsKey(field)))
                        throw new InvalidDataException("This PLA wild encounter record or field is protected.");
                    var effective = changes.Where(pair => record.Values[pair.Key] != pair.Value).ToDictionary(pair => pair.Key, pair => pair.Value);
                    if (effective.Count == 0) continue;
                    var values = record.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    foreach (var pair in changes) values[pair.Key] = pair.Value;
                    int Value(string field) => int.Parse(values[field], CultureInfo.InvariantCulture);
                    if (IsSpawn(key.Kind))
                    {
                        if (Value("spawnMin") < 1 || Value("spawnMax") > 4 || Value("spawnMin") > Value("spawnMax"))
                            throw new InvalidDataException("PLA spawn counts must be between 1 and 4 with minimum no greater than maximum.");
                        if (b.VectorReferences(b.Root, 0, 4096).Count(r => b.TableAtReference(r) == table) != 1)
                            throw new InvalidDataException("The PLA spawner object is shared and cannot be changed independently.");
                        foreach (var field in effective.Keys) b.SetExistingInt32(reference, SpawnWidths, field == "spawnMin" ? 7 : 8, Value(field));
                        continue;
                    }
                    var identity = values["species"] + ":" + values["form"];
                    if (!catalog.Pokemon.ContainsKey(identity) || !AllowedIdentity(identity) || !defaults.ContainsKey(identity))
                        throw new InvalidDataException("The PLA wild Pokemon or form is unsupported.");
                    var min = Value("wildMin") == 0 ? Int(b, table, 1) : Value("wildMin");
                    var max = Value("wildMax") == 0 ? Int(b, table, 2) : Value("wildMax");
                    if (min < 1 || max > 100 || min > max) throw new InvalidDataException("The PLA encounter level range is invalid.");
                    var slotReference = b.VectorReferences(table, 3, 100)[entry.Slot]; var slot = b.TableAtReference(slotReference);
                    var scalars = new Dictionary<int, long>(); var eligibility = new Dictionary<int, long>();
                    foreach (var field in effective.Keys)
                    {
                        var multiplier = Array.IndexOf(Multipliers, field);
                        if (multiplier >= 0)
                        {
                            var value = float.Parse(values[field], CultureInfo.InvariantCulture);
                            if (!float.IsFinite(value) || value != -1 && value < 0 || value > 100)
                                throw new InvalidDataException("PLA eligibility multipliers must be -1 for inherited values or between 0 and 100.");
                            eligibility[multiplier + 7] = BitConverter.SingleToInt32Bits(value);
                        }
                        else scalars[field switch
                        {
                            "species" => 0, "form" => 3, "wildWeight" => 29, "wildMin" => 30, "wildMax" => 31, "perfectIvs" => 25,
                            _ when Array.IndexOf(Efforts, field) >= 0 => 13 + Array.IndexOf(Efforts, field),
                            _ => throw new InvalidDataException("The PLA wild encounter field is unsupported."),
                        }] = Value(field);
                    }
                    treeEdits.Add(slotReference, scalars);
                    if (eligibility.Count > 0) treeEdits.Add(b.Field(slot, 45, 4), eligibility);
                }
                if (treeEdits.Count > 0)
                {
                    b.EditObjectTree(reference, TableLayout, treeEdits);
                    var slots = b.VectorReferences(b.TableAtReference(reference), 3, 100);
                    if (!slots.Any(slot => Int(b, b.TableAtReference(slot), 29) > 0))
                        throw new InvalidDataException("A PLA encounter table must retain a positive slot weight.");
                }
            }
            members[member.Key] = b.ToArray();
        }
        return members;
    }
}
