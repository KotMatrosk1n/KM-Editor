// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using KM.Formats.Models;

namespace KM.Formats.ZA;

/// <summary>A qualified shared object-normal field for disjoint source mesh sections.</summary>
public sealed class ZaSwShNormalAtlas
{
    private readonly PreviewPrimitive geometry;
    private readonly string materialHash;
    private readonly HashSet<string> sections;
    private readonly HashSet<string> materialNames;

    private ZaSwShNormalAtlas(PreviewPrimitive geometry, string materialHash, HashSet<string> sections, HashSet<string> materialNames)
    { this.geometry = geometry; this.materialHash = materialHash; this.sections = sections; this.materialNames = materialNames; }

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Golurk(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Golurk",
            "C57228BE959EC3CF684D9EDCF331FA85C6B971F19A7C75F2A5F185E6740E7C36",
            "ADCE309A10A78535ACA46C725AFC8D13F768FC45F2A5ADFE770F7940DABA720F",
            "8FDFBA844DF6CE5E077DECEE85E9B2721ED9A94173C0D7573240453B4C70F191");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Chesnaught(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Chesnaught",
            "0DB5D4BF73FE097E1A13270ED2586EB573143395CC4B65F3F272823D65D4AE75",
            "25F60304710C512069FE4165A135D80DABA5589DAD5EBAE6066F99BB9EB5A109",
            "EB96AD8DF63CDF1470F08A865778024DE968D723A44D8B37C533B000CC0A0C5B");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Staraptor(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Staraptor",
            "4053C83B5CB2E0F0D5176E463B17D4D6E970656A06FD58A47902EFFE0304929C",
            "02EC5C4BBBF86BF73FBB74264353BB4349D40383F9E3020D794FF0DCF1C6C47E",
            "4D4C6F3743333EAB38C3179B4894DB549CF0A6D4A75ACA0E95B317EC8DF6C33D");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Pidgeot(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Pidgeot",
            "635E22ACA011F2CA55EDCF2CEEED2C687D605A48CE703BD8D61452D039002709",
            "3E0E8E6E58F4D3F699B8F83861A2EE37B7492C94DD21010980DD3033D10EC391",
            "98824E1FB9AA1D6728B56468FBE26CE66B546D17F8045095C5A9BD3C296514C4");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Golisopod(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Golisopod",
            "9FAC1909B884A108B99DFE469ABAA6AFD051EFCF7D411BD5441BCAD4D780B9A4",
            "374193D9150242671C20BC4C58CCD0A0A12500F2F6816133056E37D37BD38BEE",
            "BB86B7A6E3CCA5C59BD406B29B452E10EFB30A69301BA304DCA0B5907146E434");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Drampa(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Drampa",
            "F0CD056CF88F24D3A838086951F646B14E71166D017F0D12A78EB995A6E2D2F4",
            "C4C6731FA8A9AD1080B2FCC3B30798B201210003867AFD1C37AC7A910A8B828F",
            "706FD04A02996374B1354D161369E0FA4144E518E9A3F4137CE3A03D3806F917",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["body_a_00"] = "body_a", ["body_a_01"] = "body_a",
                ["body_b_00"] = "body_b", ["body_b_01"] = "body_b",
                ["l_eye"] = "l_eye", ["r_eye"] = "r_eye",
            });

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> LucarioZ(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Lucario Z",
            "29376F2880E83314EEF0D417876D5541778E1ACD929D8A253C316238BBEA1425",
            "14E2CBB767F820E5235A87A5528CEAC9D217AFE6C7B09E00F772D45659AA188F",
            "02B5D388B0C10D2E36C7D7DAAE757FAF8E723841B2B9DCA8B4A8118FB0859E93");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Scolipede(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Scolipede",
            "489AAAF984BC9922057A45B602F4F4F6CC9F43797226EBEAC6D23B43A0092F4A",
            "0F58FDDDFA9EE7D96905F9A582E5A39212F478ED83E426B7771771D383302DF4",
            "1AE82F6185B7714320D6F4A495AF22D942E4D8250DD53B39A1C86694663B10D2");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Watchog(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Watchog",
            "11F8B303568D942BEF92EA3725DBD6E03484BD17A183808F1F1216A853DF14DC",
            "B6F21D5E2FDB170B4E46A722B24323CCDFEB7E79D182CE0CD53DF73514B8F894",
            "BC0316B03CD7F7FF22978D0E58ACC4C4B893A3B664AFD890C6D233856682E844",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["body_00"] = "body", ["body_01"] = "body",
                ["body_02"] = "body", ["body_03"] = "body",
                ["l_eye"] = "l_eye", ["r_eye"] = "r_eye",
            });

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> RaichuY(PreviewScene scene, byte[] materials)
    {
        var atlases = Qualified(scene, materials, "Raichu Y",
            "11CB578E2494066ACFF1A9EE55D1A84A53C873508B757A855A4BFE10002C8025",
            "8AB5028F68EB211F9782FA8BD887638EF8B6FC82C886162DFD252BAF8847B66F",
            "618BCA4B8306FE6ECEA80C560E2E9DE4BD6F2A69687D5BCC9FB2AFE6FB70C07C",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["body_a_00"] = "body_a", ["body_a_01"] = "body_a", ["body_a_02"] = "body_a",
                ["body_b_00"] = "body_b", ["body_b_01"] = "body_b",
                ["l_eye"] = "l_eye", ["r_eye"] = "r_eye",
            });
        // The body_a_00 sections overlap at one covered texel. Keep their
        // individual normal bakes; the remaining consumers match this union exactly.
        return atlases.Where(pair => pair.Key != "body_a_00")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Barbaracle(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Barbaracle",
            "F08D327BF28C30D92900547ECC28D093321EA42DFD4803BDBB07AEACD065B591",
            "ACCE375DC916C25000A2060FF54158B2C0EEE53F431AA1C7CC3B966F90618E4C",
            "810AB911BF85FFB23654447E512EB6D70B75078BD36AEFCA945D471560FA99A3");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Crabominable(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Crabominable",
            "2FDD263FD37F6D4AC2204BCF2D4EEBBD1B44F600A7D66DABE1A193C5D5701049",
            "6C93EC8560BC21E126891628706EE644B92D7284584765A0EC743F8484308A72",
            "7E6FF07D66C98CF40F9A3EA68278AB57DBE81B1FCEAAB3BE1B32EC0A1BC7FAEF");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Magearna(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Magearna",
            "C3DFDAA6655598E4DBCDA26C8C5133EA6157DFEE8500E23F39B7353BF78D6EF6",
            "862FBACBE6B8568D3C88F23A0023B3B0F72446DC06DAE909F2E1455999DD99A8",
            "9AB2C0D64C2B1F68830B01D252DC6AF58BD1986540B0805C30C4B103B8154233");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> MagearnaOriginal(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "MagearnaOriginal",
            "4A92294514296448403FE82BC84CF00AB528E83B7AED39F20546989602BB47BE",
            "42293B45B6FDA16CD1ECF25FE63EA5E99ECDFF176FC822E1DC484F6649607341",
            "9AB2C0D64C2B1F68830B01D252DC6AF58BD1986540B0805C30C4B103B8154233");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Pyroar(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Pyroar",
            "F0FAA5497CE602B67E830BB557125DA4F4EB11FF27CB4CA1A7FD47AA98AD5464",
            "88E001810589C93CCC0E56512077AD19DAD6AF6E0E281AEC2454824E2B56494A",
            "7847BF7E0A399E659A3E7FE00DCA7033D903003CAD76B3995E4607A96891A0E5");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Darkrai(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Darkrai",
            "A5C04F39076F31F8EAA3851FEF6A9958E72F33A621E9DB09D231DC0F9AF80025",
            "3D995455BCB8161AFC5C675B8654C2AAA00B17B755163EA7DA01181E52135CAB",
            "F95E9594267743DDB1796BE9F885011E4BA4F50B99464796C40C2C900FA4BAB4");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Glalie(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Glalie",
            "1F8BAD8FA650AFEA95F5BC8955C0D39316C4195973C1C22490EAC53799358D54",
            "14679C9EEBACA146550BAD1138CB30BB01F1197FD9015E072EE2719951C538E1",
            "7E8A703CC2D7F12178B9D82341507E51151A2D8BF82C605F08BF5B3DF10CB1E5");

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Excadrill(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Excadrill",
            "2E9161C91B79D7AA498C2E5C112C34EAADDB5C8BAC0DA7865E252BBA25E9C245",
            "3447357AB683557C8EF2EB95E748762980D5B3BECBEF5BCB212EBDB789E775B9",
            "AE4E8A5D4B8F49C0D6599246B49AADDF438E241A5B617A085C8FD5600E9F0724",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["body_b"] = "body_b", ["body_a_00"] = "body_a", ["body_a_01"] = "body_a",
                ["body_a_02"] = "body_a", ["body_a_03"] = "body_a", ["l_eye"] = "l_eye", ["r_eye"] = "r_eye",
            });

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Zeraora(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Zeraora",
            "4F5BFBBA8769FDEA7C6BA07C5FF156C4723C8B9093C0BDFC1F23895454361E5D",
            "B920921085867A9F24161C49275ADAE01522CC4313DDCED45E8EE61855CCE971",
            "B8F4915A1A5048572172B8DBBF625C98EFD1C22A421DDEC10ACB92E4E85DE016",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["body_b_00"] = "body_b", ["body_b_01"] = "body_b", ["body_b_02"] = "body_b",
                ["body_a_00"] = "body_a", ["body_a_01"] = "body_a", ["body_a_02"] = "body_a",
                ["l_eye"] = "l_eye", ["r_eye"] = "r_eye",
            }).Where(pair => pair.Key.StartsWith("body_", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Zygarde(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Zygarde",
            "540500834158C3F1C519FAF798360BDED5BA2A6BDD70B4364FFAD954D96830C5",
            "77A14CA5B9B8BD6739CDD205E7C8A8B59F9D1B9EF40D0FFBDEAC41E9A5594360",
            "BB979DD662B1A072892B47CF33762BA6B9823D43F79AE7ED2D0AD18F2B34A4D5")
            // The mirrored armor and energy sections overlap with different
            // normals. Keep their independent fields; only disjoint unions share.
            .Where(pair => pair.Key is not ("body_a_01" or "body_e"))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, ZaSwShNormalAtlas> Qualified(PreviewScene scene, byte[] materials,
        string species, string normalHash, string rareHash, string geometryHash,
        IReadOnlyDictionary<string, string>? normalGroups = null)
    {
        var hash = Hash(materials);
        if (hash != normalHash && hash != rareHash || GeometryHash(scene.Primitives) != geometryHash)
            throw new InvalidDataException($"Shared {species} normal atlases require the qualified donor materials and geometry.");
        if (normalGroups is not null && !normalGroups.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(scene.Primitives.Select(p => p.Material.Name)))
            throw new InvalidDataException("Shared normal group material ownership differs from its qualification.");
        var result = new Dictionary<string, ZaSwShNormalAtlas>(StringComparer.Ordinal);
        foreach (var group in scene.Primitives.GroupBy(p => normalGroups?[p.Material.Name] ?? p.Material.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            // Only the bake geometry is combined. Native mesh sections, rigging,
            // visibility groups and material animation identities remain separate.
            var primitives = group.ToArray();
            var indices = new List<uint>(); var vertexBase = 0;
            foreach (var primitive in primitives)
            {
                indices.AddRange(primitive.Indices.Select(index => checked(index + (uint)vertexBase)));
                vertexBase = checked(vertexBase + primitive.Vertices.Length / 16);
            }
            var merged = primitives[0] with
            {
                Vertices = primitives.SelectMany(p => p.Vertices).ToArray(), Indices = indices.ToArray(), SecondaryUv = null,
            };
            var names = primitives.Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal);
            if (names.Count > 1)
            {
                // These qualified sections have different color layers but the same
                // authored tangent normal image and UV transform. Preserve those
                // color identities while sharing only their object-normal field.
                var source = new ModelBuffer(materials);
                var normalPaths = source.Tables(source.Root, 1, 128).Where(t => names.Contains(source.Text(t, 0)!))
                    .Select(t => source.Text(source.Tables(t, 2, 64).Single(p => source.Text(p, 0) == "NormalMap"), 1)).ToArray();
                if (normalPaths.Length != names.Count || normalPaths.Distinct(StringComparer.Ordinal).Count() != 1)
                    throw new InvalidDataException("Shared normal material groups require the same authored normal image.");
            }
            var atlas = new ZaSwShNormalAtlas(merged, hash, primitives.Select(p => GeometryHash([p])).ToHashSet(StringComparer.Ordinal), names);
            foreach (var name in names) result.Add(name, atlas);
        }
        return result;
    }

    internal PreviewPrimitive Geometry(PreviewPrimitive primitive, byte[] materials)
    {
        if (Hash(materials) != materialHash || !materialNames.Contains(primitive.Material.Name)
            || !sections.Contains(GeometryHash([primitive])))
            throw new InvalidDataException("Shared normal atlas does not own this donor section.");
        return geometry;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string GeometryHash(IReadOnlyList<PreviewPrimitive> primitives)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
        {
            writer.Write(primitives.Count);
            foreach (var primitive in primitives)
            {
                writer.Write(primitive.Name); writer.Write(primitive.Material.Name);
                writer.Write(primitive.Vertices.Length); foreach (var value in primitive.Vertices) writer.Write(value);
                writer.Write(primitive.Indices.Length); foreach (var index in primitive.Indices) writer.Write(index);
            }
        }
        return Hash(buffer.ToArray());
    }
}
