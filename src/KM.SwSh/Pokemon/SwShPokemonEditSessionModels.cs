// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Pokemon;

public sealed record SwShPokemonEditResult(
    SwShPokemonWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShPokemonFieldUpdate(int PersonalId, string Field, string Value);

public sealed record SwShPokemonLearnsetUpdate(
    int PersonalId,
    string Action,
    int? Slot,
    int? MoveId,
    int? Level);

public sealed record SwShPokemonEvolutionUpdate(
    int PersonalId,
    string Action,
    int? Slot,
    int? Method,
    int? Argument,
    int? Species,
    int? Form,
    int? Level);
