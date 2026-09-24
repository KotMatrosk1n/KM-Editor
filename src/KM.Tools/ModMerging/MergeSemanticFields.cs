// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json.Nodes;
using KM.Core.ModMerging;

namespace KM.Tools.ModMerging;

internal static class MergeSemanticFields
{
    private const string Identity = "Pokemon identity";
    private const string Evs = "EV allocation";
    private const string Levels = "Level range";
    private const string Outcome = "Answer outcome ";
    private static readonly string[] FlatEvs = ["EvHp", "EvAttack", "EvDefense", "EvSpecialAttack", "EvSpecialDefense", "EvSpeed"];

    internal static MergeDocument Wrap(MergeDocument document)
    {
        if (document.Kind is not ("trainers" or "encounters" or "battle-sequences")) return document;
        return document with
        {
            Content = Transform(document.Content.DeepClone(), document.Kind, false),
            Write = node => document.Write(Transform(node.DeepClone(), document.Kind, true)),
        };
    }

    private static JsonNode Transform(JsonNode node, string kind, bool restore)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array.OfType<JsonObject>()) Transform(child, kind, restore);
            return node;
        }
        if (node is not JsonObject obj) return node;
        if (restore)
        {
            foreach (var key in obj.Select(p => p.Key).Where(IsGroup).ToArray())
            {
                var group = obj[key]!.AsObject();
                obj.Remove(key);
                foreach (var pair in group) obj.Add(pair.Key, pair.Value?.DeepClone());
            }
        }
        foreach (var child in obj.Select(p => p.Value).OfType<JsonNode>().ToArray()) Transform(child, kind, restore);
        if (restore) return node;
        if (kind is "trainers" or "encounters")
        {
            foreach (var pair in new[] { new[] { "Species", "Form" }, ["SpeciesId", "FormId"], ["SpeciesId", "Form"], ["DevId", "FormId"], ["Devid", "Formno"] })
                if (pair.All(obj.ContainsKey)) { Group(Identity, pair); break; }
            if (kind == "trainers" && FlatEvs.All(obj.ContainsKey)) Group(Evs, FlatEvs);
            if (obj.ContainsKey("Minlevel") && obj.ContainsKey("Maxlevel")) Group(Levels, ["Minlevel", "Maxlevel"]);
        }
        if (kind == "battle-sequences")
        {
            if (obj["name"]?.GetValue<string>() == "SpecialQuizResult" && obj["parameters"]?["Slots"] is JsonObject parameters)
                for (var answer = 0; answer < 3; answer++)
                {
                    var first = (answer * 2).ToString("D5"); var second = (answer * 2 + 1).ToString("D5");
                    if (!parameters.ContainsKey(first) || !parameters.ContainsKey(second)) throw new InvalidDataException("Incomplete quiz outcome.");
                    var pair = new JsonObject { [first] = parameters[first]?.DeepClone(), [second] = parameters[second]?.DeepClone() };
                    parameters.Remove(first); parameters.Remove(second); parameters.Add(Outcome + (answer + 1), pair);
                }
            for (var answer = 1; answer <= 3; answer++)
                if (obj.ContainsKey("type" + answer) && obj.ContainsKey("result" + answer)) Group(Outcome + answer, ["type" + answer, "result" + answer]);
        }
        return node;

        void Group(string name, string[] fields)
        {
            var values = new JsonObject();
            foreach (var field in fields) { values[field] = obj[field]?.DeepClone(); obj.Remove(field); }
            obj.Add(name, values);
        }
    }

    private static bool IsGroup(string key) => key is Identity or Evs or Levels || key.StartsWith(Outcome, StringComparison.Ordinal);
    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
    private static bool IsEvs(string key) => key is Evs or "Evs" or "EffortValue";
    private static double Number(JsonNode? node) => node is null ? 0 : double.Parse(node.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    internal static bool RequiresWholeValue(string path, JsonNode? baseline, IReadOnlyList<MergeCandidate> edits)
    {
        var key = Name(path);
        if (key == Identity || key.StartsWith(Outcome, StringComparison.Ordinal)) return true;
        if (!IsEvs(key) && key != Levels) return false;
        var values = edits.Select(e => e.Value).OfType<JsonObject>().ToArray();
        if (values.Length != edits.Count) return true;
        IEnumerable<double> Possibilities(string field)
        {
            var changed = values.Where(v => !JsonNode.DeepEquals(v[field], baseline?[field])).Select(v => Number(v[field])).ToArray();
            return changed.Length == 0 ? [Number(baseline?[field] ?? values[0][field])] : changed;
        }
        if (key == Levels) return Possibilities("Minlevel").Max() > Possibilities("Maxlevel").Min();
        return values.SelectMany(v => v.Select(p => p.Key)).Distinct().Sum(field => Possibilities(field).Max()) > 510;
    }

    internal static bool IsValid(JsonNode node, JsonNode? original, string path = "")
    {
        if (JsonNode.DeepEquals(node, original)) return true;
        var key = Name(path);
        if (node is JsonObject obj)
        {
            if (IsEvs(key) && (obj.Any(p => Number(p.Value) is < 0 or > 252) || obj.Sum(p => Number(p.Value)) > 510)) return false;
            if (key == Levels && (Number(obj["Minlevel"]) < 0 || Number(obj["Maxlevel"]) > 100 || Number(obj["Minlevel"]) > Number(obj["Maxlevel"]))) return false;
            foreach (var pair in obj)
                if (pair.Value is { } value && !IsValid(value, (original as JsonObject)?[pair.Key], path + "/" + pair.Key)) return false;
        }
        return true;
    }
}
