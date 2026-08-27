// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Output;

namespace KM.Api.RuntimeSettings;

public static class GameplaySettingsContract
{
    public const int MaximumCachedReviews = 16;
    public const int MaximumReviewIdLength = 64;
}

public enum GameplaySettingsStateDto
{
    Missing,
    Ready,
    Incomplete,
    Unmanaged,
    Conflict,
    Unsupported,
    Corrupt,
}

public sealed record GameplaySettingsValuesDto(
    bool ExperienceShareEnabled,
    uint ExperienceRateBasisPoints,
    bool LevelCapEnabled,
    byte LevelCap);

public sealed record GameplaySettingCapabilityDto(
    bool Available,
    string ReasonCode,
    string ScopeCode);

public sealed record GameplaySettingsSnapshotDto(
    string TitleId,
    string ExecutableProfileId,
    string SupportedGameVersion,
    string Generation,
    bool HasExperienceShare,
    bool HasExperienceRate,
    bool HasLevelCap,
    GameplaySettingCapabilityDto ExperienceShareCapability,
    GameplaySettingCapabilityDto ExperienceRateCapability,
    GameplaySettingCapabilityDto LevelCapCapability,
    GameplaySettingsValuesDto Values);

public sealed record GetGameplaySettingsRequest(OutputScopeDto Scope);

public sealed record GetGameplaySettingsResponse(
    GameplaySettingsStateDto State,
    GameplaySettingsSnapshotDto? Snapshot,
    string? Detail = null);

public sealed record PreviewGameplaySettingsUpdateRequest(
    OutputScopeDto Scope,
    string ExpectedGeneration,
    bool? ExperienceShareEnabled = null,
    uint? ExperienceRateBasisPoints = null,
    bool? LevelCapEnabled = null,
    byte? LevelCap = null);

public sealed record PreviewGameplaySettingsUpdateResponse(
    string ReviewId,
    DateTimeOffset ExpiresAtUtc,
    GameplaySettingsSnapshotDto Before,
    GameplaySettingsSnapshotDto After);

public sealed record ApplyGameplaySettingsUpdateRequest(
    OutputScopeDto Scope,
    string ReviewId);

public enum GameplaySettingsApplyOutcomeDto
{
    Committed,
    RolledBack,
    RecoveryRequired,
}

public sealed record ApplyGameplaySettingsUpdateResponse(
    string TransactionId,
    GameplaySettingsApplyOutcomeDto Outcome,
    GameplaySettingsSnapshotDto? Snapshot);
