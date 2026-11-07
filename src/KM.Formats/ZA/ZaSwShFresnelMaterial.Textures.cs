// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

public static partial class ZaSwShFresnelMaterial
{
    public const string SamplerRuntimeRequirement = "source-clamp-samplers-v1";
    private static readonly string[] NativeSamplers = ["Col0Tex", "EmissionMaskTex", "LyCol0Tex", "AmbientTex",
        "NormalMapTex", "LightTblTex", "SphereMapTex", "EffectTex"];

    public sealed record TextureBinding(string SourceRole, string NativeRole, string FileName, int Slot,
        int AddressU, int AddressV, int AddressW, Vector4 Border, bool Cube);

    // Source sampler fields0..8 and13+ are absent in every qualified palette.
    // Native ZA parsing establishes linear/linear-mipmap filtering, unit
    // anisotropy, zero bias, disabled compare and the original white border.
    // Address0 requires the independently guarded executable extension.
    public static IReadOnlyList<TextureBinding> TextureBindings(byte[] bytes, string materialName, bool masked)
    {
        _ = Read(bytes, materialName);
        var source = new ModelBuffer(bytes); var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == materialName);
        var maps = new[] { ("BaseColorMap", "SourceGemBaseTex", 8), ("BaseColorMap1", "SourceGemLayerTex", 9),
            ("NormalMap", "SourceGemNormalTex", 10), ("NormalMap1", "SourceGemLayerNormalTex", 11),
            ("AOMap", "SourceGemAoTex", 12), ("LocalSpecularProbe", "SourceGemProbeTex", 13) }.ToList();
        if (masked) maps.Add(((QualifiesCrabominableEmissionMask(bytes, materialName)
            || QualifiesFroslassEmissionMask(bytes, materialName)
            || QualifiesGlalieEmissionMask(bytes, materialName)
            || QualifiesAbomasnowEmissionMask(bytes, materialName))
            ? "FresnelMaskMap" : "MaskedEmissionMap", "SourceGemMaskTex", 14));
        var textures = source.Tables(material, 2, 32); var samplers = source.Tables(material, 3, 32);
        if (!textures.Select(t => source.Text(t, 0)).Order(StringComparer.Ordinal)
            .SequenceEqual(maps.Select(m => m.Item1).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("ZA gem source texture roles require another adapter.");
        var result = new List<TextureBinding>();
        foreach (var (role, native, slot) in maps)
        {
            var texture = textures.Single(t => source.Text(t, 0) == role); var index = source.Value(texture, 2);
            if (index >= samplers.Length) throw new InvalidDataException("ZA gem source sampler index is invalid.");
            var sampler = samplers[index]; var vt = sampler - source.I32(sampler); var vtLength = source.U16(vt); var size = source.U16(vt + 2);
            if (vtLength is < 4 or > 516 || vtLength % 2 != 0 || size < 4)
                throw new InvalidDataException("ZA gem source sampler layout is invalid.");
            int fields = (vtLength - 4) / 2;
            if (Enumerable.Range(0, fields).Any(f => (f < 9 || f > 12) && source.Field(sampler, f) != 0)
                || source.Value(sampler, 11) != 0)
                throw new InvalidDataException("ZA gem source filtering controls require another adapter.");
            var border = source.Field(sampler, 12);
            if (border == 0 || border + 16 > sampler + size || Enumerable.Range(0, 4).Any(c => source.Float(border + c * 4) != 1))
                throw new InvalidDataException("ZA gem requires its authored white sampler border.");
            var file = source.Text(texture, 1);
            if (string.IsNullOrWhiteSpace(file)) throw new InvalidDataException("ZA gem source texture identity is missing.");
            int Address(int field) => source.Value(sampler, field) switch
            {
                0 => 4, 1 => 0, 6 => 2, 7 => 1,
                _ => throw new InvalidDataException("ZA gem source address mode requires another adapter.")
            };
            result.Add(new(role, native, file, slot, Address(9), Address(10), 4, Vector4.One, role == "LocalSpecularProbe"));
        }
        return result;
    }

    // Names must come from the actual prepared texture pool. Every new sampler
    // receives an explicit image identity; no temporary image0 binding is made.
    // The returned model requires SamplerRuntimeRequirement before installation.
    public static byte[] TemplateWithTextures(byte[] bytes, byte[] sourceMaterials, string materialName,
        int layerUv, int aoUv, int sourceVariant, int maskUv, IReadOnlyDictionary<string, string> textureNames)
    {
        var bindings = TextureBindings(sourceMaterials, materialName, sourceVariant >= 6).ToArray();
        var template = Template(bytes, sourceMaterials, materialName, layerUv, aoUv, sourceVariant, maskUv);
        var source = new ModelBuffer(template); var material = MaterialForProgram(source, Program(layerUv, aoUv, sourceVariant, maskUv));
        return RewriteTextures(template, [new(material, bindings, textureNames)]);
    }

    public static byte[] BindTextures(byte[] bytes, byte[] sourceMaterials, IReadOnlyDictionary<string, string> originalNames,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> textureNamesBySourceMaterial)
    {
        bytes = BindParameters(bytes, sourceMaterials, originalNames);
        var source = new ModelBuffer(bytes); var (start, count) = source.Vector(source.Root, 3, 4, 256);
        var targets = new List<MaterialTextures>(); var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            var index = source.Value(material, 8);
            if (index >= count) throw new InvalidDataException("ZA gem texture program index is invalid.");
            var program = source.StringAt(start + checked((int)index) * 4);
            if (!Programs.Contains(program)) continue;
            if (!originalNames.TryGetValue(source.Text(material, 0)!, out var original)
                || !textureNamesBySourceMaterial.TryGetValue(original, out var names))
                throw new InvalidDataException("ZA gem textures require their original material and prepared image identities.");
            var bindings = TextureBindings(sourceMaterials, original, program.StartsWith("ZaSourceFresnel6L", StringComparison.Ordinal)
                || program.StartsWith("ZaSourceFresnel8L", StringComparison.Ordinal)).ToArray();
            var rows = source.Tables(material, 11, 32);
            if (rows.Length != NativeSamplers.Length + bindings.Length
                || !rows.Skip(NativeSamplers.Length).Select(t => source.Text(t, 0)).SequenceEqual(bindings.Select(b => b.NativeRole)))
                throw new InvalidDataException("ZA gem source sampler declarations are missing or reordered.");
            targets.Add(new(material, bindings, names)); consumed.Add(original);
        }
        if (targets.Count == 0 || !consumed.Order(StringComparer.Ordinal).SequenceEqual(textureNamesBySourceMaterial.Keys.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("ZA gem textures have missing or undeclared material consumers.");
        return RewriteTextures(bytes, targets);
    }

    public static bool RequiresClampRuntime(byte[] bytes)
    {
        var source = new ModelBuffer(bytes);
        var (vertexStart, vertexCount) = source.Vector(source.Root, 3, 4, 256);
        var (fragmentStart, fragmentCount) = source.Vector(source.Root, 5, 4, 256);
        var (_, textureCount) = source.Vector(source.Root, 2, 4, 4096);
        bool found = false;
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            var vertex = source.Value(material, 8); var fragment = source.Value(material, 10);
            if (vertex >= vertexCount || fragment >= fragmentCount)
                throw new InvalidDataException("ZA gem sampler program index is invalid.");
            var name = source.StringAt(vertexStart + checked((int)vertex) * 4);
            var fragmentName = source.StringAt(fragmentStart + checked((int)fragment) * 4);
            if (name.StartsWith("ZaSourceFresnel", StringComparison.Ordinal) && !Programs.Contains(name)
                || fragmentName.StartsWith("ZaSourceFresnel", StringComparison.Ordinal) && !Programs.Contains(fragmentName))
                throw new InvalidDataException("ZA gem sampler program selectors are unqualified.");
            if (!Programs.Contains(name) && !Programs.Contains(fragmentName)) continue;
            if (name != fragmentName) throw new InvalidDataException("ZA gem samplers require their paired program.");
            bool masked = name.StartsWith("ZaSourceFresnel6L", StringComparison.Ordinal)
                || name.StartsWith("ZaSourceFresnel8L", StringComparison.Ordinal);
            var ownNames = SwShZaFresnelSamples.Samplers(masked).Select(s => s.Name);
            var rows = source.Tables(material, 11, 32);
            if (rows.Length != (masked ? 15 : 14)
                || !rows.Take(8).Select(t => source.Text(t, 0)).SequenceEqual(NativeSamplers)
                || !rows.Skip(8).Select(t => source.Text(t, 0)).SequenceEqual(ownNames))
                throw new InvalidDataException("ZA gem source samplers are incomplete or reordered.");
            foreach (var row in rows.Skip(8))
            {
                var sampler = source.Table(row, 2);
                if (source.Value(row, 1) >= textureCount)
                    throw new InvalidDataException("ZA gem source image index is invalid.");
                if (sampler == 0 || source.Value(sampler, 3) != 4
                    || source.Value(sampler, 1) > 4 || source.Value(sampler, 2) > 4
                    || source.Value(sampler, 1) == 3 || source.Value(sampler, 2) == 3)
                    throw new InvalidDataException("ZA gem source clamp ABI is missing.");
            }
            found = true;
        }
        return found;
    }

    private sealed record MaterialTextures(int Material, TextureBinding[] Bindings, IReadOnlyDictionary<string, string> Names);
    internal static byte[] BindSourceTextures(byte[] bytes, int material, TextureBinding[] bindings,
        IReadOnlyDictionary<string, string> names)
        => RewriteTextures(bytes, [new(material, bindings, names)]);
    private static byte[] RewriteTextures(byte[] bytes, IReadOnlyList<MaterialTextures> targets)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var (nameStart, nameCount) = source.Vector(source.Root, 2, 4, 4096);
        var names = Enumerable.Range(0, nameCount).Select(i => source.StringAt(nameStart + i * 4)).ToList();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            throw new InvalidDataException("ZA gem native texture identities are ambiguous.");
        foreach (var target in targets)
        {
            if (!target.Names.Keys.Order(StringComparer.Ordinal).SequenceEqual(target.Bindings.Select(b => b.SourceRole).Order(StringComparer.Ordinal)))
                throw new InvalidDataException("ZA gem requires an explicit prepared image for every source sampler.");
            foreach (var name in target.Names.Values)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Contains('\0')) throw new InvalidDataException("ZA gem prepared texture identity is invalid.");
                if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
            }
        }
        var nameSlots = writer.Vector(source.Root, 2, names.Count);
        for (var i = 0; i < names.Count; i++) writer.Pointer(nameSlots[i], writer.String(names[i]));
        foreach (var target in targets)
        {
            var original = source.Tables(target.Material, 11, 32);
            if (original.Length < NativeSamplers.Length || !original.Take(8).Select(t => source.Text(t, 0)).SequenceEqual(NativeSamplers))
                throw new InvalidDataException("ZA gem requires its pinned native sampler prefix.");
            var slots = writer.Vector(target.Material, 11, 8 + target.Bindings.Length);
            for (var i = 0; i < 8; i++)
            {
                var row = original[i]; var at = source.Field(row, 1); bool absent = at == 0;
                var rowVt = row - source.I32(row); int rowFields = (source.U16(rowVt) - 4) / 2;
                if (Enumerable.Range(3, Math.Max(0, rowFields - 3)).Any(f => source.Field(row, f) != 0))
                    throw new InvalidDataException("ZA gem native texture row has unqualified fields.");
                var clone = writer.Table(3, absent ? null : new Dictionary<int, byte[]> { [1] = source.Slice(at, 4).ToArray() },
                    absent: absent ? new HashSet<int> { 1 } : null);
                writer.Pointer(slots[i], clone); writer.Text(clone, 0, source.Text(row, 0)!);
                writer.Reference(clone, 2, CloneSampler(writer, source, source.Table(row, 2)));
            }
            foreach (var binding in target.Bindings)
            {
                var index = names.IndexOf(target.Names[binding.SourceRole]);
                var row = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(index) });
                writer.Pointer(slots[binding.Slot], row); writer.Text(row, 0, binding.NativeRole);
                var sampler = writer.Table(6, new Dictionary<int, byte[]>
                {
                    [1] = ModelTableBuilder.Integer(binding.AddressU), [2] = ModelTableBuilder.Integer(binding.AddressV),
                    [3] = ModelTableBuilder.Integer(binding.AddressW), [4] = ModelTableBuilder.Floats(binding.Border.X, binding.Border.Y, binding.Border.Z, binding.Border.W)
                }, new Dictionary<int, int> { [4] = 16 }, new HashSet<int> { 0, 5 });
                writer.Reference(row, 2, sampler);
            }
        }
        return writer.Finish(source.Root);
    }

    internal static int CloneSampler(ModelTableBuilder writer, ModelBuffer source, int sampler)
    {
        if (sampler == 0) throw new InvalidDataException("ZA gem native sampler is missing.");
        var vt = checked(sampler - source.I32(sampler)); var vtLength = source.U16(vt); var size = source.U16(vt + 2);
        if (vtLength is < 4 or > 22 || vtLength % 2 != 0 || size < 4)
            throw new InvalidDataException("ZA gem native sampler layout requires another adapter.");
        for (var f = 0; f < (vtLength - 4) / 2; f++)
        {
            var at = source.Field(sampler, f);
            if (at != 0 && at + (f == 4 ? 16 : 4) > sampler + size)
                throw new InvalidDataException("ZA gem native sampler inline field is invalid.");
        }
        // All known fields are inline scalars or the16-byte border struct.
        // Copy full body/vtable/padding, adjusting only the vtable distance.
        var tableMetadata = writer.Append(source.Slice(vt, vtLength), 2);
        var clone = writer.Append(source.Slice(sampler, size)); writer.Int32(clone, clone - tableMetadata); return clone;
    }
}
