// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Text;

namespace KM.Formats.Models;

public sealed record ModelMaterialChange(string Key, double[] Values, string? Text = null);
public sealed record ModelMaterialField(string Key, string Material, string Group, string Name,
    string Kind, double[] Values, string? Text, string[] Options, bool Editable, bool Previewed);

/// <summary>Edits declared material values without rebuilding unrelated model metadata.</summary>
public sealed class ModelMaterialDocument
{
    private sealed record FieldLocation(int Table, int Reference, int Field, int Components, bool Named);
    private sealed record Binding(ModelMaterialField Field, int Address, FieldLocation? Location = null);
    private readonly byte[] original;
    private readonly List<Binding> bindings = [];
    public IReadOnlyList<ModelMaterialField> Fields => bindings.Select(b => b.Field).ToArray();

    public ModelMaterialDocument(byte[] bytes, bool packedModel)
    {
        original = bytes.ToArray();
        var data = new ModelBuffer(original);
        var textureNames = Array.Empty<string>();
        if (packedModel)
        {
            var (start, count) = data.Vector(data.Root, 2, 4, 256);
            textureNames = Enumerable.Range(0, count).Select(i => data.StringAt(start + i * 4)).ToArray();
        }
        var materials = data.Tables(data.Root, packedModel ? 6 : 1, 256);
        for (var m = 0; m < materials.Length; m++)
        {
            var table = materials[m];
            var material = data.Text(table, 0) ?? throw new InvalidDataException("Material name is missing.");
            void Numeric(string key, string group, string name, string kind, int at, int components, double fallback = 0, string[]? options = null, FieldLocation? location = null)
            {
                if (at != 0 && location is not null && at + components * 4 > location.Table + data.U16(location.Table - data.I32(location.Table) + 2))
                    throw new InvalidDataException("Material value extends beyond its table.");
                var values = Enumerable.Range(0, components).Select(i => at == 0 ? fallback : kind == "int" ? data.I32(at + i * 4) : data.Float(at + i * 4)).ToArray();
                if (values.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Material contains a nonfinite value.");
                var previewed = PreviewSurface.Supports(name) || name is "BaseColor" or "ConstantColor0" or "UVScaleOffset" or "UVScaleOffset1"
                    or "BaseColorLayer1" or "BaseColorLayer2" or "BaseColorLayer3" or "BaseColorLayer4"
                    or "Col0SkinColor" or "Col0PrimaryColor" or "Col0SecondaryColor"
                    || name.StartsWith("ColorUV", StringComparison.Ordinal) || name.StartsWith("Layer1UV", StringComparison.Ordinal)
                    || name is "LayerMaskScale1" or "LayerMaskScale2" or "LayerMaskScale3" or "LayerMaskScale4" or "EmissionIntensityLayer5" or "EmissionColorLayer5"
                        or "ColorBaseU" or "ColorBaseV" or "Layer1BaseU" or "Layer1BaseV";
                bindings.Add(new(new(key, material, group, name, kind, values, null, options ?? [], at != 0 || location is not null, previewed), at, location));
            }
            void Parameters(int field, string group, int components, string kind, double fallback = 0)
            {
                var parameters = data.Tables(table, field, 256);
                var (start, _) = data.Vector(table, field, 4, 256);
                for (var p = 0; p < parameters.Length; p++)
                    Numeric($"{m}/{field}/{p}", group, data.Text(parameters[p], 0) ?? $"{p + 1}", kind,
                        data.Field(parameters[p], 1), components, fallback, location: new(parameters[p], start + p * 4, 1, components, true));
            }
            Parameters(packedModel ? 13 : 4, "parameters", 1, "float");
            Parameters(packedModel ? 14 : 7, "colors", packedModel ? 3 : 4, "vector");
            if (!packedModel)
            {
                Parameters(6, "lighting", 4, "vector");
                Parameters(9, "flags", 1, "int", -1);
                var shaders = data.Tables(table, 1, 32);
                for (var s = 0; s < shaders.Length; s++)
                    bindings.Add(new(new($"{m}/shader/{s}", material, "shader", "Shader", "text", [], data.Text(shaders[s], 0), [], false, false), 0));
            }
            var textures = data.Tables(table, packedModel ? 11 : 2, 32);
            var paths = packedModel ? textureNames : textures.Select(t => data.Text(t, 1)).OfType<string>().Distinct().ToArray();
            for (var t = 0; t < textures.Length; t++)
            {
                var binding = textures[t]; var role = data.Text(binding, 0) ?? $"{t + 1}";
                var at = data.Field(binding, 1);
                var index = packedModel ? checked((int)data.Value(binding, 1)) : 0;
                if (packedModel && index >= paths.Length) throw new InvalidDataException("Texture binding is invalid.");
                bindings.Add(new(new($"{m}/texture/{t}", material, "textures", role, packedModel ? "textureIndex" : "texture",
                    packedModel ? [index] : [], packedModel ? paths[index] : data.Text(binding, 1), paths, at != 0, RenderedTexture(role)), at));
                if (packedModel && data.Table(binding, 2) is var sampler && sampler != 0)
                {
                    Numeric($"{m}/wrap/{t}/u", "samplers", role + " U", "int", data.Field(sampler, 1), 1, options: ["0", "1", "2"], location: new(sampler, data.Field(binding, 2), 1, 1, false));
                    Numeric($"{m}/wrap/{t}/v", "samplers", role + " V", "int", data.Field(sampler, 2), 1, options: ["0", "1", "2"], location: new(sampler, data.Field(binding, 2), 2, 1, false));
                }
            }
            if (!packedModel)
            {
                var samplers = data.Tables(table, 3, 32);
                var (samplerStart, _) = data.Vector(table, 3, 4, 32);
                for (var s = 0; s < samplers.Length; s++)
                {
                    for (var axis = 0; axis < 3; axis++)
                        Numeric($"{m}/sampler/{s}/{axis}", "samplers", $"{s + 1} {"UVW"[axis]}", "int", data.Field(samplers[s], 9 + axis), 1, options: ["0", "1", "6", "7"], location: new(samplers[s], samplerStart + s * 4, 9 + axis, 1, false));
                    Numeric($"{m}/sampler/{s}/border", "samplers", $"{s + 1} Border", "vector", data.Field(samplers[s], 12), 4, location: new(samplers[s], samplerStart + s * 4, 12, 4, false));
                }
                var at = data.Field(table, 15); var alpha = data.Text(table, 15);
                bindings.Add(new(new($"{m}/alpha", material, "surface", "Alpha", "text", [], alpha,
                    new[] { "Opaque", "Mask", "Blend", alpha }.OfType<string>().Distinct().ToArray(), at != 0, true), at));
            }
            for (var i = 0; i < bindings.Count; i++)
            {
                var field = bindings[i].Field;
                if (field.Material != material || field.Group != "samplers") continue;
                bool rendered;
                if (packedModel) rendered = RenderedTexture(field.Name[..^2]);
                else
                {
                    var parts = field.Key.Split('/'); var slot = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    rendered = parts[3] is "0" or "1" && textures.Any(t => data.Value(t, 2) == slot && RenderedTexture(data.Text(t, 0) ?? ""));
                }
                if (rendered) bindings[i] = bindings[i] with { Field = field with { Previewed = true } };
            }
        }
        if (bindings.Count > 16384) throw new InvalidDataException("Material control budget exceeded.");
    }

    private static bool RenderedTexture(string role) => PreviewSurface.TextureSlot(role) >= 0
        || role is "BaseColorMap" or "Col0Tex" or "LyCol0Tex" or "LayerMaskMap" or "HighlightMaskMap" or "Col0ColChangeTex";

    public byte[] Apply(IReadOnlyList<ModelMaterialChange> changes)
    {
        if (changes.Count > 512 || changes.Select(c => c.Key).Distinct().Count() != changes.Count)
            throw new InvalidDataException("Material changes are duplicated or exceed the editing budget.");
        var output = original.ToList();
        var moved = new Dictionary<int, Dictionary<int, int>>();
        var data = new ModelBuffer(original);
        foreach (var group in changes.Select(c => bindings.SingleOrDefault(b => b.Field.Key == c.Key))
            .OfType<Binding>().Where(b => b.Location is not null).GroupBy(b => b.Location!.Reference))
        {
            var missing = group.Where(b => b.Address == 0 && !changes.Single(c => c.Key == b.Field.Key).Values.SequenceEqual(b.Field.Values)).ToArray();
            if (missing.Length == 0) continue;
            var location = missing[0].Location!;
            var vt = location.Table - data.I32(location.Table);
            var oldLength = data.U16(vt); var objectLength = data.U16(vt + 2);
            // Named parameters contain a name reference and one inline value.
            // Samplers contain only numeric fields and inline border colors.
            if (oldLength > (location.Named ? 8 : 30)) throw new InvalidDataException("Material default has an unknown layout.");
            var vtLength = Math.Max(oldLength, 4 + (missing.Max(b => b.Location!.Field) + 1) * 2);
            var vtable = new byte[vtLength]; data.Slice(vt, oldLength).CopyTo(vtable);
            var body = data.Slice(location.Table, objectLength).ToArray().ToList();
            foreach (var binding in missing)
            {
                while ((body.Count & 3) != 0) body.Add(0);
                BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(4 + binding.Location!.Field * 2), checked((ushort)body.Count));
                body.AddRange(new byte[binding.Location.Components * 4]);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(vtable, checked((ushort)vtLength));
            BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(2), checked((ushort)body.Count));
            while ((output.Count & 3) != 0) output.Add(0);
            var newVt = output.Count; output.AddRange(vtable);
            while ((output.Count & 3) != 0) output.Add(0);
            var newTable = output.Count; output.AddRange(body);
            Write(newTable, BitConverter.GetBytes(newTable - newVt));
            Write(location.Reference, BitConverter.GetBytes(checked((uint)(newTable - location.Reference))));
            if (location.Named && data.Field(location.Table, 0) is var nameAt && nameAt != 0)
            {
                while ((output.Count & 3) != 0) output.Add(0);
                var nameStart = output.Count; var text = Encoding.UTF8.GetBytes(data.StringAt(nameAt));
                output.AddRange(BitConverter.GetBytes(text.Length)); output.AddRange(text); output.Add(0);
                var slot = newTable + nameAt - location.Table;
                Write(slot, BitConverter.GetBytes(checked((uint)(nameStart - slot))));
            }
            moved.Add(location.Reference, Enumerable.Range(0, (vtLength - 4) / 2)
                .ToDictionary(f => f, f => BinaryPrimitives.ReadUInt16LittleEndian(vtable.AsSpan(4 + f * 2)) is var offset && offset != 0 ? newTable + offset : 0));
        }
        foreach (var change in changes)
        {
            var binding = bindings.SingleOrDefault(b => b.Field.Key == change.Key) ?? throw new InvalidDataException("Material control no longer exists.");
            var field = binding.Field;
            if (!field.Editable || change.Values is null) throw new InvalidDataException("Material control is read-only.");
            var address = binding.Location is { } loc && moved.TryGetValue(loc.Reference, out var addresses) ? addresses.GetValueOrDefault(loc.Field) : binding.Address;
            if (field.Kind is "text" or "texture")
            {
                if (change.Values.Length != 0 || change.Text is null || !field.Options.Contains(change.Text)) throw new InvalidDataException("Select an existing material value.");
                if (change.Text == field.Text) continue;
                // Append a new string and redirect only this reference, preserving shared strings.
                while ((output.Count & 3) != 0) output.Add(0);
                var start = output.Count; var text = Encoding.UTF8.GetBytes(change.Text);
                output.AddRange(BitConverter.GetBytes(text.Length)); output.AddRange(text); output.Add(0);
                Write(address, BitConverter.GetBytes(checked((uint)(start - address))));
            }
            else if (field.Kind == "textureIndex")
            {
                var index = Array.IndexOf(field.Options, change.Text);
                if (index < 0 || change.Values.Length != 0) throw new InvalidDataException("Select an existing texture binding.");
                Write(address, BitConverter.GetBytes(index));
            }
            else
            {
                if (change.Text is not null || change.Values.Length != field.Values.Length || change.Values.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1_000_000))
                    throw new InvalidDataException("Material numeric value is invalid.");
                for (var i = 0; i < change.Values.Length; i++)
                {
                    var value = change.Values[i];
                    if (address == 0 && value == field.Values[i]) continue;
                    if (address == 0) throw new InvalidDataException("Material value cannot be stored.");
                    if (field.Kind == "int")
                    {
                        if (value != Math.Truncate(value) || (field.Options.Length > 0 && !field.Options.Contains(value.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                            throw new InvalidDataException("Material enumeration is invalid.");
                        Write(address + i * 4, BitConverter.GetBytes(checked((int)value)));
                    }
                    else Write(address + i * 4, BitConverter.GetBytes((float)value));
                }
            }
        }
        return output.ToArray();
        void Write(int at, byte[] bytes) { for (var i = 0; i < bytes.Length; i++) output[at + i] = bytes[i]; }
    }
}
