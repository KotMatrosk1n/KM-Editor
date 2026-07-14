// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.StaticEncounters;

public sealed record SwShStaticEncounterFieldUpdate(
    int EncounterIndex,
    string Field,
    string Value,
    string? ExpectedEncounterId = null);

public sealed record SwShStaticEncountersEditResult(
    SwShStaticEncountersWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
