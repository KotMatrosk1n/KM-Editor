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
    long Value,
    string Label);

public sealed record SwShRaidRewardItemRecord(
    int Slot,
    long EntryId,
    long ItemId,
    string ItemName,
    long Quantity,
    long Weight,
    IReadOnlyList<long> Values);

public sealed record SwShRaidRewardTableRecord(
    string TableId,
    string DisplayName,
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
