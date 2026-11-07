// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Formats.SV;

namespace KM.Formats.SwSh;

/// <summary>Shared authored RG normals on qualified opaque ZA body surfaces.</summary>
public static class SwShZaTangentBodyShader
{
    public const string Body = "ZaTangentBody";
    public const string BodyAoMirror = "ZaTangentBodyAoMirror";
    public const string SourceBase = "ZaTangentSourceBase";
    public const string MirrorBase = "ZaTangentMirrorBase";
    public const string SharedUvBase = "ZaTangentSharedUvBase";
    public static bool Recognizes(string name) => name is Body or BodyAoMirror or SourceBase or MirrorBase or SharedUvBase;
    public static string Parent(string name) => name switch
    {
        Body or BodyAoMirror => "Body00", SourceBase => SvSwShSourceBase.ProgramName,
        MirrorBase => SvSwShSourceBase.MirroredProgramName,
        SharedUvBase => SvSwShSourceBase.SharedUvProgramName,
        _ => throw new InvalidDataException("Unknown ZA tangent body program."),
    };

    public static byte[] Vertex(byte[] template) => SwShTangentNormalShader.Vertex(template);

    public static byte[] Fragment(byte[] original, string program)
    {
        int OriginalPointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(original.AsSpan(at, 8)));
        if (original.Length < 512 || !original.AsSpan(0, 4).SequenceEqual("BNSH"u8))
            throw new InvalidDataException("ZA tangent body requires the native shader container.");
        var originalStage = OriginalPointer(OriginalPointer(OriginalPointer(0x80) + 16) + 40);
        var originalData = OriginalPointer(originalStage + 16);
        var originalLength = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(originalStage + 24, 4));
        var identity = Convert.ToHexString(SHA256.HashData(original.AsSpan(originalData, originalLength)));
        var ordinary = identity == "1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37";
        if (!ordinary && identity != "FBB44266C993CCFF1EC67B48E26A89B13981836CD0A6071E4F2BF8F4156135A2")
            throw new InvalidDataException("ZA tangent body requires its pinned native lighting stage.");
        var template = program switch
        {
            Body or BodyAoMirror => original, SourceBase => SvSwShLayerEmissionShader.SourceLayerBase(original),
            MirrorBase => SvSwShLayerEmissionShader.MirroredSourceBase(original),
            SharedUvBase => SvSwShLayerEmissionShader.SharedUvSourceBase(original),
            _ => throw new InvalidDataException("ZA tangent body palette program is unsupported."),
        };
        int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at, 4));
        int P(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(at, 8)));
        var stage = P(P(P(0x80) + 16) + 40); var control = P(stage + 8); var data = P(stage + 16);
        var end = checked(I(control + 0x6f8) + 0x30); var constants = I(control + 0x700);
        var constantLength = I(control + 0x6fc); var length = I(stage + 24); var relocation = I(24);
        if (I(control) != unchecked((int)0x98761234) || I(data) != 0x12345678 || end % 32 != 0
            || end <= 0x80 || end > constants || constants + constantLength > length
            || !template.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || I(relocation + 8) != 6)
            throw new InvalidDataException("ZA tangent body binary layout changed.");
        var section = Enumerable.Range(0, 6).Select(i => relocation + 16 + i * 24).Single(s => I(s + 8) == data);
        var capacity = I(section + 12);
        if (length > capacity || data + capacity > template.Length || template.AsSpan(data + length, capacity - length).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("ZA tangent body code allocation is unavailable.");
        var code = new List<(ulong Instruction, ulong Control)>(); int boundary = 0, normals = 0;
        bool perspective = false, sampled = false; int occlusion = 0;
        void Emit(ulong w) => code.Add((w, SwShShaderScheduling.Serial(w)));
        void Move(int d, int s) => Emit(0x5c98078000070000UL | (ulong)s << 20 | (uint)d);
        void Constant(int d, float v) => Emit(0x010000000007f000UL | (ulong)BitConverter.SingleToUInt32Bits(v) << 20 | (uint)d);
        void Multiply(int d, int a, int b) => Emit(0x5c68100000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Fma(int d, int a, int b, int c, bool negative = false) => Emit(0x59a0000000070000UL | (negative ? 1UL << 48 : 0) | (ulong)c << 39 | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        for (var block = 0x80; block < end; block += 32)
        {
            var schedule = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var w = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                var component = ordinary ? w switch { 0x5c68100000170b20UL => 0, 0x5c68100000170824UL => 1, 0x5c68100000170023UL => 2, _ => -1 }
                    : w switch { 0x5c68100000f72a2aUL => 0, 0x5c68100000f70d29UL => 1, 0x5c68100000f70928UL => 2, _ => -1 };
                if (component >= 0)
                {
                    if (!sampled) throw new InvalidDataException("ZA tangent normal is consumed before its sample.");
                    Move((int)(w & 255), 112 + component); normals++; continue;
                }
                if (program == BodyAoMirror && w >> 56 == 0xd8 && (w >> 36 & 8191) == 14)
                {
                    var coordinate = (int)(w >> 8 & 255);
                    if (coordinate >= 55) throw new InvalidDataException("ZA source occlusion coordinates changed.");
                    Constant(102, 2); Multiply(104, coordinate, 102); Move(105, coordinate + 1);
                    w = (w & ~0xff00UL) | 104UL << 8;
                }
                SwShShaderScheduling.Original(code, ref boundary, w, schedule >> (lane * 21) & 0x1ffff);
                var first = (int)(w & 255);
                if ((w & 0xffffffffff00ff00UL) == 0xe043ff910100ff00UL && first == 1)
                {
                    if (perspective) throw new InvalidDataException("ZA tangent perspective input is ambiguous.");
                    Move(74, (int)(w >> 20 & 255)); perspective = true;
                }
                if (program is Body or BodyAoMirror && w >> 56 == 0xd8 && (w >> 36 & 8191) == 14)
                {
                    // The shared source BC4 stores visibility. The native lighting
                    // input stores inverse visibility, zero green and 128/255 blue.
                    var blue = (int)(w >> 28 & 255);
                    if (++occlusion != 1 || first >= 55 || blue >= 56)
                        throw new InvalidDataException("ZA source occlusion sample boundary changed.");
                    Constant(102, -1); Constant(103, 1); Fma(first, first, 102, 103);
                    Constant(first + 1, 0); Constant(blue, 128f / 255);
                }
                if (w >> 56 != 0xd8 || (w >> 36 & 8191) != 16) continue;
                var second = (int)(w >> 28 & 255);
                if (!perspective || sampled || first >= 55 || second >= 56)
                    throw new InvalidDataException("ZA tangent normal sample boundary changed.");
                // Keep the source compressed RG map, its full mips and implicit LOD.
                // The source unit-height normal reconstructs Z from its two channels.
                Constant(102, 2); Constant(103, -1);
                Fma(96, first, 102, 103); Fma(97, first + 1, 102, 103);
                Constant(98, 1); Fma(98, 96, 96, 98, true); Fma(98, 97, 97, 98, true);
                Constant(102, 0); Emit(0x5c60178000070000UL | 102UL << 20 | 98UL << 8 | 98);
                Emit(0x5080000000870000UL | 98UL << 8 | 98);
                code.AddRange(SwShZaFresnelInputs.Geometry(false));
                code.AddRange(SwShZaFresnelView.Create());
                Multiply(104, 99, 99); Fma(104, 100, 100, 104); Fma(104, 101, 101, 104);
                Emit(0x5080000000570000UL | 104UL << 8 | 104);
                for (var c = 0; c < 3; c++) Multiply(112 + c, 99 + c, 104);
                Constant(first, .5f); Constant(first + 1, .5f); Constant(second, 1); sampled = true;
            }
        }
        if (!perspective || !sampled || normals != 3 || occlusion != (program is Body or BodyAoMirror ? 1 : 0)) throw new InvalidDataException("ZA tangent normal bindings are incomplete.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = checked(0x80 + code.Count / 3 * 32); var newConstants = (newEnd + 255) & ~255;
        var newLength = (newConstants + constantLength + 255) & ~255;
        if (newLength > capacity) throw new InvalidDataException("ZA tangent normal exceeds the native code allocation.");
        var output = template.ToArray(); output.AsSpan(data + 0x80, capacity - 0x80).Clear();
        template.AsSpan(data + constants, constantLength).CopyTo(output.AsSpan(data + newConstants));
        foreach (var (location, c) in new[] { (1, 3), (2, 3), (3, 3), (7, 2), (8, 3), (9, 2), (9, 3) })
            output[data + 0x30 + 24 + location] |= (byte)(2 << (c * 2));
        void Write(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at, 4), value);
        Write(stage + 24, newLength); Write(control + 0x6f8, newEnd - 0x30); Write(control + 0x700, newConstants);
        Write(control + 0x704, newLength); Write(control + 0x70c, 116);
        for (var i = 0; i < code.Count; i += 3)
        {
            var at = data + 0x80 + i / 3 * 32;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(at), code[i].Control | code[i + 1].Control << 21 | code[i + 2].Control << 42);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(at + 8 + lane * 8), code[i + lane].Instruction);
        }
        return output;
    }
}
