// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Placement;

internal sealed class ZaPlacementEditSessionService
{
    private const string WorkflowName = "Placement";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaPlacementWorkflowService placementWorkflowService;

    public ZaPlacementEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaPlacementWorkflowService? placementWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.placementWorkflowService = placementWorkflowService ?? new ZaPlacementWorkflowService(this.fileSource);
    }

    public ZaPlacementEditResult UpdateObjectField(
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

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PlacementDomain,
                diagnostics))
        {
            return new ZaPlacementEditResult(workflow, currentSession, diagnostics);
        }

        var placedObject = workflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == objectId);
        if (placedObject is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement edit targets an object that is not loaded.",
                field: "objectId",
                expected: "Existing Pokemon Legends Z-A placement object"));
            return new ZaPlacementEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(placedObject, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaPlacementEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new ZaPlacementEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPlacementEditResult UpdateObjectFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPlacementObjectFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = placementWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PlacementDomain,
                diagnostics))
        {
            return new ZaPlacementEditResult(workflow, currentSession, diagnostics);
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
                    expected: "Existing Pokemon Legends Z-A placement object"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(placedObject, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
        }

        return new ZaPlacementEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = placementWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.PlacementDomain,
            diagnostics);

        var effectiveWorkflow = workflow;
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(effectiveWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Info, "Pending Placement change is valid."));
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

        var diagnostics = Validate(paths, session).Diagnostics.ToList();
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
                .GroupBy(GetEditSourcePath, StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group =>
                {
                    var sources = group.SelectMany(edit => edit.Sources).Distinct().ToArray();
                    var writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(paths, group.Key, sources, outputMode);
                    return new PlannedFileWrite(
                        writeInfo.TargetRelativePath,
                        writeInfo.Sources,
                        writeInfo.ReplacesExistingOutput,
                        $"Apply pending Placement edits for {writeInfo.TargetRelativePath}.");
                })
                .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
                .ToList();

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
                expected: "Current reviewed Placement change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
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
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage(WorkflowName, outputMode)));
        }
        catch (Exception exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement output could not be written: {exception.Message}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles.Distinct().ToArray(), diagnostics);
    }

    private void ApplySourceGroup(
        OpenedProject project,
        ProjectPaths paths,
        string sourcePath,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles,
        ZaOutputMode outputMode)
    {
        var source = fileSource.Read(project, sourcePath);
        var document = ZaSpawnerTransformDocument.Parse(source.Bytes);
        foreach (var edit in edits)
        {
            if (!TryResolveRow(document, edit.RecordId, out var row))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Placement edit is not valid for apply.",
                    field: edit.Field,
                    expected: "Valid Placement edit"));
                continue;
            }

            ApplyField(row, edit.Field, edit.NewValue, diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        ZaWorkflowFileSource.Write(paths, sourcePath, document.Write(), outputMode);
        writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(sourcePath, outputMode));
    }

    private static PendingEdit? CreatePendingEdit(
        ZaPlacedObjectRecord placedObject,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = ZaPlacementWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var normalizedValue = TryParseEditableValue(editableField, value, diagnostics);
        if (normalizedValue is null)
        {
            return null;
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.PlacementDomain,
            $"Set {placedObject.Label} {editableField.Label.ToLowerInvariant()} to {FormatDisplayValue(editableField, normalizedValue)}.",
            new ProjectFileReference(placedObject.Provenance.SourceLayer, placedObject.Provenance.SourceFile),
            placedObject.ObjectId,
            normalizedField,
            normalizedValue);
    }

    private static void ValidatePendingEdit(
        ZaPlacementWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.PlacementDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Placement.",
                expected: ZaEditSessionSupport.PlacementDomain));
            return;
        }

        var editableField = ZaPlacementWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!ZaPlacementWorkflowService.TryParseRecordId(
                edit.RecordId,
                out _,
                out _,
                out _,
                out _)
            || workflow.Objects.All(placedObject => placedObject.ObjectId != edit.RecordId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Placement edit targets an object that is not loaded.",
                field: "objectId",
                expected: "Existing Pokemon Legends Z-A placement object"));
            return;
        }

        _ = TryParseEditableValue(editableField, edit.NewValue, diagnostics);
    }

    private static string? TryParseEditableValue(
        ZaPlacementEditableField editableField,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (editableField.ValueKind == "integer")
        {
            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
                || integer < editableField.MinimumValue
                || integer > editableField.MaximumValue)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                    field: editableField.Field,
                    expected: $"Safe {editableField.Label.ToLowerInvariant()}"));
                return null;
            }

            return integer.ToString(CultureInfo.InvariantCulture);
        }

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || number < editableField.MinimumValue
            || number > editableField.MaximumValue)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: $"Safe {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        return number.ToString("R", CultureInfo.InvariantCulture);
    }

    private static ZaPlacementWorkflow OverlayPendingEdits(
        ZaPlacementWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static ZaPlacementWorkflow OverlayPendingEdit(
        ZaPlacementWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.PlacementDomain, StringComparison.Ordinal)
            || ZaPlacementWorkflowService.GetEditableField(edit.Field) is not { } editableField
            || TryParseEditableValue(editableField, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        return workflow with
        {
            Objects = workflow.Objects
                .Select(placedObject => placedObject.ObjectId == edit.RecordId
                    ? OverlayObject(placedObject, editableField, value)
                    : placedObject)
                .ToArray(),
        };
    }

    private static ZaPlacedObjectRecord OverlayObject(
        ZaPlacedObjectRecord placedObject,
        ZaPlacementEditableField field,
        string value)
    {
        var displayValue = FormatDisplayValue(field, value);
        var fields = placedObject.Fields
            .Select(fieldValue => fieldValue.Field == field.Field
                ? fieldValue with
                {
                    Value = value,
                    DisplayValue = displayValue,
                }
                : fieldValue)
            .ToArray();

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            number = 0;
        }

        return field.Field switch
        {
            ZaPlacementWorkflowService.PositionXField => placedObject with { X = number, Fields = fields },
            ZaPlacementWorkflowService.PositionYField => placedObject with { Y = number, Fields = fields },
            ZaPlacementWorkflowService.PositionZField => placedObject with { Z = number, Fields = fields },
            ZaPlacementWorkflowService.RotationYawField => placedObject with { RotationY = number, Fields = fields },
            _ => placedObject with { Fields = fields },
        };
    }

    private static void ApplyField(
        ZaSpawnerTransformRow row,
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = ZaPlacementWorkflowService.GetEditableField(field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
            return;
        }

        if (TryParseEditableValue(editableField, value, diagnostics) is not { } normalizedValue)
        {
            return;
        }

        if (field == ZaPlacementWorkflowService.AttachTransformEnableField)
        {
            row.AttachTransformEnable = normalizedValue == "1";
            return;
        }

        var number = float.Parse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture);
        row.Position = field switch
        {
            ZaPlacementWorkflowService.PositionXField => row.Position with { X = number },
            ZaPlacementWorkflowService.PositionYField => row.Position with { Y = number },
            ZaPlacementWorkflowService.PositionZField => row.Position with { Z = number },
            _ => row.Position,
        };
        row.Rotation = field switch
        {
            ZaPlacementWorkflowService.RotationPitchField => row.Rotation with { X = number },
            ZaPlacementWorkflowService.RotationYawField => row.Rotation with { Y = number },
            ZaPlacementWorkflowService.RotationRollField => row.Rotation with { Z = number },
            _ => row.Rotation,
        };
    }

    private static bool TryResolveRow(
        ZaSpawnerTransformDocument document,
        string? recordId,
        out ZaSpawnerTransformRow row)
    {
        row = null!;
        if (!ZaPlacementWorkflowService.TryParseRecordId(
                recordId,
                out _,
                out _,
                out var groupIndex,
                out var rowIndex))
        {
            return false;
        }

        row = document.Groups.FirstOrDefault(group => group.GroupIndex == groupIndex)
            ?.Rows.FirstOrDefault(candidate => candidate.RowIndex == rowIndex)!;
        return row is not null;
    }

    private static string GetEditSourcePath(PendingEdit edit)
    {
        return ZaPlacementWorkflowService.TryParseRecordId(edit.RecordId, out _, out var sourcePath, out _, out _)
            ? sourcePath
            : string.Empty;
    }

    private static string FormatDisplayValue(
        ZaPlacementEditableField field,
        string value)
    {
        return field.Field == ZaPlacementWorkflowService.AttachTransformEnableField
            ? value == "1" ? "Yes" : "No"
            : value;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Placement field '{field}' is not supported by Pokemon Legends Z-A Placement yet.",
            field: "field",
            expected: "Supported Z-A Placement field");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            severity,
            message,
            ZaEditSessionSupport.PlacementDomain,
            file,
            field,
            expected);
    }
}
