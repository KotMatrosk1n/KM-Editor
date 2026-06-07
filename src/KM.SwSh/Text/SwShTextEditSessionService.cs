// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.Text;

public sealed class SwShTextEditSessionService
{
    public const string TextValueField = SwShTextWorkflowService.TextValueField;

    private const string TextEditDomain = "workflow.text";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShTextWorkflowService textWorkflowService;

    public SwShTextEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTextWorkflowService? textWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.textWorkflowService = textWorkflowService ?? new SwShTextWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShTextEditResult UpdateEntry(
        ProjectPaths paths,
        EditSession? session,
        string textKey,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(textKey);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = textWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditText(project, workflow, diagnostics))
        {
            return new SwShTextEditResult(workflow, currentSession, diagnostics);
        }

        var selectedEntry = workflow.Entries.FirstOrDefault(entry =>
            string.Equals(entry.TextKey, textKey, StringComparison.Ordinal));
        if (selectedEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text entry '{textKey}' is not present in the loaded Text workflow.",
                field: "textKey",
                expected: "Existing text entry"));
            return new SwShTextEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedEntry, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShTextEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingTextEdit(currentSession, pendingEdit);

        return new SwShTextEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = textWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditText(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending text change is valid."));
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

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Text edit before reviewing a change plan.",
                expected: "Pending text edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = CreatePlannedWrites(paths, session.PendingEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target file{(writes.Count == 1 ? string.Empty : "s")}."));

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
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Text change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var pendingOutputs = new List<TextOutput>();

        foreach (var editGroup in session.PendingEdits.GroupBy(edit => GetSourceFile(edit.RecordId)))
        {
            if (string.IsNullOrWhiteSpace(editGroup.Key))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending text edit does not include a valid source file.",
                    field: "textKey",
                    expected: "Text key in source#line format"));
                continue;
            }

            var source = SwShTextWorkflowService.ResolveWorkflowFile(project, editGroup.Key);
            if (source is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Text apply could not resolve source message table '{editGroup.Key}'.",
                    file: editGroup.Key,
                    expected: "Loaded Sword/Shield message table"));
                continue;
            }

            var targetPath = ResolveOutputPath(paths, source.Entry.RelativePath, diagnostics);
            if (targetPath is null)
            {
                continue;
            }

            try
            {
                var textFile = SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath));
                var lines = textFile.Lines.ToArray();

                foreach (var edit in editGroup)
                {
                    if (!SwShTextWorkflowService.TryParseTextKey(edit.RecordId, out _, out var lineIndex)
                        || lineIndex >= lines.Length)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "Pending text edit targets a line that is not loaded.",
                            field: "textKey",
                            expected: "Existing text line"));
                        continue;
                    }

                    var value = edit.NewValue ?? string.Empty;
                    if (!TryValidateTextValue(value, lines[lineIndex].Text, diagnostics))
                    {
                        continue;
                    }

                    lines[lineIndex] = lines[lineIndex] with { Text = value };
                }

                pendingOutputs.Add(new TextOutput(source.Entry.RelativePath, targetPath, SwShGameTextFile.Write(lines)));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Text source file could not be decoded: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Sword/Shield encrypted text table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Text source file could not be read: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Readable Sword/Shield message table"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Text source file could not be read: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Readable Sword/Shield message table"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        foreach (var output in pendingOutputs)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output.AbsolutePath)!);
                File.WriteAllBytes(output.AbsolutePath, output.Contents);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, output.RelativePath));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Text output file could not be written: {exception.Message}",
                    file: output.RelativePath,
                    expected: "Writable output root"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Text output file could not be written: {exception.Message}",
                    file: output.RelativePath,
                    expected: "Writable output root"));
            }
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Text change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditText(
        OpenedProject project,
        SwShTextWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Text edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void ValidatePendingEdit(
        SwShTextWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, TextEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Text workflow.",
                expected: TextEditDomain));
            return;
        }

        if (!string.Equals(edit.Field, TextValueField, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        var entry = workflow.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.TextKey, edit.RecordId, StringComparison.Ordinal));
        if (entry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending text edit targets a record that is not loaded.",
                field: "textKey",
                expected: "Existing text entry"));
            return;
        }

        if (!entry.CanEdit)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text entry '{entry.Label}' is read-only: {entry.EditBlockedReason}",
                field: TextValueField,
                expected: "Editable text line"));
            return;
        }

        TryValidateTextValue(edit.NewValue ?? string.Empty, entry.Value, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShTextEntryRecord selectedEntry,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!selectedEntry.CanEdit)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text entry '{selectedEntry.Label}' is read-only: {selectedEntry.EditBlockedReason}",
                field: TextValueField,
                expected: "Editable text line"));
            return null;
        }

        if (!TryValidateTextValue(value, selectedEntry.Value, diagnostics))
        {
            return null;
        }

        return new PendingEdit(
            TextEditDomain,
            $"Set {selectedEntry.Label} to \"{CreatePreview(value)}\".",
            [new ProjectFileReference(selectedEntry.Provenance.SourceLayer, selectedEntry.Provenance.SourceFile)],
            RecordId: selectedEntry.TextKey,
            Field: TextValueField,
            NewValue: value);
    }

    private static bool TryValidateTextValue(
        string value,
        string currentValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (value.Length > SwShTextWorkflowService.MaximumTextLength)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text value must be {SwShTextWorkflowService.MaximumTextLength} characters or fewer.",
                field: TextValueField,
                expected: "Safe text line length"));
            return false;
        }

        if (!SwShTextWorkflowService.IsSafelyEditable(currentValue)
            || !SwShTextWorkflowService.IsSafelyEditable(value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Text lines with variable placeholders are read-only in this text editing slice.",
                field: TextValueField,
                expected: "Plain text without variable placeholders"));
            return false;
        }

        return true;
    }

    private static EditSession ReplacePendingTextEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameTextEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameTextEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShTextWorkflow OverlayPendingEdit(SwShTextWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, TextEditDomain, StringComparison.Ordinal)
            || !string.Equals(edit.Field, TextValueField, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(edit.RecordId))
        {
            return workflow;
        }

        var newValue = edit.NewValue ?? string.Empty;
        var updatedEntries = workflow.Entries
            .Select(entry => string.Equals(entry.TextKey, edit.RecordId, StringComparison.Ordinal)
                ? entry with { Value = newValue }
                : entry)
            .ToArray();
        var textId = updatedEntries.FirstOrDefault(entry =>
            string.Equals(entry.TextKey, edit.RecordId, StringComparison.Ordinal))?.TextId;
        var updatedReferences = textId is null
            ? workflow.DialogueReferences
            : workflow.DialogueReferences
                .Select(reference => reference.TextId == textId.Value
                    ? reference with { Preview = CreatePreview(newValue) }
                    : reference)
                .ToArray();

        return workflow with
        {
            Entries = updatedEntries,
            DialogueReferences = updatedReferences,
        };
    }

    private static SwShTextWorkflow OverlayPendingEdits(
        SwShTextWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static IReadOnlyList<PlannedFileWrite> CreatePlannedWrites(
        ProjectPaths paths,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return edits
            .GroupBy(edit => GetSourceFile(edit.RecordId), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var targetRelativePath = group.Key;
                if (string.IsNullOrWhiteSpace(targetRelativePath))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending text edit does not include a valid source file.",
                        field: "textKey",
                        expected: "Text key in source#line format"));
                    return null;
                }

                var targetPath = SwShTextWorkflowService.ResolveOutputPath(paths, targetRelativePath);
                if (targetPath is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Text apply target must stay inside the configured output root.",
                        file: targetRelativePath,
                        expected: "Output-root-contained target"));
                    return null;
                }

                var groupEdits = group.ToArray();
                var sources = groupEdits
                    .SelectMany(edit => edit.Sources)
                    .Distinct()
                    .ToArray();
                var reason = groupEdits.Length == 1
                    ? $"Apply pending Text edit: {groupEdits[0].Summary}"
                    : $"Apply {groupEdits.Length} pending Text edits.";

                return new PlannedFileWrite(
                    targetRelativePath,
                    sources,
                    File.Exists(targetPath),
                    reason);
            })
            .Where(write => write is not null)
            .Select(write => write!)
            .OrderBy(write => write.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
                "Text apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Text apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShTextWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Text apply target must stay inside the configured output root.",
                file: targetRelativePath,
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

    private static string? GetSourceFile(string? textKey)
    {
        return SwShTextWorkflowService.TryParseTextKey(textKey, out var sourceFile, out _)
            ? sourceFile
            : null;
    }

    private static string CreatePreview(string value)
    {
        const int maxPreviewLength = 72;
        var singleLine = value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return singleLine.Length <= maxPreviewLength ? singleLine : $"{singleLine[..maxPreviewLength]}...";
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Text field '{field}' is not supported by the Text workflow yet.",
            field: "field",
            expected: TextValueField);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: TextEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record TextOutput(
        string RelativePath,
        string AbsolutePath,
        byte[] Contents);
}
