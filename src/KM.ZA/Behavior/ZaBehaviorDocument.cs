// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.ZA.Behavior;

internal sealed class ZaBehaviorDocument
{
    private readonly byte[] bytes;
    private readonly int row;
    private readonly int reaction;
    public string[] Tags { get; }
    public float[] Values { get; }

    public ZaBehaviorDocument(byte[] bytes)
    {
        this.bytes = bytes;
        if (bytes.Length is < 16 or > 4 * 1024 * 1024) throw Invalid();
        var root = Target(0);
        var rows = Vector(Field(root, 0, 1), 1);
        if (rows.Length != 1) throw Invalid();
        row = Target(rows[0]);
        if (Int(Field(row, 0, 4)) != 4) throw Invalid();
        reaction = Target(Field(row, 2, 4));
        Tags = Vector(Field(row, 1, 4), 256).Select(String).ToArray();
        Values = Enumerable.Range(0, 4).Select(i => Float(Field(reaction, i, 5)))
            .Append(Float(Field(row, 3, 4))).ToArray();
        if (Values.Any(v => !float.IsFinite(v))) throw Invalid();
        // Validate the optional sense vector without changing or reconstructing its contents.
        var sense = Field(reaction, 4, 5);
        if (sense != 0) _ = Vector(sense, 1024);
    }

    public static string[] Catalog(byte[] bytes)
    {
        var reader = new CatalogReader(bytes);
        return reader.Read();
    }

    public byte[] Write(string[] tags, float[] values)
    {
        if (tags.Length > 256 || tags.Any(t => string.IsNullOrEmpty(t) || Encoding.UTF8.GetByteCount(t) > 512)
            || values.Length != 5 || values.Any(v => !float.IsFinite(v))) throw Invalid();
        if (tags.SequenceEqual(Tags) && values.SequenceEqual(Values)) return bytes.ToArray();
        // Prepend complete known tables. Every referenced opaque value stays in the original blob.
        const int prefix = 112;
        using var stream = new MemoryStream();
        stream.Write(new byte[prefix]);
        stream.Write(bytes);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        void U(int at, uint value) { stream.Position = at; writer.Write(value); }
        void H(int at, params ushort[] valuesToWrite)
        { stream.Position = at; foreach (var value in valuesToWrite) writer.Write(value); }
        U(0, 12); H(6, 6, 8, 4); U(12, 6); U(16, 4);
        U(20, 1); U(24, 16); H(28, 12, 20, 4, 8, 12, 16);
        U(40, 12); U(44, 4); U(52, 28);
        H(64, 14, 24, 4, 8, 12, 16, 20); U(80, 16);
        for (var i = 0; i < 4; i++) U(84 + i * 4, BitConverter.SingleToUInt32Bits(values[i]));
        U(56, BitConverter.SingleToUInt32Bits(values[4]));
        var sense = Field(reaction, 4, 5);
        if (sense != 0) U(100, checked((uint)(Target(sense) + prefix - 100)));
        else H(76, 0);
        stream.Position = stream.Length;
        while (stream.Length % 4 != 0) writer.Write((byte)0);
        var vector = checked((int)stream.Position);
        writer.Write(tags.Length);
        writer.Write(new byte[tags.Length * 4]);
        for (var i = 0; i < tags.Length; i++)
        {
            stream.Position = stream.Length;
            while (stream.Length % 4 != 0) writer.Write((byte)0);
            var at = checked((int)stream.Position);
            var text = Encoding.UTF8.GetBytes(tags[i]);
            writer.Write(text.Length); writer.Write(text); writer.Write((byte)0);
            U(vector + 4 + i * 4, checked((uint)(at - (vector + 4 + i * 4))));
        }
        U(48, checked((uint)(vector - 48)));
        var output = stream.ToArray();
        var check = new ZaBehaviorDocument(output);
        if (!check.Tags.SequenceEqual(tags) || !check.Values.SequenceEqual(values)) throw Invalid();
        return output;
    }

    private int Field(int table, int field, int maximumFields)
    {
        Range(table, 4);
        var vt = checked(table - Int(table)); Range(vt, 4);
        var length = Short(vt); var size = Short(vt + 2);
        if (length < 4 || length % 2 != 0 || length > 4 + maximumFields * 2 || size < 4) throw Invalid();
        Range(vt, length); Range(table, size);
        var offsets = Enumerable.Range(0, (length - 4) / 2).Select(i => (int)Short(vt + 4 + i * 2)).ToArray();
        var present = offsets.Where(o => o != 0).ToArray();
        if (present.Any(o => o < 4 || o % 4 != 0 || o > size - 4) || present.Distinct().Count() != present.Length) throw Invalid();
        return field >= offsets.Length || offsets[field] == 0 ? 0 : checked(table + offsets[field]);
    }
    private int Target(int at) { Range(at, 4); var relative = UInt(at); if (relative < 4 || relative > int.MaxValue) throw Invalid(); var target = checked(at + (int)relative); Range(target, 4); return target; }
    private int[] Vector(int at, int maximum)
    {
        if (at == 0) return [];
        var target = Target(at); var count = UInt(target);
        if (count > maximum) throw Invalid(); Range(target + 4, checked((int)count * 4));
        return Enumerable.Range(0, (int)count).Select(i => target + 4 + i * 4).ToArray();
    }
    private string String(int at)
    {
        var target = Target(at); var count = UInt(target);
        if (count > 512) throw Invalid(); Range(target + 4, checked((int)count + 1));
        if (bytes[target + 4 + (int)count] != 0) throw Invalid();
        return new UTF8Encoding(false, true).GetString(bytes, target + 4, (int)count);
    }
    private ushort Short(int at) { Range(at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)); }
    private int Int(int at) { Range(at, 4); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)); }
    private uint UInt(int at) { Range(at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)); }
    private float Float(int at) => at == 0 ? 0 : BitConverter.UInt32BitsToSingle(UInt(at));
    private void Range(int at, int size) { if (at < 0 || size < 0 || at > bytes.Length - size) throw Invalid(); }
    private static InvalidDataException Invalid() => new("Behavior resource has an unsupported or malformed layout.");

    private sealed class CatalogReader(byte[] source)
    {
        public string[] Read()
        {
            if (source.Length is < 16 or > 1024 * 1024) throw Invalid();
            // The catalog is a single string-vector table, with no gameplay parameter rows.
            var root = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source));
            if (root < 4 || root > source.Length - 4) throw Invalid();
            var vt = checked(root - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(root)));
            if (vt < 4 || vt > source.Length - 6 || BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vt)) != 6) throw Invalid();
            var field = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vt + 4));
            if (field < 4 || root + field > source.Length - 4) throw Invalid();
            var vector = checked(root + field + (int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(root + field)));
            if (vector < root || vector > source.Length - 4) throw Invalid();
            var count = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(vector));
            if (count is 0 or > 10000 || (long)vector + 4 + count * 4 > source.Length) throw Invalid();
            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var at = vector + 4 + i * 4;
                var text = checked(at + (int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at)));
                if (text <= at || text > source.Length - 4) throw Invalid();
                var length = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(text));
                if (length != 10 || text > source.Length - 15 || source[text + 14] != 0) throw Invalid();
                var name = Encoding.ASCII.GetString(source, text + 4, 10);
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9]{4}_[0-9]{2}_[0-9]{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw Invalid();
                names.Add(name);
            }
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count) throw Invalid();
            return names.ToArray();
        }
    }
}
