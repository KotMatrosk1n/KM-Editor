// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Formats.SV;

/// <summary>Retains qualified static source parameters in shared animation palettes.</summary>
public static class SvSwShStaticSourcePalette
{
    private sealed record Parameter(string Native, string Source, bool Vector = true);

    public static bool Supports(int species, int form) => form == 0 && species is 731 or 733 or 779;

    private static Parameter[] Parameters(int species, string material)
    {
        if (species == 733 && material is "body_b_00" or "body_b_01" or "body_b_02" or "body_b_03")
            return Enumerable.Range(1, 4).Select(i => new Parameter(SvSwShSourceCoverage.VectorNames[i - 1], "BaseColorLayer" + i)).ToArray();
        if (material is not ("l_eye" or "r_eye")) return [];
        var layers = species switch { 731 => new[] { 1, 2, 3 }, 779 => [2, 3, 4], _ => [] };
        return layers.Select(i => new Parameter(ZaSwShEyelidMaterial.EmissionVectorNames[i - 1], "EmissionColorLayer" + i)).ToArray();
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
                    if (source.Tables(material, parameter.Vector ? 2 : 1, 256).Any(t => source.Text(t, 0) == parameter.Source))
                        throw new InvalidDataException("An authored animation channel requires another static palette adapter.");
        }
    }

    public static byte[] Animation(byte[] animation, byte[] ordinaryModel, byte[] paletteModel,
        IReadOnlyDictionary<string, string> originalNames, int species, int form)
    {
        if (!Supports(species, form)) throw new InvalidDataException("Static source palette identity is not qualified.");
        var ordinary = Values(ordinaryModel); var palette = Values(paletteModel);
        return SvSwShDolphinPalette.BindConstants(animation, ordinary.Vectors, palette.Vectors, ordinary.Scalars, palette.Scalars);

        (Dictionary<string, Dictionary<string, Vector3>> Vectors, Dictionary<string, Dictionary<string, float>> Scalars) Values(byte[] model)
        {
            var source = new ModelBuffer(model); var materials = source.Tables(source.Root, 6, 128).ToDictionary(m => source.Text(m, 0)!);
            var vectors = new Dictionary<string, Dictionary<string, Vector3>>(StringComparer.Ordinal);
            var scalars = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
            foreach (var pair in originalNames)
            {
                var parameters = Parameters(species, pair.Value);
                if (parameters.Length == 0) continue;
                if (!materials.TryGetValue(pair.Key, out var material)) throw new InvalidDataException("Static source palette material is missing.");
                var expected = species == 733
                    ? pair.Value is "body_b_02" or "body_b_03" ? SvSwShSourceBase.MirroredProgramName : SvSwShSourceBase.ProgramName
                    : ZaSwShEyelidMaterial.EmissionProgramName;
                foreach (var (field, binding) in new[] { (3, 8), (5, 10) })
                {
                    var (start, count) = source.Vector(source.Root, field, 4, 256); var index = source.Value(material, binding);
                    if (index >= count || source.StringAt(start + checked((int)index) * 4) != expected)
                        throw new InvalidDataException("Static palette needs its qualified material program.");
                }
                var colors = new Dictionary<string, Vector3>(StringComparer.Ordinal); var values = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var parameter in parameters)
                {
                    var row = source.Tables(material, parameter.Vector ? 14 : 13, 256).Single(t => source.Text(t, 0) == parameter.Native);
                    var at = source.Field(row, 1);
                    if (at == 0) throw new InvalidDataException("Static source palette parameter is absent.");
                    if (parameter.Vector) colors.Add(parameter.Native, new(source.Float(at), source.Float(at + 4), source.Float(at + 8)));
                    else values.Add(parameter.Native, source.Float(at));
                }
                vectors.Add(pair.Key, colors); scalars.Add(pair.Key, values);
            }
            if (vectors.Count == 0) throw new InvalidDataException("Static source palette has no qualified consumers.");
            return (vectors, scalars);
        }
    }
}
