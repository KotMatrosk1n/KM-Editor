using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.ZA;

public static class ZaSwShEyeShader
{
    public static byte[] Convert(byte[] template, bool objectNormals = false, bool lava = false, bool independentHighlights = false, bool layerEmission = false, bool layerTint = false, bool mirrorEmissionColor = false)
    {
        if (mirrorEmissionColor && !layerEmission)
            throw new InvalidDataException("Mirrored emission eye color requires its layer consumer.");
        if ((lava ? 1 : 0) + (independentHighlights ? 1 : 0) + (layerEmission ? 1 : 0) + (layerTint ? 1 : 0) > 1)
            throw new InvalidDataException("Eye effects require separate samplers.");
        int Integer(int offset) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(offset, 4));
        int Pointer(int offset) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(offset, 8)));
        if (template.Length < 512 || !template.AsSpan(0, 4).SequenceEqual("BNSH"u8) || Integer(0x7c) != 1)
            throw new InvalidDataException("Eye shader container is unsupported.");
        var program = Pointer(Pointer(0x80) + 16);
        var stage = Pointer(program + 0x28);
        var control = Pointer(stage + 8);
        var data = Pointer(stage + 16);
        var length = Integer(stage + 24);
        var controlLength = Integer(stage + 28);
        var identity = System.Convert.ToHexString(SHA256.HashData(template.AsSpan(data, length)));
        if (identity is not ("1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37" or "FBB44266C993CCFF1EC67B48E26A89B13981836CD0A6071E4F2BF8F4156135A2"))
            throw new InvalidDataException("Eye shader program needs another adapter.");
        if (controlLength < 0x848 || Integer(control) != unchecked((int)0x98761234) || Integer(data) != 0x12345678
            || Integer(control + 0x708) != 0x30 || Integer(control + 0x704) != length)
            throw new InvalidDataException("Eye shader binary contract is unsupported.");
        var instructionsEnd = checked(Integer(control + 0x6f8) + 0x30);
        var constants = Integer(control + 0x700);
        var constantLength = Integer(control + 0x6fc);
        if (instructionsEnd <= 0x80 || instructionsEnd > constants || constants + constantLength > length
            || Integer(control + 0x70c) > 56 || instructionsEnd % 32 != 0)
            throw new InvalidDataException("Eye shader allocation is unsupported.");
        var relocation = Integer(24);
        if (!template.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || Integer(relocation + 8) != 6)
            throw new InvalidDataException("Eye shader relocation is unsupported.");
        var sections = Enumerable.Range(0, 6).Select(index => relocation + 16 + index * 24)
            .Where(section => Integer(section + 8) == data).ToArray();
        if (sections.Length != 1) throw new InvalidDataException("Eye shader data section is ambiguous.");
        var capacity = Integer(sections[0] + 12);
        if (length > capacity || data + capacity > template.Length || template.AsSpan(data + length, capacity - length).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Eye shader extension storage is unavailable.");
        var code = new List<(ulong Instruction, ulong Control)>(); int originalBoundary = 0;
        var textures = new HashSet<int>();
        var capturedUv = false;
        var normalPatched = false;
        var colorPatched = false;
        var preservedLayerControls = 0;
        var emissionReads = identity == "1EAC62E9112F11530759B7F8FD91C53ED73EF5C4B2195591282EC4E354C07D37"
            ? new HashSet<ulong>
        {
            0x49A2169802472D28UL, 0x49A213180257260FUL, 0x49A2179802672F28UL,
            0x49A2169802072D2EUL, 0x49A2179802272F31UL, 0x49A2131802172630UL,
            0x49A2031803C70603UL, 0x49A2049803D70902UL, 0x49A2001803E70005UL
        } : new HashSet<ulong>
        {
            0x49A218180247302EUL, 0x49A2181802073031UL, 0x49A219180257320FUL,
            0x49A2191802173231UL, 0x49A2191802673231UL, 0x49A2191802273233UL,
            0x49A2029803C70504UL, 0x49A2001803D70006UL, 0x49A2011803E70207UL
        };
        void Emit(ulong instruction, bool asynchronous = false) =>
            code.Add((instruction, KM.Formats.SwSh.SwShShaderScheduling.Serial(instruction)));
        void Move(int destination, int source) => Emit(0x5c98078000070000UL | (ulong)source << 20 | (uint)destination);
        void Constant(int destination, float value) => Emit(0x010000000007f000UL | (ulong)BitConverter.SingleToUInt32Bits(value) << 20 | (uint)destination);
        void Multiply(int destination, int left, int right) => Emit(0x5c68100000070000UL | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void Add(int destination, int left, int right, bool subtract = false) =>
            Emit(0x5c58100000070000UL | (subtract ? 1UL << 45 : 0) | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void Fma(int destination, int left, int right, int addend, bool negateProduct = false) =>
            Emit(0x59a0000000070000UL | (negateProduct ? 1UL << 48 : 0) | (ulong)addend << 39 | (ulong)right << 20 | (uint)left << 8 | (uint)destination);
        void Uniform(int destination, int buffer, int word) =>
            Emit(0x4c98078000070000UL | (ulong)buffer << 34 | (ulong)word << 20 | (uint)destination);
        void Sample(int slot, int horizontal, int vertical, int destination) =>
            Emit(0xd830000000070000UL | (ulong)slot << 36 | (ulong)(destination + 2) << 28 | (ulong)vertical << 20 | (uint)horizontal << 8 | (uint)destination, true);
        void Normalize(int first, int count)
        {
            Multiply(75, first, first);
            for (var component = 1; component < count; component++) Fma(75, first + component, first + component, 75);
            Emit(0x5080000000570000UL | 75UL << 8 | 75, true);
            for (var component = 0; component < count; component++) Multiply(first + component, first + component, 75);
        }
        void Transform(int row)
        {
            for (var component = 0; component < 2; component++)
            {
                int[] shadowWords = [10, 12, 25, 26, 13, 24];
                for (var coefficient = 0; coefficient < 3; coefficient++)
                    Uniform(68 + coefficient, row == 8 ? 5 : 6,
                        row == 8 ? shadowWords[component * 3 + coefficient] : (row + component) * 4 + coefficient);
                Fma(58 + component, 56, 68, 70);
                Fma(58 + component, 57, 69, 58 + component);
            }
        }
        void Normal(int first, int second)
        {
            Constant(76, 2); Constant(77, -1); Constant(78, .5f); Constant(79, 1);
            Fma(60, first, 76, 77); Fma(61, first + 1, 76, 77); Fma(62, second, 76, 77);
            int[] inputs = [0x9c, 0xac, 0xbc, 0xf8, 0x10c, 0x118, 0x11c];
            for (var component = 0; component < inputs.Length; component++)
                Emit(0xe043ff800007ff00UL & ~(255UL << 30) | (ulong)(inputs[component] / 4) << 30 | 74UL << 20 | (uint)(64 + component));
            Normalize(64, 3);
            Multiply(75, 64, 67); Fma(75, 65, 68, 75); Fma(75, 66, 69, 75);
            for (var component = 0; component < 3; component++) Fma(67 + component, 64 + component, 75, 67 + component, true);
            Normalize(67, 3);
            Multiply(61, 61, 70);
            for (var component = 0; component < 3; component++)
            {
                var next = (component + 1) % 3; var last = (component + 2) % 3;
                Multiply(71 + component, 64 + next, 67 + last);
                Fma(71 + component, 64 + last, 67 + next, 71 + component, true);
                Multiply(71 + component, 71 + component, 61);
                Fma(71 + component, 67 + component, 60, 71 + component);
                Fma(71 + component, 64 + component, 62, 71 + component);
            }
            Normalize(71, 3);
            Fma(first, 71, 78, 78); Fma(first + 1, 72, 78, 78); Fma(second, 73, 78, 78);
        }
        void Color(int first, int second)
        {
            int[] colors = [first, first + 1, second];
            if (layerTint)
            {
                Sample(22, 92, 93, 88);
                for (var c = 0; c < 3; c++) Constant(80 + c, 1);
                int[] colorWords = [32, 36, 60, 72];
                for (var layer = 0; layer < 4; layer++)
                    for (var c = 0; c < 3; c++)
                    {
                        Uniform(68, 6, colorWords[layer] + c);
                        Add(68, 68, 80 + c, true); Fma(80 + c, 68, 88 + layer, 80 + c);
                    }
                for (var c = 0; c < 3; c++) Multiply(colors[c], colors[c], 80 + c);
            }
            if (layerEmission || layerTint)
            {
                if (!layerTint)
                {
                    Move(58, 92); Move(59, 93);
                    Sample(22, 58, 59, 88);
                }
                Constant(84, 1);
                for (var c = 0; c < 3; c++) Constant(80 + c, 0);
                // Source layers overwrite in order: a later mask attenuates
                // every earlier layer, including its independent emission.
                int[] intensityWords = [43, 44, 48, 51];
                int[] colorWords = [32, 36, 60, 72];
                for (var layer = 3; layer >= 0; layer--)
                {
                    Uniform(87, 5, intensityWords[layer]);
                    Multiply(86, 88 + layer, 84); Multiply(86, 86, 87);
                    for (var c = 0; c < 3; c++)
                    {
                        if (layerTint) Constant(68, ZaSwShEyelidMaterial.TintEmissionColors[layer][c]);
                        else Uniform(68, 6, colorWords[layer] + c);
                        Fma(80 + c, 68, 86, 80 + c);
                    }
                    Constant(85, 1); Add(85, 85, 88 + layer, true); Multiply(84, 84, 85);
                }
            }
            if (independentHighlights)
            {
                Uniform(68, 5, 49); Uniform(69, 5, 46); Fma(58, 56, 68, 69);
                Uniform(68, 5, 50); Uniform(69, 5, 47); Fma(59, 57, 68, 69);
                Constant(68, 1); Add(59, 68, 59, true); Sample(22, 58, 59, 60);
                for (var component = 0; component < 3; component++)
                {
                    Uniform(68, 6, 72 + component); Fma(colors[component], 68, 60, colors[component]);
                }
            }
            if (lava || layerEmission || layerTint) Constant(94, 1);
            Transform(8); Sample(0x0e, 58, 59, 60);
            for (var component = 0; component < 3; component++) Multiply(colors[component], colors[component], 60 + component);
            if (layerEmission || layerTint)
                for (var component = 0; component < 3; component++) Multiply(80 + component, 80 + component, 60 + component);
            foreach (var (row, slot) in new[] { (4, 0x0a), (6, 0x0c) })
            {
                Transform(row); Sample(slot, 58, 59, 60);
                if (lava || layerEmission || layerTint) { Constant(84, 1); Add(84, 84, 63, true); Multiply(94, 94, 84); }
                for (var component = 0; component < 3; component++)
                {
                    Add(68, 60 + component, colors[component], true);
                    Fma(colors[component], 68, 63, colors[component]);
                }
            }
            if (layerEmission || layerTint)
                for (var c = 0; c < 3; c++) Multiply(80 + c, 80 + c, 94);
            if (lava)
            {
                // The native eye surface retains the eyelid occlusion while its
                // lava glow follows a separate moving detail field.
                Sample(20, 92, 93, 80); Constant(84, 4);
                for (var c = 0; c < 3; c++) Multiply(80 + c, 80 + c, 84);
                Uniform(84, 5, 49); Uniform(85, 5, 46); Fma(86, 56, 84, 85);
                Uniform(84, 5, 50); Uniform(85, 5, 47); Fma(87, 57, 84, 85);
                Constant(84, 1); Add(87, 84, 87, true); Sample(22, 86, 87, 88);
                Move(95, 88); Uniform(84, 5, 42); Multiply(88, 88, 84);
                for (var c = 0; c < 3; c++)
                {
                    Uniform(84, 6, 72 + c); Fma(80 + c, 84, 88, 80 + c); Multiply(80 + c, 80 + c, 94);
                    Uniform(84, 6, 48 + c); Constant(85, 1); Add(84, 84, 85, true); Fma(84, 84, 95, 85);
                    Multiply(colors[c], colors[c], 84);
                }
            }
            Constant(second + 1, 1);
        }
        for (var block = 0x80; block < instructionsEnd; block += 32)
        {
            var scheduling = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var instruction = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                var schedulingInstruction = (scheduling >> (lane * 21)) & 0x1ffff;
                var texture = (instruction & 0xff00000000000000UL) == 0xd800000000000000UL;
                var slot = texture ? (int)(instruction >> 36 & 0x1fff) : -1;
                var first = (int)(instruction & 255); var second = (int)(instruction >> 28 & 255);
                if ((layerEmission || layerTint) && (instruction >> 34 & 31) == 6
                    && (instruction >> 20 & 16383) is 32 or 33 or 34 or 36 or 37 or 38 or 60 or 61 or 62)
                {
                    // These exact FFMA.C instructions compute x*1-x in the
                    // native template. Free their neutral color slots while
                    // substituting the independently evaluated glow at output.
                    if (!colorPatched || !emissionReads.Remove(instruction))
                        throw new InvalidDataException("Eye emission color reads need another adapter.");
                    var word = (int)(instruction >> 20 & 16383);
                    if (word >= 60) Move(first, 80 + word - 60); else Constant(first, 0);
                    continue;
                }
                if ((instruction >> 34 & 31) == 5 && (instruction >> 20 & 16383) is 13 or 24)
                {
                    var word = (int)(instruction >> 20 & 16383);
                    if (word == 13 && instruction >> 48 == 0x49a1)
                    {
                        Constant(79, 1);
                        Fma(first, (int)(instruction >> 8 & 255), 79, (int)(instruction >> 39 & 255), true);
                        preservedLayerControls++;
                        continue;
                    }
                    if (word == 24 && instruction >> 48 == 0x4c68)
                    {
                        Move(first, (int)(instruction >> 8 & 255));
                        preservedLayerControls++;
                        continue;
                    }
                }
                if (slot >= 0 && !textures.Add(slot)) throw new InvalidDataException("Eye shader sampler identity is ambiguous.");
                if (slot == 0x10)
                {
                    if (!capturedUv || normalPatched) throw new InvalidDataException("Eye shader normal input is unsupported.");
                    Uniform(68, 5, 7); Uniform(69, 5, 8); Uniform(70, 5, 9); Uniform(71, 5, 11);
                    Fma(1, 56, 68, 70); Fma(0, 57, 69, 71);
                    if ((instruction >> 8 & 255) != 1 || (instruction >> 20 & 255) != 0)
                        throw new InvalidDataException("Eye shader normal coordinates are unsupported.");
                }
                if (slot is 0x0e or 0x0a or 0x0c || lava && slot is 20 or 22 || (independentHighlights || layerEmission || layerTint) && slot == 22)
                {
                    if (slot == 0x0e) { Constant(first, 0); Constant(first + 1, 0); Constant(second, 166 / 255f); }
                    else
                    {
                        Constant(first, 0); Constant(first + 1, 0);
                        if (second != 255) { Constant(second, 0); Constant(second + 1, 0); }
                    }
                    continue;
                }
                if (lava && (instruction >> 34 & 31) == 6 && (instruction >> 20 & 16383) is >= 60 and <= 62
                    && instruction >> 48 is >= 0x49a0 and <= 0x49a3)
                    Add((int)(instruction >> 8 & 255), (int)(instruction >> 8 & 255), 80 + (int)(instruction >> 20 & 16383) - 60);
                if ((lava || mirrorEmissionColor) && slot == 8)
                {
                    // Preserve unmodified coordinates for the independently addressed emission map.
                    Move(92, (int)(instruction >> 8 & 255)); Move(93, (int)(instruction >> 20 & 255));
                }
                if ((layerEmission || layerTint) && slot == 8)
                {
                    Move(92, (int)(instruction >> 8 & 255)); Move(93, (int)(instruction >> 20 & 255));
                }
                KM.Formats.SwSh.SwShShaderScheduling.Original(code, ref originalBoundary, instruction, schedulingInstruction);
                if ((instruction & 0xffffffffff00ff00UL) == 0xe043ff910100ff00UL && first == 1)
                {
                    if (capturedUv) throw new InvalidDataException("Eye shader UV input is ambiguous.");
                    Move(56, 1); Move(57, 0); Move(74, (int)(instruction >> 20 & 255)); capturedUv = true;
                }
                if (slot == 0x10) { if (!objectNormals) Normal(first, second); normalPatched = true; }
                if (slot == 8) { Color(first, second); colorPatched = true; }
            }
        }
        if (!normalPatched || !colorPatched || preservedLayerControls != 3 || (layerEmission || layerTint) && emissionReads.Count != 0
            || !textures.SetEquals(new[] { 8, 10, 12, 14, 16, 18, 20, 22 }))
            throw new InvalidDataException("Eye shader contract is incomplete.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = checked(0x80 + code.Count / 3 * 32);
        var newConstants = (newEnd + 255) & ~255;
        var newLength = (newConstants + constantLength + 255) & ~255;
        if (newLength > capacity) throw new InvalidDataException("Eye shader extension exceeds its native allocation.");
        var output = template.ToArray();
        foreach (var (location, component) in new[] { (1, 3), (2, 3), (3, 3), (7, 2), (8, 3), (9, 2), (9, 3) })
            output[data + 0x30 + 24 + location] |= (byte)(2 << (component * 2));
        output.AsSpan(data + 0x80, capacity - 0x80).Clear();
        template.AsSpan(data + constants, constantLength).CopyTo(output.AsSpan(data + newConstants));
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, 4), value);
        Write(stage + 24, newLength); Write(control + 0x6f8, newEnd - 0x30);
        Write(control + 0x700, newConstants); Write(control + 0x704, newLength); Write(control + 0x70c, lava || layerEmission || layerTint ? 96 : 80);
        for (var index = 0; index < code.Count; index += 3)
        {
            var offset = data + 0x80 + index / 3 * 32;
            var scheduling = code[index].Control | code[index + 1].Control << 21 | code[index + 2].Control << 42;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(offset, 8), scheduling);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(offset + 8 + lane * 8, 8), code[index + lane].Instruction);
        }
        return output;
    }

    public static byte[] Vertex(byte[] template)
    {
        int Integer(int offset) => BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(offset, 4));
        int Pointer(int offset) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(offset, 8)));
        if (template.Length < 512 || !template.AsSpan(0, 4).SequenceEqual("BNSH"u8) || Integer(0x7c) != 1)
            throw new InvalidDataException("Eye vertex container is unsupported.");
        var stage = Pointer(Pointer(Pointer(0x80) + 16) + 8);
        var control = Pointer(stage + 8); var data = Pointer(stage + 16); var length = Integer(stage + 24);
        if (System.Convert.ToHexString(SHA256.HashData(template.AsSpan(data, length))) != "5692C7A693293F7C076613DC7F5C4E57095EB29E9A4E7269552285681EFA1004"
            || Integer(control) != unchecked((int)0x98761234) || Integer(control + 0x708) != 0x30 || Integer(control + 0x704) != length)
            throw new InvalidDataException("Eye vertex program needs another adapter.");
        var end = Integer(control + 0x6f8) + 0x30; var constants = Integer(control + 0x700); var constantLength = Integer(control + 0x6fc);
        var relocation = Integer(24);
        if (!template.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || Integer(relocation + 8) != 6)
            throw new InvalidDataException("Eye vertex relocation is unsupported.");
        var section = Enumerable.Range(0, 6).Select(index => relocation + 16 + index * 24).Single(entry => Integer(entry + 8) == data);
        var capacity = Integer(section + 12);
        if (end % 32 != 0 || end > constants || constants + constantLength > length || length > capacity
            || template.AsSpan(data + length, capacity - length).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Eye vertex extension storage is unavailable.");
        var code = new List<(ulong Instruction, ulong Control)>(); int originalBoundary = 0; var patched = false;
        for (var block = 0x80; block < end; block += 32)
        {
            var scheduling = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block, 8));
            for (var lane = 0; lane < 3; lane++)
            {
                var instruction = BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data + block + 8 + lane * 8, 8));
                if (instruction == 0xe30000000007000fUL)
                {
                    if (patched) throw new InvalidDataException("Eye vertex exit is ambiguous.");
                    foreach (var (input, output) in new[] { (0x90, 0x9c), (0x94, 0xac), (0x98, 0xbc), (0xa0, 0xf8), (0xa4, 0x10c), (0xa8, 0x118), (0xac, 0x11c) })
                    {
                        var load = 0xefd87f800007ff00UL | (ulong)input << 20;
                        code.Add((load, KM.Formats.SwSh.SwShShaderScheduling.Serial(load)));
                        var store = 0xeff07f800007ff00UL | (ulong)output << 20;
                        code.Add((store, KM.Formats.SwSh.SwShShaderScheduling.Serial(store)));
                    }
                    patched = true;
                }
                KM.Formats.SwSh.SwShShaderScheduling.Original(code, ref originalBoundary, instruction, scheduling >> (lane * 21) & 0x1ffff);
            }
        }
        if (!patched) throw new InvalidDataException("Eye vertex exit is missing.");
        while (code.Count % 3 != 0) code.Add((0x50b0000000070f00UL, 0x7ff));
        var newEnd = 0x80 + code.Count / 3 * 32;
        var newConstants = (newEnd + 255) & ~255; var newLength = (newConstants + constantLength + 255) & ~255;
        if (newLength > capacity) throw new InvalidDataException("Eye vertex extension exceeds its native allocation.");
        var result = template.ToArray(); result.AsSpan(data + 0x80, capacity - 0x80).Clear();
        template.AsSpan(data + constants, constantLength).CopyTo(result.AsSpan(data + newConstants));
        result[data + 0x30 + 24 + 1] |= 15;
        foreach (var (location, component) in new[] { (1, 3), (2, 3), (3, 3), (7, 2), (8, 3), (9, 2), (9, 3) })
            result[data + 0x30 + 52 + location / 2] |= (byte)(1 << (location % 2 * 4 + component));
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset, 4), value);
        Write(stage + 24, newLength); Write(control + 0x6f8, newEnd - 0x30); Write(control + 0x700, newConstants); Write(control + 0x704, newLength);
        for (var index = 0; index < code.Count; index += 3)
        {
            var offset = data + 0x80 + index / 3 * 32;
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(offset, 8), code[index].Control | code[index + 1].Control << 21 | code[index + 2].Control << 42);
            for (var lane = 0; lane < 3; lane++) BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(offset + 8 + lane * 8, 8), code[index + lane].Instruction);
        }
        return result;
    }
}
