// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.HyperTraining;

public sealed record LoadHyperTrainingWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageHyperTrainingRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int MinimumLevel);

public sealed record HyperTrainingProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record HyperTrainingSourceRecordDto(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    HyperTrainingProvenanceDto Provenance);

public sealed record HyperTrainingLevelRuleDto(
    int MinimumLevel,
    int ScriptMinimumLevel,
    int RuntimeMinimumLevel,
    int? DialogueMinimumLevel,
    bool LevelsMatch,
    int VanillaMinimumLevel,
    int MinimumAllowedLevel,
    int MaximumAllowedLevel,
    string ScriptCell,
    string DialogueSummary,
    string RuntimeSummary);

public sealed record HyperTrainingWorkflowStatsDto(
    int SourceFileCount,
    int OutputFileCount);

public sealed record HyperTrainingWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    ProjectGameDto? DetectedGame,
    HyperTrainingLevelRuleDto LevelRule,
    IReadOnlyList<HyperTrainingSourceRecordDto> Sources,
    HyperTrainingWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadHyperTrainingWorkflowResponse(HyperTrainingWorkflowDto Workflow);

public sealed record StageHyperTrainingResponse(
    HyperTrainingWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
