// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Formats.SV;

/// <summary>Qualified S/V interior emission with independent height and intensity coordinates.</summary>
public static class SvSwShParadoxSurface
{
    private static string[] Allowed(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)) switch
        {
            "D29A27549086C369CC534BBE620A7E59DFC6796F23BEE68CFF646E3CFBA052DF" or "7A231DBDBBFD28449D57E0EA998B7B957DC01D450360157AF1F1C3FA8FC8175E" => ["body_02"],
            "E2A2EB8B41D8A380B77126BAD280E3DE0F18EE39DB8CB8FD4DD8F642A052E9ED" or "26B38129C86B7844A836D8339A3D7EAB8E8B13F2FA7F9073EFFB4197D2F41115" => ["body_a_00", "body_b", "body_a_01"],
            "837188B00DB789B09CDA7F924F31CBE65D2A36573F7F146DEBE591C89034528A" or "25B468494D71CFDE51AE401E58E1257B5A95206CFFA81ECE9C0C1A60C93D399B" => ["body_c"],
            "A356E22899CF3D239010843616786B7B8699216AFC9ECD68B196D1852197AE07" or "81DA841B5557F8FB48CD9A9117A144E9E254BE3A523661164149CBE1AC3596B1" => ["body_02", "body_04"],
            "74E8E0E4A5A50CD835CA070669EE8C21D241A0BBA19617DA53C64268F069AB27" or "1F62B2E5B83FAF869F456A76574217DFDA62FE717CD7727938272B088FE43C79" => ["body_c"],
            "19685989CA24011D77BEBFEC16C0C83F389E9528F2912E1CCD1076EF5FAF5E0F" or "FF4C31C9A9409E21894687FBE02ECB0538359BB091583676F72BC313B2C3955A" => ["body_a_03", "body_a_01", "eye_b", "l_eye", "r_eye"],
            "92BB7C6B8E2CA86BCA8DA937A456168636A4C1B2575F616D229FA72B40FA4448" or "56CF38BFB73E4904BE7FE631600BB407B2860EFD9BC407AFA4D1CD6CB7F33C69" => ["body_b_01"],
            // Thorns' irises use the dedicated animated eye interface. Keeping
            // them here would select the body program before eye binding and
            // overwrite its uniforms while leaving the iris sampler unbound.
            "C4F1227465DE9F66C706C7782764B0A98390C83ADBCECF1AF896362EEC7C3E74" or "9E054855AB73A915C5777488FAE4C868D01C4F1C24A8E3B709A5848C327DD3D2" => ["body_a_03"],
            "2FB1CC932CF2CC522DEB0ED2FEB9254753432AFCD74E9177B20921D45B07E5ED" or "7519B173ECA04DDCDD0E036ECB7A3B63AAEEA3E7628F552C6E7D6E856CFCD392" => ["body_b_01", "body_b_02", "l_eye", "r_eye"],
            "32FA7A946F51C7F2F3340D0711A4EAC3410BE914425918982F0DBEF9B36E96EE" or "C48ECB662EDF5669BFA2F49E6A37BDEF7982283AA35F44C4406F5F8B9101EFA3" => ["body_b", "body_d_00", "body_a_00", "body_d_01", "body_d_02", "body_c", "eye"],
            "0FC9C77D7FB4F194052CC0789D6117527859A56F8A3988BE4B7D8C126CE13B6C" or "F73CBDDE823DF78130586CC8F88ED13F2D91A69640DB5DAA4531B14F0588006D" => ["body_b_01", "l_eye", "r_eye"],
            "CED970CDED63C0561B618245D00E7828D5572A85994DDB971298CE6069F79CBA" or "235A17512CF4D048A78820CBADD820A30CE1DC598B298A518D84A18EED38160C" => ["body_a_01", "l_eye", "r_eye"],
            "D95BED35F0DB2F0B751D638DC299301AC0E6ABEC39FEEAB1D208B7211CF81565" or "420725464E305AD43583ADDD984D38D7808F7EAFE1A21A81F449CC2DADD85929" => ["body_b_01", "body_b_02", "l_eye", "r_eye"],
            _ => Array.Empty<string>(),
        };

    public static void RequireMaterial(byte[] bytes, string material)
    {
        if (!Allowed(bytes).Contains(material, StringComparer.Ordinal))
            throw new InvalidDataException("Source surface requires its qualified S/V palette.");
    }

    // These inputs are consumed by paired vertex/fragment programs. They must
    // never be flattened into object-normal images across overlapping UVs.
    public static bool GeometricNormals(byte[] bytes, string material) => material == "eye"
        && Convert.ToHexString(SHA256.HashData(bytes)) is "32FA7A946F51C7F2F3340D0711A4EAC3410BE914425918982F0DBEF9B36E96EE" or "C48ECB662EDF5669BFA2F49E6A37BDEF7982283AA35F44C4406F5F8B9101EFA3";
    public static bool TangentNormals(byte[] bytes, string material) => material switch
    {
        "eye_b" => Convert.ToHexString(SHA256.HashData(bytes)) is "19685989CA24011D77BEBFEC16C0C83F389E9528F2912E1CCD1076EF5FAF5E0F" or "FF4C31C9A9409E21894687FBE02ECB0538359BB091583676F72BC313B2C3955A",
        "body_d_02" => Convert.ToHexString(SHA256.HashData(bytes)) is "32FA7A946F51C7F2F3340D0711A4EAC3410BE914425918982F0DBEF9B36E96EE" or "C48ECB662EDF5669BFA2F49E6A37BDEF7982283AA35F44C4406F5F8B9101EFA3",
        _ => false,
    };

    public static IReadOnlyDictionary<string, ZaSwShLayerSurface.Contract> Contracts(byte[] bytes, PreviewScene scene)
    {
        var allowed = Allowed(bytes);
        var result = new Dictionary<string, ZaSwShLayerSurface.Contract>(StringComparer.Ordinal);
        if (allowed.Length == 0) return result;
        var source = new ModelBuffer(bytes);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            var name = source.Text(material, 0)!; if (!allowed.Contains(name)) continue;
            if (!scene.Primitives.Any(p => p.Material.Name == name)) throw new InvalidDataException("Paradox surface geometry is missing.");
            var shader = source.Tables(material, 1, 16).Single();
            var options = source.Tables(shader, 1, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1));
            var transparentIep = source.Text(shader, 0) == "InsideEmissionParallaxTransparent" && name == "body_d_02" && TangentNormals(bytes, name);
            var iep = source.Text(shader, 0) == "InsideEmissionParallax" || transparentIep;
            var blend = source.Text(shader, 0) == "FresnelBlend";
            var premultipliedEye = source.Text(shader, 0) == "Eye" && source.Text(material, 15) == "BlendPreMultiAlpha";
            if (transparentIep ? source.Text(material, 15) != "Blend"
                : !premultipliedEye && source.Text(material, 15) != "Opaque" || source.Text(shader, 0) is not ("Standard" or "SSS" or "InsideEmissionParallax" or "FresnelBlend" or "Eye"))
                throw new InvalidDataException("Paradox surface pass changed.");
            var one = options.GetValueOrDefault("NumMaterialLayer") == "1";
            if (one && !iep || !one && options.GetValueOrDefault("NumMaterialLayer") != "5")
                throw new InvalidDataException("Paradox surface layer count changed.");
            var scalars = source.Tables(material, 4, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
            var geometric = GeometricNormals(bytes, name); var tangent = TangentNormals(bytes, name);
            if (scalars.GetValueOrDefault("NormalHeight", 1) != (geometric ? 0 : 1))
                throw new InvalidDataException("Paradox normal strength requires another adapter.");
            var vectors = source.Tables(material, 7, 256).ToDictionary(t => source.Text(t, 0)!, t => Enumerable.Range(0, 4).Select(c => source.Float(source.Field(t, 1) + c * 4)).ToArray());
            var selectors = source.Tables(material, 9, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Value(t, 1));
            if (iep && (selectors.GetValueOrDefault("UVIndexInsideEmissionParallaxHeight", uint.MaxValue) is not (0 or uint.MaxValue)
                || selectors.GetValueOrDefault("UVIndexInsideEmissionParallaxIntensity", uint.MaxValue) != 1))
                throw new InvalidDataException("Paradox interior emission coordinates changed.");
            float Scalar(string n) => scalars.GetValueOrDefault(n, n.StartsWith("LayerMaskScale", StringComparison.Ordinal) || n.StartsWith("FresnelPower", StringComparison.Ordinal) ? 1 : 0);
            float[] Vector(string n) => vectors.GetValueOrDefault(n, n.StartsWith("UVScaleOffset", StringComparison.Ordinal) ? [1, 1, 0, 0] : [1, 1, 1, 1]);
            var values = ZaSwShLayerSurface.Scalars.Select(Scalar).Concat(ZaSwShLayerSurface.Vectors.SelectMany(Vector)).ToArray();
            // Enabled map variants use the map instead of the unused constant.
            if (options.GetValueOrDefault("EnableBaseColorMap") == "True")
                for (var c = 0; c < (transparentIep ? 3 : 4); c++) values[ZaSwShLayerSurface.ParameterIndex("BaseColor", c)] = 1;
            if (values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Paradox surface values are invalid.");
            var textures = source.Tables(material, 2, 64); var samplers = source.Tables(material, 3, 32);
            var roles = new List<string> { "BaseColorMap", "LayerMaskMap" };
            if (iep) roles.AddRange(["InsideEmissionParallaxIntensityMap", "ParallaxMap"]);
            if (blend) roles.AddRange(["HighlightMaskMap", "MetallicMap", "RoughnessMap", "AOMap"]);
            if (one) roles.Add("EmissionColorMap");
            if (tangent) roles.Add("NormalMap");
            var bindings = roles.Select((role, i) =>
            {
                var texture = textures.Single(t => source.Text(t, 0) == (one && role == "LayerMaskMap" ? "BaseColorMap" : role));
                var index = source.Value(texture, 2);
                if (index >= samplers.Length) throw new InvalidDataException("Paradox surface sampler is missing.");
                return new ZaSwShFresnelMaterial.TextureBinding(role, "SourceLayer" + role, source.Text(texture, 1)!, 8 + i,
                    TrinityTextureAddressing.SwSh(source.Value(samplers[index], 9)), TrinityTextureAddressing.SwSh(source.Value(samplers[index], 10)), 4, Vector4.One, false);
            }).ToArray();
            // These exact Miraidon palettes share an IEP surface with sections
            // that omit TEXCOORD1. The source shader receives the zero attribute.
            var zeroSecondary = iep && scene.Primitives.Any(p => p.Material.Name == name
                && p.Name.StartsWith("pm1103_11_00_", StringComparison.Ordinal));
            result.Add(name, new(name, values, bindings) { DisableNativeSecondaryLayer = true, MissingSecondaryUvIsZero = zeroSecondary,
                Shader = transparentIep ? "SvLayerSurface5IepTangentTransparentMask0" : geometric ? "SvLayerSurface5GeometricMask0" : tangent ? "SvLayerSurface5TangentMask0"
                    : one ? "SvLayerSurface1Iep" : iep ? "SvLayerSurface5IepMask0" : blend ? "ZaLayerSurface5Blend" : premultipliedEye ? "SvLayerSurface5EyeMask0" : "SvLayerSurface5Mask0" });
        }
        if (result.Count != allowed.Length) throw new InvalidDataException("Paradox surface set is incomplete.");
        return result;
    }
}
