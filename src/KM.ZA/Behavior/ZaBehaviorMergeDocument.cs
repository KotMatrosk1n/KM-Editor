// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json.Nodes;
using KM.Core.ModMerging;

namespace KM.ZA.Behavior;

public static class ZaBehaviorMergeDocument
{
    public static MergeDocument Read(byte[] bytes)
    {
        var source = new ZaBehaviorDocument(bytes);
        var node = new JsonObject { ["Tags"] = new JsonArray(source.Tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()) };
        for (var index = 0; index < source.Values.Length; index++) node[ZaBehaviorSettings.Fields[index].Field] = source.Values[index];
        // The preserving writer retains every uninterpreted value. Include that state in
        // compatibility checks so different opaque sense data requires a complete file choice.
        var opaque = source.OpaqueIdentity;
        return new(node, value =>
        {
            var tags = value["Tags"]!.AsArray().Select(t => t!.GetValue<string>()).ToArray();
            var values = ZaBehaviorSettings.Fields.Select(f => value[f.Field]!.GetValue<float>()).ToArray();
            if (values.Any(v => !float.IsFinite(v) || v < 0)) throw new InvalidDataException("Behavior values must be finite and nonnegative.");
            return source.Write(tags, values);
        }, "behavior", opaque);
    }
}
