// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.ZA.Trainers;

public sealed record ZaTrainersEditResult(
    ZaTrainersWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaTrainerFieldUpdate(int TrainerId, int? Slot, string Field, string Value);
