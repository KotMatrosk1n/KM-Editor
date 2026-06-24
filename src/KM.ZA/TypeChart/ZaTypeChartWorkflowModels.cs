// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Workflows;

namespace KM.ZA.TypeChart;

public sealed record ZaTypeChartProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaTypeChartSourceRecord(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    ZaTypeChartProvenance Provenance);

public sealed record ZaTypeChartTypeDefinition(
    int TypeIndex,
    string Label,
    string ShortLabel,
    string Color);

public sealed record ZaTypeChartCell(
    int AttackTypeIndex,
    int DefenseTypeIndex,
    int Effectiveness,
    int VanillaEffectiveness);

public sealed record ZaTypeChartWorkflowStats(
    int SourceFileCount,
    int OutputFileCount,
    int ChartCellCount);

public sealed record ZaTypeChartWorkflow(
    ZaWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string ChartOffsetHex,
    ProjectGame? DetectedGame,
    ZaTypeChartSourceRecord? Source,
    IReadOnlyList<ZaTypeChartTypeDefinition> Types,
    IReadOnlyList<ZaTypeChartCell> Cells,
    ZaTypeChartWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaTypeChartEditResult(
    ZaTypeChartWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
