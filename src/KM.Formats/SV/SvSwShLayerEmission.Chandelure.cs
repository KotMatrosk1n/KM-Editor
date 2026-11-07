// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract> ChandelureContracts(byte[] bytes, PreviewScene scene)
    {
        if (System.Convert.ToHexString(SHA256.HashData(bytes)) is not
            ("2CDA6E85ED88A7E1EF015994C09DDBDF44A68B21A709D0543E4C9F4DEF6EDAE0"
            or "BEF33DF56A6083744572D7E055A1976138D568B0CADDE5155E52AD0665B5193F"))
            throw new InvalidDataException("Chandelure emission source identity changed.");
        var source = new ModelBuffer(bytes);
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var name in new[] { "body_a_01", "l_eye", "r_eye" })
        {
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
            var transparent = name == "body_a_01";
            var meshes = scene.Primitives.Where(p => p.Material.Name == name).Select(p => p.Name).Order(StringComparer.Ordinal);
            var expected = transparent ? new[] { "pm0609_51_00_body_a_back_mesh_shape", "pm0609_51_00_body_a_mesh_shape" }
                : ["pm0609_51_00_eye_mesh_shape"];
            if (!meshes.SequenceEqual(expected.Order(StringComparer.Ordinal))
                || !source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(transparent
                    ? ["IkCharacterTransparent", "IkCharacterTransparentInner"] : ["IkCharacter"]))
                throw new InvalidDataException("Chandelure emission geometry or passes changed.");
            var scalars = source.Tables(material, 4, 256).ToDictionary(t => source.Text(t, 0)!,
                t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(t => source.Text(t, 0)!, t =>
            {
                var at = source.Field(t, 1);
                return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
            });
            if (vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0) || scalars["EmissionIntensity"] != 0
                || Enumerable.Range(1, 4).Any(i => scalars["LayerMaskScale" + i] != 1
                    || scalars["EmissionIntensityLayer" + i] != (i == 1 ? .3f : 0)))
                throw new InvalidDataException("Chandelure emission values changed.");
            var texture = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "LayerMaskMap");
            var sampler = source.Tables(material, 3, 32)[checked((int)source.Value(texture, 2))];
            var wrap = new Vector2(source.Value(sampler, 9), source.Value(sampler, 10));
            var uv = source.Tables(material, 9, 256).Single(t => source.Text(t, 0) == "UVIndexLayerMask");
            if (wrap != new Vector2(transparent ? 6 : 1, 1) || source.Value(uv, 1) != uint.MaxValue)
                throw new InvalidDataException("Chandelure emission addressing changed.");
            result.Add(name, new(name, source.Text(texture, 1)!, Enumerable.Range(1, 4)
                .Select(i => new Vector3(vectors["EmissionColorLayer" + i].X, vectors["EmissionColorLayer" + i].Y, vectors["EmissionColorLayer" + i].Z)).ToArray())
                { Transparent = transparent, Initial = [.3f, 0, 0, 0], MaskWrap = wrap,
                    BaseColors = Enumerable.Range(1, 4).Select(i => new Vector3(vectors["BaseColorLayer" + i].X, vectors["BaseColorLayer" + i].Y, vectors["BaseColorLayer" + i].Z)).ToArray() });
        }
        return result;
    }
}
