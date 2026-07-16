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

    private static readonly int[] DisplayTypeOrderGameIndexes =
    [
        0,  // Normal
        9,  // Fire
        10, // Water
        12, // Electric
        11, // Grass
        14, // Ice
        1,  // Fighting
        3,  // Poison
        4,  // Ground
        2,  // Flying
        13, // Psychic
        6,  // Bug
        5,  // Rock
        7,  // Ghost
        15, // Dragon
        16, // Dark
        8,  // Steel
        17, // Fairy
    ];

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

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Type Chart requires Pokemon Sword or Pokemon Shield to be selected before it can load.",
                    expected: "Selected Pokemon Sword or Pokemon Shield project"));
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
            var installMessage = project.Health.CanOpenReadOnlyWorkflows
                && !IsSupportedGame(project.Paths.SelectedGame)
                    ? "Type Chart cannot load until Pokemon Sword or Pokemon Shield is selected."
                    : "Type Chart cannot load until project paths validate.";
            return CreateWorkflow(
                summary,
                "disabled",
                installMessage,
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
        var basePath = ResolveBaseSourcePath(project.Paths, ExeFsMainPath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Base ExeFS main could not be resolved from the project graph.",
                file: ExeFsMainPath,
                expected: "Readable selected-game vanilla Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Type Chart cannot verify the selected-game vanilla base exefs/main.",
                CreateDefaultAnalysis(),
                source,
                diagnostics);
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var sameSource = PathsReferToSameFile(basePath, mainSource.AbsolutePath);
            var effectiveBytes = sameSource
                ? baseBytes
                : File.ReadAllBytes(mainSource.AbsolutePath);
            var baseAnalysis = SwShTypeChartMainPatcher.Analyze(
                baseBytes,
                project.Paths.SelectedGame);
            var analysis = sameSource
                ? baseAnalysis
                : SwShTypeChartMainPatcher.Analyze(
                    effectiveBytes,
                    project.Paths.SelectedGame);

            if (baseAnalysis.Kind != SwShTypeChartMainKind.Vanilla)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Base exefs/main is not a selected-game vanilla Type Chart source. {baseAnalysis.Message}",
                    file: ExeFsMainPath,
                    expected: "Selected-game Sword/Shield 1.3.2 base exefs/main with the vanilla 18x18 type chart"));
            }

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
                    expected: "Selected-game Sword/Shield 1.3.2 effective exefs/main with one legal 18x18 type chart table"));
            }

            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                try
                {
                    SwShTypeChartMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, effectiveBytes);
                }
                catch (InvalidDataException exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        exception.Message,
                        file: ExeFsMainPath,
                        expected: "Compatible selected-game base and effective Sword/Shield exefs/main NSOs"));
                }
            }

            var isBlocked = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var installStatus = isBlocked
                ? "blocked"
                : analysis.Kind == SwShTypeChartMainKind.Modified
                    ? "modified"
                    : summary.Availability == SwShWorkflowAvailability.Available
                        ? "available"
                        : "readOnly";
            var installMessage = isBlocked && baseAnalysis.Kind != SwShTypeChartMainKind.Vanilla
                ? "Type Chart requires a verified selected-game vanilla base exefs/main before it can edit or restore the effective source."
                : analysis.Message;

            return CreateWorkflow(
                summary,
                installStatus,
                installMessage,
                analysis,
                source,
                diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main could not be read for Type Chart verification.",
                file: ExeFsMainPath,
                expected: "Readable selected-game base and effective Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Type Chart cannot inspect effectiveness values because an exefs/main source could not be read.",
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

    internal static string? ResolveBaseSourcePath(ProjectPaths paths, string relativePath)
    {
        return SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, relativePath);
    }

    internal IReadOnlyList<ProjectFileReference> GetPlanSources(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var source = ResolveWorkflowFile(project, ExeFsMainPath);
        if (source is null)
        {
            return Array.Empty<ProjectFileReference>();
        }

        var sources = new List<ProjectFileReference>(2);
        if (source.Entry.BaseFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, ExeFsMainPath));
        }

        if (source.Entry.LayeredFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Layered, ExeFsMainPath));
        }

        return sources
            .Distinct()
            .OrderBy(candidate => candidate.Layer)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool IsSupportedGame(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
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
            new SwShTypeChartWorkflowStats(
                source?.Status == "available" ? 1 : 0,
                source?.Provenance.SourceLayer == ProjectFileLayer.Layered ? 1 : 0,
                SwShTypeChartMainPatcher.ChartLength),
            diagnostics);
    }

    private static SwShTypeChartCell[] CreateCells(IReadOnlyList<int> values)
    {
        var displayValues = ToDisplayOrder(values);
        var vanilla = ToDisplayOrder(SwShTypeChartMainPatcher.VanillaChartValues);
        return Enumerable.Range(0, SwShTypeChartMainPatcher.ChartLength)
            .Select(index => new SwShTypeChartCell(
                AttackTypeIndex: index / SwShTypeChartMainPatcher.TypeCount,
                DefenseTypeIndex: index % SwShTypeChartMainPatcher.TypeCount,
                Effectiveness: displayValues[index],
                VanillaEffectiveness: vanilla[index]))
            .ToArray();
    }

    internal static int[] ToDisplayOrder(IReadOnlyList<int> gameOrderValues)
    {
        SwShTypeChartMainPatcher.ValidateValues(gameOrderValues);

        var values = new int[SwShTypeChartMainPatcher.ChartLength];
        for (var attackDisplayIndex = 0; attackDisplayIndex < SwShTypeChartMainPatcher.TypeCount; attackDisplayIndex++)
        {
            var attackGameIndex = DisplayTypeOrderGameIndexes[attackDisplayIndex];
            for (var defenseDisplayIndex = 0; defenseDisplayIndex < SwShTypeChartMainPatcher.TypeCount; defenseDisplayIndex++)
            {
                var defenseGameIndex = DisplayTypeOrderGameIndexes[defenseDisplayIndex];
                values[(attackDisplayIndex * SwShTypeChartMainPatcher.TypeCount) + defenseDisplayIndex] =
                    gameOrderValues[(attackGameIndex * SwShTypeChartMainPatcher.TypeCount) + defenseGameIndex];
            }
        }

        return values;
    }

    internal static int[] ToGameOrder(IReadOnlyList<int> displayOrderValues)
    {
        SwShTypeChartMainPatcher.ValidateValues(displayOrderValues);

        var values = new int[SwShTypeChartMainPatcher.ChartLength];
        for (var attackDisplayIndex = 0; attackDisplayIndex < SwShTypeChartMainPatcher.TypeCount; attackDisplayIndex++)
        {
            var attackGameIndex = DisplayTypeOrderGameIndexes[attackDisplayIndex];
            for (var defenseDisplayIndex = 0; defenseDisplayIndex < SwShTypeChartMainPatcher.TypeCount; defenseDisplayIndex++)
            {
                var defenseGameIndex = DisplayTypeOrderGameIndexes[defenseDisplayIndex];
                values[(attackGameIndex * SwShTypeChartMainPatcher.TypeCount) + defenseGameIndex] =
                    displayOrderValues[(attackDisplayIndex * SwShTypeChartMainPatcher.TypeCount) + defenseDisplayIndex];
            }
        }

        return values;
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
