// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Requested replacements for conditions unavailable in Sword and Shield.</summary>
public static class SwShRestorationEvolutionPolicy
{
    public const int DuskStone = 108;

    public static SwShEvolutionRecord Convert(SwShPokemonIdentity parent, SwShEvolutionRecord source) =>
        (parent.Species, parent.Form, source.Species, source.Form) switch
        {
            (211, 1, 904, 0) => source with { Method = 4, Argument = 0, Level = 36 },
            (217, 0, 901, 0) => source with { Method = 4, Argument = 0, Level = 40 },
            (550, 2, 902, 0) => source with { Method = 23, Argument = 0, Level = 40 },
            (550, 2, 902, 1) => source with { Method = 24, Argument = 0, Level = 40 },
            (704, 0, 705, 1) or (723, 0, 724, 1) => source with { Method = 8, Argument = DuskStone, Level = 0 },
            _ => source,
        };

    public static int? NativeParent(SwShPokemonIdentity identity) => (identity.Species, identity.Form) switch
    {
        (705, 1) => 704,
        (724, 1) => 723,
        _ => null,
    };

    internal static bool IsNativeParentFile(SwShRestorationDefinition definition, SwShRestorationContribution contribution)
        => contribution.Before?.Length == SwShEvolutionSet.FileSize
        && contribution.After.Length == SwShEvolutionSet.FileSize
        && definition.Identities.Any(identity => NativeParent(identity) is { } species
            && contribution.Path == $"romfs/bin/pml/evolution/evo_{species:000}.bin");
}
