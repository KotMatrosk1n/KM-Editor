// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Owns donor tint uniforms independently of the native global color controls.</summary>
public static class SvSwShSourceCoverage
{
    public static readonly string[] Programs = ["SourceCoverageFront", "SourceCoverageBack"];
    public const string OpaqueProgramName = "SourceOpaqueCoverage";
    public static readonly string[] AllPrograms = [OpaqueProgramName, .. Programs];
    public static readonly string[] VectorNames = ["SourceTint1", "SourceTint2", "SourceTint3", "SourceTint4"];
    internal const int FirstVector = 19;

    internal static byte[] Template(byte[] bytes, string program)
    {
        if (!AllPrograms.Contains(program, StringComparer.Ordinal) && !SvSwShSourceBase.AllPrograms.Contains(program, StringComparer.Ordinal))
            throw new InvalidDataException("Source coverage requires its declared program.");
        var source = new ModelBuffer(bytes);
        var (start, count) = source.Vector(source.Root, 3, 4, 256);
        var programIndex = Enumerable.Range(0, count).Single(i => source.StringAt(start + i * 4) == program);
        var material = source.Tables(source.Root, 6, 128).Single(m => source.Value(m, 8) == programIndex);
        var colors = source.Tables(material, 14, 256);
        string[] expected = ["ConstantColor0", "ConstantColorSd0", "ConstantColor1", "ConstantColorSd1",
            "L1ConstantColor0", "L1AddColor0", "L1ConstantColor1", "L1AddColor1", "L1ConstantColorSd0", "L1ConstantColorSd1",
            "DeepShadowColor", "SpecularColor", "SphereMapColor", "RimColor", "RimColorShadow", "EffectColor01",
            "ConstantColor", "OnGameColor", "OutLineCol"];
        if (program is OpaqueProgramName or SvSwShSourceBase.ProgramName or SvSwShSourceBase.MirroredProgramName or SvSwShSourceBase.SharedUvProgramName or SvSwShSourceBase.MatteMirrorProgramName or SvSwShSourceBase.UnlitProgramName or SvSwShSourceBase.DynamicProgram)
            expected = [.. expected.Take(15), "ConstantColor", "OnGameColor", "OutLineCol", "EffectColor01"];
        if (colors.Length != FirstVector || !colors.Select(c => source.Text(c, 0)).SequenceEqual(expected))
            throw new InvalidDataException("Source coverage native vector interface changed.");
        var writer = new ModelTableBuilder(bytes); var slots = writer.Vector(material, 14, FirstVector + VectorNames.Length);
        // Clone small parameter rows rather than appending the entire model.
        // Native names, absence and all twelve value bytes remain unchanged.
        for (var i = 0; i < FirstVector; i++)
        {
            var at = source.Field(colors[i], 1);
            var row = writer.Table(2, at == 0 ? new Dictionary<int, byte[]>() : new Dictionary<int, byte[]> { [1] = source.Slice(at, 12).ToArray() },
                new Dictionary<int, int> { [1] = 12 }, absent: at == 0 ? new HashSet<int> { 1 } : new HashSet<int>());
            writer.Pointer(slots[i], row); writer.Text(row, 0, expected[i]);
        }
        for (var i = 0; i < VectorNames.Length; i++)
        {
            // The template inherits native placeholders; BindCoverageParameters
            // always replaces these owned rows with the qualified donor tints.
            var inherited = colors.Single(c => source.Text(c, 0) == SvSwShLayerEmission.BaseVectors[i]);
            var row = writer.Table(2, new Dictionary<int, byte[]> { [1] = source.Slice(source.Field(inherited, 1), 12).ToArray() },
                new Dictionary<int, int> { [1] = 12 });
            writer.Pointer(slots[FirstVector + i], row); writer.Text(row, 0, VectorNames[i]);
        }
        return writer.Finish(source.Root);
    }

    internal static void RequireBindings(ModelBuffer source, int material)
    {
        var colors = source.Tables(material, 14, 256);
        if (colors.Length != FirstVector + VectorNames.Length
            || !colors.Skip(FirstVector).Select(c => source.Text(c, 0)).SequenceEqual(VectorNames))
            throw new InvalidDataException("Source coverage tint uniforms are missing or reordered.");
    }
}
