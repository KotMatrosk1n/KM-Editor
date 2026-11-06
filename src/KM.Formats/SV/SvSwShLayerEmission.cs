// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.ZA;

namespace KM.Formats.SV;

/// <summary>Retains independently animated source emission layers on the native body surface.</summary>
public static partial class SvSwShLayerEmission
{
    public const string ProgramName = "LayerEmission";
    public const string MapProgramName = "MapEmission";
    public const string FresnelProgramName = "FresnelEmission";
    public const string FresnelMirrorProgramName = "FresnelMirrorEmission";
    public const string LavaProgramName = "LavaEmission";
    public const string MirroredProgramName = "MirrorEmission";
    public const string ThreeLayerProgramName = "ThreeLayerEmission";
    public const string BaseProgramName = "LayerBaseEmission";
    public const string UnlitBaseProgramName = "UnlitLayerBase";
    public const string AdditiveProgramName = "AdditiveLayerEmission";
    public static readonly string[] TransparentPrograms = ["LayerEmissionFront", "LayerEmissionBack"];
    public static readonly string[] TransparentBasePrograms = ["LayerBaseFront", "LayerBaseBack"];
    public static readonly string[] CoveragePrograms = ["CoverageFront", "CoverageBack"];
    public const string CoverageIntensity = "L1ConstantColorSd0Val";
    public const string CoverageColor = "L1ConstantColorSd0";
    public const string CoverageTrack = "CoverageEmission";
    public const string UniformProgramName = "UniformEmission";
    public const string OpaqueCoverageProgramName = "OpaqueCoverage";
    public const string FiveLayerProgramName = "GroudonFiveLayers";
    public const string PointLightProgramName = "PointLightEye";
    public const string PointLightIntensity = "L1ConstantColorSd1Val";
    public const string PointLightColor = "L1ConstantColorSd1";
    public static readonly string[] Scalars = ["L1ConstantColor0Val", "L1AddColor0Val", "L1ConstantColor1Val", "L1AddColor1Val"];
    public static readonly string[] Vectors = ["L1ConstantColor0", "L1AddColor0", "L1ConstantColor1", "L1AddColor1"];
    public static readonly string[] BaseVectors = ["ConstantColor0", "ConstantColor1", "ConstantColor", "OnGameColor"];
    public sealed record Contract(string Name, string Mask, Vector3[] Colors)
    {
        public Vector4 MaskUv { get; init; } = new(1, 1, 0, 0);
        public Vector2 MaskWrap { get; init; }
        public Vector4 MaskScale { get; init; } = Vector4.One;
        public float[] Initial { get; init; } = new float[4];
        public float MaximumIntensity { get; init; } = 1;
        public bool Transparent { get; init; }
        public bool ColorMap { get; init; }
        public bool BaseColorMap { get; init; }
        public bool MaskUnused { get; init; }
        public string? FresnelNormal { get; init; }
        public string? FresnelNormal1 { get; init; }
        public bool FresnelMirrorOnce { get; init; }
        public IReadOnlyDictionary<string, Vector2>? FresnelWraps { get; init; }
        public bool Lava { get; init; }
        public bool MirroredMask { get; init; }
        public bool AnimatedColors { get; init; }
        public Vector3[]? BaseColors { get; init; }
        public Vector4 SourceBaseAlphas { get; init; } = Vector4.One;
        public bool PreservesTextureAlpha { get; init; }
        public Vector2? ColorUvOrigin { get; init; }
        public bool Unlit { get; init; }
        public bool Additive { get; init; }
        public bool NormalizedCoverage { get; init; }
        public bool SeparateBaseColors { get; init; }
        public Vector3 GlobalEmissionColor { get; init; }
        public float GlobalEmissionInitial { get; init; }
        public Vector3? PointColor { get; init; }
        public float PointIntensity { get; init; }
        public bool UniformEmission { get; init; }
        public string? FifthMask { get; init; }
        public string Program(bool inner = false) => FifthMask is not null ? FiveLayerProgramName : SeparateBaseColors
            ? SvSwShSourceBase.Program(this, inner)
            : PointColor is not null ? PointLightProgramName : UniformEmission ? UniformProgramName : NormalizedCoverage ? Transparent ? CoveragePrograms[inner ? 1 : 0] : OpaqueCoverageProgramName : Additive ? AdditiveProgramName : Unlit ? UnlitBaseProgramName : BaseColors is not null ? Transparent ? TransparentBasePrograms[inner ? 1 : 0] : BaseProgramName : MaskScale.W == 0 ? ThreeLayerProgramName : MirroredMask && MaskUv.X == 2 ? MirroredProgramName : Lava ? LavaProgramName : FresnelNormal is not null ? FresnelMirrorOnce ? FresnelMirrorProgramName : FresnelProgramName : ColorMap ? MapProgramName : Transparent ? TransparentPrograms[inner ? 1 : 0] : ProgramName;
    }

