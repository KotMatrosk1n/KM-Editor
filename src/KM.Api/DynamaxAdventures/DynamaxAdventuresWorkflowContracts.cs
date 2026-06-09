// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.DynamaxAdventures;

public sealed record LoadDynamaxAdventuresWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateDynamaxAdventureFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int EntryIndex,
    string Field,
    string Value);

public sealed record DynamaxAdventureProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record DynamaxAdventureMoveRecordDto(
    int Slot,
    int MoveId,
    string Move);

public sealed record DynamaxAdventureIvsDto(
    int Hp,
    int Attack,
    int Defense,
    int Speed,
    int SpecialAttack,
    int SpecialDefense);

public sealed record DynamaxAdventureRecordDto(
    int EntryIndex,
    string Label,
    int AdventureIndex,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int BallItemId,
    string BallItem,
    int Ability,
    string AbilityLabel,
    int GigantamaxState,
    string GigantamaxLabel,
    int Version,
    string VersionLabel,
    int ShinyRoll,
    string ShinyRollLabel,
    bool IsSingleCapture,
    string SingleCaptureFlagBlock,
    bool IsStoryProgressGated,
    string UiMessageId,
    int OtGender,
    string OtGenderLabel,
    IReadOnlyList<DynamaxAdventureMoveRecordDto> Moves,
    DynamaxAdventureIvsDto Ivs,
    int GuaranteedPerfectIvs,
    string IvSummary,
    DynamaxAdventureProvenanceDto Provenance)
{
    public IReadOnlyList<DynamaxAdventureEditableFieldOptionDto> AbilityOptions { get; init; } =
        Array.Empty<DynamaxAdventureEditableFieldOptionDto>();
}

public sealed record DynamaxAdventureEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<DynamaxAdventureEditableFieldOptionDto> Options);

public sealed record DynamaxAdventureEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record DynamaxAdventuresWorkflowStatsDto(
    int TotalEncounterCount,
    int SingleCaptureCount,
    int StoryGatedCount,
    int GuaranteedPerfectIvEncounterCount,
    int SourceFileCount);

public sealed record DynamaxAdventuresWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<DynamaxAdventureRecordDto> Encounters,
    IReadOnlyList<DynamaxAdventureEditableFieldDto> EditableFields,
    DynamaxAdventuresWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadDynamaxAdventuresWorkflowResponse(DynamaxAdventuresWorkflowDto Workflow);

public sealed record UpdateDynamaxAdventureFieldResponse(
    DynamaxAdventuresWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
