// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Moves;

public sealed record LoadMovesWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateMoveFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int MoveId,
    string Field,
    string Value);

public sealed record MoveProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record MoveStatChangeRecordDto(
    int Slot,
    int Stat,
    string StatName,
    int Stage,
    int Percent);

public sealed record MoveFlagRecordDto(
    string Field,
    string Label,
    bool Enabled);

public sealed record MoveEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue);

public sealed record MoveRecordDto(
    int MoveId,
    string Name,
    string? Description,
    uint Version,
    bool CanUseMove,
    int Type,
    string TypeName,
    int Quality,
    int Category,
    string CategoryName,
    int Power,
    int Accuracy,
    int PP,
    int Priority,
    int CritStage,
    int MaxMovePower,
    int Target,
    string TargetName,
    int HitMin,
    int HitMax,
    int TurnMin,
    int TurnMax,
    int Inflict,
    string InflictName,
    int InflictPercent,
    int RawInflictCount,
    int Flinch,
    int EffectSequence,
    int Recoil,
    int RawHealing,
    IReadOnlyList<MoveStatChangeRecordDto> StatChanges,
    IReadOnlyList<MoveFlagRecordDto> Flags,
    MoveProvenanceDto Provenance);

public sealed record MovesWorkflowStatsDto(
    int TotalMoveCount,
    int EnabledMoveCount,
    int SourceFileCount,
    int ActiveFlagCount);

public sealed record MovesWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<MoveRecordDto> Moves,
    IReadOnlyList<MoveEditableFieldDto> EditableFields,
    MovesWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadMovesWorkflowResponse(MovesWorkflowDto Workflow);

public sealed record UpdateMoveFieldResponse(
    MovesWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
