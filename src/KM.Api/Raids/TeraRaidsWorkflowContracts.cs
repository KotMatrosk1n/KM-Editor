// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Raids;

public sealed record LoadTeraRaidsWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateTeraRaidFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string RecordId,
    string Field,
    string Value);

public sealed record TeraRaidFieldUpdateDto(
    string RecordId,
    string Field,
    string Value);

public sealed record UpdateTeraRaidFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<TeraRaidFieldUpdateDto> Updates);

public sealed record TeraRaidProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record TeraRaidEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record TeraRaidEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<TeraRaidEditableFieldOptionDto> Options);

public sealed record TeraRaidMoveDto(
    int Slot,
    int MoveId,
    string? Move,
    int PointUps);

public sealed record TeraRaidIvsDto(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record TeraRaidRewardItemDto(
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
    TeraRaidProvenanceDto Provenance);

public sealed record TeraRaidRewardTableDto(
    string RecordId,
    string RewardKind,
    string RewardKindLabel,
    int TableIndex,
    string TableHash,
    int RewardItemCount,
    string Preview,
    IReadOnlyList<TeraRaidRewardItemDto> Rewards,
    TeraRaidProvenanceDto Provenance);

public sealed record TeraRaidRecordDto(
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
    IReadOnlyList<TeraRaidMoveDto> Moves,
    TeraRaidIvsDto Ivs,
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
    TeraRaidProvenanceDto Provenance)
{
    public IReadOnlyList<TeraRaidEditableFieldOptionDto> AbilityOptions { get; init; } =
        Array.Empty<TeraRaidEditableFieldOptionDto>();
}

public sealed record TeraRaidsWorkflowStatsDto(
    int TotalRaidCount,
    int TotalRewardTableCount,
    int TotalRewardItemCount,
    int SourceFileCount);

public sealed record TeraRaidsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<TeraRaidRecordDto> Raids,
    IReadOnlyList<TeraRaidRewardTableDto> FixedRewardTables,
    IReadOnlyList<TeraRaidRewardTableDto> LotteryRewardTables,
    IReadOnlyList<TeraRaidEditableFieldDto> EditableFields,
    TeraRaidsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadTeraRaidsWorkflowResponse(TeraRaidsWorkflowDto Workflow);

public sealed record UpdateTeraRaidFieldResponse(
    TeraRaidsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdateTeraRaidFieldsResponse(
    TeraRaidsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
