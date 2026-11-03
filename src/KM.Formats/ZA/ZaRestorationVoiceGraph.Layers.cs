// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats.ZA;

public static partial class ZaRestorationVoiceGraph
{
    private static ZaRestorationVoice Layer(byte[] bytes, uint parent, float inheritedGain)
    {
        void Require(bool condition)
        {
            if (!condition) throw new InvalidDataException("Z-A layered cry sound requires another adapter.");
        }
        bool Zero(int at, int count) => bytes.AsSpan(at, count).IndexOfAnyExcept((byte)0) < 0;
        ushort Short(int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
        float Float(int at) => BitConverter.UInt32BitsToSingle(U32(bytes, at));
        Require(bytes.Length is 46 or 51 or 101 or 122 && U32(bytes, 0) == 0x00020001 && bytes[13] == 0
            && U32(bytes, 23) == parent && U32(bytes, 9) > 0);
        if (bytes.Length == 46)
        {
            Require(bytes[4] == 2 && Zero(14, 9) && Zero(27, 10) && bytes[37] == 1 && Zero(38, 8));
            return new(U32(bytes, 5), inheritedGain);
        }
        if (bytes.Length == 51)
        {
            Require(bytes[4] == 2 && Zero(14, 9) && bytes[27] == 0 && bytes[28] == 1 && bytes[29] == 6
                && Zero(34, 8) && bytes[42] == 1 && Zero(43, 8));
            var streamedMakeup = Float(30);
            Require(float.IsFinite(streamedMakeup) && streamedMakeup is >= -96 and <= 24);
            return new(U32(bytes, 5), inheritedGain + streamedMakeup);
        }
        // Version 145's embedded effect overrides an empty FX chain, inherits
        // routing/positioning, and has no delay, randomization or state modifiers.
        Require(bytes[4] == 0 && bytes[14] == 1 && bytes[15] == 0 && Zero(16, 7) && bytes[27] == 0);
        var curve = 49;
        var makeupAt = 30;
        if (bytes.Length == 122)
        {
            // Primal Kyogre stores a local makeup gain and an auxiliary-send
            // property. User sends and virtual-voice settings inherit from the
            // parent (override bits are clear); the four stored donor buses are
            // inactive. Preserve the target parent's environment routing.
            Require(bytes[28] == 2 && bytes[29] == 6 && bytes[30] == 19
                && float.IsFinite(Float(35)) && Float(35) is >= -96 and <= 24
                && Zero(39, 2) && bytes[41] == 10 && Zero(58, 5)
                && bytes[63] == 1 && Short(64) == 0 && bytes[66] == 1 && Zero(67, 3));
            curve = 70;
            makeupAt = 31;
        }
        else Require(bytes[28] == 1 && bytes[29] == 6 && Zero(34, 8) && bytes[42] == 1 && Zero(43, 6));
        Require(Short(curve) == 1 && U32(bytes, curve + 2) == ZaRestorationVoiceContext.NormalAlphaFrenzy
            && bytes[curve + 6] == 0 && bytes[curve + 7] == 2 && bytes[curve + 8] == 0 && U32(bytes, curve + 9) != 0
            && bytes[curve + 13] == 2 && Short(curve + 14) == 3);
        var makeup = Float(makeupAt);
        Require(float.IsFinite(makeup) && makeup is >= -96 and <= 24);
        Require(Float(curve + 16) == 0 && Float(curve + 20) == -1 && U32(bytes, curve + 24) == 9
            && Float(curve + 28) == 1 && Float(curve + 32) == -1 && U32(bytes, curve + 36) == 4
            && Float(curve + 40) == 2 && Float(curve + 44) == 0 && U32(bytes, curve + 48) == 4);
        // The verified Init context selects NORMAL=0. Its constant first curve
        // segment is -1 in Wwise dB scaling (mode 2): the -96.3 dB endpoint,
        // not -1 dB. Keep volume separate from the authored makeup gain.
        return new(U32(bytes, 5), inheritedGain + makeup, true, -96.3f);
    }
}
