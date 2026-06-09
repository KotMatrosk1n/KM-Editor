// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Rentals;

public sealed class SwShRentalPokemonEditSessionService
{
    private const int MaximumPokemonEvTotal = 510;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShRentalPokemonWorkflowService rentalPokemonWorkflowService;

    public SwShRentalPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRentalPokemonWorkflowService? rentalPokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.rentalPokemonWorkflowService = rentalPokemonWorkflowService ?? new SwShRentalPokemonWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShRentalPokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int rentalIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = rentalPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditRentalPokemon(project, workflow, diagnostics))
        {
            return new SwShRentalPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var effectiveWorkflow = OverlayPendingEdits(workflow, currentSession.PendingEdits);
        var rental = effectiveWorkflow.Rentals.FirstOrDefault(candidate => candidate.RentalIndex == rentalIndex);
        if (rental is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon index {rentalIndex} is not present in the loaded workflow.",
                field: "rentalIndex",
                expected: "Existing Rental Pokemon record"));
            return new SwShRentalPokemonEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(rental, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShRentalPokemonEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingRentalEdit(currentSession, pendingEdit);

        return new SwShRentalPokemonEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = rentalPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditRentalPokemon(project, workflow, diagnostics);

        var effectiveWorkflow = workflow;
        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(effectiveWorkflow, edit, diagnostics);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Rental Pokemon change is valid."));
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
                "Create a pending Rental Pokemon edit before reviewing a change plan.",
                expected: "Pending Rental Pokemon edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShRentalPokemonWorkflowService.ResolveRentalPokemonDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon change plan could not resolve the source table.",
                expected: SwShRentalPokemonWorkflowService.RentalPokemonDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = SwShRentalPokemonWorkflowService.ResolveOutputPath(paths, source.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon apply target must stay inside the configured output root.",
                file: source.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var write = new PlannedFileWrite(
            source.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(source.GraphEntry), source.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Rental Pokemon edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Rental Pokemon edits.");

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
                expected: "Current reviewed Rental Pokemon change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShRentalPokemonWorkflowService.ResolveRentalPokemonDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon apply could not resolve the source table.",
                expected: SwShRentalPokemonWorkflowService.RentalPokemonDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var archive = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = session.PendingEdits
                .Select(edit => ToRentalEdit(edit, diagnostics))
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
                "Applied Rental Pokemon change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon source file could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield Rental Pokemon table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShRentalPokemonEntry rental,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SwShRentalPokemonWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = TryParseFieldValue(editableField, value, diagnostics, rental.Evs);
        if (parsedValue is null)
        {
            return null;
        }

        AddLinkedUsageWarning(normalizedField, diagnostics);

        return new PendingEdit(
            SwShRentalPokemonWorkflowService.RentalPokemonEditDomain,
            $"Set {rental.Label} {editableField.Label} to {parsedValue.Value}.",
            [new ProjectFileReference(rental.Provenance.SourceLayer, rental.Provenance.SourceFile)],
            RecordId: SwShRentalPokemonWorkflowService.CreateRentalRecordId(rental.RentalIndex),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShRentalPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShRentalPokemonWorkflowService.RentalPokemonEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Rental Pokemon workflow.",
                expected: SwShRentalPokemonWorkflowService.RentalPokemonEditDomain));
            return;
        }

        var editableField = SwShRentalPokemonWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShRentalPokemonWorkflowService.TryParseRentalRecordId(edit.RecordId, out var rentalIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Rental Pokemon edit targets an invalid record.",
                field: "rentalIndex",
                expected: "Rental Pokemon record"));
            return;
        }

        var rental = workflow.Rentals.FirstOrDefault(rental => rental.RentalIndex == rentalIndex);
        if (rental is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Rental Pokemon edit targets a record that is not loaded.",
                field: "rentalIndex",
                expected: "Existing Rental Pokemon record"));
            return;
        }

        TryParseFieldValue(editableField, edit.NewValue, diagnostics, rental.Evs);
        AddLinkedUsageWarning(edit.Field, diagnostics);
    }

    private static int? TryParseFieldValue(
        SwShRentalPokemonEditableField editableField,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics,
        SwShRentalPokemonStatsRecord? currentEvs = null)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be an integer value.",
                field: editableField.Field,
                expected: "Integer value"));
            return null;
        }

        if (IsIvField(editableField.Field))
        {
            parsedValue = ClampFixedIvValue(parsedValue);
        }

        if (IsEvField(editableField.Field))
        {
            parsedValue = NormalizeEvValue(editableField.Field, parsedValue, currentEvs);
        }

        if ((editableField.MinimumValue is not null && parsedValue < editableField.MinimumValue.Value)
            || (editableField.MaximumValue is not null && parsedValue > editableField.MaximumValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: "Supported Rental Pokemon field value"));
            return null;
        }

        return parsedValue;
    }

    private static int ClampFixedIvValue(int value)
    {
        return Math.Clamp(
            value,
            SwShRentalPokemonArchive.MinimumFixedIvValue,
            SwShRentalPokemonArchive.MaximumFixedIvValue);
    }

    private static int NormalizeEvValue(
        string field,
        int value,
        SwShRentalPokemonStatsRecord? currentEvs)
    {
        var clamped = ClampEvValue(value);
        if (currentEvs is null)
        {
            return clamped;
        }

        var remainingBudget = Math.Max(0, MaximumPokemonEvTotal - GetOtherEvTotal(currentEvs, field));
        return Math.Min(clamped, remainingBudget);
    }

    private static int GetOtherEvTotal(SwShRentalPokemonStatsRecord evs, string field)
    {
        return (field == SwShRentalPokemonWorkflowService.EvHpField ? 0 : ClampEvValue(evs.HP))
            + (field == SwShRentalPokemonWorkflowService.EvAttackField ? 0 : ClampEvValue(evs.Attack))
            + (field == SwShRentalPokemonWorkflowService.EvDefenseField ? 0 : ClampEvValue(evs.Defense))
            + (field == SwShRentalPokemonWorkflowService.EvSpecialAttackField ? 0 : ClampEvValue(evs.SpecialAttack))
            + (field == SwShRentalPokemonWorkflowService.EvSpecialDefenseField ? 0 : ClampEvValue(evs.SpecialDefense))
            + (field == SwShRentalPokemonWorkflowService.EvSpeedField ? 0 : ClampEvValue(evs.Speed));
    }

    private static int ClampEvValue(int value)
    {
        return Math.Clamp(value, 0, SwShRentalPokemonWorkflowService.MaximumPokemonEvValue);
    }

    private static bool IsIvField(string field)
    {
        return field is
            SwShRentalPokemonWorkflowService.IvHpField
            or SwShRentalPokemonWorkflowService.IvAttackField
            or SwShRentalPokemonWorkflowService.IvDefenseField
            or SwShRentalPokemonWorkflowService.IvSpeedField
            or SwShRentalPokemonWorkflowService.IvSpecialAttackField
            or SwShRentalPokemonWorkflowService.IvSpecialDefenseField
            or SwShRentalPokemonWorkflowService.FixedIvPresetField;
    }

    private static bool IsEvField(string field)
    {
        return field is
            SwShRentalPokemonWorkflowService.EvHpField
            or SwShRentalPokemonWorkflowService.EvAttackField
            or SwShRentalPokemonWorkflowService.EvDefenseField
            or SwShRentalPokemonWorkflowService.EvSpecialAttackField
            or SwShRentalPokemonWorkflowService.EvSpecialDefenseField
            or SwShRentalPokemonWorkflowService.EvSpeedField;
    }

    private static void AddLinkedUsageWarning(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShRentalPokemonWorkflowService.SpeciesField
            or SwShRentalPokemonWorkflowService.FormField
            or SwShRentalPokemonWorkflowService.Move0Field
            or SwShRentalPokemonWorkflowService.Move1Field
            or SwShRentalPokemonWorkflowService.Move2Field
            or SwShRentalPokemonWorkflowService.Move3Field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Rental Pokemon species, form, and move edits update the rental table only; review linked rental-team usage if the record is referenced by another workflow.",
                field: field,
                expected: "Review linked rental-team usage when changing Rental Pokemon identity or moves"));
        }
    }

    private static bool CanEditRentalPokemon(
        OpenedProject project,
        SwShRentalPokemonWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static EditSession ReplacePendingRentalEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameRentalEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameRentalEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShRentalPokemonWorkflow OverlayPendingEdits(
        SwShRentalPokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShRentalPokemonWorkflow OverlayPendingEdit(
        SwShRentalPokemonWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShRentalPokemonWorkflowService.RentalPokemonEditDomain, StringComparison.Ordinal)
            || !SwShRentalPokemonWorkflowService.IsEditableField(edit.Field)
            || !SwShRentalPokemonWorkflowService.TryParseRentalRecordId(edit.RecordId, out var rentalIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Rentals = workflow.Rentals
                .Select(rental => rental.RentalIndex == rentalIndex
                    ? OverlayRentalField(workflow, rental, edit.Field!, value)
                    : rental)
                .ToArray(),
        };
    }

    private static SwShRentalPokemonEntry OverlayRentalField(
        SwShRentalPokemonWorkflow workflow,
        SwShRentalPokemonEntry rental,
        string field,
        int value)
    {
        var updatedRental = field switch
        {
            SwShRentalPokemonWorkflowService.SpeciesField => rental with
            {
                SpeciesId = value,
                Species = GetOptionLabel(workflow, field, value, "Species"),
            },
            SwShRentalPokemonWorkflowService.FormField => rental with { Form = value },
            SwShRentalPokemonWorkflowService.LevelField => rental with { Level = value },
            SwShRentalPokemonWorkflowService.HeldItemIdField => rental with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShRentalPokemonWorkflowService.BallItemIdField => rental with
            {
                BallItemId = value,
                BallItem = GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShRentalPokemonWorkflowService.AbilityField => rental with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, field, value, "Ability slot"),
            },
            SwShRentalPokemonWorkflowService.NatureField => rental with
            {
                Nature = value,
                NatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            SwShRentalPokemonWorkflowService.GenderField => rental with
            {
                Gender = value,
                GenderLabel = GetOptionLabel(workflow, field, value, "Gender"),
            },
            SwShRentalPokemonWorkflowService.TrainerIdField => rental with { TrainerId = checked((uint)value) },
            SwShRentalPokemonWorkflowService.Move0Field => rental with { Moves = SetMove(workflow, rental.Moves, 0, value) },
            SwShRentalPokemonWorkflowService.Move1Field => rental with { Moves = SetMove(workflow, rental.Moves, 1, value) },
            SwShRentalPokemonWorkflowService.Move2Field => rental with { Moves = SetMove(workflow, rental.Moves, 2, value) },
            SwShRentalPokemonWorkflowService.Move3Field => rental with { Moves = SetMove(workflow, rental.Moves, 3, value) },
            SwShRentalPokemonWorkflowService.EvHpField => rental with { Evs = rental.Evs with { HP = value } },
            SwShRentalPokemonWorkflowService.EvAttackField => rental with { Evs = rental.Evs with { Attack = value } },
            SwShRentalPokemonWorkflowService.EvDefenseField => rental with { Evs = rental.Evs with { Defense = value } },
            SwShRentalPokemonWorkflowService.EvSpeedField => rental with { Evs = rental.Evs with { Speed = value } },
            SwShRentalPokemonWorkflowService.EvSpecialAttackField => rental with { Evs = rental.Evs with { SpecialAttack = value } },
            SwShRentalPokemonWorkflowService.EvSpecialDefenseField => rental with { Evs = rental.Evs with { SpecialDefense = value } },
            SwShRentalPokemonWorkflowService.IvHpField => rental with { Ivs = rental.Ivs with { HP = value } },
            SwShRentalPokemonWorkflowService.IvAttackField => rental with { Ivs = rental.Ivs with { Attack = value } },
            SwShRentalPokemonWorkflowService.IvDefenseField => rental with { Ivs = rental.Ivs with { Defense = value } },
            SwShRentalPokemonWorkflowService.IvSpeedField => rental with { Ivs = rental.Ivs with { Speed = value } },
            SwShRentalPokemonWorkflowService.IvSpecialAttackField => rental with { Ivs = rental.Ivs with { SpecialAttack = value } },
            SwShRentalPokemonWorkflowService.IvSpecialDefenseField => rental with { Ivs = rental.Ivs with { SpecialDefense = value } },
            SwShRentalPokemonWorkflowService.FixedIvPresetField => rental with { Ivs = CreateFixedIvPreset(value) },
            _ => rental,
        };

        updatedRental = updatedRental with
        {
            HasPerfectIvs = ArePerfectIvs(updatedRental.Ivs),
            IvSummary = SwShRentalPokemonWorkflowService.FormatIvSummary(updatedRental.Ivs),
            Label = FormatRentalLabel(updatedRental),
        };

        return updatedRental;
    }

    private static IReadOnlyList<SwShRentalPokemonMoveRecord> SetMove(
        SwShRentalPokemonWorkflow workflow,
        IReadOnlyList<SwShRentalPokemonMoveRecord> moves,
        int slot,
        int value)
    {
        return moves
            .Select(move => move.Slot == slot
                ? move with
                {
                    MoveId = value,
                    Move = value == 0
                        ? null
                        : GetOptionLabel(workflow, GetMoveField(slot), value, "Move"),
                }
                : move)
            .ToArray();
    }

    private static string GetMoveField(int slot)
    {
        return slot switch
        {
            0 => SwShRentalPokemonWorkflowService.Move0Field,
            1 => SwShRentalPokemonWorkflowService.Move1Field,
            2 => SwShRentalPokemonWorkflowService.Move2Field,
            3 => SwShRentalPokemonWorkflowService.Move3Field,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    private static string FormatRentalLabel(SwShRentalPokemonEntry rental)
    {
        return SwShRentalPokemonWorkflowService.FormatRentalLabel(
            rental.RentalIndex,
            rental.Species,
            rental.SpeciesId,
            rental.Form,
            rental.Level,
            rental.Moves);
    }

    private static SwShRentalPokemonStatsRecord CreateFixedIvPreset(int fixedValue)
    {
        return new SwShRentalPokemonStatsRecord(fixedValue, fixedValue, fixedValue, fixedValue, fixedValue, fixedValue);
    }

    private static bool ArePerfectIvs(SwShRentalPokemonStatsRecord ivs)
    {
        return ivs.HP == SwShRentalPokemonArchive.MaximumFixedIvValue
            && ivs.Attack == SwShRentalPokemonArchive.MaximumFixedIvValue
            && ivs.Defense == SwShRentalPokemonArchive.MaximumFixedIvValue
            && ivs.Speed == SwShRentalPokemonArchive.MaximumFixedIvValue
            && ivs.SpecialAttack == SwShRentalPokemonArchive.MaximumFixedIvValue
            && ivs.SpecialDefense == SwShRentalPokemonArchive.MaximumFixedIvValue;
    }

    private static string GetOptionLabel(
        SwShRentalPokemonWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];

        return SwShRentalPokemonWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static SwShRentalPokemonEdit? ToRentalEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShRentalPokemonWorkflowService.TryParseRentalRecordId(edit.RecordId, out var rentalIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Rental Pokemon edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Rental Pokemon edit"));
            return null;
        }

        return new SwShRentalPokemonEdit(rentalIndex, field, value);
    }

    private static SwShRentalPokemonField? MapField(string? field)
    {
        return field switch
        {
            SwShRentalPokemonWorkflowService.SpeciesField => SwShRentalPokemonField.Species,
            SwShRentalPokemonWorkflowService.FormField => SwShRentalPokemonField.Form,
            SwShRentalPokemonWorkflowService.LevelField => SwShRentalPokemonField.Level,
            SwShRentalPokemonWorkflowService.HeldItemIdField => SwShRentalPokemonField.HeldItem,
            SwShRentalPokemonWorkflowService.BallItemIdField => SwShRentalPokemonField.BallItemId,
            SwShRentalPokemonWorkflowService.AbilityField => SwShRentalPokemonField.Ability,
            SwShRentalPokemonWorkflowService.NatureField => SwShRentalPokemonField.Nature,
            SwShRentalPokemonWorkflowService.GenderField => SwShRentalPokemonField.Gender,
            SwShRentalPokemonWorkflowService.TrainerIdField => SwShRentalPokemonField.TrainerId,
            SwShRentalPokemonWorkflowService.Move0Field => SwShRentalPokemonField.Move0,
            SwShRentalPokemonWorkflowService.Move1Field => SwShRentalPokemonField.Move1,
            SwShRentalPokemonWorkflowService.Move2Field => SwShRentalPokemonField.Move2,
            SwShRentalPokemonWorkflowService.Move3Field => SwShRentalPokemonField.Move3,
            SwShRentalPokemonWorkflowService.EvHpField => SwShRentalPokemonField.EvHp,
            SwShRentalPokemonWorkflowService.EvAttackField => SwShRentalPokemonField.EvAttack,
            SwShRentalPokemonWorkflowService.EvDefenseField => SwShRentalPokemonField.EvDefense,
            SwShRentalPokemonWorkflowService.EvSpeedField => SwShRentalPokemonField.EvSpeed,
            SwShRentalPokemonWorkflowService.EvSpecialAttackField => SwShRentalPokemonField.EvSpecialAttack,
            SwShRentalPokemonWorkflowService.EvSpecialDefenseField => SwShRentalPokemonField.EvSpecialDefense,
            SwShRentalPokemonWorkflowService.IvHpField => SwShRentalPokemonField.IvHp,
            SwShRentalPokemonWorkflowService.IvAttackField => SwShRentalPokemonField.IvAttack,
            SwShRentalPokemonWorkflowService.IvDefenseField => SwShRentalPokemonField.IvDefense,
            SwShRentalPokemonWorkflowService.IvSpeedField => SwShRentalPokemonField.IvSpeed,
            SwShRentalPokemonWorkflowService.IvSpecialAttackField => SwShRentalPokemonField.IvSpecialAttack,
            SwShRentalPokemonWorkflowService.IvSpecialDefenseField => SwShRentalPokemonField.IvSpecialDefense,
            SwShRentalPokemonWorkflowService.FixedIvPresetField => SwShRentalPokemonField.FixedIvPreset,
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
                "Rental Pokemon apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShRentalPokemonWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon apply target must stay inside the configured output root.",
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
            "Rental Pokemon IV values must be fixed values from 0 through 31.",
            field: field,
            expected: "Supported rental IV value");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Rental Pokemon field '{field}' is not supported by the workflow yet.",
            field: "field",
            expected: "Supported Rental Pokemon field");
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
            Domain: SwShRentalPokemonWorkflowService.RentalPokemonEditDomain,
            Field: field,
            Expected: expected);
    }
}
