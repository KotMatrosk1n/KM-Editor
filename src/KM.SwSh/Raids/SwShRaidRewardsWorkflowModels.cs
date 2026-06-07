// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Raids;

public sealed record SwShRaidRewardProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShRaidRewardItemRecord(
    int Slot,
    int ItemId,
    string ItemName,
    int Quantity,
    int Weight);

public sealed record SwShRaidRewardTableRecord(
    string TableId,
    string DenId,
    int Rank,
    string GameVersion,
    IReadOnlyList<SwShRaidRewardItemRecord> Rewards,
    SwShRaidRewardProvenance Provenance);

public sealed record SwShRaidRewardsWorkflowStats(
    int TotalTableCount,
    int TotalRewardItemCount,
    int SourceFileCount);

public sealed record SwShRaidRewardsWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShRaidRewardTableRecord> Tables,
    SwShRaidRewardsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
