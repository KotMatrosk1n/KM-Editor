// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.Models;

/// <summary>Declared surface properties evaluated under the preview's studio lighting.</summary>
public sealed record PreviewSurface(float[][] Values, int[] Textures, float[][] Wraps)
{
    public const int ValueCount = 41;
    public const int TextureCount = 12;
    private static readonly string[][] Scalars =
    [
        ["SaturationPower", "BaseColorDarkness", "NormalHeight", "Metallic"],
        ["Roughness", "OcclusionStrength", "EmissionIntensity", "DiscardValue"],
        ["SpecularIntensity", "SpecularOffset", "SpecularContrast", "SpecularPower"],
        ["ShadowStrength", "ShadingBias", "HalfLambertBias", "ShadowingGIGain"],
        ["RimLightIntensity", "RimLightOffset", "RimLightContrast", "BackRimLightIntensity"],
        [], [], [], [], [],
        ["UVRotation", "UVRotation2", "NormalHeight1", "Reflectance"],
        ["MetallicLayer1", "MetallicLayer2", "MetallicLayer3", "MetallicLayer4"],
        ["RoughnessLayer1", "RoughnessLayer2", "RoughnessLayer3", "RoughnessLayer4"],
        ["EmissionIntensityLayer1", "EmissionIntensityLayer2", "EmissionIntensityLayer3", "EmissionIntensityLayer4"],
        [], [], [], [], [], [], [], [],
        ["SpecularLayer1Intensity", "SpecularLayer2Intensity", "SpecularLayer3Intensity", "SpecularLayer4Intensity"],
        ["SpecularLayer1Offset", "SpecularLayer2Offset", "SpecularLayer3Offset", "SpecularLayer4Offset"],
        ["SpecularLayer1Contrast", "SpecularLayer2Contrast", "SpecularLayer3Contrast", "SpecularLayer4Contrast"],
        ["ShadowingShift", "ShadowingContrast", "HueShiftBias", "ShadowingColorMaskMapValue"],
        ["MidAreaShift", "MidAreaContrast", "MidAreaHueOffset", "HueShiftAreaValue"],
        ["DarkAreaShift", "DarkAreaContrast", "DarkAreaHueOffset", "ShadowingBias"],
        [],
        ["MetallicClearCoat", "RoughnessClearCoat", "MetallicHighlight", "RoughnessHighlight"],
        [], ["SSSMaskScale", "SSSMaskOffset", "LightMul", "ApplyLightingVal"],
        [], [], ["ConstantAlpha", "Layer1OverLerpValue", "RimPower", "RimStrength"],
        [], [], [], [], [], ["DiscardValue_R", "DiscardValue_G", "DiscardValue_B", "DiscardValue_A"]
    ];
    private static readonly Dictionary<string, int> Vectors = new(StringComparer.Ordinal)
    {
        ["EmissionColor"] = 5, ["ShadowingColor"] = 6, ["SpecularColor"] = 7, ["RimColor"] = 8,
        ["UVScaleOffsetNormal"] = 9, ["BaseColorClearCoat"] = 28, ["SubsurfaceColor"] = 30,
        ["RimColorShadow"] = 35, ["UVCenter0"] = 36, ["UVCenter1"] = 37,
        ["UVScaleOffset2"] = 38, ["BaseColorLayer6"] = 39
    };
    public static int TextureSlot(string role) => role switch
    {
        "NormalMap" or "NormalMapTex" => 0,
        "RoughnessMap" => 1,
        "AOMap" or "OcclusionMap" => 2,
        "EmissionMap" => 3,
        "SpecularMaskMap" or "SpecularMap" => 4,
        "RimLightMaskMap" => 5,
        "ShadowingColorMap" => 6,
        "NormalMap1" => 7,
        "ShadowingColorMaskMap" => 8,
        "SSSMaskMap" => 9,
        "EyelidShadowMaskMap" => 10,
        "DiscardMaskMap" => 11,
        _ => -1
    };
    public static bool Supports(string name) => (name != "ShadowingGIGain" && Scalars.Any(row => row.Contains(name))) || Vectors.ContainsKey(name)
        || Enumerable.Range(1, 4).Any(i => name == $"EmissionColorLayer{i}" || name == $"ShadowingColorLayer{i}")
        || name is "SpecularScale" or "ConstantColor0Val" or "ConstantColor1" or "ConstantColor1Val" or "ColorLerpValue"
            or "L1ConstantColor0" or "L1ConstantColor0Val" or "L1ConstantColor1" or "L1ConstantColor1Val"
            or "L1AddColor0" or "L1AddColor0Val" or "L1AddColor1" or "L1AddColor1Val"
            or "NormalMapUVScaleU" or "NormalMapUVScaleV"
            or "ConstantColorSd0" or "ConstantColorSd1" or "ConstantColorSd0Val" or "ConstantColorSd1Val"
            or "DeepShadowColor" or "RimColorVal" or "ConstantColor" or "ConstantColorVal"
            or "OnGameColor" or "OnGameColorVal" or "OnGameAlpha";

