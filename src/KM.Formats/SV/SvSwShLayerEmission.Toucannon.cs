// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static Vector3[] ToucannonBaseColors(ModelBuffer source, int material,
        IReadOnlyDictionary<string, Vector4> vectors, string name)
    {
        var textures = source.Tables(material, 2, 64);
        var color = textures.Single(t => source.Text(t, 0) == "BaseColorMap");
        var mask = textures.Single(t => source.Text(t, 0) == "LayerMaskMap");
        if (source.Text(color, 1) is not ("pm0809_00_00_body_msk.bntx"
                or "pm0809_00_00_body_b_alb.bntx" or "pm0809_00_00_body_b_rare_alb.bntx")
            || (name == "body_b_00") != (source.Text(color, 1) != "pm0809_00_00_body_msk.bntx")
            || source.Text(mask, 1) != (name is "body_b_02" or "body_b_03"
                ? "pm0809_00_00_body_b_02_lym.bntx" : "pm0809_00_00_body_b_lym.bntx"))
            throw new InvalidDataException("Toucannon source palette textures require another adapter.");
        var samplers = source.Tables(material, 3, 32); var sampler = checked((int)source.Value(color, 2));
        if ((uint)sampler >= samplers.Length || source.Value(samplers[sampler], 9) != 1 || source.Value(samplers[sampler], 10) != 1
            || vectors.Keys.Any(k => k.StartsWith("UV", StringComparison.Ordinal) && k != "UVScaleOffset"))
            throw new InvalidDataException("Toucannon source albedo coordinates require another adapter.");
        return MiniorBaseColors(vectors);
    }

    /// <summary>Retains the unchanged wing albedo, shared with the layered body.</summary>
    public static bool PreservesToucannonUnlayeredAlbedo(byte[] bytes, PreviewPrimitive primitive)
    {
        if (primitive.Material.Name != "body_b_04" || primitive.Name is not
            ("pm0809_00_00_left_wing_mesh_shape" or "pm0809_00_00_right_wing_mesh_shape"
                or "pm0809_00_00_left_closewing_mesh_shape" or "pm0809_00_00_right_closewing_mesh_shape")) return false;
        var source = new ModelBuffer(bytes);
        var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == "body_b_04");
        var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
        { var at = source.Field(p, 1); return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12)); });
        var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
        var textures = source.Tables(material, 2, 64);
        if (primitive.Material.Alpha != "Opaque"
            || !source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["SSS"])
            || vectors.Count != 2 || vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0)
            || vectors["SubsurfaceColor"] != new Vector4(.4f, .4f, .4f, 1)
            || scalars.Count != 3 || scalars["NormalHeight"] != 1 || scalars["SSSMaskScale"] != 1 || scalars["SSSMaskOffset"] != 0
            || textures.Length != 5 || textures.Any(t => source.Text(t, 0) == "LayerMaskMap"))
            throw new InvalidDataException("Toucannon unlayered wing requires another adapter.");
        var color = textures.Single(t => source.Text(t, 0) == "BaseColorMap");
        var samplers = source.Tables(material, 3, 32); var sampler = checked((int)source.Value(color, 2));
        if (source.Text(color, 1) is not ("pm0809_00_00_body_b_alb.bntx" or "pm0809_00_00_body_b_rare_alb.bntx")
            || (uint)sampler >= samplers.Length || source.Value(samplers[sampler], 9) != 1 || source.Value(samplers[sampler], 10) != 1)
            throw new InvalidDataException("Toucannon wing albedo requires its original clamped source texture.");
        return true;
    }
}
