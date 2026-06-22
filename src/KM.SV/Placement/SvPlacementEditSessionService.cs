// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV.Placement;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Placement;

internal sealed class SvPlacementEditSessionService
{
    private const string WorkflowName = "Placement";
    private const int AlcremieSpeciesId = (int)global::pml.common.DevID.DEV_MAHOIPPU;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvPlacementWorkflowService placementWorkflowService;

    public SvPlacementEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvPlacementWorkflowService? placementWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.placementWorkflowService = placementWorkflowService ?? new SvPlacementWorkflowService(this.fileSource);
    }

    public SvPlacementEditResult UpdateObjectField(
        ProjectPaths paths,
        EditSession? session,
        string objectId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = placementWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.PlacementDomain,
                diagnostics))
        {
            return new SvPlacementEditResult(workflow, currentSession, diagnostics);
        }

        var placedObject = workflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == objectId);
        if (placedObject is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement edit targets an object that is not loaded.",
                field: "objectId",
                expected: "Existing placement object"));
            return new SvPlacementEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, placedObject, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvPlacementEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingPlacementEdit(currentSession, pendingEdit);
        return new SvPlacementEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvPlacementEditResult UpdateObjectFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvPlacementObjectFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = placementWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.PlacementDomain,
                diagnostics))
        {
            return new SvPlacementEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.ObjectId)
                || string.IsNullOrWhiteSpace(update.Field)
                || update.Value is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Placement batch update is missing an object, field, or value.",
                    field: "updates",
                    expected: "Complete Placement object field update"));
                continue;
            }

            var placedObject = effectiveWorkflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == update.ObjectId);
            if (placedObject is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Placement edit targets an object that is not loaded.",
                    field: "objectId",
                    expected: "Existing placement object"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(effectiveWorkflow, placedObject, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ReplacePendingPlacementEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits);
        }

        return new SvPlacementEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = placementWorkflowService.Load(project);
        var projectedWorkflow = OverlayPendingEdits(workflow, session.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.PlacementDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(projectedWorkflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Info, "Pending Placement change is valid."));
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
        return CreateChangePlan(paths, session, outputMode, validateSession: true);
    }

    private ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode,
        bool validateSession)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var diagnostics = validateSession
            ? Validate(paths, session).Diagnostics.ToList()
            : new List<ValidationDiagnostic>();
        if (!validateSession)
        {
            ValidatePendingEditEnvelope(session.PendingEdits, diagnostics);
        }
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Placement edit before reviewing a change plan.",
                expected: "Pending Placement edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var writes = session.PendingEdits
                .SelectMany(edit => edit.Sources)
                .Select(source => source.RelativePath)
                .Where(path => !string.IsNullOrWhiteSpace(path)
                    && !string.Equals(path, $"romfs/{SvWorkflowFileSource.DescriptorVirtualPath}", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var writeInfo = SvWorkflowFileSource.CreatePlannedWrite(
                        paths,
                        path,
                        session.PendingEdits.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                        outputMode);
                    return new PlannedFileWrite(
                        writeInfo.TargetRelativePath,
                        writeInfo.Sources,
                        writeInfo.ReplacesExistingOutput,
                        $"Apply pending Placement edits for {writeInfo.TargetRelativePath}.");
                })
                .ToList();

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
                $"Change plan preview contains {writes.Count} target files."));

            return new ChangePlan(session.Id, writes, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement change plan could not resolve output targets: {exception.Message}",
                expected: "Writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
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
        var currentPlan = CreateChangePlan(paths, session, outputMode, validateSession: false);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Placement change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            foreach (var group in session.PendingEdits.GroupBy(GetEditSourcePath, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Placement edit is missing a source path.",
                        expected: "Placement record id with source path"));
                    continue;
                }

                ApplySourceGroup(project, paths, group.Key, group.ToArray(), diagnostics, writtenFiles, outputMode);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                outputMode == SvOutputMode.Standalone
                    ? "Applied Placement change plan as standalone Scarlet/Violet output and patched the Trinity descriptor."
                    : "Applied Placement change plan for Trinity Mod Manager. Run this output folder through Trinity Mod Manager before installing."));
        }
        catch (Exception exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement output could not be written: {exception.Message}",
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private void ApplySourceGroup(
        OpenedProject project,
        ProjectPaths paths,
        string sourcePath,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles,
        SvOutputMode outputMode)
    {
        var firstCategory = GetEditCategory(edits[0]);
        switch (firstCategory)
        {
            case SvPlacementWorkflowService.FixedSymbolsCategory:
                ApplyFixedSymbols(project, paths, sourcePath, edits, diagnostics, writtenFiles, outputMode);
                break;
            case SvPlacementWorkflowService.CoinSymbolsCategory:
                ApplyCoinSymbols(project, paths, sourcePath, edits, diagnostics, writtenFiles, outputMode);
                break;
            case SvPlacementWorkflowService.HiddenItemsCategory:
                ApplyHiddenItems(project, paths, sourcePath, edits, diagnostics, writtenFiles, outputMode);
                break;
            case SvPlacementWorkflowService.RummagingPointsCategory:
                ApplyRummaging(project, paths, sourcePath, edits, diagnostics, writtenFiles, outputMode);
                break;
            default:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Placement category '{firstCategory}' is read-only.",
                    expected: "Editable Placement category"));
                break;
        }
    }

    private void ApplyFixedSymbols(
        OpenedProject project,
        ProjectPaths paths,
        string sourcePath,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles,
        SvOutputMode outputMode)
    {
        var source = fileSource.Read(project, sourcePath);
        var moveResolver = edits.Any(IsFixedMoveField)
            ? SvDefaultMoveResolver.Load(project, fileSource, diagnostics)
            : SvDefaultMoveResolver.Empty;
        var rows = ReadFixedRows(source.Bytes);
        foreach (var edit in edits)
        {
            if (!SvPlacementWorkflowService.TryParseRecordId(edit.RecordId, out _, out _, out var index)
                || index < 0
                || index >= rows.Count)
            {
                diagnostics.Add(CreateInvalidApplyDiagnostic(edit));
                continue;
            }

            ApplyFixedEdit(rows[index], edit, moveResolver, diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        SvWorkflowFileSource.Write(paths, sourcePath, WriteFixedRows(rows), outputMode);
        writtenFiles.Add(SvEditSessionSupport.GeneratedReference(sourcePath, outputMode));
    }

    private void ApplyCoinSymbols(
        OpenedProject project,
        ProjectPaths paths,
        string sourcePath,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles,
        SvOutputMode outputMode)
    {
        var source = fileSource.Read(project, sourcePath);
        var moveResolver = edits.Any(IsCoinMoveField)
            ? SvDefaultMoveResolver.Load(project, fileSource, diagnostics)
            : SvDefaultMoveResolver.Empty;
        var rows = ReadCoinRows(source.Bytes);
        foreach (var edit in edits)
        {
            if (!SvPlacementWorkflowService.TryParseRecordId(edit.RecordId, out _, out _, out var index)
                || index < 0
                || index >= rows.Count)
            {
                diagnostics.Add(CreateInvalidApplyDiagnostic(edit));
                continue;
            }

            ApplyCoinEdit(rows[index], edit, moveResolver, diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        SvWorkflowFileSource.Write(paths, sourcePath, WriteCoinRows(rows), outputMode);
        writtenFiles.Add(SvEditSessionSupport.GeneratedReference(sourcePath, outputMode));
    }

    private void ApplyHiddenItems(
        OpenedProject project,
        ProjectPaths paths,
        string sourcePath,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles,
        SvOutputMode outputMode)
    {
        var source = fileSource.Read(project, sourcePath);
        var rows = ReadHiddenRows(source.Bytes);
        foreach (var edit in edits)
        {
            if (!SvPlacementWorkflowService.TryParseRecordId(edit.RecordId, out _, out _, out var index)
                || index < 0
                || index >= rows.Count)
            {
                diagnostics.Add(CreateInvalidApplyDiagnostic(edit));
                continue;
            }

            ApplyHiddenEdit(rows[index], edit, diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        SvWorkflowFileSource.Write(paths, sourcePath, WriteHiddenRows(rows), outputMode);
        writtenFiles.Add(SvEditSessionSupport.GeneratedReference(sourcePath, outputMode));
    }

    private void ApplyRummaging(
        OpenedProject project,
        ProjectPaths paths,
        string sourcePath,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles,
        SvOutputMode outputMode)
    {
        var source = fileSource.Read(project, sourcePath);
        var rows = ReadRummagingRows(source.Bytes);
        foreach (var edit in edits)
        {
            if (!SvPlacementWorkflowService.TryParseRecordId(edit.RecordId, out _, out _, out var index)
                || index < 0
                || index >= rows.Count)
            {
                diagnostics.Add(CreateInvalidApplyDiagnostic(edit));
                continue;
            }

            ApplyRummagingEdit(rows[index], edit, diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        SvWorkflowFileSource.Write(paths, sourcePath, WriteRummagingRows(rows), outputMode);
        writtenFiles.Add(SvEditSessionSupport.GeneratedReference(sourcePath, outputMode));
    }

    private static PendingEdit? CreatePendingEdit(
        SvPlacementWorkflow workflow,
        SvPlacedObjectRecord placedObject,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, normalizedField, StringComparison.Ordinal));
        var fieldValue = placedObject.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, normalizedField, StringComparison.Ordinal));
        if (editableField is null || fieldValue is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (editableField.IsReadOnly || fieldValue.IsReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement field '{editableField.Label}' is read-only for Scarlet/Violet.",
                field: normalizedField,
                expected: "Editable Placement field"));
            return null;
        }

        if (!ValidateValue(editableField, value, diagnostics))
        {
            return null;
        }

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.PlacementDomain,
            $"Set {placedObject.Label} {editableField.Label.ToLowerInvariant()} to {value}.",
            new ProjectFileReference(placedObject.Provenance.SourceLayer, placedObject.Provenance.SourceFile),
            placedObject.ObjectId,
            normalizedField,
            NormalizeValue(editableField, value));
    }

    private static void ValidatePendingEdit(
        SvPlacementWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.PlacementDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Placement.",
                expected: SvEditSessionSupport.PlacementDomain));
            return;
        }

        var placedObject = workflow.Objects.FirstOrDefault(candidate =>
            string.Equals(candidate.ObjectId, edit.RecordId, StringComparison.Ordinal));
        if (placedObject is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Placement edit targets an object that is not loaded.",
                field: "objectId",
                expected: "Existing placement object"));
            return;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        var fieldValue = placedObject.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (editableField is null || fieldValue is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (editableField.IsReadOnly || fieldValue.IsReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Placement edit targets read-only field '{editableField.Label}'.",
                field: edit.Field,
                expected: "Editable Placement field"));
            return;
        }

        _ = ValidateValue(editableField, edit.NewValue ?? string.Empty, diagnostics);
    }

    private static void ValidatePendingEditEnvelope(
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var edit in edits)
        {
            if (!string.Equals(edit.Domain, SvEditSessionSupport.PlacementDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Placement.",
                    expected: SvEditSessionSupport.PlacementDomain));
                continue;
            }

            if (string.IsNullOrWhiteSpace(edit.RecordId)
                || string.IsNullOrWhiteSpace(edit.Field)
                || edit.NewValue is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Placement edit is missing record, field, or value metadata.",
                    expected: "Complete Placement pending edit"));
            }
        }
    }

    private static bool ValidateValue(
        SvPlacementEditableField field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field.ValueKind == "integer")
        {
            return SvEditSessionSupport.TryParseInt(
                value,
                (int)Math.Ceiling(field.MinimumValue),
                (int)Math.Floor(field.MaximumValue),
                field.Field,
                SvEditSessionSupport.PlacementDomain,
                diagnostics) is not null;
        }

        if (field.ValueKind == "number")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || parsed < field.MinimumValue
                || parsed > field.MaximumValue)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Placement value must be a number between {field.MinimumValue} and {field.MaximumValue}.",
                    field: field.Field,
                    expected: "Safe numeric Placement value"));
                return false;
            }

            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Placement field '{field.Label}' is not editable.",
            field: field.Field,
            expected: "Numeric editable Placement field"));
        return false;
    }

    private static string NormalizeValue(SvPlacementEditableField field, string value)
    {
        if (field.ValueKind == "integer"
            && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        if (field.ValueKind == "number"
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static SvPlacementWorkflow OverlayPendingEdits(SvPlacementWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SvPlacementWorkflow OverlayPendingEdit(SvPlacementWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.PlacementDomain, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(edit.RecordId)
            || string.IsNullOrWhiteSpace(edit.Field))
        {
            return workflow;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (editableField is null)
        {
            return workflow;
        }

        return workflow with
        {
            Objects = workflow.Objects
                .Select(placedObject => placedObject.ObjectId == edit.RecordId
                    ? placedObject with
                    {
                        Fields = RefreshConditionalFieldStates(placedObject.Fields
                            .Select(field => field.Field == edit.Field
                                ? field with
                                {
                                    Value = edit.NewValue ?? string.Empty,
                                    DisplayValue = FormatDisplayValue(edit.NewValue ?? string.Empty, editableField),
                                }
                                : field)
                            .ToArray()),
                    }
                    : placedObject)
                .ToArray(),
        };
    }

    private static EditSession ReplacePendingPlacementEdit(EditSession session, PendingEdit pendingEdit)
    {
        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(session, pendingEdit);
        if (!IsFixedSpeciesEditAwayFromAlcremie(pendingEdit))
        {
            return updatedSession;
        }

        return updatedSession with
        {
            PendingEdits = updatedSession.PendingEdits
                .Where(edit => !IsSamePlacementField(edit, pendingEdit.RecordId, SvPlacementWorkflowService.FixedAlcremieSweetField))
                .ToArray(),
        };
    }

    private static bool IsFixedSpeciesEditAwayFromAlcremie(PendingEdit edit)
    {
        return string.Equals(edit.Domain, SvEditSessionSupport.PlacementDomain, StringComparison.Ordinal)
            && string.Equals(edit.Field, SvPlacementWorkflowService.FixedSpeciesIdField, StringComparison.Ordinal)
            && int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var speciesId)
            && speciesId != AlcremieSpeciesId;
    }

    private static bool IsSamePlacementField(PendingEdit edit, string? recordId, string field)
    {
        return string.Equals(edit.Domain, SvEditSessionSupport.PlacementDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, recordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, field, StringComparison.Ordinal);
    }

    private static IReadOnlyList<SvPlacementFieldValue> RefreshConditionalFieldStates(IReadOnlyList<SvPlacementFieldValue> fields)
    {
        if (fields.All(field => !string.Equals(field.Field, SvPlacementWorkflowService.FixedAlcremieSweetField, StringComparison.Ordinal)))
        {
            return fields;
        }

        var speciesField = fields.FirstOrDefault(field =>
            string.Equals(field.Field, SvPlacementWorkflowService.FixedSpeciesIdField, StringComparison.Ordinal));
        var isAlcremie = speciesField is not null
            && int.TryParse(speciesField.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var speciesId)
            && speciesId == AlcremieSpeciesId;

        return fields
            .Select(field => string.Equals(field.Field, SvPlacementWorkflowService.FixedAlcremieSweetField, StringComparison.Ordinal)
                ? field with { IsReadOnly = !isAlcremie }
                : field)
            .ToArray();
    }

    private static string FormatDisplayValue(string value, SvPlacementEditableField field)
    {
        if (field.Options.FirstOrDefault(option => option.Value.ToString(CultureInfo.InvariantCulture) == value) is { } option)
        {
            return option.Label;
        }

        return value;
    }

    private static bool IsFixedMoveField(PendingEdit edit)
    {
        return edit.Field is
            SvPlacementWorkflowService.FixedMove1Field or
            SvPlacementWorkflowService.FixedMove2Field or
            SvPlacementWorkflowService.FixedMove3Field or
            SvPlacementWorkflowService.FixedMove4Field;
    }

    private static bool IsCoinMoveField(PendingEdit edit)
    {
        return edit.Field is
            SvPlacementWorkflowService.CoinMove1Field or
            SvPlacementWorkflowService.CoinMove2Field or
            SvPlacementWorkflowService.CoinMove3Field or
            SvPlacementWorkflowService.CoinMove4Field;
    }

    private static void ApplyFixedEdit(
        FixedSymbolRow row,
        PendingEdit edit,
        SvDefaultMoveResolver moveResolver,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseInt(edit.NewValue, out var integer) && !TryParseFloat(edit.NewValue, out _))
        {
            diagnostics.Add(CreateInvalidApplyDiagnostic(edit));
            return;
        }

        switch (edit.Field)
        {
            case SvPlacementWorkflowService.FixedSpeciesIdField:
                row.PokeData.DevId = integer;
                break;
            case SvPlacementWorkflowService.FixedFormField:
                row.PokeData.FormId = checked((short)integer);
                break;
            case SvPlacementWorkflowService.FixedLevelField:
                row.PokeData.Level = integer;
                break;
            case SvPlacementWorkflowService.FixedGenderField:
                row.PokeData.Sex = integer;
                break;
            case SvPlacementWorkflowService.FixedShinyField:
                row.PokeData.RareType = integer;
                break;
            case SvPlacementWorkflowService.FixedIvModeField:
                row.PokeData.TalentType = integer;
                break;
            case SvPlacementWorkflowService.FixedIvHpField:
                row.PokeData.TalentValue.Hp = integer;
                break;
            case SvPlacementWorkflowService.FixedIvAttackField:
                row.PokeData.TalentValue.Attack = integer;
                break;
            case SvPlacementWorkflowService.FixedIvDefenseField:
                row.PokeData.TalentValue.Defense = integer;
                break;
            case SvPlacementWorkflowService.FixedIvSpecialAttackField:
                row.PokeData.TalentValue.SpecialAttack = integer;
                break;
            case SvPlacementWorkflowService.FixedIvSpecialDefenseField:
                row.PokeData.TalentValue.SpecialDefense = integer;
                break;
            case SvPlacementWorkflowService.FixedIvSpeedField:
                row.PokeData.TalentValue.Speed = integer;
                break;
            case SvPlacementWorkflowService.FixedGuaranteedPerfectIvsField:
                row.PokeData.TalentVNum = checked((sbyte)integer);
                break;
            case SvPlacementWorkflowService.FixedMoveModeField:
                row.PokeData.WazaType = integer;
                break;
            case SvPlacementWorkflowService.FixedMove1Field:
                row.PokeData.SetMove(0, integer, moveResolver);
                break;
            case SvPlacementWorkflowService.FixedMove2Field:
                row.PokeData.SetMove(1, integer, moveResolver);
                break;
            case SvPlacementWorkflowService.FixedMove3Field:
                row.PokeData.SetMove(2, integer, moveResolver);
                break;
            case SvPlacementWorkflowService.FixedMove4Field:
                row.PokeData.SetMove(3, integer, moveResolver);
                break;
            case SvPlacementWorkflowService.FixedAbilityModeField:
                row.PokeData.TokuseiIndex = integer;
                break;
            case SvPlacementWorkflowService.FixedScaleModeField:
                row.PokeData.ScaleType = integer;
                break;
            case SvPlacementWorkflowService.FixedScaleValueField:
                row.PokeData.ScaleValue = checked((short)integer);
                break;
            case SvPlacementWorkflowService.FixedTeraTypeField:
                row.PokeData.GemType = integer;
                break;
            case SvPlacementWorkflowService.FixedAlcremieSweetField:
                row.PokeData.MahoippuViewId = checked((byte)integer);
                break;
            case SvPlacementWorkflowService.FixedAiActionField:
                row.AI.ActionId = integer;
                break;
            case SvPlacementWorkflowService.FixedAiHungerField when TryParseFloat(edit.NewValue, out var value):
                row.AI.Hunger = value;
                break;
            case SvPlacementWorkflowService.FixedAiFatigueField when TryParseFloat(edit.NewValue, out var value):
                row.AI.Fatigue = value;
                break;
            case SvPlacementWorkflowService.FixedAiSleepinessField when TryParseFloat(edit.NewValue, out var value):
                row.AI.Sleepiness = value;
                break;
            case SvPlacementWorkflowService.FixedAiPriorityField:
                row.AI.Priority = integer;
                break;
            case SvPlacementWorkflowService.FixedAiTriggerActionField:
                row.AI.TriggerActionId = integer;
                break;
            case SvPlacementWorkflowService.FixedAiFrequencyField:
                row.AI.OverrideFrequency = integer;
                break;
            case SvPlacementWorkflowService.FixedSpawnMinDistanceField when TryParseFloat(edit.NewValue, out var value):
                row.Generation.MinCreateDistance = value;
                break;
            case SvPlacementWorkflowService.FixedSpawnMaxDistanceField when TryParseFloat(edit.NewValue, out var value):
                row.Generation.MaxCreateDistance = value;
                break;
            case SvPlacementWorkflowService.FixedDespawnMinDistanceField when TryParseFloat(edit.NewValue, out var value):
                row.Generation.MinDestroyDistance = value;
                break;
            case SvPlacementWorkflowService.FixedDespawnMaxDistanceField when TryParseFloat(edit.NewValue, out var value):
                row.Generation.MaxDestroyDistance = value;
                break;
            case SvPlacementWorkflowService.FixedSpawnModeField:
                row.Generation.GenerationPattern = integer;
                break;
            case SvPlacementWorkflowService.FixedSpawnOnLoadField:
                row.Generation.FirstGenerate = integer != 0;
                break;
            case SvPlacementWorkflowService.FixedRespawnChanceField:
                row.Generation.RepopProbability = integer;
                break;
            default:
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                break;
        }
    }

    private static void ApplyCoinEdit(
        CoinSymbolRow row,
        PendingEdit edit,
        SvDefaultMoveResolver moveResolver,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseInt(edit.NewValue, out var integer))
        {
            diagnostics.Add(CreateInvalidApplyDiagnostic(edit));
            return;
        }

        switch (edit.Field)
        {
            case SvPlacementWorkflowService.CoinDisableBattleOutField:
                row.DisableBattleOut = integer != 0;
                break;
            case SvPlacementWorkflowService.CoinEventEncounterField:
                row.EventEncount = integer != 0;
                break;
            case SvPlacementWorkflowService.CoinSpeciesIdField:
                row.PokeData.DevId = integer;
                break;
            case SvPlacementWorkflowService.CoinFormField:
                row.PokeData.FormId = checked((short)integer);
                break;
            case SvPlacementWorkflowService.CoinLevelField:
                row.PokeData.Level = integer;
                break;
            case SvPlacementWorkflowService.CoinGenderField:
                row.PokeData.Sex = integer;
                break;
            case SvPlacementWorkflowService.CoinShinyField:
                row.PokeData.RareType = integer;
                break;
            case SvPlacementWorkflowService.CoinIvModeField:
                row.PokeData.TalentType = integer;
                break;
            case SvPlacementWorkflowService.CoinGuaranteedPerfectIvsField:
                row.PokeData.TalentVNum = checked((sbyte)integer);
                break;
            case SvPlacementWorkflowService.CoinHeldItemField:
                row.PokeData.Item = integer;
                break;
            case SvPlacementWorkflowService.CoinDropItemField:
                row.PokeData.DropItem = integer;
                break;
            case SvPlacementWorkflowService.CoinDropCountField:
                row.PokeData.DropItemNum = checked((sbyte)integer);
                break;
            case SvPlacementWorkflowService.CoinNatureField:
                row.PokeData.Seikaku = integer;
                break;
            case SvPlacementWorkflowService.CoinNatureBoostField:
                row.PokeData.SeikakuHosei = integer;
                break;
            case SvPlacementWorkflowService.CoinAbilityModeField:
                row.PokeData.Tokusei = integer;
                break;
            case SvPlacementWorkflowService.CoinMoveModeField:
                row.PokeData.WazaType = integer;
                break;
            case SvPlacementWorkflowService.CoinMove1Field:
                row.PokeData.SetMove(0, integer, moveResolver);
                break;
            case SvPlacementWorkflowService.CoinMove2Field:
                row.PokeData.SetMove(1, integer, moveResolver);
                break;
            case SvPlacementWorkflowService.CoinMove3Field:
                row.PokeData.SetMove(2, integer, moveResolver);
                break;
            case SvPlacementWorkflowService.CoinMove4Field:
                row.PokeData.SetMove(3, integer, moveResolver);
                break;
            case SvPlacementWorkflowService.CoinTeraTypeField:
                row.PokeData.GemType = integer;
                break;
            case SvPlacementWorkflowService.CoinScaleModeField:
                row.PokeData.ScaleType = integer;
                break;
            case SvPlacementWorkflowService.CoinScaleValueField:
                row.PokeData.ScaleValue = checked((short)integer);
                break;
            case SvPlacementWorkflowService.CoinRibbonField:
                row.PokeData.SetRibbon = integer;
                break;
            default:
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                break;
        }
    }

    private static void ApplyHiddenEdit(
        HiddenItemRow row,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseInt(edit.NewValue, out var integer)
            || !SvPlacementWorkflowService.TryParseHiddenItemField(edit.Field ?? string.Empty, out var slot, out var slotField))
        {
            diagnostics.Add(CreateInvalidApplyDiagnostic(edit));
            return;
        }

        switch (slotField)
        {
            case SvPlacementWorkflowService.HiddenItemSlotField.ItemId:
                row.Items[slot].ItemId = integer;
                break;
            case SvPlacementWorkflowService.HiddenItemSlotField.Chance:
                row.Items[slot].EmergePercent = integer;
                break;
            case SvPlacementWorkflowService.HiddenItemSlotField.Count:
                row.Items[slot].DropCount = integer;
                break;
            default:
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                break;
        }
    }

    private static void ApplyRummagingEdit(
        RummagingRow row,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseInt(edit.NewValue, out var integer))
        {
            diagnostics.Add(CreateInvalidApplyDiagnostic(edit));
            return;
        }

        if (edit.Field == SvPlacementWorkflowService.RummagingCategoryField)
        {
            row.Category = integer;
            return;
        }

        if (edit.Field == SvPlacementWorkflowService.RummagingPatternField)
        {
            row.Pattern = integer;
            return;
        }

        if (SvPlacementWorkflowService.TryParseRummagingItemField(edit.Field ?? string.Empty, out var slot))
        {
            row.Items[slot] = integer;
            return;
        }

        diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
    }

    private static IReadOnlyList<FixedSymbolRow> ReadFixedRows(byte[] bytes)
    {
        var table = FixedSymbolTableArray.GetRootAsFixedSymbolTableArray(new ByteBuffer(bytes));
        var rows = new List<FixedSymbolRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add(FixedSymbolRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteFixedRows(IReadOnlyList<FixedSymbolRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = FixedSymbolTableArray.CreateValuesVector(builder, offsets);
        var root = FixedSymbolTableArray.CreateFixedSymbolTableArray(builder, vector);
        FixedSymbolTableArray.FinishFixedSymbolTableArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static IReadOnlyList<CoinSymbolRow> ReadCoinRows(byte[] bytes)
    {
        var table = EventBattlePokemonArray.GetRootAsEventBattlePokemonArray(new ByteBuffer(bytes));
        var rows = new List<CoinSymbolRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add(CoinSymbolRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteCoinRows(IReadOnlyList<CoinSymbolRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = EventBattlePokemonArray.CreateValuesVector(builder, offsets);
        var root = EventBattlePokemonArray.CreateEventBattlePokemonArray(builder, vector);
        EventBattlePokemonArray.FinishEventBattlePokemonArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static IReadOnlyList<HiddenItemRow> ReadHiddenRows(byte[] bytes)
    {
        var table = HiddenItemDataTableArray.GetRootAsHiddenItemDataTableArray(new ByteBuffer(bytes));
        var rows = new List<HiddenItemRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add(HiddenItemRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteHiddenRows(IReadOnlyList<HiddenItemRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = HiddenItemDataTableArray.CreateValuesVector(builder, offsets);
        var root = HiddenItemDataTableArray.CreateHiddenItemDataTableArray(builder, vector);
        HiddenItemDataTableArray.FinishHiddenItemDataTableArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static IReadOnlyList<RummagingRow> ReadRummagingRows(byte[] bytes)
    {
        var table = RummagingItemDataTableArray.GetRootAsRummagingItemDataTableArray(new ByteBuffer(bytes));
        var rows = new List<RummagingRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add(RummagingRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteRummagingRows(IReadOnlyList<RummagingRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = RummagingItemDataTableArray.CreateValuesVector(builder, offsets);
        var root = RummagingItemDataTableArray.CreateRummagingItemDataTableArray(builder, vector);
        RummagingItemDataTableArray.FinishRummagingItemDataTableArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static string GetEditSourcePath(PendingEdit edit)
    {
        return SvPlacementWorkflowService.TryParseRecordId(edit.RecordId, out _, out var sourcePath, out _)
            ? sourcePath
            : string.Empty;
    }

    private static string GetEditCategory(PendingEdit edit)
    {
        return SvPlacementWorkflowService.TryParseRecordId(edit.RecordId, out var category, out _, out _)
            ? category
            : string.Empty;
    }

    private static bool TryParseInt(string? value, out int parsed)
    {
        return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseFloat(string? value, out float parsed)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    private static ValidationDiagnostic CreateInvalidApplyDiagnostic(PendingEdit edit)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pending Placement edit is not valid for apply.",
            field: edit.Field,
            expected: "Valid Placement edit");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Placement field '{field}' is not editable for Scarlet/Violet yet.",
            field: "field",
            expected: "Supported S/V Placement field");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            severity,
            message,
            SvEditSessionSupport.PlacementDomain,
            field,
            expected,
            file);
    }

    private sealed class FixedSymbolRow
    {
        public string TableKey { get; init; } = string.Empty;
        public SymbolPokeData PokeData { get; init; } = new();
        public FixedAiRow AI { get; init; } = new();
        public FixedGenerationRow Generation { get; init; } = new();

        public static FixedSymbolRow From(FixedSymbolTable row)
        {
            return new FixedSymbolRow
            {
                TableKey = row.TableKey ?? string.Empty,
                PokeData = SymbolPokeData.From(row.PokeDataSymbol),
                AI = FixedAiRow.From(row.PokeAI),
                Generation = FixedGenerationRow.From(row.PokeGeneration),
            };
        }

        public Offset<FixedSymbolTable> Write(FlatBufferBuilder builder)
        {
            var tableKeyOffset = string.IsNullOrEmpty(TableKey) ? default : builder.CreateString(TableKey);
            return FixedSymbolTable.CreateFixedSymbolTable(
                builder,
                tableKeyOffset,
                PokeData.WriteSymbol(builder),
                AI.Write(builder),
                Generation.Write(builder));
        }
    }

    private sealed class CoinSymbolRow
    {
        public string Label { get; init; } = string.Empty;
        public EventBattlePokeData PokeData { get; init; } = new();
        public bool DisableBattleOut { get; set; }
        public bool EventEncount { get; set; }

        public static CoinSymbolRow From(EventBattlePokemon row)
        {
            return new CoinSymbolRow
            {
                Label = row.Label ?? string.Empty,
                PokeData = EventBattlePokeData.From(row.PokeData),
                DisableBattleOut = row.DisableBattleOut,
                EventEncount = row.EventEncount,
            };
        }

        public Offset<EventBattlePokemon> Write(FlatBufferBuilder builder)
        {
            var labelOffset = string.IsNullOrEmpty(Label) ? default : builder.CreateString(Label);
            return EventBattlePokemon.CreateEventBattlePokemon(
                builder,
                labelOffset,
                PokeData.WriteEventBattle(builder),
                DisableBattleOut,
                EventEncount);
        }
    }

    private sealed class HiddenItemRow
    {
        public string TableId { get; init; } = string.Empty;
        public HiddenItemSlotRow[] Items { get; init; } = Enumerable.Range(0, 10).Select(_ => new HiddenItemSlotRow()).ToArray();

        public static HiddenItemRow From(HiddenItemDataTable row)
        {
            return new HiddenItemRow
            {
                TableId = row.TableId ?? string.Empty,
                Items = Enumerable.Range(0, 10)
                    .Select(slot => HiddenItemSlotRow.From(row.Item(slot)))
                    .ToArray(),
            };
        }

        public Offset<HiddenItemDataTable> Write(FlatBufferBuilder builder)
        {
            var tableIdOffset = string.IsNullOrEmpty(TableId) ? default : builder.CreateString(TableId);
            var itemOffsets = Items.Select(item => item.Write(builder)).ToArray();
            return HiddenItemDataTable.CreateHiddenItemDataTable(builder, tableIdOffset, itemOffsets);
        }
    }

    private sealed class HiddenItemSlotRow
    {
        public int ItemId { get; set; }
        public int EmergePercent { get; set; }
        public int DropCount { get; set; }

        public static HiddenItemSlotRow From(HiddenItemDataTableInfo? row)
        {
            return new HiddenItemSlotRow
            {
                ItemId = row?.ItemId ?? 0,
                EmergePercent = row?.EmergePercent ?? 0,
                DropCount = row?.DropCount ?? 0,
            };
        }

        public Offset<HiddenItemDataTableInfo> Write(FlatBufferBuilder builder)
        {
            return HiddenItemDataTableInfo.CreateHiddenItemDataTableInfo(builder, ItemId, EmergePercent, DropCount);
        }
    }

    private sealed class RummagingRow
    {
        public int Category { get; set; }
        public int Pattern { get; set; }
        public int[] Items { get; init; } = new int[5];

        public static RummagingRow From(RummagingItemDataTable row)
        {
            return new RummagingRow
            {
                Category = (int)row.Category,
                Pattern = (int)row.Pattern,
                Items = Enumerable.Range(0, 5).Select(row.Item).ToArray(),
            };
        }

        public Offset<RummagingItemDataTable> Write(FlatBufferBuilder builder)
        {
            return RummagingItemDataTable.CreateRummagingItemDataTable(
                builder,
                (RummagingCategory)Category,
                (RummagingPattern)Pattern,
                Items.ElementAtOrDefault(0),
                Items.ElementAtOrDefault(1),
                Items.ElementAtOrDefault(2),
                Items.ElementAtOrDefault(3),
                Items.ElementAtOrDefault(4));
        }
    }

    private class SymbolPokeData
    {
        public int DevId { get; set; }
        public short FormId { get; set; }
        public int Level { get; set; }
        public int Sex { get; set; }
        public int RareType { get; set; }
        public int TalentType { get; set; }
        public ParamSetRow TalentValue { get; init; } = new();
        public sbyte TalentVNum { get; set; }
        public int WazaType { get; set; }
        public WazaSetRow Waza1 { get; init; } = new();
        public WazaSetRow Waza2 { get; init; } = new();
        public WazaSetRow Waza3 { get; init; } = new();
        public WazaSetRow Waza4 { get; init; } = new();
        public int TokuseiIndex { get; set; }
        public int ScaleType { get; set; }
        public short ScaleValue { get; set; }
        public int GemType { get; set; }
        public byte MahoippuViewId { get; set; }

        public static SymbolPokeData From(global::PokeDataSymbol? row)
        {
            if (row is null)
            {
                return new SymbolPokeData();
            }

            return new SymbolPokeData
            {
                DevId = (int)row.Value.DevId,
                FormId = row.Value.FormId,
                Level = row.Value.Level,
                Sex = (int)row.Value.Sex,
                RareType = (int)row.Value.RareType,
                TalentType = (int)row.Value.TalentType,
                TalentValue = ParamSetRow.From(row.Value.TalentValue),
                TalentVNum = row.Value.TalentVNum,
                WazaType = (int)row.Value.WazaType,
                Waza1 = WazaSetRow.From(row.Value.Waza1),
                Waza2 = WazaSetRow.From(row.Value.Waza2),
                Waza3 = WazaSetRow.From(row.Value.Waza3),
                Waza4 = WazaSetRow.From(row.Value.Waza4),
                TokuseiIndex = (int)row.Value.TokuseiIndex,
                ScaleType = (int)row.Value.ScaleType,
                ScaleValue = row.Value.ScaleValue,
                GemType = (int)row.Value.GemType,
                MahoippuViewId = (byte)row.Value.MahoippuViewId,
            };
        }

        public Offset<global::PokeDataSymbol> WriteSymbol(FlatBufferBuilder builder)
        {
            return global::PokeDataSymbol.CreatePokeDataSymbol(
                builder,
                (global::pml.common.DevID)checked((ushort)DevId),
                FormId,
                Level,
                (global::SexType)Sex,
                (global::RareType)RareType,
                (global::TalentType)TalentType,
                TalentValue.Write(builder),
                TalentVNum,
                (global::WazaType)WazaType,
                Waza1.Write(builder),
                Waza2.Write(builder),
                Waza3.Write(builder),
                Waza4.Write(builder),
                (global::TokuseiType)TokuseiIndex,
                (global::SizeType)ScaleType,
                ScaleValue,
                (global::GemType)GemType,
                (global::MahoippuViewID)MahoippuViewId);
        }

        public void SetMove(int index, int moveId, SvDefaultMoveResolver moveResolver)
        {
            var moveRows = MoveRows();
            if (WazaType == (int)global::WazaType.DEFAULT || moveRows.All(row => row.MoveId == 0))
            {
                var currentMoves = moveRows.Select(row => row.MoveId).ToArray();
                var defaultMoves = currentMoves.Any(move => move != 0)
                    ? currentMoves
                    : moveResolver.Resolve(DevId, FormId, Level);

                for (var defaultIndex = 0; defaultIndex < moveRows.Length; defaultIndex++)
                {
                    moveRows[defaultIndex].MoveId = defaultMoves.ElementAtOrDefault(defaultIndex);
                }

                WazaType = (int)global::WazaType.MANUAL;
            }

            moveRows[index].MoveId = moveId;
        }

        private WazaSetRow[] MoveRows()
        {
            return [Waza1, Waza2, Waza3, Waza4];
        }
    }

    private sealed class EventBattlePokeData : SymbolPokeData
    {
        public ParamSetRow EffortValue { get; init; } = new();
        public int Item { get; set; }
        public int DropItem { get; set; }
        public sbyte DropItemNum { get; set; }
        public int Seikaku { get; set; }
        public int SeikakuHosei { get; set; }
        public int Tokusei { get; set; }
        public int SetRibbon { get; set; }

        public static EventBattlePokeData From(global::PokeDataEventBattle? row)
        {
            if (row is null)
            {
                return new EventBattlePokeData();
            }

            return new EventBattlePokeData
            {
                DevId = (int)row.Value.DevId,
                FormId = row.Value.FormId,
                Sex = (int)row.Value.Sex,
                Level = row.Value.Level,
                RareType = (int)row.Value.RareType,
                TalentType = (int)row.Value.TalentType,
                TalentVNum = row.Value.TalentVnum,
                TalentValue = ParamSetRow.From(row.Value.TalentValue),
                EffortValue = ParamSetRow.From(row.Value.EffortValue),
                Item = (int)row.Value.Item,
                DropItem = (int)row.Value.DropItem,
                DropItemNum = row.Value.DropItemNum,
                Seikaku = (int)row.Value.Seikaku,
                SeikakuHosei = (int)row.Value.SeikakuHosei,
                Tokusei = (int)row.Value.Tokusei,
                WazaType = (int)row.Value.WazaType,
                Waza1 = WazaSetRow.From(row.Value.Waza1),
                Waza2 = WazaSetRow.From(row.Value.Waza2),
                Waza3 = WazaSetRow.From(row.Value.Waza3),
                Waza4 = WazaSetRow.From(row.Value.Waza4),
                GemType = (int)row.Value.GemType,
                ScaleType = (int)row.Value.ScaleType,
                ScaleValue = row.Value.ScaleValue,
                SetRibbon = (int)row.Value.SetRibbon,
            };
        }

        public Offset<global::PokeDataEventBattle> WriteEventBattle(FlatBufferBuilder builder)
        {
            return global::PokeDataEventBattle.CreatePokeDataEventBattle(
                builder,
                (global::pml.common.DevID)checked((ushort)DevId),
                FormId,
                (global::SexType)Sex,
                Level,
                (global::RareType)RareType,
                (global::TalentType)TalentType,
                TalentVNum,
                TalentValue.Write(builder),
                EffortValue.Write(builder),
                (global::ItemID)Item,
                (global::ItemID)DropItem,
                DropItemNum,
                (global::SeikakuType)Seikaku,
                (global::SeikakuType)SeikakuHosei,
                (global::TokuseiType)Tokusei,
                (global::WazaType)WazaType,
                Waza1.Write(builder),
                Waza2.Write(builder),
                Waza3.Write(builder),
                Waza4.Write(builder),
                (global::GemType)GemType,
                (global::SizeType)ScaleType,
                ScaleValue,
                (global::RibbonType)SetRibbon);
        }
    }

    private sealed class FixedAiRow
    {
        public int ActionId { get; set; }
        public float Hunger { get; set; }
        public float Fatigue { get; set; }
        public float Sleepiness { get; set; }
        public int Priority { get; set; }
        public int TriggerActionId { get; set; }
        public int OverrideFrequency { get; set; }

        public static FixedAiRow From(FixedSymbolAI? row)
        {
            return row is null
                ? new FixedAiRow()
                : new FixedAiRow
                {
                    ActionId = row.Value.ActionId,
                    Hunger = row.Value.Hunger,
                    Fatigue = row.Value.Fatigue,
                    Sleepiness = row.Value.Sleepiness,
                    Priority = row.Value.Priority,
                    TriggerActionId = row.Value.TriggerActionId,
                    OverrideFrequency = (int)row.Value.OverrideFrequency,
                };
        }

        public Offset<FixedSymbolAI> Write(FlatBufferBuilder builder)
        {
            return FixedSymbolAI.CreateFixedSymbolAI(
                builder,
                ActionId,
                Hunger,
                Fatigue,
                Sleepiness,
                Priority,
                TriggerActionId,
                (BehaviorFrequency)OverrideFrequency);
        }
    }

    private sealed class FixedGenerationRow
    {
        public float MinCreateDistance { get; set; }
        public float MaxCreateDistance { get; set; }
        public float MinDestroyDistance { get; set; }
        public float MaxDestroyDistance { get; set; }
        public int GenerationPattern { get; set; }
        public bool FirstGenerate { get; set; }
        public int RepopProbability { get; set; }
        public string RequireScenarioId { get; init; } = string.Empty;

        public static FixedGenerationRow From(FixedSymbolGeneration? row)
        {
            return row is null
                ? new FixedGenerationRow()
                : new FixedGenerationRow
                {
                    MinCreateDistance = row.Value.MinCreateDistance,
                    MaxCreateDistance = row.Value.MaxCreateDistance,
                    MinDestroyDistance = row.Value.MinDestroyDistance,
                    MaxDestroyDistance = row.Value.MaxDestroyDistance,
                    GenerationPattern = (int)row.Value.GenerationPattern,
                    FirstGenerate = row.Value.FirstGenerate,
                    RepopProbability = row.Value.RepopProbability,
                    RequireScenarioId = row.Value.RequireScenarioId ?? string.Empty,
                };
        }

        public Offset<FixedSymbolGeneration> Write(FlatBufferBuilder builder)
        {
            var scenarioOffset = string.IsNullOrEmpty(RequireScenarioId)
                ? default
                : builder.CreateString(RequireScenarioId);
            return FixedSymbolGeneration.CreateFixedSymbolGeneration(
                builder,
                MinCreateDistance,
                MaxCreateDistance,
                MinDestroyDistance,
                MaxDestroyDistance,
                (GenerationPattern)GenerationPattern,
                FirstGenerate,
                RepopProbability,
                scenarioOffset);
        }
    }

    private sealed class ParamSetRow
    {
        public int Hp { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int SpecialAttack { get; set; }
        public int SpecialDefense { get; set; }
        public int Speed { get; set; }

        public static ParamSetRow From(global::ParamSet? row)
        {
            return row is null
                ? new ParamSetRow()
                : new ParamSetRow
                {
                    Hp = row.Value.Hp,
                    Attack = row.Value.Atk,
                    Defense = row.Value.Def,
                    SpecialAttack = row.Value.SpAtk,
                    SpecialDefense = row.Value.SpDef,
                    Speed = row.Value.Agi,
                };
        }

        public Offset<global::ParamSet> Write(FlatBufferBuilder builder)
        {
            return global::ParamSet.CreateParamSet(builder, Hp, Attack, Defense, SpecialAttack, SpecialDefense, Speed);
        }
    }

    private sealed class WazaSetRow
    {
        public int MoveId { get; set; }
        public sbyte PointUp { get; init; }

        public static WazaSetRow From(global::WazaSet? row)
        {
            return row is null
                ? new WazaSetRow()
                : new WazaSetRow
                {
                    MoveId = (int)row.Value.WazaId,
                    PointUp = row.Value.PointUp,
                };
        }

        public Offset<global::WazaSet> Write(FlatBufferBuilder builder)
        {
            return global::WazaSet.CreateWazaSet(
                builder,
                (global::pml.common.WazaID)checked((ushort)MoveId),
                PointUp);
        }
    }
}
