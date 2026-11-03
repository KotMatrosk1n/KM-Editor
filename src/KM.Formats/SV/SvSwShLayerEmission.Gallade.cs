// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private const string SlashMesh = "pm0475_51_00_eff_mesh_shape";

    private static Contract GalladeSlashContract(byte[] bytes, PreviewScene scene)
    {
        // Both complete material palettes were audited: Add, IkCharacterTransparent,
        // black diffuse, one enabled emission mask channel, no specular or AO.
        // Pin the donor graph as well as the geometry; other additive surfaces
        // need their own qualification, including their blend and mask equations.
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var rare = hash == "C6D859D72AA7F5738E2192A61D2E0C6CEBB4B3D25E6C90F134064B1214D98BE6";
        if (!rare && hash != "32946C970AABAF0CDEF5133ADC971B7C243F81C1B9A72143A8C5E67E80447461"
            || !scene.Primitives.Where(p => p.Material.Name == "eff_slashing")
                .Select(p => (p.Name, p.Material.Alpha)).SequenceEqual([(SlashMesh, "Add")]))
            throw new InvalidDataException("Mega Gallade additive donor requires another adapter.");
        var color = rare ? new Vector3(.822785f, .254152f, .0451856f) : new Vector3(.7835445f, .056129687f, .12214059f);
        return new("eff_slashing", "pm0475_51_00_eff_lym.bntx", [color, Vector3.Zero, Vector3.Zero, Vector3.Zero])
        { Additive = true, MaskScale = new(1, 0, 0, 0), MaskWrap = Vector2.One, Initial = [1, 0, 0, 0] };
    }

    private static void RequireGalladeSlash(Contract c)
    {
        if (!c.Additive || c.Name != "eff_slashing" || Path.GetFileName(c.Mask) != "pm0475_51_00_eff_lym.bntx"
            || c.MaskScale != new Vector4(1, 0, 0, 0) || c.MaskUv != new Vector4(1, 1, 0, 0)
            || c.MaskWrap != Vector2.One || c.Transparent || c.ColorMap || c.BaseColorMap || c.MaskUnused
            || c.FresnelNormal is not null || c.FresnelNormal1 is not null || c.FresnelMirrorOnce || c.FresnelWraps is not null
            || c.Lava || c.MirroredMask || c.AnimatedColors || c.BaseColors is not null || c.Unlit
            || c.MaximumIntensity != 1 || !c.Initial.SequenceEqual([1f, 0, 0, 0])
            || c.Colors.Length != 4 || c.Colors.Skip(1).Any(v => v != Vector3.Zero)
            || c.Colors[0] != new Vector3(.822785f, .254152f, .0451856f)
                && c.Colors[0] != new Vector3(.7835445f, .056129687f, .12214059f))
            throw new InvalidDataException("Mega Gallade additive contract is invalid.");
    }

    public static SwShBakedMaterial AdditiveColor(Contract contract, PreviewPrimitive primitive, SwShBakedMaterial baked,
        byte[] sourceMaterials, Func<string, byte[]> read, CancellationToken token = default, ModelTextureMipCache? encodingCache = null)
    {
        RequireGalladeSlash(contract);
        var qualified = GalladeSlashContract(sourceMaterials, new([primitive], []));
        if (!qualified.Colors.SequenceEqual(contract.Colors) || baked.ColorUvTransform != new Vector4(1, 1, 0, 0))
            throw new InvalidDataException("Mega Gallade additive palette or coordinates differ.");
        var source = new ModelBuffer(sourceMaterials);
        var material = source.Tables(source.Root, 1, 128).Single(t => source.Text(t, 0) == contract.Name);
        var color = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "BaseColorMap");
        var path = source.Text(color, 1)!;
        if (Path.GetFileName(path) != "pm0475_51_00_eff_alb.bntx")
            throw new InvalidDataException("Mega Gallade additive alpha texture is unqualified.");
        var authored = new ModelTextureDocument(read(path));
        var target = new ModelTextureDocument(SwShTextureAllocation.Create(baked.Color, authored.Width, authored.Height, true, compressed: false));
        if (target.MipCount != authored.MipCount)
            throw new InvalidDataException("Mega Gallade additive alpha mip chain is incomplete.");
        var mips = Enumerable.Range(0, authored.MipCount).Select(mip =>
        {
            token.ThrowIfCancellationRequested();
            var pixels = authored.Pixels(mip);
            // The audited diffuse is black; the shader adds the independently
            // sampled emission mask. Preserve each authored alpha byte in RGBA.
            for (var i = 0; i < pixels.Length; i += 4) pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
            return pixels;
        }).ToArray();
        return baked with { Color = target.EncodeMipImages(mips, token, encodingCache) };
    }

    private static PreviewMaterialTrack ReadSlashAlpha(ModelBuffer source, int vector, string mesh, Contract contract, float rate)
    {
        RequireGalladeSlash(contract);
        if (mesh != SlashMesh || source.Text(vector, 0) != "BaseColor")
            throw new InvalidDataException("Mega Gallade additive animation target is invalid.");
        var channels = source.Table(vector, 1);
        var keys = Enumerable.Range(0, 4).Select(c => ReadColorKeys(source, source.Table(channels, c), rate, false)).ToArray();
        if (keys.Take(3).Any(c => c.Any(k => k.Value[0] != 0)) || keys[3].Any(k => k.Value[0] is < 0 or > 1))
            throw new InvalidDataException("Mega Gallade additive animation requires another adapter.");
        return new(contract.Name, "SurfaceAlpha", [keys[3]]);
    }

    private static byte[] BindAdditive(byte[] bytes, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, Contract> contracts)
    {
        if (!contracts.Values.Any(c => c.Additive)) return bytes;
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var materials = source.Tables(source.Root, 6, 128); var slots = writer.Vector(source.Root, 6, materials.Length);
        var replacements = new Dictionary<int, int>();
        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (!names.TryGetValue(source.Text(material, 0)!, out var original)
                || !contracts.TryGetValue(original, out var contract) || !contract.Additive)
                continue;
            RequireGalladeSlash(contract);
            if (source.Value(material, 6) != 0) throw new InvalidDataException("Additive native body template is not opaque.");
            var fields = Enumerable.Range(2, 9).Concat(Enumerable.Range(15, 5));
            var absent = fields.Where(f => f is not (3 or 6) && source.Field(material, f) == 0).ToHashSet();
            var values = fields.Where(f => !absent.Contains(f)).ToDictionary(f => f, f => f == 3 ? new byte[] { 0 }
                : f == 6 ? ModelTableBuilder.Integer(5)
                : source.Slice(source.Field(material, f), f is 3 or 4 or >= 15 ? 1 : 4).ToArray());
            var target = writer.Table(21, values, absent: absent); writer.Pointer(slots[i], target);
            replacements.Add(material, target);
        }
        // Create replacement tables before their shared immutable subgraphs;
        // native offsets may only point forward.
        var copy = writer.Append(bytes);
        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (!replacements.TryGetValue(material, out var target))
            { writer.Pointer(slots[i], copy + material); continue; }
            foreach (var f in new[] { 0, 1, 11, 12, 13, 14, 20 }) writer.Reference(target, f, copy + source.Table(material, f));
            var alpha = source.Tables(material, 13, 256).Single(p => source.Text(p, 0) == "ConstantAlpha");
            if (materials.Where(m => m != material).Any(m => source.Tables(m, 13, 256).Contains(alpha)))
                throw new InvalidDataException("Additive opacity is shared with another material.");
            // Resting donor clips all hide this effect. Camp retargeting also
            // supplies zero; attack clips carry their own authored opacity.
            writer.Single(copy + source.Field(alpha, 1), 0);
        }
        return writer.Finish(source.Root);
    }
}
