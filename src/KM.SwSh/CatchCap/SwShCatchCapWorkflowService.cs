// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using KM.SwSh.Workflows;

namespace KM.SwSh.CatchCap;

public sealed class SwShCatchCapWorkflowService
{
    public const string ExeFsMainPath = SwShExeFsPatchWorkflowService.ExeFsMainPath;

    private static readonly string[] CapLabels =
    [
        "No badges",
        "First badge",
        "Second badge",
        "Third badge",
        "Fourth badge",
        "Fifth badge",
        "Sixth badge",
        "Seventh badge",
        "Eighth badge",
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
                    "Catch Cap Editor requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Catch Cap Editor requires Pokemon Sword or Pokemon Shield to be selected before it can load.",
                    expected: "Selected Pokemon Sword or Pokemon Shield project"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShCatchCapWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var provenance = CreateMissingProvenance(ExeFsMainPath);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            var installMessage = project.Health.CanOpenReadOnlyWorkflows
                && !IsSupportedGame(project.Paths.SelectedGame)
                    ? "Catch Cap Editor cannot load until Pokemon Sword or Pokemon Shield is selected."
                    : "Catch Cap Editor cannot load until project paths validate.";
            return CreateWorkflow(
                summary,
                "disabled",
                installMessage,
                CreateDefaultAnalysis(),
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        var entry = FindEntry(project, ExeFsMainPath);
        if (entry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main is missing.",
                file: ExeFsMainPath,
                expected: "exefs/main"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Catch Cap Editor cannot inspect the hook because exefs/main is missing.",
                CreateDefaultAnalysis(),
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        provenance = CreateProvenance(entry);
        var basePath = ResolveBaseSourcePath(project.Paths, ExeFsMainPath);
        var effectivePath = ResolveSourcePath(project.Paths, entry);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Base ExeFS main could not be resolved from the project graph.",
                file: entry.RelativePath,
                expected: "Readable selected-game vanilla Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Catch Cap Editor cannot verify the selected-game vanilla base exefs/main.",
                CreateDefaultAnalysis(),
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        if (effectivePath is null || !File.Exists(effectivePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Effective ExeFS main could not be resolved from the project graph.",
                file: entry.RelativePath,
                expected: "Readable base or LayeredFS exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Catch Cap Editor cannot inspect the effective exefs/main.",
                CreateDefaultAnalysis(),
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        SwShCatchCapAnalysis baseAnalysis;
        SwShCatchCapAnalysis effectiveAnalysis;
        try
        {
            baseAnalysis = SwShCatchCapMainPatcher.Analyze(
                File.ReadAllBytes(basePath),
                project.Paths.SelectedGame);
            effectiveAnalysis = PathsReferToSameFile(basePath, effectivePath)
                ? baseAnalysis
                : SwShCatchCapMainPatcher.Analyze(
                    File.ReadAllBytes(effectivePath),
                    project.Paths.SelectedGame);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main could not be read for Catch Cap verification.",
                file: entry.RelativePath,
                expected: "Readable selected-game base and effective Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Catch Cap Editor cannot inspect the hook because an exefs/main source could not be read.",
                CreateDefaultAnalysis(),
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        var sourceFileCount = PathsReferToSameFile(basePath, effectivePath) ? 1 : 2;
        if (baseAnalysis.Kind != SwShCatchCapInstallKind.NotInstalled)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Base exefs/main is not a selected-game vanilla Catch Cap source. {baseAnalysis.Message}",
                file: entry.RelativePath,
                expected: "Selected-game Sword/Shield 1.3.2 base exefs/main with vanilla Catch Cap regions"));
            AddEffectiveAnalysisDiagnostic(effectiveAnalysis, entry.RelativePath, diagnostics);
            return CreateWorkflow(
                summary,
                "blocked",
                "Catch Cap Editor requires a verified selected-game vanilla base exefs/main before it can edit or restore the effective source.",
                effectiveAnalysis,
                provenance,
                sourceFileCount,
                diagnostics);
        }

        AddEffectiveAnalysisDiagnostic(effectiveAnalysis, entry.RelativePath, diagnostics);
        var installStatus = effectiveAnalysis.Kind switch
        {
            SwShCatchCapInstallKind.InstalledV1 => "installed",
            SwShCatchCapInstallKind.NotInstalled => summary.Availability == SwShWorkflowAvailability.Available
                ? "available"
                : "readOnly",
            SwShCatchCapInstallKind.ForeignPatch => "foreign",
            _ => "blocked",
        };

        return CreateWorkflow(
            summary,
            installStatus,
            effectiveAnalysis.Message,
            effectiveAnalysis,
            provenance,
            sourceFileCount,
            diagnostics);
    }

    internal IReadOnlyList<ProjectFileReference> GetPlanSources(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var entry = FindEntry(project, ExeFsMainPath);
        if (entry is null)
        {
            return Array.Empty<ProjectFileReference>();
        }

        var sources = new List<ProjectFileReference>(2);
        if (entry.BaseFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, ExeFsMainPath));
        }

        if (entry.LayeredFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Layered, ExeFsMainPath));
        }

        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static SwShCatchCapWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        SwShCatchCapAnalysis analysis,
        SwShCatchCapProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var caps = analysis.Caps
            .Select((cap, index) => new SwShCatchCapRecord(
                index,
                CapLabels[index],
                index == SwShCatchCapMainPatcher.FinalBadgeCount
                    ? SwShCatchCapMainPatcher.FinalBadgeCap
                    : cap,
                MinimumLevelCap(index),
                MaximumLevelCap(index)))
            .ToArray();

        return new SwShCatchCapWorkflow(
            summary,
            installStatus,
            installMessage,
            analysis.LogicExpression,
            analysis.CapLogicSha256,
            analysis.BuildId,
            analysis.DetectedGame,
            analysis.DisplayHookOffsetHex,
            analysis.RuntimeHookOffsetHex,
            caps,
            provenance,
            new SwShCatchCapWorkflowStats(caps.Length, sourceFileCount),
            diagnostics);
    }

    private static SwShCatchCapAnalysis CreateDefaultAnalysis()
    {
        return new SwShCatchCapAnalysis(
            SwShCatchCapInstallKind.NotInstalled,
            "Catch Cap Editor hook is not installed.",
            [20, 25, 30, 35, 40, 45, 50, 55, 100],
            "badge_count < 8 ? 20 + badge_count * 5 : 100",
            SwShCatchCapMainPatcher.ComputeCapLogicSha256([20, 25, 30, 35, 40, 45, 50, 55, 100]),
            BuildId: "unknown",
            DisplayHookOffsetHex: "unknown",
            RuntimeHookOffsetHex: "unknown",
            DetectedGame: null);
    }

    private static int MinimumLevelCap(int badgeCount)
    {
        return badgeCount == SwShCatchCapMainPatcher.FinalBadgeCount
            ? SwShCatchCapMainPatcher.FinalBadgeCap
            : SwShCatchCapMainPatcher.MinimumCap;
    }

    private static int MaximumLevelCap(int badgeCount)
    {
        return badgeCount == SwShCatchCapMainPatcher.FinalBadgeCount
            ? SwShCatchCapMainPatcher.FinalBadgeCap
            : SwShCatchCapMainPatcher.MaximumCap;
    }

    private static ProjectFileGraphEntry? FindEntry(OpenedProject project, string relativePath)
    {
        return project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
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

    internal static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (!targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..]);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        return SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, targetRelativePath);
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShCatchCapProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShCatchCapProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShCatchCapProvenance CreateMissingProvenance(string relativePath)
    {
        return new SwShCatchCapProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static void AddEffectiveAnalysisDiagnostic(
        SwShCatchCapAnalysis analysis,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (analysis.Kind is not (SwShCatchCapInstallKind.UnsupportedBuild
            or SwShCatchCapInstallKind.GameMismatch
            or SwShCatchCapInstallKind.ForeignPatch
            or SwShCatchCapInstallKind.Conflict))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            analysis.Kind == SwShCatchCapInstallKind.ForeignPatch
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error,
            analysis.Message,
            file: relativePath,
            expected: "Selected-game Sword/Shield 1.3.2 effective exefs/main with vanilla Catch Cap regions or an exact KM Catch Cap Hook"));
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    internal static bool IsSupportedGame(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.CatchCap,
            "Catch Cap Editor",
            "Independent ExeFS editor for badge catch caps 0-7. It patches the display and runtime capture checks; eight badges remains Lv.100 because the game treats full badges as catch any level. Stage Uninstall removes only Catch Cap bytes and preserves other hook editors.",
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
            Domain: SwShCatchCapEditSessionService.CatchCapEditDomain,
            Expected: expected);
    }
}
