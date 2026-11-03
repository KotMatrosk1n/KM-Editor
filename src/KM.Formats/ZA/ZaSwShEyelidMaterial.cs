using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.ZA;

public static partial class ZaSwShEyelidMaterial
{
    public const string ProgramName = "LayeredEye";
    public const string HighlightProgramName = "LayeredHighlightEye";
    public const string EmissionProgramName = "LayeredEmissionEye";
    public const string TintProgramName = "LayeredTintEye";
    public const string NativeProgramName = "LEye";
    public static readonly string[] ScalarNames =
    [
        "ColorUVScaleU", "ColorUVScaleV", "ColorUVTranslateU", "ColorUVTranslateV", "ColorBaseU", "ColorBaseV",
        "Layer1UVScaleU", "Layer1UVScaleV", "Layer1UVTranslateU", "Layer1UVTranslateV",
        "L1ConstantColor0Val", "L1AddColor0Val", "L1ConstantColor1Val", "L1AddColor1Val", "L1ConstantColorSd0Val", "L1ConstantColorSd1Val",
        "Layer1BaseU", "Layer1BaseV", "NormalMapUVScaleU", "NormalMapUVScaleV", "EmissionMaskVal", "Layer1OverLerpValue"
    ];
    public static readonly string[] VectorNames =
    [
        "L1ConstantColor0", "L1AddColor0", "L1ConstantColor1", "L1AddColor1"
    ];
    public static readonly string[] HighlightScalarNames =
        ["EffectTexScaleU", "EffectTexScaleV", "EffectTexTranslateU", "EffectTexTranslateV"];
    public static readonly string[] EmissionScalarNames = ["DistortionIntensity", "Sin01", "EffectTexRotate", "EffectColPower"];
    public static readonly string[] EmissionVectorNames = ["L1ConstantColorSd0", "L1ConstantColorSd1", "ConstantColor", "EffectColor01"];
    // Both Falinks palettes use these emission colors. Reserve the native
    // vector uniforms for its independently animated iris tints.
    internal static readonly Vector3[] TintEmissionColors =
        [new(0, .401975f, 1.0000241f), new(1.0000241f), new(0, .401975f, 1.0000241f), new(1.0000241f)];

    public sealed record Contract(string Name, Vector2 Origin, IReadOnlyDictionary<string, Vector4> Vectors,
        IReadOnlyDictionary<string, float> Scalars, IReadOnlyDictionary<string, string> Textures,
        IReadOnlyDictionary<string, Vector2> Wraps)
    {
        public int? NormalAtlasHalf { get; init; }
        public bool IndependentHighlights { get; init; }
        public bool LayerEmission { get; init; }
        public bool LayerTint { get; init; }
        public bool StaticNormalCoverage { get; init; }
        public bool SourceEyelidTintIdentity { get; init; }
        public bool MirroredColor { get; init; }
    }

