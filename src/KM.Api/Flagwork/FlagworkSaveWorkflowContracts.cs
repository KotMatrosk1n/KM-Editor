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
    string Kind,
    string ValueKind,
    string DefaultValue,
    string Description,
    string Table,
    int Index,
    string Hash,
    string Low32Key,
    FlagworkSaveProvenanceDto Provenance);

public sealed record SaveBlockRecordDto(
    string BlockId,
    string Name,
    string Key,
    string Hash,
    string Kind,
    string ValueKind,
    string Description,
    FlagworkSaveProvenanceDto Provenance);

public sealed record SaveFileRecordDto(
    string FileName,
    long SizeBytes,
    string Sha256,
    string Status,
    string Description);

public sealed record FlagworkSaveWorkflowStatsDto(
    int TotalFlagCount,
    int TotalSaveBlockCount,
    int SourceFileCount,
    bool HasSaveFile);

public sealed record FlagworkSaveWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<FlagRecordDto> Flags,
    IReadOnlyList<SaveBlockRecordDto> SaveBlocks,
    SaveFileRecordDto? SaveFile,
    FlagworkSaveWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadFlagworkSaveWorkflowResponse(FlagworkSaveWorkflowDto Workflow);
