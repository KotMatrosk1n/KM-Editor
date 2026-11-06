// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public sealed record SwShNormalSectionPartition(string Mesh, string Material, int Vertices, int Triangles, IReadOnlyList<int> IsolatedTriangles);

/// <summary>Separates qualified opaque triangle sets without changing source geometry or mesh nodes.</summary>
public static class SwShNormalSections
{
    public static PreviewScene Split(PreviewScene scene, IReadOnlyList<SwShNormalSectionPartition> partitions)
    {
        if (partitions.Count == 0 || partitions.GroupBy(p => (p.Mesh, p.Material)).Any(g => g.Count() != 1))
            throw new InvalidDataException("Normal section partition identity is missing or ambiguous.");
        var byPrimitive = new Dictionary<PreviewPrimitive, SwShNormalSectionPartition>();
        foreach (var partition in partitions)
        {
            var matches = scene.Primitives.Where(p => p.Name == partition.Mesh && p.Material.Name == partition.Material).ToArray();
            if (matches.Length != 1 || matches[0].Material.Alpha != "Opaque"
                || matches[0].Vertices.Length != checked(partition.Vertices * 16)
                || matches[0].Indices.Length != checked(partition.Triangles * 3)
                || partition.IsolatedTriangles.Count == 0 || partition.IsolatedTriangles.Count >= partition.Triangles
                || partition.IsolatedTriangles.Distinct().Count() != partition.IsolatedTriangles.Count
                || partition.IsolatedTriangles.Any(i => i < 0 || i >= partition.Triangles))
                throw new InvalidDataException("Normal section partition requires its qualified opaque source geometry.");
            byPrimitive.Add(matches[0], partition);
        }
        return scene with { Primitives = scene.Primitives.SelectMany(primitive =>
        {
            if (!byPrimitive.TryGetValue(primitive, out var partition)) return new[] { primitive };
            var selected = partition.IsolatedTriangles.ToHashSet();
            var ordinary = new List<uint>(); var isolated = new List<uint>();
            for (var triangle = 0; triangle < partition.Triangles; triangle++)
            {
                var target = selected.Contains(triangle) ? isolated : ordinary;
                target.Add(primitive.Indices[triangle * 3]); target.Add(primitive.Indices[triangle * 3 + 1]); target.Add(primitive.Indices[triangle * 3 + 2]);
            }
            // Both sections retain the same source vertex/skin array, material,
            // mesh name and visibility node. Only their triangle lists differ.
            return new[] { primitive with { Indices = ordinary.ToArray() }, primitive with { Indices = isolated.ToArray() } };
        }).ToArray() };
    }
}
