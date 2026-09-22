// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KM.Core.ModMerging;
using KM.Formats.SwSh;

namespace KM.Tools.ModMerging;

internal static class MergeSwShStructured
{
    internal static MergeDocument? Read(string game, string path, byte[] bytes)
    {
        if (MergeInputs.Family(game) != "swsh") return null;
        if (path.Equals("romfs/bin/appli/shop/bin/shop_data.bin", StringComparison.OrdinalIgnoreCase)
            || path.Equals("romfs/bin/app/shop/shop_data.bin", StringComparison.OrdinalIgnoreCase)) return ReadShops(bytes);
        if (path.EndsWith(".bseq", StringComparison.OrdinalIgnoreCase)) return ReadSequence(bytes);
        return null;
    }

    private static MergeDocument ReadShops(byte[] bytes)
    {
        var source = SwShShopDataFile.Parse(bytes);
        var content = JsonSerializer.SerializeToNode(source, MergeRowDocument.SerializerOptions)!;
        var shape = new StringBuilder();
        var structured = Transform(content, false, shape);
        var ids = string.Join(",", source.SingleShops.Select(shop => shop.Hash)) + ";" + string.Join(",", source.MultiShops.Select(shop => shop.Hash));
        return new(structured, node =>
        {
            var target = Transform(node.DeepClone(), true, null).Deserialize<SwShShopDataFile>(MergeRowDocument.SerializerOptions)
                ?? throw new InvalidDataException("Missing shop table.");
            if (!source.SingleShops.Select(shop => shop.Hash).SequenceEqual(target.SingleShops.Select(shop => shop.Hash))
                || !source.MultiShops.Select(shop => shop.Hash).SequenceEqual(target.MultiShops.Select(shop => shop.Hash)))
                throw new InvalidDataException("Shop identities changed.");
            var edits = new List<SwShShopInventoryEdit>();
            for (var index = 0; index < source.SingleShops.Count; index++)
                if (!source.SingleShops[index].Inventory.Items.SequenceEqual(target.SingleShops[index].Inventory.Items))
                    edits.Add(new(SwShShopKind.Single, source.SingleShops[index].Hash, 0, 0, 0, SwShShopInventoryEditAction.Set, target.SingleShops[index].Inventory.Items, index));
            for (var index = 0; index < source.MultiShops.Count; index++)
            {
                var original = source.MultiShops[index]; var edited = target.MultiShops[index];
                if (original.Inventories.Count != edited.Inventories.Count) throw new InvalidDataException("Shop inventory layout changed.");
                for (var slot = 0; slot < original.Inventories.Count; slot++)
                    if (!original.Inventories[slot].Items.SequenceEqual(edited.Inventories[slot].Items))
                        edits.Add(new(SwShShopKind.Multi, original.Hash, slot, 0, 0, SwShShopInventoryEditAction.Set, edited.Inventories[slot].Items, index));
            }
            return source.WriteEdits(edits);
        }, "shops", Identity(ids + shape));
    }

    private static MergeDocument ReadSequence(byte[] bytes)
    {
        var source = JsonNode.Parse(SwShBseqJsonConverter.ExportToJson(bytes))!;
        var rebuilt = SwShBseqJsonConverter.ImportFromJson(source.ToJsonString());
        // Preserve the uninterpreted header word. Other unrepresented material must not disappear.
        bytes.AsSpan(8, 4).CopyTo(rebuilt.AsSpan(8));
        if (!bytes.AsSpan().SequenceEqual(rebuilt)) throw new InvalidDataException("The sequence has unrepresented data.");
        var shape = new StringBuilder();
        var structured = Transform(source, false, shape);
        var commands = SwShBseqFile.Parse(bytes).Commands;
        var identity = Identity(shape + ";" + string.Join(",", commands.Select(command => $"{command.Hash}:{command.PayloadLength}")));
        structured["HeaderData"] = Convert.ToBase64String(bytes.AsSpan(8, 4));
        return new(structured, node =>
        {
            var restored = Transform(node.DeepClone(), true, null);
            var header = Convert.FromBase64String(restored["HeaderData"]!.GetValue<string>());
            if (header.Length != 4) throw new InvalidDataException("Sequence header has the wrong size.");
            restored.AsObject().Remove("HeaderData");
            var result = SwShBseqJsonConverter.ImportFromJson(restored.ToJsonString());
            header.CopyTo(result, 8);
            return result;
        }, "battle-sequences", identity);
    }

    // These formats have physical ordered slots. Their shape is part of the compatibility identity.
    private static JsonNode Transform(JsonNode node, bool restore, StringBuilder? shape)
    {
        if (node is JsonArray array)
        {
            shape?.Append('[').Append(array.Count).Append(']');
            return new JsonObject { ["Slots"] = new JsonObject(array.Select((value, index) =>
                new KeyValuePair<string, JsonNode?>(index.ToString("D5"), value is null ? null : Transform(value.DeepClone(), false, shape)))) };
        }
        if (node is not JsonObject obj) return node;
        if (restore && obj.Count == 1 && obj["Slots"] is JsonObject slots)
        {
            var values = slots.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            if (values.Where((pair, index) => pair.Key != index.ToString("D5")).Any()) throw new InvalidDataException("Invalid sequence slots.");
            return new JsonArray(values.Select(pair => pair.Value is null ? null : Transform(pair.Value.DeepClone(), true, null)).ToArray());
        }
        foreach (var pair in obj.ToArray()) if (pair.Value is not null) obj[pair.Key] = Transform(pair.Value.DeepClone(), restore, shape);
        return obj;
    }

    private static string Identity(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
