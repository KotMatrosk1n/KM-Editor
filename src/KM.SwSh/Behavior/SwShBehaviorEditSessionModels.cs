// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Behavior;

public sealed record SwShBehaviorFieldUpdate(
    string EntryId,
    string Field,
    string Value);

public sealed record SwShBehaviorEditResult(
    SwShBehaviorWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
