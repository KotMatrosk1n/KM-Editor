// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Identifies the qualified Standard surface that uses authored geometry normals without a normal texture.</summary>
public static class SvSwShGeometricNormalMaterial
{
    public static bool IsQualified(PreviewPrimitive primitive, ModelBuffer source, int material)
    {
        if (source.Text(material, 0) != "body_c") return false;
        var maps = source.Tables(material, 2, 64);
        if (!maps.Any(m => Path.GetFileName(source.Text(m, 1)) == "pm0483_12_00_body_c_alb.bntx")) return false;
        if (primitive.Material.Name != "body_c" || primitive.Name != "pm0483_12_00_body_a_mesh_shape"
            || primitive.Material.Alpha != "Opaque" || primitive.Vertices.Length != 5824 * 16 || primitive.Indices.Length != 438
            || maps.Length != 1 || source.Text(maps[0], 0) != "BaseColorMap")
            throw new InvalidDataException("Origin Dialga geometric normal surface requires another adapter.");
        var shaders = source.Tables(material, 1, 16);
        if (shaders.Length != 1 || source.Text(shaders[0], 0) != "Standard")
            throw new InvalidDataException("Origin Dialga geometric normal shader requires another adapter.");
        var expectedOptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EnableBaseColorMap"] = "True", ["EnableNormalMap"] = "False", ["EnableParallaxMap"] = "False",
            ["EnableMetallicMap"] = "False", ["EnableRoughnessMap"] = "False", ["EnableEmissionColorMap"] = "False",
            ["EnableAOMap"] = "False", ["EnableAlphaTest"] = "False", ["EnableVertexBaseColor"] = "False",
        };
        var options = source.Tables(shaders[0], 1, 128);
        if (options.Length != expectedOptions.Count || options.Select(o => source.Text(o, 0)).Distinct().Count() != options.Length
            || options.Any(o => !expectedOptions.TryGetValue(source.Text(o, 0) ?? "", out var value) || source.Text(o, 1) != value))
            throw new InvalidDataException("Origin Dialga geometric normal shader options differ.");
        var expectedScalars = new Dictionary<string, float>(StringComparer.Ordinal)
        { ["DiscardValue"] = 0, ["Metallic"] = 1, ["Roughness"] = .5f, ["EmissionIntensity"] = 0 };
        var scalars = source.Tables(material, 4, 256);
        if (scalars.Length != expectedScalars.Count || scalars.Select(s => source.Text(s, 0)).Distinct().Count() != scalars.Length
            || scalars.Any(s => !expectedScalars.TryGetValue(source.Text(s, 0) ?? "", out var value)
                || (source.Field(s, 1) is var at && at != 0 ? source.Float(at) : 0) != value))
            throw new InvalidDataException("Origin Dialga geometric normal controls differ.");
        var transforms = source.Tables(material, 7, 256).Where(v => source.Text(v, 0) == "UVScaleOffset").ToArray();
        if (transforms.Length != 1) throw new InvalidDataException("Origin Dialga geometric normal coordinates are missing.");
        var uv = source.Field(transforms[0], 1);
        if (uv == 0 || new Vector4(source.Float(uv), source.Float(uv + 4), source.Float(uv + 8), source.Float(uv + 12)) != new Vector4(1, 1, 0, 0))
            throw new InvalidDataException("Origin Dialga geometric normal coordinates differ.");
        var sampler = source.Tables(material, 3, 32)[checked((int)source.Value(maps[0], 2))];
        if (source.Value(sampler, 9) != 1 || source.Value(sampler, 10) != 1
            || Enumerable.Range(0, 9).Append(11).Any(f => source.Value(sampler, f) != 0))
            throw new InvalidDataException("Origin Dialga geometric normal sampler differs.");
        var border = source.Field(sampler, 12);
        if (border == 0 || Enumerable.Range(0, 4).Any(c => source.Float(border + c * 4) != 1))
            throw new InvalidDataException("Origin Dialga geometric normal sampler border differs.");
        foreach (var index in primitive.Indices.Distinct())
        {
            if (index >= 5824) throw new InvalidDataException("Origin Dialga geometric normal index is invalid.");
            var at = checked((int)index) * 16 + 3;
            var normal = new Vector3(primitive.Vertices[at], primitive.Vertices[at + 1], primitive.Vertices[at + 2]);
            if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z) || normal.LengthSquared() < 1e-12f)
                throw new InvalidDataException("Origin Dialga authored geometry normal is invalid.");
        }
        return true;
    }
}
