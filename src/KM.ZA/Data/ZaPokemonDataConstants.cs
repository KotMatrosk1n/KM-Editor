// SPDX-License-Identifier: GPL-3.0-only

namespace KM.ZA.Data;

internal static class ZaPokemonDataConstants
{
    public const int MoveNone = -1;
    public const int MoveAuto = 0;

    public const int RareNotShiny = 0x1FFFFFFF;
    public const int RareForcedShiny = 0x2FFFFFFF;
    public const int RareDefaultShinyRoll = 0x3FFFFFFF;

    public const string MoveNoneLabel = "None";
    public const string MoveAutoLabel = "Game default / auto move";

    public const string RareNotShinyLabel = "Not shiny";
    public const string RareForcedShinyLabel = "Forced shiny";
    public const string RareDefaultShinyRollLabel = "Default shiny roll";
}
