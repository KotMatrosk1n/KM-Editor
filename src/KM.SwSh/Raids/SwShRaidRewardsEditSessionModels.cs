// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Raids;

public sealed record SwShRaidRewardsEditResult(
    SwShRaidRewardsWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShRaidRewardFieldUpdate(
    string TableId,
    int Slot,
    string Field,
    string Value);
