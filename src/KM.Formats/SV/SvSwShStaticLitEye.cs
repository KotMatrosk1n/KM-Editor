// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Qualifies Bramblin's and Brambleghast's static SSS eyeballs for the native lit surface.</summary>
public static class SvSwShStaticLitEye
{
    public static void RequireMaterial(PreviewPrimitive primitive, byte[] materials)
    {
        var source = new ModelBuffer(materials);
        var tables = source.Tables(source.Root, 1, 128).Where(t => source.Text(t, 0) == primitive.Material.Name).ToArray();
        var qualifiedGeometry = primitive.Name == "pm1079_00_00_eyeball_mesh_shape"
            || primitive.Name == "pm1078_00_00_eye_mesh_shape" && primitive.Vertices.Length == 152 * 16
                && primitive.Indices.Length == 336;
        if (!qualifiedGeometry || primitive.Material.Name is not ("l_eye" or "r_eye")
            || primitive.Material.Alpha != "Opaque" || tables.Length != 1)
            throw new InvalidDataException("Static lit eyes require the qualified opaque eyeball geometry.");
        var material = tables[0];
        var shaders = source.Tables(material, 1, 16).Select(t => source.Text(t, 0));
        var scalars = source.Tables(material, 4, 256).ToDictionary(t => source.Text(t, 0)!,
            t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
        var vectors = source.Tables(material, 7, 256).ToDictionary(t => source.Text(t, 0)!, t =>
        {
            var at = source.Field(t, 1);
            return at == 0 ? Vector4.Zero : new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
        });
        var textures = source.Tables(material, 2, 64);
        var samplers = source.Tables(material, 3, 32);
        var options = source.Tables(material, 9, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Value(t, 1));
        // These eyes use the ordinary lit surface, with no secondary UV selectors,
        // weather or crystal effects. CategoryLabel is donor metadata.
        var expectedOptions = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["CastShadow"] = 1, ["CategoryLabel"] = 6, ["EnableJewel"] = 0,
            ["EnableGlobalWeather"] = 0, ["EnableAdditionalLight"] = 1,
        };
        if (!shaders.SequenceEqual(new[] { "SSS" })
            || scalars.Count != 3 || scalars.GetValueOrDefault("NormalHeight", float.NaN) != 1
            || scalars.GetValueOrDefault("SSSMaskScale", float.NaN) != 1 || scalars.GetValueOrDefault("SSSMaskOffset", float.NaN) != 0
            || vectors.Count != 2 || vectors.GetValueOrDefault("UVScaleOffset") != new Vector4(1, 1, 0, 0)
            || vectors.GetValueOrDefault("SubsurfaceColor") != new Vector4(.2f, .2f, .2f, 1)
            || !textures.Select(t => source.Text(t, 0)).Order().SequenceEqual(new[] { "AOMap", "BaseColorMap", "NormalMap", "RoughnessMap", "SSSMaskMap" })
            || textures.Any(t => source.Value(t, 2) >= samplers.Length || source.Value(samplers[source.Value(t, 2)], 9) != 1
                || source.Value(samplers[source.Value(t, 2)], 10) != 1)
            || options.Count != expectedOptions.Count || expectedOptions.Any(p => !options.TryGetValue(p.Key, out var value) || value != p.Value))
            throw new InvalidDataException("Static lit eye material needs another surface adapter.");
        foreach (var index in primitive.Indices.Distinct())
            if (primitive.Vertices[index * 16 + 6] is not (> 0 and < 1) || primitive.Vertices[index * 16 + 7] is not (> 0 and < 1))
                throw new InvalidDataException("Static lit eye coordinates leave their authored unit tile.");
    }

    public static void RequireAnimation(byte[] bytes)
    {
        var source = new ModelBuffer(bytes);
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4);
            if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
                if (source.Text(material, 0) is "l_eye" or "r_eye"
                    && (source.Tables(material, 1, 256).Length != 0 || source.Tables(material, 2, 256).Length != 0))
                    throw new InvalidDataException("Static lit eyes cannot discard authored material animation.");
        }
    }
}