    public static IReadOnlyDictionary<string, Contract> Contracts(byte[] bytes, PreviewScene scene)
        => ContractsCore(bytes, scene).ToDictionary(p => p.Key,
            p => SvSwShSourceBase.Recognizes(p.Value) ? p.Value with { SeparateBaseColors = true } : p.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, Contract> ContractsCore(byte[] bytes, PreviewScene scene)
    {
        if (scene.Primitives.Any(p => p.Name.StartsWith("pm0383_51_00_", StringComparison.Ordinal)))
            return GroudonContracts(bytes, scene);
        if (DialgaContracts(bytes, scene) is { } dialga) return dialga;
        if (PalkiaContracts(bytes, scene) is { } palkia) return palkia;
        if (BodyDonorContracts(bytes, scene) is { } bodyDonors) return bodyDonors;
        if (scene.Primitives.Any(p => p.Name.StartsWith("pm0382_51_00_", StringComparison.Ordinal)))
            return KyogreContracts(bytes, scene);
        if (scene.Primitives.Any(p => p.Name.StartsWith("pm0036_51_00_", StringComparison.Ordinal)))
            return ClefableContracts(bytes, scene);
        if (scene.Primitives.Any(p => p.Name.StartsWith("pm0359_52_00_", StringComparison.Ordinal)))
            return AbsolZContracts(bytes, scene);
        if (scene.Primitives.Any(p => p.Name.StartsWith("pm0026_51_00_", StringComparison.Ordinal)
            || p.Name.StartsWith("pm0026_52_00_", StringComparison.Ordinal)))
            return RaichuContracts(bytes, scene);

        if (SvSwShLavaMaterial.IsLava(scene)) return SvSwShLavaMaterial.EmissionContracts(bytes, scene);
        if (scene.Primitives.Any(p => p.Name.StartsWith("pm0179_", StringComparison.Ordinal)
            || p.Name.StartsWith("pm0180_", StringComparison.Ordinal) || p.Name.StartsWith("pm0181_", StringComparison.Ordinal)))
            return FresnelContracts(bytes);
        var source = new ModelBuffer(bytes); var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        var spherical = scene.Primitives.Any(p => p.Name.StartsWith("pm0100_00_41_", StringComparison.Ordinal)
            || p.Name.StartsWith("pm0101_00_41_", StringComparison.Ordinal));
        var tynamo = scene.Primitives.Any(p => p.Name.StartsWith("pm0602_00_00_", StringComparison.Ordinal));
        var volbeat = scene.Primitives.Any(p => p.Name.StartsWith("pm0313_00_00_", StringComparison.Ordinal));
        var blitzle = scene.Primitives.Any(p => p.Name.StartsWith("pm0522_00_00_", StringComparison.Ordinal));
        var enamorus = scene.Primitives.Any(p => p.Name.StartsWith("pm1004_12_00_", StringComparison.Ordinal));
        var toucannon = scene.Primitives.Any(p => p.Name.StartsWith("pm0809_00_00_", StringComparison.Ordinal));
        var litleo = scene.Primitives.Any(p => p.Name.StartsWith("pm0704_00_00_", StringComparison.Ordinal));
        var pyroarMale = scene.Primitives.Any(p => p.Name.StartsWith("pm0705_00_00_", StringComparison.Ordinal));
        var pyroarFemale = scene.Primitives.Any(p => p.Name.StartsWith("pm0705_01_00_", StringComparison.Ordinal));
        // Both Minior donor states retain the meteor mesh names. The four core
        // surfaces and mouth share authored layers, even while the shell hides them.
        var minior = scene.Primitives.Any(p => p.Name.StartsWith("pm0824_11_00_", StringComparison.Ordinal));
        var expected = scene.Primitives.Any(p => p.Name.StartsWith("pm0075_00_11_", StringComparison.Ordinal))
            ? new[] { "body_b" } : scene.Primitives.Any(p => p.Name.StartsWith("pm0076_00_11_", StringComparison.Ordinal))
                ? new[] { "body_a_01", "body_a_02", "body_b_01" }
                : scene.Primitives.Any(p => p.Name.StartsWith("pm0100_00_41_", StringComparison.Ordinal)) ? new[] { "eye", "body_01" }
                : scene.Primitives.Any(p => p.Name.StartsWith("pm0101_00_41_", StringComparison.Ordinal)) ? new[] { "eye", "body_a" }
                : scene.Primitives.Any(p => p.Name.StartsWith("pm0386_11_00_", StringComparison.Ordinal)) ? new[] { "body_b_01", "body_b_02", "body_b_00", "body_a" }
                : scene.Primitives.Any(p => p.Name.StartsWith("pm0386_", StringComparison.Ordinal)) ? new[] { "body_a_01", "body_a_02", "body_a_00", "body_b" }
                : tynamo ? new[] { "body_00", "body_01" }
                : volbeat ? new[] { "body_b" }
                : blitzle || enamorus ? new[] { "body_b" }
                : toucannon ? new[] { "body_b_00", "body_b_01", "body_b_02", "body_b_03" }
                : litleo ? new[] { "body_a_02", "body_a_03" }
                : pyroarMale ? new[] { "body_a_01", "body_b_00" }
                : pyroarFemale ? new[] { "body_a_01" }
                : minior ? new[] { "body_a_00", "body_a_01", "body_a_02", "body_a_03", "eye" }
                : throw new InvalidDataException("Layer emission requires a qualified source model.");
        foreach (var name in expected)
        {
            var sections = scene.Primitives.Count(p => p.Material.Name == name);
            var deoxys = scene.Primitives.Any(p => p.Name.StartsWith("pm0386_", StringComparison.Ordinal));
            var deoxysSections = name switch
            {
                "body_b_00" => 3, "body_a" => 2,
                "body_b" => 2, "body_a_00" => scene.Primitives.Any(p => p.Name.StartsWith("pm0386_14_", StringComparison.Ordinal)) ? 1 : 2,
                _ => 1,
            };
            if (minior ? sections != (name == "body_a_02" ? 2 : 1) : deoxys ? sections != deoxysSections : spherical ? sections is < 1 or > 2 : enamorus ? sections != 2 : volbeat ? sections != 3 : sections != 1)
                throw new InvalidDataException("Layer emission geometry is ambiguous.");
            if (toucannon && !scene.Primitives.Where(p => p.Material.Name == name).Select(p => p.Name)
                    .SequenceEqual(["pm0809_00_00_body_mesh_shape"]))
                throw new InvalidDataException("Toucannon emission geometry requires another adapter.");
            if ((blitzle || enamorus) && !scene.Primitives.Where(p => p.Material.Name == name).Select(p => p.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(blitzle ? new[] { "pm0522_00_00_body_mesh_shape" }
                        : new[] { "pm1004_12_00_tail_a_mesh_shape", "pm1004_12_00_tail_b_mesh_shape" }))
                throw new InvalidDataException("Body emission geometry requires another adapter.");
            if (minior && !scene.Primitives.Where(p => p.Material.Name == name).Select(p => p.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(name == "eye" ? new[] { "pm0824_11_00_eye_mesh_shape" }
                        : name == "body_a_02" ? new[] { "pm0824_11_00_core_mesh_shape", "pm0824_11_00_mouth_mesh_shape" }
                        : new[] { "pm0824_11_00_core_mesh_shape" }))
                throw new InvalidDataException("Minior emission geometry requires another adapter.");
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
            var scalars = source.Tables(material, 4, 256).ToDictionary(p => source.Text(p, 0)!, p => source.Field(p, 1) is var at && at != 0 ? source.Float(at) : 0);
            var vectors = source.Tables(material, 7, 256).ToDictionary(p => source.Text(p, 0)!, p =>
            { var at = source.Field(p, 1); return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12)); });
            var transparent = name.EndsWith("_02", StringComparison.Ordinal) && scene.Primitives.Any(p => p.Name.StartsWith("pm0386_", StringComparison.Ordinal));
            var colorMap = source.Tables(material, 2, 64).Any(t => source.Text(t, 0) == "EmissionColorMap");
            var initial = minior ? name switch
                {
                    "body_a_00" => new[] { .8f, .8f, 0f, .8f },
                    "body_a_03" => new[] { .8f, .8f, .8f, 0f },
                    "eye" => new[] { .8f, .8f, 0f, 0f },
                    _ => new[] { .8f, .8f, .8f, .8f },
                } : new float[4];
            var expectedUv = new Vector4(tynamo || pyroarMale || pyroarFemale
                || toucannon && name is ("body_b_02" or "body_b_03") ? 2 : 1, 1, 0, 0);
            if (!source.Tables(material, 1, 16).Select(t => source.Text(t, 0)).SequenceEqual(transparent
                    ? ["Transparent", "TransparentInner"] : [tynamo || litleo || pyroarMale || pyroarFemale ? "IkCharacter" : spherical || volbeat || toucannon || blitzle || enamorus ? "SSS" : "Standard"])
                || vectors["UVScaleOffset"] != expectedUv || (minior || toucannon) && colorMap
                || (colorMap ? scalars["EmissionIntensity"] is not (.1f or .2f) : scalars["EmissionIntensity"] != 0)
                || !colorMap && Enumerable.Range(1, 4).Any(layer => scalars["LayerMaskScale" + layer] != 1 || scalars["EmissionIntensityLayer" + layer] != initial[layer - 1]))
                throw new InvalidDataException("Source emission surface requires another adapter.");
            var colors = Enumerable.Range(1, 4).Select(layer => colorMap ? new Vector4(0, 0, 0, 1) : vectors["EmissionColorLayer" + layer]).ToArray();
            var maximumColor = blitzle ? 3.5f : volbeat ? 2.5f : 2;
            if (colors.Any(c => !float.IsFinite(c.X + c.Y + c.Z + c.W) || c.X < 0 || c.X > maximumColor || c.Y < 0 || c.Y > maximumColor || c.Z < 0 || c.Z > maximumColor || c.W != 1))
                throw new InvalidDataException("Source emission color exceeds its supported range.");
            var texture = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == (colorMap ? "EmissionColorMap" : "LayerMaskMap"));
            var selector = source.Tables(material, 9, 256).SingleOrDefault(p => source.Text(p, 0) == (colorMap ? "UVIndexEmission" : "UVIndexLayerMask"));
            var uvIndex = selector == 0 ? uint.MaxValue : source.Value(selector, 1);
            if (uvIndex is not (0 or uint.MaxValue)) throw new InvalidDataException("Source emission mask coordinates need another adapter.");
            var samplers = source.Tables(material, 3, 32); var sampler = checked((int)source.Value(texture, 2));
            if ((uint)sampler >= samplers.Length) throw new InvalidDataException("Source emission sampler is missing.");
            if (toucannon && name == "body_b_01" && (uvIndex != uint.MaxValue || colorMap || source.Value(samplers[sampler], 9) != 1 || source.Value(samplers[sampler], 10) != 1))
                throw new InvalidDataException("Toucannon emission mask requires another adapter.");
            var mirrored = blitzle || pyroarMale || pyroarFemale || toucannon && name != "body_b_01";
            if (mirrored && (uvIndex != uint.MaxValue || colorMap || source.Value(samplers[sampler], 9) != 6 || source.Value(samplers[sampler], 10) != 1))
                throw new InvalidDataException("Source mirrored emission mask requires another adapter.");
            result.Add(name, new(name, source.Text(texture, 1)!, colors.Select(c => new Vector3(c.X, c.Y, c.Z)).ToArray())
                { ColorMap = colorMap, BaseColorMap = colorMap
                    && source.Tables(material, 2, 64).Any(t => source.Text(t, 0) == "BaseColorMap" && source.Text(t, 1) == source.Text(texture, 1))
                    && !source.Tables(material, 2, 64).Any(t => source.Text(t, 0) == "LayerMaskMap")
                    && (!vectors.TryGetValue("BaseColor", out var baseColor) || baseColor == Vector4.One),
                    Initial = colorMap ? [scalars["EmissionIntensity"], 0, 0, 0] : initial,
                    MaximumIntensity = minior ? 1.5f : 1,
                    MaskScale = colorMap ? Vector4.One : new(scalars["LayerMaskScale1"], scalars["LayerMaskScale2"], scalars["LayerMaskScale3"], scalars["LayerMaskScale4"]),
                    Transparent = transparent, MirroredMask = mirrored, AnimatedColors = minior,
                    BaseColors = toucannon ? ToucannonBaseColors(source, material, vectors, name) : minior ? MiniorBaseColors(vectors) : null,
                    MaskUv = uvIndex == 0 ? new(1, 1, 0, 0) : vectors["UVScaleOffset"],
                    MaskWrap = new(source.Value(samplers[sampler], 9), source.Value(samplers[sampler], 10)) });
        }
        return result;
    }

