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
        var bytes = read(path);
        _ = KM.Formats.ZA.ZaSwShFresnelMaterial.RequiresClampRuntime(bytes);
        var data = new ModelBuffer(bytes);
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
            var unlit = SwShPreviewLayerEmission.UsesProgram(data, material, KM.Formats.SV.SvSwShLayerEmission.UnlitBaseProgramName);
            var additive = SwShPreviewLayerEmission.UsesProgram(data, material, KM.Formats.SV.SvSwShLayerEmission.AdditiveProgramName);
            var baseEmission = unlit || SwShPreviewLayerEmission.UsesProgram(data, material, KM.Formats.SV.SvSwShLayerEmission.BaseProgramName);
            var separateBaseColors = KM.Formats.SV.SvSwShSourceCoverage.AllPrograms.Concat(KM.Formats.SV.SvSwShSourceBase.AllPrograms)
                .Any(p => SwShPreviewLayerEmission.UsesProgram(data, material, p));
            var surface = PreviewSurface.Read(data, material, true, baseEmission && !separateBaseColors)
                with { LayerBaseEmission = baseEmission, SeparateBaseColors = separateBaseColors, Unlit = unlit };
            var restorationEffect = SwShPreviewRestorationMaterial.Kind(data, material);
            var layerEmission = SwShPreviewLayerEmission.UsesProgram(data, material) || restorationEffect is 1 or 2;
            var threeLayerEmission = SwShPreviewLayerEmission.UsesProgram(data, material, KM.Formats.SV.SvSwShLayerEmission.ThreeLayerProgramName);
            var mapEmission = SwShPreviewLayerEmission.UsesProgram(data, material, KM.Formats.SV.SvSwShLayerEmission.MapProgramName);
            if (mapEmission)
            {
                surface = surface with { MapEmission = true };
                surface.Values[39][3] = 0;
                surface.Values[5] = [1, 1, 1, 1];
            }
            var surfaceLayers = SwShPreviewLayerEmission.UsesProgram(data, material, KM.Formats.SV.SvSwShSurfaceFlow.LayerProgramName);
            if (surfaceLayers) surface = surface with { SurfaceLayers = true };
            if (layerEmission)
            {
                surface = surface with { LayerEmission = true };
                surface.Values[32] = [1, 1, 1, 1]; surface.Values[33] = [0, 0, 0, 0];
                foreach (var parameter in data.Tables(material, 13, 256))
                {
                    var channel = Array.IndexOf(KM.Formats.SV.SvSwShLayerEmission.Scalars, data.Text(parameter, 0));
                    if (channel >= 0) surface.Values[13][channel] = data.Float(data.Field(parameter, 1));
                }
                foreach (var parameter in data.Tables(material, 14, 256))
                {
                    var channel = Array.IndexOf(KM.Formats.SV.SvSwShLayerEmission.Vectors, data.Text(parameter, 0));
                    if (channel >= 0) for (var c = 0; c < 3; c++) surface.Values[14 + channel][c] = data.Float(data.Field(parameter, 1) + c * 4);
                }
            }
            var fire = SwShPreviewFire.Kind(data.Text(material, 1));
            var switches = data.Tables(material, 12, 256).ToDictionary(p => data.Text(p, 0) ?? "",
                p => data.Field(p, 1) is var at && at != 0 ? data.U8(at) : 0, StringComparer.Ordinal);
            var emission = mapEmission || fire == 0 && switches.GetValueOrDefault("EmissionMaskUse") != 0
                && switches.GetValueOrDefault("SwitchEmissionMaskTexUV") == 0;
            if (emission && !mapEmission) surface.Values[1][2] = 1;
            var color = Vector4.One;
            var layers = new[] { Vector4.One, Vector4.One, Vector4.One, Vector4.One };
            foreach (var parameter in data.Tables(material, 14, 256))
            {
                var at = data.Field(parameter, 1);
                if (at == 0) continue;
                var value = new Vector4(data.Float(at), data.Float(at + 4), data.Float(at + 8), 1);
                if (baseEmission && Array.IndexOf(separateBaseColors ? KM.Formats.SV.SvSwShSourceCoverage.VectorNames
                    : KM.Formats.SV.SvSwShLayerEmission.BaseVectors, data.Text(parameter, 0)) is var baseLayer && baseLayer >= 0)
                { layers[baseLayer] = value; continue; }
                if (surfaceLayers && Array.IndexOf(KM.Formats.SV.SvSwShLayerEmission.Vectors, data.Text(parameter, 0)) is var layer && layer >= 0)
                    layers[layer] = value;
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
            Vector4? maskUv = null;
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
                    case "L1ConstantColor0Val" when mapEmission: surface.Values[1][2] = value; break;
                    case "EmissionMaskVal" when emission && !mapEmission: surface.Values[1][2] = value; break;
                }
            }
            if (emission && !mapEmission)
            {
                var gain = data.Tables(material, 13, 256).FirstOrDefault(p => data.Text(p, 0) == "OnGameEmissionVal");
                if (gain != 0) surface.Values[5][3] = data.Field(gain, 1) is var at && at != 0 ? data.Float(at) : 0;
            }
            if (surfaceLayers) maskUv = underlayUv;
            if (SwShPreviewLayerEmission.UsesProgram(data, material, KM.Formats.SV.SvSwShLayerEmission.MirroredProgramName)
                || SwShPreviewLayerEmission.UsesProgram(data, material, KM.Formats.SV.SvSwShSourceBase.MirroredProgramName))
                maskUv = new Vector4(2, 1, 0, 0);
            surface = SwShPreviewRestorationMaterial.Read(data, material, surface, restorationEffect);
            if (restorationEffect is 3 or 4) origins = Vector4.Zero;
            if (fire != 0)
            {
                warnings.Add("materialEffectApproximate");
                surface = SwShPreviewFire.Read(data, material, surface, fire, out uv, out var fireMaskUv, out underlayUv);
                maskUv = fireMaskUv;
            }
            foreach (var binding in data.Tables(material, 11, 32))
            {
                var role = data.Text(binding, 0);
                if (surfaceLayers && role == "Col0ColChangeTex") continue;
                if (restorationEffect == 10 && role?.StartsWith("SourceGem", StringComparison.Ordinal) != true) continue;
                var probe = restorationEffect == 10 && role == "SourceGemProbeTex";
                if (restorationEffect == 10) SwShPreviewFresnel.RequireSampler(data, binding, probe);
                var restorationRole = SwShPreviewRestorationMaterial.TextureRole(restorationEffect, role);
                if (restorationRole != role) role = restorationRole;
                else if (fire != 0) role = SwShPreviewFire.TextureRole(fire, role ?? "");
                else if (role == "LyCol0Tex" && surfaceLayers) role = "Col0ColChangeTex";
                else if (role == "EmissionMaskTex" && layerEmission) role = "Col0ColChangeTex";
                else if (role == "EmissionMaskTex" && emission) role = "EmissionMap";
                var surfaceSlot = PreviewSurface.TextureSlot(role ?? "");
                if (role is not ("Col0Tex" or "LyCol0Tex" or "Col0ColChangeTex") && surfaceSlot < 0 && !probe) continue;
                // An identity color change does not consume its placeholder mask.
                if (restorationEffect != 10 && fire == 0 && !layerEmission && !surfaceLayers && role == "Col0ColChangeTex" && layers.Take(3).All(layer => layer == Vector4.One)) continue;
                var index = checked((int)data.Value(binding, 1));
                if (index >= names.Length) throw new InvalidDataException("Material texture index is invalid.");
                var textureName = names[index] + ".bntx";
                if (role == "NormalMapTex" && names[index] is "chara_d_white" or "dummy_col") continue;
                if (role == "LyCol0Tex" && names[index] == "dummy_col") continue;
                if (!textureIds.TryGetValue(textureName, out var loaded))
                {
                    if (textures.Count >= 128) throw new InvalidDataException("Preview texture limit exceeded.");
                    try { var decoded = PreviewTexture.Read(read(textureName)) with { SourcePath = textureName }; loaded = textures.Count; textures.Add(decoded); }
                    catch (IOException) when (restorationEffect != 10) { loaded = -1; warnings.Add("textureUnavailable"); }
                    textureIds.Add(textureName, loaded);
                }
                if (restorationEffect == 10 && (loaded < 0 || (textures[loaded].Format == 0x1505) != probe))
                    throw new InvalidDataException("ZA gem preview texture dimension is incompatible with its role.");
                if (probe) { surface = surface with { GemProbeTexture = loaded }; continue; }
                var sampler = data.Table(binding, 2);
                float Address(int field) => sampler == 0 ? 0 : data.Value(sampler, field) switch
                {
                    0 => 0,
                    1 => 1,
                    2 => 6,
                    _ => throw new InvalidDataException("Texture addressing mode is unsupported.")
                };
                if (surfaceSlot >= 0) { surface.Textures[surfaceSlot] = loaded; surface.Wraps[surfaceSlot] = [Address(1), Address(2), 0, 0]; }
                else if (role == "Col0Tex") { texture = loaded; wrap.X = Address(1); wrap.Y = Address(2); }
                else if (role == "Col0ColChangeTex") { mask = loaded; wrap.Z = Address(1); wrap.W = Address(2); }
                else { underlay = loaded; underlayWrap = new(Address(1), Address(2), 0, 0); }
            }
            if (restorationEffect == 10 && (texture < 0 || underlay < 0 || surface.GemProbeTexture is null
                || new[] { 0, 2, 7 }.Any(i => surface.Textures[i] < 0)
                || (mask >= 0) != (SwShPreviewFresnel.Settings(data, material)?.MaskedEmission == true)))
                throw new InvalidDataException("ZA gem preview requires its authored textures and matching emission-mask variant.");
            color = fire != 0 ? Vector4.One : new(surface.Values[39][0], surface.Values[39][1], surface.Values[39][2], color.W);
            return new PreviewMaterial(name, texture, mask, color, layers,
                uv, additive ? "Add" : data.Value(material, 6) == 0 ? "Opaque" : "Blend")
            { Surface = surface, Wrap = wrap, MaskUv = maskUv, UvOrigins = origins, MaskChannels = additive ? new(1, 0, 0, 0) : new(1, 1, 1, !threeLayerEmission && (layerEmission || surfaceLayers) ? 1 : 0), Underlay = underlay, UnderlayUv = underlayUv, UnderlayWrap = underlayWrap };
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
        var polygons = data.Tables(shape, 0, 128);
        var authoredFrame = polygons.Any(p => data.Value(p, 0) < materials.Length
            && materials[data.Value(p, 0)].Surface?.RestorationEffect == 10);
        var skinned = attributes.ContainsKey(11) && attributes.ContainsKey(12);
        if (attributes.ContainsKey(11) != attributes.ContainsKey(12)) throw new InvalidDataException("Incomplete vertex skinning.");
        var vertices = new float[count * 16];
        var secondaryUv = attributes.ContainsKey(4) ? new float[count * 2] : null;
        var tangent0 = attributes.TryGetValue(2, out var tangentAttribute) && tangentAttribute is { Format: 1, Count: 4 }
            ? new float[count * 4] : null;
        var colors = attributes.TryGetValue(7, out var colorAttribute) && colorAttribute.Count == 4
            && colorAttribute.Format is 3 or 8 ? new float[count * 4] : null;
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
            if (secondaryUv is not null) { secondaryUv[v * 2] = Value(4, v, 0); secondaryUv[v * 2 + 1] = Value(4, v, 1); }
            if (colors is not null) for (var c = 0; c < 4; c++) colors[v * 4 + c] = Value(7, v, c) / (attributes[7].Format == 3 ? 255f : 1f);
            if (tangent0 is not null) for (var c = 0; c < 4; c++) tangent0[v * 4 + c] = Value(2, v, c);
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
            // The paired native stages consume the authored decoded weights.
            // Re-normalizing them on the CPU would change its matrix operands.
            if (total > 0 && !authoredFrame) for (var c = 0; c < 4; c++) vertices[v * 16 + 12 + c] /= total;
        }
        foreach (var polygon in polygons)
        {
            var material = checked((int)data.Value(polygon, 0));
            var (start, length) = data.Vector(polygon, 1, 2, 3_000_000);
            indicesRead = checked(indicesRead + length);
            if (material >= materials.Length || indicesRead > 3_000_000)
                throw new InvalidDataException("Mesh triangle range is invalid.");
            var indices = new uint[length];
            for (var i = 0; i < length; i++)
            {
                indices[i] = data.U16(start + i * 2);
                if (indices[i] >= count) throw new InvalidDataException("Triangle refers to an unavailable vertex.");
            }
            if (length % 3 != 0)
            {
                warnings.Add("incompleteTriangles");
                indices = indices[..(length - length % 3)];
            }
            if (indices.Length == 0) continue;
            if (materials[material].Surface?.RestorationEffect == 10)
            {
                var gem = materials[material].Surface!;
                if (!skinned || tangent0 is null
                    || !attributes.TryGetValue(1, out var normal) || normal is not { Format: 1, Count: 4 }
                    || !attributes.TryGetValue(11, out var joints) || joints is not { Format: 3, Count: 4 }
                    || !attributes.TryGetValue(12, out var weights) || weights is not { Format: 8, Count: 4 }
                    || !attributes.TryGetValue(3, out var uv0) || uv0 is not { Format: 0, Count: 2 }
                    || (gem.RestorationValues[10][0] == 1 || gem.RestorationValues[10][1] == 1)
                        && (!attributes.TryGetValue(4, out var uv1) || uv1 is not { Format: 0, Count: 2 }))
                    throw new InvalidDataException("ZA gem preview needs its authored normal, tangent, skin and UV interface.");
            }
            yield return new PreviewPrimitive(vertices, indices, materials[material], rig.Bones[meshBone].Name) { SecondaryUv = secondaryUv, Color0 = colors, Tangent0 = tangent0 };
        }
    }
}
