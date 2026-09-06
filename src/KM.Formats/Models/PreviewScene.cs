// SPDX-License-Identifier: GPL-3.0-only

using System.Numerics;

namespace KM.Formats.Models;

public sealed record PreviewPrimitive(float[] Vertices, uint[] Indices, int Texture, Vector4 Color);
public sealed record PreviewScene(IReadOnlyList<PreviewPrimitive> Primitives, IReadOnlyList<PreviewTexture> Textures)
{
    public void Write(Stream stream)
    {
        if (Primitives.Sum(x => (long)x.Vertices.Length / 8) > 500_000 ||
            Textures.Sum(x => (long)x.Blocks.Length) > 48 * 1024 * 1024)
            throw new InvalidDataException("Model preview exceeds the GPU budget.");
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("KMV1"u8); writer.Write(Primitives.Count); writer.Write(Textures.Count);
        foreach (var texture in Textures)
        {
            writer.Write(texture.Width); writer.Write(texture.Height); writer.Write(texture.Format);
            writer.Write(texture.Blocks.Length); writer.Write(texture.Blocks);
        }
        foreach (var primitive in Primitives)
        {
            writer.Write(primitive.Vertices.Length / 8); writer.Write(primitive.Indices.Length); writer.Write(primitive.Texture);
            writer.Write(primitive.Color.X); writer.Write(primitive.Color.Y); writer.Write(primitive.Color.Z); writer.Write(primitive.Color.W);
            foreach (var value in primitive.Vertices) writer.Write(value);
            foreach (var value in primitive.Indices) writer.Write(value);
        }
    }
}
