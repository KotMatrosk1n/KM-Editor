// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.ScriptedBosses;
using KM.Api.Workflows;
using System.Text.Json.Serialization;

namespace KM.Api.Moves;

public sealed record LoadMovesWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateMoveFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int MoveId,
    string Field,
    string Value);

public sealed record MoveFieldUpdateDto(
    int MoveId,
    string Field,
    string Value);

public sealed record UpdateMoveFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<MoveFieldUpdateDto> Updates);

public sealed record StageMoveVanillaRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int MoveId);

public sealed record MoveProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record MoveStatChangeRecordDto(
    int Slot,
    int Stat,
    string StatName,
    int Stage,
    int Percent);

public sealed record MoveFlagRecordDto(
    string Field,
    string Label,
    bool Enabled);

public sealed record MoveEditableFieldOptionDto(
    double Value,
    string Label);

public sealed record MoveEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    double? MinimumValue,
    double? MaximumValue,
    IReadOnlyList<MoveEditableFieldOptionDto> Options);

public sealed record MoveRuntimeVariantRecordDto(
    int Variant,
    int Type,
    string TypeName,
    int EffectCategory,
    int DamageType,
    string DamageTypeName,
    int Power,
    int CriticalRank,
    int HpRecoverRatio,
    int ShrinkPercent,
    int ConditionId,
    int ConditionPercent,
    int ConditionCount,
    int ConditionTurnMin,
    int ConditionTurnMax,
    IReadOnlyList<MoveStatChangeRecordDto> StatChanges,
    int DamageRecoverRatio,
    int DamageDrainRatio,
    bool IsGuard,
    bool IsAvoidedByFloating,
    bool MakesContact,
    bool IsSlicing,
    bool IsWind,
    bool BypassesSubstitute,
    bool ThawsUser,
    bool RestoresHp,
    bool AllowedWhileHealBlocked,
    bool CallableByMetronome,
    bool AppliesCondition,
    bool BlockedByProtect,
    bool CannotKnockOut,
    int ValueEffectRatio);

public sealed record MoveTimingRecordDto(
    int TimingMoveId,
    int Variant,
    int Occurrence,
    int ChargeFrames,
    int AttackLoopFrames,
    int SpawnOrigin,
    string SpawnLocator,
    int SpawnLocatorOption,
    double SpawnOffsetX,
    double SpawnOffsetY,
    double SpawnOffsetZ,
    int ShotDirection,
    int TargetCorrectionType,
    double ImpactMotionSpeed,
    int MovementType,
    double RangeMin,
    double RangeMax,
    double HeightTolerance,
    double EffectiveRange,
    int ProjectileCountMin,
    int ProjectileCountMax,
    int HitPercent,
    double Cooldown,
    double EffectTime,
    int EffectValue,
    double MegaPowerBonus,
    double PlayedMotionSpeed,
    int OverwriteProjectile1,
    int ReplacementProjectile1,
    int OverwriteProjectile2,
    int ReplacementProjectile2,
    int OverwriteProjectile3,
    int ReplacementProjectile3,
    int OverwriteProjectile4,
    int ReplacementProjectile4,
    int OverwriteProjectile5,
    int ReplacementProjectile5,
    double ProjectileCorrectionScale);

public sealed record MovePlayerDamageInvocationSourceRecordDto(
    int ParentBulletId,
    string Kind);

public sealed record MovePlayerDamageTimelinePathEdgeRecordDto(
    int ParentBulletId,
    int ChildBulletId,
    string Kind);

public sealed record MovePlayerDamageLocalConditionRecordDto(
    string State,
    string Kind,
    string SemanticKey,
    string? RawTag);

public sealed record MovePlayerDamageTimelineLaunchRecordDto(
    string ShootActionKey,
    int RootBulletId,
    string TimelineName,
    string TimelinePath,
    MovePlayerDamageLocalConditionRecordDto LocalCondition)
{
    public IReadOnlyList<IReadOnlyList<MovePlayerDamageTimelinePathEdgeRecordDto>>
        RelationshipPaths
    { get; init; } = [];
}

public sealed record MovePlayerDamageInvocationRecordDto(
    int BulletId,
    string ResourceName,
    string ResourcePath,
    string Role,
    double LifetimeSeconds,
    bool IsSelf,
    IReadOnlyList<MovePlayerDamageInvocationSourceRecordDto> Sources,
    IReadOnlyList<MovePlayerDamageTimelineLaunchRecordDto> VerifiedVanillaTimelineLaunches);

public sealed record MovePlayerDamageRecordDto(
    int AttackId,
    int RuntimeMoveId,
    int DefaultDamage,
    int PlayerDamage,
    int VanillaPlayerDamage,
    double HitIntervalSeconds,
    bool BulletMappingMatchesVerifiedVanilla,
    bool VerifiedVanillaTimelineCatalogAvailable,
    IReadOnlyList<MovePlayerDamageInvocationRecordDto> Invocations);

public sealed record MoveVanillaFieldValueDto(string Field, string Value);

public sealed record MoveRecordDto(
    int MoveId,
    string Name,
    string? Description,
    uint Version,
    bool CanUseMove,
    int Type,
    string TypeName,
    int Quality,
    int Category,
    string CategoryName,
    int Power,
    int Accuracy,
    int PP,
    int Priority,
    int CritStage,
    int MaxMovePower,
    int Target,
    string TargetName,
    int HitMin,
    int HitMax,
    int TurnMin,
    int TurnMax,
    int Inflict,
    string InflictName,
    int InflictPercent,
    int RawInflictCount,
    int Flinch,
    int EffectSequence,
    int Recoil,
    int RawHealing,
    IReadOnlyList<MoveStatChangeRecordDto> StatChanges,
    IReadOnlyList<MoveFlagRecordDto> Flags,
    MoveProvenanceDto Provenance)
{
    public IReadOnlyList<MoveRuntimeVariantRecordDto> RuntimeVariants { get; init; } = [];

    public MoveTimingRecordDto? Timing { get; init; }

    public IReadOnlyList<MoveTimingRecordDto> TimingRows { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyDictionary<int, int> GameModuleTimingCounts { get; init; } =
        new Dictionary<int, int>();

    public IReadOnlyList<MovePlayerDamageRecordDto> PlayerDamageRows { get; init; } = [];

    public IReadOnlyList<MoveVanillaFieldValueDto> VanillaValues { get; init; } = [];

    public IReadOnlyList<string> RuntimeSourceFiles { get; init; } = [];

    public bool HasRuntimeData { get; init; }

    public bool CanRevertToVanilla { get; init; }

    public string? RevertToVanillaBlockedReason { get; init; }
}

public sealed record MovesWorkflowStatsDto(
    int TotalMoveCount,
    int EnabledMoveCount,
    int SourceFileCount,
    int ActiveFlagCount);

public sealed record MovesWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<MoveRecordDto> Moves,
    IReadOnlyList<MoveEditableFieldDto> EditableFields,
    MovesWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public IReadOnlyList<MoveEditableFieldOptionDto> ProjectileOptions { get; init; } = [];

    public IReadOnlyList<ScriptedBossProfileDto> ScriptedBosses { get; init; } = [];
}

public sealed record LoadMovesWorkflowResponse(MovesWorkflowDto Workflow);

public sealed record UpdateMoveFieldResponse(
    MovesWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdateMoveFieldsResponse(
    MovesWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageMoveVanillaResponse(
    MovesWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
