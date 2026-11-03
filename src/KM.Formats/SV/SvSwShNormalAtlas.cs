// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>A qualified normal-only bake geometry shared by authored source material sections.</summary>
public sealed class SvSwShNormalAtlas
{
    private readonly PreviewPrimitive geometry;
    private readonly string materialHash;
    private readonly HashSet<string> sections;

    private SvSwShNormalAtlas(PreviewPrimitive geometry, string materialHash, HashSet<string> sections)
    { this.geometry = geometry; this.materialHash = materialHash; this.sections = sections; }

    public static IReadOnlyDictionary<string, SvSwShNormalAtlas> Espathra(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Espathra",
            "FC5C6FE31CF701BD57D38DC7C6591BBCF5025ED3A653CE5134B937436126E376",
            "BF3BA5F2136FFE7892B89C3FDF73C2045A1C30F148CA6C96DF0C6A4BE1776BC5",
            "9D6876C37A2B655F10DDD14FB6793889280F96B5CE7436CE978B5D473643948B",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["body_a_00"] = "body_a", ["body_a_01"] = "body_a", ["body_a_02"] = "body_a",
                ["body_b"] = "body_b", ["body_b_01"] = "body_b",
            });

    public static IReadOnlyDictionary<string, SvSwShNormalAtlas> Tinkaton(PreviewScene scene, byte[] materials) =>
        Qualified(scene, materials, "Tinkaton",
            "CBA413A176E4092B8CAA37D39DE56E22F68DBA3D3B4FA36FD45918C374B0DA45",
            "3F963EF39C810AF2CBB5BBC8B40C08F4F39C7C5A817C5378D91A8B87F238D261",
            "A83668848C4AB05CE408A7EB0B12177199975C1F9329B234040C1092C1EB0D0B",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["body_a_00"] = "body_a", ["body_a_01"] = "body_a",
                ["body_b_00"] = "body_b", ["body_b_01"] = "body_b",
            });

    private static IReadOnlyDictionary<string, SvSwShNormalAtlas> Qualified(PreviewScene scene, byte[] materials,
        string species, string normalHash, string rareHash, string geometryHash, IReadOnlyDictionary<string, string> normalGroups)
    {
        var hash = Hash(materials);
        if (hash != normalHash && hash != rareHash || GeometryHash(scene.Primitives) != geometryHash)
            throw new InvalidDataException($"Shared {species} normal atlases require the qualified donor materials and geometry.");
        var result = new Dictionary<string, SvSwShNormalAtlas>(StringComparer.Ordinal);
        var source = new ModelBuffer(materials);
        foreach (var group in scene.Primitives.Where(p => normalGroups.ContainsKey(p.Material.Name))
            .GroupBy(p => normalGroups[p.Material.Name], StringComparer.Ordinal))
        {
            var primitives = group.ToArray();
            var names = primitives.Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal);
            var materialTables = source.Tables(source.Root, 1, 128).Where(t => names.Contains(source.Text(t, 0)!)).ToArray();
            var normalPaths = materialTables.Select(t => source.Text(source.Tables(t, 2, 64)
                .Single(p => source.Text(p, 0) == "NormalMap"), 1)).ToArray();
            if (normalPaths.Length != names.Count || normalPaths.Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidDataException("Shared normal material groups require the same authored normal image.");
            foreach (var material in materialTables)
                foreach (var vector in source.Tables(material, 7, 128).Where(v => source.Text(v, 0) == "UVScaleOffsetNormal"))
                {
                    var at = source.Field(vector, 1);
                    if (at == 0 || source.Float(at) != 1 || source.Float(at + 4) != 1
                        || source.Float(at + 8) != 0 || source.Float(at + 12) != 0)
                        throw new InvalidDataException("Shared normal fields require their qualified identity UV transform.");
                }
            var indices = new List<uint>(); var vertexBase = 0;
            foreach (var primitive in primitives)
            {
                indices.AddRange(primitive.Indices.Select(index => checked(index + (uint)vertexBase)));
                vertexBase = checked(vertexBase + primitive.Vertices.Length / 16);
            }
            // Only the normal rasterizer sees this geometry. Color layers, AO,
            // native mesh sections, visibility, rigging and animations retain their identities.
            var merged = primitives[0] with
            {
                Vertices = primitives.SelectMany(p => p.Vertices).ToArray(), Indices = indices.ToArray(), SecondaryUv = null,
            };
            var atlas = new SvSwShNormalAtlas(merged, hash, primitives.Select(p => GeometryHash([p])).ToHashSet(StringComparer.Ordinal));
            foreach (var name in names) result.Add(name, atlas);
        }
        if (!result.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(normalGroups.Keys))
            throw new InvalidDataException("Shared normal group material ownership differs from its qualification.");
        return result;
    }

    internal PreviewPrimitive Geometry(PreviewPrimitive primitive, byte[] materials)
    {
        if (Hash(materials) != materialHash || !sections.Contains(GeometryHash([primitive])))
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
