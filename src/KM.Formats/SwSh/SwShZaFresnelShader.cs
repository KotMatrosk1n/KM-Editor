// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Formats.SV;

namespace KM.Formats.SwSh;

/// <summary>Places the authored gem surface into the pinned native lighting program.</summary>
public static class SwShZaFresnelShader
{
    public static byte[] Vertex(byte[] template, int layerUv, int aoUv, int sourceVariant, int maskUv)
    {
        RequireSelectors(layerUv, aoUv, sourceVariant, maskUv);
        return SwShZaFresnelInputs.Vertex(template, NeedsSecondaryUv(layerUv, aoUv, sourceVariant, maskUv));
    }

    // Source resources, material selectors, geometry and sampler states must be
    // qualified and bound separately. Constructing a program does not enable it.
    public static byte[] Fragment(byte[] template, int layerUv, int aoUv, int sourceVariant, int maskUv, int probeWidth)
    {
        RequireSelectors(layerUv, aoUv, sourceVariant, maskUv);
        var surface = SwShZaFresnelPipeline.Create(layerUv, aoUv, sourceVariant, maskUv, probeWidth);
        int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at, 4));
        int P(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(at, 8)));
        if (template.Length < 512 || !template.AsSpan(0, 4).SequenceEqual("BNSH"u8) || I(0x7c) != 1)
            throw new InvalidDataException("ZA gem requires its native opaque shader container.");
        var stage = P(P(P(0x80) + 16) + 40); var control = P(stage + 8); var data = P(stage + 16); var length = I(stage + 24);
        var identity = Convert.ToHexString(SHA256.HashData(template.AsSpan(data, length)));
        bool ordinary = identity == "1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37";
        if (!ordinary && identity != "FBB44266C993CCFF1EC67B48E26A89B13981836CD0A6071E4F2BF8F4156135A2"
            || I(stage + 28) < 0x848 || I(control) != unchecked((int)0x98761234) || I(data) != 0x12345678
            || I(control + 0x708) != 0x30 || I(control + 0x704) != length || I(control + 0x70c) > 56)
            throw new InvalidDataException("ZA gem native lighting program requires another adapter.");
        var end = checked(I(control + 0x6f8) + 0x30); var constants = I(control + 0x700); var constantLength = I(control + 0x6fc);
        if (end <= 0x80 || end % 32 != 0 || end > constants || constants + constantLength > length)
            throw new InvalidDataException("ZA gem native binary layout is invalid.");
        var code = new List<(ulong Instruction, ulong Control)>(); int boundary = 0;
        bool perspective = false, composed = false; int normals = 0, albedo = 0, ambient = 0, mask = 0, layer = 0;
        var globalColors = new HashSet<int>();
        void Emit(ulong word) => code.Add((word, SwShShaderScheduling.Serial(word)));
        void Move(int d, int s) => Emit(0x5c98078000070000UL | (ulong)s << 20 | (uint)d);
        void Constant(int d, float v) => Emit(0x010000000007f000UL | (ulong)BitConverter.SingleToUInt32Bits(v) << 20 | (uint)d);
        void Multiply(int d, int a, int b) => Emit(0x5c68100000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Add(int d, int a, int b) => Emit(0x5c58100000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        for (var block = 0x80; block < end; block += 32)
        {
            var schedule = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var word = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                int component = ordinary ? word switch
                { 0x5C68100000170B20UL => 0, 0x5C68100000170824UL => 1, 0x5C68100000170023UL => 2, _ => -1 }
                    : word switch { 0x5C68100000F72A2AUL => 0, 0x5C68100000F70D29UL => 1, 0x5C68100000F70928UL => 2, _ => -1 };
                if (component >= 0)
                {
                    if (!composed) throw new InvalidDataException("ZA gem normal is consumed before its material surface.");
                    Move((int)(word & 255), 71 + component); normals++; continue;
                }
                var uniform = (word >> 62 & 1) != 0 && (word >> 61 & 1) == 0;
                var buffer = (int)(word >> 34 & 31); var offset = (int)(word >> 20 & 0x3fff);
                if (uniform && buffer == 6 && offset is >= 60 and <= 62 && word >> 48 is >= 0x49a0 and <= 0x49a3)
                {
                    int c = offset - 60, value = (int)(word >> 8 & 255);
                    if (!composed || value != (int)(word >> 39 & 255) || !globalColors.Add(c))
                        throw new InvalidDataException("ZA gem native global color boundary changed.");
                    // Native lighting, dirt and weather already produced value.
                    // Source emission and the authored AO-attenuated probe join
                    // before intact native ConstantColor/OnGameColor controls.
                    Add(value, value, 68 + c); Add(value, value, 64 + c);
                }
                SwShShaderScheduling.Original(code, ref boundary, word, schedule >> (lane * 21) & 0x1ffff);
                var first = (int)(word & 255);
                if ((word & 0xffffffffff00ff00UL) == 0xe043ff910100ff00UL && first == 1)
                {
                    if (perspective) throw new InvalidDataException("ZA gem native perspective input is ambiguous.");
                    Move(74, (int)(word >> 20 & 255)); perspective = true;
                }
                if (word >> 56 != 0xd8) continue;
                int tid = (int)(word >> 36 & 8191), second = (int)(word >> 28 & 255);
                if (tid == 16)
                {
                    if (!perspective || composed || first >= 56 || second >= 56)
                        throw new InvalidDataException("ZA gem requires the pinned native normal sample boundary.");
                    // The original native 2D samples use implicit derivative LOD
                    // without a shader bias. Preserve that policy explicitly.
                    Constant(78, 0); code.AddRange(surface);
                    for (var c = 0; c < 3; c++)
                    {
                        Move(71 + c, 99 + c);
                        // Source AO is the exact TEX20 red operand at source
                        // fragment0 instructions736..738, outside scene lighting.
                        Multiply(64 + c, 64 + c, 63);
                    }
                    Multiply(75, 71, 71);
                    Emit(0x59a0000000070000UL | 75UL << 39 | 72UL << 20 | 72UL << 8 | 75);
                    Emit(0x59a0000000070000UL | 75UL << 39 | 73UL << 20 | 73UL << 8 | 75);
                    Emit(0x5080000000570000UL | 75UL << 8 | 75);
                    for (var c = 0; c < 3; c++) Multiply(71 + c, 71 + c, 75);
                    // Retain finite unused native intermediates. Its three final
                    // world-normal consumers above receive the source normal.
                    Constant(first, .5f); Constant(first + 1, .5f); Constant(second, 1); composed = true;
                }
                else if (tid == 8)
                {
                    if (!composed || second >= 56 || ++albedo != 1) throw new InvalidDataException("ZA gem native albedo boundary changed.");
                    Move(first, 56); Move(first + 1, 57); Move(second, 58); Constant(second + 1, 1);
                }
                else if (tid == 14)
                {
                    if (!composed || second >= 56 || ++ambient != 1) throw new InvalidDataException("ZA gem native AO boundary changed.");
                    Move(first, 63); Move(first + 1, 63); Move(second, 63);
                }
                else if (tid == 10)
                {
                    if (!composed || second != 255 || ++mask != 1) throw new InvalidDataException("ZA gem native layer mask boundary changed.");
                    // Its two native layer-mask channels do not describe the
                    // source Fresnel layer, already composed from its own inputs.
                    Constant(first, 0); Constant(first + 1, 0);
                }
                else if (tid == 12)
                {
                    if (!composed || second >= 56 || ++layer != 1) throw new InvalidDataException("ZA gem native layer boundary changed.");
                    Constant(first, 0); Constant(first + 1, 0); Constant(second, 0); Constant(second + 1, 0);
                }
            }
        }
        if (!perspective || !composed || normals != 3 || albedo != 1 || ambient != 1 || mask != 1 || layer != 1 || globalColors.Count != 3)
            throw new InvalidDataException("ZA gem native lighting bindings are incomplete.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, SwShShaderScheduling.Serial(0x50b0000000070f00UL)));
        var newEnd = checked(0x80 + code.Count / 3 * 32); var newConstants = (newEnd + 255) & ~255; var newLength = (newConstants + constantLength + 255) & ~255;
        var payload = new byte[newLength]; template.AsSpan(data, 0x80).CopyTo(payload);
        template.AsSpan(data + constants, constantLength).CopyTo(payload.AsSpan(newConstants));
        foreach (var (location, c) in new[] { (1, 3), (2, 3), (3, 3), (7, 2), (8, 3), (9, 2), (9, 3) })
            payload[0x30 + 24 + location] |= (byte)(2 << (c * 2));
        bool secondary = NeedsSecondaryUv(layerUv, aoUv, sourceVariant, maskUv);
        if (secondary)
        {
            if ((payload[0x30 + 24 + 10] & 15) != 0) throw new InvalidDataException("ZA gem UV1 varying is already assigned.");
            payload[0x30 + 24 + 10] |= 10;
        }
        for (var i = 0; i < code.Count; i += 3)
        {
            var at = 0x80 + i / 3 * 32;
            BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(at), code[i].Control | code[i + 1].Control << 21 | code[i + 2].Control << 42);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(at + 8 + lane * 8), code[i + lane].Instruction);
        }
        var reflected = SvSwShPointLightReflection.Add(template, vertex: false,
            zaGem: new SvSwShPointLightReflection.ZaGemBindings(secondary, sourceVariant >= 6));
        return ReplaceProgram(reflected, payload, stage, control, newEnd, newConstants);
    }

    private static bool NeedsSecondaryUv(int layer, int ao, int variant, int mask) => layer == 1 || ao == 1 || variant >= 6 && mask == 1;
    private static void RequireSelectors(int layer, int ao, int variant, int mask)
    {
        if (layer is < -1 or > 1 || ao is < -1 or > 1 || mask is < -1 or > 1 || variant is not (0 or 2 or 6 or 8) || variant < 6 && mask != 0)
            throw new InvalidDataException("ZA gem shader requires explicit source selectors.");
    }

    private static byte[] ReplaceProgram(byte[] original, byte[] payload, int stage, int control, int end, int constants)
    {
        int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(at, 4));
        var relocation = I(24); var count = I(relocation + 8);
        if (!original.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || count != 7)
            throw new InvalidDataException("ZA gem requires the qualified reflected shader sections.");
        var data = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(original.AsSpan(stage + 16, 8)));
        var sections = Enumerable.Range(0, count).Select(s => relocation + 16 + s * 24)
            .Where(at => I(at + 8) == data).ToArray();
        if (sections.Length != 1) throw new InvalidDataException("ZA gem native code section is ambiguous.");
        var capacity = I(sections[0] + 12); var oldLength = I(stage + 24);
        if (payload.Length > capacity || oldLength > capacity || data + capacity > original.Length
            || original.AsSpan(data + oldLength, capacity - oldLength).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("ZA gem exceeds its existing native code allocation.");
        var output = original.ToArray(); output.AsSpan(data, capacity).Clear(); payload.CopyTo(output, data);
        void Write(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at, 4), value);
        Write(stage + 24, payload.Length); Write(control + 0x6f8, end - 0x30); Write(control + 0x700, constants);
        Write(control + 0x704, payload.Length); Write(control + 0x70c, 112); return output;
    }
}
