// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KM.Formats.SV.Blueberry;

public enum SvBlueberryKind { BbqRewards, SupportBoard, Snacksworth }
public sealed record SvBlueberryRow(string Id, string LabelKey, int Group, int Difficulty, int Goal,
    int Species, IReadOnlyDictionary<string, int> Values);

/// <summary>Edits existing Blueberry records without rebuilding unrelated data.</summary>
public sealed class SvBlueberryDocument
{
    public const int MaximumSourceBytes = 4 * 1024 * 1024;
    public const int MaximumBp = 9_999_999;
    public static readonly string[] EligibilityFields = ["soloScarlet", "soloViolet", "groupScarlet", "groupViolet"];
    private readonly byte[] bytes;
    private readonly SvBlueberryKind kind;
    private readonly Dictionary<string, (int Slot, Table Table)> records = [];
    private readonly List<(int Start, int End)> objects = [];
    private readonly HashSet<(int Start, int End)> metadata = [];
    private sealed record Table(int At, int Vtable, int Vlength, int Length);
    public IReadOnlyList<SvBlueberryRow> Rows { get; }
    public string Revision { get; }
    public static string PathFor(SvBlueberryKind kind) => kind switch
    {
        SvBlueberryKind.BbqRewards => "world/data/club/MissionInfo/MissionInfo_array.bin",
        SvBlueberryKind.SupportBoard => "world/data/club/ClubRoomBoardInfo/ClubRoomBoardInfo_array.bin",
        SvBlueberryKind.Snacksworth => "world/data/event/s2_sub_012_legend_poke/release_poke/release_poke_array.bin",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public SvBlueberryDocument(byte[] source, SvBlueberryKind kind)
    {
        if (source.Length is < 8 or > MaximumSourceBytes) throw new InvalidDataException("Unsupported Blueberry data size.");
        bytes = source.ToArray(); this.kind = kind;
        Revision = Convert.ToHexString(SHA256.HashData(bytes));
        Reserve(0, 4, true);
        var root = ReadTable(Target(0));
        var vector = Target(Required(root, 0, 4));
        var count = Int(vector);
        if (count is < 1 or > 512) throw new InvalidDataException("Unsupported Blueberry row count.");
        Bounds(vector, checked(4 + 4 * count)); Reserve(vector, 4 + 4 * count, false);
        var rows = new List<SvBlueberryRow>();
        for (var i = 0; i < count; i++)
        {
            var slot = vector + 4 + i * 4;
            var table = ReadTable(Target(slot));
            SvBlueberryRow row;
            if (kind == SvBlueberryKind.BbqRewards)
            {
                var info = Required(table, 0, 36);
                var type = Int(info + 4); var id = Int(info + 8); var difficulty = bytes[info + 29];
                if (bytes[info + 28] > 2 || difficulty > 2) throw new InvalidDataException("Unknown quest classification.");
                row = new($"{type}:{id}:{difficulty}", $"{type}:{id}", bytes[info + 28], difficulty,
                    BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(info + 12, 2)), 0,
                    new Dictionary<string, int> { ["rewardBp"] = Int(info + 16) });
            }
            else if (kind == SvBlueberryKind.SupportBoard)
            {
                ValidateFields(table, [1, 4, 1, 1, 1, 1, 4, 4, 4, 1, 1]);
                row = new(Scalar(table, 0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Text(table, 1),
                    Scalar(table, 2, 1), 0, Scalar(table, 9, 1), 0,
                    new Dictionary<string, int> { ["costBp"] = Scalar(table, 6, 4) });
                Text(table, 7); Text(table, 8);
            }
            else
            {
                ValidateFields(table, [4, 4, 2, 4, 1, 1, 1, 1]);
                var values = EligibilityFields.Select((name, index) => (name, value: Scalar(table, index + 4, 1)))
                    .ToDictionary(pair => pair.name, pair => pair.value);
                if (values.Values.Any(value => value is not (0 or 1))) throw new InvalidDataException("Invalid treat eligibility.");
                row = new(Scalar(table, 0, 4).ToString(System.Globalization.CultureInfo.InvariantCulture), Text(table, 1),
                    0, 0, 0, Scalar(table, 2, 2), values);
            }
            if (!records.TryAdd(row.Id, (slot, table))) throw new InvalidDataException("Duplicate Blueberry record identity.");
            rows.Add(row);
        }
        Rows = rows;
    }

    public static bool ValidValue(SvBlueberryKind kind, string field, int value) => kind == SvBlueberryKind.Snacksworth
        ? EligibilityFields.Contains(field) && value is 0 or 1
        : field == (kind == SvBlueberryKind.BbqRewards ? "rewardBp" : "costBp") && value is >= 0 and <= MaximumBp;

    public byte[] Write(IReadOnlyList<(string Row, string Field, int Value)> edits)
    {
        if (edits.Count > 2048) throw new InvalidDataException("Too many Blueberry edits.");
        using var output = new MemoryStream(); output.Write(bytes);
        Span<byte> scalar = stackalloc byte[4];
        var seen = new HashSet<(string, string)>();
        foreach (var group in edits.GroupBy(edit => edit.Row))
        {
            if (!records.TryGetValue(group.Key, out var record)) throw new InvalidDataException("Missing Blueberry record.");
            var changes = group.ToArray();
            foreach (var edit in changes)
                if (!ValidValue(kind, edit.Field, edit.Value) || !seen.Add((edit.Row, edit.Field)))
                    throw new InvalidDataException("Invalid or duplicate Blueberry field.");
            var table = record.Table;
            var expanded = kind != SvBlueberryKind.BbqRewards && changes.Any(edit =>
                Field(table, kind == SvBlueberryKind.SupportBoard ? 6 : 4 + Array.IndexOf(EligibilityFields, edit.Field),
                    kind == SvBlueberryKind.SupportBoard ? 4 : 1) == 0 && edit.Value != 0);
            var target = expanded ? Expand(output, table, record.Slot) : table.At;
            foreach (var edit in changes)
            {
                int at;
                if (kind == SvBlueberryKind.BbqRewards) at = Required(table, 0, 36) + 16;
                else if (expanded) at = target + ExpandedOffsets()[kind == SvBlueberryKind.SupportBoard ? 6 : 4 + Array.IndexOf(EligibilityFields, edit.Field)];
                else at = Field(table, kind == SvBlueberryKind.SupportBoard ? 6 : 4 + Array.IndexOf(EligibilityFields, edit.Field), kind == SvBlueberryKind.SupportBoard ? 4 : 1);
                if (at == 0) continue;
                output.Position = at;
                if (kind == SvBlueberryKind.Snacksworth) output.WriteByte((byte)edit.Value);
                else { BinaryPrimitives.WriteInt32LittleEndian(scalar, edit.Value); output.Write(scalar); }
            }
        }
        var result = output.ToArray();
        _ = new SvBlueberryDocument(result, kind);
        return result;
    }

    private int[] ExpandedOffsets() => kind == SvBlueberryKind.Snacksworth
        ? [4, 8, 12, 16, 20, 21, 22, 23] : [4, 8, 12, 13, 14, 15, 16, 20, 24, 28, 29];
    private int Expand(MemoryStream output, Table table, int slot)
    {
        var offsets = ExpandedOffsets();
        if (table.Vlength > 4 + offsets.Length * 2) throw new InvalidDataException("Cannot expand an unknown Blueberry schema.");
        output.Position = output.Length;
        while (output.Position % 4 != 0) output.WriteByte(0);
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        var vt = checked((int)output.Position); var vlength = 4 + offsets.Length * 2;
        var size = kind == SvBlueberryKind.Snacksworth ? 24 : 32;
        writer.Write((ushort)vlength); writer.Write((ushort)size);
        foreach (var offset in offsets) writer.Write((ushort)offset);
        while (output.Position % 4 != 0) output.WriteByte(0);
        var target = checked((int)output.Position); writer.Write(new byte[size]);
        output.Position = target; writer.Write(target - vt);
        var widths = kind == SvBlueberryKind.Snacksworth ? new[] { 4, 4, 2, 4, 1, 1, 1, 1 } : [1, 4, 1, 1, 1, 1, 4, 4, 4, 1, 1];
        for (var i = 0; i < offsets.Length; i++)
        {
            var at = target + offsets[i];
            if (i == 1 || kind == SvBlueberryKind.SupportBoard && i is 7 or 8)
            {
                var sourceField = Field(table, i, 4);
                if (sourceField == 0)
                {
                    output.Position = vt + 4 + 2 * i; writer.Write((ushort)0); continue;
                }
                output.Position = output.Length;
                while (output.Position % 4 != 0) output.WriteByte(0);
                var start = checked((int)output.Position); var text = Encoding.UTF8.GetBytes(Text(table, i));
                writer.Write(text.Length); writer.Write(text); writer.Write((byte)0);
                output.Position = at; writer.Write(start - at);
            }
            else
            {
                output.Position = at; var value = Scalar(table, i, widths[i]);
                if (widths[i] == 4) writer.Write(value); else if (widths[i] == 2) writer.Write((ushort)value); else writer.Write((byte)value);
            }
        }
        output.Position = slot; writer.Write(target - slot);
        return target;
    }

    private Table ReadTable(int at)
    {
        var vt = checked(at - Int(at)); var length = Short(vt); var size = Short(vt + 2);
        if (length < 4 || length % 2 != 0 || size < 4) throw new InvalidDataException("Invalid Blueberry table.");
        Bounds(vt, length); Bounds(at, size); Reserve(vt, length, true); Reserve(at, size, false);
        var occupied = new HashSet<int>();
        for (var i = 4; i < length; i += 2)
        { var offset = Short(vt + i); if (offset != 0 && (offset < 4 || offset >= size || !occupied.Add(offset))) throw new InvalidDataException("Invalid Blueberry field layout."); }
        return new(at, vt, length, size);
    }
    private void ValidateFields(Table table, int[] widths) { for (var i = 0; i < widths.Length; i++) Field(table, i, widths[i]); }
    private int Field(Table table, int index, int width)
    {
        var slot = 4 + index * 2; var offset = slot < table.Vlength ? Short(table.Vtable + slot) : 0;
        if (offset == 0) return 0;
        if (offset + width > table.Length || width > 1 && (table.At + offset) % Math.Min(width, 4) != 0) throw new InvalidDataException("Invalid Blueberry field bounds.");
        for (var i = 4; i < table.Vlength; i += 2)
        { var next = Short(table.Vtable + i); if (i != slot && next > offset && next < offset + width) throw new InvalidDataException("Overlapping Blueberry fields."); }
        return table.At + offset;
    }
    private int Required(Table table, int index, int width) => Field(table, index, width) is > 0 and var at ? at : throw new InvalidDataException("Missing Blueberry field.");
    private int Scalar(Table table, int index, int width) => Field(table, index, width) is > 0 and var at ? width == 4 ? Int(at) : width == 2 ? Short(at) : bytes[at] : 0;
    private string Text(Table table, int index)
    {
        var field = Field(table, index, 4); if (field == 0) return "";
        var at = Target(field); var length = Int(at);
        if (length is < 0 or > 4096) throw new InvalidDataException("Invalid Blueberry label.");
        Bounds(at + 4, length + 1); Reserve(at, length + 5, true);
        if (bytes[at + 4 + length] != 0) throw new InvalidDataException("Invalid Blueberry string.");
        return new UTF8Encoding(false, true).GetString(bytes, at + 4, length);
    }
    private int Target(int at) { var offset = Int(at); if (offset < 4) throw new InvalidDataException("Invalid Blueberry reference."); var target = checked(at + offset); Bounds(target, 4); return target; }
    private void Reserve(int start, int length, bool shared)
    {
        var range = (start, checked(start + length));
        if (shared && metadata.Contains(range)) return;
        if (objects.Concat(metadata).Any(other => start < other.End && range.Item2 > other.Start)) throw new InvalidDataException("Overlapping Blueberry objects.");
        if (shared) metadata.Add(range); else objects.Add(range);
    }
    private int Int(int at) { Bounds(at, 4); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4)); }
    private int Short(int at) { Bounds(at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2)); }
    private void Bounds(int at, int count) { if (at < 0 || count < 0 || at > bytes.Length - count) throw new InvalidDataException("Truncated Blueberry data."); }
}
