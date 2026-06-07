// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Encounters;

public sealed record LoadEncountersWorkflowRequest(ProjectPathsDto Paths);

public sealed record EncounterProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record EncounterSlotRecordDto(
    int Slot,
    string Species,
    int LevelMin,
    int LevelMax,
    int Weight,
    string? TimeOfDay,
    string Weather);

public sealed record EncounterTableRecordDto(
    string TableId,
    string Location,
    string Area,
    string EncounterType,
    string GameVersion,
    IReadOnlyList<EncounterSlotRecordDto> Slots,
    EncounterProvenanceDto Provenance);

public sealed record EncountersWorkflowStatsDto(
    int TotalTableCount,
    int TotalSlotCount,
    int SourceFileCount);

public sealed record EncountersWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<EncounterTableRecordDto> Tables,
    EncountersWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadEncountersWorkflowResponse(EncountersWorkflowDto Workflow);
