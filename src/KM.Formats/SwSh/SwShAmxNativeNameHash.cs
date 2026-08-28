// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.SwSh;

public static class SwShAmxNativeNameHash
{
    public static uint Compute(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
        {
            throw new ArgumentException("A Sword/Shield AMX native name must not be empty.", nameof(name));
        }

        uint hash = 0;
        foreach (var character in name)
        {
            if (character > 0x7F)
            {
                throw new ArgumentException(
                    "Sword/Shield AMX native names must use the Pawn ASCII identifier domain.",
                    nameof(name));
            }

            hash = unchecked((hash * 0x83U) ^ (uint)character);
        }

        return hash;
    }
}
