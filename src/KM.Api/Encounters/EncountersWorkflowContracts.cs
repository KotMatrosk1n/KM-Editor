// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Encounters;

public sealed record LoadEncountersWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateEncounterSlotFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string TableId,
    int Slot,
    string Field,
    string Value);

public sealed record EncounterSlotFieldUpdateDto(
    string TableId,
    int Slot,
    string Field,
    string Value);

public sealed record UpdateEncounterSlotFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<EncounterSlotFieldUpdateDto> Updates);

public sealed record EncounterProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record EncounterSlotRecordDto(
    int Slot,
    int SpeciesId,
    string Species,
    int Form,
    int LevelMin,
    int LevelMax,
    int Weight,
    string? TimeOfDay,
    string Weather,
    string? EncounterDataId = null,
    string? EncounterKind = null,
    bool IsAlpha = false,
    string? EncounterRecordId = null,
    bool? ContributesToWildZoneCompletion = null);

public sealed record EncounterTableRecordDto(
    string TableId,
    string Location,
    string Area,
    string EncounterType,
    string GameVersion,
    string ArchiveMember,
    IReadOnlyList<EncounterSlotRecordDto> Slots,
    EncounterProvenanceDto Provenance,
    string? LocationKey = null,
    int? LocationSort = null,
    string? TableLabel = null,
    string? TableDetails = null);

public sealed record EncounterEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<EncounterEditableFieldOptionDto> Options);

public sealed record EncounterEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record EncountersWorkflowStatsDto(
    int TotalTableCount,
    int TotalSlotCount,
    int SourceFileCount);

public sealed record EncountersWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<EncounterTableRecordDto> Tables,
    IReadOnlyList<EncounterEditableFieldDto> EditableFields,
    EncountersWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadEncountersWorkflowResponse(EncountersWorkflowDto Workflow);

public sealed record UpdateEncounterSlotFieldResponse(
    EncountersWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdateEncounterSlotFieldsResponse(
    EncountersWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
