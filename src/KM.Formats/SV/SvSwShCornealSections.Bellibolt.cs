// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShCornealSections
{
    private const string BelliboltStem = "pm1045_00_00";

    public static PreviewScene SplitBellibolt(PreviewScene scene, byte[] sourceMaterials)
    {
        if (scene.Primitives.Any(p => !p.Name.StartsWith(BelliboltStem + "_", StringComparison.Ordinal))
            || scene.Primitives.Count(p => p.Material.Name == "eye") != 1)
            throw new InvalidDataException("Bellibolt corneal sections require the qualified source model.");
        var b = new ModelBuffer(sourceMaterials); var matches = b.Tables(b.Root, 1, 128).Where(m => b.Text(m, 0) == "eye").ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Bellibolt eye material is missing or ambiguous.");
        var material = matches[0];
        var shaders = b.Tables(material, 1, 16);
        if (!shaders.Select(s => b.Text(s, 0)).SequenceEqual(new[] { "EyeClearCoat", "Eye" }))
            throw new InvalidDataException("Bellibolt corneal sections require the paired source shader.");
        var expectedOptions = new[]
        {
            new Dictionary<string, string> { ["EnableHighlight"] = "False", ["EyelidType"] = "Lower",
                ["EnableBaseColorMap1"] = "False", ["EnableNormalMap1"] = "True" },
            new Dictionary<string, string> { ["EnableNormalMap"] = "True", ["EnableParallaxMap"] = "False",
                ["EnableMetallicMap"] = "False", ["EnableRoughnessMap"] = "False", ["EnableEmissionColorMap"] = "False",
                ["EnableAOMap"] = "False", ["NumMaterialLayer"] = "5", ["EnableHighlight"] = "False",
                ["EyelidType"] = "Lower", ["EnableEyeClearCoat"] = "True", ["ShadingType"] = "SSS" },
        };
        for (var pass = 0; pass < shaders.Length; pass++)
        {
            var options = b.Tables(shaders[pass], 1, 64); var expectedPass = expectedOptions[pass];
            if (options.Length != expectedPass.Count || options.Select(o => b.Text(o, 0)).Distinct().Count() != options.Length
                || options.Any(o => !expectedPass.TryGetValue(b.Text(o, 0) ?? "", out var value) || b.Text(o, 1) != value))
                throw new InvalidDataException("Bellibolt eye shader options require another adapter.");
        }
        var scalars = b.Tables(material, 4, 256).ToDictionary(p => b.Text(p, 0)!, p => b.Field(p, 1) is var at && at != 0 ? b.Float(at) : 0);
        if (scalars.GetValueOrDefault("NormalHeight", float.NaN) != .8f || scalars.GetValueOrDefault("NormalHeight1", float.NaN) != 1
            || scalars.GetValueOrDefault("UVRotation", float.NaN) != 0 || scalars.GetValueOrDefault("UVRotation4", float.NaN) != 0)
            throw new InvalidDataException("Bellibolt eye normal/eyelid layout requires another adapter.");
        var expected = new Dictionary<string, string> { ["BaseColorMap"] = BelliboltStem + "_eye_alb.bntx",
            ["LayerMaskMap"] = BelliboltStem + "_eye_lym.bntx", ["NormalMap"] = BelliboltStem + "_eye_nrm.bntx",
            ["LowerEyelidColorMap"] = BelliboltStem + "_lower_eyelid_alb.bntx", ["SSSMaskMap"] = BelliboltStem + "_body_msk.bntx",
            ["NormalMap1"] = BelliboltStem + "_eye2_nrm.bntx" };
        var maps = b.Tables(material, 2, 64);
        if (!maps.Select(t => b.Text(t, 0)!).Order().SequenceEqual(expected.Keys.Order())
            || maps.Any(t => b.Text(t, 1) != expected[b.Text(t, 0)!]
                && !(b.Text(t, 0) == "LowerEyelidColorMap" && b.Text(t, 1) == BelliboltStem + "_lower_eyelid_rare_alb.bntx")))
            throw new InvalidDataException("Bellibolt corneal sections require all six authored eye maps.");
        var result = new List<PreviewPrimitive>();
        foreach (var part in scene.Primitives)
        {
            if (part.Material.Name != "eye") { result.Add(part); continue; }
            if (part.Name != BelliboltStem + "_body_mesh_shape" || part.Material.Alpha != "BlendPreMultiAlpha"
                || part.Vertices.Length != 1512 * 16 || part.Indices.Length != 120)
                throw new InvalidDataException("Bellibolt shared body/eye geometry changed.");
            var sections = new[] { part with { Indices = part.Indices[..60] }, part with { Indices = part.Indices[60..] } };
            if (!IsBelliboltPair("eye", sections)) throw new InvalidDataException("Bellibolt eye components are not independently qualified.");
            result.AddRange(sections);
        }
        return scene with { Primitives = result.ToArray() };
    }

    internal static bool IsBelliboltPair(string material, IReadOnlyList<PreviewPrimitive> parts)
    {
        if (material != "eye" || parts.Count != 2 || parts.Any(p => p.Material.Name != "eye" || p.Name != BelliboltStem + "_body_mesh_shape"
                || p.Material.Alpha != "BlendPreMultiAlpha" || p.Vertices.Length != 1512 * 16 || p.Indices.Length != 60
                || p.Indices.Any(i => i >= 1512)) || !parts[0].Vertices.AsSpan().SequenceEqual(parts[1].Vertices)) return false;
        var sets = parts.Select(p => p.Indices.ToHashSet()).ToArray();
        if (sets.Any(s => s.Count != 18) || sets[0].Overlaps(sets[1]) || sets.SelectMany(s => s).Distinct().Count() != 36) return false;
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
            if (seen.Count != 18 || seen.Any(i =>
                {
                    var at = checked((int)i) * 16; var u = part.Vertices[at + 6]; var v = part.Vertices[at + 7];
                    return !float.IsFinite(u + v) || u is <= 0 or >= .5f || v is <= 3 or >= 4;
                })) return false;
        }
        return true;
    }
}
