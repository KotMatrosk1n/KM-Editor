// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.SwSh.GameModules;
using KM.SwSh.Workflows;

namespace KM.SwSh.BattleCafeRewards;

public sealed record SwShBattleCafeRewardsProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShBattleCafeRewardsItemOption(
    int ItemId,
    string Name,
    string Category);

public sealed record SwShBattleCafeRewardsTotals(
    int DwightPercent,
    int BernardPercent,
    int RichardPercent);

public sealed record SwShBattleCafeRewardsWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShBattleCafeRewardEntry> Rewards,
    IReadOnlyList<SwShBattleCafeRewardsItemOption> ItemOptions,
    SwShBattleCafeRewardsTotals Totals,
    SwShBattleCafeRewardsProvenance? Provenance,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShBattleCafeRewardsRowEdit(
    int RowIndex,
    int ExpectedItemId,
    int ExpectedDwightPercent,
    int ExpectedBernardPercent,
    int ExpectedRichardPercent,
    int ItemId,
    int DwightPercent,
    int BernardPercent,
    int RichardPercent);

public sealed record SwShBattleCafeRewardsEditResult(
    SwShBattleCafeRewardsWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed record SwShBattleCafeRewardsLoadedSource(
    byte[] Bytes,
    IReadOnlyDictionary<int, string> ItemNames,
    IReadOnlyList<SwShBattleCafeRewardEntry> Rewards,
    IReadOnlyList<SwShBattleCafeRewardsItemOption> ItemOptions,
    SwShBattleCafeRewardsProvenance Provenance,
    ProjectFileReference EffectiveSource);
