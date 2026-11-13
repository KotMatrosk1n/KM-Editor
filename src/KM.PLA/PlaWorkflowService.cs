// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.PLA;
using KM.Formats.Models;
using KM.PLA.Models;

namespace KM.PLA;

public sealed partial class PlaWorkflowService
{
    private const string Owner = "pla.loose.v1";
    private static string Domain(string kind) => kind switch
    {
        "moves" => "workflow.moves", "text" => "workflow.text",
        "trainers" => "workflow.trainers", "gifts" => "workflow.giftPokemon", "static" => "workflow.staticEncounters",
        "items" => "workflow.items", "crafting" => "workflow.crafting", "shops" or "ginkgo" => "workflow.shops",
        "drops" => "workflow.pokemonDrops", "research" or "ranks" => "workflow.research",
        "encounters" or "spawns" => "workflow.encounters",
        "outbreaks" or "massiveOutbreaks" or "outbreakGroups" or "outbreakWaves" or "outbreakDurations" or "outbreakEncounters" => "workflow.outbreaks",
        "distortionEncounters" or "distortionSpawns" or "distortionItems" => "workflow.distortions",
        "capture" or "friendship" or "shiny" or "bagPrices" or "charmPrices" => "workflow.gameplayRules",
        "weather" => "workflow.weather",
        "typeChart" => "workflow.typeChart",
        "personal" or "learnsets" or "evolutions" or "compatibility" or "tutors" => "workflow.pokemon",
        _ => throw new InvalidDataException("The PLA editor is unsupported."),
    };
    private static ValidationDiagnostic Error(string message, string code = "KM-PLA-INVALID-DATA") =>
        new(DiagnosticSeverity.Error, message, Domain: "pla") { Code = code };
    private static bool Expected(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException or JsonException;
    private sealed record Prepared(ChangePlan Plan, Dictionary<string, byte[]> Files);

    public string CaptureRowClipboardSourceFingerprint(ProjectPaths paths)
    {
        using var scope = new PlaSourceScope(paths);
        var catalog = new PlaCatalog(scope);
        _ = Records(catalog, "learnsets", null, [], out _, out _);
        _ = Records(catalog, "trainers", null, [], out _, out _);
        // Fingerprint compressed source bytes without requiring its runtime for loose editors.
        if (File.Exists(PlaSourceScope.Resolve(paths.BaseRomFsPath!, PlaWildCatalog.ArchivePath)))
            _ = scope.Read(PlaWildCatalog.ArchivePath);
        scope.VerifyOutput();
        return scope.Fingerprint();
    }

    public PlaPage Load(ProjectPaths paths, EditSession session, string kind, string? textFile = null, string? query = null, int offset = 0, string? messageLanguage = null)
    {
        if (offset < 0 || query?.Length > 256) throw new InvalidDataException("The PLA page request is invalid.");
        _ = Domain(kind);
        if (messageLanguage is not null)
        {
            if (kind != "text" || !PlaSourceScope.Languages.Contains(messageLanguage, StringComparer.Ordinal))
                throw new InvalidDataException("Select a supported PLA message language.");
            paths = paths with { GameTextLanguage = messageLanguage };
        }
        using var scope = new PlaSourceScope(paths);
        var listing = !session.HasPendingChanges ? cache.LoadListing(scope, kind, textFile) : null;
        if (listing is null)
        {
            listing = ReadListing(scope, session, kind, textFile);
            if (!session.HasPendingChanges) cache.StoreListing(scope, kind, textFile, listing);
        }
        var records = listing.Records;
        bool Matches(PlaRecord record) => string.IsNullOrEmpty(query) || record.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || record.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || kind == "text" && record.Values["text"].Contains(query, StringComparison.OrdinalIgnoreCase)
            || record.Metadata.Any(pair => pair.Key is "area" or "areaName" or "move" or "moveType" or "contents"
                && pair.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            || record.RelatedRecords.Any(related => related.Label.Contains(query, StringComparison.OrdinalIgnoreCase));
        var total = 0;
        PlaRecord[] visible;
        PlaGroup[] groupChoices = [];
        if (kind == "trainers")
        {
            // Keep complete parties together while bounding the response to 96 slots.
            var parties = records.GroupBy(record => record.Id[..record.Id.LastIndexOf(':')]).Where(party => party.Any(Matches)).ToArray();
            total = parties.Length;
            visible = parties.Skip(offset).Take(16).SelectMany(party => party).ToArray();
        }
        else if (kind == "shops")
        {
            var inventories = records.GroupBy(record => record.Metadata["inventory"]).Where(group => group.Any(Matches)).ToArray();
            if (inventories.Any(group => group.Count() > 32)) throw new InvalidDataException("The PLA shop inventory exceeds the supported size.");
            total = inventories.Length;
            visible = inventories.Skip(offset).Take(3).SelectMany(group => group).ToArray();
        }
        else if (PlaWildCatalog.IsEncounter(kind))
        {
            var groups = records.GroupBy(record => record.Metadata["area"] + "/" + record.Metadata["table"]).Where(group => group.Any(Matches)).ToArray();
            groupChoices = groups.Select((group, index) => new PlaGroup(index.ToString(CultureInfo.InvariantCulture),
                string.Join(", ", group.Select(record => record.Metadata["pokemon"]).Distinct(StringComparer.Ordinal)),
                group.First().Metadata["area"], group.First().Metadata["table"])).ToArray();
            total = groups.Length;
            visible = groups.Skip(offset).Take(1).SelectMany(group => group).ToArray();
        }
        else
        {
            List<PlaRecord> page = [with(capacity: Math.Min(100, records.Count))];
            foreach (var record in records)
            {
                if (!Matches(record)) continue;
                if (total >= offset && page.Count < 100) page.Add(record);
                total++;
            }
            visible = page.ToArray();
        }
        return listing with { Records = visible, Total = total, Groups = groupChoices };
    }

    private static PlaPage ReadListing(PlaSourceScope scope, EditSession session, string kind, string? textFile)
    {
        var catalog = new PlaCatalog(scope);
        var files = BuildFiles(catalog, session);
        if (files.Keys.Any(path => path.StartsWith("bin/message/", StringComparison.Ordinal)))
        {
            // Build the display catalog after composing pending messages so all
            // consumers share their labels without changing source identities.
            catalog = new PlaCatalog(scope, files);
        }
        var records = Records(catalog, kind, textFile, files, out var textFiles, out var selectedFile);
        var notices = new List<string>();
        if (kind == "moves") notices.Add("Special signature move values controlled by separate battle configuration are protected.");
        if (kind == "evolutions") notices.Add("Unqualified evolution conditions are preserved. Argument meanings depend on the selected method; form 65535 preserves the source form.");
        if (kind == "learnsets") notices.Add("Edits affect the PLA learnset and mastery levels. The separate Mainline learnset is preserved.");
        if (kind is "tutors" or "compatibility") notices.Add("Tutor identities are fixed. Changing a tutor move affects every Pokemon compatible with that slot.");
        if (PlaBattleCatalog.Supports(kind)) notices.Add("Existing party slots and event links are preserved. Starter, legendary, boss, and unqualified event records are protected. Fixed challenge and quest identities cannot be replaced. Move 0 is an unspecified move slot.");
        if (PlaEconomyCatalog.Supports(kind)) notices.Add("Existing identities, unlock conditions, and protected records are preserved. Research edits change game rules, not saved progress.");
        var fields = catalog.Fields(kind);
        var rowFields = catalog.RowFields(kind);
        return new(kind, scope.Fingerprint(), fields, rowFields, records, records.Count,
            textFiles, selectedFile, notices)
        { PokemonSpriteNames = PlaBattleCatalog.Supports(kind) || PlaWildCatalog.IsEncounter(kind) || kind is "personal" or "learnsets" or "evolutions" or "compatibility"
            ? catalog.PokemonSpriteNames() : new Dictionary<string, string>() };
    }

    private static IReadOnlyList<PlaRecord> Records(PlaCatalog catalog, string kind, string? textFile, Dictionary<string, byte[]> edited,
        out string[] textFiles, out string? selectedFile, string? selectedId = null)
    {
        textFiles = [];
        selectedFile = null;
        catalog.RefreshTutorLabels(edited);
        if (PlaExecutableCatalog.Supports(kind)) return catalog.Executable.Records(kind, edited);
        if (PlaScriptCatalog.Supports(kind)) return catalog.Scripts.Records(kind, edited);
        if (PlaSettingsCatalog.Supports(kind)) return catalog.Settings.Records(kind, edited);
        if (kind == "weather") return catalog.Weather.Records(edited, selectedId);
        if (PlaWildCatalog.Supports(kind))
        {
            if (kind is not ("encounters" or "spawns")) _ = catalog.Events;
            return catalog.Wild.Records(kind, edited, selectedId);
        }
        if (PlaEventCatalog.Supports(kind)) return catalog.Events.Records(kind, edited, selectedId);
        if (PlaEconomyCatalog.Supports(kind)) return catalog.Economy.Records(kind, edited, selectedId);
        if (kind == "text")
        {
            textFiles = catalog.Scope.TextFiles();
            selectedFile = textFile ?? textFiles.FirstOrDefault();
            if (selectedFile is null || !textFiles.Contains(selectedFile, StringComparer.Ordinal)) throw new InvalidDataException("Select a supported PLA message file.");
            var path = TextPath(catalog.Scope, selectedFile);
            var original = catalog.Scope.Read(path);
            var text = new PlaMessageFile(edited.GetValueOrDefault(path) ?? original, catalog.Scope.Read(Path.ChangeExtension(path, ".tbl")));
            var fileIdentity = selectedFile;
            return text.Lines.Select((line, index) => (line, index)).Where(row => selectedId is null || $"{catalog.Scope.Language}/{fileIdentity}:{row.index}" == selectedId).Select(row => new PlaRecord($"{catalog.Scope.Language}/{fileIdentity}:{row.index}", $"{row.index}: {text.Keys[row.index].Name}", new Dictionary<string, string> { ["text"] = row.line.Text })).ToArray();
        }
        var ids = kind switch
        {
            "moves" => catalog.Moves.Keys.Order().Select(id => id.ToString(CultureInfo.InvariantCulture)),
            "tutors" => catalog.Tutors.Keys.Order().Select(id => id.ToString(CultureInfo.InvariantCulture)),
            "trainers" or "gifts" or "static" => catalog.Battles.Ids(kind),
            _ => catalog.Pokemon.Keys,
        };
        return ids.Where(id => selectedId is null || id == selectedId).Select(id => catalog.Record(kind, id, edited.GetValueOrDefault(catalog.Locate(kind, id).Path))).ToArray();
    }

    public EditSession Stage(ProjectPaths paths, EditSession session, string kind, string id, IReadOnlyDictionary<string, string> values,
        string revision, bool restoreBase = false)
    {
        if (values is null || values.Count is < 1 or > 128 || string.IsNullOrEmpty(id) || id.Length > 512 || string.IsNullOrEmpty(revision))
            throw new InvalidDataException("The PLA stage request is invalid.");
        if (kind == "text")
        {
            var language = SplitTextId(id).File.Split('/')[0];
            if (!PlaSourceScope.Languages.Contains(language, StringComparer.Ordinal)) throw new InvalidDataException("The PLA text language identity is invalid.");
            paths = paths with { GameTextLanguage = language };
        }
        using var scope = new PlaSourceScope(paths);
        var catalog = new PlaCatalog(scope);
        // Load the same dependencies as a displayed page, including pending data.
        var files = BuildFiles(catalog, session);
        var records = Records(catalog, kind, kind == "text" ? SplitTextId(id).File[(scope.Language.Length + 1)..] : null, files, out _, out _, kind == "personal" && id == "*" ? null : id);
        var fields = catalog.Fields(kind).ToDictionary(field => field.Key, StringComparer.Ordinal);
        _ = catalog.RowFields(kind);
        if (!string.Equals(revision, scope.Fingerprint(), StringComparison.Ordinal))
            throw new InvalidDataException("The PLA source changed. Reload the editor before staging.");
        if (kind == "personal" && id == "*")
            return StageExperienceYield(catalog, session, records, values, restoreBase);
        var current = records.SingleOrDefault(record => record.Id == id) ?? throw new InvalidDataException("The PLA record is unavailable.");
        var edits = session.PendingEdits.ToList();
        foreach (var (key, requested) in values)
        {
            if (!fields.TryGetValue(key, out var field) || field.ReadOnly || current.ReadOnlyFields.Contains(key)) throw new InvalidDataException("This PLA field is protected or unsupported.");
            var value = restoreBase ? BaseValue(catalog, kind, id, key) : requested;
            value = Normalize(field, value);
            edits.RemoveAll(edit => edit.Owner == Owner && edit.Field == key && (edit.RecordId == kind + "/" + id
                || PlaWildCatalog.IsEncounter(kind) && new[] { "encounters", "outbreakEncounters", "distortionEncounters" }.Any(alias => edit.RecordId == alias + "/" + id)));
            var actual = kind == "text" ? TextRecord(catalog, id).Values[key] : catalog.Record(kind, id).Values[key];
            if (value == actual) continue;
            var path = kind == "text" ? TextPath(scope, SplitTextId(id).File) : catalog.ResourcePath(kind, id);
            edits.Add(new PendingEdit(Domain(kind), $"PLA {current.Label}: {key}", [new(ProjectFileLayer.Base, PlaSourceScope.OutputPath(path))], kind + "/" + id, key, value, Owner));
        }
        var result = session with { PendingEdits = edits };
        _ = BuildFiles(catalog, result);
        return result;
    }

    private static EditSession StageExperienceYield(PlaCatalog catalog, EditSession session, IReadOnlyList<PlaRecord> records,
        IReadOnlyDictionary<string, string> values, bool restoreBase)
    {
        const string field = "baseExperience";
        if (values.Count != 1 || !values.TryGetValue(field, out var requested) || requested != "0")
            throw new InvalidDataException("The PLA experience yield action is invalid.");
        var edits = session.PendingEdits.ToList();
        foreach (var record in records)
        {
            if (record.ReadOnlyFields.Contains(field)) continue;
            var value = restoreBase ? BaseValue(catalog, "personal", record.Id, field) : "0";
            edits.RemoveAll(edit => edit.Owner == Owner && edit.RecordId == "personal/" + record.Id && edit.Field == field);
            if (value == catalog.Record("personal", record.Id).Values[field]) continue;
            var path = catalog.ResourcePath("personal", record.Id);
            edits.Add(new PendingEdit(Domain("personal"), $"PLA {record.Label}: {field}",
                [new(ProjectFileLayer.Base, PlaSourceScope.OutputPath(path))], "personal/" + record.Id, field, value, Owner));
        }
        var result = session with { PendingEdits = edits };
        _ = BuildFiles(catalog, result);
        return result;
    }

    private static string BaseValue(PlaCatalog catalog, string kind, string id, string key)
    {
        if (kind == "text")
        {
            var (file, index) = SplitTextId(id);
            var path = TextPath(catalog.Scope, file);
            var text = new PlaMessageFile(catalog.Scope.Base(path), catalog.Scope.Base(Path.ChangeExtension(path, ".tbl")));
            if (index < 0 || index >= text.Lines.Count) throw new InvalidDataException("The PLA text line is invalid.");
            return text.Lines[index].Text;
        }
        return catalog.Record(kind, id, catalog.Scope.Base(catalog.ResourcePath(kind, id))).Values[key];
    }

    private static string Normalize(PlaField field, string value)
    {
        if (value is null || value.Length > 131072) throw new InvalidDataException("The PLA field value is too large.");
        if (field.Kind == "text") return value;
        if (field.Kind == "rows") return JsonSerializer.Serialize(JsonSerializer.Deserialize<ushort[][]>(value) ?? throw new InvalidDataException("The PLA rows are invalid."));
        if (field.Kind == "factor")
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) || !float.IsFinite(factor)
                || factor < field.Minimum || factor > field.Maximum)
                throw new InvalidDataException("The PLA factor is outside its supported range.");
            return factor.ToString("R", CultureInfo.InvariantCulture);
        }
        if (field.Kind == "decimal")
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier)
                || !float.IsFinite(multiplier) || multiplier != -1 && multiplier < 0 || multiplier > 100)
                throw new InvalidDataException("PLA eligibility multipliers must be -1 for inherited values or between 0 and 100.");
            return multiplier.ToString("R", CultureInfo.InvariantCulture);
        }
        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            || field.Minimum is { } min && number < min || field.Maximum is { } max && number > max
            || field.Choices is { } choices && !choices.Any(choice => choice.Value == number.ToString(CultureInfo.InvariantCulture)))
            throw new InvalidDataException($"The PLA value for {field.Key} is invalid.");
        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static (string File, int Index) SplitTextId(string id)
    {
        var separator = id.LastIndexOf(':');
        if (separator < 0 || !int.TryParse(id.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            throw new InvalidDataException("The PLA message identity is invalid.");
        return (id[..separator], index);
    }
    private static string TextPath(PlaSourceScope scope, string file)
    {
        var language = scope.Language;
        var firstSlash = file.IndexOf('/');
        if (firstSlash > 0 && PlaSourceScope.Languages.Contains(file[..firstSlash], StringComparer.Ordinal))
        {
            language = file[..firstSlash];
            file = file[(firstSlash + 1)..];
        }
        if (!scope.TextFiles(language).Contains(file, StringComparer.Ordinal)) throw new InvalidDataException("The PLA message resource is unsupported.");
        return $"bin/message/{language}/{file}";
    }
    private static PlaRecord TextRecord(PlaCatalog catalog, string id)
    {
        var (file, index) = SplitTextId(id);
        var path = TextPath(catalog.Scope, file);
        var message = new PlaMessageFile(catalog.Scope.Read(path), catalog.Scope.Read(Path.ChangeExtension(path, ".tbl")));
        if (index < 0 || index >= message.Lines.Count) throw new InvalidDataException("The PLA text line is invalid.");
        return new(id, message.Keys[index].Name, new Dictionary<string, string> { ["text"] = message.Lines[index].Text });
    }

    private static Dictionary<string, byte[]> BuildFiles(PlaCatalog catalog, EditSession session)
    {
        if (session.PendingEdits.Count > 10000) throw new InvalidDataException("The PLA edit session is too large.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var battles = new Dictionary<(string Kind, string Id), Dictionary<string, string>>();
        var economy = new Dictionary<(string Kind, string Id), Dictionary<string, string>>();
        var wild = new Dictionary<(string Kind, string Id), Dictionary<string, string>>();
        var events = new Dictionary<(string Kind, string Id), Dictionary<string, string>>();
        var scripts = new Dictionary<(string Kind, string Id), Dictionary<string, string>>();
        var settings = new Dictionary<(string Kind, string Id), Dictionary<string, string>>();
        var weather = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edit in session.PendingEdits.OrderBy(edit => edit.RecordId, StringComparer.Ordinal).ThenBy(edit => edit.Field, StringComparer.Ordinal))
        {
            if (edit.Domain == ModelTextureIntent.Domain) continue;
            if (edit.Owner != Owner || edit.RecordId is null || edit.Field is null || edit.NewValue is null || !seen.Add(edit.RecordId + "/" + edit.Field))
                throw new InvalidDataException("The PLA session contains unsupported or conflicting edits.");
            var slash = edit.RecordId.IndexOf('/');
            if (slash < 0) throw new InvalidDataException("The PLA edit identity is invalid.");
            var kind = edit.RecordId[..slash];
            var id = edit.RecordId[(slash + 1)..];
            if (edit.Domain != Domain(kind)) throw new InvalidDataException("The PLA edit domain is invalid.");
            var field = catalog.Fields(kind).SingleOrDefault(field => field.Key == edit.Field) ?? throw new InvalidDataException("The PLA field is unsupported.");
            var value = Normalize(field, edit.NewValue);
            if (PlaExecutableCatalog.Supports(kind))
            {
                var path = PlaExecutableCatalog.PathFor(kind, id);
                files[path] = catalog.Executable.Edit(kind, id, files.GetValueOrDefault(path) ?? catalog.Scope.Read(path),
                    new Dictionary<string, string> { [field.Key] = value });
                continue;
            }
            if (kind == "weather")
            {
                if (!weather.TryGetValue(id, out var values)) weather[id] = values = new(StringComparer.Ordinal);
                values.Add(field.Key, value);
                continue;
            }
            if (PlaScriptCatalog.Supports(kind))
            {
                var key = (kind, id);
                if (!scripts.TryGetValue(key, out var values)) scripts[key] = values = new(StringComparer.Ordinal);
                values.Add(field.Key, value);
                continue;
            }
            if (PlaSettingsCatalog.Supports(kind))
            {
                var key = (kind, id);
                if (!settings.TryGetValue(key, out var values)) settings[key] = values = new(StringComparer.Ordinal);
                values.Add(field.Key, value);
                continue;
            }
            if (PlaEventCatalog.Supports(kind))
            {
                var key = (kind, id);
                if (!events.TryGetValue(key, out var values)) events[key] = values = new(StringComparer.Ordinal);
                values.Add(field.Key, value);
                continue;
            }
            if (PlaWildCatalog.Supports(kind))
            {
                if (kind is not ("encounters" or "spawns")) _ = catalog.Events;
                var key = (kind, id);
                if (!wild.TryGetValue(key, out var values)) wild[key] = values = new(StringComparer.Ordinal);
                values.Add(field.Key, value);
                continue;
            }
            if (PlaEconomyCatalog.Supports(kind))
            {
                var key = (kind, id);
                if (!economy.TryGetValue(key, out var values)) economy[key] = values = new(StringComparer.Ordinal);
                values.Add(field.Key, value);
                continue;
            }
            if (PlaBattleCatalog.Supports(kind))
            {
                var key = (kind, id);
                if (!battles.TryGetValue(key, out var values)) battles[key] = values = new(StringComparer.Ordinal);
                values.Add(field.Key, value);
                continue;
            }
            if (kind == "text")
            {
                var (file, index) = SplitTextId(id);
                var path = TextPath(catalog.Scope, file);
                var original = catalog.Scope.Read(path);
                var text = new PlaMessageFile(files.GetValueOrDefault(path) ?? original, catalog.Scope.Read(Path.ChangeExtension(path, ".tbl")));
                files[path] = text.Write(new Dictionary<int, string> { [index] = value });
                continue;
            }
            var source = catalog.Locate(kind, id);
            var record = catalog.Record(kind, id);
            if (record.ReadOnlyFields.Contains(field.Key)) throw new InvalidDataException("This PLA move field is controlled by unqualified battle configuration.");
            var buffer = new PlaFlatBuffer(files.GetValueOrDefault(source.Path) ?? catalog.Scope.Read(source.Path));
            var table = buffer.TableAtReference(source.Reference);
            if (field.Kind == "rows")
            {
                var rows = JsonSerializer.Deserialize<ushort[][]>(value)!;
                ValidateRows(catalog, kind, id, rows, buffer, source.Reference);
                var old = PlaCatalog.Rows(buffer, source.Reference, kind == "learnsets" ? 3 : 2, kind == "learnsets" ? 3 : 5);
                if (!RowsEqual(rows, old)) buffer.ReplaceUShortRows(source.Reference, kind == "learnsets" ? 3 : 2, rows, kind == "learnsets" ? 3 : 5);
            }
            else
            {
                var number = long.Parse(value, CultureInfo.InvariantCulture);
                int index;
                int[] widths;
                switch (kind)
                {
                    case "personal": index = PlaCatalog.PersonalFields[field.Key]; widths = PlaCatalog.PersonalWidths; break;
                    case "moves": index = PlaCatalog.MoveFields[field.Key]; widths = PlaCatalog.MoveWidths; break;
                    case "tutors": index = field.Key == "move" ? 1 : 2; widths = [4, 4, 4, 8]; break;
                    case "compatibility":
                        var slot = int.Parse(field.Key.AsSpan(6), CultureInfo.InvariantCulture) - 1;
                        index = 56 + slot / 32;
                        var mask = 1L << (slot % 32);
                        number = (buffer.Scalar(table, index, 4) & ~mask) | (number == 1 ? mask : 0);
                        widths = PlaCatalog.PersonalWidths;
                        break;
                    default: throw new InvalidDataException("The PLA scalar editor is unsupported.");
                }
                buffer.SetScalars(source.Reference, widths, new Dictionary<int, long> { [index] = number });
            }
            files[source.Path] = buffer.ToArray();
        }
        foreach (var (key, values) in battles)
        {
            var source = catalog.Locate(key.Kind, key.Id);
            files[source.Path] = catalog.Battles.Edit(key.Kind, key.Id, files.GetValueOrDefault(source.Path) ?? catalog.Scope.Read(source.Path), values);
        }
        foreach (var (key, values) in economy)
        {
            var path = catalog.ResourcePath(key.Kind, key.Id);
            files[path] = catalog.Economy.Edit(key.Kind, key.Id, files.GetValueOrDefault(path) ?? catalog.Scope.Read(path), values);
        }
        var members = wild.Count > 0 ? catalog.Wild.EditMembers(wild) : new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (key, values) in scripts) files[catalog.Scripts.PathFor(key.Kind, key.Id)] = catalog.Scripts.Edit(key.Kind, key.Id, values);
        foreach (var (key, values) in settings) files[catalog.Settings.PathFor(key.Kind, key.Id)] = catalog.Settings.Edit(key.Kind, key.Id, values);
        if (events.Count > 0) catalog.Events.Edit(events, files, members);
        if (weather.Count > 0) members.Add(PlaWeatherCatalog.MemberPath, catalog.Weather.Edit(weather));
        if (members.Count > 0) files[PlaWildCatalog.ArchivePath] = catalog.Wild.Compose(members);
        if (economy.Count > 0) catalog.Economy.ValidateComposition(files);
        foreach (var (path, bytes) in PlaModelEditSessionService.BuildFiles(catalog.Scope, session.PendingEdits.Where(edit => edit.Domain == ModelTextureIntent.Domain)))
            if (!files.TryAdd(path, bytes)) throw new InvalidDataException("The PLA model output conflicts with another editor.");
        ValidateComposition(catalog, files);
        // A no-op never creates output or claims ownership.
        foreach (var path in files.Keys.ToArray())
            if (files[path].AsSpan().SequenceEqual(catalog.Scope.Read(path))) files.Remove(path);
        return files;
    }

    private static bool RowsEqual(ushort[][] left, ushort[][] right) => left.Length == right.Length && left.Zip(right).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second));

    private static void ValidateRows(PlaCatalog catalog, string kind, string id, ushort[][] rows, PlaFlatBuffer buffer, int reference)
    {
        var columns = kind == "learnsets" ? 3 : 5;
        if (rows.Length > (kind == "learnsets" ? 256 : 32) || rows.Any(row => row is null || row.Length != columns))
            throw new InvalidDataException("The PLA row collection is invalid.");
        if (kind == "learnsets")
        {
            if (rows.Any(row => row[0] > 100 || row[1] > 100 || row[1] != 0 && row[1] < row[0] || !catalog.Moves.ContainsKey(row[2]))
                || rows.Select(row => row[2]).Distinct().Count() != rows.Length
                || !rows.Select(row => row[0]).SequenceEqual(rows.Select(row => row[0]).Order()))
                throw new InvalidDataException("PLA learnsets require usable unique moves, ascending levels, and valid mastery levels (0 means no level requirement).");
            return;
        }
        var old = PlaCatalog.Rows(buffer, reference, 2, 5);
        var protectedOld = old.Where(row => !PlaCatalog.EvolutionMethods.ContainsKey(row[0])).Select(row => JsonSerializer.Serialize(row)).Order(StringComparer.Ordinal).ToArray();
        var protectedNew = rows.Where(row => !PlaCatalog.EvolutionMethods.ContainsKey(row[0])).Select(row => JsonSerializer.Serialize(row)).Order(StringComparer.Ordinal).ToArray();
        if (!protectedOld.SequenceEqual(protectedNew)) throw new InvalidDataException("Unqualified PLA evolution conditions must remain unchanged.");
        foreach (var row in rows.Where(row => PlaCatalog.EvolutionMethods.ContainsKey(row[0])))
        {
            var form = row[3] == ushort.MaxValue ? id.Split(':')[1] : row[3].ToString(CultureInfo.InvariantCulture);
            if (row[4] > 100 || !catalog.Pokemon.ContainsKey($"{row[2]}:{form}") || $"{row[2]}:{form}" == id)
                throw new InvalidDataException("The PLA evolution target, form, or level is invalid.");
            if (row[0] == 21 && !catalog.Moves.ContainsKey(row[1])
                || row[0] == 22 && !catalog.Pokemon.Keys.Any(key => key.StartsWith(row[1] + ":", StringComparison.Ordinal))
                || row[0] is 8 or 17 or 18 && !catalog.EvolutionItems().Contains(row[1])
                || row[0] is 1 or 2 or 3 or 4 or 23 or 24 && row[1] != 0)
                throw new InvalidDataException("The PLA evolution argument is invalid.");
        }
    }

    private static void ValidateComposition(PlaCatalog catalog, IReadOnlyDictionary<string, byte[]> files)
    {
        if (files.TryGetValue(PlaSourceScope.TutorsPath, out var tutors))
        {
            var table = new PlaFlatBuffer(tutors);
            var moves = table.VectorReferences(table.Root, 0, 64).Select(reference => table.Scalar(table.TableAtReference(reference), 1, 4)).ToArray();
            if (moves.Distinct().Count() != moves.Length) throw new InvalidDataException("PLA tutor slots must offer distinct usable moves.");
        }
        if (!files.TryGetValue(PlaSourceScope.EvolutionsPath, out var evolutions)) return;
        var buffer = new PlaFlatBuffer(evolutions);
        var graph = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var reference in buffer.VectorReferences(buffer.Root, 0, 4096))
        {
            var id = PlaCatalog.Identity(buffer, buffer.TableAtReference(reference));
            if (!catalog.Pokemon.ContainsKey(id)) continue;
            graph[id] = PlaCatalog.Rows(buffer, reference, 2, 5).Select(row => $"{row[2]}:{(row[3] == ushort.MaxValue ? id.Split(':')[1] : row[3].ToString(CultureInfo.InvariantCulture))}")
                .Where(catalog.Pokemon.ContainsKey).ToArray();
        }
        var complete = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (complete.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("The PLA evolution edits create a cycle.");
            foreach (var next in graph.GetValueOrDefault(id) ?? []) Visit(next);
            visiting.Remove(id);
            complete.Add(id);
        }
        foreach (var id in graph.Keys) Visit(id);
    }

    private static Prepared Prepare(PlaSourceScope scope, EditSession session)
    {
        var catalog = new PlaCatalog(scope);
        var files = BuildFiles(catalog, session);
        var sourceFingerprint = scope.Fingerprint();
        var writes = files.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new PlannedFileWrite(PlaSourceScope.OutputPath(pair.Key),
            [new(scope.OutputState(pair.Key).Exists ? ProjectFileLayer.Layered : ProjectFileLayer.Base, PlaSourceScope.OutputPath(pair.Key))], scope.OutputState(pair.Key).Exists,
            "PLA data edits", PlaSourceScope.Hash(Encoding.UTF8.GetBytes(sourceFingerprint + ":" + pair.Key + ":" + PlaSourceScope.Hash(pair.Value)))))
            .ToArray();
        return new(new ChangePlan(session.Id, writes, []), files);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        try
        {
            using var scope = new PlaSourceScope(paths);
            if (string.IsNullOrWhiteSpace(paths.OutputRootPath)) throw new InvalidDataException("Choose an Output Root before reviewing PLA edits.");
            return Prepare(scope, session).Plan;
        }
        catch (Exception exception) when (Expected(exception)) { return new(session.Id, [], [Error(exception.Message)]); }
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        try
        {
            using var scope = new PlaSourceScope(paths);
            if (string.IsNullOrWhiteSpace(paths.OutputRootPath)) throw new InvalidDataException("Choose an Output Root before applying PLA edits.");
            var prepared = Prepare(scope, session);
            if (!ChangePlanReview.Matches(reviewed, prepared.Plan))
                return new(id, now, [], new(id, now, []), [Error("PLA sources or edits changed after Review. Review the current changes again.", "KM-PLA-STALE-REVIEW")]);
            if (prepared.Files.Count == 0) return new(id, now, [], new(id, now, []), []);
            var coordinator = OutputTransactionCoordinator.ForProject(paths);
            var inventory = coordinator.GetOwnershipInventorySnapshotAsync().GetAwaiter().GetResult();
            var mutations = prepared.Files.Select(pair =>
            {
                var path = new RelativeOutputPath(PlaSourceScope.OutputPath(pair.Key));
                var claim = new OwnedTarget(GameFamily.LegendsArceus, new OwnedTargetAddress(path), new OwnershipOwnerId(Owner),
                    new PreservationRuleDescriptor(pair.Key == PlaSourceScope.ExecutablePath ? "pla.executable-preserve" : "pla.loose-preserve", 1, true, true));
                var existing = inventory.Inventory.Files.SingleOrDefault(file => file.Path.CanonicalKey == path.CanonicalKey);
                var claims = existing is not null && existing.CurrentState == scope.OutputState(pair.Key) ? existing.Claims.Append(claim).Distinct().ToArray() : [claim];
                return OutputMutation.Write(path, pair.Value, scope.OutputState(pair.Key), claims);
            }).ToArray();
            var plan = new OutputApplyPlan(ProjectIdentity.FromPaths(paths), GameFamily.LegendsArceus, "standalone",
                PlaSourceScope.Hash(Encoding.UTF8.GetBytes(string.Join('\n', prepared.Plan.Writes.Select(write => write.SourceFingerprint)))),
                [new(OutputApplyOriginKind.Workflow, Owner)], mutations, scope.Dependencies, ownershipInventoryRevision: inventory.Revision);
            var transaction = coordinator.ApplyAsync(plan).GetAwaiter().GetResult();
            var committed = transaction.Outcome == OutputApplyOutcome.Committed;
            return new(id, now, committed ? prepared.Plan.Writes.Select(write => new ProjectFileReference(ProjectFileLayer.Layered, write.TargetRelativePath)).ToArray() : [],
                new(id, now, committed ? prepared.Plan.Writes : []), committed ? [] : [Error("The PLA output transaction did not commit. Check Output recovery before trying again.", "KM-PLA-APPLY-FAILED")], transaction);
        }
        catch (Exception exception) when (Expected(exception)) { return new(id, now, [], new(id, now, []), [Error(exception.Message, "KM-PLA-APPLY-FAILED")]); }
    }
}
