// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public readonly record struct SwShBattleCameraIdentity(int Species, int Form, int Sex, int Giant);
public sealed record SwShBattleCameraAdjustment(SwShBattleCameraIdentity Identity, float AdjustScale, float CameraScale);

/// <summary>Edits independent battle camera rows while retaining all other indexed PRMB data.</summary>
public sealed class SwShBattleCameraTable
{
    private const int MaximumEntries = 100_000;
    private const ulong AdjustmentsKey = 0x6ea1bfd3805093cc;
    private static readonly ulong[] RowKeys = [0xf3b47097d1e17785, 0x430f1d44ee020fcc,
        0x68a3732777251727, 0x5069f221ce2139d6, 0xfe1af640edd1a57c, 0xcd4798d42317aa2b];
    private sealed record Node(int Parent, int[] Children, int Value, int Schema);
    private sealed record Schema(ulong Hash, (ulong Key, ulong Type)[] Fields);
    private readonly List<Node> nodes = [];
    private readonly List<(int Type, int Bits)> values = [];
    private readonly List<Schema> schemas = [];
    private readonly Dictionary<SwShBattleCameraIdentity, int> rows = [];
    private readonly int array;

    public SwShBattleCameraTable(byte[] source)
    {
        var b = new ModelBuffer(source);
        var tables = b.Tables(b.Root, 0, MaximumEntries);
        foreach (var table in b.Tables(b.Root, 1, MaximumEntries))
        {
            var type = b.Field(table, 0) is var at && at != 0 ? b.U8(at) : 0;
            var payload = b.Table(table, 1);
            // Retail poke_data uses only int32 and float32. Fail closed on new
            // unions instead of silently dropping an unfamiliar payload.
            if (type is not (1 or 2) || payload == 0) throw new InvalidDataException("Unsupported battle parameter value.");
            values.Add((type, unchecked((int)b.Value(payload, 0))));
        }
        foreach (var table in b.Tables(b.Root, 2, 4096))
        {
            var fields = b.Tables(table, 1, 4096).Select(entry => (Hash(entry, 0), Hash(entry, 1))).ToArray();
            if (fields.Select(f => f.Item1).Distinct().Count() != fields.Length)
                throw new InvalidDataException("Battle parameter keys are duplicated.");
            schemas.Add(new(Hash(table, 0), fields));
        }
        var childCount = 0;
        foreach (var table in tables)
        {
            var (start, count) = b.Vector(table, 1, 4, MaximumEntries);
            childCount = checked(childCount + count);
            if (childCount > MaximumEntries) throw new InvalidDataException("Battle parameter graph exceeds its limit.");
            var children = new int[count];
            for (var i = 0; i < count; i++) children[i] = b.I32(start + i * 4);
            var node = new Node(unchecked((int)b.Value(table, 0)), children,
                unchecked((int)b.Value(table, 2)), unchecked((int)b.Value(table, 3)));
            if (node.Value < -1 || node.Value >= values.Count || node.Schema < -1 || node.Schema >= schemas.Count
                || node.Value >= 0 && (count != 0 || node.Schema != -1)
                || node.Schema >= 0 && schemas[node.Schema].Fields.Length != count)
                throw new InvalidDataException("Battle parameter node is invalid.");
            nodes.Add(node);
        }
        ValidateGraph();
        var root = nodes[0];
        if (root.Schema < 0) throw new InvalidDataException("Battle parameter root keys are missing.");
        var slot = Array.FindIndex(schemas[root.Schema].Fields, f => f.Key == AdjustmentsKey);
        if (slot < 0) throw new InvalidDataException("Battle camera adjustments are missing.");
        array = root.Children[slot];
        if (nodes[array].Schema != -1 || nodes[array].Value != -1)
            throw new InvalidDataException("Battle camera adjustment list is invalid.");
        foreach (var row in nodes[array].Children)
        {
            var adjustment = ReadRow(row);
            if (!rows.TryAdd(adjustment.Identity, row)) throw new InvalidDataException("Battle camera identity is ambiguous.");
        }
        return;

        ulong Hash(int table, int field) => b.Field(table, field) is var at && at != 0
            ? BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(at, 8)) : 0;
    }

    private SwShBattleCameraTable(SwShBattleCameraTable original)
    {
        nodes.AddRange(original.nodes); values.AddRange(original.values); schemas.AddRange(original.schemas);
        foreach (var item in original.rows) rows.Add(item.Key, item.Value);
        array = original.array;
    }

    public IReadOnlyList<SwShBattleCameraAdjustment> Rows => rows.Values.Select(ReadRow).ToArray();
    public SwShBattleCameraAdjustment? Read(SwShBattleCameraIdentity identity) => rows.TryGetValue(identity, out var row) ? ReadRow(row) : null;

    public byte[] Compose(IReadOnlyDictionary<SwShBattleCameraIdentity, SwShBattleCameraAdjustment?> changes)
    {
        if (changes.Count > 4096) throw new InvalidDataException("Too many battle camera changes.");
        // Work on a fresh graph, so one parsed source remains immutable for callers.
        var edited = new SwShBattleCameraTable(this);
        foreach (var (identity, adjustment) in changes)
        {
            if (adjustment is not null) Validate(adjustment);
            if (adjustment is not null && adjustment.Identity != identity) throw new InvalidDataException("Battle camera identity does not match.");
            if (edited.rows.TryGetValue(identity, out var row))
            {
                if (adjustment is null)
                    edited.nodes[edited.array] = edited.nodes[edited.array] with { Children = edited.nodes[edited.array].Children.Where(i => i != row).ToArray() };
                else edited.Replace(row, adjustment);
            }
            else if (adjustment is not null) edited.Add(adjustment);
        }
        return edited.Write();
    }

    public static void Validate(SwShBattleCameraAdjustment row)
    {
        var id = row.Identity;
        if (id.Species is < 1 or > 2047 || id.Form is < 0 or > 255 || id.Sex is < 0 or > 3 || id.Giant is < 0 or > 1
            || !Scale(row.AdjustScale) || !Scale(row.CameraScale)) throw new InvalidDataException("Battle camera adjustment is invalid.");
        static bool Scale(float value) => float.IsFinite(value) && (value == -1 || value is > 0 and <= 10);
    }

    private int[] Fields(int row)
    {
        var node = nodes[row];
        if (node.Schema < 0 || node.Value != -1) throw new InvalidDataException("Battle camera row keys are missing.");
        return RowKeys.Select(key =>
        {
            var slot = Array.FindIndex(schemas[node.Schema].Fields, f => f.Key == key);
            if (slot < 0) throw new InvalidDataException("Battle camera row field is missing.");
            return node.Children[slot];
        }).ToArray();
    }

    private SwShBattleCameraAdjustment ReadRow(int row)
    {
        var fields = Fields(row);
        var data = fields.Select((field, i) =>
        {
            var index = nodes[field].Value;
            if (index < 0 || values[index].Type != (i < 4 ? 1 : 2)) throw new InvalidDataException("Battle camera field type is invalid.");
            return values[index].Bits;
        }).ToArray();
        var result = new SwShBattleCameraAdjustment(new(data[0], data[1], data[2], data[3]),
            BitConverter.Int32BitsToSingle(data[4]), BitConverter.Int32BitsToSingle(data[5]));
        Validate(result); return result;
    }

    private void Replace(int row, SwShBattleCameraAdjustment adjustment)
    {
        // Native rows share scalar nodes. Detach only this row's owned leaves.
        var children = nodes[row].Children.ToArray();
        foreach (var (slot, bits) in new[] { (4, BitConverter.SingleToInt32Bits(adjustment.AdjustScale)), (5, BitConverter.SingleToInt32Bits(adjustment.CameraScale)) })
        {
            var field = Array.FindIndex(schemas[nodes[row].Schema].Fields, f => f.Key == RowKeys[slot]);
            children[field] = nodes.Count;
            nodes.Add(new(row, [], values.Count, -1));
            values.Add((2, bits));
        }
        nodes[row] = nodes[row] with { Children = children };
    }

    private void Add(SwShBattleCameraAdjustment adjustment)
    {
        var schema = schemas.FindIndex(s => s.Fields.Length == RowKeys.Length && s.Fields.Select(f => f.Key).SequenceEqual(RowKeys));
        if (schema < 0) throw new InvalidDataException("Battle camera row schema is unsupported.");
        var row = nodes.Count;
        var children = Enumerable.Range(row + 1, 6).ToArray();
        nodes.Add(new(array, children, -1, schema));
        int[] bits = [adjustment.Identity.Species, adjustment.Identity.Form, adjustment.Identity.Sex, adjustment.Identity.Giant,
            BitConverter.SingleToInt32Bits(adjustment.AdjustScale), BitConverter.SingleToInt32Bits(adjustment.CameraScale)];
        for (var i = 0; i < bits.Length; i++) { nodes.Add(new(row, [], values.Count, -1)); values.Add((i < 4 ? 1 : 2, bits[i])); }
        nodes[array] = nodes[array] with { Children = [.. nodes[array].Children, row] };
    }

    private void ValidateGraph()
    {
        if (nodes.Count == 0) throw new InvalidDataException("Battle parameter graph is empty.");
        if (nodes[0].Parent != -1) throw new InvalidDataException("Battle parameter root parent is invalid.");
        for (var i = 1; i < nodes.Count; i++)
            if ((uint)nodes[i].Parent >= nodes.Count || !nodes[nodes[i].Parent].Children.Contains(i))
                throw new InvalidDataException("Battle parameter parent is invalid.");
        var state = new byte[nodes.Count];
        Visit(0, 0);
        if (state.Any(s => s != 2)) throw new InvalidDataException("Battle parameter graph contains unreachable nodes.");
        void Visit(int index, int depth)
        {
            if ((uint)index >= nodes.Count || depth > 64 || state[index] == 1)
                throw new InvalidDataException("Battle parameter graph is invalid.");
            if (state[index] == 2) return;
            state[index] = 1;
            foreach (var child in nodes[index].Children) Visit(child, depth + 1);
            state[index] = 2;
        }
    }

    private byte[] Write()
    {
        // Compact reachable nodes and values on every write. Repeated updates do
        // not retain old files, detached rows, or obsolete scalar payloads.
        var order = new List<int>(); var parents = new Dictionary<int, int>();
        Visit(0, -1);
        var map = order.Select((old, index) => (old, index)).ToDictionary(x => x.old, x => x.index);
        var usedValues = order.Select(i => nodes[i].Value).Where(i => i >= 0).Distinct().ToArray();
        var valueMap = usedValues.Select((old, index) => (old, index)).ToDictionary(x => x.old, x => x.index);
        var w = new ModelTableBuilder(); var root = w.Table(3);
        var nodeRefs = w.Vector(root, 0, order.Count); var valueRefs = w.Vector(root, 1, usedValues.Length); var schemaRefs = w.Vector(root, 2, schemas.Count);
        for (var i = 0; i < order.Count; i++)
        {
            var old = order[i]; var node = nodes[old];
            var originalParent = node.Parent;
            var parent = originalParent >= 0 && map.TryGetValue(originalParent, out var mappedParent)
                && nodes[originalParent].Children.Contains(old) ? mappedParent : parents[old];
            var table = w.Table(4, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(parent),
                [2] = ModelTableBuilder.Integer(node.Value < 0 ? -1 : valueMap[node.Value]), [3] = ModelTableBuilder.Integer(node.Schema) });
            w.Pointer(nodeRefs[i], table);
            var children = w.Vector(table, 1, node.Children.Length);
            for (var j = 0; j < children.Length; j++) w.Int32(children[j], map[node.Children[j]]);
        }
        for (var i = 0; i < usedValues.Length; i++)
        {
            var value = values[usedValues[i]]; var table = w.Table(2, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(value.Type) });
            w.Pointer(valueRefs[i], table);
            w.Reference(table, 1, w.Table(1, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(value.Bits) }));
        }
        for (var i = 0; i < schemas.Count; i++)
        {
            var schema = schemas[i]; var table = HashTable(schema.Hash, 0); w.Pointer(schemaRefs[i], table);
            var fields = w.Vector(table, 1, schema.Fields.Length);
            for (var j = 0; j < fields.Length; j++) w.Pointer(fields[j], HashTable(schema.Fields[j].Key, schema.Fields[j].Type));
        }
        return w.Finish(root);

        void Visit(int index, int parent)
        {
            if (parents.ContainsKey(index)) return;
            var mapped = order.Count;
            parents.Add(index, parent); order.Add(index);
            foreach (var child in nodes[index].Children) Visit(child, mapped);
        }
        int HashTable(ulong first, ulong second)
        {
            byte[] vt = [8, 0, 24, 0, 8, 0, 16, 0]; var metadata = w.Append(vt);
            var table = w.Append(new byte[24], 8); w.Int32(table, table - metadata);
            Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, first); w.Write(table + 8, bytes);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, second); w.Write(table + 16, bytes); return table;
        }
    }
}
