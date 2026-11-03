// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract> ClefableContracts(byte[] bytes, PreviewScene scene)
    {
        var wings = scene.Primitives.Where(p => p.Material.Name == "body_b").ToArray();
        if (!wings.Select(p => p.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(ZaSwShUnlitMaterial.ClefableWings.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Mega Clefable unlit wing geometry requires another adapter.");
        var palettes = wings.Select(p => ZaSwShUnlitMaterial.ClefablePalette(bytes, p)).ToArray();
        if (!palettes[0].SequenceEqual(palettes[1])) throw new InvalidDataException("Mega Clefable wing palette is ambiguous.");
        return new Dictionary<string, Contract>(StringComparer.Ordinal)
        {
            ["body_b"] = new("body_b", "pm0036_51_00_00_body_b_lym.bntx", new Vector3[4])
            {
                MaskUv = new(2, 1, 0, 0), MaskWrap = new(6, 1), MirroredMask = true,
                BaseColors = palettes[0], Unlit = true, MaximumIntensity = 0,
            },
        };
    }

    private static Vector3[] MiniorBaseColors(IReadOnlyDictionary<string, Vector4> vectors)
    {
        if (vectors.TryGetValue("BaseColor", out var global) && global != Vector4.One)
            throw new InvalidDataException("Minior global base color requires another adapter.");
        var colors = Enumerable.Range(1, 4).Select(layer => vectors.TryGetValue("BaseColorLayer" + layer, out var color)
            ? color : throw new InvalidDataException("Minior base palette is incomplete.")).ToArray();
        if (colors.Any(c => c.W != 1 || !float.IsFinite(c.X + c.Y + c.Z)
            || c.X < 0 || c.X > 2 || c.Y < 0 || c.Y > 2 || c.Z < 0 || c.Z > 2))
            throw new InvalidDataException("Minior base palette requires another adapter.");
        return colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray();
    }

    private static void RequireNativeBaseSlots(ModelBuffer source, int material, bool transparent = false)
    {
        if (transparent)
        {
            foreach (var name in new[] { "ConstantAlpha", "OnGameAlpha" })
            {
                var parameters = source.Tables(material, 13, 256).Where(t => source.Text(t, 0) == name).ToArray();
                if (parameters.Length != 1 || source.Field(parameters[0], 1) is var at && (at == 0 || source.Float(at) != 1))
                    throw new InvalidDataException("Original source alpha requires unit native alpha controls.");
            }
            foreach (var name in new[] { "ColorUVScaleU", "ColorUVScaleV", "ColorUVTranslateU", "ColorUVTranslateV" })
            {
                var parameters = source.Tables(material, 13, 256).Where(t => source.Text(t, 0) == name).ToArray();
                if (parameters.Length != 1 || (source.Field(parameters[0], 1) is var at && at != 0 ? source.Float(at) : 0)
                    != (name.StartsWith("ColorUVScale", StringComparison.Ordinal) ? 1 : 0))
                    throw new InvalidDataException("Original source alpha requires neutral native UV controls.");
            }
        }
        var vectors = source.Tables(material, 14, 256);
        foreach (var (name, index) in BaseVectors.Zip(transparent ? new[] { 0, 2, 16, 17 } : new[] { 0, 2, 15, 16 }))
        {
            if (vectors.Length <= index || source.Text(vectors[index], 0) != name
                || Enumerable.Range(0, 3).Any(c => source.Float(source.Field(vectors[index], 1) + c * 4) != 1))
                throw new InvalidDataException("Native base palette slots require another adapter.");
        }
    }

    private static PreviewClip WithBaseColors(PreviewClip clip, IReadOnlyDictionary<string, Contract> contracts)
    {
        var names = contracts.Where(p => p.Value.BaseColors is not null).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var owned = clip.Materials.Where(t => names.Contains(t.Material) && t.Parameter.StartsWith("BaseColorLayer", StringComparison.Ordinal)).ToArray();
        var bases = owned.GroupBy(t => (t.Material, t.Parameter)).Select(group =>
        {
            var first = group.First();
            if (group.Any(track => track.Channels.Length != first.Channels.Length
                || !track.Channels.Zip(first.Channels).All(channels => channels.First.Length == channels.Second.Length
                    && channels.First.Zip(channels.Second).All(keys => keys.First.Frame == keys.Second.Frame
                        && keys.First.Value.SequenceEqual(keys.Second.Value)))))
                throw new InvalidDataException("Source base palette binding is ambiguous.");
            return first;
        }).ToList();
        foreach (var name in names)
        {
            for (var layer = 1; layer <= 4; layer++) if (!bases.Any(t => t.Material == name && t.Parameter == "BaseColorLayer" + layer))
            {
                var color = contracts[name].BaseColors![layer - 1];
                bases.Add(new(name, "BaseColorLayer" + layer,
                    new[] { color.X, color.Y, color.Z, contracts[name].SourceBaseAlphas[layer - 1] }.Select(v => new[] { new PreviewKey(0, [v]) }).ToArray()));
            }
            RequireBaseTracks(bases.Where(t => t.Material == name).ToArray(), contracts[name].SourceBaseAlphas);
        }
        return clip with { Materials = clip.Materials.Except(owned).Concat(bases).ToArray() };
    }

    private static void RequireBaseTracks(PreviewMaterialTrack[] tracks, Vector4? sourceAlphas = null)
    {
        if (tracks.Length != 4 || tracks.Select(t => t.Parameter).Distinct(StringComparer.Ordinal).Count() != 4
            || tracks.Any(t => !Enumerable.Range(1, 4).Any(layer => t.Parameter == "BaseColorLayer" + layer) || t.Channels.Length != 4))
            throw new InvalidDataException("Base palette channels are incomplete or ambiguous.");
        foreach (var track in tracks)
        {
            ValidateEmissionKeys(track.Channels);
            var alpha = (sourceAlphas ?? Vector4.One)[int.Parse(track.Parameter[^1..], System.Globalization.CultureInfo.InvariantCulture) - 1];
            if (track.Channels[3].Any(k => k.Value[0] != alpha)
                || track.Channels.Take(3).SelectMany(c => c).Any(k => k.Value[0] < 0 || k.Value[0] > 2))
                throw new InvalidDataException("Base palette color requires another adapter.");
        }
    }
}
