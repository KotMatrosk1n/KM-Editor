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
    string? MessageKey,
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

public sealed record SvTextCategoryRecord(
    string CategoryId,
    string Label,
    string Description,
    int SourceFileCount);

public sealed record SvTextLanguageRecord(
    string Language,
    string Label);

public sealed record SvTextResultPage(
    int Offset,
    int Limit,
    int ReturnedEntryCount,
    bool HasPrevious,
    bool HasNext);

public sealed record SvTextWorkflowQuery(
    string? SearchText,
    int Offset,
    int Limit,
    string? CategoryId = null,
    string? Language = null);

public sealed record SvTextWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvTextEntryRecord> Entries,
    IReadOnlyList<SvDialogueReferenceRecord> DialogueReferences,
    IReadOnlyList<SvTextEditableField> EditableFields,
    IReadOnlyList<SvTextCategoryRecord> Categories,
    string SelectedCategoryId,
    IReadOnlyList<SvTextLanguageRecord> Languages,
    string SelectedLanguage,
    SvTextResultPage? Page,
    SvTextWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
