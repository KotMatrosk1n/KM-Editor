// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.DumpImport;

public sealed record ZaDumpImportProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaDumpImportColumnRecord(
    int Column,
    string Header,
    string ValueKind,
    bool IsRequired,
    string Description);

public sealed record ZaDumpImportProfileRecord(
    string ProfileId,
    string Name,
    string SourceKind,
    string TargetWorkflow,
    string Status,
    string Description,
    IReadOnlyList<ZaDumpImportColumnRecord> Columns,
    ZaDumpImportProvenance Provenance);

public sealed record ZaDumpImportWorkflowStats(
    int TotalProfileCount,
    int TotalColumnCount,
    int SourceFileCount);

public sealed record ZaDumpImportWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaDumpImportProfileRecord> Profiles,
    ZaDumpImportWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaDumpImportCellPreviewRecord(
    string Header,
    string Field,
    string Value,
    string Status,
    string Message);

public sealed record ZaDumpImportRowPreviewRecord(
    int RowNumber,
    string RecordId,
    string Status,
    string Summary,
    IReadOnlyList<ZaDumpImportCellPreviewRecord> Cells,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaDumpImportPreview(
    string ProfileId,
    string SourcePath,
    int TotalRowCount,
    int AcceptedRowCount,
    int RejectedRowCount,
    int SkippedRowCount,
    IReadOnlyList<ZaDumpImportRowPreviewRecord> Rows);

public sealed record ZaDumpImportExecutionResult(
    ZaDumpImportWorkflow Workflow,
    EditSession Session,
    ZaDumpImportPreview Preview,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
