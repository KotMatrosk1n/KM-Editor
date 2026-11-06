// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract> KyogreContracts(byte[] bytes, PreviewScene scene)
    {
        var source = new ModelBuffer(bytes);
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var name in new[] { "body_b_00", "body_b_02" })
        {
            var meshes = name == "body_b_00"
                ? new[] { "bodyura", "core", "entrails", "entrailstail", "mouthura" } : new[] { "mouth" };
            if (!scene.Primitives.Where(p => p.Material.Name == name).Select(p => p.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(meshes.Select(m => "pm0382_51_00_" + m + "_mesh_shape").Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Primal Kyogre emission geometry requires another adapter.");
            var material = source.Tables(source.Root, 1, 128).Single(t => source.Text(t, 0) == name);
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!,
                p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
            {
                var at = source.Field(p, 1);
                return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
            });
            float[] initial = [1, 1, 1, 0];
            if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(new[] { "IkCharacter" })
                || vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0) || scalars["EmissionIntensity"] != 0
                || vectors["BaseColor"] != Vector4.One
                || Enumerable.Range(1, 4).Any(layer => scalars["LayerMaskScale" + layer] != 1
                    || scalars["EmissionIntensityLayer" + layer] != initial[layer - 1]))
                throw new InvalidDataException("Primal Kyogre emission surface requires another adapter.");
            var baseColors = Enumerable.Range(1, 4).Select(layer => vectors["BaseColorLayer" + layer]).ToArray();
            var colors = Enumerable.Range(1, 4).Select(layer => vectors["EmissionColorLayer" + layer]).ToArray();
            if (colors.Concat(baseColors).Any(c => !float.IsFinite(c.X + c.Y + c.Z + c.W) || c.X < 0 || c.X > 2
                    || c.Y < 0 || c.Y > 2 || c.Z < 0 || c.Z > 2 || c.W != 1))
                throw new InvalidDataException("Primal Kyogre emission color exceeds its supported range.");
            var texture = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "LayerMaskMap");
            var samplerIndex = source.Value(texture, 2);
            var samplers = source.Tables(material, 3, 32);
            var selector = source.Tables(material, 9, 256).Single(p => source.Text(p, 0) == "UVIndexLayerMask");
            var baseTexture = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "BaseColorMap");
            var baseSampler = source.Value(baseTexture, 2);
            if (source.Text(texture, 1) != "pm0382_51_00_body_b_lym.bntx" || samplerIndex >= samplers.Length
                || source.Text(baseTexture, 1) != "pm0382_51_00_body_b_alb.bntx" || baseSampler >= samplers.Length
                || source.Value(samplers[baseSampler], 9) != 6 || source.Value(samplers[baseSampler], 10) != 1
                || source.Value(selector, 1) != uint.MaxValue || source.Value(samplers[samplerIndex], 9) != 6
                || source.Value(samplers[samplerIndex], 10) != 1)
                throw new InvalidDataException("Primal Kyogre emission mask requires another adapter.");
            // Apply the static palette in the native shader. Both opaque surfaces share
            // the authored albedo and mask, including their mips and mirrored U sampling.
            result.Add(name, new(name, source.Text(texture, 1)!, colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray())
            {
                Initial = initial, MaximumIntensity = 2,
                BaseColors = baseColors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray(),
                MirroredMask = true, MaskWrap = new(6, 1),
            });
        }
        return result;
    }
}
