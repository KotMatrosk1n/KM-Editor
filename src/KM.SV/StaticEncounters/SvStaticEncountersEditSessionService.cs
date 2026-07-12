// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Placement;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.StaticEncounters;

internal sealed class SvStaticEncountersEditSessionService
{
    private const int AlcremieSpeciesId = (int)global::pml.common.DevID.DEV_MAHOIPPU;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvStaticEncountersWorkflowService staticEncountersWorkflowService;
    private readonly SvPlacementEditSessionService placementEditSessionService;

    public SvStaticEncountersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvStaticEncountersWorkflowService? staticEncountersWorkflowService = null,
        SvPlacementWorkflowService? placementWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        var sharedFileSource = fileSource ?? new SvWorkflowFileSource();
        var sharedPlacementWorkflowService = placementWorkflowService ?? new SvPlacementWorkflowService(sharedFileSource);
        this.staticEncountersWorkflowService = staticEncountersWorkflowService
            ?? new SvStaticEncountersWorkflowService(sharedPlacementWorkflowService);
        placementEditSessionService = new SvPlacementEditSessionService(
            this.projectWorkspaceService,
            sharedFileSource,
            sharedPlacementWorkflowService,
            includeStaticEncounterObjects: true);
    }

    public SvStaticEncountersEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int encounterIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = staticEncountersWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.StaticEncountersDomain,
                diagnostics))
        {
            return new SvStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EncounterIndex == encounterIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter index {encounterIndex} is not present in the loaded workflow.",
                field: "encounterIndex",
                expected: "Existing Static Encounter record"));
            return new SvStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, encounter, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SvStaticEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = OverlayPendingEdits(staticEncountersWorkflowService.Load(project), session.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.StaticEncountersDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Info, "Pending Static Encounter change is valid."));
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
        if (validation.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), validation.Diagnostics);
        }

        if (session.PendingEdits.Count == 0)
        {
            return new ChangePlan(
                session.Id,
                Array.Empty<PlannedFileWrite>(),
                [CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Create a pending Static Encounter edit before reviewing a change plan.",
                    expected: "Pending Static Encounter edit")]);
        }

        var placementSession = TranslateToPlacementSession(paths, session, out var translationDiagnostics);
        if (translationDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), translationDiagnostics);
        }

        var plan = placementEditSessionService.CreateChangePlan(paths, placementSession, outputMode);
        return plan with { Diagnostics = RemapDiagnostics(plan.Diagnostics) };
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

        var placementSession = TranslateToPlacementSession(paths, session, out var translationDiagnostics);
        if (translationDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            var applyId = Guid.NewGuid().ToString("N");
            var appliedAt = DateTimeOffset.UtcNow;
            var plan = new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), translationDiagnostics);
            return SvEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                plan,
                Array.Empty<ProjectFileReference>(),
                translationDiagnostics);
        }

        var result = placementEditSessionService.ApplyChangePlan(paths, placementSession, reviewedPlan, outputMode);
        return result with { Diagnostics = RemapDiagnostics(result.Diagnostics) };
    }

    private static PendingEdit? CreatePendingEdit(
        SvStaticEncountersWorkflow workflow,
        SvStaticEncounterEntry encounter,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!encounter.SupportedFields.Contains(normalizedField, StringComparer.Ordinal)
            || !SvStaticEncountersWorkflowService.TryMapField(encounter.CategoryId, normalizedField, out _))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, normalizedField, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (editableField.IsReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter field '{editableField.Label}' is read-only for Scarlet/Violet.",
                field: normalizedField,
                expected: "Editable Static Encounter field"));
            return null;
        }

        if (encounter.FieldReadOnly.TryGetValue(normalizedField, out var isFieldReadOnly) && isFieldReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter field '{editableField.Label}' is read-only for this encounter.",
                field: normalizedField,
                expected: "Editable Static Encounter field"));
            return null;
        }

        var normalizedValue = NormalizeValue(editableField, value, diagnostics);
        if (normalizedValue is null)
        {
            return null;
        }

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.StaticEncountersDomain,
            $"Set {encounter.Label} {editableField.Label.ToLowerInvariant()} to {normalizedValue}.",
            new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile),
            SvStaticEncountersWorkflowService.CreateRecordId(encounter.EncounterIndex),
            normalizedField,
            normalizedValue);
    }

    private static void ValidatePendingEdit(
        SvStaticEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.StaticEncountersDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Static Encounters.",
                expected: SvEditSessionSupport.StaticEncountersDomain));
            return;
        }

        if (!SvStaticEncountersWorkflowService.TryParseRecordId(edit.RecordId, out var encounterIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounter edit targets an invalid record id.",
                field: "encounterIndex",
                expected: "Existing Static Encounter record"));
            return;
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EncounterIndex == encounterIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounter edit targets a record that is not loaded.",
                field: "encounterIndex",
                expected: "Existing Static Encounter record"));
            return;
        }

        if (!encounter.SupportedFields.Contains(edit.Field ?? string.Empty, StringComparer.Ordinal)
            || !SvStaticEncountersWorkflowService.TryMapField(encounter.CategoryId, edit.Field ?? string.Empty, out _))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (editableField.IsReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Static Encounter edit targets read-only field '{editableField.Label}'.",
                field: edit.Field,
                expected: "Editable Static Encounter field"));
            return;
        }

        if (encounter.FieldReadOnly.TryGetValue(edit.Field ?? string.Empty, out var isFieldReadOnly) && isFieldReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Static Encounter edit targets field '{editableField.Label}', which is read-only for this encounter.",
                field: edit.Field,
                expected: "Editable Static Encounter field"));
            return;
        }

        _ = NormalizeValue(editableField, edit.NewValue ?? string.Empty, diagnostics);
    }

    private EditSession TranslateToPlacementSession(
        ProjectPaths paths,
        EditSession session,
        out IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var workflow = staticEncountersWorkflowService.Load(project);
        var placementEdits = new List<PendingEdit>();
        var translationDiagnostics = new List<ValidationDiagnostic>();

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, SvEditSessionSupport.StaticEncountersDomain, StringComparison.Ordinal)
                || !SvStaticEncountersWorkflowService.TryParseRecordId(edit.RecordId, out var encounterIndex))
            {
                translationDiagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Static Encounter edit is not valid for output.",
                    expected: "Valid Static Encounter edit"));
                continue;
            }

            var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EncounterIndex == encounterIndex);
            if (encounter is null)
            {
                translationDiagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Static Encounter edit targets a record that is not loaded.",
                    field: "encounterIndex",
                    expected: "Existing Static Encounter record"));
                continue;
            }

            if (!SvStaticEncountersWorkflowService.TryMapField(encounter.CategoryId, edit.Field ?? string.Empty, out var placementField))
            {
                translationDiagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                continue;
            }

            placementEdits.Add(new PendingEdit(
                SvEditSessionSupport.PlacementDomain,
                edit.Summary,
                [new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile)],
                encounter.ObjectId,
                placementField,
                edit.NewValue));
        }

        diagnostics = translationDiagnostics;
        return session with { PendingEdits = placementEdits };
    }

    private static SvStaticEncountersWorkflow OverlayPendingEdits(
        SvStaticEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SvStaticEncountersWorkflow OverlayPendingEdit(
        SvStaticEncountersWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.StaticEncountersDomain, StringComparison.Ordinal)
            || !SvStaticEncountersWorkflowService.TryParseRecordId(edit.RecordId, out var encounterIndex)
            || string.IsNullOrWhiteSpace(edit.Field)
            || edit.NewValue is null)
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
            Encounters = workflow.Encounters
                .Select(encounter => encounter.EncounterIndex == encounterIndex
                    ? OverlayEntry(encounter, edit.Field, edit.NewValue, editableField)
                    : encounter)
                .ToArray(),
        };
    }

    private static SvStaticEncounterEntry OverlayEntry(
        SvStaticEncounterEntry encounter,
        string field,
        string value,
        SvStaticEncounterEditableField editableField)
    {
        var fieldValues = new Dictionary<string, string>(encounter.FieldValues, StringComparer.Ordinal)
        {
            [field] = value,
        };
        var fieldDisplayValues = new Dictionary<string, string>(encounter.FieldDisplayValues, StringComparer.Ordinal)
        {
            [field] = FormatDisplayValue(value, editableField),
        };
        var fieldReadOnly = new Dictionary<string, bool>(encounter.FieldReadOnly, StringComparer.Ordinal);
        var parsed = int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            ? integer
            : 0;
        if (field == SvStaticEncountersWorkflowService.SpeciesField
            && fieldReadOnly.ContainsKey(SvStaticEncountersWorkflowService.AlcremieSweetField))
        {
            fieldReadOnly[SvStaticEncountersWorkflowService.AlcremieSweetField] = parsed != AlcremieSpeciesId;
        }

        return field switch
        {
            SvStaticEncountersWorkflowService.SpeciesField => encounter with
            {
                SpeciesId = parsed,
                Species = StripLeadingValue(fieldDisplayValues[field]),
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            SvStaticEncountersWorkflowService.FormField => encounter with
            {
                Form = parsed,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            SvStaticEncountersWorkflowService.LevelField => encounter with
            {
                Level = parsed,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            SvStaticEncountersWorkflowService.HeldItemIdField => encounter with
            {
                HeldItemId = parsed,
                HeldItem = parsed == 0 ? null : StripLeadingValue(fieldDisplayValues[field]),
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            SvStaticEncountersWorkflowService.AbilityField => encounter with
            {
                Ability = parsed,
                AbilityLabel = fieldDisplayValues[field],
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            SvStaticEncountersWorkflowService.NatureField => encounter with
            {
                Nature = parsed,
                NatureLabel = fieldDisplayValues[field],
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            SvStaticEncountersWorkflowService.GenderField => encounter with
            {
                Gender = parsed,
                GenderLabel = fieldDisplayValues[field],
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            SvStaticEncountersWorkflowService.ShinyLockField => encounter with
            {
                ShinyLock = parsed,
                ShinyLockLabel = fieldDisplayValues[field],
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            SvStaticEncountersWorkflowService.FlawlessIvCountField => encounter with
            {
                FlawlessIvCount = parsed,
                IvSummary = $"{parsed.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs",
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            _ when TryUpdateIvs(encounter.Ivs, field, parsed, out var ivs) => encounter with
            {
                Ivs = ivs,
                IvSummary = FormatIvSummary(ivs, encounter.FlawlessIvCount),
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            _ when TryUpdateMove(encounter.Moves, field, parsed, fieldDisplayValues[field], out var moves) => encounter with
            {
                Moves = moves,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
            _ => encounter with
            {
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
                FieldReadOnly = fieldReadOnly,
            },
        };
    }

    private static bool TryUpdateIvs(
        SvStaticEncounterStatsRecord current,
        string field,
        int value,
        out SvStaticEncounterStatsRecord updated)
    {
        updated = field switch
        {
            SvStaticEncountersWorkflowService.IvHpField => current with { HP = value },
            SvStaticEncountersWorkflowService.IvAttackField => current with { Attack = value },
            SvStaticEncountersWorkflowService.IvDefenseField => current with { Defense = value },
            SvStaticEncountersWorkflowService.IvSpecialAttackField => current with { SpecialAttack = value },
            SvStaticEncountersWorkflowService.IvSpecialDefenseField => current with { SpecialDefense = value },
            SvStaticEncountersWorkflowService.IvSpeedField => current with { Speed = value },
            _ => current,
        };

        return !ReferenceEquals(updated, current);
    }

    private static bool TryUpdateMove(
        IReadOnlyList<SvStaticEncounterMoveRecord> current,
        string field,
        int value,
        string displayValue,
        out IReadOnlyList<SvStaticEncounterMoveRecord> updated)
    {
        var slot = field switch
        {
            SvStaticEncountersWorkflowService.Move0Field => 0,
            SvStaticEncountersWorkflowService.Move1Field => 1,
            SvStaticEncountersWorkflowService.Move2Field => 2,
            SvStaticEncountersWorkflowService.Move3Field => 3,
            _ => -1,
        };

        if (slot < 0)
        {
            updated = current;
            return false;
        }

        updated = current
            .Select(move => move.Slot == slot
                ? move with { MoveId = value, Move = value == 0 ? null : StripLeadingValue(displayValue) }
                : move)
            .ToArray();
        return true;
    }

    private static string? NormalizeValue(
        SvStaticEncounterEditableField field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedValue = value.Trim();
        if (field.ValueKind == "integer" || field.ValueKind == "boolean")
        {
            var parsed = SvEditSessionSupport.TryParseInt(
                normalizedValue,
                field.MinimumValue,
                field.MaximumValue,
                field.Field,
                SvEditSessionSupport.StaticEncountersDomain,
                diagnostics);
            return parsed?.ToString(CultureInfo.InvariantCulture);
        }

        if (field.ValueKind == "number")
        {
            if (!double.TryParse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || field.MinimumValue is not null && parsed < field.MinimumValue.Value
                || field.MaximumValue is not null && parsed > field.MaximumValue.Value)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Static Encounter value must be a number between {field.MinimumValue} and {field.MaximumValue}.",
                    field: field.Field,
                    expected: "Safe numeric Static Encounter value"));
                return null;
            }

            return parsed.ToString("R", CultureInfo.InvariantCulture);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Static Encounter field '{field.Label}' is not editable.",
            field: field.Field,
            expected: "Numeric editable Static Encounter field"));
        return null;
    }

    private static string FormatDisplayValue(string value, SvStaticEncounterEditableField field)
    {
        if (field.Options.FirstOrDefault(option => option.Value.ToString(CultureInfo.InvariantCulture) == value) is { } option)
        {
            return option.Label;
        }

        return value;
    }

    private static string StripLeadingValue(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return separator > 0
            && int.TryParse(trimmed[..separator], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
            ? trimmed[(separator + 1)..]
            : trimmed;
    }

    private static string FormatIvSummary(SvStaticEncounterStatsRecord ivs, int? flawlessIvCount)
    {
        return flawlessIvCount is > 0
            ? $"{flawlessIvCount.Value.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"HP {ivs.HP} / Atk {ivs.Attack} / Def {ivs.Defense} / SpA {ivs.SpecialAttack} / SpD {ivs.SpecialDefense} / Spe {ivs.Speed}");
    }

    private static IReadOnlyList<ValidationDiagnostic> RemapDiagnostics(
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return diagnostics
            .Select(diagnostic => diagnostic.Domain == SvEditSessionSupport.PlacementDomain
                ? diagnostic with { Domain = SvEditSessionSupport.StaticEncountersDomain }
                : diagnostic)
            .ToArray();
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Static Encounter field '{field}' is not supported by Scarlet/Violet.",
            field: field,
            expected: "Supported Static Encounter field");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            severity,
            message,
            SvEditSessionSupport.StaticEncountersDomain,
            field,
            expected);
    }
}
