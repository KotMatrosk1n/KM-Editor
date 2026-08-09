// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Text;

public sealed record ZaTextProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaTextEntryRecord(
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
    ZaTextProvenance Provenance);

public sealed record ZaDialogueReferenceRecord(
    string DialogueId,
    string Label,
    int TextId,
    string Context,
    string Preview,
    ZaTextProvenance Provenance);

public sealed record ZaTextWorkflowStats(
    int TotalTextEntryCount,
    int DialogueReferenceCount,
    int SourceFileCount);

public sealed record ZaTextEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumLength,
    int? MaximumLength);

public sealed record ZaTextWorkflowQuery(
    string? SearchText,
    int Offset,
    int Limit,
    string? CategoryId = null,
    string? Language = null);

public sealed record ZaTextCategoryRecord(
    string CategoryId,
    string Label,
    string Description,
    int SourceFileCount);

public sealed record ZaTextResultPage(
    int Offset,
    int Limit,
    int ReturnedEntryCount,
    bool HasPrevious,
    bool HasNext);

public sealed record ZaTextLanguageRecord(
    string Language,
    string Label);

public sealed record ZaTextWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaTextEntryRecord> Entries,
    IReadOnlyList<ZaDialogueReferenceRecord> DialogueReferences,
    IReadOnlyList<ZaTextEditableField> EditableFields,
    IReadOnlyList<ZaTextCategoryRecord> Categories,
    string SelectedCategoryId,
    IReadOnlyList<ZaTextLanguageRecord> Languages,
    string SelectedLanguage,
    ZaTextResultPage? Page,
    ZaTextWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);


