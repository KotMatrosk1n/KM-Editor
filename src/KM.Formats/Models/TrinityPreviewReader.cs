// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Numerics;

namespace KM.Formats.Models;

/// <summary>Projects supported static model geometry and base-color materials into a preview.</summary>
public sealed class TrinityPreviewReader(Func<string, byte[]> read)
{
    private readonly List<PreviewTexture> textures = [];
    private readonly Dictionary<string, int> textureIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Texture, Vector4 Color)> materials = new(StringComparer.Ordinal);
    private int totalVertices;
    private int totalIndices;

    public PreviewScene Load(string modelPath)
    {
        var model = Metadata(modelPath);
        var meshes = model.Tables(model.Root, 1, 16);
        if (meshes.Length == 0) throw new InvalidDataException("Model has no supported mesh.");
        var (materialStart, materialCount) = model.Vector(model.Root, 3, 4, 16);
        for (var i = 0; i < materialCount; i++) ReadMaterials(Resolve(modelPath, model.StringAt(materialStart + i * 4)));
        var skeleton = model.Table(model.Root, 2);
        if (skeleton != 0 && model.Text(skeleton, 0) is { } skeletonName) _ = Metadata(Resolve(modelPath, skeletonName));
        var meshPath = Resolve(modelPath, Required(model.Text(meshes[0], 0)));
        var meshData = Metadata(meshPath);
        var buffer = Metadata(Resolve(meshPath, Required(meshData.Text(meshData.Root, 2))));
        var shapes = meshData.Tables(meshData.Root, 1, 128);
        var groups = buffer.Tables(buffer.Root, 1, 128);
        if (shapes.Length != groups.Length) throw new InvalidDataException("Mesh and buffer groups do not match.");
        var primitives = new List<PreviewPrimitive>();
        for (var i = 0; i < shapes.Length; i++)
            primitives.AddRange(ReadShape(meshData, shapes[i], buffer, groups[i]));
        if (primitives.Count == 0 || primitives.Count > 256) throw new InvalidDataException("Model primitive count is unsupported.");
        return new PreviewScene(primitives, textures);
    }

    private ModelBuffer Metadata(string path) => new(read(path));
    private static string Required(string? text) => string.IsNullOrWhiteSpace(text) ? throw new InvalidDataException("Model dependency is missing.") : text;
    public static string Resolve(string parent, string relative)
    {
        if (relative.Length > 1024 || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') || relative.Any(char.IsControl))
            throw new InvalidDataException("Model dependency path is invalid.");
        var parts = parent[..(parent.LastIndexOf('/') + 1)].Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var part in relative.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new InvalidDataException("Model dependency leaves the resource root.");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }
        if (parts.Count == 0) throw new InvalidDataException("Empty model dependency.");
        return string.Join('/', parts);
    }

    private void ReadMaterials(string path)
    {
        var data = Metadata(path);
        foreach (var material in data.Tables(data.Root, 1, 128))
        {
            var name = Required(data.Text(material, 0));
            var color = Vector4.One;
            foreach (var parameter in data.Tables(material, 7, 256))
            {
                if (data.Text(parameter, 0) != "BaseColor") continue;
                var at = data.Field(parameter, 1);
                if (at != 0) color = new(data.Float(at), data.Float(at + 4), data.Float(at + 8), data.Float(at + 12));
            }
            if (!float.IsFinite(color.X + color.Y + color.Z + color.W)) throw new InvalidDataException("Invalid material color.");
            var texture = -1;
            foreach (var entry in data.Tables(material, 2, 32))
            {
                if (data.Text(entry, 0) != "BaseColorMap") continue;
                var texturePath = Resolve(path, Required(data.Text(entry, 1)));
                if (!textureIndices.TryGetValue(texturePath, out texture))
                {
                    if (textures.Count >= 32) throw new InvalidDataException("Preview texture limit exceeded.");
                    texture = textures.Count;
                    textures.Add(PreviewTexture.Read(read(texturePath)));
                    textureIndices.Add(texturePath, texture);
                }
            }
            if (!materials.TryAdd(name, (texture, color))) throw new InvalidDataException("Ambiguous model material.");
        }
    }

    private IEnumerable<PreviewPrimitive> ReadShape(ModelBuffer metadata, int shape, ModelBuffer buffers, int group)
    {
        var declarations = metadata.Tables(shape, 3, 16);
        if (declarations.Length != 1) throw new InvalidDataException("Multiple vertex declarations are not supported in this preview.");
        var sizes = metadata.Tables(declarations[0], 1, 16);
        var vertexBuffers = buffers.Tables(group, 1, 16);
        var indexBuffers = buffers.Tables(group, 0, 16);
        if (sizes.Length != 1 || vertexBuffers.Length != 1 || indexBuffers.Length != 1)
            throw new InvalidDataException("Multiple mesh streams are not supported in this preview.");
        var stride = checked((int)metadata.Value(sizes[0], 0));
        if (stride is < 12 or > 512) throw new InvalidDataException("Vertex stride is unsupported.");
        var vertices = buffers.Blob(vertexBuffers[0], 0);
        var indexBytes = buffers.Blob(indexBuffers[0], 0);
        if (vertices.Length % stride != 0) throw new InvalidDataException("Vertex buffer size does not match its stride.");
        var count = vertices.Length / stride;
        totalVertices = checked(totalVertices + count);
        if (count == 0 || totalVertices > 500_000) throw new InvalidDataException("Preview vertex limit exceeded.");
        var attributes = metadata.Tables(declarations[0], 0, 32);
        var position = attributes.SingleOrDefault(t => metadata.Value(t, 1) == 1);
        var normal = attributes.SingleOrDefault(t => metadata.Value(t, 1) == 2);
        var uv = attributes.FirstOrDefault(t => metadata.Value(t, 1) == 6 && metadata.Value(t, 2) == 0);
        if (position == 0 || metadata.Value(position, 3) != 51) throw new InvalidDataException("Position format is unsupported.");
        float Component(int attribute, int vertex, int component)
        {
            var type = metadata.Value(attribute, 3);
            var offset = checked((int)metadata.Value(attribute, 4));
            var componentBytes = type == 43 ? 2 : 4;
            if (type is not (43 or 48 or 51 or 54) || offset < 0 || offset + (component + 1) * componentBytes > stride)
                throw new InvalidDataException("Vertex attribute is unsupported.");
            var at = checked(vertex * stride + offset + component * componentBytes);
            var value = componentBytes == 2
                ? (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(vertices.AsSpan(at, 2)))
                : BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertices.AsSpan(at, 4)));
            if (!float.IsFinite(value) || Math.Abs(value) > 1_000_000) throw new InvalidDataException("Invalid vertex component.");
            return value;
        }
        var output = new float[checked(count * 8)];
        for (var v = 0; v < count; v++)
        {
            for (var c = 0; c < 3; c++) output[v * 8 + c] = Component(position, v, c);
            for (var c = 0; c < 3; c++) output[v * 8 + c + 3] = normal == 0 ? (c == 1 ? 1 : 0) : Component(normal, v, c);
            for (var c = 0; c < 2; c++) output[v * 8 + c + 6] = uv == 0 ? 0 : Component(uv, v, c);
        }
        var indexSize = metadata.Value(shape, 2) switch { 1 => 2, 2 => 4, _ => throw new InvalidDataException("Mesh index format is unsupported.") };
        foreach (var part in metadata.Tables(shape, 4, 128))
        {
            var length = checked((int)metadata.Value(part, 0)); var start = checked((int)metadata.Value(part, 1));
            totalIndices = checked(totalIndices + length);
            if (length <= 0 || length % 3 != 0 || totalIndices > 3_000_000 || start > indexBytes.Length / indexSize - length)
                throw new InvalidDataException("Mesh triangle range is invalid.");
            var indices = new uint[length];
            for (var n = 0; n < length; n++)
            {
                var at = checked((start + n) * indexSize);
                indices[n] = indexSize == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(indexBytes.AsSpan(at, 2)) : BinaryPrimitives.ReadUInt32LittleEndian(indexBytes.AsSpan(at, 4));
                if (indices[n] >= count) throw new InvalidDataException("Triangle refers to a missing vertex.");
            }
            if (!materials.TryGetValue(Required(metadata.Text(part, 3)), out var material)) throw new InvalidDataException("Mesh material is missing.");
            yield return new PreviewPrimitive(output, indices, material.Texture, material.Color);
        }
    }
}
