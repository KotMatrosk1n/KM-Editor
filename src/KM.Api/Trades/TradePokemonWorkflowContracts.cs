// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;
using System.Text.Json.Serialization;

namespace KM.Api.Trades;

public sealed record LoadTradePokemonWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateTradePokemonFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int TradeIndex,
    string Field,
    string Value);

public sealed record TradePokemonFieldUpdateDto(
    int TradeIndex,
    string Field,
    string Value);

public sealed record UpdateTradePokemonFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<TradePokemonFieldUpdateDto> Updates);

public sealed record TradePokemonProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record TradePokemonIvsDto(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record TradePokemonRecordDto(
    int TradeIndex,
    string Label,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
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
    int RequiredSpeciesId,
    string RequiredSpecies,
    int RequiredForm,
    int RequiredNature,
    string RequiredNatureLabel,
    int UnknownRequirement,
    int TrainerId,
    int OtGender,
    string OtGenderLabel,
    int MemoryCode,
    int MemoryTextVariable,
    int MemoryFeel,
    int MemoryIntensity,
    int Field03,
    string Hash0,
    string Hash1,
    string Hash2,
    IReadOnlyList<TradePokemonMoveRecordDto> RelearnMoves,
    TradePokemonIvsDto Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    TradePokemonProvenanceDto Provenance)
{
    public string EditorFamily { get; init; } = "swsh";
    public string? EventLabel { get; init; }
    public IReadOnlyList<TradePokemonMoveRecordDto> Moves { get; init; } =
        Array.Empty<TradePokemonMoveRecordDto>();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TeraType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TeraTypeLabel { get; init; }
    public int? ScaleMode { get; init; }
    public string? ScaleModeLabel { get; init; }
    public int? ScaleValue { get; init; }
    public IReadOnlyList<TradePokemonEditableFieldOptionDto> AbilityOptions { get; init; } =
        Array.Empty<TradePokemonEditableFieldOptionDto>();
    public IReadOnlyList<TradePokemonEditableFieldOptionDto> GenderOptions { get; init; } =
        Array.Empty<TradePokemonEditableFieldOptionDto>();
}

public sealed record TradePokemonMoveRecordDto(
    int Slot,
    int MoveId,
    string? Move);

public sealed record TradePokemonEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<TradePokemonEditableFieldOptionDto> Options);

public sealed record TradePokemonEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record TradePokemonWorkflowStatsDto(
    int TotalTradeCount,
    int FixedIvTradeCount,
    int SourceFileCount);

public sealed record TradePokemonWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<TradePokemonRecordDto> Trades,
    IReadOnlyList<TradePokemonEditableFieldDto> EditableFields,
    TradePokemonWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public string EditorFamily { get; init; } = "swsh";
}

public sealed record LoadTradePokemonWorkflowResponse(TradePokemonWorkflowDto Workflow);

public sealed record UpdateTradePokemonFieldResponse(
    TradePokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdateTradePokemonFieldsResponse(
    TradePokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
