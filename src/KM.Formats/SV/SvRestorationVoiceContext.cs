// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Audio;
using KM.Formats.SwSh;

namespace KM.Formats.SV;

/// <summary>Verifies the donor's normal Paradox cry context in its Init bank.</summary>
public sealed class SvRestorationVoiceContext
{
    internal const uint NormalContext = 2507540714;
    internal const uint NormalState = 1160234136;
    private SvRestorationVoiceContext() { }

    public static SvRestorationVoiceContext ReadNormal(byte[] initBank)
    {
        if (initBank.Length > 16 * 1024 * 1024 || new SoundBankDocument(initBank).Version != 140)
            throw new InvalidDataException("S/V voice context bank is unsupported.");
        byte[]? settings = null;
        // SoundBankDocument already validates unique, bounded chunks.
        for (var offset = 0; offset < initBank.Length;)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(initBank.AsSpan(offset + 4)));
            if (initBank.AsSpan(offset, 4).SequenceEqual("STMG"u8)) settings = initBank.AsSpan(offset + 8, size).ToArray();
            offset = checked(offset + 8 + size);
        }
        if (settings is null) throw new InvalidDataException("S/V voice context settings are missing.");
        var at = 0;
        void Need(int size)
        {
            if (size < 0 || at > settings.Length - size) throw new InvalidDataException("S/V voice context is truncated.");
        }
        byte Byte() { Need(1); return settings[at++]; }
        ushort Short() { Need(2); var value = BinaryPrimitives.ReadUInt16LittleEndian(settings.AsSpan(at)); at += 2; return value; }
        uint Word() { Need(4); var value = BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(at)); at += 4; return value; }
        float Float() => BitConverter.UInt32BitsToSingle(Word());
        int Count() => Word() is var value && value <= 4096 ? (int)value
            : throw new InvalidDataException("S/V voice context count exceeds its budget.");
        void Skip(int size) { Need(size); at += size; }
        _ = Float(); _ = Short(); _ = Short();
        var stateGroups = Count();
        for (var i = 0; i < stateGroups; i++) { _ = Word(); _ = Word(); Skip(checked(Count() * 12)); }
        var switchGroups = Count(); var foundSwitch = false;
        var normalStates = new uint[] { NormalState, 927871640 };
        for (var i = 0; i < switchGroups; i++)
        {
            var group = Word(); var parameter = Word(); var type = Byte(); var points = Count();
            if (group != NormalContext) { Skip(checked(points * 12)); continue; }
            if (foundSwitch || parameter != NormalContext || type != 0 || points != 2)
                throw new InvalidDataException("S/V normal cry context selector is unsupported.");
            foundSwitch = true;
            for (var point = 0; point < 2; point++)
                if (Float() != point || Word() != normalStates[point] || Word() != 9)
                    throw new InvalidDataException("S/V normal cry context mapping changed.");
        }
        var parameters = Count(); var ids = new HashSet<uint>(); var foundDefault = false;
        for (var i = 0; i < parameters; i++)
        {
            var id = Word(); var value = Float(); var ramp = Word(); var up = Float(); var down = Float(); var binding = Byte();
            if (!ids.Add(id)) throw new InvalidDataException("S/V voice context parameter is duplicated.");
            if (id != NormalContext) continue;
            if (value != 0 || ramp != 0 || up != 0 || down != 0 || binding != 0)
                throw new InvalidDataException("S/V normal cry context is not an unbound zero default.");
            foundDefault = true;
        }
        Skip(checked(Count() * 28)); // Version 140 acoustic textures.
        if (at != settings.Length || !foundSwitch || !foundDefault)
            throw new InvalidDataException("S/V normal cry context is incomplete or has unconverted data.");
        return new();
    }
}
