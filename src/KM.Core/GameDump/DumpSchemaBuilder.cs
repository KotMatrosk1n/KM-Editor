// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KM.Core.GameDump;

public sealed record DumpFieldDefinition(string Field, string Kind, double? Minimum, double? Maximum,
    IReadOnlyList<DumpChoice> Choices, bool ReadOnly);

public static class DumpSchemaBuilder
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<DumpFieldDefinition> Definitions<T>(IEnumerable<T> definitions) =>
        definitions.Select(definition =>
        {
            var value = JsonSerializer.SerializeToElement(definition, Options);
            return new DumpFieldDefinition(Read(value, "field")!, Read(value, "valueKind") ?? "integer",
                Number(value, "minimumValue"), Number(value, "maximumValue"), Choices(value, "options"),
                Read(value, "isReadOnly") == "1");
        }).ToArray();

    public static JsonElement Data<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static void Fields(DumpRecord record, JsonElement data, IReadOnlyList<DumpFieldDefinition> definitions,
        IReadOnlyDictionary<string, string>? paths = null, IReadOnlySet<string>? included = null)
    {
        foreach (var field in definitions)
        {
            if (included is not null && !included.Contains(field.Field)) continue;
            var value = Read(data, paths?.GetValueOrDefault(field.Field) ?? field.Field);
            if (value is null) continue;
            var choices = field.Choices;
            var kind = field.Kind switch { "boolean" => "boolean", "float" or "decimal" or "number" => "number", "text" or "string" or "hash" => "text", _ => "integer" };
            record.Fields.Add(new(Key(field.Field), field.Field, value, kind, field.Minimum, field.Maximum, choices, !field.ReadOnly));
        }
    }

    public static IReadOnlyList<DumpChoice> Choices(JsonElement data, string path)
    {
        var array = Element(data, path);
        return array is { ValueKind: JsonValueKind.Array }
            ? array.Value.EnumerateArray().Select(option => new DumpChoice(Read(option, "value")!, Read(option, "label")!)).ToArray()
            : [];
    }

    public static string? Read(JsonElement data, string path)
    {
        var value = Element(data, path);
        return value?.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null,
        };
    }

    public static JsonElement? Element(JsonElement value, string path)
    {
        foreach (var part in path.Split('.'))
        {
            if (value.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index))
            {
                if (index < 0 || index >= value.GetArrayLength()) return null;
                value = value[index];
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                var match = value.EnumerateObject().FirstOrDefault(property => string.Equals(property.Name, part, StringComparison.OrdinalIgnoreCase));
                if (match.Value.ValueKind == JsonValueKind.Undefined) return null;
                value = match.Value;
            }
            else return null;
        }
        return value;
    }

    public static string Key(string field) => Regex.Replace(Regex.Replace(field, "([A-Z]+)([A-Z][a-z])", "$1_$2"), "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();

    public static string Value(object value) => Convert.ToString(value, CultureInfo.InvariantCulture)!;

    public static DumpRecord Record<T>(T row, IReadOnlyList<DumpFieldDefinition> fields, string id, string name,
        string route, IReadOnlyDictionary<string, string>? paths = null, string? targetId = null, int? slot = null)
    {
        var data = Data(row);
        var identity = Read(data, id) ?? throw new InvalidOperationException($"Missing dump identity: {id}.");
        var record = new DumpRecord(identity, Read(data, name) ?? identity, route, targetId ?? identity, slot);
        Fields(record, data, fields, paths);
        return record;
    }

    public static DumpField Field(DumpFieldDefinition definition, string value, bool? editable = null) =>
        new(Key(definition.Field), definition.Field, value,
            definition.Kind switch { "boolean" => "boolean", "float" or "decimal" or "number" => "number", "text" or "string" or "hash" => "text", _ => "integer" },
            definition.Minimum, definition.Maximum, definition.Choices, editable ?? !definition.ReadOnly);

    private static double? Number(JsonElement value, string path) =>
        double.TryParse(Read(value, path), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}
