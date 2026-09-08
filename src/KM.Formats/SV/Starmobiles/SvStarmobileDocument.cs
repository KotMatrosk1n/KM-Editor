// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KM.Formats.SV.Starmobiles;

public sealed record SvStarmobileRow(
    string Id, int BossType, int Difficulty, string TrainerId, string EventId,
    IReadOnlyDictionary<string, int> Values);

public sealed class SvStarmobileDocument
{
    public const int MaximumSourceBytes = 1024 * 1024;
    public static readonly IReadOnlyList<string> Fields =
        ["level", "hp", "attack", "defense", "specialAttack", "specialDefense", "speed", "hpMultiplier"];
    public static readonly IReadOnlyList<string> MoveFields = ["move1", "move2", "move3", "move4"];
    public static readonly IReadOnlyList<string> TraitFields = ["ability", "type1", "type2"];
    public static bool IsSignatureMove(int value) => value is >= 896 and <= 900;
    private readonly byte[] bytes;
    private readonly Dictionary<(string Row, string Field), int> offsets = [];
    private readonly List<(int Start, int End)> objects = [];
    private readonly HashSet<(int Start, int End)> metadata = [];
    public IReadOnlyList<SvStarmobileRow> Rows { get; }
    public string Revision { get; }

    public SvStarmobileDocument(byte[] source)
    {
        if (source.Length is < 8 or > MaximumSourceBytes)
            throw new InvalidDataException("Starmobile data exceeds the supported size.");
        bytes = source.ToArray();
        Revision = Convert.ToHexString(SHA256.HashData(bytes));
        var root = Table(Target(0));
        ReserveMetadata(0, 4);
        var vector = Target(Field(root, 0, 4));
        var count = Int(vector);
        if (count is < 1 or > 64) throw new InvalidDataException("Unsupported Starmobile row count.");
        Bounds(vector, checked(4 + count * 4));
        Claim(vector, 4 + count * 4);
        var rows = new List<SvStarmobileRow>();
        for (var index = 0; index < count; index++)
        {
            var wrapper = Table(Target(vector + 4 + index * 4));
            var table = Table(Target(Field(wrapper, 0, 4)));
            for (var field = 0; field < 16; field++)
                Field(table, field, field is >= 5 and <= 8 or 13 ? 2 : field is 14 or 15 ? 1 : 4);
            var type = Scalar(table, 0);
            var difficulty = Scalar(table, 1);
            var id = $"{index}:{type}:{difficulty}";
            var trainer = Text(table, 11);
            var eventId = Text(table, 12);
            var stats = Table(Target(Field(table, 3, 4)));
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < Fields.Count; i++)
            {
                var offset = i == 0 ? Field(table, 2, 4) : i == 7 ? Field(table, 4, 4) : Field(stats, i - 1, 4);
                // Missing scalar fields cannot be added without changing the source layout.
                if (offset == 0) continue;
                if (offsets.Values.Contains(offset)) throw new InvalidDataException("Shared Starmobile scalar storage is unsupported.");
                offsets.Add((id, Fields[i]), offset);
                values.Add(Fields[i], Int(offset));
            }
            for (var i = 0; i < MoveFields.Count; i++)
            {
                var offset = Field(table, 5 + i, 2);
                if (offset == 0) continue;
                if (offsets.Values.Contains(offset)) throw new InvalidDataException("Shared Starmobile scalar storage is unsupported.");
                offsets.Add((id, MoveFields[i]), offset);
                values.Add(MoveFields[i], Short(offset));
            }
            for (var i = 0; i < TraitFields.Count; i++)
            {
                var offset = Field(table, 13 + i, i == 0 ? 2 : 1);
                if (offset == 0) continue;
                if (offsets.Values.Contains(offset)) throw new InvalidDataException("Shared Starmobile scalar storage is unsupported.");
                offsets.Add((id, TraitFields[i]), offset);
                values.Add(TraitFields[i], i == 0 ? Short(offset) : bytes[offset]);
            }
            rows.Add(new(id, type, difficulty, trainer, eventId, values));
        }
        Rows = rows;
    }

    public byte[] Write(IReadOnlyList<(string Row, string Field, int Value)> edits)
    {
        if (edits.Count > 512) throw new InvalidDataException("Too many Starmobile edits.");
        var result = bytes.ToArray();
        var targets = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (edit.Field == "speed")
                throw new InvalidDataException("Starmobile Speed is determined by its locked signature move.");
            if (!offsets.TryGetValue((edit.Row, edit.Field), out var offset) || !targets.Add(offset))
                throw new InvalidDataException("The Starmobile edit target is missing or duplicated.");
            if (MoveFields.Contains(edit.Field))
            {
                if (edit.Field == "move1" || IsSignatureMove(Short(offset)) || IsSignatureMove(edit.Value)
                    || edit.Value is < 0 or > ushort.MaxValue)
                    throw new InvalidDataException("The Starmobile move is locked or outside its range.");
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset, 2), (ushort)edit.Value);
            }
            else if (edit.Field is "type1" or "type2")
            {
                if (edit.Value is < 0 or > 17) throw new InvalidDataException("Invalid Starmobile type.");
                result[offset] = (byte)edit.Value;
            }
            else if (edit.Field == "ability")
            {
                if (edit.Value is < 0 or > ushort.MaxValue) throw new InvalidDataException("Invalid Starmobile ability.");
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset, 2), (ushort)edit.Value);
            }
            else BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset, 4), edit.Value);
        }
        return result;
    }

    private (int Position, int Vtable, int Vlength, int Length) Table(int position)
    {
        Bounds(position, 4);
        var vtable = checked(position - Int(position));
        var length = Short(vtable);
        var objectLength = Short(vtable + 2);
        if (length < 4 || (length & 1) != 0 || objectLength < 4)
            throw new InvalidDataException("Invalid Starmobile table header.");
        Bounds(vtable, length);
        Bounds(position, objectLength);
        ReserveMetadata(vtable, length);
        Claim(position, objectLength);
        var occupied = new HashSet<int>();
        for (var slot = 4; slot < length; slot += 2)
        {
            var offset = Short(vtable + slot);
            if (offset != 0 && (offset < 4 || offset >= objectLength || !occupied.Add(offset)))
                throw new InvalidDataException("Invalid Starmobile field layout.");
        }
        return (position, vtable, length, objectLength);
    }

    private int Field((int Position, int Vtable, int Vlength, int Length) table, int field, int width)
    {
        var slot = 4 + field * 2;
        var offset = slot < table.Vlength ? Short(table.Vtable + slot) : 0;
        if (offset == 0) return 0;
        if (offset + width > table.Length) throw new InvalidDataException("Truncated Starmobile field.");
        for (var other = 4; other < table.Vlength; other += 2)
        {
            var next = Short(table.Vtable + other);
            if (other != slot && next > offset && next < offset + width)
                throw new InvalidDataException("Overlapping Starmobile fields.");
        }
        return table.Position + offset;
    }

    private int Scalar((int Position, int Vtable, int Vlength, int Length) table, int field)
    {
        var offset = Field(table, field, 4);
        return offset == 0 ? 0 : Int(offset);
    }

    private string Text((int Position, int Vtable, int Vlength, int Length) table, int field)
    {
        var start = Target(Field(table, field, 4));
        var length = Int(start);
        if (length is < 1 or > 256) throw new InvalidDataException("Unsupported Starmobile binding.");
        Bounds(start + 4, length + 1);
        ReserveMetadata(start, 4 + length + 1);
        if (bytes[start + 4 + length] != 0) throw new InvalidDataException("Invalid Starmobile string.");
        return new UTF8Encoding(false, true).GetString(bytes, start + 4, length);
    }

    private int Target(int offset)
    {
        if (offset == 0 && objects.Count > 0) throw new InvalidDataException("Missing Starmobile reference.");
        var relative = Int(offset);
        if (relative < 4) throw new InvalidDataException("Invalid Starmobile reference.");
        var target = checked(offset + relative);
        Bounds(target, 4);
        return target;
    }

    private void Claim(int start, int length)
    {
        if (objects.Concat(metadata).Any(other => start < other.End && start + length > other.Start))
            throw new InvalidDataException("Shared Starmobile records are unsupported.");
        objects.Add((start, start + length));
    }

    private void ReserveMetadata(int start, int length)
    {
        var range = (start, start + length);
        if (metadata.Contains(range)) return;
        if (objects.Concat(metadata).Any(other => start < other.End && start + length > other.Start))
            throw new InvalidDataException("Starmobile metadata overlaps a record.");
        metadata.Add(range);
    }

    private int Int(int offset) { Bounds(offset, 4); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)); }
    private int Short(int offset) { Bounds(offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)); }
    private void Bounds(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException("Truncated Starmobile data.");
    }
}
