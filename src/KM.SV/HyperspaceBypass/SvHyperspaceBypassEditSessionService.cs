// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.HyperspaceBypass;

public sealed class SvHyperspaceBypassEditSessionService
{
    public const string HyperspaceBypassEditDomain = "workflow.hyperspaceBypass";

    private const string InstallRecordId = "hyperspace-bypass-v1-install";
    private const string UninstallRecordId = "hyperspace-bypass-v1-uninstall";
    private const string InstallField = "install";
    private const string UninstallField = "uninstall";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvHyperspaceBypassWorkflowService hyperspaceBypassWorkflowService;

    public SvHyperspaceBypassEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvHyperspaceBypassWorkflowService? hyperspaceBypassWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.hyperspaceBypassWorkflowService = hyperspaceBypassWorkflowService ?? new SvHyperspaceBypassWorkflowService();
    }

    public SvHyperspaceBypassEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = hyperspaceBypassWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, HyperspaceBypassEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass needs its own edit session before staging.",
                expected: "A Hyperspace Bypass-only edit session"));
            return new SvHyperspaceBypassEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new SvHyperspaceBypassEditResult(workflow, currentSession, diagnostics);
        }

        var source = SvHyperspaceBypassWorkflowService.ResolveWorkflowFile(
            project,
            SvHyperspaceBypassWorkflowService.ExeFsMainPath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass source could not be resolved.",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source"));
            return new SvHyperspaceBypassEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, HyperspaceBypassEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingInstallEdit([CreateSourceReference(source.Entry)]))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Hyperspace Bypass install is staged for change-plan review."));

        return new SvHyperspaceBypassEditResult(workflow, updatedSession, diagnostics);
    }

    public SvHyperspaceBypassEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = hyperspaceBypassWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, HyperspaceBypassEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass needs its own edit session before staging uninstall.",
                expected: "A Hyperspace Bypass-only edit session"));
            return new SvHyperspaceBypassEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new SvHyperspaceBypassEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, HyperspaceBypassEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingUninstallEdit())
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Hyperspace Bypass uninstall is staged for change-plan review."));

        return new SvHyperspaceBypassEditResult(workflow, updatedSession, diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = hyperspaceBypassWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Hyperspace Bypass install or uninstall before validating.",
                expected: "Pending Hyperspace Bypass install or uninstall"));
            return new SvEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, HyperspaceBypassEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Hyperspace Bypass.",
                    expected: HyperspaceBypassEditDomain));
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
                $"Pending Hyperspace Bypass edit '{edit.RecordId}' is not supported.",
                expected: "Hyperspace Bypass install or uninstall"));
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Hyperspace Bypass change is valid for change-plan review."));
        }

        return new SvEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (outputMode != SvOutputMode.Standalone)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass edits exefs/main, which is outside Scarlet/Violet RomFS output modes. Use standalone LayeredFS output for this editor.",
                expected: "Standalone LayeredFS output mode"));
        }

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
        var source = SvHyperspaceBypassWorkflowService.ResolveWorkflowFile(
            project,
            SvHyperspaceBypassWorkflowService.ExeFsMainPath);
        if (!isUninstall && source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass source could not be resolved.",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new[]
        {
            new PlannedFileWrite(
                SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                isUninstall
                    ? [
                        new ProjectFileReference(ProjectFileLayer.Generated, SvHyperspaceBypassWorkflowService.ExeFsMainPath),
                        new ProjectFileReference(ProjectFileLayer.Base, SvHyperspaceBypassWorkflowService.ExeFsMainPath),
                    ]
                    : [CreateSourceReference(source!.Entry)],
                File.Exists(targetPath),
                isUninstall
                    ? "Uninstall Hyperspace Bypass from exefs/main while preserving other generated ExeFS edits."
                    : "Install or refresh Hyperspace Bypass in exefs/main."),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Hyperspace Bypass change plan preview contains {writes.Length:N0} target file(s).")));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Hyperspace Bypass change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Hyperspace Bypass change plan"));
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
        var source = SvHyperspaceBypassWorkflowService.ResolveWorkflowFile(project, SvHyperspaceBypassWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass source or output target could not be resolved.",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Readable source and writable LayeredFS target"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var output = SvHyperspaceBypassMainPatcher.Apply(
                File.ReadAllBytes(source.AbsolutePath),
                paths.SelectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SvHyperspaceBypassWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyperspace Bypass changes to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyperspace Bypass source file could not be patched: {exception.Message}",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Supported Scarlet/Violet exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyperspace Bypass output file could not be written: {exception.Message}",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyperspace Bypass output file could not be written: {exception.Message}",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
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
        var basePath = ResolveBaseSourcePath(paths, SvHyperspaceBypassWorkflowService.ExeFsMainPath);
        if (targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass uninstall could not resolve base exefs/main for restoration.",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Readable base ExeFS main"));
            return;
        }

        if (!File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass uninstall target no longer exists. Review the change plan again before applying.",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Existing reviewed LayeredFS exefs/main"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var restored = SvHyperspaceBypassMainPatcher.RestoreFromBase(
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

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SvHyperspaceBypassWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Hyperspace Bypass from the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyperspace Bypass uninstall could not restore exefs/main: {exception.Message}",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Supported Scarlet/Violet exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyperspace Bypass uninstall could not update output: {exception.Message}",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyperspace Bypass uninstall could not update output: {exception.Message}",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
    }

    private static bool CanStageInstall(
        OpenedProject project,
        SvHyperspaceBypassWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SvWorkflowFileSource.IsScarletViolet(project.Paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass install requires a Scarlet or Violet project.",
                expected: "Scarlet/Violet project"));
            return false;
        }

        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SvWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass install requires valid base paths and a valid output root.",
                expected: "Editable Scarlet/Violet project paths"));
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
                    "Hyperspace Bypass cannot stage while exefs/main has an unsupported build or conflicting runtime gate bytes.",
                    expected: "Supported Scarlet or Violet Hyperspace runtime gate bytes"));
            }

            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SvHyperspaceBypassWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SvWorkflowFileSource.IsScarletViolet(project.Paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass uninstall requires a Scarlet or Violet project.",
                expected: "Scarlet/Violet project"));
            return false;
        }

        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SvWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass uninstall requires valid base paths and a valid output root.",
                expected: "Editable Scarlet/Violet project paths"));
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
                    "Hyperspace Bypass is not installed in the current project output.",
                    expected: "Installed Hyperspace Bypass branch"));
            }

            return false;
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass uninstall can only restore a generated exefs/main file.",
                file: SvHyperspaceBypassWorkflowService.ExeFsMainPath,
                expected: "Hyperspace Bypass installed in the configured output root"));
            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static PendingEdit CreatePendingInstallEdit(IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            HyperspaceBypassEditDomain,
            "Stage Hyperspace Bypass install.",
            sources,
            InstallRecordId,
            InstallField,
            "true");
    }

    private static PendingEdit CreatePendingUninstallEdit()
    {
        return new PendingEdit(
            HyperspaceBypassEditDomain,
            "Stage Hyperspace Bypass uninstall.",
            [
                new ProjectFileReference(ProjectFileLayer.Generated, SvHyperspaceBypassWorkflowService.ExeFsMainPath),
                new ProjectFileReference(ProjectFileLayer.Base, SvHyperspaceBypassWorkflowService.ExeFsMainPath),
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

    private static ProjectFileReference CreateSourceReference(ProjectFileGraphEntry entry)
    {
        var layer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
        return new ProjectFileReference(layer, entry.RelativePath);
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SvHyperspaceBypassWorkflowService.ResolveOutputPath(paths, SvHyperspaceBypassWorkflowService.ExeFsMainPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyperspace Bypass target must stay inside the configured output root.",
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

        var reviewedWrites = reviewedPlan.Writes
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
        var currentWrites = currentPlan.Writes
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();

        return reviewedWrites
            .Zip(currentWrites)
            .All(pair => string.Equals(pair.First.TargetRelativePath, pair.Second.TargetRelativePath, StringComparison.Ordinal)
                && pair.First.Sources.SequenceEqual(pair.Second.Sources));
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
            Domain: HyperspaceBypassEditDomain,
            Field: field,
            Expected: expected);
    }
}