    public static IReadOnlyDictionary<string, Contract> Materials(byte[] sourceMaterials, PreviewScene scene,
        IReadOnlyDictionary<string, Vector2>? coordinateOffsets = null, bool independentHighlights = false, bool layerEmission = false, bool layerTint = false,
        IReadOnlyDictionary<string, float>? cornealPrimaryNormalHeights = null)
    {
        var source = new ModelBuffer(sourceMaterials);
        var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var material in source.Tables(source.Root, 1, 128))
        {
            var name = source.Text(material, 0) ?? throw new InvalidDataException("Eye material is unnamed.");
            if (!name.Contains("eye", StringComparison.Ordinal)) continue;
            var primitives = scene.Primitives.Where(primitive => primitive.Material.Name == name).ToArray();
            var tadbulbPair = KM.Formats.SV.SvSwShCornealSections.IsTadbulbPair(name, primitives);
            var belliboltPair = KM.Formats.SV.SvSwShCornealSections.IsBelliboltPair(name, primitives);
            var splitCorneal = KM.Formats.SV.SvSwShCornealSections.IsScovillainPair(name, primitives)
                || KM.Formats.SV.SvSwShCornealSections.IsFarigirafPair(name, primitives) || tadbulbPair || belliboltPair;
            // Mega Falinks' two rear troopers share one authored eye material.
            // They use the same UV island, so retain both meshes with one shader
            // contract. Other split layouts still need their own qualification.
            if (primitives.Length != 1 && !FalinksSharedEyeGeometry(name, primitives) && !splitCorneal)
                throw new InvalidDataException("Eye material geometry is ambiguous.");
            var primitive = primitives[0];
            var banetteEye = primitive.Name is "pm0354_51_00_eye_a_mesh_shape" or "pm0354_51_00_eye_b_mesh_shape";
            if (banetteEye) ZaSwShBanetteMaterial.RequireSource(sourceMaterials, scene);
            var farigirafInner = primitives.Length == 1 && name is "r_eye_a" or "l_eye_a"
                && primitive.Name == "pm1032_00_00_eye_mesh_shape" && primitive.Material.Alpha == "BlendPreMultiAlpha"
                && primitive.Vertices.Length == 48 * 16 && primitive.Indices.Length == 96 && primitive.Indices.Distinct().Count() == 24;
            var coordinates = primitives.SelectMany(part => part.Indices.Distinct()
                .Select(index => new Vector2(part.Vertices[index * 16 + 6], part.Vertices[index * 16 + 7]))).ToArray();
            var origin = new Vector2(MathF.Floor(coordinates.Min(point => point.X)), MathF.Floor(coordinates.Min(point => point.Y)));
            if (coordinates.Any(point => !float.IsFinite(point.X + point.Y) || point.X - origin.X >= 1 || point.Y - origin.Y >= 1))
                throw new InvalidDataException("Eye UV tile needs another adapter.");
            if (coordinateOffsets?.TryGetValue(name, out var coordinateOffset) == true)
            {
                if (!float.IsFinite(coordinateOffset.X + coordinateOffset.Y) || coordinates.Any(point =>
                        point.X + coordinateOffset.X is < 0 or > 1 || point.Y + coordinateOffset.Y is < 0 or > 1))
                    throw new InvalidDataException("Eye atlas coordinates need another adapter.");
                origin += coordinateOffset;
            }
            var vectors = source.Tables(material, 7, 256).ToDictionary(parameter => source.Text(parameter, 0)!, parameter =>
            {
                var offset = source.Field(parameter, 1);
                return new Vector4(source.Float(offset), source.Float(offset + 4), source.Float(offset + 8), source.Float(offset + 12));
            }, StringComparer.Ordinal);
            var scalars = source.Tables(material, 4, 256).ToDictionary(parameter => source.Text(parameter, 0)!,
                parameter => source.Field(parameter, 1) is var offset && offset != 0 ? source.Float(offset) : 0, StringComparer.Ordinal);
            var samplers = source.Tables(material, 3, 32);
            var textures = new Dictionary<string, string>(StringComparer.Ordinal);
            var wraps = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            foreach (var texture in source.Tables(material, 2, 64))
            {
                var role = source.Text(texture, 0)!;
                textures.Add(role, source.Text(texture, 1) ?? throw new InvalidDataException("Eye image is unnamed."));
                var slot = checked((int)source.Value(texture, 2));
                if ((uint)slot >= samplers.Length) throw new InvalidDataException("Eye sampler is missing.");
                wraps.Add(role, new(source.Value(samplers[slot], 9), source.Value(samplers[slot], 10)));
            }
            var pairedPrimary = cornealPrimaryNormalHeights?.TryGetValue(name, out var requestedHeight) == true ? requestedHeight : (float?)null;
            if (pairedPrimary is not null && !layerEmission && (scalars.GetValueOrDefault("EmissionIntensity") != 0
                || Enumerable.Range(1, 4).Any(layer => scalars.GetValueOrDefault("EmissionIntensityLayer" + layer) != 0)))
                throw new InvalidDataException("Paired eye emission requires its layered emission adapter.");
            var paldeaEmissionEye = primitive.Name is "pm1030_00_00_eye_mesh_shape" or "pm1031_00_00_eye_mesh_shape"
                or "pm1068_00_00_body_a_mesh_shape" or "pm1069_00_00_head_mesh_shape"
                or "pm1070_00_00_eye_mesh_shape" or "pm1071_00_00_eye_mesh_shape" or "pm1036_00_00_eye_mesh_shape" || splitCorneal || farigirafInner;
            if (primitive.Name == "pm1036_00_00_eye_mesh_shape" && (primitive.Vertices.Length != 130 * 16
                || primitive.Indices.Length != 336 || primitive.Material.Alpha != "BlendPreMultiAlpha"))
                throw new InvalidDataException("Veluza eye emission requires its authored paired geometry.");
            if (layerEmission && paldeaEmissionEye && (name is not ("l_eye" or "r_eye") && !splitCorneal && !farigirafInner
                || !source.Tables(material, 1, 16).Select(shader => source.Text(shader, 0)).SequenceEqual(new[] { "EyeClearCoat", "Eye" })
                || scalars.GetValueOrDefault("NormalHeight1") != 1
                || primitive.Name == "pm1068_00_00_body_a_mesh_shape" && scalars.GetValueOrDefault("EmissionIntensityLayer5") != 0
                || !textures.Keys.Order(StringComparer.Ordinal).SequenceEqual((primitive.Name == "pm1031_00_00_eye_mesh_shape"
                    ? new[] { "BaseColorMap", "LayerMaskMap", "NormalMap", "EyelidShadowMaskMap", "UpperEyelidColorMap", "SSSMaskMap", "NormalMap1" }
                    : tadbulbPair || belliboltPair ? new[] { "BaseColorMap", "LayerMaskMap", "NormalMap", "LowerEyelidColorMap", "SSSMaskMap", "NormalMap1" }
                    : new[] { "BaseColorMap", "LayerMaskMap", "NormalMap", "NormalMap1" }).Order(StringComparer.Ordinal))))
                throw new InvalidDataException("Paldea eye emission requires the qualified material layout.");
            // These palettes use source edge clamping for both normal maps.
            // Keep their qualified static geometry and corneal coverage checks.
            var staticNormalCoverage = pairedPrimary is not null
                && primitives.All(part => part.Name is "pm0855_00_00_eye_mesh_shape" or "pm0807_00_00_eye_mesh_shape")
                && origin == Vector2.Zero
                && coordinates.All(point => point.X is > 0 and < 1 && point.Y is > 0 and < 1)
                && vectors.GetValueOrDefault("UVScaleOffsetNormal", new(1, 1, 0, 0)) == new Vector4(1, 1, 0, 0)
                && wraps.GetValueOrDefault("NormalMap") == new Vector2(7)
                && wraps.GetValueOrDefault("NormalMap1") == new Vector2(7);
            if (pairedPrimary is { } primaryHeight && (!float.IsFinite(primaryHeight) || primaryHeight is < 0 or > 2
                    || !source.Tables(material, 1, 16).Select(shader => source.Text(shader, 0)).SequenceEqual(new[] { "EyeClearCoat", "Eye" })
                    || !textures.ContainsKey("NormalMap1") || !scalars.TryGetValue("NormalHeight1", out var secondaryHeight)
                    || secondaryHeight is < 0 or > 1
                    || !staticNormalCoverage && (wraps.GetValueOrDefault("NormalMap") != Vector2.One || wraps.GetValueOrDefault("NormalMap1") != Vector2.One)))
                throw new InvalidDataException("Layered corneal normal strengths require the qualified paired surface.");
            if (vectors.Values.Any(value => !float.IsFinite(value.X + value.Y + value.Z + value.W))
                || scalars.Values.Any(value => !float.IsFinite(value)) || scalars.GetValueOrDefault("NormalHeight") != (pairedPrimary ?? 1)
                    && !(scalars.GetValueOrDefault("NormalHeight") == 0 && textures.ContainsKey("OpacityMap1")
                        && (primitive.Name.StartsWith("pm0218_", StringComparison.Ordinal) || primitive.Name.StartsWith("pm0219_", StringComparison.Ordinal))))
                throw new InvalidDataException("Eye parameters are unsupported.");
            var maskIndex = source.Tables(material, 9, 256).SingleOrDefault(parameter => source.Text(parameter, 0) == "UVIndexLayerMask");
            if (maskIndex != 0 && source.Value(maskIndex, 1) != uint.MaxValue)
                throw new InvalidDataException("Eye color mask coordinates need another adapter.");
            if (independentHighlights && (!primitives.All(part => part.Name.StartsWith("pm0441_00_00_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm0707_00_00_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm0201_18_00_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm0115_51_00_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm0376_51_00_", StringComparison.Ordinal)
                    || part.Name == "pm1074_00_00_eye_mesh_shape")
                || !textures.ContainsKey("HighlightMaskMap") || !vectors.ContainsKey("UVScaleOffset1")
                || !vectors.ContainsKey("EmissionColorLayer5") || !scalars.TryGetValue("EmissionIntensityLayer5", out var highlightIntensity)
                || !float.IsFinite(highlightIntensity) || highlightIntensity < 0
                || !wraps.TryGetValue("HighlightMaskMap", out var highlightWrap) || highlightWrap.X != highlightWrap.Y
                || highlightWrap.X is not (1 or 7) || textures.ContainsKey("OpacityMap1")))
                throw new InvalidDataException("Independent eye highlight contract is unsupported.");
            if (layerEmission && (independentHighlights || primitives.Length != 1 && !splitCorneal
                || !primitives.All(part => part.Name.StartsWith("pm0843_00_41_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm1007_11_00_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm1133_11_00_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm1133_12_00_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm0522_00_00_", StringComparison.Ordinal)
                    || part.Name.StartsWith("pm0855_00_00_", StringComparison.Ordinal)
                    || part.Name is "pm0807_00_00_eye_mesh_shape" or "pm1045_00_00_body_mesh_shape"
                    || part.Name is "pm1030_00_00_eye_mesh_shape" or "pm1031_00_00_eye_mesh_shape"
                        or "pm1037_00_00_body_mesh_shape" or "pm1038_11_00_body_mesh_shape"
                        or "pm1068_00_00_body_a_mesh_shape" or "pm1069_00_00_head_mesh_shape"
                        or "pm1070_00_00_eye_mesh_shape" or "pm1071_00_00_eye_mesh_shape" or "pm1036_00_00_eye_mesh_shape"
                    // Both Mega Magearna palettes and forms retain this mesh
                    // identity, with independent animated iris emission layers.
                    || part.Name.StartsWith("pm0882_51_00_", StringComparison.Ordinal) || splitCorneal || farigirafInner || banetteEye)
                || textures.ContainsKey("OpacityMap1") || !textures.ContainsKey("LayerMaskMap")
                || (wraps.GetValueOrDefault("LayerMaskMap") is var emissionWrap
                    && emissionWrap != Vector2.One
                    && !(emissionWrap == new Vector2(7) && primitives.All(part => part.Name.StartsWith("pm0855_00_00_", StringComparison.Ordinal)
                        || part.Name == "pm0807_00_00_eye_mesh_shape" || banetteEye)))
                || scalars.GetValueOrDefault("EmissionIntensity") != 0
                || Enumerable.Range(1, 4).Any(layer => scalars.GetValueOrDefault("LayerMaskScale" + layer) != 1
                    || !scalars.TryGetValue("EmissionIntensityLayer" + layer, out var intensity) || intensity < 0
                    || !vectors.TryGetValue("EmissionColorLayer" + layer, out var color)
                    || color.X < 0 || color.Y < 0 || color.Z < 0)))
                throw new InvalidDataException("Layered eye emission contract is unsupported.");
            if (layerTint && (independentHighlights || layerEmission
                || !primitives.All(part => part.Name.StartsWith("pm0923_51_00_", StringComparison.Ordinal))
                || textures.ContainsKey("OpacityMap1") || !textures.ContainsKey("LayerMaskMap")
                || wraps.GetValueOrDefault("LayerMaskMap") != new Vector2(6, 1)
                || scalars.GetValueOrDefault("EmissionIntensity") != 0
                || scalars.GetValueOrDefault("EmissionIntensityLayer5") != 0
                || Enumerable.Range(1, 4).Any(layer => scalars.GetValueOrDefault("LayerMaskScale" + layer) != 1
                    || !vectors.TryGetValue("BaseColorLayer" + layer, out var tint)
                    || tint.X < 0 || tint.Y < 0 || tint.Z < 0
                    || !scalars.TryGetValue("EmissionIntensityLayer" + layer, out var intensity) || intensity < 0
                    || !vectors.TryGetValue("EmissionColorLayer" + layer, out var glow)
                    || glow.X < 0 || glow.Y < 0 || glow.Z < 0
                    || intensity != 0 && new Vector3(glow.X, glow.Y, glow.Z) != TintEmissionColors[layer - 1])))
                throw new InvalidDataException("Layered eye tint contract is unsupported.");
            var contract = new Contract(name, origin, vectors, scalars, textures, wraps)
                { IndependentHighlights = independentHighlights, LayerEmission = layerEmission, LayerTint = layerTint,
                    StaticNormalCoverage = staticNormalCoverage, MirroredColor = banetteEye };
            if (primitive.Name == "pm1100_00_00_body_a_mesh_shape" && name is "l_eye" or "r_eye"
                && System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceMaterials))
                    == "20580F028D7AEB856FD70B6DDC07E3632E00D632F13AB43E2EFB2EF85B5CABBF")
                contract = contract with { SourceEyelidTintIdentity = true };
            if (name is "r_eye_a" or "l_eye_a" && Path.GetFileName(textures.GetValueOrDefault("NormalMap")) == "pm0923_51_00_eye_a_nrm.bntx")
            {
                var half = name == "r_eye_a" ? 0 : 1;
                if (primitives.Length != 1 || primitive.Name != "pm0923_51_00_body_a_mesh_shape"
                    || primitive.Indices.Distinct().Count() != 19 || origin != Vector2.Zero
                    || vectors.GetValueOrDefault("UVScaleOffsetNormal") != new Vector4(2, 4, 0, 0)
                    || wraps.GetValueOrDefault("NormalMap") != Vector2.One
                    || coordinates.Any(point => point.X * 2 - half * .5f is < (1f / 1024) or > (.5f - 1f / 1024)))
                    throw new InvalidDataException("Falinks eye normal atlas coverage needs another adapter.");
                // Each leader eye occupies one horizontal half of the atlas.
                // Keep the full texel density within the native allocation limit.
                contract = contract with { NormalAtlasHalf = half };
            }
            result.Add(name, contract);
        }
        if (result.Count == 0) throw new InvalidDataException("Layered eye materials are missing.");
        if (coordinateOffsets is not null && coordinateOffsets.Keys.Any(name => !result.ContainsKey(name)))
            throw new InvalidDataException("Eye atlas material is missing.");
        if (cornealPrimaryNormalHeights is not null && cornealPrimaryNormalHeights.Keys.Any(name => !result.ContainsKey(name)))
            throw new InvalidDataException("Paired corneal material is missing.");
        return result;
    }

    private static bool FalinksSharedEyeGeometry(string material, IReadOnlyList<PreviewPrimitive> primitives)
    {
        if (material != "eye_e" || primitives.Count != 2 || !primitives.Select(part => part.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "pm0923_51_00_body_e_mesh_shape", "pm0923_51_00_body_f_mesh_shape" })) return false;
        Vector2[] Coordinates(PreviewPrimitive part) => part.Indices.Distinct()
            .Select(index => new Vector2(part.Vertices[index * 16 + 6], part.Vertices[index * 16 + 7]))
            .OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        var first = Coordinates(primitives[0]);
        var second = Coordinates(primitives[1]);
        // The source exports the duplicated island with at most two float ULPs
        // of rounding. Neither mesh's coordinates are modified or snapped.
        bool SameCoordinate(float a, float b) => float.IsFinite(a) && float.IsFinite(b)
            && Math.Abs((long)BitConverter.SingleToInt32Bits(a) - BitConverter.SingleToInt32Bits(b)) <= 2;
        return first.Length == 22 && second.Length == 22 && first.Zip(second).All(pair =>
            SameCoordinate(pair.First.X, pair.Second.X) && SameCoordinate(pair.First.Y, pair.Second.Y));
    }

    public static byte[] Template(byte[] bodyModel, byte[] layeredModel, bool corneal = false, bool independentHighlights = false, bool layerEmission = false, bool layerTint = false)
    {
        if ((layerEmission ? 1 : 0) + (independentHighlights ? 1 : 0) + (layerTint ? 1 : 0) > 1) throw new InvalidDataException("Eye effect samplers overlap.");
        var programName = layerTint ? TintProgramName : layerEmission ? EmissionProgramName : independentHighlights ? HighlightProgramName : ProgramName;
        var body = new ModelBuffer(bodyModel); var layered = new ModelBuffer(layeredModel);
        var eye = body.Tables(body.Root, 6, 128).Single(material => body.Text(material, 0) == "Eye");
        var donor = layered.Tables(layered.Root, 6, 128).Single(material => layered.Text(material, 0) == NativeProgramName);
        if ((layerEmission || layerTint) && EmissionVectorNames.Take(3).Any(name =>
            body.Tables(eye, 14, 256).Single(parameter => body.Text(parameter, 0) == name) is var parameter
                && body.Field(parameter, 1) is var offset
                && (offset == 0 || body.Float(offset) != 1 || body.Float(offset + 4) != 1 || body.Float(offset + 8) != 1)))
            throw new InvalidDataException("Eye emission requires the native neutral color parameters.");
        if (body.Text(eye, 1) != "PokeDefaultShader" || layered.Text(donor, 1) != "PokeDefaultShader")
            throw new InvalidDataException("Layered eyes require the native opaque material contract.");
        byte Switch(ModelBuffer source, int parameter) => source.Field(parameter, 1) is var offset && offset != 0 ? source.U8(offset) : (byte)0;
        var originalSwitches = body.Tables(eye, 12, 128).ToDictionary(parameter => body.Text(parameter, 0)!, parameter => Switch(body, parameter));
        var switches = layered.Tables(donor, 12, 128);
        if (switches.Length != originalSwitches.Count) throw new InvalidDataException("Native eye switches are incomplete.");
        foreach (var parameter in switches)
        {
            var name = layered.Text(parameter, 0)!;
            var expected = name is "Layer1Enable" or "SwitchPriority" or "NormalEdgeEnable" ? 1
                : corneal && name == "SphereMapEnable" ? 0 : originalSwitches[name];
            if (Switch(layered, parameter) != expected) throw new InvalidDataException("Native eye switches need another adapter.");
        }
        var writer = new ModelTableBuilder(bodyModel);
        var entries = writer.Vector(eye, 12, switches.Length);
        for (var index = 0; index < switches.Length; index++)
        {
            var name = layered.Text(switches[index], 0)!;
            var value = name == "Layer1Enable" ? 0 : corneal && name == "SphereMapEnable" ? 1 : Switch(layered, switches[index]);
            var parameter = writer.Table(2, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Integer(value) });
            writer.Pointer(entries[index], parameter); writer.Text(parameter, 0, name);
        }
        foreach (var field in new[] { 3, 5 })
        {
            var programField = field == 3 ? 8 : 10;
            var (start, count) = body.Vector(body.Root, field, 4, 256);
            var names = Enumerable.Range(0, count).Select(index => body.StringAt(start + index * 4)).ToArray();
            if (names.Contains(programName, StringComparer.Ordinal)) throw new InvalidDataException("Eye program identity is already allocated.");
            var slots = writer.Vector(body.Root, field, count + 1);
            for (var index = 0; index < count; index++) writer.Pointer(slots[index], writer.String(names[index]));
            writer.Pointer(slots[count], writer.String(programName));
            writer.Int32(writer.Field(eye, programField), count);
        }
        return writer.Finish(body.Root);
    }

    public static byte[] Bind(byte[] nativeModel, IReadOnlyDictionary<string, Contract> contracts,
        IReadOnlyDictionary<string, string> names)
    {
        var native = new ModelBuffer(nativeModel); var writer = new ModelTableBuilder(nativeModel);
        foreach (var material in native.Tables(native.Root, 6, 128))
        {
            var name = native.Text(material, 0)!;
            if (!names.TryGetValue(name, out var originalName) || !contracts.TryGetValue(originalName, out var contract)) continue;
            var (values, vectors) = Parameters(contract, (_, _) => null);
            if (contract.Scalars.GetValueOrDefault("NormalHeight") == 0 && contract.Textures.ContainsKey("OpacityMap1"))
            {
                var colorWrap = contract.Wraps.GetValueOrDefault("BaseColorMap", Vector2.One);
                if (colorWrap.X != colorWrap.Y || colorWrap.X is not (1 or 7))
                    throw new InvalidDataException("Lava eye addressing requires matching clamp modes.");
                values["ScaleUV"] = 0;
            }
            var scalars = native.Tables(material, 13, 256);
            var scalarSlots = writer.Vector(material, 13, scalars.Length);
            for (var index = 0; index < scalars.Length; index++)
            {
                var parameterName = native.Text(scalars[index], 0)!;
                var value = values.TryGetValue(parameterName, out var mapped) ? mapped
                    : native.Field(scalars[index], 1) is var offset && offset != 0 ? native.Float(offset) : 0;
                var parameter = writer.Table(2, new Dictionary<int, byte[]> { [1] = ModelTableBuilder.Floats(value) });
                writer.Pointer(scalarSlots[index], parameter); writer.Text(parameter, 0, parameterName);
            }
            foreach (var parameter in native.Tables(material, 14, 256))
                if (vectors.TryGetValue(native.Text(parameter, 0)!, out var value))
                {
                    var offset = native.Field(parameter, 1);
                    if (offset == 0) throw new InvalidDataException("Eye matrix storage is missing.");
                    for (var component = 0; component < 3; component++) writer.Single(offset + component * 4, value[component]);
                }
            foreach (var binding in native.Tables(material, 11, 64))
            {
                var role = native.Text(binding, 0) switch
                {
                    "Col0Tex" => "BaseColorMap", "NormalMapTex" => "NormalMap", "EmissionMaskTex" => "UpperEyelidColorMap",
                    "LyCol0Tex" => "LowerEyelidColorMap", "AmbientTex" => "EyelidShadowMaskMap",
                    "EffectTex" when contract.IndependentHighlights => "HighlightMaskMap",
                    "EffectTex" when contract.LayerEmission || contract.LayerTint => "LayerMaskMap",
                    _ => null
                };
                if (role is null) continue;
                var wrap = contract.Wraps.GetValueOrDefault(role, Vector2.One);
                var sampler = native.Table(binding, 2);
                var fields = new Dictionary<int, byte[]>();
                for (var field = 0; field < 9; field++)
                    fields[field] = native.Field(sampler, field) is var offset && offset != 0 ? native.Slice(offset, 4).ToArray() : ModelTableBuilder.Integer(0);
                for (var component = 0; component < 2; component++)
                    fields[1 + component] = ModelTableBuilder.Integer(TrinityTextureAddressing.SwSh(wrap[component]));
                writer.Reference(binding, 2, writer.Table(9, fields));
            }
        }
        return writer.Finish(native.Root);
    }
}
