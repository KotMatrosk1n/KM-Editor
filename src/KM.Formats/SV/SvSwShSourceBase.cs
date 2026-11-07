// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SV;

/// <summary>Qualified source base palettes with independent native global controls.</summary>
public static class SvSwShSourceBase
{
    public const string ProgramName = "SourceLayerBase";
    public const string MirroredProgramName = "MirrorSourceBase";
    public const string SharedUvProgramName = "SharedUvBase";
    public const string MatteMirrorProgramName = "MatteMirrorBase";
    public const string UnlitProgramName = "SourceUnlitBase";
    public static readonly string[] TransparentPrograms = ["SourceBaseFront", "SourceBaseBack"];
    public const string DynamicProgram = "DynamicSourceBase";
    public static readonly string[] DynamicTransparentPrograms = ["DynamicSourceFront", "DynamicSourceBack"];
    public static readonly string[] AllPrograms = [ProgramName, MirroredProgramName, SharedUvProgramName, MatteMirrorProgramName, UnlitProgramName, DynamicProgram, .. TransparentPrograms, .. DynamicTransparentPrograms];

    internal static bool IsToucannon(SvSwShLayerEmission.Contract c) => (c.Name, c.Mask, c.Transparent, c.Unlit) is
        ("body_b_00" or "body_b_01", "pm0809_00_00_body_b_lym.bntx", false, false)
        or ("body_b_02" or "body_b_03", "pm0809_00_00_body_b_02_lym.bntx", false, false);

    internal static bool Recognizes(SvSwShLayerEmission.Contract c) => IsToucannon(c) || (c.Name, c.Mask, c.Transparent, c.Unlit) switch
    {
        ("body_a_01", "pm0609_51_00_body_a_01_lym.bntx", true, false) => true,
        ("l_eye" or "r_eye", "pm0609_51_00_eye_lym.bntx", false, false) => true,
        ("body_a_01" or "body_a_02" or "body_04" or "body_05", "pm0727_51_00_body_01_lym.bntx", _, false) => true,
        ("body_03", "pm0727_51_00_body_05_lym.bntx", true, false) => true,
        ("l_eye" or "r_eye", "pm0727_51_00_eye_lym.bntx", false, false) => true,
        ("body_b", "pm0036_51_00_00_body_b_lym.bntx", false, true) => true,
        ("body_b_00" or "body_b_01", "pm0359_52_00_body_b_lym.bntx", false, false) => true,
        ("body_a_01", "pm0373_51_00_body_a_lym.bntx", false, false) => true,
        ("body_b", "pm0373_51_00_body_b_lym.bntx", false, false) => true,
        ("body_b_00" or "body_b_02", "pm0382_51_00_body_b_lym.bntx", false, false) => true,
        ("body_a_00" or "body_a_01" or "body_a_02", "pm0705_51_00_body_a_lym.bntx", false, false) => true,
        ("body_b_00" or "body_b_01" or "body_b_02" or "body_b_03", "pm0491_51_00_00_body_b_lym.bntx", false, false) => true,
        ("body_b_00" or "body_b_01" or "body_b_02", "pm0888_51_00_body_b_lym.bntx", false, false) => true,
        ("body_a_00" or "body_a_01" or "body_a_02", "pm0888_51_00_body_a_lym.bntx", false, false) => true,
        ("body_b", "pm0923_51_00_body_b_lym.bntx", false, false) => true,
        ("body_a", "pm0923_51_00_body_a_lym.bntx", false, false) => true,
        ("body_a_00" or "body_a_01" or "body_a_02" or "body_a_03", "pm0824_11_00_body_a_lym.bntx" or "../pm0824_11_00/pm0824_11_00_body_a_lym.bntx", false, false) => true,
        ("eye", "pm0824_11_00_eye_lym.bntx" or "../pm0824_11_00/pm0824_11_00_eye_lym.bntx", false, false) => true,
        ("body_a_00" or "body_a_01", "pm1062_00_00_body_a_lym.bntx", false, false) => true,
        ("body_b_01", "pm1036_00_00_body_b_lym.bntx", true, false) => true,
        ("ring", "pm1037_00_00_body_01_lym.bntx" or "pm1038_11_00_body_01_lym.bntx", true, false) => true,
        _ => false,
    };

    internal static string Program(SvSwShLayerEmission.Contract c, bool inner)
    {
        // Source factories retain their palette, geometry, animation and material
        // guards. This dispatch cannot expand that set to another source material.
        if (!Recognizes(c) || c.BaseColors is not { Length: 4 } || c.NormalizedCoverage
            || c.PointColor is not null || c.UniformEmission || c.Additive || c.ColorMap || c.BaseColorMap
            || c.Lava || c.FresnelNormal is not null || c.FresnelNormal1 is not null)
            throw new InvalidDataException("Separate source tints require a qualified base palette.");
        if (c.MatteCrescent)
        {
            if (c.Name != "body_b" || c.Mask != "pm0373_51_00_body_b_lym.bntx" || c.Transparent || c.Unlit
                || !c.MirroredMask || c.MaskUv.X != 2)
                throw new InvalidDataException("The matte mirrored response requires the qualified crescent.");
            return MatteMirrorProgramName;
        }
        return c.DynamicSource ? c.Transparent ? DynamicTransparentPrograms[inner ? 1 : 0] : DynamicProgram
            : c.MirroredMask && c.MaskUv.X == 2 ? (c.SharedColorUv ? SharedUvProgramName : MirroredProgramName)
            : c.Transparent ? TransparentPrograms[inner ? 1 : 0] : c.Unlit ? UnlitProgramName : ProgramName;
    }
}
