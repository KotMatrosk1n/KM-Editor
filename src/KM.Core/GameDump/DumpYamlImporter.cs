// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.Core.GameDump;

public sealed class DumpYamlImporter(IEditableDumpProvider provider)
{
    public const string OwnerPrefix = "workflow.dump-import.yaml:";

    public DumpImportResult Preview(string path, string game, EditSession? session)
    {
        var original = session ?? EditSession.Start();
        var issues = new List<DumpIssue>();
        var rows = new List<DumpImportRow>();
        var diagnostics = new List<ValidationDiagnostic>();
        var file = Path.GetFileName(path);
        var categoryLocation = new DumpLocation(file, 1, 1, "category");
        try
        {
            var root = DumpYamlReader.ReadFile(path);
            var header = Map(root);
            CheckKeys(root, ["format", "version", "game", "category", "language", "document_id", "records"], issues);
            if (Text(header, "format", root) != "km-editor-dump") issues.Add(Issue(root, "format", "This is not a KM Editor YAML dump."));
            if (Text(header, "version", root) != "1") issues.Add(Issue(root, "version", "This dump format version is not supported."));
            if (Text(header, "game", root) != game) issues.Add(Issue(root, "game", "This file belongs to a different game."));
            var category = Text(header, "category", root);
            categoryLocation = header["category"].Location;
            var language = Text(header, "language", root);
            if (!SupportedLanguages.Contains(language) || game != "za" && LatinAmericanLanguages.Contains(language))
                issues.Add(Issue(root, "language", "This game text language is not supported."));
            var documentId = Text(header, "document_id", root);
            var identity = documentId.Split('/');
            if (documentId.Length > 256 || identity.Length != 4 || identity[0] != game || identity[1] != category || identity[2] != language
                || !int.TryParse(identity.ElementAtOrDefault(3), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var part) || part < 1)
                issues.Add(Issue(root, "document_id", "Keep the document identity from the export."));
            if (issues.Count > 0) return Result(original);
            var document = provider.Load(category, language);
            diagnostics.AddRange(document.Diagnostics);
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return Result(original);
            if (!header.TryGetValue("records", out var records)) throw DumpYamlReader.Error(root.Location with { Field = "records" }, "The records list is missing.");
            CompareRecords(records, document.Records, rows, issues);
            if (issues.Count > 0 || rows.Any(row => row.Issues.Count > 0)) return Result(original, staged: false);

            var owner = OwnerPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(documentId)));
            var current = original with { PendingEdits = original.PendingEdits.Where(edit => edit.ImportOwner != owner).ToArray() };
            var targets = rows.SelectMany(row => row.Updates).GroupBy(update =>
                (update.Record.Route, update.Record.TargetId, update.Record.Slot, update.Field.Target)).ToArray();
            foreach (var target in targets.Where(group => group.Select(update => update.Value).Distinct(StringComparer.Ordinal).Count() > 1))
                foreach (var update in target)
                    issues.Add(new("KM-DUMP-YAML-VALUE", "This shared field has conflicting values in the same file.", update.Location));
            if (issues.Count > 0) return Result(original, staged: false);
            var updates = targets.Select(group => group.First()).ToArray();
            if (updates.Length == 0) return Result(current);
            if (category == "typeChart" && current.PendingEdits.Any(edit => edit.Domain == "workflow.typeChart"))
            {
                issues.Add(new("KM-DUMP-YAML-EDITOR-REJECTED",
                    "Type Chart is staged as one complete table. Apply or discard the other pending chart before importing this document.", updates[0].Location));
                return Result(original, staged: false);
            }
            var staged = provider.Stage(category, updates, current);
            var failures = staged.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (failures.Length > 0)
            {
                var attempts = 0;
                foreach (var (diagnostic, affectedUpdates) in LocateFailures(updates, failures))
                {
                    var affected = affectedUpdates.Where(update => diagnostic.Field is not null
                        && (update.Field.Target == diagnostic.Field || update.Field.Key == diagnostic.Field)).ToArray();
                    if (affected.Length == 0) affected = affectedUpdates;
                    foreach (var update in affected)
                        issues.Add(new("KM-DUMP-YAML-EDITOR-REJECTED", diagnostic.Message, update.Location, diagnostic.Expected));
                }
                return Result(original, staged: false);

                IEnumerable<(ValidationDiagnostic Diagnostic, DumpUpdate[] Updates)> LocateFailures(DumpUpdate[] subset, ValidationDiagnostic[] errors)
                {
                    // Keep interdependent fields for one editor record together. Isolate backend failures without replaying every valid row.
                    var groups = subset.GroupBy(update => update.Record.TargetId).ToArray();
                    if (groups.Length > 1 && attempts < 24)
                    {
                        var halves = new[] { groups.Take(groups.Length / 2), groups.Skip(groups.Length / 2) };
                        var found = false;
                        foreach (var half in halves)
                        {
                            var candidate = half.SelectMany(group => group).ToArray();
                            attempts++;
                            var errorsInHalf = provider.Stage(category, candidate, current).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                            if (errorsInHalf.Length == 0) continue;
                            found = true;
                            foreach (var failure in LocateFailures(candidate, errorsInHalf)) yield return failure;
                        }
                        if (found) yield break;
                    }
                    foreach (var error in errors) yield return (error, subset);
                }
            }
            diagnostics.AddRange(staged.Diagnostics);
            var before = current.PendingEdits.ToDictionary(EditKey, StringComparer.Ordinal);
            var owned = staged.Session.PendingEdits.Select(edit =>
                !before.TryGetValue(EditKey(edit), out var existing) || existing.NewValue != edit.NewValue
                    ? edit with { ImportOwner = owner } : edit).ToArray();
            return Result(staged.Session with { PendingEdits = owned });
        }
        catch (DumpYamlException exception) { issues.Add(exception.Issue); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            issues.Add(new("KM-DUMP-YAML-SOURCE", "The file could not be read as UTF-8 YAML.", new(file, 1, 1, "document")));
        }
        catch (ArgumentException exception)
        {
            issues.Add(new("KM-DUMP-YAML-CATEGORY", exception.Message, categoryLocation));
        }
        return Result(original, staged: false);

        DumpImportResult Result(EditSession result, bool staged = true) => new(result,
            staged ? rows : rows.Select(row => row with { Status = row.Issues.Count > 0 ? "rejected" : "skipped" }).ToArray(),
            issues, diagnostics);
    }

    private static string EditKey(PendingEdit edit) => $"{edit.Domain}\0{edit.RecordId}\0{edit.Field}";

    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "English", "en", "Spanish", "es", "French", "fr", "German", "de", "Italian", "it",
        "JPN", "ja", "jp", "japanese", "JPN_KANJI", "ja-kanji", "ja_kanji", "jpn-kanji", "japanese-kanji", "japanese_kanji",
        "Korean", "ko", "kr", "Simp_Chinese", "zh", "zh-cn", "zh-hans", "cn", "simplifiedchinese", "simplified_chinese",
        "Trad_Chinese", "zh-tw", "zh-hant", "tw", "traditionalchinese", "traditional_chinese",
        "LATAM", "es-419", "latinamerican", "latin_american", "latin-american",
    };

    private static readonly HashSet<string> LatinAmericanLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "LATAM", "es-419", "latinamerican", "latin_american", "latin-american",
    };

    private static void CompareRecords(DumpYamlNode input, IReadOnlyList<DumpRecord> existing,
        List<DumpImportRow> rows, List<DumpIssue> issues)
    {
        if (input.Sequence is null) throw DumpYamlReader.Error(input.Location, "Expected a list of records.");
        var lookup = existing.ToDictionary(record => record.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in input.Sequence)
        {
            var problems = new List<DumpIssue>();
            var updates = new List<DumpUpdate>();
            var map = Map(entry);
            var id = Text(map, "id", entry);
            if (!seen.Add(id)) { issues.Add(Issue(entry, "id", "This record is listed more than once.", "KM-DUMP-YAML-DUPLICATE")); continue; }
            if (!lookup.TryGetValue(id, out var current)) { issues.Add(Issue(entry, "id", "The record does not exist in the selected game.", "KM-DUMP-YAML-RECORD")); continue; }
            CheckKeys(entry, new[] { "id", "name", "source", "values", "read_only" }.Concat(current.Children.Keys).ToArray(), problems);
            if (map.TryGetValue("name", out var name) && name.Scalar != current.Name)
                problems.Add(Issue(entry, "name", "The record name is reference information. Keep the exported name.", "KM-DUMP-YAML-READONLY"));
            if (Text(map, "source", entry) != current.Fingerprint)
                problems.Add(Issue(entry, "source", "The source record has changed since export. Export it again before editing.", "KM-DUMP-YAML-STALE"));
            foreach (var group in new[] { "values", "read_only" })
            {
                if (!map.TryGetValue(group, out var values)) continue;
                var editable = group == "values";
                var fields = current.Fields.Where(field => field.Editable == editable).ToDictionary(field => field.Key, StringComparer.Ordinal);
                foreach (var pair in Map(values))
                {
                    var node = pair.Value;
                    if (!fields.TryGetValue(pair.Key, out var field))
                    {
                        problems.Add(new("KM-DUMP-YAML-FIELD", "This field is not supported here. Check its name and record type.", node.Location));
                        continue;
                    }
                    if (node.Scalar is null || node.IsNull)
                    {
                        problems.Add(new("KM-DUMP-YAML-VALUE", "Enter an explicit field value. Null values and nested objects are not accepted here.", node.Location));
                        continue;
                    }
                    if (node.Scalar == field.Value || node.Scalar == field.DisplayValue) continue;
                    var value = field.Parse(node.Scalar, out var problem);
                    if (problem is not null) problems.Add(new("KM-DUMP-YAML-VALUE", problem, node.Location));
                    else if (value != field.Value)
                    {
                        if (!field.Editable) problems.Add(new("KM-DUMP-YAML-READONLY", "This field is read only.", node.Location));
                        else updates.Add(new(current, field, value!, node.Location));
                    }
                }
            }
            rows.Add(new(current.Id, current.Name, entry.Location, problems.Count > 0 ? "rejected" : updates.Count > 0 ? "accepted" : "skipped", updates, problems));
            foreach (var child in current.Children)
                if (map.TryGetValue(child.Key, out var children)) CompareRecords(children, child.Value, rows, issues);
        }
    }

    private static Dictionary<string, DumpYamlNode> Map(DumpYamlNode node) =>
        node.Mapping ?? throw DumpYamlReader.Error(node.Location, "Expected named fields.");

    private static string Text(Dictionary<string, DumpYamlNode> map, string key, DumpYamlNode parent) =>
        map.TryGetValue(key, out var value) && value.Scalar is not null && !value.IsNull ? value.Scalar
            : throw DumpYamlReader.Error(parent.Location with { Field = parent.Location.Field + "." + key }, $"The '{key}' field is missing or is not a text value.");

    private static DumpIssue Issue(DumpYamlNode parent, string key, string message, string code = "KM-DUMP-YAML-HEADER") =>
        new(code, message, parent.Mapping?.GetValueOrDefault(key)?.Location ?? parent.Location with { Field = key });

    private static void CheckKeys(DumpYamlNode node, IReadOnlyCollection<string> allowed, List<DumpIssue> issues)
    {
        foreach (var pair in Map(node))
            if (!allowed.Contains(pair.Key)) issues.Add(new("KM-DUMP-YAML-FIELD", "Unknown field. Check the spelling and indentation.", pair.Value.Location));
    }
}