    public static (string Name, byte[] Bytes) Mask(Contract contract, Func<string, byte[]> read, CancellationToken token,
        ModelTextureMipCache? encodingCache = null)
    {
        var bytes = read(contract.Mask); var name = Path.GetFileNameWithoutExtension(contract.Mask);
        if (contract.MaskScale != Vector4.One)
        {
            // Watchog's shiny body disables the fourth mask channel. Leaving its
            // source alpha in place would still attenuate the lower emission
            // layers, even though that fourth layer has zero emission intensity.
            // Apply the disabled channel after sampling in ThreeLayerEmission,
            // retaining the original compressed image, authored mips and filtering.
            ValidateMaskScale(contract);
            return (name, bytes);
        }
        if (contract.MirroredMask)
        {
            ValidateMirroredMask(contract);
            return (name, bytes);
        }
        if (contract.MaskUv == new Vector4(1, 1, 0, 0)) return (name, bytes);
        var image = ZaSwShMaterialBaker.Read(bytes); var pixels = new byte[image.Pixels.Length];
        for (var y = 0; y < image.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                var value = ZaSwShMaterialBaker.Sample(image, new((x + .5f) / image.Width, (y + .5f) / image.Height), contract.MaskUv, TrinityTextureAddressing.Preview(contract.MaskWrap));
                for (var c = 0; c < 4; c++) pixels[(y * image.Width + x) * 4 + c] = (byte)Math.Clamp((int)MathF.Round(value[c] * 255), 0, 255);
            }
        }
        var target = "e" + name[1..];
        var document = new ModelTextureDocument(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(bytes, image.Width, image.Height, false, compressed: false), name, target));
        return (target, document.EncodeMipImages(SwShTextureFiltering.Mips(pixels, image.Width, image.Height, document.MipCount), token, encodingCache));
    }

    private static void ValidateMaskScale(Contract contract)
    {
        if (contract.Additive) { RequireGalladeSlash(contract); return; }
        if (contract.MaskScale != Vector4.One && (contract.MaskScale != new Vector4(1, 1, 1, 0) || contract.Name != "body_00"
            || Path.GetFileNameWithoutExtension(contract.Mask) != "pm0505_00_00_body_lym" || contract.MaskUv != new Vector4(1, 1, 0, 0)
            || contract.MirroredMask || contract.ColorMap || contract.Transparent || contract.Lava || contract.FresnelNormal is not null))
            throw new InvalidDataException("Emission mask scales require another adapter.");
    }

    public static byte[] Template(byte[] bytes, string programName = ProgramName, string? sourceProgram = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var field in new[] { 3, 5 })
        {
            var (start, count) = source.Vector(source.Root, field, 4, 256);
            var names = Enumerable.Range(0, count).Select(i => source.StringAt(start + i * 4)).ToArray();
            if (names.Contains(programName, StringComparer.Ordinal)) throw new InvalidDataException("Layer emission program is already allocated.");
            var slots = writer.Vector(source.Root, field, count + 1);
            for (var i = 0; i < count; i++) writer.Pointer(slots[i], writer.String(names[i]));
            writer.Pointer(slots[count], writer.String(programName));
        }
        var originals = source.Tables(source.Root, 6, 128);
        var (programStart, _) = source.Vector(source.Root, 3, 4, 256);
        var original = sourceProgram is null ? originals[0] : originals.Single(m => source.StringAt(programStart + checked((int)source.Value(m, 8)) * 4) == sourceProgram);
        if (programName is FiveLayerProgramName or BaseProgramName or UnlitBaseProgramName or OpaqueCoverageProgramName or SvSwShSourceCoverage.OpaqueProgramName or SvSwShSourceBase.ProgramName or SvSwShSourceBase.MirroredProgramName or SvSwShSourceBase.UnlitProgramName) RequireNativeBaseSlots(source, original);
        if (programName is FiveLayerProgramName or OpaqueCoverageProgramName or SvSwShSourceCoverage.OpaqueProgramName) RequireNativeCoverageSlots(source, original);
        if (programName == FiveLayerProgramName)
        {
            var rows = source.Tables(original, 13, 256);
            if (new[] { (7, "Layer1UVScaleU"), (8, "Layer1UVScaleV"), (9, "Layer1UVTranslateU"), (11, "Layer1UVTranslateV") }
                .Any(p => rows.Length <= p.Item1 || source.Text(rows[p.Item1], 0) != p.Item2))
                throw new InvalidDataException("Primal Groudon native fifth-mask UV slots changed.");
        }
        if (programName == PointLightProgramName)
        {
            if (sourceProgram != SvSwShCornealMaterial.ProgramName) throw new InvalidDataException("Point light eyes require the paired native corneal surface.");
            RequireNativeBaseSlots(source, original); RequireNativeCoverageSlots(source, original);
            var nativeScalars = source.Tables(original, 13, 256); var nativeColors = source.Tables(original, 14, 256);
            if (nativeScalars.Length <= 23 || nativeColors.Length <= 9) throw new InvalidDataException("Native point light parameter slots are missing.");
            var scalar = nativeScalars[23]; var color = nativeColors[9];
            if (source.Text(scalar, 0) != PointLightIntensity || source.Float(source.Field(scalar, 1)) != 1
                || source.Text(color, 0) != PointLightColor || Enumerable.Range(0, 3).Any(c => source.Float(source.Field(color, 1) + 4 * c) != 1))
                throw new InvalidDataException("Native point light parameter slots changed.");
        }
        var basePass = Array.IndexOf(TransparentBasePrograms, programName);
        var sourceBasePass = Array.IndexOf(SvSwShSourceBase.TransparentPrograms, programName);
        if (sourceBasePass >= 0) basePass = sourceBasePass;
        var coveragePass = Array.IndexOf(CoveragePrograms, programName);
        var sourceCoveragePass = Array.IndexOf(SvSwShSourceCoverage.Programs, programName);
        if (sourceCoveragePass >= 0) coveragePass = sourceCoveragePass;
        if (coveragePass >= 0) basePass = coveragePass;
        if (basePass >= 0)
        {
            var nativePass = SvSwShTransparentMaterial.NativePrograms[basePass];
            var (fragmentStart, fragmentCount) = source.Vector(source.Root, 5, 4, 256);
            var fragment = checked((int)source.Value(original, 10));
            var layerMix = source.Tables(original, 13, 256).Single(p => source.Text(p, 0) == "Layer1OverLerpValue");
            if (sourceProgram != nativePass && sourceProgram != SvSwShTransparentMaterial.Programs[basePass]
                || source.Text(original, 0) != nativePass || source.Value(original, 6) != 5
                || fragment >= fragmentCount || source.StringAt(fragmentStart + fragment * 4) != sourceProgram
                || (source.Field(original, 3) is var depth && depth != 0 ? source.U8(depth) : 0) != basePass
                || (source.Field(layerMix, 1) is var mix && mix != 0 ? source.Float(mix) : 0) != basePass)
                throw new InvalidDataException("Transparent base palette source pass is not registered.");
            RequireNativeBaseSlots(source, original, transparent: true);
            if (coveragePass >= 0) RequireNativeCoverageSlots(source, original);
        }
        var slotsMaterials = writer.Vector(source.Root, 6, originals.Length + 1);
        var fields = Enumerable.Range(2, 9).Concat(Enumerable.Range(15, 5));
        var absent = fields.Where(f => f is not (8 or 10) && source.Field(original, f) == 0).ToHashSet();
        var values = fields.Where(f => !absent.Contains(f)).ToDictionary(f => f, f => f is 8 or 10
            ? ModelTableBuilder.Integer(source.Vector(source.Root, f == 8 ? 3 : 5, 4, 256).Count)
            : source.Slice(source.Field(original, f), f is 3 or 4 or >= 15 ? 1 : 4).ToArray());
        var target = writer.Table(21, values, absent: absent); writer.Pointer(slotsMaterials[^1], target);
        var copy = writer.Append(bytes);
        for (var i = 0; i < originals.Length; i++) writer.Pointer(slotsMaterials[i], copy + originals[i]);
        foreach (var field in new[] { 0, 1, 11, 12, 13, 14, 20 }) writer.Reference(target, field, copy + source.Table(original, field));
        var result = writer.Finish(source.Root);
        return SvSwShSourceCoverage.AllPrograms.Contains(programName, StringComparer.Ordinal) || SvSwShSourceBase.AllPrograms.Contains(programName, StringComparer.Ordinal) ? SvSwShSourceCoverage.Template(result, programName) : result;
    }

    public static byte[] Bind(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts, IReadOnlyDictionary<string, string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        int Program(int field, string name)
        {
            var (start, count) = source.Vector(source.Root, field, 4, 256);
            return Enumerable.Range(0, count).Single(i => source.StringAt(start + i * 4) == name);
        }
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            if (!names.TryGetValue(source.Text(material, 0)!, out var original) || !contracts.TryGetValue(original, out var contract)) continue;
            ValidateMaskScale(contract);
            var program = contract.Program(source.Text(material, 0)!.EndsWith("_inner", StringComparison.Ordinal));
            writer.Int32(writer.Field(material, 8), Program(3, program)); writer.Int32(writer.Field(material, 10), Program(5, program));
            if (contract.FresnelNormal is null && contract.MaskWrap != Vector2.Zero && !contract.MirroredMask)
            {
                var texture = source.Tables(material, 11, 64).Single(t => source.Text(t, 0) == "EmissionMaskTex");
                var sampler = source.Table(texture, 2);
                if (sampler == 0) throw new InvalidDataException("Native emission sampler is missing.");
                var absent = Enumerable.Range(0, 9).Where(f => source.Field(sampler, f) == 0 && f is not (1 or 2)).ToHashSet();
                var values = Enumerable.Range(0, 9).Where(f => !absent.Contains(f)).ToDictionary(f => f,
                    f => f is 1 or 2 ? ModelTableBuilder.Integer(TrinityTextureAddressing.SwSh(contract.MaskWrap[f - 1])) : source.Slice(source.Field(sampler, f), 4).ToArray());
                writer.Reference(texture, 2, writer.Table(9, values, absent: absent));
            }
            if (contract.MirroredMask)
            {
                ValidateMirroredMask(contract);
                foreach (var role in contract.BaseColors is null || SvSwShSourceBase.IsToucannon(contract)
                    ? new[] { "EmissionMaskTex" } : ["EmissionMaskTex", "Col0Tex"])
                {
                    var texture = source.Tables(material, 11, 64).Single(t => source.Text(t, 0) == role);
                    var sampler = source.Table(texture, 2);
                    if (sampler == 0) throw new InvalidDataException("Native mirrored texture sampler is missing.");
                    var absent = Enumerable.Range(0, 9).Where(f => source.Field(sampler, f) == 0 && f is not (1 or 2)).ToHashSet();
                    var values = Enumerable.Range(0, 9).Where(f => !absent.Contains(f)).ToDictionary(f => f,
                        f => f is 1 or 2 ? ModelTableBuilder.Integer(f == 1 ? 2 : 0) : source.Slice(source.Field(sampler, f), 4).ToArray());
                    writer.Reference(texture, 2, writer.Table(9, values, absent: absent));
                }
            }
            if (contract.FresnelNormal is not null)
            {
                var wraps = contract.FresnelWraps ?? throw new InvalidDataException("Source gem addressing is missing.");
                foreach (var (role, sourceRole) in new[] { ("EmissionMaskTex", "BaseColorMap1"), ("LyCol0Tex", "NormalMap"), ("EffectTex", "NormalMap1") })
                {
                    if (!wraps.TryGetValue(sourceRole, out var wrap)) throw new InvalidDataException("Source gem addressing is missing.");
                    var texture = source.Tables(material, 11, 64).Single(t => source.Text(t, 0) == role);
                    var sampler = source.Table(texture, 2);
                    if (sampler == 0) throw new InvalidDataException("Native gem texture sampler is missing.");
                    var absent = Enumerable.Range(0, 9).Where(f => source.Field(sampler, f) == 0 && f is not (1 or 2)).ToHashSet();
                    var values = Enumerable.Range(0, 9).Where(f => !absent.Contains(f)).ToDictionary(f => f,
                        f => f is 1 or 2 ? ModelTableBuilder.Integer(TrinityTextureAddressing.SwSh(wrap[f - 1])) : source.Slice(source.Field(sampler, f), 4).ToArray());
                    writer.Reference(texture, 2, writer.Table(9, values, absent: absent));
                }
            }
            foreach (var p in source.Tables(material, 12, 256))
                if (source.Text(p, 0) is "Layer1Enable" or "EmissionMaskUse" or "SwitchEmissionMaskTexUV")
                { var at = source.Field(p, 1); if (at != 0) writer.Write(at, [0]); }
            if (contract.NormalizedCoverage || contract.SeparateBaseColors || contract.FifthMask is not null)
            {
                BindCoverageParameters(source, writer, material, contract);
                continue;
            }
            foreach (var p in source.Tables(material, 13, 256))
                if (Scalars.Contains(source.Text(p, 0)!, StringComparer.Ordinal) || source.Text(p, 0) == "EmissionMaskVal")
                {
                    var channel = Array.IndexOf(Scalars, source.Text(p, 0));
                    writer.Single(source.Field(p, 1), channel < 0 ? 0 : contract.Initial[channel]);
                }
            foreach (var p in source.Tables(material, 14, 256))
            {
                var baseIndex = Array.IndexOf(BaseVectors, source.Text(p, 0));
                if (contract.BaseColors is not null && baseIndex >= 0)
                    for (var c = 0; c < 3; c++) writer.Single(source.Field(p, 1) + c * 4, contract.BaseColors[baseIndex][c]);
                var index = Array.IndexOf(Vectors, source.Text(p, 0)); if (index < 0) continue;
                for (var c = 0; c < 3; c++) writer.Single(source.Field(p, 1) + c * 4, contract.Colors[index][c]);
            }
        }
        return BindAdditive(writer.Finish(source.Root), names, contracts);
    }

    private static void ValidateMirroredMask(Contract contract)
    {
        if (contract.MaskUv != new Vector4(2, 1, 0, 0) && contract.MaskUv != new Vector4(1, 1, 0, 0)
            || contract.MaskWrap != new Vector2(6, 1)
            || contract.ColorMap || contract.Transparent || contract.Lava || contract.FresnelNormal is not null)
            throw new InvalidDataException("Mirrored emission mask coordinates require another adapter.");
    }

    public static PreviewClip Read(byte[] bytes, PreviewClip skeleton, PreviewScene scene, IReadOnlyDictionary<string, Contract> contracts,
        Func<byte[], PreviewClip>? decode = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var rate = source.Value(source.Table(source.Root, 0), 2, (uint)skeleton.Rate);
        if (rate == 0) throw new InvalidDataException("Layer emission animation rate is invalid.");
        var additional = new List<PreviewMaterialTrack>();
        var retainedVectors = new List<(int Slot, int Target)>();
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4); if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var scalars = source.Tables(material, 1, 256);
                var vectors = source.Tables(material, 2, 256);
                var colors = vectors.Where(p => source.Text(p, 0)?.StartsWith("EmissionColorLayer", StringComparison.Ordinal) == true).ToArray();
                var name = source.Text(material, 0)!; var mesh = source.Text(track, 0)!;
                var bases = contracts.TryGetValue(name, out var baseContract) && baseContract.Unlit
                    ? vectors.Where(p => source.Text(p, 0)?.StartsWith("BaseColorLayer", StringComparison.Ordinal) == true).ToArray() : [];
                var alphas = baseContract?.Additive == true ? vectors.Where(p => source.Text(p, 0) == "BaseColor").ToArray() : [];
                var fifthUvs = baseContract?.FifthMask is not null ? vectors.Where(p => source.Text(p, 0) == "UVScaleOffset5").ToArray() : [];
                if (baseContract?.PreservesTextureAlpha == true && vectors.Any(p => source.Text(p, 0)?.StartsWith("UV", StringComparison.Ordinal) == true))
                    throw new InvalidDataException("Original source alpha UV animation requires another tile adapter.");
                if (scalars.Length == 0 && colors.Length == 0 && bases.Length == 0 && alphas.Length == 0 && fifthUvs.Length == 0) continue;
                var bound = scene.Primitives.Any(p => p.Name == mesh && p.Material.Name == name);
                // Qualified LODs repeat their bound surface's curves.
                // Remove a repeated scalar set only after comparing every channel
                // against its bound counterpart in this same clip.
                var repeatedLod = !bound && (RepeatedQualifiedLod(source, track, material, scene)
                    || RepeatedToucannonWing(source, track, material, scene));
                // Leave other bound materials intact for the supplied strict decoder.
                // Layered eyelids own their scalar channels separately; repeated LOD
                // channels must still match the bound mesh before reaching that decoder.
                if (bound && !contracts.ContainsKey(name) && decode is not null) continue;
                var stale = name == "body_b" && (mesh == "pm0075_00_11_body_a_mesh_shape"
                    || mesh is "pm0076_00_11_bodybinc_mesh_shape" or "pm0076_00_11_body_c_mesh_shape" or "pm0076_00_11_body_d_mesh_shape");
                if (!bound && !stale && !repeatedLod || bound && !contracts.ContainsKey(name))
                    throw new InvalidDataException("Source emission target is not qualified: " + mesh + "/" + name);
                foreach (var alpha in alphas)
                    additional.Add(ReadSlashAlpha(source, alpha, mesh, contracts[name], (float)skeleton.Rate / rate));
                foreach (var uv in fifthUvs)
                {
                    if (!bound) throw new InvalidDataException("Primal Groudon fifth-layer UV target is not bound.");
                    additional.Add(ReadGroudonFifthUv(source, uv, name, (float)skeleton.Rate / rate));
                }
                foreach (var scalar in scalars)
                {
                    var parameter = source.Text(scalar, 0)!;
                    var coverage = bound && (contracts[name].NormalizedCoverage && parameter == "EmissionIntensity"
                        || contracts[name].FifthMask is not null && parameter == "EmissionIntensityLayer5");
                    var uniform = bound && contracts[name].UniformEmission && parameter == "EmissionIntensity";
                    if (coverage || uniform) RequireCoverageKeys(source, scalar);
                    var fresnel = bound && contracts[name].FresnelNormal is not null;
                    var maximum = fresnel ? 8 : contracts.TryGetValue(name, out var surface) ? surface.MaximumIntensity : 1;
                    var layer = uniform ? 0 : fresnel ? parameter switch { "EmissionIntensity" => 0, "EmissionIntensityLayer1" => 1, _ => -1 }
                        : Array.IndexOf(new[] { "EmissionIntensityLayer1", "EmissionIntensityLayer2", "EmissionIntensityLayer3", "EmissionIntensityLayer4" }, parameter);
                    if (layer < 0 && !coverage) throw new InvalidDataException("Source scalar animation requires another adapter.");
                    var keys = source.Tables(source.Table(scalar, 1), 0, 18000).Select(k =>
                    {
                        float Value(int f) => source.Field(k, f) is var at && at != 0 ? source.Float(at) : 0;
                        return new PreviewKey(Value(0) * skeleton.Rate / rate, [Value(1)]);
                    }).ToArray();
                    if (keys.Length == 0 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || !float.IsFinite(k.Value[0]) || k.Value[0] < 0 || k.Value[0] > maximum)
                        || keys.Zip(keys.Skip(1)).Any(p => p.First.Frame >= p.Second.Frame)) throw new InvalidDataException("Source emission keys are invalid.");
                    if (bound) additional.Add(new(name, coverage ? CoverageTrack : fresnel || uniform ? "EmissionIntensityLayer" + (layer + 1) : parameter, [keys]));
                }
                if (scalars.Length != 0) writer.Vector(material, 1, 0);
                if (colors.Length == 0 && bases.Length == 0 && alphas.Length == 0 && fifthUvs.Length == 0) continue;
                if (!bound || colors.Length != 0 && !contracts[name].AnimatedColors
                    || bases.Length != 0 && contracts[name].BaseColors is null)
                    throw new InvalidDataException("Source emission color target is not qualified.");
                foreach (var color in colors.Concat(bases))
                {
                    var parameter = source.Text(color, 0)!;
                    if (!Enumerable.Range(1, 4).Any(layer => parameter == (bases.Contains(color) ? "BaseColorLayer" : "EmissionColorLayer") + layer))
                        throw new InvalidDataException("Source emission color requires another adapter.");
                    var channels = source.Table(color, 1);
                    if (channels == 0) throw new InvalidDataException("Source emission color channels are missing.");
                    var alpha = bases.Contains(color) ? contracts[name].SourceBaseAlphas[int.Parse(parameter[^1..], System.Globalization.CultureInfo.InvariantCulture) - 1] : 1;
                    var keys = Enumerable.Range(0, 4).Select(channel => ReadColorKeys(source, source.Table(channels, channel), (float)skeleton.Rate / rate, channel == 3, alpha)).ToArray();
                    additional.Add(new(name, parameter, keys));
                }
                var retained = vectors.Except(colors).Except(bases).Except(alphas).Except(fifthUvs).ToArray();
                var slots = writer.Vector(material, 2, retained.Length);
                for (var i = 0; i < slots.Length; i++) retainedVectors.Add((slots[i], retained[i]));
            }
        }
        if (retainedVectors.Count != 0)
        {
            var copy = writer.Append(bytes);
            foreach (var (slot, target) in retainedVectors) writer.Pointer(slot, copy + target);
        }
        var warnings = new List<string>(); var sanitized = writer.Finish(source.Root);
        var decoded = decode is null ? PreviewMaterialAnimation.Read(new(sanitized), skeleton, warnings, true) : decode(sanitized);
        if (warnings.Count != 0) throw new InvalidDataException("Layer emission source needs another animation adapter.");
        additional = additional.GroupBy(t => (t.Material, t.Parameter)).Select(group =>
        {
            var first = group.First();
            if (group.Any(track => track.Channels.Length != first.Channels.Length
                || !track.Channels.Zip(first.Channels).All(channels => channels.First.Length == channels.Second.Length
                    && channels.First.Zip(channels.Second).All(keys => keys.First.Frame == keys.Second.Frame
                        && keys.First.Value.SequenceEqual(keys.Second.Value)))))
                throw new InvalidDataException("Source emission binding is ambiguous.");
            return first;
        }).ToList();
        foreach (var name in contracts.Keys) for (var layer = 1; layer <= 4; layer++)
        {
            if (!additional.Any(t => t.Material == name && t.Parameter == "EmissionIntensityLayer" + layer))
                additional.Add(new(name, "EmissionIntensityLayer" + layer, [[new(0, [contracts[name].Initial[layer - 1]])]]));
            if (contracts[name].AnimatedColors && !additional.Any(t => t.Material == name && t.Parameter == "EmissionColorLayer" + layer))
            {
                var color = contracts[name].Colors[layer - 1];
                additional.Add(new(name, "EmissionColorLayer" + layer,
                    new[] { color.X, color.Y, color.Z, 1 }.Select(value => new[] { new PreviewKey(0, [value]) }).ToArray()));
            }
        }
        foreach (var contract in contracts.Values.Where(c => c.Additive))
            if (!additional.Any(t => t.Material == contract.Name && t.Parameter == "SurfaceAlpha"))
                additional.Add(new(contract.Name, "SurfaceAlpha", [[new(0, [0])]]));
        foreach (var contract in contracts.Values.Where(c => c.NormalizedCoverage || c.FifthMask is not null))
            if (!additional.Any(t => t.Material == contract.Name && t.Parameter == CoverageTrack))
                additional.Add(new(contract.Name, CoverageTrack, [[new(0, [contract.GlobalEmissionInitial])]]));
        foreach (var contract in contracts.Values.Where(c => c.FifthMask is not null))
            if (!additional.Any(t => t.Material == contract.Name && t.Parameter == "UVScaleOffset3"))
                additional.Add(new(contract.Name, "UVScaleOffset3", new[] { 1f, 1f, 0f, 0f }
                    .Select(value => new[] { new PreviewKey(0, [value]) }).ToArray()));
        return WithBaseColors(decoded with { Materials = decoded.Materials.Concat(additional).ToArray() }, contracts);
    }

    private static PreviewKey[] ReadColorKeys(ModelBuffer source, int channel, float rate, bool alpha, float expectedAlpha = 1)
    {
        if (channel == 0) throw new InvalidDataException("Source emission color channel is missing.");
        var keys = source.Tables(channel, 0, 18000).Select(key =>
        {
            float Value(int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
            return new PreviewKey(Value(0) * rate, [Value(1)]);
        }).ToArray();
        if (keys.Length == 0 || keys.Any(key => !float.IsFinite(key.Frame) || key.Frame < 0 || !float.IsFinite(key.Value[0])
                || key.Value[0] < 0 || key.Value[0] > 2 || alpha && key.Value[0] != expectedAlpha)
            || keys.Zip(keys.Skip(1)).Any(pair => pair.First.Frame >= pair.Second.Frame))
            throw new InvalidDataException("Source emission color keys are invalid.");
        return keys;
    }

    private static bool RepeatedQualifiedLod(ModelBuffer source, int track, int material, PreviewScene scene)
    {
        var mesh = source.Text(track, 0)!;
        var additionalMegaLod = mesh.Length > 5 && mesh[^5..] is ("_lod1" or "_lod2" or "_lod3")
            && mesh[..^5] is ("pm0026_52_00_body_mesh_shape" or "pm0026_52_00_teeth_mesh_shape"
                or "pm0026_52_00_rip_a_mesh_shape" or "pm0026_52_00_rip_b_mesh_shape" or "pm0026_52_00_rip_c_mesh_shape"
                or "pm0705_51_00_body_c_mesh_shape");
        if (!additionalMegaLod && mesh is not ("pm0705_00_00_body_mesh_shape_lod1" or "pm0705_00_00_body_mesh_shape_lod2" or "pm0705_00_00_body_mesh_shape_lod3"
            or "pm0705_00_00_face_mesh_shape_lod1" or "pm0705_00_00_face_mesh_shape_lod2" or "pm0705_00_00_face_mesh_shape_lod3"
            or "pm0160_51_00_arm_a_mesh_shape_lod1" or "pm0160_51_00_arm_a_mesh_shape_lod2" or "pm0160_51_00_arm_a_mesh_shape_lod3"
            or "pm0160_51_00_arm_b_mesh_shape_lod1" or "pm0160_51_00_arm_b_mesh_shape_lod2" or "pm0160_51_00_arm_b_mesh_shape_lod3"
            or "pm0160_51_00_eye_mesh_shape_lod1" or "pm0160_51_00_eye_mesh_shape_lod2" or "pm0160_51_00_eye_mesh_shape_lod3"
            or "pm0160_51_00_body_mesh_shape_lod1" or "pm0160_51_00_body_mesh_shape_lod2" or "pm0160_51_00_body_mesh_shape_lod3")) return false;
        var name = source.Text(material, 0)!; var originalMesh = mesh[..^5];
        if (!scene.Primitives.Any(p => p.Name == originalMesh && p.Material.Name == name)) return false;
        var originalTrack = source.Tables(source.Root, 1, 512).SingleOrDefault(t => source.Text(t, 0) == originalMesh);
        if (originalTrack == 0) return false;
        var original = source.Tables(source.Table(originalTrack, 4), 2, 128).SingleOrDefault(m => source.Text(m, 0) == name);
        if (original == 0) return false;
        var channels = source.Tables(material, 1, 256); var originals = source.Tables(original, 1, 256);
        if (channels.Length != originals.Length) return false;
        foreach (var channel in channels)
        {
            var counterpart = originals.SingleOrDefault(c => source.Text(c, 0) == source.Text(channel, 0));
            if (counterpart == 0) return false;
            var keys = source.Tables(source.Table(channel, 1), 0, 18000);
            var originalKeys = source.Tables(source.Table(counterpart, 1), 0, 18000);
            float Value(int key, int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
            if (keys.Length != originalKeys.Length || !keys.Zip(originalKeys).All(pair => Value(pair.First, 0) == Value(pair.Second, 0)
                && Value(pair.First, 1) == Value(pair.Second, 1))) return false;
        }
        return true;
    }

    private static bool RepeatedToucannonWing(ModelBuffer source, int track, int material, PreviewScene scene)
    {
        // The donor repeats the body's beak-emission channels on wing targets
        // named body_b_00, while all four actual wings bind body_b_04. Preserve
        // the bound body animation and remove only exact redundant scalar sets.
        var mesh = source.Text(track, 0)!;
        if (mesh is not ("pm0809_00_00_left_wing_mesh_shape" or "pm0809_00_00_right_wing_mesh_shape"
            or "pm0809_00_00_left_closewing_mesh_shape" or "pm0809_00_00_right_closewing_mesh_shape")
            || source.Text(material, 0) != "body_b_00"
            || !scene.Primitives.Any(p => p.Name == mesh && p.Material.Name == "body_b_04")
            || !scene.Primitives.Any(p => p.Name == "pm0809_00_00_body_mesh_shape" && p.Material.Name == "body_b_00")
            || source.Tables(material, 2, 256).Length != 0 || source.Tables(material, 3, 256).Length != 0) return false;
        var originalTrack = source.Tables(source.Root, 1, 512).SingleOrDefault(t => source.Text(t, 0) == "pm0809_00_00_body_mesh_shape");
        if (originalTrack == 0 || source.Table(originalTrack, 4) == 0) return false;
        var original = source.Tables(source.Table(originalTrack, 4), 2, 128).SingleOrDefault(m => source.Text(m, 0) == "body_b_00");
        if (original == 0) return false;
        var channels = source.Tables(material, 1, 256); var originals = source.Tables(original, 1, 256);
        if (channels.Length != 2 || channels.Length != originals.Length
            || channels.Any(c => source.Text(c, 0) is not ("EmissionIntensityLayer2" or "EmissionIntensityLayer4"))) return false;
        foreach (var channel in channels)
        {
            var counterpart = originals.SingleOrDefault(c => source.Text(c, 0) == source.Text(channel, 0));
            if (counterpart == 0) return false;
            var keys = source.Tables(source.Table(channel, 1), 0, 18000);
            var originalKeys = source.Tables(source.Table(counterpart, 1), 0, 18000);
            float Value(int key, int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
            if (keys.Length != originalKeys.Length || !keys.Zip(originalKeys).All(pair => Value(pair.First, 0) == Value(pair.Second, 0)
                && Value(pair.First, 1) == Value(pair.Second, 1))) return false;
        }
        return true;
    }

    public static byte[] Animation(byte[] bytes, IReadOnlyList<PreviewMaterialTrack> tracks, IReadOnlySet<string>? baseNames = null,
        IReadOnlyDictionary<string, Vector4>? baseAlphas = null, IReadOnlySet<string>? separateBaseNames = null)
    {
        var groups = tracks.Where(t => t.Parameter.StartsWith("EmissionIntensityLayer", StringComparison.Ordinal)
            || t.Parameter.StartsWith("EmissionColorLayer", StringComparison.Ordinal) || t.Parameter == CoverageTrack).GroupBy(t => t.Material).ToArray();
        if (baseNames?.Any(name => !groups.Any(group => group.Key == name)) == true)
            throw new InvalidDataException("Native base and emission targets are incomplete.");
        if (groups.Length == 0) return bytes;
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder();
        var root = writer.Table(5, absent: Enumerable.Range(0, 5).Where(f => f != 2 && source.Table(source.Root, f) == 0).ToHashSet());
        var parent = writer.Table(1); writer.Reference(root, 2, parent);
        var existing = source.Tables(source.Table(source.Root, 2), 0, 128); var frames = checked((int)source.Value(source.Table(source.Root, 0), 1));
        if (frames is < 1 or > 18000) throw new InvalidDataException("Native emission timeline is invalid.");
        var retained = existing.Where(t => !groups.Any(g => g.Key == source.Text(t, 0))).ToArray();
        var slots = writer.Vector(parent, 0, retained.Length + groups.Length);
        var retainedReferences = new List<(int Slot, int Target)>();
        foreach (var field in new[] { 0, 1, 3, 4 })
            if (source.Table(source.Root, field) is var table && table != 0) retainedReferences.Add((writer.Field(root, field), table));
        for (var i = 0; i < retained.Length; i++) retainedReferences.Add((slots[i], retained[i]));
        for (var i = 0; i < groups.Length; i++)
        {
            var baseVectors = separateBaseNames?.Contains(groups[i].Key) == true ? SvSwShSourceCoverage.VectorNames : BaseVectors;
            var intensities = groups[i].Where(t => t.Parameter.StartsWith("EmissionIntensityLayer", StringComparison.Ordinal)).ToArray();
            if (intensities.Length != 4 || intensities.Select(t => t.Parameter).Distinct(StringComparer.Ordinal).Count() != 4
                || intensities.Any(t => !Enumerable.Range(1, 4).Any(layer => t.Parameter == "EmissionIntensityLayer" + layer)))
                throw new InvalidDataException("Native emission intensity channels are incomplete or ambiguous.");
            var material = writer.Table(4); writer.Pointer(slots[retained.Length + i], material); writer.Text(material, 0, groups[i].Key);
            var prior = existing.SingleOrDefault(t => source.Text(t, 0) == groups[i].Key);
            foreach (var field in new[] { 1 })
                if (prior != 0 && source.Table(prior, field) is var table && table != 0) retainedReferences.Add((writer.Field(material, field), table));
                else writer.Vector(material, field, 0);
            var priorScalars = prior == 0 ? [] : source.Tables(prior, 2, 256);
            var priorVectors = prior == 0 ? [] : source.Tables(prior, 3, 256);
            if (priorScalars.Any(p => Scalars.Contains(source.Text(p, 0)!, StringComparer.Ordinal)))
                throw new InvalidDataException("Native emission scalar is already animated.");
            var global = groups[i].Where(t => t.Parameter == CoverageTrack).ToArray();
            if (global.Length > 1 || global.Length != 0 && (global[0].Channels.Length != 1
                || priorScalars.Any(p => source.Text(p, 0) == CoverageIntensity)))
                throw new InvalidDataException("Coverage emission animation is ambiguous.");
            var values = writer.Vector(material, 2, 4 + global.Length + priorScalars.Length);
            for (var p = 0; p < priorScalars.Length; p++) retainedReferences.Add((values[4 + global.Length + p], priorScalars[p]));
            if (global.Length != 0) WriteEmissionCurve(writer, values[4], CoverageIntensity, global[0].Channels, frames);
            for (var layer = 0; layer < 4; layer++)
            {
                var track = groups[i].Single(t => t.Parameter == "EmissionIntensityLayer" + (layer + 1));
                if (track.Channels.Length != 1 || track.Channels[0].Length == 0) throw new InvalidDataException("Native emission channel is invalid.");
                WriteEmissionCurve(writer, values[layer], Scalars[layer], track.Channels, frames);
            }
            var colors = groups[i].Where(t => t.Parameter.StartsWith("EmissionColorLayer", StringComparison.Ordinal)).ToArray();
            if (colors.Length is not (0 or 4) || colors.Select(t => t.Parameter).Distinct(StringComparer.Ordinal).Count() != colors.Length
                || colors.Any(t => !Enumerable.Range(1, 4).Any(layer => t.Parameter == "EmissionColorLayer" + layer) || t.Channels.Length != 4)
                || colors.Length != 0 && priorVectors.Any(p => Vectors.Contains(source.Text(p, 0)!, StringComparer.Ordinal)))
                throw new InvalidDataException("Native emission color channels are incomplete or already animated.");
            foreach (var color in colors)
            {
                ValidateEmissionKeys(color.Channels);
                if (color.Channels[3].Any(key => key.Value[0] != 1))
                    throw new InvalidDataException("Native emission color alpha requires another adapter.");
            }
            var bases = baseNames?.Contains(groups[i].Key) == true
                ? tracks.Where(t => t.Material == groups[i].Key && t.Parameter.StartsWith("BaseColorLayer", StringComparison.Ordinal)).ToArray() : [];
            if (baseNames?.Contains(groups[i].Key) == true) RequireBaseTracks(bases, baseAlphas?.GetValueOrDefault(groups[i].Key, Vector4.One) ?? Vector4.One);
            if (bases.Length != 0 && priorVectors.Any(p => baseVectors.Contains(source.Text(p, 0)!, StringComparer.Ordinal)))
                throw new InvalidDataException("Native base color is already animated.");
            var vectorSlots = writer.Vector(material, 3, colors.Length + bases.Length + priorVectors.Length);
            for (var p = 0; p < priorVectors.Length; p++) retainedReferences.Add((vectorSlots[colors.Length + bases.Length + p], priorVectors[p]));
            for (var layer = 0; layer < colors.Length; layer++)
                WriteEmissionCurve(writer, vectorSlots[layer], Vectors[layer], colors.Single(t => t.Parameter == "EmissionColorLayer" + (layer + 1)).Channels.Take(3).ToArray(), frames);
            for (var layer = 0; layer < bases.Length; layer++)
                WriteEmissionCurve(writer, vectorSlots[colors.Length + layer], baseVectors[layer], bases.Single(t => t.Parameter == "BaseColorLayer" + (layer + 1)).Channels.Take(3).ToArray(), frames);
        }
        // Replacement material tables must precede every retained subgraph: native
        // relative references are unsigned and cannot point back into an earlier copy.
        var copy = writer.Append(bytes);
        foreach (var (slot, target) in retainedReferences) writer.Pointer(slot, copy + target);
        return writer.Finish(root);
    }

    private static void WriteEmissionCurve(ModelTableBuilder writer, int slot, string name, PreviewKey[][] channels, int frames)
    {
        ValidateEmissionKeys(channels);
        var constant = channels.All(keys => keys.All(key => key.Value[0] == keys[0].Value[0]));
        var parameter = writer.Table(3, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(constant ? 1 : 2) });
        writer.Pointer(slot, parameter); writer.Text(parameter, 0, name);
        var curve = writer.Table(1, sizes: constant ? new Dictionary<int, int> { [0] = channels.Length * 4 } : null);
        writer.Reference(parameter, 2, curve);
        float Sample(PreviewKey[] keys, int frame)
        {
            if (frame <= keys[0].Frame) return keys[0].Value[0];
            for (var k = 1; k < keys.Length; k++) if (frame < keys[k].Frame)
                return keys[k - 1].Value[0] + (keys[k].Value[0] - keys[k - 1].Value[0]) * (frame - keys[k - 1].Frame) / (keys[k].Frame - keys[k - 1].Frame);
            return keys[^1].Value[0];
        }
        var values = constant ? channels.Select(keys => keys[0].Value[0]).ToArray()
            : Enumerable.Range(0, frames).SelectMany(frame => channels.Select(keys => Sample(keys, frame))).ToArray();
        if (constant) writer.Write(writer.Field(curve, 0), ModelTableBuilder.Floats(values));
        else writer.Blob(curve, 0, ModelTableBuilder.Floats(values), frames);
    }

    private static void ValidateEmissionKeys(PreviewKey[][] channels)
    {
        if (channels.Any(keys => keys.Length == 0 || keys.Any(key => !float.IsFinite(key.Frame) || key.Frame < 0
                || key.Value.Length != 1 || !float.IsFinite(key.Value[0]))
            || keys.Zip(keys.Skip(1)).Any(pair => pair.First.Frame >= pair.Second.Frame)))
            throw new InvalidDataException("Native emission keys are invalid.");
    }

    public static byte[] GlobalLoop(byte[] bytes, IReadOnlySet<string> names, IReadOnlySet<string>? baseNames = null, IReadOnlySet<string>? alphaNames = null,
        IReadOnlySet<string>? coverageNames = null, IReadOnlySet<string>? pointNames = null, IReadOnlySet<string>? separateBaseNames = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var retained = new List<(int[] Slots, int[] Entries)>();
        foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128).Where(m => names.Contains(source.Text(m, 0)!)))
            foreach (var (field, parameters) in new[] { (2, Scalars), (3, Vectors) })
            {
                var entries = source.Tables(material, field, 256).Where(p => !parameters.Contains(source.Text(p, 0)!, StringComparer.Ordinal)
                    && !(coverageNames?.Contains(source.Text(material, 0)!) == true && source.Text(p, 0) == (field == 2 ? CoverageIntensity : CoverageColor))
                    && !(pointNames?.Contains(source.Text(material, 0)!) == true && source.Text(p, 0) == (field == 2 ? PointLightIntensity : PointLightColor))
                    && !(field == 2 && alphaNames?.Contains(source.Text(material, 0)!) == true && source.Text(p, 0) == "ConstantAlpha")
                    && !(field == 3 && baseNames?.Contains(source.Text(material, 0)!) == true
                        && (separateBaseNames?.Contains(source.Text(material, 0)!) == true ? SvSwShSourceCoverage.VectorNames : BaseVectors)
                            .Contains(source.Text(p, 0)!, StringComparer.Ordinal))).ToArray();
                retained.Add((writer.Vector(material, field, entries.Length), entries));
            }
        // Retained curves are immutable. All replacement vectors can reference
        // one source graph instead of copying it for every material parameter kind.
        var copy = writer.Append(bytes);
        foreach (var (slots, entries) in retained)
            for (var i = 0; i < entries.Length; i++) writer.Pointer(slots[i], copy + entries[i]);
        return writer.Finish(source.Root);
    }

    public static byte[] Controller(byte[] bytes, IReadOnlySet<string> names, IReadOnlySet<string>? colorNames = null, IReadOnlySet<string>? baseNames = null, IReadOnlySet<string>? alphaNames = null,
        IReadOnlySet<string>? coverageNames = null, IReadOnlySet<string>? pointNames = null, IReadOnlySet<string>? separateBaseNames = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            if (source.Text(layer, 1) is "eye01" or "mouth01") continue;
            foreach (var material in source.Tables(source.Table(source.Table(layer, 2), 1), 1, 128))
            {
                if (!names.Contains(source.Text(material, 0)!)) continue;
                foreach (var (field, parameters) in new[] { (2, Scalars), (3, Vectors) })
                {
                    var baseColors = field == 3 && baseNames?.Contains(source.Text(material, 0)!) == true;
                    if (field == 3 && colorNames?.Contains(source.Text(material, 0)!) != true && !baseColors
                        && pointNames?.Contains(source.Text(material, 0)!) != true) continue;
                    var mask = source.Table(material, field);
                    if (mask == 0) throw new InvalidDataException("Native emission controller mask is missing.");
                    var entries = source.Tables(mask, 1, 256).Select(t => source.Text(t, 0)!)
                        .Concat(field == 2 || colorNames?.Contains(source.Text(material, 0)!) == true ? parameters : [])
                        .Concat(field == 2 && alphaNames?.Contains(source.Text(material, 0)!) == true ? ["ConstantAlpha"] : [])
                        .Concat(field == 2 && coverageNames?.Contains(source.Text(material, 0)!) == true ? [CoverageIntensity] : [])
                        .Concat(pointNames?.Contains(source.Text(material, 0)!) == true ? [field == 2 ? PointLightIntensity : PointLightColor] : [])
                        .Concat(baseColors ? separateBaseNames?.Contains(source.Text(material, 0)!) == true
                            ? SvSwShSourceCoverage.VectorNames : BaseVectors : []).Distinct(StringComparer.Ordinal).ToArray();
                    var replacement = writer.Table(2, new Dictionary<int, byte[]> { [0] = [source.Field(mask, 0) is var at && at != 0 ? source.U8(at) : (byte)0] });
                    writer.Reference(material, field, replacement); var slots = writer.Vector(replacement, 1, entries.Length);
                    for (var i = 0; i < entries.Length; i++) { var item = writer.Table(1); writer.Pointer(slots[i], item); writer.Text(item, 0, entries[i]); }
                }
            }
        }
        return writer.Finish(source.Root);
    }
}
