// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Formats.SV;

/// <summary>Preserves the qualified dolphins' static eye colors in each palette's clips.</summary>
public static class SvSwShDolphinPalette
{
    public static byte[] Animation(byte[] animation, byte[] ordinaryModel, byte[] paletteModel)
        => BindConstants(animation, Colors(ordinaryModel), Colors(paletteModel));

    // Each caller must qualify these parameters as static in its source clips.
    // Active source channels remain owned by their animation adapters.
    internal static byte[] BindConstants(byte[] animation,
        IReadOnlyDictionary<string, Dictionary<string, Vector3>> ordinary,
        IReadOnlyDictionary<string, Dictionary<string, Vector3>> palette,
        IReadOnlyDictionary<string, Dictionary<string, float>>? ordinaryScalars = null,
        IReadOnlyDictionary<string, Dictionary<string, float>>? paletteScalars = null)
    {
        Dictionary<string, Dictionary<string, float[]>> Vectors(IReadOnlyDictionary<string, Dictionary<string, Vector3>> values)
            => values.ToDictionary(p => p.Key, p => p.Value.ToDictionary(v => v.Key, v => new[] { v.Value.X, v.Value.Y, v.Value.Z }, StringComparer.Ordinal), StringComparer.Ordinal);
        Dictionary<string, Dictionary<string, float[]>> Scalars(IReadOnlyDictionary<string, Dictionary<string, float>>? values)
            => values?.ToDictionary(p => p.Key, p => p.Value.ToDictionary(v => v.Key, v => new[] { v.Value }, StringComparer.Ordinal), StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
        var fields = new[] { (Field: 3, Components: 3, Ordinary: Vectors(ordinary), Palette: Vectors(palette)),
            (Field: 2, Components: 1, Ordinary: Scalars(ordinaryScalars), Palette: Scalars(paletteScalars)) };
        foreach (var field in fields)
        {
            if (!field.Ordinary.Keys.Order(StringComparer.Ordinal).SequenceEqual(field.Palette.Keys.Order(StringComparer.Ordinal))
                || field.Ordinary.Any(p => !p.Value.Keys.Order(StringComparer.Ordinal).SequenceEqual(field.Palette[p.Key].Keys.Order(StringComparer.Ordinal)))
                || field.Ordinary.Values.Concat(field.Palette.Values).SelectMany(p => p.Values).Any(v => v.Length != field.Components || v.Any(x => !float.IsFinite(x))))
                throw new InvalidDataException("Static material palette consumers disagree.");
        }
        var source = new ModelBuffer(animation); var writer = new ModelTableBuilder(animation);
        var references = new List<(int Slot, int Target)>(); var changed = false;
        foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128))
        {
            var name = source.Text(material, 0)!;
            foreach (var field in fields)
            {
                if (!field.Ordinary.TryGetValue(name, out var defaults)) continue;
                var tracks = source.Tables(material, field.Field, 256); var targets = field.Palette[name];
                if (!tracks.Any(t => defaults.TryGetValue(source.Text(t, 0)!, out var value)
                    && !targets[source.Text(t, 0)!].AsSpan().SequenceEqual(value))) continue;
                var slots = writer.Vector(material, field.Field, tracks.Length);
                for (var i = 0; i < tracks.Length; i++)
                {
                    var parameter = source.Text(tracks[i], 0)!;
                    if (!defaults.TryGetValue(parameter, out var original) || targets[parameter].AsSpan().SequenceEqual(original))
                    {
                        references.Add((slots[i], tracks[i])); continue;
                    }
                    var curve = source.Table(tracks[i], 2); var at = source.Field(curve, 0); var vtable = curve - source.I32(curve);
                    if (source.Value(tracks[i], 1) != 1 || at == 0
                        || source.U16(tracks[i] - source.I32(tracks[i])) != 10
                        || source.U16(vtable) != 6 || source.U16(vtable + 2) != 4 + field.Components * 4
                        || Enumerable.Range(0, field.Components).Any(c => source.Float(at + c * 4) != original[c]))
                        throw new InvalidDataException("Static palette requires its qualified constant animation parameter.");
                    var target = writer.Table(3, new Dictionary<int, byte[]> { [1] = [1] });
                    writer.Pointer(slots[i], target); writer.Text(target, 0, parameter);
                    writer.Reference(target, 2, writer.Table(1,
                        new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Floats(targets[parameter]) },
                        sizes: new Dictionary<int, int> { [0] = field.Components * 4 }));
                    changed = true;
                }
            }
        }
        if (!changed) return animation;
        if (references.Count != 0)
        {
            var copy = writer.Append(animation);
            foreach (var (slot, target) in references) writer.Pointer(slot, copy + target);
        }
        return writer.Finish(source.Root);
    }

    private static Dictionary<string, Dictionary<string, Vector3>> Colors(byte[] model)
    {
        var source = new ModelBuffer(model); var result = new Dictionary<string, Dictionary<string, Vector3>>(StringComparer.Ordinal);
        var (start, count) = source.Vector(source.Root, 3, 4, 256);
        foreach (var material in source.Tables(source.Root, 6, 128).Where(m => source.Text(m, 0) is "l_eye" or "r_eye"))
        {
            var program = source.Value(material, 8);
            if (program >= count || source.StringAt(start + checked((int)program) * 4) != ZaSwShEyelidMaterial.EmissionProgramName)
                throw new InvalidDataException("Dolphin palette requires its qualified layered eye program.");
            var rows = source.Tables(material, 14, 256);
            result.Add(source.Text(material, 0)!, ZaSwShEyelidMaterial.EmissionVectorNames.ToDictionary(name => name, name =>
            {
                var row = rows.Single(t => source.Text(t, 0) == name); var at = source.Field(row, 1);
                if (at == 0) throw new InvalidDataException("Dolphin source eye color is missing.");
                return new Vector3(source.Float(at), source.Float(at + 4), source.Float(at + 8));
            }, StringComparer.Ordinal));
        }
        if (result.Count != 2) throw new InvalidDataException("Dolphin palette requires both eye consumers.");
        return result;
    }
}
