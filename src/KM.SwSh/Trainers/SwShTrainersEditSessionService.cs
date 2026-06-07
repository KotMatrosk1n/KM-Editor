// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Trainers;

public sealed class SwShTrainersEditSessionService
{
    private const string TrainersEditDomain = "workflow.trainers";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShTrainersWorkflowService trainersWorkflowService;

    public SwShTrainersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTrainersWorkflowService? trainersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.trainersWorkflowService = trainersWorkflowService ?? new SwShTrainersWorkflowService();
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

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = trainersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditTrainers(project, workflow, diagnostics))
        {
            return new SwShTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var selectedTrainer = workflow.Trainers.FirstOrDefault(trainer => trainer.TrainerId == trainerId);
        if (selectedTrainer is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainer {trainerId} is not present in the loaded Trainers workflow.",
                field: "trainerId",
                expected: "Existing trainer record"));
            return new SwShTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedTrainer, slot, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingTrainerEdit(currentSession, pendingEdit);

        return new SwShTrainersEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = trainersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditTrainers(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
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

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();

        if (session.PendingEdits.Count == 0)
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

        var project = projectWorkspaceService.Open(paths);
        var workflow = trainersWorkflowService.Load(project);
        var writes = CreatePlannedWrites(workflow, paths, session.PendingEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target file{(writes.Count == 1 ? string.Empty : "s")}."));

        return new ChangePlan(session.Id, writes, diagnostics);
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
                expected: "Current reviewed Trainers change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = trainersWorkflowService.Load(project);
        var pendingOutputs = new List<TrainerOutput>();

        foreach (var editGroup in session.PendingEdits.GroupBy(edit => GetTargetRelativePath(workflow, edit), StringComparer.OrdinalIgnoreCase))
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
                var output = SwShTrainersWorkflowService.IsTrainerDataField(editGroup.First().Field)
                    ? WriteTrainerDataEdits(source, editGroup, diagnostics)
                    : WriteTrainerTeamEdits(source, editGroup, diagnostics);

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

        foreach (var output in pendingOutputs)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output.AbsolutePath)!);
                File.WriteAllBytes(output.AbsolutePath, output.Contents);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, output.RelativePath));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer output file could not be written: {exception.Message}",
                    file: output.RelativePath,
                    expected: "Writable output root"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer output file could not be written: {exception.Message}",
                    file: output.RelativePath,
                    expected: "Writable output root"));
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

        if (SwShTrainersWorkflowService.IsTrainerDataField(edit.Field))
        {
            ValidateTrainerDataEdit(workflow, edit, diagnostics);
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
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var trainerId)
            || workflow.Trainers.All(trainer => trainer.TrainerId != trainerId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit targets a record that is not loaded.",
                field: "trainerId",
                expected: "Existing trainer record"));
            return;
        }

        TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
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

        TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
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

            return CreateTrainerPokemonPendingEdit(selectedTrainer, pokemon, normalizedField, value, diagnostics);
        }

        diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
        return null;
    }

    private static PendingEdit? CreateTrainerDataPendingEdit(
        SwShTrainerRecord trainer,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
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

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue)
            || parsedValue < (editableField.MinimumValue ?? int.MinValue)
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

    private static EditSession ReplacePendingTrainerEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameTrainerEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameTrainerEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShTrainersWorkflow OverlayPendingEdits(
        SwShTrainersWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShTrainersWorkflow OverlayPendingEdit(SwShTrainersWorkflow workflow, PendingEdit edit)
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
                        ? OverlayTrainerDataField(trainer, edit.Field!, value)
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
                                    ? OverlayTrainerPokemonField(pokemon, edit.Field!, value)
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
        SwShTrainerRecord trainer,
        string field,
        int value)
    {
        return field switch
        {
            SwShTrainersWorkflowService.TrainerClassIdField => trainer with
            {
                TrainerClassId = value,
                TrainerClass = $"Class {value}",
            },
            SwShTrainersWorkflowService.BattleTypeField => trainer with
            {
                BattleTypeValue = value,
                BattleType = value switch
                {
                    0 => "Singles",
                    1 => "Doubles",
                    2 => "Multi",
                    _ => $"Mode {value}",
                },
            },
            _ => trainer,
        };
    }

    private static SwShTrainerPokemonRecord OverlayTrainerPokemonField(
        SwShTrainerPokemonRecord pokemon,
        string field,
        int value)
    {
        return field switch
        {
            SwShTrainersWorkflowService.SpeciesIdField => pokemon with
            {
                SpeciesId = value,
                Species = $"Species {value}",
            },
            SwShTrainersWorkflowService.LevelField => pokemon with { Level = value },
            SwShTrainersWorkflowService.HeldItemIdField => pokemon with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : $"Item {value}",
            },
            SwShTrainersWorkflowService.Move1IdField => OverlayMove(pokemon, 0, value),
            SwShTrainersWorkflowService.Move2IdField => OverlayMove(pokemon, 1, value),
            SwShTrainersWorkflowService.Move3IdField => OverlayMove(pokemon, 2, value),
            SwShTrainersWorkflowService.Move4IdField => OverlayMove(pokemon, 3, value),
            _ => pokemon,
        };
    }

    private static SwShTrainerPokemonRecord OverlayMove(
        SwShTrainerPokemonRecord pokemon,
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
        moves[moveIndex] = value == 0 ? "None" : $"Move {value}";

        return pokemon with
        {
            MoveIds = moveIds,
            Moves = moves,
        };
    }

    private static IReadOnlyList<PlannedFileWrite> CreatePlannedWrites(
        SwShTrainersWorkflow workflow,
        ProjectPaths paths,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return edits
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

                var targetPath = SwShTrainersWorkflowService.ResolveOutputPath(paths, targetRelativePath);
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
                    .Select(edit => GetSourceReference(workflow, edit))
                    .Where(source => source is not null)
                    .Select(source => source!)
                    .Distinct()
                    .ToArray();
                var reason = groupEdits.Length == 1
                    ? $"Apply pending Trainers edit: {groupEdits[0].Summary}"
                    : $"Apply {groupEdits.Length} pending Trainers edits.";

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

        var targetPath = SwShTrainersWorkflowService.ResolveOutputPath(paths, targetRelativePath);
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
            : teamFile.WriteEdits(pokemonEdits);
    }

    private static SwShTrainerDataEdit? ToTrainerDataEdit(
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
            SwShTrainersWorkflowService.TrainerClassIdField => SwShTrainerDataField.ClassId,
            SwShTrainersWorkflowService.BattleTypeField => SwShTrainerDataField.BattleMode,
            _ => (SwShTrainerDataField?)null,
        };

        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        return new SwShTrainerDataEdit(field.Value, value.Value);
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
            SwShTrainersWorkflowService.LevelField => SwShTrainerPokemonField.Level,
            SwShTrainersWorkflowService.HeldItemIdField => SwShTrainerPokemonField.HeldItemId,
            SwShTrainersWorkflowService.Move1IdField => SwShTrainerPokemonField.Move1Id,
            SwShTrainersWorkflowService.Move2IdField => SwShTrainerPokemonField.Move2Id,
            SwShTrainersWorkflowService.Move3IdField => SwShTrainerPokemonField.Move3Id,
            SwShTrainersWorkflowService.Move4IdField => SwShTrainerPokemonField.Move4Id,
            _ => (SwShTrainerPokemonField?)null,
        };

        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        return new SwShTrainerPokemonEdit(slot, field.Value, value.Value);
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

    private static string CreateTrainerDataSummary(SwShTrainerRecord trainer, string field, int value)
    {
        var label = SwShTrainersWorkflowService.GetEditableField(field)?.Label ?? field;
        return $"Set {trainer.Name} {label.ToLowerInvariant()} to {value}.";
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
            expected: "trainerClassId, battleType, speciesId, level, heldItemId, or move IDs");
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
