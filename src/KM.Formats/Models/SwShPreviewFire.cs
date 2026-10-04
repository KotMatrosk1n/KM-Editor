// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace KM.Formats.Models;

/// <summary>Authored fire colors, masks and scrolling layers for the packed family.</summary>
internal static class SwShPreviewFire
{
    // Effect surfaces use values 0..8 for these colors and scalar groups.
    // Other surfaces retain the shared lighting layout.
    private static readonly string[] Colors = ["BaseColor0", "BaseColor1", "LayerColor0", "LayerColor1", "OnGameColor", "ConstantColor0"];
    private static readonly string[][] Scalars =
    [
        ["ColorLerpValue", "ConstantColor0Val", "OnGameColorVal", "OnGameEmissionVal"],
        ["OnGameAlpha", "DiscardValuie", "BaseRimVal", "LayerRimVal"],
        ["RimPower", "RimStrength"]
    ];
    public static int Kind(string? shader) => shader switch { "PokeFireCoreShader1" => 1, "PokeFireMaskShader1" => 2, _ => 0 };
    public static int ColorSlot(string name) => Array.IndexOf(Colors, name);
    public static (int Slot, int Channel) ScalarSlot(string name)
    {
        for (var row = 0; row < Scalars.Length; row++)
            if (Array.IndexOf(Scalars[row], name) is var channel && channel >= 0) return (6 + row, channel);
        return (-1, -1);
    }
    public static (string Parameter, int Channel) UvChannel(string name)
    {
        foreach (var (prefix, parameter) in new[] { ("Blend0", "MaskUV"), ("Blend1", "UnderlayUV"), ("Mask0", "UVScaleOffset"), ("Mask1", "MaskUV") })
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var channel = name[prefix.Length..] switch { "UVScaleU" => 0, "UVScaleV" => 1, "UVTranslateU" => 2, "UVTranslateV" => 3, _ => -1 };
            if (channel >= 0) return (parameter, channel);
        }
        return ("", -1);
    }
    public static bool Supports(int kind, string name) => name == "MatLinkID" || (kind == 1
        ? name != "DiscardValuie" && (ColorSlot(name) >= 0 || ScalarSlot(name).Slot >= 0 || UvChannel(name).Channel >= 0)
        : name == "DiscardValuie" || UvChannel(name).Channel >= 0);
    public static string? TextureRole(int kind, string role) => (kind, role) switch
    {
        (1, "LerpTex") or (2, "Mask0Texture") => "Col0Tex",
        (1, "Blend0Tex") or (2, "Mask1Texture") => "Col0ColChangeTex",
        (1, "Blend1Tex") => "LyCol0Tex",
        _ => null
    };
    public static PreviewSurface Read(ModelBuffer data, int material, PreviewSurface surface, int kind,
        out Vector4 uv, out Vector4 maskUv, out Vector4 underlayUv)
    {
        var values = surface.Values;
        for (var i = 0; i < 6; i++) values[i] = [1, 1, 1, 1];
        values[6] = [0, 0, 1, 1]; values[7] = [1, .85f, 1, 1]; values[8] = [1, 0, 0, 0];
        var transforms = new Dictionary<string, float[]>(StringComparer.Ordinal)
        { ["UVScaleOffset"] = [1, 1, 0, 0], ["MaskUV"] = [1, 1, 0, 0], ["UnderlayUV"] = [1, 1, 0, 0] };
        var link = 0f;
        foreach (var parameter in data.Tables(material, 13, 256))
        {
            var name = data.Text(parameter, 0) ?? "";
            var at = data.Field(parameter, 1); var value = at == 0 ? 0 : data.Float(at);
            var scalar = ScalarSlot(name); var transform = UvChannel(name);
            if (scalar.Slot >= 0) values[scalar.Slot][scalar.Channel] = value;
            if (transform.Channel >= 0) transforms[transform.Parameter][transform.Channel] = value;
            if (name == "MatLinkID") link = value;
        }
        if (!float.IsFinite(link) || link != MathF.Truncate(link) || link is < 0 or > 254)
            throw new InvalidDataException("Fire material link is invalid.");
        foreach (var parameter in data.Tables(material, 14, 256))
        {
            var slot = ColorSlot(data.Text(parameter, 0) ?? ""); var at = data.Field(parameter, 1);
            if (slot >= 0) values[slot] = at == 0 ? [0, 0, 0, 1] : [data.Float(at), data.Float(at + 4), data.Float(at + 8), 1];
        }
        Vector4 Transform(string name) { var v = transforms[name]; return new(v[0], v[1], v[2], v[3]); }
        uv = Transform("UVScaleOffset"); maskUv = Transform("MaskUV"); underlayUv = Transform("UnderlayUV");
        return surface with { Effect = kind, EffectLink = (int)link };
    }
}
