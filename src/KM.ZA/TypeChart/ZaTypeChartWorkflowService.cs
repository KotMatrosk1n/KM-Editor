// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.ExeFs;
using KM.ZA.Workflows;

namespace KM.ZA.TypeChart;

public sealed class ZaTypeChartWorkflowService
{
    public const string ExeFsMainPath = ZaExeFsReservedRegionLedger.ExeFsMainPath;

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

    private static readonly ZaTypeChartTypeDefinition[] TypeDefinitions =
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

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                ZaWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Type Chart requires valid Pokemon Legends Z-A base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable Pokemon Legends Z-A project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? ZaWorkflowAvailability.Available
            : ZaWorkflowAvailability.ReadOnly);
    }

    public ZaTypeChartWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == ZaWorkflowAvailability.Disabled)
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
            var analysis = ZaTypeChartMainPatcher.Analyze(
                File.ReadAllBytes(mainSource.AbsolutePath),
                project.Paths.SelectedGame);

            if (analysis.Kind is ZaTypeChartMainKind.UnsupportedBuild
                or ZaTypeChartMainKind.GameMismatch
                or ZaTypeChartMainKind.MissingChart
                or ZaTypeChartMainKind.AmbiguousChart
                or ZaTypeChartMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: ExeFsMainPath,
                    expected: "Selected-game Pokemon Legends Z-A exefs/main with one legal 18x18 type chart table"));
            }

            var isBlocked = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                || analysis.Kind is ZaTypeChartMainKind.UnsupportedBuild
                    or ZaTypeChartMainKind.GameMismatch
                    or ZaTypeChartMainKind.MissingChart
                    or ZaTypeChartMainKind.AmbiguousChart
                    or ZaTypeChartMainKind.Conflict;
            var installStatus = isBlocked
                ? "blocked"
                : analysis.Kind == ZaTypeChartMainKind.Modified
                    ? "modified"
                    : summary.Availability == ZaWorkflowAvailability.Available
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
                expected: "Readable Pokemon Legends Z-A exefs/main"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Type Chart cannot inspect effectiveness values because exefs/main could not be read.",
                CreateDefaultAnalysis(),
                source,
                diagnostics);
        }
    }

    internal static ZaTypeChartWorkflowFileSource? ResolveWorkflowFile(
        OpenedProject project,
        string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new ZaTypeChartWorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    internal static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
        }

        return null;
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);
        if (pathFromOutputRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(pathFromOutputRoot))
        {
            return null;
        }

        return targetPath;
    }

    public static IReadOnlyList<ZaTypeChartTypeDefinition> Types => TypeDefinitions;

    private static ZaTypeChartWorkflow CreateWorkflow(
        ZaWorkflowSummary summary,
        string installStatus,
        string installMessage,
        ZaTypeChartMainAnalysis analysis,
        ZaTypeChartSourceRecord? source,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ZaTypeChartWorkflow(
            summary,
            installStatus,
            installMessage,
            analysis.BuildId,
            analysis.ChartOffsetHex,
            analysis.DetectedGame,
            source,
            TypeDefinitions,
            CreateCells(analysis.EffectivenessValues),
            new ZaTypeChartWorkflowStats(source?.Status == "available" ? 1 : 0, 1, ZaTypeChartMainPatcher.ChartLength),
            diagnostics);
    }

    private static ZaTypeChartCell[] CreateCells(IReadOnlyList<int> values)
    {
        var displayValues = ToDisplayOrder(values);
        var vanilla = ToDisplayOrder(ZaTypeChartMainPatcher.VanillaChartValues);
        return Enumerable.Range(0, ZaTypeChartMainPatcher.ChartLength)
            .Select(index => new ZaTypeChartCell(
                AttackTypeIndex: index / ZaTypeChartMainPatcher.TypeCount,
                DefenseTypeIndex: index % ZaTypeChartMainPatcher.TypeCount,
                Effectiveness: displayValues[index],
                VanillaEffectiveness: vanilla[index]))
            .ToArray();
    }

    internal static int[] ToDisplayOrder(IReadOnlyList<int> gameOrderValues)
    {
        ZaTypeChartMainPatcher.ValidateValues(gameOrderValues);

        var values = new int[ZaTypeChartMainPatcher.ChartLength];
        for (var attackDisplayIndex = 0; attackDisplayIndex < ZaTypeChartMainPatcher.TypeCount; attackDisplayIndex++)
        {
            var attackGameIndex = DisplayTypeOrderGameIndexes[attackDisplayIndex];
            for (var defenseDisplayIndex = 0; defenseDisplayIndex < ZaTypeChartMainPatcher.TypeCount; defenseDisplayIndex++)
            {
                var defenseGameIndex = DisplayTypeOrderGameIndexes[defenseDisplayIndex];
                values[(attackDisplayIndex * ZaTypeChartMainPatcher.TypeCount) + defenseDisplayIndex] =
                    gameOrderValues[(attackGameIndex * ZaTypeChartMainPatcher.TypeCount) + defenseGameIndex];
            }
        }

        return values;
    }

    internal static int[] ToGameOrder(IReadOnlyList<int> displayOrderValues)
    {
        ZaTypeChartMainPatcher.ValidateValues(displayOrderValues);

        var values = new int[ZaTypeChartMainPatcher.ChartLength];
        for (var attackDisplayIndex = 0; attackDisplayIndex < ZaTypeChartMainPatcher.TypeCount; attackDisplayIndex++)
        {
            var attackGameIndex = DisplayTypeOrderGameIndexes[attackDisplayIndex];
            for (var defenseDisplayIndex = 0; defenseDisplayIndex < ZaTypeChartMainPatcher.TypeCount; defenseDisplayIndex++)
            {
                var defenseGameIndex = DisplayTypeOrderGameIndexes[defenseDisplayIndex];
                values[(attackGameIndex * ZaTypeChartMainPatcher.TypeCount) + defenseGameIndex] =
                    displayOrderValues[(attackDisplayIndex * ZaTypeChartMainPatcher.TypeCount) + defenseDisplayIndex];
            }
        }

        return values;
    }

    private static ZaTypeChartMainAnalysis CreateDefaultAnalysis()
    {
        return new ZaTypeChartMainAnalysis(
            ZaTypeChartMainKind.Vanilla,
            "Type Chart values are unavailable until exefs/main can be inspected.",
            ZaTypeChartMainPatcher.VanillaChartValues,
            "unknown",
            "unknown",
            ChartOffset: null,
            DetectedGame: null);
    }

    private static ZaTypeChartSourceRecord CreateSource(ProjectFileGraphEntry entry, string status)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new ZaTypeChartSourceRecord(
            "exefs-main",
            "ExeFS main",
            entry.RelativePath,
            status,
            new ZaTypeChartProvenance(entry.RelativePath, sourceLayer, entry.State));
    }

    private static ZaTypeChartSourceRecord CreateMissingSource()
    {
        return new ZaTypeChartSourceRecord(
            "exefs-main",
            "ExeFS main",
            ExeFsMainPath,
            "missing",
            new ZaTypeChartProvenance(
                ExeFsMainPath,
                ProjectFileLayer.Generated,
                ProjectFileGraphEntryState.BaseOnly));
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static ZaWorkflowSummary CreateSummary(
        ZaWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new ZaWorkflowSummary(
            ZaWorkflowIds.TypeChart,
            "Type Chart",
            "Advanced Pokemon Legends Z-A ExeFS editor for the type-effectiveness table in exefs/main.",
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
            Domain: ZaTypeChartEditSessionService.TypeChartEditDomain,
            Expected: expected);
    }
}

internal sealed record ZaTypeChartWorkflowFileSource(
    ProjectFileGraphEntry Entry,
    string AbsolutePath);
