// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.HyperTraining;
using KM.SwSh.ShinyRate;

namespace KM.SwSh.ExeFs;

public sealed record SwShExecutableMergeRegion(string Owner, int Offset, int Length);

/// <summary>Game owned settings and reservations used by executable composition.</summary>
public static class SwShExecutableMergeSupport
{
    public const string HyperScript = "romfs/bin/script/amx/hyper_training.amx";
    public const string HyperDialogue = "romfs/bin/message/English/script/sub_event_007.dat";

    public static ProjectGame? GameForBuild(string build) => build.TrimEnd('0').ToUpperInvariant() switch
    {
        "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471" => ProjectGame.Sword,
        "A16802625E7826BF83B6F9708E475B912A9AB7DF" => ProjectGame.Shield,
        _ => null,
    };

    public static IReadOnlyList<SwShExecutableMergeRegion> Regions(string build)
    {
        var detected = GameForBuild(build);
        if (detected is null && build.Length is >= 16 and < 40 && build.Length % 2 == 0)
        {
            var matches = new[] { "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471", "A16802625E7826BF83B6F9708E475B912A9AB7DF" }
                .Where(id => id.StartsWith(build, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1) detected = GameForBuild(matches[0]);
        }
        if (detected is not { } game) return [];
        return SwShExeFsReservedRegionLedger.Regions.Select(r => r.Owner).Distinct()
            .SelectMany(owner => SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(owner, game))
            .Where(r => !r.FeatureId.Contains(game == ProjectGame.Sword ? "-shield-" : "-sword-", StringComparison.Ordinal))
            .Where(r => r.Rule != "requires-vanilla")
            .Select(r => new SwShExecutableMergeRegion(r.Owner, r.StartOffset!.Value, r.Length!.Value)).ToArray();
    }

    public static int? HyperLevel(string path, byte[] bytes)
    {
        if (path.Equals("exefs/main", StringComparison.OrdinalIgnoreCase))
        {
            var state = SwShHyperTrainingMainPatcher.Analyze(bytes);
            return state.Kind is SwShHyperTrainingMainKind.NotInstalled or SwShHyperTrainingMainKind.CustomMinimumLevel ? state.MinimumLevel : null;
        }
        if (path.Equals(HyperScript, StringComparison.OrdinalIgnoreCase))
        {
            var state = SwShHyperTrainingAmxPatcher.Analyze(bytes);
            return state.Kind is SwShHyperTrainingScriptKind.NotInstalled or SwShHyperTrainingScriptKind.CustomMinimumLevel ? state.MinimumLevel : null;
        }
        if (path.Equals(HyperDialogue, StringComparison.OrdinalIgnoreCase))
        {
            var state = SwShHyperTrainingDialoguePatcher.Analyze(bytes);
            return state.Kind is SwShHyperTrainingDialogueKind.NotInstalled or SwShHyperTrainingDialogueKind.CustomMinimumLevel ? state.MinimumLevel : null;
        }
        return null;
    }

    public static byte[] SetHyperLevel(string path, byte[] bytes, int level) => path switch
    {
        "exefs/main" => SwShHyperTrainingMainPatcher.ApplyMinimumLevel(bytes, level, GameForBuild(Convert.ToHexString(NsoFile.Parse(bytes).BuildId))),
        HyperScript => SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(bytes, level),
        HyperDialogue => SwShHyperTrainingDialoguePatcher.ApplyMinimumLevel(bytes, level),
        _ => throw new ArgumentException("Not a Hyper Training resource."),
    };

    public static bool IsValid(byte[] original, byte[] merged)
    {
        if (GameForBuild(Convert.ToHexString(NsoFile.Parse(original).BuildId)) is not { } game) return true;
        var shinyBase = SwShShinyRateMainPatcher.Analyze(original, game);
        var shiny = SwShShinyRateMainPatcher.Analyze(merged, game);
        if (shinyBase.Kind is SwShShinyRateMainKind.Default or SwShShinyRateMainKind.FixedRolls or SwShShinyRateMainKind.AlwaysShiny
            && shiny.Kind is not (SwShShinyRateMainKind.Default or SwShShinyRateMainKind.FixedRolls or SwShShinyRateMainKind.AlwaysShiny)) return false;
        return HyperLevel("exefs/main", original) is null || HyperLevel("exefs/main", merged) is not null;
    }
}
