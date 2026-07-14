// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Moves;

public sealed record SwShMoveFieldUpdate(
    int MoveId,
    string Field,
    string Value);

public sealed record SwShMovesEditResult(
    SwShMovesWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
