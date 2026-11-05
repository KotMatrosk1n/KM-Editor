// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Retains each Minior palette without replacing its authored color poses.</summary>
public static class SvSwShMiniorPalette
{
    private static bool Material(string name) => name is "body_a_00" or "body_a_01" or "body_a_02" or "body_a_03" or "eye";

    public static IReadOnlySet<(string Material, string Parameter)> AuthoredColors(byte[] bytes, bool standalone)
    {
        var source = new ModelBuffer(bytes); var result = new HashSet<(string, string)>();
        foreach (var mesh in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(mesh, 4);
            if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            foreach (var parameter in source.Tables(material, 2, 256))
            {
                var name = source.Text(parameter, 0)!;
                if (!name.StartsWith("BaseColorLayer", StringComparison.Ordinal)
                    && !name.StartsWith("EmissionColorLayer", StringComparison.Ordinal)) continue;
                var target = source.Text(material, 0)!;
                if (!Material(target) || !Enumerable.Range(1, 4).Any(i => name == "BaseColorLayer" + i || name == "EmissionColorLayer" + i))
                    throw new InvalidDataException("Minior color pose has an unqualified target.");
                result.Add((target, name));
            }
        }
        // Declared standalone poses never enter the shared native aliases.
        // A future color-bearing ordinary clip needs its own palette consumer.
        if (!standalone && result.Count != 0)
            throw new InvalidDataException("Minior shared motion has an authored color channel.");
        return result;
    }

    public static byte[] Animation(byte[] animation, byte[] ordinaryModel, byte[] paletteModel,
        IReadOnlyDictionary<string, string> originalNames, IReadOnlySet<(string Material, string Parameter)>? authored)
    {
        return SvSwShDolphinPalette.BindConstants(animation, Values(ordinaryModel), Values(paletteModel));

        Dictionary<string, Dictionary<string, Vector3>> Values(byte[] model)
        {
            var source = new ModelBuffer(model);
            var materials = source.Tables(source.Root, 6, 128).ToDictionary(m => source.Text(m, 0)!, StringComparer.Ordinal);
            var result = new Dictionary<string, Dictionary<string, Vector3>>(StringComparer.Ordinal);
            foreach (var pair in originalNames.Where(p => Material(p.Value)))
            {
                if (!materials.TryGetValue(pair.Key, out var material))
                    throw new InvalidDataException("Minior palette material is missing.");
                foreach (var (field, binding) in new[] { (3, 8), (5, 10) })
                {
                    var (start, count) = source.Vector(source.Root, field, 4, 256); var index = source.Value(material, binding);
                    if (index >= count || source.StringAt(start + checked((int)index) * 4) != SvSwShSourceBase.ProgramName)
                        throw new InvalidDataException("Minior palette requires its qualified source base program.");
                }
                SvSwShSourceCoverage.RequireBindings(source, material);
                var colors = source.Tables(material, 14, 256).ToDictionary(t => source.Text(t, 0)!);
                var parameters = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                for (var layer = 0; layer < 4; layer++)
                foreach (var (native, original) in new[] { (SvSwShSourceCoverage.VectorNames[layer], "BaseColorLayer" + (layer + 1)),
                    (SvSwShLayerEmission.Vectors[layer], "EmissionColorLayer" + (layer + 1)) })
                {
                    if (authored?.Contains((pair.Value, original)) == true) continue;
                    var at = source.Field(colors[native], 1);
                    if (at == 0) throw new InvalidDataException("Minior palette color is absent.");
                    parameters.Add(native, new(source.Float(at), source.Float(at + 4), source.Float(at + 8)));
                }
                result.Add(pair.Key, parameters);
            }
            if (result.Count == 0) throw new InvalidDataException("Minior palette has no qualified consumers.");
            return result;
        }
    }
}
