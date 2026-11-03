// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static void RequireNativeCoverageSlots(ModelBuffer source, int material)
    {
        var scalars = source.Tables(material, 13, 256); var vectors = source.Tables(material, 14, 256);
        if (scalars.Length <= 22 || source.Text(scalars[22], 0) != CoverageIntensity
            || (source.Field(scalars[22], 1) is var at && at != 0 ? source.Float(at) : 0) != 1
            || vectors.Length <= 8 || source.Text(vectors[8], 0) != CoverageColor
            || Enumerable.Range(0, 3).Any(c => source.Float(source.Field(vectors[8], 1) + c * 4) != 1))
            throw new InvalidDataException("Native coverage emission controls changed.");
    }

    private static void BindCoverageParameters(ModelBuffer source, ModelTableBuilder writer, int material, Contract contract)
    {
        if (contract.SeparateBaseColors) SvSwShSourceCoverage.RequireBindings(source, material);
        // Native defaults can be shared by several material rows. Allocate owned
        // parameter tables before applying source values so siblings keep theirs.
        foreach (var field in new[] { 13, 14 })
        {
            var rows = source.Tables(material, field, 256); var slots = writer.Vector(material, field, rows.Length);
            for (var i = 0; i < rows.Length; i++)
            {
                var name = source.Text(rows[i], 0)!; var at = source.Field(rows[i], 1);
                float[] values;
                if (field == 13)
                {
                    var layer = Array.IndexOf(Scalars, name);
                    values = [layer >= 0 ? contract.Initial[layer] : contract.NormalizedCoverage && name == CoverageIntensity ? contract.GlobalEmissionInitial
                        : !contract.NormalizedCoverage && name == "EmissionMaskVal" ? 0
                        : contract.PointColor is not null && name == PointLightIntensity ? contract.PointIntensity : at == 0 ? 0 : source.Float(at)];
                }
                else
                {
                    var layer = Array.IndexOf(contract.SeparateBaseColors ? SvSwShSourceCoverage.VectorNames : BaseVectors, name);
                    var emission = Array.IndexOf(Vectors, name);
                    var color = layer >= 0 ? contract.BaseColors![layer] : emission >= 0 ? contract.Colors[emission]
                        : contract.NormalizedCoverage && name == CoverageColor ? contract.GlobalEmissionColor : name == PointLightColor && contract.PointColor is { } point ? point
                        : new(source.Float(at), source.Float(at + 4), source.Float(at + 8));
                    values = [color.X, color.Y, color.Z];
                }
                var row = writer.Table(2, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Floats(values) },
                    sizes: new Dictionary<int, int> { [1] = values.Length * 4 });
                writer.Text(row, 0, name); writer.Pointer(slots[i], row);
            }
        }
    }

    private static void RequireCoverageKeys(ModelBuffer source, int scalar)
    {
        var keys = source.Tables(source.Table(scalar, 1), 0, 18000);
        float Value(int key, int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
        // The source evaluator reads field4 and dispatches value1 to linear
        // interpolation; fields2/3 are the unused Hermite tangents in this mode.
        if (keys.Length != 0 && Value(keys[0], 0) != 0
            || keys.Any(k => source.Value(k, 2) != 0 || source.Value(k, 3) != 0 || source.Value(k, 4) != 1
                || Value(k, 0) != MathF.Truncate(Value(k, 0))))
            throw new InvalidDataException("Coverage emission requires its qualified linear source keys.");
    }
}
