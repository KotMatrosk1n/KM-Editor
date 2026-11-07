// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using KM.Formats.Models;

namespace KM.Formats.ZA;

/// <summary>Exact Mega Eelektross material ownership for the FresnelBlend layer-color cut.</summary>
/// <remarks>This interface is a private building block. It does not admit a complete shader or profile.</remarks>
public static class ZaSwShEelektrossMaterial
{
    public const string Program = "ZaSourceEelektrossBlend";
    public const int FirstScalar = 52;
    public const string NormalSource = "D26E3B5B99664670E949C9E50977E55CB00FF18D3C5463FFDFFF68A09DC0ECA7";
    public const string RareSource = "E8BF7BBD42973E236FA0711ED0239EC71E7F39FB0117732C64E5A84D2A675C25";
    public sealed record Parameter(int Buffer, int Word, string Property, int Component)
    {
        public string NativeName => $"SourceFb{Buffer}_{Word:D3}";
    }
    private static readonly Parameter[] Definitions =
    [
        new(7, 33, "EmissionIntensity", -1),
        new(7, 34, "EmissionIntensityLayer1", -1),
        new(7, 35, "EmissionIntensityLayer2", -1),
        new(8, 4, "UVScaleOffset", 0), new(8, 5, "UVScaleOffset", 1),
        new(8, 6, "UVScaleOffset", 2), new(8, 7, "UVScaleOffset", 3),
        new(8, 56, "BaseColorLayer5", 0), new(8, 57, "BaseColorLayer5", 1),
        new(8, 58, "BaseColorLayer5", 2), new(8, 59, "BaseColorLayer5", 3),
        new(8, 60, "BaseColorLayer6", 0), new(8, 61, "BaseColorLayer6", 1),
        new(8, 62, "BaseColorLayer6", 2), new(8, 63, "BaseColorLayer6", 3),
        new(8, 64, "BaseColorLayer7", 0), new(8, 65, "BaseColorLayer7", 1),
        new(8, 66, "BaseColorLayer7", 2), new(8, 67, "BaseColorLayer7", 3),
    ];
    public static IReadOnlyList<Parameter> Inputs { get; } = Array.AsReadOnly(Definitions);
    public static bool Owns(string name) => name is "body_b_00" or "body_a_02" or "body_b_01";
    public static int Index(int buffer, int word)
    {
        var index = Array.FindIndex(Definitions, p => p.Buffer == buffer && p.Word == word);
        return index < 0 ? throw new InvalidDataException("Unowned FresnelBlend source word.") : index;
    }
    public static int ScalarWord(int buffer, int word) => FirstScalar + Index(buffer, word);

