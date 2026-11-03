// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using KM.Formats.Models;
using KM.Formats.SV;

namespace KM.Formats.ZA;

/// <summary>Owns ZA gem uniforms separately from Sword/Shield's lighting controls.</summary>
public static partial class ZaSwShFresnelMaterial
{
    internal const int FirstScalar = 52;
    internal const int FirstVector = 19;
    public static readonly string[] ScalarNames =
    [
        "SourceFresnelMin", "SourceFresnelMax", "SourceFresnelBias", "SourceGemSaturation", "SourceGemProbeIntensity",
        ZaSwShFresnelAnimation.BaseIntensity, ZaSwShFresnelAnimation.LayerIntensity,
        "SourceGemMaskScale", "SourceGemNormalHeight", "SourceGemNormalHeight1",
        "SourceGemUvScaleU", "SourceGemUvScaleV", "SourceGemUvOffsetU", "SourceGemUvOffsetV",
        "SourceGemLayerUvScaleU", "SourceGemLayerUvScaleV", "SourceGemLayerUvOffsetU", "SourceGemLayerUvOffsetV",
        "SourceGemParallaxU", "SourceGemParallaxV", "SourceGemMetallic", "SourceGemRoughness"
    ];
    private static readonly string[] SourceScalars = ["FresnelAlphaMin", "FresnelAlphaMax", "FresnelAngleBias", "BaseColorMapSaturation",
        "LocalSpecularProbeIntensity", "EmissionIntensity", "EmissionIntensityLayer1", "LayerMaskScale1", "NormalHeight", "NormalHeight1", "Metallic", "Roughness"];
    private static readonly string[] SourceVectors = ["BaseColor", "BaseColorLayer1", "EmissionColor", "UVScaleOffset", "UVScaleOffset1", "ParallaxUVIntensity"];
    private static readonly HashSet<string> Programs = Enumerable.Range(-1, 3).SelectMany(layer => Enumerable.Range(-1, 3)
        .SelectMany(ao => new[] { 0, 2, 6, 8 }.SelectMany(variant => (variant < 6 ? new[] { 0 } : new[] { -1, 0, 1 })
            .Select(mask => Program(layer, ao, variant, mask))))).ToHashSet(StringComparer.Ordinal);

    public static string Program(int layerUv, int aoUv, int sourceVariant, int maskUv)
    {
        if (layerUv is < -1 or > 1 || aoUv is < -1 or > 1 || maskUv is < -1 or > 1
            || sourceVariant is not (0 or 2 or 6 or 8) || sourceVariant < 6 && maskUv != 0)
            throw new InvalidDataException("ZA gem program requires explicit qualified source selectors.");
        return $"ZaSourceFresnel{sourceVariant}L{layerUv + 1}A{aoUv + 1}M{maskUv + 1}";
    }

    // Uniform metadata only. The paired shaders, source samplers and source
    // geometry must be bound separately before a complete renderer can use it.
    public static byte[] Template(byte[] bytes, byte[] sourceMaterials, string materialName,
        int layerUv, int aoUv, int sourceVariant, int maskUv)
    {
        var values = Read(sourceMaterials, materialName);
        var program = Program(layerUv, aoUv, sourceVariant, maskUv);
        var original = new ModelBuffer(bytes);
        var body = MaterialForProgram(original, "Body00");
        RequireNative(original, body);
        var template = SvSwShLayerEmission.Template(bytes, program, "Body00");
        var source = new ModelBuffer(template); var material = MaterialForProgram(source, program);
        var writer = new ModelTableBuilder(template);
        Extend(13, 1, ScalarNames, values.Scalars.Select(v => new[] { v }).ToArray(), FirstScalar);
        Extend(14, 3, SvSwShSourceCoverage.VectorNames, values.Colors.Select(v => new[] { v.X, v.Y, v.Z }).ToArray(), FirstVector);
        return writer.Finish(source.Root);

        void Extend(int field, int components, string[] names, float[][] additions, int first)
        {
            var oldRows = source.Tables(material, field, 256);
            if (oldRows.Length != first || additions.Length != names.Length)
                throw new InvalidDataException("ZA gem native parameter interface changed.");
            var slots = writer.Vector(material, field, first + names.Length);
            for (var i = 0; i < slots.Length; i++)
            {
                var at = i < first ? source.Field(oldRows[i], 1) : 0;
                bool absent = i < first && at == 0;
                var payload = i < first ? absent ? null : source.Slice(at, components * 4).ToArray()
                    : ModelTableBuilder.Floats(additions[i - first]);
                var row = writer.Table(2, payload is null ? new Dictionary<int, byte[]>() : new Dictionary<int, byte[]> { [1] = payload },
                    new Dictionary<int, int> { [1] = components * 4 }, absent: absent ? new HashSet<int> { 1 } : null);
                writer.Pointer(slots[i], row); writer.Text(row, 0, i < first ? source.Text(oldRows[i], 0)! : names[i - first]);
            }
        }
    }

