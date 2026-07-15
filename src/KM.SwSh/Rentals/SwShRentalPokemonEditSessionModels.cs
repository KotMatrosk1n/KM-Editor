// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Rentals;

public sealed record SwShRentalPokemonFieldUpdate(
    int RentalIndex,
    string Field,
    string Value);

public sealed record SwShRentalPokemonEditResult(
    SwShRentalPokemonWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
