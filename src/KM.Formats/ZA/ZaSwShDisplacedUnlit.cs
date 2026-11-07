// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;
using KM.Formats.SV;

namespace KM.Formats.ZA;

/// <summary>Source Unlit parameters, independent of native lighting uniforms.</summary>
public static class ZaSwShDisplacedUnlit
{
    public const string Program = "ZaDisplacedUnlit5";
    public static readonly string[] Programs = [Program, Program + "Uv1", "ZaDisplacedUnlit6", "ZaDisplacedUnlit6Uv1", "ZaDisplacedUnlit6Cutout"];
    public static bool Recognizes(string? name) => name is not null && Programs.Contains(name);
    public static bool SixLayers(string name) => Recognizes(name) && name.StartsWith("ZaDisplacedUnlit6", StringComparison.Ordinal);
    public static readonly string[] Scalars = ["EmissionIntensity", "DisplacementHeight", "LayerMaskScale1", "LayerMaskScale2", "LayerMaskScale3", "LayerMaskScale4"];
    public static readonly string[] Vectors = ["BaseColor", "BaseColorLayer1", "BaseColorLayer2", "BaseColorLayer3", "BaseColorLayer4", "UVScaleOffset", "UVScaleOffset3"];
    public static readonly string[] Names = [.. Scalars.Select(Name), .. Vectors.SelectMany(v => Enumerable.Range(0, 4).Select(c => Name(v) + c)),
        Name("LayerMaskScale5"), .. Enumerable.Range(0, 4).Select(c => Name("BaseColorLayer5") + c),
        .. Enumerable.Range(0, 4).Select(c => Name("UVScaleOffset5") + c), Name("DiscardValue")];
    public static string Name(string parameter) => "SourceUnlit" + parameter;
    public static int ParameterIndex(string parameter, int component = 0)
    {
        var scalar = Array.IndexOf(Scalars, parameter);
        if (scalar >= 0) return scalar;
        var vector = Array.IndexOf(Vectors, parameter);
        if (vector >= 0) return Scalars.Length + vector * 4 + component;
        return parameter switch { "LayerMaskScale5" => 34, "BaseColorLayer5" => 35 + component, "UVScaleOffset5" => 39 + component, "DiscardValue" => 43, _ => -1 };
    }
    public sealed record Contract(string Material, float[] Values, ZaSwShFresnelMaterial.TextureBinding[] Textures)
    {
        public string Shader { get; init; } = Program;
        public bool SecondaryUv => SixLayers(Shader) || Shader.EndsWith("Uv1", StringComparison.Ordinal);
    }

