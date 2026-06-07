// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Text;

public sealed record LoadTextWorkflowRequest(ProjectPathsDto Paths);

public sealed record TextProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record TextEntryRecordDto(
    int TextId,
    string Label,
    string Language,
    string Value,
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

public sealed record TextWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<TextEntryRecordDto> Entries,
    IReadOnlyList<DialogueReferenceRecordDto> DialogueReferences,
    TextWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadTextWorkflowResponse(TextWorkflowDto Workflow);
