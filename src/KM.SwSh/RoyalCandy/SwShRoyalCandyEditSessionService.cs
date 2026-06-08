// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
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
    private const string RoyalCandyName = "Royal Candy";
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

    public SwShRoyalCandyEditResult StageWorkflow(
        ProjectPaths paths,
        string workflowId,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(workflowId);

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

        var pendingEdit = CreatePendingEdit(selectedWorkflow);
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

        var project = projectWorkspaceService.Open(paths);
        var workflow = royalCandyWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage a Royal Candy workflow before validating.",
                expected: "Pending Royal Candy workflow"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(project, workflow, edit, diagnostics);
        }

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

        var writes = CreateConcreteWrites(paths, workflow, selectedWorkflow, diagnostics);
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
                "Reviewed Royal Candy change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Royal Candy change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var edit = session.PendingEdits.Single();
        var workflowId = edit.RecordId ?? string.Empty;
        var isCleanup = string.Equals(workflowId, UninstallWorkflowId, StringComparison.Ordinal);

        foreach (var write in currentPlan.Writes)
        {
            var targetPath = ResolveOutputPath(paths, write.TargetRelativePath, diagnostics);
            if (targetPath is null)
            {
                continue;
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
                        continue;
                    }

                    File.Delete(targetPath);
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, write.TargetRelativePath));
                    continue;
                }

                var output = CreateOutputBytes(project, workflowId, write.TargetRelativePath, diagnostics);
                if (output is null)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllBytes(targetPath, output);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, write.TargetRelativePath));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy source file '{write.TargetRelativePath}' could not be decoded: {exception.Message}",
                    file: write.TargetRelativePath,
                    expected: "Supported Sword/Shield source data"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy output file '{write.TargetRelativePath}' could not be written: {exception.Message}",
                    file: write.TargetRelativePath,
                    expected: "Writable output root"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Royal Candy output file '{write.TargetRelativePath}' could not be written: {exception.Message}",
                    file: write.TargetRelativePath,
                    expected: "Writable output root"));
            }
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                isCleanup
                    ? "Removed reviewed Royal Candy LayeredFS output files from the configured output root."
                    : "Applied Royal Candy change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
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

        if (selectedWorkflow.Status == "blocked" || selectedWorkflow.Status == "readOnly")
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Royal Candy workflow '{selectedWorkflow.Name}' is not ready to stage.",
                expected: "Available or warning workflow status"));
            return false;
        }

        var isCleanup = string.Equals(selectedWorkflow.WorkflowId, UninstallWorkflowId, StringComparison.Ordinal);
        if (!isCleanup)
        {
            foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(diagnostic);
            }
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
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

        var selectedWorkflow = GetApplicableWorkflow(workflow, edit.RecordId ?? string.Empty, diagnostics);
        if (selectedWorkflow is null)
        {
            return;
        }

        CanStage(project, workflow, selectedWorkflow, diagnostics);
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

    private static PendingEdit CreatePendingEdit(SwShRoyalCandyWorkflowRecord workflow)
    {
        return new PendingEdit(
            RoyalCandyEditDomain,
            $"Stage Royal Candy workflow: {workflow.Name}.",
            [new ProjectFileReference(workflow.Provenance.SourceLayer, workflow.Provenance.SourceFile)],
            RecordId: workflow.WorkflowId,
            Field: WorkflowField,
            NewValue: workflow.Mode);
    }

    private static IReadOnlyList<PlannedFileWrite> CreateConcreteWrites(
        ProjectPaths paths,
        SwShRoyalCandyWorkflow workflow,
        SwShRoyalCandyWorkflowRecord selectedWorkflow,
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
                [new ProjectFileReference(output.Provenance.SourceLayer, output.Provenance.SourceFile)],
                File.Exists(targetPath),
                isCleanup
                    ? $"Remove Royal Candy LayeredFS output: {output.Description}"
                    : $"Apply Royal Candy workflow '{selectedWorkflow.Name}': {output.Description}"));
        }

        return writes;
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
            "Royal Candy plan currently generates item data, item text, the Bag-event grant, and ExeFS item-use patches. Shop, raid reward, and placement targets remain reserved for their dedicated apply workflows.",
            expected: "Review remaining Royal Candy output targets before full install"));
    }

    private static byte[]? CreateOutputBytes(
        OpenedProject project,
        string workflowId,
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
            return SwShItemTable.Parse(File.ReadAllBytes(source.AbsolutePath))
                .WriteClonedRows(
                [
                    new SwShItemTableCloneEdit(
                        TemplateItemId: 50,
                        TargetItemId: 1128),
                ]);
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase))
        {
            var itemHashTable = SwShItemHashTable.Parse(File.ReadAllBytes(source.AbsolutePath));
            if (itemHashTable.Entries.All(entry => entry.ItemId != 1128))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy item hash output needs an existing hash-table entry for item 1128 before it can be preserved safely.",
                    file: relativePath,
                    expected: "Item hash table entry for item 1128"));
                return null;
            }

            return itemHashTable.Write();
        }

        if (IsItemTextOutput(relativePath))
        {
            var textFile = SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath));
            if (textFile.Lines.Count <= 1128)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy item text output requires a text table with item 1128 present.",
                    file: relativePath,
                    expected: "Item text table containing item 1128"));
                return null;
            }

            var lines = textFile.Lines.ToArray();
            var replacement = relativePath.EndsWith("/itemname.dat", StringComparison.OrdinalIgnoreCase)
                ? RoyalCandyName
                : GetRoyalCandyDescription(workflowId);
            lines[1128] = lines[1128] with { Text = replacement };
            return SwShGameTextFile.Write(lines);
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            return SwShRoyalCandyBagEventScriptPatcher.ApplyGrantPatch(File.ReadAllBytes(source.AbsolutePath), 1128);
        }

        if (string.Equals(relativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase))
        {
            return SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(File.ReadAllBytes(source.AbsolutePath));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Royal Candy output '{relativePath}' is not implemented by this apply workflow yet.",
            file: relativePath,
            expected: "Concrete Royal Candy apply target"));
        return null;
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
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
            || IsItemTextOutput(relativePath);
    }

    private static bool IsItemTextOutput(string relativePath)
    {
        return relativePath.StartsWith("romfs/bin/message/", StringComparison.OrdinalIgnoreCase)
            && (relativePath.EndsWith("/itemname.dat", StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith("/iteminfo.dat", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRoyalCandyDescription(string workflowId)
    {
        return string.Equals(workflowId, StoryLimitsWorkflowId, StringComparison.Ordinal)
            ? StoryLimitsDescription
            : UnlimitedDescription;
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
