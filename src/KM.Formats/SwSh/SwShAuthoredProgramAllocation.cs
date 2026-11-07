// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats.SwSh;

/// <summary>Relocates a dedicated, already validated native shader allocation.</summary>
internal static class SwShAuthoredProgramAllocation
{
    internal static byte[] Replace(byte[] source, int stage, int control, int data, byte[] payload,
        int end, int constants, int registers)
    {
        int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(at, 4));
        int P(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(at, 8)));
        var relocation = I(24); var sections = I(relocation + 8);
        var constantLength = I(control + 0x6fc);
        if (!source.AsSpan(relocation, 4).SequenceEqual("_RLT"u8) || I(relocation + 4) != relocation
            || sections is not (6 or 7) || I(relocation + 12) != 0 || registers is < 56 or > 216
            || payload.Length % 256 != 0 || end <= 0x80 || end % 32 != 0
            || constants != ((end + 255) & ~255)
            || payload.Length != ((constants + constantLength + 255) & ~255)
            || !payload.AsSpan(0, 0x30).SequenceEqual(source.AsSpan(data, 0x30))
            || !payload.AsSpan(constants, constantLength).SequenceEqual(source.AsSpan(data + I(control + 0x700), constantLength)))
            throw new InvalidDataException("Authored shader payload does not preserve the native header and constants.");
        var entries = relocation + 16 + sections * 24;
        var pointers = new List<(int Position, int Target, int Section)>();
        var codeSection = -1; var entryCount = 0; var capacity = 0;
        for (var section = 0; section < sections; section++)
        {
            var at = relocation + 16 + section * 24; var start = I(at + 8); var length = I(at + 12);
            if (P(at) != 0 || start < 0 || length < 0 || start > relocation || length > relocation - start
                || I(at + 16) != entryCount || I(at + 20) is < 0 or > 256)
                throw new InvalidDataException("Authored shader relocation section is unsupported.");
            if (start == data)
            {
                if (codeSection >= 0) throw new InvalidDataException("Authored shader allocation is ambiguous.");
                codeSection = section; capacity = length;
            }
            var count = I(at + 20);
            for (var n = 0; n < count; n++)
            {
                var entry = entries + (entryCount + n) * 8; var position = I(entry);
                var structures = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(entry + 4, 2));
                var offsets = source[entry + 6]; var padding = source[entry + 7];
                if (structures == 0 || offsets == 0 || position < 0)
                    throw new InvalidDataException("Authored shader relocation packing is unsupported.");
                for (var structure = 0; structure < structures; structure++)
                    for (var offset = 0; offset < offsets; offset++)
                    {
                        var pointer = checked(position + (structure * (offsets + padding) + offset) * 8);
                        if (pointer > relocation - 8) throw new InvalidDataException("Authored shader relocation exceeds the container.");
                        var target = P(pointer);
                        if (target != 0 && (target < start || target - start >= length))
                            throw new InvalidDataException("Authored shader relocation leaves its section.");
                        pointers.Add((pointer, target, section));
                    }
            }
            entryCount = checked(entryCount + count);
        }
        if (codeSection < 0 || I(stage + 24) > capacity
            || pointers.Select(p => p.Position).Distinct().Count() != pointers.Count
            || pointers.Where(p => p.Section == codeSection).Any(p => p.Target != data)
            || !pointers.Any(p => p.Section == codeSection && p.Position == stage + 16)
            || pointers.Any(p => p.Position >= data && p.Position - data < capacity))
            throw new InvalidDataException("Authored shader code references are unsupported.");
        // The program bytes are also the dedicated NVN memory pool storage.
        // Both its address and its capacity must retain the native page alignment.
        var pool = P(0x88);
        if (I(pool) != 0x61 || P(pool + 8) != data || I(pool + 4) != capacity
            || (data & 4095) != 0 || (capacity & 4095) != 0)
            throw new InvalidDataException("Authored shader requires a dedicated page aligned native memory pool.");
        var newData = checked((source.Length + 4095) & ~4095);
        var newCapacity = checked((payload.Length + 4095) & ~4095);
        var newRelocation = checked(newData + newCapacity);
        var relocationLength = checked(16 + sections * 24 + entryCount * 8);
        var output = new byte[checked((newRelocation + relocationLength + 255) & ~255)];
        source.CopyTo(output, 0); output.AsSpan(data, capacity).Clear(); output.AsSpan(relocation).Clear();
        payload.CopyTo(output, newData);
        source.AsSpan(relocation, relocationLength).CopyTo(output.AsSpan(newRelocation));
        void Write(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at, 4), value);
        Write(24, newRelocation); Write(28, output.Length); Write(newRelocation + 4, newRelocation);
        Write(newRelocation + 16 + codeSection * 24 + 8, newData);
        Write(newRelocation + 16 + codeSection * 24 + 12, newCapacity);
        foreach (var pointer in pointers.Where(p => p.Section == codeSection))
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(pointer.Position, 8), (uint)newData);
        Write(pool + 4, newCapacity);
        Write(stage + 24, payload.Length); Write(control + 0x6f8, end - 0x30);
        Write(control + 0x700, constants); Write(control + 0x704, payload.Length); Write(control + 0x70c, registers);
        return output;
    }
}
