// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    internal static IReadOnlyDictionary<string, Contract>? ParadoxTransparentContracts(byte[] bytes, PreviewScene scene)
    {
        var hash = System.Convert.ToHexString(SHA256.HashData(bytes));
        var jugulis = hash is "19685989CA24011D77BEBFEC16C0C83F389E9528F2912E1CCD1076EF5FAF5E0F" or "FF4C31C9A9409E21894687FBE02ECB0538359BB091583676F72BC313B2C3955A";
        var chien = hash is "53D369341FA127435CF428AF84FA739822F3196F05280F1FB200D5965CCFDC25" or "CE390CA5752FD886397939A2F28F41A6BF9254CCB8EB20A2F1AFC3B5CC65B51B";
        var treads = hash is "837188B00DB789B09CDA7F924F31CBE65D2A36573F7F146DEBE591C89034528A" or "25B468494D71CFDE51AE401E58E1257B5A95206CFFA81ECE9C0C1A60C93D399B";
        var bundle = hash is "A356E22899CF3D239010843616786B7B8699216AFC9ECD68B196D1852197AE07" or "81DA841B5557F8FB48CD9A9117A144E9E254BE3A523661164149CBE1AC3596B1";
        var hands = hash is "74E8E0E4A5A50CD835CA070669EE8C21D241A0BBA19617DA53C64268F069AB27" or "1F62B2E5B83FAF869F456A76574217DFDA62FE717CD7727938272B088FE43C79";
        var moth = hash is "92BB7C6B8E2CA86BCA8DA937A456168636A4C1B2575F616D229FA72B40FA4448" or "56CF38BFB73E4904BE7FE631600BB407B2860EFD9BC407AFA4D1CD6CB7F33C69";
        if (!jugulis && !chien && !treads && !bundle && !hands && !moth) return null;
        var stem = jugulis ? "pm1094_00_00" : treads ? "pm1090_00_00" : bundle ? "pm1096_00_00" : hands ? "pm1093_00_00" : moth ? "pm1092_00_00" : "pm1099_00_00";
        var name = jugulis ? "body_a_02" : treads ? "body_e" : bundle ? "body_03" : hands ? "body_b_01" : "body_c";
        var expected = jugulis
            ? new[] { ("glass", 161, 816), ("left_arm_glass", 113, 492), ("right_arm_glass", 113, 492) }
            : treads ? new[] { ("glass", 63, 276) }
            : bundle ? new[] { ("glass", 46, 144) }
            : hands ? new[] { ("glass", 57, 270) }
            : moth ? new[] { ("glass", 210, 924) }
            : new[] { ("feeler_a_ice", 135, 582), ("feeler_b_ice", 154, 684) };
        var parts = scene.Primitives.Where(p => p.Material.Name == name).ToArray();
        if (parts.Length != expected.Length || expected.Any(e => !parts.Any(p => p.Name == stem + "_" + e.Item1 + "_mesh_shape"
                && p.Vertices.Length == e.Item2 * 16 && p.Indices.Length == e.Item3 && p.Material.Alpha == "BlendPreMultiAlpha")))
            throw new InvalidDataException("Paradox transparent geometry changed.");
        var b = new ModelBuffer(bytes);
        var material = b.Tables(b.Root, 1, 128).Single(m => b.Text(m, 0) == name);
        if (!b.Tables(material, 1, 16).Select(s => b.Text(s, 0)).SequenceEqual(["Transparent", "TransparentInner"]))
            throw new InvalidDataException("Paradox transparent surface requires its two source passes.");
        var scalars = b.Tables(material, 4, 256).ToDictionary(p => b.Text(p, 0)!, p => b.Field(p, 1) is var at && at != 0 ? b.Float(at) : 0);
        var vectors = b.Tables(material, 7, 256).ToDictionary(p => b.Text(p, 0)!, p =>
        {
            var at = b.Field(p, 1); return new Vector4(b.Float(at), b.Float(at + 4), b.Float(at + 8), b.Float(at + 12));
        });
        var maps = b.Tables(material, 2, 64).ToDictionary(p => b.Text(p, 0)!, p => b.Text(p, 1)!);
        if (scalars["EmissionIntensity"] != 0 || scalars["NormalHeight"] != 1
            || Enumerable.Range(1, 4).Any(i => scalars["LayerMaskScale" + i] != 1)
            || vectors["UVScaleOffset"] != new Vector4(hands ? 2 : 1, 1, 0, 0) || vectors["UVScaleOffsetNormal"] != new Vector4(1, 1, 0, 0))
            throw new InvalidDataException("Paradox transparent controls changed.");
        Vector3 Rgb(Vector4 v) => new(v.X, v.Y, v.Z);
        var contract = new Contract(name, maps["LayerMaskMap"], Enumerable.Range(1, 4).Select(i => Rgb(vectors["EmissionColorLayer" + i])).ToArray())
        {
            Transparent = true, DynamicSource = true,
            SourceOpacity = new(scalars["FresnelAlphaMin"], scalars["FresnelAlphaMax"], scalars["FresnelAngleBias"]),
            BaseColors = Enumerable.Range(1, 4).Select(i => Rgb(vectors["BaseColorLayer" + i])).ToArray(),
            Initial = Enumerable.Range(1, 4).Select(i => scalars["EmissionIntensityLayer" + i]).ToArray(),
            GlobalBaseColor = vectors["BaseColor"], SourceBaseAlphas = new(vectors["BaseColorLayer1"].W, vectors["BaseColorLayer2"].W,
                vectors["BaseColorLayer3"].W, vectors["BaseColorLayer4"].W), PreservesTextureAlpha = true,
            ColorUvOrigin = Vector2.Zero, MaskWrap = Vector2.One, MaskUv = vectors["UVScaleOffset"],
        };
        return new Dictionary<string, Contract>(StringComparer.Ordinal) { [name] = contract };
    }

    internal static bool PreservesParadoxTransparentColor(byte[] bytes, PreviewPrimitive primitive)
    {
        if (primitive.Material.Alpha != "BlendPreMultiAlpha") return false;
        var hash = System.Convert.ToHexString(SHA256.HashData(bytes));
        return (hash, primitive.Material.Name, primitive.Name, primitive.Vertices.Length / 16, primitive.Indices.Length) switch
        {
            ("92BB7C6B8E2CA86BCA8DA937A456168636A4C1B2575F616D229FA72B40FA4448" or "56CF38BFB73E4904BE7FE631600BB407B2860EFD9BC407AFA4D1CD6CB7F33C69",
                "body_c", "pm1092_00_00_glass_mesh_shape", 210, 924) => true,
            ("74E8E0E4A5A50CD835CA070669EE8C21D241A0BBA19617DA53C64268F069AB27" or "1F62B2E5B83FAF869F456A76574217DFDA62FE717CD7727938272B088FE43C79",
                "body_b_01", "pm1093_00_00_glass_mesh_shape", 57, 270) => true,
            ("A356E22899CF3D239010843616786B7B8699216AFC9ECD68B196D1852197AE07" or "81DA841B5557F8FB48CD9A9117A144E9E254BE3A523661164149CBE1AC3596B1",
                "body_03", "pm1096_00_00_glass_mesh_shape", 46, 144) => true,
            ("837188B00DB789B09CDA7F924F31CBE65D2A36573F7F146DEBE591C89034528A" or "25B468494D71CFDE51AE401E58E1257B5A95206CFFA81ECE9C0C1A60C93D399B",
                "body_e", "pm1090_00_00_glass_mesh_shape", 63, 276) => true,
            ("19685989CA24011D77BEBFEC16C0C83F389E9528F2912E1CCD1076EF5FAF5E0F" or "FF4C31C9A9409E21894687FBE02ECB0538359BB091583676F72BC313B2C3955A",
                "body_a_02", "pm1094_00_00_glass_mesh_shape", 161, 816) => true,
            ("19685989CA24011D77BEBFEC16C0C83F389E9528F2912E1CCD1076EF5FAF5E0F" or "FF4C31C9A9409E21894687FBE02ECB0538359BB091583676F72BC313B2C3955A",
                "body_a_02", "pm1094_00_00_left_arm_glass_mesh_shape" or "pm1094_00_00_right_arm_glass_mesh_shape", 113, 492) => true,
            ("53D369341FA127435CF428AF84FA739822F3196F05280F1FB200D5965CCFDC25" or "CE390CA5752FD886397939A2F28F41A6BF9254CCB8EB20A2F1AFC3B5CC65B51B",
                "body_c", "pm1099_00_00_feeler_a_ice_mesh_shape", 135, 582) => true,
            ("53D369341FA127435CF428AF84FA739822F3196F05280F1FB200D5965CCFDC25" or "CE390CA5752FD886397939A2F28F41A6BF9254CCB8EB20A2F1AFC3B5CC65B51B",
                "body_c", "pm1099_00_00_feeler_a_ice_mesh_shape", 289, 1266) => true,
            ("53D369341FA127435CF428AF84FA739822F3196F05280F1FB200D5965CCFDC25" or "CE390CA5752FD886397939A2F28F41A6BF9254CCB8EB20A2F1AFC3B5CC65B51B",
                "body_c", "pm1099_00_00_feeler_b_ice_mesh_shape", 154, 684) => true,
            _ => false,
        };
    }
}
