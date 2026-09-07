// SPDX-License-Identifier: GPL-3.0-only

using System.Numerics;

namespace KM.Formats.Models;

public sealed record PreviewMaterial(string Name, int Texture, int Mask, Vector4 Color, Vector4[] Layers, Vector4 Uv, string Alpha)
{
    public Vector4 Wrap { get; init; }
    public Vector4? MaskUv { get; init; }
    public bool TopOriginUv { get; init; }
    public int Highlight { get; init; } = -1;
    public Vector4 HighlightColor { get; init; }
    public Vector4 HighlightUv { get; init; } = new(1, 1, 0, 0);
    public Vector4 HighlightWrap { get; init; }
    public int Underlay { get; init; } = -1;
    public Vector4 UnderlayUv { get; init; } = new(1, 1, 0, 0);
    public Vector4 UnderlayWrap { get; init; }
    public Vector4 MaskChannels { get; init; } = Vector4.One;
    public Vector4 UvOrigins { get; init; }
    public PreviewSurface? Surface { get; init; }
}
public sealed record PreviewPrimitive(float[] Vertices, uint[] Indices, PreviewMaterial Material, string Name);
public sealed record PreviewScene(IReadOnlyList<PreviewPrimitive> Primitives, IReadOnlyList<PreviewTexture> Textures)
{
    public PreviewRig Rig { get; init; } = new([], [], null, []);
    public void Write(Stream stream, int resolution = 1)
    {
        if (resolution is not (1 or 2 or 4)) throw new InvalidDataException("Model preview resolution is invalid.");
        if (Primitives.Sum(x => (long)x.Vertices.Length / 16) > 500_000 ||
            Textures.Sum(x => x.ByteLength) > 64 * 1024 * 1024)
            throw new InvalidDataException("Model preview exceeds the GPU budget.");
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("KMV3"u8); writer.Write(resolution); writer.Write(Primitives.Count); writer.Write(Textures.Count);
        foreach (var texture in Textures)
        {
            writer.Write(texture.Width); writer.Write(texture.Height); writer.Write(texture.Format);
            writer.Write(1 + texture.Mips.Length);
            writer.Write(texture.Blocks.Length); writer.Write(texture.Blocks);
            foreach (var mip in texture.Mips) { writer.Write(mip.Length); writer.Write(mip); }
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
        var metadata = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            Rig.Bones,
            Rig.Clips,
            Rig.Clip,
            Rig.Warnings,
            Meshes = Primitives.Select(p => new
            {
                p.Name,
                Material = p.Material.Name,
                p.Material.TopOriginUv,
                p.Material.Surface,
                UvOrigins = new[] { p.Material.UvOrigins.X, p.Material.UvOrigins.Y, p.Material.UvOrigins.Z, p.Material.UvOrigins.W },
                MaskChannels = new[] { p.Material.MaskChannels.X, p.Material.MaskChannels.Y, p.Material.MaskChannels.Z, p.Material.MaskChannels.W },
                Underlay = p.Material.Underlay < 0 ? (int?)null : p.Material.Underlay,
                UnderlayUv = new[] { p.Material.UnderlayUv.X, p.Material.UnderlayUv.Y, p.Material.UnderlayUv.Z, p.Material.UnderlayUv.W },
                UnderlayWrap = new[] { p.Material.UnderlayWrap.X, p.Material.UnderlayWrap.Y, p.Material.UnderlayWrap.Z, p.Material.UnderlayWrap.W },
                MaskUv = p.Material.MaskUv is { } uv ? new[] { uv.X, uv.Y, uv.Z, uv.W } : null,
                Highlight = p.Material.Highlight < 0 ? (int?)null : p.Material.Highlight,
                HighlightColor = new[] { p.Material.HighlightColor.X, p.Material.HighlightColor.Y, p.Material.HighlightColor.Z, p.Material.HighlightColor.W },
                HighlightWrap = new[] { p.Material.HighlightWrap.X, p.Material.HighlightWrap.Y, p.Material.HighlightWrap.Z, p.Material.HighlightWrap.W },
                HighlightUv = new[] { p.Material.HighlightUv.X, p.Material.HighlightUv.Y, p.Material.HighlightUv.Z, p.Material.HighlightUv.W }
            }).ToArray()
        }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        if (metadata.Length > 16 * 1024 * 1024) throw new InvalidDataException("Animation exceeds the preview budget.");
        writer.Write(metadata.Length); writer.Write(metadata);
    }
}
