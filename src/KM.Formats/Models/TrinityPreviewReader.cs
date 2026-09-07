// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Numerics;

namespace KM.Formats.Models;

/// <summary>Reads bounded model geometry, skinning and layered color materials.</summary>
public sealed class TrinityPreviewReader(Func<string, byte[]> read, bool topOriginMaterialUv = false)
{
    private readonly List<PreviewTexture> textures = [];
    private readonly Dictionary<string, int> textureIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PreviewMaterial> materials = new(StringComparer.Ordinal);
    private PreviewBone[] bones = [];
    private readonly HashSet<string> warnings = new(StringComparer.Ordinal);
    private int totalVertices;
    private int totalIndices;

    public bool HasCharacterSurface(string modelPath)
    {
        var model = Metadata(modelPath);
        if (model.Table(model.Root, 2) == 0 || model.Tables(model.Root, 1, 16).Length == 0) return false;
        var (start, count) = model.Vector(model.Root, 3, 4, 16);
        for (var i = 0; i < count; i++)
        {
            var material = Metadata(Resolve(modelPath, model.StringAt(start + i * 4)));
            if (material.Tables(material.Root, 1, 128).Any(table => material.Tables(table, 2, 32)
                .Any(binding => material.Text(binding, 0) == "BaseColorMap"))) return true;
        }
        return false;
    }

