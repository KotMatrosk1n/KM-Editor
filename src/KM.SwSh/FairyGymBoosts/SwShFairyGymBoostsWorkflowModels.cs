// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.FairyGymBoosts;

public sealed record SwShFairyGymBoostsProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShFairyGymBoostsSourceRecord(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    SwShFairyGymBoostsProvenance Provenance);

public sealed record SwShFairyGymBoostRecord(
    string BoostId,
    string SequenceFile,
    int AnswerChoice,
    string AnswerText,
    string QuestionText,
    string DefaultResultKind,
    string ResultKind,
    int EffectId,
    string EffectLabel,
    int StageAmount,
    IReadOnlyList<string> AffectedStats);

public sealed record SwShFairyGymBoostTrainer(
    int TrainerId,
    string NpcName,
    int DisplayOrder,
    IReadOnlyList<SwShFairyGymBoostRecord> Boosts);

public sealed record SwShFairyGymBoostsWorkflowStats(
    int TrainerCount,
    int BoostCount,
    int SourceFileCount);

public sealed record SwShFairyGymBoostsWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShFairyGymBoostTrainer> Trainers,
    IReadOnlyList<SwShFairyGymBoostsSourceRecord> Sources,
    SwShFairyGymBoostsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShFairyGymBoostSelection(
    string BoostId,
    int EffectId,
    string ResultKind);

public sealed record SwShFairyGymBoostsEditResult(
    SwShFairyGymBoostsWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed record SwShFairyGymBoostSourceDefinition(
    string SourceId,
    string Label,
    string RelativePath);

internal sealed record SwShFairyGymBoostTrainerDefinition(
    int TrainerId,
    string NpcName,
    int DisplayOrder,
    IReadOnlyList<SwShFairyGymBoostDefinition> Boosts);

internal sealed record SwShFairyGymBoostDefinition(
    string BoostId,
    string SequenceFile,
    int AnswerChoice,
    string AnswerText,
    string QuestionText,
    string ResultKind,
    int EffectId);

internal sealed record SwShFairyGymBoostEffect(
    string Label,
    int StageAmount,
    IReadOnlyList<string> AffectedStats);

internal sealed record SwShFairyGymBoostAnswerSlot(
    int EffectId,
    int ResultValue);

internal sealed record SwShFairyGymBoostAnswerPatch(
    int AnswerChoice,
    int EffectId,
    int ResultValue);
