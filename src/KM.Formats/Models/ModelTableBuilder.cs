// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace KM.Formats.Models;

/// <summary>Builds bounded native tables and retains unmodified embedded subgraphs.</summary>
public sealed class ModelTableBuilder
{
    private const int MaximumBytes = 64 * 1024 * 1024;
    private readonly List<byte> data;
    public ModelTableBuilder(byte[]? original = null)
    {
        if (original is { Length: > MaximumBytes }) throw new InvalidDataException("Native table input exceeds its size limit.");
        data = original is null ? [with(capacity: 8), 0, 0, 0, 0, 0, 0, 0, 0] : [.. original];
    }
    public int Length => data.Count;
    public int Append(ReadOnlySpan<byte> bytes, int alignment = 4)
    {
        if (alignment is not (1 or 2 or 4 or 8 or 16)) throw new ArgumentOutOfRangeException(nameof(alignment));
        var padding = (alignment - data.Count % alignment) % alignment;
        if ((long)data.Count + padding + bytes.Length > MaximumBytes) throw new InvalidDataException("Native table output exceeds its size limit.");
        for (var i = 0; i < padding; i++) data.Add(0);
        var at = data.Count;
        data.AddRange(bytes);
        return at;
    }
    public int Table(int count, IReadOnlyDictionary<int, byte[]>? values = null,
        IReadOnlyDictionary<int, int>? sizes = null, IReadOnlySet<int>? absent = null)
    {
        if (count is < 0 or > 256) throw new InvalidDataException("Native table field count is invalid.");
        var offsets = new ushort[count];
        var size = 4;
        for (var i = 0; i < count; i++)
        {
            if (absent?.Contains(i) == true) continue;
            offsets[i] = checked((ushort)size);
            var width = sizes?.GetValueOrDefault(i, 4) ?? 4;
            if (width is < 1 or > 4096) throw new InvalidDataException("Native table field size is invalid.");
            size = checked(size + width);
        }
        var vtable = new byte[4 + count * 2];
        BinaryPrimitives.WriteUInt16LittleEndian(vtable, checked((ushort)vtable.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(2), checked((ushort)size));
        for (var i = 0; i < count; i++) BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(4 + i * 2), offsets[i]);
        var vt = Append(vtable);
        var table = Append(new byte[size]);
        Int32(table, table - vt);
        if (values is not null)
        {
            foreach (var (index, bytes) in values)
            {
                if ((uint)index >= count || offsets[index] == 0 || bytes.Length > (sizes?.GetValueOrDefault(index, 4) ?? 4))
                    throw new InvalidDataException("Native table value does not fit its field.");
                Write(table + offsets[index], bytes);
            }
        }
        return table;
    }
    public int Field(int table, int index)
    {
        var span = CollectionsMarshal.AsSpan(data);
        if (table < 0 || table > span.Length - 4 || index < 0) throw new InvalidDataException("Invalid table address.");
        var vt = checked(table - BinaryPrimitives.ReadInt32LittleEndian(span[table..]));
        if (vt < 0 || vt > span.Length - 4) throw new InvalidDataException("Invalid table metadata.");
        var length = BinaryPrimitives.ReadUInt16LittleEndian(span[vt..]);
        var slot = checked(4 + index * 2);
        if (slot >= length || vt > span.Length - length) throw new InvalidDataException("Native field is absent.");
        var offset = BinaryPrimitives.ReadUInt16LittleEndian(span[(vt + slot)..]);
        if (offset < 4 || table + offset >= span.Length) throw new InvalidDataException("Native field is absent.");
        return table + offset;
    }
    public void Write(int at, ReadOnlySpan<byte> bytes)
    {
        if (at < 0 || at > data.Count - bytes.Length) throw new InvalidDataException("Native output write is out of bounds.");
        bytes.CopyTo(CollectionsMarshal.AsSpan(data).Slice(at, bytes.Length));
    }
    public void Int32(int at, int value)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); Write(at, bytes);
    }
    public void Single(int at, float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("Native output contains a nonfinite value.");
        Int32(at, BitConverter.SingleToInt32Bits(value));
    }
    public void Pointer(int at, int target)
    {
        if (target <= at || target >= data.Count) throw new InvalidDataException("Native table reference must point to a later object.");
        Int32(at, target - at);
    }
    public void Reference(int table, int field, int target) => Pointer(Field(table, field), target);
    public int String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 4096 || value.Contains('\0')) throw new InvalidDataException("Native string is invalid.");
        var result = new byte[bytes.Length + 5]; BinaryPrimitives.WriteInt32LittleEndian(result, bytes.Length);
        bytes.CopyTo(result, 4); return Append(result);
    }
    public void Text(int table, int field, string value) => Reference(table, field, String(value));
    public int[] Vector(int table, int field, int count)
    {
        if (count is < 0 or > 200000) throw new InvalidDataException("Native vector count is invalid.");
        var bytes = new byte[checked(4 + count * 4)]; BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        var at = Append(bytes); Reference(table, field, at);
        var elements = new int[count];
        for (var i = 0; i < elements.Length; i++) elements[i] = at + 4 + i * 4;
        return elements;
    }
    public void Blob(int table, int field, ReadOnlySpan<byte> bytes, int? count = null)
    {
        var payload = new byte[checked(bytes.Length + 4)];
        BinaryPrimitives.WriteInt32LittleEndian(payload, count ?? bytes.Length); bytes.CopyTo(payload.AsSpan(4));
        Reference(table, field, Append(payload));
    }
    public byte[] Finish(int root)
    {
        if (root < 4 || root >= data.Count) throw new InvalidDataException("Native root is invalid.");
        Int32(0, root); return data.ToArray();
    }
    public static byte[] Integer(int value) => BitConverter.GetBytes(value);
    public static byte[] Floats(params float[] values)
    {
        if (values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Native values must be finite.");
        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }
}
