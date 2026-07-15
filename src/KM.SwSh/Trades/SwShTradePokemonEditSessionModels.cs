// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Trades;

public sealed record SwShTradePokemonFieldUpdate(
    int TradeIndex,
    string Field,
    string Value);

public sealed record SwShTradePokemonEditResult(
    SwShTradePokemonWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
