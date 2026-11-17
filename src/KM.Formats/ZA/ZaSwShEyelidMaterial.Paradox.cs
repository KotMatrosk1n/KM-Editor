// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.ZA;

public static partial class ZaSwShEyelidMaterial
{
    // Exact donor palettes retain the same four-layer eye emission equation.
    // Geometry, UV coverage, texture roles, samplers and animated values still
    // pass the ordinary eye contract below.
    private static bool ParadoxEmissionEye(byte[] bytes, PreviewPrimitive primitive, string name)
    {
        var hash = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        return (hash, primitive.Name, name, primitive.Vertices.Length / 16, primitive.Indices.Length) switch
        {
            // Iron Thorns uses a single Eye surface per side, with its green
            // iris in the first emission mask layer and no clear coat.
            ("C4F1227465DE9F66C706C7782764B0A98390C83ADBCECF1AF896362EEC7C3E74", "pm1095_00_00_eye_mesh_shape", "l_eye" or "r_eye", 12, 12) => true,
            ("9E054855AB73A915C5777488FAE4C868D01C4F1C24A8E3B709A5848C327DD3D2", "pm1095_00_00_eye_mesh_shape", "l_eye" or "r_eye", 12, 12) => true,
            ("4537A73406F144514B2901A9D19B3746B3208E05D95E5FC907DD2DF841B674B9", "pm1087_00_00_jewel_mesh_shape", "body_b", 480, 2280) => true,
            ("674211FB0CCAF721B950EDAC27F59F1A3E737288AEB0359B1B6DC9C7F2C1AA36", "pm1087_00_00_jewel_mesh_shape", "body_b", 480, 2280) => true,
            ("4537A73406F144514B2901A9D19B3746B3208E05D95E5FC907DD2DF841B674B9", "pm1087_00_00_eye_mesh_shape", "l_eye", 44, 84) => true,
            ("4537A73406F144514B2901A9D19B3746B3208E05D95E5FC907DD2DF841B674B9", "pm1087_00_00_eye_mesh_shape", "r_eye", 44, 84) => true,
            ("674211FB0CCAF721B950EDAC27F59F1A3E737288AEB0359B1B6DC9C7F2C1AA36", "pm1087_00_00_eye_mesh_shape", "l_eye", 44, 84) => true,
            ("674211FB0CCAF721B950EDAC27F59F1A3E737288AEB0359B1B6DC9C7F2C1AA36", "pm1087_00_00_eye_mesh_shape", "r_eye", 44, 84) => true,
            ("D29A27549086C369CC534BBE620A7E59DFC6796F23BEE68CFF646E3CFBA052DF", "pm1102_11_00_eye_ball_mesh_shape", "eye_l", 216, 540) => true,
            ("D29A27549086C369CC534BBE620A7E59DFC6796F23BEE68CFF646E3CFBA052DF", "pm1102_11_00_eye_ball_mesh_shape", "eye_r", 216, 552) => true,
            ("7A231DBDBBFD28449D57E0EA998B7B957DC01D450360157AF1F1C3FA8FC8175E", "pm1102_11_00_eye_ball_mesh_shape", "eye_l", 216, 540) => true,
            ("7A231DBDBBFD28449D57E0EA998B7B957DC01D450360157AF1F1C3FA8FC8175E", "pm1102_11_00_eye_ball_mesh_shape", "eye_r", 216, 552) => true,
            // Archaludon's layer-four color rotation is baked into the color
            // image; the four constant emission colors use the shared mask UV.
            ("E0A18FEFEFA324924CDE8776811E21ED82C3075CAF81CEB05C2217AA6005702E", "pm1132_00_00_head_mesh_shape", "l_eye", 662, 33) => true,
            ("E0A18FEFEFA324924CDE8776811E21ED82C3075CAF81CEB05C2217AA6005702E", "pm1132_00_00_head_mesh_shape", "r_eye", 662, 33) => true,
            ("CDD90CC9C30A3F91A8FF76A57FA5014C5BA4F2DBEED841C856E94E4E6253AE25", "pm1132_00_00_head_mesh_shape", "l_eye", 662, 33) => true,
            ("CDD90CC9C30A3F91A8FF76A57FA5014C5BA4F2DBEED841C856E94E4E6253AE25", "pm1132_00_00_head_mesh_shape", "r_eye", 662, 33) => true,
            ("53D369341FA127435CF428AF84FA739822F3196F05280F1FB200D5965CCFDC25", "pm1099_00_00_eye_mesh_shape", "l_eye", 48, 114) => true,
            ("53D369341FA127435CF428AF84FA739822F3196F05280F1FB200D5965CCFDC25", "pm1099_00_00_eye_mesh_shape", "r_eye", 48, 114) => true,
            ("CE390CA5752FD886397939A2F28F41A6BF9254CCB8EB20A2F1AFC3B5CC65B51B", "pm1099_00_00_eye_mesh_shape", "l_eye", 48, 114) => true,
            ("CE390CA5752FD886397939A2F28F41A6BF9254CCB8EB20A2F1AFC3B5CC65B51B", "pm1099_00_00_eye_mesh_shape", "r_eye", 48, 114) => true,
            ("E2A2EB8B41D8A380B77126BAD280E3DE0F18EE39DB8CB8FD4DD8F642A052E9ED", "pm1101_00_00_eyeball_mesh_shape", "l_eye", 382, 960) => true,
            ("E2A2EB8B41D8A380B77126BAD280E3DE0F18EE39DB8CB8FD4DD8F642A052E9ED", "pm1101_00_00_eyeball_mesh_shape", "r_eye", 382, 960) => true,
            ("26B38129C86B7844A836D8339A3D7EAB8E8B13F2FA7F9073EFFB4197D2F41115", "pm1101_00_00_eyeball_mesh_shape", "l_eye", 382, 960) => true,
            ("26B38129C86B7844A836D8339A3D7EAB8E8B13F2FA7F9073EFFB4197D2F41115", "pm1101_00_00_eyeball_mesh_shape", "r_eye", 382, 960) => true,
            _ => false,
        };
    }
}
