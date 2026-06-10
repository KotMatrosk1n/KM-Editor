// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Behavior;

public sealed class SwShBehaviorEditSessionService
{
    private const string BehaviorEditDomain = "workflow.behavior";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShBehaviorWorkflowService behaviorWorkflowService;

    public SwShBehaviorEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShBehaviorWorkflowService? behaviorWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.behaviorWorkflowService = behaviorWorkflowService ?? new SwShBehaviorWorkflowService();
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
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = behaviorWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditBehavior(project, workflow, diagnostics))
        {
            return new SwShBehaviorEditResult(workflow, currentSession, diagnostics);
        }

        var entry = workflow.Entries.FirstOrDefault(candidate => candidate.EntryId == entryId);
        if (entry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior entry '{entryId}' is not present in the loaded workflow.",
                field: "entryId",
                expected: "Existing behavior entry"));
            return new SwShBehaviorEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(entry, workflow.Fields, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShBehaviorEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingBehaviorEdit(currentSession, pendingEdit);

        return new SwShBehaviorEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = behaviorWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditBehavior(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Behavior change is valid."));
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
                "Create a pending Behavior edit before reviewing a change plan.",
                expected: "Pending Behavior edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShBehaviorWorkflowService.ResolveBehaviorDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior change plan could not resolve the source behavior file.",
                expected: SwShBehaviorWorkflowService.BehaviorDataPath));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = SwShBehaviorWorkflowService.ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior apply target must stay inside the configured output root.",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            dataSource.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(dataSource.GraphEntry), dataSource.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Behavior edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Behavior edits.");

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Change plan preview contains 1 target file."));

        return new ChangePlan(session.Id, [write], diagnostics);
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
                expected: "Current reviewed Behavior change plan"));
        }

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

        try
        {
            var archive = SwShSymbolBehaviorArchive.Parse(File.ReadAllBytes(dataSource.AbsolutePath));
            var archiveEdits = session.PendingEdits
                .Select(edit => ToArchiveEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, archive.WriteEdits(archiveEdits));
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, dataSource.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Behavior change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior apply failed because the source data could not be decoded: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Supported Sword/Shield behavior data"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior apply failed while writing output: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Writable LayeredFS output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static SwShBehaviorWorkflow OverlayPendingEdits(
        SwShBehaviorWorkflow workflow,
        IReadOnlyList<PendingEdit> pendingEdits)
    {
        if (pendingEdits.Count == 0)
        {
            return workflow;
        }

        var entries = workflow.Entries.ToArray();
        foreach (var edit in pendingEdits.Where(edit => edit.Domain == BehaviorEditDomain))
        {
            var index = Array.FindIndex(entries, entry => entry.EntryId == edit.RecordId);
            if (index < 0 || edit.Field is null || edit.NewValue is null)
            {
                continue;
            }

            entries[index] = OverlayPendingEdit(entries[index], edit, workflow.Fields);
        }

        return workflow with { Entries = entries };
    }

    private static SwShBehaviorEntryRecord OverlayPendingEdit(
        SwShBehaviorEntryRecord entry,
        PendingEdit edit,
        IReadOnlyList<SwShBehaviorField> fields)
    {
        var field = edit.Field!;
        var value = edit.NewValue!;
        var fieldValues = entry.Fields
            .Select(current => current.Field == field ? current with { Value = value } : current)
            .ToArray();
        var speciesId = field == SwShSymbolBehaviorArchive.SpeciesIdField && TryParseInt(value, out var parsedSpecies)
            ? parsedSpecies
            : entry.SpeciesId;
        var speciesName = field == SwShSymbolBehaviorArchive.SpeciesIdField
            ? ResolveOptionLabel(fields, SwShSymbolBehaviorArchive.SpeciesIdField, value, $"Species {speciesId}", stripLeadingId: true)
            : entry.SpeciesName;
        var form = field == SwShSymbolBehaviorArchive.FormField && TryParseInt(value, out var parsedForm)
            ? parsedForm
            : entry.Form;
        var behavior = field == SwShSymbolBehaviorArchive.BehaviorField ? value : entry.Behavior;
        var behaviorLabel = SwShBehaviorWorkflowService.GetBehaviorLabel(behavior);
        var modelPart = field == SwShSymbolBehaviorArchive.ModelPartField ? value : entry.ModelPart;
        var hitboxRadius = field == SwShSymbolBehaviorArchive.HitboxRadiusField && TryParseDouble(value, out var parsedHitbox)
            ? parsedHitbox
            : entry.HitboxRadius;
        var grassShakeRadius = field == SwShSymbolBehaviorArchive.GrassShakeRadiusField && TryParseDouble(value, out var parsedGrassShake)
            ? parsedGrassShake
            : entry.GrassShakeRadius;
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
        };
    }

    private static PendingEdit? CreatePendingEdit(
        SwShBehaviorEntryRecord entry,
        IReadOnlyList<SwShBehaviorField> fields,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!ValidateFieldValue(entry, fields, field, value, diagnostics))
        {
            return null;
        }

        var normalized = NormalizeValue(field, value);
        return new PendingEdit(
            BehaviorEditDomain,
            $"{entry.Label} {SwShBehaviorWorkflowService.GetFieldLabel(field)} -> {FormatSummaryValue(fields, field, normalized)}",
            [new ProjectFileReference(entry.Provenance.SourceLayer, entry.Provenance.SourceFile)],
            RecordId: entry.EntryId,
            Field: field,
            NewValue: normalized);
    }

    private static bool ValidateFieldValue(
        SwShBehaviorEntryRecord entry,
        IReadOnlyList<SwShBehaviorField> fields,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var fieldMetadata = fields.FirstOrDefault(candidate => candidate.Field == field);
        if (fieldMetadata is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field}' is not supported.",
                field: "field",
                expected: "Supported Behavior field"));
            return false;
        }

        if (fieldMetadata.IsReadOnly || !SwShBehaviorWorkflowService.IsEditableField(field))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{fieldMetadata.Label}' is disabled until its role is confirmed.",
                field: "field",
                expected: "Editable Behavior field"));
            return false;
        }

        if (fieldMetadata.ValueKind == "string")
        {
            return ValidateString(fieldMetadata, value, diagnostics);
        }

        if (fieldMetadata.ValueKind == "number")
        {
            return ValidateDouble(fieldMetadata, value, diagnostics);
        }

        if (!ValidateInt(fieldMetadata, value, diagnostics))
        {
            return false;
        }

        if (field == SwShSymbolBehaviorArchive.SpeciesIdField
            && entry.Fields.All(candidate => candidate.Field != SwShSymbolBehaviorArchive.SpeciesIdField))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior entry does not expose a species field.",
                field: "field",
                expected: "Behavior species field"));
            return false;
        }

        return true;
    }

    private static bool ValidateString(
        SwShBehaviorField field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > SwShBehaviorWorkflowService.MaximumStringLength)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must be 1-{SwShBehaviorWorkflowService.MaximumStringLength} characters.",
                field: "value",
                expected: "Behavior text value"));
            return false;
        }

        return true;
    }

    private static bool ValidateDouble(
        SwShBehaviorField field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseDouble(value, out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || parsed < field.MinimumValue
            || parsed > field.MaximumValue)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must be between {field.MinimumValue.ToString(CultureInfo.InvariantCulture)} and {field.MaximumValue.ToString(CultureInfo.InvariantCulture)}.",
                field: "value",
                expected: "Behavior numeric value"));
            return false;
        }

        return true;
    }

    private static bool ValidateInt(
        SwShBehaviorField field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseInt(value, out var parsed) || parsed < field.MinimumValue || parsed > field.MaximumValue)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior field '{field.Label}' must be an integer between {field.MinimumValue.ToString(CultureInfo.InvariantCulture)} and {field.MaximumValue.ToString(CultureInfo.InvariantCulture)}.",
                field: "value",
                expected: "Behavior integer value"));
            return false;
        }

        return true;
    }

    private static void ValidatePendingEdit(
        SwShBehaviorWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (edit.Domain != BehaviorEditDomain)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Behavior.",
                expected: BehaviorEditDomain));
            return;
        }

        if (edit.RecordId is null || edit.Field is null || edit.NewValue is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior edit is missing record, field, or value metadata.",
                expected: "Complete Behavior pending edit"));
            return;
        }

        var entry = workflow.Entries.FirstOrDefault(candidate => candidate.EntryId == edit.RecordId);
        if (entry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Behavior entry '{edit.RecordId}' is no longer present.",
                field: "entryId",
                expected: "Existing Behavior entry"));
            return;
        }

        ValidateFieldValue(entry, workflow.Fields, edit.Field, edit.NewValue, diagnostics);
    }

    private static SwShSymbolBehaviorEdit? ToArchiveEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (edit.RecordId is null
            || edit.Field is null
            || edit.NewValue is null
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var entryIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Behavior edit record id is invalid.",
                field: "entryId",
                expected: "Behavior entry id"));
            return null;
        }

        return new SwShSymbolBehaviorEdit(entryIndex, edit.Field, edit.NewValue);
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

    private static EditSession ReplacePendingBehaviorEdit(EditSession session, PendingEdit edit)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(candidate =>
                    candidate.Domain != edit.Domain
                    || candidate.RecordId != edit.RecordId
                    || candidate.Field != edit.Field)
                .Append(edit)
                .ToArray(),
        };
    }

    private static string NormalizeValue(string field, string value)
    {
        var spec = SwShSymbolBehaviorArchive.GetFieldSpec(field);
        return spec.FieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => double.Parse(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            SwShSymbolBehaviorFieldType.Int32 => int.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SwShSymbolBehaviorFieldType.Byte => byte.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SwShSymbolBehaviorFieldType.UInt64 => ulong.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SwShSymbolBehaviorFieldType.String => value.Trim(),
            _ => value.Trim(),
        };
    }

    private static string FormatSummaryValue(
        IReadOnlyList<SwShBehaviorField> fields,
        string field,
        string value)
    {
        return ResolveOptionLabel(fields, field, value, value, stripLeadingId: false);
    }

    private static string ResolveOptionLabel(
        IReadOnlyList<SwShBehaviorField> fields,
        string field,
        string value,
        string fallback,
        bool stripLeadingId)
    {
        var label = fields
            .FirstOrDefault(candidate => candidate.Field == field)
            ?.Options
            .FirstOrDefault(option => option.Value == value)
            ?.Label;

        if (string.IsNullOrWhiteSpace(label))
        {
            return fallback;
        }

        if (!stripLeadingId)
        {
            return label;
        }

        var firstSpace = label.IndexOf(' ');
        return firstSpace >= 0 && firstSpace + 1 < label.Length ? label[(firstSpace + 1)..] : label;
    }

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        return reviewedPlan.SessionId == currentPlan.SessionId
            && reviewedPlan.Writes.Select(write => write.TargetRelativePath).SequenceEqual(
                currentPlan.Writes.Select(write => write.TargetRelativePath),
                StringComparer.Ordinal)
            && currentPlan.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
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

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = SwShBehaviorWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Behavior output path could not be resolved inside the output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base;
    }

    private static bool TryParseDouble(string value, out double parsed)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseInt(string value, out int parsed)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
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
            Domain: BehaviorEditDomain,
            Expected: expected);
    }
}
