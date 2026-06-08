// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShDynamaxAdventuresWorkflowService dynamaxAdventuresWorkflowService;

    public SwShDynamaxAdventuresEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShDynamaxAdventuresWorkflowService? dynamaxAdventuresWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.dynamaxAdventuresWorkflowService = dynamaxAdventuresWorkflowService ?? new SwShDynamaxAdventuresWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShDynamaxAdventuresEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int entryIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditDynamaxAdventures(project, workflow, diagnostics))
        {
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EntryIndex == entryIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventure entry index {entryIndex} is not present in the loaded workflow.",
                field: "entryIndex",
                expected: "Existing Dynamax Adventure record"));
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(encounter, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingEncounterEdit(currentSession, pendingEdit);

        return new SwShDynamaxAdventuresEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditDynamaxAdventures(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Dynamax Adventures change is valid."));
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
                "Create a pending Dynamax Adventures edit before reviewing a change plan.",
                expected: "Pending Dynamax Adventures edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShDynamaxAdventuresWorkflowService.ResolveDynamaxAdventureDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures change plan could not resolve the source table.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(paths, source.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures apply target must stay inside the configured output root.",
                file: source.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var write = new PlannedFileWrite(
            source.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(source.GraphEntry), source.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Dynamax Adventures edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Dynamax Adventures edits.");

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
                expected: "Current reviewed Dynamax Adventures change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShDynamaxAdventuresWorkflowService.ResolveDynamaxAdventureDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures apply could not resolve the source table.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var archive = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = session.PendingEdits
                .Select(edit => ToDynamaxAdventureEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = archive.WriteEdits(edits);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, source.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Dynamax Adventures change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures source file could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield Dynamax Adventures table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShDynamaxAdventureEntry encounter,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SwShDynamaxAdventuresWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = TryParseFieldValue(editableField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        AddLinkedUsageWarning(normalizedField, diagnostics);

        return new PendingEdit(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            $"Set {encounter.Label} {editableField.Label} to {parsedValue.Value}.",
            [new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile)],
            RecordId: SwShDynamaxAdventuresWorkflowService.CreateEncounterRecordId(encounter.EntryIndex),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShDynamaxAdventuresWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Dynamax Adventures workflow.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain));
            return;
        }

        var editableField = SwShDynamaxAdventuresWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit targets an invalid record.",
                field: "entryIndex",
                expected: "Dynamax Adventure record"));
            return;
        }

        if (workflow.Encounters.All(encounter => encounter.EntryIndex != entryIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit targets a record that is not loaded.",
                field: "entryIndex",
                expected: "Existing Dynamax Adventure record"));
            return;
        }

        TryParseFieldValue(editableField, edit.NewValue, diagnostics);
        AddLinkedUsageWarning(edit.Field, diagnostics);
    }

    private static int? TryParseFieldValue(
        SwShDynamaxAdventureEditableField editableField,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be an integer value.",
                field: editableField.Field,
                expected: "Integer value"));
            return null;
        }

        if ((editableField.MinimumValue is not null && parsedValue < editableField.MinimumValue.Value)
            || (editableField.MaximumValue is not null && parsedValue > editableField.MaximumValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: "Supported Dynamax Adventures field value"));
            return null;
        }

        if (editableField.Field == SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField
            && parsedValue == 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Guaranteed perfect IVs supports Random IVs or 2-6 guaranteed perfect IVs; 1 is not representable in this table.",
                field: editableField.Field,
                expected: "0, 2, 3, 4, 5, or 6"));
            return null;
        }

        return parsedValue;
    }

    private static void AddLinkedUsageWarning(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShDynamaxAdventuresWorkflowService.SpeciesField
            or SwShDynamaxAdventuresWorkflowService.FormField
            or SwShDynamaxAdventuresWorkflowService.Move0Field
            or SwShDynamaxAdventuresWorkflowService.Move1Field
            or SwShDynamaxAdventuresWorkflowService.Move2Field
            or SwShDynamaxAdventuresWorkflowService.Move3Field
            or SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Dynamax Adventures identity, move, and single-capture edits update the encounter table only; review linked story, save-flag, and UI references before shipping large changes.",
                field: field,
                expected: "Review linked story/save/UI usage for Adventure identity edits"));
        }
    }

    private static bool CanEditDynamaxAdventures(
        OpenedProject project,
        SwShDynamaxAdventuresWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static EditSession ReplacePendingEncounterEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameEncounterEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameEncounterEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShDynamaxAdventuresWorkflow OverlayPendingEdits(
        SwShDynamaxAdventuresWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShDynamaxAdventuresWorkflow OverlayPendingEdit(
        SwShDynamaxAdventuresWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal)
            || !SwShDynamaxAdventuresWorkflowService.IsEditableField(edit.Field)
            || !SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Encounters = workflow.Encounters
                .Select(encounter => encounter.EntryIndex == entryIndex
                    ? OverlayEncounterField(workflow, encounter, edit.Field!, value)
                    : encounter)
                .ToArray(),
        };
    }

    private static SwShDynamaxAdventureEntry OverlayEncounterField(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        string field,
        int value)
    {
        var updatedEncounter = field switch
        {
            SwShDynamaxAdventuresWorkflowService.SpeciesField => encounter with
            {
                SpeciesId = value,
                Species = GetOptionLabel(workflow, field, value, "Species"),
            },
            SwShDynamaxAdventuresWorkflowService.FormField => encounter with { Form = value },
            SwShDynamaxAdventuresWorkflowService.LevelField => encounter with { Level = value },
            SwShDynamaxAdventuresWorkflowService.BallItemIdField => encounter with
            {
                BallItemId = value,
                BallItem = GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShDynamaxAdventuresWorkflowService.AbilityField => encounter with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, field, value, "Ability roll"),
            },
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField => encounter with
            {
                GigantamaxState = value,
                GigantamaxLabel = GetOptionLabel(workflow, field, value, "Gigantamax"),
            },
            SwShDynamaxAdventuresWorkflowService.VersionField => encounter with
            {
                Version = value,
                VersionLabel = GetOptionLabel(workflow, field, value, "Version"),
            },
            SwShDynamaxAdventuresWorkflowService.ShinyRollField => encounter with
            {
                ShinyRoll = value,
                ShinyRollLabel = GetOptionLabel(workflow, field, value, "Shiny roll"),
            },
            SwShDynamaxAdventuresWorkflowService.Move0Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 0, value) },
            SwShDynamaxAdventuresWorkflowService.Move1Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 1, value) },
            SwShDynamaxAdventuresWorkflowService.Move2Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 2, value) },
            SwShDynamaxAdventuresWorkflowService.Move3Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 3, value) },
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField => encounter with
            {
                GuaranteedPerfectIvs = value,
                Ivs = encounter.Ivs with { Hp = value == 0 ? SwShDynamaxAdventureArchive.RandomIvValue : -value },
            },
            SwShDynamaxAdventuresWorkflowService.IvAttackField => encounter with { Ivs = encounter.Ivs with { Attack = value } },
            SwShDynamaxAdventuresWorkflowService.IvDefenseField => encounter with { Ivs = encounter.Ivs with { Defense = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpeedField => encounter with { Ivs = encounter.Ivs with { Speed = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpecialAttackField => encounter with { Ivs = encounter.Ivs with { SpecialAttack = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpecialDefenseField => encounter with { Ivs = encounter.Ivs with { SpecialDefense = value } },
            SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField => encounter with { IsSingleCapture = value != 0 },
            SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField => encounter with { IsStoryProgressGated = value != 0 },
            SwShDynamaxAdventuresWorkflowService.OtGenderField => encounter with
            {
                OtGender = value,
                OtGenderLabel = GetOptionLabel(workflow, field, value, "OT gender"),
            },
            _ => encounter,
        };

        updatedEncounter = updatedEncounter with
        {
            IvSummary = FormatIvSummary(updatedEncounter.Ivs, updatedEncounter.GuaranteedPerfectIvs),
            Label = FormatEncounterLabel(updatedEncounter),
        };

        return updatedEncounter;
    }

    private static IReadOnlyList<SwShDynamaxAdventureMoveRecord> SetMove(
        SwShDynamaxAdventuresWorkflow workflow,
        IReadOnlyList<SwShDynamaxAdventureMoveRecord> moves,
        int slot,
        int value)
    {
        return moves
            .Select(move => move.Slot == slot + 1
                ? move with
                {
                    MoveId = value,
                    Move = GetOptionLabel(workflow, GetMoveField(slot), value, "Move"),
                }
                : move)
            .ToArray();
    }

    private static string GetMoveField(int slot)
    {
        return slot switch
        {
            0 => SwShDynamaxAdventuresWorkflowService.Move0Field,
            1 => SwShDynamaxAdventuresWorkflowService.Move1Field,
            2 => SwShDynamaxAdventuresWorkflowService.Move2Field,
            3 => SwShDynamaxAdventuresWorkflowService.Move3Field,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    private static string FormatEncounterLabel(SwShDynamaxAdventureEntry encounter)
    {
        var speciesLabel = SwShSpeciesFormLabels.FormatSpeciesFormLabel(
            encounter.Species,
            encounter.SpeciesId,
            encounter.Form);
        var versionSuffix = string.Equals(encounter.VersionLabel, "Both", StringComparison.Ordinal)
            ? string.Empty
            : $" [{encounter.VersionLabel}]";
        return $"{encounter.EntryIndex.ToString("000", CultureInfo.InvariantCulture)} / {encounter.AdventureIndex.ToString("000", CultureInfo.InvariantCulture)} - {speciesLabel}{versionSuffix}";
    }

    private static string FormatIvSummary(SwShDynamaxAdventureIvsRecord ivs, int guaranteedPerfectIvs)
    {
        return string.Join(
            ", ",
            [
                guaranteedPerfectIvs > 0
                    ? $"{guaranteedPerfectIvs.ToString(CultureInfo.InvariantCulture)} guaranteed perfect"
                    : "random HP",
                $"Atk {FormatIvValue(ivs.Attack)}",
                $"Def {FormatIvValue(ivs.Defense)}",
                $"SpA {FormatIvValue(ivs.SpecialAttack)}",
                $"SpD {FormatIvValue(ivs.SpecialDefense)}",
                $"Spe {FormatIvValue(ivs.Speed)}",
            ]);
    }

    private static string FormatIvValue(int value)
    {
        return value == SwShDynamaxAdventureArchive.RandomIvValue
            ? "random"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetOptionLabel(
        SwShDynamaxAdventuresWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];

        return SwShDynamaxAdventuresWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static SwShDynamaxAdventureEdit? ToDynamaxAdventureEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Dynamax Adventures edit"));
            return null;
        }

        return new SwShDynamaxAdventureEdit(entryIndex, field, value);
    }

    private static SwShDynamaxAdventureField? MapField(string? field)
    {
        return field switch
        {
            SwShDynamaxAdventuresWorkflowService.SpeciesField => SwShDynamaxAdventureField.Species,
            SwShDynamaxAdventuresWorkflowService.FormField => SwShDynamaxAdventureField.Form,
            SwShDynamaxAdventuresWorkflowService.LevelField => SwShDynamaxAdventureField.Level,
            SwShDynamaxAdventuresWorkflowService.BallItemIdField => SwShDynamaxAdventureField.BallItemId,
            SwShDynamaxAdventuresWorkflowService.AbilityField => SwShDynamaxAdventureField.Ability,
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField => SwShDynamaxAdventureField.GigantamaxState,
            SwShDynamaxAdventuresWorkflowService.VersionField => SwShDynamaxAdventureField.Version,
            SwShDynamaxAdventuresWorkflowService.ShinyRollField => SwShDynamaxAdventureField.ShinyRoll,
            SwShDynamaxAdventuresWorkflowService.Move0Field => SwShDynamaxAdventureField.Move0,
            SwShDynamaxAdventuresWorkflowService.Move1Field => SwShDynamaxAdventureField.Move1,
            SwShDynamaxAdventuresWorkflowService.Move2Field => SwShDynamaxAdventureField.Move2,
            SwShDynamaxAdventuresWorkflowService.Move3Field => SwShDynamaxAdventureField.Move3,
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField => SwShDynamaxAdventureField.GuaranteedPerfectIvs,
            SwShDynamaxAdventuresWorkflowService.IvAttackField => SwShDynamaxAdventureField.IvAttack,
            SwShDynamaxAdventuresWorkflowService.IvDefenseField => SwShDynamaxAdventureField.IvDefense,
            SwShDynamaxAdventuresWorkflowService.IvSpeedField => SwShDynamaxAdventureField.IvSpeed,
            SwShDynamaxAdventuresWorkflowService.IvSpecialAttackField => SwShDynamaxAdventureField.IvSpecialAttack,
            SwShDynamaxAdventuresWorkflowService.IvSpecialDefenseField => SwShDynamaxAdventureField.IvSpecialDefense,
            SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField => SwShDynamaxAdventureField.IsSingleCapture,
            SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField => SwShDynamaxAdventureField.IsStoryProgressGated,
            SwShDynamaxAdventuresWorkflowService.OtGenderField => SwShDynamaxAdventureField.OtGender,
            _ => null,
        };
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
                "Dynamax Adventures apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures apply target must stay inside the configured output root.",
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

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Dynamax Adventures field '{field}' is not supported by the workflow yet.",
            field: "field",
            expected: "Supported Dynamax Adventures field");
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
            Domain: SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            Field: field,
            Expected: expected);
    }
}
