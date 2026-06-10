// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Behavior;

public sealed record LoadBehaviorWorkflowRequest(ProjectPathsDto Paths);

public sealed record BehaviorProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record BehaviorFieldOptionDto(
    string Value,
    string Label);

public sealed record BehaviorFieldDto(
    string Field,
    string Label,
    string Group,
    string ValueKind,
    double MinimumValue,
    double MaximumValue,
    bool IsReadOnly,
    string Description,
    IReadOnlyList<BehaviorFieldOptionDto> Options);

public sealed record BehaviorFieldValueDto(
    string Field,
    string Value);

public sealed record BehaviorEntryRecordDto(
    string EntryId,
    int Index,
    string Label,
    int SpeciesId,
    string SpeciesName,
    int Form,
    string Behavior,
    string BehaviorLabel,
    string ModelPart,
    double HitboxRadius,
    double GrassShakeRadius,
    string Hash1,
    string Hash2,
    string InternalSpeciesName,
    IReadOnlyList<BehaviorFieldValueDto> Fields,
    BehaviorProvenanceDto Provenance);

public sealed record BehaviorWorkflowStatsDto(
    int TotalEntryCount,
    int TotalBehaviorCount,
    int SourceFileCount);

public sealed record BehaviorWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<BehaviorEntryRecordDto> Entries,
    IReadOnlyList<BehaviorFieldDto> Fields,
    BehaviorWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadBehaviorWorkflowResponse(BehaviorWorkflowDto Workflow);

public sealed record UpdateBehaviorEntryFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string EntryId,
    string Field,
    string Value);

public sealed record UpdateBehaviorEntryFieldResponse(
    BehaviorWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
