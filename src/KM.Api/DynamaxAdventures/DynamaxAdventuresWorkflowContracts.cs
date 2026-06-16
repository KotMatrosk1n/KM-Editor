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

public sealed record PlanDynamaxAdventureSeedRequest(
    ProjectPathsDto Paths,
    string Seed,
    int NpcCount,
    IReadOnlyList<int> RequiredRows);

public sealed record SearchDynamaxAdventureSeedRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<int> RequiredRows,
    int NpcCount,
    string StartSeed,
    string Limit,
    int MaxResults);

public sealed record SetDynamaxAdventureSaveSeedRequest(
    ProjectPathsDto Paths,
    string Seed);

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

public sealed record DynamaxAdventurePokemonSnapshotDto(
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int Ability,
    string AbilityLabel,
    int GigantamaxState,
    string GigantamaxLabel,
    IReadOnlyList<DynamaxAdventureMoveRecordDto> Moves,
    DynamaxAdventureIvsDto Ivs,
    int GuaranteedPerfectIvs,
    string IvSummary);

public sealed record DynamaxAdventureBossTargetOptionDto(
    int EntryIndex,
    int AdventureIndex,
    int SpeciesId,
    string Species,
    int Form,
    int Version,
    string VersionLabel,
    bool IsStoryProgressGated,
    string Label);

public sealed record DynamaxAdventureRecordDto(
    int EntryIndex,
    bool IsEditable,
    string Label,
    int AdventureIndex,
    int SpeciesId,
    string Species,
    int Form,
    int BossTargetSpeciesId,
    string BossTargetSpecies,
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

    public IReadOnlyList<DynamaxAdventureEditableFieldOptionDto> MoveOptions { get; init; } =
        Array.Empty<DynamaxAdventureEditableFieldOptionDto>();

    public IReadOnlyList<DynamaxAdventureBossTargetOptionDto> BossTargetOptions { get; init; } =
        Array.Empty<DynamaxAdventureBossTargetOptionDto>();

    public DynamaxAdventurePokemonSnapshotDto? VanillaPokemon { get; init; }
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

public sealed record DynamaxAdventureSeedTemplateDto(
    int Row,
    int Species,
    int Form,
    bool IsBoss);

public sealed record DynamaxAdventureSeedRowPositionDto(
    int Row,
    string Kind,
    int Slot);

public sealed record DynamaxAdventureSeedPlanDto(
    string Seed,
    int NpcCount,
    IReadOnlyList<DynamaxAdventureSeedTemplateDto> Rentals,
    IReadOnlyList<DynamaxAdventureSeedTemplateDto> Encounters,
    IReadOnlyList<DynamaxAdventureSeedRowPositionDto> RequiredRowPositions,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record DynamaxAdventureSeedSearchMatchDto(
    string Seed,
    IReadOnlyList<DynamaxAdventureSeedRowPositionDto> Positions);

public sealed record DynamaxAdventureSeedSearchDto(
    int NpcCount,
    string StartSeed,
    string Limit,
    int MaxResults,
    IReadOnlyList<DynamaxAdventureSeedSearchMatchDto> Results,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record DynamaxAdventureSaveSeedDto(
    string? SaveFilePath,
    string? BackupFilePath,
    string? OldSeed,
    string NewSeed,
    bool WasChanged,
    bool ChecksumsValid,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

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
    IReadOnlyList<DynamaxAdventureEditableFieldOptionDto> SafeNormalSpeciesOptions,
    DynamaxAdventuresWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadDynamaxAdventuresWorkflowResponse(DynamaxAdventuresWorkflowDto Workflow);

public sealed record PlanDynamaxAdventureSeedResponse(DynamaxAdventureSeedPlanDto Plan);

public sealed record SearchDynamaxAdventureSeedResponse(DynamaxAdventureSeedSearchDto Search);

public sealed record SetDynamaxAdventureSaveSeedResponse(DynamaxAdventureSaveSeedDto Result);

public sealed record UpdateDynamaxAdventureFieldResponse(
    DynamaxAdventuresWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
