// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SV;

public sealed record SvMaterialDropRow(int SpeciesId, int ItemId, int Rate, int Count);

/// <summary>Bounded, preservation edits for species material drops.</summary>
public sealed class SvMaterialDropTable
{
    private byte[] data;
    private readonly Dictionary<int, Entry> entries = new();
    public IReadOnlyList<SvMaterialDropRow> Rows => entries.Values.Select(entry => entry.Row).ToArray();

    public SvMaterialDropTable(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 12 or > 4 * 1024 * 1024)
            throw new InvalidDataException("The material drop table size is invalid.");
        data = bytes.ToArray();
        var root = Reference(0);
        var vectorField = Field(root, 0, 4);
        if (vectorField == 0) throw new InvalidDataException("The material drop vector is missing.");
        var vector = Reference(vectorField, isTable: false);
        var count = Int32(vector);
        if (count is < 1 or > 50_000)
            throw new InvalidDataException("The material drop row count is invalid.");
        Bounds(vector + 4, checked(count * 4));
        var itemReferences = new Dictionary<int, int>();
        for (var i = 0; i < count; i++)
        {
            var table = Reference(vector + 4 + i * 4);
            var speciesField = Field(table, 0, 2);
            var species = speciesField == 0 ? 0 : UInt16(speciesField);
            var itemPointer = Field(table, 1, 4);
            EnsureSeparateFields((speciesField, 2), (itemPointer, 4));
            if (species == 0 || itemPointer == 0)
                throw new InvalidDataException("A material drop row has no species or item data.");
            var item = Reference(itemPointer);
            var itemField = Field(item, 0, 4);
            var rateField = Field(item, 1, 1);
            var countField = Field(item, 2, 1);
            EnsureSeparateFields((itemField, 4), (rateField, 1), (countField, 1));
            var row = new SvMaterialDropRow(species, itemField == 0 ? 0 : Int32(itemField),
                rateField == 0 ? 0 : (sbyte)data[rateField], countField == 0 ? 0 : (sbyte)data[countField]);
            if (row.ItemId < 0 || row.Rate is < 0 or > 100 || row.Count < 0
                || !entries.TryAdd(species, new Entry(row, itemPointer, item, itemField, rateField, countField)))
                throw new InvalidDataException("The material drop table has invalid values or duplicate species.");
            itemReferences[item] = itemReferences.GetValueOrDefault(item) + 1;
        }
        foreach (var entry in entries.Values) entry.SharedItem = itemReferences[entry.ItemTable] > 1;
    }

    public void SetValues(int speciesId, int? itemId = null, int? rate = null, int? count = null)
    {
        if (itemId is < 0) throw new ArgumentOutOfRangeException(nameof(itemId));
        if (rate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(rate));
        if (count is < 0 or > sbyte.MaxValue) throw new ArgumentOutOfRangeException(nameof(count));
        if (!entries.TryGetValue(speciesId, out var entry))
            throw new InvalidDataException("This species has no material drop row.");
        var updated = entry.Row with { ItemId = itemId ?? entry.Row.ItemId, Rate = rate ?? entry.Row.Rate, Count = count ?? entry.Row.Count };
        if (entry.Row == updated) return;
        if (!entry.SharedItem
            && (updated.ItemId == entry.Row.ItemId || entry.ItemField != 0)
            && (updated.Rate == entry.Row.Rate || entry.RateField != 0)
            && (updated.Count == entry.Row.Count || entry.CountField != 0))
        {
            if (updated.ItemId != entry.Row.ItemId) Write32(entry.ItemField, updated.ItemId);
            if (updated.Rate != entry.Row.Rate) data[entry.RateField] = (byte)updated.Rate;
            if (updated.Count != entry.Row.Count) data[entry.CountField] = (byte)updated.Count;
        }
        else
        {
            // A missing default or an aliased item needs its own child. Keep the
            // original file intact and redirect only this species' item pointer.
            var (vtable, _) = Table(entry.ItemTable);
            for (var index = 3; 4 + index * 2 < UInt16(vtable); index++)
                if (UInt16(vtable + 4 + index * 2) != 0)
                    throw new InvalidDataException("Cannot clone material item data with unknown fields.");
            var start = checked((data.Length + 3) & ~3);
            if (start > 4 * 1024 * 1024 - 24)
                throw new InvalidDataException("The material drop table is too large to extend.");
            Array.Resize(ref data, start + 24);
            Write16(start, 10); Write16(start + 2, 12);
            Write16(start + 4, 4); Write16(start + 6, 8); Write16(start + 8, 9);
            var item = start + 12;
            Write32(item, 12); Write32(item + 4, updated.ItemId);
            data[item + 8] = (byte)updated.Rate;
            data[item + 9] = (byte)updated.Count;
            Write32(entry.ItemPointer, checked(item - entry.ItemPointer));
            entry.ItemTable = item;
            entry.ItemField = item + 4;
            entry.RateField = item + 8;
            entry.CountField = item + 9;
            entry.SharedItem = false;
        }
        entry.Row = updated;
    }

    public byte[] ToArray() => (byte[])data.Clone();

    private static void EnsureSeparateFields(params (int Offset, int Width)[] fields)
    {
        var present = fields.Where(field => field.Offset != 0).OrderBy(field => field.Offset).ToArray();
        for (var i = 1; i < present.Length; i++)
            if (present[i - 1].Offset + present[i - 1].Width > present[i].Offset)
                throw new InvalidDataException("Material drop fields overlap.");
    }

    private void Bounds(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException("A material drop reference is outside the file.");
    }
    private int Int32(int offset) { Bounds(offset, 4); return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)); }
    private ushort UInt16(int offset) { Bounds(offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)); }
    private void Write16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
    private void Write32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
    private (int Vtable, int Size) Table(int table)
    {
        var vtable = checked(table - Int32(table));
        var length = UInt16(vtable);
        var size = UInt16(vtable + 2);
        if (length < 4 || length % 2 != 0 || size < 4)
            throw new InvalidDataException("A material drop table layout is invalid.");
        Bounds(vtable, length); Bounds(table, size);
        return (vtable, size);
    }
    private int Reference(int pointer, bool isTable = true)
    {
        var relative = Int32(pointer);
        if (relative < 4) throw new InvalidDataException("A material drop offset is invalid.");
        var target = checked(pointer + relative);
        Bounds(target, 4);
        if (isTable) _ = Table(target);
        return target;
    }
    private int Field(int table, int index, int width)
    {
        var (vtable, size) = Table(table);
        if (4 + index * 2 >= UInt16(vtable)) return 0;
        var offset = UInt16(vtable + 4 + index * 2);
        if (offset == 0) return 0;
        if (offset < 4 || offset > size - width)
            throw new InvalidDataException("A material drop field extends outside its table.");
        return checked(table + offset);
    }
    private sealed class Entry(SvMaterialDropRow row, int itemPointer, int itemTable, int itemField, int rateField, int countField)
    {
        public SvMaterialDropRow Row { get; set; } = row;
        public int ItemPointer { get; } = itemPointer;
        public int ItemTable { get; set; } = itemTable;
        public int ItemField { get; set; } = itemField;
        public int RateField { get; set; } = rateField;
        public int CountField { get; set; } = countField;
        public bool SharedItem { get; set; }
    }
}
