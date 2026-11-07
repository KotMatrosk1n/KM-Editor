// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Audio;

namespace KM.Formats.ZA;

public static partial class ZaRestorationVoiceGraph
{
    private static byte[] NormalZygardeSelector(byte[] bytes, SoundBankDocument bank, ZaRestorationVoiceContext? context)
    {
        bool Hash(byte[] data, string expected) => Convert.ToHexString(SHA256.HashData(data)).Equals(expected, StringComparison.OrdinalIgnoreCase);
        if (!Hash(bytes, "78187724c8a36497ee5efe046da410de83dd76e411acac9446397da6ee97a3c8")) return bytes;
        if (context?.ZygardeNormalDefaults != true)
            throw new InvalidDataException("Zygarde's normal cry context defaults are not qualified.");
        foreach (var (id, hash) in new (uint, string)[] {
            (2314907427, "7411c674982b0115f32092ee569acf0b5c9a424fa3649fd0d082f766317d9f26"),
            (2761220127, "33f3fd0f2091ebea3b62b1df74470da33c3fa5e55ac90d97703b02504e0c60f0"),
            (3975494976, "8199fa6b7e5890a2c0f16e03cd9b53d490dd6ae5ebf41328ef29f62f766a1fdb") })
        {
            var effects = bank.Objects.Where(o => o.Type == 16 && o.Id == id).ToArray();
            if (effects.Length != 1 || !Hash(effects[0].Payload, hash))
                throw new InvalidDataException("Zygarde's contextual voice effects changed.");
        }
        // This exact FORM parent repeats donor gameplay context controls, not
        // form-specific cry processing. NORMAL=0 bypasses FX0/FX1, full health
        // makes pitch/LPF neutral, and FX3 is a zero-gain environment mixer.
        // Preserve the native target mixer instead of importing donor map states.
        // Keep the complete FORM child/state/transition graph byte for byte.
        var result = new byte[234];
        bytes.AsSpan(24, 28).CopyTo(result.AsSpan(2));
        bytes.AsSpan(518).CopyTo(result.AsSpan(32));
        return result;
    }
}
