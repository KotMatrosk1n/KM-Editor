// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace KM.Formats.Models;

/// <summary>Translates Trinity sampler values at the format boundary.</summary>
public static class TrinityTextureAddressing
{
    public static bool TryPreview(uint source, out int mode)
    {
        mode = source switch { 1 => 0, 6 => 6, 7 => 1, _ => -1 };
        return mode >= 0;
    }

    public static Vector2 Preview(Vector2 source) => new(Preview(source.X), Preview(source.Y));

    private static int Preview(float source)
    {
        if (source is 1 or 6 or 7 && TryPreview((uint)source, out var mode)) return mode;
        throw new InvalidDataException("Trinity texture addressing requires another adapter.");
    }

    public static int SwSh(float source) => source switch
    {
        1 => 0, 6 => 2, 7 => 1,
        _ => throw new InvalidDataException("Trinity texture addressing requires another adapter.")
    };
}
