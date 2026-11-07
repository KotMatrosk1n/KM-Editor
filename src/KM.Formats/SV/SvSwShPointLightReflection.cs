// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace KM.Formats.SV;

internal static class SvSwShPointLightReflection
{
    internal readonly record struct ZaGemBindings(bool SecondaryUv, bool Masked);
    internal readonly record struct AuthoredBindings(bool SecondaryUv, (string Name, int Slot)[] Samplers,
        bool EffectFrame = false, bool Tangent = false);

    private sealed class Node(BigInteger key, int bit)
    {
        public readonly BigInteger Key = key;
        public readonly int Bit = bit;
        public int Left;
        public int Right;
    }

    internal static byte[] Add(byte[] bytes, bool vertex, bool tangentInputOnly = false,
        bool tangentSecondaryUv = false, ZaGemBindings? zaGem = null, AuthoredBindings? authored = null)
    {
        if (tangentInputOnly && !vertex) throw new InvalidDataException("Authored tangent reflection requires a vertex program.");
        if (tangentSecondaryUv && (!vertex || !tangentInputOnly))
            throw new InvalidDataException("Secondary UV reflection requires its authored tangent vertex program.");
        if (zaGem is not null && (vertex || tangentInputOnly || tangentSecondaryUv))
            throw new InvalidDataException("ZA gem sampler reflection requires its dedicated fragment program.");
        if (authored is not null && (zaGem is not null || tangentInputOnly || tangentSecondaryUv))
            throw new InvalidDataException("Authored palette reflection requires its dedicated program.");
        int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4));
        int P(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at, 8)));
        var program = P(P(0x80) + 16); var reflection = P(program + 0x78); var stage = vertex ? 0 : 4;
        if (reflection == 0 || P(reflection + stage * 8) == 0)
            throw new InvalidDataException("Point light shader reflection is missing.");
        var record = P(reflection + stage * 8); var slotStart = I(record + 56); var count = I(record + 76);
        var starts = new[] { 0, I(record + 40), I(record + 44), I(record + 48), I(record + 52) };
        var groups = new List<(string Name, int Slot)>[5];
        for (var g = 0; g < groups.Length; g++)
        {
            groups[g] = []; var dictionary = P(record + g * 8);
            if (dictionary == 0) continue;
            if (!bytes.AsSpan(dictionary, 4).SequenceEqual("_DIC"u8) || I(dictionary + 4) is < 0 or > 128)
                throw new InvalidDataException("Point light shader dictionary is unsupported.");
            for (var n = 0; n < I(dictionary + 4); n++)
            {
                var text = P(dictionary + 8 + (n + 1) * 16 + 8);
                var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(text, 2));
                var name = Encoding.UTF8.GetString(bytes, text + 2, length);
                if (name.Length == 0 || name.Contains('\0') || starts[g] + n >= count)
                    throw new InvalidDataException("Point light shader reflection entry is invalid.");
                groups[g].Add((name, I(slotStart + (starts[g] + n) * 4)));
            }
        }
        if (vertex && !groups[0].SequenceEqual(new (string, int)[] { ("IN_Normal", 1), ("IN_Position", 0),
                ("IN_colorSet1", 7), ("IN_colorSet2", 8), ("IN_map1", 3), ("i_BlendIndices", 11), ("i_BlendWeight", 12) })
            || groups.SelectMany(g => g).Any(v => v.Name is "IN_Tangent" or "OUT_PointLightVector"))
            throw new InvalidDataException("Point light shader input reflection needs another adapter.");
        var relocation = I(24);
        if (!bytes.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || I(relocation + 8) != 6)
            throw new InvalidDataException("Point light reflection relocation is unsupported.");
        var sections = new List<(int Start, int Length)>(); var pointers = new Dictionary<int, int>();
        var entries = relocation + 16 + 6 * 24;
        for (var s = 0; s < 6; s++)
        {
            var at = relocation + 16 + s * 24; sections.Add((I(at + 8), I(at + 12)));
            for (var n = 0; n < I(at + 20); n++)
            {
                var entry = entries + (I(at + 16) + n) * 8; var position = I(entry);
                var structs = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 4, 2));
                var offsets = bytes[entry + 6]; var padding = bytes[entry + 7];
                for (var a = 0; a < structs; a++) for (var b = 0; b < offsets; b++)
                    if (!pointers.TryAdd(checked(position + (a * (offsets + padding) + b) * 8), s))
                        throw new InvalidDataException("Point light reflection relocation overlaps.");
            }
        }
        using var stream = new MemoryStream(); stream.Write(bytes); using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        void Align(int n) { while (stream.Position % n != 0) writer.Write((byte)0); }
        void Int(int at, int value) { var end = stream.Position; stream.Position = at; writer.Write(value); stream.Position = end; }
        void Pointer(int at, int value) { var end = stream.Position; stream.Position = at; writer.Write((long)value); stream.Position = end; pointers.TryAdd(at, -1); }
        Align(8); var addedStart = checked((int)stream.Position);
        int Dictionary(IReadOnlyList<(string Name, int Slot)> values)
        {
            var nodes = new List<Node> { new(BigInteger.Zero, -1) };
            int Bit(BigInteger key, int bit) => bit < 0 ? 0 : (int)(key >> bit & BigInteger.One);
            for (var k = 0; k < values.Count; k++)
            {
                var key = new BigInteger(Encoding.UTF8.GetBytes(values[k].Name), isUnsigned: true, isBigEndian: true);
                var parent = 0; var child = nodes[0].Left;
                while (nodes[parent].Bit < nodes[child].Bit)
                { parent = child; child = Bit(key, nodes[child].Bit) == 0 ? nodes[child].Left : nodes[child].Right; }
                var different = key ^ nodes[child].Key;
                if (different.IsZero) throw new InvalidDataException("Point light reflection has duplicate keys.");
                var bit = 0; while ((different & BigInteger.One).IsZero) { different >>= 1; bit++; }
                parent = 0; child = nodes[0].Left;
                while (nodes[parent].Bit < nodes[child].Bit && nodes[child].Bit < bit)
                { parent = child; child = Bit(key, nodes[child].Bit) == 0 ? nodes[child].Left : nodes[child].Right; }
                var index = nodes.Count; var node = new Node(key, bit); nodes.Add(node);
                if (Bit(key, bit) == 0) { node.Left = index; node.Right = child; } else { node.Left = child; node.Right = index; }
                if (Bit(key, nodes[parent].Bit) == 0) nodes[parent].Left = index; else nodes[parent].Right = index;
            }
            Align(8); var dictionary = checked((int)stream.Position); writer.Write("_DIC"u8); writer.Write(values.Count);
            for (var k = 0; k < nodes.Count; k++)
            { writer.Write(nodes[k].Bit); writer.Write(checked((ushort)nodes[k].Left)); writer.Write(checked((ushort)nodes[k].Right)); writer.Write(0L); }
            for (var k = 0; k < nodes.Count; k++)
            {
                Align(2); var text = checked((int)stream.Position); var encoded = Encoding.UTF8.GetBytes(k == 0 ? "" : values[k - 1].Name);
                writer.Write(checked((ushort)encoded.Length)); writer.Write(encoded); writer.Write((byte)0);
                Pointer(dictionary + 8 + k * 16 + 8, text);
            }
            return dictionary;
        }
        if (authored is { } palette)
        {
            if (palette.Samplers.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != palette.Samplers.Length
                || palette.Samplers.Select(s => s.Slot).Distinct().Count() != palette.Samplers.Length
                || palette.Samplers.Any(s => s.Slot is < 8 or > 15)
                || groups.SelectMany(g => g).Any(v => v.Name is "IN_map2" or "OUT_Layer5UV" || palette.Samplers.Any(s => s.Name == v.Name)))
                throw new InvalidDataException("Authored palette shader interface is ambiguous.");
            if (!vertex && (!groups[2].OrderBy(v => v.Slot).Select(v => v.Name).SequenceEqual(new[]
                { "Col0Tex", "EmissionMaskTex", "LyCol0Tex", "AmbientTex", "NormalMapTex", "LightTblTex", "SphereMapTex", "EffectTex" })
                || !groups[2].OrderBy(v => v.Slot).Select(v => v.Slot).SequenceEqual(Enumerable.Range(0, 8))))
                throw new InvalidDataException("Authored palette requires the pinned native sampler interface.");
            if (palette.SecondaryUv)
            {
                if (vertex) { groups[0].Add(("IN_map2", 4)); groups[1].Add(("OUT_Layer5UV", -1)); }
                else groups[0].Add(("OUT_Layer5UV", -1));
            }
            if (palette.EffectFrame)
            {
                if (vertex && palette.Tangent) groups[0].Add(("IN_Tangent", 2));
                groups[vertex ? 1 : 0].Add(("OUT_SourceEffect0", -1));
                groups[vertex ? 1 : 0].Add(("OUT_SourceEffect1", -1));
            }
            groups[2].AddRange(palette.Samplers);
        }
        else if (zaGem is { } gem)
        {
            var samplers = KM.Formats.SwSh.SwShZaFresnelSamples.Samplers(gem.Masked);
            if (!groups[2].OrderBy(v => v.Slot).Select(v => v.Name).SequenceEqual(new[]
                { "Col0Tex", "EmissionMaskTex", "LyCol0Tex", "AmbientTex", "NormalMapTex", "LightTblTex", "SphereMapTex", "EffectTex" })
                || !groups[2].OrderBy(v => v.Slot).Select(v => v.Slot).SequenceEqual(Enumerable.Range(0, 8))
                || groups.SelectMany(g => g).Any(v => samplers.Any(s => s.Name == v.Name) || v.Name == "OUT_Layer5UV"))
                throw new InvalidDataException("ZA gem requires the pinned native sampler interface.");
            if (gem.SecondaryUv) groups[0].Add(("OUT_Layer5UV", -1));
            groups[2].AddRange(samplers);
        }
        else
        {
            if (vertex) groups[0].Add(("IN_Tangent", 2));
            if (tangentSecondaryUv)
            {
                groups[0].Add(("IN_map2", 4)); groups[1].Add(("OUT_Layer5UV", -1));
            }
            if (!tangentInputOnly) groups[vertex ? 1 : 0].Add(("OUT_PointLightVector", -1));
        }
        var changedGroups = authored is { } authoredBindings ? authoredBindings.SecondaryUv || authoredBindings.EffectFrame ? vertex ? new[] { 0, 1, 2 } : [0, 2] : [2]
            : zaGem is { } bindings ? bindings.SecondaryUv ? new[] { 0, 2 } : [2]
            : vertex && (!tangentInputOnly || tangentSecondaryUv) ? new[] { 0, 1 } : [0];
        foreach (var g in changedGroups) Pointer(record + g * 8, Dictionary(groups[g]));
        Align(8); var slots = checked((int)stream.Position); var total = 0;
        for (var g = 0; g < groups.Length; g++)
        {
            if (g != 0) Int(record + 36 + g * 4, total);
            foreach (var entry in groups[g]) writer.Write(entry.Slot);
            total += groups[g].Count;
        }
        Int(record + 56, slots); Int(record + 76, total); Align(8);
        sections.Add((addedStart, checked((int)stream.Position - addedStart)));
        var output = stream.ToArray(); var relocated = sections.Select(_ => new List<int>()).ToArray();
        foreach (var pointer in pointers.OrderBy(v => v.Key))
        {
            var target = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(pointer.Key, 8)));
            var section = target == 0 ? pointer.Value : sections.FindIndex(s => target >= s.Start && target < s.Start + s.Length);
            if (section < 0) throw new InvalidDataException("Point light reflection pointer leaves its allocation.");
            relocated[section].Add(pointer.Key);
        }
        var newRelocation = checked((int)stream.Position); writer.Write("_RLT"u8); writer.Write(newRelocation); writer.Write(sections.Count); writer.Write(0);
        var first = 0;
        for (var s = 0; s < sections.Count; s++)
        { writer.Write(0L); writer.Write(sections[s].Start); writer.Write(sections[s].Length); writer.Write(first); writer.Write(relocated[s].Count); first += relocated[s].Count; }
        foreach (var group in relocated) foreach (var at in group) { writer.Write(at); writer.Write((ushort)1); writer.Write((byte)1); writer.Write((byte)0); }
        Int(24, newRelocation); Int(28, checked((int)stream.Length));
        if (stream.Length - bytes.Length > 4096) throw new InvalidDataException("Point light reflection exceeds its metadata allocation.");
        return stream.ToArray();
    }
}
