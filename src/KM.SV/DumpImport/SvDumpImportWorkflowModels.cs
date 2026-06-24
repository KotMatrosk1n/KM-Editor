// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.DumpImport;

public sealed record SvDumpImportProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvDumpImportColumnRecord(
    int Column,
    string Header,
    string ValueKind,
    bool IsRequired,
    string Description);

public sealed record SvDumpImportProfileRecord(
    string ProfileId,
    string Name,
    string SourceKind,
    string TargetWorkflow,
    string Status,
    string Description,
    IReadOnlyList<SvDumpImportColumnRecord> Columns,
    SvDumpImportProvenance Provenance);

public sealed record SvDumpImportWorkflowStats(
    int TotalProfileCount,
    int TotalColumnCount,
    int SourceFileCount);

public sealed record SvDumpImportWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvDumpImportProfileRecord> Profiles,
    SvDumpImportWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvDumpImportCellPreviewRecord(
    string Header,
    string Field,
    string Value,
    string Status,
    string Message);

public sealed record SvDumpImportRowPreviewRecord(
    int RowNumber,
    string RecordId,
    string Status,
    string Summary,
    IReadOnlyList<SvDumpImportCellPreviewRecord> Cells,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvDumpImportPreview(
    string ProfileId,
    string SourcePath,
    int TotalRowCount,
    int AcceptedRowCount,
    int RejectedRowCount,
    int SkippedRowCount,
    IReadOnlyList<SvDumpImportRowPreviewRecord> Rows);

public sealed record SvDumpImportExecutionResult(
    SvDumpImportWorkflow Workflow,
    EditSession Session,
    SvDumpImportPreview Preview,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
