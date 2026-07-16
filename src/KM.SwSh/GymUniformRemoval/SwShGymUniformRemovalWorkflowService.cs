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
    public const int OwnedByteCount = SwShGymUniformRemovalMainPatcher.PatchLength;

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

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Gym Uniform Removal requires Pokemon Sword or Pokemon Shield to be selected before it can load.",
                    expected: "Selected Pokemon Sword or Pokemon Shield project"));
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
        var defaultAnalysis = CreateDefaultAnalysis();

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            var message = project.Health.CanOpenReadOnlyWorkflows
                && !IsSupportedGame(project.Paths.SelectedGame)
                    ? "Gym Uniform Removal cannot load until Pokemon Sword or Pokemon Shield is selected."
                    : "Gym Uniform Removal cannot load until project paths validate.";
            return CreateWorkflow(
                summary,
                "disabled",
                message,
                canUninstall: false,
                defaultAnalysis,
                ipsArtifactState: "notInspected",
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
                "Gym Uniform Removal cannot inspect the patch site because exefs/main is missing.",
                canUninstall: false,
                defaultAnalysis,
                ipsArtifactState: "notInspected",
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
                "Gym Uniform Removal cannot verify the selected-game vanilla base exefs/main.",
                canUninstall: false,
                defaultAnalysis,
                ipsArtifactState: "notInspected",
                project.Paths.SelectedGame,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        byte[] baseBytes;
        SwShGymUniformRemovalAnalysis baseAnalysis;
        try
        {
            baseBytes = File.ReadAllBytes(basePath);
            baseAnalysis = SwShGymUniformRemovalMainPatcher.Analyze(
                baseBytes,
                project.Paths.SelectedGame);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Base exefs/main could not be verified for Gym Uniform Removal: {exception.Message}",
                file: entry.RelativePath,
                expected: "Readable selected-game vanilla Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Gym Uniform Removal cannot verify the selected-game vanilla base exefs/main.",
                canUninstall: false,
                defaultAnalysis,
                ipsArtifactState: "notInspected",
                project.Paths.SelectedGame,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        if (baseAnalysis.Kind != SwShGymUniformRemovalInstallKind.NotInstalled)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Base exefs/main is not a selected-game vanilla Gym Uniform Removal source. {baseAnalysis.Message}",
                file: entry.RelativePath,
                expected: "Selected-game Sword/Shield 1.3.2 base exefs/main with the vanilla gym outfit handler"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Gym Uniform Removal requires a verified selected-game vanilla base exefs/main.",
                canUninstall: false,
                baseAnalysis,
                ipsArtifactState: "notInspected",
                project.Paths.SelectedGame,
                provenance,
                sourceFileCount: 0,
                diagnostics);
        }

        SwShGymUniformRemovalAnalysis effectiveAnalysis;
        var sourceFileCount = 1;
        if (effectivePath is null || !File.Exists(effectivePath))
        {
            effectiveAnalysis = CreateUnreadableEffectiveAnalysis(
                baseAnalysis,
                "The effective exefs/main could not be resolved from the project graph.");
        }
        else if (PathsReferToSameFile(basePath, effectivePath))
        {
            effectiveAnalysis = baseAnalysis;
        }
        else
        {
            effectiveAnalysis = CreateUnreadableEffectiveAnalysis(
                baseAnalysis,
                "The effective exefs/main is empty and cannot be inspected.");
            byte[] effectiveBytes;
            try
            {
                effectiveBytes = File.ReadAllBytes(effectivePath);
                sourceFileCount++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                effectiveBytes = [];
                effectiveAnalysis = CreateUnreadableEffectiveAnalysis(
                    baseAnalysis,
                    $"The effective exefs/main could not be read. {exception.Message}");
            }

            if (effectiveBytes.Length > 0)
            {
                try
                {
                    effectiveAnalysis = SwShGymUniformRemovalMainPatcher.Analyze(
                        effectiveBytes,
                        project.Paths.SelectedGame);
                    SwShGymUniformRemovalMainPatcher.EnsureCompatibleExecutableIdentity(
                        baseBytes,
                        effectiveBytes);
                }
                catch (Exception exception) when (exception is InvalidDataException
                    or ArgumentException
                    or OverflowException)
                {
                    effectiveAnalysis = baseAnalysis with
                    {
                        Kind = SwShGymUniformRemovalInstallKind.Conflict,
                        Message = $"Effective exefs/main is not compatible with the verified base. {effectiveAnalysis.Message} {exception.Message}",
                        MainHandlerState = "conflict",
                    };
                }
            }
        }

        var ipsRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(
            project.Paths.SelectedGame!.Value);
        var ipsPath = ResolveOutputPath(project.Paths, ipsRelativePath);
        SwShGymUniformRemovalIpsAnalysis? ipsAnalysis = null;
        if (ipsPath is not null && File.Exists(ipsPath))
        {
            byte[] ipsBytes;
            try
            {
                ipsBytes = File.ReadAllBytes(ipsPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Gym Uniform Removal IPS could not be read: {exception.Message}",
                    file: ipsRelativePath,
                    expected: "Readable Gym Uniform Removal IPS"));
                AddEffectiveAnalysisDiagnostic(
                    effectiveAnalysis,
                    entry.RelativePath,
                    diagnostics);
                return CreateWorkflow(
                    summary,
                    "blocked",
                    "Gym Uniform Removal found an IPS artifact that could not be read.",
                    canUninstall: false,
                    effectiveAnalysis,
                    ipsArtifactState: "notInspected",
                    project.Paths.SelectedGame,
                    provenance,
                    sourceFileCount,
                    diagnostics);
            }

            sourceFileCount++;
            try
            {
                ipsAnalysis = SwShGymUniformRemovalMainPatcher.AnalyzeIpsArtifact(
                    ipsBytes,
                    baseBytes,
                    project.Paths.SelectedGame);
            }
            catch (Exception exception) when (exception is InvalidDataException
                or ArgumentException
                or OverflowException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Gym Uniform Removal IPS could not be verified: {exception.Message}",
                    file: ipsRelativePath,
                    expected: "Exact current or recognized legacy Gym Uniform Removal IPS"));
                AddEffectiveAnalysisDiagnostic(
                    effectiveAnalysis,
                    entry.RelativePath,
                    diagnostics);
                return CreateWorkflow(
                    summary,
                    "blocked",
                    "Gym Uniform Removal found a readable IPS artifact that could not be verified.",
                    canUninstall: false,
                    effectiveAnalysis,
                    ipsArtifactState: "invalid",
                    project.Paths.SelectedGame,
                    provenance,
                    sourceFileCount,
                    diagnostics);
            }
        }

        AddEffectiveAnalysisDiagnostic(effectiveAnalysis, entry.RelativePath, diagnostics);
        AddIpsAnalysisDiagnostic(ipsAnalysis, ipsRelativePath, diagnostics);

        var effectiveIsEditable = effectiveAnalysis.Kind is SwShGymUniformRemovalInstallKind.NotInstalled
            or SwShGymUniformRemovalInstallKind.InstalledV1
            or SwShGymUniformRemovalInstallKind.InstalledCompatible;
        var ipsIsOwned = ipsAnalysis?.Kind is SwShGymUniformRemovalIpsArtifactKind.Current
            or SwShGymUniformRemovalIpsArtifactKind.Legacy;
        var canUninstall = summary.Availability == SwShWorkflowAvailability.Available && ipsIsOwned;
        var installStatus = ipsAnalysis?.Kind == SwShGymUniformRemovalIpsArtifactKind.Invalid
            ? "blocked"
            : !effectiveIsEditable
                ? effectiveAnalysis.Kind == SwShGymUniformRemovalInstallKind.ForeignPatch && !ipsIsOwned
                    ? "foreign"
                    : "blocked"
            : ipsAnalysis?.Kind switch
            {
                SwShGymUniformRemovalIpsArtifactKind.Current or SwShGymUniformRemovalIpsArtifactKind.Legacy => "installed",
                SwShGymUniformRemovalIpsArtifactKind.Foreign => "foreign",
                SwShGymUniformRemovalIpsArtifactKind.Invalid => "blocked",
                _ => summary.Availability == SwShWorkflowAvailability.Available ? "available" : "readOnly",
            };
        var installMessage = CreateInstallMessage(
            effectiveAnalysis,
            ipsAnalysis,
            canUninstall);

        return CreateWorkflow(
            summary,
            installStatus,
            installMessage,
            canUninstall,
            effectiveAnalysis,
            ipsAnalysis?.ArtifactState ?? "notPresent",
            project.Paths.SelectedGame,
            provenance,
            sourceFileCount,
            diagnostics);
    }

    internal IReadOnlyList<ProjectFileReference> GetPlanSources(
        OpenedProject project,
        string ipsRelativePath,
        bool isUninstall)
    {
        ArgumentNullException.ThrowIfNull(project);

        var entry = FindEntry(project, ExeFsMainPath);
        if (entry is null)
        {
            return Array.Empty<ProjectFileReference>();
        }

        var sources = new List<ProjectFileReference>(3);
        if (entry.BaseFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, ExeFsMainPath));
        }

        if (!isUninstall && entry.LayeredFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Layered, ExeFsMainPath));
        }

        sources.Add(new ProjectFileReference(ProjectFileLayer.Generated, ipsRelativePath));
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

    private static SwShGymUniformRemovalWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        bool canUninstall,
        SwShGymUniformRemovalAnalysis analysis,
        string ipsArtifactState,
        ProjectGame? activeGame,
        SwShGymUniformRemovalProvenance provenance,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var reservedRegions = !IsSupportedGame(activeGame)
            ? Array.Empty<SwShGymUniformRemovalReservedRegion>()
            : SwShGymUniformRemovalMainPatcher.ReservedMainTextRegions(activeGame!.Value)
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
            canUninstall,
            analysis.BuildId,
            analysis.PatchOffsetHex,
            ToMainHandlerState(analysis),
            ipsArtifactState,
            analysis.DetectedGame,
            reservedRegions,
            provenance,
            new SwShGymUniformRemovalWorkflowStats(
                reservedRegions.Length,
                sourceFileCount,
                reservedRegions.Sum(region => region.Length ?? 0)),
            diagnostics);
    }

    private static SwShGymUniformRemovalAnalysis CreateDefaultAnalysis(
        SwShGymUniformRemovalAnalysis? identity = null)
    {
        return new SwShGymUniformRemovalAnalysis(
            SwShGymUniformRemovalInstallKind.NotInstalled,
            "Gym Uniform Removal has not been inspected.",
            identity?.BuildId ?? "unknown",
            identity?.PatchOffsetHex ?? "unknown",
            "notInspected",
            identity?.DetectedGame);
    }

    private static SwShGymUniformRemovalAnalysis CreateUnreadableEffectiveAnalysis(
        SwShGymUniformRemovalAnalysis verifiedBase,
        string message)
    {
        return verifiedBase with
        {
            Kind = SwShGymUniformRemovalInstallKind.Conflict,
            Message = message,
            MainHandlerState = "unreadable",
        };
    }

    private static string ToMainHandlerState(SwShGymUniformRemovalAnalysis analysis)
    {
        if (string.Equals(analysis.MainHandlerState, "notInspected", StringComparison.Ordinal))
        {
            return "notInspected";
        }

        if (string.Equals(analysis.MainHandlerState, "unreadable", StringComparison.Ordinal))
        {
            return "unreadable";
        }

        return analysis.Kind switch
        {
            SwShGymUniformRemovalInstallKind.NotInstalled => "vanilla",
            SwShGymUniformRemovalInstallKind.InstalledV1 => "kmReturnTrue",
            SwShGymUniformRemovalInstallKind.InstalledCompatible => "compatibleReturnTrue",
            SwShGymUniformRemovalInstallKind.UnsupportedBuild => "unsupported",
            SwShGymUniformRemovalInstallKind.GameMismatch => "gameMismatch",
            SwShGymUniformRemovalInstallKind.ForeignPatch => "foreign",
            _ => "conflict",
        };
    }

    private static string CreateInstallMessage(
        SwShGymUniformRemovalAnalysis effectiveAnalysis,
        SwShGymUniformRemovalIpsAnalysis? ipsAnalysis,
        bool canUninstall)
    {
        if (ipsAnalysis is not null)
        {
            if (effectiveAnalysis.Kind is SwShGymUniformRemovalInstallKind.UnsupportedBuild
                or SwShGymUniformRemovalInstallKind.GameMismatch
                or SwShGymUniformRemovalInstallKind.ForeignPatch
                or SwShGymUniformRemovalInstallKind.Conflict)
            {
                return ipsAnalysis.Kind is SwShGymUniformRemovalIpsArtifactKind.Current
                    or SwShGymUniformRemovalIpsArtifactKind.Legacy
                    ? canUninstall
                        ? ipsAnalysis.Message
                            + " The effective exefs/main conflict blocks install or refresh, but the owned IPS can still be uninstalled."
                        : ipsAnalysis.Message
                            + " The effective exefs/main conflict blocks install or refresh, and uninstall requires an editable output root."
                    : ipsAnalysis.Message
                        + " The effective exefs/main also conflicts with the selected-game mapping.";
            }

            return ipsAnalysis.Message;
        }

        if (effectiveAnalysis.Kind is SwShGymUniformRemovalInstallKind.UnsupportedBuild
            or SwShGymUniformRemovalInstallKind.GameMismatch
            or SwShGymUniformRemovalInstallKind.ForeignPatch
            or SwShGymUniformRemovalInstallKind.Conflict)
        {
            return effectiveAnalysis.Message + " Gym Uniform Removal install or refresh is blocked.";
        }

        return effectiveAnalysis.Kind is SwShGymUniformRemovalInstallKind.InstalledV1
            or SwShGymUniformRemovalInstallKind.InstalledCompatible
            ? "A direct exefs/main Gym Uniform Removal stub is present. KM installs this editor as a build-ID IPS patch, which is not currently present."
            : "Gym Uniform Removal IPS is not installed. Installing creates the selected build-ID IPS patch in exefs.";
    }

    private static void AddEffectiveAnalysisDiagnostic(
        SwShGymUniformRemovalAnalysis analysis,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (analysis.Kind is not (SwShGymUniformRemovalInstallKind.UnsupportedBuild
            or SwShGymUniformRemovalInstallKind.GameMismatch
            or SwShGymUniformRemovalInstallKind.ForeignPatch
            or SwShGymUniformRemovalInstallKind.Conflict))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            analysis.Message,
            file: relativePath,
            expected: "Selected-game effective exefs/main with vanilla or recognized Gym Uniform Removal handler bytes"));
    }

    private static void AddIpsAnalysisDiagnostic(
        SwShGymUniformRemovalIpsAnalysis? analysis,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (analysis?.Kind is not (SwShGymUniformRemovalIpsArtifactKind.Foreign
            or SwShGymUniformRemovalIpsArtifactKind.Invalid))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            analysis.Kind == SwShGymUniformRemovalIpsArtifactKind.Foreign
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error,
            analysis.Message,
            file: relativePath,
            expected: "Exact current or recognized legacy Gym Uniform Removal IPS"));
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

    private static bool PathsReferToSameFile(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static SwShGymUniformRemovalProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        return new SwShGymUniformRemovalProvenance(
            entry.RelativePath,
            entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
            entry.State);
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
