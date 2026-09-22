// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json.Nodes;

namespace KM.Core.ModMerging;

/// <summary>A format owned structural representation and its preserving writer.</summary>
public sealed record MergeDocument(JsonNode Content, Func<JsonNode, byte[]> Write, string Kind, string? LayoutIdentity = null);

public sealed record MergeCandidate(string SourceId, JsonNode? Value, bool Exists = true);
public sealed record MergeDifference(string Key, JsonNode? Original, IReadOnlyList<MergeCandidate> Candidates, bool OriginalExists = true);

public static class StructuralMerge
{
    private const int MaximumDepth = 64;
    private const int MaximumNodes = 2_000_000;

    public static JsonNode? Combine(
        JsonNode? original,
        IReadOnlyList<MergeCandidate> candidates,
        bool compareOriginal,
        Func<MergeDifference, string?> resolve)
    {
        var remaining = MaximumNodes;
        return Visit(original, compareOriginal, candidates, "", 0).Value;

        (JsonNode? Value, bool Exists) Visit(JsonNode? baseline, bool baselineExists, IReadOnlyList<MergeCandidate> values, string key, int depth)
        {
            if (depth > MaximumDepth || --remaining < 0)
                throw new InvalidDataException("Structured merge exceeds its supported size.");
            var edits = compareOriginal
                ? values.Where(value => value.Exists != baselineExists || !JsonNode.DeepEquals(value.Value, baseline)).ToArray()
                : values.ToArray();
            if (edits.Length == 0) return (baseline?.DeepClone(), baselineExists);
            if (edits.All(value => value.Exists == edits[0].Exists && JsonNode.DeepEquals(value.Value, edits[0].Value)))
                return (edits[0].Value?.DeepClone(), edits[0].Exists);

            if (edits.All(value => value.Value is JsonObject) && (baseline is JsonObject || baseline is null))
            {
                var result = new JsonObject();
                var names = edits.SelectMany(value => ((JsonObject)value.Value!).Select(pair => pair.Key))
                    .Concat((baseline as JsonObject)?.Select(pair => pair.Key) ?? [])
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
                foreach (var name in names)
                {
                    var child = Visit(baseline?[name], baseline is JsonObject baseObject && baseObject.ContainsKey(name),
                        edits.Select(value => new MergeCandidate(value.SourceId, value.Value?[name], ((JsonObject)value.Value!).ContainsKey(name))).ToArray(),
                        key + "/" + Escape(name), depth + 1);
                    if (child.Exists) result[name] = child.Value;
                }
                return (result, true);
            }

            // Only codecs may turn ordered game vectors into identity keyed objects. Arbitrary arrays are atomic.
            var choice = resolve(new MergeDifference(key, baseline, edits, baselineExists));
            var selected = edits.FirstOrDefault(value => value.SourceId == choice) ?? edits[0];
            return (selected.Value?.DeepClone(), selected.Exists);
        }
    }

    public static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
