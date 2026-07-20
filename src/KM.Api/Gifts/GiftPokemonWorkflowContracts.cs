// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;
using System.Text.Json.Serialization;

namespace KM.Api.Gifts;

public sealed record LoadGiftPokemonWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateGiftPokemonFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int GiftIndex,
    string Field,
    string Value);

public sealed record GiftPokemonFieldUpdateDto(
    int GiftIndex,
    string Field,
    string Value);

public sealed record UpdateGiftPokemonFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<GiftPokemonFieldUpdateDto> Updates);

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

public sealed record GiftPokemonMoveDto(
    int Slot,
    int MoveId,
    string? Move,
    int PointUps);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? DynamaxLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CanGigantamax,
    int SpecialMoveId,
    string? SpecialMove,
    GiftPokemonIvsDto Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    GiftPokemonProvenanceDto Provenance)
{
    public string EditorFamily { get; init; } = "swsh";

    public IReadOnlyList<GiftPokemonEditableFieldOptionDto> AbilityOptions { get; init; } =
        Array.Empty<GiftPokemonEditableFieldOptionDto>();

    public IReadOnlyList<GiftPokemonEditableFieldOptionDto> FormOptions { get; init; } =
        Array.Empty<GiftPokemonEditableFieldOptionDto>();

    public IReadOnlyList<GiftPokemonEditableFieldOptionDto> GenderOptions { get; init; } =
        Array.Empty<GiftPokemonEditableFieldOptionDto>();

    public string? EventLabel { get; init; }

    public IReadOnlyList<GiftPokemonMoveDto> Moves { get; init; } =
        Array.Empty<GiftPokemonMoveDto>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TeraType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TeraTypeLabel { get; init; }

    public int? ScaleMode { get; init; }

    public string? ScaleModeLabel { get; init; }

    public int? ScaleValue { get; init; }
}

public sealed record GiftPokemonEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<GiftPokemonEditableFieldOptionDto> Options);

public sealed record GiftPokemonEditableFieldOptionDto(
    int Value,
    string Label)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GiftPokemonEditableFieldOptionDto>? FormOptions { get; init; }
}

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
    IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public string EditorFamily { get; init; } = "swsh";
}

public sealed record LoadGiftPokemonWorkflowResponse(GiftPokemonWorkflowDto Workflow);

public sealed record UpdateGiftPokemonFieldResponse(
    GiftPokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdateGiftPokemonFieldsResponse(
    GiftPokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
