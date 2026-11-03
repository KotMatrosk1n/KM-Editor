// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract> RaichuContracts(byte[] bytes, PreviewScene scene)
    {
        var x = scene.Primitives.Any(p => p.Name.StartsWith("pm0026_51_00_", StringComparison.Ordinal));
        var expected = x ? new[] { "body_a_00", "body_b_00", "body_b_01", "body_b_02", "body_c" }
            : new[] { "body_a_00", "body_a_02" };
        var source = new ModelBuffer(bytes); var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var name in expected)
        {
            var meshes = x ? name switch
            {
                "body_a_00" => new[] { "pm0026_51_00_body_mesh_shape" },
                "body_b_00" => ["pm0026_51_00_tail_a_mesh_shape"],
                "body_b_01" => ["pm0026_51_00_tail_b_mesh_shape"],
                "body_b_02" => ["pm0026_51_00_body_mesh_shape", "pm0026_51_00_teeth_mesh_shape"],
                _ => ["pm0026_51_00_tail_c_mesh_shape"],
            } : name == "body_a_00"
                ? new[] { "pm0026_52_00_body_mesh_shape", "pm0026_52_00_rip_a_mesh_shape", "pm0026_52_00_rip_b_mesh_shape", "pm0026_52_00_rip_c_mesh_shape" }
                : ["pm0026_52_00_body_mesh_shape", "pm0026_52_00_teeth_mesh_shape"];
            if (!scene.Primitives.Where(p => p.Material.Name == name).Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(meshes))
                throw new InvalidDataException("Mega Raichu emission geometry requires another adapter.");
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
            { var at = source.Field(p, 1); return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12)); });
            var initial = x && name == "body_c" ? new[] { 1f, 0f, 0f, 0f } : new float[4];
            var mirrored = !x || name == "body_a_00";
            var expectedUv = new Vector4(mirrored ? 2 : 1, 1, 0, 0);
            if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["IkCharacter"])
                || vectors["UVScaleOffset"] != expectedUv
                || source.Tables(material, 2, 64).Any(t => source.Text(t, 0) == "EmissionColorMap")
                || scalars["EmissionIntensity"] != 0
                || Enumerable.Range(1, 4).Any(layer => scalars["LayerMaskScale" + layer] != 1 || scalars["EmissionIntensityLayer" + layer] != initial[layer - 1]))
                throw new InvalidDataException("Mega Raichu emission surface requires another adapter.");
            var colors = Enumerable.Range(1, 4).Select(layer => vectors["EmissionColorLayer" + layer]).ToArray();
            if (colors.Any(c => !float.IsFinite(c.X + c.Y + c.Z + c.W) || c.X < 0 || c.X > 2 || c.Y < 0 || c.Y > 2 || c.Z < 0 || c.Z > 2 || c.W != 1))
                throw new InvalidDataException("Mega Raichu emission color exceeds its supported range.");
            var texture = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "LayerMaskMap");
            var selector = source.Tables(material, 9, 256).SingleOrDefault(p => source.Text(p, 0) == "UVIndexLayerMask");
            var uvIndex = selector == 0 ? uint.MaxValue : source.Value(selector, 1);
            if (uvIndex is not (0 or uint.MaxValue)) throw new InvalidDataException("Mega Raichu emission mask coordinates need another adapter.");
            var samplers = source.Tables(material, 3, 32); var sampler = checked((int)source.Value(texture, 2));
            if ((uint)sampler >= samplers.Length) throw new InvalidDataException("Mega Raichu emission sampler is missing.");
            if (mirrored && (uvIndex != uint.MaxValue || source.Value(samplers[sampler], 9) != 6 || source.Value(samplers[sampler], 10) != 1))
                throw new InvalidDataException("Mega Raichu mirrored emission mask requires another adapter.");
            result.Add(name, new(name, source.Text(texture, 1)!, colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray())
            {
                Initial = initial, MaximumIntensity = x && name == "body_c" ? 2 : 1, MirroredMask = mirrored,
                MaskScale = new(scalars["LayerMaskScale1"], scalars["LayerMaskScale2"], scalars["LayerMaskScale3"], scalars["LayerMaskScale4"]),
                MaskUv = uvIndex == 0 ? new(1, 1, 0, 0) : vectors["UVScaleOffset"],
                MaskWrap = new(source.Value(samplers[sampler], 9), source.Value(samplers[sampler], 10)),
            });
        }
        return result;
    }
}
