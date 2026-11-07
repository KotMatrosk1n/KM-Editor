// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;

namespace KM.Tools.Application;

/// <summary>Applies a neutral native lighting response to converted opaque materials.</summary>
internal static class SwShRestorationMaterialResponse
{
    // The compiled native material template also carries its donor's color choices.
    // Keep those colors out of imported palettes without adding texture allocations.
    private static readonly (string Name, float[] Values)[] Parameters =
    [
        ("ConstantColorSd0", [.68f, .68f, .68f]),
        ("ConstantColorSd1", [.68f, .68f, .68f]),
        ("SpecularColor", [.85f, .85f, .85f]),
        ("RimColor", [.3f, .3f, .3f]),
        ("RimColorShadow", [.2f, .2f, .2f]),
        ("LightTblIndex", [5]),
        ("SpecularScale", [.3f]),
    ];

    internal static byte[] Apply(byte[] model, IReadOnlyDictionary<string, (float Power, float Scale)>? specular = null)
    {
        var source = new ModelBuffer(model);
        var output = model.ToArray();
        var converted = 0;
        foreach (var material in source.Tables(source.Root, 6, 256))
        {
            var shader = source.Text(material, 1);
            if (shader is "PokeFireCoreShader1" or "PokeFireMaskShader1") continue;
            if (shader != "PokeDefaultShader")
                throw new InvalidDataException("Converted material needs a qualified lighting response.");
            foreach (var (name, values) in Parameters) WriteParameter(material, name, values);
            if (specular is not null && specular.TryGetValue(source.Text(material, 0)!, out var highlight))
            {
                WriteParameter(material, "SpecularPower", [highlight.Power]);
                WriteParameter(material, "SpecularScale", [highlight.Scale]);
            }
            converted++;
        }
        if (converted == 0) throw new InvalidDataException("Converted model has no supported opaque materials.");
        return output;

        void WriteParameter(int material, string name, ReadOnlySpan<float> values)
        {
            var fields = source.Tables(material, values.Length == 1 ? 13 : 14, 256)
                .Where(p => source.Text(p, 0) == name).ToArray();
            if (fields.Length != 1)
                throw new InvalidDataException("Native lighting parameter is missing or ambiguous.");
            var table = fields[0];
            var at = source.Field(table, 1);
            var end = table + source.U16(table - source.I32(table) + 2);
            if (at == 0 || at + values.Length * 4 > end)
                throw new InvalidDataException("Native lighting parameter has an unsupported layout.");
            for (var i = 0; i < values.Length; i++)
            {
                if (!float.IsFinite(source.Float(at + i * 4)))
                    throw new InvalidDataException("Native lighting parameter is not finite.");
                BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(at + i * 4, 4), values[i]);
            }
        }
    }
}
