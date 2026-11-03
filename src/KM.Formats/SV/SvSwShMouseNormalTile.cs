// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.SV;

internal static class SvSwShMouseNormalTile
{
    internal static void Require(byte[] bytes, PreviewPrimitive primitive, Vector2 scale)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var source = hash switch
        {
            "ED1F984C69F7E2A64767B4498BFD3A95944CB0B30EA75843ED72A573F6658947" or "D62513D1BE95AF337B8E12256BED50E820835F2812FBF25D85E55F82FD7A532A" => "tandemaus",
            "FD925983D964FFC396CB3E998A8B9CDBC8EE24D624B4F44CED0873425A3123E2" or "E51743DB261AF730A477A17AC414F5E1EC6726418FE1D8163E77CC04399B6057" => "maushold",
            "3D5606DC181F55B06385E6B2203267794767994E4A30982E48CD91FFA7534241" or "2ECB7C053AFEAEF576B1FF4879AC9459B062AF7B5619934D40E3E570891EC5CF" => "maushold-family4",
            _ => throw new InvalidDataException("Mouse normal tile requires its qualified source palettes."),
        };
        var expected = (source, primitive.Name, primitive.Material.Name) switch
        {
            ("tandemaus", "pm1049_00_00_body_a_01_mesh_shape", "body_a_03") => (738, 732),
            ("tandemaus", "pm1049_00_00_body_a_02_mesh_shape", "body_a_03") => (860, 828),
            ("tandemaus", "pm1049_00_00_body_b_01_mesh_shape", "body_b_03") => (738, 732),
            ("tandemaus", "pm1049_00_00_body_b_02_mesh_shape", "body_b_03") => (860, 768),
            ("maushold", "pm1050_11_00_body_a_01_mesh_shape", "body_a_03") => (738, 732),
            ("maushold", "pm1050_11_00_body_a_02_mesh_shape", "body_a_03") => (860, 828),
            ("maushold", "pm1050_11_00_body_b_01_mesh_shape", "body_a_03") => (738, 60),
            ("maushold", "pm1050_11_00_body_b_02_mesh_shape", "body_a_03") => (860, 60),
            ("maushold", "pm1050_11_00_body_b_01_mesh_shape", "body_b_03") => (738, 672),
            ("maushold", "pm1050_11_00_body_b_02_mesh_shape", "body_b_03") => (860, 768),
            ("maushold", "pm1050_11_00_body_c_01_mesh_shape", "body_c_03") => (464, 618),
            ("maushold", "pm1050_11_00_body_c_02_mesh_shape", "body_c_03") => (563, 678),
            ("maushold-family4", "pm1050_11_00_body_a_01_mesh_shape", "body_a_03") => (738, 732),
            ("maushold-family4", "pm1050_11_00_body_a_02_mesh_shape", "body_a_03") => (860, 828),
            ("maushold-family4", "pm1050_11_00_body_b_01_mesh_shape", "body_a_03") => (738, 60),
            ("maushold-family4", "pm1050_11_00_body_b_02_mesh_shape", "body_a_03") => (860, 60),
            ("maushold-family4", "pm1050_11_00_body_b_01_mesh_shape", "body_b_03") => (738, 672),
            ("maushold-family4", "pm1050_11_00_body_b_02_mesh_shape", "body_b_03") => (860, 768),
            ("maushold-family4", "pm1050_11_00_body_c_01_mesh_shape", "body_c_03") => (464, 618),
            ("maushold-family4", "pm1050_11_00_body_c_02_mesh_shape", "body_c_03") => (563, 678),
            ("maushold-family4", "pm1050_12_00_body_d_01_mesh_shape", "body_c_03") => (464, 618),
            ("maushold-family4", "pm1050_12_00_body_d_02_mesh_shape", "body_c_03") => (563, 678),
            _ => throw new InvalidDataException("Mouse normal tile geometry is not qualified."),
        };
        if (scale != new Vector2(2, 4) || primitive.Material.Alpha != "Opaque"
            || primitive.Vertices.Length != expected.Item1 * 16 || primitive.Indices.Length != expected.Item2)
            throw new InvalidDataException("Mouse normal tile allocation changed.");
    }
}
