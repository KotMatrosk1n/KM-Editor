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
