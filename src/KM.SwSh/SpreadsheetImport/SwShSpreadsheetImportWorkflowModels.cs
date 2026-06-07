// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.SpreadsheetImport;

public sealed record SwShSpreadsheetImportProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShSpreadsheetImportColumnRecord(
    int Column,
    string Header,
    string ValueKind,
    bool IsRequired,
    string Description);

public sealed record SwShSpreadsheetImportProfileRecord(
    string ProfileId,
    string Name,
    string SourceKind,
    string TargetWorkflow,
    string Status,
    string Description,
    IReadOnlyList<SwShSpreadsheetImportColumnRecord> Columns,
    SwShSpreadsheetImportProvenance Provenance);

public sealed record SwShSpreadsheetImportWorkflowStats(
    int TotalProfileCount,
    int TotalColumnCount,
    int SourceFileCount);

public sealed record SwShSpreadsheetImportWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShSpreadsheetImportProfileRecord> Profiles,
    SwShSpreadsheetImportWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
