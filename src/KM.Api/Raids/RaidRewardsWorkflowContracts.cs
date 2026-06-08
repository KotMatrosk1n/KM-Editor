// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Raids;

public sealed record LoadRaidRewardsWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateRaidRewardFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string TableId,
    int Slot,
    string Field,
    string Value);

public sealed record UpdateRaidRewardFieldResponse(
    RaidRewardsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record RaidRewardProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record RaidRewardItemRecordDto(
    int Slot,
    int EntryId,
    int ItemId,
    string ItemName,
    int Quantity,
    int Weight,
    IReadOnlyList<int> Values);

public sealed record RaidRewardTableRecordDto(
    string TableId,
    string DenId,
    int Rank,
    string GameVersion,
    string RewardKind,
    string RewardKindLabel,
    string ArchiveMember,
    int TableIndex,
    string SourceTableHash,
    IReadOnlyList<RaidRewardItemRecordDto> Rewards,
    RaidRewardProvenanceDto Provenance);

public sealed record RaidRewardEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<RaidRewardEditableFieldOptionDto> Options);

public sealed record RaidRewardEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record RaidRewardsWorkflowStatsDto(
    int TotalTableCount,
    int TotalRewardItemCount,
    int SourceFileCount);

public sealed record RaidRewardsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<RaidRewardTableRecordDto> Tables,
    IReadOnlyList<RaidRewardEditableFieldDto> EditableFields,
    RaidRewardsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadRaidRewardsWorkflowResponse(RaidRewardsWorkflowDto Workflow);
