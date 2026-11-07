// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;
using KM.Formats.SV;

namespace KM.Formats.ZA;

/// <summary>Authored six-layer surface palettes with independent emission and layer Fresnel.</summary>
public static class ZaSwShLayerSurface
{
    public const string Program = "ZaLayerSurface6";
    public static readonly string[] Programs = [Program, "ZaLayerSurface6Mask1", "ZaLayerSurface6Fresnel", "ZaLayerSurface6FresnelMask1",
        "ZaLayerSurface5", "ZaLayerSurface5Displaced", "ZaLayerSurface5Blend", "ZaLayerSurface5Noise", "ZaLayerSurface5Iep",
        "ZaLayerSurface5Mask0", "ZaLayerSurface5IepMask0"];
    public static bool Recognizes(string? name) => name is not null && Programs.Contains(name);
    public static bool SixLayers(string name) => name.StartsWith("ZaLayerSurface6", StringComparison.Ordinal);
    public static readonly string[] Scalars = ["EmissionIntensity", .. Enumerable.Range(1, 5).Select(i => "EmissionIntensityLayer" + i),
        .. Enumerable.Range(1, 5).Select(i => "LayerMaskScale" + i), .. Enumerable.Range(1, 5).SelectMany(i => new[] {
            "FresnelAlphaMin" + i, "FresnelAlphaMax" + i, "FresnelAngleBias" + i, "FresnelPower" + i }),
        "MetallicLayer5", "RoughnessLayer5", "FresnelAlphaMin", "FresnelAlphaMax", "SSSMaskOffset", "FresnelAngleBias", "FresnelPower", "AOIntensity", "AOIntensityFresnel",
        "DisplacementHeight", "InsideEmissionParallaxIntensity", "InsideEmissionParallaxHeight", "InsideEmissionParallaxF0", "InsideEmissionParallaxF90",
        "InsideEmissionParallaxOffset", "InsideEmissionParallaxIntensityMapLatticeU", "InsideEmissionParallaxIntensityMapLatticeV", "InsideEmissionParallaxIntensityMapRangeAvoidEdge",
        "EmissionRimF0", "EmissionRimF90", "EmissionRimRate", "EmissionRimThreshold", "EmissionRimCurve",
        .. Enumerable.Range(1, 4).Select(i => "EnableIEPTexture" + i),
        "NoiseIntensity", "NoiseScrollSpeed", "NoiseFadeStart", "NoiseFadeEnd", "NoiseScale", "NoiseLightMin", "EffectTime"];
    public static readonly string[] Vectors = ["BaseColor", "EmissionColor", "UVScaleOffset", "UVScaleOffset5", "UVScaleOffsetLayerMask",
        .. Enumerable.Range(1, 5).Select(i => "BaseColorLayer" + i), .. Enumerable.Range(1, 5).Select(i => "EmissionColorLayer" + i),
        .. Enumerable.Range(1, 5).Select(i => "FresnelColor" + i),
        .. Enumerable.Range(6, 4).Select(i => "BaseColorLayer" + i), "UVScaleOffset3", "UVScaleOffsetInsideEmissionParallaxIntensity", "UVScaleOffsetInsideEmissionParallaxHeight", "EmissionRimColor", "NoiseBaseColor"];
    public static readonly string[] Names = [.. Scalars.Select(Name), .. Vectors.SelectMany(v => Enumerable.Range(0, 4).Select(c => Name(v) + c))];
    public static string Name(string parameter) => "SourceLayer" + parameter;
    public static int ParameterIndex(string parameter, int component = 0)
    {
        var s = Array.IndexOf(Scalars, parameter); if (s >= 0) return s;
        var v = Array.IndexOf(Vectors, parameter); return v < 0 ? -1 : Scalars.Length + v * 4 + component;
    }
    public static int Word(string parameter, int component = 0) => 52 + ParameterIndex(parameter, component);
    public sealed record Contract(string Material, float[] Values, ZaSwShFresnelMaterial.TextureBinding[] Textures)
    {
        public string Shader { get; init; } = Program;
        public bool DisableNativeSecondaryLayer { get; init; }
        public bool SecondaryUv => SixLayers(Shader) || Shader.Contains("Iep", StringComparison.Ordinal);
    }
    public static IReadOnlyDictionary<string, Contract> Contracts(byte[] bytes)
    {
        var identity = Convert.ToHexString(SHA256.HashData(bytes));
        var allowed = identity switch
        {
            "4C7398D3FADE67A8B861515ABD5FF5ADA4B553C8375F5EA41AB92656ACC0E8EA" or "C1FF5CA74CE645DB2310D094946EC9E9BB13EDAB04D66BDB035BD5D3D0408B11" => new[] { "body_c" },
            "858AAF123C05A90715DD7AEF3AC08B97AFF9ADAF9C003A04ED2CE7D121848ED6" or "BFCCDEEA623B1505C0AEE39D3829DB15E31069FB1304A7529CC558E2235AE0CC" => ["fire_a", "fire_b"],
            "540500834158C3F1C519FAF798360BDED5BA2A6BDD70B4364FFAD954D96830C5" or "77A14CA5B9B8BD6739CDD205E7C8A8B59F9D1B9EF40D0FFBDEAC41E9A5594360" => ["body_d_01", "body_a_00", "body_c", "body_d_00", "body_e"],
            "A6DE96DD6073801C196AD3730062760283E9570C13BE715037384972259FA14D" or "F207BBA5B4526D756D7DE9DBA1B6F043FD88889F9A335F15429CA26EA450A5CF" => ["body_b", "magma"],
            "D26E3B5B99664670E949C9E50977E55CB00FF18D3C5463FFDFFF68A09DC0ECA7" or "E8BF7BBD42973E236FA0711ED0239EC71E7F39FB0117732C64E5A84D2A675C25" => ["body_b_00", "body_a_02", "body_b_01"],
            "2552BC93CEA7F7465EEDB6BE4DF5BAEF1D0108880080808CB3BF754283A06B85" or "9357D108DB9E3208D91ED25D162AB1422A5DBF6A63034771E2C8A13258410A1C" => ["body_a", "body_b"],
            _ => Array.Empty<string>(),
        };
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal); if (allowed.Length == 0) return result;
        var source = new ModelBuffer(bytes);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            var name = source.Text(material, 0)!; if (!allowed.Contains(name)) continue;
            var program = source.Tables(material, 1, 16).Single();
            var options = source.Tables(program, 1, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1));
            var six = options["NumMaterialLayer"] == "6";
            if (source.Text(material, 15) != "Opaque" || options["NumMaterialLayer"] is not ("5" or "6")) throw new InvalidDataException("Source six-layer pass changed.");
            var scalars = source.Tables(material, 4, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(t => source.Text(t, 0)!, t => Enumerable.Range(0, 4).Select(c => source.Float(source.Field(t, 1) + c * 4)).ToArray());
            var selectors = source.Tables(material, 9, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Value(t, 1));
            var mask1 = selectors.GetValueOrDefault("UVIndexLayerMask", uint.MaxValue) == 1;
            if (six && selectors["UVIndexLayer5"] != 1 || selectors.GetValueOrDefault("UVIndexLayerMask", uint.MaxValue) is not (0 or 1 or uint.MaxValue)) throw new InvalidDataException("Source six-layer coordinates changed.");
            float Scalar(string n) => scalars.GetValueOrDefault(n, n.StartsWith("LayerMaskScale", StringComparison.Ordinal) || n.StartsWith("FresnelPower", StringComparison.Ordinal) ? 1 : 0);
            float[] Vector(string n) => vectors.GetValueOrDefault(n, n.StartsWith("UVScaleOffset", StringComparison.Ordinal) ? [1, 1, 0, 0] : [1, 1, 1, 1]);
            var values = Scalars.Select(Scalar).Concat(Vectors.SelectMany(Vector)).ToArray();
            // Heatran's InsideEmissionParallax variant samples BaseColorMap
            // directly; BaseColor is its unused alternative, not a multiplier.
            if (identity is "A6DE96DD6073801C196AD3730062760283E9570C13BE715037384972259FA14D" or "F207BBA5B4526D756D7DE9DBA1B6F043FD88889F9A335F15429CA26EA450A5CF"
                && options.GetValueOrDefault("EnableBaseColorMap") == "True")
                for (var c = 0; c < 4; c++) values[ParameterIndex("BaseColor", c)] = 1;
            if (values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Source six-layer values are invalid.");
            var samplers = source.Tables(material, 3, 32); var textures = source.Tables(material, 2, 64);
            var blend = source.Text(program, 0) == "FresnelBlend";
            var displaced = scalars.GetValueOrDefault("DisplacementHeight") != 0;
            var iep = source.Text(program, 0) == "InsideEmissionParallax" && Enumerable.Range(1, 4).Any(i => scalars.GetValueOrDefault("EnableIEPTexture" + i) != 0);
            var noise = options.GetValueOrDefault("EnableMGUpwordNoise") == "True";
            var roles = new List<string> { "BaseColorMap", "LayerMaskMap" };
            if (six) roles.Add("OpacityMap1");
            if (displaced) roles.Add("DisplacementMap");
            if (blend) roles.AddRange(["HighlightMaskMap", "MetallicMap", "RoughnessMap", "AOMap"]);
            if (iep) roles.AddRange(["InsideEmissionParallaxIntensityMap", "ParallaxMap"]);
            if (noise) roles.Add("NoiseSourceMap");
            var bindings = roles.Select((role, i) =>
            {
                var texture = textures.Single(t => source.Text(t, 0) == role); var index = source.Value(texture, 2);
                if (index >= samplers.Length) throw new InvalidDataException("Source six-layer sampler is invalid.");
                var sampler = samplers[index];
                return new ZaSwShFresnelMaterial.TextureBinding(role, "SourceLayer" + role, source.Text(texture, 1)!, 8 + i,
                    TrinityTextureAddressing.SwSh(source.Value(sampler, 9)), TrinityTextureAddressing.SwSh(source.Value(sampler, 10)), 4, Vector4.One, false);
            }).ToArray();
            var fresnel = source.Text(program, 0) == "Standard";
            result.Add(name, new(name, values, bindings) {
                DisableNativeSecondaryLayer = identity is "A6DE96DD6073801C196AD3730062760283E9570C13BE715037384972259FA14D" or "F207BBA5B4526D756D7DE9DBA1B6F043FD88889F9A335F15429CA26EA450A5CF",
                Shader = six ? Program + (fresnel ? "Fresnel" : "") + (mask1 ? "Mask1" : "") : "ZaLayerSurface5" + (blend ? "Blend" : displaced ? "Displaced" : iep ? "Iep" : noise ? "Noise" : "")
                    + (identity is "A6DE96DD6073801C196AD3730062760283E9570C13BE715037384972259FA14D" or "F207BBA5B4526D756D7DE9DBA1B6F043FD88889F9A335F15429CA26EA450A5CF" ? "Mask0" : "") });
        }
        if (result.Count != allowed.Length) throw new InvalidDataException("Source six-layer material set is incomplete.");
        return result;
    }
    public static PreviewScene Geometry(PreviewScene scene, IReadOnlyDictionary<string, Contract> contracts)
    {
        if (contracts.Count == 0) return scene;
        // The original Zygarde head shares body_e with the energy mesh but
        // declares no TEXCOORD1. Preserve its zero generic attribute value.
        if (contracts.ContainsKey("body_e") && contracts.ContainsKey("body_d_01"))
            scene = scene with { Primitives = scene.Primitives.Select(p => p.Name == "pm0770_51_00_head_mesh_shape"
                && p.Material.Name == "body_e" && p.SecondaryUv is null
                    ? p with { SecondaryUv = new float[p.Vertices.Length / 16 * 2] } : p).ToArray() };
        foreach (var p in scene.Primitives.Where(p => contracts.TryGetValue(p.Material.Name, out var contract) && contract.SecondaryUv))
            if (p.SecondaryUv is null) throw new InvalidDataException("Source six-layer UV1 is missing: " + p.Name);
        // This warning is emitted solely for the qualified UVIndexLayerMask=1.
        return scene with { Rig = scene.Rig with { Warnings = scene.Rig.Warnings.Where(w => w != "materialUvUnsupported").ToArray() } };
    }

    public static byte[] Template(byte[] bytes, Contract contract, IReadOnlyDictionary<string, string> images)
    {
        bytes = SvSwShLayerEmission.Template(bytes, contract.Shader, "Body00");
        var source = new ModelBuffer(bytes); var material = Material(source, contract.Shader);
        var writer = new ModelTableBuilder(bytes); var rows = source.Tables(material, 13, 256);
        if (rows.Length != 52 || source.Tables(material, 14, 256).Length != 19)
            throw new InvalidDataException("Source layer surface requires the native material prefix.");
        var slots = writer.Vector(material, 13, 52 + Names.Length);
        for (var i = 0; i < slots.Length; i++)
        {
            var at = i < 52 ? source.Field(rows[i], 1) : 0;
            var disableLayer = i < 52 && contract.DisableNativeSecondaryLayer && NativeOverlay(source.Text(rows[i], 0));
            var absent = i < 52 && at == 0;
            var row = writer.Table(2, absent ? null : new Dictionary<int, byte[]> { [1] = disableLayer ? ModelTableBuilder.Floats(0) : i < 52 ? source.Slice(at, 4).ToArray() : ModelTableBuilder.Floats(contract.Values[i - 52]) },
                absent: absent ? new HashSet<int> { 1 } : null);
            writer.Pointer(slots[i], row); writer.Text(row, 0, i < 52 ? source.Text(rows[i], 0)! : Names[i - 52]);
        }
        bytes = writer.Finish(source.Root);
        return ZaSwShFresnelMaterial.BindSourceTextures(bytes, Material(new(bytes), contract.Shader), contract.Textures, images);
    }

    private static int Material(ModelBuffer source, string program)
    {
        var (start, count) = source.Vector(source.Root, 3, 4, 256);
        var index = Enumerable.Range(0, count).Single(i => source.StringAt(start + i * 4) == program);
        return source.Tables(source.Root, 6, 128).Single(m => source.Value(m, 8) == index);
    }

    public static byte[] Bind(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts,
        IReadOnlyDictionary<string, string> originals, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> images)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var (start, count) = source.Vector(source.Root, 3, 4, 256);
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            var index = source.Value(material, 8);
            if (index >= count) throw new InvalidDataException("Source layer surface native program index is invalid.");
            if (!Recognizes(source.StringAt(start + (int)index * 4))) continue;
            var original = originals[source.Text(material, 0)!]; var contract = contracts[original];
            var rows = source.Tables(material, 13, 256);
            if (rows.Length != 52 + Names.Length || !rows.Skip(52).Select(t => source.Text(t, 0)).SequenceEqual(Names))
                throw new InvalidDataException("Source layer surface scalar ABI is incomplete.");
            for (var i = 0; i < Names.Length; i++) writer.Single(writer.Field(rows[52 + i], 1), contract.Values[i]);
            // Heatran supplies its own surface palette and emission rim. The
            // donor's emission mask also selects its sphere/reflection blend:
            // a white mask bypasses SphereMapColorVal, even when it is zero.
            // Source emission is supplied by the custom shader instead.
            if (contract.DisableNativeSecondaryLayer)
                foreach (var row in rows.Take(52).Where(t => NativeOverlay(source.Text(t, 0))))
                    writer.Single(writer.Field(row, 1), 0);
        }
        bytes = writer.Finish(source.Root); source = new(bytes);
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            var index = source.Value(material, 8);
            if (!Recognizes(source.StringAt(start + (int)index * 4))) continue;
            var original = originals[source.Text(material, 0)!];
            var current = new ModelBuffer(bytes);
            var currentMaterial = current.Tables(current.Root, 6, 128).Single(m => current.Text(m, 0) == source.Text(material, 0));
            bytes = ZaSwShFresnelMaterial.BindSourceTextures(bytes, currentMaterial, contracts[original].Textures, images[original]);
        }
        return bytes;
    }

    private static bool NativeOverlay(string? name) => name is "Layer1OverLerpValue" or "EmissionMaskVal" or "SphereMapColorVal" or "RimColorVal" or "SpecularScale";
}
