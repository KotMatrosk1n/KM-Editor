// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;

namespace KM.SwSh.ShinyRate;

public sealed record SwShShinyRateProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShShinyRateSourceRecord(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    SwShShinyRateProvenance Provenance);

public sealed record SwShShinyRateRule(
    string Mode,
    int? RollCount,
    int MinimumRollCount,
    int MaximumRollCount,
    int MinimumCustomDenominator,
    int MaximumCustomDenominator,
    int? OddsDenominator,
    double ChancePercent,
    string OddsLabel,
    string PercentLabel,
    string RuntimeSummary);

public sealed record SwShShinyRatePreset(
    string PresetId,
    string Label,
    string Mode,
    int? RollCount,
    int? TargetDenominator,
    bool IsEnabled,
    string OddsLabel,
    string PercentLabel,
    string Description);

public sealed record SwShShinyRateWorkflowStats(
    int SourceFileCount,
    int OutputFileCount,
    int PresetCount);

public sealed record SwShShinyRateWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string FunctionOffsetHex,
    string CompareOffsetHex,
    string BreakOffsetHex,
    ProjectGame? DetectedGame,
    SwShShinyRateSourceRecord? Source,
    SwShShinyRateRule RateRule,
    IReadOnlyList<SwShShinyRatePreset> Presets,
    SwShShinyRateWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShShinyRateEditResult(
    SwShShinyRateWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
