// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Adapts separate transparent surfaces to the native two sided cell shader.</summary>
public static class SvSwShTransparentMaterial
{
    public static readonly string[] Programs = ["TransparentFront", "TransparentBack"];
    public static readonly string[] NativePrograms = ["BodyUniran_Cell_Front", "BodyUniran_Cell_Back"];
    public const string EnvironmentName = "sphere_0577a_env";

    public static byte[] Template(byte[] model, byte[] nativeCell)
    {
        var body = new ModelBuffer(model); var source = new ModelBuffer(nativeCell); var writer = new ModelTableBuilder(model);
        var materials = body.Tables(body.Root, 6, 128);
        var selected = NativePrograms.Select(name => source.Tables(source.Root, 6, 128).Single(t => source.Text(t, 0) == name)).ToArray();
        if (selected.Any(t => source.Text(t, 1) != "PokeDefaultShader" || source.Value(t, 6) != 5))
            throw new InvalidDataException("Native transparent material requires the qualified alpha blend contract.");
        var entries = writer.Vector(body.Root, 6, materials.Length + selected.Length); var original = writer.Append(model);
        for (var i = 0; i < materials.Length; i++) writer.Pointer(entries[i], original + materials[i]);
        var mapped = new Dictionary<(int Pass, int Field), int>();
        foreach (var field in new[] { 3, 5 })
        {
            var programField = field == 3 ? 8 : 10;
            var (start, count) = body.Vector(body.Root, field, 4, 256);
            var names = Enumerable.Range(0, count).Select(i => body.StringAt(start + i * 4)).ToArray();
            if (names.Intersect(Programs, StringComparer.Ordinal).Any()) throw new InvalidDataException("Transparent program identity is already allocated.");
            var slots = writer.Vector(body.Root, field, count + selected.Length);
            for (var i = 0; i < names.Length; i++) writer.Pointer(slots[i], writer.String(names[i]));
            var (nativeStart, nativeCount) = source.Vector(source.Root, field, 4, 256);
            for (var i = 0; i < selected.Length; i++)
            {
                if (source.Field(selected[i], programField) == 0)
                    throw new InvalidDataException("Native transparent program binding is missing.");
                var index = checked((int)source.Value(selected[i], programField));
                if ((uint)index >= nativeCount || source.StringAt(nativeStart + index * 4) != NativePrograms[i])
                    throw new InvalidDataException("Native transparent program binding is invalid.");
                writer.Pointer(slots[count + i], writer.String(Programs[i]));
                mapped.Add((i, programField), count + i);
            }
        }
        var copy = writer.Append(nativeCell);
        for (var i = 0; i < selected.Length; i++)
        {
            writer.Pointer(entries[materials.Length + i], copy + selected[i]);
            foreach (var field in new[] { 8, 10 }) writer.Int32(copy + source.Field(selected[i], field), mapped[(i, field)]);
            var over = source.Tables(selected[i], 13, 256).Single(p => source.Text(p, 0) == "Layer1OverLerpValue");
            var at = source.Field(over, 1); var value = at == 0 ? 0 : source.Float(at);
            if (value != i) throw new InvalidDataException("Native transparent layer mixing changed.");
            var depthWrite = source.Field(selected[i], 3);
            if ((depthWrite == 0 ? 0 : source.U8(depthWrite)) != i)
                throw new InvalidDataException("Native transparent depth writing changed.");
            // The inner cell retains its native opaque underlay and depth write.
            // Inner() supplies inward faces, matching the native shell geometry.
        }
        return writer.Finish(body.Root);
    }

