// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.RoyalCandy;

public sealed class SwShRoyalCandyEditSessionService
{
    public const string RoyalCandyEditDomain = "workflow.royalCandy";

    private const string WorkflowField = "workflowId";
    private const string UnlimitedWorkflowId = "royal-candy-unlimited";
    private const string StoryLimitsWorkflowId = "royal-candy-story-limits";
    private const string UninstallWorkflowId = "royal-candy-uninstall";
    private const int RoyalCandyItemId = SwShBagHookAmxPatcher.RoyalCandyItemId;
    private const string RoyalCandyName = "Royal Candy";
    private const string RoyalCandyPluralName = "Royal Candies";
    private const string UnlimitedDescription = "A candy packed with strange energy. It can be used repeatedly by compatible Pokemon.";
    private const string StoryLimitsDescription = "A candy packed with strange energy. Its full power follows the current story limit.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShRoyalCandyWorkflowService royalCandyWorkflowService;

    public SwShRoyalCandyEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRoyalCandyWorkflowService? royalCandyWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.royalCandyWorkflowService = royalCandyWorkflowService ?? new SwShRoyalCandyWorkflowService();
    }

    public void ClearMemoryCache()
    {
        royalCandyWorkflowService.ClearMemoryCache();
    }

    public SwShRoyalCandyEditResult StageWorkflow(
        ProjectPaths paths,
        string workflowId,
        IReadOnlyList<SwShRoyalCandyLevelCapSelection>? levelCaps,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(workflowId);

        workflowId = workflowId.Trim();
        projectWorkspaceService.ClearMemoryCache();
        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = royalCandyWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, RoyalCandyEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy workflows need their own edit session before staging.",
                expected: "A Royal Candy-only edit session"));
            return new SwShRoyalCandyEditResult(workflow, currentSession, diagnostics);
        }

        var selectedWorkflow = GetApplicableWorkflow(workflow, workflowId, diagnostics);
        if (selectedWorkflow is null || !CanStage(project, workflow, selectedWorkflow, diagnostics))
        {
            return new SwShRoyalCandyEditResult(workflow, currentSession, diagnostics);
        }

        var selectedLevelCaps = NormalizeLevelCapSelections(selectedWorkflow, levelCaps, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRoyalCandyEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedWorkflow, selectedLevelCaps);
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, RoyalCandyEditDomain, StringComparison.Ordinal))
                .Append(pendingEdit)
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Royal Candy workflow '{selectedWorkflow.Name}' is staged for change-plan review."));

        return new SwShRoyalCandyEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = royalCandyWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage a Royal Candy workflow before validating."
                    : "Royal Candy validation requires exactly one pending Royal Candy workflow.",
                expected: "Exactly one pending Royal Candy workflow"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        ValidatePendingEdit(project, workflow, session.PendingEdits[0], diagnostics);

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Royal Candy workflow is valid for change-plan review."));
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
        var workflow = royalCandyWorkflowService.Load(project);
        var edit = session.PendingEdits.Single();
        var selectedWorkflow = GetApplicableWorkflow(workflow, edit.RecordId ?? string.Empty, diagnostics);
        if (selectedWorkflow is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = CreateConcreteWrites(paths, workflow, selectedWorkflow, edit, diagnostics);
        if (!string.Equals(selectedWorkflow.WorkflowId, UninstallWorkflowId, StringComparison.Ordinal))
        {
            var levelCaps = ParseLevelCapSelections(selectedWorkflow, edit.NewValue, diagnostics);
            ValidateConcreteOutputs(project, selectedWorkflow, levelCaps, writes, diagnostics);
        }
        if (writes.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy change plan has no writable output targets.",
                expected: "Writable item or text targets"));
        }
        else
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Royal Candy change plan preview contains {writes.Count:N0} target file(s).")));
        }

        AddDeferredOutputDiagnostics(workflow, selectedWorkflow, writes, diagnostics);

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
                "Reviewed Royal Candy change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Royal Candy change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = royalCandyWorkflowService.Load(project);
        var edit = session.PendingEdits.Single();
        var workflowId = edit.RecordId ?? string.Empty;
        var selectedWorkflow = GetApplicableWorkflow(workflow, workflowId, diagnostics);
        if (selectedWorkflow is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var levelCaps = ParseLevelCapSelections(selectedWorkflow, edit.NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var isCleanup = string.Equals(workflowId, UninstallWorkflowId, StringComparison.Ordinal);
        if (!SwShOutputRollbackScope.TryCapture(
            paths,
            currentPlan.Writes.Select(write => write.TargetRelativePath),
            out var rollbackScope,
            out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Royal Candy could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var outputRollback = rollbackScope!;
        using (outputRollback)
        {
            foreach (var write in currentPlan.Writes)
            {
                var targetPath = ResolveOutputPath(paths, write.TargetRelativePath, diagnostics);
                if (targetPath is null)
                {
                    break;
                }

                try
                {
                    if (isCleanup)
                    {
                        if (!File.Exists(targetPath))
                        {
                            diagnostics.Add(CreateDiagnostic(
                                DiagnosticSeverity.Error,
                                $"Royal Candy cleanup target '{write.TargetRelativePath}' no longer exists. Review the change plan again before applying.",
                                file: write.TargetRelativePath,
                                expected: "Existing reviewed LayeredFS output file"));
                            break;
                        }

                        if (!SwShRoyalCandyCleanup.TryApplyCleanupTarget(
                            paths,
                            targetPath,
                            write.TargetRelativePath,
                            RoyalCandyEditDomain,
                            diagnostics,
                            clearBagHookSlot: true))
                        {
                            diagnostics.Add(CreateDiagnostic(
                                DiagnosticSeverity.Error,
                                $"Royal Candy cleanup could not complete reviewed target '{write.TargetRelativePath}'.",
                                file: write.TargetRelativePath,
                                expected: "Every reviewed cleanup target restored successfully"));
                            break;
                        }

                        writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, write.TargetRelativePath));
                        continue;
                    }

                    var errorCountBeforeOutput = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                    var output = CreateOutputBytes(project, selectedWorkflow, levelCaps, write.TargetRelativePath, diagnostics);
                    if (output is null)
                    {
                        if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCountBeforeOutput)
                        {
                            diagnostics.Add(CreateDiagnostic(
                                DiagnosticSeverity.Error,
                                $"Royal Candy did not produce reviewed output '{write.TargetRelativePath}'.",
                                file: write.TargetRelativePath,
                                expected: "Output bytes for every reviewed target"));
                        }

                        break;
                    }

                    WriteOutputAtomically(targetPath, output);
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, write.TargetRelativePath));
                }
                catch (InvalidDataException exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Royal Candy source file '{write.TargetRelativePath}' could not be decoded: {exception.Message}",
                        file: write.TargetRelativePath,
                        expected: "Supported Sword/Shield source data"));
                    break;
                }
                catch (IOException exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Royal Candy output file '{write.TargetRelativePath}' could not be written: {exception.Message}",
                        file: write.TargetRelativePath,
                        expected: "Writable output root"));
                    break;
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Royal Candy output file '{write.TargetRelativePath}' could not be written: {exception.Message}",
                        file: write.TargetRelativePath,
                        expected: "Writable output root"));
                    break;
                }
                catch (Exception exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Royal Candy apply failed unexpectedly for '{write.TargetRelativePath}': {exception.Message}",
                        file: write.TargetRelativePath,
                        expected: "Every reviewed output applied successfully"));
                    break;
                }
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
            }
            else
            {
                outputRollback.Commit();
            }
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            projectWorkspaceService.ClearMemoryCache();
            royalCandyWorkflowService.ClearMemoryCache();
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                isCleanup
                    ? "Cleaned reviewed Royal Candy LayeredFS output from the configured output root."
                    : "Applied Royal Candy change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static void WriteOutputAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Royal Candy output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Royal Candy output target directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, contents);
            if (!File.ReadAllBytes(tempPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Royal Candy temporary output verification failed.");
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The rollback scope still owns restoration of the reviewed output target.
            }
        }
    }

    private static void RollbackFailedApply(
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
                "Royal Candy apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Royal Candy rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShRoyalCandyWorkflow workflow,
        SwShRoyalCandyWorkflowRecord selectedWorkflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        var isCleanup = string.Equals(selectedWorkflow.WorkflowId, UninstallWorkflowId, StringComparison.Ordinal);
        if (isCleanup)
        {
            if (selectedWorkflow.Status != "warning")
            {
                foreach (var check in workflow.Checks.Where(check =>
                    check.WorkflowId == UninstallWorkflowId && check.Status == "Fail"))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        check.Message,
                        file: check.Target,
                        expected: "Resolve every blocked Royal Candy cleanup target before uninstalling"));
                }

                if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Royal Candy cleanup is not ready to stage.",
                        expected: "A warning-status cleanup workflow with no blocked target"));
                }
            }
        }
        else
        {
            AddBlockingPreflightDiagnostics(workflow, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return false;
            }

            if (selectedWorkflow.Status != "available"
                && selectedWorkflow.Status != "warning"
                && selectedWorkflow.Status != "installed")
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy workflow '{selectedWorkflow.Name}' is not ready to stage.",
                    expected: "Available, warning, or installed workflow status"));
                return false;
            }

            foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(diagnostic);
            }
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void AddBlockingPreflightDiagnostics(
        SwShRoyalCandyWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var check in workflow.Checks.Where(check =>
            check.WorkflowId == "royal-candy-preflight" && check.Status == "Fail"))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                check.Message,
                file: check.Target,
                expected: check.CheckId.EndsWith(":bag-hook-starting-items-item-1128", StringComparison.Ordinal)
                    ? "Clear item 1128 from Starting Items slots 2-20 before staging Royal Candy."
                    : check.Area == "Bag Hook"
                        ? "Install Bag Hook from Hooks before staging Royal Candy."
                        : "Resolve the failed Royal Candy preflight check."));
        }
    }

    private static void ValidatePendingEdit(
        OpenedProject project,
        SwShRoyalCandyWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, RoyalCandyEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Royal Candy workflows.",
                expected: RoyalCandyEditDomain));
            return;
        }

        if (!string.Equals(edit.Field, WorkflowField, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Royal Candy workflow field is not canonical.",
                field: edit.Field,
                expected: WorkflowField));
            return;
        }

        var selectedWorkflow = GetApplicableWorkflow(workflow, edit.RecordId ?? string.Empty, diagnostics);
        if (selectedWorkflow is null)
        {
            return;
        }

        CanStage(project, workflow, selectedWorkflow, diagnostics);
        var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var levelCaps = ParseLevelCapSelections(selectedWorkflow, edit.NewValue, diagnostics);
        if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
        {
            var canonicalValue = CreatePendingEditValue(selectedWorkflow, levelCaps);
            if (!string.Equals(edit.NewValue, canonicalValue, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Royal Candy workflow value is not canonical.",
                    field: WorkflowField,
                    expected: canonicalValue));
            }
        }

        var hasCanonicalSource = edit.Sources.Count == 1
            && edit.Sources[0].Layer == selectedWorkflow.Provenance.SourceLayer
            && string.Equals(
                edit.Sources[0].RelativePath,
                selectedWorkflow.Provenance.SourceFile,
                StringComparison.OrdinalIgnoreCase);
        if (!hasCanonicalSource)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Royal Candy workflow source metadata is not canonical.",
                expected: $"{selectedWorkflow.Provenance.SourceLayer}:{selectedWorkflow.Provenance.SourceFile}"));
        }
    }

    private static SwShRoyalCandyWorkflowRecord? GetApplicableWorkflow(
        SwShRoyalCandyWorkflow workflow,
        string workflowId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var selectedWorkflow = workflow.Workflows.FirstOrDefault(candidate =>
            string.Equals(candidate.WorkflowId, workflowId, StringComparison.Ordinal));
        if (selectedWorkflow is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Royal Candy workflow '{workflowId}' is not available.",
                field: WorkflowField,
                expected: $"{UnlimitedWorkflowId}, {StoryLimitsWorkflowId}, or {UninstallWorkflowId}"));
            return null;
        }

        if (!string.Equals(selectedWorkflow.WorkflowId, UnlimitedWorkflowId, StringComparison.Ordinal)
            && !string.Equals(selectedWorkflow.WorkflowId, StoryLimitsWorkflowId, StringComparison.Ordinal))
        {
            if (!string.Equals(selectedWorkflow.WorkflowId, UninstallWorkflowId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy workflow '{selectedWorkflow.Name}' cannot be applied yet.",
                    field: WorkflowField,
                    expected: "Install or cleanup workflow"));
                return null;
            }
        }

        return selectedWorkflow;
    }

    private static PendingEdit CreatePendingEdit(
        SwShRoyalCandyWorkflowRecord workflow,
        IReadOnlyList<SwShRoyalCandyLevelCapSelection> levelCaps)
    {
        return new PendingEdit(
            RoyalCandyEditDomain,
            $"Stage Royal Candy workflow: {workflow.Name}.",
            [new ProjectFileReference(workflow.Provenance.SourceLayer, workflow.Provenance.SourceFile)],
            RecordId: workflow.WorkflowId,
            Field: WorkflowField,
            NewValue: CreatePendingEditValue(workflow, levelCaps));
    }

    private static string CreatePendingEditValue(
        SwShRoyalCandyWorkflowRecord workflow,
        IReadOnlyList<SwShRoyalCandyLevelCapSelection> levelCaps)
    {
        if (!UsesStoryLimits(workflow))
        {
            return workflow.Mode;
        }

        var serializedCaps = string.Join(
            ';',
            levelCaps
                .OrderBy(selection => selection.Slot)
                .Select(selection => string.Create(CultureInfo.InvariantCulture, $"{selection.Slot}={selection.LevelCap}")));
        return string.Create(CultureInfo.InvariantCulture, $"{workflow.Mode}|{serializedCaps}");
    }

    private static IReadOnlyList<SwShRoyalCandyLevelCapSelection> ParseLevelCapSelections(
        SwShRoyalCandyWorkflowRecord workflow,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!UsesStoryLimits(workflow))
        {
            return Array.Empty<SwShRoyalCandyLevelCapSelection>();
        }

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, workflow.Mode, StringComparison.Ordinal))
        {
            return NormalizeLevelCapSelections(workflow, null, diagnostics);
        }

        var prefix = $"{workflow.Mode}|";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy story-limit pending edit has an invalid cap payload.",
                field: "levelCaps",
                expected: "storyLimits|slot=level;..."));
            return Array.Empty<SwShRoyalCandyLevelCapSelection>();
        }

        var selections = new List<SwShRoyalCandyLevelCapSelection>();
        foreach (var part in value[prefix.Length..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2
                || !int.TryParse(keyValue[0], NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                || !int.TryParse(keyValue[1], NumberStyles.None, CultureInfo.InvariantCulture, out var levelCap))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy story-limit cap entry '{part}' is not valid.",
                    field: "levelCaps",
                    expected: "slot=level"));
                continue;
            }

            selections.Add(new SwShRoyalCandyLevelCapSelection(slot, levelCap));
        }

        return NormalizeLevelCapSelections(workflow, selections, diagnostics);
    }

    private static IReadOnlyList<SwShRoyalCandyLevelCapSelection> NormalizeLevelCapSelections(
        SwShRoyalCandyWorkflowRecord workflow,
        IReadOnlyList<SwShRoyalCandyLevelCapSelection>? selections,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!UsesStoryLimits(workflow))
        {
            return Array.Empty<SwShRoyalCandyLevelCapSelection>();
        }

        if (workflow.LevelCaps.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy story-limit workflow is missing its level-cap definitions.",
                field: "levelCaps",
                expected: "Configured story-limit milestone caps"));
            return Array.Empty<SwShRoyalCandyLevelCapSelection>();
        }

        var requested = new Dictionary<int, int>();
        if (selections is not null)
        {
            foreach (var selection in selections)
            {
                if (requested.ContainsKey(selection.Slot))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Royal Candy story-limit cap slot {selection.Slot} was supplied more than once.",
                        field: "levelCaps",
                        expected: "One cap value per milestone slot"));
                    continue;
                }

                requested.Add(selection.Slot, selection.LevelCap);
            }
        }

        var knownSlots = workflow.LevelCaps.Select(cap => cap.Slot).ToHashSet();
        foreach (var slot in requested.Keys.Where(slot => !knownSlots.Contains(slot)).Order())
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Royal Candy story-limit cap slot {slot} is not available.",
                field: "levelCaps",
                expected: "Known story-limit milestone slot"));
        }

        var normalized = new List<SwShRoyalCandyLevelCapSelection>(workflow.LevelCaps.Count);
        var previousCap = workflow.LevelCaps.Min(cap => cap.MinimumLevelCap);
        foreach (var definition in workflow.LevelCaps.OrderBy(cap => cap.Slot))
        {
            var levelCap = requested.TryGetValue(definition.Slot, out var requestedCap)
                ? requestedCap
                : definition.LevelCap;

            if (levelCap < definition.MinimumLevelCap || levelCap > definition.MaximumLevelCap)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Royal Candy cap for {definition.Label} must be between {definition.MinimumLevelCap} and {definition.MaximumLevelCap}."),
                    field: "levelCaps",
                    expected: "Story cap between 1 and 100"));
            }

            if (levelCap < previousCap)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Royal Candy cap for {definition.Label} is {levelCap}, but it must be at least {previousCap}."),
                    field: "levelCaps",
                    expected: "Equal or ascending story caps"));
            }

            normalized.Add(new SwShRoyalCandyLevelCapSelection(definition.Slot, levelCap));
            previousCap = levelCap;
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? Array.Empty<SwShRoyalCandyLevelCapSelection>()
            : normalized;
    }

    private static bool UsesStoryLimits(SwShRoyalCandyWorkflowRecord workflow)
    {
        return string.Equals(workflow.WorkflowId, StoryLimitsWorkflowId, StringComparison.Ordinal);
    }

    private static IReadOnlyList<SwShRoyalCandyStoryLevelCap> CreateStoryLevelCapPatches(
        SwShRoyalCandyWorkflowRecord workflow,
        IReadOnlyList<SwShRoyalCandyLevelCapSelection> selections,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!UsesStoryLimits(workflow))
        {
            return Array.Empty<SwShRoyalCandyStoryLevelCap>();
        }

        var selectedCaps = selections.ToDictionary(selection => selection.Slot, selection => selection.LevelCap);
        var storyLevelCaps = new List<SwShRoyalCandyStoryLevelCap>(workflow.LevelCaps.Count);
        foreach (var definition in workflow.LevelCaps.OrderBy(levelCap => levelCap.Slot))
        {
            var progressHashText = definition.ProgressHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? definition.ProgressHash[2..]
                : definition.ProgressHash;
            if (!ulong.TryParse(progressHashText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var progressHash))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy story-limit milestone '{definition.Label}' has an invalid progress hash '{definition.ProgressHash}'.",
                    field: "levelCaps",
                    expected: "64-bit progress hash"));
                continue;
            }

            var progressKind = definition.ProgressKind switch
            {
                "flag" => SwShRoyalCandyStoryLevelCapProgressKind.Flag,
                "workAtLeast" => SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast,
                _ => (SwShRoyalCandyStoryLevelCapProgressKind?)null,
            };
            if (progressKind is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy story-limit milestone '{definition.Label}' has an unknown progress kind '{definition.ProgressKind}'.",
                    field: "levelCaps",
                    expected: "flag or workAtLeast"));
                continue;
            }

            storyLevelCaps.Add(new SwShRoyalCandyStoryLevelCap(
                selectedCaps.TryGetValue(definition.Slot, out var selectedCap)
                    ? selectedCap
                    : definition.LevelCap,
                progressHash,
                definition.Label,
                progressKind.Value,
                definition.WorkMinimum ?? 0));
        }

        return storyLevelCaps;
    }

    private static IReadOnlyList<PlannedFileWrite> CreateConcreteWrites(
        ProjectPaths paths,
        SwShRoyalCandyWorkflow workflow,
        SwShRoyalCandyWorkflowRecord selectedWorkflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var writes = new List<PlannedFileWrite>();
        var isCleanup = string.Equals(selectedWorkflow.WorkflowId, UninstallWorkflowId, StringComparison.Ordinal);
        var outputs = workflow.Outputs
            .Where(output => string.Equals(output.WorkflowId, selectedWorkflow.WorkflowId, StringComparison.Ordinal))
            .Where(output => isCleanup || IsConcreteApplyOutput(output.RelativePath))
            .OrderBy(output => output.RelativePath, StringComparer.Ordinal)
            .ToArray();

        foreach (var output in outputs)
        {
            var targetPath = ResolveOutputPath(paths, output.RelativePath, diagnostics);
            if (targetPath is null)
            {
                continue;
            }

            writes.Add(new PlannedFileWrite(
                output.RelativePath,
                CreateConcreteWriteSources(output, isCleanup),
                File.Exists(targetPath),
                isCleanup
                    ? $"Remove Royal Candy LayeredFS output: {output.Description} Pending value: {edit.NewValue}."
                    : $"Apply Royal Candy workflow '{selectedWorkflow.Name}': {output.Description} Pending value: {edit.NewValue}."));
        }

        return writes;
    }

    private static IReadOnlyList<ProjectFileReference> CreateConcreteWriteSources(
        SwShRoyalCandyOutputRecord output,
        bool isCleanup)
    {
        var sources = new List<ProjectFileReference>
        {
            new(output.Provenance.SourceLayer, output.Provenance.SourceFile),
        };
        var needsBaseSource = string.Equals(
                output.RelativePath,
                SwShRoyalCandyWorkflowService.ExeFsMainPath,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                output.RelativePath,
                SwShRoyalCandyWorkflowService.ItemHashPath,
                StringComparison.OrdinalIgnoreCase)
            || IsShopDataOutput(output.RelativePath)
            || string.Equals(
                output.RelativePath,
                SwShRoyalCandyWorkflowService.ItemPath,
                StringComparison.OrdinalIgnoreCase)
            || (isCleanup && IsItemTextOutput(output.RelativePath));
        if (needsBaseSource)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, output.RelativePath));
        }

        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateConcreteOutputs(
        OpenedProject project,
        SwShRoyalCandyWorkflowRecord selectedWorkflow,
        IReadOnlyList<SwShRoyalCandyLevelCapSelection> levelCaps,
        IReadOnlyList<PlannedFileWrite> writes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var write in writes)
        {
            try
            {
                var output = CreateOutputBytes(
                    project,
                    selectedWorkflow,
                    levelCaps,
                    write.TargetRelativePath,
                    diagnostics);
                if (output is null)
                {
                    continue;
                }

                VerifyOutputBytes(selectedWorkflow, write.TargetRelativePath, output);
            }
            catch (Exception exception) when (exception is InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy output '{write.TargetRelativePath}' could not be generated and verified during review: {exception.Message}",
                    file: write.TargetRelativePath,
                    expected: "Round-trip verified Royal Candy output"));
            }
        }
    }

    private static void VerifyOutputBytes(
        SwShRoyalCandyWorkflowRecord selectedWorkflow,
        string relativePath,
        byte[] output)
    {
        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase))
        {
            var table = SwShItemTable.Parse(output);
            if (table.Records.Count(record => record.ItemId == RoyalCandyItemId) != 1)
            {
                throw new InvalidDataException($"Royal Candy item output does not expose exactly one item {RoyalCandyItemId} mapping.");
            }

            return;
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase))
        {
            var table = SwShItemHashTable.Parse(output);
            if (table.Entries.All(entry => entry.ItemId != RoyalCandyItemId))
            {
                throw new InvalidDataException($"Royal Candy item-hash output does not contain item {RoyalCandyItemId}.");
            }

            return;
        }

        if (IsShopDataOutput(relativePath))
        {
            _ = SwShShopDataFile.Parse(output);
            return;
        }

        if (IsItemTextOutput(relativePath))
        {
            var text = SwShGameTextFile.Parse(output);
            var expected = GetRoyalCandyTextReplacement(relativePath, selectedWorkflow.WorkflowId);
            if (text.Lines.Count <= RoyalCandyItemId
                || !string.Equals(text.Lines[RoyalCandyItemId].Text, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Royal Candy text output did not round-trip item {RoyalCandyItemId} with the expected value.");
            }

            return;
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            var slot = SwShBagHookAmxPatcher.Analyze(output).Slots.FirstOrDefault(candidate =>
                candidate.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
            if (slot?.ItemId != RoyalCandyItemId || slot.Quantity != 1)
            {
                throw new InvalidDataException("Royal Candy Bag Hook output did not round-trip slot 1 as item 1128 with quantity 1.");
            }
        }
    }

    private static void AddDeferredOutputDiagnostics(
        SwShRoyalCandyWorkflow workflow,
        SwShRoyalCandyWorkflowRecord selectedWorkflow,
        IReadOnlyList<PlannedFileWrite> writes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var concreteTargets = writes
            .Select(write => write.TargetRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deferredOutputs = workflow.Outputs
            .Where(output => string.Equals(output.WorkflowId, selectedWorkflow.WorkflowId, StringComparison.Ordinal))
            .Where(output => !concreteTargets.Contains(output.RelativePath))
            .ToArray();

        if (deferredOutputs.Length == 0)
        {
            return;
        }

        if (string.Equals(selectedWorkflow.WorkflowId, UninstallWorkflowId, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Royal Candy cleanup will remove only the reviewed known LayeredFS output targets.",
                expected: "Reviewed Royal Candy cleanup targets"));
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Warning,
            "Royal Candy plan currently generates item data, item text, shop inventory cleanup, the Bag-event grant, and ExeFS item-use patches. Raid reward and placement targets remain reserved for their dedicated apply workflows.",
            expected: "Review remaining Royal Candy output targets before full install"));
    }

    private static byte[]? CreateOutputBytes(
        OpenedProject project,
        SwShRoyalCandyWorkflowRecord selectedWorkflow,
        IReadOnlyList<SwShRoyalCandyLevelCapSelection> levelCaps,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = ResolveWorkflowFile(project, relativePath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Royal Candy source '{relativePath}' could not be resolved.",
                file: relativePath,
                expected: "Readable source file"));
            return null;
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase))
        {
            var basePath = ResolveBaseSourcePath(project.Paths, relativePath);
            if (basePath is null || !File.Exists(basePath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy item generation requires base item.dat to verify the current item 1128 mapping.",
                    file: relativePath,
                    expected: "Readable base item.dat"));
                return null;
            }

            return SwShItemTable.Parse(File.ReadAllBytes(source.AbsolutePath))
                .WriteRoyalCandyRow(
                    SwShItemTable.Parse(File.ReadAllBytes(basePath)),
                    templateItemId: 50,
                    targetItemId: 1128);
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase))
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var itemHashTable = SwShItemHashTable.Parse(sourceBytes);
            if (itemHashTable.Entries.All(entry => entry.ItemId != 1128))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy item hash output needs an existing hash-table entry for item 1128 before it can be preserved safely.",
                    file: relativePath,
                    expected: "Item hash table entry for item 1128"));
                return null;
            }

            if (TryRestoreFilteredItemHashFromBase(project.Paths, source, sourceBytes, out var restoredHashBytes))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Royal Candy detected a previously filtered item hash output and will restore the full base item hash table.",
                    file: relativePath,
                    expected: "Full base item hash table"));
                return restoredHashBytes;
            }

            return sourceBytes;
        }

        if (IsShopDataOutput(relativePath))
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var shopData = SwShShopDataFile.Parse(sourceBytes);
            var basePath = ResolveBaseSourcePath(project.Paths, relativePath);
            if (basePath is null || !File.Exists(basePath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy shop output requires base shop data so only vanilla Exp. Candy XL shop entries are removed.",
                    file: relativePath,
                    expected: "Readable base shop_data.bin"));
                return null;
            }

            var baseBytes = File.ReadAllBytes(basePath);
            var baseShopData = SwShShopDataFile.Parse(baseBytes);
            var mapping = SwShRoyalCandyShopPatchMapper.Analyze(shopData, baseShopData);
            if (mapping.BaseOccurrences == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy shop output could not find a base item 1128 shop occurrence.",
                    file: relativePath,
                    expected: "A vanilla shop inventory entry for item 1128"));
                return null;
            }

            var isInstalledRefresh = string.Equals(selectedWorkflow.Status, "installed", StringComparison.Ordinal);
            if (mapping.MissingOccurrences > 0 && !isInstalledRefresh)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy preserved the layered shop file because a base item 1128 occurrence was already missing before this workflow was installed.",
                    file: relativePath,
                    expected: "Every mapped base item 1128 occurrence present before a fresh Royal Candy install"));
                return null;
            }

            if (mapping.MatchedOccurrences == 0)
            {
                return sourceBytes;
            }

            var output = shopData.WriteEdits(mapping.RemovalEdits);
            var outputMapping = SwShRoyalCandyShopPatchMapper.Analyze(
                SwShShopDataFile.Parse(output),
                baseShopData);
            if (outputMapping.MatchedOccurrences != 0
                || outputMapping.MissingOccurrences != outputMapping.BaseOccurrences)
            {
                throw new InvalidDataException("Royal Candy shop output did not remove every uniquely mapped base item 1128 occurrence.");
            }

            return output;
        }

        if (IsItemTextOutput(relativePath))
        {
            var textFile = SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath));
            if (textFile.Lines.Count <= RoyalCandyItemId)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy item text output requires a text table with item 1128 present.",
                    file: relativePath,
                    expected: "Item text table containing item 1128"));
                return null;
            }

            var lines = textFile.Lines.ToArray();
            var replacement = GetRoyalCandyTextReplacement(relativePath, selectedWorkflow.WorkflowId);
            lines[RoyalCandyItemId] = lines[RoyalCandyItemId] with { Text = replacement };
            return textFile.WritePreserving(lines);
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            return SwShBagHookAmxPatcher.ApplySlotPatches(
                File.ReadAllBytes(source.AbsolutePath),
                [
                    new SwShBagHookSlotPatch(
                        SwShBagHookAmxPatcher.RoyalCandySlot,
                        RoyalCandyItemId,
                        1),
                ]);
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase))
        {
            var sourceBytes = ReadExeFsMainSourceBytes(project, source, diagnostics);
            if (sourceBytes is null)
            {
                return null;
            }

            var selectedGame = project.Paths.SelectedGame
                ?? SwShExeFsRoyalCandyMainPatcher.DetectSupportedGame(NsoFile.Parse(sourceBytes).BuildId)
                ?? throw new InvalidDataException("Royal Candy requires a supported Sword or Shield executable build.");

            if (UsesStoryLimits(selectedWorkflow))
            {
                var storyLevelCaps = CreateStoryLevelCapPatches(selectedWorkflow, levelCaps, diagnostics);
                if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    return null;
                }

                var output = SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
                    sourceBytes,
                    storyLevelCaps,
                    selectedGame);
                SwShExeFsRoyalCandyMainPatcher.VerifyStoryLimitsPatchOutput(
                    sourceBytes,
                    output,
                    storyLevelCaps,
                    selectedGame);
                return output;
            }

            var baseOutput = SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(
                sourceBytes,
                selectedGame);
            SwShExeFsRoyalCandyMainPatcher.VerifyBasePatchOutput(
                sourceBytes,
                baseOutput,
                selectedGame);
            return baseOutput;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Royal Candy output '{relativePath}' is not implemented by this apply workflow yet.",
            file: relativePath,
            expected: "Concrete Royal Candy apply target"));
        return null;
    }

    private static byte[]? ReadExeFsMainSourceBytes(
        OpenedProject project,
        WorkflowFileSource source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
        if (source.GraphEntry.LayeredFile is null)
        {
            return sourceBytes;
        }

        var signature = SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(
            sourceBytes,
            project.Paths.SelectedGame);
        if (signature.Kind is not (SwShRoyalCandyExeFsSignatureKind.Unlimited or SwShRoyalCandyExeFsSignatureKind.StoryLimits))
        {
            return sourceBytes;
        }

        var basePath = ResolveBaseSourcePath(project.Paths, SwShRoyalCandyWorkflowService.ExeFsMainPath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy ExeFS reapply requires base exefs/main so KM can refresh only its own owned bytes.",
                file: SwShRoyalCandyWorkflowService.ExeFsMainPath,
                expected: "Readable base exefs/main"));
            return null;
        }

        return SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(
            sourceBytes,
            File.ReadAllBytes(basePath),
            project.Paths.SelectedGame);
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
                "Royal Candy apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy apply target must be relative to the output root.",
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShItemsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy apply target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (targetRelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, targetRelativePath["romfs/".Length..]);
        }

        if (targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static bool TryRestoreFilteredItemHashFromBase(
        ProjectPaths paths,
        WorkflowFileSource source,
        byte[] sourceBytes,
        out byte[] restoredBytes)
    {
        restoredBytes = [];
        if (source.GraphEntry.LayeredFile is null || source.GraphEntry.BaseFile is null)
        {
            return false;
        }

        var basePath = ResolveBaseSourcePath(paths, SwShRoyalCandyWorkflowService.ItemHashPath);
        if (basePath is null || !File.Exists(basePath))
        {
            return false;
        }

        var baseBytes = File.ReadAllBytes(basePath);
        SwShItemHashTable baseTable;
        try
        {
            baseTable = SwShItemHashTable.Parse(baseBytes);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        if (baseTable.Entries.All(entry => entry.ItemId != 1128))
        {
            return false;
        }

        var exactLegacyOutput = baseTable.Write();
        if (exactLegacyOutput.SequenceEqual(baseBytes)
            || !sourceBytes.SequenceEqual(exactLegacyOutput))
        {
            return false;
        }

        restoredBytes = baseBytes;
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

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsConcreteApplyOutput(string relativePath)
    {
        return string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase)
            || IsShopDataOutput(relativePath)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
            || IsItemTextOutput(relativePath);
    }

    private static bool IsShopDataOutput(string relativePath)
    {
        return string.Equals(relativePath, SwShRoyalCandyWorkflowService.ShopDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.LegacyShopDataPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsItemTextOutput(string relativePath)
    {
        return TryGetMessageCommonFileName(relativePath, out var fileName)
            && (string.Equals(fileName, "iteminfo.dat", StringComparison.OrdinalIgnoreCase)
                // Match the original Royal Candy builder: every itemname*.dat table is owned text,
                // including classified plural tables used by the in-bag quantity prompt.
                || (fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)));
    }

    private static string GetRoyalCandyDescription(string workflowId)
    {
        return string.Equals(workflowId, StoryLimitsWorkflowId, StringComparison.Ordinal)
            ? StoryLimitsDescription
            : UnlimitedDescription;
    }

    private static string GetRoyalCandyTextReplacement(string relativePath, string workflowId)
    {
        if (TryGetMessageCommonFileName(relativePath, out var fileName)
            && fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase))
        {
            // The working reference uses plural by filename, not by exact table name.
            return fileName.Contains("plural", StringComparison.OrdinalIgnoreCase)
                ? RoyalCandyPluralName
                : RoyalCandyName;
        }

        return GetRoyalCandyDescription(workflowId);
    }

    private static bool TryGetMessageCommonFileName(string relativePath, out string fileName)
    {
        fileName = string.Empty;
        var parts = relativePath.Split('/');
        if (parts.Length != 6
            || !string.Equals(parts[0], "romfs", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[1], "bin", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "message", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[4], "common", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fileName = parts[5];
        return true;
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
            Domain: RoyalCandyEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
