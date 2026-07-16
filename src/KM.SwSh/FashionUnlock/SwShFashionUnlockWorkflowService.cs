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
    public const int OwnedByteCount = SwShFashionUnlockMainPatcher.PatchLength * 2;

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

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fashion Unlock requires Pokemon Sword or Pokemon Shield to be selected before it can load.",
                    expected: "Selected Pokemon Sword or Pokemon Shield project"));
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
            var message = project.Health.CanOpenReadOnlyWorkflows
                && !IsSupportedGame(project.Paths.SelectedGame)
                    ? "Fashion Unlock cannot load until Pokemon Sword or Pokemon Shield is selected."
                    : "Fashion Unlock cannot load until project paths validate.";
            return CreateWorkflow(
                summary,
                "disabled",
                message,
                CreateDefaultAnalysis(),
                project.Paths.SelectedGame,
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
                CreateDefaultAnalysis(),
                project.Paths.SelectedGame,
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
                "Fashion Unlock cannot verify the selected-game vanilla base exefs/main.",
                CreateDefaultAnalysis(),
                project.Paths.SelectedGame,
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
                "Fashion Unlock cannot inspect the effective exefs/main.",
                CreateDefaultAnalysis(),
                project.Paths.SelectedGame,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        SwShFashionUnlockAnalysis baseAnalysis;
        SwShFashionUnlockAnalysis effectiveAnalysis;
        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            baseAnalysis = SwShFashionUnlockMainPatcher.Analyze(baseBytes, project.Paths.SelectedGame);
            if (PathsReferToSameFile(basePath, effectivePath))
            {
                effectiveAnalysis = baseAnalysis;
            }
            else
            {
                var effectiveBytes = File.ReadAllBytes(effectivePath);
                effectiveAnalysis = SwShFashionUnlockMainPatcher.Analyze(
                    effectiveBytes,
                    project.Paths.SelectedGame);
                SwShFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, effectiveBytes);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS main sources could not be verified for Fashion Unlock: {exception.Message}",
                file: entry.RelativePath,
                expected: "Readable selected-game base and effective Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Fashion Unlock cannot inspect ownership checks because the base and effective exefs/main sources are not verified as compatible.",
                CreateDefaultAnalysis(),
                project.Paths.SelectedGame,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        var sourcesAreSameFile = PathsReferToSameFile(basePath, effectivePath);
        var baseIsVerified = baseAnalysis.Kind == SwShFashionUnlockInstallKind.NotInstalled;
        var effectiveIsVerified = effectiveAnalysis.Kind is SwShFashionUnlockInstallKind.NotInstalled
            or SwShFashionUnlockInstallKind.Installed;
        var sourceFileCount = (baseIsVerified ? 1 : 0)
            + (!sourcesAreSameFile && effectiveIsVerified ? 1 : 0);
        if (baseAnalysis.Kind != SwShFashionUnlockInstallKind.NotInstalled)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Base exefs/main is not a selected-game vanilla Fashion Unlock source. {baseAnalysis.Message}",
                file: entry.RelativePath,
                expected: "Selected-game Sword/Shield 1.3.2 base exefs/main with vanilla Fashion Unlock getter entries"));
            AddEffectiveAnalysisDiagnostic(effectiveAnalysis, entry.RelativePath, diagnostics);
            return CreateWorkflow(
                summary,
                "blocked",
                "Fashion Unlock requires a verified selected-game vanilla base exefs/main before it can install or restore the effective source.",
                effectiveAnalysis,
                project.Paths.SelectedGame,
                provenance,
                sourceFileCount,
                diagnostics);
        }

        AddEffectiveAnalysisDiagnostic(effectiveAnalysis, entry.RelativePath, diagnostics);
        var installStatus = effectiveAnalysis.Kind switch
        {
            SwShFashionUnlockInstallKind.Installed => "installed",
            SwShFashionUnlockInstallKind.NotInstalled => summary.Availability == SwShWorkflowAvailability.Available
                ? "available"
                : "readOnly",
            _ => "blocked",
        };
        var canUninstall = summary.Availability == SwShWorkflowAvailability.Available
            && effectiveAnalysis.Kind == SwShFashionUnlockInstallKind.Installed
            && entry.LayeredFile is not null
            && ResolveOutputPath(project.Paths, ExeFsMainPath) is { } outputPath
            && File.Exists(outputPath);

        return CreateWorkflow(
            summary,
            installStatus,
            effectiveAnalysis.Message,
            effectiveAnalysis,
            project.Paths.SelectedGame,
            provenance,
            sourceFileCount,
            diagnostics,
            canUninstall);
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
        return targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
            ? CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..])
            : null;
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        return SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, targetRelativePath);
    }

    internal static bool IsSupportedGame(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    private static SwShFashionUnlockWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        SwShFashionUnlockAnalysis analysis,
        ProjectGame? activeGame,
        SwShFashionUnlockProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        bool canUninstall = false)
    {
        var reservedRegions = !IsSupportedGame(activeGame)
            ? Array.Empty<SwShFashionUnlockReservedRegion>()
            : SwShFashionUnlockMainPatcher.ReservedMainTextRegions(activeGame!.Value)
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
            canUninstall,
            analysis.BuildId,
            analysis.DirectGetterOffsetHex,
            analysis.MappedGetterOffsetHex,
            analysis.StubKind,
            analysis.DetectedGame,
            reservedRegions,
            provenance,
            new SwShFashionUnlockWorkflowStats(
                reservedRegions.Length,
                sourceFileCount,
                reservedRegions.Sum(region => region.Length ?? 0)),
            diagnostics);
    }

    private static SwShFashionUnlockAnalysis CreateDefaultAnalysis()
    {
        return new SwShFashionUnlockAnalysis(
            SwShFashionUnlockInstallKind.NotInstalled,
            "Fashion Unlock is not installed.",
            BuildId: "unknown",
            DirectGetterOffsetHex: "unknown",
            MappedGetterOffsetHex: "unknown",
            StubKind: "not inspected",
            DetectedGame: null);
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
        return new SwShFashionUnlockProvenance(
            entry.RelativePath,
            entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
            entry.State);
    }

    private static SwShFashionUnlockProvenance CreateMissingProvenance(string relativePath)
    {
        return new SwShFashionUnlockProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static void AddEffectiveAnalysisDiagnostic(
        SwShFashionUnlockAnalysis analysis,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (analysis.Kind is not (SwShFashionUnlockInstallKind.UnsupportedBuild
            or SwShFashionUnlockInstallKind.GameMismatch
            or SwShFashionUnlockInstallKind.Conflict))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            analysis.Message,
            file: relativePath,
            expected: "Selected-game Sword/Shield 1.3.2 effective exefs/main with vanilla Fashion Unlock getter entries or an exact KM install"));
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
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
