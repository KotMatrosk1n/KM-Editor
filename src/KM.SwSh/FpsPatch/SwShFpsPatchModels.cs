// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;

namespace KM.SwSh.FpsPatch;

public static class SwShFpsPatchAnimationTimingComponents
{
    public const string BattleSequences = "battleSequences";
    public const string BattleCameras = "battleCameras";
    public const string BattleInterface = "battleInterface";
    public const string BattleModels = "battleModels";
    public const string OpeningAndDemos = "openingAndDemos";
    public const string RecoveryAnimation = "recoveryAnimation";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
        new[]
        {
            BattleSequences,
            BattleCameras,
            BattleInterface,
            BattleModels,
            OpeningAndDemos,
            RecoveryAnimation,
        });
}

public sealed record SwShFpsPatchRomFsCategoryStatus(
    string Category,
    int ManagedFileCount,
    int PatchedFileCount,
    int StaleOwnedFileCount,
    int ConflictingFileCount);

public sealed record SwShFpsPatchAnimationTimingComponentStatus(
    string Id,
    bool Enabled,
    string InputState,
    IReadOnlyList<ValidationDiagnostic> InputDiagnostics,
    int ManagedFileCount,
    int PatchedFileCount,
    int StaleOwnedFileCount,
    int ConflictingFileCount);

public sealed record SwShFpsPatchStatus(
    string Status,
    string Message,
    bool GlobalApplyBlocked,
    bool GlobalRestoreBlocked,
    bool HasRemovableKmState,
    IReadOnlyList<ValidationDiagnostic> RestoreDiagnostics,
    string? BuildId,
    ProjectGame? DetectedGame,
    int PatchedMainSiteCount,
    int MainSiteCount,
    int PatchedRomFsFileCount,
    int ManagedRomFsFileCount,
    int StaleOwnedRomFsFileCount,
    int ConflictingRomFsFileCount,
    IReadOnlyList<string> StaleOwnedRomFsFiles,
    IReadOnlyList<string> ConflictingRomFsFiles,
    IReadOnlyList<SwShFpsPatchRomFsCategoryStatus> RomFsCategories,
    IReadOnlyList<SwShFpsPatchAnimationTimingComponentStatus> AnimationTimingComponents,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShFpsPatchApplyResult(
    SwShFpsPatchStatus Status,
    ApplyResult ApplyResult,
    bool RecoveryRequired);
