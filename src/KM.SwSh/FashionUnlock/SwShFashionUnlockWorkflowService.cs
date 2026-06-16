// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using KM.SwSh.Workflows;

namespace KM.SwSh.FashionUnlock;

public sealed class SwShFashionUnlockWorkflowService
{
    public const string ExeFsMainPath = SwShExeFsPatchWorkflowService.ExeFsMainPath;

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fashion Unlock requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShFashionUnlockWorkflow Load(OpenedProject project)
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
                "Fashion Unlock cannot load until project paths validate.",
                buildId: "unknown",
                directGetterOffsetHex: "unknown",
                mappedGetterOffsetHex: "unknown",
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
                "Fashion Unlock cannot inspect ownership checks because exefs/main is missing.",
                buildId: "unknown",
                directGetterOffsetHex: "unknown",
                mappedGetterOffsetHex: "unknown",
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
                expected: "Readable Sword or Shield 1.3.2 exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Fashion Unlock cannot inspect ownership checks because exefs/main cannot be read.",
                buildId: "unknown",
                directGetterOffsetHex: "unknown",
                mappedGetterOffsetHex: "unknown",
                stubKind: "not inspected",
                detectedGame: null,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        try
        {
            var analysis = SwShFashionUnlockMainPatcher.Analyze(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame);
            if (analysis.Kind is SwShFashionUnlockInstallKind.UnsupportedBuild
                or SwShFashionUnlockInstallKind.GameMismatch
                or SwShFashionUnlockInstallKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: entry.RelativePath,
                    expected: "Supported Sword or Shield 1.3.2 fashion ownership getter bytes"));
                return CreateWorkflow(
                    summary,
                    "blocked",
                    analysis.Message,
                    analysis.BuildId,
                    analysis.DirectGetterOffsetHex,
                    analysis.MappedGetterOffsetHex,
                    analysis.StubKind,
                    analysis.DetectedGame,
                    provenance,
                    sourceFileCount: 1,
                    diagnostics);
            }

            var installStatus = analysis.Kind == SwShFashionUnlockInstallKind.Installed
                ? "installed"
                : summary.Availability == SwShWorkflowAvailability.Available
                    ? "available"
                    : "readOnly";

            return CreateWorkflow(
                summary,
                installStatus,
                analysis.Message,
                analysis.BuildId,
                analysis.DirectGetterOffsetHex,
                analysis.MappedGetterOffsetHex,
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
                expected: "Readable Sword or Shield 1.3.2 exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Fashion Unlock cannot inspect ownership checks because exefs/main could not be read.",
                buildId: "unknown",
                directGetterOffsetHex: "unknown",
                mappedGetterOffsetHex: "unknown",
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
        return SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, targetRelativePath);
    }

    private static SwShFashionUnlockWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        string buildId,
        string directGetterOffsetHex,
        string mappedGetterOffsetHex,
        string stubKind,
        ProjectGame? detectedGame,
        SwShFashionUnlockProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var reservedRegions = SwShFashionUnlockMainPatcher.ReservedMainTextRegions()
            .Select(region => new SwShFashionUnlockReservedRegion(
                region.FeatureId,
                region.Label,
                region.OffsetLabel,
                region.StartOffset,
                region.Length,
                region.Rule))
            .ToArray();

        return new SwShFashionUnlockWorkflow(
            summary,
            installStatus,
            installMessage,
            buildId,
            directGetterOffsetHex,
            mappedGetterOffsetHex,
            stubKind,
            detectedGame,
            reservedRegions,
            provenance,
            new SwShFashionUnlockWorkflowStats(reservedRegions.Length, sourceFileCount),
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

    private static SwShFashionUnlockProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShFashionUnlockProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShFashionUnlockProvenance CreateMissingProvenance(string relativePath)
    {
        return new SwShFashionUnlockProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.FashionUnlock,
            "Fashion Unlock",
            "Advanced ExeFS editor that unlocks Sword/Shield fashion ownership checks without editing the save file.",
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
            Domain: SwShFashionUnlockEditSessionService.FashionUnlockEditDomain,
            Expected: expected);
    }
}

internal sealed record WorkflowFileSource(
    ProjectFileGraphEntry Entry,
    string AbsolutePath);
