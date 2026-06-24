// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Text;

public sealed record SvTextProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvTextEntryRecord(
    int TextId,
    string TextKey,
    string Label,
    string Language,
    string SourceFile,
    int LineIndex,
    string Value,
    bool CanEdit,
    string? EditBlockedReason,
    SvTextProvenance Provenance);

public sealed record SvDialogueReferenceRecord(
    string DialogueId,
    string Label,
    int TextId,
    string Context,
    string Preview,
    SvTextProvenance Provenance);

public sealed record SvTextWorkflowStats(
    int TotalTextEntryCount,
    int DialogueReferenceCount,
    int SourceFileCount);

public sealed record SvTextEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumLength,
    int? MaximumLength);

public sealed record SvTextWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvTextEntryRecord> Entries,
    IReadOnlyList<SvDialogueReferenceRecord> DialogueReferences,
    IReadOnlyList<SvTextEditableField> EditableFields,
    SvTextWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
