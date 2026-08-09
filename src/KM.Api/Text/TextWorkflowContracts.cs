// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Text;

public sealed record TextWorkflowQueryDto(
    string? SearchText,
    int? Offset,
    int? Limit,
    string? CategoryId = null,
    string? Language = null);

public sealed record LoadTextWorkflowRequest(
    ProjectPathsDto Paths,
    TextWorkflowQueryDto? Query = null);

public sealed record TextProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record TextEntryRecordDto(
    int TextId,
    string TextKey,
    string Label,
    string Language,
    string SourceFile,
    int LineIndex,
    string Value,
    bool CanEdit,
    string? EditBlockedReason,
    TextProvenanceDto Provenance,
    string? MessageKey = null);

public sealed record DialogueReferenceRecordDto(
    string DialogueId,
    string Label,
    int TextId,
    string Context,
    string Preview,
    TextProvenanceDto Provenance);

public sealed record TextWorkflowStatsDto(
    int TotalTextEntryCount,
    int DialogueReferenceCount,
    int SourceFileCount);

public sealed record TextEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumLength,
    int? MaximumLength);

public sealed record TextCategoryDto(
    string CategoryId,
    string Label,
    string Description,
    int SourceFileCount);

public sealed record TextResultPageDto(
    int Offset,
    int Limit,
    int ReturnedEntryCount,
    bool HasPrevious,
    bool HasNext);

public sealed record TextLanguageDto(
    string Language,
    string Label);

public sealed record TextWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<TextEntryRecordDto> Entries,
    IReadOnlyList<DialogueReferenceRecordDto> DialogueReferences,
    IReadOnlyList<TextEditableFieldDto> EditableFields,
    TextWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public IReadOnlyList<TextCategoryDto> Categories { get; init; } = [];

    public string? SelectedCategoryId { get; init; }

    public TextResultPageDto? Page { get; init; }

    public IReadOnlyList<TextLanguageDto> Languages { get; init; } = [];

    public string? SelectedLanguage { get; init; }
}

public sealed record LoadTextWorkflowResponse(TextWorkflowDto Workflow);

public sealed record UpdateTextEntryRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string TextKey,
    string Value,
    TextWorkflowQueryDto? Query = null);

public sealed record UpdateTextEntryResponse(
    TextWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
