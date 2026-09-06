// SPDX-License-Identifier: GPL-3.0-only

using System.Numerics;

namespace KM.Formats.Models;

public sealed record PreviewMaterial(string Name, int Texture, int Mask, Vector4 Color, Vector4[] Layers, Vector4 Uv, string Alpha)
{
    public Vector4 Wrap { get; init; }
}
public sealed record PreviewPrimitive(float[] Vertices, uint[] Indices, PreviewMaterial Material, string Name);
public sealed record PreviewScene(IReadOnlyList<PreviewPrimitive> Primitives, IReadOnlyList<PreviewTexture> Textures)
{
    public PreviewRig Rig { get; init; } = new([], [], null, []);
    public void Write(Stream stream)
    {
        if (Primitives.Sum(x => (long)x.Vertices.Length / 16) > 500_000 ||
            Textures.Sum(x => (long)x.Blocks.Length) > 48 * 1024 * 1024)
            throw new InvalidDataException("Model preview exceeds the GPU budget.");
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("KMV2"u8); writer.Write(Primitives.Count); writer.Write(Textures.Count);
        foreach (var texture in Textures)
        {
            writer.Write(texture.Width); writer.Write(texture.Height); writer.Write(texture.Format);
            writer.Write(texture.Blocks.Length); writer.Write(texture.Blocks);
        }
        foreach (var primitive in Primitives)
        {
            writer.Write(primitive.Vertices.Length / 16); writer.Write(primitive.Indices.Length); writer.Write(primitive.Material.Texture);
            writer.Write(primitive.Material.Mask);
            void Color(Vector4 c) { writer.Write(c.X); writer.Write(c.Y); writer.Write(c.Z); writer.Write(c.W); }
            Color(primitive.Material.Color);
            foreach (var layer in primitive.Material.Layers) Color(layer);
            Color(primitive.Material.Uv);
            Color(primitive.Material.Wrap);
            writer.Write(primitive.Material.Alpha.Contains("Blend", StringComparison.Ordinal) ? 1 : 0);
            foreach (var value in primitive.Vertices) writer.Write(value);
            foreach (var value in primitive.Indices) writer.Write(value);
        }
        var metadata = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
            Rig.Bones, Rig.Clips, Rig.Clip, Rig.Warnings,
            Meshes = Primitives.Select(p => new { p.Name, Material = p.Material.Name }).ToArray()
        }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        if (metadata.Length > 16 * 1024 * 1024) throw new InvalidDataException("Animation exceeds the preview budget.");
        writer.Write(metadata.Length); writer.Write(metadata);
    }
}
