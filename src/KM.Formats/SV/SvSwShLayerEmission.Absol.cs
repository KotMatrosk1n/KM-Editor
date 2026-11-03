// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract> AbsolZContracts(byte[] bytes, PreviewScene scene)
    {
        var source = new ModelBuffer(bytes);
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var name in new[] { "body_b_00", "body_b_01" })
        {
            var meshes = name == "body_b_00" ? new[] { "body" } : new[] { "face", "body", "teeth", "tail" };
            if (!scene.Primitives.Where(p => p.Material.Name == name).Select(p => p.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(meshes.Select(m => "pm0359_52_00_" + m + "_mesh_shape").Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Mega Absol Z palette geometry requires another adapter.");
            var material = source.Tables(source.Root, 1, 128).Single(t => source.Text(t, 0) == name);
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!,
                p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
            {
                var at = source.Field(p, 1);
                return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
            });
            if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(new[] { "IkCharacter" })
                || vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0) || vectors["BaseColor"] != Vector4.One
                || scalars["EmissionIntensity"] != 0 || scalars["SaturationPower"] != 1 || scalars["BaseColorDarkness"] != 0
                || vectors.Keys.Any(k => k.StartsWith("BaseColorLayer", StringComparison.Ordinal)
                    && !Enumerable.Range(1, 4).Any(layer => k == "BaseColorLayer" + layer))
                || Enumerable.Range(1, 4).Any(layer => scalars["LayerMaskScale" + layer] != 1
                    || scalars["EmissionIntensityLayer" + layer] != 0 || vectors["EmissionColorLayer" + layer] != new Vector4(0, 0, 0, 1)))
                throw new InvalidDataException("Mega Absol Z palette surface requires another adapter.");
            var colors = Enumerable.Range(1, 4).Select(layer => vectors["BaseColorLayer" + layer]).ToArray();
            if (colors.Any(c => !float.IsFinite(c.X + c.Y + c.Z + c.W) || c.X < 0 || c.X > 2
                || c.Y < 0 || c.Y > 2 || c.Z < 0 || c.Z > 2 || c.W != 1))
                throw new InvalidDataException("Mega Absol Z palette color exceeds its supported range.");
            var textures = source.Tables(material, 2, 64);
            var mask = textures.Single(t => source.Text(t, 0) == "LayerMaskMap");
            var albedo = textures.Single(t => source.Text(t, 0) == "BaseColorMap");
            var samplers = source.Tables(material, 3, 32);
            var selector = source.Tables(material, 9, 256).Single(t => source.Text(t, 0) == "UVIndexLayerMask");
            if (source.Text(mask, 1) != "pm0359_52_00_body_b_lym.bntx" || source.Text(albedo, 1) != "pm0359_52_00_body_b_alb.bntx"
                || source.Value(selector, 1) != uint.MaxValue
                || new[] { mask, albedo }.Any(t => source.Value(t, 2) >= samplers.Length
                    || source.Value(samplers[source.Value(t, 2)], 9) != 6 || source.Value(samplers[source.Value(t, 2)], 10) != 1))
                throw new InvalidDataException("Mega Absol Z palette sampling requires another adapter.");
            // The two opaque surfaces share the authored albedo and full mask mip chain.
            // Their palette differs; retaining it in native constants avoids duplicate images.
            result.Add(name, new(name, source.Text(mask, 1)!, new Vector3[4])
            {
                BaseColors = colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray(),
                MirroredMask = true, MaskWrap = new(6, 1), MaximumIntensity = 0,
            });
        }
        return result;
    }
}
