// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Placement;

public sealed record SwShPlacementEditResult(
    SwShPlacementWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    IReadOnlyList<SwShPlacedObjectRecord>? UpdatedObjects = null);

public sealed record SwShPlacementObjectFieldUpdate(
    string ObjectId,
    string Field,
    string Value);
