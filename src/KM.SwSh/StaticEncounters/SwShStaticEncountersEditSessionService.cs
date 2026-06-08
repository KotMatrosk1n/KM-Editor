// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.StaticEncounters;

public sealed class SwShStaticEncountersEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShStaticEncountersWorkflowService staticEncountersWorkflowService;

    public SwShStaticEncountersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShStaticEncountersWorkflowService? staticEncountersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.staticEncountersWorkflowService = staticEncountersWorkflowService ?? new SwShStaticEncountersWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShStaticEncountersEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int encounterIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = staticEncountersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditStaticEncounters(project, workflow, diagnostics))
        {
            return new SwShStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EncounterIndex == encounterIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static encounter index {encounterIndex} is not present in the loaded workflow.",
                field: "encounterIndex",
                expected: "Existing static encounter record"));
            return new SwShStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(encounter, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingStaticEncounterEdit(currentSession, pendingEdit);

        return new SwShStaticEncountersEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = staticEncountersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditStaticEncounters(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Static Encounter change is valid."));
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
                "Create a pending Static Encounter edit before reviewing a change plan.",
                expected: "Pending static encounter edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShStaticEncountersWorkflowService.ResolveStaticEncounterDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter change plan could not resolve the source table.",
                expected: SwShStaticEncountersWorkflowService.StaticEncounterDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = SwShStaticEncountersWorkflowService.ResolveOutputPath(paths, source.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter apply target must stay inside the configured output root.",
                file: source.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var write = new PlannedFileWrite(
            source.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(source.GraphEntry), source.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Static Encounter edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Static Encounter edits.");

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
                expected: "Current reviewed Static Encounter change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShStaticEncountersWorkflowService.ResolveStaticEncounterDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter apply could not resolve the source table.",
                expected: SwShStaticEncountersWorkflowService.StaticEncounterDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var archive = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = session.PendingEdits
                .Select(edit => ToStaticEncounterEdit(edit, diagnostics))
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
                "Applied Static Encounter change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter source file could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield static encounter table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShStaticEncounterEntry encounter,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SwShStaticEncountersWorkflowService.GetEditableField(normalizedField);
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

        AddAdvancedFieldWarnings(normalizedField, diagnostics);

        return new PendingEdit(
            SwShStaticEncountersWorkflowService.StaticEncountersEditDomain,
            $"Set {encounter.Label} {editableField.Label} to {parsedValue.Value}.",
            [new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile)],
            RecordId: SwShStaticEncountersWorkflowService.CreateEncounterRecordId(encounter.EncounterIndex),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShStaticEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShStaticEncountersWorkflowService.StaticEncountersEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Static Encounters workflow.",
                expected: SwShStaticEncountersWorkflowService.StaticEncountersEditDomain));
            return;
        }

        var editableField = SwShStaticEncountersWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var encounterIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounter edit targets an invalid record.",
                field: "encounterIndex",
                expected: "Static encounter record"));
            return;
        }

        if (workflow.Encounters.All(encounter => encounter.EncounterIndex != encounterIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounter edit targets a record that is not loaded.",
                field: "encounterIndex",
                expected: "Existing static encounter record"));
            return;
        }

        TryParseFieldValue(editableField, edit.NewValue, diagnostics);
        AddAdvancedFieldWarnings(edit.Field, diagnostics);
    }

    private static int? TryParseFieldValue(
        SwShStaticEncounterEditableField editableField,
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

        if (editableField.Field == SwShStaticEncountersWorkflowService.FlawlessIvCountField
            && parsedValue is not 0 and not 3 and not 6)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter IV preset must be 0, 3, or 6.",
                field: editableField.Field,
                expected: "Supported IV preset"));
            return null;
        }

        if (editableField.Field == SwShStaticEncountersWorkflowService.IvHpField
            && !IsValidHpIvValue(parsedValue))
        {
            diagnostics.Add(CreateIvDiagnostic(editableField.Field));
            return null;
        }

        if (IsNonHpIvField(editableField.Field) && !IsValidIvValue(parsedValue))
        {
            diagnostics.Add(CreateIvDiagnostic(editableField.Field));
            return null;
        }

        if ((editableField.MinimumValue is not null && parsedValue < editableField.MinimumValue.Value)
            || (editableField.MaximumValue is not null && parsedValue > editableField.MaximumValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: "Supported Static Encounter field value"));
            return null;
        }

        return parsedValue;
    }

    private static bool IsValidHpIvValue(int value)
    {
        return value == SwShStaticEncounterArchive.ThreePerfectIvSentinel || IsValidIvValue(value);
    }

    private static bool IsValidIvValue(int value)
    {
        return value == SwShStaticEncounterArchive.RandomIvValue
            || value is >= SwShStaticEncounterArchive.MinimumFixedIvValue and <= SwShStaticEncounterArchive.MaximumFixedIvValue;
    }

    private static bool IsNonHpIvField(string field)
    {
        return field is
            SwShStaticEncountersWorkflowService.IvAttackField
            or SwShStaticEncountersWorkflowService.IvDefenseField
            or SwShStaticEncountersWorkflowService.IvSpeedField
            or SwShStaticEncountersWorkflowService.IvSpecialAttackField
            or SwShStaticEncountersWorkflowService.IvSpecialDefenseField;
    }

    private static void AddAdvancedFieldWarnings(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShStaticEncountersWorkflowService.SpeciesField or SwShStaticEncountersWorkflowService.FormField)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Species and form edits update the static encounter table only; visible placement, model, or script references may need a separate review.",
                field: field,
                expected: "Review linked placement assets when changing visible static Pokemon"));
        }

        if (field == SwShStaticEncountersWorkflowService.EncounterScenarioField)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Scenario edits should stay aligned with encounters designed to use that story or battle rule.",
                field: field,
                expected: "Use a scenario compatible with the encounter setup"));
        }
    }

    private static bool CanEditStaticEncounters(
        OpenedProject project,
        SwShStaticEncountersWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static EditSession ReplacePendingStaticEncounterEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameStaticEncounterEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameStaticEncounterEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShStaticEncountersWorkflow OverlayPendingEdits(
        SwShStaticEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShStaticEncountersWorkflow OverlayPendingEdit(
        SwShStaticEncountersWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShStaticEncountersWorkflowService.StaticEncountersEditDomain, StringComparison.Ordinal)
            || !SwShStaticEncountersWorkflowService.IsEditableField(edit.Field)
            || !SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var encounterIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Encounters = workflow.Encounters
                .Select(encounter => encounter.EncounterIndex == encounterIndex
                    ? OverlayEncounterField(workflow, encounter, edit.Field!, value)
                    : encounter)
                .ToArray(),
        };
    }

    private static SwShStaticEncounterEntry OverlayEncounterField(
        SwShStaticEncountersWorkflow workflow,
        SwShStaticEncounterEntry encounter,
        string field,
        int value)
    {
        var updatedEncounter = field switch
        {
            SwShStaticEncountersWorkflowService.SpeciesField => encounter with
            {
                SpeciesId = value,
                Species = GetOptionLabel(workflow, field, value, "Species"),
            },
            SwShStaticEncountersWorkflowService.FormField => encounter with { Form = value },
            SwShStaticEncountersWorkflowService.LevelField => encounter with { Level = value },
            SwShStaticEncountersWorkflowService.HeldItemIdField => encounter with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShStaticEncountersWorkflowService.AbilityField => encounter with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, field, value, "Ability slot"),
            },
            SwShStaticEncountersWorkflowService.NatureField => encounter with
            {
                Nature = value,
                NatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            SwShStaticEncountersWorkflowService.GenderField => encounter with
            {
                Gender = value,
                GenderLabel = GetOptionLabel(workflow, field, value, "Gender"),
            },
            SwShStaticEncountersWorkflowService.ShinyLockField => encounter with
            {
                ShinyLock = value,
                ShinyLockLabel = GetOptionLabel(workflow, field, value, "Shiny lock"),
            },
            SwShStaticEncountersWorkflowService.EncounterScenarioField => encounter with
            {
                EncounterScenario = value,
                EncounterScenarioLabel = GetOptionLabel(workflow, field, value, "Scenario"),
            },
            SwShStaticEncountersWorkflowService.DynamaxLevelField => encounter with { DynamaxLevel = value },
            SwShStaticEncountersWorkflowService.CanGigantamaxField => encounter with { CanGigantamax = value != 0 },
            SwShStaticEncountersWorkflowService.Move0Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 0, value) },
            SwShStaticEncountersWorkflowService.Move1Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 1, value) },
            SwShStaticEncountersWorkflowService.Move2Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 2, value) },
            SwShStaticEncountersWorkflowService.Move3Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 3, value) },
            SwShStaticEncountersWorkflowService.EvHpField => encounter with { Evs = encounter.Evs with { HP = value } },
            SwShStaticEncountersWorkflowService.EvAttackField => encounter with { Evs = encounter.Evs with { Attack = value } },
            SwShStaticEncountersWorkflowService.EvDefenseField => encounter with { Evs = encounter.Evs with { Defense = value } },
            SwShStaticEncountersWorkflowService.EvSpeedField => encounter with { Evs = encounter.Evs with { Speed = value } },
            SwShStaticEncountersWorkflowService.EvSpecialAttackField => encounter with { Evs = encounter.Evs with { SpecialAttack = value } },
            SwShStaticEncountersWorkflowService.EvSpecialDefenseField => encounter with { Evs = encounter.Evs with { SpecialDefense = value } },
            SwShStaticEncountersWorkflowService.IvHpField => encounter with { Ivs = encounter.Ivs with { HP = value } },
            SwShStaticEncountersWorkflowService.IvAttackField => encounter with { Ivs = encounter.Ivs with { Attack = value } },
            SwShStaticEncountersWorkflowService.IvDefenseField => encounter with { Ivs = encounter.Ivs with { Defense = value } },
            SwShStaticEncountersWorkflowService.IvSpeedField => encounter with { Ivs = encounter.Ivs with { Speed = value } },
            SwShStaticEncountersWorkflowService.IvSpecialAttackField => encounter with { Ivs = encounter.Ivs with { SpecialAttack = value } },
            SwShStaticEncountersWorkflowService.IvSpecialDefenseField => encounter with { Ivs = encounter.Ivs with { SpecialDefense = value } },
            SwShStaticEncountersWorkflowService.FlawlessIvCountField => encounter with { Ivs = CreateIvPreset(value) },
            _ => encounter,
        };

        var flawlessIvCount = GetFlawlessIvCount(updatedEncounter.Ivs);
        updatedEncounter = updatedEncounter with
        {
            FlawlessIvCount = flawlessIvCount,
            IvSummary = SwShStaticEncountersWorkflowService.FormatIvSummary(updatedEncounter.Ivs, flawlessIvCount),
            Label = SwShStaticEncountersWorkflowService.FormatEncounterLabel(
                updatedEncounter.EncounterIndex,
                updatedEncounter.Species,
                updatedEncounter.SpeciesId,
                updatedEncounter.Form,
                updatedEncounter.Level,
                updatedEncounter.EncounterScenarioLabel,
                updatedEncounter.Moves),
        };

        return updatedEncounter;
    }

    private static IReadOnlyList<SwShStaticEncounterMoveRecord> SetMove(
        SwShStaticEncountersWorkflow workflow,
        IReadOnlyList<SwShStaticEncounterMoveRecord> moves,
        int slot,
        int moveId)
    {
        return moves
            .Select(move => move.Slot == slot
                ? move with
                {
                    MoveId = moveId,
                    Move = moveId == 0 ? null : GetOptionLabel(workflow, GetMoveField(slot), moveId, "Move"),
                }
                : move)
            .ToArray();
    }

    private static string GetMoveField(int slot)
    {
        return slot switch
        {
            0 => SwShStaticEncountersWorkflowService.Move0Field,
            1 => SwShStaticEncountersWorkflowService.Move1Field,
            2 => SwShStaticEncountersWorkflowService.Move2Field,
            3 => SwShStaticEncountersWorkflowService.Move3Field,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    private static SwShStaticEncounterStatsRecord CreateIvPreset(int flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => new SwShStaticEncounterStatsRecord(-1, -1, -1, -1, -1, -1),
            3 => new SwShStaticEncounterStatsRecord(-4, -1, -1, -1, -1, -1),
            6 => new SwShStaticEncounterStatsRecord(31, 31, 31, 31, 31, 31),
            _ => throw new ArgumentOutOfRangeException(nameof(flawlessIvCount)),
        };
    }

    private static int? GetFlawlessIvCount(SwShStaticEncounterStatsRecord ivs)
    {
        return SwShStaticEncounterArchive.GetFlawlessIvCount(
            new SwShStaticEncounterStats(
                ivs.HP,
                ivs.Attack,
                ivs.Defense,
                ivs.SpecialAttack,
                ivs.SpecialDefense,
                ivs.Speed));
    }

    private static string GetOptionLabel(
        SwShStaticEncountersWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];

        return SwShStaticEncountersWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static SwShStaticEncounterEdit? ToStaticEncounterEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var encounterIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounter edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Static Encounter edit"));
            return null;
        }

        return new SwShStaticEncounterEdit(encounterIndex, field, value);
    }

    private static SwShStaticEncounterField? MapField(string? field)
    {
        return field switch
        {
            SwShStaticEncountersWorkflowService.SpeciesField => SwShStaticEncounterField.Species,
            SwShStaticEncountersWorkflowService.FormField => SwShStaticEncounterField.Form,
            SwShStaticEncountersWorkflowService.LevelField => SwShStaticEncounterField.Level,
            SwShStaticEncountersWorkflowService.HeldItemIdField => SwShStaticEncounterField.HeldItem,
            SwShStaticEncountersWorkflowService.AbilityField => SwShStaticEncounterField.Ability,
            SwShStaticEncountersWorkflowService.NatureField => SwShStaticEncounterField.Nature,
            SwShStaticEncountersWorkflowService.GenderField => SwShStaticEncounterField.Gender,
            SwShStaticEncountersWorkflowService.ShinyLockField => SwShStaticEncounterField.ShinyLock,
            SwShStaticEncountersWorkflowService.EncounterScenarioField => SwShStaticEncounterField.EncounterScenario,
            SwShStaticEncountersWorkflowService.DynamaxLevelField => SwShStaticEncounterField.DynamaxLevel,
            SwShStaticEncountersWorkflowService.CanGigantamaxField => SwShStaticEncounterField.CanGigantamax,
            SwShStaticEncountersWorkflowService.Move0Field => SwShStaticEncounterField.Move0,
            SwShStaticEncountersWorkflowService.Move1Field => SwShStaticEncounterField.Move1,
            SwShStaticEncountersWorkflowService.Move2Field => SwShStaticEncounterField.Move2,
            SwShStaticEncountersWorkflowService.Move3Field => SwShStaticEncounterField.Move3,
            SwShStaticEncountersWorkflowService.EvHpField => SwShStaticEncounterField.EvHp,
            SwShStaticEncountersWorkflowService.EvAttackField => SwShStaticEncounterField.EvAttack,
            SwShStaticEncountersWorkflowService.EvDefenseField => SwShStaticEncounterField.EvDefense,
            SwShStaticEncountersWorkflowService.EvSpeedField => SwShStaticEncounterField.EvSpeed,
            SwShStaticEncountersWorkflowService.EvSpecialAttackField => SwShStaticEncounterField.EvSpecialAttack,
            SwShStaticEncountersWorkflowService.EvSpecialDefenseField => SwShStaticEncounterField.EvSpecialDefense,
            SwShStaticEncountersWorkflowService.IvHpField => SwShStaticEncounterField.IvHp,
            SwShStaticEncountersWorkflowService.IvAttackField => SwShStaticEncounterField.IvAttack,
            SwShStaticEncountersWorkflowService.IvDefenseField => SwShStaticEncounterField.IvDefense,
            SwShStaticEncountersWorkflowService.IvSpeedField => SwShStaticEncounterField.IvSpeed,
            SwShStaticEncountersWorkflowService.IvSpecialAttackField => SwShStaticEncounterField.IvSpecialAttack,
            SwShStaticEncountersWorkflowService.IvSpecialDefenseField => SwShStaticEncounterField.IvSpecialDefense,
            SwShStaticEncountersWorkflowService.FlawlessIvCountField => SwShStaticEncounterField.FlawlessIvCount,
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
                "Static Encounter apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShStaticEncountersWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter apply target must stay inside the configured output root.",
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

    private static ValidationDiagnostic CreateIvDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Static Encounter IV values must be -1 for random or 0-31 for fixed values; HP IV also accepts -4 for the 3-perfect sentinel.",
            field: field,
            expected: "Supported static encounter IV value");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Static Encounter field '{field}' is not supported by the workflow yet.",
            field: "field",
            expected: "Supported Static Encounter field");
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
            Domain: SwShStaticEncountersWorkflowService.StaticEncountersEditDomain,
            Field: field,
            Expected: expected);
    }
}