    internal static PreviewSurface Read(ModelBuffer data, int material, bool packed)
    {
        var scalars = data.Tables(material, packed ? 13 : 4, 256).ToDictionary(
            p => data.Text(p, 0) ?? "", p => data.Field(p, 1) is var at && at != 0 ? data.Float(at) : 0, StringComparer.Ordinal);
        var colors = data.Tables(material, packed ? 14 : 7, 256).ToDictionary(p => data.Text(p, 0) ?? "", p =>
        {
            var at = data.Field(p, 1);
            return at == 0 ? new float[4] : new[] { data.Float(at), data.Float(at + 4), data.Float(at + 8), packed ? 1 : data.Float(at + 12) };
        }, StringComparer.Ordinal);
        float S(string name, float fallback) => scalars.GetValueOrDefault(name, fallback);
        float[] C(string name, float[] fallback) => colors.GetValueOrDefault(name, fallback).ToArray();
        var values = Enumerable.Range(0, ValueCount).Select(_ => new float[4]).ToArray();
        values[0] = [1, 0, 1, 0]; values[1] = [1, 1, 0, .01f]; values[2] = [.04f, 0, 0, 32];
        values[3] = [1, 1, 0, .48f]; values[4] = [0, 0, 1, 0];
        values[5] = [1, 1, 1, 1]; values[6] = [1, 1, 1, 1]; values[7] = [1, 1, 1, 1]; values[8] = [1, 1, 1, 1];
        values[9] = [1, 1, 0, 0]; values[10] = [0, 0, 1, .04f]; values[12] = [1, 1, 1, 1];
        for (var i = 14; i < 22; i++) values[i] = [1, 1, 1, 1];
        values[22] = [.04f, .04f, .04f, .04f]; values[25][2] = .5f; values[27][3] = 1;
        values[29] = [0, 1, 0, 1]; values[31] = [1, 0, 1, 1]; values[32] = [1, 1, 1, 1];
        values[34] = [1, 1, 4, 0]; values[36] = [.5f, .5f, 0, 0]; values[37] = [.5f, .5f, 0, 0]; values[38] = [1, 1, 0, 0];
        for (var i = 0; i < Scalars.Length; i++) for (var j = 0; j < Scalars[i].Length; j++) values[i][j] = S(Scalars[i][j], values[i][j]);
        foreach (var (name, index) in Vectors) values[index] = C(name, values[index]);
        for (var i = 0; i < 4; i++) { values[14 + i] = C($"EmissionColorLayer{i + 1}", values[14 + i]); values[18 + i] = C($"ShadowingColorLayer{i + 1}", values[18 + i]); }
        if (packed)
        {
            values[2][0] = S("SpecularScale", .04f);
            values[9][0] = S("NormalMapUVScaleU", 1); values[9][1] = S("NormalMapUVScaleV", 1);
            var lerp = Math.Clamp(S("ColorLerpValue", 0), 0, 1);
            float[] Mix(string a, string b, string av, string bv) => C(a, [1, 1, 1, 1]).Select((v, i) => i == 3 ? 1 : v * S(av, 1) * (1 - lerp) + C(b, [1, 1, 1, 1])[i] * S(bv, 1) * lerp).ToArray();
            values[32] = Mix("L1ConstantColor0", "L1ConstantColor1", "L1ConstantColor0Val", "L1ConstantColor1Val");
            values[33] = C("L1AddColor0", [0, 0, 0, 0]).Select((v, i) => v * S("L1AddColor0Val", 0) * (1 - lerp) + C("L1AddColor1", [0, 0, 0, 0])[i] * S("L1AddColor1Val", 0) * lerp).ToArray();
            values[39] = Mix("ConstantColor0", "ConstantColor1", "ConstantColor0Val", "ConstantColor1Val");
            values[6] = Mix("ConstantColorSd0", "ConstantColorSd1", "ConstantColorSd0Val", "ConstantColorSd1Val");
            var shadow = C("DeepShadowColor", [1, 1, 1, 1]);
            var constant = C("ConstantColor", [1, 1, 1, 1]); var gameColor = C("OnGameColor", [1, 1, 1, 1]);
            for (var i = 0; i < 3; i++)
            {
                values[6][i] *= shadow[i]; values[8][i] *= S("RimColorVal", 1);
                values[39][i] *= constant[i] * S("ConstantColorVal", 1) * gameColor[i] * S("OnGameColorVal", 1);
            }
            values[34][0] *= S("OnGameAlpha", 1);
        }
        else values[39] = C("BaseColorLayer6", [1, 1, 1, 1]);
        values[39][3] = packed ? 1 : 0;
        return new(values, Enumerable.Repeat(-1, TextureCount).ToArray(), Enumerable.Range(0, TextureCount).Select(_ => new float[4]).ToArray());
    }
}
