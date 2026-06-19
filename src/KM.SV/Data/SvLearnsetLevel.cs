// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SV.Data;

internal static class SvLearnsetLevel
{
    public const int EvolutionRawLevel = 253;
    public const int EvolutionDisplayLevel = 0;
    public const string EvolutionLabel = "Evolution";

    public static int ToDisplayLevel(int rawLevel)
    {
        return rawLevel == EvolutionRawLevel ? EvolutionDisplayLevel : rawLevel;
    }

    public static string? ToLevelLabel(int rawLevel)
    {
        return rawLevel == EvolutionRawLevel ? EvolutionLabel : null;
    }

    public static int PreserveRawLevel(int requestedLevel, int existingRawLevel, int existingDisplayLevel)
    {
        return existingRawLevel == EvolutionRawLevel && requestedLevel == existingDisplayLevel
            ? existingRawLevel
            : requestedLevel;
    }
}
