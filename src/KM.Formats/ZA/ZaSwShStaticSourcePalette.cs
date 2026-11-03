// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SV;

namespace KM.Formats.ZA;

/// <summary>Retains qualified static Z-A colors in each palette's shared clips.</summary>
public static class ZaSwShStaticSourcePalette
{
    private sealed record Parameter(string Native, string Source, string? Scalar = null);

    public static bool Supports(int species, int form) => species == 665 && form is >= 0 and <= 19
        || (species, form) is (354, 1) or (359, 2) or (376, 1) or (382, 1) or (668, 1);

    private static Parameter[] Parameters(int species, string material)
    {
        // These colors are authored only in the two source material palettes.
        // Every clip animates their independent intensities, not the colors.
        if (species == 354 && material is "r_eye_a" or "l_eye_a" or "eye_b")
            return Enumerable.Range(1, 4).Select(layer => new Parameter(
                ZaSwShEyelidMaterial.EmissionVectorNames[layer - 1], "EmissionColorLayer" + layer)).ToArray();
        if (species == 665 && material is "l_eye" or "r_eye"
            || species == 376 && material is "l_eye_a" or "r_eye_a")
            return [new("EffectColor01", "EmissionColorLayer5", "EmissionIntensityLayer5")];
        int[] layers = (species, material) switch
        {
            (359, "body_b_01") => [1, 2],
            (668, "body_a_00") or (382, "body_b_02") => [1, 2, 3, 4],
            (668, "body_a_02") => [1, 2],
            (668, "body_a_01") => [2],
            (382, "body_b_00") => [1, 2, 3],
            _ => [],
        };
        return layers.Select(layer => new Parameter(SvSwShSourceCoverage.VectorNames[layer - 1], "BaseColorLayer" + layer)).ToArray();
    }

    public static void RequireAnimation(byte[] bytes, int species, int form)
    {
        if (!Supports(species, form)) return;
        var source = new ModelBuffer(bytes);
        foreach (var node in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(node, 4);
            if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
                foreach (var parameter in Parameters(species, source.Text(material, 0)!))
                    if (source.Tables(material, 2, 256).Any(t => source.Text(t, 0) == parameter.Source)
                        || parameter.Scalar is not null && source.Tables(material, 1, 256).Any(t => source.Text(t, 0) == parameter.Scalar))
                        throw new InvalidDataException("An authored Z-A channel requires another static palette adapter.");
        }
    }

    public static byte[] Animation(byte[] animation, byte[] ordinaryModel, byte[] paletteModel,
        IReadOnlyDictionary<string, string> originalNames, int species, int form)
    {
        if (!Supports(species, form)) throw new InvalidDataException("Static Z-A palette identity is not qualified.");
        return SvSwShDolphinPalette.BindConstants(animation, Values(ordinaryModel), Values(paletteModel));

        Dictionary<string, Dictionary<string, Vector3>> Values(byte[] model)
        {
            var source = new ModelBuffer(model);
            var materials = source.Tables(source.Root, 6, 128).ToDictionary(m => source.Text(m, 0)!);
            var result = new Dictionary<string, Dictionary<string, Vector3>>(StringComparer.Ordinal);
            foreach (var pair in originalNames)
            {
                var parameters = Parameters(species, pair.Value);
                if (parameters.Length == 0) continue;
                if (!materials.TryGetValue(pair.Key, out var material)) throw new InvalidDataException("Static Z-A palette material is missing.");
                var expected = species == 354 ? ZaSwShEyelidMaterial.EmissionProgramName
                    : parameters[0].Scalar is not null ? ZaSwShEyelidMaterial.HighlightProgramName : SvSwShSourceBase.ProgramName;
                foreach (var (field, binding) in new[] { (3, 8), (5, 10) })
                {
                    var (start, count) = source.Vector(source.Root, field, 4, 256);
                    var index = source.Value(material, binding);
                    if (index >= count || source.StringAt(start + checked((int)index) * 4) != expected)
                        throw new InvalidDataException("Static Z-A palette needs its qualified material program.");
                }
                var vectors = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                foreach (var parameter in parameters)
                {
                    var row = source.Tables(material, 14, 256).Single(t => source.Text(t, 0) == parameter.Native);
                    var at = source.Field(row, 1);
                    if (at == 0) throw new InvalidDataException("Static Z-A palette color is absent.");
                    vectors.Add(parameter.Native, new(source.Float(at), source.Float(at + 4), source.Float(at + 8)));
                }
                result.Add(pair.Key, vectors);
            }
            if (result.Count == 0) throw new InvalidDataException("Static Z-A palette has no qualified consumers.");
            return result;
        }
    }
}
