// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json;
using KM.Formats.PLA;

namespace KM.PLA;

internal sealed class PlaCatalog
{
    internal static readonly int[] PersonalWidths = Enumerable.Range(0, 58).Select(i => i switch
    {
        0 or 1 or 5 or 6 or 7 or 20 or 21 or 31 or 38 or 39 or 40 or 43 or 44 or 46 or 49 or 50 => 2,
        >= 22 and <= 30 or >= 51 and <= 57 => 4,
        _ => 1,
    }).ToArray();
    internal static readonly int[] MoveWidths = Enumerable.Range(0, 90).Select(i => i switch
    {
        0 or 67 or >= 69 and <= 73 or 87 => 4,
        11 or 18 => 2,
        _ => 1,
    }).ToArray();
    internal static bool MoveSigned(int index) => index is 8 or >= 25 and <= 27 or 67 or >= 69 and <= 73 or 87;
    internal static readonly Dictionary<string, int> PersonalFields = new()
    {
        ["hp"] = 8, ["attack"] = 9, ["defense"] = 10, ["specialAttack"] = 11,
        ["specialDefense"] = 12, ["speed"] = 13, ["type1"] = 3, ["type2"] = 4,
        ["catchRate"] = 17, ["baseFriendship"] = 48, ["growthRate"] = 15, ["baseExperience"] = 31,
    };
    internal static readonly Dictionary<string, int> MoveFields = new()
    {
        ["type"] = 2, ["category"] = 4, ["power"] = 5, ["accuracy"] = 6, ["pp"] = 7,
        ["priority"] = 8, ["canStyle"] = 68, ["agilePower"] = 74, ["strongPower"] = 79,
        ["strongAccuracy"] = 80, ["actionSpeedUser"] = 69, ["actionSpeedUserAgile"] = 70,
        ["actionSpeedUserStrong"] = 71, ["actionSpeedTarget"] = 72, ["actionSpeedTargetAgile"] = 73,
    };
    internal static readonly string[] SpecialMoveFields = ["power", "accuracy", "agilePower", "strongPower", "strongAccuracy",
        "actionSpeedUser", "actionSpeedUserAgile", "actionSpeedUserStrong", "actionSpeedTarget", "actionSpeedTargetAgile"];
    internal static readonly Dictionary<int, string> EvolutionMethods = new()
    {
        [1] = "Friendship", [2] = "Friendship during daytime", [3] = "Friendship at night", [4] = "Level",
        [8] = "Use item", [17] = "Use item, male", [18] = "Use item, female", [21] = "Know move",
        [22] = "Species in party", [23] = "Level, male", [24] = "Level, female",
    };
    internal PlaSourceScope Scope { get; }
    internal PlaBattleCatalog Battles { get; }
    private PlaEconomyCatalog? economy;
    internal PlaEconomyCatalog Economy => economy ??= new(this);
    private PlaWildCatalog? wild;
    internal PlaWildCatalog Wild => wild ??= new(this);
    private PlaEventCatalog? events;
    internal PlaEventCatalog Events => events ??= new(this);
    private PlaScriptCatalog? scripts;
    internal PlaScriptCatalog Scripts => scripts ??= new(this);
    private PlaExecutableCatalog? executable;
    internal PlaExecutableCatalog Executable => executable ??= new(this);
    private PlaSettingsCatalog? settings;
    internal PlaSettingsCatalog Settings => settings ??= new(this);
    private PlaWeatherCatalog? weather;
    internal PlaWeatherCatalog Weather => weather ??= new(this);
    internal PlaFlatBuffer Personal { get; }
    internal Dictionary<string, int> Pokemon { get; } = new(StringComparer.Ordinal);
    private Dictionary<int, (string Path, PlaFlatBuffer Buffer)>? moves;
    internal Dictionary<int, (string Path, PlaFlatBuffer Buffer)> Moves => moves ??= LoadMoves();
    private Dictionary<int, (int Reference, int Move)>? tutors;
    internal Dictionary<int, (int Reference, int Move)> Tutors => tutors ??= LoadTutors();
    private PlaFlatBuffer? tutorTable;
    internal PlaFlatBuffer TutorTable => tutorTable ??= new(Scope.Read(PlaSourceScope.TutorsPath));
    private readonly string[] names;
    private readonly string[] moveNames;
    private readonly string[] typeNames;
    private readonly IReadOnlyDictionary<string, byte[]>? displayFiles;
    private readonly Dictionary<string, PlaMessageFile> messages = new(StringComparer.Ordinal);
    private string[]? moveDescriptions;
    private readonly Dictionary<string, Dictionary<ulong, string>> messageLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (PlaFlatBuffer Buffer, Dictionary<string, int> References)> tables = new(StringComparer.Ordinal);

