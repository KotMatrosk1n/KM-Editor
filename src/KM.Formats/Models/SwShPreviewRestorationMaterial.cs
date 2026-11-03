// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SV;
using KM.Formats.ZA;

namespace KM.Formats.Models;

/// <summary>Preview bindings for the emitted restoration shader contracts.</summary>
internal static class SwShPreviewRestorationMaterial
{
    public static int Kind(ModelBuffer data, int material)
    {
        if (SwShPreviewFresnel.Settings(data, material) is not null) return 10;
        if (SwShPreviewLayerEmission.UsesProgram(data, material, SvSwShLayerEmission.OpaqueCoverageProgramName)
            || SvSwShLayerEmission.CoveragePrograms.Any(p => SwShPreviewLayerEmission.UsesProgram(data, material, p))
            || SvSwShSourceCoverage.AllPrograms.Any(p => SwShPreviewLayerEmission.UsesProgram(data, material, p))) return 7;
        if (SwShPreviewLayerEmission.UsesProgram(data, material, SvSwShLayerEmission.UniformProgramName)) return 8;
        if (SwShPreviewLayerEmission.UsesProgram(data, material, SvSwShLayerEmission.FresnelProgramName)
            || SwShPreviewLayerEmission.UsesProgram(data, material, SvSwShLayerEmission.FresnelMirrorProgramName)) return 1;
        if (SwShPreviewLayerEmission.UsesProgram(data, material, SvSwShLayerEmission.LavaProgramName)) return 2;
        if (SwShPreviewLayerEmission.UsesProgram(data, material, ZaSwShEyelidMaterial.EmissionProgramName)) return 5;
        if (SwShPreviewLayerEmission.UsesProgram(data, material, ZaSwShEyelidMaterial.TintProgramName)) return 6;
        if (!SwShPreviewLayerEmission.UsesProgram(data, material, ZaSwShEyelidMaterial.ProgramName)
            && !SwShPreviewLayerEmission.UsesProgram(data, material, ZaSwShEyelidMaterial.HighlightProgramName)) return 0;
        return data.Tables(data.Root, 6, 256).Any(m => SwShPreviewLayerEmission.UsesProgram(data, m, SvSwShLayerEmission.LavaProgramName)) ? 3 : 4;
    }

    public static PreviewSurface Read(ModelBuffer data, int material, PreviewSurface surface, int kind)
    {
        if (kind == 0) return surface;
        if (kind == 10) return SwShPreviewFresnel.Read(data, material, surface);
        var scalars = data.Tables(material, 13, 256).ToDictionary(p => data.Text(p, 0)!, p => data.Field(p, 1) is var at && at != 0 ? data.Float(at) : 0, StringComparer.Ordinal);
        var vectors = data.Tables(material, 14, 256).ToDictionary(p => data.Text(p, 0)!, p =>
        { var at = data.Field(p, 1); return new[] { data.Float(at), data.Float(at + 4), data.Float(at + 8), 0f }; }, StringComparer.Ordinal);
        if (kind is 7 or 8)
        {
            var coverage = Enumerable.Range(0, 10).Select(_ => new float[4]).ToArray();
            if (kind == 7)
            {
                coverage[0][0] = scalars[SvSwShLayerEmission.CoverageIntensity];
                coverage[1] = vectors[SvSwShLayerEmission.CoverageColor];
            }
            surface.Values[32] = [1, 1, 1, 1]; surface.Values[33] = [0, 0, 0, 0];
            return surface with { RestorationEffect = kind, RestorationValues = coverage };
        }
        var values = Enumerable.Range(0, kind is 5 or 6 ? 15 : 10).Select(_ => new float[4]).ToArray();
        values[0] = [scalars.GetValueOrDefault(SvSwShLavaMaterial.ExtraEmission), scalars.GetValueOrDefault("Sin01"), scalars.GetValueOrDefault("ScaleUV"), scalars.GetValueOrDefault("DistortionIntensity", 1)];
        if (kind == 1) values[0][2] = SwShPreviewLayerEmission.UsesProgram(data, material, SvSwShLayerEmission.FresnelMirrorProgramName) ? 1 : 0;
        if (kind == 4) values[0][1] = SwShPreviewLayerEmission.UsesProgram(data, material, ZaSwShEyelidMaterial.HighlightProgramName) ? 1 : 0;
        var uv = kind is 3 or 4 or 5 or 6 ? SvSwShLavaMaterial.EyeUv : SvSwShLavaMaterial.BodyUv;
        values[1] = uv.Select((name, i) => scalars.GetValueOrDefault(name, i < 2 ? 1 : 0)).ToArray();
        values[2] = vectors.GetValueOrDefault("EffectColor01", new float[4]);
        values[3] = vectors.GetValueOrDefault("SphereMapColor", new[] { 1f, 1f, 1f, 0f });
        if (kind is 3 or 4 or 5 or 6)
        {
            values[2][3] = scalars.GetValueOrDefault("ColorBaseU");
            values[3][3] = scalars.GetValueOrDefault("ColorBaseV");
        }
        for (var i = 0; i < 4; i++) values[4 + i] = vectors.GetValueOrDefault(ZaSwShEyelidMaterial.VectorNames[i], new float[4]);
        for (var i = 0; i < 6; i++) values[8 + i / 3][i % 3] = scalars.GetValueOrDefault(ZaSwShEyelidMaterial.ScalarNames[16 + i]);
        for (var i = 0; i < 6; i++) values[4 + i][3] = scalars.GetValueOrDefault(ZaSwShEyelidMaterial.ScalarNames[10 + i]);
        if (kind is 5 or 6)
            for (var i = 0; i < 4; i++)
            {
                values[10 + i] = vectors[ZaSwShEyelidMaterial.EmissionVectorNames[i]];
                values[14][i] = scalars[ZaSwShEyelidMaterial.EmissionScalarNames[i]];
            }
        // These programs evaluate mask colors independently of the lit albedo.
        surface.Values[32] = [1, 1, 1, 1]; surface.Values[33] = [0, 0, 0, 0];
        return surface with { RestorationEffect = kind, RestorationValues = values };
    }

