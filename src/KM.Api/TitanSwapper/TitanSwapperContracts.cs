// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;
namespace KM.Api.TitanSwapper;

public sealed record TitanSwapperRowDto(string Id, int StorySpecies, int Phase, string Edition, IReadOnlyDictionary<string, int> Values);
public sealed record TitanSpeciesOptionDto(int Value, string Label);
public sealed record TitanSwapperWorkflowDto(WorkflowSummaryDto Summary, string SourceRevision,
    IReadOnlyList<TitanSwapperRowDto> Rows, IReadOnlyList<TitanSpeciesOptionDto> SpeciesOptions, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record TitanSwapperUpdateDto(string RowId, string Field, int Value);
public sealed record LoadTitanSwapperRequest(ProjectPathsDto Paths, EditSessionDto? Session);
public sealed record LoadTitanSwapperResponse(TitanSwapperWorkflowDto Workflow);
public sealed record StageTitanSwapperRequest(ProjectPathsDto Paths, EditSessionDto? Session, string SourceRevision, IReadOnlyList<TitanSwapperUpdateDto> Updates);
public sealed record StageTitanSwapperResponse(TitanSwapperWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
