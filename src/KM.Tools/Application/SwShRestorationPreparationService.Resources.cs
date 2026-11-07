// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed class RestorationTexturePool(Dictionary<string, byte[]> members, string folder, bool share)
    {
        private readonly Dictionary<string, string> identities = new(StringComparer.Ordinal);

        internal string Add(string name, byte[] bytes)
        {
            if (share)
            {
                // Compare the entire container, including every mip and sampling field.
                // A palette or material name alone is never sufficient for sharing.
                var neutral = ModelTextureIdentity.Rename(bytes, name, new string('x', name.Length));
                var key = Convert.ToHexString(SHA256.HashData(neutral));
                if (identities.TryGetValue(key, out var existing)) return existing;
                identities.Add(key, name);
            }
            if (!members.TryAdd(folder + name + ".bntx", bytes))
                throw new InvalidDataException("Converted texture identity is duplicated.");
            return name;
        }

        internal string AddAuxiliary(string name, byte[] bytes)
        {
            _ = PreviewTexture.Read(bytes);
            var path = folder + name + ".bntx";
            if (members.TryGetValue(path, out var current))
            {
                if (!current.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Auxiliary texture identity has conflicting contents.");
            }
            else members.Add(path, bytes);
            return name;
        }
    }

    private sealed partial class Preparation
    {
        private static string[] ModelNames(ModelBuffer model, int field)
        {
            var (at, count) = model.Vector(model.Root, field, 4, 256);
            return Enumerable.Range(0, count).Select(i => model.StringAt(at + 4 * i)).ToArray();
        }

        private static byte[] CompactPrograms(byte[] bytes)
        {
            var model = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
            var materials = model.Tables(model.Root, 6, 128);
            foreach (var (list, index) in new[] { (3, 8), (5, 10) })
            {
                var original = ModelNames(model, list);
                string Program(int material)
                {
                    var slot = model.Value(material, index);
                    return slot < original.Length ? original[slot] : throw new InvalidDataException("Native shader index is invalid.");
                }
                var referenced = materials.Select(Program).ToHashSet(StringComparer.Ordinal);
                var names = original.Where(referenced.Contains).ToArray();
                var slots = writer.Vector(model.Root, list, names.Length);
                for (var i = 0; i < names.Length; i++) writer.Pointer(slots[i], writer.String(names[i]));
                foreach (var material in materials)
                {
                    var mapped = Array.IndexOf(names, Program(material));
                    if (model.Field(material, index) != 0) writer.Int32(writer.Field(material, index), mapped);
                    else if (mapped != 0) throw new InvalidDataException("Native implicit shader index cannot be remapped.");
                }
            }
            return writer.Finish(model.Root);
        }

        private static byte[] CompactTextures(byte[] bytes)
        {
            var model = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
            var original = ModelNames(model, 2);
            var bindings = model.Tables(model.Root, 6, 128).SelectMany(m => model.Tables(m, 11, 64)).ToArray();
            int Slot(int binding)
            {
                var slot = checked((int)model.Value(binding, 1));
                return slot < original.Length ? slot : throw new InvalidDataException("Native texture index is invalid.");
            }
            // Bindings can contain roles absent from the selected native template.
            // Retain every actual material reference and its entire image container.
            var used = bindings.Select(Slot).ToHashSet();
            var retained = Enumerable.Range(0, original.Length).Where(used.Contains).ToArray();
            var indices = retained.Select((old, index) => (old, index)).ToDictionary(p => p.old, p => p.index);
            var slots = writer.Vector(model.Root, 2, retained.Length);
            for (var i = 0; i < retained.Length; i++) writer.Pointer(slots[i], writer.String(original[retained[i]]));
            foreach (var binding in bindings)
            {
                var mapped = indices[Slot(binding)];
                if (model.Field(binding, 1) != 0) writer.Int32(writer.Field(binding, 1), mapped);
                else if (mapped != 0) throw new InvalidDataException("Native implicit texture index cannot be remapped.");
            }
            return writer.Finish(model.Root);
        }

        private static Dictionary<string, byte[]> PaletteMembers(Dictionary<string, byte[]> members, string modelPath)
        {
            var model = new ModelBuffer(members[modelPath]); var folder = modelPath[..(modelPath.LastIndexOf('/') + 1)];
            var textures = ModelNames(model, 2).Select(n => folder + n + ".bntx").ToHashSet(StringComparer.Ordinal);
            var shaders = new[] { 3, 5 }.SelectMany(f => ModelNames(model, f)).Distinct(StringComparer.Ordinal)
                .SelectMany(n => new[] { folder + n + ".bnsh_vsh", folder + n + ".bnsh_fsh" }).ToHashSet(StringComparer.Ordinal);
            if (textures.Concat(shaders).Any(path => !members.ContainsKey(path)))
                throw new InvalidDataException("Converted palette resource is missing.");
            var selected = members.Where(pair => pair.Key.EndsWith(".gfbmdl", StringComparison.Ordinal) ? pair.Key == modelPath
                : pair.Key.EndsWith(".bntx", StringComparison.Ordinal) ? textures.Contains(pair.Key)
                : pair.Key.EndsWith(".bnsh_vsh", StringComparison.Ordinal) || pair.Key.EndsWith(".bnsh_fsh", StringComparison.Ordinal) ? shaders.Contains(pair.Key)
                : true).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (Path.GetFileName(modelPath) is "pm0484_01.gfbmdl" or "pm0484_01_rare.gfbmdl")
            {
                // Keep the independent pearl, body and translucent-cover images.
                // Their authored 512px mip chains fit the native model loading
                // pool; retaining five 1024px images exhausts it in Summary.
                var rare = Path.GetFileName(modelPath) == "pm0484_01_rare.gfbmdl";
                foreach (var name in new[] { rare ? "d00484_12_00_body_b_rare_alb" : "c00484_12_00_body_b_alb",
                    rare ? "d10484_12_00_body_a_rare_alb" : "c10484_12_00_body_a_alb",
                    rare ? "pm0484_12_00_body_a_rare_alb" : "pm0484_12_00_body_a_alb",
                    "pm0484_12_00_body_a_lym", "pm0484_12_00_body_c_alb" })
                {
                    var path = folder + name + ".bntx";
                    if (!selected.TryGetValue(path, out var bytes))
                        throw new InvalidDataException("Origin Palkia layered texture is missing.");
                    var texture = new ModelTextureDocument(bytes);
                    if (texture.Width != 1024 || texture.Height != 1024 || texture.MipCount != 11)
                        throw new InvalidDataException("Origin Palkia layered texture dimensions changed.");
                    selected[path] = SwShTextureAllocation.MipTail(bytes, 1);
                }
            }
            if (Path.GetFileName(modelPath) is "pm0720_01.gfbmdl" or "pm0720_01_rare.gfbmdl")
            {
                // Unbound's four body maps otherwise exhaust the model loader
                // pool even when opened first in a fresh Summary session.
                // Preserve each independent image using its authored mip tail.
                var bodyMaps = textures.Where(path =>
                    Path.GetFileName(path) is "a00774_12_00_body_a_amb.bntx" or "a30774_12_00_body_b_amb.bntx"
                    or "c00774_12_00_body_a_alb.bntx" or "c30774_12_00_body_b_alb.bntx"
                    or "b00774_12_00_body_a_rare_amb.bntx" or "b30774_12_00_body_b_rare_amb.bntx"
                    or "d00774_12_00_body_a_rare_alb.bntx" or "d30774_12_00_body_b_rare_alb.bntx").ToArray();
                if (bodyMaps.Length != 4) throw new InvalidDataException("Hoopa Unbound body texture set changed.");
                foreach (var path in bodyMaps)
                {
                    var texture = new ModelTextureDocument(selected[path]);
                    if (texture.Width != 1024 || texture.Height != 1024 || texture.MipCount != 11)
                        throw new InvalidDataException("Hoopa Unbound body texture dimensions changed.");
                    selected[path] = SwShTextureAllocation.MipTail(selected[path], 1);
                }
            }
            SwShRestorationResourceBudget.LimitLayeredMegaTextures(selected, textures, modelPath);
            SwShRestorationResourceBudget.Validate(selected);
            return selected;
        }

        private static PreviewScene CompactGeometry(PreviewScene scene) => scene with
        {
            Primitives = scene.Primitives.Select(primitive =>
            {
                // Material sections can each expose the whole shared source buffer.
                // Preserve every triangle corner and skin attribute, omitting only unused vertices.
                var used = primitive.Indices.Distinct().Order().ToArray();
                var remap = used.Select((source, index) => (source, index))
                    .ToDictionary(pair => pair.source, pair => (uint)pair.index);
                return primitive with
                {
                    Vertices = used.SelectMany(index => primitive.Vertices.Skip(checked((int)index * 16)).Take(16)).ToArray(),
                    SecondaryUv = primitive.SecondaryUv is null ? null : used.SelectMany(index => primitive.SecondaryUv.Skip(checked((int)index * 2)).Take(2)).ToArray(),
                    Color0 = primitive.Color0 is null ? null : used.SelectMany(index => primitive.Color0.Skip(checked((int)index * 4)).Take(4)).ToArray(),
                    Tangent0 = primitive.Tangent0 is null ? null : used.SelectMany(index => primitive.Tangent0.Skip(checked((int)index * 4)).Take(4)).ToArray(),
                    Tangent1 = primitive.Tangent1 is null ? null : used.SelectMany(index => primitive.Tangent1.Skip(checked((int)index * 4)).Take(4)).ToArray(),
                    Indices = primitive.Indices.Select(index => remap[index]).ToArray(),
                };
            }).ToArray(),
        };
    }
}
