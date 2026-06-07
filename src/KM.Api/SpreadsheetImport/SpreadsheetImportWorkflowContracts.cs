// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.SpreadsheetImport;

public sealed record LoadSpreadsheetImportWorkflowRequest(ProjectPathsDto Paths);

public sealed record SpreadsheetImportProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record SpreadsheetImportColumnRecordDto(
    int Column,
    string Header,
    string ValueKind,
    bool IsRequired,
    string Description);

public sealed record SpreadsheetImportProfileRecordDto(
    string ProfileId,
    string Name,
    string SourceKind,
    string TargetWorkflow,
    string Status,
    string Description,
    IReadOnlyList<SpreadsheetImportColumnRecordDto> Columns,
    SpreadsheetImportProvenanceDto Provenance);

public sealed record SpreadsheetImportWorkflowStatsDto(
    int TotalProfileCount,
    int TotalColumnCount,
    int SourceFileCount);

public sealed record SpreadsheetImportWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<SpreadsheetImportProfileRecordDto> Profiles,
    SpreadsheetImportWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadSpreadsheetImportWorkflowResponse(SpreadsheetImportWorkflowDto Workflow);
