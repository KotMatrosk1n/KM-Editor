// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Behavior;

public sealed class SwShBehaviorEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShBehaviorWorkflowService behaviorWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShBehaviorEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShBehaviorWorkflowService? behaviorWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.behaviorWorkflowService = behaviorWorkflowService ?? new SwShBehaviorWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShBehaviorEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShBehaviorWorkflowService? behaviorWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.behaviorWorkflowService = behaviorWorkflowService ?? new SwShBehaviorWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShBehaviorEditResult UpdateEntryField(
        ProjectPaths paths,
        EditSession? session,
        string entryId,
        string field,
        string value)
    {
        return UpdateEntryFields(
            paths,
            session,
            [new SwShBehaviorFieldUpdate(entryId, field, value)]);
    }

    public SwShBehaviorEditResult UpdateEntryFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShBehaviorFieldUpdate?>? updates)
    {
        ArgumentNullException.ThrowIfNull(paths);

        projectWorkspaceService.ClearMemoryCache();
        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = behaviorWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(loadedWorkflow, originalSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditBehavior(project, loadedWorkflow, diagnostics))
        {
            return new SwShBehaviorEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (updates is null || updates.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Update at least one Behavior field.",
                field: "updates",
                expected: "One or more Behavior field updates"));
            return new SwShBehaviorEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var workingSession = originalSession;
        var effectiveWorkflow = originalWorkflow;
        var seenUpdates = new HashSet<(int EntryIndex, string Field)>();
        foreach (var update in updates)
        {
            if (update is null
                || update.EntryId is null
                || update.Field is null
                || update.Value is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Behavior update fields are required.",
                    field: "updates",
                    expected: "Non-null Behavior field update"));
                break;
            }

            if (!string.Equals(update.Field, update.Field.Trim(), StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Behavior field must use canonical text without surrounding whitespace.",
                    field: "field",
                    expected: update.Field.Trim()));
                break;
            }

            var sourceEntry = ResolveSourceEntry(loadedWorkflow, update.EntryId, diagnostics);
            if (sourceEntry is null)
            {
                break;
            }

            var effectiveEntry = effectiveWorkflow.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.EntryId, sourceEntry.EntryId, StringComparison.Ordinal));
            if (effectiveEntry is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Behavior entry is not available in the effective workflow.",
                    field: "entryId",
                    expected: sourceEntry.EntryId));
                break;
            }

            if (!seenUpdates.Add((sourceEntry.Index, update.Field)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Behavior batch contains the same field more than once.",
                    field: update.Field,
                    expected: "One value per Behavior field"));
                break;
            }

            var normalizedValue = TryNormalizeFieldValue(
                sourceEntry,
                effectiveEntry,
                loadedWorkflow.Fields,
                update.Field,
                update.Value,
                diagnostics);
            if (normalizedValue is null)
            {
                break;
            }

            var sourceValue = GetEntryFieldValue(sourceEntry, update.Field);
            if (string.Equals(sourceValue, normalizedValue, StringComparison.Ordinal))
            {
                workingSession = RemovePendingBehaviorEdit(
                    workingSession,
                    sourceEntry.EntryId,
                    update.Field);
            }
            else
            {
                var pendingEdit = CreatePendingEdit(
                    project,
                    sourceEntry,
                    effectiveEntry,
                    loadedWorkflow.Fields,
                    update.Field,
                    normalizedValue);
                workingSession = ReplacePendingBehaviorEdit(workingSession, pendingEdit);
            }

            effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, workingSession.PendingEdits);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShBehaviorEditResult(originalWorkflow, originalSession, diagnostics);
        }

        ValidateLoadedSession(
            project,
            loadedWorkflow,
            workingSession,
            diagnostics,
            addSuccessDiagnostic: false);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShBehaviorEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShBehaviorEditResult(
            OverlayPendingEdits(loadedWorkflow, workingSession.PendingEdits),
            workingSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        if (!OwnsDirectSession(session))
        {
            return new SwShEditSessionValidation(
                session,
                IsValid: false,
                [CreateDirectDomainOwnershipDiagnostic(session)]);
        }

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = behaviorWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (CanEditBehavior(project, workflow, diagnostics))
        {
            ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);
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

        if (!OwnsDirectSession(session))
        {
            return new ChangePlan(
                session.Id,
                [],
                [CreateDirectDomainOwnershipDiagnostic(session)]);
        }

        projectWorkspaceService.ClearMemoryCache();
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        var behaviorEdits = GetBehaviorEdits(session).ToArray();

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShBehaviorWorkflowService.ResolveBehaviorDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior change plan could not resolve the source behavior file.",
                expected: SwShBehaviorWorkflowService.BehaviorDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var write = new PlannedFileWrite(
            dataSource.GraphEntry.RelativePath,
            behaviorEdits
                .SelectMany(edit => edit.Sources)
                .Append(new ProjectFileReference(GetSourceLayer(dataSource.GraphEntry), dataSource.GraphEntry.RelativePath))
                .Distinct()
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            File.Exists(targetPath),
            CreatePlanReason(behaviorEdits));

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Change plan preview contains 1 target file."));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, [write], diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        projectWorkspaceService.ClearMemoryCache();
        try
        {
            return ApplyChangePlanCore(paths, session, reviewedPlan);
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
        }
    }

    private ApplyResult ApplyChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        if (!OwnsDirectSession(session))
        {
            var ownershipDiagnostic = CreateDirectDomainOwnershipDiagnostic(session);
            var rejectedPlan = new ChangePlan(session.Id, [], [ownershipDiagnostic]);
            return CreateApplyResult(
                applyId,
                appliedAt,
                rejectedPlan,
                [],
                [ownershipDiagnostic]);
        }

        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Behavior change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShBehaviorWorkflowService.ResolveBehaviorDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior apply could not resolve the source behavior file.",
                expected: SwShBehaviorWorkflowService.BehaviorDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        byte[] output;
        try
        {
            output = CreateArchiveOutput(dataSource.AbsolutePath, session, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            VerifyOutput(output, session, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior source file could not be decoded or safely edited: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield symbol encounter behavior data"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior source file could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield behavior data"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (!SwShOutputRollbackScope.TryCapture(
                paths,
                currentPlan.Writes.Select(write => write.TargetRelativePath),
                out var rollbackScope,
                out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        using (var outputRollback = rollbackScope!)
        {
            try
            {
                WriteOutputAtomically(targetPath, output);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, dataSource.GraphEntry.RelativePath));
                outputRollback.Commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Behavior output file could not be written: {exception.Message}",
                    file: dataSource.GraphEntry.RelativePath,
                    expected: "Writable output root"));
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
            }
        }

        if (writtenFiles.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Behavior change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShBehaviorWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        bool addSuccessDiagnostic)
    {
        var behaviorEdits = GetBehaviorEdits(session).ToArray();
        var effectiveWorkflow = workflow;
        var seen = new HashSet<(string RecordId, string Field)>();
        var semanticRecordIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var edit in behaviorEdits)
        {
            if (!seen.Add((edit.RecordId ?? string.Empty, edit.Field ?? string.Empty)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "A Behavior field has more than one pending value.",
                    field: edit.Field,
                    expected: "One pending value per Behavior field"));
                continue;
            }

            var errorsBefore = CountErrors(diagnostics);
            var sourceEntry = ValidatePendingEdit(project, workflow, effectiveWorkflow, edit, diagnostics);
            if (sourceEntry is null || CountErrors(diagnostics) != errorsBefore)
            {
                continue;
            }

            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            if (edit.Field is SwShSymbolBehaviorArchive.SpeciesIdField
                or SwShSymbolBehaviorArchive.FormField)
            {
                semanticRecordIds.Add(sourceEntry.EntryId);
            }
        }

        ValidateSemanticRecords(effectiveWorkflow, semanticRecordIds, diagnostics);

        if (behaviorEdits.Length > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            PreflightArchiveWrite(project, session, diagnostics);
        }

        if (behaviorEdits.Length > 0
            && addSuccessDiagnostic
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Behavior change is valid."));
        }
    }

    private static SwShBehaviorEntryRecord? ValidatePendingEdit(
        OpenedProject project,
        SwShBehaviorWorkflow sourceWorkflow,
        SwShBehaviorWorkflow effectiveWorkflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(
                edit.Domain,
                SwShBehaviorWorkflowService.BehaviorEditDomain,
                StringComparison.Ordinal)
            || edit.RecordId is null
            || edit.Field is null
            || edit.NewValue is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior edit is missing canonical domain, record, field, or value metadata.",
                expected: "Complete canonical Behavior pending edit"));
            return null;
        }

        if (!SwShBehaviorWorkflowService.TryParseEntryId(
                edit.RecordId,
                out var entryIndex,
                out _,
                out var isLegacy)
            || isLegacy)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior edit does not use a current source-signed entry identity.",
                field: "entryId",
                expected: "Current signed Behavior entry identity"));
            return null;
        }

        var sourceEntry = sourceWorkflow.Entries.FirstOrDefault(candidate => candidate.Index == entryIndex);
        if (sourceEntry is null
            || !string.Equals(sourceEntry.EntryId, edit.RecordId, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior entry is no longer present or no longer matches its source contents.",
                field: "entryId",
                expected: "Current signed Behavior entry"));
            return null;
        }

        var effectiveEntry = effectiveWorkflow.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.EntryId, sourceEntry.EntryId, StringComparison.Ordinal));
        if (effectiveEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior entry is not available in the effective workflow.",
                field: "entryId",
                expected: sourceEntry.EntryId));
            return null;
        }

        var normalizedValue = TryNormalizeFieldValue(
            sourceEntry,
            effectiveEntry,
            sourceWorkflow.Fields,
            edit.Field,
            edit.NewValue,
            diagnostics);
        if (normalizedValue is null)
        {
            return null;
        }

        if (!string.Equals(normalizedValue, edit.NewValue, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior value is not in canonical storage form.",
                field: edit.Field,
                expected: normalizedValue));
        }

        if (string.Equals(
                GetEntryFieldValue(sourceEntry, edit.Field),
                normalizedValue,
                StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior edit is source equivalent and must be staged again.",
                field: edit.Field,
                expected: "Changed Behavior value"));
        }

        var expectedSources = CreateExpectedSources(project, sourceEntry, edit.Field);
        if (!HaveSameSources(edit.Sources, expectedSources))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior edit does not contain its complete current source set.",
                field: edit.Field,
                expected: "Current Behavior and semantic sources"));
        }

        return sourceEntry;
    }

    private static void ValidateSemanticRecords(
        SwShBehaviorWorkflow workflow,
        IReadOnlySet<string> recordIds,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (recordIds.Count == 0)
        {
            return;
        }

        if (workflow.PersonalRecords.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior species and form validation requires the Sword/Shield personal data table.",
                field: SwShSymbolBehaviorArchive.SpeciesIdField,
                expected: SwShPersonalTable.PersonalDataRelativePath));
            return;
        }

        foreach (var recordId in recordIds)
        {
            var entry = workflow.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.EntryId, recordId, StringComparison.Ordinal));
            if (entry is null)
            {
                continue;
            }

            if (entry.SpeciesId <= 0 || (uint)entry.SpeciesId >= (uint)workflow.PersonalRecords.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Behavior entry {entry.Index} does not target a species in the loaded Sword/Shield personal data.",
                    field: SwShSymbolBehaviorArchive.SpeciesIdField,
                    expected: "Species present in Sword/Shield personal data"));
                continue;
            }

            var basePersonal = workflow.PersonalRecords[entry.SpeciesId];
            var formCount = Math.Max(1, basePersonal.FormCount);
            if (entry.Form < 0 || entry.Form >= formCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Behavior entry {entry.Index} uses form {entry.Form}, but species {entry.SpeciesId} exposes {formCount} supported form slot(s).",
                    field: SwShSymbolBehaviorArchive.FormField,
                    expected: $"Form 0 through {formCount - 1}"));
                continue;
            }

            var personal = basePersonal;
            if (entry.Form > 0 && basePersonal.FormStatsIndex > 0)
            {
                var formPersonalId = basePersonal.FormStatsIndex + entry.Form - 1;
                if ((uint)formPersonalId >= (uint)workflow.PersonalRecords.Count)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Behavior entry {entry.Index} points beyond the loaded personal form table.",
                        field: SwShSymbolBehaviorArchive.FormField,
                        expected: "Personal form record inside the loaded table"));
                    continue;
                }

                personal = workflow.PersonalRecords[formPersonalId];
            }

            if (!personal.IsPresentInGame)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Behavior entry {entry.Index} uses a species and form not marked present in Sword/Shield personal data.",
                    field: SwShSymbolBehaviorArchive.SpeciesIdField,
                    expected: "Species and form present in Sword/Shield"));
            }
        }
    }

    private static string? TryNormalizeFieldValue(
        SwShBehaviorEntryRecord sourceEntry,
        SwShBehaviorEntryRecord effectiveEntry,
        IReadOnlyList<SwShBehaviorField> fields,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var metadata = fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
        if (metadata is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field}' is not supported.",
                field: "field",
                expected: "Supported Behavior field"));
            return null;
        }

        if (metadata.IsReadOnly || !SwShBehaviorWorkflowService.IsEditableField(field))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{metadata.Label}' is disabled until its role is confirmed.",
                field: "field",
                expected: "Editable Behavior field"));
            return null;
        }

        var sourceValue = GetEntryFieldValue(sourceEntry, field);
        if (metadata.ValueKind == "string")
        {
            return NormalizeStringValue(metadata, value, sourceValue, diagnostics);
        }

        if (metadata.ValueKind == "number")
        {
            return NormalizeSingleValue(metadata, value, sourceValue, diagnostics);
        }

        if (metadata.ValueKind == "integer")
        {
            return NormalizeInt32Value(metadata, value, sourceValue, diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Behavior field '{metadata.Label}' has unsupported editable value kind '{metadata.ValueKind}'.",
            field: field,
            expected: "String, number, or integer Behavior field"));
        return null;
    }

    private static string? NormalizeStringValue(
        SwShBehaviorField field,
        string value,
        string sourceValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.Equals(value, sourceValue, StringComparison.Ordinal))
        {
            return value;
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || string.IsNullOrEmpty(value)
            || value.Length > SwShBehaviorWorkflowService.MaximumStringLength
            || value.Any(char.IsControl))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must use 1 through {SwShBehaviorWorkflowService.MaximumStringLength} visible characters without surrounding whitespace.",
                field: field.Field,
                expected: "Canonical Behavior text value"));
            return null;
        }

        if (field.Options.Count > 0
            && !field.Options.Any(option => string.Equals(option.Value, value, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must use a profile or model anchor present in the loaded source.",
                field: field.Field,
                expected: "Loaded Behavior option"));
            return null;
        }

        return value;
    }

    private static string? NormalizeSingleValue(
        SwShBehaviorField field,
        string value,
        string sourceValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || float.IsNaN(parsed)
            || float.IsInfinity(parsed))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must be a finite 32-bit number.",
                field: field.Field,
                expected: "Finite float32 value"));
            return null;
        }

        var normalized = parsed.ToString("R", CultureInfo.InvariantCulture);
        if (!string.Equals(normalized, sourceValue, StringComparison.Ordinal)
            && (parsed < field.MinimumValue || parsed > field.MaximumValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must be between {field.MinimumValue.ToString(CultureInfo.InvariantCulture)} and {field.MaximumValue.ToString(CultureInfo.InvariantCulture)}.",
                field: field.Field,
                expected: "Supported Behavior float32 value"));
            return null;
        }

        return normalized;
    }

    private static string? NormalizeInt32Value(
        SwShBehaviorField field,
        string value,
        string sourceValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must be a signed 32-bit integer.",
                field: field.Field,
                expected: "Int32 value"));
            return null;
        }

        var normalized = parsed.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(normalized, sourceValue, StringComparison.Ordinal)
            && (parsed < field.MinimumValue || parsed > field.MaximumValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must be between {field.MinimumValue.ToString(CultureInfo.InvariantCulture)} and {field.MaximumValue.ToString(CultureInfo.InvariantCulture)}.",
                field: field.Field,
                expected: "Supported Behavior integer value"));
            return null;
        }

        return normalized;
    }

    private static SwShBehaviorEntryRecord? ResolveSourceEntry(
        SwShBehaviorWorkflow workflow,
        string entryId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShBehaviorWorkflowService.TryParseEntryId(
                entryId,
                out var entryIndex,
                out _,
                out var isLegacy))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior entry '{entryId}' is not a valid entry identity.",
                field: "entryId",
                expected: "Current signed Behavior entry or legacy physical index"));
            return null;
        }

        var entry = workflow.Entries.FirstOrDefault(candidate => candidate.Index == entryIndex);
        if (entry is null
            || (!isLegacy && !string.Equals(entry.EntryId, entryId, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior entry '{entryId}' is not present or no longer matches the loaded source.",
                field: "entryId",
                expected: "Current Behavior entry"));
            return null;
        }

        return entry;
    }

    private static PendingEdit CreatePendingEdit(
        OpenedProject project,
        SwShBehaviorEntryRecord sourceEntry,
        SwShBehaviorEntryRecord effectiveEntry,
        IReadOnlyList<SwShBehaviorField> fields,
        string field,
        string normalizedValue)
    {
        var metadata = fields.Single(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal));
        var displayValue = metadata.Options
            .FirstOrDefault(option => string.Equals(option.Value, normalizedValue, StringComparison.Ordinal))?
            .Label ?? normalizedValue;
        return new PendingEdit(
            SwShBehaviorWorkflowService.BehaviorEditDomain,
            $"Set {effectiveEntry.Label} {metadata.Label} to {displayValue}.",
            CreateExpectedSources(project, sourceEntry, field),
            RecordId: sourceEntry.EntryId,
            Field: field,
            NewValue: normalizedValue);
    }

    private static IReadOnlyList<ProjectFileReference> CreateExpectedSources(
        OpenedProject project,
        SwShBehaviorEntryRecord entry,
        string field)
    {
        var sources = new List<ProjectFileReference>
        {
            new(entry.Provenance.SourceLayer, entry.Provenance.SourceFile),
        };

        if (field is SwShSymbolBehaviorArchive.SpeciesIdField
            or SwShSymbolBehaviorArchive.FormField)
        {
            var personalSource = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
            if (personalSource is not null)
            {
                sources.Add(new ProjectFileReference(
                    GetSourceLayer(personalSource.GraphEntry),
                    personalSource.GraphEntry.RelativePath));
            }
        }

        return sources.Distinct().ToArray();
    }

    private static bool HaveSameSources(
        IReadOnlyList<ProjectFileReference> actual,
        IReadOnlyList<ProjectFileReference> expected)
    {
        return actual.Count == expected.Count
            && actual.OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .SequenceEqual(
                    expected.OrderBy(source => source.Layer)
                        .ThenBy(source => source.RelativePath, StringComparer.Ordinal));
    }

    private static SwShBehaviorWorkflow OverlayPendingEdits(
        SwShBehaviorWorkflow workflow,
        IReadOnlyList<PendingEdit> pendingEdits)
    {
        var effective = workflow;
        foreach (var edit in pendingEdits.Where(edit => string.Equals(
                     edit.Domain,
                     SwShBehaviorWorkflowService.BehaviorEditDomain,
                     StringComparison.Ordinal)))
        {
            if (edit.RecordId is null || edit.Field is null || edit.NewValue is null)
            {
                continue;
            }

            effective = OverlayPendingEdit(effective, edit);
        }

        return effective;
    }

    private static SwShBehaviorWorkflow OverlayPendingEdit(
        SwShBehaviorWorkflow workflow,
        PendingEdit edit)
    {
        var entries = workflow.Entries.ToArray();
        var index = Array.FindIndex(entries, entry => string.Equals(
            entry.EntryId,
            edit.RecordId,
            StringComparison.Ordinal));
        if (index < 0 || edit.Field is null || edit.NewValue is null)
        {
            return workflow;
        }

        entries[index] = OverlayPendingEdit(entries[index], edit, workflow);
        return workflow with { Entries = entries };
    }

    private static SwShBehaviorEntryRecord OverlayPendingEdit(
        SwShBehaviorEntryRecord entry,
        PendingEdit edit,
        SwShBehaviorWorkflow workflow)
    {
        var field = edit.Field!;
        var value = edit.NewValue!;
        var fieldValues = entry.Fields
            .Select(current => string.Equals(current.Field, field, StringComparison.Ordinal)
                ? current with { Value = value }
                : current)
            .ToArray();
        var parsedSpeciesId = entry.SpeciesId;
        var hasSpeciesId = field == SwShSymbolBehaviorArchive.SpeciesIdField
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSpeciesId);
        var speciesId = hasSpeciesId ? parsedSpeciesId : entry.SpeciesId;
        var speciesName = hasSpeciesId
            ? ResolveOptionLabel(
                workflow.Fields,
                SwShSymbolBehaviorArchive.SpeciesIdField,
                value,
                $"Species {speciesId.ToString(CultureInfo.InvariantCulture)}",
                stripLeadingId: true)
            : entry.SpeciesName;
        var parsedForm = entry.Form;
        var hasForm = field == SwShSymbolBehaviorArchive.FormField
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedForm);
        var form = hasForm ? parsedForm : entry.Form;
        var behavior = field == SwShSymbolBehaviorArchive.BehaviorField ? value : entry.Behavior;
        var behaviorLabel = SwShBehaviorWorkflowService.GetBehaviorLabel(behavior);
        var modelPart = field == SwShSymbolBehaviorArchive.ModelPartField ? value : entry.ModelPart;
        var parsedHitboxRadius = (float)entry.HitboxRadius;
        var hasHitboxRadius = field == SwShSymbolBehaviorArchive.HitboxRadiusField
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedHitboxRadius)
            && float.IsFinite(parsedHitboxRadius);
        var hitboxRadius = hasHitboxRadius ? parsedHitboxRadius : entry.HitboxRadius;
        var parsedGrassShakeRadius = (float)entry.GrassShakeRadius;
        var hasGrassShakeRadius = field == SwShSymbolBehaviorArchive.GrassShakeRadiusField
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedGrassShakeRadius)
            && float.IsFinite(parsedGrassShakeRadius);
        var grassShakeRadius = hasGrassShakeRadius ? parsedGrassShakeRadius : entry.GrassShakeRadius;
        var formSuffix = form == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"-{form}");

        return entry with
        {
            Label = string.Create(CultureInfo.InvariantCulture, $"{entry.Index:000} {speciesName}{formSuffix} | {behaviorLabel}"),
            SpeciesId = speciesId,
            SpeciesName = speciesName,
            Form = form,
            Behavior = behavior,
            BehaviorLabel = behaviorLabel,
            ModelPart = modelPart,
            HitboxRadius = hitboxRadius,
            GrassShakeRadius = grassShakeRadius,
            Fields = fieldValues,
            FormOptions = SwShBehaviorWorkflowService.CreateFormOptions(
                workflow.PersonalRecords,
                speciesId,
                form),
        };
    }

    private static string ResolveOptionLabel(
        IReadOnlyList<SwShBehaviorField> fields,
        string field,
        string value,
        string fallback,
        bool stripLeadingId)
    {
        var label = fields
            .FirstOrDefault(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal))?
            .Options
            .FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.Ordinal))?
            .Label;
        if (string.IsNullOrWhiteSpace(label))
        {
            return fallback;
        }

        if (!stripLeadingId)
        {
            return label;
        }

        var firstSpace = label.IndexOf(' ');
        return firstSpace >= 0 && firstSpace + 1 < label.Length
            ? label[(firstSpace + 1)..]
            : label;
    }

    private static string GetEntryFieldValue(SwShBehaviorEntryRecord entry, string field)
    {
        return entry.Fields.FirstOrDefault(value => string.Equals(
                value.Field,
                field,
                StringComparison.Ordinal))?
            .Value ?? string.Empty;
    }

    private static EditSession ReplacePendingBehaviorEdit(EditSession session, PendingEdit pendingEdit)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !string.Equals(
                        edit.Domain,
                        SwShBehaviorWorkflowService.BehaviorEditDomain,
                        StringComparison.Ordinal)
                    || !string.Equals(edit.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
                    || !string.Equals(edit.Field, pendingEdit.Field, StringComparison.Ordinal))
                .Append(pendingEdit)
                .ToArray(),
        };
    }

    private static EditSession RemovePendingBehaviorEdit(
        EditSession session,
        string recordId,
        string field)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !string.Equals(
                        edit.Domain,
                        SwShBehaviorWorkflowService.BehaviorEditDomain,
                        StringComparison.Ordinal)
                    || !string.Equals(edit.RecordId, recordId, StringComparison.Ordinal)
                    || !string.Equals(edit.Field, field, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static SwShSymbolBehaviorEdit? ToArchiveEdit(
        SwShSymbolBehaviorArchive archive,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (edit.RecordId is null
            || edit.Field is null
            || edit.NewValue is null
            || !SwShBehaviorWorkflowService.TryParseEntryId(
                edit.RecordId,
                out var entryIndex,
                out _,
                out var isLegacy)
            || isLegacy
            || (uint)entryIndex >= (uint)archive.Entries.Count
            || !string.Equals(
                SwShBehaviorWorkflowService.CreateEntryId(archive.Entries[entryIndex]),
                edit.RecordId,
                StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior edit no longer targets its signed source entry.",
                field: edit.Field,
                expected: "Current signed Behavior entry"));
            return null;
        }

        return new SwShSymbolBehaviorEdit(entryIndex, edit.Field, edit.NewValue);
    }

    private static byte[] CreateArchiveOutput(
        string sourcePath,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var archive = SwShSymbolBehaviorArchive.Parse(File.ReadAllBytes(sourcePath));
        var edits = GetBehaviorEdits(session)
            .Select(edit => ToArchiveEdit(archive, edit, diagnostics))
            .Where(edit => edit is not null)
            .Select(edit => edit!)
            .ToArray();
        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? []
            : archive.WriteEdits(edits);
    }

    private static void PreflightArchiveWrite(
        OpenedProject project,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShBehaviorWorkflowService.ResolveBehaviorDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior edit preflight could not resolve the source archive.",
                expected: SwShBehaviorWorkflowService.BehaviorDataPath));
            return;
        }

        try
        {
            var output = CreateArchiveOutput(source.AbsolutePath, session, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            VerifyOutput(output, session, diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior edit cannot be encoded safely: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Compatible Sword/Shield behavior archive"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior edit preflight could not read the source archive: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield behavior archive"));
        }
    }

    private static void VerifyOutput(
        byte[] output,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var archive = SwShSymbolBehaviorArchive.Parse(output);
        foreach (var edit in GetBehaviorEdits(session))
        {
            if (edit.RecordId is null
                || edit.Field is null
                || edit.NewValue is null
                || !SwShBehaviorWorkflowService.TryParseEntryId(
                    edit.RecordId,
                    out var entryIndex,
                    out _,
                    out var isLegacy)
                || isLegacy
                || (uint)entryIndex >= (uint)archive.Entries.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Generated Behavior output no longer contains the staged target.",
                    field: edit.Field,
                    expected: "Verified Behavior output"));
                continue;
            }

            var actualValue = archive.Entries[entryIndex].GetStringValue(edit.Field);
            if (!string.Equals(actualValue, edit.NewValue, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Generated Behavior output did not retain the staged value.",
                    field: edit.Field,
                    expected: edit.NewValue));
            }
        }
    }

    private void WriteOutputAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Behavior output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Behavior output target directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(tempPath, contents);
            if (!File.Exists(tempPath)
                || !File.ReadAllBytes(tempPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Behavior temporary output verification failed.");
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
                // The original output remains available through the rollback scope.
            }
        }
    }

    private static void RollbackFailedApply(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var failure in rollbackScope.Rollback())
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior output rollback failed: {failure.Message}",
                file: failure.RelativePath,
                expected: "Output restored to its pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(paths, out var stablePaths, out var failure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior output root could not be resolved safely: {failure}",
                file: targetRelativePath,
                expected: "Stable physical output root"));
            return null;
        }

        var targetPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            stablePaths.OutputRootPath,
            targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior output path could not be resolved inside the output root.",
                file: targetRelativePath,
                expected: "Physically contained output target"));
        }

        return targetPath;
    }

    private static bool CanEditBehavior(
        OpenedProject project,
        SwShBehaviorWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior edits require valid base RomFS, base ExeFS, and output root paths.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior workflow is not available for editing.",
                expected: "Available Behavior workflow"));
            return false;
        }

        if (workflow.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Resolve Behavior workflow errors before editing.",
                expected: "Behavior workflow without errors"));
            return false;
        }

        return true;
    }

    private static IEnumerable<PendingEdit> GetBehaviorEdits(EditSession session)
    {
        return session.PendingEdits.Where(edit => string.Equals(
            edit.Domain,
            SwShBehaviorWorkflowService.BehaviorEditDomain,
            StringComparison.Ordinal));
    }

    private static bool OwnsDirectSession(EditSession session)
    {
        return session.PendingEdits.Count > 0
            && session.PendingEdits.All(edit => string.Equals(
                edit.Domain,
                SwShBehaviorWorkflowService.BehaviorEditDomain,
                StringComparison.Ordinal));
    }

    private static ValidationDiagnostic CreateDirectDomainOwnershipDiagnostic(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = domains.Length == 0 ? "no pending edit domain" : string.Join(", ", domains);
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Behavior direct validation, planning, and apply require only Behavior edits; found {actual}.",
            expected: SwShBehaviorWorkflowService.BehaviorEditDomain);
    }

    private static int CountErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static string CreatePlanReason(IReadOnlyList<PendingEdit> edits)
    {
        var canonical = new StringBuilder();
        foreach (var edit in edits
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                     .ThenBy(edit => edit.NewValue, StringComparer.Ordinal))
        {
            AppendFingerprintComponent(canonical, edit.Domain);
            AppendFingerprintComponent(canonical, edit.RecordId);
            AppendFingerprintComponent(canonical, edit.Field);
            AppendFingerprintComponent(canonical, edit.NewValue);
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintComponent(
                    canonical,
                    ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintComponent(canonical, source.RelativePath);
            }
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        var summary = edits.Count == 1
            ? "Apply 1 pending Behavior edit."
            : $"Apply {edits.Count} pending Behavior edits.";
        return $"{summary} Fingerprint {fingerprint}.";
    }

    private static void AppendFingerprintComponent(StringBuilder destination, string? value)
    {
        destination.Append(value?.Length ?? -1);
        destination.Append(':');
        destination.Append(value);
        destination.Append('|');
    }

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base;
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
            Field: field,
            Domain: SwShBehaviorWorkflowService.BehaviorEditDomain,
            Expected: expected);
    }
}
