// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SV.Workflows;

namespace KM.SV.Text;

public sealed class SvTextEditSessionService
{
    public const string TextValueField = SvTextWorkflowService.TextValueField;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvTextWorkflowService textWorkflowService;

    internal SvTextEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvTextWorkflowService? textWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.textWorkflowService = textWorkflowService ?? new SvTextWorkflowService(this.fileSource);
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SvTextEditResult UpdateEntry(
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
            return new SvTextEditResult(workflow, currentSession, diagnostics);
        }

        var selectedEntry = workflow.Entries.FirstOrDefault(entry =>
            string.Equals(entry.TextKey, textKey, StringComparison.Ordinal));
        if (selectedEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text entry '{textKey}' is not present in the loaded S/V Text workflow.",
                field: "textKey",
                expected: "Existing text entry"));
            return new SvTextEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedEntry, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvTextEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);

        return new SvTextEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
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
                "Pending S/V text change is valid."));
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

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending S/V Text edit before reviewing a change plan.",
                expected: "Pending S/V text edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = CreatePlannedWrites(paths, session.PendingEdits, outputMode, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        if (outputMode == SvOutputMode.Standalone)
        {
            var descriptorWriteInfo = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
            writes.Add(new PlannedFileWrite(
                descriptorWriteInfo.TargetRelativePath,
                descriptorWriteInfo.Sources,
                descriptorWriteInfo.ReplacesExistingOutput,
                "Patch Scarlet/Violet Trinity descriptor for standalone LayeredFS overrides."));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target file{(writes.Count == 1 ? string.Empty : "s")}."));

        return new ChangePlan(
            session.Id,
            writes.OrderBy(write => write.TargetRelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics);
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

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed S/V Text change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var pendingOutputs = new List<TextOutput>();

        foreach (var editGroup in session.PendingEdits.GroupBy(GetVirtualPath, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(editGroup.Key))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending S/V text edit does not include a valid source file.",
                    field: "textKey",
                    expected: "Text key in source#line format"));
                continue;
            }

            try
            {
                var source = fileSource.Read(project, editGroup.Key);
                var textFile = SwShGameTextFile.Parse(source.Bytes);
                var lines = textFile.Lines.ToArray();

                foreach (var edit in editGroup)
                {
                    if (!SvTextWorkflowService.TryParseTextKey(edit.RecordId, out _, out var lineIndex)
                        || lineIndex >= lines.Length)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "Pending S/V text edit targets a line that is not loaded.",
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

                pendingOutputs.Add(new TextOutput(editGroup.Key, SwShGameTextFile.Write(lines)));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"S/V text source file could not be decoded: {exception.Message}",
                    file: $"romfs/{editGroup.Key}",
                    expected: "S/V encrypted text table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"S/V text source file could not be read: {exception.Message}",
                    file: $"romfs/{editGroup.Key}",
                    expected: "Readable S/V message table"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"S/V text source file could not be read: {exception.Message}",
                    file: $"romfs/{editGroup.Key}",
                    expected: "Readable S/V message table"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        foreach (var output in pendingOutputs)
        {
            try
            {
                SvWorkflowFileSource.Write(paths, output.VirtualPath, output.Contents, outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(output.VirtualPath, outputMode));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"S/V text output file could not be written: {exception.Message}",
                    file: $"romfs/{output.VirtualPath}",
                    expected: "Writable output root"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"S/V text output file could not be written: {exception.Message}",
                    file: $"romfs/{output.VirtualPath}",
                    expected: "Writable output root"));
            }
        }

        if (outputMode == SvOutputMode.Standalone && writtenFiles.Count > 0)
        {
            writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                SvEditSessionSupport.CreateApplyOutputMessage("S/V Text", outputMode)));
        }

        return SvEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
    }

    private static bool CanEditText(
        OpenedProject project,
        SvTextWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.TextDomain,
            diagnostics);
    }

    private static void ValidatePendingEdit(
        SvTextWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TextDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the S/V Text workflow.",
                expected: SvEditSessionSupport.TextDomain));
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
                "Pending S/V text edit targets a record that is not loaded.",
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
        SvTextEntryRecord selectedEntry,
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

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.TextDomain,
            $"Set {selectedEntry.Label} to \"{SvTextWorkflowService.CreatePreview(value)}\".",
            new ProjectFileReference(selectedEntry.Provenance.SourceLayer, selectedEntry.Provenance.SourceFile),
            selectedEntry.TextKey,
            TextValueField,
            value);
    }

    private static bool TryValidateTextValue(
        string value,
        string currentValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (value.Length > SvTextWorkflowService.MaximumTextLength)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text value must be {SvTextWorkflowService.MaximumTextLength} characters or fewer.",
                field: TextValueField,
                expected: "Safe text line length"));
            return false;
        }

        if (!SvTextWorkflowService.IsSafelyEditable(currentValue)
            || !SvTextWorkflowService.IsSafelyEditable(value))
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

    private static SvTextWorkflow OverlayPendingEdit(SvTextWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TextDomain, StringComparison.Ordinal)
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
                    ? reference with { Preview = SvTextWorkflowService.CreatePreview(newValue) }
                    : reference)
                .ToArray();

        return workflow with
        {
            Entries = updatedEntries,
            DialogueReferences = updatedReferences,
        };
    }

    private static SvTextWorkflow OverlayPendingEdits(
        SvTextWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static List<PlannedFileWrite> CreatePlannedWrites(
        ProjectPaths paths,
        IReadOnlyList<PendingEdit> edits,
        SvOutputMode outputMode,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return edits
            .GroupBy(GetVirtualPath, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending S/V text edit does not include a valid source file.",
                        field: "textKey",
                        expected: "Text key in source#line format"));
                    return null;
                }

                PlannedWriteInfo writeInfo;
                try
                {
                    writeInfo = SvWorkflowFileSource.CreatePlannedWrite(
                        paths,
                        group.Key,
                        group.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                        outputMode);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"S/V Text change plan could not resolve the output target: {exception.Message}",
                        file: $"romfs/{group.Key}",
                        expected: "Writable output root"));
                    return null;
                }

                var groupEdits = group.ToArray();
                var reason = groupEdits.Length == 1
                    ? $"Apply pending S/V Text edit: {groupEdits[0].Summary}"
                    : $"Apply {groupEdits.Length} pending S/V Text edits.";

                return new PlannedFileWrite(
                    writeInfo.TargetRelativePath,
                    writeInfo.Sources,
                    writeInfo.ReplacesExistingOutput,
                    reason);
            })
            .Where(write => write is not null)
            .Select(write => write!)
            .ToList();
    }

    private static string? GetVirtualPath(PendingEdit edit)
    {
        return SvTextWorkflowService.TryGetVirtualPathFromTextKey(edit.RecordId, out var virtualPath, out _)
            ? virtualPath
            : null;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Text field '{field}' is not supported by the S/V Text workflow yet.",
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
            Domain: SvEditSessionSupport.TextDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record TextOutput(string VirtualPath, byte[] Contents);
}
