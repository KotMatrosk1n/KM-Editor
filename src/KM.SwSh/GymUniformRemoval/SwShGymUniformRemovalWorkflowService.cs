// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using KM.SwSh.Workflows;

namespace KM.SwSh.GymUniformRemoval;

public sealed class SwShGymUniformRemovalWorkflowService
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
                    "Gym Uniform Removal requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShGymUniformRemovalWorkflow Load(OpenedProject project)
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
                "Gym Uniform Removal cannot load until project paths validate.",
                buildId: "unknown",
                patchOffsetHex: "unknown",
                stubKind: "not inspected",
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
                "Gym Uniform Removal cannot inspect the patch site because exefs/main is missing.",
                buildId: "unknown",
                patchOffsetHex: "unknown",
                stubKind: "not inspected",
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
                "Gym Uniform Removal cannot inspect the patch site because exefs/main cannot be read.",
                buildId: "unknown",
                patchOffsetHex: "unknown",
                stubKind: "not inspected",
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        try
        {
            var analysis = SwShGymUniformRemovalMainPatcher.Analyze(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame);
            if (analysis.Kind is SwShGymUniformRemovalInstallKind.UnsupportedBuild
                or SwShGymUniformRemovalInstallKind.GameMismatch
                or SwShGymUniformRemovalInstallKind.ForeignPatch
                or SwShGymUniformRemovalInstallKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    analysis.Kind == SwShGymUniformRemovalInstallKind.ForeignPatch ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    analysis.Message,
                    file: entry.RelativePath,
                    expected: "Supported Sword or Shield 1.3.2 gym uniform handler bytes"));
                return CreateWorkflow(
                    summary,
                    analysis.Kind == SwShGymUniformRemovalInstallKind.ForeignPatch ? "foreign" : "blocked",
                    analysis.Message,
                    analysis.BuildId,
                    analysis.PatchOffsetHex,
                    analysis.StubKind,
                    provenance,
                    sourceFileCount: 1,
                    diagnostics);
            }

            var ipsRelativePath = analysis.DetectedGame is null
                ? null
                : SwShGymUniformRemovalMainPatcher.IpsRelativePath(analysis.DetectedGame.Value);
            var ipsPath = ipsRelativePath is null
                ? null
                : ResolveOutputPath(project.Paths, ipsRelativePath);
            if (ipsRelativePath is not null && ipsPath is not null && File.Exists(ipsPath))
            {
                var ipsAnalysis = SwShGymUniformRemovalMainPatcher.AnalyzeIpsPatch(
                    File.ReadAllBytes(ipsPath),
                    File.ReadAllBytes(sourcePath),
                    project.Paths.SelectedGame);
                var ipsStatus = ipsAnalysis.Kind switch
                {
                    SwShGymUniformRemovalInstallKind.InstalledV1 => "installed",
                    SwShGymUniformRemovalInstallKind.InstalledCompatible => "installed",
                    SwShGymUniformRemovalInstallKind.ForeignPatch => "foreign",
                    _ => "blocked",
                };

                if (ipsAnalysis.Kind is SwShGymUniformRemovalInstallKind.ForeignPatch
                    or SwShGymUniformRemovalInstallKind.Conflict)
                {
                    diagnostics.Add(CreateDiagnostic(
                        ipsAnalysis.Kind == SwShGymUniformRemovalInstallKind.ForeignPatch ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                        ipsAnalysis.Message,
                        file: ipsRelativePath,
                        expected: "KM Gym Uniform Removal IPS patch"));
                }

                return CreateWorkflow(
                    summary,
                    ipsStatus,
                    ipsAnalysis.Message,
                    ipsAnalysis.BuildId,
                    ipsAnalysis.PatchOffsetHex,
                    ipsAnalysis.StubKind,
                    provenance,
                    sourceFileCount: 2,
                    diagnostics);
            }

            var installStatus = summary.Availability == SwShWorkflowAvailability.Available ? "available" : "readOnly";
            var installMessage = analysis.Kind is SwShGymUniformRemovalInstallKind.InstalledV1
                or SwShGymUniformRemovalInstallKind.InstalledCompatible
                ? "A direct exefs/main Gym Uniform Removal stub is present. KM now installs this editor as a build-ID IPS patch instead."
                : "Gym Uniform Removal IPS is not installed. Installing creates a build-ID IPS patch in exefs.";
            var stubKind = analysis.Kind is SwShGymUniformRemovalInstallKind.InstalledV1
                or SwShGymUniformRemovalInstallKind.InstalledCompatible
                ? analysis.StubKind + "; IPS not installed"
                : "IPS not installed";

            return CreateWorkflow(
                summary,
                installStatus,
                installMessage,
                analysis.BuildId,
                analysis.PatchOffsetHex,
                stubKind,
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
                "Gym Uniform Removal cannot inspect the patch site because exefs/main could not be read.",
                buildId: "unknown",
                patchOffsetHex: "unknown",
                stubKind: "not inspected",
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }
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

    private static SwShGymUniformRemovalWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        string buildId,
        string patchOffsetHex,
        string stubKind,
        SwShGymUniformRemovalProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var reservedRegions = SwShGymUniformRemovalMainPatcher.ReservedMainTextRegions()
            .Select(region => new SwShGymUniformRemovalReservedRegion(
                region.FeatureId,
                region.Label,
                region.OffsetLabel,
                region.StartOffset,
                region.Length,
                region.Rule))
            .ToArray();

        return new SwShGymUniformRemovalWorkflow(
            summary,
            installStatus,
            installMessage,
            buildId,
            patchOffsetHex,
            stubKind,
            reservedRegions,
            provenance,
            new SwShGymUniformRemovalWorkflowStats(reservedRegions.Length, sourceFileCount),
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

    private static SwShGymUniformRemovalProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShGymUniformRemovalProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShGymUniformRemovalProvenance CreateMissingProvenance(string relativePath)
    {
        return new SwShGymUniformRemovalProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.GymUniformRemoval,
            "Gym Uniform Removal",
            "Independent ExeFS editor that keeps gym challenge and gym leader battle scripts from changing the player into the gym uniform.",
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
            Domain: SwShGymUniformRemovalEditSessionService.GymUniformRemovalEditDomain,
            Expected: expected);
    }
}
