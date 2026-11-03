// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Qualifies Archaludon's authored UV0 tangent frames and unmodified BC5 normal chains.</summary>
public static class SvSwShArchaludonNormals
{
    public const string ProgramName = "AuthoredTangentNormal";
    public static bool IsBody(string name) => name is "body_a_00" or "body_a_01" or "body_b_00" or "body_b_01";
    private static readonly IReadOnlyDictionary<(string Mesh, string Material), string> Geometry = new Dictionary<(string, string), string>
    {
        [("pm1132_00_00_head_mesh_shape", "body_a_01")] = "5833A5FBDB30621D0369082C89DAC32E50DA6FB81C730304089E2BE2D615F360",
        [("pm1132_00_00_head_mesh_shape", "body_a_00")] = "540F4812B77AA27F87A6515AFFC038F80CF5737B9255C3666FA043493DBE3C81",
        [("pm1132_00_00_helmet_mesh_shape", "body_a_01")] = "86BB49E763294022623B66533A5548D283BD8043AE45C94167117979ED39AFF1",
        [("pm1132_00_00_teeth_mesh_shape", "body_a_00")] = "934EA0993C6D3CB8BD8F9A7A4E224876F1F0C71D61C3F2E0D91F1996C8AC5334",
        [("pm1132_00_00_body_a_mesh_shape", "body_a_01")] = "BC1CD04E038EA25B4F88DB1BC8D2EFBE8A2E894EBFA6EC889CF721A02A4DBE26",
        [("pm1132_00_00_body_a_mesh_shape", "body_b_01")] = "21095F7579626FC0DE712B697F3FB0793C548E9DC78E1E8FBB4795C6C87561C3",
        [("pm1132_00_00_body_a_mesh_shape", "body_a_00")] = "EC9EDAC95A1D4C8C888EAC9C7519A1078B9A7C3FCBE0625253342FEC081D38D0",
        [("pm1132_00_00_body_b_mesh_shape", "body_b_01")] = "454C5AB047177BF247823039C20B28A163E0FE9B4707A9E4FD9F68800E247589",
        [("pm1132_00_00_body_b_mesh_shape", "body_b_00")] = "6C3F4F7B5E6B198385E21CBB65F8C1E7036201E5B0A4961395AFC4F867674323",
        [("pm1132_00_00_neck_mesh_shape", "body_a_00")] = "71D1E7E2B82AAE39B2A72BA79219F03811AD106953CECF23D45518B9CE96F942",
        [("pm1132_00_00_left_leg_mesh_shape", "body_a_01")] = "7F2EE1F1643BC7ABE37EF91E33199D9AAF01F96D6A3622F815F40A1D0F2DAAF8",
        [("pm1132_00_00_left_leg_mesh_shape", "body_b_01")] = "8B44F8089FC06A6A98B9ED22465C029F878D5595402E794519AC5661FDB249CD",
        [("pm1132_00_00_left_leg_mesh_shape", "body_a_00")] = "51A2C73FAD010649066FEF3289C73E6521CC0E45246A55E0030843B22D54B814",
        [("pm1132_00_00_right_leg_mesh_shape", "body_a_01")] = "698B02C9F2669FDCDD866622671C7F3B6CF55C81F331FF37F89E713C9DC71F2B",
        [("pm1132_00_00_right_leg_mesh_shape", "body_b_01")] = "4916CC25E0D89897881D6918E2F2E58423994B4448B8D4AF081E026731C7CE93",
        [("pm1132_00_00_right_leg_mesh_shape", "body_a_00")] = "C9C4B458A0CFDBA0E0801416040206F6191D96745CC493F175C033AEBD6C768E",
        [("pm1132_00_00_left_hand_mesh_shape", "body_b_01")] = "187380CDED583C5F96ABAF2FCF98E3E9CFD02D2BE7EC0B259B4B9AFDE1439B9C",
        [("pm1132_00_00_left_hand_mesh_shape", "body_b_00")] = "DE82FABD5AEA2C02196D7E2CAECAFD645F1F42A0B4AD97F3F4CCA9ACC8D07CDF",
        [("pm1132_00_00_right_hand_mesh_shape", "body_b_01")] = "B2F7401D9325BBF5150A75FC9D4689CEFDC9D5D3E497BE5515B4E2F508F19811",
        [("pm1132_00_00_right_hand_mesh_shape", "body_b_00")] = "E1920ABA504477151CBC037B3B4B965AAAAAFF4866B8CD6A844D8A68E317D75C",
    };
    private static readonly IReadOnlyDictionary<string, string> NormalHashes = new Dictionary<string, string>
    {
        ["body_a_01"] = "5E0721B912FA33A30E53FE42E44ADD8739134213F1F70B8B9DD56E70BC4BB0C8",
        ["body_a_00"] = "B87F48AF67D8ECFD6E87873F750FBD5EA692F5CA42F9D98AE8DFA850D0713A4E",
        ["body_b_01"] = "34E88FDF0CE1AE1544D0841BBCEAF77D6247B5A82BACC78E6972BE466EB29F48",
        ["body_b_00"] = "2572B7CCE12147B5E49BE288FDF4353E5D08228F2FD22ABDC7ABB477E93D38F7",
    };
    public static void RequireMaterial(byte[] bytes, string material)
    {
        if (!IsBody(material) || Convert.ToHexString(SHA256.HashData(bytes)) is not
            ("CDD90CC9C30A3F91A8FF76A57FA5014C5BA4F2DBEED841C856E94E4E6253AE25" or "E0A18FEFEFA324924CDE8776811E21ED82C3075CAF81CEB05C2217AA6005702E"))
            throw new InvalidDataException("Authored tangent normals require the qualified Archaludon palette.");
    }
    public static void RequireSource(byte[] bytes, PreviewScene scene)
    {
        var bodies = scene.Primitives.Where(p => IsBody(p.Material.Name)).ToArray();
        if (bodies.Length != Geometry.Count) throw new InvalidDataException("Authored tangent body sections changed.");
        foreach (var primitive in bodies)
        {
            RequireMaterial(bytes, primitive.Material.Name);
            if (!Geometry.TryGetValue((primitive.Name, primitive.Material.Name), out var expected)
                || primitive.Material.Alpha != "Opaque" || primitive.Tangent0 is not { } tangent
                || tangent.Length != primitive.Vertices.Length / 16 * 4)
                throw new InvalidDataException("Authored tangent body geometry is unsupported.");
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(primitive.Indices.Length);
            foreach (var index in primitive.Indices)
            {
                for (var component = 0; component < 16; component++) writer.Write(primitive.Vertices[index * 16 + component]);
                for (var component = 0; component < 4; component++) writer.Write(tangent[index * 4 + component]);
            }
            if (Convert.ToHexString(SHA256.HashData(stream.ToArray())) != expected)
                throw new InvalidDataException("Authored indexed tangent frame changed.");
        }
    }
    public static byte[] Texture(byte[] materials, string material, Func<string, byte[]> read)
    {
        RequireMaterial(materials, material);
        var path = "pm1132_00_00_" + material + "_nrm.bntx"; var bytes = read(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != NormalHashes[material])
            throw new InvalidDataException("Authored tangent normal texture changed.");
        var preview = PreviewTexture.Read(bytes);
        var info = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(
            checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(40, 8))), 8)));
        if (preview.Format != 0x1e01 || preview.Width != 1024 || preview.Height != 1024 || preview.Mips.Length != 10
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(info + 88, 4)) != 0x05040302)
            throw new InvalidDataException("Authored tangent normal storage changed.");
        return bytes;
    }
    public static byte[] Bind(byte[] bytes)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var (programs, count) = source.Vector(source.Root, 3, 4, 256);
        var program = Enumerable.Range(0, count).Single(i => source.StringAt(programs + i * 4) == ProgramName);
        var materials = source.Tables(source.Root, 6, 128).Where(m => source.Value(m, 8) == program).ToArray();
        if (materials.Length != Geometry.Count) throw new InvalidDataException("Authored tangent native consumers changed.");
        foreach (var material in materials)
        {
            var texture = source.Tables(material, 11, 64).Single(t => source.Text(t, 0) == "NormalMapTex");
            var sampler = source.Table(texture, 2);
            var values = Enumerable.Range(0, 9).ToDictionary(f => f, f => f is 1 or 2
                ? ModelTableBuilder.Integer(f == 1 ? 2 : 0)
                : source.Field(sampler, f) is var at && at != 0 ? source.Slice(at, 4).ToArray() : new byte[4]);
            writer.Reference(texture, 2, writer.Table(9, values));
        }
        return writer.Finish(source.Root);
    }
}
