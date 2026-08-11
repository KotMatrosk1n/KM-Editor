// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.Formats.SwSh;
using KM.SV.Workflows;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SV.Text;

public sealed class SvTextEditSessionService
{
    public const string TextValueField = SvTextWorkflowService.TextValueField;

    private static readonly EnumerationOptions OutputEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };

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
        string value,
        SvTextWorkflowQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(textKey);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var sourceWorkflow = textWorkflowService.Load(project, query);
        var workflow = OverlayPendingEdits(sourceWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditText(project, workflow, diagnostics))
        {
            return new SvTextEditResult(workflow, currentSession, diagnostics);
        }

        var selectedEntry = workflow.Entries.FirstOrDefault(entry =>
            string.Equals(entry.TextKey, textKey, StringComparison.Ordinal));
        var sourceEntry = sourceWorkflow.Entries.FirstOrDefault(entry =>
            string.Equals(entry.TextKey, textKey, StringComparison.Ordinal));
        if (selectedEntry is null || sourceEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text entry '{textKey}' is not present in the loaded S/V Text workflow.",
                field: "textKey",
                expected: "Existing text entry"));
            return new SvTextEditResult(workflow, currentSession, diagnostics);
        }

        if (string.Equals(value, sourceEntry.Value, StringComparison.Ordinal))
        {
            var revertedSession = RemovePendingTextEdit(currentSession, textKey);
            return new SvTextEditResult(
                OverlayPendingEdits(sourceWorkflow, revertedSession.PendingEdits),
                revertedSession,
                diagnostics);
        }

        var pendingEdit = CreatePendingEdit(sourceEntry, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvTextEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);

        return new SvTextEditResult(
            OverlayPendingEdits(sourceWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        var summary = textWorkflowService.CreateSummary(project);

        SvEditSessionSupport.CanEdit(
            project,
            summary,
            summary.Diagnostics,
            SvEditSessionSupport.TextDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(project, edit, diagnostics);
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

        var project = projectWorkspaceService.Open(paths);
        var writes = CreatePlannedWrites(project, paths, session.PendingEdits, outputMode, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        if (outputMode == SvOutputMode.Standalone)
        {
            try
            {
                var descriptorWriteInfo = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Scarlet/Violet Trinity descriptor for standalone LayeredFS overrides.",
                    CreateDescriptorPlanFingerprint(project, paths, session.PendingEdits)));
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or NotSupportedException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"S/V Text change plan could not inspect the standalone descriptor inputs: {exception.Message}",
                    file: $"romfs/{SvWorkflowFileSource.DescriptorVirtualPath}",
                    expected: "Readable base descriptor and output state"));
            }
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

        try
        {
            lock (SvWorkflowFileSource.OutputWriteSyncRoot)
            {
                return ApplyChangePlanCore(paths, session, reviewedPlan, outputMode);
            }
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
            textWorkflowService.ClearMemoryCache();
        }
    }

    private ApplyResult ApplyChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
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

        var snapshots = CaptureOutputSnapshots(paths, pendingOutputs, outputMode, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        TextOutput? activeOutput = null;
        try
        {
            foreach (var output in pendingOutputs)
            {
                activeOutput = output;
                SvWorkflowFileSource.Write(paths, output.VirtualPath, output.Contents, outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(output.VirtualPath, outputMode));
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V text output file could not be written: {exception.Message}",
                file: activeOutput is null ? null : $"romfs/{activeOutput.VirtualPath}",
                expected: "Writable output root"));
            RestoreOutputSnapshots(snapshots, diagnostics);
            writtenFiles.Clear();
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

    private static IReadOnlyList<OutputSnapshot> CaptureOutputSnapshots(
        ProjectPaths paths,
        IReadOnlyList<TextOutput> outputs,
        SvOutputMode outputMode,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var output in outputs)
            {
                targetPaths.Add(SvWorkflowFileSource.ResolveOutputPath(
                    paths,
                    output.VirtualPath,
                    outputMode));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                targetPaths.Add(SvWorkflowFileSource.ResolveOutputPath(
                    paths,
                    SvWorkflowFileSource.DescriptorVirtualPath,
                    outputMode));
            }

            return targetPaths
                .Select(path => File.Exists(path)
                    ? new OutputSnapshot(
                        path,
                        Existed: true,
                        File.ReadAllBytes(path),
                        File.GetLastWriteTimeUtc(path))
                    : new OutputSnapshot(
                        path,
                        Existed: false,
                        Contents: null,
                        LastWriteTimeUtc: null))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V Text output rollback state could not be prepared: {exception.Message}",
                expected: "Readable and writable output targets"));
            return [];
        }
    }

    private static void RestoreOutputSnapshots(
        IReadOnlyList<OutputSnapshot> snapshots,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var snapshot in snapshots.Reverse())
        {
            try
            {
                if (snapshot.Existed)
                {
                    File.WriteAllBytes(snapshot.Path, snapshot.Contents!);
                    File.SetLastWriteTimeUtc(snapshot.Path, snapshot.LastWriteTimeUtc!.Value);
                }
                else if (File.Exists(snapshot.Path))
                {
                    File.Delete(snapshot.Path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"S/V Text output rollback could not restore a target: {exception.Message}",
                    file: snapshot.Path,
                    expected: "Original output state"));
            }
        }
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

    private void ValidatePendingEdit(
        OpenedProject project,
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

        if (!textWorkflowService.TryLoadEntry(project, edit.RecordId, diagnostics, out var entry)
            || entry is null)
        {
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

        TryValidateTextValue(edit.NewValue ?? string.Empty, diagnostics);
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

        if (!TryValidateTextValue(value, diagnostics))
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

    private static EditSession RemovePendingTextEdit(EditSession session, string textKey)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !string.Equals(edit.Domain, SvEditSessionSupport.TextDomain, StringComparison.Ordinal)
                || !string.Equals(edit.RecordId, textKey, StringComparison.Ordinal)
                || !string.Equals(edit.Field, TextValueField, StringComparison.Ordinal))
            .ToArray();

        return session with { PendingEdits = pendingEdits };
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

    private List<PlannedFileWrite> CreatePlannedWrites(
        OpenedProject project,
        ProjectPaths paths,
        IReadOnlyList<PendingEdit> edits,
        SvOutputMode outputMode,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var writes = new List<PlannedFileWrite>();
        foreach (var group in edits.GroupBy(GetVirtualPath, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending S/V text edit does not include a valid source file.",
                    field: "textKey",
                    expected: "Text key in source#line format"));
                continue;
            }

            var groupEdits = group
                .OrderBy(edit => edit.RecordId, StringComparer.Ordinal)
                .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                .ToArray();
            PlannedWriteInfo writeInfo;
            string sourceFingerprint;
            try
            {
                writeInfo = SvWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    group.Key,
                    groupEdits.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                    outputMode);
                sourceFingerprint = CreatePlanFingerprint(
                    project,
                    group.Key,
                    groupEdits,
                    SvWorkflowFileSource.ResolveOutputPath(paths, group.Key, outputMode));
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or NotSupportedException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"S/V Text change plan could not inspect its source or resolve the output target: {exception.Message}",
                    file: $"romfs/{group.Key}",
                    expected: "Readable source and writable output root"));
                continue;
            }

            var reason = groupEdits.Length == 1
                ? $"Apply pending S/V Text edit: {groupEdits[0].Summary}"
                : $"Apply {groupEdits.Length} pending S/V Text edits.";

            writes.Add(new PlannedFileWrite(
                writeInfo.TargetRelativePath,
                writeInfo.Sources,
                writeInfo.ReplacesExistingOutput,
                reason,
                sourceFingerprint));
        }

        return writes;
    }

    private string CreatePlanFingerprint(
        OpenedProject project,
        string dataVirtualPath,
        IReadOnlyList<PendingEdit> edits,
        string outputTargetPath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintComponent(hash, "KM.SV.Text.ChangePlan.v1");
        AppendFingerprintComponent(hash, dataVirtualPath.Replace('\\', '/'));
        AppendFingerprintComponent(hash, fileSource.Read(project, dataVirtualPath).Bytes);

        var keyVirtualPath = Path.ChangeExtension(dataVirtualPath, ".tbl");
        byte[]? keyBytes;
        try
        {
            keyBytes = fileSource.Read(project, keyVirtualPath).Bytes;
        }
        catch (Exception exception) when (IsMissingFile(exception))
        {
            keyBytes = null;
        }
        AppendFingerprintComponent(hash, keyBytes is null ? "tbl-missing" : "tbl-present");
        if (keyBytes is not null)
        {
            AppendFingerprintComponent(hash, keyBytes);
        }

        foreach (var edit in edits)
        {
            AppendFingerprintComponent(hash, edit.Domain);
            AppendFingerprintComponent(hash, edit.RecordId);
            AppendFingerprintComponent(hash, edit.Field);
            AppendFingerprintComponent(hash, edit.NewValue);
            AppendFingerprintComponent(hash, edit.Owner);
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintComponent(hash, ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintComponent(hash, source.RelativePath);
            }
        }

        AppendFingerprintComponent(hash, Path.GetFullPath(outputTargetPath));
        AppendOutputTargetState(hash, outputTargetPath);

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private string CreateDescriptorPlanFingerprint(
        OpenedProject project,
        ProjectPaths paths,
        IReadOnlyList<PendingEdit> edits)
    {
        var removedHashes = new HashSet<ulong>();
        var outputRoot = Path.GetFullPath(paths.OutputRootPath!);
        var outputRomFsRoot = Path.Combine(outputRoot, "romfs");
        if (Directory.Exists(outputRomFsRoot))
        {
            foreach (var path in Directory.EnumerateFiles(outputRomFsRoot, "*", OutputEnumeration))
            {
                var relativePath = Path.GetRelativePath(outputRomFsRoot, path)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                if (!string.Equals(
                    relativePath,
                    SvWorkflowFileSource.DescriptorVirtualPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    removedHashes.Add(SvTrinityPathHasher.HashPath(relativePath));
                }
            }
        }

        foreach (var virtualPath in edits
                     .Select(GetVirtualPath)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            removedHashes.Add(SvTrinityPathHasher.HashPath(virtualPath!));
        }

        var descriptorBytes = SvTrinityDescriptorPatcher.RemoveFileHashes(
            fileSource.ReadBase(project, SvWorkflowFileSource.DescriptorVirtualPath).Bytes,
            removedHashes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintComponent(hash, "KM.SV.Text.DescriptorPlan.v1");
        AppendFingerprintComponent(hash, descriptorBytes);
        var descriptorTargetPath = SvWorkflowFileSource.ResolveOutputPath(
            paths,
            SvWorkflowFileSource.DescriptorVirtualPath,
            SvOutputMode.Standalone);
        AppendFingerprintComponent(hash, Path.GetFullPath(descriptorTargetPath));
        AppendOutputTargetState(hash, descriptorTargetPath);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendOutputTargetState(IncrementalHash hash, string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            AppendFingerprintComponent(hash, "target-missing");
            return;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            AppendFingerprintComponent(hash, "target-directory");
            AppendFingerprintComponent(hash, ((int)attributes).ToString(CultureInfo.InvariantCulture));
            return;
        }

        AppendFingerprintComponent(hash, "target-file");
        AppendFingerprintComponent(hash, ((int)attributes).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintComponent(
            hash,
            File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture));
        AppendFingerprintComponent(hash, File.ReadAllBytes(path));
    }

    private static void AppendFingerprintComponent(IncrementalHash hash, string? value)
    {
        AppendFingerprintComponent(
            hash,
            value is null ? ReadOnlySpan<byte>.Empty : Encoding.UTF8.GetBytes(value));
        hash.AppendData(value is null ? new byte[] { 0 } : new byte[] { 1 });
    }

    private static void AppendFingerprintComponent(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static bool IsMissingFile(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }
        }

        return false;
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

    private sealed record OutputSnapshot(
        string Path,
        bool Existed,
        byte[]? Contents,
        DateTime? LastWriteTimeUtc);
}
