// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static PreviewMaterialTrack ReadGroudonFifthUv(ModelBuffer source, int parameter, string name, float rate)
    {
        if (name != "body_b_01") throw new InvalidDataException("Primal Groudon fifth-layer UV material changed.");
        var channels = source.Table(parameter, 1);
        var values = new[] { 1f, 1f, 0f, 0f };
        var result = Enumerable.Range(0, 4).Select(channel =>
        {
            var curve = source.Table(channels, channel);
            var keys = ReadColorKeys(source, curve, rate, false);
            var rows = source.Tables(curve, 0, 18000);
            if (keys[0].Frame != 0 || keys.Any(k => channel < 3 ? k.Value[0] != values[channel] : k.Value[0] > 1)
                || rows.Any(k => source.Value(k, 2) != 0 || source.Value(k, 3) != 0
                    || source.Value(k, 4) != (channel == 3 ? 1u : 0u)))
                throw new InvalidDataException("Primal Groudon fifth-layer UV keys changed.");
            return keys;
        }).ToArray();
        // The spare native layer UV scalars drive only the independently sampled fifth mask.
        return new(name, "UVScaleOffset3", result);
    }

    private static IReadOnlyDictionary<string, Contract> GroudonContracts(byte[] bytes, PreviewScene scene)
    {
        var source = new ModelBuffer(bytes);
        var expected = new Dictionary<string, (string Mesh, int Vertices, int Indices)[]>(StringComparer.Ordinal)
        {
            ["body_b_03"] = [("bodyinc_a",1495,276),("body",3998,1926),("foot",786,666)],
            ["body_b_01"] = [("bodyinc_a",1495,4485)],
            ["body_b_00"] = [("foot",786,2316),("head",268,774)],
            ["eye"] = [("eye",90,420)],
            ["body_b_02"] = [("bodyinc_b",937,3615)],
        };
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var (name, geometry) in expected)
        {
            var sections = scene.Primitives.Where(p => p.Material.Name == name).ToArray();
            if (sections.Length != geometry.Length || geometry.Any(g => !sections.Any(p =>
                p.Name == "pm0383_51_00_" + g.Mesh + "_mesh_shape" && p.Vertices.Length == g.Vertices * 16 && p.Indices.Length == g.Indices)))
                throw new InvalidDataException("Primal Groudon emission geometry changed.");
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p => {
                var at = source.Field(p, 1); return new Vector4(source.Float(at),source.Float(at+4),source.Float(at+8),source.Float(at+12)); });
            var transparent = name == "body_b_02"; var fifth = name == "body_b_01";
            float[] initial = name == "body_b_03" ? [0,1,1,0] : name == "eye" ? [1,1,1,0] : [1,0,0,1];
            if (!source.Tables(material,1,16).Select(t => source.Text(t,0)).SequenceEqual(new[] { transparent ? "Transparent" : fifth ? "Standard" : "IkCharacter" })
                || vectors["UVScaleOffset"] != new Vector4(1,1,0,0) || scalars["EmissionIntensity"] != 0
                || Enumerable.Range(1,4).Any(i => scalars["LayerMaskScale"+i] != 1 || scalars["EmissionIntensityLayer"+i] != initial[i-1]))
                throw new InvalidDataException("Primal Groudon emission surface changed.");
            var colors = Enumerable.Range(1,4).Select(i => vectors["EmissionColorLayer"+i]).ToArray();
            var bases = Enumerable.Range(1,4).Select(i => vectors["BaseColorLayer"+i]).ToArray();
            if (colors.Concat(bases).Any(c => !float.IsFinite(c.X+c.Y+c.Z) || c.X < 0 || c.Y < 0 || c.Z < 0 || c.X > 2 || c.Y > 2 || c.Z > 2 || c.W != 1))
                throw new InvalidDataException("Primal Groudon palette changed.");
            string Texture(string role)
            {
                var texture = source.Tables(material,2,64).Single(t => source.Text(t,0) == role);
                var samplers = source.Tables(material,3,32); var sampler = source.Value(texture,2);
                if (sampler >= samplers.Length || source.Value(samplers[sampler],9) != 1 || source.Value(samplers[sampler],10) != 1)
                    throw new InvalidDataException("Primal Groudon emission addressing changed.");
                return source.Text(texture,1)!;
            }
            if (fifth && (scalars["LayerMaskScale5"] != 1 || scalars["EmissionIntensityLayer5"] != 1
                || vectors["BaseColorLayer5"] != vectors["EmissionColorLayer5"]
                || Enumerable.Range(0, 3).Any(c => vectors["BaseColorLayer5"][c] is < 0 or > 2)
                || vectors["UVScaleOffset5"] != new Vector4(1,1,0,0)))
                throw new InvalidDataException("Primal Groudon fifth layer changed.");
            var baseColor = vectors.GetValueOrDefault("BaseColor",Vector4.One);
            result.Add(name,new(name,Texture("LayerMaskMap"),colors.Select(c => new Vector3(c.X,c.Y,c.Z)).ToArray()) {
                Initial = initial, MaximumIntensity = 2, Transparent = transparent, PreservesTextureAlpha = transparent,
                BaseColors = bases.Select(c => new Vector3(c.X*baseColor.X,c.Y*baseColor.Y,c.Z*baseColor.Z)).ToArray(),
                MaskWrap = new(1,1), FifthMask = fifth ? Texture("OpacityMap1") : null,
                GlobalEmissionColor = fifth ? new(vectors["EmissionColorLayer5"].X,vectors["EmissionColorLayer5"].Y,vectors["EmissionColorLayer5"].Z) : Vector3.Zero,
                GlobalEmissionInitial = fifth ? 1 : 0,
            });
        }
        return result;
    }
}
