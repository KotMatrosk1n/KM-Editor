// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

namespace KM.Formats.SV;

public static class SvTrinityPathHasher
{
    public const ulong Prime = 0x00000100000001B3;
    public const ulong Basis = 0xCBF29CE484222645;

    public static ulong HashPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalizedPath = path.Replace('\\', '/');
        var hash = Basis;
        foreach (var value in Encoding.UTF8.GetBytes(normalizedPath))
        {
            hash ^= value;
            hash *= Prime;
        }

        return hash;
    }
}
