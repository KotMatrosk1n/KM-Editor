// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    /// <summary>Adds the programs consumed by the qualified source material contracts.</summary>
    public static byte[] Templates(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts)
    {
        // Template retains a forward-reference copy of the existing material
        // graph. Adding unused alternatives repeatedly duplicates that graph
        // and can exhaust the bounded builder before geometry conversion.
        var programs = contracts.Values.SelectMany(c => c.Transparent
            ? new[] { c.Program(), c.Program(inner: true) } : [c.Program()]).Distinct(StringComparer.Ordinal);
        foreach (var program in programs)
        {
            var pass = Array.IndexOf(TransparentPrograms, program);
            if (pass < 0) pass = Array.IndexOf(TransparentBasePrograms, program);
            if (pass < 0) pass = Array.IndexOf(SvSwShSourceBase.TransparentPrograms, program);
            if (pass < 0) pass = Array.IndexOf(SvSwShSourceBase.DynamicTransparentPrograms, program);
            if (pass < 0) pass = Array.IndexOf(CoveragePrograms, program);
            if (pass < 0) pass = Array.IndexOf(SvSwShSourceCoverage.Programs, program);
            var source = pass >= 0 ? SvSwShTransparentMaterial.Programs[pass]
                : program == PointLightProgramName ? SvSwShCornealMaterial.ProgramName : null;
            bytes = Template(bytes, program, source);
            if (program == SvSwShSourceBase.DynamicProgram || SvSwShSourceBase.DynamicTransparentPrograms.Contains(program))
                bytes = SvSwShDynamicSource.Template(bytes, program);
        }
        return bytes;
    }
}
