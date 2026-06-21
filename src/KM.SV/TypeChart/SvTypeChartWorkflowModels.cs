// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Workflows;

namespace KM.SV.TypeChart;

public sealed record SvTypeChartProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvTypeChartSourceRecord(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    SvTypeChartProvenance Provenance);

public sealed record SvTypeChartTypeDefinition(
    int TypeIndex,
    string Label,
    string ShortLabel,
    string Color);

public sealed record SvTypeChartCell(
    int AttackTypeIndex,
    int DefenseTypeIndex,
    int Effectiveness,
    int VanillaEffectiveness);

public sealed record SvTypeChartWorkflowStats(
    int SourceFileCount,
    int OutputFileCount,
    int ChartCellCount);

public sealed record SvTypeChartWorkflow(
    SvWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string ChartOffsetHex,
    ProjectGame? DetectedGame,
    SvTypeChartSourceRecord? Source,
    IReadOnlyList<SvTypeChartTypeDefinition> Types,
    IReadOnlyList<SvTypeChartCell> Cells,
    SvTypeChartWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvTypeChartEditResult(
    SvTypeChartWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
