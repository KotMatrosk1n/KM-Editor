// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Formats.SV;

/// <summary>The two-opacity, constant-color display eyes in the qualified SV palettes.</summary>
public static class SvSwShFlipbookEye
{
    public const string Program = "SvFlipbookEye2";
    public const string PanelProgram = "SvFlipbookEyePanel2";
    public static bool Recognizes(string? program) => program is Program or PanelProgram;

    public static IReadOnlyDictionary<string, ZaSwShDisplacedUnlit.Contract> Contracts(byte[] bytes, PreviewScene scene)
    {
        var identity = Convert.ToHexString(SHA256.HashData(bytes));
        var layout = identity switch
        {
            "837188B00DB789B09CDA7F924F31CBE65D2A36573F7F146DEBE591C89034528A" or
            "25B468494D71CFDE51AE401E58E1257B5A95206CFFA81ECE9C0C1A60C93D399B" => ("pm1090_00_00", 25, 96, 2, 4),
            "A356E22899CF3D239010843616786B7B8699216AFC9ECD68B196D1852197AE07" or
            "81DA841B5557F8FB48CD9A9117A144E9E254BE3A523661164149CBE1AC3596B1" => ("pm1096_00_00", 21, 66, 1, 1),
            "74E8E0E4A5A50CD835CA070669EE8C21D241A0BBA19617DA53C64268F069AB27" or
            "1F62B2E5B83FAF869F456A76574217DFDA62FE717CD7727938272B088FE43C79" => ("pm1093_00_00", 18, 66, 1, 1),
            "92BB7C6B8E2CA86BCA8DA937A456168636A4C1B2575F616D229FA72B40FA4448" or
            "56CF38BFB73E4904BE7FE631600BB407B2860EFD9BC407AFA4D1CD6CB7F33C69" => ("pm1092_00_00", 25, 102, 1, 1),
            _ => default,
        };
        var result = new Dictionary<string, ZaSwShDisplacedUnlit.Contract>(StringComparer.Ordinal);
        if (layout.Item1 is null) return result;
        var source = new ModelBuffer(bytes);
        foreach (var name in new[] { "l_eye", "r_eye" })
        {
            var primitive = scene.Primitives.Single(p => p.Material.Name == name);
            if (primitive.Name != layout.Item1 + "_" + name + "_mesh_shape"
                || primitive.Vertices.Length != layout.Item2 * 16 || primitive.Indices.Length != layout.Item3
                || primitive.Indices.Distinct().Count() != layout.Item2)
                throw new InvalidDataException("Source display eye geometry changed.");
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
            var shaders = source.Tables(material, 1, 16);
            if (shaders.Length != 1 || source.Text(shaders[0], 0) != "Unlit" || source.Text(material, 15) != "Blend")
                throw new InvalidDataException("Source display eye pass changed.");
            var options = source.Tables(shaders[0], 1, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1));
            foreach (var (option, value) in new Dictionary<string, string> {
                ["EnableBaseColorMap"] = "False", ["EnableOpacityMap"] = "True", ["EnableOpacityMap1"] = "True",
                ["EnableFlipBookUV"] = "True", ["EnableAlphaTest"] = "False", ["NumMaterialLayer"] = "2" })
                if (options.GetValueOrDefault(option) != value) throw new InvalidDataException("Source display eye shader options changed.");
            var scalars = source.Tables(material, 4, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(t => source.Text(t, 0)!, t => {
                var at = source.Field(t, 1); return Enumerable.Range(0, 4).Select(c => source.Float(at + c * 4)).ToArray(); });
            var selectors = source.Tables(material, 9, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Value(t, 1));
            if (scalars["DiscardValue"] != 0 || scalars["LayerMaskScale1"] != 1 || selectors["FlipBookInvertU"] != 0
                || scalars["FlipBookWidth"] != layout.Item4 || scalars["FlipBookHeight"] != layout.Item5)
                throw new InvalidDataException("Source display eye flipbook changed.");
            var values = new float[ZaSwShDisplacedUnlit.FlipbookNames.Length];
            void Scalar(string key) => values[ZaSwShDisplacedUnlit.ParameterIndex(key)] = scalars[key];
            void Vector(string key, string target) => vectors[key].CopyTo(values, ZaSwShDisplacedUnlit.ParameterIndex(target));
            foreach (var key in new[] { "EmissionIntensity", "LayerMaskScale1", "FlipBookWidth", "FlipBookHeight", "FlipBookFrame" }) Scalar(key);
            Vector("BaseColor", "BaseColor"); Vector("UVScaleOffset", "UVScaleOffset"); Vector("UVScaleOffset1", "UVScaleOffset3");
            if (values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Source display eye parameters are not finite.");
            var samplers = source.Tables(material, 3, 32);
            var rows = source.Tables(material, 2, 32);
            string[] roles = ["OpacityMap", "OpacityMap1"];
            if (!rows.Select(t => source.Text(t, 0)).ToHashSet().SetEquals(roles)) throw new InvalidDataException("Source display eye texture roles changed.");
            var textures = roles.Select((role, i) => {
                var row = rows.Single(t => source.Text(t, 0) == role); var slot = source.Value(row, 2);
                if (slot >= samplers.Length) throw new InvalidDataException("Source display eye sampler is missing.");
                var sampler = samplers[slot];
                if (source.Value(sampler, 9) != 1 || source.Value(sampler, 10) != 1 || source.Value(sampler, 11) != 0)
                    throw new InvalidDataException("Source display eye sampler changed.");
                return new ZaSwShFresnelMaterial.TextureBinding(role, "SourceUnlit" + role, source.Text(row, 1)!, 8 + i, 0, 0, 4, Vector4.One, false);
            }).ToArray();
            // Treads and Moth have dark display panels beneath separate glass.
            // Their masks control illumination while authored alpha covers the
            // panel, including its unlit pixels.
            result.Add(name, new(name, values, textures) { Shader = layout.Item1 is "pm1090_00_00" or "pm1092_00_00" ? PanelProgram : Program });
        }
        return result;
    }
}
