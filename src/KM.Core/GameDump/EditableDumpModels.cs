// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.Core.GameDump;

public sealed record DumpLocation(string File, int Line, int Column, string Field);

public sealed record DumpIssue(string Code, string Message, DumpLocation Location, string? Expected = null)
{
    public ValidationDiagnostic ToDiagnostic() => new(
        DiagnosticSeverity.Error,
        $"{Location.File}, line {Location.Line}, column {Location.Column}, field '{Location.Field}': {Message}",
        File: Location.File, Domain: "dumpImport", Field: Location.Field, Expected: Expected)
        { Code = Code, SourceLine = Location.Line, SourceColumn = Location.Column };
}

public sealed record DumpChoice(string Value, string Label)
{
    public string Name => Label.StartsWith(Value + " ", StringComparison.Ordinal) ? Label[(Value.Length + 1)..] : Label;
}

public sealed record DumpField(
    string Key,
    string Target,
    string Value,
    string Kind = "integer",
    double? Minimum = null,
    double? Maximum = null,
    IReadOnlyList<DumpChoice>? Choices = null,
    bool Editable = true)
{
    public string DisplayValue
    {
        get
        {
            var option = Choices?.FirstOrDefault(choice => choice.Value == Value);
            return option is not null && Choices!.Count(choice =>
                string.Equals(choice.Name, option.Name, StringComparison.OrdinalIgnoreCase)) == 1
                ? option.Name : Value;
        }
    }

    public string? Parse(string value, out string? problem)
    {
        problem = null;
        var choices = Choices?.Where(choice => string.Equals(choice.Label, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(choice.Name, value, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (choices?.Length > 1)
        {
            problem = "This name is ambiguous. Use the numeric value for the intended entry.";
            return null;
        }
        if (choices?.Length == 1) value = choices[0].Value;
        if (Kind == "text")
        {
            if (Choices is { Count: > 0 } && !Choices.Any(choice => choice.Value == value))
            {
                problem = "Choose one of the values supported by this field.";
                return null;
            }
            return value;
        }
        if (Kind == "boolean")
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) value = "1";
            else if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) value = "0";
        }
        var style = Kind == "number" ? NumberStyles.Float : NumberStyles.AllowLeadingSign;
        if (!double.TryParse(value, style, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number) || Kind != "number" && number != Math.Truncate(number))
        {
            problem = Kind == "number" ? "Enter a finite number." : "Enter a whole number or a recognized name.";
            return null;
        }
        if (Kind == "boolean" && number is not (0 or 1)
            || Minimum is not null && number < Minimum || Maximum is not null && number > Maximum)
        {
            problem = $"Value is outside the allowed range ({Minimum?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"} to {Maximum?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"}).";
            return null;
        }
        if (Kind != "number" && !decimal.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            problem = "Enter a whole number within the supported range.";
            return null;
        }
        var normalized = Kind == "number" ? number.ToString("R", CultureInfo.InvariantCulture)
            : decimal.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture).ToString("0", CultureInfo.InvariantCulture);
        if (Choices is { Count: > 0 } && !Choices.Any(choice => choice.Value == normalized))
        {
            problem = "Choose one of the values supported by this field.";
            return null;
        }
        return normalized;
    }
}

public sealed class DumpRecord(string id, string name, string route, string targetId, int? slot = null)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Route { get; } = route;
    public string TargetId { get; } = targetId;
    public int? Slot { get; } = slot;
    public List<DumpField> Fields { get; } = [];
    public Dictionary<string, List<DumpRecord>> Children { get; } = new(StringComparer.Ordinal);
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { Id, Name, Route, TargetId, Slot, Fields = Fields.Select(f => new { f.Key, f.Value }) }))));
}

public sealed record EditableDumpDocument(
    string Game,
    string Category,
    string Language,
    IReadOnlyList<DumpRecord> Records,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public string? RequestedLanguage { get; init; }
    public int SourceFileCount { get; init; }
}

public sealed record DumpUpdate(DumpRecord Record, DumpField Field, string Value, DumpLocation Location);

public sealed record DumpStageResult(EditSession Session, IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record DumpImportRow(
    string RecordId,
    string Name,
    DumpLocation Location,
    string Status,
    IReadOnlyList<DumpUpdate> Updates,
    IReadOnlyList<DumpIssue> Issues);

public sealed record DumpImportResult(
    EditSession Session,
    IReadOnlyList<DumpImportRow> Rows,
    IReadOnlyList<DumpIssue> Issues,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public interface IEditableDumpProvider
{
    EditableDumpDocument Load(string category, string? language = null);
    DumpStageResult Stage(string category, IReadOnlyList<DumpUpdate> updates, EditSession session);
}
