// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private sealed record BodyLayout(string Stem, string Material, string[] Meshes, bool Mirror,
        float UvX = 1, string Program = "SSS", float[]? Initial = null, bool AnimatedColors = false);

    private static IReadOnlyDictionary<string, Contract>? BodyDonorContracts(byte[] bytes, PreviewScene scene)
    {
        // These surfaces use four ordinary mask layers, with no initial emission.
        // Keep their exact bindings separate from eyes and transparent surfaces.
        var layouts = new BodyLayout[]
        {
            new("pm1004_11_00", "body_a_01", ["tail"], false),
            new("pm0523_00_00", "body_a_00", ["eyelid", "body"], false),
            new("pm0523_00_00", "body_b_00", ["hair", "body"], true),
            new("pm0160_51_00", "body_b", ["arm_a", "arm_b", "body"], true, 2, "IkCharacter"),
            new("pm0248_51_00", "body_b_00", ["body"], true, 2, "IkCharacter"),
            new("pm0354_51_00", "body_b_01", ["body"], true, 2, "IkCharacter"),
            new("pm0354_51_00", "body_b_02", ["suit"], true, 2, "IkCharacter"),
            new("pm0373_51_00", "body_a_01", ["body"], true, 2, "IkCharacter"),
            new("pm0373_51_00", "body_b", ["bodyarmor_a", "bodyarmor_b", "teeth", "body"], true, 2, "IkCharacter"),
            new("pm0475_51_00", "body_b_01", ["right_arm2"], false, 1, "IkCharacter", [1, 0, 1, 0]),
            new("pm0705_51_00", "body_b", ["body_c"], false, 1, "IkCharacter"),
            new("pm0705_51_00", "body_a_00", ["body_a", "body_b"], true, 2, "IkCharacter"),
            new("pm0705_51_00", "body_a_02", ["body_a"], true, 2, "IkCharacter"),
            new("pm0705_51_00", "body_a_01", ["left_nail_a", "right_nail_a", "left_nail_b", "right_nail_b"], true, 2, "IkCharacter"),
            new("pm0772_51_00", "body_a", ["pm0772_00_00_body_mesh_shape"], true, 2, "IkCharacter"),
            new("pm0772_51_00", "body_c", ["innerskirt_a", "veil_left_a", "veil_right_a"], true, 2, "IkCharacter"),
            new("pm0323_51_00", "body_b_02", ["body_b"], true, 2, "IkCharacter", [0, 0, .7f, 0], true),
            new("pm0323_51_00", "body_b_00", ["body_b"], true, 2, "IkCharacter", [0, .7f, .8f, 0], true),
            new("pm0491_51_00", "body_b_00", ["head", "body"], false, 1, "IkCharacter", [0, .0010050139f, .0010050139f, 0]),
            new("pm0491_51_00", "body_b_01", ["neck_b", "body", "foot_a"], false, 1, "IkCharacter", [.0010050139f, .0010050139f, .0010050139f, .0010050139f]),
            new("pm0491_51_00", "body_b_03", ["neck_c"], false, 1, "IkCharacter", [1, 1, 1, 1], true),
            new("pm0491_51_00", "body_b_02", ["neck_c", "foot_b"], false, 1, "IkCharacter", [1, 0, 1, 1], true),
            new("pm0888_51_00", "body_b_02", ["body"], true, 2, "IkCharacter"),
            new("pm0888_51_00", "body_b_00", ["body", "hair_a"], true, 2, "IkCharacter"),
            new("pm0888_51_00", "body_b_01", ["body"], true, 2, "IkCharacter"),
            new("pm0888_51_00", "body_a_00", ["body", "tail"], true, 2, "IkCharacter"),
            new("pm0888_51_00", "body_a_01", ["body"], true, 2, "IkCharacter"),
            new("pm0888_51_00", "body_a_02", ["feeler", "horn", "blade", "hair_b", "hair_c"], true, 2, "IkCharacter"),
            new("pm0923_51_00", "body_b", ["shield_b", "shield_a", "horn_b", "horn_a", "foot_f", "foot_e", "foot_d", "foot_c", "foot_b", "foot_a", "body_c", "body_b"], true, 2, "IkCharacter"),
            new("pm0923_51_00", "body_a", ["body_f", "body_e", "body_d", "body_a", "arm_a", "arm_b"], false, 1, "IkCharacter"),
        };
        var matches = layouts.Where(layout => scene.Primitives.Any(p => p.Name.StartsWith(layout.Stem + "_", StringComparison.Ordinal))).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Select(layout => layout.Stem).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidDataException("Body emission source identity is ambiguous.");
        var result = matches.ToDictionary(layout => layout.Material, layout => BodyDonorContract(bytes, scene, layout), StringComparer.Ordinal);
        if (matches[0].Stem == "pm0475_51_00") result.Add("eff_slashing", GalladeSlashContract(bytes, scene));
        return result;
    }

    private static Contract BodyDonorContract(byte[] bytes, PreviewScene scene,
        BodyLayout layout)
    {
        var name = layout.Material;
        var meshes = layout.Meshes.Select(mesh => mesh.StartsWith("pm", StringComparison.Ordinal)
            ? mesh : layout.Stem + "_" + mesh + "_mesh_shape");
        if (!scene.Primitives.Where(p => p.Material.Name == name).Select(p => p.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(meshes.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Body emission geometry requires another adapter.");
        var source = new ModelBuffer(bytes);
        var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
        var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!,
            p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
        var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
        {
            var at = source.Field(p, 1);
            return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
        });
        if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual([layout.Program])
            || vectors["UVScaleOffset"] != new Vector4(layout.UvX, 1, 0, 0) || scalars["EmissionIntensity"] != 0
            || Enumerable.Range(1, 4).Any(layer => scalars["LayerMaskScale" + layer] != 1 || scalars["EmissionIntensityLayer" + layer] != (layout.Initial?[layer - 1] ?? 0))
            || source.Tables(material, 2, 64).Any(t => source.Text(t, 0) == "EmissionColorMap"))
            throw new InvalidDataException("Body emission surface requires another adapter.");
        var colors = Enumerable.Range(1, 4).Select(layer => vectors["EmissionColorLayer" + layer]).ToArray();
        if (colors.Any(c => !float.IsFinite(c.X + c.Y + c.Z + c.W) || c.X < 0 || c.X > 2
            || c.Y < 0 || c.Y > 2 || c.Z < 0 || c.Z > 2 || c.W != 1))
            throw new InvalidDataException("Body emission color exceeds its supported range.");
        var texture = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "LayerMaskMap");
        var selector = source.Tables(material, 9, 256).SingleOrDefault(t => source.Text(t, 0) == "UVIndexLayerMask");
        var uvIndex = selector == 0 ? uint.MaxValue : source.Value(selector, 1);
        var samplers = source.Tables(material, 3, 32); var sampler = checked((int)source.Value(texture, 2));
        if ((uint)sampler >= samplers.Length) throw new InvalidDataException("Body emission sampler is missing.");
        var wrap = new Vector2(source.Value(samplers[sampler], 9), source.Value(samplers[sampler], 10));
        if (uvIndex != uint.MaxValue || wrap != new Vector2(layout.Mirror ? 6 : 1, 1))
            throw new InvalidDataException("Body emission mask coordinates require another adapter.");
        var sourcePalette = layout.Stem is "pm0373_51_00" or "pm0491_51_00" or "pm0888_51_00" or "pm0923_51_00"
            || layout.Stem == "pm0705_51_00" && name.StartsWith("body_a_", StringComparison.Ordinal);
        var bases = sourcePalette ? Enumerable.Range(1, 4).Select(i => vectors["BaseColorLayer" + i]).ToArray() : null;
        var matteCrescent = layout.Stem == "pm0373_51_00" && name == "body_b";
        if (matteCrescent)
        {
            var identity = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            if (identity is not ("7260C3CFC9863C9551D3E2E3671B8136B1701BD5EBA0085B5AAF260173063CBC" or "EAF970DA45424403A9B0872B8F75CB0940BC40E5B02B5123DD0136861AF02291")
                || scalars["SpecularLayer1Intensity"] != .01f)
                throw new InvalidDataException("Salamence's crescent no longer has its qualified matte source response.");
        }
        if (bases is not null && (vectors["BaseColor"] != Vector4.One
            || bases.Any(c => !float.IsFinite(c.X + c.Y + c.Z + c.W) || c.X is < 0 or > 2 || c.Y is < 0 or > 2 || c.Z is < 0 or > 2)))
            throw new InvalidDataException("Body source palette requires its qualified white global tint and finite layer colors.");
        if (sourcePalette)
        {
            var baseMap = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "BaseColorMap");
            var baseSampler = checked((int)source.Value(baseMap, 2));
            if ((uint)baseSampler >= samplers.Length || source.Value(samplers[baseSampler], 9) != wrap.X || source.Value(samplers[baseSampler], 10) != wrap.Y)
                throw new InvalidDataException("Body source palette addressing differs from its layer mask.");
        }
        string? occlusion = null;
        if (layout.Stem == "pm0705_51_00" && name == "body_b")
        {
            var identity = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            if (identity is not ("F0FAA5497CE602B67E830BB557125DA4F4EB11FF27CB4CA1A7FD47AA98AD5464" or "88E001810589C93CCC0E56512077AD19DAD6AF6E0E281AEC2454824E2B56494A"))
                throw new InvalidDataException("Pyroar source occlusion palette identity changed.");
            var ao = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "OcclusionMap");
            var aoSampler = checked((int)source.Value(ao, 2));
            var aoUv = source.Tables(material, 9, 256).Single(t => source.Text(t, 0) == "UVIndexAO");
            if (source.Text(ao, 1) != "pm0705_51_00_body_b_ao.bntx" || source.Value(aoUv, 1) != uint.MaxValue
                || aoSampler >= samplers.Length || source.Value(samplers[aoSampler], 9) != 6 || source.Value(samplers[aoSampler], 10) != 1)
                throw new InvalidDataException("Pyroar source occlusion coordinates changed.");
            occlusion = source.Text(ao, 1);
        }
        return new(name, source.Text(texture, 1)!, colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray())
            { SourceOcclusion = occlusion, MaskWrap = wrap, MaskUv = new(layout.UvX, 1, 0, 0), MirroredMask = layout.Mirror,
                Initial = layout.Initial ?? new float[4], AnimatedColors = layout.AnimatedColors,
                MaximumIntensity = layout.Stem == "pm0160_51_00" ? 2 : 1,
                BaseColors = bases?.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray(),
                SharedColorUv = sourcePalette && layout.Mirror && layout.UvX == 2,
                MatteCrescent = matteCrescent };
    }

}
