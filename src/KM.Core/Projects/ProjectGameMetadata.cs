// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Projects;

public sealed record ProjectGameInfo(
    ProjectGame Game,
    string DisplayName,
    ulong TitleId,
    bool UsesTrinityRomFs);

public static class ProjectGameMetadata
{
    private static readonly ProjectGameInfo[] GameInfos =
    [
        new(ProjectGame.Sword, "Pokemon Sword", 0x0100ABF008968000, UsesTrinityRomFs: false),
        new(ProjectGame.Shield, "Pokemon Shield", 0x01008DB008C2C000, UsesTrinityRomFs: false),
        new(ProjectGame.Scarlet, "Pokemon Scarlet", 0x0100A3D008C5C000, UsesTrinityRomFs: true),
        new(ProjectGame.Violet, "Pokemon Violet", 0x01008F6008C5E000, UsesTrinityRomFs: true),
        new(ProjectGame.ZA, "Pokemon Legends Z-A", 0x0100F43008C44000, UsesTrinityRomFs: true),
    ];

    public static IReadOnlyList<ProjectGameInfo> All => GameInfos;

    public static ProjectGameInfo Get(ProjectGame game)
    {
        return GameInfos.FirstOrDefault(info => info.Game == game)
            ?? throw new ArgumentOutOfRangeException(nameof(game), game, null);
    }

    public static ProjectGame? DetectByTitleId(ulong titleId)
    {
        return GameInfos.FirstOrDefault(info => info.TitleId == titleId)?.Game;
    }

    public static bool IsSwordShield(ProjectGame? game)
    {
        return game is null or ProjectGame.Sword or ProjectGame.Shield;
    }

    public static bool IsScarletViolet(ProjectGame? game)
    {
        return game is ProjectGame.Scarlet or ProjectGame.Violet;
    }

    public static bool IsPokemonLegendsZA(ProjectGame? game)
    {
        return game is ProjectGame.ZA;
    }

    public static string FormatRecognizedGameList()
    {
        return string.Join(", ", GameInfos.Select(info => info.DisplayName));
    }
}
