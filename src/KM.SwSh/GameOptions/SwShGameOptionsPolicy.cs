// SPDX-License-Identifier: GPL-3.0-only
namespace KM.SwSh.GameOptions;

/// <summary>Choice state: 0 = Default, 1 = On, 2 = Off; bit 2 hides the choice.</summary>
public static class SwShGameOptionsPolicy
{
    public static readonly int[] ChoiceCounts = [3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 11, 11, 11];
    public static readonly int[] RetailValues = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 8, 8, 8];
    internal static readonly int[] Shifts = [0, 4, 2, 3, 21, 26, 22, 28, 29, 23, 24, 27, 25, 9, 13, 17];
    internal static int Width(int row) => row is 1 or >= 13 ? 4 : row == 0 ? 2 : 1;
    internal static bool Inverted(int row) => row is 7 or 8;
    public static int[] Defaults => new int[60];

    public static int[] Normalize(IReadOnlyList<int>? selections)
    {
        if (selections is not { Count: 60 } || selections.Any(value => value is < 0 or > 6 || (value & 3) == 3))
            throw new InvalidDataException("Each game option selection requires Default, On or Off and a Hide flag.");
        var result = selections.ToArray();
        var offset = 0;
        for (var row = 0; row < ChoiceCounts.Length; row++)
        {
            var choices = result.AsSpan(offset, ChoiceCounts[row]);
            var on = -1;
            for (var choice = 0; choice < choices.Length; choice++)
            {
                if ((choices[choice] & 3) != 1) continue;
                if (on != -1) throw new InvalidDataException("Only one selection can be On for each option.");
                if ((choices[choice] & 4) != 0) throw new InvalidDataException("The starting selection cannot be hidden.");
                on = choice;
            }
            // Explicit On wins over every Default, regardless of payload order.
            if (on != -1)
                for (var choice = 0; choice < choices.Length; choice++)
                    if (choice != on) choices[choice] = (choices[choice] & 4) | 2;
            if (!choices.Contains(0) && !choices.Contains(1))
                throw new InvalidDataException("Keep a visible Default or On selection for each option.");
            offset += choices.Length;
        }
        return result;
    }

    internal static uint Metadata(int row, ReadOnlySpan<int> choices)
    {
        var hidden = 0;
        var on = -1;
        var firstDefault = -1;
        for (var i = 0; i < choices.Length; i++)
        {
            if ((choices[i] & 4) != 0) hidden |= 1 << i;
            if (choices[i] == 1) on = i;
            if (choices[i] == 0 && firstDefault < 0) firstDefault = i;
        }
        var initial = on >= 0 ? on : choices[RetailValues[row]] == 0 ? RetailValues[row] : firstDefault;
        // Leave the native language choice intact when both Japanese choices are available.
        // Other language codes share this packed nibble and are never rewritten by the helper.
        if (on < 0 && hidden == 0 && (row != 1 ? choices[RetailValues[row]] == 0 : choices[0] == 0 && choices[1] == 0)) initial = 15;
        return (uint)(hidden | initial << 11 | Shifts[row] << 15 | Width(row) << 20
            | (Inverted(row) ? 1 << 24 : 0)) | 0xAA000000;
    }
}
