// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Raids;

public sealed record SwShRaidBattleProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShRaidBattleEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShRaidBattleEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShRaidBattleEditableFieldOption> Options);

public sealed record SwShRaidBattleRewardLinkRecord(
    string RewardKind,
    string RewardKindLabel,
    string TableId,
    string SourceTableHash,
    bool IsMatched,
    int RewardItemCount,
    string Preview);

public sealed record SwShRaidBattleSlotRecord(
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
    SwShRaidBattleRewardLinkRecord DropRewardLink,
    SwShRaidBattleRewardLinkRecord BonusRewardLink)
{
    public IReadOnlyList<SwShRaidBattleEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<SwShRaidBattleEditableFieldOption>();

    public IReadOnlyList<SwShRaidBattleEditableFieldOption> FormOptions { get; init; } =
        Array.Empty<SwShRaidBattleEditableFieldOption>();
}

public sealed record SwShRaidBattleTableRecord(
    string TableId,
    string DenId,
    int TableIndex,
    string GameVersion,
    string SourceTableHash,
    IReadOnlyList<SwShRaidBattleSlotRecord> Slots,
    SwShRaidBattleProvenance Provenance);

public sealed record SwShRaidBattlesWorkflowStats(
    int TotalTableCount,
    int TotalSlotCount,
    int GigantamaxSlotCount,
    int SourceFileCount);

public sealed record SwShRaidBattlesWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShRaidBattleTableRecord> Tables,
    IReadOnlyList<SwShRaidBattleEditableField> EditableFields,
    SwShRaidBattlesWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
