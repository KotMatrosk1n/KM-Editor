// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;

namespace KM.SwSh.TypeChart;

public sealed record SwShTypeChartProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShTypeChartSourceRecord(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    SwShTypeChartProvenance Provenance);

public sealed record SwShTypeChartTypeDefinition(
    int TypeIndex,
    string Label,
    string ShortLabel,
    string Color);

public sealed record SwShTypeChartCell(
    int AttackTypeIndex,
    int DefenseTypeIndex,
    int Effectiveness,
    int VanillaEffectiveness);

public sealed record SwShTypeChartWorkflowStats(
    int SourceFileCount,
    int OutputFileCount,
    int ChartCellCount);

public sealed record SwShTypeChartWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string ChartOffsetHex,
    ProjectGame? DetectedGame,
    SwShTypeChartSourceRecord? Source,
    IReadOnlyList<SwShTypeChartTypeDefinition> Types,
    IReadOnlyList<SwShTypeChartCell> Cells,
    SwShTypeChartWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShTypeChartEditResult(
    SwShTypeChartWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
