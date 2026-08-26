// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.TrainerPools;

public sealed record LoadTrainerPoolsWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageTrainerPoolFixedCountSwapRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string SourceLogicalPoolId,
    string SourceRawTrainerId,
    string DestinationLogicalPoolId,
    string DestinationRawTrainerId);

public enum TrainerPoolKindDto
{
    Story,
    Infinity,
}

public sealed record TrainerPoolMemberDto(
    string RawTrainerId,
    string AppearanceAssetId,
    string RawRosterId,
    int RosterIndex,
    string DisplayName,
    int StoredRank,
    int TeamSize,
    int Weight);

public sealed record TrainerPoolRecordDto(
    string LogicalPoolId,
    string DisplayLabel,
    string CompatibilityGroup,
    TrainerPoolKindDto Kind,
    IReadOnlyList<string> PhysicalTableIds,
    int ReferencedPhysicalTableCount,
    int MemberCount,
    int TotalWeight,
    IReadOnlyList<TrainerPoolMemberDto> Members);

public sealed record TrainerPoolsWorkflowStatsDto(
    int LogicalPoolCount,
    int PhysicalMirrorCount,
    int MemberReferenceCount,
    int DormantPhysicalMirrorCount);

public sealed record TrainerPoolsWorkflowDto(
    IReadOnlyList<TrainerPoolRecordDto> Pools,
    TrainerPoolsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    bool CanStage);

public sealed record LoadTrainerPoolsWorkflowResponse(TrainerPoolsWorkflowDto Workflow);

public sealed record StageTrainerPoolFixedCountSwapResponse(
    TrainerPoolsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
