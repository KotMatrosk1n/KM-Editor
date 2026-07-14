// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;
using System.Text.Json.Serialization;

namespace KM.Api.StaticEncounters;

public sealed record LoadStaticEncountersWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateStaticEncounterFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int EncounterIndex,
    string Field,
    string Value,
    string? EncounterId = null);

public sealed record StaticEncounterFieldUpdateDto(
    int EncounterIndex,
    string Field,
    string Value,
    string? EncounterId = null);

public sealed record UpdateStaticEncounterFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<StaticEncounterFieldUpdateDto> Updates);

public sealed record StaticEncounterProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record StaticEncounterStatsDto(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record StaticEncounterMoveDto(
    int Slot,
    int MoveId,
    string? Move);

public sealed record StaticEncounterRecordDto(
    int EncounterIndex,
    string Label,
    string EncounterId,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int HeldItemId,
    string? HeldItem,
    int Ability,
    string AbilityLabel,
    int Nature,
    string NatureLabel,
    int Gender,
    string GenderLabel,
    int ShinyLock,
    string ShinyLockLabel,
    int EncounterScenario,
    string EncounterScenarioLabel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? DynamaxLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CanGigantamax,
    StaticEncounterStatsDto Evs,
    StaticEncounterStatsDto Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    IReadOnlyList<StaticEncounterMoveDto> Moves,
    StaticEncounterProvenanceDto Provenance)
{
    public string EditorFamily { get; init; } = "swsh";
    public string? CategoryId { get; init; }
    public string? CategoryLabel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScenarioDetails { get; init; }
    public IReadOnlyList<string> SupportedFields { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> FieldValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> FieldDisplayValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, bool> FieldReadOnly { get; init; } =
        new Dictionary<string, bool>(StringComparer.Ordinal);
    public IReadOnlyList<StaticEncounterEditableFieldOptionDto> AbilityOptions { get; init; } =
        Array.Empty<StaticEncounterEditableFieldOptionDto>();
    public IReadOnlyList<StaticEncounterEditableFieldOptionDto> GenderOptions { get; init; } =
        Array.Empty<StaticEncounterEditableFieldOptionDto>();
}

public sealed record StaticEncounterEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<StaticEncounterEditableFieldOptionDto> Options)
{
    public string? Group { get; init; }
    public bool IsReadOnly { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed record StaticEncounterEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record StaticEncountersWorkflowStatsDto(
    int TotalEncounterCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? GigantamaxEncounterCount,
    int FixedIvEncounterCount,
    int SourceFileCount)
{
    public int FixedSymbolCount { get; init; }
    public int CoinSymbolCount { get; init; }
}

public sealed record StaticEncountersWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<StaticEncounterRecordDto> Encounters,
    IReadOnlyList<StaticEncounterEditableFieldDto> EditableFields,
    StaticEncountersWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public string EditorFamily { get; init; } = "swsh";
}

public sealed record LoadStaticEncountersWorkflowResponse(StaticEncountersWorkflowDto Workflow);

public sealed record UpdateStaticEncounterFieldResponse(
    StaticEncountersWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
