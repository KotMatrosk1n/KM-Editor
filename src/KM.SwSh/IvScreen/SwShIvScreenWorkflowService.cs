// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using KM.SwSh.Workflows;
using System.Globalization;

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
            return CreateWorkflow(
                summary,
                "disabled",
                "IV Screen cannot load until project paths validate.",
                detectedGame: null,
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
                detectedGame: null,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        provenance = CreateProvenance(entry);
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main could not be resolved from the project graph.",
                file: entry.RelativePath,
                expected: "Readable Sword/Shield 1.3.2 exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "IV Screen cannot inspect the hook because exefs/main cannot be read.",
                detectedGame: null,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        try
        {
            var analysis = SwShIvScreenMainPatcher.Analyze(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame);
            var installStatus = analysis.Kind switch
            {
                SwShIvScreenInstallKind.InstalledV1 => "installed",
                SwShIvScreenInstallKind.InstalledLegacyV1 => "installed",
                SwShIvScreenInstallKind.NotInstalled => summary.Availability == SwShWorkflowAvailability.Available ? "available" : "readOnly",
                SwShIvScreenInstallKind.ForeignPatch => "foreign",
                _ => "blocked",
            };
            if (analysis.Kind is SwShIvScreenInstallKind.UnsupportedBuild
                or SwShIvScreenInstallKind.GameMismatch
                or SwShIvScreenInstallKind.ForeignPatch
                or SwShIvScreenInstallKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    analysis.Kind == SwShIvScreenInstallKind.ForeignPatch ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    analysis.Message,
                    file: entry.RelativePath,
                    expected: "Selected-game Sword/Shield 1.3.2 exefs/main with vanilla Pokemon Summary hooks or an installed IV Screen marker"));
            }

            return CreateWorkflow(
                summary,
                installStatus,
                analysis.Message,
                analysis.DetectedGame,
                provenance,
                sourceFileCount: 1,
                diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS main could not be inspected: {exception.Message}",
                file: entry.RelativePath,
                expected: "Readable Sword/Shield 1.3.2 exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "IV Screen cannot inspect the hook because exefs/main could not be read.",
                detectedGame: null,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }
    }

    private static SwShIvScreenWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        ProjectGame? detectedGame,
        SwShIvScreenProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var reservedRegions = SwShIvScreenMainPatcher.ReservedMainTextRegions(detectedGame)
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
            FormatTextOffset(detectedGame == ProjectGame.Shield
                ? SwShIvScreenMainPatcher.ShieldExeFsHookSiteOffset
                : SwShIvScreenMainPatcher.ExeFsHookSiteOffset),
            FormatTextOffset(SwShIvScreenMainPatcher.RawIvGetterOffset),
            FormatTextOffset(SwShIvScreenMainPatcher.HyperTrainingIvWrapperOffset),
            reservedRegions,
            provenance,
            new SwShIvScreenWorkflowStats(reservedRegions.Length, sourceFileCount),
            diagnostics);
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

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.IvScreen,
            "IV Screen",
            "Independent ExeFS editor for raw IV numbers on the Pokemon Summary stats graph. Install and uninstall touch only IV Screen reserved bytes.",
            availability,
            diagnostics);
    }

    private static string FormatTextOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"main.text+0x{offset:X8}");
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
