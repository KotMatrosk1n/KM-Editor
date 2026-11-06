// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private const string DialgaStem = "pm0483_12_00";
    private const string DialgaGem = DialgaStem + "_gem_mesh_shape";
    private static readonly Vector4 DialgaTint = new(.008568006f, .14126593f, 1.0000241f, 1);
    private static readonly Vector3 DialgaGlow = new(.029556919f, .73792315f, .9911121f);

    internal static IReadOnlyDictionary<string, Contract>? DialgaContracts(byte[] bytes, PreviewScene scene)
    {
        if (!scene.Primitives.Any(p => p.Name.StartsWith(DialgaStem + "_", StringComparison.Ordinal))) return null;
        if (scene.Primitives.Any(p => !p.Name.StartsWith(DialgaStem + "_", StringComparison.Ordinal)))
            throw new InvalidDataException("Origin Dialga source identity is ambiguous.");
        var b = new ModelBuffer(bytes); var materials = b.Tables(b.Root, 1, 128);
        if (!materials.Select(m => b.Text(m, 0)).Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "body_a_00", "body_b_00", "body_b_01", "body_c", "l_eye", "r_eye" }))
            throw new InvalidDataException("Origin Dialga source material closure changed.");
        var gem = scene.Primitives.Single(p => p.Material.Name == "body_b_01");
        var body = scene.Primitives.Single(p => p.Material.Name == "body_c");
        var table = materials.Single(m => b.Text(m, 0) == "body_c");
        if (!SvSwShGeometricNormalMaterial.IsQualified(body, b, table))
            throw new InvalidDataException("Origin Dialga uniform emission needs its geometric normal surface.");
        var vectors = b.Tables(table, 7, 256);
        var glow = vectors.Single(v => b.Text(v, 0) == "EmissionColor"); var at = b.Field(glow, 1);
        if (vectors.Length != 2 || new Vector4(b.Float(at), b.Float(at + 4), b.Float(at + 8), b.Float(at + 12)) != new Vector4(DialgaGlow, 1))
            throw new InvalidDataException("Origin Dialga body emission color changed.");
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal)
        {
            ["body_b_01"] = DialgaGemContract(bytes, gem),
            ["body_c"] = new("body_c", b.Text(b.Tables(table, 2, 64).Single(), 1)!, [DialgaGlow, Vector3.Zero, Vector3.Zero, Vector3.Zero])
                { UniformEmission = true, MaskUnused = true, MaskWrap = Vector2.One },
        };
        foreach (var (name, letter) in new[] { ("body_a_00", "a"), ("body_b_00", "b") })
        {
            var parts = scene.Primitives.Where(p => p.Material.Name == name).ToArray();
            var expected = letter == "a" ? new[] { (DialgaStem + "_body_a_mesh_shape", 5824, 16602) }
                : new[] { (DialgaStem + "_body_a_mesh_shape", 5824, 7800), (DialgaStem + "_body_b_mesh_shape", 911, 4422) };
            if (parts.Length != expected.Length || expected.Any(e => !parts.Any(p => p.Name == e.Item1
                    && p.Vertices.Length == e.Item2 * 16 && p.Indices.Length == e.Item3 && p.Material.Alpha == "Opaque")))
                throw new InvalidDataException("Origin Dialga emission map geometry changed.");
            table = materials.Single(m => b.Text(m, 0) == name);
            var shaders = b.Tables(table, 1, 16);
            var options = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EnableBaseColorMap"] = "True", ["EnableNormalMap"] = "True", ["EnableParallaxMap"] = "False",
                ["EnableMetallicMap"] = "True", ["EnableRoughnessMap"] = "True", ["EnableEmissionColorMap"] = "True",
                ["EnableAOMap"] = "True", ["EnableAlphaTest"] = "False", ["NumMaterialLayer"] = "1",
                ["EnableVertexBaseColor"] = "False", ["NumRequiredUV"] = "1",
            };
            if (shaders.Length != 1 || b.Text(shaders[0], 0) != "Standard")
                throw new InvalidDataException("Origin Dialga emission map shader changed.");
            var actual = b.Tables(shaders[0], 1, 128).ToDictionary(t => b.Text(t, 0)!, t => b.Text(t, 1));
            if (actual.Count != options.Count || options.Any(o => actual.GetValueOrDefault(o.Key) != o.Value))
                throw new InvalidDataException("Origin Dialga emission map options changed.");
            var scalars = b.Tables(table, 4, 256).ToDictionary(t => b.Text(t, 0)!, t => b.Field(t, 1) is var a && a != 0 ? b.Float(a) : 0);
            vectors = b.Tables(table, 7, 256);
            if (scalars.Count != 3 || scalars.GetValueOrDefault("NormalHeight", float.NaN) != 1
                || scalars.GetValueOrDefault("DiscardValue", float.NaN) != 0 || scalars.GetValueOrDefault("EmissionIntensity", float.NaN) != .1f
                || vectors.Length != 1 || b.Text(vectors[0], 0) != "UVScaleOffset")
                throw new InvalidDataException("Origin Dialga emission map controls changed.");
            at = b.Field(vectors[0], 1);
            if (new Vector4(b.Float(at), b.Float(at + 4), b.Float(at + 8), b.Float(at + 12)) != new Vector4(1, 1, 0, 0))
                throw new InvalidDataException("Origin Dialga emission map coordinates changed.");
            var maps = b.Tables(table, 2, 64).ToDictionary(t => b.Text(t, 0)!, t => t); var samplers = b.Tables(table, 3, 32);
            var roles = new Dictionary<string, string> { ["BaseColorMap"] = "alb", ["EmissionColorMap"] = "alb", ["NormalMap"] = "nrm", ["MetallicMap"] = "mtl", ["RoughnessMap"] = "rgn", ["AOMap"] = "ao" };
            if (maps.Count != roles.Count || roles.Keys.Any(role => !maps.ContainsKey(role)))
                throw new InvalidDataException("Origin Dialga emission map roles changed.");
            foreach (var (role, suffix) in roles)
            {
                var map = maps[role]; var path = b.Text(map, 1); var sampler = b.Value(map, 2);
                if (path != DialgaStem + "_body_" + letter + "_" + suffix + ".bntx"
                        && !(suffix == "alb" && path == DialgaStem + "_body_" + letter + "_rare_alb.bntx")
                    || sampler >= samplers.Length || b.Value(samplers[sampler], 9) != 1 || b.Value(samplers[sampler], 10) != 1)
                    throw new InvalidDataException("Origin Dialga emission map identity or sampler changed.");
            }
            if (b.Text(maps["BaseColorMap"], 1) != b.Text(maps["EmissionColorMap"], 1))
                throw new InvalidDataException("Origin Dialga emission map no longer shares its albedo.");
            result.Add(name, new(name, b.Text(maps["EmissionColorMap"], 1)!, new Vector3[4])
                { ColorMap = true, BaseColorMap = true, Initial = [.1f, 0, 0, 0], MaskWrap = Vector2.One });
        }
        return result;
    }

    public static Contract DialgaGemContract(byte[] bytes, PreviewPrimitive primitive)
    {
        RequireDialgaGemGeometry(primitive);
        var b = new ModelBuffer(bytes);
        var material = b.Tables(b.Root, 1, 128).Single(m => b.Text(m, 0) == "body_b_01");
        var shaders = b.Tables(material, 1, 16);
        if (!shaders.Select(s => b.Text(s, 0)).SequenceEqual(new[] { "Transparent", "TransparentInner" }))
            throw new InvalidDataException("Origin Dialga gem requires its two source passes.");
        var expectedOptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EnableBaseColorMap"] = "True", ["EnableNormalMap"] = "True", ["EnableParallaxMap"] = "False",
            ["EnableMetallicMap"] = "False", ["EnableRoughnessMap"] = "False", ["EnableEmissionColorMap"] = "False",
            ["EnableAOMap"] = "True", ["EnableAlphaTest"] = "False", ["NumMaterialLayer"] = "5", ["RefractionMode"] = "Thin",
        };
        for (var pass = 0; pass < shaders.Length; pass++)
        {
            var options = b.Tables(shaders[pass], 1, 128).ToDictionary(t => b.Text(t, 0)!, t => b.Text(t, 1));
            if (pass == 1)
            {
                expectedOptions.Add("EnableUVScaleOffsetNormal", "True");
                expectedOptions.Add("EnableLerpBaseColorEmission", "True");
            }
            if (options.Count != expectedOptions.Count || expectedOptions.Any(p => options.GetValueOrDefault(p.Key) != p.Value))
                throw new InvalidDataException("Origin Dialga gem shader options changed.");
        }
        var scalars = b.Tables(material, 4, 256).ToDictionary(t => b.Text(t, 0)!, t => b.Field(t, 1) is var at && at != 0 ? b.Float(at) : 0);
        var expected = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["FresnelAlphaMin"] = 1, ["FresnelAlphaMax"] = 1, ["FresnelAngleBias"] = 0, ["NormalHeight"] = 1,
            ["Metallic"] = 0, ["Reflectance"] = .3f, ["Roughness"] = .5f, ["EmissionIntensity"] = 0,
        };
        for (var i = 1; i <= 4; i++)
        {
            expected["MetallicLayer" + i] = i == 3 ? 1 : 0;
            expected["RoughnessLayer" + i] = i == 3 ? .5f : .1f;
            expected["EmissionIntensityLayer" + i] = .2f; expected["LayerMaskScale" + i] = 1;
        }
        if (scalars.Count != expected.Count || expected.Any(p => scalars.GetValueOrDefault(p.Key, float.NaN) != p.Value))
            throw new InvalidDataException("Origin Dialga gem scalar controls changed.");
        var vectors = b.Tables(material, 7, 256).ToDictionary(t => b.Text(t, 0)!, t =>
        { var at = b.Field(t, 1); return new Vector4(b.Float(at), b.Float(at + 4), b.Float(at + 8), b.Float(at + 12)); });
        if (vectors.Count != 12 || vectors.GetValueOrDefault("BaseColor") != DialgaTint
            || vectors.GetValueOrDefault("UVScaleOffset") != new Vector4(1, 1, 0, 0)
            || vectors.GetValueOrDefault("UVScaleOffsetNormal") != new Vector4(1, 1, 0, 0)
            || vectors.GetValueOrDefault("EmissionColor") != new Vector4(DialgaGlow, 1)
            || Enumerable.Range(1, 4).Any(i => vectors.GetValueOrDefault("BaseColorLayer" + i) != DialgaTint
                || vectors.GetValueOrDefault("EmissionColorLayer" + i) != DialgaTint))
            throw new InvalidDataException("Origin Dialga gem palette or coordinates changed.");
        var selectors = b.Tables(material, 9, 256).ToDictionary(t => b.Text(t, 0)!, t => b.Value(t, 1));
        if (selectors.Count != 4 || selectors.GetValueOrDefault("CastShadow", uint.MaxValue) != 1
            || selectors.GetValueOrDefault("CategoryLabel", uint.MaxValue) != 6
            || selectors.GetValueOrDefault("EnableJewel", uint.MaxValue) != 0
            || selectors.GetValueOrDefault("EnableGlobalWeather", uint.MaxValue) != 0)
            throw new InvalidDataException("Origin Dialga gem selectors changed.");
        var roles = new Dictionary<string, string> { ["BaseColorMap"] = "alb", ["LayerMaskMap"] = "lym", ["NormalMap"] = "nrm", ["AOMap"] = "ao" };
        var textures = b.Tables(material, 2, 64); var samplers = b.Tables(material, 3, 32);
        if (!textures.Select(t => b.Text(t, 0)!).Order(StringComparer.Ordinal).SequenceEqual(roles.Keys.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Origin Dialga gem texture roles changed.");
        foreach (var texture in textures)
        {
            var suffix = roles[b.Text(texture, 0)!]; var path = b.Text(texture, 1); var sampler = b.Value(texture, 2);
            if (path != DialgaStem + "_body_b_" + suffix + ".bntx"
                    && !(suffix == "alb" && path == DialgaStem + "_body_b_rare_alb.bntx")
                || sampler >= samplers.Length || b.Value(samplers[sampler], 9) != 1 || b.Value(samplers[sampler], 10) != 1)
                throw new InvalidDataException("Origin Dialga gem texture identity or sampler changed.");
        }
        var color = new Vector3(DialgaTint.X, DialgaTint.Y, DialgaTint.Z);
        return new("body_b_01", DialgaStem + "_body_b_lym.bntx", Enumerable.Repeat(color, 4).ToArray())
        {
            Transparent = true, NormalizedCoverage = true, BaseColors = Enumerable.Repeat(color, 4).ToArray(),
            Initial = Enumerable.Repeat(.2f, 4).ToArray(), GlobalEmissionColor = DialgaGlow,
            PreservesTextureAlpha = true, ColorUvOrigin = new(0, 1), MaskWrap = Vector2.One,
        };
    }

    private static void RequireDialgaGemGeometry(PreviewPrimitive primitive)
    {
        if (primitive.Name != DialgaGem || primitive.Material.Name != "body_b_01" || primitive.Material.Alpha != "BlendPreMultiAlpha"
            || primitive.Vertices.Length != 81 * 16 || primitive.Indices.Length != 174
            || primitive.Indices.Any(i => i >= 81 || !float.IsFinite(primitive.Vertices[i * 16 + 6] + primitive.Vertices[i * 16 + 7])
                || primitive.Vertices[i * 16 + 6] is <= 0 or >= 1 || primitive.Vertices[i * 16 + 7] is <= 1 or >= 2))
            throw new InvalidDataException("Origin Dialga gem geometry or static UV tile changed.");
    }

}
