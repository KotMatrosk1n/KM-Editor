// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Items;

public sealed record SwShItemsEditResult(
    SwShItemsWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShEditSessionValidation(
    EditSession Session,
    bool IsValid,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
