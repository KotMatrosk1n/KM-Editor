// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private static void SetMealVerticalOffset(byte[] data, float offset)
        {
            if (!float.IsFinite(offset)) throw new InvalidDataException("Camp meal vertical offset must be finite.");
            var metadata = new ModelBuffer(data); var table = metadata.Root;
            foreach (var field in new[] { 1, 0, 0 })
            {
                table = metadata.Table(table, field);
                if (table == 0) throw new InvalidDataException("Native camp meal framing is missing.");
            }
            var at = metadata.Field(table, 1);
            if (at == 0 || !float.IsFinite(metadata.Float(at)))
                throw new InvalidDataException("Native camp meal vertical offset is invalid.");
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), offset);
        }

        private static string[] ModelNames(ModelBuffer model, int field)
        {
            var (at, count) = model.Vector(model.Root, field, 4, 256);
            return Enumerable.Range(0, count).Select(i => model.StringAt(at + 4 * i)).ToArray();
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
            SwShRestorationResourceBudget.Validate(selected);
            return selected;
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
                var names = original.Where(referenced.Contains).ToArray(); var slots = writer.Vector(model.Root, list, names.Length);
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
            // Conversion inputs can include roles absent from the native material.
            // Retain every actual binding and its complete image container.
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
    }
}
