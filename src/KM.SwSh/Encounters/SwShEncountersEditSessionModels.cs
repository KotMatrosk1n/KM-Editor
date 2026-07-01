// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Encounters;

public sealed record SwShEncountersEditResult(
    SwShEncountersWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShEncounterSlotFieldUpdate(
    string TableId,
    int Slot,
    string Field,
    string Value);
