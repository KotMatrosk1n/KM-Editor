// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using KM.SwSh.Workflows;

namespace KM.SwSh.IvScreen;

public sealed class SwShIvScreenWorkflowService
{
    public const string ExeFsMainPath = SwShExeFsPatchWorkflowService.ExeFsMainPath;
    public const string Marker = "SWSH_IV_DISPLAY_V1";

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "IV Screen requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "IV Screen requires Pokemon Sword or Pokemon Shield to be selected before it can load.",
                    expected: "Selected Pokemon Sword or Pokemon Shield project"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShIvScreenWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var provenance = CreateMissingProvenance(ExeFsMainPath);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            var installMessage = project.Health.CanOpenReadOnlyWorkflows
                && !IsSupportedGame(project.Paths.SelectedGame)
                    ? "IV Screen cannot load until Pokemon Sword or Pokemon Shield is selected."
                    : "IV Screen cannot load until project paths validate.";
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
                "IV Screen cannot inspect the hook because exefs/main is missing.",
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
                "IV Screen cannot verify the selected-game vanilla base exefs/main.",
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
                "IV Screen cannot inspect the effective exefs/main.",
                CreateDefaultAnalysis(),
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        SwShIvScreenAnalysis baseAnalysis;
        SwShIvScreenAnalysis effectiveAnalysis;
        string? effectiveApplyPreflightError;
        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            baseAnalysis = SwShIvScreenMainPatcher.Analyze(
                baseBytes,
                project.Paths.SelectedGame);
            if (PathsReferToSameFile(basePath, effectivePath))
            {
                effectiveAnalysis = baseAnalysis;
                effectiveApplyPreflightError = SwShIvScreenMainPatcher.GetApplyPreflightError(
                    baseBytes,
                    project.Paths.SelectedGame);
            }
            else
            {
                var effectiveBytes = File.ReadAllBytes(effectivePath);
                effectiveAnalysis = SwShIvScreenMainPatcher.Analyze(
                    effectiveBytes,
                    project.Paths.SelectedGame);
                SwShIvScreenMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, effectiveBytes);
                effectiveApplyPreflightError = SwShIvScreenMainPatcher.GetApplyPreflightError(
                    effectiveBytes,
                    project.Paths.SelectedGame);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS main sources could not be verified for IV Screen: {exception.Message}",
                file: entry.RelativePath,
                expected: "Readable selected-game base and effective Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "IV Screen cannot inspect the hook because the base and effective exefs/main sources could not be verified as compatible.",
                CreateDefaultAnalysis(),
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        var sourceFileCount = PathsReferToSameFile(basePath, effectivePath) ? 1 : 2;
        if (baseAnalysis.Kind != SwShIvScreenInstallKind.NotInstalled)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Base exefs/main is not a selected-game vanilla IV Screen source. {baseAnalysis.Message}",
                file: entry.RelativePath,
                expected: "Selected-game Sword/Shield 1.3.2 base exefs/main with vanilla IV Screen regions"));
            AddEffectiveAnalysisDiagnostic(effectiveAnalysis, entry.RelativePath, diagnostics);
            return CreateWorkflow(
                summary,
                "blocked",
                "IV Screen requires a verified selected-game vanilla base exefs/main before it can edit or restore the effective source.",
                effectiveAnalysis,
                provenance,
                sourceFileCount,
                diagnostics);
        }

        AddEffectiveAnalysisDiagnostic(effectiveAnalysis, entry.RelativePath, diagnostics);
        var legacyMigrationBlocked = effectiveAnalysis.Kind == SwShIvScreenInstallKind.InstalledLegacyV1
            && effectiveApplyPreflightError is not null;
        if (legacyMigrationBlocked)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Legacy IV Screen uninstall remains available, but migration is unavailable: {effectiveApplyPreflightError}",
                file: entry.RelativePath,
                expected: "Exact current IV Screen dependency anchors for migration"));
        }

        var installStatus = effectiveAnalysis.Kind switch
        {
            SwShIvScreenInstallKind.InstalledV1 => "installed",
            SwShIvScreenInstallKind.InstalledLegacyV1 when legacyMigrationBlocked => "blocked",
            SwShIvScreenInstallKind.InstalledLegacyV1 => "installed",
            SwShIvScreenInstallKind.NotInstalled => summary.Availability == SwShWorkflowAvailability.Available
                ? "available"
                : "readOnly",
            SwShIvScreenInstallKind.ForeignPatch => "foreign",
            _ => "blocked",
        };

        return CreateWorkflow(
            summary,
            installStatus,
            legacyMigrationBlocked
                ? $"Exact legacy IV Screen uninstall remains available, but migration is blocked: {effectiveApplyPreflightError}"
                : effectiveAnalysis.Message,
            effectiveAnalysis,
            provenance,
            sourceFileCount,
            diagnostics,
            canUninstall: summary.Availability == SwShWorkflowAvailability.Available
                && effectiveAnalysis.Kind is SwShIvScreenInstallKind.InstalledV1 or SwShIvScreenInstallKind.InstalledLegacyV1
                && entry.LayeredFile is not null
                && ResolveOutputPath(project.Paths, ExeFsMainPath) is { } outputPath
                && File.Exists(outputPath));
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

    private static SwShIvScreenWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        SwShIvScreenAnalysis analysis,
        SwShIvScreenProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        bool canUninstall = false)
    {
        var reservedRegions = analysis.DetectedGame is null
            ? Array.Empty<SwShIvScreenReservedRegion>()
            : SwShIvScreenMainPatcher.ReservedMainTextRegions(analysis.DetectedGame)
                .Select(region => new SwShIvScreenReservedRegion(
                    region.FeatureId,
                    region.Label,
                    region.OffsetLabel,
                    region.StartOffset,
                    region.Length,
                    region.Rule))
                .ToArray();

        return new SwShIvScreenWorkflow(
            summary,
            installStatus,
            installMessage,
            Marker,
            analysis.BuildId,
            analysis.DetectedGame,
            analysis.PrimaryValueSourceOffsetHex,
            analysis.XToggleRefreshOffsetHex,
            FormatTextOffset(SwShIvScreenMainPatcher.RawIvGetterOffset),
            FormatTextOffset(SwShIvScreenMainPatcher.HyperTrainingIvWrapperOffset),
            canUninstall,
            reservedRegions,
            provenance,
            new SwShIvScreenWorkflowStats(reservedRegions.Length, sourceFileCount),
            diagnostics);
    }

    private static SwShIvScreenAnalysis CreateDefaultAnalysis()
    {
        return new SwShIvScreenAnalysis(
            SwShIvScreenInstallKind.NotInstalled,
            "IV Screen is not installed.",
            BuildId: "unknown",
            PrimaryValueSourceOffsetHex: "unknown",
            XToggleRefreshOffsetHex: "unknown",
            DetectedGame: null);
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

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShIvScreenProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShIvScreenProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShIvScreenProvenance CreateMissingProvenance(string relativePath)
    {
        return new SwShIvScreenProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static void AddEffectiveAnalysisDiagnostic(
        SwShIvScreenAnalysis analysis,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (analysis.Kind is not (SwShIvScreenInstallKind.UnsupportedBuild
            or SwShIvScreenInstallKind.NotInstalledDependencyConflict
            or SwShIvScreenInstallKind.GameMismatch
            or SwShIvScreenInstallKind.ForeignPatch
            or SwShIvScreenInstallKind.Conflict))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            analysis.Kind == SwShIvScreenInstallKind.ForeignPatch
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error,
            analysis.Message,
            file: relativePath,
            expected: "Selected-game Sword/Shield 1.3.2 effective exefs/main with vanilla IV Screen regions or an exact KM IV Screen install"));
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
            SwShWorkflowIds.IvScreen,
            "IV Screen",
            "Independent ExeFS editor for raw IV numbers on the Pokemon Summary stats graph. Install and uninstall touch only exact IV Screen-owned bytes.",
            availability,
            diagnostics);
    }

    private static string FormatTextOffset(int offset)
    {
        return $"main.text+0x{offset:X8}";
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
            Domain: SwShIvScreenEditSessionService.IvScreenEditDomain,
            Expected: expected);
    }
}
