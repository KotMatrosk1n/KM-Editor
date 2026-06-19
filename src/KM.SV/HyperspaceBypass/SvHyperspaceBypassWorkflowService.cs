// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.ExeFs;
using KM.SV.Workflows;

namespace KM.SV.HyperspaceBypass;

public sealed class SvHyperspaceBypassWorkflowService
{
    public const string ExeFsMainPath = SvExeFsReservedRegionLedger.ExeFsMainPath;

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SvWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Hyperspace Bypass requires valid Scarlet/Violet base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable Scarlet/Violet project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SvWorkflowAvailability.Available
            : SvWorkflowAvailability.ReadOnly);
    }

    public SvHyperspaceBypassWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var provenance = CreateMissingProvenance(ExeFsMainPath);

        if (summary.Availability == SvWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                "disabled",
                "Hyperspace Bypass cannot load until project paths validate.",
                buildId: "unknown",
                patchOffsetHex: "unknown",
                stubKind: "not inspected",
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
                "Hyperspace Bypass cannot inspect the runtime gate because exefs/main is missing.",
                buildId: "unknown",
                patchOffsetHex: "unknown",
                stubKind: "not inspected",
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
                expected: "Readable Scarlet or Violet exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Hyperspace Bypass cannot inspect the runtime gate because exefs/main cannot be read.",
                buildId: "unknown",
                patchOffsetHex: "unknown",
                stubKind: "not inspected",
                detectedGame: null,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        try
        {
            var analysis = SvHyperspaceBypassMainPatcher.Analyze(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame);
            if (analysis.Kind is SvHyperspaceBypassInstallKind.UnsupportedBuild
                or SvHyperspaceBypassInstallKind.GameMismatch
                or SvHyperspaceBypassInstallKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: entry.RelativePath,
                    expected: "Supported Scarlet or Violet Hyperspace runtime gate bytes"));
                return CreateWorkflow(
                    summary,
                    "blocked",
                    analysis.Message,
                    analysis.BuildId,
                    analysis.PatchOffsetHex,
                    analysis.StubKind,
                    analysis.DetectedGame,
                    provenance,
                    sourceFileCount: 1,
                    diagnostics);
            }

            var installStatus = analysis.Kind == SvHyperspaceBypassInstallKind.Installed
                ? "installed"
                : summary.Availability == SvWorkflowAvailability.Available
                    ? "available"
                    : "readOnly";

            return CreateWorkflow(
                summary,
                installStatus,
                analysis.Message,
                analysis.BuildId,
                analysis.PatchOffsetHex,
                analysis.StubKind,
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
                expected: "Readable Scarlet or Violet exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Hyperspace Bypass cannot inspect the runtime gate because exefs/main could not be read.",
                buildId: "unknown",
                patchOffsetHex: "unknown",
                stubKind: "not inspected",
                detectedGame: null,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }
    }

    internal static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
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
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
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

    private static SvHyperspaceBypassWorkflow CreateWorkflow(
        SvWorkflowSummary summary,
        string installStatus,
        string installMessage,
        string buildId,
        string patchOffsetHex,
        string stubKind,
        ProjectGame? detectedGame,
        SvHyperspaceBypassProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var reservedRegions = SvHyperspaceBypassMainPatcher.ReservedMainTextRegions()
            .Select(region => new SvHyperspaceBypassReservedRegion(
                region.FeatureId,
                region.Label,
                region.OffsetLabel,
                region.StartOffset,
                region.Length,
                region.Rule))
            .ToArray();

        return new SvHyperspaceBypassWorkflow(
            summary,
            installStatus,
            installMessage,
            buildId,
            patchOffsetHex,
            stubKind,
            detectedGame,
            reservedRegions,
            provenance,
            new SvHyperspaceBypassWorkflowStats(reservedRegions.Length, sourceFileCount),
            diagnostics);
    }

    private static ProjectFileGraphEntry? FindEntry(OpenedProject project, string relativePath)
    {
        return project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SvHyperspaceBypassProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SvHyperspaceBypassProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SvHyperspaceBypassProvenance CreateMissingProvenance(string relativePath)
    {
        return new SvHyperspaceBypassProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static SvWorkflowSummary CreateSummary(
        SvWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SvWorkflowSummary(
            SvWorkflowIds.HyperspaceBypass,
            "Hyperspace Bypass",
            "Advanced S/V ExeFS editor that lets any Pokemon pass the Hyperspace Hole/Fury Hoopa runtime gate.",
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
            Domain: SvHyperspaceBypassEditSessionService.HyperspaceBypassEditDomain,
            Expected: expected);
    }
}

internal sealed record WorkflowFileSource(
    ProjectFileGraphEntry Entry,
    string AbsolutePath);
