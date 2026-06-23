// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Raids;

public sealed record SvTeraRaidProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvTeraRaidEditableFieldOption(
    int Value,
    string Label);

public sealed record SvTeraRaidEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvTeraRaidEditableFieldOption> Options)
{
    public SvTeraRaidEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SvTeraRaidEditableFieldOption>())
    {
    }
}

public sealed record SvTeraRaidMoveRecord(
    int Slot,
    int MoveId,
    string? Move,
    int PointUps);

public sealed record SvTeraRaidIvsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SvTeraRaidRewardItemRecord(
    string RecordId,
    string RewardKind,
    string RewardKindLabel,
    int TableIndex,
    string TableHash,
    int Slot,
    int Category,
    string CategoryLabel,
    int? SubjectType,
    string? SubjectTypeLabel,
    int ItemId,
    string ItemName,
    int Count,
    int? Rate,
    bool? RareItemFlag,
    SvTeraRaidProvenance Provenance);

public sealed record SvTeraRaidRewardTableRecord(
    string RecordId,
    string RewardKind,
    string RewardKindLabel,
    int TableIndex,
    string TableHash,
    int RewardItemCount,
    string Preview,
    IReadOnlyList<SvTeraRaidRewardItemRecord> Rewards,
    SvTeraRaidProvenance Provenance);

public sealed record SvTeraRaidEntry(
    string RecordId,
    string Region,
    int? StarRank,
    string StarLabel,
    int EntryIndex,
    int RaidNo,
    int Version,
    string VersionLabel,
    int DeliveryGroupId,
    int Difficulty,
    int SpawnRate,
    int CaptureRate,
    int CaptureLevel,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int HeldItemId,
    string? HeldItem,
    int BallItemId,
    string BallItem,
    int Ability,
    string AbilityLabel,
    int Nature,
    string NatureLabel,
    int Gender,
    string GenderLabel,
    int ShinyLock,
    string ShinyLockLabel,
    int TeraType,
    string TeraTypeLabel,
    int MoveMode,
    string MoveModeLabel,
    IReadOnlyList<SvTeraRaidMoveRecord> Moves,
    SvTeraRaidIvsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    int ScaleMode,
    string ScaleModeLabel,
    int ScaleValue,
    int HeightMode,
    string HeightModeLabel,
    int HeightValue,
    int WeightMode,
    string WeightModeLabel,
    int WeightValue,
    int HpMultiplier,
    int ShieldTriggerHp,
    int ShieldTriggerTime,
    int DoubleActionHp,
    int DoubleActionTime,
    int DoubleActionRate,
    string FixedRewardTableHash,
    string LotteryRewardTableHash,
    string FixedRewardPreview,
    string LotteryRewardPreview,
    SvTeraRaidProvenance Provenance)
{
    public IReadOnlyList<SvTeraRaidEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<SvTeraRaidEditableFieldOption>();
}

public sealed record SvTeraRaidsWorkflowStats(
    int TotalRaidCount,
    int TotalRewardTableCount,
    int TotalRewardItemCount,
    int SourceFileCount);

public sealed record SvTeraRaidsWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvTeraRaidEntry> Raids,
    IReadOnlyList<SvTeraRaidRewardTableRecord> FixedRewardTables,
    IReadOnlyList<SvTeraRaidRewardTableRecord> LotteryRewardTables,
    IReadOnlyList<SvTeraRaidEditableField> EditableFields,
    SvTeraRaidsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
