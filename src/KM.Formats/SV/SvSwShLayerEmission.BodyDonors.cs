// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SV;

public static partial class SvSwShLayerEmission
{
    private static IReadOnlyDictionary<string, Contract>? BodyDonorContracts(byte[] bytes, PreviewScene scene)
    {
        // These surfaces use four ordinary mask layers, with no initial emission.
        // Keep their exact bindings separate from eyes and transparent surfaces.
        var layouts = new (string Stem, string Material, string[] Meshes, bool Mirror)[]
        {
            ("pm1004_11_00", "body_a_01", ["tail"], false),            ("pm0523_00_00", "body_a_00", ["eyelid", "body"], false),
            ("pm0523_00_00", "body_b_00", ["hair", "body"], true),        };
        var matches = layouts.Where(layout => scene.Primitives.Any(p => p.Name.StartsWith(layout.Stem + "_", StringComparison.Ordinal))).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Select(layout => layout.Stem).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidDataException("Body emission source identity is ambiguous.");
        return matches.ToDictionary(layout => layout.Material, layout => BodyDonorContract(bytes, scene, layout), StringComparer.Ordinal);
    }

    private static Contract BodyDonorContract(byte[] bytes, PreviewScene scene,
        (string Stem, string Material, string[] Meshes, bool Mirror) layout)
    {
        var name = layout.Material;
        var meshes = layout.Meshes.Select(mesh => layout.Stem + "_" + mesh + "_mesh_shape");
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
        if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["SSS"])
            || vectors["UVScaleOffset"] != new Vector4(1, 1, 0, 0) || scalars["EmissionIntensity"] != 0
            || Enumerable.Range(1, 4).Any(layer => scalars["LayerMaskScale" + layer] != 1 || scalars["EmissionIntensityLayer" + layer] != 0)
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
        return new(name, source.Text(texture, 1)!, colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray())
            { MaskWrap = wrap, MirroredMask = layout.Mirror };
    }

}
