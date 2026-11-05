// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.SV;

public static class SvSwShLayerEmissionShader
{
    public static byte[] Convert(byte[] template)
        => Convert(template, 0);

    public static byte[] SurfaceFlow(byte[] template)
        => Convert(template, 1);

    public static byte[] SurfaceLayers(byte[] template)
        => Convert(template, 2);

    public static byte[] ColorMap(byte[] template)
        => Convert(template, 3);

    public static byte[] Fresnel(byte[] template)
        => Convert(template, 4);

    public static byte[] FresnelMirrorOnce(byte[] template)
        => Convert(template, 10);

    public static byte[] Lava(byte[] template)
        => Convert(template, 5);

    public static byte[] MirroredMask(byte[] template)
        => Convert(template, 6);

    public static byte[] ThreeLayer(byte[] template)
        => Convert(template, 7);

    public static byte[] LayerBase(byte[] template)
        => Convert(template, 8);

    public static byte[] UnlitLayerBase(byte[] template)
        => Convert(template, 9);

    public static byte[] TransparentLayerBase(byte[] template)
        => Convert(template, 12);

    public static byte[] TransparentCoverage(byte[] template)
        => Convert(template, 13);

    /// <summary>Source coverage with four separate tints and intact native global colors.</summary>
    public static byte[] TransparentSourceCoverage(byte[] template)
        => Convert(template, 18);

    public static byte[] UniformEmission(byte[] template)
        => Convert(template, 14);

    public static byte[] OpaqueCoverage(byte[] template)
        => Convert(template, 15);

    /// <summary>Opaque source coverage retaining native global color controls.</summary>
    public static byte[] OpaqueSourceCoverage(byte[] template)
        => Convert(template, 19);

    public static byte[] SourceLayerBase(byte[] template) => Convert(template, 20);

    public static byte[] MirroredSourceBase(byte[] template) => Convert(template, 23);

    public static byte[] SourceUnlitBase(byte[] template) => Convert(template, 21);

    public static byte[] TransparentSourceBase(byte[] template) => Convert(template, 22);

    /// <summary>Five-layer Unlit color with the source coverage normalization.</summary>
    public static byte[] DisplacedUnlit(byte[] template)
        => Convert(template, 17);

    /// <summary>Archaludon's qualified 0.37 alpha test and opaque Unlit survivors.</summary>
    public static byte[] ArchaludonEffect(byte[] template)
        => Convert(template, 17, sourceCutout: true);

    public static byte[] Additive(byte[] template)
        => Convert(template, 11);

