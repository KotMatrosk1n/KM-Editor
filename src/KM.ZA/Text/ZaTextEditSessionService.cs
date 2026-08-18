// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.ZA.Workflows;

namespace KM.ZA.Text;

public sealed class ZaTextEditSessionService
{
    public const string TextValueField = ZaTextWorkflowService.TextValueField;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaTextWorkflowService textWorkflowService;

    internal ZaTextEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaTextWorkflowService? textWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.textWorkflowService = textWorkflowService ?? new ZaTextWorkflowService(this.fileSource);
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public ZaTextEditResult UpdateEntry(
        ProjectPaths paths,
        EditSession? session,
        string textKey,
        string value,
        ZaTextWorkflowQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(textKey);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = textWorkflowService.Load(project, query);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditText(project, workflow, diagnostics))
        {
            return new ZaTextEditResult(workflow, currentSession, diagnostics);
        }

        var selectedEntry = workflow.Entries.FirstOrDefault(entry =>
            string.Equals(entry.TextKey, textKey, StringComparison.Ordinal));
        if (selectedEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text entry '{textKey}' is not present in the loaded Pokemon Legends Z-A Text workflow.",
                field: "textKey",
                expected: "Existing text entry"));
            return new ZaTextEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedEntry, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaTextEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);

        return new ZaTextEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        var summary = textWorkflowService.CreateSummary(project);

        if (ZaEditSessionSupport.CanEdit(
            project,
            summary,
            summary.Diagnostics,
            ZaEditSessionSupport.TextDomain,
            diagnostics))
        {
            ValidatePendingEdits(project, session.PendingEdits, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Pokemon Legends Z-A text change is valid."));
        }

        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Pokemon Legends Z-A Text edit before reviewing a change plan.",
                expected: "Pending Pokemon Legends Z-A text edit"));
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

        if (outputMode == ZaOutputMode.Standalone)
        {
            var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
            writes.Add(new PlannedFileWrite(
                descriptorWriteInfo.TargetRelativePath,
                descriptorWriteInfo.Sources,
                descriptorWriteInfo.ReplacesExistingOutput,
                "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides."));
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
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Pokemon Legends Z-A Text change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var pendingOutputs = new List<TextOutput>();

        foreach (var editGroup in session.PendingEdits.GroupBy(GetVirtualPath, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(editGroup.Key))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon Legends Z-A text edit does not include a valid source file.",
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
                    if (!ZaTextWorkflowService.TryParseTextKey(edit.RecordId, out _, out var lineIndex)
                        || lineIndex >= lines.Length)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "Pending Pokemon Legends Z-A text edit targets a line that is not loaded.",
                            field: "textKey",
                            expected: "Existing text line"));
                        continue;
                    }

                    var value = edit.NewValue ?? string.Empty;
                    if (!TryValidateTextValue(value, diagnostics))
                    {
                        continue;
                    }

                    lines[lineIndex] = lines[lineIndex] with { Text = value };
                }

                pendingOutputs.Add(new TextOutput(
                    editGroup.Key,
                    textFile.WritePreserving(lines, GameTextNullLineEncoding.PayloadCountTwo)));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon Legends Z-A text source file could not be decoded: {exception.Message}",
                    file: $"romfs/{editGroup.Key}",
                    expected: "Pokemon Legends Z-A encrypted text table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon Legends Z-A text source file could not be read: {exception.Message}",
                    file: $"romfs/{editGroup.Key}",
                    expected: "Readable Pokemon Legends Z-A message table"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon Legends Z-A text source file could not be read: {exception.Message}",
                    file: $"romfs/{editGroup.Key}",
                    expected: "Readable Pokemon Legends Z-A message table"));
            }
            catch (ArgumentException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending Pokemon Legends Z-A text edit has an invalid source path: {exception.Message}",
                    file: $"romfs/{editGroup.Key}",
                    expected: "Canonical Pokemon Legends Z-A message-table path"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (pendingOutputs.Count > 0)
        {
            try
            {
                ZaWorkflowFileSource.WriteBatch(
                    paths,
                    pendingOutputs
                        .Select(output => new ZaWorkflowFileWrite(
                            output.VirtualPath,
                            output.Contents))
                        .ToArray(),
                    outputMode,
                    revalidateReviewedState: () =>
                        ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                            reviewedPlan,
                            CreateChangePlan(paths, session, outputMode)));
                writtenFiles.AddRange(pendingOutputs.Select(output =>
                    ZaEditSessionSupport.GeneratedReference(
                        output.VirtualPath,
                        outputMode)));
            }
            catch (IOException exception) when (!ZaEditSessionSupport.IsOutputSafetyException(exception))
            {
                AddOutputDiagnostics(pendingOutputs, exception, diagnostics);
            }
            catch (UnauthorizedAccessException exception)
            {
                AddOutputDiagnostics(pendingOutputs, exception, diagnostics);
            }
        }

        if (outputMode == ZaOutputMode.Standalone && writtenFiles.Count > 0)
        {
            writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Pokemon Legends Z-A Text", outputMode)));
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
    }

    private static void AddOutputDiagnostics(
        IEnumerable<TextOutput> outputs,
        Exception exception,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var output in outputs)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Legends Z-A text output file could not be written: {exception.Message}",
                file: $"romfs/{output.VirtualPath}",
                expected: "Writable output root"));
        }
    }

    private static bool CanEditText(
        OpenedProject project,
        ZaTextWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.TextDomain,
            diagnostics);
    }

    private void ValidatePendingEdits(
        OpenedProject project,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var validTargets = new List<(PendingEdit Edit, string VirtualPath, int LineIndex)>();
        foreach (var edit in edits)
        {
            if (!string.Equals(edit.Domain, ZaEditSessionSupport.TextDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by the Pokemon Legends Z-A Text workflow.",
                    expected: ZaEditSessionSupport.TextDomain));
                continue;
            }

            if (!string.Equals(edit.Field, TextValueField, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                continue;
            }

            if (!ZaTextWorkflowService.TryGetVirtualPathFromTextKey(
                edit.RecordId,
                out var virtualPath,
                out var lineIndex))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon Legends Z-A text edit does not include a valid source line.",
                    field: "textKey",
                    expected: "Text key in source#line format"));
                continue;
            }

            validTargets.Add((edit, virtualPath, lineIndex));
        }

        foreach (var targetGroup in validTargets.GroupBy(
            target => target.VirtualPath,
            StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var source = fileSource.Read(project, targetGroup.Key);
                var textFile = SwShGameTextFile.Parse(source.Bytes);
                foreach (var target in targetGroup)
                {
                    if (target.LineIndex >= textFile.Lines.Count)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "Pending Pokemon Legends Z-A text edit targets a line that is not present in its source table.",
                            field: "textKey",
                            file: $"romfs/{target.VirtualPath}",
                            expected: "Existing text line"));
                        continue;
                    }

                    TryValidateTextValue(target.Edit.NewValue ?? string.Empty, diagnostics);
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon Legends Z-A text source file could not be decoded: {exception.Message}",
                    file: $"romfs/{targetGroup.Key}",
                    expected: "Pokemon Legends Z-A encrypted text table"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon Legends Z-A text source file could not be read: {exception.Message}",
                    file: $"romfs/{targetGroup.Key}",
                    expected: "Readable Pokemon Legends Z-A message table"));
            }
        }
    }

    private static PendingEdit? CreatePendingEdit(
        ZaTextEntryRecord selectedEntry,
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

        if (!TryValidateTextValue(value, diagnostics))
        {
            return null;
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.TextDomain,
            $"Set {selectedEntry.Label} to \"{ZaTextWorkflowService.CreatePreview(value)}\".",
            new ProjectFileReference(selectedEntry.Provenance.SourceLayer, selectedEntry.Provenance.SourceFile),
            selectedEntry.TextKey,
            TextValueField,
            value);
    }

    private static bool TryValidateTextValue(
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (value.Length > ZaTextWorkflowService.MaximumTextLength)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text value must be {ZaTextWorkflowService.MaximumTextLength} characters or fewer.",
                field: TextValueField,
                expected: "Safe text line length"));
            return false;
        }

        try
        {
            SwShGameTextFile.ValidateText(value);
            return true;
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                field: TextValueField,
                expected: "Valid escaped text, [VAR], [WAIT n], [~ n], or {base|ruby} syntax"));
            return false;
        }
    }

    private static ZaTextWorkflow OverlayPendingEdit(ZaTextWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.TextDomain, StringComparison.Ordinal)
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
                    ? reference with { Preview = ZaTextWorkflowService.CreatePreview(newValue) }
                    : reference)
                .ToArray();

        return workflow with
        {
            Entries = updatedEntries,
            DialogueReferences = updatedReferences,
        };
    }

    private static ZaTextWorkflow OverlayPendingEdits(
        ZaTextWorkflow workflow,
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
        ZaOutputMode outputMode,
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
                        "Pending Pokemon Legends Z-A text edit does not include a valid source file.",
                        field: "textKey",
                        expected: "Text key in source#line format"));
                    return null;
                }

                PlannedWriteInfo writeInfo;
                try
                {
                    writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                        paths,
                        group.Key,
                        group.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                        outputMode);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Pokemon Legends Z-A Text change plan could not resolve the output target: {exception.Message}",
                        file: $"romfs/{group.Key}",
                        expected: "Writable output root"));
                    return null;
                }

                var groupEdits = group.ToArray();
                var reason = groupEdits.Length == 1
                    ? $"Apply pending Pokemon Legends Z-A Text edit: {groupEdits[0].Summary}"
                    : $"Apply {groupEdits.Length} pending Pokemon Legends Z-A Text edits.";

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
        return ZaTextWorkflowService.TryGetVirtualPathFromTextKey(edit.RecordId, out var virtualPath, out _)
            ? virtualPath
            : null;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Text field '{field}' is not supported by the Pokemon Legends Z-A Text workflow yet.",
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
            Domain: ZaEditSessionSupport.TextDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record TextOutput(string VirtualPath, byte[] Contents);
}


