// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;

namespace KM.Formats.Models;

/// <summary>Projects bounded packed character geometry into the shared preview scene.</summary>
public sealed class SwShPreviewReader(Func<string, byte[]> read)
{
    private readonly List<PreviewTexture> textures = [];
    private readonly Dictionary<string, int> textureIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnings = new(StringComparer.Ordinal);
    private int verticesRead;
    private int indicesRead;

    public PreviewScene Load(string path)
    {
        var data = new ModelBuffer(read(path));
        var rig = SwShPreviewRig.Read(data);
        var materials = ReadMaterials(data);
        var shapes = data.Tables(data.Root, 8, 256);
        var primitives = new List<PreviewPrimitive>();
        foreach (var mesh in data.Tables(data.Root, 7, 256))
        {
            var shape = checked((int)data.Value(mesh, 1));
            var bone = checked((int)data.Value(mesh, 0));
            if (shape >= shapes.Length || bone >= rig.Bones.Length) throw new InvalidDataException("Mesh references an unavailable shape or transform.");
            primitives.AddRange(ReadShape(data, shapes[shape], bone, rig, materials));
            if (primitives.Count > 256) throw new InvalidDataException("Model primitive count exceeds the preview budget.");
        }
        if (primitives.Count == 0) throw new InvalidDataException("Model has no supported mesh.");
        return new(primitives, textures) { Rig = new(rig.Bones, [], null, warnings.ToArray()) };
    }

    private PreviewMaterial[] ReadMaterials(ModelBuffer data)
    {
        var (start, count) = data.Vector(data.Root, 2, 4, 256);
        var names = Enumerable.Range(0, count).Select(i => data.StringAt(start + 4 * i)).ToArray();
        return data.Tables(data.Root, 6, 256).Select(material =>
        {
            var name = data.Text(material, 0) ?? throw new InvalidDataException("Material identity is missing.");
            var color = Vector4.One;
            var layers = new[] { Vector4.One, Vector4.One, Vector4.One, Vector4.One };
            foreach (var parameter in data.Tables(material, 14, 256))
            {
                var at = data.Field(parameter, 1);
                if (at == 0) continue;
                var value = new Vector4(data.Float(at), data.Float(at + 4), data.Float(at + 8), 1);
                switch (data.Text(parameter, 0))
                {
                    case "ConstantColor0": color = value; break;
                    case "Col0SkinColor": layers[0] = value; break;
                    case "Col0PrimaryColor": layers[1] = value; break;
                    case "Col0SecondaryColor": layers[2] = value; break;
                }
            }
            var texture = -1;
            var mask = -1;
            var underlay = -1;
            var underlayUv = new Vector4(1, 1, 0, 0);
            var underlayWrap = Vector4.Zero;
            var wrap = Vector4.Zero;
            var uv = new Vector4(1, 1, 0, 0);
            var origins = Vector4.Zero;
            foreach (var parameter in data.Tables(material, 13, 256))
            {
                var at = data.Field(parameter, 1);
                var value = at == 0 ? 0 : data.Float(at);
                switch (data.Text(parameter, 0))
                {
                    case "ColorUVScaleU": uv.X = value; break;
                    case "ColorUVScaleV": uv.Y = value; break;
                    case "ColorUVTranslateU": uv.Z = value; break;
                    case "ColorUVTranslateV": uv.W = value; break;
                    case "Layer1UVScaleU": underlayUv.X = value; break;
                    case "Layer1UVScaleV": underlayUv.Y = value; break;
                    case "Layer1UVTranslateU": underlayUv.Z = value; break;
                    case "Layer1UVTranslateV": underlayUv.W = value; break;
                    case "ColorBaseU": origins.X = value; break;
                    case "ColorBaseV": origins.Y = value; break;
                    case "Layer1BaseU": origins.Z = value; break;
                    case "Layer1BaseV": origins.W = value; break;
                }
            }
            foreach (var binding in data.Tables(material, 11, 32))
            {
                var role = data.Text(binding, 0);
                if (role is not ("Col0Tex" or "LyCol0Tex" or "Col0ColChangeTex")) continue;
                // An identity color change does not consume its placeholder mask.
                if (role == "Col0ColChangeTex" && layers.Take(3).All(layer => layer == Vector4.One)) continue;
                var index = checked((int)data.Value(binding, 1));
                if (index >= names.Length) throw new InvalidDataException("Material texture index is invalid.");
                var textureName = names[index] + ".bntx";
                if (role == "LyCol0Tex" && names[index] == "dummy_col") continue;
                if (!textureIds.TryGetValue(textureName, out var loaded))
                {
                    if (textures.Count >= 32) throw new InvalidDataException("Preview texture limit exceeded.");
                    try { var decoded = PreviewTexture.Read(read(textureName)); loaded = textures.Count; textures.Add(decoded); }
                    catch (IOException) { loaded = -1; warnings.Add("textureUnavailable"); }
                    textureIds.Add(textureName, loaded);
                }
                var sampler = data.Table(binding, 2);
                float Address(int field) => sampler == 0 ? 0 : data.Value(sampler, field) switch
                {
                    0 => 0,
                    1 => 1,
                    2 => 6,
                    _ => throw new InvalidDataException("Texture addressing mode is unsupported.")
                };
                if (role == "Col0Tex") { texture = loaded; wrap.X = Address(1); wrap.Y = Address(2); }
                else if (role == "Col0ColChangeTex") { mask = loaded; wrap.Z = Address(1); wrap.W = Address(2); }
                else { underlay = loaded; underlayWrap = new(Address(1), Address(2), 0, 0); }
            }
            return new PreviewMaterial(name, texture, mask, color, layers,
                uv, data.Value(material, 6) == 0 ? "Opaque" : "Blend")
            { Wrap = wrap, UvOrigins = origins, MaskChannels = new(1, 1, 1, 0), Underlay = underlay, UnderlayUv = underlayUv, UnderlayWrap = underlayWrap };
        }).ToArray();
    }