    internal PlaCatalog(PlaSourceScope scope, IReadOnlyDictionary<string, byte[]>? displayFiles = null)
    {
        Scope = scope;
        this.displayFiles = displayFiles;
        Battles = new(this);
        Personal = new(scope.Read(PlaSourceScope.PersonalPath));
        var allIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in Personal.VectorReferences(Personal.Root, 0, 4096))
        {
            var row = Personal.TableAtReference(reference);
            var id = Identity(Personal, row);
            if (!allIdentities.Add(id)) throw new InvalidDataException("The PLA personal table contains duplicate identities.");
            if (Personal.Scalar(row, 2, 1) == 1) Pokemon.Add(id, reference);
        }
        if (Pokemon.Count == 0) throw new InvalidDataException("The PLA personal table contains no supported Pokemon.");
        names = Labels("monsname");
        moveNames = Labels("wazaname");
        typeNames = Labels("typename");
    }

    private Dictionary<int, (string Path, PlaFlatBuffer Buffer)> LoadMoves()
    {
        var result = new Dictionary<int, (string Path, PlaFlatBuffer Buffer)>();
        foreach (var path in Scope.MoveFiles())
        {
            var buffer = new PlaFlatBuffer(Scope.Read(path));
            if (buffer.Scalar(buffer.Root, 1, 1) == 0) continue;
            var id = checked((int)buffer.Scalar(buffer.Root, 0, 4));
            if (id <= 0 || !result.TryAdd(id, (path, buffer))) throw new InvalidDataException("The PLA move identities are ambiguous.");
        }
        return result;
    }

    private Dictionary<int, (int Reference, int Move)> LoadTutors()
    {
        var result = new Dictionary<int, (int Reference, int Move)>();
        foreach (var reference in TutorTable.VectorReferences(TutorTable.Root, 0, 64))
        {
            var table = TutorTable.TableAtReference(reference);
            var index = checked((int)TutorTable.Scalar(table, 0, 4));
            var move = checked((int)TutorTable.Scalar(table, 1, 4));
            if (index is < 1 or > 61 || !Moves.ContainsKey(move) || !result.TryAdd(index, (reference, move)))
                throw new InvalidDataException("The PLA tutor index or move is invalid.");
        }
        if (result.Count != 61) throw new InvalidDataException("The PLA tutor slot layout is unsupported.");
        return result;
    }

    private PlaMessageFile Message(string file)
    {
        if (messages.TryGetValue(file, out var message)) return message;
        var path = $"bin/message/{Scope.Language}/{(file.Contains('/') ? file : "common/" + file)}";
        byte[] Read(string relative) => displayFiles?.TryGetValue(relative, out var bytes) == true ? bytes : Scope.Read(relative);
        return messages[file] = new(Read(path + ".dat"), Read(path + ".tbl"));
    }
    internal string[] Labels(string file) => Message(file).Lines.Select(line => line.Text).ToArray();
    internal string MessageLabel(string file, ulong hash)
    {
        if (!messageLabels.TryGetValue(file, out var labels))
        {
            var message = Message(file);
            labels = message.Lines.Select((line, index) => (message.Keys[index].Hash, line.Text)).ToDictionary(pair => pair.Hash, pair => pair.Text);
            messageLabels.Add(file, labels);
        }
        return labels.GetValueOrDefault(hash) ?? "";
    }
    internal string AreaLabel(string area) => area.StartsWith("ha_area", StringComparison.Ordinal)
        ? MessageLabel("place_name_indirect", PlaFieldArchive.HashPath("PLACENAME_" + area[3..].ToUpperInvariant())) : "";
    internal string TypeLabel(int id) => Name(typeNames, id);
    internal string SpeciesLabel(int id) => Name(names, id);
    internal static string Identity(PlaFlatBuffer buffer, int row) => $"{buffer.Scalar(row, 0)}:{buffer.Scalar(row, 1)}";
    internal string PokemonLabel(string identity)
    {
        var parts = identity.Split(':');
        var species = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var form = FormLabel(identity);
        return $"{species:D3} {Name(names, species)}" + (form.Length > 0 ? $" ({form}, {parts[1]})" : parts[1] == "0" ? "" : $" ({parts[1]})");
    }
    internal string FormLabel(string identity)
    {
        var parts = identity.Split(':');
        return MessageLabel("zkn_form", PlaFieldArchive.HashPath($"ZKN_FORM_{int.Parse(parts[0], CultureInfo.InvariantCulture):D3}_{int.Parse(parts[1], CultureInfo.InvariantCulture):D3}"));
    }
    internal string MoveLabel(int id) => $"{id:D3} {Name(moveNames, id)}";
    internal IReadOnlyDictionary<string, string> PokemonSpriteNames()
    {
        const string path = "bin/message/English/common/monsname";
        // Sprite identities use original English names independently of edited display text.
        return new PlaMessageFile(Scope.Base(path + ".dat"), Scope.Base(path + ".tbl")).Lines
            .Select((line, id) => (Id: id.ToString(CultureInfo.InvariantCulture), line.Text))
            .ToDictionary(pair => pair.Id, pair => pair.Text, StringComparer.Ordinal);
    }
    private static string Name(string[] labels, int id) => id >= 0 && id < labels.Length ? labels[id] : id.ToString(CultureInfo.InvariantCulture);
    internal IReadOnlyList<PlaChoice> MoveChoices => Moves.Keys.Order().Select(id => new PlaChoice(id.ToString(CultureInfo.InvariantCulture), MoveLabel(id))).ToArray();
    internal void RefreshTutorLabels(IReadOnlyDictionary<string, byte[]> edited)
    {
        if (!edited.TryGetValue(PlaSourceScope.TutorsPath, out var bytes)) return;
        var table = new PlaFlatBuffer(bytes);
        foreach (var slot in Tutors.Keys.ToArray())
        {
            var original = Tutors[slot];
            Tutors[slot] = (original.Reference, checked((int)table.Scalar(table.TableAtReference(original.Reference), 1, 4)));
        }
    }

    internal HashSet<ushort> EvolutionItems()
    {
        var source = new PlaFlatBuffer(Scope.Base(PlaSourceScope.EvolutionsPath));
        return source.VectorReferences(source.Root, 0, 4096)
            .SelectMany(reference => Rows(source, reference, 2, 5))
            .Where(row => row[0] is 8 or 17 or 18).Select(row => row[1]).ToHashSet();
    }
    internal IReadOnlyList<PlaChoice> SpeciesChoices => Pokemon.Keys.Select(id => int.Parse(id.Split(':')[0], CultureInfo.InvariantCulture)).Distinct().Order()
        .Select(id => new PlaChoice(id.ToString(CultureInfo.InvariantCulture), Name(names, id))).ToArray();
    internal IReadOnlyList<PlaChoice> TypeChoices => Enumerable.Range(0, 18).Select(id => new PlaChoice(id.ToString(CultureInfo.InvariantCulture), Name(typeNames, id))).ToArray();

    internal PlaField FormField(Func<string, bool>? allowed = null, bool preserveForm = false) => new("form", "form", Minimum: 0, Maximum: 65535,
        DependsOn: "species", DependentChoices: Pokemon.Keys.Where(id => allowed?.Invoke(id) ?? true)
            .GroupBy(id => id.Split(':')[0]).ToDictionary(group => group.Key,
                group => (IReadOnlyList<PlaChoice>)group.OrderBy(id => int.Parse(id.Split(':')[1], CultureInfo.InvariantCulture))
                    .Select(id => new PlaChoice(id.Split(':')[1], PokemonLabel(id)))
                    .Concat(preserveForm ? [new PlaChoice("65535", "65535")] : []).ToArray()));

    internal PlaField[] Fields(string kind) => kind switch
    {
        _ when PlaExecutableCatalog.Supports(kind) => PlaExecutableCatalog.Fields,
        _ when PlaScriptCatalog.Supports(kind) => Scripts.Fields(kind),
        _ when PlaSettingsCatalog.Supports(kind) => Settings.Fields(kind),
        "weather" => Weather.Fields(),
        _ when PlaWildCatalog.Supports(kind) => Wild.Fields(kind),
        _ when PlaEventCatalog.Supports(kind) => Events.Fields(kind),
        _ when PlaEconomyCatalog.Supports(kind) => Economy.Fields(kind),
        "personal" => PersonalFields.Keys.Select(key => key switch
        {
            "type1" or "type2" => new PlaField(key, key, Choices: TypeChoices),
            "growthRate" => new PlaField(key, key, Choices: Enumerable.Range(0, 6).Select(value => new PlaChoice(value.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture))).ToArray()),
            "baseExperience" => new PlaField(key, key, Minimum: 0, Maximum: 65535),
            _ => new PlaField(key, key, Minimum: key is "catchRate" or "baseFriendship" ? 0 : 1, Maximum: 255),
        }).ToArray(),
        "moves" => MoveFields.Keys.Select(key => key switch
        {
            "type" => new PlaField(key, key, Choices: TypeChoices),
            "category" => new PlaField(key, key, Choices: [new("0", "Status"), new("1", "Physical"), new("2", "Special")]),
            "priority" => new PlaField(key, key, Minimum: -128, Maximum: 127),
            "canStyle" => new PlaField(key, key, "boolean", 0, 1),
            "accuracy" or "strongAccuracy" => new PlaField(key, key, Minimum: 0, Maximum: 100),
            _ when key.StartsWith("actionSpeed", StringComparison.Ordinal) => new PlaField(key, key, Minimum: -100, Maximum: 100),
            _ => new PlaField(key, key, Minimum: key == "pp" ? 1 : 0, Maximum: 255),
        }).ToArray(),
        "compatibility" => Tutors.OrderBy(p => p.Key).Select(pair => new PlaField($"tutor.{pair.Key}", MoveLabel(pair.Value.Move), "boolean", 0, 1)).ToArray(),
        "tutors" => [new("move", "move", Choices: MoveChoices), new("price", "price", Minimum: 0, Maximum: 999999)],
        "learnsets" or "evolutions" => [new("entries", "entries", "rows")],
        "text" => [new("text", "text", "text")],
        "trainers" or "gifts" or "static" => Battles.Fields(kind),
        _ => throw new InvalidDataException("The PLA editor is unsupported."),
    };

    internal PlaField[] RowFields(string kind) => kind switch
    {
        "learnsets" => [new("level", "level", Minimum: 0, Maximum: 100), new("mastery", "mastery", Minimum: 0, Maximum: 100), new("move", "move", Choices: MoveChoices)],
        "evolutions" => [new("method", "method", Choices: EvolutionMethods.Select(p => new PlaChoice(p.Key.ToString(CultureInfo.InvariantCulture), p.Value)).ToArray()),
            new("argument", "argument", Minimum: 0, Maximum: 65535, ChoicesByMethod: EvolutionArgumentChoices()), new("species", "species", Choices: SpeciesChoices),
            FormField(preserveForm: true), new("level", "level", Minimum: 0, Maximum: 100)],
        _ => [],
    };

    private IReadOnlyDictionary<string, IReadOnlyList<PlaChoice>> EvolutionArgumentChoices()
    {
        var itemNames = Labels("itemname");
        var items = EvolutionItems().Order().Select(id => new PlaChoice(id.ToString(CultureInfo.InvariantCulture), $"{id}: {Name(itemNames, id)}")).ToArray();
        return EvolutionMethods.Keys.ToDictionary(method => method.ToString(CultureInfo.InvariantCulture),
            method => method switch
            {
                8 or 17 or 18 => (IReadOnlyList<PlaChoice>)items,
                21 => MoveChoices,
                22 => SpeciesChoices,
                _ => [new("0", "0")],
            });
    }

    internal (string Path, PlaFlatBuffer Buffer, int Reference) Locate(string kind, string id)
    {
        if (PlaBattleCatalog.Supports(kind)) return Battles.Locate(kind, id);
        if (kind == "moves")
        {
            if (!int.TryParse(id, CultureInfo.InvariantCulture, out var move) || !Moves.TryGetValue(move, out var value))
                throw new InvalidDataException("The PLA move is not usable in this game.");
            return (value.Path, value.Buffer, 0);
        }
        if (kind == "tutors")
        {
            if (!int.TryParse(id, CultureInfo.InvariantCulture, out var index) || !Tutors.TryGetValue(index, out var value))
                throw new InvalidDataException("The PLA tutor identity is invalid.");
            return (PlaSourceScope.TutorsPath, TutorTable, value.Reference);
        }
        if (!Pokemon.TryGetValue(id, out var reference)) throw new InvalidDataException("The PLA Pokemon or form is not present in this game.");
        if (kind is "personal" or "compatibility") return (PlaSourceScope.PersonalPath, Personal, reference);
        var path = kind switch { "learnsets" => PlaSourceScope.LearnsetsPath, "evolutions" => PlaSourceScope.EvolutionsPath, _ => throw new InvalidDataException("The PLA editor is unsupported.") };
        if (!tables.TryGetValue(path, out var cached))
        {
            var table = new PlaFlatBuffer(Scope.Read(path));
            var references = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in table.VectorReferences(table.Root, 0, 4096))
                if (!references.TryAdd(Identity(table, table.TableAtReference(item)), item))
                    throw new InvalidDataException("The PLA Pokemon table contains ambiguous identities.");
            cached = (table, references);
            tables.Add(path, cached);
        }
        if (!cached.References.TryGetValue(id, out var target)) throw new InvalidDataException("The PLA Pokemon table identity is missing.");
        return (path, cached.Buffer, target);
    }

    internal string ResourcePath(string kind, string id) => PlaWildCatalog.Supports(kind) ? Wild.PathFor(kind, id)
        : PlaExecutableCatalog.Supports(kind) ? PlaExecutableCatalog.PathFor(kind, id)
        : PlaScriptCatalog.Supports(kind) ? Scripts.PathFor(kind, id)
        : PlaSettingsCatalog.Supports(kind) ? Settings.PathFor(kind, id)
        : kind == "weather" ? Weather.PathFor(id)
        : PlaEventCatalog.Supports(kind) ? Events.PathFor(kind, id)
        : PlaEconomyCatalog.Supports(kind) ? Economy.PathFor(kind, id) : Locate(kind, id).Path;

    internal static ushort[][] Rows(PlaFlatBuffer table, int reference, int field, int columns) => table.VectorReferences(table.TableAtReference(reference), field, 1024)
        .Select(r => Enumerable.Range(0, columns).Select(i => checked((ushort)table.Scalar(table.TableAtReference(r), i))).ToArray()).ToArray();

    internal PlaRecord Record(string kind, string id, byte[]? edited = null)
    {
        if (PlaExecutableCatalog.Supports(kind)) return Executable.Record(kind, id, edited);
        if (PlaScriptCatalog.Supports(kind)) return Scripts.Record(kind, id, edited);
        if (PlaSettingsCatalog.Supports(kind)) return Settings.Record(kind, id, edited);
        if (kind == "weather") return Weather.Record(id, edited);
        if (PlaWildCatalog.Supports(kind)) return Wild.Record(kind, id, edited);
        if (PlaEventCatalog.Supports(kind)) return Events.Record(kind, id, edited);
        if (PlaEconomyCatalog.Supports(kind)) return Economy.Record(kind, id, edited);
        if (PlaBattleCatalog.Supports(kind)) return Battles.Record(kind, id, edited);
        var resource = Locate(kind, id);
        var table = edited is null ? resource.Buffer : new PlaFlatBuffer(edited);
        var reference = resource.Reference;
        if (edited is not null && kind != "moves")
        {
            var matches = table.VectorReferences(table.Root, 0, 4096).Where(item => kind == "tutors"
                ? table.Scalar(table.TableAtReference(item), 0, 4).ToString(CultureInfo.InvariantCulture) == id
                : Identity(table, table.TableAtReference(item)) == id).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("The PLA source identity is missing or ambiguous.");
            reference = matches[0];
        }
        var row = table.TableAtReference(reference);
        Dictionary<string, string> values;
        switch (kind)
        {
            case "personal": values = PersonalFields.ToDictionary(p => p.Key, p => table.Scalar(row, p.Value, PersonalWidths[p.Value]).ToString(CultureInfo.InvariantCulture)); break;
            case "moves": values = MoveFields.ToDictionary(p => p.Key, p => table.Scalar(row, p.Value, MoveWidths[p.Value], MoveSigned(p.Value)).ToString(CultureInfo.InvariantCulture)); break;
            case "compatibility": values = Tutors.Keys.ToDictionary(index => $"tutor.{index}", index => ((table.Scalar(row, 56 + (index - 1) / 32, 4) >> ((index - 1) % 32)) & 1).ToString(CultureInfo.InvariantCulture)); break;
            case "tutors": values = new() { ["move"] = table.Scalar(row, 1, 4).ToString(CultureInfo.InvariantCulture), ["price"] = table.Scalar(row, 2, 4).ToString(CultureInfo.InvariantCulture) }; break;
            default: values = new() { ["entries"] = JsonSerializer.Serialize(Rows(table, reference, kind == "learnsets" ? 3 : 2, kind == "learnsets" ? 3 : 5)) }; break;
        }
        return new PlaRecord(id, kind == "moves" ? MoveLabel(int.Parse(id, CultureInfo.InvariantCulture))
            : kind == "tutors" ? $"{id}: {MoveLabel(Tutors[int.Parse(id, CultureInfo.InvariantCulture)].Move)}" : PokemonLabel(id), values)
        { ReadOnlyFields = kind == "moves" && id is "459" or "460" or "467" ? SpecialMoveFields : [],
            Metadata = kind switch
            {
                "moves" => MoveMetadata(int.Parse(id, CultureInfo.InvariantCulture), resource.Path),
                "personal" or "learnsets" or "evolutions" or "compatibility" => new Dictionary<string, string> { ["formName"] = FormLabel(id) },
                _ => new Dictionary<string, string>()
            } };
    }

    private Dictionary<string, string> MoveMetadata(int id, string path)
    {
        var layered = Scope.OutputState(path).Exists;
        var metadata = new Dictionary<string, string>
        {
            ["sourceFile"] = layered
                ? PlaSourceScope.Resolve(Scope.Paths.OutputRootPath!, PlaSourceScope.OutputPath(path))
                : PlaSourceScope.Resolve(Scope.Paths.BaseRomFsPath!, path),
            ["sourceLayer"] = layered ? "layered" : "base",
            ["fileState"] = layered ? "layeredOverride" : "baseOnly"
        };

        var descriptionPath = $"bin/message/{Scope.Language}/common/wazainfo";
        moveDescriptions ??= File.Exists(PlaSourceScope.Resolve(Scope.Paths.BaseRomFsPath!, descriptionPath + ".dat"))
            && File.Exists(PlaSourceScope.Resolve(Scope.Paths.BaseRomFsPath!, descriptionPath + ".tbl"))
            ? Labels("wazainfo") : [];
        if (id >= 0 && id < moveDescriptions.Length && !string.IsNullOrWhiteSpace(moveDescriptions[id]))
            metadata["description"] = moveDescriptions[id];

        return metadata;
    }
}
