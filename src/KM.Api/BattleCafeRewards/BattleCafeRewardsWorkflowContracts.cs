// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.BattleCafeRewards;

public static class BattleCafeRewardsContract
{
    public const int RowCount = 23;
    public const int MaximumItemOptions = 10_000;
}

public sealed record LoadBattleCafeRewardsWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageBattleCafeRewardRowsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<BattleCafeRewardRowEditDto> Rows);

public sealed record BattleCafeRewardRowEditDto(
    int RowIndex,
    int ExpectedItemId,
    int ExpectedDwightPercent,
    int ExpectedBernardPercent,
    int ExpectedRichardPercent,
    int ItemId,
    int DwightPercent,
    int BernardPercent,
    int RichardPercent);

public sealed record BattleCafeRewardRowDto(
    int RowIndex,
    int ItemId,
    string ItemName,
    int DwightPercent,
    int BernardPercent,
    int RichardPercent);

public sealed record BattleCafeRewardItemOptionDto(
    int ItemId,
    string Name,
    string Category);

public sealed record BattleCafeRewardTotalsDto(
    int DwightPercent,
    int BernardPercent,
    int RichardPercent);

public sealed record BattleCafeRewardsProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record BattleCafeRewardsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<BattleCafeRewardRowDto> Rewards,
    IReadOnlyList<BattleCafeRewardItemOptionDto> ItemOptions,
    BattleCafeRewardTotalsDto Totals,
    BattleCafeRewardsProvenanceDto? Provenance,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadBattleCafeRewardsWorkflowResponse(
    BattleCafeRewardsWorkflowDto Workflow);

public sealed record StageBattleCafeRewardRowsResponse(
    BattleCafeRewardsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
