// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

/// <summary>Attaches native animated smoke to Mega Heatran's authored emission joints.</summary>
public sealed class SwShHeatranSmoke
{
    private const float Scale = 1f;
    private readonly byte[] model;
    private readonly byte[] idle;
    private readonly byte[] loop;
    private readonly ModelBuffer source;
    private readonly SwShPreviewRig rig;
    private readonly int[] materials;
    private readonly int[] meshes;
    private readonly int[] bones;
    private readonly Quaternion[] rootRotations = new Quaternion[2];
    public string[] MaterialNames { get; } = ["HeatranSmoke0Core", "HeatranSmoke0Mask", "HeatranSmoke1Core", "HeatranSmoke1Mask"];
    public string[] BoneNames => Enumerable.Range(0, 2).SelectMany(e => bones.Select(b => Name(e, rig.Bones[b].Name))).ToArray();
    public string[] TextureNames { get; }
    public string[] ShaderNames { get; }

    public SwShHeatranSmoke(byte[] nativeModel, byte[] nativeIdle, byte[] nativeLoop)
    {
        model = nativeModel; idle = nativeIdle; loop = nativeLoop;
        source = new(model); rig = SwShPreviewRig.Read(source);
        var allMaterials = source.Tables(source.Root, 6, 128);
        materials = new[] { "SmokeCoreA", "SmokeMaskA" }.Select(n => allMaterials.Single(m => source.Text(m, 0) == n)).ToArray();
        var allMeshes = source.Tables(source.Root, 7, 256);
        meshes = new[] { "SmokeCoreASkin", "SmokeMaskASkin", "SmokeGeomASkin" }
            .Select(n => allMeshes.Single(m => rig.Bones[checked((int)source.Value(m, 0))].Name.EndsWith("_" + n, StringComparison.Ordinal))).ToArray();
        bones = Enumerable.Range(40, 8).ToArray();
        if (rig.Bones[40].Name != "Feeler" || bones.Skip(1).Any(b => rig.Bones[b].Parent < 40 || rig.Bones[b].Parent >= b))
            throw new InvalidDataException("Native smoke hierarchy requires another adapter.");
        TextureNames = materials.SelectMany(m => source.Tables(m, 11, 64)).Select(t => Strings(source, 2)[source.Value(t, 1)])
            .Distinct(StringComparer.Ordinal).ToArray();
        ShaderNames = new[] { (Field: 3, Index: 8), (Field: 5, Index: 10) }
            .SelectMany(f => materials.Select(m => Strings(source, f.Field)[source.Value(m, f.Index)]))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    public byte[] Model(byte[] body, IReadOnlyDictionary<string, string> aliases)
    {
        var original = new ModelBuffer(body); var bodyRig = SwShPreviewRig.Read(original);
        // Z-A emits particles at joint 02 and carries the rising plume along
        // the authored 02..06 smoke chains. Torkoal's compact mesh plume has
        // no particle travel: fit its enlarged volume to joint 04 so its
        // lower lobes overlap the back and its upper lobes clear the vents.
        // At 02 it is buried; at 05 the compact plume floats above the body.
        var parents = new[] { "feeler_a_04", "feeler_b_04" }.Select(n =>
            Array.FindIndex(bodyRig.Bones, b => b.Name == aliases.GetValueOrDefault(n, n))).ToArray();
        if (parents.Any(p => p < 0) || bodyRig.Bones.Length + 22 > 255 || bodyRig.Skin.Length + 16 > 255)
            throw new InvalidDataException("Heatran smoke attachment joints are unavailable.");
        if (!Matrix4x4.Invert(rig.World[40], out var inverse)) throw new InvalidDataException("Smoke root is singular.");
        var writer = new ModelTableBuilder();
        var nativeReferences = new List<(int Slot, int Offset)>();
        void NativeReference(int table, int field, int offset) => nativeReferences.Add((writer.Field(table, field), offset));
        var root = writer.Table(12, new Dictionary<int, byte[]> {
            [0] = original.Slice(original.Field(original.Root, 0), 4).ToArray(),
            [1] = original.Slice(original.Field(original.Root, 1), 24).ToArray(),
        }, new Dictionary<int, int> { [1] = 24 });
        var names = new Dictionary<int, string[]>();
        foreach (var field in new[] { 2, 3, 5 })
        {
            names[field] = Strings(original, field).Concat(field == 2 ? TextureNames : ShaderNames).Distinct(StringComparer.Ordinal).ToArray();
            WriteStrings(writer, root, field, names[field]);
        }
        foreach (var field in new[] { 4, 10, 11 }) WriteStrings(writer, root, field, []);
        var old = new[] { 6, 7, 8, 9 }.ToDictionary(f => f, f => original.Tables(original.Root, f, 512));
        var slots = old.ToDictionary(p => p.Key, p => writer.Vector(root, p.Key, p.Value.Length + (p.Key == 6 ? 4 : p.Key == 9 ? 22 : 6)));
        var copy = writer.Append(body);
        foreach (var (field, tables) in old) for (var i = 0; i < tables.Length; i++) writer.Pointer(slots[field][i], copy + tables[i]);
        var allMaterials = source.Tables(source.Root, 6, 128); var shapes = source.Tables(source.Root, 8, 256);
        var minimum = new Vector3(original.Float(original.Field(original.Root, 1)), original.Float(original.Field(original.Root, 1) + 4), original.Float(original.Field(original.Root, 1) + 8));
        var maximum = new Vector3(original.Float(original.Field(original.Root, 1) + 12), original.Float(original.Field(original.Root, 1) + 16), original.Float(original.Field(original.Root, 1) + 20));
        for (var emitter = 0; emitter < 2; emitter++)
        {
            // Particle plumes rise in world Y. The emission joints point along
            // Heatran's horns; inheriting their bind rotation turns the plume
            // down into the body. Retain the native smoke orientation while
            // following each authored attachment's later animation delta.
            if (!Matrix4x4.Decompose(rig.World[40], out _, out var nativeRotation, out var nativePosition)
                || !Matrix4x4.Decompose(bodyRig.World[parents[emitter]], out _, out var parentRotation, out var parentPosition))
                throw new InvalidDataException("Smoke attachment cannot be decomposed.");
            var localRotation = Matrix4x4.CreateFromQuaternion(nativeRotation) * Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(parentRotation));
            rootRotations[emitter] = Quaternion.CreateFromRotationMatrix(localRotation);
            var angles = new[] { MathF.Atan2(localRotation.M23, localRotation.M33), MathF.Asin(Math.Clamp(-localRotation.M13, -1, 1)), MathF.Atan2(localRotation.M12, localRotation.M11) };
            var transform = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateTranslation(parentPosition - nativePosition * Scale);
            for (var j = 0; j < 8; j++)
            {
                var b = bones[j]; var node = source.Tables(source.Root, 9, 512)[b];
                var values = Enumerable.Range(1, 10).ToDictionary(f => f, f => source.Field(node, f) is var at && at != 0
                    ? source.Slice(at, f is >= 5 and <= 9 ? 12 : 4).ToArray() : new byte[f is >= 5 and <= 9 ? 12 : 4]);
                values[1] = ModelTableBuilder.Integer(1); values[2] = ModelTableBuilder.Integer(j == 0 ? parents[emitter] : old[9].Length + emitter * 8 + rig.Bones[b].Parent - 40);
                values[10] = ModelTableBuilder.Integer(1);
                values[5] = ModelTableBuilder.Floats(rig.Bones[b].Scale);
                values[7] = ModelTableBuilder.Floats(j == 0 ? [0, 0, 0] : rig.Bones[b].Translation.Select(v => v * Scale).ToArray());
                if (j == 0) { values[4] = ModelTableBuilder.Integer(0); values[6] = ModelTableBuilder.Floats(angles); }
                var target = writer.Table(11, values, Enumerable.Range(5, 5).ToDictionary(f => f, _ => 12));
                writer.Pointer(slots[9][old[9].Length + emitter * 8 + j], target); writer.Text(target, 0, Name(emitter, rig.Bones[b].Name));
            }
            for (var m = 0; m < 2; m++)
            {
                var material = materials[m];
                var scalarFields = Enumerable.Range(2, 9).Concat(Enumerable.Range(15, 5)).ToArray();
                int Width(int f) => f is 3 or 4 or >= 15 ? 1 : 4;
                var values = scalarFields.Where(f => source.Field(material, f) != 0)
                    .ToDictionary(f => f, f => source.Slice(source.Field(material, f), Width(f)).ToArray());
                values[8] = ModelTableBuilder.Integer(Array.IndexOf(names[3], Strings(source, 3)[source.Value(material, 8)]));
                values[10] = ModelTableBuilder.Integer(Array.IndexOf(names[5], Strings(source, 5)[source.Value(material, 10)]));
                var absent = scalarFields.Where(f => !values.ContainsKey(f))
                    .Concat(new[] { 12, 14, 20 }.Where(f => source.Field(material, f) == 0)).ToHashSet();
                var target = writer.Table(21, values, absent: absent);
                writer.Pointer(slots[6][old[6].Length + emitter * 2 + m], target);
                writer.Text(target, 0, MaterialNames[emitter * 2 + m]);
                writer.Text(target, 1, source.Text(material, 1)!);
                var textureSlots = writer.Vector(target, 11, source.Tables(material, 11, 64).Length);
                foreach (var f in new[] { 12, 14, 20 }.Where(f => !absent.Contains(f)))
                    NativeReference(target, f, source.Table(material, f));
                var parameters = source.Tables(material, 13, 256); var parameterSlots = writer.Vector(target, 13, parameters.Length);
                for (var p = 0; p < parameters.Length; p++)
                {
                    if (source.Text(parameters[p], 0) != "MatLinkID") { nativeReferences.Add((parameterSlots[p], parameters[p])); continue; }
                    var parameter = writer.Table(2, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Floats(emitter) });
                    writer.Pointer(parameterSlots[p], parameter); writer.Text(parameter, 0, "MatLinkID");
                }
                var nativeTextures = source.Tables(material, 11, 64);
                for (var t = 0; t < nativeTextures.Length; t++)
                {
                    var binding = nativeTextures[t];
                    var texture = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(Array.IndexOf(names[2], Strings(source, 2)[source.Value(binding, 1)])) });
                    writer.Pointer(textureSlots[t], texture); writer.Text(texture, 0, source.Text(binding, 0)!);
                    NativeReference(texture, 2, source.Table(binding, 2));
                }
            }
            for (var meshIndex = 0; meshIndex < 3; meshIndex++)
            {
                var mesh = meshes[meshIndex]; var shape = shapes[source.Value(mesh, 1)];
                // Native effect meshes have their own non-skin hierarchy nodes.
                // A skin joint is not a substitute for the renderer's mesh node.
                var meshBone = old[9].Length + 16 + emitter * 3 + meshIndex;
                var nativeMeshBone = source.Tables(source.Root, 9, 512)[source.Value(mesh, 0)];
                var meshNode = writer.Table(11, new Dictionary<int, byte[]> {
                    [2] = ModelTableBuilder.Integer(0), [5] = ModelTableBuilder.Floats(1, 1, 1),
                    [6] = ModelTableBuilder.Floats(0, 0, 0), [7] = ModelTableBuilder.Floats(0, 0, 0),
                    [8] = ModelTableBuilder.Floats(0, 0, 0), [9] = ModelTableBuilder.Floats(0, 0, 0),
                    [10] = ModelTableBuilder.Integer(0),
                }, Enumerable.Range(5, 5).ToDictionary(f => f, _ => 12), absent: new HashSet<int> { 1, 3, 4 });
                writer.Text(meshNode, 0, Name(emitter, source.Text(nativeMeshBone, 0)!));
                writer.Pointer(slots[9][meshBone], meshNode);
                var targetShape = writer.Table(3); var shapeIndex = old[8].Length + emitter * 3 + meshIndex;
                writer.Pointer(slots[8][shapeIndex], targetShape);
                var polygons = source.Tables(shape, 0, 32); var polygonSlots = writer.Vector(targetShape, 0, polygons.Length);
                NativeReference(targetShape, 1, source.Offset(source.Field(shape, 1)));
                for (var p = 0; p < polygons.Length; p++)
                {
                    var originalMaterial = checked((int)source.Value(polygons[p], 0));
                    var localMaterial = Array.IndexOf(materials, allMaterials[originalMaterial]);
                    if (localMaterial < 0) throw new InvalidDataException("Smoke shape uses an undeclared material.");
                    var polygon = writer.Table(2, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(old[6].Length + emitter * 2 + localMaterial) });
                    writer.Pointer(polygonSlots[p], polygon);
                    NativeReference(polygon, 1, source.Offset(source.Field(polygons[p], 1)));
                }
                var (vertices, low, high) = Vertices(shape, transform, bodyRig.Skin.Length + emitter * 8);
                writer.Blob(targetShape, 2, vertices);
                minimum = Vector3.Min(minimum, low); maximum = Vector3.Max(maximum, high);
                var targetMesh = writer.Table(4, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(meshBone), [1] = ModelTableBuilder.Integer(shapeIndex),
                    [2] = ModelTableBuilder.Floats(low.X, low.Y, low.Z, high.X, high.Y, high.Z) }, new Dictionary<int, int> { [2] = 24 }, absent: new HashSet<int> { 3 });
                writer.Pointer(slots[7][old[7].Length + emitter * 3 + meshIndex], targetMesh);
            }
        }
        var nativeCopy = writer.Append(model);
        foreach (var reference in nativeReferences) writer.Pointer(reference.Slot, nativeCopy + reference.Offset);
        writer.Write(writer.Field(root, 1), ModelTableBuilder.Floats(minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z));
        var result = writer.Finish(root); _ = SwShPreviewRig.Read(new(result)); return result;
    }

    private (byte[] Bytes, Vector3 Min, Vector3 Max) Vertices(int shape, Matrix4x4 transform, int skinStart)
    {
        var attributes = new Dictionary<int, (int Format, int Count, int Offset)>(); var stride = 0;
        foreach (var a in source.Tables(shape, 1, 32))
        {
            var kind = source.Field(a, 0) is var ka && ka != 0 ? source.U8(ka) : 0;
            var format = source.Field(a, 1) is var fa && fa != 0 ? source.U8(fa) : 0; var count = (int)source.Value(a, 2);
            attributes.Add(kind, (format, count, stride)); stride += count * (format == 0 ? 4 : format == 1 ? 2 : format is 3 or 8 ? 1 : throw new InvalidDataException("Smoke vertex format is unsupported."));
        }
        var bytes = source.Blob(shape, 2); var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
        if (bytes.Length % stride != 0 || attributes[0] is not (0, 3, _) || attributes[11] is not (3, 4, _))
            throw new InvalidDataException("Native smoke vertex contract is invalid.");
        for (var v = 0; v < bytes.Length; v += stride)
        {
            var at = v + attributes[0].Offset; var point = Vector3.Transform(new Vector3(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4), BitConverter.ToSingle(bytes, at + 8)), transform);
            ModelTableBuilder.Floats(point.X, point.Y, point.Z).CopyTo(bytes, at); min = Vector3.Min(min, point); max = Vector3.Max(max, point);
            foreach (var kind in new[] { 1, 2 })
            {
                if (attributes[kind] is not (1, 4, _)) throw new InvalidDataException("Native smoke tangent contract is invalid.");
                at = v + attributes[kind].Offset;
                float HalfAt(int c) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + c * 2)));
                var normal = Vector3.Normalize(Vector3.TransformNormal(new Vector3(HalfAt(0), HalfAt(1), HalfAt(2)), transform));
                for (var c = 0; c < 3; c++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + c * 2), BitConverter.HalfToUInt16Bits((Half)(c == 0 ? normal.X : c == 1 ? normal.Y : normal.Z)));
            }
            for (var c = 0; c < 4; c++)
            {
                at = v + attributes[11].Offset + c;
                if (bytes[v + attributes[12].Offset + c] == 0) { bytes[at] = 0; continue; }
                var bone = rig.Skin[bytes[at]];
                if (!bones.Contains(bone)) throw new InvalidDataException("Smoke has a non-effect skin joint.");
                bytes[at] = checked((byte)(skinStart + bone - 40));
            }
        }
        return (bytes, min, max);
    }

    public byte[] GlobalLoop(byte[] bodyLoop)
    {
        var body = new ModelBuffer(bodyLoop); var native = new ModelBuffer(loop);
        var clip = PreviewRigReader.Animation(new(idle), "smoke", rig.Bones);
        var frames = checked((int)native.Value(native.Table(native.Root, 0), 1));
        var rate = checked((int)native.Value(native.Table(native.Root, 0), 2));
        var writer = new ModelTableBuilder(); var root = writer.Table(5, absent: new HashSet<int> { 3, 4 });
        writer.Reference(root, 0, writer.Table(3, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(1), [1] = ModelTableBuilder.Integer(frames), [2] = ModelTableBuilder.Integer(rate) }));
        var skeleton = writer.Table(1); writer.Reference(root, 1, skeleton); var slots = writer.Vector(skeleton, 0, 16);
        for (var emitter = 0; emitter < 2; emitter++) for (var j = 0; j < 8; j++)
        {
            var track = clip.Tracks.Single(t => t.Bone == bones[j]); var target = writer.Table(7);
            writer.Pointer(slots[emitter * 8 + j], target); writer.Text(target, 0, Name(emitter, rig.Bones[bones[j]].Name));
            foreach (var field in new[] { 1, 3, 5 })
            {
                var keys = field == 1 ? track.Scale : field == 3 ? track.Rotation : track.Translation;
                var constant = j == 0 || keys.All(k => k.Value.SequenceEqual(keys[0].Value));
                byte[] Value(int frame)
                {
                    var f = frame * clip.Rate / (float)rate % (clip.Frames - 1);
                    var k = keys.MinBy(k => Math.Abs(k.Frame - f))!.Value;
                    if (j == 0) { var q = rootRotations[emitter]; k = field == 1 ? [1, 1, 1] : field == 3 ? [q.X, q.Y, q.Z, q.W] : [0, 0, 0]; }
                    return field == 3 ? SwShAnimationWriter.PackRotation(k) : ModelTableBuilder.Floats(field == 5 ? k.Select(v => v * Scale).ToArray() : k);
                }
                writer.Int32(writer.Field(target, field), constant ? 1 : 2); var curve = writer.Table(1, sizes: constant ? new Dictionary<int, int> { [0] = field == 3 ? 8 : 12 } : null);
                writer.Reference(target, field + 1, curve);
                if (constant) writer.Write(writer.Field(curve, 0), Value(0));
                else writer.Blob(curve, 0, Enumerable.Range(0, frames).SelectMany(Value).ToArray(), frames);
            }
        }
        var bodyTracks = body.Tables(body.Table(body.Root, 2), 0, 128)
            .Where(t => !MaterialNames.Contains(body.Text(t, 0), StringComparer.Ordinal)).ToArray();
        var smokeTracks = new[] { "SmokeCoreA", "SmokeMaskA" }.Select(n => native.Tables(native.Table(native.Root, 2), 0, 128).Single(t => native.Text(t, 0) == n)).ToArray();
        var material = writer.Table(1); writer.Reference(root, 2, material); var targets = writer.Vector(material, 0, bodyTracks.Length + 4);
        var bodyCopy = writer.Append(bodyLoop); for (var i = 0; i < bodyTracks.Length; i++) writer.Pointer(targets[i], bodyCopy + bodyTracks[i]);
        var nativeReferences = new List<(int Slot, int Offset)>();
        for (var emitter = 0; emitter < 2; emitter++)
        {
            for (var m = 0; m < 2; m++)
            {
                var original = smokeTracks[m];
                // An absent native channel is not an offset to the native blob's
                // root. Preserve its absence so the game never treats that root
                // as a vector of material parameter tracks.
                var absent = new[] { 1, 3 }.Where(f => native.Field(original, f) == 0).ToHashSet();
                var target = writer.Table(4, absent: absent);
                writer.Pointer(targets[bodyTracks.Length + emitter * 2 + m], target); writer.Text(target, 0, MaterialNames[emitter * 2 + m]);
                foreach (var field in new[] { 1, 3 }.Where(f => !absent.Contains(f)))
                    nativeReferences.Add((writer.Field(target, field), native.Table(original, field)));
                var scalars = native.Tables(original, 2, 128); var scalarSlots = writer.Vector(target, 2, scalars.Length);
                for (var i = 0; i < scalars.Length; i++)
                {
                    if (native.Text(scalars[i], 0) != "MatLinkID") { nativeReferences.Add((scalarSlots[i], scalars[i])); continue; }
                    var value = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(1) }); writer.Pointer(scalarSlots[i], value); writer.Text(value, 0, "MatLinkID");
                    writer.Reference(value, 2, writer.Table(1, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Floats(emitter) }));
                }
            }
        }
        var nativeCopy = writer.Append(loop);
        foreach (var reference in nativeReferences) writer.Pointer(reference.Slot, nativeCopy + reference.Offset);
        return writer.Finish(root);
    }

    public byte[] Controller(byte[] controller)
    {
        var data = new ModelBuffer(controller); var writer = new ModelTableBuilder(controller);
        foreach (var layer in data.Tables(data.Table(data.Root, 0), 0, 128))
        {
            var global = data.Text(layer, 1) == "gloop01";
            if (global)
            {
                var mask = writer.Table(2, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(1) });
                writer.Reference(data.Table(layer, 2), 0, mask); var slots = writer.Vector(mask, 1, BoneNames.Length);
                for (var i = 0; i < slots.Length; i++) { var bone = writer.Table(1); writer.Pointer(slots[i], bone); writer.Text(bone, 0, BoneNames[i]); }
            }
            foreach (var material in data.Tables(data.Table(data.Table(layer, 2), 1), 1, 128))
            {
                if (!MaterialNames.Contains(data.Text(material, 0), StringComparer.Ordinal)) continue;
                // The loop exclusively owns every smoke material channel. Other
                // actions must not reset its scrolling or paired mask identities.
                foreach (var f in new[] { 1, 2, 3 })
                {
                    var filter = writer.Table(2, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(global ? 0 : 1) });
                    writer.Reference(material, f, filter); writer.Vector(filter, 1, 0);
                }
            }
        }
        return writer.Finish(data.Root);
    }

    private static string Name(int emitter, string native) => "HeatranSmoke" + emitter + native;
    private static string[] Strings(ModelBuffer data, int field)
    { var (at, count) = data.Vector(data.Root, field, 4, 512); return Enumerable.Range(0, count).Select(i => data.StringAt(at + i * 4)).ToArray(); }
    private static void WriteStrings(ModelTableBuilder writer, int root, int field, string[] names)
    { var slots = writer.Vector(root, field, names.Length); for (var i = 0; i < names.Length; i++) writer.Pointer(slots[i], writer.String(names[i])); }
}
