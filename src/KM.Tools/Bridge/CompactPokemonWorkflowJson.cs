// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Api.Bridge;
using KM.Api.Pokemon;

namespace KM.Tools.Bridge;

// Catalog entries are repeated for every Pokemon. Intern them within one response,
// without retaining data between requests or changing freshness and editing rules.
internal sealed class CompactPokemonWorkflowJson : JsonConverter<PokemonWorkflowDto>
{
    private static readonly AsyncLocal<int> Enabled = new();
    private static readonly JsonSerializerOptions[] ResponseFormats = [BridgeJson.SerializerOptions,
        CreateOptions(true, false), CreateOptions(false, true), CreateOptions(true, true)];
    internal static JsonSerializerOptions ResponseOptions => ResponseFormats[Enabled.Value];

    internal static IDisposable Begin(bool enabled, bool compactPlacement = false)
    {
        var previous = Enabled.Value;
        Enabled.Value = (enabled ? 1 : 0) | (compactPlacement ? 2 : 0);
        return new Scope(previous);
    }

    private sealed class Scope(int previous) : IDisposable
    {
        public void Dispose() => Enabled.Value = previous;
    }

    private static JsonSerializerOptions CreateOptions(bool pokemon, bool placement)
    {
        var options = new JsonSerializerOptions(BridgeJson.SerializerOptions);
        if (pokemon) options.Converters.Add(new CompactPokemonWorkflowJson());
        if (placement) options.Converters.Add(new CompactPlacementWorkflowJson());
        return options;
    }

    public override PokemonWorkflowDto? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => throw new NotSupportedException("Compact Pokemon workflows are response-only.");

    public override void Write(Utf8JsonWriter writer, PokemonWorkflowDto value, JsonSerializerOptions options)
    {
        var catalog = new List<PokemonCompatibilityEntryDto>();
        var ids = new Dictionary<PokemonCompatibilityEntryDto, int>();
        var plain = BridgeJson.SerializerOptions;
        writer.WriteStartObject();
        // Let the ordinary DTO serializer own optional fields and naming conventions.
        var header = JsonSerializer.SerializeToElement(value with { Pokemon = [] }, plain);
        foreach (var property in header.EnumerateObject())
        {
            if (property.Name != "pokemon") { property.WriteTo(writer); continue; }
            writer.WritePropertyName("pokemon");
            writer.WriteStartArray();
            foreach (var pokemon in value.Pokemon)
            {
                writer.WriteStartObject();
                var record = JsonSerializer.SerializeToElement(pokemon with { Compatibility = [] }, plain);
                foreach (var field in record.EnumerateObject())
                {
                    if (field.Name != "compatibility") { field.WriteTo(writer); continue; }
                    writer.WritePropertyName("compatibility");
                    writer.WriteStartArray();
                    foreach (var group in pokemon.Compatibility)
                    {
                        writer.WriteStartObject();
                        var groupHeader = JsonSerializer.SerializeToElement(group with { Entries = [] }, plain);
                        foreach (var groupField in groupHeader.EnumerateObject())
                        {
                            if (groupField.Name != "entries") groupField.WriteTo(writer);
                        }
                        writer.WritePropertyName("entryIds");
                        writer.WriteStartArray();
                        foreach (var entry in group.Entries)
                        {
                            if (!ids.TryGetValue(entry, out var id))
                            {
                                id = catalog.Count;
                                ids.Add(entry, id);
                                catalog.Add(entry);
                            }
                            writer.WriteNumberValue(id);
                        }
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WritePropertyName("compatibilityEntries");
        JsonSerializer.Serialize(writer, catalog, plain);
        writer.WriteEndObject();
    }
}
