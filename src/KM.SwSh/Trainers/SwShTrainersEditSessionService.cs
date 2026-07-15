// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Trainers;

public sealed class SwShTrainersEditSessionService
{
    private const string TrainersEditDomain = "workflow.trainers";
    private const int MaximumPokemonEvTotal = 510;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShTrainersWorkflowService trainersWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShTrainersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTrainersWorkflowService? trainersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.trainersWorkflowService = trainersWorkflowService ?? new SwShTrainersWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShTrainersEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTrainersWorkflowService? trainersWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.trainersWorkflowService = trainersWorkflowService ?? new SwShTrainersWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShTrainersEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int trainerId,
        int? slot,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        projectWorkspaceService.ClearMemoryCache();
        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = trainersWorkflowService.Load(project);
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditTrainers(project, workflow, diagnostics))
        {
            return new SwShTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var effectiveWorkflow = OverlayPendingEdits(workflow, currentSession.PendingEdits, abilityResolver);
        var selectedTrainer = effectiveWorkflow.Trainers.FirstOrDefault(trainer => trainer.TrainerId == trainerId);
        if (selectedTrainer is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainer {trainerId} is not present in the loaded Trainers workflow.",
                field: "trainerId",
                expected: "Existing trainer record"));
            return new SwShTrainersEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        if (ConflictsWithPendingTrainerClassEdit(currentSession, field))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Apply and reload a pending trainer class change before editing a class ball.",
                field: field,
                expected: "Trainer class and class ball changes reviewed in separate applies"));
            return new SwShTrainersEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedTrainer, slot, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShTrainersEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        pendingEdit = AddSemanticValidationSource(project, pendingEdit);

        var sourceTrainer = workflow.Trainers.First(candidate => candidate.TrainerId == trainerId);
        if (GetTrainerFieldValue(sourceTrainer, slot, pendingEdit.Field) is { } sourceValue
            && string.Equals(
                pendingEdit.NewValue,
                sourceValue.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            var revertedSession = RemovePendingTrainerEdit(currentSession, pendingEdit);
            return new SwShTrainersEditResult(
                OverlayPendingEdits(workflow, revertedSession.PendingEdits, abilityResolver),
                revertedSession,
                diagnostics);
        }

        var updatedSession = ReplacePendingTrainerEdit(currentSession, pendingEdit);

        return new SwShTrainersEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits, abilityResolver),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        return Validate(project, session);
    }

    private SwShEditSessionValidation Validate(OpenedProject project, EditSession session)
    {
        var workflow = trainersWorkflowService.Load(project);
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var trainerEdits = session.PendingEdits.Where(IsTrainerEdit).ToArray();

        CanEditTrainers(project, workflow, diagnostics);

        if (HasTrainerClassAndClassBallEdits(trainerEdits))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainer class and class ball changes must be applied separately so class ownership can be reloaded.",
                field: SwShTrainersWorkflowService.ClassBallIdField,
                expected: "Apply trainer class changes before staging class ball changes"));
        }

        var effectiveWorkflow = workflow;
        var evRecords = new HashSet<(int TrainerId, int Slot)>();
        var identityRecords = new HashSet<(int TrainerId, int Slot)>();
        var abilityRecords = new HashSet<(int TrainerId, int Slot)>();
        var genderRecords = new HashSet<(int TrainerId, int Slot)>();
        var gigantamaxRecords = new HashSet<(int TrainerId, int Slot)>();
        var rosterTrainerIds = new HashSet<int>();
        foreach (var edit in trainerEdits)
        {
            var errorsBefore = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(project, effectiveWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorsBefore)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit, abilityResolver);
            }

            if (!SwShTrainersWorkflowService.TryParseTeamRecordId(edit.RecordId, out var trainerId, out var slot))
            {
                continue;
            }

            var identity = (trainerId, slot);
            if (IsEvField(edit.Field!))
            {
                evRecords.Add(identity);
            }

            if (edit.Field is SwShTrainersWorkflowService.SpeciesIdField
                or SwShTrainersWorkflowService.FormField)
            {
                identityRecords.Add(identity);
                abilityRecords.Add(identity);
                genderRecords.Add(identity);
                gigantamaxRecords.Add(identity);
            }

            if (edit.Field == SwShTrainersWorkflowService.AbilityField)
            {
                abilityRecords.Add(identity);
            }

            if (edit.Field == SwShTrainersWorkflowService.GenderField)
            {
                genderRecords.Add(identity);
            }

            if (edit.Field is SwShTrainersWorkflowService.CanGigantamaxField
                or SwShTrainersWorkflowService.CanDynamaxField)
            {
                gigantamaxRecords.Add(identity);
            }

            if (edit.Field == SwShTrainersWorkflowService.SpeciesIdField)
            {
                rosterTrainerIds.Add(trainerId);
            }
        }

        ValidateFinalTrainerInvariants(
            project,
            effectiveWorkflow,
            evRecords,
            identityRecords,
            abilityRecords,
            genderRecords,
            gigantamaxRecords,
            rosterTrainerIds,
            diagnostics);

        if (trainerEdits.Length > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending trainer change is valid."));
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
        var validation = Validate(project, session);
        var diagnostics = validation.Diagnostics.ToList();
        var trainerEdits = session.PendingEdits.Where(IsTrainerEdit).ToArray();

        if (trainerEdits.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Trainers edit before reviewing a change plan.",
                expected: "Pending trainer edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var workflow = trainersWorkflowService.Load(project);
        var writes = CreatePlannedWrites(workflow, paths, trainerEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target file{(writes.Count == 1 ? string.Empty : "s")}."));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        try
        {
            return ApplyChangePlanCore(paths, session, reviewedPlan);
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
        }
    }

    private ApplyResult ApplyChangePlanCore(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
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
                expected: "Current reviewed Trainers change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = trainersWorkflowService.Load(project);
        var trainerEdits = session.PendingEdits.Where(IsTrainerEdit).ToArray();
        var applyEdits = trainerEdits
            .Concat(CreateImpliedPokemonCountEdits(workflow, trainerEdits))
            .ToArray();
        var projectedWorkflow = OverlayPendingEdits(
            workflow,
            trainerEdits,
            SwShPokemonAbilityOptionResolver.Load(project));
        var pendingOutputs = new List<TrainerOutput>();

        foreach (var editGroup in applyEdits.GroupBy(edit => GetTargetRelativePath(workflow, edit), StringComparer.OrdinalIgnoreCase))
        {
            var targetRelativePath = editGroup.Key;
            if (string.IsNullOrWhiteSpace(targetRelativePath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer edit does not include a valid target source file.",
                    expected: "Trainer data or party source"));
                continue;
            }

            var source = SwShTrainersWorkflowService.ResolveWorkflowFile(project, targetRelativePath);
            if (source is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainers apply could not resolve source file '{targetRelativePath}'.",
                    file: targetRelativePath,
                    expected: "Loaded Sword/Shield trainer source file"));
                continue;
            }

            var targetPath = ResolveOutputPath(paths, source.Entry.RelativePath, diagnostics);
            if (targetPath is null)
            {
                continue;
            }

            try
            {
                var firstField = editGroup.First().Field;
                var output = SwShTrainersWorkflowService.IsTrainerDataField(firstField)
                    ? WriteTrainerDataEdits(source, editGroup, diagnostics)
                    : SwShTrainersWorkflowService.IsTrainerClassField(firstField)
                        ? WriteTrainerClassEdits(source, editGroup, diagnostics)
                        : WriteTrainerTeamEdits(
                            source,
                            editGroup,
                            editGroup.Any(edit => edit.Field == SwShTrainersWorkflowService.SpeciesIdField)
                                ? GetProjectedPokemonCount(projectedWorkflow, source.TrainerId)
                                : null,
                            diagnostics);

                if (output is not null)
                {
                    pendingOutputs.Add(new TrainerOutput(source.Entry.RelativePath, targetPath, output));
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer source file could not be decoded: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Sword/Shield trainer data or party file"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer source file could not be read: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Readable trainer source file"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer source file could not be read: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Readable trainer source file"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
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
                $"Trainers could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var outputRollback = rollbackScope!;
        using (outputRollback)
        {
            foreach (var output in pendingOutputs)
            {
                try
                {
                    WriteAllBytesAtomically(output.AbsolutePath, output.Contents);
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, output.RelativePath));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Trainer output file could not be written: {exception.Message}",
                        file: output.RelativePath,
                        expected: "Writable output root"));
                    break;
                }
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
            }
            else
            {
                outputRollback.Commit();
            }
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Trainers change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditTrainers(
        OpenedProject project,
        SwShTrainersWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainers edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void ValidatePendingEdit(
        OpenedProject project,
        SwShTrainersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, TrainersEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Trainers workflow.",
                expected: TrainersEditDomain));
            return;
        }

        ValidatePendingEditSources(project, workflow, edit, diagnostics);

        if (SwShTrainersWorkflowService.IsTrainerDataField(edit.Field))
        {
            ValidateTrainerDataEdit(workflow, edit, diagnostics);
            return;
        }

        if (SwShTrainersWorkflowService.IsTrainerClassField(edit.Field))
        {
            ValidateTrainerClassEdit(workflow, edit, diagnostics);
            return;
        }

        if (SwShTrainersWorkflowService.IsTrainerPokemonField(edit.Field))
        {
            ValidateTrainerPokemonEdit(workflow, edit, diagnostics);
            return;
        }

        diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
    }

    private static void ValidateTrainerDataEdit(
        SwShTrainersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var trainerId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit targets a record that is not loaded.",
                field: "trainerId",
                expected: "Existing trainer record"));
            return;
        }

        var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
        if (trainer is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit targets a record that is not loaded.",
                field: "trainerId",
                expected: "Existing trainer record"));
            return;
        }

        if (IsReadOnlyRawHeaderField(edit.Field))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "This trainer header value has unverified game semantics and is preserved as raw read-only data.",
                field: edit.Field,
                expected: "Read-only raw trainer header value"));
            return;
        }

        var parsedValue = TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
        if (parsedValue is not null)
        {
            ValidateOptionBackedValue(workflow, edit.Field!, parsedValue.Value, options: null, diagnostics);
        }
    }

    private static void ValidateTrainerClassEdit(
        SwShTrainersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var classId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer class edit targets an invalid class.",
                field: "classId",
                expected: "Existing trainer class record"));
            return;
        }

        var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerClassId == classId);
        if (trainer is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer class edit targets a class that is not loaded.",
                field: "classId",
                expected: "Loaded trainer class"));
            return;
        }

        if (!trainer.CanEditClassBall)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainer class ball edits require a uniquely owned trainer class.",
                field: edit.Field,
                expected: "Unique trainer class with a loaded class file"));
            return;
        }

        var parsedValue = TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
        if (parsedValue is not null)
        {
            ValidateOptionBackedValue(workflow, edit.Field!, parsedValue.Value, options: null, diagnostics);
        }
    }

    private static void ValidateTrainerPokemonEdit(
        SwShTrainersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShTrainersWorkflowService.TryParseTeamRecordId(edit.RecordId, out var trainerId, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer party edit targets an invalid trainer slot.",
                field: "slot",
                expected: "Trainer party slot"));
            return;
        }

        var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
        if (trainer is null || trainer.Team.All(pokemon => pokemon.Slot != slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer party edit targets a slot that is not loaded.",
                field: "slot",
                expected: "Existing trainer party slot"));
            return;
        }

        var pokemon = trainer.Team.First(candidate => candidate.Slot == slot);
        if (pokemon.SpeciesId <= 0 && !string.Equals(edit.Field, SwShTrainersWorkflowService.SpeciesIdField, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainer party slot is empty. Set a Pokemon species before editing slot details.",
                field: edit.Field,
                expected: "Occupied trainer party slot"));
            return;
        }

        var parsedValue = TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
        if (parsedValue is not null)
        {
            ValidateOptionBackedValue(
                workflow,
                edit.Field!,
                parsedValue.Value,
                edit.Field == SwShTrainersWorkflowService.AbilityField ? pokemon.AbilityOptions : null,
                diagnostics);
            ValidateTeamOrder(
                trainer with
                {
                    Team = trainer.Team
                        .Select(candidate => candidate.Slot == slot
                            ? OverlayTrainerPokemonField(candidate, edit.Field!, parsedValue.Value)
                            : candidate)
                        .ToArray(),
                },
                diagnostics);
        }
    }

    private static PendingEdit? CreatePendingEdit(
        SwShTrainerRecord selectedTrainer,
        int? slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (SwShTrainersWorkflowService.IsTrainerDataField(normalizedField))
        {
            return CreateTrainerDataPendingEdit(selectedTrainer, normalizedField, value, diagnostics);
        }

        if (SwShTrainersWorkflowService.IsTrainerClassField(normalizedField))
        {
            return CreateTrainerClassPendingEdit(selectedTrainer, normalizedField, value, diagnostics);
        }

        if (SwShTrainersWorkflowService.IsTrainerPokemonField(normalizedField))
        {
            if (slot is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trainer party edits require a Pokemon slot.",
                    field: "slot",
                    expected: "Existing trainer party slot"));
                return null;
            }

            var pokemon = selectedTrainer.Team.FirstOrDefault(candidate => candidate.Slot == slot.Value);
            if (pokemon is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {selectedTrainer.TrainerId} does not have party slot {slot.Value}.",
                    field: "slot",
                    expected: "Existing trainer party slot"));
                return null;
            }

            if (pokemon.SpeciesId <= 0 && !string.Equals(normalizedField, SwShTrainersWorkflowService.SpeciesIdField, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trainer party slot is empty. Set a Pokemon species before editing slot details.",
                    field: normalizedField,
                    expected: "Occupied trainer party slot"));
                return null;
            }

            return CreateTrainerPokemonPendingEdit(selectedTrainer, pokemon, normalizedField, value, diagnostics);
        }

        diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
        return null;
    }

    private static bool IsTrainerEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, TrainersEditDomain, StringComparison.Ordinal);
    }

    private static bool ConflictsWithPendingTrainerClassEdit(EditSession session, string field)
    {
        var isClassChange = string.Equals(field, SwShTrainersWorkflowService.TrainerClassIdField, StringComparison.Ordinal);
        var isClassBallChange = string.Equals(field, SwShTrainersWorkflowService.ClassBallIdField, StringComparison.Ordinal);
        return (isClassChange && session.PendingEdits.Any(edit =>
                    IsTrainerEdit(edit)
                    && edit.Field == SwShTrainersWorkflowService.ClassBallIdField))
            || (isClassBallChange && session.PendingEdits.Any(edit =>
                    IsTrainerEdit(edit)
                    && edit.Field == SwShTrainersWorkflowService.TrainerClassIdField));
    }

    private static bool HasTrainerClassAndClassBallEdits(IEnumerable<PendingEdit> edits)
    {
        var fields = edits.Select(edit => edit.Field).ToHashSet(StringComparer.Ordinal);
        return fields.Contains(SwShTrainersWorkflowService.TrainerClassIdField)
            && fields.Contains(SwShTrainersWorkflowService.ClassBallIdField);
    }

    private static bool IsReadOnlyRawHeaderField(string? field)
    {
        return field is SwShTrainersWorkflowService.HealField or SwShTrainersWorkflowService.GiftField;
    }

    private static bool RequiresPersonalDataValidation(string? field)
    {
        return field is
            SwShTrainersWorkflowService.SpeciesIdField
            or SwShTrainersWorkflowService.FormField
            or SwShTrainersWorkflowService.AbilityField
            or SwShTrainersWorkflowService.GenderField
            or SwShTrainersWorkflowService.CanGigantamaxField
            or SwShTrainersWorkflowService.CanDynamaxField;
    }

    private static PendingEdit AddSemanticValidationSource(OpenedProject project, PendingEdit pendingEdit)
    {
        if (!RequiresPersonalDataValidation(pendingEdit.Field)
            || SwShPokemonWorkflowService.ResolvePersonalDataSource(project) is not { } personalSource)
        {
            return pendingEdit;
        }

        return pendingEdit with
        {
            Sources = pendingEdit.Sources
                .Append(new ProjectFileReference(
                    GetSourceLayer(personalSource.GraphEntry),
                    personalSource.GraphEntry.RelativePath))
                .Distinct()
                .ToArray(),
        };
    }

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
    }

    private static PendingEdit? CreateTrainerDataPendingEdit(
        SwShTrainerRecord trainer,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (IsReadOnlyRawHeaderField(field))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "This trainer header value has unverified game semantics and is preserved as raw read-only data.",
                field: field,
                expected: "Read-only raw trainer header value"));
            return null;
        }

        var parsedValue = TryParseEditableValue(field, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return new PendingEdit(
            TrainersEditDomain,
            CreateTrainerDataSummary(trainer, field, parsedValue.Value),
            [new ProjectFileReference(trainer.Provenance.SourceLayer, trainer.Provenance.SourceFile)],
            RecordId: trainer.TrainerId.ToString(CultureInfo.InvariantCulture),
            Field: field,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static PendingEdit? CreateTrainerClassPendingEdit(
        SwShTrainerRecord trainer,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!trainer.CanEditClassBall)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainer class ball edits require a uniquely owned trainer class.",
                field,
                expected: "Unique trainer class with a loaded class file"));
            return null;
        }

        var parsedValue = TryParseEditableValue(field, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return new PendingEdit(
            TrainersEditDomain,
            CreateTrainerClassSummary(trainer, field, parsedValue.Value),
            [new ProjectFileReference(trainer.Provenance.ClassSourceLayer!.Value, trainer.Provenance.ClassSourceFile!)],
            RecordId: trainer.TrainerClassId.ToString(CultureInfo.InvariantCulture),
            Field: field,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static PendingEdit? CreateTrainerPokemonPendingEdit(
        SwShTrainerRecord trainer,
        SwShTrainerPokemonRecord pokemon,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var parsedValue = TryParseEditableValue(field, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        ValidateTeamOrder(
            trainer with
            {
                Team = trainer.Team
                    .Select(candidate => candidate.Slot == pokemon.Slot
                        ? OverlayTrainerPokemonField(candidate, field, parsedValue.Value)
                        : candidate)
                    .ToArray(),
            },
            diagnostics);
        if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) != errorCount)
        {
            return null;
        }

        return new PendingEdit(
            TrainersEditDomain,
            CreateTrainerPokemonSummary(trainer, pokemon, field, parsedValue.Value),
            [new ProjectFileReference(trainer.Provenance.TeamSourceLayer, trainer.Provenance.TeamSourceFile)],
            RecordId: SwShTrainersWorkflowService.CreateTeamRecordId(trainer.TrainerId, pokemon.Slot),
            Field: field,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryParseEditableValue(
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = SwShTrainersWorkflowService.GetEditableField(field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be an integer value.",
                field: editableField.Field,
                expected: $"Safe trainer {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (parsedValue < (editableField.MinimumValue ?? int.MinValue)
            || parsedValue > (editableField.MaximumValue ?? int.MaxValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: $"Safe trainer {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        return parsedValue;
    }

    private static bool IsEvField(string field)
    {
        return field is
            SwShTrainersWorkflowService.EvHpField
            or SwShTrainersWorkflowService.EvAttackField
            or SwShTrainersWorkflowService.EvDefenseField
            or SwShTrainersWorkflowService.EvSpecialAttackField
            or SwShTrainersWorkflowService.EvSpecialDefenseField
            or SwShTrainersWorkflowService.EvSpeedField;
    }

    private static bool IsIvField(string field)
    {
        return field is
            SwShTrainersWorkflowService.IvHpField
            or SwShTrainersWorkflowService.IvAttackField
            or SwShTrainersWorkflowService.IvDefenseField
            or SwShTrainersWorkflowService.IvSpecialAttackField
            or SwShTrainersWorkflowService.IvSpecialDefenseField
            or SwShTrainersWorkflowService.IvSpeedField;
    }

    private static EditSession ReplacePendingTrainerEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameTrainerEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static EditSession RemovePendingTrainerEdit(EditSession session, PendingEdit pendingEdit)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !IsSameTrainerEdit(edit, pendingEdit))
                .ToArray(),
        };
    }

    private static int? GetTrainerFieldValue(SwShTrainerRecord trainer, int? slot, string? field)
    {
        if (field is null)
        {
            return null;
        }

        if (slot is null)
        {
            return field switch
            {
                SwShTrainersWorkflowService.TrainerClassIdField => trainer.TrainerClassId,
                SwShTrainersWorkflowService.ClassBallIdField => trainer.ClassBallId,
                SwShTrainersWorkflowService.BattleTypeField => trainer.BattleTypeValue,
                SwShTrainersWorkflowService.TrainerItem1IdField => trainer.ItemIds.ElementAtOrDefault(0),
                SwShTrainersWorkflowService.TrainerItem2IdField => trainer.ItemIds.ElementAtOrDefault(1),
                SwShTrainersWorkflowService.TrainerItem3IdField => trainer.ItemIds.ElementAtOrDefault(2),
                SwShTrainersWorkflowService.TrainerItem4IdField => trainer.ItemIds.ElementAtOrDefault(3),
                SwShTrainersWorkflowService.AiFlagsField => trainer.AiFlags,
                SwShTrainersWorkflowService.HealField => trainer.Heal ? 1 : 0,
                SwShTrainersWorkflowService.MoneyField => trainer.Money,
                SwShTrainersWorkflowService.GiftField => trainer.Gift,
                _ => null,
            };
        }

        var pokemon = trainer.Team.FirstOrDefault(candidate => candidate.Slot == slot.Value);
        if (pokemon is null)
        {
            return null;
        }

        return field switch
        {
            SwShTrainersWorkflowService.SpeciesIdField => pokemon.SpeciesId,
            SwShTrainersWorkflowService.FormField => pokemon.Form,
            SwShTrainersWorkflowService.LevelField => pokemon.Level,
            SwShTrainersWorkflowService.HeldItemIdField => pokemon.HeldItemId,
            SwShTrainersWorkflowService.Move1IdField => pokemon.MoveIds.ElementAtOrDefault(0),
            SwShTrainersWorkflowService.Move2IdField => pokemon.MoveIds.ElementAtOrDefault(1),
            SwShTrainersWorkflowService.Move3IdField => pokemon.MoveIds.ElementAtOrDefault(2),
            SwShTrainersWorkflowService.Move4IdField => pokemon.MoveIds.ElementAtOrDefault(3),
            SwShTrainersWorkflowService.GenderField => pokemon.Gender,
            SwShTrainersWorkflowService.AbilityField => pokemon.Ability,
            SwShTrainersWorkflowService.NatureField => pokemon.Nature,
            SwShTrainersWorkflowService.EvHpField => pokemon.Evs.HP,
            SwShTrainersWorkflowService.EvAttackField => pokemon.Evs.Attack,
            SwShTrainersWorkflowService.EvDefenseField => pokemon.Evs.Defense,
            SwShTrainersWorkflowService.EvSpecialAttackField => pokemon.Evs.SpecialAttack,
            SwShTrainersWorkflowService.EvSpecialDefenseField => pokemon.Evs.SpecialDefense,
            SwShTrainersWorkflowService.EvSpeedField => pokemon.Evs.Speed,
            SwShTrainersWorkflowService.DynamaxLevelField => pokemon.DynamaxLevel,
            SwShTrainersWorkflowService.CanGigantamaxField => pokemon.CanGigantamax ? 1 : 0,
            SwShTrainersWorkflowService.IvHpField => pokemon.Ivs.HP,
            SwShTrainersWorkflowService.IvAttackField => pokemon.Ivs.Attack,
            SwShTrainersWorkflowService.IvDefenseField => pokemon.Ivs.Defense,
            SwShTrainersWorkflowService.IvSpecialAttackField => pokemon.Ivs.SpecialAttack,
            SwShTrainersWorkflowService.IvSpecialDefenseField => pokemon.Ivs.SpecialDefense,
            SwShTrainersWorkflowService.IvSpeedField => pokemon.Ivs.Speed,
            SwShTrainersWorkflowService.ShinyField => pokemon.Shiny ? 1 : 0,
            SwShTrainersWorkflowService.CanDynamaxField => pokemon.CanDynamax ? 1 : 0,
            _ => null,
        };
    }

    private static bool IsSameTrainerEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShTrainersWorkflow OverlayPendingEdits(
        SwShTrainersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        SwShPokemonAbilityOptionResolver? abilityResolver = null)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit, abilityResolver);
        }

        return updatedWorkflow;
    }

    private static SwShTrainersWorkflow OverlayPendingEdit(
        SwShTrainersWorkflow workflow,
        PendingEdit edit,
        SwShPokemonAbilityOptionResolver? abilityResolver = null)
    {
        if (!string.Equals(edit.Domain, TrainersEditDomain, StringComparison.Ordinal)
            || TryParseEditableValue(edit.Field, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        if (SwShTrainersWorkflowService.IsTrainerDataField(edit.Field)
            && int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var trainerId))
        {
            return workflow with
            {
                Trainers = workflow.Trainers
                    .Select(trainer => trainer.TrainerId == trainerId
                        ? OverlayTrainerDataField(workflow, trainer, edit.Field!, value)
                        : trainer)
                    .ToArray(),
            };
        }

        if (SwShTrainersWorkflowService.IsTrainerClassField(edit.Field)
            && int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var classId))
        {
            return workflow with
            {
                Trainers = workflow.Trainers
                    .Select(trainer => trainer.TrainerClassId == classId
                        ? OverlayTrainerClassField(trainer, edit.Field!, value)
                        : trainer)
                    .ToArray(),
            };
        }

        if (SwShTrainersWorkflowService.IsTrainerPokemonField(edit.Field)
            && SwShTrainersWorkflowService.TryParseTeamRecordId(edit.RecordId, out trainerId, out var slot))
        {
            return workflow with
            {
                Trainers = workflow.Trainers
                    .Select(trainer => trainer.TrainerId == trainerId
                        ? trainer with
                        {
                            Team = trainer.Team
                                .Select(pokemon => pokemon.Slot == slot
                                    ? OverlayTrainerPokemonField(
                                        pokemon,
                                        edit.Field!,
                                        value,
                                        workflow,
                                        abilityResolver)
                                    : pokemon)
                                .ToArray(),
                        }
                        : trainer)
                    .ToArray(),
            };
        }

        return workflow;
    }

    private static SwShTrainerRecord OverlayTrainerDataField(
        SwShTrainersWorkflow workflow,
        SwShTrainerRecord trainer,
        string field,
        int value)
    {
        return field switch
        {
            SwShTrainersWorkflowService.TrainerClassIdField => trainer with
            {
                TrainerClassId = value,
                TrainerClass = GetWorkflowOptionLabel(workflow, field, value, "Class"),
                ClassBallId = null,
                ClassBall = null,
                CanEditClassBall = false,
                ClassBallScope = "Apply and reload to resolve class ownership",
                Provenance = trainer.Provenance with
                {
                    ClassSourceFile = null,
                    ClassSourceLayer = null,
                    ClassFileState = null,
                },
            },
            SwShTrainersWorkflowService.BattleTypeField => trainer with
            {
                BattleTypeValue = value,
                BattleType = value switch
                {
                    0 => "Singles",
                    1 => "Doubles",
                    _ => $"Mode {value}",
                },
            },
            SwShTrainersWorkflowService.TrainerItem1IdField => OverlayTrainerItem(workflow, trainer, field, 0, value),
            SwShTrainersWorkflowService.TrainerItem2IdField => OverlayTrainerItem(workflow, trainer, field, 1, value),
            SwShTrainersWorkflowService.TrainerItem3IdField => OverlayTrainerItem(workflow, trainer, field, 2, value),
            SwShTrainersWorkflowService.TrainerItem4IdField => OverlayTrainerItem(workflow, trainer, field, 3, value),
            SwShTrainersWorkflowService.AiFlagsField => trainer with
            {
                AiFlags = value,
                AiFlagStates = SwShTrainersWorkflowService.CreateAiFlagStates((uint)value),
            },
            SwShTrainersWorkflowService.HealField => trainer with { Heal = value != 0 },
            SwShTrainersWorkflowService.MoneyField => trainer with { Money = value },
            SwShTrainersWorkflowService.GiftField => trainer with { Gift = value },
            _ => trainer,
        };
    }

    private static SwShTrainerRecord OverlayTrainerItem(
        SwShTrainersWorkflow workflow,
        SwShTrainerRecord trainer,
        string field,
        int itemIndex,
        int value)
    {
        var itemIds = trainer.ItemIds.ToArray();
        var items = trainer.Items.ToArray();

        if ((uint)itemIndex >= (uint)itemIds.Length || (uint)itemIndex >= (uint)items.Length)
        {
            return trainer;
        }

        itemIds[itemIndex] = value;
        items[itemIndex] = value == 0 ? "None" : GetWorkflowOptionLabel(workflow, field, value, "Item");

        return trainer with
        {
            ItemIds = itemIds,
            Items = items,
        };
    }

    private static SwShTrainerRecord OverlayTrainerClassField(
        SwShTrainerRecord trainer,
        string field,
        int value)
    {
        return field switch
        {
            SwShTrainersWorkflowService.ClassBallIdField => trainer with
            {
                ClassBallId = value,
                ClassBall = SwShTrainersWorkflowService.GetEditableField(field)
                    ?.Options
                    .FirstOrDefault(option => option.Value == value)
                    ?.Label ?? $"Ball {value}",
            },
            _ => trainer,
        };
    }

    private static SwShTrainerPokemonRecord OverlayTrainerPokemonField(
        SwShTrainerPokemonRecord pokemon,
        string field,
        int value,
        SwShTrainersWorkflow? workflow = null,
        SwShPokemonAbilityOptionResolver? abilityResolver = null)
    {
        if (string.Equals(field, SwShTrainersWorkflowService.SpeciesIdField, StringComparison.Ordinal) && value == 0)
        {
            return CreateEmptyPokemonRecord(pokemon.Slot);
        }

        var updated = field switch
        {
            SwShTrainersWorkflowService.SpeciesIdField => pokemon with
            {
                SpeciesId = value,
                Species = value == 0
                    ? "None"
                    : GetWorkflowOptionLabel(workflow, field, value, "Species"),
            },
            SwShTrainersWorkflowService.FormField => pokemon with { Form = value },
            SwShTrainersWorkflowService.LevelField => pokemon with { Level = value },
            SwShTrainersWorkflowService.HeldItemIdField => pokemon with
            {
                HeldItemId = value,
                HeldItem = value == 0
                    ? null
                    : GetWorkflowOptionLabel(workflow, field, value, "Item"),
            },
            SwShTrainersWorkflowService.Move1IdField => OverlayMove(workflow, pokemon, field, 0, value),
            SwShTrainersWorkflowService.Move2IdField => OverlayMove(workflow, pokemon, field, 1, value),
            SwShTrainersWorkflowService.Move3IdField => OverlayMove(workflow, pokemon, field, 2, value),
            SwShTrainersWorkflowService.Move4IdField => OverlayMove(workflow, pokemon, field, 3, value),
            SwShTrainersWorkflowService.GenderField => pokemon with
            {
                Gender = value,
                GenderLabel = SwShTrainersWorkflowService.FormatTrainerPokemonGender(value),
            },
            SwShTrainersWorkflowService.AbilityField => pokemon with
            {
                Ability = value,
                AbilityLabel = pokemon.AbilityOptions.FirstOrDefault(option => option.Value == value)?.Label
                    ?? SwShTrainersWorkflowService.FormatTrainerPokemonAbility(value),
            },
            SwShTrainersWorkflowService.NatureField => pokemon with
            {
                Nature = value,
                NatureLabel = SwShTrainersWorkflowService.FormatTrainerPokemonNature(value),
            },
            SwShTrainersWorkflowService.EvHpField => pokemon with { Evs = pokemon.Evs with { HP = value } },
            SwShTrainersWorkflowService.EvAttackField => pokemon with { Evs = pokemon.Evs with { Attack = value } },
            SwShTrainersWorkflowService.EvDefenseField => pokemon with { Evs = pokemon.Evs with { Defense = value } },
            SwShTrainersWorkflowService.EvSpecialAttackField => pokemon with { Evs = pokemon.Evs with { SpecialAttack = value } },
            SwShTrainersWorkflowService.EvSpecialDefenseField => pokemon with { Evs = pokemon.Evs with { SpecialDefense = value } },
            SwShTrainersWorkflowService.EvSpeedField => pokemon with { Evs = pokemon.Evs with { Speed = value } },
            SwShTrainersWorkflowService.DynamaxLevelField => pokemon with { DynamaxLevel = value },
            SwShTrainersWorkflowService.CanGigantamaxField => pokemon with { CanGigantamax = value != 0 },
            SwShTrainersWorkflowService.IvHpField => pokemon with { Ivs = pokemon.Ivs with { HP = value } },
            SwShTrainersWorkflowService.IvAttackField => pokemon with { Ivs = pokemon.Ivs with { Attack = value } },
            SwShTrainersWorkflowService.IvDefenseField => pokemon with { Ivs = pokemon.Ivs with { Defense = value } },
            SwShTrainersWorkflowService.IvSpecialAttackField => pokemon with { Ivs = pokemon.Ivs with { SpecialAttack = value } },
            SwShTrainersWorkflowService.IvSpecialDefenseField => pokemon with { Ivs = pokemon.Ivs with { SpecialDefense = value } },
            SwShTrainersWorkflowService.IvSpeedField => pokemon with { Ivs = pokemon.Ivs with { Speed = value } },
            SwShTrainersWorkflowService.ShinyField => pokemon with { Shiny = value != 0 },
            SwShTrainersWorkflowService.CanDynamaxField => pokemon with { CanDynamax = value != 0 },
            _ => pokemon,
        };

        return field is SwShTrainersWorkflowService.SpeciesIdField or SwShTrainersWorkflowService.FormField
            ? RefreshPokemonDerivedContext(updated, workflow, abilityResolver)
            : updated;
    }

    private static SwShTrainerPokemonRecord CreateEmptyPokemonRecord(int slot)
    {
        return new SwShTrainerPokemonRecord(
            slot,
            0,
            "None",
            0,
            SwShTrainerTeamFile.MinimumLevel,
            0,
            null,
            [0, 0, 0, 0],
            ["None", "None", "None", "None"],
            0,
            SwShTrainersWorkflowService.FormatTrainerPokemonGender(0),
            0,
            SwShTrainersWorkflowService.FormatTrainerPokemonAbility(0),
            0,
            SwShTrainersWorkflowService.FormatTrainerPokemonNature(0),
            new SwShTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0),
            0,
            false,
            new SwShTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0),
            false,
            true)
        {
            AbilityOptions = Array.Empty<SwShTrainerEditableFieldOption>(),
        };
    }

    private static SwShTrainerPokemonRecord OverlayMove(
        SwShTrainersWorkflow? workflow,
        SwShTrainerPokemonRecord pokemon,
        string field,
        int moveIndex,
        int value)
    {
        var moveIds = pokemon.MoveIds.ToArray();
        var moves = pokemon.Moves.ToArray();

        if ((uint)moveIndex >= (uint)moveIds.Length || (uint)moveIndex >= (uint)moves.Length)
        {
            return pokemon;
        }

        moveIds[moveIndex] = value;
        moves[moveIndex] = value == 0 ? "None" : GetWorkflowOptionLabel(workflow, field, value, "Move");

        return pokemon with
        {
            MoveIds = moveIds,
            Moves = moves,
        };
    }

    private static SwShTrainerPokemonRecord RefreshPokemonDerivedContext(
        SwShTrainerPokemonRecord pokemon,
        SwShTrainersWorkflow? workflow,
        SwShPokemonAbilityOptionResolver? abilityResolver)
    {
        if (pokemon.SpeciesId <= 0 || abilityResolver is null)
        {
            return pokemon;
        }

        var personal = abilityResolver.ResolvePersonalRecord(pokemon.SpeciesId, pokemon.Form);
        var abilityOptions = abilityResolver
            .CreateOptions(pokemon.SpeciesId, pokemon.Form, SwShAbilityOptionMode.DefaultPlusSlots)
            .Where(option => personal is not null && IsAvailableAbilitySlot(personal, option.Value))
            .Select(option => new SwShTrainerEditableFieldOption(option.Value, option.Label))
            .ToArray();
        var species = GetWorkflowOptionLabel(
            workflow,
            SwShTrainersWorkflowService.SpeciesIdField,
            pokemon.SpeciesId,
            "Species");

        return pokemon with
        {
            Species = species,
            AbilityOptions = abilityOptions,
            AbilityLabel = abilityOptions.FirstOrDefault(option => option.Value == pokemon.Ability)?.Label
                ?? SwShTrainersWorkflowService.FormatTrainerPokemonAbility(pokemon.Ability),
            BaseStats = personal is null
                ? null
                : new SwShTrainerPokemonStatsRecord(
                    personal.HP,
                    personal.Attack,
                    personal.Defense,
                    personal.SpecialAttack,
                    personal.SpecialDefense,
                    personal.Speed),
            SpriteName = species,
        };
    }

    private static string GetWorkflowOptionLabel(
        SwShTrainersWorkflow? workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var label = workflow?.EditableFields
            .FirstOrDefault(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal))
            ?.Options
            .FirstOrDefault(option => option.Value == value)
            ?.Label;
        if (string.IsNullOrWhiteSpace(label))
        {
            return $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
        }

        var separator = label.IndexOf(' ');
        return separator > 0 && label[..separator].All(char.IsDigit)
            ? label[(separator + 1)..]
            : label;
    }

    private static void ValidateTeamOrder(
        SwShTrainerRecord trainer,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var hasEmptySlot = false;
        foreach (var pokemon in trainer.Team.OrderBy(candidate => candidate.Slot))
        {
            if (pokemon.SpeciesId <= 0)
            {
                hasEmptySlot = true;
                continue;
            }

            if (hasEmptySlot)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trainer party slots must be filled in order. Fill the previous slot before adding this Pokemon, or clear later slots first.",
                    field: SwShTrainersWorkflowService.SpeciesIdField,
                    expected: "Contiguous trainer party slots"));
                return;
            }
        }
    }

    private static void ValidateOptionBackedValue(
        SwShTrainersWorkflow workflow,
        string field,
        int value,
        IReadOnlyList<SwShTrainerEditableFieldOption>? options,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var requiresKnownOption = field is
            SwShTrainersWorkflowService.TrainerClassIdField
            or SwShTrainersWorkflowService.ClassBallIdField
            or SwShTrainersWorkflowService.BattleTypeField
            or SwShTrainersWorkflowService.TrainerItem1IdField
            or SwShTrainersWorkflowService.TrainerItem2IdField
            or SwShTrainersWorkflowService.TrainerItem3IdField
            or SwShTrainersWorkflowService.TrainerItem4IdField
            or SwShTrainersWorkflowService.SpeciesIdField
            or SwShTrainersWorkflowService.HeldItemIdField
            or SwShTrainersWorkflowService.Move1IdField
            or SwShTrainersWorkflowService.Move2IdField
            or SwShTrainersWorkflowService.Move3IdField
            or SwShTrainersWorkflowService.Move4IdField
            or SwShTrainersWorkflowService.GenderField
            or SwShTrainersWorkflowService.AbilityField;
        if (!requiresKnownOption)
        {
            return;
        }

        var availableOptions = options
            ?? workflow.EditableFields
                .FirstOrDefault(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal))
                ?.Options
            ?? [];
        if (field == SwShTrainersWorkflowService.GenderField && value == 3)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainer gender value 3 is not verified for Sword and Shield and cannot be newly selected.",
                field: field,
                expected: "Random, Male, or Female"));
            return;
        }

        if (availableOptions.Count == 0)
        {
            if (value == 0 && AllowsZeroWithoutLookup(field))
            {
                return;
            }

            var label = SwShTrainersWorkflowService.GetEditableField(field)?.Label ?? field;
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{label} cannot be changed because its Sword and Shield lookup data is unavailable.",
                field: field,
                expected: $"Readable lookup data with a listed {label.ToLowerInvariant()} value"));
            return;
        }

        if (availableOptions.All(option => option.Value != value))
        {
            var label = SwShTrainersWorkflowService.GetEditableField(field)?.Label ?? field;
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{label} value {value.ToString(CultureInfo.InvariantCulture)} is not available in the loaded Sword and Shield lookup data.",
                field: field,
                expected: $"A listed {label.ToLowerInvariant()} value"));
        }
    }

    private static bool AllowsZeroWithoutLookup(string field)
    {
        return field is
            SwShTrainersWorkflowService.TrainerItem1IdField
            or SwShTrainersWorkflowService.TrainerItem2IdField
            or SwShTrainersWorkflowService.TrainerItem3IdField
            or SwShTrainersWorkflowService.TrainerItem4IdField
            or SwShTrainersWorkflowService.SpeciesIdField
            or SwShTrainersWorkflowService.HeldItemIdField
            or SwShTrainersWorkflowService.Move1IdField
            or SwShTrainersWorkflowService.Move2IdField
            or SwShTrainersWorkflowService.Move3IdField
            or SwShTrainersWorkflowService.Move4IdField;
    }

    private static void ValidatePendingEditSources(
        OpenedProject project,
        SwShTrainersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var primarySource = GetSourceReference(workflow, edit);
        if (primarySource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit no longer resolves to its loaded source file.",
                field: edit.Field,
                expected: "Current trainer source"));
            return;
        }

        var currentSources = new List<ProjectFileReference> { primarySource };
        if (RequiresPersonalDataValidation(edit.Field)
            && SwShPokemonWorkflowService.ResolvePersonalDataSource(project) is { } personalSource)
        {
            currentSources.Add(new ProjectFileReference(
                GetSourceLayer(personalSource.GraphEntry),
                personalSource.GraphEntry.RelativePath));
        }

        if (edit.Sources.Count != currentSources.Count
            || currentSources.Any(source => !edit.Sources.Contains(source)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The trainer source layer or semantic lookup source changed after this edit was staged. Stage the edit again against the current sources.",
                field: edit.Field,
                expected: "Pending edit staged from the current trainer and personal data sources"));
        }
    }

    private static void ValidateFinalTrainerInvariants(
        OpenedProject project,
        SwShTrainersWorkflow workflow,
        IReadOnlySet<(int TrainerId, int Slot)> evRecords,
        IReadOnlySet<(int TrainerId, int Slot)> identityRecords,
        IReadOnlySet<(int TrainerId, int Slot)> abilityRecords,
        IReadOnlySet<(int TrainerId, int Slot)> genderRecords,
        IReadOnlySet<(int TrainerId, int Slot)> gigantamaxRecords,
        IReadOnlySet<int> rosterTrainerIds,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var identity in evRecords)
        {
            var pokemon = ResolveTrainerPokemon(workflow, identity);
            if (pokemon is null)
            {
                continue;
            }

            var total = pokemon.Evs.HP
                + pokemon.Evs.Attack
                + pokemon.Evs.Defense
                + pokemon.Evs.SpecialAttack
                + pokemon.Evs.SpecialDefense
                + pokemon.Evs.Speed;
            if (total > MaximumPokemonEvTotal)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {identity.TrainerId} slot {identity.Slot} has {total} total EVs; a Pokemon may use at most {MaximumPokemonEvTotal}.",
                    field: "evs",
                    expected: $"Combined EV total of {MaximumPokemonEvTotal} or less"));
            }
        }

        var semanticRecords = identityRecords
            .Concat(abilityRecords)
            .Concat(genderRecords)
            .Concat(gigantamaxRecords)
            .Distinct()
            .ToArray();
        var needsPersonalRecords = semanticRecords.Any(identity =>
            ResolveTrainerPokemon(workflow, identity)?.SpeciesId > 0);
        var personalRecords = !needsPersonalRecords
            ? []
            : LoadPersonalRecords(project, diagnostics);
        foreach (var identity in semanticRecords)
        {
            var pokemon = ResolveTrainerPokemon(workflow, identity);
            if (pokemon is null || pokemon.SpeciesId <= 0 || personalRecords.Count == 0)
            {
                continue;
            }

            if (!TryResolvePersonalRecord(
                    personalRecords,
                    pokemon.SpeciesId,
                    pokemon.Form,
                    out var personal,
                    out var formCount))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {identity.TrainerId} slot {identity.Slot} does not use a valid species and form from the loaded Sword and Shield personal data.",
                    field: SwShTrainersWorkflowService.FormField,
                    expected: formCount is null
                        ? "Species present in Sword and Shield personal data"
                        : $"Form 0 through {formCount.Value - 1}"));
                continue;
            }

            if (identityRecords.Contains(identity) && !personal!.IsPresentInGame)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {identity.TrainerId} slot {identity.Slot} uses a species and form not marked present in Sword and Shield.",
                    field: SwShTrainersWorkflowService.SpeciesIdField,
                    expected: "Species and form present in Sword and Shield personal data"));
            }

            if (abilityRecords.Contains(identity) && !IsAvailableAbilitySlot(personal!, pokemon.Ability))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {identity.TrainerId} slot {identity.Slot} uses an ability slot unavailable for its species and form.",
                    field: SwShTrainersWorkflowService.AbilityField,
                    expected: "Ability slot listed for the selected species and form"));
            }

            if (genderRecords.Contains(identity) && !IsCompatibleGender(personal!, pokemon.Gender))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {identity.TrainerId} slot {identity.Slot} uses a gender unavailable for its species and form.",
                    field: SwShTrainersWorkflowService.GenderField,
                    expected: "Random or a gender supported by the selected species and form"));
            }

            if (gigantamaxRecords.Contains(identity))
            {
                ValidateGigantamaxCapability(identity, pokemon, personal!, diagnostics);
            }
        }

        ValidateRosterSourceShape(project, workflow, rosterTrainerIds, diagnostics);
    }

    private static SwShTrainerPokemonRecord? ResolveTrainerPokemon(
        SwShTrainersWorkflow workflow,
        (int TrainerId, int Slot) identity)
    {
        return workflow.Trainers
            .FirstOrDefault(trainer => trainer.TrainerId == identity.TrainerId)
            ?.Team
            .FirstOrDefault(pokemon => pokemon.Slot == identity.Slot);
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
                "Trainer species, form, ability, gender, and Gigantamax validation requires the Sword and Shield personal data table.",
                field: SwShTrainersWorkflowService.SpeciesIdField,
                expected: SwShPokemonWorkflowService.PersonalDataPath));
            return [];
        }

        try
        {
            return SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainer semantic validation could not read personal data: {exception.Message}",
                field: SwShTrainersWorkflowService.SpeciesIdField,
                expected: "Readable Sword and Shield personal data table",
                file: source.GraphEntry.RelativePath));
            return [];
        }
    }

    private static bool TryResolvePersonalRecord(
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        int speciesId,
        int form,
        out SwShPersonalRecord? personal,
        out int? formCount)
    {
        personal = null;
        formCount = null;
        if (speciesId <= 0 || (uint)speciesId >= (uint)personalRecords.Count)
        {
            return false;
        }

        var basePersonal = personalRecords[speciesId];
        formCount = Math.Max(1, basePersonal.FormCount);
        if (form < 0 || form >= formCount)
        {
            return false;
        }

        personal = basePersonal;
        if (form > 0 && basePersonal.FormStatsIndex > 0)
        {
            var personalId = basePersonal.FormStatsIndex + form - 1;
            if ((uint)personalId >= (uint)personalRecords.Count)
            {
                personal = null;
                return false;
            }

            personal = personalRecords[personalId];
        }

        return true;
    }

    private static bool IsAvailableAbilitySlot(SwShPersonalRecord personal, int ability)
    {
        return ability switch
        {
            0 or 1 => personal.Ability1 != 0,
            2 => personal.Ability2 != 0,
            3 => personal.HiddenAbility != 0,
            _ => false,
        };
    }

    private static bool IsCompatibleGender(SwShPersonalRecord personal, int gender)
    {
        return gender switch
        {
            0 => true,
            1 => personal.GenderRatio is not 254 and not 255,
            2 => personal.GenderRatio is not 0 and not 255,
            _ => false,
        };
    }

    private static void ValidateGigantamaxCapability(
        (int TrainerId, int Slot) identity,
        SwShTrainerPokemonRecord pokemon,
        SwShPersonalRecord personal,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (pokemon.CanDynamax && personal.CanNotDynamax)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainer {identity.TrainerId} slot {identity.Slot} uses a species and form that cannot Dynamax.",
                field: SwShTrainersWorkflowService.CanDynamaxField,
                expected: "Can Dynamax disabled for this species and form"));
        }

        if (!pokemon.CanGigantamax)
        {
            return;
        }

        if (!pokemon.CanDynamax)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainer {identity.TrainerId} slot {identity.Slot} cannot Gigantamax while Can Dynamax is disabled.",
                field: SwShTrainersWorkflowService.CanGigantamaxField,
                expected: "Can Dynamax enabled or Can Gigantamax disabled"));
        }

        var isEligibleForm = pokemon.SpeciesId is not (25 or 52) || pokemon.Form == 0;
        if (personal.CanNotDynamax
            || !isEligibleForm
            || !SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(pokemon.SpeciesId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainer {identity.TrainerId} slot {identity.Slot} does not use a Gigantamax-capable Sword and Shield species and form.",
                field: SwShTrainersWorkflowService.CanGigantamaxField,
                expected: "Gigantamax-capable species and form or Can Gigantamax disabled"));
        }
    }

    private static void ValidateRosterSourceShape(
        OpenedProject project,
        SwShTrainersWorkflow workflow,
        IReadOnlySet<int> trainerIds,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var trainerId in trainerIds)
        {
            var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
            if (trainer is null)
            {
                continue;
            }

            var dataSource = SwShTrainersWorkflowService.ResolveWorkflowFile(project, trainer.Provenance.SourceFile);
            var teamSource = SwShTrainersWorkflowService.ResolveWorkflowFile(project, trainer.Provenance.TeamSourceFile);
            if (dataSource is null || teamSource is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {trainerId} roster editing requires matching trainer data and party files.",
                    field: SwShTrainersWorkflowService.SpeciesIdField,
                    expected: "Readable paired trainer data and party sources"));
                continue;
            }

            try
            {
                var data = SwShTrainerDataFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));
                var team = SwShTrainerTeamFile.Parse(File.ReadAllBytes(teamSource.AbsolutePath));
                if (data.Record.PokemonCount != team.Records.Count)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Trainer {trainerId} roster cannot be resized while its declared Pokemon count and party row count differ.",
                        field: SwShTrainersWorkflowService.SpeciesIdField,
                        expected: "Trainer data Pokemon count matching party rows"));
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {trainerId} roster sources could not be validated: {exception.Message}",
                    field: SwShTrainersWorkflowService.SpeciesIdField,
                    expected: "Readable Sword and Shield trainer data and party files"));
            }
        }
    }

    private static IReadOnlyList<PlannedFileWrite> CreatePlannedWrites(
        SwShTrainersWorkflow workflow,
        ProjectPaths paths,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var allEdits = edits
            .Concat(CreateImpliedPokemonCountEdits(workflow, edits))
            .ToArray();

        return allEdits
            .GroupBy(edit => GetTargetRelativePath(workflow, edit), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var targetRelativePath = group.Key;
                if (string.IsNullOrWhiteSpace(targetRelativePath))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending trainer edit does not include a valid target source file.",
                        expected: "Trainer data or party source"));
                    return null;
                }

                var targetPath = ResolveOutputPath(paths, targetRelativePath, diagnostics);
                if (targetPath is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Trainer apply target must stay inside the configured output root.",
                        file: targetRelativePath,
                        expected: "Output-root-contained target"));
                    return null;
                }

                var groupEdits = group.ToArray();
                var sources = groupEdits
                    .SelectMany(edit => edit.Sources)
                    .Distinct()
                    .ToArray();
                var reason = groupEdits.Length == 1
                    ? $"Apply pending Trainers edit: {groupEdits[0].Summary}"
                    : $"Apply {groupEdits.Length} pending Trainers edits.";
                reason = $"{reason} Edit fingerprint {ComputePendingEditFingerprint(groupEdits)}.";

                return new PlannedFileWrite(
                    targetRelativePath,
                    sources,
                    File.Exists(targetPath),
                    reason);
            })
            .Where(write => write is not null)
            .Select(write => write!)
            .OrderBy(write => write.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PendingEdit> CreateImpliedPokemonCountEdits(
        SwShTrainersWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var trainerIds = edits
            .Where(edit => SwShTrainersWorkflowService.IsTrainerPokemonField(edit.Field))
            .Select(edit => SwShTrainersWorkflowService.TryParseTeamRecordId(edit.RecordId, out var trainerId, out _)
                ? trainerId
                : -1)
            .Where(trainerId => trainerId >= 0)
            .Distinct()
            .ToArray();
        if (trainerIds.Length == 0)
        {
            return [];
        }

        var projectedWorkflow = OverlayPendingEdits(workflow, edits);
        var pendingEdits = new List<PendingEdit>();
        foreach (var trainerId in trainerIds)
        {
            var baseTrainer = workflow.Trainers.FirstOrDefault(trainer => trainer.TrainerId == trainerId);
            var projectedTrainer = projectedWorkflow.Trainers.FirstOrDefault(trainer => trainer.TrainerId == trainerId);
            if (baseTrainer is null || projectedTrainer is null)
            {
                continue;
            }

            var baseCount = GetOccupiedPokemonCount(baseTrainer);
            var projectedCount = GetOccupiedPokemonCount(projectedTrainer);
            if (baseCount == projectedCount)
            {
                continue;
            }

            pendingEdits.Add(new PendingEdit(
                TrainersEditDomain,
                $"Set {projectedTrainer.Name} Pokemon count to {projectedCount.ToString(CultureInfo.InvariantCulture)}.",
                [new ProjectFileReference(projectedTrainer.Provenance.SourceLayer, projectedTrainer.Provenance.SourceFile)],
                RecordId: projectedTrainer.TrainerId.ToString(CultureInfo.InvariantCulture),
                Field: SwShTrainersWorkflowService.PokemonCountField,
                NewValue: projectedCount.ToString(CultureInfo.InvariantCulture)));
        }

        return pendingEdits;
    }

    private static int GetOccupiedPokemonCount(SwShTrainerRecord trainer)
    {
        return trainer.Team.Count(pokemon => pokemon.SpeciesId > 0);
    }

    private static int? GetProjectedPokemonCount(SwShTrainersWorkflow projectedWorkflow, int trainerId)
    {
        var trainer = projectedWorkflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
        return trainer is null ? null : GetOccupiedPokemonCount(trainer);
    }

    private static string? GetTargetRelativePath(SwShTrainersWorkflow workflow, PendingEdit edit)
    {
        return GetSourceReference(workflow, edit)?.RelativePath;
    }

    private static ProjectFileReference? GetSourceReference(SwShTrainersWorkflow workflow, PendingEdit edit)
    {
        if (SwShTrainersWorkflowService.IsTrainerDataField(edit.Field)
            && int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var trainerId))
        {
            var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
            return trainer is null
                ? null
                : new ProjectFileReference(trainer.Provenance.SourceLayer, trainer.Provenance.SourceFile);
        }

        if (SwShTrainersWorkflowService.IsTrainerClassField(edit.Field)
            && int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var classId))
        {
            var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerClassId == classId);
            return trainer?.Provenance.ClassSourceFile is null || trainer.Provenance.ClassSourceLayer is null
                ? null
                : new ProjectFileReference(trainer.Provenance.ClassSourceLayer.Value, trainer.Provenance.ClassSourceFile);
        }

        if (SwShTrainersWorkflowService.IsTrainerPokemonField(edit.Field)
            && SwShTrainersWorkflowService.TryParseTeamRecordId(edit.RecordId, out trainerId, out _))
        {
            var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
            return trainer is null
                ? null
                : new ProjectFileReference(trainer.Provenance.TeamSourceLayer, trainer.Provenance.TeamSourceFile);
        }

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
                "Trainers apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainers apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(paths, out var stablePaths, out var stableRootFailure))
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
                "Trainers apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static string ComputePendingEditFingerprint(IReadOnlyList<PendingEdit> edits)
    {
        var canonical = new StringBuilder();
        var orderedEdits = edits
            .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
            .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
            .ThenBy(edit => edit.Field, StringComparer.Ordinal)
            .ThenBy(edit => edit.NewValue, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < orderedEdits.Length; index++)
        {
            var edit = orderedEdits[index];
            AppendFingerprintComponent(canonical, index.ToString(CultureInfo.InvariantCulture));
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

    private static byte[]? WriteTrainerClassEdits(
        SwShTrainersWorkflowService.WorkflowFileSource source,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var classFile = SwShTrainerClassFile.Parse(File.ReadAllBytes(source.AbsolutePath));
        var classEdits = edits
            .Select(edit => ToTrainerClassEdit(edit, diagnostics))
            .Where(edit => edit is not null)
            .Select(edit => edit!)
            .ToArray();

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? null
            : classFile.WriteEdits(classEdits);
    }

    private static byte[]? WriteTrainerDataEdits(
        SwShTrainersWorkflowService.WorkflowFileSource source,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var dataFile = SwShTrainerDataFile.Parse(File.ReadAllBytes(source.AbsolutePath));
        var trainerDataEdits = edits
            .Select(edit => ToTrainerDataEdit(edit, diagnostics))
            .Where(edit => edit is not null)
            .Select(edit => edit!)
            .ToArray();

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? null
            : dataFile.WriteEdits(trainerDataEdits);
    }

    private static byte[]? WriteTrainerTeamEdits(
        SwShTrainersWorkflowService.WorkflowFileSource source,
        IEnumerable<PendingEdit> edits,
        int? outputPokemonCount,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var teamFile = SwShTrainerTeamFile.Parse(File.ReadAllBytes(source.AbsolutePath));
        var pokemonEdits = edits
            .Select(edit => ToTrainerPokemonEdit(edit, diagnostics))
            .Where(edit => edit is not null)
            .Select(edit => edit!)
            .ToArray();

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? null
            : teamFile.WriteEdits(
                pokemonEdits
                    .Where(edit => outputPokemonCount is null || edit.Slot <= outputPokemonCount.Value)
                    .ToArray(),
                outputPokemonCount);
    }

    private static SwShTrainerDataEdit? ToTrainerDataEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var value = string.Equals(edit.Field, SwShTrainersWorkflowService.PokemonCountField, StringComparison.Ordinal)
            ? TryParseImpliedPokemonCount(edit.NewValue, diagnostics)
            : TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
        if (value is null)
        {
            return null;
        }

        var field = edit.Field switch
        {
            SwShTrainersWorkflowService.TrainerClassIdField => SwShTrainerDataField.ClassId,
            SwShTrainersWorkflowService.BattleTypeField => SwShTrainerDataField.BattleMode,
            SwShTrainersWorkflowService.TrainerItem1IdField => SwShTrainerDataField.Item1Id,
            SwShTrainersWorkflowService.TrainerItem2IdField => SwShTrainerDataField.Item2Id,
            SwShTrainersWorkflowService.TrainerItem3IdField => SwShTrainerDataField.Item3Id,
            SwShTrainersWorkflowService.TrainerItem4IdField => SwShTrainerDataField.Item4Id,
            SwShTrainersWorkflowService.AiFlagsField => SwShTrainerDataField.AiFlags,
            SwShTrainersWorkflowService.HealField => SwShTrainerDataField.Heal,
            SwShTrainersWorkflowService.MoneyField => SwShTrainerDataField.Money,
            SwShTrainersWorkflowService.GiftField => SwShTrainerDataField.Gift,
            SwShTrainersWorkflowService.PokemonCountField => SwShTrainerDataField.PokemonCount,
            _ => (SwShTrainerDataField?)null,
        };

        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        return new SwShTrainerDataEdit(field.Value, value.Value);
    }

    private static int? TryParseImpliedPokemonCount(
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue)
            || parsedValue < 0
            || parsedValue > SwShTrainerDataFile.MaximumPokemonCount)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainer Pokemon count must match the contiguous party size.",
                field: SwShTrainersWorkflowService.PokemonCountField,
                expected: "Safe trainer Pokemon count"));
            return null;
        }

        return parsedValue;
    }

    private static SwShTrainerClassEdit? ToTrainerClassEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var value = TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
        if (value is null)
        {
            return null;
        }

        var field = edit.Field switch
        {
            SwShTrainersWorkflowService.ClassBallIdField => SwShTrainerClassField.BallId,
            _ => (SwShTrainerClassField?)null,
        };

        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        return new SwShTrainerClassEdit(field.Value, value.Value);
    }

    private static SwShTrainerPokemonEdit? ToTrainerPokemonEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShTrainersWorkflowService.TryParseTeamRecordId(edit.RecordId, out _, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer party edit targets an invalid trainer slot.",
                field: "slot",
                expected: "Trainer party slot"));
            return null;
        }

        var value = TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
        if (value is null)
        {
            return null;
        }

        var field = edit.Field switch
        {
            SwShTrainersWorkflowService.SpeciesIdField => SwShTrainerPokemonField.SpeciesId,
            SwShTrainersWorkflowService.FormField => SwShTrainerPokemonField.Form,
            SwShTrainersWorkflowService.LevelField => SwShTrainerPokemonField.Level,
            SwShTrainersWorkflowService.HeldItemIdField => SwShTrainerPokemonField.HeldItemId,
            SwShTrainersWorkflowService.Move1IdField => SwShTrainerPokemonField.Move1Id,
            SwShTrainersWorkflowService.Move2IdField => SwShTrainerPokemonField.Move2Id,
            SwShTrainersWorkflowService.Move3IdField => SwShTrainerPokemonField.Move3Id,
            SwShTrainersWorkflowService.Move4IdField => SwShTrainerPokemonField.Move4Id,
            SwShTrainersWorkflowService.GenderField => SwShTrainerPokemonField.Gender,
            SwShTrainersWorkflowService.AbilityField => SwShTrainerPokemonField.Ability,
            SwShTrainersWorkflowService.NatureField => SwShTrainerPokemonField.Nature,
            SwShTrainersWorkflowService.EvHpField => SwShTrainerPokemonField.EvHp,
            SwShTrainersWorkflowService.EvAttackField => SwShTrainerPokemonField.EvAttack,
            SwShTrainersWorkflowService.EvDefenseField => SwShTrainerPokemonField.EvDefense,
            SwShTrainersWorkflowService.EvSpecialAttackField => SwShTrainerPokemonField.EvSpecialAttack,
            SwShTrainersWorkflowService.EvSpecialDefenseField => SwShTrainerPokemonField.EvSpecialDefense,
            SwShTrainersWorkflowService.EvSpeedField => SwShTrainerPokemonField.EvSpeed,
            SwShTrainersWorkflowService.DynamaxLevelField => SwShTrainerPokemonField.DynamaxLevel,
            SwShTrainersWorkflowService.CanGigantamaxField => SwShTrainerPokemonField.CanGigantamax,
            SwShTrainersWorkflowService.IvHpField => SwShTrainerPokemonField.IvHp,
            SwShTrainersWorkflowService.IvAttackField => SwShTrainerPokemonField.IvAttack,
            SwShTrainersWorkflowService.IvDefenseField => SwShTrainerPokemonField.IvDefense,
            SwShTrainersWorkflowService.IvSpecialAttackField => SwShTrainerPokemonField.IvSpecialAttack,
            SwShTrainersWorkflowService.IvSpecialDefenseField => SwShTrainerPokemonField.IvSpecialDefense,
            SwShTrainersWorkflowService.IvSpeedField => SwShTrainerPokemonField.IvSpeed,
            SwShTrainersWorkflowService.ShinyField => SwShTrainerPokemonField.Shiny,
            SwShTrainersWorkflowService.CanDynamaxField => SwShTrainerPokemonField.CanDynamax,
            _ => (SwShTrainerPokemonField?)null,
        };

        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        return new SwShTrainerPokemonEdit(slot, field.Value, value.Value);
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
                "Trainers apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainers rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private void WriteAllBytesAtomically(string targetPath, byte[] contents)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Trainer output has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(temporaryPath, contents);
            if (!File.ReadAllBytes(temporaryPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Trainer temporary output verification failed.");
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
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

    private static string CreateTrainerDataSummary(SwShTrainerRecord trainer, string field, int value)
    {
        var label = SwShTrainersWorkflowService.GetEditableField(field)?.Label ?? field;
        return $"Set {trainer.Name} {label.ToLowerInvariant()} to {value}.";
    }

    private static string CreateTrainerClassSummary(SwShTrainerRecord trainer, string field, int value)
    {
        var label = SwShTrainersWorkflowService.GetEditableField(field)?.Label ?? field;
        return $"Set {trainer.TrainerClass} {label.ToLowerInvariant()} to {value}.";
    }

    private static string CreateTrainerPokemonSummary(
        SwShTrainerRecord trainer,
        SwShTrainerPokemonRecord pokemon,
        string field,
        int value)
    {
        var label = SwShTrainersWorkflowService.GetEditableField(field)?.Label ?? field;
        return $"Set {trainer.Name} slot {pokemon.Slot} {label.ToLowerInvariant()} to {value}.";
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Trainer field '{field}' is not supported by the Trainers workflow yet.",
            field: "field",
            expected: "Supported trainer data, trainer class, or trainer party field");
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
            Domain: TrainersEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record TrainerOutput(
        string RelativePath,
        string AbsolutePath,
        byte[] Contents);
}