    public static PreviewPrimitive Inner(PreviewPrimitive primitive)
    {
        var vertices = (float[])primitive.Vertices.Clone();
        var indices = (uint[])primitive.Indices.Clone();
        for (var i = 0; i < vertices.Length; i += 16)
            for (var component = 3; component < 6; component++) vertices[i + component] = -vertices[i + component];
        for (var i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        return primitive with { Vertices = vertices, Indices = indices, Name = primitive.Name + "_inner",
            Material = primitive.Material with { Name = primitive.Material.Name + "_inner" } };
    }

    public static byte[] GlobalLoop(byte[] animation, IReadOnlySet<string> materials)
    {
        var source = new ModelBuffer(animation); var writer = new ModelTableBuilder(animation);
        var tracks = source.Tables(source.Table(source.Root, 2), 0, 128);
        if (materials.Count == 0 || materials.Any(name => tracks.Count(t => source.Text(t, 0) == name) != 1))
            throw new InvalidDataException("Transparent global material targets are missing or ambiguous.");
        // The foreign global loop resets every material to Squirtle's defaults.
        // Separate static corneas must retain their own UV, alpha and reflection
        // settings, including the full density atlas and native inner pass.
        foreach (var track in tracks.Where(t => materials.Contains(source.Text(t, 0)!)))
            foreach (var field in new[] { 1, 2, 3 }) writer.Vector(track, field, 0);
        return writer.Finish(source.Root);
    }

    public static void RequireSource(byte[] bytes, PreviewScene scene, bool deoxys = false)
    {
        var source = new ModelBuffer(bytes);
        var dialga = scene.Primitives.Any(p => p.Name.StartsWith("pm0483_12_00_", StringComparison.Ordinal));
        var palkia = scene.Primitives.Any(p => p.Name.StartsWith("pm0484_12_00_", StringComparison.Ordinal));
        if (dialga || palkia)
        {
            var stem = dialga ? "pm0483_12_00" : "pm0484_12_00";
            var name = dialga ? "body_b_01" : "body_a_02";
            var surfaces = scene.Primitives.Where(p => p.Material.Name == name).ToArray();
            var material = source.Tables(source.Root, 1, 128).Single(t => source.Text(t, 0) == name);
            if (scene.Primitives.Any(p => !p.Name.StartsWith(stem + "_", StringComparison.Ordinal))
                || surfaces.Length != 1 || surfaces[0].Name != stem + (dialga ? "_gem_mesh_shape" : "_cover_mesh_shape")
                || surfaces[0].Vertices.Length != (dialga ? 81 : 218) * 16 || surfaces[0].Indices.Length != (dialga ? 174 : 1152)
                || surfaces[0].Material.Alpha != "BlendPreMultiAlpha"
                || !source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["Transparent", "TransparentInner"]))
                throw new InvalidDataException("Origin transparent surface requires its qualified source geometry and two passes.");
            return;
        }
        if (deoxys)
        {
            var name = scene.Primitives.Any(p => p.Name.StartsWith("pm0386_11_00_", StringComparison.Ordinal)) ? "body_b_02" : "body_a_02";
            var qualified = scene.Primitives.All(p => p.Name.StartsWith("pm0386_", StringComparison.Ordinal));
            var surface = source.Tables(source.Root, 1, 128).Single(t => source.Text(t, 0) == name);
            if (!qualified || !source.Tables(surface, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["Transparent", "TransparentInner"])
                || scene.Primitives.Count(p => p.Material.Name == name && p.Material.Alpha == "BlendPreMultiAlpha") != 1)
                throw new InvalidDataException("The Deoxys transparent surface requires another conversion adapter.");
            return;
        }
        foreach (var (name, shaders) in new[] { ("l_eye_b", new[] { "Transparent", "TransparentInner" }), ("l_highlight", new[] { "Transparent" }) })
        {
            var material = source.Tables(source.Root, 1, 128).Single(t => source.Text(t, 0) == name);
            if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(shaders)
                || scene.Primitives.Count(p => p.Material.Name == name && p.Material.Alpha == "BlendPreMultiAlpha") != 2)
                throw new InvalidDataException("The transparent source surfaces need another conversion adapter.");
        }
    }
}
