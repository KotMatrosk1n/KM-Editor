// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.SV;

public static class SvTrinityPathHasher
{
    public const ulong Prime = 0x00000100000001B3;
    public const ulong Basis = 0xCBF29CE484222645;

    public static ulong HashPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Utf8PathHash.Compute(path, normalizeSeparators: true);
    }
}
