// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public sealed record SwShMaterialBinding(string Name, int NativeMaterial, IReadOnlyDictionary<string, string> Textures)
{
    public Vector2 NormalUvScale { get; init; } = Vector2.One;
    public Vector4 ColorUvTransform { get; init; } = new(1, 1, 0, 0);
    public bool UseSecondaryUv { get; init; }
    /// <summary>Preserves authored half precision normal components.</summary>
    public bool UseSourceNormalComponents { get; init; }
    public bool UseSourceColor0 { get; init; }
    /// <summary>Requires the authored half precision tangent frame for UV0.</summary>
    public int? SourceTangentSet { get; init; }
}
public sealed record SwShEffectLocator(string Name, string Parent, Vector3 Translation)
{
    public Quaternion Rotation { get; init; } = Quaternion.Identity;
    public Vector3 Scale { get; init; } = Vector3.One;
}
public sealed record SwShConvertedGeometry(byte[] Model, PreviewBone[] Bones, Vector3 Minimum, Vector3 Maximum);

/// <summary>Encodes skinned geometry against an explicitly selected native shader contract.</summary>
public static class SwShModelWriter
{
    public static SwShConvertedGeometry Convert(PreviewScene scene, byte[] nativeModel,
        IReadOnlyList<SwShMaterialBinding> bindings, IReadOnlyDictionary<string, string> boneAliases,
        IReadOnlyList<SwShEffectLocator> locators, float scale = 100, CancellationToken cancellationToken = default,
        bool preserveMeshNames = false)
    {
        if (!float.IsFinite(scale) || scale <= 0 || scene.Primitives.Count is < 1 or > 128
            || scene.Rig.Bones.Length is < 1 or > 255 || bindings.Count != scene.Primitives.Count)
            throw new InvalidDataException("Model conversion inputs are outside the supported limits.");
        var native = new ModelBuffer(nativeModel);
        var materials = native.Tables(native.Root, 6, 128);
        var names = bindings.SelectMany(b => b.Textures.Values).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var bones = scene.Rig.Bones.Select(b => b with
        {
            Name = boneAliases.GetValueOrDefault(b.Name, b.Name),
            Translation = b.Translation.Select(v => v * scale).ToArray(),
        }).ToList();
        if (bones.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != bones.Count)
            throw new InvalidDataException("Converted bone names are ambiguous.");
        foreach (var locator in locators)
        {
            var parent = bones.FindIndex(b => b.Name == locator.Parent);
            if (parent < 0 || bones.Any(b => b.Name == locator.Name)) throw new InvalidDataException("Effect locator parent or identity is invalid.");
            var rotation = locator.Rotation;
            if (!float.IsFinite(rotation.LengthSquared()) || MathF.Abs(rotation.LengthSquared() - 1) > .001f
                || !float.IsFinite(locator.Translation.X + locator.Translation.Y + locator.Translation.Z)
                || !float.IsFinite(locator.Scale.X + locator.Scale.Y + locator.Scale.Z)
                || locator.Scale.X <= 0 || locator.Scale.Y <= 0 || locator.Scale.Z <= 0)
                throw new InvalidDataException("Effect locator transform is invalid.");
            bones.Add(new(locator.Name, parent, -1, false, [locator.Scale.X, locator.Scale.Y, locator.Scale.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W],
                [locator.Translation.X, locator.Translation.Y, locator.Translation.Z], []));
        }
        var meshBones = new Dictionary<string, int>(StringComparer.Ordinal);
        if (preserveMeshNames)
            foreach (var name in scene.Primitives.Select(p => p.Name).Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(name) || bones.Any(b => b.Name == name))
                    throw new InvalidDataException("Converted mesh node identity is invalid.");
                meshBones.Add(name, bones.Count);
                bones.Add(new(name, 0, -1, false, [1, 1, 1], [0, 0, 0, 1], [0, 0, 0], []));
            }
        if (bones.Count > 255) throw new InvalidDataException("Converted skeleton exceeds the native joint limit.");
        var joints = scene.Rig.Bones.Select((bone, index) => (bone.Joint, Index: index)).Where(b => b.Joint >= 0)
            .ToDictionary(b => b.Joint, b => b.Index);
        var bounds = scene.Primitives.Select(p => Bounds(p, scale)).ToArray();
        var minimum = bounds.Select(b => b.Min).Aggregate(Vector3.Min);
        var maximum = bounds.Select(b => b.Max).Aggregate(Vector3.Max);
        var writer = new ModelTableBuilder();
        var root = writer.Table(12, new Dictionary<int, byte[]>
        {
            [0] = native.Slice(native.Field(native.Root, 0), 4).ToArray(),
            [1] = BoundsBytes(minimum, maximum),
        }, new Dictionary<int, int> { [1] = 24 });
        Strings(writer, root, 2, names);
        foreach (var field in new[] { 3, 5 })
        {
            var (start, count) = native.Vector(native.Root, field, 4, 256);
            Strings(writer, root, field, Enumerable.Range(0, count).Select(i => native.StringAt(start + i * 4)).ToArray());
        }
        Strings(writer, root, 4, []);
        var slots = writer.Vector(root, 6, bindings.Count);
        for (var i = 0; i < slots.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = bindings[i];
            if ((uint)binding.NativeMaterial >= materials.Length || binding.Name != scene.Primitives[i].Material.Name)
                throw new InvalidDataException($"Native material binding {binding.Name} ({binding.NativeMaterial}/{materials.Length}) does not match mesh material {scene.Primitives[i].Material.Name}.");
            var preserveSourceSamplers = false;
            if (binding.UseSourceColor0)
            {
                if (scale != 100) throw new InvalidDataException("Source Unlit displacement requires the native scale of 100.");
                var (vstart, vcount) = native.Vector(native.Root, 3, 4, 256);
                var (fstart, fcount) = native.Vector(native.Root, 5, 4, 256);
                var vi = native.Value(materials[binding.NativeMaterial], 8); var fi = native.Value(materials[binding.NativeMaterial], 10);
                if (vi >= vcount || fi >= fcount || !ZA.ZaSwShDisplacedUnlit.Recognizes(native.StringAt(vstart + (int)vi * 4))
                    || native.StringAt(fstart + (int)fi * 4) != native.StringAt(vstart + (int)vi * 4) || !binding.UseSourceNormalComponents)
                    throw new InvalidDataException("Source vertex colors require their paired displacement program.");
                preserveSourceSamplers = true;
            }
            if (binding.SourceTangentSet is not null)
            {
                string? Program(int field, int indexField)
                {
                    var (programs, count) = native.Vector(native.Root, field, 4, 256);
                    var program = native.Value(materials[binding.NativeMaterial], indexField);
                    return program < count ? native.StringAt(programs + checked((int)program * 4)) : null;
                }
                var vertex = Program(3, 8); var fragment = Program(5, 10);
                var gem = vertex is not null && fragment is not null && binding.UseSourceNormalComponents
                    && ZA.ZaSwShFresnelMaterial.MatchesGeometryPrograms(vertex, fragment, binding.UseSecondaryUv)
                    && ZA.ZaSwShFresnelMaterial.RequiresClampRuntime(nativeModel);
                var body = vertex is not null && vertex == fragment && SwShZaTangentBodyShader.Recognizes(vertex)
                    && binding.UseSourceNormalComponents && !binding.UseSecondaryUv && binding.NormalUvScale == Vector2.One;
                if (binding.SourceTangentSet != 0 || !gem && !body)
                    throw new InvalidDataException("Authored tangent geometry requires its qualified UV0 vertex and fragment consumers.");
                preserveSourceSamplers = true;
            }
            writer.Pointer(slots[i], Material(writer, native, materials[binding.NativeMaterial], binding, names, preserveSourceSamplers));
        }
        slots = writer.Vector(root, 7, scene.Primitives.Count);
        for (var i = 0; i < slots.Length; i++)
            writer.Pointer(slots[i], writer.Table(4, new Dictionary<int, byte[]>
            {
                [0] = ModelTableBuilder.Integer(preserveMeshNames ? meshBones[scene.Primitives[i].Name] : 0), [1] = ModelTableBuilder.Integer(i),
                [2] = BoundsBytes(bounds[i].Min, bounds[i].Max), [3] = ModelTableBuilder.Integer(0),
            }, new Dictionary<int, int> { [2] = 24 }));
        slots = writer.Vector(root, 8, scene.Primitives.Count);
        for (var i = 0; i < slots.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primitive = scene.Primitives[i];
            var mesh = writer.Table(3); writer.Pointer(slots[i], mesh);
            var polygonSlot = writer.Vector(mesh, 0, 1)[0];
            var polygon = writer.Table(2, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(i) });
            writer.Pointer(polygonSlot, polygon);
            var indices = new byte[primitive.Indices.Length * 2];
            for (var j = 0; j < primitive.Indices.Length; j++)
                BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(j * 2), checked((ushort)primitive.Indices[j]));
            writer.Blob(polygon, 1, indices, primitive.Indices.Length);
            (int Kind, int Format, int Count)[] attributes = [(0, 0, 3), (1, 1, 4), (2, 1, 4), (3, 0, 2), (7, 3, 4), (8, 3, 4), (11, 3, 4), (12, 8, 4)];
            var secondaryUv = bindings[i].UseSecondaryUv;
            if (secondaryUv) attributes = attributes[..4].Concat([(4, 0, 2)]).Concat(attributes[4..]).ToArray();
            var attributeSlots = writer.Vector(mesh, 1, attributes.Length);
            for (var j = 0; j < attributes.Length; j++)
                writer.Pointer(attributeSlots[j], writer.Table(3, new Dictionary<int, byte[]>
                {
                    [0] = ModelTableBuilder.Integer(attributes[j].Kind), [1] = ModelTableBuilder.Integer(attributes[j].Format),
                    [2] = ModelTableBuilder.Integer(attributes[j].Count),
                }));
            writer.Blob(mesh, 2, Vertices(primitive, joints, scale, secondaryUv, bindings[i].UseSourceNormalComponents, bindings[i].SourceTangentSet, bindings[i].UseSourceColor0, cancellationToken));
        }
        slots = writer.Vector(root, 9, bones.Count);
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i]; var locator = i >= scene.Rig.Bones.Length;
            var meshNode = i >= scene.Rig.Bones.Length + locators.Count;
            if (bone.Parent < -1 || bone.Parent >= i || bone.Scale.Length != 3 || bone.Translation.Length != 3 || bone.Rotation.Length != 4)
                throw new InvalidDataException("Converted skeleton hierarchy is invalid.");
            var table = writer.Table(11, new Dictionary<int, byte[]>
            {
                [1] = ModelTableBuilder.Integer(meshNode ? 0 : locator ? 2 : 1), [2] = ModelTableBuilder.Integer(bone.Parent),
                [3] = ModelTableBuilder.Integer(0), [4] = ModelTableBuilder.Integer(bone.CompensateScale ? 1 : 0),
                [5] = ModelTableBuilder.Floats(bone.Scale), [6] = ModelTableBuilder.Floats(Euler(bone.Rotation)),
                [7] = ModelTableBuilder.Floats(bone.Translation), [8] = new byte[12], [9] = new byte[12],
                [10] = ModelTableBuilder.Integer(locator ? 0 : 1),
            }, Enumerable.Range(5, 5).ToDictionary(f => f, _ => 12));
            writer.Pointer(slots[i], table); writer.Text(table, 0, bone.Name);
        }
        writer.Vector(root, 10, 0); writer.Vector(root, 11, 0);
        return new(writer.Finish(root), bones.ToArray(), minimum, maximum);
    }

    public static byte[] Configuration(byte[] nativeConfiguration, int species, SwShConvertedGeometry geometry,
        IReadOnlyList<string> meshNames, float battleHeight, float fieldHeight)
    {
        if (species is < 1 or > ushort.MaxValue || meshNames.Count is < 1 or > 128 || battleHeight <= 0 || fieldHeight <= 0)
            throw new InvalidDataException("Converted model configuration is invalid.");
        var source = new ModelBuffer(nativeConfiguration); var writer = new ModelTableBuilder();
        var values = Enumerable.Range(0, 30).Where(i => i is not (4 or 5 or 28 or 29) && source.Field(source.Root, i) != 0)
            .ToDictionary(i => i, i => source.Slice(source.Field(source.Root, i), 4).ToArray());
        values[2] = ModelTableBuilder.Integer(species); values[6] = ModelTableBuilder.Integer(3);
        var root = writer.Table(30, values); writer.Text(root, 4, ""); writer.Text(root, 5, "");
        float[] dimensions = [geometry.Maximum.Y - geometry.Minimum.Y, battleHeight, fieldHeight,
            geometry.Minimum.X, geometry.Minimum.Y, geometry.Minimum.Z, geometry.Maximum.X, geometry.Maximum.Y, geometry.Maximum.Z];
        for (var i = 0; i < dimensions.Length; i++) writer.Single(writer.Field(root, 8 + i), dimensions[i]);
        var slots = writer.Vector(root, 28, meshNames.Count);
        for (var i = 0; i < slots.Length; i++) { var t = writer.Table(1); writer.Pointer(slots[i], t); writer.Text(t, 0, meshNames[i]); }
        var property = writer.Table(2); writer.Reference(root, 29, property); writer.Vector(property, 1, 0);
        return writer.Finish(root);
    }

    private static int Material(ModelTableBuilder writer, ModelBuffer source, int original, SwShMaterialBinding binding, string[] names, bool preserveSamplers)
    {
        var colorUv = binding.ColorUvTransform;
        if (!float.IsFinite(colorUv.X + colorUv.Y + colorUv.Z + colorUv.W) || colorUv.X is <= 0 or > 8 || colorUv.Y is <= 0 or > 8
            || MathF.Abs(colorUv.Z) > 8 || MathF.Abs(colorUv.W) > 8)
            throw new InvalidDataException("Native color UV transform is unsupported.");
        if (colorUv != new Vector4(1, 1, 0, 0))
        {
            var parameters = source.Tables(original, 13, 256).Select(p => source.Text(p, 0)).ToHashSet();
            if (new[] { "ColorUVScaleU", "ColorUVScaleV", "ColorUVTranslateU", "ColorUVTranslateV" }.Any(p => !parameters.Contains(p)))
                throw new InvalidDataException("Native color atlas transform parameters are missing.");
        }
        if (binding.NormalUvScale.X is not (1 or 2) || binding.NormalUvScale.Y is not (1 or 2))
            throw new InvalidDataException("Native normal UV scale is unsupported.");
        if (binding.NormalUvScale != Vector2.One)
        {
            var parameters = source.Tables(original, 13, 256).Select(parameter => source.Text(parameter, 0)).ToHashSet();
            var normal = source.Tables(original, 11, 64).Single(texture => source.Text(texture, 0) == "NormalMapTex");
            var sampler = source.Table(normal, 2);
            if (!parameters.Contains("NormalMapUVScaleU") || !parameters.Contains("NormalMapUVScaleV")
                || source.Value(sampler, 1) is not (0 or 2) || source.Value(sampler, 2) is not (0 or 2))
                throw new InvalidDataException("Native normal texture cropping requires a supported scaled sampler.");
        }
        var scalarFields = Enumerable.Range(2, 9).Concat(Enumerable.Range(15, 5)).ToArray();
        int Width(int field) => field is 3 or 4 or >= 15 ? 1 : 4;
        var missing = scalarFields.Where(f => source.Field(original, f) == 0).ToHashSet();
        var values = scalarFields.Where(f => !missing.Contains(f)).ToDictionary(f => f, f => source.Slice(source.Field(original, f), Width(f)).ToArray());
        var table = writer.Table(21, values, absent: missing);
        writer.Text(table, 0, binding.Name); writer.Text(table, 1, source.Text(original, 1) ?? throw new InvalidDataException("Native shader name is missing."));
        void Parameters(int parent, int field, int sourceTable, int sourceField, int width)
        {
            var entries = source.Tables(sourceTable, sourceField, 256); var slots = writer.Vector(parent, field, entries.Length);
            for (var i = 0; i < entries.Length; i++)
            {
                var at = source.Field(entries[i], 1); var name = source.Text(entries[i], 0) ?? throw new InvalidDataException("Native material parameter is unnamed.");
                var value = at == 0 ? new byte[width] : source.Slice(at, width).ToArray();
                if (width == 4 && name is "ColorUVScaleU" or "ColorUVScaleV" or "NormalMapUVScaleU" or "NormalMapUVScaleV") value = ModelTableBuilder.Floats(1);
                if (width == 4 && name == "NormalMapUVScaleU") value = ModelTableBuilder.Floats(binding.NormalUvScale.X);
                if (width == 4 && name == "NormalMapUVScaleV") value = ModelTableBuilder.Floats(binding.NormalUvScale.Y);
                if (width == 4 && name is "ColorBaseU" or "ColorBaseV" or "ColorUVTranslateU" or "ColorUVTranslateV") value = ModelTableBuilder.Floats(0);
                if (width == 4 && name == "ColorUVScaleU") value = ModelTableBuilder.Floats(colorUv.X);
                if (width == 4 && name == "ColorUVScaleV") value = ModelTableBuilder.Floats(colorUv.Y);
                // Native shaders use scale * (uv - translation) + base.
                if (width == 4 && name == "ColorUVTranslateU") value = ModelTableBuilder.Floats(colorUv.Z == 0 ? 0 : -colorUv.Z / colorUv.X);
                if (width == 4 && name == "ColorUVTranslateV") value = ModelTableBuilder.Floats(colorUv.W == 0 ? 0 : -colorUv.W / colorUv.Y);
                var item = writer.Table(2, new Dictionary<int, byte[]> { [1] = value }, new Dictionary<int, int> { [1] = Math.Max(4, width) });
                writer.Pointer(slots[i], item); writer.Text(item, 0, name);
            }
        }
        Parameters(table, 12, original, 12, 1); Parameters(table, 13, original, 13, 4); Parameters(table, 14, original, 14, 12);
        var common = writer.Table(3); writer.Reference(table, 20, common);
        var sourceCommon = source.Table(original, 20);
        Parameters(common, 0, sourceCommon, 0, 1); Parameters(common, 1, sourceCommon, 1, 4); Parameters(common, 2, sourceCommon, 2, 12);
        var textures = source.Tables(original, 11, 64); var textureSlots = writer.Vector(table, 11, textures.Length);
        for (var i = 0; i < textures.Length; i++)
        {
            var role = source.Text(textures[i], 0) ?? throw new InvalidDataException("Native texture binding is unnamed.");
            if (!binding.Textures.TryGetValue(role, out var name)) throw new InvalidDataException($"Texture binding {role} has no conversion.");
            var texture = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(Array.IndexOf(names, name)) });
            writer.Pointer(textureSlots[i], texture); writer.Text(texture, 0, role);
            var sampler = source.Table(textures[i], 2);
            if (preserveSamplers)
            {
                if (binding.NormalUvScale != Vector2.One)
                    throw new InvalidDataException("ZA gem source samplers cannot receive a baked normal UV transform.");
                writer.Reference(texture, 2, ZA.ZaSwShFresnelMaterial.CloneSampler(writer, source, sampler));
                continue;
            }
            var absent = Enumerable.Range(0, 9).Where(f => source.Field(sampler, f) == 0).ToHashSet();
            var samplerValues = Enumerable.Range(0, 9).Where(f => !absent.Contains(f))
                .ToDictionary(f => f, f => source.Slice(source.Field(sampler, f), f == 4 ? 16 : 4).ToArray());
            if (role == "NormalMapTex")
                foreach (var axis in new[] { (Field: 1, Scale: binding.NormalUvScale.X), (Field: 2, Scale: binding.NormalUvScale.Y) })
                    if (axis.Scale != 1) { absent.Remove(axis.Field); samplerValues[axis.Field] = ModelTableBuilder.Integer(0); }
            writer.Reference(texture, 2, writer.Table(9, samplerValues,
                new Dictionary<int, int> { [4] = 16 }, absent));
        }
        return table;
    }

    public static (Vector3 Min, Vector3 Max) Bounds(PreviewPrimitive primitive, float scale)
    {
        var vertices = primitive.Vertices;
        if (vertices.Length == 0 || vertices.Length % 16 != 0 || vertices.Length / 16 > ushort.MaxValue
            || vertices.Any(v => !float.IsFinite(v)) || primitive.Indices.Length is < 3 or > 1_500_000
            || primitive.Indices.Length % 3 != 0 || primitive.Indices.Any(i => i >= vertices.Length / 16))
            throw new InvalidDataException("Converted geometry has invalid vertices or triangles.");
        var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < vertices.Length; i += 16)
        { var v = new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]) * scale; min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        return (min, max);
    }
    private static byte[] BoundsBytes(Vector3 min, Vector3 max) => ModelTableBuilder.Floats(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    private static void Strings(ModelTableBuilder writer, int table, int field, string[] values)
    { var slots = writer.Vector(table, field, values.Length); for (var i = 0; i < slots.Length; i++) writer.Pointer(slots[i], writer.String(values[i])); }
    private static float[] Euler(float[] q)
    {
        var rotation = Quaternion.Normalize(new(q[0], q[1], q[2], q[3]));
        var m = Matrix4x4.CreateFromQuaternion(rotation);
        var y = MathF.Asin(Math.Clamp(-m.M13, -1, 1));
        return MathF.Abs(MathF.Cos(y)) > 0.000001f ? [MathF.Atan2(m.M23, m.M33), y, MathF.Atan2(m.M12, m.M11)]
            : [MathF.Atan2(-m.M32, m.M22), y, 0];
    }
    private static byte[] Vertices(PreviewPrimitive primitive, IReadOnlyDictionary<int, int> joints, float scale, bool secondaryUv,
        bool sourceNormalComponents, int? sourceTangentSet, bool sourceColor0, CancellationToken token)
    {
        var v = primitive.Vertices; var count = v.Length / 16;
        if (sourceColor0 && primitive.Color0 is { } colors && (colors.Length != count * 4
            || colors.Any(v => !float.IsFinite(v) || v < 0 || v > 1 || MathF.Abs(v * 255 - MathF.Round(v * 255)) > .0001f)))
            throw new InvalidDataException("Source displacement colors must retain their normalized byte values.");
        if (sourceTangentSet is not null && (sourceTangentSet is not (0 or 1) || !sourceNormalComponents))
            throw new InvalidDataException("Authored tangents require their source normals and a dedicated native consumer.");
        var sourceTangents = sourceTangentSet == 0 ? primitive.Tangent0 : sourceTangentSet == 1 ? primitive.Tangent1 : null;
        if (sourceTangentSet is not null && (sourceTangents is null || sourceTangents.Length != count * 4
            || sourceTangents.Any(value => !float.IsFinite(value) || value != (float)(Half)value)))
            throw new InvalidDataException("Converted material requires complete authored half precision tangents.");
        if (secondaryUv && (primitive.SecondaryUv is not { } uv1 || uv1.Length != count * 2 || uv1.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException($"Converted material {primitive.Material.Name} on {primitive.Name} requires complete secondary texture coordinates.");
        var tangents = new Vector3[count]; var bitangents = new Vector3[count];
        Vector3 Position(int i) => new(v[i * 16], v[i * 16 + 1], v[i * 16 + 2]);
        Vector2 Uv(int i) => new(v[i * 16 + 6], v[i * 16 + 7]);
        for (var i = 0; i < primitive.Indices.Length; i += 3)
        {
            var a = (int)primitive.Indices[i]; var b = (int)primitive.Indices[i + 1]; var c = (int)primitive.Indices[i + 2];
            var e1 = Position(b) - Position(a); var e2 = Position(c) - Position(a); var u1 = Uv(b) - Uv(a); var u2 = Uv(c) - Uv(a);
            var determinant = u1.X * u2.Y - u1.Y * u2.X;
            if (MathF.Abs(determinant) <= 1e-9f) continue;
            var tangent = (e1 * u2.Y - e2 * u1.Y) / determinant; var bitangent = (e2 * u1.X - e1 * u2.X) / determinant;
            tangents[a] += tangent; tangents[b] += tangent; tangents[c] += tangent;
            bitangents[a] += bitangent; bitangents[b] += bitangent; bitangents[c] += bitangent;
        }
        using var stream = new MemoryStream(count * (secondaryUv ? 60 : 52)); using var output = new BinaryWriter(stream);
        void Half(float value) => output.Write(BitConverter.HalfToUInt16Bits((Half)value));
        Span<double> normalized = stackalloc double[4];
        Span<int> quantized = stackalloc int[4];
        Span<int> weightOrder = stackalloc int[4];
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var at = i * 16; var normal = new Vector3(v[at + 3], v[at + 4], v[at + 5]);
            if (normal.LengthSquared() < 1e-12) throw new InvalidDataException("Converted geometry contains an empty normal.");
            if (sourceNormalComponents && (normal.X != (float)(Half)normal.X || normal.Y != (float)(Half)normal.Y || normal.Z != (float)(Half)normal.Z))
                throw new InvalidDataException("Converted material requires exactly representable authored half precision normals.");
            var storedNormal = normal;
            normal = Vector3.Normalize(normal);
            if (!sourceNormalComponents) storedNormal = normal;
            var tangent = tangents[i] - normal * Vector3.Dot(normal, tangents[i]);
            if (tangent.LengthSquared() < 1e-12) tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX);
            tangent = Vector3.Normalize(tangent);
            var position = Position(i) * scale; output.Write(position.X); output.Write(position.Y); output.Write(position.Z);
            Half(storedNormal.X); Half(storedNormal.Y); Half(storedNormal.Z);
            Half(1);
            if (sourceTangents is not null)
            {
                var authored = new Vector3(sourceTangents[i * 4], sourceTangents[i * 4 + 1], sourceTangents[i * 4 + 2]);
                var handedness = sourceTangents[i * 4 + 3];
                if (handedness is not (-1 or 1) || authored.LengthSquared() < 1e-12f
                    || Vector3.Cross(normal, authored).LengthSquared() < 1e-12f)
                    throw new InvalidDataException("Authored tangent frame is degenerate.");
                Half(authored.X); Half(authored.Y); Half(authored.Z); Half(handedness);
            }
            else { Half(tangent.X); Half(tangent.Y); Half(tangent.Z); Half(Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0 ? -1 : 1); }
            output.Write(v[at + 6]); output.Write(v[at + 7]);
            if (secondaryUv) { output.Write(primitive.SecondaryUv![i * 2]); output.Write(primitive.SecondaryUv[i * 2 + 1]); }
            if (sourceColor0)
            {
                for (var channel = 0; channel < 4; channel++)
                    output.Write(primitive.Color0 is { } color ? (byte)MathF.Round(color[i * 4 + channel] * 255) : channel == 3 ? (byte)255 : (byte)0);
            }
            else { output.Write((byte)127); output.Write((byte)127); output.Write((byte)255); output.Write((byte)255); }
            output.Write((byte)Math.Clamp((normal.X + 1) * 127.5f, 0, 255));
            output.Write((byte)Math.Clamp((normal.Y + 1) * 127.5f, 0, 255));
            output.Write((byte)Math.Clamp((normal.Z + 1) * 127.5f, 0, 255));
            output.Write((byte)255);
            var weights = v.AsSpan(at + 12, 4);
            // Enumerable.Sum(float) accumulates in double precision before rounding to float.
            var sum = (float)((double)weights[0] + weights[1] + weights[2] + weights[3]);
            if (sum <= 0 || weights[0] < 0 || weights[1] < 0 || weights[2] < 0 || weights[3] < 0)
                throw new InvalidDataException("Skin weights are invalid.");
            var quantizedSum = 0;
            for (var j = 0; j < 4; j++)
            {
                normalized[j] = (double)weights[j] / sum * 255;
                quantized[j] = (int)Math.Floor(normalized[j]);
                quantizedSum = checked(quantizedSum + quantized[j]);
                weightOrder[j] = j;
            }
            // Preserve the stable largest-remainder ordering, including ties between joints.
            for (var j = 1; j < 4; j++)
            {
                var index = weightOrder[j];
                var remainder = normalized[index] - quantized[index];
                var atOrder = j;
                while (atOrder > 0 && remainder.CompareTo(normalized[weightOrder[atOrder - 1]] - quantized[weightOrder[atOrder - 1]]) > 0)
                {
                    weightOrder[atOrder] = weightOrder[atOrder - 1];
                    atOrder--;
                }
                weightOrder[atOrder] = index;
            }
            for (var j = 0; j < Math.Min(4, unchecked(255 - quantizedSum)); j++) quantized[weightOrder[j]]++;
            for (var j = 0; j < 4; j++)
            {
                var joint = v[at + 8 + j];
                if (quantized[j] != 0 && (joint != (int)joint || !joints.ContainsKey((int)joint))) throw new InvalidDataException("Skin joint does not exist.");
                output.Write(quantized[j] == 0 ? (byte)0 : checked((byte)joints[(int)joint]));
            }
            for (var j = 0; j < 4; j++) output.Write(checked((byte)quantized[j]));
        }
        return stream.ToArray();
    }
}
