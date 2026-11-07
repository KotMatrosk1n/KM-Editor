// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract> MalamarContracts(byte[] bytes, PreviewScene scene)
    {
        if (System.Convert.ToHexString(SHA256.HashData(bytes)) is not
            ("4FE24EFF0C058A47681910FAA1DF367ECB9A1C0E549809DD28D219493E8BEA46"
            or "74261F7901C9092EAAA617EED97A069079A4553425911E44A10D36C09ACF4955"))
            throw new InvalidDataException("Malamar layered source identity changed.");
        var source = new ModelBuffer(bytes);
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var name in new[] { "body_a_01", "body_a_02", "l_eye", "r_eye", "body_03", "body_04", "body_05" })
        {
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
            var programs = source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).ToArray();
            var transparent = name is "body_a_02" or "body_03" or "body_04" or "body_05";
            var inner = transparent && name != "body_04";
            if (!programs.SequenceEqual(inner ? ["IkCharacterTransparent", "IkCharacterTransparentInner"]
                    : transparent ? ["Transparent"] : name.EndsWith("eye", StringComparison.Ordinal) ? ["Eye"] : ["IkCharacter"])
                || !scene.Primitives.Any(p => p.Material.Name == name))
                throw new InvalidDataException("Malamar source passes or geometry changed.");
            var scalars = source.Tables(material, 4, 256).ToDictionary(t => source.Text(t, 0)!,
                t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(t => source.Text(t, 0)!, t =>
            {
                var at = source.Field(t, 1);
                return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
            });
            if (vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0) || scalars["EmissionIntensity"] != 0
                || Enumerable.Range(1, 4).Any(i => scalars["LayerMaskScale" + i] != 1))
                throw new InvalidDataException("Malamar source layered controls changed.");
            var texture = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "LayerMaskMap");
            var sampler = source.Tables(material, 3, 32)[checked((int)source.Value(texture, 2))];
            var wrap = new Vector2(source.Value(sampler, 9), source.Value(sampler, 10));
            if (wrap != Vector2.One) throw new InvalidDataException("Malamar source mask addressing changed.");
            Vector3 Rgb(string n) => new(vectors[n].X, vectors[n].Y, vectors[n].Z);
            result.Add(name, new(name, source.Text(texture, 1)!, Enumerable.Range(1, 4).Select(i => Rgb("EmissionColorLayer" + i)).ToArray())
            {
                Transparent = transparent, InnerPass = inner, DynamicSource = true,
                Initial = Enumerable.Range(1, 4).Select(i => scalars["EmissionIntensityLayer" + i]).ToArray(),
                BaseColors = Enumerable.Range(1, 4).Select(i => Rgb("BaseColorLayer" + i)).ToArray(),
                GlobalBaseColor = vectors.GetValueOrDefault("BaseColor", Vector4.One), MaskWrap = wrap,
            });
        }
        return result;
    }
}
