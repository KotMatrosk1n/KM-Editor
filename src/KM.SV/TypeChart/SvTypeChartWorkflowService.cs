// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.ExeFs;
using KM.SV.Workflows;

namespace KM.SV.TypeChart;

public sealed class SvTypeChartWorkflowService
{
    public const string ExeFsMainPath = SvExeFsReservedRegionLedger.ExeFsMainPath;

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

    private static readonly SvTypeChartTypeDefinition[] TypeDefinitions =
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

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SvWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Type Chart requires valid Scarlet/Violet base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable Scarlet/Violet project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SvWorkflowAvailability.Available
            : SvWorkflowAvailability.ReadOnly);
    }

    public SvTypeChartWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SvWorkflowAvailability.Disabled)
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
            var analysis = SvTypeChartMainPatcher.Analyze(
                File.ReadAllBytes(mainSource.AbsolutePath),
                project.Paths.SelectedGame);

            if (analysis.Kind is SvTypeChartMainKind.UnsupportedBuild
                or SvTypeChartMainKind.GameMismatch
                or SvTypeChartMainKind.MissingChart
                or SvTypeChartMainKind.AmbiguousChart
                or SvTypeChartMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: ExeFsMainPath,
                    expected: "Selected-game Scarlet/Violet exefs/main with one legal 18x18 type chart table"));
            }

            var isBlocked = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                || analysis.Kind is SvTypeChartMainKind.UnsupportedBuild
                    or SvTypeChartMainKind.GameMismatch
                    or SvTypeChartMainKind.MissingChart
                    or SvTypeChartMainKind.AmbiguousChart
                    or SvTypeChartMainKind.Conflict;
            var installStatus = isBlocked
                ? "blocked"
                : analysis.Kind == SvTypeChartMainKind.Modified
                    ? "modified"
                    : summary.Availability == SvWorkflowAvailability.Available
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
                expected: "Readable Scarlet/Violet exefs/main"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Type Chart cannot inspect effectiveness values because exefs/main could not be read.",
                CreateDefaultAnalysis(),
                source,
                diagnostics);
        }
    }

    internal static SvTypeChartWorkflowFileSource? ResolveWorkflowFile(
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
            ? new SvTypeChartWorkflowFileSource(graphEntry, sourcePath)
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
        if (PathContainment.IsOutsideRoot(pathFromOutputRoot))
        {
            return null;
        }

        return targetPath;
    }

    public static IReadOnlyList<SvTypeChartTypeDefinition> Types => TypeDefinitions;

    private static SvTypeChartWorkflow CreateWorkflow(
        SvWorkflowSummary summary,
        string installStatus,
        string installMessage,
        SvTypeChartMainAnalysis analysis,
        SvTypeChartSourceRecord? source,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SvTypeChartWorkflow(
            summary,
            installStatus,
            installMessage,
            analysis.BuildId,
            analysis.ChartOffsetHex,
            analysis.DetectedGame,
            source,
            TypeDefinitions,
            CreateCells(analysis.EffectivenessValues),
            new SvTypeChartWorkflowStats(source?.Status == "available" ? 1 : 0, 1, SvTypeChartMainPatcher.ChartLength),
            diagnostics);
    }

    private static SvTypeChartCell[] CreateCells(IReadOnlyList<int> values)
    {
        var displayValues = ToDisplayOrder(values);
        var vanilla = ToDisplayOrder(SvTypeChartMainPatcher.VanillaChartValues);
        return Enumerable.Range(0, SvTypeChartMainPatcher.ChartLength)
            .Select(index => new SvTypeChartCell(
                AttackTypeIndex: index / SvTypeChartMainPatcher.TypeCount,
                DefenseTypeIndex: index % SvTypeChartMainPatcher.TypeCount,
                Effectiveness: displayValues[index],
                VanillaEffectiveness: vanilla[index]))
            .ToArray();
    }

    internal static int[] ToDisplayOrder(IReadOnlyList<int> gameOrderValues)
    {
        SvTypeChartMainPatcher.ValidateValues(gameOrderValues);

        var values = new int[SvTypeChartMainPatcher.ChartLength];
        for (var attackDisplayIndex = 0; attackDisplayIndex < SvTypeChartMainPatcher.TypeCount; attackDisplayIndex++)
        {
            var attackGameIndex = DisplayTypeOrderGameIndexes[attackDisplayIndex];
            for (var defenseDisplayIndex = 0; defenseDisplayIndex < SvTypeChartMainPatcher.TypeCount; defenseDisplayIndex++)
            {
                var defenseGameIndex = DisplayTypeOrderGameIndexes[defenseDisplayIndex];
                values[(attackDisplayIndex * SvTypeChartMainPatcher.TypeCount) + defenseDisplayIndex] =
                    gameOrderValues[(attackGameIndex * SvTypeChartMainPatcher.TypeCount) + defenseGameIndex];
            }
        }

        return values;
    }

    internal static int[] ToGameOrder(IReadOnlyList<int> displayOrderValues)
    {
        SvTypeChartMainPatcher.ValidateValues(displayOrderValues);

        var values = new int[SvTypeChartMainPatcher.ChartLength];
        for (var attackDisplayIndex = 0; attackDisplayIndex < SvTypeChartMainPatcher.TypeCount; attackDisplayIndex++)
        {
            var attackGameIndex = DisplayTypeOrderGameIndexes[attackDisplayIndex];
            for (var defenseDisplayIndex = 0; defenseDisplayIndex < SvTypeChartMainPatcher.TypeCount; defenseDisplayIndex++)
            {
                var defenseGameIndex = DisplayTypeOrderGameIndexes[defenseDisplayIndex];
                values[(attackGameIndex * SvTypeChartMainPatcher.TypeCount) + defenseGameIndex] =
                    displayOrderValues[(attackDisplayIndex * SvTypeChartMainPatcher.TypeCount) + defenseDisplayIndex];
            }
        }

        return values;
    }

    private static SvTypeChartMainAnalysis CreateDefaultAnalysis()
    {
        return new SvTypeChartMainAnalysis(
            SvTypeChartMainKind.Vanilla,
            "Type Chart values are unavailable until exefs/main can be inspected.",
            SvTypeChartMainPatcher.VanillaChartValues,
            "unknown",
            "unknown",
            ChartOffset: null,
            DetectedGame: null);
    }

    private static SvTypeChartSourceRecord CreateSource(ProjectFileGraphEntry entry, string status)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SvTypeChartSourceRecord(
            "exefs-main",
            "ExeFS main",
            entry.RelativePath,
            status,
            new SvTypeChartProvenance(entry.RelativePath, sourceLayer, entry.State));
    }

    private static SvTypeChartSourceRecord CreateMissingSource()
    {
        return new SvTypeChartSourceRecord(
            "exefs-main",
            "ExeFS main",
            ExeFsMainPath,
            "missing",
            new SvTypeChartProvenance(
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

    private static SvWorkflowSummary CreateSummary(
        SvWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SvWorkflowSummary(
            SvWorkflowIds.TypeChart,
            "Type Chart",
            "Advanced S/V ExeFS editor for the type-effectiveness table in exefs/main.",
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
            Domain: SvTypeChartEditSessionService.TypeChartEditDomain,
            Expected: expected);
    }
}

internal sealed record SvTypeChartWorkflowFileSource(
    ProjectFileGraphEntry Entry,
    string AbsolutePath);
