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

namespace KM.SwSh.Rentals;

public sealed class SwShRentalPokemonEditSessionService
{
    private const int MaximumPokemonEvTotal = 510;

    private static readonly IReadOnlyList<string> IndividualIvFields =
    [
        SwShRentalPokemonWorkflowService.IvHpField,
        SwShRentalPokemonWorkflowService.IvAttackField,
        SwShRentalPokemonWorkflowService.IvDefenseField,
        SwShRentalPokemonWorkflowService.IvSpeedField,
        SwShRentalPokemonWorkflowService.IvSpecialAttackField,
        SwShRentalPokemonWorkflowService.IvSpecialDefenseField,
    ];

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShRentalPokemonWorkflowService rentalPokemonWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShRentalPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRentalPokemonWorkflowService? rentalPokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.rentalPokemonWorkflowService = rentalPokemonWorkflowService ?? new SwShRentalPokemonWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShRentalPokemonEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRentalPokemonWorkflowService? rentalPokemonWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.rentalPokemonWorkflowService = rentalPokemonWorkflowService ?? new SwShRentalPokemonWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
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
        return UpdateFields(
            paths,
            session,
            [new SwShRentalPokemonFieldUpdate(rentalIndex, field, value)]);
    }

    public SwShRentalPokemonEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShRentalPokemonFieldUpdate?>? updates)
    {
        ArgumentNullException.ThrowIfNull(paths);

        projectWorkspaceService.ClearMemoryCache();
        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = rentalPokemonWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(workflow, originalSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditRentalPokemon(project, workflow, diagnostics))
        {
            return new SwShRentalPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (updates is null || updates.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Update at least one Rental Pokemon field.",
                field: "updates",
                expected: "One or more Rental Pokemon field updates"));
            return new SwShRentalPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var workingSession = originalSession;
        var effectiveWorkflow = originalWorkflow;
        foreach (var update in updates)
        {
            if (update is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Rental Pokemon field update is missing.",
                    field: "updates",
                    expected: "Rental Pokemon field update"));
                break;
            }

            var effectiveRental = ResolveRental(effectiveWorkflow, update.RentalIndex, diagnostics, update.Field);
            var sourceRental = ResolveRental(workflow, update.RentalIndex, diagnostics, update.Field);
            if (effectiveRental is null || sourceRental is null)
            {
                break;
            }

            var pendingEdit = CreatePendingEdit(
                project,
                workflow,
                sourceRental,
                effectiveRental,
                update.Field,
                update.Value,
                diagnostics);
            if (pendingEdit is null)
            {
                break;
            }

            workingSession = NormalizeIvEditsBeforeUpdate(
                project,
                workflow,
                workingSession,
                sourceRental,
                effectiveRental,
                pendingEdit.Field!,
                diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                break;
            }
            var sourceValue = GetRentalFieldValue(sourceRental, pendingEdit.Field!);
            var parsedValue = long.Parse(pendingEdit.NewValue!, CultureInfo.InvariantCulture);
            workingSession = sourceValue == parsedValue
                ? RemovePendingRentalField(workingSession, effectiveRental.RentalIndex, pendingEdit.Field!)
                : ReplacePendingRentalEdit(workingSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdits(workflow, workingSession.PendingEdits);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRentalPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        ValidateLoadedSession(project, workflow, workingSession, diagnostics, addSuccessDiagnostic: false);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRentalPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShRentalPokemonEditResult(
            OverlayPendingEdits(workflow, workingSession.PendingEdits),
            workingSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = rentalPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (CanEditRentalPokemon(project, workflow, diagnostics))
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

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = rentalPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        if (CanEditRentalPokemon(project, workflow, diagnostics))
        {
            ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);
        }

        var rentalEdits = GetRentalEdits(session).ToArray();
        if (rentalEdits.Length == 0)
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

        var source = SwShRentalPokemonWorkflowService.ResolveRentalPokemonDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon change plan could not resolve the source table.",
                expected: SwShRentalPokemonWorkflowService.RentalPokemonDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var write = new PlannedFileWrite(
            source.GraphEntry.RelativePath,
            rentalEdits
                .SelectMany(edit => GetPlanSources(project, workflow, edit))
                .Distinct()
                .OrderBy(reference => reference.Layer)
                .ThenBy(reference => reference.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            File.Exists(targetPath),
            CreatePlanReason(rentalEdits));

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
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Rental Pokemon change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));

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

        byte[] output;
        try
        {
            var archive = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = GetRentalEdits(session)
                .Select(edit => ToRentalEdit(archive, edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            output = archive.WriteEdits(edits);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon source file could not be decoded or safely edited: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield Rental Pokemon table"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon source file could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Rental Pokemon table"));
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
                $"Rental Pokemon could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        using (var outputRollback = rollbackScope!)
        {
            try
            {
                WriteOutputAtomically(targetPath, output);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, source.GraphEntry.RelativePath));
                outputRollback.Commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Rental Pokemon output file could not be written: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Writable output root"));
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
            }
        }

        if (writtenFiles.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Rental Pokemon change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        OpenedProject project,
        SwShRentalPokemonWorkflow sourceWorkflow,
        SwShRentalPokemonEntry sourceRental,
        SwShRentalPokemonEntry effectiveRental,
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is null || value is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
            return null;
        }

        var normalizedField = field.Trim();
        var editableField = SwShRentalPokemonWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var sourceValue = GetRentalFieldValue(sourceRental, normalizedField);
        var parsedValue = TryParseFieldValue(editableField, value, sourceValue, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return new PendingEdit(
            SwShRentalPokemonWorkflowService.RentalPokemonEditDomain,
            $"Set {effectiveRental.Label} {editableField.Label} to {parsedValue.Value}.",
            CreateExpectedSources(
                project,
                sourceWorkflow,
                sourceRental,
                normalizedField,
                parsedValue.Value),
            RecordId: SwShRentalPokemonWorkflowService.CreateRentalRecordId(
                sourceRental.RentalIndex,
                sourceRental.SourceIdentity),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static long? TryParseFieldValue(
        SwShRentalPokemonEditableField editableField,
        string? value,
        long sourceValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be an integer value.",
                field: editableField.Field,
                expected: "Integer value"));
            return null;
        }

        var canonical = parsedValue.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(value, canonical, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must use canonical integer text without whitespace, a plus sign, or leading zeroes.",
                field: editableField.Field,
                expected: canonical));
            return null;
        }

        // Preserve and permit reversion to unsupported legacy source values, but never stage new ones.
        if (parsedValue == sourceValue)
        {
            return parsedValue;
        }

        if (IsIvField(editableField.Field)
            && parsedValue is < SwShRentalPokemonArchive.MinimumFixedIvValue
                or > SwShRentalPokemonArchive.MaximumFixedIvValue)
        {
            diagnostics.Add(CreateIvDiagnostic(editableField.Field));
            return null;
        }

        if (editableField.Field == SwShRentalPokemonWorkflowService.BallItemIdField
            && (parsedValue < int.MinValue
                || parsedValue > int.MaxValue
                || !SwShRentalPokemonArchive.IsValidBallItemId((int)parsedValue)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ball item {parsedValue.ToString(CultureInfo.InvariantCulture)} is not a supported Sword/Shield Poke Ball item ID.",
                field: editableField.Field,
                expected: "0, 1-16, 492-499, 576, or 851"));
            return null;
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

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShRentalPokemonWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        bool addSuccessDiagnostic)
    {
        var rentalEdits = GetRentalEdits(session).ToArray();
        var effectiveWorkflow = workflow;
        var seenFields = new HashSet<(int RentalIndex, string Field)>();
        var evRentalIndexes = new HashSet<int>();
        var semanticRentalIndexes = new HashSet<int>();
        var ivModes = new Dictionary<int, (bool HasPreset, bool HasIndividual)>();

        foreach (var edit in rentalEdits)
        {
            var errorsBefore = CountErrors(diagnostics);
            var rental = ValidatePendingEdit(project, workflow, effectiveWorkflow, edit, diagnostics);
            if (rental is not null)
            {
                var field = edit.Field ?? string.Empty;
                if (!seenFields.Add((rental.RentalIndex, field)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Rental Pokemon {rental.RentalIndex} has more than one pending edit for '{field}'.",
                        field: field,
                        expected: "One pending value per Rental Pokemon field"));
                }

                if (IsEvField(field))
                {
                    evRentalIndexes.Add(rental.RentalIndex);
                }

                if (IsSemanticField(field))
                {
                    semanticRentalIndexes.Add(rental.RentalIndex);
                }

                if (IsIvField(field))
                {
                    ivModes.TryGetValue(rental.RentalIndex, out var modes);
                    ivModes[rental.RentalIndex] = field == SwShRentalPokemonWorkflowService.FixedIvPresetField
                        ? modes with { HasPreset = true }
                        : modes with { HasIndividual = true };
                }
            }

            if (CountErrors(diagnostics) == errorsBefore)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        foreach (var (rentalIndex, modes) in ivModes)
        {
            if (modes.HasPreset && modes.HasIndividual)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Rental Pokemon {rentalIndex} mixes an IV preset with individual IV edits.",
                    field: SwShRentalPokemonWorkflowService.FixedIvPresetField,
                    expected: "Either one IV preset or individual IV values"));
            }
        }

        ValidateFinalEvValues(effectiveWorkflow, evRentalIndexes, diagnostics);
        if (semanticRentalIndexes.Count > 0)
        {
            var personalRecords = LoadPersonalRecords(project, diagnostics);
            ValidateRentalSemantics(effectiveWorkflow, semanticRentalIndexes, personalRecords, diagnostics);
        }

        if (rentalEdits.Length > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            PreflightArchiveWrite(project, rentalEdits, diagnostics);
        }

        if (addSuccessDiagnostic
            && rentalEdits.Length > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Rental Pokemon change is valid."));
        }
    }

    private static SwShRentalPokemonEntry? ValidatePendingEdit(
        OpenedProject project,
        SwShRentalPokemonWorkflow sourceWorkflow,
        SwShRentalPokemonWorkflow effectiveWorkflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = SwShRentalPokemonWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        if (!SwShRentalPokemonWorkflowService.TryParseRentalRecordId(
                edit.RecordId,
                out var rentalIndex,
                out var sourceIdentity))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Rental Pokemon edit targets an invalid record.",
                field: "rentalIndex",
                expected: "Rental Pokemon record"));
            return null;
        }

        var sourceRental = ResolveRental(sourceWorkflow, rentalIndex, diagnostics, edit.Field);
        var effectiveRental = ResolveRental(effectiveWorkflow, rentalIndex, diagnostics, edit.Field);
        if (sourceRental is null || effectiveRental is null)
        {
            return null;
        }

        var signedRecord = sourceIdentity is not null;
        if (signedRecord
            && !string.Equals(sourceIdentity, sourceRental.SourceIdentity, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The staged Rental Pokemon source record changed. Stage the edit again against the current record.",
                field: edit.Field,
                expected: "Pending edit signed by the current Rental Pokemon source identity"));
            return null;
        }

        var sourceValue = GetRentalFieldValue(sourceRental, editableField.Field);
        var parsedValue = TryParseFieldValue(editableField, edit.NewValue, sourceValue, diagnostics);
        if (parsedValue is null)
        {
            return effectiveRental;
        }

        var expectedSources = CreateExpectedSources(
            project,
            sourceWorkflow,
            sourceRental,
            editableField.Field,
            parsedValue.Value);
        if (!SourcesMatchCurrent(edit.Sources, expectedSources, sourceRental, signedRecord))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The Rental Pokemon source layer changed after this edit was staged. Stage the edit again against the current source.",
                field: edit.Field,
                expected: "Pending edit staged from the current Rental Pokemon sources"));
            return null;
        }

        if (parsedValue.Value != sourceValue)
        {
            ValidateOptionBackedValue(sourceWorkflow, editableField, parsedValue.Value, diagnostics);
        }

        AddLinkedUsageWarning(edit.Field, diagnostics);
        return effectiveRental;
    }

    private static void ValidateOptionBackedValue(
        SwShRentalPokemonWorkflow workflow,
        SwShRentalPokemonEditableField editableField,
        long value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (editableField.Field is SwShRentalPokemonWorkflowService.AbilityField
            or SwShRentalPokemonWorkflowService.GenderField
            or SwShRentalPokemonWorkflowService.SpeciesField
            or SwShRentalPokemonWorkflowService.FormField
            or SwShRentalPokemonWorkflowService.BallItemIdField)
        {
            return;
        }

        if (editableField.Field == SwShRentalPokemonWorkflowService.HeldItemIdField)
        {
            if (value == 0)
            {
                return;
            }

            if (!workflow.HasItemSemanticData || workflow.ItemSemanticSource is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Held item cannot be changed to a nonzero value because a valid Sword/Shield item data table is unavailable.",
                    field: editableField.Field,
                    expected: $"Readable {SwShItemsWorkflowService.ItemDataPath}"));
                return;
            }

            if (value is < int.MinValue or > int.MaxValue
                || !workflow.ValidHeldItemIds.Contains((int)value))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Held item value {value.ToString(CultureInfo.InvariantCulture)} is not present in the loaded Sword/Shield item data table.",
                    field: editableField.Field,
                    expected: "An item ID present in Sword/Shield item data"));
            }

            return;
        }

        if (editableField.Field is
            SwShRentalPokemonWorkflowService.Move0Field
            or SwShRentalPokemonWorkflowService.Move1Field
            or SwShRentalPokemonWorkflowService.Move2Field
            or SwShRentalPokemonWorkflowService.Move3Field)
        {
            if (value == 0)
            {
                return;
            }

            if (!workflow.HasMoveSemanticData)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Move cannot be changed to a nonzero value because valid Sword/Shield move data is unavailable.",
                    field: editableField.Field,
                    expected: $"Readable move records under {SwShMoveDataFile.MoveDataRelativeDirectory}"));
                return;
            }

            if (value is < int.MinValue or > int.MaxValue
                || !workflow.UsableMoveSources.ContainsKey((int)value))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move value {value.ToString(CultureInfo.InvariantCulture)} is not usable in the loaded Sword/Shield move data.",
                    field: editableField.Field,
                    expected: "A usable move ID present in Sword/Shield move data"));
            }

            return;
        }

        if (editableField.Field != SwShRentalPokemonWorkflowService.NatureField)
        {
            return;
        }

        var options = workflow.EditableFields.FirstOrDefault(field =>
            string.Equals(field.Field, editableField.Field, StringComparison.Ordinal))?.Options
            ?? editableField.Options;
        if (options.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} cannot be changed because its Sword/Shield lookup data is unavailable.",
                field: editableField.Field,
                expected: $"Loaded {editableField.Label.ToLowerInvariant()} lookup data"));
            return;
        }

        if (options.Count > 0 && !options.Any(option => option.Value == value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} value {value.ToString(CultureInfo.InvariantCulture)} is not available in the loaded Sword/Shield lookup data.",
                field: editableField.Field,
                expected: $"A listed {editableField.Label.ToLowerInvariant()} value"));
        }
    }

    private static void ValidateFinalEvValues(
        SwShRentalPokemonWorkflow workflow,
        IReadOnlySet<int> rentalIndexes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var rentalIndex in rentalIndexes)
        {
            var rental = ResolveRental(workflow, rentalIndex, diagnostics, SwShRentalPokemonWorkflowService.EvHpField);
            if (rental is null)
            {
                continue;
            }

            var values = new[]
            {
                (SwShRentalPokemonWorkflowService.EvHpField, rental.Evs.HP),
                (SwShRentalPokemonWorkflowService.EvAttackField, rental.Evs.Attack),
                (SwShRentalPokemonWorkflowService.EvDefenseField, rental.Evs.Defense),
                (SwShRentalPokemonWorkflowService.EvSpecialAttackField, rental.Evs.SpecialAttack),
                (SwShRentalPokemonWorkflowService.EvSpecialDefenseField, rental.Evs.SpecialDefense),
                (SwShRentalPokemonWorkflowService.EvSpeedField, rental.Evs.Speed),
            };
            foreach (var (field, value) in values)
            {
                if (value is < 0 or > SwShRentalPokemonWorkflowService.MaximumPokemonEvValue)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Rental Pokemon EV value {value} is outside the supported range 0-{SwShRentalPokemonWorkflowService.MaximumPokemonEvValue}.",
                        field: field,
                        expected: "Supported Rental Pokemon EV value"));
                }
            }

            if (values.Sum(entry => entry.Item2) > MaximumPokemonEvTotal)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{rental.Label} has more than {MaximumPokemonEvTotal} total EVs.",
                    field: SwShRentalPokemonWorkflowService.EvHpField,
                    expected: $"At most {MaximumPokemonEvTotal} total EVs"));
            }
        }
    }

    private static void PreflightArchiveWrite(
        OpenedProject project,
        IReadOnlyList<PendingEdit> rentalEdits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShRentalPokemonWorkflowService.ResolveRentalPokemonDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon edit preflight could not resolve the source table.",
                expected: SwShRentalPokemonWorkflowService.RentalPokemonDataPath));
            return;
        }

        try
        {
            var archive = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = rentalEdits
                .Select(edit => ToRentalEdit(archive, edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();
            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                _ = archive.WriteEdits(edits);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon edit cannot be written safely: {exception.Message}",
                expected: "Safely editable Sword/Shield Rental Pokemon table",
                file: source.GraphEntry.RelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon edit preflight could not read the source table: {exception.Message}",
                expected: "Readable Sword/Shield Rental Pokemon table",
                file: source.GraphEntry.RelativePath));
        }
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
                "Rental Pokemon identity and move edits preserve this record's hash identifiers and update only rental.bin. Linked rental-team or script references are not rewritten and must be reviewed separately.",
                field: field,
                expected: "Review linked rental-team and script usage when changing Rental Pokemon identity or moves"));
        }
    }

    private static IReadOnlyList<SwShPersonalRecord> LoadPersonalRecords(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon species, form, ability, and gender validation requires the Sword/Shield personal data table.",
                field: SwShRentalPokemonWorkflowService.SpeciesField,
                expected: SwShPokemonWorkflowService.PersonalDataPath));
            return [];
        }

        try
        {
            return SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException exception)
        {
            AddPersonalDataDiagnostic(source.GraphEntry.RelativePath, exception.Message, diagnostics);
            return [];
        }
        catch (IOException exception)
        {
            AddPersonalDataDiagnostic(source.GraphEntry.RelativePath, exception.Message, diagnostics);
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            AddPersonalDataDiagnostic(source.GraphEntry.RelativePath, exception.Message, diagnostics);
            return [];
        }
    }

    private static void AddPersonalDataDiagnostic(
        string relativePath,
        string detail,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Rental Pokemon species and form validation could not read personal data: {detail}",
            field: SwShRentalPokemonWorkflowService.SpeciesField,
            expected: "Readable Sword/Shield personal data table",
            file: relativePath));
    }

    private static void ValidateRentalSemantics(
        SwShRentalPokemonWorkflow workflow,
        IReadOnlySet<int> rentalIndexes,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (rentalIndexes.Count == 0 || personalRecords.Count == 0)
        {
            return;
        }

        foreach (var rentalIndex in rentalIndexes)
        {
            var rental = workflow.Rentals.FirstOrDefault(candidate => candidate.RentalIndex == rentalIndex);
            if (rental is null
                || rental.SpeciesId <= 0
                || (uint)rental.SpeciesId >= (uint)personalRecords.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Rental Pokemon {rentalIndex + 1} does not target a species available in the loaded Sword/Shield personal data.",
                    field: SwShRentalPokemonWorkflowService.SpeciesField,
                    expected: "Species present in Sword/Shield personal data"));
                continue;
            }

            var basePersonal = personalRecords[rental.SpeciesId];
            var formCount = Math.Max(1, basePersonal.FormCount);
            if (rental.Form < 0 || rental.Form >= formCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{rental.Label} uses form {rental.Form}, but species {rental.SpeciesId} exposes {formCount} supported form slot(s) in personal data.",
                    field: SwShRentalPokemonWorkflowService.FormField,
                    expected: $"Form 0 through {formCount - 1}"));
                continue;
            }

            var personal = basePersonal;
            if (rental.Form > 0 && basePersonal.FormStatsIndex > 0)
            {
                var formPersonalId = basePersonal.FormStatsIndex + rental.Form - 1;
                if ((uint)formPersonalId >= (uint)personalRecords.Count)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{rental.Label} maps to a form record outside the loaded personal table.",
                        field: SwShRentalPokemonWorkflowService.FormField,
                        expected: "Mapped Sword/Shield personal form record"));
                    continue;
                }

                personal = personalRecords[formPersonalId];
            }

            if (!personal.IsPresentInGame)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{rental.Label} uses a species/form that is not marked present in Sword/Shield personal data.",
                    field: SwShRentalPokemonWorkflowService.SpeciesField,
                    expected: "Species/form present in Sword/Shield personal data"));
                continue;
            }

            var abilityIsValid = rental.Ability switch
            {
                0 => personal.Ability1 != 0,
                1 => personal.Ability2 != 0,
                2 => personal.HiddenAbility != 0,
                _ => false,
            };
            if (!abilityIsValid)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{rental.Label} uses ability slot {rental.Ability}, which is not available for the selected species and form.",
                    field: SwShRentalPokemonWorkflowService.AbilityField,
                    expected: "An ability slot present in Sword/Shield personal data"));
            }

            if (!IsCompatibleGender(personal, rental.Gender))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{rental.Label} uses a gender that is not available for the selected species and form.",
                    field: SwShRentalPokemonWorkflowService.GenderField,
                    expected: "A gender compatible with Sword/Shield personal data"));
            }
        }
    }

    private static IReadOnlyList<ProjectFileReference> CreateExpectedSources(
        OpenedProject project,
        SwShRentalPokemonWorkflow workflow,
        SwShRentalPokemonEntry rental,
        string field,
        long value)
    {
        var sources = new List<ProjectFileReference>
        {
            new(rental.Provenance.SourceLayer, rental.Provenance.SourceFile),
        };

        if (IsSemanticField(field))
        {
            var personalSource = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
            if (personalSource is not null)
            {
                sources.Add(new ProjectFileReference(
                    GetSourceLayer(personalSource.GraphEntry),
                    personalSource.GraphEntry.RelativePath));
            }
        }

        if (field == SwShRentalPokemonWorkflowService.HeldItemIdField
            && value != 0
            && workflow.ItemSemanticSource is { } itemSource)
        {
            sources.Add(itemSource);
        }

        if (field is
            SwShRentalPokemonWorkflowService.Move0Field
            or SwShRentalPokemonWorkflowService.Move1Field
            or SwShRentalPokemonWorkflowService.Move2Field
            or SwShRentalPokemonWorkflowService.Move3Field
            && value is >= int.MinValue and <= int.MaxValue
            && workflow.UsableMoveSources.TryGetValue((int)value, out var moveSource))
        {
            sources.Add(moveSource);
        }

        return sources.Distinct().ToArray();
    }

    private static bool SourcesMatchCurrent(
        IReadOnlyList<ProjectFileReference> stagedSources,
        IReadOnlyList<ProjectFileReference> expectedSources,
        SwShRentalPokemonEntry rental,
        bool signedRecord)
    {
        if (signedRecord)
        {
            return stagedSources.Count == expectedSources.Count
                && expectedSources.All(stagedSources.Contains);
        }

        var currentRentalSource = new ProjectFileReference(
            rental.Provenance.SourceLayer,
            rental.Provenance.SourceFile);
        return stagedSources.Contains(currentRentalSource)
            && stagedSources
                .Where(source => string.Equals(
                    source.RelativePath,
                    rental.Provenance.SourceFile,
                    StringComparison.OrdinalIgnoreCase))
                .All(source => source.Layer == rental.Provenance.SourceLayer)
            && expectedSources.All(expected => stagedSources
                .Where(source => string.Equals(
                    source.RelativePath,
                    expected.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
                .All(source => source.Layer == expected.Layer));
    }

    private static IEnumerable<ProjectFileReference> GetPlanSources(
        OpenedProject project,
        SwShRentalPokemonWorkflow workflow,
        PendingEdit edit)
    {
        foreach (var source in edit.Sources)
        {
            yield return source;
        }

        if (!SwShRentalPokemonWorkflowService.TryParseRentalRecordId(edit.RecordId, out var rentalIndex)
            || edit.Field is null
            || !long.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            yield break;
        }

        var rental = workflow.Rentals.SingleOrDefault(candidate => candidate.RentalIndex == rentalIndex);
        if (rental is null)
        {
            yield break;
        }

        foreach (var source in CreateExpectedSources(project, workflow, rental, edit.Field, value))
        {
            yield return source;
        }
    }

    private static int CountErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static bool IsSemanticField(string field)
    {
        return field is
            SwShRentalPokemonWorkflowService.SpeciesField
            or SwShRentalPokemonWorkflowService.FormField
            or SwShRentalPokemonWorkflowService.AbilityField
            or SwShRentalPokemonWorkflowService.GenderField;
    }

    private static bool IsCompatibleGender(SwShPersonalRecord personal, int gender)
    {
        return gender switch
        {
            0 => true,
            1 => personal.GenderRatio is not 254 and not 255,
            2 => personal.GenderRatio != 0,
            _ => false,
        };
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
        return IsRentalEdit(candidate)
            && IsRentalEdit(pendingEdit)
            && SwShRentalPokemonWorkflowService.TryParseRentalRecordId(candidate.RecordId, out var candidateIndex)
            && SwShRentalPokemonWorkflowService.TryParseRentalRecordId(pendingEdit.RecordId, out var pendingIndex)
            && candidateIndex == pendingIndex
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static EditSession RemovePendingRentalField(EditSession session, int rentalIndex, string field)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !IsRentalEditForRecord(edit, rentalIndex)
                    || !string.Equals(edit.Field, field, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static EditSession NormalizeIvEditsBeforeUpdate(
        OpenedProject project,
        SwShRentalPokemonWorkflow sourceWorkflow,
        EditSession session,
        SwShRentalPokemonEntry sourceRental,
        SwShRentalPokemonEntry effectiveRental,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field == SwShRentalPokemonWorkflowService.FixedIvPresetField)
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit => !IsRentalEditForRecord(edit, sourceRental.RentalIndex)
                        || edit.Field is not (
                            SwShRentalPokemonWorkflowService.IvHpField
                            or SwShRentalPokemonWorkflowService.IvAttackField
                            or SwShRentalPokemonWorkflowService.IvDefenseField
                            or SwShRentalPokemonWorkflowService.IvSpeedField
                            or SwShRentalPokemonWorkflowService.IvSpecialAttackField
                            or SwShRentalPokemonWorkflowService.IvSpecialDefenseField))
                    .ToArray(),
            };
        }

        if (!IsIndividualIvField(field)
            || !session.PendingEdits.Any(edit =>
                IsRentalEditForRecord(edit, sourceRental.RentalIndex)
                && edit.Field == SwShRentalPokemonWorkflowService.FixedIvPresetField))
        {
            return session;
        }

        var normalizedSession = session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !IsRentalEditForRecord(edit, sourceRental.RentalIndex)
                    || edit.Field != SwShRentalPokemonWorkflowService.FixedIvPresetField)
                .ToArray(),
        };
        foreach (var individualField in IndividualIvFields)
        {
            var effectiveValue = GetRentalFieldValue(effectiveRental, individualField);
            if (effectiveValue == GetRentalFieldValue(sourceRental, individualField))
            {
                continue;
            }

            var materializedEdit = CreatePendingEdit(
                project,
                sourceWorkflow,
                sourceRental,
                effectiveRental,
                individualField,
                effectiveValue.ToString(CultureInfo.InvariantCulture),
                diagnostics);
            if (materializedEdit is null)
            {
                return session;
            }

            normalizedSession = ReplacePendingRentalEdit(normalizedSession, materializedEdit);
        }

        return normalizedSession;
    }

    private static bool IsIndividualIvField(string field)
    {
        return field is
            SwShRentalPokemonWorkflowService.IvHpField
            or SwShRentalPokemonWorkflowService.IvAttackField
            or SwShRentalPokemonWorkflowService.IvDefenseField
            or SwShRentalPokemonWorkflowService.IvSpeedField
            or SwShRentalPokemonWorkflowService.IvSpecialAttackField
            or SwShRentalPokemonWorkflowService.IvSpecialDefenseField;
    }

    private static bool IsRentalEditForRecord(PendingEdit edit, int rentalIndex)
    {
        return IsRentalEdit(edit)
            && SwShRentalPokemonWorkflowService.TryParseRentalRecordId(edit.RecordId, out var candidateIndex)
            && candidateIndex == rentalIndex;
    }

    private static bool IsRentalEdit(PendingEdit edit)
    {
        return string.Equals(
            edit.Domain,
            SwShRentalPokemonWorkflowService.RentalPokemonEditDomain,
            StringComparison.Ordinal);
    }

    private static IEnumerable<PendingEdit> GetRentalEdits(EditSession session)
    {
        return session.PendingEdits.Where(IsRentalEdit);
    }

    private static SwShRentalPokemonWorkflow OverlayPendingEdits(
        SwShRentalPokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits.Where(IsRentalEdit))
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
            || !SwShRentalPokemonWorkflowService.TryParseRentalRecordId(
                edit.RecordId,
                out var rentalIndex,
                out var sourceIdentity)
            || !long.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || (edit.Field != SwShRentalPokemonWorkflowService.TrainerIdField
                && value is < int.MinValue or > int.MaxValue)
            || (edit.Field == SwShRentalPokemonWorkflowService.TrainerIdField
                && value is < 0 or > uint.MaxValue))
        {
            return workflow;
        }

        var rentals = workflow.Rentals
            .Select(rental => rental.RentalIndex == rentalIndex
                && (sourceIdentity is null
                    || string.Equals(sourceIdentity, rental.SourceIdentity, StringComparison.OrdinalIgnoreCase))
                ? OverlayRentalField(workflow, rental, edit.Field!, value)
                : rental)
            .ToArray();
        return workflow with
        {
            Rentals = rentals,
            Stats = workflow.Stats with
            {
                TotalRentalCount = rentals.Length,
                PerfectIvRentalCount = rentals.Count(rental => rental.HasPerfectIvs),
            },
        };
    }

    private static SwShRentalPokemonEntry OverlayRentalField(
        SwShRentalPokemonWorkflow workflow,
        SwShRentalPokemonEntry rental,
        string field,
        long value)
    {
        var intValue = field == SwShRentalPokemonWorkflowService.TrainerIdField
            ? 0
            : checked((int)value);
        var updatedRental = field switch
        {
            SwShRentalPokemonWorkflowService.SpeciesField => rental with
            {
                SpeciesId = intValue,
                Species = GetOptionDisplayName(workflow, field, intValue, "Species"),
            },
            SwShRentalPokemonWorkflowService.FormField => rental with { Form = intValue },
            SwShRentalPokemonWorkflowService.LevelField => rental with { Level = intValue },
            SwShRentalPokemonWorkflowService.HeldItemIdField => rental with
            {
                HeldItemId = intValue,
                HeldItem = intValue == 0 ? null : GetOptionDisplayName(workflow, field, intValue, "Item"),
            },
            SwShRentalPokemonWorkflowService.BallItemIdField => rental with
            {
                BallItemId = intValue,
                BallItem = GetOptionDisplayName(workflow, field, intValue, "Item"),
            },
            SwShRentalPokemonWorkflowService.AbilityField => rental with
            {
                Ability = intValue,
            },
            SwShRentalPokemonWorkflowService.NatureField => rental with
            {
                Nature = intValue,
                NatureLabel = GetOptionLabel(workflow, field, intValue, "Nature"),
            },
            SwShRentalPokemonWorkflowService.GenderField => rental with
            {
                Gender = intValue,
            },
            SwShRentalPokemonWorkflowService.TrainerIdField => rental with { TrainerId = checked((uint)value) },
            SwShRentalPokemonWorkflowService.Move0Field => rental with { Moves = SetMove(workflow, rental.Moves, 0, intValue) },
            SwShRentalPokemonWorkflowService.Move1Field => rental with { Moves = SetMove(workflow, rental.Moves, 1, intValue) },
            SwShRentalPokemonWorkflowService.Move2Field => rental with { Moves = SetMove(workflow, rental.Moves, 2, intValue) },
            SwShRentalPokemonWorkflowService.Move3Field => rental with { Moves = SetMove(workflow, rental.Moves, 3, intValue) },
            SwShRentalPokemonWorkflowService.EvHpField => rental with { Evs = rental.Evs with { HP = intValue } },
            SwShRentalPokemonWorkflowService.EvAttackField => rental with { Evs = rental.Evs with { Attack = intValue } },
            SwShRentalPokemonWorkflowService.EvDefenseField => rental with { Evs = rental.Evs with { Defense = intValue } },
            SwShRentalPokemonWorkflowService.EvSpeedField => rental with { Evs = rental.Evs with { Speed = intValue } },
            SwShRentalPokemonWorkflowService.EvSpecialAttackField => rental with { Evs = rental.Evs with { SpecialAttack = intValue } },
            SwShRentalPokemonWorkflowService.EvSpecialDefenseField => rental with { Evs = rental.Evs with { SpecialDefense = intValue } },
            SwShRentalPokemonWorkflowService.IvHpField => rental with { Ivs = rental.Ivs with { HP = intValue } },
            SwShRentalPokemonWorkflowService.IvAttackField => rental with { Ivs = rental.Ivs with { Attack = intValue } },
            SwShRentalPokemonWorkflowService.IvDefenseField => rental with { Ivs = rental.Ivs with { Defense = intValue } },
            SwShRentalPokemonWorkflowService.IvSpeedField => rental with { Ivs = rental.Ivs with { Speed = intValue } },
            SwShRentalPokemonWorkflowService.IvSpecialAttackField => rental with { Ivs = rental.Ivs with { SpecialAttack = intValue } },
            SwShRentalPokemonWorkflowService.IvSpecialDefenseField => rental with { Ivs = rental.Ivs with { SpecialDefense = intValue } },
            SwShRentalPokemonWorkflowService.FixedIvPresetField => rental with { Ivs = CreateFixedIvPreset(intValue) },
            _ => rental,
        };

        var abilityOptions = SwShRentalPokemonWorkflowService.CreateAbilityOptions(
            workflow.AbilityResolver,
            updatedRental.SpeciesId,
            updatedRental.Form);
        var genderOptions = SwShRentalPokemonWorkflowService.CreateGenderOptions(
            workflow.AbilityResolver,
            updatedRental.SpeciesId,
            updatedRental.Form);
        updatedRental = updatedRental with
        {
            AbilityOptions = abilityOptions,
            AbilityLabel = SwShRentalPokemonWorkflowService.GetOptionLabel(
                abilityOptions,
                updatedRental.Ability,
                "Ability slot"),
            GenderOptions = genderOptions,
            GenderLabel = SwShRentalPokemonWorkflowService.GetOptionLabel(
                genderOptions,
                updatedRental.Gender,
                "Gender"),
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
                        : GetOptionDisplayName(workflow, GetMoveField(slot), value, "Move"),
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

    private static string GetOptionDisplayName(
        SwShRentalPokemonWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var label = GetOptionLabel(workflow, field, value, fallbackPrefix);
        var prefix = $"{value.ToString("000", CultureInfo.InvariantCulture)} ";
        return label.StartsWith(prefix, StringComparison.Ordinal)
            ? label[prefix.Length..]
            : label;
    }

    private static long GetRentalFieldValue(SwShRentalPokemonEntry rental, string field)
    {
        return field switch
        {
            SwShRentalPokemonWorkflowService.SpeciesField => rental.SpeciesId,
            SwShRentalPokemonWorkflowService.FormField => rental.Form,
            SwShRentalPokemonWorkflowService.LevelField => rental.Level,
            SwShRentalPokemonWorkflowService.HeldItemIdField => rental.HeldItemId,
            SwShRentalPokemonWorkflowService.BallItemIdField => rental.BallItemId,
            SwShRentalPokemonWorkflowService.AbilityField => rental.Ability,
            SwShRentalPokemonWorkflowService.NatureField => rental.Nature,
            SwShRentalPokemonWorkflowService.GenderField => rental.Gender,
            SwShRentalPokemonWorkflowService.TrainerIdField => rental.TrainerId,
            SwShRentalPokemonWorkflowService.Move0Field => rental.Moves.Single(move => move.Slot == 0).MoveId,
            SwShRentalPokemonWorkflowService.Move1Field => rental.Moves.Single(move => move.Slot == 1).MoveId,
            SwShRentalPokemonWorkflowService.Move2Field => rental.Moves.Single(move => move.Slot == 2).MoveId,
            SwShRentalPokemonWorkflowService.Move3Field => rental.Moves.Single(move => move.Slot == 3).MoveId,
            SwShRentalPokemonWorkflowService.EvHpField => rental.Evs.HP,
            SwShRentalPokemonWorkflowService.EvAttackField => rental.Evs.Attack,
            SwShRentalPokemonWorkflowService.EvDefenseField => rental.Evs.Defense,
            SwShRentalPokemonWorkflowService.EvSpeedField => rental.Evs.Speed,
            SwShRentalPokemonWorkflowService.EvSpecialAttackField => rental.Evs.SpecialAttack,
            SwShRentalPokemonWorkflowService.EvSpecialDefenseField => rental.Evs.SpecialDefense,
            SwShRentalPokemonWorkflowService.IvHpField => rental.Ivs.HP,
            SwShRentalPokemonWorkflowService.IvAttackField => rental.Ivs.Attack,
            SwShRentalPokemonWorkflowService.IvDefenseField => rental.Ivs.Defense,
            SwShRentalPokemonWorkflowService.IvSpeedField => rental.Ivs.Speed,
            SwShRentalPokemonWorkflowService.IvSpecialAttackField => rental.Ivs.SpecialAttack,
            SwShRentalPokemonWorkflowService.IvSpecialDefenseField => rental.Ivs.SpecialDefense,
            SwShRentalPokemonWorkflowService.FixedIvPresetField =>
                rental.Ivs.HP == rental.Ivs.Attack
                && rental.Ivs.HP == rental.Ivs.Defense
                && rental.Ivs.HP == rental.Ivs.Speed
                && rental.Ivs.HP == rental.Ivs.SpecialAttack
                && rental.Ivs.HP == rental.Ivs.SpecialDefense
                    ? rental.Ivs.HP
                    : long.MinValue,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static SwShRentalPokemonEdit? ToRentalEdit(
        SwShRentalPokemonArchive archive,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShRentalPokemonWorkflowService.TryParseRentalRecordId(
                edit.RecordId,
                out var rentalIndex,
                out var sourceIdentity)
            || !long.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Rental Pokemon edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Rental Pokemon edit"));
            return null;
        }

        var matches = archive.Rentals.Where(rental => rental.Index == rentalIndex).ToArray();
        var rental = matches.Length == 1 ? matches[0] : null;
        if (rental is null
            || (sourceIdentity is not null
                && !string.Equals(
                    sourceIdentity,
                    SwShRentalPokemonWorkflowService.CreateSourceIdentity(rental),
                    StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Rental Pokemon edit no longer resolves to exactly one matching source record.",
                field: edit.Field,
                expected: "One source Rental Pokemon matching the staged index and source identity"));
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

    private static SwShRentalPokemonEntry? ResolveRental(
        SwShRentalPokemonWorkflow workflow,
        int rentalIndex,
        ICollection<ValidationDiagnostic> diagnostics,
        string? field)
    {
        var matches = workflow.Rentals.Where(rental => rental.RentalIndex == rentalIndex).ToArray();
        if (matches.Length == 1)
        {
            return matches[0];
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            matches.Length == 0
                ? $"Rental Pokemon index {rentalIndex} is not present in the loaded workflow."
                : $"Rental Pokemon index {rentalIndex} is ambiguous in the loaded workflow.",
            field: field ?? "rentalIndex",
            expected: "Exactly one Rental Pokemon record"));
        return null;
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

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Rental Pokemon apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(
                paths,
                out var stablePaths,
                out var stableRootFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                stableRootFailure ?? "Configured output root could not be resolved safely.",
                file: targetRelativePath,
                expected: "Stable output root"));
            return null;
        }

        var targetPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            stablePaths.OutputRootPath,
            targetRelativePath);
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

    private void WriteOutputAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Rental Pokemon output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Rental Pokemon output target directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(tempPath, contents);
            if (!File.Exists(tempPath)
                || !File.ReadAllBytes(tempPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Rental Pokemon temporary output verification failed.");
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
                // The original output remains untouched when temporary-file cleanup fails.
            }
        }
    }

    private static void RollbackFailedApply(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var rollbackFailures = rollbackScope.Rollback();
        writtenFiles.Clear();
        if (rollbackFailures.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Rental Pokemon apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private static string CreatePlanReason(IReadOnlyList<PendingEdit> pendingEdits)
    {
        var fingerprint = ComputePendingEditFingerprint(pendingEdits);
        var summary = pendingEdits.Count == 1
            ? $"Apply pending Rental Pokemon edit to {pendingEdits[0].RecordId}."
            : $"Apply {pendingEdits.Count} pending Rental Pokemon edits.";
        return $"{summary} Fingerprint {fingerprint}.";
    }

    private static string ComputePendingEditFingerprint(IReadOnlyList<PendingEdit> edits)
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
                AppendFingerprintComponent(canonical, ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintComponent(canonical, source.RelativePath);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendFingerprintComponent(StringBuilder destination, string? value)
    {
        destination.Append(value?.Length ?? -1);
        destination.Append(':');
        destination.Append(value);
        destination.Append('|');
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
