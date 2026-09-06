// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.Formats.Models;

/// <summary>Bounded, read-only access to model metadata.</summary>
public sealed class ModelBuffer
{
    private readonly byte[] bytes;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public ModelBuffer(byte[] bytes)
    {
        if (bytes.Length is < 8 or > 64 * 1024 * 1024) throw new InvalidDataException("Model metadata size is unsupported.");
        this.bytes = bytes;
    }
    public int Root => Offset(0);
    public int Length => bytes.Length;
    public ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(at, 2));
    public uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(at, 4));
    public int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(Slice(at, 4));
    public byte U8(int at) => Slice(at, 1)[0];
    public float Float(int at) => BitConverter.Int32BitsToSingle(I32(at));
    public int Offset(int at) => Check(checked(at + checked((int)U32(at))), 4);
    public ReadOnlySpan<byte> Slice(int at, int count) => bytes.AsSpan(Check(at, count), count);
    private int Check(int at, int count)
    {
        if (at < 0 || count < 0 || at > bytes.Length - count) throw new InvalidDataException("Model metadata is truncated.");
        return at;
    }
    public int Field(int table, int index)
    {
        var vt = checked(table - I32(table));
        var length = U16(vt);
        var objectLength = U16(checked(vt + 2));
        Check(vt, length); Check(table, objectLength);
        if (length < 4 || (length & 1) != 0 || objectLength < 4 || index < 0) throw new InvalidDataException("Invalid model table.");
        var entry = checked(4 + index * 2);
        if (entry >= length) return 0;
        var relative = U16(checked(vt + entry));
        if (relative == 0) return 0;
        if (relative < 4 || relative >= objectLength) throw new InvalidDataException("Invalid model field.");
        return checked(table + relative);
    }
    public uint Value(int table, int index, uint fallback = 0) => Field(table, index) is var at && at != 0 ? U32(at) : fallback;
    public int Table(int table, int index) => Field(table, index) is var at && at != 0 ? Offset(at) : 0;
    public string? Text(int table, int index) => Field(table, index) is var at && at != 0 ? StringAt(at) : null;
    public string StringAt(int at)
    {
        var start = Offset(at); var count = checked((int)U32(start));
        if (count > 4096 || U8(checked(start + 4 + count)) != 0) throw new InvalidDataException("Invalid model string.");
        return Utf8.GetString(Slice(start + 4, count));
    }
    public (int Start, int Count) Vector(int table, int index, int elementSize, int maximum)
    {
        var field = Field(table, index);
        if (field == 0) return (0, 0);
        var vector = Offset(field); var count = checked((int)U32(vector));
        if (count > maximum) throw new InvalidDataException("Model element limit exceeded.");
        Check(vector + 4, checked(count * elementSize));
        return (vector + 4, count);
    }
    public int[] Tables(int table, int index, int maximum = 4096)
    {
        var (start, count) = Vector(table, index, 4, maximum);
        return Enumerable.Range(0, count).Select(i => Offset(checked(start + i * 4))).ToArray();
    }
    public byte[] Blob(int table, int index, int maximum = 32 * 1024 * 1024)
    {
        var (start, count) = Vector(table, index, 1, maximum);
        return Slice(start, count).ToArray();
    }
}