    private IEnumerable<PreviewPrimitive> ReadShape(ModelBuffer data, int shape, int meshBone, SwShPreviewRig rig, PreviewMaterial[] materials)
    {
        var attributes = new Dictionary<int, (int Format, int Count, int Offset)>();
        var stride = 0;
        foreach (var attribute in data.Tables(shape, 1, 32))
        {
            var kindAt = data.Field(attribute, 0); var formatAt = data.Field(attribute, 1);
            var kind = kindAt == 0 ? 0 : data.U8(kindAt); var format = formatAt == 0 ? 0 : data.U8(formatAt);
            var components = checked((int)data.Value(attribute, 2));
            var size = format switch { 0 => 4, 1 => 2, 3 or 8 => 1, _ => throw new InvalidDataException("Vertex component format is unsupported.") };
            if (components is < 1 or > 4 || !attributes.TryAdd(kind, (format, components, stride)))
                throw new InvalidDataException("Vertex declaration is invalid.");
            stride = checked(stride + size * components);
        }
        if (stride is < 12 or > 512 || !attributes.TryGetValue(0, out var position) || position.Count != 3 || position.Format != 0)
            throw new InvalidDataException("Vertex positions are unsupported.");
        var bytes = data.Blob(shape, 2);
        if (bytes.Length % stride != 0) throw new InvalidDataException("Vertex buffer does not match its declaration.");
        var count = bytes.Length / stride;
        verticesRead = checked(verticesRead + count);
        if (count == 0 || verticesRead > 500_000) throw new InvalidDataException("Preview vertex limit exceeded.");
        float Value(int kind, int vertex, int component, float fallback = 0)
        {
            if (!attributes.TryGetValue(kind, out var attribute)) return fallback;
            if (component >= attribute.Count) throw new InvalidDataException("Vertex attribute has too few components.");
            var size = attribute.Format == 0 ? 4 : attribute.Format == 1 ? 2 : 1;
            var at = vertex * stride + attribute.Offset + component * size;
            var value = attribute.Format switch
            {
                0 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4))),
                1 => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2))),
                8 => bytes[at] / 255f,
                _ => bytes[at]
            };
            if (!float.IsFinite(value) || Math.Abs(value) > 1_000_000) throw new InvalidDataException("Invalid vertex component.");
            return value;
        }
        var skinned = attributes.ContainsKey(11) && attributes.ContainsKey(12);
        if (attributes.ContainsKey(11) != attributes.ContainsKey(12)) throw new InvalidDataException("Incomplete vertex skinning.");
        var vertices = new float[count * 16];
        if (!Matrix4x4.Invert(rig.World[meshBone], out var inverseWorld)) throw new InvalidDataException("Mesh transform is singular.");
        var normalTransform = Matrix4x4.Transpose(inverseWorld);
        for (var v = 0; v < count; v++)
        {
            var point = new Vector3(Value(0, v, 0), Value(0, v, 1), Value(0, v, 2));
            var normal = new Vector3(Value(1, v, 0), Value(1, v, 1, 1), Value(1, v, 2));
            if (!skinned)
            {
                point = Vector3.Transform(point, rig.World[meshBone]);
                normal = Vector3.TransformNormal(normal, normalTransform);
            }
            vertices[v * 16] = point.X; vertices[v * 16 + 1] = point.Y; vertices[v * 16 + 2] = point.Z;
            vertices[v * 16 + 3] = normal.X; vertices[v * 16 + 4] = normal.Y; vertices[v * 16 + 5] = normal.Z;
            vertices[v * 16 + 6] = Value(3, v, 0); vertices[v * 16 + 7] = Value(3, v, 1);
            var total = 0f;
            for (var c = 0; c < 4; c++)
            {
                var weight = skinned ? Value(12, v, c) : c == 0 ? 1 : 0;
                var joint = skinned ? checked((int)Value(11, v, c)) : 0;
                if (weight < 0 || weight > 1 || (skinned && weight > 0 && (joint < 0 || joint >= rig.Skin.Length)))
                    throw new InvalidDataException("Vertex skinning references an unavailable joint.");
                vertices[v * 16 + 8 + c] = weight == 0 ? 0 : skinned ? rig.Skin[joint] : meshBone;
                vertices[v * 16 + 12 + c] = weight; total += weight;
            }
            if (total > 0) for (var c = 0; c < 4; c++) vertices[v * 16 + 12 + c] /= total;
        }
        foreach (var polygon in data.Tables(shape, 0, 128))
        {
            var material = checked((int)data.Value(polygon, 0));
            var (start, length) = data.Vector(polygon, 1, 2, 3_000_000);
            indicesRead = checked(indicesRead + length);
            if (material >= materials.Length || length == 0 || length % 3 != 0 || indicesRead > 3_000_000)
                throw new InvalidDataException("Mesh triangle range is invalid.");
            var indices = new uint[length];
            for (var i = 0; i < length; i++)
            {
                indices[i] = data.U16(start + i * 2);
                if (indices[i] >= count) throw new InvalidDataException("Triangle refers to an unavailable vertex.");
            }
            yield return new(vertices, indices, materials[material], rig.Bones[meshBone].Name);
        }
    }
}
