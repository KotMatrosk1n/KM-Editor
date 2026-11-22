// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.ExeFs;
using KM.ZA.Workflows;

namespace KM.ZA.FashionUnlock;

public sealed class ZaFashionUnlockWorkflowService
{
    public const string ExeFsMainPath = "exefs/main";
    public const int OwnedByteCount = ZaFashionUnlockMainPatcher.OwnedByteCount;

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                ZaWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fashion Unlock requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                ZaWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fashion Unlock requires Pokemon Legends Z-A to be selected before it can load.",
                    expected: "Selected Pokemon Legends Z-A project"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? ZaWorkflowAvailability.Available
            : ZaWorkflowAvailability.ReadOnly);
    }

    public ZaFashionUnlockWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var provenance = CreateMissingProvenance(ExeFsMainPath);

        if (summary.Availability == ZaWorkflowAvailability.Disabled)
        {
            var message = project.Health.CanOpenReadOnlyWorkflows
                && !IsSupportedGame(project.Paths.SelectedGame)
                    ? "Fashion Unlock cannot load until Pokemon Legends Z-A is selected."
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
                expected: "Readable selected-game vanilla Z-A exefs/main NSO"));
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

        ZaFashionUnlockAnalysis baseAnalysis;
        ZaFashionUnlockAnalysis effectiveAnalysis;
        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            baseAnalysis = ZaFashionUnlockMainPatcher.Analyze(baseBytes, project.Paths.SelectedGame);
            if (PathsReferToSameFile(basePath, effectivePath))
            {
                effectiveAnalysis = baseAnalysis;
            }
            else
            {
                var effectiveBytes = File.ReadAllBytes(effectivePath);
                effectiveAnalysis = ZaFashionUnlockMainPatcher.Analyze(
                    effectiveBytes,
                    project.Paths.SelectedGame);
                ZaFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, effectiveBytes);
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
                expected: "Readable selected-game base and effective Z-A exefs/main NSO"));
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
        var baseIsVerified = baseAnalysis.Kind == ZaFashionUnlockInstallKind.NotInstalled;
        var effectiveIsVerified = effectiveAnalysis.Kind is ZaFashionUnlockInstallKind.NotInstalled
            or ZaFashionUnlockInstallKind.Installed;
        var sourceFileCount = (baseIsVerified ? 1 : 0)
            + (!sourcesAreSameFile && effectiveIsVerified ? 1 : 0);
        if (baseAnalysis.Kind != ZaFashionUnlockInstallKind.NotInstalled)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Base exefs/main is not a selected-game vanilla Fashion Unlock source. {baseAnalysis.Message}",
                file: entry.RelativePath,
                expected: "Selected-game Z-A 2.0.2 base exefs/main with vanilla Fashion Unlock wardrobe instructions"));
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
        ZaFashionUnlockSalon? salon = null;
        if (effectiveIsVerified)
        {
            try
            {
                salon = ZaFashionUnlockSalon.Read(project);
            }
            catch (Exception exception) when (exception is InvalidDataException
                or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fashion Unlock could not verify the Hair Salon lineup. {exception.Message}",
                    file: $"romfs/{ZaFashionUnlockSalon.VirtualPath}"));
            }
        }

        var installStatus = effectiveAnalysis.Kind switch
        {
            _ when salon is null => "blocked",
            ZaFashionUnlockInstallKind.Installed when salon.IsUnlocked => "installed",
            ZaFashionUnlockInstallKind.Installed => summary.Availability == ZaWorkflowAvailability.Available
                ? "available"
                : "readOnly",
            ZaFashionUnlockInstallKind.NotInstalled => summary.Availability == ZaWorkflowAvailability.Available
                ? "available"
                : "readOnly",
            _ => "blocked",
        };
        var canUninstall = summary.Availability == ZaWorkflowAvailability.Available
            && effectiveAnalysis.Kind == ZaFashionUnlockInstallKind.Installed
            && salon is not null
            && entry.LayeredFile is not null
            && ResolveOutputPath(project.Paths, ExeFsMainPath) is { } outputPath
            && File.Exists(outputPath);

        var installMessage = effectiveAnalysis.Kind == ZaFashionUnlockInstallKind.Installed
            ? salon switch
            {
                { IsUnlocked: true } => "Fashion Unlock is installed for the wardrobe and the five Hair Salon quest hairstyles.",
                { IsUnlocked: false } => "Wardrobe unlock is installed. Stage an install to add the five quest hairstyles to the Hair Salon.",
                _ => effectiveAnalysis.Message,
            }
            : effectiveAnalysis.Message;
        return CreateWorkflow(
            summary,
            installStatus,
            installMessage,
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
        return ZaExeFsMainFileResolver.ResolveOutputPath(paths);
    }

    internal static bool IsSupportedGame(ProjectGame? game)
    {
        return game == ProjectGame.ZA;
    }

    private static ZaFashionUnlockWorkflow CreateWorkflow(
        ZaWorkflowSummary summary,
        string installStatus,
        string installMessage,
        ZaFashionUnlockAnalysis analysis,
        ProjectGame? activeGame,
        ZaFashionUnlockProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        bool canUninstall = false)
    {
        var reservedRegions = !IsSupportedGame(activeGame)
            ? Array.Empty<ZaFashionUnlockReservedRegion>()
            : ZaFashionUnlockMainPatcher.ReservedMainTextRegions()
                .Select(region => new ZaFashionUnlockReservedRegion(
                    region.FeatureId,
                    region.Label,
                    region.OffsetLabel,
                    region.StartOffset,
                    region.Length,
                    region.Rule))
                .ToArray();

        return new ZaFashionUnlockWorkflow(
            summary,
            installStatus,
            installMessage,
            canUninstall,
            analysis.BuildId,
            analysis.DetectedGame is null ? "unknown" : "main.text+0x02BB2A44",
            analysis.StubKind,
            analysis.DetectedGame,
            reservedRegions,
            provenance,
            new ZaFashionUnlockWorkflowStats(
                reservedRegions.Length,
                sourceFileCount,
                reservedRegions.Sum(region => region.Length ?? 0)),
            diagnostics);
    }

    private static ZaFashionUnlockAnalysis CreateDefaultAnalysis()
    {
        return new ZaFashionUnlockAnalysis(
            ZaFashionUnlockInstallKind.NotInstalled,
            "Fashion Unlock is not installed.",
            BuildId: "unknown",
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

    private static ZaFashionUnlockProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        return new ZaFashionUnlockProvenance(
            entry.RelativePath,
            entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
            entry.State);
    }

    private static ZaFashionUnlockProvenance CreateMissingProvenance(string relativePath)
    {
        return new ZaFashionUnlockProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static void AddEffectiveAnalysisDiagnostic(
        ZaFashionUnlockAnalysis analysis,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (analysis.Kind is not (ZaFashionUnlockInstallKind.UnsupportedBuild
            or ZaFashionUnlockInstallKind.GameMismatch
            or ZaFashionUnlockInstallKind.Conflict))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            analysis.Message,
            file: relativePath,
            expected: "Selected-game Z-A 2.0.2 effective exefs/main with vanilla Fashion Unlock wardrobe instructions or an exact KM install"));
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static ZaWorkflowSummary CreateSummary(
        ZaWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new ZaWorkflowSummary(
            ZaWorkflowIds.FashionUnlock,
            "Fashion Unlock",
            "Makes Z-A clothing available in the wardrobe and unlocks quest hairstyles at the Hair Salon.",
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
            Domain: ZaFashionUnlockEditSessionService.FashionUnlockEditDomain,
            Expected: expected);
    }
}
