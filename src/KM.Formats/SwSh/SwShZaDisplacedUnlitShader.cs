// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.SwSh;

/// <summary>Five-layer source Unlit shading and normal-directed vertex displacement.</summary>
public static class SwShZaDisplacedUnlitShader
{
    public static byte[] Vertex(byte[] template, string program = ZA.ZaSwShDisplacedUnlit.Program) => Convert(template, true, program);
    public static byte[] Fragment(byte[] template, string program = ZA.ZaSwShDisplacedUnlit.Program) => Convert(template, false, program);

    private static byte[] Convert(byte[] template, bool vertex, string program)
    {
        if (!ZA.ZaSwShDisplacedUnlit.Recognizes(program)) throw new InvalidDataException("Unknown source Unlit program.");
        var six = ZA.ZaSwShDisplacedUnlit.SixLayers(program);
        var displacementUv1 = program.EndsWith("Uv1", StringComparison.Ordinal);
        var secondary = six || displacementUv1;
        var cutout = program.EndsWith("Cutout", StringComparison.Ordinal);
        int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at, 4));
        int P(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(at, 8)));
        if (template.Length < 512 || !template.AsSpan(0, 4).SequenceEqual("BNSH"u8))
            throw new InvalidDataException("Source Unlit requires a native BNSH container.");
        var stage = P(P(P(0x80) + 16) + (vertex ? 8 : 40));
        var control = P(stage + 8); var data = P(stage + 16); var length = I(stage + 24);
        var hash = System.Convert.ToHexString(SHA256.HashData(template.AsSpan(data, length)));
        if (vertex ? hash != "5692C7A693293F7C076613DC7F5C4E57095EB29E9A4E7269552285681EFA1004"
            : hash is not ("1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37" or "FBB44266C993CCFF1EC67B48E26A89B13981836CD0A6071E4F2BF8F4156135A2"))
            throw new InvalidDataException("Source Unlit native stage identity changed.");
        var end = checked(I(control + 0x6f8) + 0x30); var constants = I(control + 0x700);
        var constantLength = I(control + 0x6fc); var relocation = I(24);
        if (I(control) != unchecked((int)0x98761234) || I(data) != 0x12345678 || end <= 0x80 || end % 32 != 0
            || end > constants || constants + constantLength > length || I(control + 0x704) != length
            || !template.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || I(relocation + 8) != 6)
            throw new InvalidDataException("Source Unlit native stage layout changed.");
        var section = Enumerable.Range(0, 6).Select(i => relocation + 16 + i * 24).Single(s => I(s + 8) == data);
        var capacity = I(section + 12);
        if (length > capacity || data + capacity > template.Length || template.AsSpan(data + length, capacity - length).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Source Unlit code allocation is unavailable.");
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
        void Palette(int u, int v)
        {
            Uv(u, v, 78); Sample(56, 24); Sample(60, 26);
            if (six)
            {
                Emit(0xE043FF918157FF4CUL); Emit(0xE043FF91C157FF4DUL);
                Uv(76, 77, 91, sourceVertex: true); Sample(64, 30);
                Uniform(88, 86); Mul(64, 64, 88);
                if (cutout)
                {
                    Uniform(95, 95);
                    Emit(0x5bb1838000070037UL | 95UL << 20 | 64UL << 8);
                    Emit(0xe33000000006000fUL);
                }
            }
            for (var c = 0; c < 3; c++) { Uniform(88, 58 + c); Mul(56 + c, 56 + c, 88); }
            for (var l = 0; l < 4; l++) { Uniform(88, 54 + l); Mul(60 + l, 60 + l, 88); }
            Add(89, 60, 61); Add(89, 89, 62); Add(89, 89, 63); Constant(88, 1);
            if (six) Add(89, 89, 64);
            Add(84, 88, 89, true); Constant(89, 0); Emit(0x5c60178000070000UL | 89UL << 20 | 84UL << 8 | 84);
            Move(85, 84);
            for (var c = 0; c < 3; c++) Mul(68 + c, 56 + c, 84);
            for (var l = 0; l < (six ? 5 : 4); l++)
            {
                for (var c = 0; c < 3; c++)
                {
                    Uniform(88, l == 4 ? 87 + c : 62 + l * 4 + c); Mul(89, 56 + c, 88); Add(89, 89, 68 + c, true);
                    Fma(68 + c, 60 + l, 89, 68 + c);
                }
                Constant(88, 1); Add(89, 88, 85, true); Fma(85, 60 + l, 89, 85);
            }
            Constant(88, 1e-5f); Emit(0x5c60178000070000UL | 88UL << 20 | 85UL << 8 | 85);
            Emit(0x5080000000470000UL | 85UL << 8 | 86);
            for (var c = 0; c < 3; c++) Mul(56 + c, 68 + c, 86);
        }
        if (vertex)
        {
            Load(76, displacementUv1 ? 0xc0 : 0xb0); Load(77, displacementUv1 ? 0xc4 : 0xb4);
            Uv(76, 77, 82, sourceVertex: true); Sample(84, 28, true);
            Emit(0x5080000000170000UL | 84UL << 8 | 84); // MUFU.SIN
            Load(88, 0xf0); Uniform(89, 53); Mul(84, 84, 88); Mul(84, 84, 89); Constant(88, 100); Mul(84, 84, 88);
            for (var c = 0; c < 3; c++) Load(72 + c, 0x90 + c * 4);
        }
        var sampled = 0; var positions = new HashSet<int>();
        var emission = new Dictionary<int, (int Channel, int Diffuse)>(); var outputs = new HashSet<int>();
        for (var block = 0x80; block < end; block += 32)
        {
            var scheduling = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var w = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                var prefix = w >> 48; var d = (int)(w & 255); var buffer = (int)(w >> 34 & 31); var word = (int)(w >> 20 & 16383);
                if (vertex && secondary && w == 0xE30000000007000FUL)
                {
                    Load(76, 0xc0); Load(77, 0xc4);
                    Emit(0xEFF07F801187FF4CUL); Emit(0xEFF07F8011C7FF4DUL);
                }
                if (!vertex && prefix is >= 0x49a0 and <= 0x49a3 && buffer == 5 && word == 36
                    && emission.TryGetValue((int)(w >> 8 & 255), out var result))
                {
                    if ((int)(w >> 39 & 255) != result.Diffuse || !outputs.Add(result.Channel))
                        throw new InvalidDataException("Source Unlit output boundary is ambiguous.");
                    Uniform(88, 52); Mul(d, 56 + result.Channel, 88); continue;
                }
                if (!vertex && w >> 56 == 0xd8 && (w >> 36 & 8191) == 8)
                {
                    if (++sampled != 1) throw new InvalidDataException("Source Unlit coordinate boundary is ambiguous.");
                    Palette((int)(w >> 8 & 255), (int)(w >> 20 & 255));
                }
                if (!vertex && prefix is >= 0x49a0 and <= 0x49a3 && buffer == 6 && word is >= 60 and <= 62)
                {
                    var diffuse = (int)(w >> 8 & 255);
                    if ((int)(w >> 39 & 255) != diffuse || !emission.TryAdd(d, (word - 60, diffuse)))
                        throw new InvalidDataException("Source Unlit emission boundary is ambiguous.");
                }
                SwShShaderScheduling.Original(code, ref boundary, w, scheduling >> (lane * 21) & 0x1ffff);
                if (!vertex && w >> 56 == 0xd8 && (w >> 36 & 8191) == 8)
                {
                    var second = (int)(w >> 28 & 255);
                    if (second >= 55) throw new InvalidDataException("Source Unlit alpha boundary changed.");
                    Uniform(88, 61); Mul(second + 1, 59, 88);
                }
                if (vertex && (w & 0xffffffff0000ff00UL) == 0xefd87f800000ff00UL)
                {
                    var attr = (int)(w >> 20 & 1023);
                    if (attr is 0x80 or 0x84 or 0x88)
                    {
                        var c = (attr - 0x80) / 4;
                        if (!positions.Add(c)) throw new InvalidDataException("Source Unlit position load is ambiguous.");
                        Fma(d, 72 + c, 84, d);
                    }
                }
            }
        }
        if (vertex ? !positions.SetEquals([0, 1, 2]) : sampled != 1 || !outputs.SetEquals([0, 1, 2]))
            throw new InvalidDataException("Source Unlit stage conversion is incomplete.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = checked(0x80 + code.Count / 3 * 32); var newConstants = (newEnd + 255) & ~255;
        var newLength = (newConstants + constantLength + 255) & ~255;
        if (newLength > capacity) throw new InvalidDataException("Source Unlit shader exceeds its native allocation.");
        var output = template.ToArray(); output.AsSpan(data + 0x80, capacity - 0x80).Clear();
        if (vertex) output[data + 0x30 + 24 + 7 / 2] |= 0x10; // COLOR0 red (generic attribute 7).
        if (vertex && secondary)
        {
            output[data + 0x30 + 24 + 4 / 2] |= 3;
            output[data + 0x30 + 54 + 9 / 2] |= 0xc0;
        }
        if (!vertex && six) output[data + 0x30 + 24 + 9] |= 0xa0;
        if (!vertex && cutout) output[data + 0x31] |= 0x80;
        template.AsSpan(data + constants, constantLength).CopyTo(output.AsSpan(data + newConstants));
        void Write(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at, 4), value);
        Write(stage + 24, newLength); Write(control + 0x6f8, newEnd - 0x30); Write(control + 0x700, newConstants);
        Write(control + 0x704, newLength); Write(control + 0x70c, 96);
        for (var i = 0; i < code.Count; i += 3)
        {
            var at = data + 0x80 + i / 3 * 32;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(at, 8), code[i].Control | code[i + 1].Control << 21 | code[i + 2].Control << 42);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(at + 8 + lane * 8, 8), code[i + lane].Instruction);
        }
        return SV.SvSwShPointLightReflection.Add(output, vertex, authored: new(secondary,
            vertex ? [("SourceUnlitDisplacementMap", 10)]
            : six ? [("SourceUnlitBaseColorMap", 8), ("SourceUnlitLayerMaskMap", 9), ("SourceUnlitOpacityMap", 11)]
            : [("SourceUnlitBaseColorMap", 8), ("SourceUnlitLayerMaskMap", 9)]));
    }
}
