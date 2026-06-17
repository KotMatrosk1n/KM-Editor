// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.ShinyRate;

public sealed record LoadShinyRateWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageShinyRateRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string Mode,
    int? RollCount);

public sealed record ShinyRateProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record ShinyRateSourceRecordDto(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    ShinyRateProvenanceDto Provenance);

public sealed record ShinyRateRuleDto(
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

public sealed record ShinyRatePresetDto(
    string PresetId,
    string Label,
    string Mode,
    int? RollCount,
    int? TargetDenominator,
    bool IsEnabled,
    string OddsLabel,
    string PercentLabel,
    string Description);

public sealed record ShinyRateWorkflowStatsDto(
    int SourceFileCount,
    int OutputFileCount,
    int PresetCount);

public sealed record ShinyRateWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string FunctionOffsetHex,
    string CompareOffsetHex,
    string BreakOffsetHex,
    ProjectGameDto? DetectedGame,
    ShinyRateSourceRecordDto? Source,
    ShinyRateRuleDto RateRule,
    IReadOnlyList<ShinyRatePresetDto> Presets,
    ShinyRateWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadShinyRateWorkflowResponse(ShinyRateWorkflowDto Workflow);

public sealed record StageShinyRateResponse(
    ShinyRateWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
