// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SV;
using KM.Formats.ZA;

namespace KM.Formats.Models;

/// <summary>The explicit source-material interface of native-lighting ZA gems.</summary>
internal static class SwShPreviewFresnel
{
    public static ZaSwShFresnelMaterial.Settings? Settings(ModelBuffer data, int material)
    {
        string Program(int field, int selector)
        {
            var (start, count) = data.Vector(data.Root, field, 4, 256);
            var index = data.Value(material, selector);
            if (index >= count) throw new InvalidDataException("Preview shader index is invalid.");
            return data.StringAt(start + checked((int)index) * 4);
        }
        var vertex = Program(3, 8); var fragment = Program(5, 10);
        if (!vertex.StartsWith("ZaSourceFresnel", StringComparison.Ordinal)
            && !fragment.StartsWith("ZaSourceFresnel", StringComparison.Ordinal)) return null;
        foreach (var variant in new[] { 0, 6 })
        for (var layer = -1; layer <= 1; layer++) for (var ao = -1; ao <= 1; ao++)
        {
            if (variant == 6 && (layer != 0 || ao != 0)) continue;
            var settings = new ZaSwShFresnelMaterial.Settings(layer, ao, layer == 1 || ao == 1 ? 2 : 1)
            { SourceVariant = variant, MaskUv = 0 };
            if (vertex == settings.Program && fragment == settings.Program) return settings;
        }
        throw new InvalidDataException("ZA gem preview requires the qualified native-lighting shader pair.");
    }

    public static PreviewSurface Read(ModelBuffer data, int material, PreviewSurface surface)
    {
        var settings = Settings(data, material) ?? throw new InvalidDataException("ZA gem preview has no paired program.");
        var scalars = data.Tables(material, 13, 256);
        var vectors = data.Tables(material, 14, 256);
        if (scalars.Length != 52 + ZaSwShFresnelMaterial.ScalarNames.Length
            || !scalars.Skip(52).Select(t => data.Text(t, 0)).SequenceEqual(ZaSwShFresnelMaterial.ScalarNames)
            || vectors.Length != 23 || !vectors.Skip(19).Select(t => data.Text(t, 0)).SequenceEqual(SvSwShSourceCoverage.VectorNames))
            throw new InvalidDataException("ZA gem preview uniform interface changed.");
        var values = Enumerable.Range(0, 15).Select(_ => new float[4]).ToArray();
        for (var i = 0; i < ZaSwShFresnelMaterial.ScalarNames.Length; i++)
        {
            var at = data.Field(scalars[52 + i], 1);
            values[i / 4][i % 4] = at == 0 ? 0 : data.Float(at);
        }
        for (var i = 0; i < 3; i++)
        {
            var at = data.Field(vectors[19 + i], 1);
            if (at == 0) throw new InvalidDataException("ZA gem preview color is missing.");
            for (var c = 0; c < 3; c++) values[6 + i][c] = data.Float(at + c * 4);
        }
        if (values.SelectMany(v => v).Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("ZA gem preview uniform is nonfinite.");
        values[10] = [settings.LayerUv, settings.AmbientUv, settings.SourceVariant, settings.MaskUv];
        return surface with { RestorationEffect = 10, RestorationValues = values };
    }

    public static void RequireSampler(ModelBuffer data, int binding, bool cube)
        => SwShPreviewGemSampler.Require(data, binding, cube);
}
