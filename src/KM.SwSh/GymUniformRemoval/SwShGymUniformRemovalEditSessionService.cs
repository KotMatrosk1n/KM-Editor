// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.GymUniformRemoval;

public sealed class SwShGymUniformRemovalEditSessionService
{
    public const string GymUniformRemovalEditDomain = "workflow.gymUniformRemoval";

    private const string InstallRecordId = "gym-uniform-removal-v1-install";
    private const string UninstallRecordId = "gym-uniform-removal-v1-uninstall";
    private const string InstallField = "install";
    private const string UninstallField = "uninstall";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShGymUniformRemovalWorkflowService gymUniformRemovalWorkflowService;

    public SwShGymUniformRemovalEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShGymUniformRemovalWorkflowService? gymUniformRemovalWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.gymUniformRemovalWorkflowService = gymUniformRemovalWorkflowService ?? new SwShGymUniformRemovalWorkflowService();
    }

    public SwShGymUniformRemovalEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = gymUniformRemovalWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal needs its own edit session before staging.",
                expected: "A Gym Uniform Removal-only edit session"));
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingInstallEdit(ResolveIpsRelativePath(workflow)))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Gym Uniform Removal install is staged for change-plan review."));

        return new SwShGymUniformRemovalEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShGymUniformRemovalEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = gymUniformRemovalWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal needs its own edit session before staging uninstall.",
                expected: "A Gym Uniform Removal-only edit session"));
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingUninstallEdit(ResolveIpsRelativePath(workflow)))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Gym Uniform Removal uninstall is staged for change-plan review."));

        return new SwShGymUniformRemovalEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = gymUniformRemovalWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Gym Uniform Removal install or uninstall before validating.",
                expected: "Pending Gym Uniform Removal install or uninstall"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Gym Uniform Removal.",
                    expected: GymUniformRemovalEditDomain));
                continue;
            }

            if (IsUninstallEdit(edit))
            {
                CanStageUninstall(project, workflow, paths, diagnostics);
                continue;
            }

            if (IsInstallEdit(edit))
            {
                CanStageInstall(project, workflow, diagnostics);
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Gym Uniform Removal edit '{edit.RecordId}' is not supported.",
                expected: "Gym Uniform Removal install or uninstall"));
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Gym Uniform Removal change is valid for change-plan review."));
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var targetRelativePath = ResolveIpsRelativePath(project, diagnostics);
        var targetPath = targetRelativePath is null ? null : ResolveOutputPath(paths, targetRelativePath, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var isUninstall = IsUninstallSession(session);
        var writes = new[]
        {
            new PlannedFileWrite(
                targetRelativePath!,
                [
                    new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath!),
                    new ProjectFileReference(ProjectFileLayer.Base, SwShGymUniformRemovalWorkflowService.ExeFsMainPath),
                ],
                File.Exists(targetPath),
                isUninstall
                    ? "Remove Gym Uniform Removal's build-ID IPS patch from exefs."
                    : "Install or refresh Gym Uniform Removal's build-ID IPS patch in exefs."),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Gym Uniform Removal change plan preview contains {writes.Length:N0} target file(s).")));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Gym Uniform Removal change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Gym Uniform Removal change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var pendingEdit = session.PendingEdits.Single();
        if (IsUninstallEdit(pendingEdit))
        {
            ApplyUninstall(projectWorkspaceService.Open(paths), writtenFiles, diagnostics);
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShGymUniformRemovalWorkflowService.ExeFsMainPath);
        var targetRelativePath = ResolveIpsRelativePath(project, diagnostics);
        var targetPath = targetRelativePath is null ? null : ResolveOutputPath(paths, targetRelativePath, diagnostics);
        if (source is null || targetRelativePath is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal source or IPS output target could not be resolved.",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Readable source and writable build-ID IPS target"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var output = SwShGymUniformRemovalMainPatcher.CreateIpsPatch(
                File.ReadAllBytes(source.AbsolutePath),
                paths.SelectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Gym Uniform Removal IPS changes to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal IPS file could not be created: {exception.Message}",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Supported Sword or Shield 1.3.2 exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal output file could not be written: {exception.Message}",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal output file could not be written: {exception.Message}",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static void ApplyUninstall(
        OpenedProject project,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = ResolveWorkflowFile(project, SwShGymUniformRemovalWorkflowService.ExeFsMainPath);
        var targetRelativePath = ResolveIpsRelativePath(project, diagnostics);
        var targetPath = targetRelativePath is null ? null : ResolveOutputPath(project.Paths, targetRelativePath, diagnostics);
        if (source is null || targetRelativePath is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal uninstall could not resolve source main or IPS target.",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Readable source main and existing build-ID IPS target"));
            return;
        }

        if (!File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal uninstall target no longer exists. Review the change plan again before applying.",
                file: targetRelativePath,
                expected: "Existing reviewed Gym Uniform Removal IPS patch"));
            return;
        }

        try
        {
            var ipsAnalysis = SwShGymUniformRemovalMainPatcher.AnalyzeIpsPatch(
                File.ReadAllBytes(targetPath),
                File.ReadAllBytes(source.AbsolutePath),
                project.Paths.SelectedGame);
            if (ipsAnalysis.Kind is not (SwShGymUniformRemovalInstallKind.InstalledV1
                or SwShGymUniformRemovalInstallKind.InstalledCompatible))
            {
                throw new InvalidDataException(ipsAnalysis.Message);
            }

            File.Delete(targetPath);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Gym Uniform Removal IPS from the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal uninstall could not remove IPS patch: {exception.Message}",
                file: targetRelativePath,
                expected: "KM Gym Uniform Removal IPS patch"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal uninstall could not update the output file: {exception.Message}",
                file: targetRelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal uninstall could not update the output file: {exception.Message}",
                file: targetRelativePath,
                expected: "Writable output root"));
        }
    }

    private static bool CanStageInstall(
        OpenedProject project,
        SwShGymUniformRemovalWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal install requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        if (workflow.InstallStatus is "blocked" or "foreign")
        {
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Gym Uniform Removal cannot stage while exefs/main has an unsupported build or conflicting gym uniform handler bytes.",
                    expected: "Supported Sword or Shield 1.3.2 handler bytes"));
            }

            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SwShGymUniformRemovalWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal uninstall requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        if (!string.Equals(workflow.InstallStatus, "installed", StringComparison.Ordinal))
        {
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Gym Uniform Removal is not installed in the current project output.",
                    expected: "Installed Gym Uniform Removal stub"));
            }

            return false;
        }

        var targetRelativePath = ResolveIpsRelativePath(project, diagnostics);
        var targetPath = targetRelativePath is null ? null : ResolveOutputPath(paths, targetRelativePath, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal uninstall can only remove a generated build-ID IPS patch.",
                file: targetRelativePath ?? SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Gym Uniform Removal IPS installed in the configured output root"));
            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static PendingEdit CreatePendingInstallEdit(string patchRelativePath)
    {
        return new PendingEdit(
            GymUniformRemovalEditDomain,
            "Stage Gym Uniform Removal install.",
            [
                new ProjectFileReference(ProjectFileLayer.Generated, patchRelativePath),
                new ProjectFileReference(ProjectFileLayer.Base, SwShGymUniformRemovalWorkflowService.ExeFsMainPath),
            ],
            InstallRecordId,
            InstallField,
            "true");
    }

    private static PendingEdit CreatePendingUninstallEdit(string patchRelativePath)
    {
        return new PendingEdit(
            GymUniformRemovalEditDomain,
            "Stage Gym Uniform Removal uninstall.",
            [
                new ProjectFileReference(ProjectFileLayer.Generated, patchRelativePath),
                new ProjectFileReference(ProjectFileLayer.Base, SwShGymUniformRemovalWorkflowService.ExeFsMainPath),
            ],
            UninstallRecordId,
            UninstallField,
            "true");
    }

    private static bool IsInstallEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, InstallRecordId, StringComparison.Ordinal);
    }

    private static bool IsUninstallSession(EditSession session)
    {
        return session.PendingEdits.Count == 1 && IsUninstallEdit(session.PendingEdits[0]);
    }

    private static bool IsUninstallEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, UninstallRecordId, StringComparison.Ordinal);
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShGymUniformRemovalWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static string ResolveIpsRelativePath(SwShGymUniformRemovalWorkflow workflow)
    {
        var relativePath = SwShGymUniformRemovalMainPatcher.TryGetIpsRelativePath(workflow.BuildId);
        return relativePath ?? SwShGymUniformRemovalWorkflowService.ExeFsMainPath;
    }

    private static string? ResolveIpsRelativePath(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = ResolveWorkflowFile(project, SwShGymUniformRemovalWorkflowService.ExeFsMainPath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal could not resolve exefs/main to choose the IPS filename.",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Readable Sword or Shield 1.3.2 exefs/main NSO"));
            return null;
        }

        try
        {
            var analysis = SwShGymUniformRemovalMainPatcher.Analyze(
                File.ReadAllBytes(source.AbsolutePath),
                project.Paths.SelectedGame);
            if (analysis.DetectedGame is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                    expected: "Supported Sword or Shield 1.3.2 exefs/main NSO"));
                return null;
            }

            return SwShGymUniformRemovalMainPatcher.IpsRelativePath(analysis.DetectedGame.Value);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal could not choose an IPS filename: {exception.Message}",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Supported Sword or Shield 1.3.2 exefs/main NSO"));
            return null;
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal could not read exefs/main to choose the IPS filename: {exception.Message}",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Readable source main"));
            return null;
        }
    }

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        if (!reviewedPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedTargets = reviewedPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentTargets = currentPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return reviewedTargets.SequenceEqual(currentTargets, StringComparer.Ordinal);
    }

    private static ApplyResult CreateApplyResult(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan currentPlan,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics);
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = SwShGymUniformRemovalWorkflowService.ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: GymUniformRemovalEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
