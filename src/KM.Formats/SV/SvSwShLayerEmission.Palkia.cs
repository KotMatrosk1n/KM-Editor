// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract>? PalkiaContracts(byte[] bytes, PreviewScene scene)
    {
        const string stem = "pm0484_12_00";
        if (!scene.Primitives.Any(p => p.Name.StartsWith(stem + "_", StringComparison.Ordinal))) return null;
        if (scene.Primitives.Any(p => !p.Name.StartsWith(stem + "_", StringComparison.Ordinal)))
            throw new InvalidDataException("Origin Palkia source identity is ambiguous.");
        var b = new ModelBuffer(bytes); var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var (name, mesh, vertices, indices, transparent) in new[]
        {
            ("body_c", "pearl", 134, 648, false), ("body_a_02", "cover", 218, 1152, true),
        })
        {
            var p = scene.Primitives.Single(p => p.Material.Name == name);
            if (p.Name != stem + "_" + mesh + "_mesh_shape" || p.Vertices.Length != vertices * 16 || p.Indices.Length != indices
                || p.Material.Alpha != (transparent ? "BlendPreMultiAlpha" : "Opaque"))
                throw new InvalidDataException("Origin Palkia emission geometry changed.");
            var m = b.Tables(b.Root, 1, 128).Single(m => b.Text(m, 0) == name);
            if (!b.Tables(m, 1, 16).Select(s => b.Text(s, 0)).SequenceEqual(transparent
                ? ["Transparent", "TransparentInner"] : new[] { "Standard" }))
                throw new InvalidDataException("Origin Palkia emission shader changed.");
            var scalars = b.Tables(m, 4, 256).ToDictionary(t => b.Text(t, 0)!, t => b.Field(t, 1) is var at && at != 0 ? b.Float(at) : 0);
            var vectors = b.Tables(m, 7, 256).ToDictionary(t => b.Text(t, 0)!, t =>
            { var at = b.Field(t, 1); return new Vector4(b.Float(at), b.Float(at + 4), b.Float(at + 8), b.Float(at + 12)); });
            var initial = transparent ? new[] { 0f, 0, .2f, .2f } : Enumerable.Repeat(.2f, 4).ToArray();
            if (vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0) || scalars["EmissionIntensity"] != 0
                || Enumerable.Range(1, 4).Any(i => scalars["LayerMaskScale" + i] != 1 || scalars["EmissionIntensityLayer" + i] != initial[i - 1])
                || transparent && (scalars["FresnelAlphaMin"] != 1 || scalars["FresnelAlphaMax"] != 1 || scalars["FresnelAngleBias"] != 0))
                throw new InvalidDataException("Origin Palkia emission controls changed.");
            Vector3 Color(string key)
            {
                var c = vectors[key];
                if (!float.IsFinite(c.X + c.Y + c.Z) || c.X < 0 || c.Y < 0 || c.Z < 0 || c.X > 1 || c.Y > 1 || c.Z > 1 || c.W != 1)
                    throw new InvalidDataException("Origin Palkia emission palette changed.");
                return new(c.X, c.Y, c.Z);
            }
            var letter = transparent ? "a" : "c";
            var maps = b.Tables(m, 2, 64).ToDictionary(t => b.Text(t, 0)!, t => t);
            var samplers = b.Tables(m, 3, 32);
            foreach (var (role, suffix) in new[] { ("BaseColorMap", "alb"), ("LayerMaskMap", "lym"), ("NormalMap", "nrm"), ("AOMap", "ao") })
            {
                var map = maps[role]; var path = b.Text(map, 1); var sampler = b.Value(map, 2);
                if (path != stem + "_body_" + letter + "_" + suffix + ".bntx"
                    && !(suffix == "alb" && path == stem + "_body_" + letter + "_rare_alb.bntx")
                    || sampler >= samplers.Length || b.Value(samplers[sampler], 9) != 1 || b.Value(samplers[sampler], 10) != 1)
                    throw new InvalidDataException("Origin Palkia emission texture or sampler changed.");
            }
            var tile = MathF.Floor(p.Indices.Min(i => p.Vertices[i * 16 + 7]));
            if (p.Indices.Any(i => p.Vertices[i * 16 + 7] < tile || p.Vertices[i * 16 + 7] > tile + 1))
                throw new InvalidDataException("Origin Palkia emission UV tile changed.");
            result.Add(name, new(name, b.Text(maps["LayerMaskMap"], 1)!, Enumerable.Range(1, 4).Select(i => Color("EmissionColorLayer" + i)).ToArray())
            {
                BaseColors = Enumerable.Range(1, 4).Select(i => Color("BaseColorLayer" + i)).ToArray(),
                Initial = initial, Transparent = transparent, NormalizedCoverage = true,
                GlobalEmissionColor = Color("EmissionColor"), MaskWrap = Vector2.One,
                PreservesTextureAlpha = transparent, ColorUvOrigin = transparent ? new Vector2(0, tile) : null,
            });
        }
        return result;
    }
}
