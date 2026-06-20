// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SV.Gifts;

public sealed record SvGiftPokemonEditResult(
    SvGiftPokemonWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
