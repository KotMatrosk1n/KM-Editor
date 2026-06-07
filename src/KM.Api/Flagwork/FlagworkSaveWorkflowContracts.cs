// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Flagwork;

public sealed record LoadFlagworkSaveWorkflowRequest(ProjectPathsDto Paths);

public sealed record FlagworkSaveProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record FlagRecordDto(
    string FlagId,
    string Name,
    string Category,
    string ValueKind,
    string DefaultValue,
    string Description,
    FlagworkSaveProvenanceDto Provenance);

public sealed record SaveBlockRecordDto(
    string BlockId,
    string Name,
    int Offset,
    int Length,
    string Description,
    FlagworkSaveProvenanceDto Provenance);

public sealed record FlagworkSaveWorkflowStatsDto(
    int TotalFlagCount,
    int TotalSaveBlockCount,
    int SourceFileCount);

public sealed record FlagworkSaveWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<FlagRecordDto> Flags,
    IReadOnlyList<SaveBlockRecordDto> SaveBlocks,
    FlagworkSaveWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadFlagworkSaveWorkflowResponse(FlagworkSaveWorkflowDto Workflow);
