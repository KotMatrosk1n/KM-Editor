// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

public static class SwShPreviewLayerEmission
{
    public static bool UsesProgram(ModelBuffer source, int material, string programName = KM.Formats.SV.SvSwShLayerEmission.ProgramName)
    {
        var (start, count) = source.Vector(source.Root, 5, 4, 256); var index = source.Value(material, 10);
        if (index >= count) return false;
        var name = source.StringAt(start + checked((int)index) * 4);
        return name == programName
            || programName == KM.Formats.SV.SvSwShLayerEmission.UnlitBaseProgramName && name == KM.Formats.SV.SvSwShSourceBase.UnlitProgramName
            || programName == KM.Formats.SV.SvSwShLayerEmission.BaseProgramName
                && (name == KM.Formats.SV.SvSwShLayerEmission.OpaqueCoverageProgramName
                    || KM.Formats.SV.SvSwShLayerEmission.TransparentBasePrograms.Contains(name, StringComparer.Ordinal)
                    || KM.Formats.SV.SvSwShLayerEmission.CoveragePrograms.Contains(name, StringComparer.Ordinal)
                    || KM.Formats.SV.SvSwShSourceCoverage.AllPrograms.Contains(name, StringComparer.Ordinal)
                    || KM.Formats.SV.SvSwShSourceBase.AllPrograms.Contains(name, StringComparer.Ordinal))
            || programName == KM.Formats.SV.SvSwShLayerEmission.ProgramName
            && (name == KM.Formats.SV.SvSwShLayerEmission.MirroredProgramName
                || name == KM.Formats.SV.SvSwShLayerEmission.ThreeLayerProgramName
                || name == KM.Formats.SV.SvSwShLayerEmission.BaseProgramName
                || name == KM.Formats.SV.SvSwShLayerEmission.UnlitBaseProgramName
                || name == KM.Formats.SV.SvSwShLayerEmission.AdditiveProgramName
                || name == KM.Formats.SV.SvSwShLayerEmission.UniformProgramName
                || name == KM.Formats.SV.SvSwShLayerEmission.OpaqueCoverageProgramName
                || KM.Formats.SV.SvSwShLayerEmission.CoveragePrograms.Contains(name, StringComparer.Ordinal)
                || KM.Formats.SV.SvSwShSourceCoverage.AllPrograms.Contains(name, StringComparer.Ordinal)
                    || KM.Formats.SV.SvSwShSourceBase.AllPrograms.Contains(name, StringComparer.Ordinal)
                || KM.Formats.SV.SvSwShLayerEmission.TransparentPrograms.Contains(name, StringComparer.Ordinal)
                || KM.Formats.SV.SvSwShLayerEmission.TransparentBasePrograms.Contains(name, StringComparer.Ordinal));
    }

}
