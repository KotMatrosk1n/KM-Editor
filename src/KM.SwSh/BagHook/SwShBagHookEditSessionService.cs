// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.BagHook;

public sealed class SwShBagHookEditSessionService
{
    public const string BagHookEditDomain = "workflow.bagHook";

    private const string InstallRecordId = "bag-hook-v2";
    private const string InstallField = "install";
    private const string UninstallRecordId = "bag-hook-v2-uninstall";
    private const string UninstallField = "uninstall";

    private readonly SwShBagHookWorkflowService bagHookWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShBagHookEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShBagHookWorkflowService? bagHookWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.bagHookWorkflowService = bagHookWorkflowService ?? new SwShBagHookWorkflowService();
    }

    public SwShBagHookEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = bagHookWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook install needs its own edit session before staging.",
                expected: "A Bag Hook-only edit session"));
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        var source = ResolveInstallSource(project, diagnostics);
        if (source is null)
        {
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(CreateSourceReference(source.GraphEntry)))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Bag Hook V2 install is staged for change-plan review."));

        return new SwShBagHookEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShBagHookEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = bagHookWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall needs its own edit session before staging.",
                expected: "A Bag Hook-only edit session"));
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, diagnostics))
        {
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal))
                .Append(CreateUninstallPendingEdit())
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Bag Hook V2 uninstall is staged for change-plan review."));

        return new SwShBagHookEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = bagHookWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Bag Hook install or uninstall before validating.",
                expected: "Pending Bag Hook install or uninstall"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit is not a Bag Hook workflow edit.",
                    expected: BagHookEditDomain));
                continue;
            }

            if (IsInstallEdit(edit))
            {
                CanStageInstall(project, workflow, diagnostics);
            }
            else if (IsUninstallEdit(edit))
            {
                CanStageUninstall(project, workflow, diagnostics);
            }
            else
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit is not a Bag Hook V2 install or uninstall.",
                    field: edit.Field,
                    expected: $"{InstallRecordId} or {UninstallRecordId}"));
            }
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Bag Hook workflow edit is valid for change-plan review."));
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

        var edit = session.PendingEdits.Single();
        var project = projectWorkspaceService.Open(paths);
        var writes = IsUninstallEdit(edit)
            ? CreateUninstallWrites(project, paths, diagnostics)
            : CreateInstallWrites(project, paths, diagnostics);

        if (writes.Count == 0)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Bag Hook change plan preview contains {writes.Count:N0} target file(s).")));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
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

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Bag Hook change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Bag Hook change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var isUninstall = IsUninstallEdit(session.PendingEdits.Single());
        if (isUninstall)
        {
            ApplyUninstall(paths, currentPlan, writtenFiles, diagnostics);
        }
        else
        {
            ApplyInstall(paths, writtenFiles, diagnostics);
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private void ApplyInstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShBagHookWorkflowService.BagEventScriptPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook source or output target could not be resolved.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Readable source and writable LayeredFS target"));
            return;
        }

        try
        {
            var output = SwShBagHookAmxPatcher.InstallEmptyHook(File.ReadAllBytes(source.AbsolutePath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShBagHookWorkflowService.BagEventScriptPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Installed Bag Hook V2 to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook source file could not be patched: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Supported Sword/Shield Bag-event AMX"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook output file could not be written: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook output file could not be written: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Writable output root"));
        }
    }

    private static void ApplyUninstall(
        ProjectPaths paths,
        ChangePlan currentPlan,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var preparedBagRestore = PrepareBagHookRestore(paths, currentPlan, diagnostics);
        if (preparedBagRestore is null)
        {
            return;
        }

        if (!SwShOutputRollbackScope.TryCapture(
            paths,
            currentPlan.Writes.Select(write => write.TargetRelativePath),
            out var rollbackScope,
            out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook uninstall could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return;
        }

        var outputRollback = rollbackScope!;
        using (outputRollback)
        {
            var orderedWrites = currentPlan.Writes.OrderBy(write => string.Equals(
                write.TargetRelativePath,
                SwShBagHookWorkflowService.BagEventScriptPath,
                StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1);
            foreach (var write in orderedWrites)
            {
                var isBagHookOutput = string.Equals(
                    write.TargetRelativePath,
                    SwShBagHookWorkflowService.BagEventScriptPath,
                    StringComparison.OrdinalIgnoreCase);
                var targetPath = isBagHookOutput
                    ? preparedBagRestore.TargetPath
                    : ResolveOutputPath(paths, write.TargetRelativePath, diagnostics);
                if (targetPath is null)
                {
                    break;
                }

                if (!File.Exists(targetPath))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Bag Hook uninstall target '{write.TargetRelativePath}' no longer exists. Review the change plan again before applying.",
                        file: write.TargetRelativePath,
                        expected: "Existing reviewed LayeredFS output file"));
                    break;
                }

                try
                {
                    var changed = isBagHookOutput
                        ? ApplyBagHookRestore(preparedBagRestore, diagnostics)
                        : SwShRoyalCandyCleanup.IsCleanupOutputPath(write.TargetRelativePath)
                        ? SwShRoyalCandyCleanup.TryApplyCleanupTarget(
                            paths,
                            targetPath,
                            write.TargetRelativePath,
                            BagHookEditDomain,
                            diagnostics,
                            clearBagHookSlot: false)
                        : DeleteOutput(targetPath);
                    if (!changed)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"Bag Hook uninstall could not complete reviewed target '{write.TargetRelativePath}'.",
                            file: write.TargetRelativePath,
                            expected: "Every reviewed uninstall target restored successfully"));
                        break;
                    }

                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, write.TargetRelativePath));
                }
                catch (InvalidDataException exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Bag Hook uninstall target '{write.TargetRelativePath}' could not be restored: {exception.Message}",
                        file: write.TargetRelativePath,
                        expected: "Supported Sword/Shield LayeredFS output"));
                    break;
                }
                catch (IOException exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Bag Hook uninstall target '{write.TargetRelativePath}' could not be removed: {exception.Message}",
                        file: write.TargetRelativePath,
                        expected: "Writable output root"));
                    break;
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Bag Hook uninstall target '{write.TargetRelativePath}' could not be removed: {exception.Message}",
                        file: write.TargetRelativePath,
                        expected: "Writable output root"));
                    break;
                }
                catch (Exception exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Bag Hook uninstall failed unexpectedly for '{write.TargetRelativePath}': {exception.Message}",
                        file: write.TargetRelativePath,
                        expected: "Every reviewed uninstall target restored successfully"));
                    break;
                }
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                RollbackFailedUninstall(outputRollback, writtenFiles, diagnostics);
            }
            else
            {
                outputRollback.Commit();
            }
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Bag Hook V2 and removed dependent Royal Candy and Starting Items changes."));
        }
    }

    private static void RollbackFailedUninstall(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var rollbackFailures = rollbackScope.Rollback();
        writtenFiles.Clear();
        if (rollbackFailures.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Bag Hook uninstall failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook uninstall rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private static PreparedBagHookRestore? PrepareBagHookRestore(
        ProjectPaths paths,
        ChangePlan currentPlan,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var bagWrite = currentPlan.Writes.FirstOrDefault(write => string.Equals(
            write.TargetRelativePath,
            SwShBagHookWorkflowService.BagEventScriptPath,
            StringComparison.OrdinalIgnoreCase));
        if (bagWrite is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall plan does not contain the Bag-event script target.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Reviewed Bag Hook output target"));
            return null;
        }

        var targetPath = ResolveOutputPath(paths, bagWrite.TargetRelativePath, diagnostics);
        if (targetPath is null)
        {
            return null;
        }

        if (!File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook uninstall target '{bagWrite.TargetRelativePath}' no longer exists. Review the change plan again before applying.",
                file: bagWrite.TargetRelativePath,
                expected: "Existing reviewed LayeredFS output file"));
            return null;
        }

        var basePath = ResolveBaseSourcePath(paths, bagWrite.TargetRelativePath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall requires the matching base Bag-event script so owned cells can be restored safely.",
                file: bagWrite.TargetRelativePath,
                expected: "Readable base romfs Bag-event script"));
            return null;
        }

        try
        {
            var restore = SwShBagHookAmxPatcher.RestoreFromBase(
                File.ReadAllBytes(targetPath),
                File.ReadAllBytes(basePath));
            return new PreparedBagHookRestore(targetPath, restore);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook uninstall target '{bagWrite.TargetRelativePath}' could not be restored: {exception.Message}",
                file: bagWrite.TargetRelativePath,
                expected: "Supported terminal Bag Hook V2 layout"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook uninstall inputs could not be read: {exception.Message}",
                file: bagWrite.TargetRelativePath,
                expected: "Readable Bag Hook output and base romfs source"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook uninstall inputs could not be read: {exception.Message}",
                file: bagWrite.TargetRelativePath,
                expected: "Readable Bag Hook output and base romfs source"));
        }

        return null;
    }

    private static bool ApplyBagHookRestore(
        PreparedBagHookRestore prepared,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (prepared.Restore.IsBaseEquivalent)
        {
            File.Delete(prepared.TargetPath);
            return true;
        }

        File.WriteAllBytes(prepared.TargetPath, prepared.Restore.Data);
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Removed Bag Hook owned cells and preserved unrelated Bag-event script edits in LayeredFS output.",
            file: SwShBagHookWorkflowService.BagEventScriptPath));
        return true;
    }

    private static bool CanStageInstall(
        OpenedProject project,
        SwShBagHookWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook install requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.InstallStatus == SwShBagHookWorkflowService.InstalledStatus)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook V2 is already installed.",
                expected: "Use Royal Candy or Starting Items to claim slots"));
            return false;
        }

        if (workflow.InstallStatus != "available" && workflow.InstallStatus != SwShBagHookWorkflowService.RepairableStatus)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook V2 cannot be installed or repaired while the Bag-event script is blocked, legacy, or conflicting.",
                expected: "Vanilla Bag-event script or repairable Bag Hook V2 output"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SwShBagHookWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.InstallStatus is not SwShBagHookWorkflowService.InstalledStatus
            and not SwShBagHookWorkflowService.RepairableStatus)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook V2 is not installed in the configured project output.",
                expected: "Installed Bag Hook V2"));
            return false;
        }

        var targetPath = SwShBagHookWorkflowService.ResolveOutputPath(project.Paths, SwShBagHookWorkflowService.BagEventScriptPath);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall only removes LayeredFS output, but no Bag Hook output file was found.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Installed Bag Hook V2 in the configured output root"));
            return false;
        }

        foreach (var blocker in SwShRoyalCandyCleanup.FindBlockingCleanupTargets(project))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook uninstall is blocked because dependent Royal Candy cleanup cannot be verified atomically: {blocker.Message}",
                file: blocker.Entry.RelativePath,
                expected: "Every dependent Royal Candy target decodes and maps to owned cleanup data"));
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static IReadOnlyList<PlannedFileWrite> CreateInstallWrites(
        OpenedProject project,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var source = ResolveInstallSource(project, diagnostics);
        if (targetPath is null || source is null)
        {
            return Array.Empty<PlannedFileWrite>();
        }

        return
        [
            new PlannedFileWrite(
                SwShBagHookWorkflowService.BagEventScriptPath,
                [CreateSourceReference(source.GraphEntry)],
                File.Exists(targetPath),
                "Install Bag Hook V2 with 20 disabled startup item grant slots. The hook grants no items by itself."),
        ];
    }

    private IReadOnlyList<PlannedFileWrite> CreateUninstallWrites(
        OpenedProject project,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var writes = new SortedDictionary<string, PlannedFileWrite>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in project.FileGraph.Entries.Where(entry => entry.LayeredFile is not null))
        {
            var isBagHookOutput = string.Equals(entry.RelativePath, SwShBagHookWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase);
            var isRoyalCandyDependentOutput = !isBagHookOutput
                && SwShRoyalCandyCleanup.IsBagHookDependentCleanupTarget(project, entry);
            if (!isBagHookOutput && !isRoyalCandyDependentOutput)
            {
                continue;
            }

            var targetPath = ResolveOutputPath(paths, entry.RelativePath, diagnostics);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            var sources = new List<ProjectFileReference>
            {
                new(ProjectFileLayer.Layered, entry.RelativePath),
            };
            if (isBagHookOutput || SwShRoyalCandyCleanup.IsCleanupOutputPath(entry.RelativePath))
            {
                sources.Add(new ProjectFileReference(ProjectFileLayer.Base, entry.RelativePath));
            }

            writes[entry.RelativePath] = new PlannedFileWrite(
                entry.RelativePath,
                sources,
                ReplacesExistingOutput: true,
                isBagHookOutput
                    ? "Uninstall Bag Hook V2 and remove all dependent startup item grants."
                    : "Remove dependent Royal Candy output because Royal Candy depends on Bag Hook slot 1.");
        }

        if (writes.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall did not find any reviewed LayeredFS output targets.",
                expected: "Bag Hook output or dependent Royal Candy text, shop, or ExeFS output"));
        }

        return writes.Values.ToArray();
    }

    private static PendingEdit CreatePendingEdit(ProjectFileReference source)
    {
        return new PendingEdit(
            BagHookEditDomain,
            "Stage Bag Hook install: 20 disabled startup item grant slots.",
            [source],
            RecordId: InstallRecordId,
            Field: InstallField,
            NewValue: "v2-empty");
    }

    private static WorkflowFileSource? ResolveInstallSource(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate => string.Equals(
            candidate.RelativePath,
            SwShBagHookWorkflowService.BagEventScriptPath,
            StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook source could not be resolved.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Readable Bag Hook script source"));
            return null;
        }

        var sourcePath = SwShBagHookWorkflowService.ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook source could not be read.",
                file: entry.RelativePath,
                expected: "Readable current script source"));
            return null;
        }

        return new WorkflowFileSource(entry, sourcePath);
    }

    private static ProjectFileReference CreateSourceReference(ProjectFileGraphEntry entry)
    {
        return new ProjectFileReference(
            entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
            entry.RelativePath);
    }

    private static PendingEdit CreateUninstallPendingEdit()
    {
        return new PendingEdit(
            BagHookEditDomain,
            "Stage Bag Hook uninstall: remove Bag Hook plus dependent Royal Candy and Starting Items outputs.",
            [new ProjectFileReference(ProjectFileLayer.Layered, SwShBagHookWorkflowService.BagEventScriptPath)],
            RecordId: UninstallRecordId,
            Field: UninstallField,
            NewValue: "remove-bag-hook-and-dependents");
    }

    private static bool IsInstallEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, InstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, InstallField, StringComparison.Ordinal);
    }

    private static bool IsUninstallEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, UninstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, UninstallField, StringComparison.Ordinal);
    }

    private static string? ResolveOutputPath(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook install requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShBagHookWorkflowService.ResolveOutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
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
                "Bag Hook workflow requires a configured output root.",
                file: targetRelativePath,
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook workflow target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShItemsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook workflow target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            || !targetRelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.Combine(
            paths.BaseRomFsPath,
            targetRelativePath["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool DeleteOutput(string targetPath)
    {
        File.Delete(targetPath);
        return true;
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

        var sourcePath = SwShBagHookWorkflowService.ResolveSourcePath(project.Paths, graphEntry);
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
            Domain: BagHookEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);

    private sealed record PreparedBagHookRestore(
        string TargetPath,
        SwShBagHookRestoreResult Restore);
}
