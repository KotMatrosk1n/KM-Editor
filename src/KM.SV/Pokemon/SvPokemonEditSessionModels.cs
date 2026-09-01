// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SV.Pokemon;

public sealed record SvPokemonEditResult(
    SvPokemonWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvPokemonLearnsetUpdate(
    int PersonalId,
    string Action,
    int? Slot,
    int? MoveId,
    int? Level);

public sealed record SvPokemonEvolutionUpdate(
    int PersonalId,
    string Action,
    int? Slot,
    int? Method,
    int? Argument,
    int? Species,
    int? Form,
    int? Level);
