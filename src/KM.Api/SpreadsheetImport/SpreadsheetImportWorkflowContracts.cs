// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.SpreadsheetImport;

public sealed record LoadSpreadsheetImportWorkflowRequest(ProjectPathsDto Paths);

public sealed record PreviewSpreadsheetImportRequest(
    ProjectPathsDto Paths,
    string ProfileId,
    string SourcePath,
    EditSessionDto? Session);

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

public sealed record SpreadsheetImportCellPreviewRecordDto(
    string Header,
    string Field,
    string Value,
    string Status,
    string Message);

public sealed record SpreadsheetImportRowPreviewRecordDto(
    int RowNumber,
    string RecordId,
    string Status,
    string Summary,
    IReadOnlyList<SpreadsheetImportCellPreviewRecordDto> Cells,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record SpreadsheetImportPreviewDto(
    string ProfileId,
    string SourcePath,
    int TotalRowCount,
    int AcceptedRowCount,
    int RejectedRowCount,
    int SkippedRowCount,
    IReadOnlyList<SpreadsheetImportRowPreviewRecordDto> Rows);

public sealed record PreviewSpreadsheetImportResponse(
    SpreadsheetImportWorkflowDto Workflow,
    EditSessionDto Session,
    SpreadsheetImportPreviewDto Preview,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
