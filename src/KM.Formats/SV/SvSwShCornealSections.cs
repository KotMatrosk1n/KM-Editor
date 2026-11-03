// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Retains independently oriented source eye surfaces sharing one UV island.</summary>
public static partial class SvSwShCornealSections
{
    private const string Stem = "pm1043_00_00";
    private static string? EyeMesh(string material, bool farigiraf = false) => (farigiraf, material) switch
    {
        (false, "l_eye_a") => Stem + "_eye_a_mesh_shape",
        (false, "l_eye_b") => Stem + "_eye_b_mesh_shape",
        (true, "eye_b") => "pm1032_00_00_outereye_a_mesh_shape",
        (true, "eye_c") => "pm1032_00_00_outereye_b_mesh_shape",
        _ => null,
    };

    public static PreviewScene SplitScovillain(PreviewScene scene, byte[] sourceMaterials) => Split(scene, sourceMaterials, false);
    public static PreviewScene SplitFarigiraf(PreviewScene scene, byte[] sourceMaterials) => Split(scene, sourceMaterials, true);

    private static PreviewScene Split(PreviewScene scene, byte[] sourceMaterials, bool farigiraf)
    {
        var stem = farigiraf ? "pm1032_00_00" : Stem;
        var vertices = farigiraf ? 82 : 54; var halfIndices = farigiraf ? 168 : 120;
        if (scene.Primitives.Any(p => !p.Name.StartsWith(stem + "_", StringComparison.Ordinal)))
            throw new InvalidDataException("Corneal sections require their qualified source model.");
        var b = new ModelBuffer(sourceMaterials); var result = new List<PreviewPrimitive>();
        foreach (var name in farigiraf ? new[] { "eye_b", "eye_c" } : new[] { "l_eye_a", "l_eye_b" })
        {
            if (scene.Primitives.Count(p => p.Material.Name == name) != 1)
                throw new InvalidDataException("Source corneal material is missing or ambiguous.");
            var material = b.Tables(b.Root, 1, 128).Single(m => b.Text(m, 0) == name);
            if (!b.Tables(material, 1, 16).Select(s => b.Text(s, 0)).SequenceEqual(new[] { "EyeClearCoat", "Eye" }))
                throw new InvalidDataException("Corneal sections require the paired source shader.");
            var scalars = b.Tables(material, 4, 256).ToDictionary(p => b.Text(p, 0)!, p => b.Field(p, 1) is var at && at != 0 ? b.Float(at) : 0);
            if (scalars.GetValueOrDefault("NormalHeight", float.NaN) != 1 || scalars.GetValueOrDefault("NormalHeight1", float.NaN) != 1)
                throw new InvalidDataException("Corneal sections require both authored normal strengths.");
            var expected = new Dictionary<string, string> { ["BaseColorMap"] = stem + "_eye_alb.bntx",
                ["LayerMaskMap"] = stem + (farigiraf ? "_" + name : "_eye") + "_lym.bntx",
                ["NormalMap"] = stem + (farigiraf ? "_eye_b" : "_eye") + "_nrm.bntx",
                ["NormalMap1"] = stem + (farigiraf ? "_eye2_" + name[^1] : "_eye2") + "_nrm.bntx" };
            var maps = b.Tables(material, 2, 64);
            if (maps.Length != 4 || !maps.Select(t => b.Text(t, 0)!).Order().SequenceEqual(expected.Keys.Order())
                || maps.Any(t => b.Text(t, 1) != expected[b.Text(t, 0)!]
                    && !(b.Text(t, 0) == "BaseColorMap" && b.Text(t, 1) == stem + "_eye_rare_alb.bntx")))
                throw new InvalidDataException("Corneal sections require every authored eye map.");
        }
        foreach (var part in scene.Primitives)
        {
            if (EyeMesh(part.Material.Name, farigiraf) is null) { result.Add(part); continue; }
            if (part.Name != EyeMesh(part.Material.Name, farigiraf) || part.Material.Alpha != "BlendPreMultiAlpha"
                || part.Vertices.Length != vertices * 16 || part.Indices.Length != halfIndices * 2)
                throw new InvalidDataException("Source corneal geometry changed.");
            var sections = new[] { part with { Indices = part.Indices[..halfIndices] }, part with { Indices = part.Indices[halfIndices..] } };
            if (!IsPair(part.Material.Name, sections, farigiraf))
                throw new InvalidDataException("Source corneal components are not independently qualified.");
            result.AddRange(sections);
        }
        return scene with { Primitives = result.ToArray() };
    }

    internal static bool IsScovillainPair(string material, IReadOnlyList<PreviewPrimitive> parts) => IsPair(material, parts, false);
    internal static bool IsFarigirafPair(string material, IReadOnlyList<PreviewPrimitive> parts) => IsPair(material, parts, true);

    private static bool IsPair(string material, IReadOnlyList<PreviewPrimitive> parts, bool farigiraf)
    {
        var mesh = EyeMesh(material, farigiraf); var vertices = farigiraf ? 82 : 54;
        var componentVertices = vertices / 2; var halfIndices = farigiraf ? 168 : 120;
        if (mesh is null || parts.Count != 2 || parts.Any(p => p.Name != mesh || p.Material.Name != material
                || p.Material.Alpha != "BlendPreMultiAlpha" || p.Vertices.Length != vertices * 16 || p.Indices.Length != halfIndices
                || p.Indices.Any(i => i >= vertices)) || !parts[0].Vertices.AsSpan().SequenceEqual(parts[1].Vertices)) return false;
        var sets = parts.Select(p => p.Indices.ToHashSet()).ToArray();
        if (sets.Any(s => s.Count != componentVertices) || sets[0].Overlaps(sets[1]) || sets.SelectMany(s => s).Distinct().Count() != vertices) return false;
        foreach (var part in parts)
        {
            var seen = new HashSet<uint> { part.Indices[0] }; bool changed;
            do
            {
                changed = false;
                for (var i = 0; i < part.Indices.Length; i += 3)
                    if (Enumerable.Range(0, 3).Any(k => seen.Contains(part.Indices[i + k])))
                        for (var k = 0; k < 3; k++) changed |= seen.Add(part.Indices[i + k]);
            } while (changed);
            if (seen.Count != componentVertices || seen.Any(i =>
                {
                    var at = checked((int)i) * 16; var u = part.Vertices[at + 6]; var v = part.Vertices[at + 7];
                    return !float.IsFinite(u + v) || u is <= 0 or >= 1 || v is <= 0 or >= 1;
                })) return false;
        }
        return true;
    }
}