    public static string? TextureRole(int kind, string? role) => (kind is 4 or 5 or 6 ? 3 : kind, role) switch
    {
        (10, "SourceGemBaseTex") => "Col0Tex", (10, "SourceGemLayerTex") => "LyCol0Tex",
        (10, "SourceGemNormalTex") => "NormalMapTex", (10, "SourceGemLayerNormalTex") => "NormalMap1",
        (10, "SourceGemAoTex") => "AOMap", (10, "SourceGemMaskTex") => "Col0ColChangeTex",
        (1, "LyCol0Tex") => "NormalMap1", (1, "EffectTex") => "RoughnessMap",
        (2, "LyCol0Tex") or (3, "EffectTex") => "RoughnessMap",
        (3, "SphereMapTex") => "EmissionMap", (3, "EmissionMaskTex") => "NormalMap1",
        (3, "LyCol0Tex") => "SpecularMaskMap", (3, "AmbientTex") => "EyelidShadowMaskMap", _ => role
    };

    public static (int Slot, int Channel) Scalar(string name, int kind)
    {
        if (kind == 10)
        {
            var index = Array.IndexOf(ZaSwShFresnelMaterial.ScalarNames, name);
            return index >= 0 ? (index / 4, index % 4) : (-1, -1);
        }
        if (kind is 7 or 8) return kind == 7 && name == SvSwShLayerEmission.CoverageIntensity ? (0, 0) : (-1, -1);
        if (kind is 5 or 6 && Array.IndexOf(ZaSwShEyelidMaterial.EmissionScalarNames, name) is var intensity && intensity >= 0)
            return (14, intensity);
        var component = Array.IndexOf(kind is 3 or 4 or 5 or 6 ? SvSwShLavaMaterial.EyeUv : SvSwShLavaMaterial.BodyUv, name);
        if (component >= 0) return (1, component);
        if (name == SvSwShLavaMaterial.ExtraEmission) return (0, 0);
        if (kind is not (3 or 4 or 5 or 6)) return (-1, -1);
        if (name == "ColorBaseU") return (2, 3);
        if (name == "ColorBaseV") return (3, 3);
        var eye = Array.IndexOf(ZaSwShEyelidMaterial.ScalarNames, name);
        if (eye is >= 10 and < 16) return (4 + eye - 10, 3);
        return eye >= 16 ? (8 + (eye - 16) / 3, (eye - 16) % 3) : (-1, -1);
    }
}
