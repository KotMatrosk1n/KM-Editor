// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.FairyGymBoosts;

public sealed record LoadFairyGymBoostsWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageFairyGymBoostsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<FairyGymBoostSelectionDto> Selections);

public sealed record FairyGymBoostSelectionDto(
    string BoostId,
    int EffectId,
    string ResultKind);

public sealed record FairyGymBoostsProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record FairyGymBoostsSourceRecordDto(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    FairyGymBoostsProvenanceDto Provenance);

public sealed record FairyGymBoostRecordDto(
    string BoostId,
    string SequenceFile,
    int AnswerChoice,
    string AnswerText,
    string QuestionText,
    int DefaultEffectId,
    string DefaultResultKind,
    string ResultKind,
    int EffectId,
    string EffectLabel,
    int StageAmount,
    IReadOnlyList<string> AffectedStats);

public sealed record FairyGymBoostTrainerDto(
    int TrainerId,
    string NpcName,
    int DisplayOrder,
    IReadOnlyList<FairyGymBoostRecordDto> Boosts);

public sealed record FairyGymBoostsWorkflowStatsDto(
    int TrainerCount,
    int BoostCount,
    int SourceFileCount);

public sealed record FairyGymBoostsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<FairyGymBoostTrainerDto> Trainers,
    IReadOnlyList<FairyGymBoostsSourceRecordDto> Sources,
    FairyGymBoostsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadFairyGymBoostsWorkflowResponse(FairyGymBoostsWorkflowDto Workflow);

public sealed record StageFairyGymBoostsResponse(
    FairyGymBoostsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
