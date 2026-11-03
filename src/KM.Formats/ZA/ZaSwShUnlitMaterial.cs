// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.ZA;

/// <summary>The non-displaced, opaque Unlit wing material authored for Mega Clefable.</summary>
public static class ZaSwShUnlitMaterial
{
    public static readonly string[] ClefableWings =
        ["pm0036_51_00_wing_front_mesh_shape", "pm0036_51_00_wing_back_mesh_shape"];

    public static Vector3[] ClefablePalette(byte[] bytes, PreviewPrimitive primitive)
    {
        if (!ClefableWings.Contains(primitive.Name, StringComparer.Ordinal)
            || primitive.Material.Name != "body_b" || primitive.Material.Alpha != "Opaque")
            throw new InvalidDataException("Z-A unlit geometry or opacity requires another adapter.");
        var source = new ModelBuffer(bytes);
        var material = source.Tables(source.Root, 1, 128).Single(t => source.Text(t, 0) == "body_b");
        var scalars = source.Tables(material, 4, 128).ToDictionary(t => source.Text(t, 0)!,
            t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
        var vectors = source.Tables(material, 7, 128).ToDictionary(t => source.Text(t, 0)!, t =>
        {
            var at = source.Field(t, 1);
            return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
        });
        var selectors = source.Tables(material, 9, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Value(t, 1));
        if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["Unlit"])
            || !scalars.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(
                ["DisplacementHeight", "DiscardValue", "EmissionIntensity", "LayerMaskScale1", "LayerMaskScale2", "LayerMaskScale3", "LayerMaskScale4"])
            || scalars["DisplacementHeight"] != 0 || scalars["DiscardValue"] != 0
            || scalars["EmissionIntensity"] != .7f
            || Enumerable.Range(1, 4).Any(layer => scalars["LayerMaskScale" + layer] != 1)
            || !vectors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(
                ["BaseColor", "UVScaleOffset", "UVScaleOffset3", "BaseColorLayer1", "BaseColorLayer2", "BaseColorLayer3", "BaseColorLayer4"])
            || vectors["BaseColor"] != Vector4.One
            || vectors["UVScaleOffset"] != new Vector4(2, 1, 0, 0)
            || vectors["UVScaleOffset3"] != new Vector4(1, 1, 0, 0)
            || !selectors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(
                ["CastShadow", "CategoryLabel", "EnableJewel", "EnableGlobalWeather", "EnableDirt", "UVIndexLayer3"])
            || selectors["CastShadow"] != 0 || selectors["CategoryLabel"] != 6
            || selectors["EnableJewel"] != 0 || selectors["EnableGlobalWeather"] != 0
            || selectors["EnableDirt"] != 1 || selectors["UVIndexLayer3"] != 0)
            throw new InvalidDataException("Z-A unlit displacement, palette, or selector requires another adapter.");
        var textures = source.Tables(material, 2, 32);
        var samplers = source.Tables(material, 3, 32);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BaseColorMap"] = "pm0036_51_00_00_body_b_alb.bntx",
            ["LayerMaskMap"] = "pm0036_51_00_00_body_b_lym.bntx",
            ["DisplacementMap"] = "pm0036_51_00_00_body_b_msk.bntx",
        };
        if (textures.Length != expected.Count || textures.Select(t => source.Text(t, 0)).Distinct().Count() != expected.Count
            || samplers.Length != 3 || textures.Any(t => !expected.TryGetValue(source.Text(t, 0)!, out var path)
                || source.Text(t, 1) != path || source.Value(t, 2) >= samplers.Length)
            || samplers.Any(t => source.Value(t, 9) != 6 || source.Value(t, 10) != 1))
            throw new InvalidDataException("Z-A unlit texture sampling requires another adapter.");
        var colors = Enumerable.Range(1, 4).Select(layer => vectors["BaseColorLayer" + layer]).ToArray();
        if (colors.Any(c => c.W != 1 || !float.IsFinite(c.X + c.Y + c.Z)
            || c.X < 0 || c.X > 2 || c.Y < 0 || c.Y > 2 || c.Z < 0 || c.Z > 2))
            throw new InvalidDataException("Z-A unlit base colors exceed the supported range.");
        return colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray();
    }
}
