// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Raids;

public sealed record SwShRaidRewardProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShRaidRewardEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShRaidRewardEditableFieldOption> Options)
{
    public SwShRaidRewardEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShRaidRewardEditableFieldOption>())
    {
    }
}

public sealed record SwShRaidRewardEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShRaidRewardItemRecord(
    int Slot,
    int EntryId,
    int ItemId,
    string ItemName,
    int Quantity,
    int Weight,
    IReadOnlyList<int> Values);

public sealed record SwShRaidRewardTableRecord(
    string TableId,
    string DenId,
    int Rank,
    string GameVersion,
    string RewardKind,
    string RewardKindLabel,
    string ArchiveMember,
    int TableIndex,
    string SourceTableHash,
    IReadOnlyList<SwShRaidRewardItemRecord> Rewards,
    SwShRaidRewardProvenance Provenance);

public sealed record SwShRaidRewardsWorkflowStats(
    int TotalTableCount,
    int TotalRewardItemCount,
    int SourceFileCount);

public sealed record SwShRaidRewardsWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShRaidRewardTableRecord> Tables,
    IReadOnlyList<SwShRaidRewardEditableField> EditableFields,
    SwShRaidRewardsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
