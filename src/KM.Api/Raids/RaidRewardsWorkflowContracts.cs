// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Raids;

public sealed record LoadRaidRewardsWorkflowRequest(ProjectPathsDto Paths);

public sealed record RaidRewardProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record RaidRewardItemRecordDto(
    int Slot,
    int ItemId,
    string ItemName,
    int Quantity,
    int Weight);

public sealed record RaidRewardTableRecordDto(
    string TableId,
    string DenId,
    int Rank,
    string GameVersion,
    IReadOnlyList<RaidRewardItemRecordDto> Rewards,
    RaidRewardProvenanceDto Provenance);

public sealed record RaidRewardsWorkflowStatsDto(
    int TotalTableCount,
    int TotalRewardItemCount,
    int SourceFileCount);

public sealed record RaidRewardsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<RaidRewardTableRecordDto> Tables,
    RaidRewardsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadRaidRewardsWorkflowResponse(RaidRewardsWorkflowDto Workflow);
