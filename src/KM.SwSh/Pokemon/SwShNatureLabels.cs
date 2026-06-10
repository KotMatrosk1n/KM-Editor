// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.Pokemon;

internal static class SwShNatureLabels
{
    public static readonly IReadOnlyList<SwShNatureLabel> Fixed =
    [
        new(1, "Lonely (+Atk/-Def)"),
        new(3, "Adamant (+Atk/-Sp.Atk)"),
        new(4, "Naughty (+Atk/-Sp.Def)"),
        new(2, "Brave (+Atk/-Spe)"),
        new(5, "Bold (+Def/-Atk)"),
        new(8, "Impish (+Def/-Sp.Atk)"),
        new(9, "Lax (+Def/-Sp.Def)"),
        new(7, "Relaxed (+Def/-Spe)"),
        new(15, "Modest (+Sp.Atk/-Atk)"),
        new(16, "Mild (+Sp.Atk/-Def)"),
        new(19, "Rash (+Sp.Atk/-Sp.Def)"),
        new(17, "Quiet (+Sp.Atk/-Spe)"),
        new(20, "Calm (+Sp.Def/-Atk)"),
        new(21, "Gentle (+Sp.Def/-Def)"),
        new(23, "Careful (+Sp.Def/-Sp.Atk)"),
        new(22, "Sassy (+Sp.Def/-Spe)"),
        new(10, "Timid (+Spe/-Atk)"),
        new(11, "Hasty (+Spe/-Def)"),
        new(13, "Jolly (+Spe/-Sp.Atk)"),
        new(14, "Naive (+Spe/-Sp.Def)"),
        new(0, "Hardy"),
        new(6, "Docile"),
        new(12, "Serious"),
        new(18, "Bashful"),
        new(24, "Quirky"),
    ];

    public static readonly IReadOnlyList<SwShNatureLabel> WithRandom =
    [
        ..Fixed,
        new(25, "Random"),
    ];
}

internal sealed record SwShNatureLabel(int Value, string Label);
