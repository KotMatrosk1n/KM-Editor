// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SV.Pokemon;

internal static class SvPokemonExperience
{
    public static int CalculateBaseExperience(int baseStatTotal, int evolutionStage, int expAddend)
    {
        return CalculateFormulaBase(baseStatTotal, evolutionStage) + expAddend;
    }

    public static int CalculateFormulaBase(int baseStatTotal, int evolutionStage)
    {
        return (int)Math.Ceiling(baseStatTotal * (1 + (3 * evolutionStage)) / 20d);
    }

    public static bool TryCalculateExpAddend(int baseStatTotal, int evolutionStage, int desiredBaseExperience, out short expAddend)
    {
        var addend = desiredBaseExperience - CalculateFormulaBase(baseStatTotal, evolutionStage);
        if (addend < short.MinValue || addend > short.MaxValue)
        {
            expAddend = 0;
            return false;
        }

        expAddend = (short)addend;
        return true;
    }
}
