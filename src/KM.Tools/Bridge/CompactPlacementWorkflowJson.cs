// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Api.Bridge;
using KM.Api.Placement;

namespace KM.Tools.Bridge;

// Share repeated descriptions and choice lists within a bulk response, keeping
// every object and value. Catalogs are never retained across requests.
internal sealed class CompactPlacementWorkflowJson : JsonConverter<PlacementWorkflowDto>
{
    public override PlacementWorkflowDto? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => throw new NotSupportedException("Compact placement workflows are response-only.");

    public override void Write(Utf8JsonWriter writer, PlacementWorkflowDto value, JsonSerializerOptions options)
    {
        var fields = new FieldWriter();
        var fieldOptions = new JsonSerializerOptions(BridgeJson.SerializerOptions);
        fieldOptions.Converters.Add(fields);
        writer.WriteStartObject();
        var header = JsonSerializer.SerializeToElement(value with { Objects = [] }, BridgeJson.SerializerOptions);
        foreach (var property in header.EnumerateObject())
        {
            if (property.Name != "objects") { property.WriteTo(writer); continue; }
            writer.WritePropertyName("objects");
            writer.WriteStartArray();
            foreach (var item in value.Objects) JsonSerializer.Serialize(writer, item, fieldOptions);
            writer.WriteEndArray();
        }
        writer.WritePropertyName("fieldDefinitions");
        JsonSerializer.Serialize(writer, fields.Definitions, BridgeJson.SerializerOptions);
        writer.WriteEndObject();
    }

    private sealed record Definition(string Field, string Label, string Group, bool IsReadOnly,
        string ValueKind, double MinimumValue, double MaximumValue, string Description,
        IReadOnlyList<PlacementEditableFieldOptionDto>? Options);

    private sealed class DefinitionComparer : IEqualityComparer<Definition>
    {
        public bool Equals(Definition? left, Definition? right) => ReferenceEquals(left, right)
            || left is not null && right is not null
            && left.Field == right.Field && left.Label == right.Label && left.Group == right.Group
            && left.IsReadOnly == right.IsReadOnly && left.ValueKind == right.ValueKind
            && left.MinimumValue.Equals(right.MinimumValue) && left.MaximumValue.Equals(right.MaximumValue)
            && left.Description == right.Description
            && (ReferenceEquals(left.Options, right.Options)
                || left.Options is not null && right.Options is not null && left.Options.SequenceEqual(right.Options));

        public int GetHashCode(Definition value)
        {
            var hash = new HashCode();
            hash.Add(value.Field); hash.Add(value.Label); hash.Add(value.Group); hash.Add(value.IsReadOnly);
            hash.Add(value.ValueKind); hash.Add(value.MinimumValue); hash.Add(value.MaximumValue); hash.Add(value.Description);
            hash.Add(value.Options is null);
            if (value.Options is not null) foreach (var option in value.Options) hash.Add(option);
            return hash.ToHashCode();
        }
    }

    private sealed class FieldWriter : JsonConverter<PlacementFieldValueDto>
    {
        internal List<Definition> Definitions { get; } = [];
        private readonly Dictionary<Definition, int> ids = new(new DefinitionComparer());

        public override PlacementFieldValueDto? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Compact placement fields are response-only.");

        public override void Write(Utf8JsonWriter writer, PlacementFieldValueDto value, JsonSerializerOptions options)
        {
            var definition = new Definition(value.Field, value.Label, value.Group, value.IsReadOnly,
                value.ValueKind, value.MinimumValue, value.MaximumValue, value.Description, value.Options);
            if (!ids.TryGetValue(definition, out var id))
            {
                id = Definitions.Count;
                Definitions.Add(definition);
                ids.Add(definition, id);
            }
            writer.WriteStartObject();
            writer.WriteNumber("fieldId", id);
            writer.WriteString("value", value.Value);
            writer.WriteString("displayValue", value.DisplayValue);
            writer.WriteEndObject();
        }
    }
}
