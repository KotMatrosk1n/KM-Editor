// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Text;

public sealed record TextWorkflowQueryDto(
    string? SearchText,
    int? Offset,
    int? Limit);

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
    TextProvenanceDto Provenance);

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

public sealed record TextWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<TextEntryRecordDto> Entries,
    IReadOnlyList<DialogueReferenceRecordDto> DialogueReferences,
    IReadOnlyList<TextEditableFieldDto> EditableFields,
    TextWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

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