    public static byte[] BindParameters(byte[] bytes, byte[] sourceMaterials, IReadOnlyDictionary<string, string> originalNames)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var (vertexStart, vertexCount) = source.Vector(source.Root, 3, 4, 256);
        var (fragmentStart, fragmentCount) = source.Vector(source.Root, 5, 4, 256);
        int bound = 0;
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            var vertex = source.Value(material, 8); var fragment = source.Value(material, 10);
            if (vertex >= vertexCount || fragment >= fragmentCount) throw new InvalidDataException("ZA gem program index is invalid.");
            var name = source.StringAt(vertexStart + checked((int)vertex) * 4);
            var fragmentName = source.StringAt(fragmentStart + checked((int)fragment) * 4);
            if (name.StartsWith("ZaSourceFresnel", StringComparison.Ordinal) && !Programs.Contains(name)
                || fragmentName.StartsWith("ZaSourceFresnel", StringComparison.Ordinal) && !Programs.Contains(fragmentName))
                throw new InvalidDataException("ZA gem program selectors are unqualified.");
            if (!Programs.Contains(name) && !Programs.Contains(fragmentName)) continue;
            if (name != fragmentName || !originalNames.TryGetValue(source.Text(material, 0)!, out var original))
                throw new InvalidDataException("ZA gem requires its paired program and original material identity.");
            var values = Read(sourceMaterials, original);
            var scalars = source.Tables(material, 13, 256); var colors = source.Tables(material, 14, 256);
            if (scalars.Length != FirstScalar + ScalarNames.Length || colors.Length != FirstVector + SvSwShSourceCoverage.VectorNames.Length
                || !scalars.Skip(FirstScalar).Select(t => source.Text(t, 0)).SequenceEqual(ScalarNames)
                || !colors.Skip(FirstVector).Select(t => source.Text(t, 0)).SequenceEqual(SvSwShSourceCoverage.VectorNames))
                throw new InvalidDataException("ZA gem owned parameter interface is missing or reordered.");
            RequireNative(source, material, extended: true);
            for (var i = 0; i < values.Scalars.Length; i++) writer.Single(writer.Field(scalars[FirstScalar + i], 1), values.Scalars[i]);
            for (var i = 0; i < values.Colors.Length; i++) for (var c = 0; c < 3; c++)
                writer.Single(writer.Field(colors[FirstVector + i], 1) + c * 4, values.Colors[i][c]);
            bound++;
        }
        if (bound == 0) throw new InvalidDataException("ZA gem parameters have no declared material consumer.");
        return writer.Finish(source.Root);
    }

    private static int MaterialForProgram(ModelBuffer source, string program)
    {
        var (start, count) = source.Vector(source.Root, 3, 4, 256);
        var index = Enumerable.Range(0, count).Single(i => source.StringAt(start + i * 4) == program);
        return source.Tables(source.Root, 6, 128).Single(m => source.Value(m, 8) == index);
    }

    private static void RequireNative(ModelBuffer source, int material, bool extended = false)
    {
        string Hash(int field) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0',
            source.Tables(material, field, 256).Take(field == 13 ? FirstScalar : FirstVector).Select(t => source.Text(t, 0)!)))));
        if (source.Tables(material, 13, 256).Length != FirstScalar + (extended ? ScalarNames.Length : 0)
            || source.Tables(material, 14, 256).Length != FirstVector + (extended ? SvSwShSourceCoverage.VectorNames.Length : 0)
            || Hash(13) != "65546F5C46C3A7E9783E28C7BEF17991620EEAFA941263BF9C730C8C926E5654"
            || Hash(14) != "71D6CC0CE5C96E75984E1D5E7A7217FB14358F7B8D24195BA23EE7930F634AB8")
            throw new InvalidDataException("ZA gem requires its pinned native material uniform interface.");
    }

    private sealed record Parameters(float[] Scalars, Vector3[] Colors);
    private static Parameters Read(byte[] bytes, string name)
    {
        var source = new ModelBuffer(bytes);
        var matches = source.Tables(source.Root, 1, 128).Where(m => source.Text(m, 0) == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("ZA gem source material is missing or ambiguous.");
        var material = matches[0];
        if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(["FresnelEffect"])
            || source.Text(material, 15) != "Opaque") throw new InvalidDataException("ZA gem requires its opaque source Fresnel surface.");
        var scalarRows = source.Tables(material, 4, 256); var vectorRows = source.Tables(material, 7, 256);
        if (!scalarRows.Select(t => source.Text(t, 0)).Order().SequenceEqual(SourceScalars.Order())
            || !vectorRows.Select(t => source.Text(t, 0)).Order().SequenceEqual(SourceVectors.Order()))
            throw new InvalidDataException("ZA gem source parameter set requires another adapter.");
        var scalars = scalarRows.ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0);
        var vectors = vectorRows.ToDictionary(t => source.Text(t, 0)!, t =>
        {
            var at = source.Field(t, 1); if (at == 0) throw new InvalidDataException("ZA gem vector is missing.");
            return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
        });
        var uv = vectors["UVScaleOffset"]; var layer = vectors["UVScaleOffset1"]; var parallax = vectors["ParallaxUVIntensity"];
        var colors = new[] { vectors["BaseColor"], vectors["BaseColorLayer1"], vectors["EmissionColor"] };
        if (scalars.Values.Any(v => !float.IsFinite(v)) || vectors.Values.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W))
            || colors.Any(v => v.X < 0 || v.Y < 0 || v.Z < 0 || v.W != 1)
            || parallax.Z != 0 || parallax.W != 0 || uv.X <= 0 || uv.Y <= 0 || layer.X <= 0 || layer.Y <= 0
            || new[] { "FresnelAlphaMin", "FresnelAlphaMax", "Metallic", "Roughness", "NormalHeight", "NormalHeight1" }.Any(n => scalars[n] is < 0 or > 1)
            || scalars["EmissionIntensity"] is < 0 or > 8 || scalars["EmissionIntensityLayer1"] is < 0 or > 8
            || scalars["LocalSpecularProbeIntensity"] < 0 || scalars["LayerMaskScale1"] < 0)
            throw new InvalidDataException("ZA gem source values require another adapter.");
        float[] values = [scalars["FresnelAlphaMin"], scalars["FresnelAlphaMax"], scalars["FresnelAngleBias"], scalars["BaseColorMapSaturation"],
            scalars["LocalSpecularProbeIntensity"], scalars["EmissionIntensity"], scalars["EmissionIntensityLayer1"], scalars["LayerMaskScale1"],
            scalars["NormalHeight"], scalars["NormalHeight1"], uv.X, uv.Y, uv.Z, uv.W, layer.X, layer.Y, layer.Z, layer.W,
            parallax.X, parallax.Y, scalars["Metallic"], scalars["Roughness"]];
        // The fourth tint is reserved by the shared 23-vector ABI; no gem
        // instruction reads it. It is not a substitute for any source input.
        return new(values, [.. colors.Select(v => new Vector3(v.X, v.Y, v.Z)), Vector3.Zero]);
    }
}
