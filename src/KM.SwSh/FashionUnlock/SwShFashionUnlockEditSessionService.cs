// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.FashionUnlock;

public sealed class SwShFashionUnlockEditSessionService
{
    public const string FashionUnlockEditDomain = "workflow.fashionUnlock";

    private const string InstallRecordId = "fashion-unlock-v1-install";
    private const string UninstallRecordId = "fashion-unlock-v1-uninstall";
    private const string InstallField = "install";
    private const string UninstallField = "uninstall";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShFashionUnlockWorkflowService fashionUnlockWorkflowService;

    public SwShFashionUnlockEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShFashionUnlockWorkflowService? fashionUnlockWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fashionUnlockWorkflowService = fashionUnlockWorkflowService ?? new SwShFashionUnlockWorkflowService();
    }

    public SwShFashionUnlockEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock needs its own edit session before staging.",
                expected: "A Fashion Unlock-only edit session"));
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        var source = SwShFashionUnlockWorkflowService.ResolveWorkflowFile(
            project,
            SwShFashionUnlockWorkflowService.ExeFsMainPath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock source could not be resolved.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source"));
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingInstallEdit([CreateSourceReference(source.Entry)]))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fashion Unlock install is staged for change-plan review."));

        return new SwShFashionUnlockEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShFashionUnlockEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock needs its own edit session before staging uninstall.",
                expected: "A Fashion Unlock-only edit session"));
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingUninstallEdit())
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fashion Unlock uninstall is staged for change-plan review."));

        return new SwShFashionUnlockEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Fashion Unlock install or uninstall before validating.",
                expected: "Pending Fashion Unlock install or uninstall"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Fashion Unlock.",
                    expected: FashionUnlockEditDomain));
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
                $"Pending Fashion Unlock edit '{edit.RecordId}' is not supported.",
                expected: "Fashion Unlock install or uninstall"));
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Fashion Unlock change is valid for change-plan review."));
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

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var isUninstall = IsUninstallSession(session);
        var project = projectWorkspaceService.Open(paths);
        var source = SwShFashionUnlockWorkflowService.ResolveWorkflowFile(
            project,
            SwShFashionUnlockWorkflowService.ExeFsMainPath);
        if (!isUninstall && source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock source could not be resolved.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new[]
        {
            new PlannedFileWrite(
                SwShFashionUnlockWorkflowService.ExeFsMainPath,
                isUninstall
                    ? [
                        new ProjectFileReference(ProjectFileLayer.Generated, SwShFashionUnlockWorkflowService.ExeFsMainPath),
                        new ProjectFileReference(ProjectFileLayer.Base, SwShFashionUnlockWorkflowService.ExeFsMainPath),
                    ]
                    : [CreateSourceReference(source!.Entry)],
                File.Exists(targetPath),
                isUninstall
                    ? "Uninstall Fashion Unlock from exefs/main while preserving other generated ExeFS edits."
                    : "Install or refresh Fashion Unlock ownership-check stubs in exefs/main."),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Fashion Unlock change plan preview contains {writes.Length:N0} target file(s).")));

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
                "Reviewed Fashion Unlock change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Fashion Unlock change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var pendingEdit = session.PendingEdits.Single();
        if (IsUninstallEdit(pendingEdit))
        {
            ApplyUninstall(paths, writtenFiles, diagnostics);
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShFashionUnlockWorkflowService.ResolveWorkflowFile(project, SwShFashionUnlockWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock source or output target could not be resolved.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Readable source and writable LayeredFS target"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var output = SwShFashionUnlockMainPatcher.Apply(
                File.ReadAllBytes(source.AbsolutePath),
                paths.SelectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShFashionUnlockWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Fashion Unlock changes to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fashion Unlock source file could not be patched: {exception.Message}",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Supported Sword/Shield exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fashion Unlock output file could not be written: {exception.Message}",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fashion Unlock output file could not be written: {exception.Message}",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static void ApplyUninstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = ResolveBaseSourcePath(paths, SwShFashionUnlockWorkflowService.ExeFsMainPath);
        if (targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall could not resolve base exefs/main for restoration.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Readable base ExeFS main"));
            return;
        }

        if (!File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall target no longer exists. Review the change plan again before applying.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Existing reviewed LayeredFS exefs/main"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var restored = SwShFashionUnlockMainPatcher.RestoreFromBase(
                File.ReadAllBytes(targetPath),
                baseBytes,
                paths.SelectedGame);
            if (restored.SequenceEqual(baseBytes))
            {
                File.Delete(targetPath);
            }
            else
            {
                File.WriteAllBytes(targetPath, restored);
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShFashionUnlockWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Fashion Unlock from the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fashion Unlock uninstall could not restore exefs/main: {exception.Message}",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Supported Sword/Shield exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fashion Unlock uninstall could not update output: {exception.Message}",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fashion Unlock uninstall could not update output: {exception.Message}",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
    }

    private static bool CanStageInstall(
        OpenedProject project,
        SwShFashionUnlockWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock install requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        if (workflow.InstallStatus == "blocked")
        {
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fashion Unlock cannot stage while exefs/main has an unsupported build or conflicting fashion ownership getter bytes.",
                    expected: "Supported Sword or Shield 1.3.2 ownership getter bytes"));
            }

            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SwShFashionUnlockWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall requires valid base paths and a valid output root.",
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
                    "Fashion Unlock is not installed in the current project output.",
                    expected: "Installed Fashion Unlock ownership stubs"));
            }

            return false;
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall can only restore a generated exefs/main file.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Fashion Unlock installed in the configured output root"));
            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static PendingEdit CreatePendingInstallEdit(IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            FashionUnlockEditDomain,
            "Stage Fashion Unlock install.",
            sources,
            InstallRecordId,
            InstallField,
            "true");
    }

    private static ProjectFileReference CreateSourceReference(ProjectFileGraphEntry entry)
    {
        return new ProjectFileReference(
            entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
            entry.RelativePath);
    }

    private static PendingEdit CreatePendingUninstallEdit()
    {
        return new PendingEdit(
            FashionUnlockEditDomain,
            "Stage Fashion Unlock uninstall.",
            [
                new ProjectFileReference(ProjectFileLayer.Generated, SwShFashionUnlockWorkflowService.ExeFsMainPath),
                new ProjectFileReference(ProjectFileLayer.Base, SwShFashionUnlockWorkflowService.ExeFsMainPath),
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
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShFashionUnlockWorkflowService.ResolveOutputPath(paths, SwShFashionUnlockWorkflowService.ExeFsMainPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
            Domain: FashionUnlockEditDomain,
            Field: field,
            Expected: expected);
    }
}
