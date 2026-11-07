// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.SwSh;

/// <summary>Six-layer palettes and independent Fresnel emission under native scene lighting.</summary>
public static class SwShZaLayerSurfaceShader
{
    public static byte[] Vertex(byte[] template, string program = ZA.ZaSwShLayerSurface.Program) => Convert(template, true, program);
    public static byte[] Fragment(byte[] template, string program = ZA.ZaSwShLayerSurface.Program) => Convert(template, false, program);

    private static byte[] Convert(byte[] template, bool vertex, string program)
    {
        if (!ZA.ZaSwShLayerSurface.Recognizes(program)) throw new InvalidDataException("Unknown source Unlit program.");
        var six = ZA.ZaSwShLayerSurface.SixLayers(program);
        var blend = program.EndsWith("Blend", StringComparison.Ordinal);
        var displaced = program.EndsWith("Displaced", StringComparison.Ordinal);
        var iep = program.Contains("Iep", StringComparison.Ordinal);
        var noise = program.EndsWith("Noise", StringComparison.Ordinal);
        var fresnel = program.Contains("Fresnel", StringComparison.Ordinal);
        var mask1 = program.EndsWith("Mask1", StringComparison.Ordinal);
        var mask0 = program.EndsWith("Mask0", StringComparison.Ordinal);
        var displacementUv1 = false;
        var secondary = six || iep || displacementUv1;
        var cutout = program.EndsWith("Cutout", StringComparison.Ordinal);
        int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at, 4));
        int P(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(at, 8)));
        if (template.Length < 512 || !template.AsSpan(0, 4).SequenceEqual("BNSH"u8))
            throw new InvalidDataException("Source six-layer surface requires a native BNSH container.");
        var stage = P(P(P(0x80) + 16) + (vertex ? 8 : 40));
        var control = P(stage + 8); var data = P(stage + 16); var length = I(stage + 24);
        var hash = System.Convert.ToHexString(SHA256.HashData(template.AsSpan(data, length)));
        if (vertex ? hash != "5692C7A693293F7C076613DC7F5C4E57095EB29E9A4E7269552285681EFA1004"
            : hash is not ("1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37" or "FBB44266C993CCFF1EC67B48E26A89B13981836CD0A6071E4F2BF8F4156135A2"))
            throw new InvalidDataException("Source six-layer surface native stage identity changed.");
        var end = checked(I(control + 0x6f8) + 0x30); var constants = I(control + 0x700);
        var constantLength = I(control + 0x6fc); var relocation = I(24);
        if (I(control) != unchecked((int)0x98761234) || I(data) != 0x12345678 || end <= 0x80 || end % 32 != 0
            || end > constants || constants + constantLength > length || I(control + 0x704) != length
            || !template.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || I(relocation + 8) != 6)
            throw new InvalidDataException("Source six-layer surface native stage layout changed.");
        var section = Enumerable.Range(0, 6).Select(i => relocation + 16 + i * 24).Single(s => I(s + 8) == data);
        var capacity = I(section + 12);
        if (length > capacity || data + capacity > template.Length || template.AsSpan(data + length, capacity - length).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Source six-layer surface code allocation is unavailable.");
        var code = new List<(ulong Instruction, ulong Control)>(); int boundary = 0;
        void Emit(ulong w) => code.Add((w, SwShShaderScheduling.Serial(w)));
        void Move(int d, int s) => Emit(0x5c98078000070000UL | (ulong)s << 20 | (uint)d);
        void Constant(int d, float v) => Emit(0x010000000007f000UL | (ulong)BitConverter.SingleToUInt32Bits(v) << 20 | (uint)d);
        void Uniform(int d, int word) => Emit(0x4c98078000070000UL | 5UL << 34 | (ulong)word << 20 | (uint)d);
        void Mul(int d, int a, int b) => Emit(0x5c68100000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Add(int d, int a, int b, bool negative = false) => Emit(0x5c58100000070000UL | (negative ? 1UL << 45 : 0) | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Fma(int d, int a, int b, int c, bool negativeAddend = false) => Emit(0x59a0000000070000UL | (negativeAddend ? 1UL << 49 : 0) | (ulong)c << 39 | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Load(int d, int attr) => Emit(0xefd87f800007ff00UL | (ulong)attr << 20 | (uint)d);
        void Uv(int u, int v, int first, bool sourceVertex = false)
        {
            Uniform(88, first + 2); Add(80, u, 88, true); Uniform(88, first); Mul(80, 80, 88);
            // Native interpolated UVs have flipped V; vertex attributes retain source V.
            Constant(89, 1);
            if (sourceVertex) Move(81, v); else Add(81, 89, v, true);
            Uniform(88, first + 3); Add(81, 81, 88, true);
            Uniform(88, first + 1); Mul(81, 81, 88); Add(81, 89, 81, true);
        }
        void Sample(int d, int binding, bool lod0 = false) => Emit((lod0 ? 0xd850000000070000UL : 0xd830000000070000UL)
            | (ulong)binding << 36 | (ulong)(d + 2) << 28 | 81UL << 20 | 80UL << 8 | (uint)d);
        void Parameter(int d, string name, int c = 0) => Uniform(d, ZA.ZaSwShLayerSurface.Word(name, c));
        void Maximum(int d, int a, int b) => Emit(0x5c60178000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Minimum(int d, int a, int b) => Emit(0x5c60138000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Store(int s, int attr) => Emit(0xeff07f800007ff00UL | (ulong)attr << 20 | (uint)s);
        void Reciprocal(int d, int s) => Emit(0x5080000000470000UL | (ulong)s << 8 | (uint)d);
        void Floor(int d, int s) => Emit(0x5ca8148000070a00UL | (ulong)s << 20 | (uint)d);
        void Saturate(int d) { Constant(88, 0); Maximum(d, d, 88); Constant(88, 1); Minimum(d, d, 88); }
        void Power(int d, string exponent)
        {
            Constant(88, 1e-20f); Maximum(d, d, 88);
            Emit(0x5080000000370000UL | (ulong)d << 8 | (uint)d);
            Parameter(88, exponent); Mul(d, d, 88);
            Emit(0x5c90008000070000UL | (ulong)d << 20 | (uint)d);
            Emit(0x5080000000270000UL | (ulong)d << 8 | (uint)d);
        }
        void Normalize(int first, int temporary)
        {
            Mul(temporary, first, first); Fma(temporary, first + 1, first + 1, temporary); Fma(temporary, first + 2, first + 2, temporary);
            Emit(0x5080000000570000UL | (ulong)temporary << 8 | (uint)temporary);
            for (var c = 0; c < 3; c++) Mul(first + c, first + c, temporary);
        }
        void EffectFrame()
        {
            // The pinned native skin matrix is live at its first position load.
            // Retain the source's inverse-squared column correction for N and T.
            int[][] columns = [[29, 26, 8], [5, 35, 37], [21, 19, 24]];
            for (var c = 0; c < 3; c++)
            {
                var v = columns[c]; Mul(50 + c, v[0], v[0]); Fma(50 + c, v[1], v[1], 50 + c); Fma(50 + c, v[2], v[2], 50 + c);
                Reciprocal(50 + c, 50 + c); Load(40 + c, 0x90 + c * 4);
                if (iep) Load(43 + c, 0xa0 + c * 4);
            }
            if (iep) Load(67, 0xac);
            for (var t = 0; t < (iep ? 2 : 1); t++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var first = 60 + t * 3; var input = 40 + t * 3;
                    Mul(first + c, columns[1][c], 51); Mul(first + c, first + c, input + 1);
                    Mul(66, columns[0][c], 50); Fma(first + c, 66, input, first + c);
                    Mul(66, columns[2][c], 52); Fma(first + c, 66, input + 2, first + c);
                }
                Normalize(60 + t * 3, 66);
            }
        }
        void Interpolate(int destination, int attribute) => Emit(0xe043ff800007ff00UL & ~(255UL << 30)
            | (ulong)(attribute / 4) << 30 | 110UL << 20 | (uint)destination);
        void ViewDot()
        {
            for (var c = 0; c < 3; c++)
            {
                Interpolate(96 + c, 0xe0 + c * 4);
                Emit(0x4c98078000070000UL | 3UL << 34 | (ulong)(76 + c) << 20 | (uint)(100 + c));
                Add(96 + c, 100 + c, 96 + c, true);

            }
            Mul(99, 96, 96); Fma(99, 97, 97, 99); Fma(99, 98, 98, 99);
            Emit(0x5080000000570000UL | 99UL << 8 | 99);
            Mul(107, 104, 104); Fma(107, 105, 105, 107); Fma(107, 106, 106, 107);
            Emit(0x5080000000570000UL | 107UL << 8 | 107);
            Mul(75, 96, 104); Fma(75, 97, 105, 75); Fma(75, 98, 106, 75);
            Mul(75, 75, 99); Mul(75, 75, 107); Constant(88, 0); Maximum(75, 75, 88); Constant(88, 1); Minimum(75, 75, 88);
        }
        void Palette(int u, int v)
        {
            Uv(u, v, ZA.ZaSwShLayerSurface.Word("UVScaleOffset")); Sample(56, 24);
            if (secondary) { Interpolate(76, 0x118); Interpolate(77, 0x11c); }
            if (mask1) Uv(76, 77, ZA.ZaSwShLayerSurface.Word("UVScaleOffsetLayerMask"), sourceVertex: true);
            // Heatran's source variants sample LayerMaskMap at UV0 directly,
            // independently of the doubled BaseColorMap coordinates.
            else if (mask0) Uv(u, v, ZA.ZaSwShLayerSurface.Word("UVScaleOffsetLayerMask"));
            Sample(60, 26);
            if (six) { Uv(76, 77, ZA.ZaSwShLayerSurface.Word("UVScaleOffset5"), sourceVertex: true); Sample(64, 28); } else Constant(64, 0);
            for (var l = 0; l < 5; l++) { Parameter(88, "LayerMaskScale" + (l + 1)); Mul(60 + l, 60 + l, 88); }
            Add(89, 60, 61); Add(89, 89, 62); Add(89, 89, 63); Add(89, 89, 64); Constant(88, 1);
            Add(78, 88, 89, true); Constant(89, 0); Maximum(78, 78, 89);
            Parameter(84, "EmissionIntensity"); Add(85, 88, 84, true); Maximum(85, 85, 89);
            for (var c = 0; c < 3; c++)
            {
                Parameter(88, "BaseColor", c); Mul(68 + c, 56 + c, 88); Mul(68 + c, 68 + c, 85); Mul(68 + c, 68 + c, 78);
                Parameter(88, "EmissionColor", c); Mul(72 + c, 84, 88); Mul(72 + c, 72 + c, 78);
            }
            if (fresnel || blend || iep) ViewDot();
            if (blend) { BlendPalette(); return; }
            for (var l = 0; l < (six ? 5 : 4); l++)
            {
                var layer = (l + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Parameter(84, "EmissionIntensityLayer" + layer); Constant(88, 1); Add(85, 88, 84, true); Constant(89, 0); Maximum(85, 85, 89);
                if (fresnel)
                {
                    Parameter(88, "FresnelAngleBias" + layer); Add(86, 75, 88, true); Constant(89, 0); Maximum(86, 86, 89);
                    Constant(88, 1); Add(86, 88, 86, true); Constant(89, 1e-20f); Maximum(86, 86, 89);
                    Emit(0x5080000000370000UL | 86UL << 8 | 86);
                    Parameter(88, "FresnelPower" + layer); Mul(86, 86, 88);
                    Emit(0x5c90008000070000UL | 86UL << 20 | 86); // RRO.EX2
                    Emit(0x5080000000270000UL | 86UL << 8 | 86);
                    Parameter(88, "FresnelAlphaMin" + layer); Parameter(89, "FresnelAlphaMax" + layer); Add(89, 89, 88, true); Fma(86, 86, 89, 88);
                }
                for (var c = 0; c < 3; c++)
                {
                    Parameter(88, "BaseColorLayer" + layer, c); Mul(89, 56 + c, 88); Mul(89, 89, 85); Add(89, 89, 68 + c, true); Fma(68 + c, 60 + l, 89, 68 + c);
                    Parameter(87, "EmissionColorLayer" + layer, c);
                    if (fresnel) { Parameter(88, "FresnelColor" + layer, c); Add(88, 88, 87, true); Fma(87, 86, 88, 87); }
                    Mul(87, 87, 84); Add(87, 87, 72 + c, true); Fma(72 + c, 60 + l, 87, 72 + c);
                }
                Constant(88, 1); Add(89, 88, 78, true); Fma(78, 60 + l, 89, 78);
            }
            Constant(88, 1e-5f); Maximum(78, 78, 88); Emit(0x5080000000470000UL | 78UL << 8 | 79);
            for (var c = 0; c < 3; c++) { Mul(68 + c, 68 + c, 79); Mul(72 + c, 72 + c, 79); }
            if (iep) InsideEmission();
            if (noise) UpwardNoise();
        }
        void InsideEmission()
        {
            // Original InsideEmissionParallax variant 147: source tangent view,
            // independent height/intensity UVs, tile-local wrapping and rim.
            for (var c = 0; c < 7; c++) Interpolate(112 + c, 0x120 + c * 4);
            Normalize(112, 128); Normalize(115, 128);
            // B = cross(N,T) * tangent sign; view was normalized by ViewDot.
            Mul(119, 113, 117); Mul(128, 114, 116); Add(119, 119, 128, true);
            Mul(120, 114, 115); Mul(128, 112, 117); Add(120, 120, 128, true);
            Mul(121, 112, 116); Mul(128, 113, 115); Add(121, 121, 128, true);
            for (var c = 0; c < 3; c++) { Mul(119 + c, 119 + c, 118); Mul(96 + c, 96 + c, 99); }
            Mul(122, 96, 115); Fma(122, 97, 116, 122); Fma(122, 98, 117, 122);
            Mul(123, 96, 119); Fma(123, 97, 120, 123); Fma(123, 98, 121, 123);
            Uv(76, 77, ZA.ZaSwShLayerSurface.Word("UVScaleOffsetInsideEmissionParallaxHeight"), true); Sample(124, 30);
            Parameter(88, "InsideEmissionParallaxHeight"); Parameter(89, "InsideEmissionParallaxOffset"); Fma(124, 124, 88, 89);
            Uv(76, 77, ZA.ZaSwShLayerSurface.Word("UVScaleOffsetInsideEmissionParallaxIntensity"), true);
            Move(125, 80); Move(126, 81); Fma(80, 122, 124, 80);
            Mul(128, 123, 124); Add(81, 81, 128, true);
            for (var axis = 0; axis < 2; axis++)
            {
                Parameter(128, "InsideEmissionParallaxIntensityMapLattice" + (axis == 0 ? "U" : "V"));
                Reciprocal(129, 128); Mul(130, 125 + axis, 128); Floor(130, 130); Mul(130, 130, 129);
                Parameter(131, "InsideEmissionParallaxIntensityMapRangeAvoidEdge"); Constant(132, .01f); Fma(130, 131, 132, 130);
                Constant(132, -.02f); Fma(129, 131, 132, 129); Reciprocal(128, 129);
                Add(131, 80 + axis, 130, true); Mul(132, 131, 128); Floor(132, 132); Mul(132, 132, 129);
                Add(131, 131, 132, true); Add(80 + axis, 130, 131);
            }
            Sample(124, 28);
            Constant(88, 1); Add(128, 88, 75, true); Mul(129, 128, 128); Mul(129, 129, 129); Mul(128, 128, 129);
            Parameter(129, "EmissionRimF0"); Parameter(130, "EmissionRimF90"); Add(130, 130, 129, true); Fma(129, 128, 130, 129); Saturate(129);
            Emit(0x4896038000070000UL | 5UL << 34 | (ulong)ZA.ZaSwShLayerSurface.Word("EmissionRimThreshold") << 20 | 129UL << 8 | 130);
            Parameter(131, "EmissionRimThreshold"); Add(131, 130, 131, true); Reciprocal(131, 131);
            Add(132, 130, 129, true); Mul(132, 132, 131); Power(132, "EmissionRimCurve"); Saturate(132);
            Add(131, 129, 130, true); Fma(129, 131, 132, 130); Saturate(129);
            Parameter(130, "EmissionRimRate"); Mul(129, 129, 130); Constant(88, 1); Add(130, 88, 129, true);
            Parameter(131, "EmissionIntensity"); Mul(129, 129, 131);
            for (var c = 0; c < 3; c++) { Parameter(131, "EmissionRimColor", c); Mul(72 + c, 72 + c, 130); Fma(72 + c, 129, 131, 72 + c); }
            Parameter(129, "InsideEmissionParallaxF0"); Parameter(130, "InsideEmissionParallaxF90"); Add(130, 130, 129, true); Fma(129, 128, 130, 129); Constant(88, 0); Maximum(129, 129, 88);
            Constant(130, 0);
            for (var l = 0; l < 4; l++) { Parameter(131, "EnableIEPTexture" + (l + 1)); Fma(130, 60 + l, 131, 130); }
            Parameter(131, "InsideEmissionParallaxIntensity"); Mul(129, 129, 131); Mul(129, 129, 130);
            for (var c = 0; c < 3; c++) Fma(72 + c, 124 + c, 129, 72 + c);
        }
        void UpwardNoise()
        {
            // Gengar OutputEmission variant: two object-space projections,
            // squared normal weights and the authored smooth height fade.
            for (var c = 0; c < 6; c++) Interpolate(112 + c, 0x120 + c * 4);
            Normalize(115, 128); Parameter(118, "EffectTime"); Parameter(119, "NoiseScrollSpeed");
            Mul(118, 118, 119); Add(118, 113, 118, true); Parameter(119, "NoiseScale");
            Mul(120, 112, 119); Mul(121, 118, 119); Mul(122, 114, 119);
            Move(80, 120); Move(81, 121); Sample(124, 28); Move(123, 124);
            Move(80, 121); Move(81, 122); Sample(124, 28);
            Mul(128, 117, 117); Mul(123, 123, 128); Mul(128, 115, 115); Fma(123, 124, 128, 123);
            Parameter(128, "NoiseFadeStart"); Parameter(129, "NoiseFadeEnd"); Add(129, 129, 128, true); Reciprocal(129, 129);
            Add(128, 113, 128, true); Mul(128, 128, 129); Saturate(128);
            Mul(129, 128, 128); Constant(130, -2); Constant(131, 3); Fma(128, 128, 130, 131); Mul(128, 128, 129);
            Parameter(129, "NoiseIntensity"); Mul(128, 128, 129); Add(129, 129, 128, true); Mul(129, 129, 123);
            for (var c = 0; c < 3; c++) { Parameter(128, "NoiseBaseColor", c); Fma(72 + c, 129, 128, 72 + c); }
        }
        void BlendPalette()
        {
            for (var c = 0; c < 3; c++) Move(152 + c, 56 + c);
            for (var c = 0; c < 4; c++) Move(148 + c, 60 + c);
            // Source layer5 mask is HighlightMaskMap red, at the primary UV.
            Sample(64, 28); Move(155, 64);
            Sample(64, 30); Move(156, 64);
            Sample(64, 32); Move(157, 64);
            Sample(64, 34); Move(158, 64);
            Parameter(88, "FresnelAngleBias"); Add(86, 75, 88, true); Constant(89, 0); Maximum(86, 86, 89);
            Constant(88, 1); Add(86, 88, 86, true);
            Emit(0x5080000000370000UL | 86UL << 8 | 86);
            Parameter(88, "FresnelPower"); Mul(86, 86, 88);
            Emit(0x5c90008000070000UL | 86UL << 20 | 86);
            Emit(0x5080000000270000UL | 86UL << 8 | 159);
            Constant(161, BitConverter.UInt32BitsToSingle(0x3727c5ac));
            // The normalization cut is the ordered coverage of the source's
            // four masks and its residual; source clamps the divisor to 1e-5.
            Add(86, 148, 149); Add(86, 86, 150); Add(86, 86, 151); Constant(88, 1);
            Add(86, 88, 86, true); Constant(89, 0); Maximum(86, 86, 89); Minimum(86, 86, 88);
            for (var l = 0; l < 4; l++) { Add(89, 88, 86, true); Fma(86, 148 + l, 89, 86); }
            Constant(88, 1e-5f); Maximum(86, 86, 88);
            Emit(0x5080000000470000UL | 86UL << 8 | 160);
            var kernel = ZaSwShEelektrossBodyColor.CreateKernel((buffer, word) =>
            {
                var input = ZaSwShEelektrossBodyColor.Inputs[ZaSwShEelektrossBodyColor.Index(buffer, word)];
                return ZA.ZaSwShLayerSurface.Word(input.Property, input.Component ?? 0);
            });
            foreach (var instruction in kernel.Instructions) Emit(instruction);
            for (var c = 0; c < 3; c++) { Move(68 + c, 173 + c); Move(72 + c, 170 + c); }
        }
        if (vertex && displaced)
        {
            Load(76, 0xb0); Load(77, 0xb4);
            Uv(76, 77, ZA.ZaSwShLayerSurface.Word("UVScaleOffset3"), sourceVertex: true); Sample(84, 28, true);
            Emit(0x5c90000000070000UL | 84UL << 20 | 84);
            Emit(0x5080000000170000UL | 84UL << 8 | 84);
            Load(88, 0xf0); Parameter(89, "DisplacementHeight"); Mul(84, 84, 88); Mul(84, 84, 89); Constant(88, 100); Mul(84, 84, 88);
            for (var c = 0; c < 3; c++) Load(72 + c, 0x90 + c * 4);
        }
        bool perspective = false; var effectFrames = 0; var objectPositions = 0;
        var normalMask = 0;
        var sampled = 0; var positions = new HashSet<int>();
        var blendAo = 0; var blendSpecular = 0;
        var emission = new Dictionary<int, (int Channel, int Diffuse)>(); var outputs = new HashSet<int>();
        for (var block = 0x80; block < end; block += 32)
        {
            var scheduling = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var w = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                var prefix = w >> 48; var d = (int)(w & 255); var buffer = (int)(w >> 34 & 31); var word = (int)(w >> 20 & 16383);
                if (vertex && (iep || noise) && w == 0xefd87f800807ff07UL) { EffectFrame(); effectFrames++; }
                if (vertex && (iep || noise) && w == 0xE30000000007000FUL)
                {
                    if (iep)
                    {
                        for (var t = 0; t < 2; t++) for (var c = 0; c < 3; c++)
                        {
                            var r = 112 + t * 3 + c; var input = 60 + t * 3;
                            Emit(0x4c68100000070000UL | 3UL << 34 | (ulong)(16 + c) << 20 | (ulong)input << 8 | (uint)r);
                            Emit(0x49a0000000070000UL | (ulong)r << 39 | 3UL << 34 | (ulong)(20 + c) << 20 | (ulong)(input + 1) << 8 | (uint)r);
                            Emit(0x49a0000000070000UL | (ulong)r << 39 | 3UL << 34 | (ulong)(24 + c) << 20 | (ulong)(input + 2) << 8 | (uint)r);
                        }
                        Normalize(112, 128); Normalize(115, 128);
                        for (var c = 0; c < 6; c++) Store(112 + c, 0x120 + c * 4);
                        Store(67, 0x138);
                    }
                    else for (var c = 0; c < 3; c++) Store(60 + c, 0x12c + c * 4);
                }
                if (vertex && secondary && w == 0xE30000000007000FUL)
                {
                    Load(76, 0xc0); Load(77, 0xc4);
                    Emit(0xEFF07F801187FF4CUL); Emit(0xEFF07F8011C7FF4DUL);
                }
                if (!vertex && w >> 56 == 0xd8 && (w >> 36 & 8191) == 8)
                {
                    if (++sampled != 1) throw new InvalidDataException("Source six-layer surface coordinate boundary is ambiguous.");
                    if (!perspective) throw new InvalidDataException("Source layer surface perspective producer is absent.");
                    // Native Body00 completes X immediately after its albedo
                    // sample; its exact X operands are already live here.
                    if (normalMask != 6) throw new InvalidDataException("Source layer surface normal boundary changed.");
                    if (hash == "1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37") Mul(104, 11, 1);
                    else Mul(104, 42, 15);
                    Palette((int)(w >> 8 & 255), (int)(w >> 20 & 255));
                }
                if (!vertex && prefix is >= 0x49a0 and <= 0x49a3 && buffer == 6 && word is >= 60 and <= 62)
                {
                    var diffuse = (int)(w >> 8 & 255); var channel = word - 60;
                    if ((int)(w >> 39 & 255) != diffuse || !outputs.Add(channel)) throw new InvalidDataException("Source six-layer emission boundary is ambiguous.");
                    // Body00 first calculates diffuse * ConstantColor - diffuse;
                    // its following FMA adds diffuse once. Replace only that
                    // difference with source emission, preserving the final add.
                    Move(d, 72 + channel); continue;
                }
                // Heatran's molten surfaces combine the authored layer palette
                // and glow without the native Body00's achromatic specular tint.
                // Keep a neutral lighting factor for shape and leave the native
                // metal materials on their original lighting path.
                if (!vertex && mask0 && w == 0xE30000000007000FUL)
                {
                    if (iep)
                    {
                        Constant(88, .48f); Constant(89, .44f);
                        Fma(90, 75, 89, 88);
                    }
                    else Constant(90, 1);
                    for (var channel = 0; channel < 3; channel++)
                        Fma(channel, 68 + channel, 90, 72 + channel);
                }
                SwShShaderScheduling.Original(code, ref boundary, w, scheduling >> (lane * 21) & 0x1ffff);
                if (!vertex && blend && w is 0xD83200AFF0072500UL or 0xD83200AFF0570404UL)
                {
                    // AmbientTex red is native occlusion, rather than visibility.
                    Move(d, 180); blendAo++;
                }
                if (!vertex && blend && w is 0x4C68101401D72A2BUL or 0x4C68101401D71F1CUL)
                {
                    // Convert perceptual roughness to the native specular lobe.
                    // Keep native scene lighting and the source's Fresnel blend.
                    Mul(184, 179, 179); Constant(185, .002f); Maximum(184, 184, 185);
                    Reciprocal(184, 184); Constant(185, 2); Mul(184, 184, 185);
                    Add(184, 184, 185, true); Maximum(184, 184, 185); Mul(d, (int)(w >> 8 & 255), 184);
                    blendSpecular++;
                }
                if (vertex && noise && w == 0x5C58100000770207UL)
                {
                    // Native skinned homogeneous position, before world transform.
                    Constant(88, .01f); int[] point = [15, 4, 7];
                    for (var c = 0; c < 3; c++) { Mul(89, point[c], 88); Store(89, 0x120 + c * 4); }
                    objectPositions++;
                }
                if (!vertex && (w & 0xffffffffff00ff00UL) == 0xe043ff910100ff00UL && d == 1)
                { if (perspective) throw new InvalidDataException("Source layer surface perspective producer is ambiguous."); Move(110, (int)(w >> 20 & 255)); perspective = true; }
                if (!vertex && w is 0x5C68100000170824UL or 0x5C68100000F70D29UL) { Move(105, d); normalMask |= 2; }
                if (!vertex && w is 0x5C68100000170023UL or 0x5C68100000F70928UL) { Move(106, d); normalMask |= 4; }
                if (vertex && displaced && (w & 0xffffffff0000ff00UL) == 0xefd87f800000ff00UL)
                {
                    var attr = (int)(w >> 20 & 1023);
                    if (attr is 0x80 or 0x84 or 0x88)
                    {
                        var c = (attr - 0x80) / 4;
                        if (!positions.Add(c)) throw new InvalidDataException("Source surface position load is ambiguous.");
                        Fma(d, 72 + c, 84, d);
                    }
                }
                if (!vertex && w >> 56 == 0xd8 && (w >> 36 & 8191) == 8)
                {
                    var second = (int)(w >> 28 & 255);
                    if (second >= 55) throw new InvalidDataException("Source six-layer surface alpha boundary changed.");
                    Move(d, 68); Move(d + 1, 69); Move(second, 70);
                }

            }
        }
        if (vertex && displaced && !positions.SetEquals([0, 1, 2])) throw new InvalidDataException("Source surface displacement is incomplete.");
        if (!vertex && blend && (blendAo != 1 || blendSpecular != 1)) throw new InvalidDataException("Source Fresnel blend lighting boundary changed.");
        if (vertex && (iep || noise) && (effectFrames != 1 || noise && objectPositions != 1)) throw new InvalidDataException("Source effect geometry boundary changed.");
        if (!vertex && (sampled != 1 || !outputs.SetEquals([0, 1, 2])))
            throw new InvalidDataException("Source six-layer surface stage conversion is incomplete.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = checked(0x80 + code.Count / 3 * 32); var newConstants = (newEnd + 255) & ~255;
        var newLength = (newConstants + constantLength + 255) & ~255;
        var payload = new byte[newLength]; template.AsSpan(data, 0x80).CopyTo(payload);

        if (vertex && displaced) payload[0x30 + 24 + 7 / 2] |= 0x10;
        if (vertex && secondary)
        {
            payload[0x30 + 24 + 4 / 2] |= 3;
            payload[0x30 + 52 + 9 / 2] |= 0xc0;
        }
        if (!vertex && secondary) payload[0x30 + 24 + 9] |= 0xa0;
        if (iep || noise)
        {
            if (vertex)
            {
                if (iep) payload[0x30 + 24 + 1] |= 15;
                for (var c = 0; c < (iep ? 7 : 6); c++) { var attribute = 10 * 4 + c; payload[0x30 + 52 + attribute / 8] |= (byte)(1 << (attribute % 8)); }
            }
            else for (var c = 0; c < (iep ? 7 : 6); c++) { var attribute = 10 * 4 + c; payload[0x30 + 24 + attribute / 4] |= (byte)(2 << (attribute % 4 * 2)); }
        }
        if (!vertex && cutout) payload[0x31] |= 0x80;
        template.AsSpan(data + constants, constantLength).CopyTo(payload.AsSpan(newConstants));
        for (var i = 0; i < code.Count; i += 3)
        {
            var at = 0x80 + i / 3 * 32;
            BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(at, 8), code[i].Control | code[i + 1].Control << 21 | code[i + 2].Control << 42);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(at + 8 + lane * 8, 8), code[i + lane].Instruction);
        }
        var output = SwShAuthoredProgramAllocation.Replace(template, stage, control, data, payload, newEnd, newConstants, blend ? 192 : iep || noise ? 144 : 112);
        return SV.SvSwShPointLightReflection.Add(output, vertex, authored: new(secondary,
            vertex ? displaced ? [("SourceLayerDisplacementMap", 10)] : []
            : blend ? [("SourceLayerBaseColorMap", 8), ("SourceLayerLayerMaskMap", 9), ("SourceLayerHighlightMaskMap", 10), ("SourceLayerMetallicMap", 11), ("SourceLayerRoughnessMap", 12), ("SourceLayerAOMap", 13)]
            : six ? [("SourceLayerBaseColorMap", 8), ("SourceLayerLayerMaskMap", 9), ("SourceLayerOpacityMap1", 10)]
            : iep ? [("SourceLayerBaseColorMap", 8), ("SourceLayerLayerMaskMap", 9), ("SourceLayerInsideEmissionParallaxIntensityMap", 10), ("SourceLayerParallaxMap", 11)]
            : noise ? [("SourceLayerBaseColorMap", 8), ("SourceLayerLayerMaskMap", 9), ("SourceLayerNoiseSourceMap", 10)]
            : [("SourceLayerBaseColorMap", 8), ("SourceLayerLayerMaskMap", 9)], EffectFrame: iep || noise, Tangent: iep));
    }
}
