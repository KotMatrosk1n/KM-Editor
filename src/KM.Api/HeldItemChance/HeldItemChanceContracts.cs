// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.HeldItemChance;

public sealed record LoadHeldItemChanceRequest(ProjectPathsDto Paths);
public sealed record StageHeldItemChanceRequest(ProjectPathsDto Paths, IReadOnlyList<int>? Rates = null, EditSessionDto? Session = null, IReadOnlyList<HeldItemChanceUpdateDto>? Pokemon = null);
public sealed record HeldItemChanceWorkflowDto(bool CanEdit, ProjectGameDto? DetectedGame,
    IReadOnlyList<int> Rates, string SourceLayer, IReadOnlyList<ApiDiagnostic> Diagnostics,
    IReadOnlyList<HeldItemChancePokemonDto> Pokemon, IReadOnlyList<HeldItemChanceOptionDto> ItemOptions);
public sealed record HeldItemChancePokemonDto(int PersonalId, int Species, int Form, string Name,
    string FormLabel, string Type1, string Type2, IReadOnlyList<int> Items, IReadOnlyList<int> Rates, bool CustomRates);
public sealed record HeldItemChanceOptionDto(int Value, string Label);
public sealed record HeldItemChanceUpdateDto(int PersonalId, IReadOnlyList<int> Items, IReadOnlyList<int>? Rates);
public sealed record LoadHeldItemChanceResponse(HeldItemChanceWorkflowDto Workflow);
public sealed record StageHeldItemChanceResponse(HeldItemChanceWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
