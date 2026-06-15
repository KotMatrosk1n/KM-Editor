// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.HyperTraining;
using KM.SwSh.Workflows;

namespace KM.SwSh.TypeChart;

public sealed class SwShTypeChartWorkflowService
{
    public const string ExeFsMainPath = "exefs/main";

    private static readonly SwShTypeChartTypeDefinition[] TypeDefinitions =
    [
        new(0, "Normal", "NOR", "#A8A878"),
        new(1, "Fire", "FIR", "#F05030"),
        new(2, "Water", "WAT", "#6890F0"),
        new(3, "Electric", "ELE", "#F8D030"),
        new(4, "Grass", "GRA", "#78C850"),
        new(5, "Ice", "ICE", "#78C8F0"),
        new(6, "Fighting", "FIG", "#A05038"),
        new(7, "Poison", "POI", "#A040A0"),
        new(8, "Ground", "GRO", "#E0C068"),
        new(9, "Flying", "FLY", "#8080F0"),
        new(10, "Psychic", "PSY", "#F85888"),
        new(11, "Bug", "BUG", "#A8B820"),
        new(12, "Rock", "ROC", "#B8A038"),
        new(13, "Ghost", "GHO", "#6060B0"),
        new(14, "Dragon", "DRA", "#7038F8"),
        new(15, "Dark", "DAR", "#705848"),
        new(16, "Steel", "STE", "#B8B8D0"),
        new(17, "Fairy", "FAI", "#EE99EE"),
    ];

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Type Chart requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShTypeChartWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                "disabled",
                "Type Chart cannot load until project paths validate.",
                CreateDefaultAnalysis(),
                source: null,
                diagnostics);
        }

        var mainSource = ResolveWorkflowFile(project, ExeFsMainPath);
        if (mainSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main is missing. Type Chart needs it to inspect the effectiveness table.",
                file: ExeFsMainPath,
                expected: "exefs/main"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Type Chart cannot inspect effectiveness values because exefs/main is missing.",
                CreateDefaultAnalysis(),
                CreateMissingSource(),
                diagnostics);
        }

        var source = CreateSource(mainSource.Entry, "available");
        try
        {
            var analysis = SwShTypeChartMainPatcher.Analyze(
                File.ReadAllBytes(mainSource.AbsolutePath),
                project.Paths.SelectedGame);

            if (analysis.Kind is SwShTypeChartMainKind.UnsupportedBuild
                or SwShTypeChartMainKind.GameMismatch
                or SwShTypeChartMainKind.MissingChart
                or SwShTypeChartMainKind.AmbiguousChart
                or SwShTypeChartMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: ExeFsMainPath,
                    expected: "Selected-game Sword/Shield 1.3.2 exefs/main with one legal 18x18 type chart table"));
            }

            var isBlocked = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                || analysis.Kind is SwShTypeChartMainKind.UnsupportedBuild
                    or SwShTypeChartMainKind.GameMismatch
                    or SwShTypeChartMainKind.MissingChart
                    or SwShTypeChartMainKind.AmbiguousChart
                    or SwShTypeChartMainKind.Conflict;
            var installStatus = isBlocked
                ? "blocked"
                : analysis.Kind == SwShTypeChartMainKind.Modified
                    ? "modified"
                    : summary.Availability == SwShWorkflowAvailability.Available
                        ? "available"
                        : "readOnly";

            return CreateWorkflow(
                summary,
                installStatus,
                analysis.Message,
                analysis,
                source,
                diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable Sword/Shield exefs/main"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Type Chart cannot inspect effectiveness values because exefs/main could not be read.",
                CreateDefaultAnalysis(),
                source,
                diagnostics);
        }
    }

    internal static SwShHyperTrainingWorkflowService.WorkflowFileSource? ResolveWorkflowFile(
        OpenedProject project,
        string relativePath)
    {
        return SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, relativePath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        return SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, targetRelativePath);
    }

    public static IReadOnlyList<SwShTypeChartTypeDefinition> Types => TypeDefinitions;

    private static SwShTypeChartWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        SwShTypeChartMainAnalysis analysis,
        SwShTypeChartSourceRecord? source,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShTypeChartWorkflow(
            summary,
            installStatus,
            installMessage,
            analysis.BuildId,
            analysis.ChartOffsetHex,
            analysis.DetectedGame,
            source,
            TypeDefinitions,
            CreateCells(analysis.EffectivenessValues),
            new SwShTypeChartWorkflowStats(source?.Status == "available" ? 1 : 0, 1, SwShTypeChartMainPatcher.ChartLength),
            diagnostics);
    }

    private static SwShTypeChartCell[] CreateCells(IReadOnlyList<int> values)
    {
        var vanilla = SwShTypeChartMainPatcher.VanillaChartValues;
        return Enumerable.Range(0, SwShTypeChartMainPatcher.ChartLength)
            .Select(index => new SwShTypeChartCell(
                AttackTypeIndex: index / SwShTypeChartMainPatcher.TypeCount,
                DefenseTypeIndex: index % SwShTypeChartMainPatcher.TypeCount,
                Effectiveness: values[index],
                VanillaEffectiveness: vanilla[index]))
            .ToArray();
    }

    private static SwShTypeChartMainAnalysis CreateDefaultAnalysis()
    {
        return new SwShTypeChartMainAnalysis(
            SwShTypeChartMainKind.Vanilla,
            "Type Chart values are unavailable until exefs/main can be inspected.",
            SwShTypeChartMainPatcher.VanillaChartValues,
            "unknown",
            "unknown",
            ChartOffset: null,
            DetectedGame: null);
    }

    private static SwShTypeChartSourceRecord CreateSource(ProjectFileGraphEntry entry, string status)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShTypeChartSourceRecord(
            "exefs-main",
            "ExeFS main",
            entry.RelativePath,
            status,
            new SwShTypeChartProvenance(entry.RelativePath, sourceLayer, entry.State));
    }

    private static SwShTypeChartSourceRecord CreateMissingSource()
    {
        return new SwShTypeChartSourceRecord(
            "exefs-main",
            "ExeFS main",
            ExeFsMainPath,
            "missing",
            new SwShTypeChartProvenance(
                ExeFsMainPath,
                ProjectFileLayer.Generated,
                ProjectFileGraphEntryState.BaseOnly));
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.TypeChart,
            "Type Chart",
            "Advanced editor for the Sword/Shield type-effectiveness table in exefs/main.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: SwShTypeChartEditSessionService.TypeChartEditDomain,
            Expected: expected);
    }
}
