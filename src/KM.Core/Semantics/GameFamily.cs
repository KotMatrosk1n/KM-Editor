// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.Core.Semantics;

/// <summary>
/// Identifies a format family without asserting shared mechanics between its games.
/// </summary>
public enum GameFamily
{
    SwordShield = 1,
    ScarletViolet = 2,
    LegendsZA = 3,
}

public static class GameFamilyExtensions
{
    public static GameFamily ToGameFamily(this ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword or ProjectGame.Shield => GameFamily.SwordShield,
            ProjectGame.Scarlet or ProjectGame.Violet => GameFamily.ScarletViolet,
            ProjectGame.ZA => GameFamily.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    public static bool Contains(this GameFamily family, ProjectGame game)
    {
        SemanticContractGuards.DefinedEnum(family, nameof(family));
        return game.ToGameFamily() == family;
    }
}
