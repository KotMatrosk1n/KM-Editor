// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.ScriptedBosses;
using KM.ZA.Workflows;

namespace KM.ZA.Moves;

public sealed record ZaMoveProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaMoveStatChangeRecord(
    int Slot,
    int Stat,
    string StatName,
    int Stage,
    int Percent);

public sealed record ZaMoveFlagRecord(
    string Field,
    string Label,
    bool Enabled);

public sealed record ZaMoveEditableFieldOption(
    double Value,
    string Label);

public sealed record ZaMoveEditableField(
    string Field,
    string Label,
    string ValueKind,
    double? MinimumValue,
    double? MaximumValue,
    IReadOnlyList<ZaMoveEditableFieldOption> Options);

public sealed record ZaMoveRuntimeVariantRecord(
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
    IReadOnlyList<ZaMoveStatChangeRecord> StatChanges,
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

public sealed record ZaMoveTimingRecord(
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

public sealed record ZaMovePlayerDamageInvocationSourceRecord(
    int ParentBulletId,
    string Kind);

public sealed record ZaMovePlayerDamageTimelinePathEdgeRecord(
    int ParentBulletId,
    int ChildBulletId,
    string Kind);

public sealed record ZaMovePlayerDamageLocalConditionRecord(
    string State,
    string Kind,
    string SemanticKey,
    string? RawTag);

public sealed record ZaMovePlayerDamageTimelineLaunchRecord(
    string ShootActionKey,
    int RootBulletId,
    string TimelineName,
    string TimelinePath,
    ZaMovePlayerDamageLocalConditionRecord LocalCondition)
{
    public IReadOnlyList<IReadOnlyList<ZaMovePlayerDamageTimelinePathEdgeRecord>>
        RelationshipPaths
    { get; init; } = [];
}

public sealed record ZaMovePlayerDamageInvocationRecord(
    int BulletId,
    string ResourceName,
    string ResourcePath,
    string Role,
    double LifetimeSeconds,
    bool IsSelf,
    IReadOnlyList<ZaMovePlayerDamageInvocationSourceRecord> Sources,
    IReadOnlyList<ZaMovePlayerDamageTimelineLaunchRecord> VerifiedVanillaTimelineLaunches)
{
    internal string IncomingAncestryShape { get; init; } = string.Empty;
}

public sealed record ZaMovePlayerDamageRecord(
    int AttackId,
    int RuntimeMoveId,
    int DefaultDamage,
    int PlayerDamage,
    int VanillaPlayerDamage,
    double HitIntervalSeconds,
    bool BulletMappingMatchesVerifiedVanilla,
    bool VerifiedVanillaTimelineCatalogAvailable,
    IReadOnlyList<ZaMovePlayerDamageInvocationRecord> Invocations);

public sealed record ZaMoveVanillaFieldValue(string Field, string Value);

public sealed record ZaMoveRecord(
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
    IReadOnlyList<ZaMoveStatChangeRecord> StatChanges,
    IReadOnlyList<ZaMoveFlagRecord> Flags,
    ZaMoveProvenance Provenance)
{
    public IReadOnlyList<ZaMoveRuntimeVariantRecord> RuntimeVariants { get; init; } = [];

    public ZaMoveTimingRecord? Timing { get; init; }

    public IReadOnlyList<ZaMoveTimingRecord> TimingRows { get; init; } = [];

    public IReadOnlyDictionary<int, int> GameModuleTimingCounts { get; init; } =
        new Dictionary<int, int>();

    public IReadOnlyDictionary<int, int> GameModuleVariantMultiplicities { get; init; } =
        new Dictionary<int, int>();

    public IReadOnlyList<ZaMovePlayerDamageRecord> PlayerDamageRows { get; init; } = [];

    public IReadOnlyList<ZaMoveVanillaFieldValue> VanillaValues { get; init; } = [];

    public IReadOnlyList<string> RuntimeSourceFiles { get; init; } = [];

    public ProjectFileLayer RuntimeBattleSourceLayer { get; init; } = ProjectFileLayer.Base;

    public ProjectFileLayer RuntimeTimingSourceLayer { get; init; } = ProjectFileLayer.Base;

    public ProjectFileLayer RuntimePlayerDamageSourceLayer { get; init; } = ProjectFileLayer.Base;

    internal IReadOnlyList<ZaMoveRuntimeVariantRecord> VanillaRuntimeVariants { get; init; } = [];

    internal IReadOnlySet<int> AmbiguousRuntimeVariantIds { get; init; } = new HashSet<int>();

    internal IReadOnlyList<ZaMoveTimingRecord> VanillaTimingRows { get; init; } = [];

    internal IReadOnlyList<ZaMovePlayerDamageRecord> VanillaPlayerDamageRows { get; init; } = [];

    internal string? RuntimeBattleVanillaFingerprint { get; init; }

    internal string? RuntimeTimingVanillaFingerprint { get; init; }

    internal string? RuntimePlayerDamageVanillaFingerprint { get; init; }

    internal bool RuntimeBattleDiffersFromVanilla { get; init; }

    internal bool RuntimeTimingDiffersFromVanilla { get; init; }

    internal bool RuntimePlayerDamageDiffersFromVanilla { get; init; }

    internal int? VanillaFlinch { get; init; }

    internal bool WazaFlinchDiffersFromVanilla { get; init; }

    public bool HasRuntimeData => RuntimeVariants.Count > 0 || TimingRows.Count > 0 || PlayerDamageRows.Count > 0;

    public bool CanRevertToVanilla { get; init; }

    public string? RevertToVanillaBlockedReason { get; init; }
}

public sealed record ZaMovesWorkflowStats(
    int TotalMoveCount,
    int EnabledMoveCount,
    int SourceFileCount,
    int ActiveFlagCount);

public sealed record ZaMovesWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaMoveRecord> Moves,
    IReadOnlyList<ZaMoveEditableField> EditableFields,
    ZaMovesWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public IReadOnlyList<ZaMoveEditableFieldOption> ProjectileOptions { get; init; } = [];

    public IReadOnlyList<ZaScriptedBossProfileRecord> ScriptedBosses { get; init; } = [];

    internal IReadOnlyList<ProjectFileReference> ProjectileCatalogSources { get; init; } = [];

    internal IReadOnlyList<string> SpawnLocators { get; init; } = [];
}
