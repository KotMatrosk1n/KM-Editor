// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.TypeChart;

public sealed record LoadTypeChartWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageTypeChartRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<int> Values);

public sealed record TypeChartProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record TypeChartSourceRecordDto(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    TypeChartProvenanceDto Provenance);

public sealed record TypeChartTypeDefinitionDto(
    int TypeIndex,
    string Label,
    string ShortLabel,
    string Color);

public sealed record TypeChartCellDto(
    int AttackTypeIndex,
    int DefenseTypeIndex,
    int Effectiveness,
    int VanillaEffectiveness);

public sealed record TypeChartWorkflowStatsDto(
    int SourceFileCount,
    int OutputFileCount,
    int ChartCellCount);

public sealed record TypeChartWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string ChartOffsetHex,
    ProjectGameDto? DetectedGame,
    TypeChartSourceRecordDto? Source,
    IReadOnlyList<TypeChartTypeDefinitionDto> Types,
    IReadOnlyList<TypeChartCellDto> Cells,
    TypeChartWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadTypeChartWorkflowResponse(TypeChartWorkflowDto Workflow);

public sealed record StageTypeChartResponse(
    TypeChartWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
