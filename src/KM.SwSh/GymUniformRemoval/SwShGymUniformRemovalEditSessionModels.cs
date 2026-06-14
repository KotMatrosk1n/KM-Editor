// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.GymUniformRemoval;

public sealed record SwShGymUniformRemovalEditResult(
    SwShGymUniformRemovalWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