    // No inferred defaults: every owned property must be explicitly present in either pinned palette.
    public static float[] Values(byte[] sourceMaterials, string materialName)
    {
        if (!Owns(materialName)) throw new InvalidDataException("Unowned Mega Eelektross body material.");
        var source = RequireSource(sourceMaterials);
        var matches = source.Tables(source.Root, 1, 128).Where(m => source.Text(m, 0) == materialName).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Missing or ambiguous FresnelBlend material.");
        var material = matches[0];
        var shaders = source.Tables(material, 1, 16);
        if (shaders.Length != 1 || source.Text(shaders[0], 0) != "FresnelBlend")
            throw new InvalidDataException("Mega Eelektross body requires literal FresnelBlend ownership.");
        var scalar = source.Tables(material, 4, 512).ToDictionary(p => source.Text(p, 0)!, p => p, StringComparer.Ordinal);
        var vector = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p => p, StringComparer.Ordinal);
        return Definitions.Select(p =>
        {
            if (!(p.Buffer == 7 ? scalar : vector).TryGetValue(p.Property, out var row))
                throw new InvalidDataException("FresnelBlend source default is absent.");
            var at = source.Field(row, 1);
            // A present scalar table with an omitted value field is the serialized f32 zero.
            // Vector storage is a struct and is mandatory for every requested component.
            if (at == 0 && p.Component >= 0) throw new InvalidDataException("FresnelBlend vector storage is absent.");
            var value = at == 0 ? 0 : source.Float(at + Math.Max(0, p.Component) * 4);
            if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite FresnelBlend source value.");
            return value;
        }).ToArray();
    }

    public static ModelBuffer RequireSource(byte[] sourceMaterials)
    {
        var hash = Convert.ToHexString(SHA256.HashData(sourceMaterials));
        if (hash is not (NormalSource or RareSource)) throw new InvalidDataException("Unqualified Eelektross palette bytes.");
        return new(sourceMaterials);
    }

    // Allocates a dedicated 71-scalar/19-vector material, using the exact native Body00 prefix.
    // The caller must separately supply the complete qualified paired shader. This creates no admission.
    public static byte[] Template(byte[] bytes, byte[] sourceMaterials, string originalName)
    {
        var values = Values(sourceMaterials, originalName);
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var originals = source.Tables(source.Root, 6, 128);
        var original = originals.Single(m => Shader(source, m, 3, 8) == "Body00" && Shader(source, m, 5, 10) == "Body00");
        RequirePrefix(source, original, false);
        foreach (var field in new[] { 3, 5 })
        {
            var (start, count) = source.Vector(source.Root, field, 4, 256);
            var names = Enumerable.Range(0, count).Select(i => source.StringAt(start + 4 * i)).ToArray();
            if (names.Contains(Program, StringComparer.Ordinal) || count == 256)
                throw new InvalidDataException("Eelektross program already allocated or interface full.");
            var slots = writer.Vector(source.Root, field, count + 1);
            for (var i = 0; i < count; i++) writer.Pointer(slots[i], writer.String(names[i]));
            writer.Pointer(slots[count], writer.String(Program));
        }
        var materialSlots = writer.Vector(source.Root, 6, originals.Length + 1);
        var fields = Enumerable.Range(2, 9).Concat(Enumerable.Range(15, 5));
        var absent = Enumerable.Range(0, 21).Where(f => f is not (0 or 8 or 10 or 13)
            && source.Field(original, f) == 0).ToHashSet();
        var copied = fields.Where(f => !absent.Contains(f)).ToDictionary(f => f, f => f is 8 or 10
            ? ModelTableBuilder.Integer(source.Vector(source.Root, f == 8 ? 3 : 5, 4, 256).Count)
            : source.Slice(source.Field(original, f), f is 3 or 4 or >= 15 ? 1 : 4).ToArray());
        var sizes = new[] { 3, 4, 15, 16, 17, 18, 19 }.ToDictionary(f => f, _ => 1);
        var target = writer.Table(21, copied, sizes, absent); writer.Pointer(materialSlots[^1], target);
        var copy = writer.Append(bytes);
        for (var i = 0; i < originals.Length; i++) writer.Pointer(materialSlots[i], copy + originals[i]);
        foreach (var field in new[] { 1, 11, 12, 14, 20 })
            if (!absent.Contains(field)) writer.Reference(target, field, copy + source.Table(original, field));
        writer.Text(target, 0, Program);
        var oldRows = source.Tables(original, 13, 256);
        var rows = writer.Vector(target, 13, FirstScalar + Definitions.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            var at = i < FirstScalar ? source.Field(oldRows[i], 1) : 0;
            var missing = i < FirstScalar && at == 0;
            var payload = i < FirstScalar ? missing ? null : source.Slice(at, 4).ToArray()
                : ModelTableBuilder.Floats([values[i - FirstScalar]]);
            var row = writer.Table(2, payload is null ? null : new Dictionary<int, byte[]> { [1] = payload },
                absent: missing ? new HashSet<int> { 1 } : null);
            writer.Pointer(rows[i], row);
            writer.Text(row, 0, i < FirstScalar ? source.Text(oldRows[i], 0)! : Definitions[i - FirstScalar].NativeName);
        }
        return writer.Finish(source.Root);
    }

    public static float[] ReadNativeValues(byte[] native, string name = Program)
    {
        var source = new ModelBuffer(native);
        var material = source.Tables(source.Root, 6, 128).Single(m => source.Text(m, 0) == name);
        RequirePrefix(source, material, true);
        if (Shader(source, material, 3, 8) != Program || Shader(source, material, 5, 10) != Program)
            throw new InvalidDataException("Eelektross paired native program is missing.");
        return source.Tables(material, 13, 256).Skip(FirstScalar).Select(row =>
        {
            var at = source.Field(row, 1);
            if (at == 0 || !float.IsFinite(source.Float(at))) throw new InvalidDataException("Owned native scalar storage is invalid.");
            return source.Float(at);
        }).ToArray();
    }

    /// <summary>Replace only the 19 owned scalars in an already allocated paired material.</summary>
    public static byte[] BindValues(byte[] native, ReadOnlySpan<float> values, string name = Program)
    {
        if (values.Length != Definitions.Length) throw new InvalidDataException("Eelektross scalar count changed.");
        foreach (var value in values) if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite owned scalar.");
        _ = ReadNativeValues(native, name);
        var result = native.ToArray(); var source = new ModelBuffer(result);
        var material = source.Tables(source.Root, 6, 128).Single(m => source.Text(m, 0) == name);
        var rows = source.Tables(material, 13, 256); var writer = new ModelTableBuilder(result);
        for (var i = 0; i < values.Length; i++) writer.Single(source.Field(rows[FirstScalar + i], 1), values[i]);
        return writer.Finish(source.Root);
    }

    private static string Shader(ModelBuffer source, int material, int names, int index)
    {
        var (start, count) = source.Vector(source.Root, names, 4, 256); var value = source.Value(material, index);
        if (value >= count) throw new InvalidDataException("Native program index is out of range.");
        return source.StringAt(start + checked((int)value) * 4);
    }
    private static void RequirePrefix(ModelBuffer source, int material, bool owned)
    {
        var rows = source.Tables(material, 13, 256); var vectors = source.Tables(material, 14, 256);
        string Hash(IEnumerable<int> tables) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\0', tables.Select(t => source.Text(t, 0)!)))));
        if (rows.Length != FirstScalar + (owned ? Definitions.Length : 0) || vectors.Length != 19
            || Hash(rows.Take(FirstScalar)) != "65546F5C46C3A7E9783E28C7BEF17991620EEAFA941263BF9C730C8C926E5654"
            || Hash(vectors) != "71D6CC0CE5C96E75984E1D5E7A7217FB14358F7B8D24195BA23EE7930F634AB8"
            || owned && !rows.Skip(FirstScalar).Select(r => source.Text(r, 0)).SequenceEqual(Definitions.Select(p => p.NativeName)))
            throw new InvalidDataException("Eelektross native material interface changed.");
        if (!owned) return;
        // BindValues accepts this allocator's canonical scalar tail. Names alone
        // cannot prove ownership: an enlarged row may alias another row's value.
        var tailStart = rows[FirstScalar] - 8;
        var priorEnd = 0;
        foreach (var row in rows.Take(FirstScalar))
        {
            var vt = row - source.I32(row);
            var end = checked(row + source.U16(vt + 2));
            var text = source.Offset(source.Field(row, 0));
            var textEnd = checked(text + 5 + (int)source.U32(text));
            if (end > tailStart || textEnd > tailStart)
                throw new InvalidDataException("Native prefix overlaps the owned scalar tail.");
            priorEnd = Math.Max(priorEnd, Math.Max(end, textEnd));
        }
        foreach (var row in vectors)
        {
            var vt = row - source.I32(row);
            if (row + source.U16(vt + 2) > tailStart)
                throw new InvalidDataException("Native vector overlaps the owned scalar tail.");
        }
        if ((priorEnd + 3 & ~3) != tailStart)
            throw new InvalidDataException("Owned scalar tail does not follow the allocated prefix.");
        var expectedVtable = tailStart;
        foreach (var row in rows.Skip(FirstScalar))
        {
            var vt = row - source.I32(row);
            if (vt != expectedVtable || row != vt + 8 || source.U16(vt) != 8 || source.U16(vt + 2) != 12
                || source.Field(row, 0) != row + 4 || source.Field(row, 1) != row + 8
                || source.Offset(row + 4) != row + 12)
                throw new InvalidDataException("Owned scalar row storage is not canonical and disjoint.");
            var end = checked(row + 17 + (int)source.U32(row + 12));
            expectedVtable = (end + 3) & ~3;
            if (row == rows[^1] && end != source.Length)
                throw new InvalidDataException("Unexpected bytes after the owned scalar tail.");
        }
    }
}