    private static byte[] Convert(byte[] template, int mode, bool sourceCutout = false)
    {
        int Integer(int offset) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(offset, 4));
        int Pointer(int offset) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(offset, 8)));
        if (template.Length < 512 || !template.AsSpan(0, 4).SequenceEqual("BNSH"u8) || Integer(0x7c) != 1)
            throw new InvalidDataException("Layer emission shader container is unsupported.");
        var program = Pointer(Pointer(0x80) + 16);
        var stage = Pointer(program + 0x28);
        var control = Pointer(stage + 8);
        var data = Pointer(stage + 16);
        var length = Integer(stage + 24);
        var controlLength = Integer(stage + 28);
        var identity = System.Convert.ToHexString(SHA256.HashData(template.AsSpan(data, length)));
        var transparent = identity is "CB0AB209AA69541813F7395E6D3355608E047E5D1D4C66617D66E7ACF8055276" or "A01BA67B64010C74E9A8D1C7FB3CEDC5EFAD07F18049F57D1595B26B45A4B699";
        if (mode is 12 or 13 or 18 or 22 && !transparent)
            throw new InvalidDataException("Transparent base palette requires its pinned cell shader.");
        if (mode is 14 or 15 or 16 or 17 or 19 or 20 or 21 or 23 && transparent)
            throw new InvalidDataException("Uniform emission requires its pinned opaque shader.");
        if (identity is not ("1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37" or "FBB44266C993CCFF1EC67B48E26A89B13981836CD0A6071E4F2BF8F4156135A2") && (!transparent || mode is not (0 or 12 or 13 or 18 or 22)))
            throw new InvalidDataException("Layer emission shader program needs another adapter.");
        var basePalette = mode is 8 or 9 or 12 or 13 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23;
        var baseSlots = mode is 18 or 19 or 20 or 21 or 22 or 23 ? new[] { 19, 20, 21, 22 } : mode == 17 ? new[] { 4, 5, 6, 7 }
            : mode is 12 or 13 ? new[] { 0, 2, 16, 17 } : new[] { 0, 2, 15, 16 };
        var baseWords = baseSlots.SelectMany(slot => Enumerable.Range(slot * 4, 3)).ToHashSet();
        if (controlLength < 0x848 || Integer(control) != unchecked((int)0x98761234) || Integer(data) != 0x12345678
            || Integer(control + 0x708) != 0x30 || Integer(control + 0x704) != length)
            throw new InvalidDataException("Layer emission shader binary contract is unsupported.");
        var instructionsEnd = checked(Integer(control + 0x6f8) + 0x30);
        var constants = Integer(control + 0x700);
        var constantLength = Integer(control + 0x6fc);
        if (instructionsEnd <= 0x80 || instructionsEnd > constants || constants + constantLength > length
            || Integer(control + 0x70c) > 56 || instructionsEnd % 32 != 0)
            throw new InvalidDataException("Layer emission shader allocation is unsupported.");
        var relocation = Integer(24);
        if (!template.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || Integer(relocation + 8) != 6)
            throw new InvalidDataException("Layer emission shader relocation is unsupported.");
        var sections = Enumerable.Range(0, 6).Select(index => relocation + 16 + index * 24)
            .Where(section => Integer(section + 8) == data).ToArray();
        if (sections.Length != 1) throw new InvalidDataException("Layer emission shader data section is ambiguous.");
        var capacity = Integer(sections[0] + 12);
        if (length > capacity || data + capacity > template.Length || template.AsSpan(data + length, capacity - length).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Layer emission shader extension storage is unavailable.");
        var code = new List<(ulong Instruction, ulong Control)>(); int originalBoundary = 0;
        var colorCaptured = false; var emissionCaptured = false; var components = new HashSet<int>();
        var flowInputs = 0;
        var alphaInputs = 0; var alphaOutputs = 0; var additiveExits = 0;
        var baseAlphaInputs = 0; var baseAlphaOutputs = 0;
        var cutoutInputs = 0; var opaqueOutputs = 0;
        var unlitEmissionResults = new Dictionary<int, (int Channel, int Diffuse)>();
        var unlitOutputs = new HashSet<int>();
        void Emit(ulong word, bool asynchronous = false) => code.Add((word, KM.Formats.SwSh.SwShShaderScheduling.Serial(word)));
        void Move(int destination, int source) => Emit(0x5c98078000070000UL | (ulong)source << 20 | (uint)destination);
        void Constant(int destination, float value) => Emit(0x010000000007f000UL | (ulong)BitConverter.SingleToUInt32Bits(value) << 20 | (uint)destination);
        void Uniform(int destination, int buffer, int word) => Emit(0x4c98078000070000UL | (ulong)buffer << 34 | (ulong)word << 20 | (uint)destination);
        void Multiply(int destination, int left, int right) => Emit(0x5c68100000070000UL | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void Add(int destination, int left, int right, bool subtract = false) => Emit(0x5c58100000070000UL | (subtract ? 1UL << 45 : 0) | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void Fma(int destination, int left, int right, int addend, bool negative = false, bool negativeAddend = false) =>
            Emit(0x59a0000000070000UL | (negative ? 1UL << 48 : 0) | (negativeAddend ? 1UL << 49 : 0) | (ulong)addend << 39 | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void Maximum(int destination, int left, int right) =>
            Emit(0x5c60178000070000UL | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void UnlitPalette(int destination, int second)
        {
            // The selected source Unlit permutation uses uncovered base coverage,
            // four sequential layer interpolations, then a common normalization.
            Add(89, 60, 61); Add(89, 62, 89); Add(89, 63, 89); Constant(88, 1);
            Emit(0x5c5c100000070000UL | 1UL << 45 | 89UL << 20 | 88UL << 8 | 84);
            Move(85, 84);
            for (var c = 0; c < 3; c++) Multiply(80 + c, 56 + c, 84);
            for (var layer = 0; layer < 4; layer++)
            {
                for (var c = 0; c < 3; c++)
                {
                    Uniform(88, 6, baseSlots[layer] * 4 + c);
                    Fma(89, 56 + c, 88, 80 + c, negativeAddend: true);
                    Fma(80 + c, 60 + layer, 89, 80 + c);
                }
                Fma(89, 60 + layer, 85, 60 + layer, negative: true);
                Add(85, 85, 89);
            }
            Constant(88, BitConverter.Int32BitsToSingle(0x3727c5ac)); Maximum(85, 85, 88);
            Emit(0x5080000000470000UL | 85UL << 8 | 86);
            for (var c = 0; c < 3; c++)
            {
                Multiply(56 + c, 80 + c, 86);
                Move(c == 2 ? second : destination + c, 56 + c);
            }
        }
        void CoveragePalette(int destination, int second)
        {
            // The selected Trinity Transparent program starts with uncovered base
            // coverage, then sequentially interpolates each layer and normalizes.
            // The fifth (global) intensity owns a separate native scalar, while
            // its RGB uses an otherwise neutral layer-shadow vector.
            Add(89, 60, 61); Add(89, 62, 89); Add(89, 63, 89); Constant(88, 1);
            Emit(0x5c5c100000070000UL | 1UL << 45 | 89UL << 20 | 88UL << 8 | 84);
            Move(85, 84); Uniform(86, 5, 22); Add(87, 88, 86, true);
            Constant(89, 0); Maximum(87, 87, 89);
            for (var c = 0; c < 3; c++)
            {
                Multiply(80 + c, 56 + c, 84); Multiply(80 + c, 80 + c, 87);
                Uniform(88, 6, 32 + c); Multiply(68 + c, 88, 86); Multiply(68 + c, 68 + c, 84);
            }
            for (var layer = 0; layer < 4; layer++)
            {
                Uniform(86, 5, 18 + layer); Constant(88, 1); Add(87, 88, 86, true);
                Constant(89, 0); Maximum(87, 87, 89);
                for (var c = 0; c < 3; c++)
                {
                    Uniform(88, 6, baseSlots[layer] * 4 + c); Multiply(88, 88, 87);
                    Fma(89, 56 + c, 88, 80 + c, negativeAddend: true);
                    Fma(80 + c, 60 + layer, 89, 80 + c);
                    Uniform(88, 6, (4 + layer) * 4 + c);
                    Fma(89, 86, 88, 68 + c, negativeAddend: true);
                    Fma(68 + c, 60 + layer, 89, 68 + c);
                }
                Fma(89, 60 + layer, 85, 60 + layer, negative: true); Add(85, 85, 89);
            }
            Constant(88, BitConverter.Int32BitsToSingle(0x3727c5ac)); Maximum(85, 85, 88);
            Emit(0x5080000000470000UL | 85UL << 8 | 86);
            for (var c = 0; c < 3; c++)
            {
                Multiply(56 + c, 80 + c, 86); Multiply(68 + c, 68 + c, 86);
                Move(c == 2 ? second : destination + c, 56 + c);
            }
        }
        for (var block = 0x80; block < instructionsEnd; block += 32)
        {
            var scheduling = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var instruction = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                var prefix = instruction >> 48; var destination = (int)(instruction & 255);
                var buffer = (int)(instruction >> 34 & 31); var word = (int)(instruction >> 20 & 16383);
                var uniform = prefix is 0x4c98 or 0x4c68 || prefix is >= 0x49a0 and <= 0x49a3;
                if (sourceCutout && instruction is 0x4c68101402570703UL or 0x4c68101402570103UL)
                {
                    // Source EnableBlendStateOpaque discards fractional texture alpha
                    // after the test. Retain both native global opacity controls.
                    Uniform(3, 5, 37); opaqueOutputs++;
                    continue;
                }
                if (mode == 9 && uniform && buffer == 5 && word == 36
                    && prefix is >= 0x49a0 and <= 0x49a3
                    && unlitEmissionResults.TryGetValue((int)(instruction >> 8 & 255), out var unlitOutput))
                {
                    // Replace the completed diffuse/emission mix, before the native
                    // global color and opacity controls. Replacing its earlier delta
                    // would add the old lit diffuse back into the Unlit surface.
                    if ((int)(instruction >> 39 & 255) != unlitOutput.Diffuse || !unlitOutputs.Add(unlitOutput.Channel))
                        throw new InvalidDataException("Unlit final surface output requires another adapter.");
                    Constant(79, .7f);
                    Multiply(destination, 56 + unlitOutput.Channel, 79);
                    continue;
                }
                if (mode is 12 or 13 or 18 or 22)
                {
                    var ordinary = identity == "CB0AB209AA69541813F7395E6D3355608E047E5D1D4C66617D66E7ACF8055276";
                    if (instruction == (ordinary ? 0xD832008040270302UL : 0xD832008002E70404UL)) baseAlphaInputs++;
                    if (instruction == (ordinary ? 0x5C98078000970003UL : 0x5C98078000870003UL)) baseAlphaOutputs++;
                }
                if (mode == 11)
                {
                    // The two pinned Body00 programs sample texture alpha into
                    // R7 (ordinary) or R1 (giant), then write opacity to R3.
                    if (instruction is 0x4C68101402570703UL or 0x4C68101402570103UL) alphaInputs++;
                    if (instruction == 0x4C68101402770303UL) alphaOutputs++;
                    if (instruction == 0xE30000000007000FUL)
                    {
                        if (alphaInputs != 1 || alphaOutputs != 1 || ++additiveExits != 1)
                            throw new InvalidDataException("Additive native alpha output requires another adapter.");
                        // Native blend 5 is premultiplied alpha. Multiply the final
                        // color by texture/ConstantAlpha/OnGameAlpha, then emit zero
                        // coverage: source*alpha + destination, the donor's Add mode.
                        Multiply(0, 0, 3); Multiply(1, 1, 3); Multiply(2, 2, 3); Constant(3, 0);
                    }
                }
                if (mode == 5 && (instruction & 0xffffffffff00ff00UL) == 0xe043ff910100ff00UL && destination == 1)
                {
                    KM.Formats.SwSh.SwShShaderScheduling.Original(code, ref originalBoundary, instruction, scheduling >> (lane * 21) & 0x1ffff);
                    Move(96, 1); Move(97, 0); continue;
                }
                if (mode is 1 or 2)
                {
                    // This otherwise unused control carries source distortion strength.
                    // Keep its neutral value in the inherited color equation.
                    if (uniform && buffer == 5 && word == 17)
                    {
                        Constant(95, 0);
                        if (prefix == 0x4c98) Move(destination, 95);
                        else if (prefix == 0x4c68) Multiply(destination, (int)(instruction >> 8 & 255), 95);
                        else Fma(destination, (int)(instruction >> 8 & 255), 95, (int)(instruction >> 39 & 255), (instruction >> 48 & 1) != 0, (instruction >> 49 & 1) != 0);
                        continue;
                    }
                    if (mode == 2 && uniform && (buffer == 5 && word is >= 18 and <= 21 || buffer == 6 && word is >= 16 and <= 30 && word % 4 != 3))
                    {
                        var neutral = buffer == 5 ? (word % 2 == 0 ? 1f : 0f) : (word / 4 % 2 == 0 ? 1f : 0f);
                        Constant(95, neutral);
                        if (prefix == 0x4c98) Move(destination, 95);
                        else if (prefix == 0x4c68) Multiply(destination, (int)(instruction >> 8 & 255), 95);
                        else Fma(destination, (int)(instruction >> 8 & 255), 95, (int)(instruction >> 39 & 255), (instruction >> 48 & 1) != 0, (instruction >> 49 & 1) != 0);
                        continue;
                    }
                    if (instruction >> 56 == 0xd8 && (instruction >> 36 & 8191) == 8)
                    {
                        var coordinate = (int)(instruction >> 8 & 255);
                        var vertical = (int)(instruction >> 20 & 255);
                        if (++flowInputs != 1 || coordinate >= 55 || vertical >= 55)
                            throw new InvalidDataException("Surface flow color coordinates are unsupported.");
                        Move(80, coordinate); Move(81, vertical);
                        if (mode == 2)
                        {
                            // The independent source mask uses UV1, passed by the paired vertex program.
                            Emit(0xE043FF918157FF50UL); Emit(0xE043FF91C157FF51UL);
                            var colorTail = (int)(instruction >> 28 & 255);
                            if (colorTail >= 55) throw new InvalidDataException("Surface layer color output is unsupported.");
                            KM.Formats.SwSh.SwShShaderScheduling.Original(code, ref originalBoundary, instruction, scheduling >> (lane * 21) & 0x1ffff);
                            Move(56, destination); Move(57, destination + 1); Move(58, colorTail);
                            Uniform(88, 5, 7); Uniform(89, 5, 9); Fma(82, 80, 88, 89);
                            Uniform(88, 5, 8); Uniform(89, 5, 11); Fma(83, 81, 88, 89);
                            // This source mask is tiled across UV1 and its loop translates
                            // by one full tile. Reduce the animated tile before sampling.
                            Emit(0x5CA8148000070A00UL | 82UL << 20 | 84);
                            Emit(0x5CA8148000070A00UL | 83UL << 20 | 85);
                            Add(82, 82, 84, true); Add(83, 83, 85, true);
                            Constant(88, 1); Add(83, 88, 83, true);
                            // Tile boundaries make implicit derivatives select averaged mask
                            // mips. Sample level zero to keep the palette continuous.
                            Emit(0xd850000000070000UL | 12UL << 36 | 62UL << 28 | 83UL << 20 | 82UL << 8 | 60, true);
                            Constant(68, 0); Constant(69, 0); Constant(70, 0); Constant(71, 1); Constant(74, 1);
                            for (var layer = 3; layer >= 0; layer--)
                            {
                                Multiply(72, 60 + layer, 71);
                                for (var channel = 0; channel < 3; channel++)
                                {
                                    Uniform(73, 6, (4 + layer) * 4 + channel);
                                    Fma(68 + channel, 73, 72, 68 + channel);
                                }
                                Add(73, 74, 60 + layer, true); Multiply(71, 71, 73);
                            }
                            // Trinity tints the base image in every layer; it does
                            // not replace that image with the palette uniform.
                            Add(68, 68, 71); Add(69, 69, 71); Add(70, 70, 71);
                            Multiply(destination, 56, 68); Multiply(destination + 1, 57, 69); Multiply(colorTail, 58, 70);
                            continue;
                        }
                        Uniform(88, 5, 7); Uniform(89, 5, 9); Fma(82, 80, 88, 89);
                        Uniform(88, 5, 8); Uniform(89, 5, 11); Fma(83, 81, 88, 89);
                        // The unused native layer texture supplies independently scrolling noise.
                        Emit(0xd830000000070000UL | 12UL << 36 | 86UL << 28 | 83UL << 20 | 82UL << 8 | 84, true);
                        Uniform(88, 5, 17); Fma(80, 84, 88, 80); Fma(81, 84, 88, 81);
                        instruction = (instruction & ~(255UL << 8 | 255UL << 20)) | 80UL << 8 | 81UL << 20;
                    }
                    KM.Formats.SwSh.SwShShaderScheduling.Original(code, ref originalBoundary, instruction, scheduling >> (lane * 21) & 0x1ffff);
                    continue;
                }
                // These unused layer controls carry four independent source emission channels.
                // Keep their original neutral values in the inherited diffuse calculation.
                if (uniform && (buffer == 5 && word is >= 18 and <= 21 || buffer == 6 && word is >= 16 and <= 30 && word % 4 != 3
                    || mode is 13 or 15 or 16 or 18 or 19 && (buffer == 5 && word == 22 || buffer == 6 && word is >= 32 and <= 34)
                    || mode == 16 && (buffer == 5 && word == 23 || buffer == 6 && word is >= 36 and <= 38)))
                {
                    var neutral = mode is 13 or 15 or 16 or 18 or 19 && buffer == 5 && word == 22 ? 1
                        : mode is 13 or 15 or 16 or 18 or 19 && buffer == 6 && word is >= 32 and <= 34 ? 1
                        : mode == 16 && (buffer == 5 && word == 23 || buffer == 6 && word is >= 36 and <= 38) ? 1
                        : buffer == 5 ? (word % 2 == 0 ? 1f : 0f) : (word / 4 % 2 == 0 ? 1f : 0f);
                    Constant(79, neutral);
                    if (prefix == 0x4c98) Move(destination, 79);
                    else if (prefix == 0x4c68) Multiply(destination, (int)(instruction >> 8 & 255), 79);
                    else Fma(destination, (int)(instruction >> 8 & 255), 79, (int)(instruction >> 39 & 255), (instruction >> 48 & 1) != 0, (instruction >> 49 & 1) != 0);
                    continue;
                }
                var emissionWord = transparent ? 64 : 60;
                if (uniform && buffer == 6 && word >= emissionWord && word <= emissionWord + 2 && prefix is >= 0x49a0 and <= 0x49a3)
                {
                    var channel = word - emissionWord; var value = (int)(instruction >> 8 & 255);
                    if (!emissionCaptured || (int)(instruction >> 39 & 255) != value || !components.Add(channel))
                        throw new InvalidDataException("Layer emission output binding is ambiguous.");
                    if (mode is 17 or 21)
                    {
                        // Keep the native ConstantColor and OnGameColor operations.
                        // Their inputs now receive the Unlit palette in place of lit
                        // diffuse; the palette itself owns the unused layer vectors.
                        if (!unlitOutputs.Add(channel))
                            throw new InvalidDataException("Unlit surface input is ambiguous.");
                        if (mode == 21) Constant(79, .7f); else Uniform(79, 5, 18);
                        Multiply(value, 56 + channel, 79);
                    }
                    else if (mode == 9)
                    {
                        if (!unlitEmissionResults.TryAdd(destination, (channel, value)))
                            throw new InvalidDataException("Unlit emission intermediate is ambiguous.");
                    }
                    else Add(value, value, 68 + channel);
                }
                // Minior's four base colors use white native controls. Their inherited
                // diffuse multipliers must stay white, independently of the emission
                // injection above. Shadow controls at vector slots 1 and 3 stay intact.
                if (basePalette && uniform && buffer == 6 && baseWords.Contains(word))
                {
                    Constant(79, 1);
                    if (prefix == 0x4c98) Move(destination, 79);
                    else if (prefix == 0x4c68) Multiply(destination, (int)(instruction >> 8 & 255), 79);
                    else Fma(destination, (int)(instruction >> 8 & 255), 79, (int)(instruction >> 39 & 255), (instruction >> 48 & 1) != 0, (instruction >> 49 & 1) != 0);
                    continue;
                }
                if (basePalette && instruction >> 56 == 0xd8 && (instruction >> 36 & 8191) == 8)
                {
                    var u = (int)(instruction >> 8 & 255); var v = (int)(instruction >> 20 & 255);
                    if (u >= 56 || v >= 56) throw new InvalidDataException("Base palette coordinates are unsupported.");
                    Move(80, u); Move(81, v);
                    // Toucannon's beak mask tiles twice in U. The original albedo
                    // sample keeps its own unit coordinates and clamped sampler.
                    if (mode == 23) { Constant(82, 2); Multiply(80, 80, 82); }
                }
                if (mode is 4 or 5 or 10 && instruction >> 56 == 0xd8 && (instruction >> 36 & 8191) == 8)
                { Move(80, (int)(instruction >> 8 & 255)); Move(81, (int)(instruction >> 20 & 255)); }
                if (mode == 5 && instruction >> 56 == 0xd8 && (instruction >> 36 & 8191) == 8)
                {
                    Uniform(88, 5, 7); Uniform(89, 5, 9); Fma(82, 96, 88, 89);
                    Uniform(88, 5, 8); Uniform(89, 5, 11); Fma(83, 97, 88, 89);
                    Constant(88, 1); Add(83, 88, 83, true);
                    Emit(0xd830000000070000UL | 12UL << 36 | 86UL << 28 | 83UL << 20 | 82UL << 8 | 84, true);
                    Move(90, 84);
                    Constant(88, .5f); Add(84, 84, 88, true); Uniform(88, 5, 44);
                    Fma(80, 84, 88, 80); Fma(81, 84, 88, 81);
                    instruction = (instruction & ~(255UL << 8 | 255UL << 20)) | 80UL << 8 | 81UL << 20;
                }
                KM.Formats.SwSh.SwShShaderScheduling.Original(code, ref originalBoundary, instruction, scheduling >> (lane * 21) & 0x1ffff);
                if (instruction >> 56 != 0xd8) continue;
                var slot = (int)(instruction >> 36 & 8191); var second = (int)(instruction >> 28 & 255);
                if (slot == 8)
                {
                    if (colorCaptured || second == 255) throw new InvalidDataException("Layer emission color input is ambiguous.");
                    if (sourceCutout)
                    {
                        if (second + 1 >= 56) throw new InvalidDataException("Unlit alpha input requires another adapter.");
                        // BaseColor.w is exactly one in both pinned Archaludon palettes.
                        // Compare before global opacity and use the otherwise unused P6.
                        Constant(95, .37f);
                        Emit(0x5bb1838000070037UL | 95UL << 20 | (ulong)(second + 1) << 8);
                        Emit(0xe33000000006000fUL);
                        cutoutInputs++;
                    }
                    Move(56, destination); Move(57, destination + 1); Move(58, second); colorCaptured = true;
                    if (mode == 14)
                    {
                        Uniform(73, 5, 18);
                        for (var c = 0; c < 3; c++)
                        {
                            Uniform(74, 6, 16 + c); Multiply(68 + c, 73, 74);
                        }
                        emissionCaptured = true;
                    }
                    if (basePalette)
                    {
                        Emit(0xd830000000070000UL | 10UL << 36 | 62UL << 28 | 81UL << 20 | 80UL << 8 | 60, true);
                        if (mode == 17)
                        {
                            UnlitPalette(destination, second); emissionCaptured = true;
                            continue;
                        }
                        if (mode is 13 or 15 or 16 or 18 or 19)
                        {
                            if (mode == 16) { Move(90, 80); Move(91, 81); }
                            CoveragePalette(destination, second); emissionCaptured = true;
                            // The qualified opaque eye pass ignores albedo alpha.
                            if (mode is 15 or 16 or 19) Constant(second + 1, 1);
                            if (mode == 16)
                            {
                                // Sample the independent authored corneal normal, baked
                                // into model space. The paired vertex consumer provides
                                // the unnormalized model-space light vector at location10.
                                Emit(0xd830000000070000UL | 12UL << 36 | 86UL << 28 | 91UL << 20 | 90UL << 8 | 84, true);
                                Constant(88, 2); Constant(89, -1);
                                for (var c = 0; c < 3; c++)
                                {
                                    Fma(84 + c, 84 + c, 88, 89);
                                    Emit(0xe043ff800007ff00UL | (0x120UL / 4 + (uint)c) << 30 | 21UL << 20 | (uint)(90 + c));
                                }
                                Multiply(93, 84, 84); Fma(93, 85, 85, 93); Fma(93, 86, 86, 93);
                                Emit(0x5080000000570000UL | 93UL << 8 | 93);
                                Multiply(94, 90, 90); Fma(94, 91, 91, 94); Fma(94, 92, 92, 94);
                                Emit(0x5080000000570000UL | 94UL << 8 | 94);
                                Multiply(95, 84, 90); Fma(95, 85, 91, 95); Fma(95, 86, 92, 95);
                                Multiply(95, 95, 93); Multiply(95, 95, 94);
                                Constant(88, 0); Emit(0x5c5c100000070000UL | 88UL << 20 | 95UL << 8 | 95);
                                Uniform(89, 5, 23); Multiply(95, 95, 89);
                                for (var c = 0; c < 3; c++) { Uniform(88, 6, 36 + c); Fma(68 + c, 95, 88, 68 + c); }
                            }
                            continue;
                        }
                        Constant(80, 1); Constant(81, 1); Constant(82, 1);
                        for (var layer = 0; layer < 4; layer++) for (var c = 0; c < 3; c++)
                        {
                            Uniform(88, 6, baseSlots[layer] * 4 + c);
                            Add(89, 88, 80 + c, true); Fma(80 + c, 89, 60 + layer, 80 + c);
                        }
                        for (var c = 0; c < 3; c++)
                        {
                            Multiply(56 + c, 56 + c, 80 + c);
                            Move(c == 2 ? second : destination + c, 56 + c);
                        }
                    }
                    if (mode == 5)
                    {
                        Uniform(88, 5, 43); Uniform(89, 5, 45); Constant(91, 1); Add(91, 91, 89, true);
                        for (var c = 0; c < 3; c++)
                        {
                            Multiply(56 + c, 56 + c, 88);
                            Uniform(92, 6, 48 + c); Constant(93, 1); Add(92, 92, 93, true); Fma(92, 92, 90, 93);
                            Multiply(56 + c, 56 + c, 92);
                            Multiply(c == 2 ? second : destination + c, 56 + c, 91);
                        }
                    }
                    if (mode is 4 or 10)
                    {
                        // Keep the two authored normal fields and the refracted secondary
                        // color independent of the diffuse and object-normal bake.
                        Emit(0xd830000000070000UL | 12UL << 36 | 86UL << 28 | 81UL << 20 | 80UL << 8 | 84, true);
                        Constant(88, 2); Constant(89, -1);
                        Fma(84, 84, 88, 89); Fma(85, 85, 88, 89); Constant(88, -.35f);
                        Fma(82, 84, 88, 80); Fma(83, 85, 88, 81, negative: true);
                        Emit(0xd830000000070000UL | 22UL << 36 | 86UL << 28 | 83UL << 20 | 82UL << 8 | 84, true);
                        // The second normal displaces the reflected detail within the gem.
                        Constant(88, 2); Fma(84, 84, 88, 89); Fma(85, 85, 88, 89); Constant(88, -.35f);
                        Fma(82, 84, 88, 82); Fma(83, 85, 88, 83, negative: true);
                        if (mode == 10)
                        {
                            // Source color uses mirror-once addressing before the
                            // native clamped sampler, in the source top-origin space.
                            Emit(0x5c5810000007ff00UL | 1UL << 49 | 82UL << 20 | 82);
                            Constant(88, 1); Add(83, 88, 83, true);
                            Emit(0x5c5810000007ff00UL | 1UL << 49 | 83UL << 20 | 83);
                            Add(83, 88, 83, true);
                        }
                        Emit(0xd830000000070000UL | 10UL << 36 | 62UL << 28 | 83UL << 20 | 82UL << 8 | 60, true);
                        for (var c = 0; c < 3; c++) { Uniform(88, 6, 20 + c); Multiply(60 + c, 60 + c, 88); }
                        // View-dependent Fresnel opacity uses the native world position
                        // and geometric normal, with the source angle bias and endpoints.
                        for (var c = 0; c < 3; c++)
                        {
                            Emit(0xe043ff8e0157ff00UL | (ulong)c << 30 | (uint)(90 + c));
                            Uniform(94 + c, 3, 76 + c); Add(90 + c, 94 + c, 90 + c, true);
                            Emit(0xe043ff8c0157ff00UL | (ulong)c << 30 | (uint)(98 + c));
                        }
                        Multiply(93, 90, 90); Fma(93, 91, 91, 93); Fma(93, 92, 92, 93);
                        Emit(0x5080000000570000UL | 93UL << 8 | 93);
                        Multiply(97, 98, 98); Fma(97, 99, 99, 97); Fma(97, 100, 100, 97);
                        Emit(0x5080000000570000UL | 97UL << 8 | 97);
                        Multiply(94, 90, 98); Fma(94, 91, 99, 94); Fma(94, 92, 100, 94);
                        Multiply(94, 94, 93); Multiply(94, 94, 97);
                        Constant(88, 0); Emit(0x5c5c100000070000UL | 88UL << 20 | 94UL << 8 | 94);
                        Constant(88, -.999999f); Constant(89, 1); Fma(94, 94, 88, 89);
                        Emit(0x5080000000570000UL | 94UL << 8 | 95); Multiply(94, 94, 95);
                        Constant(88, -.8f); Constant(89, .8f); Fma(94, 94, 88, 89);
                        Uniform(96, 5, 18); Uniform(97, 5, 19); Multiply(97, 97, 94);
                        for (var c = 0; c < 3; c++)
                        {
                            Uniform(88, 6, 16 + c); Multiply(68 + c, 56 + c, 88); Multiply(68 + c, 68 + c, 96);
                            Fma(68 + c, 60 + c, 97, 68 + c);
                            Add(88, 60 + c, 56 + c, true); Fma(c == 2 ? second : destination + c, 88, 94, 56 + c);
                        }
                        emissionCaptured = true;
                    }
                }
                if (mode is 4 or 10 or 13 or 14 or 15 or 16 or 17 or 18 or 19) continue;
                if (slot != 10) continue;
                if (!colorCaptured || emissionCaptured) throw new InvalidDataException("Layer emission mask input is ambiguous.");
                if (mode is 6 or 23)
                {
                    var u = (int)(instruction >> 8 & 255); var v = (int)(instruction >> 20 & 255);
                    if (u >= 56 || v >= 56) throw new InvalidDataException("Mirrored emission mask coordinates are unsupported.");
                    // Preserve the source compressed image and all its mips. The material's
                    // mirrored U sampler applies the authored two-tile mask independently
                    // of the baked diffuse image, including its implicit derivatives.
                    Constant(82, 2); Multiply(80, u, 82); Move(81, v);
                }
                // Read all four source mask channels using the native color coordinates.
                Emit(0xd830000000070000UL | 10UL << 36 | 62UL << 28 | (mode is 5 or 6 or 23 ? 81UL << 20 | 80UL << 8 : instruction & (255UL << 20 | 255UL << 8)) | 60, true);
                if (mode == 7) Constant(63, 0);
                if (mode == 11) { Constant(61, 0); Constant(62, 0); Constant(63, 0); }
                if (mode == 3)
                {
                    Uniform(73, 5, 18);
                    for (var channel = 0; channel < 3; channel++) Multiply(68 + channel, 60 + channel, 73);
                    emissionCaptured = true;
                    continue;
                }
                Constant(68, 0); Constant(69, 0); Constant(70, 0); Constant(71, 1); Constant(74, 1);
                for (var layer = 3; layer >= 0; layer--)
                {
                    Multiply(72, 60 + layer, 71); Uniform(73, 5, 18 + layer); Multiply(72, 72, 73);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        Uniform(73, 6, (4 + layer) * 4 + channel);
                        if (mode != 5) Add(73, 73, 56 + channel);
                        Fma(68 + channel, 73, 72, 68 + channel);
                    }
                    Add(73, 74, 60 + layer, true); Multiply(71, 71, 73);
                }
                if (mode == 5)
                {
                    Uniform(73, 5, 42); Multiply(72, 90, 73); Uniform(74, 5, 45);
                    for (var c = 0; c < 3; c++)
                    { Uniform(73, 6, 72 + c); Fma(68 + c, 73, 72, 68 + c); Fma(68 + c, 56 + c, 74, 68 + c); }
                }
                emissionCaptured = true;
            }
        }
        if (mode == 11 && additiveExits != 1) throw new InvalidDataException("Additive native output is missing.");
        if (sourceCutout && (cutoutInputs != 1 || opaqueOutputs != 1))
            throw new InvalidDataException("Unlit cutout input or opaque output is ambiguous.");
        if (mode is 9 or 17 or 21 && !unlitOutputs.SetEquals(new[] { 0, 1, 2 }))
            throw new InvalidDataException("Unlit final output coverage is incomplete.");
        if (mode is 12 or 13 or 18 or 22 && (baseAlphaInputs != 1 || baseAlphaOutputs != 1))
            throw new InvalidDataException("Transparent base palette alpha flow requires another adapter.");
        if (mode is 1 or 2 ? flowInputs != 1 : !colorCaptured || !emissionCaptured || !components.SetEquals(new[] { 0, 1, 2 }))
            throw new InvalidDataException("Layer emission shader contract is incomplete.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = checked(0x80 + code.Count / 3 * 32);
        var newConstants = (newEnd + 255) & ~255;
        var newLength = (newConstants + constantLength + 255) & ~255;
        if (newLength > capacity) throw new InvalidDataException("Layer emission shader extension exceeds its native allocation.");
        var output = template.ToArray();
        if (sourceCutout) output[data + 0x31] |= 0x80; // SPH commonWord0.KillsPixels, bit15.
        if (mode == 16) output[data + 0x30 + 24 + 10] |= 0x2a; // perspective location10.xyz.
        if (mode == 2) output[data + 0x30 + 24 + 9] |= 0xa0;
        output.AsSpan(data + 0x80, capacity - 0x80).Clear();
        template.AsSpan(data + constants, constantLength).CopyTo(output.AsSpan(data + newConstants));
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, 4), value);
        Write(stage + 24, newLength); Write(control + 0x6f8, newEnd - 0x30);
        Write(control + 0x700, newConstants); Write(control + 0x704, newLength); Write(control + 0x70c, mode is 4 or 5 or 10 ? 112 : mode is 1 or 2 or 6 or 8 or 9 or 12 or 13 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 ? 96 : 80);
        for (var index = 0; index < code.Count; index += 3)
        {
            var offset = data + 0x80 + index / 3 * 32;
            var scheduling = code[index].Control | code[index + 1].Control << 21 | code[index + 2].Control << 42;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(offset, 8), scheduling);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(offset + 8 + lane * 8, 8), code[index + lane].Instruction);
        }
        return output;
    }

}