    public PreviewScene Load(string modelPath, PreviewMaterialVariant? variant = null)
    {
        var model = Metadata(modelPath);
        var meshes = model.Tables(model.Root, 1, 16);
        if (meshes.Length == 0) throw new InvalidDataException("Model has no supported mesh.");
        var (materialStart, materialCount) = model.Vector(model.Root, 3, 4, 16);
        if (variant is not null) foreach (var path in variant.Materials) ReadMaterials(path);
        else for (var i = 0; i < materialCount; i++) ReadMaterials(Resolve(modelPath, model.StringAt(materialStart + i * 4)));
        var skeleton = model.Table(model.Root, 2);
        if (skeleton != 0 && model.Text(skeleton, 0) is { } skeletonName)
            bones = PreviewRigReader.Skeleton(Metadata(Resolve(modelPath, skeletonName)));
        var primitives = new List<PreviewPrimitive>();
        var lods = model.Tables(model.Root, 4, 16);
        var selected = lods.Length == 0 ? new[] { 0 }
            : lods.Select(lod => model.Tables(lod, 0, 16).FirstOrDefault())
                .Where(table => table != 0).Select(table => checked((int)model.Value(table, 0))).Distinct().ToArray();
        if (selected.Length == 0) throw new InvalidDataException("Model has no primary detail meshes.");
        foreach (var index in selected)
        {
            if (index < 0 || index >= meshes.Length) throw new InvalidDataException("Model detail mesh index is invalid.");
            var meshPath = Resolve(modelPath, Required(model.Text(meshes[index], 0)));
            var meshData = Metadata(meshPath);
            var buffer = Metadata(Resolve(meshPath, Required(meshData.Text(meshData.Root, 2))));
            var shapes = meshData.Tables(meshData.Root, 1, 128);
            var groups = buffer.Tables(buffer.Root, 1, 128);
            if (shapes.Length != groups.Length) throw new InvalidDataException("Mesh and buffer groups do not match.");
            for (var i = 0; i < shapes.Length; i++)
                primitives.AddRange(ReadShape(meshData, shapes[i], buffer, groups[i]));
        }
        if (primitives.Count == 0 || primitives.Count > 256) throw new InvalidDataException("Model primitive count is unsupported.");
        if (variant is not null) primitives.RemoveAll(primitive => variant.Visibility.TryGetValue(primitive.Name, out var visible) && !visible);
        return new PreviewScene(primitives, textures) { Rig = new(bones, [], null, warnings.ToArray()) };
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
            var surface = PreviewSurface.Read(data, material, false);
            var color = Vector4.One;
            var layers = new[] { Vector4.One, Vector4.One, Vector4.One, Vector4.One };
            var maskChannels = Vector4.Zero;
            var hasMaskScale = false;
            foreach (var parameter in data.Tables(material, 4, 256))
            {
                var at = data.Field(parameter, 1);
                var value = at == 0 ? 0 : data.Float(at);
                switch (data.Text(parameter, 0))
                {
                    case "LayerMaskScale1": maskChannels.X = value; hasMaskScale = true; break;
                    case "LayerMaskScale2": maskChannels.Y = value; hasMaskScale = true; break;
                    case "LayerMaskScale3": maskChannels.Z = value; hasMaskScale = true; break;
                    case "LayerMaskScale4": maskChannels.W = value; hasMaskScale = true; break;
                }
            }
            var uv = new Vector4(1, 1, 0, 0);
            var highlightColor = Vector4.Zero;
            var highlightUv = new Vector4(1, 1, 0, 0);
            var highlightIntensity = 0f;
            foreach (var parameter in data.Tables(material, 4, 256))
                    if (data.Text(parameter, 0) == "EmissionIntensityLayer5" && data.Field(parameter, 1) is var at && at != 0)
                        highlightIntensity = data.Float(at);
            foreach (var parameter in data.Tables(material, 7, 256))
            {
                var at = data.Field(parameter, 1);
                if (at == 0) continue;
                var value = new Vector4(data.Float(at), data.Float(at + 4), data.Float(at + 8), data.Float(at + 12));
                switch (data.Text(parameter, 0))
                {
                    case "BaseColor": color = value; break;
                    case "BaseColorLayer1": layers[0] = value; if (!hasMaskScale) maskChannels.X = 1; break;
                    case "BaseColorLayer2": layers[1] = value; if (!hasMaskScale) maskChannels.Y = 1; break;
                    case "BaseColorLayer3": layers[2] = value; if (!hasMaskScale) maskChannels.Z = 1; break;
                    case "BaseColorLayer4": layers[3] = value; if (!hasMaskScale) maskChannels.W = 1; break;
                    case "UVScaleOffset": uv = value; break;
                    case "EmissionColorLayer5": highlightColor = value; break;
                    case "UVScaleOffset1": highlightUv = value; break;
                }
            }
            if (!float.IsFinite(color.X + color.Y + color.Z + color.W)) throw new InvalidDataException("Invalid material color.");
            var texture = -1;
            var mask = -1;
            var highlight = -1;
            var highlightWrap = Vector4.Zero;
            var wrap = Vector4.Zero;
            var samplers = data.Tables(material, 3, 32);
            foreach (var entry in data.Tables(material, 2, 32))
            {
                var role = data.Text(entry, 0);
                var surfaceSlot = PreviewSurface.TextureSlot(role ?? "");
                if (role is not ("BaseColorMap" or "LayerMaskMap" or "HighlightMaskMap") && surfaceSlot < 0) continue;
                var slot = checked((int)data.Value(entry, 2));
                if (samplers.Length > 0 && slot >= samplers.Length) { warnings.Add("textureUnavailable"); continue; }
                var wrapU = samplers.Length == 0 ? 0 : data.Value(samplers[slot], 9);
                var wrapV = samplers.Length == 0 ? 0 : data.Value(samplers[slot], 10);
                if (wrapU is not (0 or 1 or 6 or 7) || wrapV is not (0 or 1 or 6 or 7)) { warnings.Add("textureUnavailable"); continue; }
                if (role == "BaseColorMap") { wrap.X = wrapU; wrap.Y = wrapV; }
                else if (role == "LayerMaskMap") { wrap.Z = wrapU; wrap.W = wrapV; }
                else if (role == "HighlightMaskMap") highlightWrap = new(wrapU, wrapV, 0, 0);
                if (surfaceSlot >= 0) surface.Wraps[surfaceSlot] = [wrapU, wrapV, 0, 0];
                var texturePath = Resolve(path, Required(data.Text(entry, 1)));
                if (!textureIndices.TryGetValue(texturePath, out var textureIndex))
                {
                    if (textures.Count >= 128) throw new InvalidDataException("Preview texture limit exceeded.");
                    textureIndex = textures.Count;
                    try { textures.Add(PreviewTexture.Read(read(texturePath)) with { SourcePath = texturePath }); }
                    catch (IOException) { warnings.Add("textureUnavailable"); textureIndices.TryAdd(texturePath, -1); continue; }
                    textureIndices.Add(texturePath, textureIndex);
                }
                if (surfaceSlot >= 0) surface.Textures[surfaceSlot] = textureIndex;
                else if (role == "BaseColorMap") texture = textureIndex; else if (role == "LayerMaskMap") mask = textureIndex; else highlight = textureIndex;
            }
            if (!materials.TryAdd(name, new(name, texture, mask, color, layers, uv, data.Text(material, 15) ?? "Opaque")
            {
                Wrap = wrap,
                Surface = surface,
                MaskChannels = maskChannels,
                TopOriginUv = topOriginMaterialUv,
                Highlight = highlight,
                HighlightWrap = highlightWrap,
                HighlightColor = highlight >= 0 ? highlightColor * highlightIntensity : Vector4.Zero,
                HighlightUv = highlightUv
            }))
                throw new InvalidDataException("Ambiguous model material.");
        }
    }

    private IEnumerable<PreviewPrimitive> ReadShape(ModelBuffer metadata, int shape, ModelBuffer buffers, int group)
    {
        var declarations = metadata.Tables(shape, 3, 128);
        if (declarations.Length == 0) throw new InvalidDataException("Vertex declaration is missing.");
        var sizes = metadata.Tables(declarations[0], 1, 16);
        var vertexBuffers = buffers.Tables(group, 1, 128);
        var indexBuffers = buffers.Tables(group, 0, 16);
        if (sizes.Length != 1 || vertexBuffers.Length != declarations.Length || indexBuffers.Length != 1)
            throw new InvalidDataException("Multiple mesh streams are not supported in this preview.");
        var stride = checked((int)metadata.Value(sizes[0], 0));
        if (stride is < 12 or > 512) throw new InvalidDataException("Vertex stride is unsupported.");
        var vertices = buffers.Blob(vertexBuffers[0], 0);
        var indexBytes = buffers.Blob(indexBuffers[0], 0);
        if (vertices.Length % stride != 0) throw new InvalidDataException("Vertex buffer size does not match its stride.");
        var count = vertices.Length / stride;
        // Additional streams contain facial morph deltas. The first stream is the
        // complete neutral mesh, including UVs and skinning, and shares its indices.
        for (var morph = 1; morph < declarations.Length; morph++)
        {
            var morphSizes = metadata.Tables(declarations[morph], 1, 16);
            var morphAttributes = metadata.Tables(declarations[morph], 0, 32);
            if (morphSizes.Length != 1 || metadata.Value(morphSizes[0], 0) != 28
                || buffers.Blob(vertexBuffers[morph], 0).Length != checked(count * 28)
                || morphAttributes.Length != 3 || morphAttributes.Any(a => metadata.Value(a, 1) is not (1 or 2 or 3)))
                throw new InvalidDataException("Morph stream layout is unsupported.");
            warnings.Add("morphUnsupported");
        }
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
            var componentBytes = type is 20 or 22 ? 1 : type is 39 or 43 ? 2 : 4;
            if (type is not (20 or 22 or 39 or 43 or 48 or 51 or 54) || offset < 0 || offset + (component + 1) * componentBytes > stride)
                throw new InvalidDataException("Vertex attribute is unsupported.");
            var at = checked(vertex * stride + offset + component * componentBytes);
            var value = type == 22 ? vertices[at] : type == 20 ? vertices[at] / 255f : type == 39 ? BinaryPrimitives.ReadUInt16LittleEndian(vertices.AsSpan(at, 2)) / 65535f : componentBytes == 2
                ? (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(vertices.AsSpan(at, 2)))
                : BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertices.AsSpan(at, 4)));
            if (!float.IsFinite(value) || Math.Abs(value) > 1_000_000) throw new InvalidDataException("Invalid vertex component.");
            return value;
        }
        var jointAttribute = attributes.SingleOrDefault(t => metadata.Value(t, 1) == 7);
        var weightAttribute = attributes.SingleOrDefault(t => metadata.Value(t, 1) == 8);
        if (bones.Length == 0 && weightAttribute != 0) warnings.Add("sharedSkeletonUnavailable");
        var output = new float[checked(count * 16)];
        for (var v = 0; v < count; v++)
        {
            for (var c = 0; c < 3; c++) output[v * 16 + c] = Component(position, v, c);
            for (var c = 0; c < 3; c++) output[v * 16 + c + 3] = normal == 0 ? (c == 1 ? 1 : 0) : Component(normal, v, c);
            for (var c = 0; c < 2; c++) output[v * 16 + c + 6] = uv == 0 ? 0 : Component(uv, v, c);
            var total = 0f;
            for (var c = 0; c < 4; c++)
            {
                var weight = weightAttribute == 0 || bones.Length == 0 ? 0 : Component(weightAttribute, v, c);
                var joint = jointAttribute == 0 ? 0 : Component(jointAttribute, v, c);
                if (weight < 0 || weight > 1 || (weight > 0 && !bones.Any(b => b.Joint == joint)))
                    throw new InvalidDataException("Vertex skinning references an unsupported joint.");
                output[v * 16 + 8 + c] = weight > 0 ? joint : 0;
                output[v * 16 + 12 + c] = weight; total += weight;
            }
            if (total > 0) for (var c = 0; c < 4; c++) output[v * 16 + 12 + c] /= total;
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
            yield return new PreviewPrimitive(output, indices, material, metadata.Text(shape, 0) ?? "");
        }
    }
}
