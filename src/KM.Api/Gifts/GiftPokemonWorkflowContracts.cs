// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Gifts;

public sealed record LoadGiftPokemonWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateGiftPokemonFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int GiftIndex,
    string Field,
    string Value);

public sealed record GiftPokemonProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record GiftPokemonIvsDto(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record GiftPokemonRecordDto(
    int GiftIndex,
    string Label,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    bool IsEgg,
    int HeldItemId,
    string? HeldItem,
    int BallItemId,
    string BallItem,
    int Ability,
    string AbilityLabel,
    int Nature,
    string NatureLabel,
    int Gender,
    string GenderLabel,
    int ShinyLock,
    string ShinyLockLabel,
    int DynamaxLevel,
    bool CanGigantamax,
    int SpecialMoveId,
    string? SpecialMove,
    GiftPokemonIvsDto Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    GiftPokemonProvenanceDto Provenance);

public sealed record GiftPokemonEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<GiftPokemonEditableFieldOptionDto> Options);

public sealed record GiftPokemonEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record GiftPokemonWorkflowStatsDto(
    int TotalGiftCount,
    int EggGiftCount,
    int FixedIvGiftCount,
    int SourceFileCount);

public sealed record GiftPokemonWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<GiftPokemonRecordDto> Gifts,
    IReadOnlyList<GiftPokemonEditableFieldDto> EditableFields,
    GiftPokemonWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadGiftPokemonWorkflowResponse(GiftPokemonWorkflowDto Workflow);

public sealed record UpdateGiftPokemonFieldResponse(
    GiftPokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
