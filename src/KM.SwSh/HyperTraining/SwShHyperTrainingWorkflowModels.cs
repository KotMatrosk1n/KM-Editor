// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.HyperTraining;

public sealed record SwShHyperTrainingProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShHyperTrainingSourceRecord(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    SwShHyperTrainingProvenance Provenance);

public sealed record SwShHyperTrainingLevelRule(
    int MinimumLevel,
    int VanillaMinimumLevel,
    int MinimumAllowedLevel,
    int MaximumAllowedLevel,
    string ScriptCell,
    string DialogueSummary,
    string RuntimeSummary);

public sealed record SwShHyperTrainingWorkflowStats(
    int SourceFileCount,
    int OutputFileCount);

public sealed record SwShHyperTrainingWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    SwShHyperTrainingLevelRule LevelRule,
    IReadOnlyList<SwShHyperTrainingSourceRecord> Sources,
    SwShHyperTrainingWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