    public static IReadOnlyDictionary<string, Contract> Contracts(byte[] bytes)
    {
        var identity = Convert.ToHexString(SHA256.HashData(bytes));
        if (identity is not ("2CDA6E85ED88A7E1EF015994C09DDBDF44A68B21A709D0543E4C9F4DEF6EDAE0"
            or "BEF33DF56A6083744572D7E055A1976138D568B0CADDE5155E52AD0665B5193F"
            or "4FE24EFF0C058A47681910FAA1DF367ECB9A1C0E549809DD28D219493E8BEA46"
            or "74261F7901C9092EAAA617EED97A069079A4553425911E44A10D36C09ACF4955"
            or "AECABA5327C72072A7CF8A39A111CD0B716AD5CFFB257D14280C7A4D7E3C3A39"
            or "D4FE89E36AC9B5D660BABEF9B6876948CFA24DB4DEF5C5E0C8551B31591A9C5A"
            or "858AAF123C05A90715DD7AEF3AC08B97AFF9ADAF9C003A04ED2CE7D121848ED6"
            or "BFCCDEEA623B1505C0AEE39D3829DB15E31069FB1304A7529CC558E2235AE0CC"
            or "88089BAD7B9B1F9BDF62D28A183956B3A72274890584FA03269F3C4F598334E7"
            or "FFAE009E67D31DB1D2768953ACF318AEC72602966003529ADEBC1DF1FEDBBCE8"))
            return new Dictionary<string, Contract>(StringComparer.Ordinal);
        var source = new ModelBuffer(bytes);
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["Unlit"])) continue;
            if (source.Text(material, 15) != "Opaque") throw new InvalidDataException("Source Unlit requires its opaque pass.");
            var scalars = source.Tables(material, 4, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(t => source.Text(t, 0)!, t =>
            {
                var at = source.Field(t, 1);
                return Enumerable.Range(0, 4).Select(c => source.Float(at + c * 4)).ToArray();
            });
            var options = source.Tables(source.Tables(material, 1, 16).Single(), 1, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1));
            var six = options["NumMaterialLayer"] == "6";
            var cutout = options["EnableAlphaTest"] == "True";
            var selectors = source.Tables(material, 9, 256).ToDictionary(t => source.Text(t, 0)!, t => source.Value(t, 1));
            var displacementUv = selectors["UVIndexLayer3"];
            if (displacementUv > 1 || six && selectors["UVIndexLayer5"] != 1 || !six && options["NumMaterialLayer"] != "5"
                || cutout && (!six || displacementUv != 0))
                throw new InvalidDataException("Source Unlit layer coordinates changed.");
            if (!scalars.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Scalars.Append("DiscardValue").Concat(six ? ["LayerMaskScale5"] : []))
                || !vectors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Vectors.Concat(six ? ["BaseColorLayer5", "UVScaleOffset5"] : [])))
                throw new InvalidDataException("Source Unlit parameter interface changed.");
            var values = Scalars.Select(n => scalars[n]).Concat(Vectors.SelectMany(n => vectors[n]))
                .Concat(new[] { scalars.GetValueOrDefault("LayerMaskScale5") }).Concat(vectors.GetValueOrDefault("BaseColorLayer5", [1, 1, 1, 1]))
                .Concat(vectors.GetValueOrDefault("UVScaleOffset5", [1, 1, 0, 0])).Append(scalars["DiscardValue"]).ToArray();
            if (values.Any(v => !float.IsFinite(v)) || scalars["DisplacementHeight"] is < 0 or > .1f
                || scalars["EmissionIntensity"] != 1 || Scalars.Skip(2).Any(n => scalars[n] != 1)
                || scalars.GetValueOrDefault("LayerMaskScale5") is < 0 or > 1 || scalars["DiscardValue"] != (cutout ? .5f : 0))
                throw new InvalidDataException("Source Unlit values require another adapter.");
            var samplers = source.Tables(material, 3, 32);
            var textures = source.Tables(material, 2, 32);
            string[] roles = ["BaseColorMap", "LayerMaskMap", "DisplacementMap", .. six ? new[] { "OpacityMap" } : []];
            if (!textures.Select(t => source.Text(t, 0)).ToHashSet().SetEquals(roles))
                throw new InvalidDataException("Source Unlit texture roles changed.");
            var bindings = roles.Select((role, i) =>
            {
                var texture = textures.Single(t => source.Text(t, 0) == role);
                var index = source.Value(texture, 2);
                if (index >= samplers.Length) throw new InvalidDataException("Source Unlit sampler index is invalid.");
                var sampler = samplers[index];
                if (source.Value(sampler, 9) is not (1 or 6) || source.Value(sampler, 10) != 1 || source.Value(sampler, 11) != 0)
                    throw new InvalidDataException("Source Unlit requires its authored repeat samplers.");
                return new ZaSwShFresnelMaterial.TextureBinding(role, "SourceUnlit" + role, source.Text(texture, 1)!, 8 + i,
                    source.Value(sampler, 9) == 6 ? 2 : 0, 0, 4, Vector4.One, false);
            }).ToArray();
            var name = source.Text(material, 0)!;
            result.Add(name, new(name, values, bindings) { Shader = six ? cutout ? "ZaDisplacedUnlit6Cutout"
                : displacementUv == 1 ? "ZaDisplacedUnlit6Uv1" : "ZaDisplacedUnlit6" : displacementUv == 1 ? Program + "Uv1" : Program });
        }
        if (result.Count == 0) throw new InvalidDataException("Pinned source Unlit material was not found.");
        return result;
    }

    public static PreviewScene Geometry(PreviewScene scene, IReadOnlyDictionary<string, Contract> contracts)
    {
        return scene with { Primitives = scene.Primitives.Select(p =>
        {
            if (!contracts.TryGetValue(p.Material.Name, out var contract) || !contract.SecondaryUv || p.SecondaryUv is not null) return p;
            // This source section omits TEXCOORD1. The source vertex input supplies
            // zero for the absent attribute; retain that value in the paired shader.
            if (p.Name != "pm0384_51_00_normalbeam_a_mesh_shape" || p.Material.Name != "beam_a" || p.Vertices.Length / 16 != 680)
                throw new InvalidDataException("Source Unlit secondary UV default is not qualified: " + p.Name);
            return p with { SecondaryUv = new float[p.Vertices.Length / 8] };
        }).ToArray() };
    }

    public static byte[] Template(byte[] bytes, Contract contract, IReadOnlyDictionary<string, string> images)
    {
        bytes = SvSwShLayerEmission.Template(bytes, contract.Shader, "Body00");
        var source = new ModelBuffer(bytes); var material = Material(source, contract.Shader);
        var writer = new ModelTableBuilder(bytes); var rows = source.Tables(material, 13, 256);
        if (rows.Length != 52 || source.Tables(material, 14, 256).Length != 19)
            throw new InvalidDataException("Source Unlit requires the native material prefix.");
        var slots = writer.Vector(material, 13, 52 + Names.Length);
        for (var i = 0; i < slots.Length; i++)
        {
            var at = i < 52 ? source.Field(rows[i], 1) : 0;
            var absent = i < 52 && at == 0;
            var row = writer.Table(2, absent ? null : new Dictionary<int, byte[]> { [1] = i < 52 ? source.Slice(at, 4).ToArray() : ModelTableBuilder.Floats(contract.Values[i - 52]) },
                absent: absent ? new HashSet<int> { 1 } : null);
            writer.Pointer(slots[i], row); writer.Text(row, 0, i < 52 ? source.Text(rows[i], 0)! : Names[i - 52]);
        }
        bytes = writer.Finish(source.Root);
        return ZaSwShFresnelMaterial.BindSourceTextures(bytes, Material(new(bytes), contract.Shader), contract.Textures, images);
    }

    private static int Material(ModelBuffer source, string program)
    {
        var (start, count) = source.Vector(source.Root, 3, 4, 256);
        var index = Enumerable.Range(0, count).Single(i => source.StringAt(start + i * 4) == program);
        return source.Tables(source.Root, 6, 128).Single(m => source.Value(m, 8) == index);
    }

    public static byte[] Bind(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts,
        IReadOnlyDictionary<string, string> originals, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> images)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var (start, count) = source.Vector(source.Root, 3, 4, 256);
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            var index = source.Value(material, 8);
            if (index >= count) throw new InvalidDataException("Source Unlit native program index is invalid.");
            if (!Recognizes(source.StringAt(start + (int)index * 4))) continue;
            var original = originals[source.Text(material, 0)!]; var contract = contracts[original];
            var rows = source.Tables(material, 13, 256);
            if (rows.Length != 52 + Names.Length || !rows.Skip(52).Select(t => source.Text(t, 0)).SequenceEqual(Names))
                throw new InvalidDataException("Source Unlit scalar ABI is incomplete.");
            for (var i = 0; i < Names.Length; i++) writer.Single(writer.Field(rows[52 + i], 1), contract.Values[i]);
        }
        bytes = writer.Finish(source.Root); source = new(bytes);
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            var index = source.Value(material, 8);
            if (!Recognizes(source.StringAt(start + (int)index * 4))) continue;
            var original = originals[source.Text(material, 0)!];
            var current = new ModelBuffer(bytes);
            var currentMaterial = current.Tables(current.Root, 6, 128).Single(m => current.Text(m, 0) == source.Text(material, 0));
            bytes = ZaSwShFresnelMaterial.BindSourceTextures(bytes, currentMaterial, contracts[original].Textures, images[original]);
        }
        return bytes;
    }
}
