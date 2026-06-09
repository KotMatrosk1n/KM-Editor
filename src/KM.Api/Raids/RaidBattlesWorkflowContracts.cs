// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Raids;

public sealed record LoadRaidBattlesWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateRaidBattleSlotFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string TableId,
    int Slot,
    string Field,
    string Value);

public sealed record UpdateRaidBattleSlotFieldResponse(
    RaidBattlesWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record RaidBattleProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record RaidBattleEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record RaidBattleEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<RaidBattleEditableFieldOptionDto> Options);

public sealed record RaidBattleRewardLinkDto(
    string RewardKind,
    string RewardKindLabel,
    string TableId,
    string SourceTableHash,
    bool IsMatched,
    int RewardItemCount,
    string Preview);

public sealed record RaidBattleSlotRecordDto(
    int Slot,
    int EntryIndex,
    int SpeciesId,
    string Species,
    int Form,
    int Ability,
    string AbilityLabel,
    bool IsGigantamax,
    int Gender,
    string GenderLabel,
    int FlawlessIvs,
    IReadOnlyList<int> Probabilities,
    string ProbabilitySummary,
    string LevelTableHash,
    string DropTableHash,
    string BonusTableHash,
    RaidBattleRewardLinkDto DropRewardLink,
    RaidBattleRewardLinkDto BonusRewardLink)
{
    public IReadOnlyList<RaidBattleEditableFieldOptionDto> AbilityOptions { get; init; } =
        Array.Empty<RaidBattleEditableFieldOptionDto>();
}

public sealed record RaidBattleTableRecordDto(
    string TableId,
    string DenId,
    int TableIndex,
    string GameVersion,
    string SourceTableHash,
    IReadOnlyList<RaidBattleSlotRecordDto> Slots,
    RaidBattleProvenanceDto Provenance);

public sealed record RaidBattlesWorkflowStatsDto(
    int TotalTableCount,
    int TotalSlotCount,
    int GigantamaxSlotCount,
    int SourceFileCount);

public sealed record RaidBattlesWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<RaidBattleTableRecordDto> Tables,
    IReadOnlyList<RaidBattleEditableFieldDto> EditableFields,
    RaidBattlesWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadRaidBattlesWorkflowResponse(RaidBattlesWorkflowDto Workflow);
