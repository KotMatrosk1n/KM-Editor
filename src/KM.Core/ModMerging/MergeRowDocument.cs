// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KM.Core.ModMerging;

public static class MergeRowDocument
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        IgnoreReadOnlyProperties = false,
        MaxDepth = 64,
    };

    public static MergeDocument Create<T>(IReadOnlyList<T> rows, Func<IReadOnlyList<T>, byte[]> write, string kind)
    {
        if (rows.Count > 50_000) throw new InvalidDataException("Table exceeds the structured record limit.");
        var content = new JsonObject();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = JsonSerializer.SerializeToNode(rows[index], SerializerOptions)!;
            row.AsObject().Remove("BaseStatTotal");
            row.AsObject().Remove("GroupKey");
            if (kind == "trainers") ConvertPartyArrays(row, false);
            content[index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture)] = row;
        }
        var identities = content.Select(pair => new {
            Key = pair.Key,
            Fields = new[] { "PersonalId", "MoveId", "Id", "TrainerId", "Trid", "RowIndex", "SourceIndex", "Slot" }
                .Where(name => pair.Value!.AsObject().ContainsKey(name)).Select(name => name + ":" + pair.Value![name]?.ToJsonString()).ToArray(),
        });
        var identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identities))));
        return new(content, node =>
        {
            var values = node.AsObject().OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            if (values.Length != rows.Count || values.Where((pair, index) => pair.Key != index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture)).Any())
                throw new InvalidDataException("Physical table rows cannot be deleted or reordered.");
            return write(values.Select(pair => {
                var value = pair.Value!.DeepClone();
                if (kind == "trainers") ConvertPartyArrays(value, true);
                return value.Deserialize<T>(SerializerOptions) ?? throw new InvalidDataException("A table row is missing.");
            }).ToArray());
        }, kind, identity);
    }

    private static void ConvertPartyArrays(JsonNode node, bool restore)
    {
        if (node is not JsonObject obj) return;
        foreach (var pair in obj.ToArray())
        {
            if (pair.Key is "Pokemon" or "Waza" or "Moves")
            {
                if (!restore && pair.Value is JsonArray array)
                {
                    if (array.Count > 6) throw new InvalidDataException("Trainer party exceeds its physical slot count.");
                    var slots = new JsonObject();
                    for (var index = 0; index < array.Count; index++) slots[index.ToString("D5")] = array[index]?.DeepClone();
                    obj[pair.Key] = slots;
                }
                else if (restore && pair.Value is JsonObject slots)
                {
                    var entries = slots.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
                    if (entries.Length > 6 || entries.Where((entry, index) => entry.Key != index.ToString("D5")).Any())
                        throw new InvalidDataException("Trainer slot identity is invalid.");
                    obj[pair.Key] = new JsonArray(entries.Select(entry => entry.Value?.DeepClone()).ToArray());
                }
            }
            if (obj[pair.Key] is JsonObject nested) ConvertPartyArrays(nested, restore);
            else if (obj[pair.Key] is JsonArray children)
                foreach (var child in children.OfType<JsonObject>()) ConvertPartyArrays(child, restore);
        }
    }
}
