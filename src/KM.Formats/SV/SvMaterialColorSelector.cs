// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Samples the declared static color selector in an S/V material variant.</summary>
public static class SvMaterialColorSelector
{
    public static byte[] Apply(byte[] variants, byte[] materials, bool rare, int selector, PreviewScene scene)
    {
        var source = new ModelBuffer(variants);
        var entries = source.Tables(source.Root, 2, 64).Where(t => source.Text(t, 0) == (rare ? "rare" : "normal")).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("Color selector material variant is ambiguous.");
        var groups = source.Tables(entries[0], 3, 64);
        if (groups.Length != 1 || source.Text(groups[0], 0) != "color")
            throw new InvalidDataException("Material color selector group is unsupported.");
        var group = groups[0];
        if (selector < 0 || selector > source.Value(group, 3) || source.Table(group, 2) != 0
            || source.Vector(group, 5, 4, 256).Count != 0 || source.Vector(group, 6, 4, 256).Count != 0)
            throw new InvalidDataException("Material color selector range or layout is unsupported.");
        var targets = new HashSet<(string Mesh, string Material, string Parameter)>();
        foreach (var target in source.Tables(group, 1, 256))
        {
            var mesh = source.Text(target, 0); var material = source.Text(target, 1); var parameter = source.Text(target, 2);
            if (string.IsNullOrEmpty(mesh) || string.IsNullOrEmpty(material) || string.IsNullOrEmpty(parameter)
                || !scene.Primitives.Any(p => p.Name == mesh && p.Material.Name == material)
                || !targets.Add((mesh, material, parameter)))
                throw new InvalidDataException("Material color selector target is unavailable or duplicated.");
        }
        var wrapper = source.Table(group, 4);
        if (wrapper == 0) throw new InvalidDataException("Material color selector curve is missing.");
        var curves = new ModelBuffer(source.Blob(wrapper, 0, 1024 * 1024));
        var values = new Dictionary<(string Material, string Parameter), float[]>();
        var matched = new HashSet<(string Mesh, string Material, string Parameter)>();
        foreach (var track in curves.Tables(curves.Root, 1, 256))
        {
            var mesh = curves.Text(track, 0) ?? "";
            var timeline = curves.Table(track, 4);
            if (timeline == 0) continue;
            foreach (var material in curves.Tables(timeline, 2, 128))
            foreach (var kind in new[] { 1, 2 })
            foreach (var parameter in curves.Tables(material, kind, 256))
            {
                var materialName = curves.Text(material, 0) ?? "";
                var name = curves.Text(parameter, 0) ?? "";
                var identity = (mesh, materialName, name);
                if (!targets.Contains(identity)) continue;
                if (!matched.Add(identity) || !Supported(name, kind))
                    throw new InvalidDataException("Material color selector parameter is unsupported or duplicated.");
                var channels = curves.Table(parameter, 1);
                if (channels == 0) throw new InvalidDataException("Material color selector channels are missing.");
                var sampled = Enumerable.Range(0, kind == 1 ? 1 : 4)
                    .Select(c => Sample(curves, kind == 1 ? channels : curves.Table(channels, c), selector)).ToArray();
                var key = (materialName, name);
                if (values.TryGetValue(key, out var previous) && !previous.SequenceEqual(sampled))
                    throw new InvalidDataException("Mesh-specific color selectors disagree on a shared material.");
                values[key] = sampled;
            }
        }
        if (!targets.SetEquals(matched)) throw new InvalidDataException("Material color selector target has no curve.");
        var document = new ModelMaterialDocument(materials, false);
        var changes = values.Select(pair =>
        {
            var fields = document.Fields.Where(f => f.Material == pair.Key.Material && f.Name == pair.Key.Parameter).ToArray();
            if (fields.Length != 1 || !fields[0].Editable || fields[0].Values.Length != pair.Value.Length)
                throw new InvalidDataException("Material color selector destination is unavailable or ambiguous.");
            return new ModelMaterialChange(fields[0].Key, pair.Value.Select(v => (double)v).ToArray());
        }).ToArray();
        return document.Apply(changes);
    }

    private static bool Supported(string name, int kind) => Enumerable.Range(1, 4).Any(layer =>
        kind == 1 ? name == $"EmissionIntensityLayer{layer}"
            : name == $"BaseColorLayer{layer}" || name == $"EmissionColorLayer{layer}");

    private static float Sample(ModelBuffer data, int channel, int selector)
    {
        if (channel == 0) throw new InvalidDataException("Material color selector channel is missing.");
        var keys = data.Tables(channel, 0, 18000).Select(key =>
        {
            float Value(int field) => data.Field(key, field) is var at && at != 0 ? data.Float(at) : 0;
            return (Frame: Value(0), Value: Value(1));
        }).ToArray();
        if (keys.Length == 0 || keys.Any(k => !float.IsFinite(k.Frame) || !float.IsFinite(k.Value) || k.Frame < 0)
            || keys.Zip(keys.Skip(1)).Any(pair => pair.First.Frame >= pair.Second.Frame)
            || keys[0].Frame > selector || keys[^1].Frame < selector)
            throw new InvalidDataException("Material color selector keys are invalid or do not cover the selection.");
        for (var i = 1; i < keys.Length; i++)
        {
            if (keys[i].Frame < selector) continue;
            var left = keys[i - 1]; var right = keys[i];
            return left.Value + (right.Value - left.Value) * ((selector - left.Frame) / (right.Frame - left.Frame));
        }
        return keys[^1].Value;
    }
}
