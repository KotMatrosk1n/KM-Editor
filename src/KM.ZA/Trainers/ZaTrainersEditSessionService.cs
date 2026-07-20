// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Trainers;

internal sealed class ZaTrainersEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaTrainersWorkflowService trainersWorkflowService;

    public ZaTrainersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaTrainersWorkflowService? trainersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.trainersWorkflowService = trainersWorkflowService ?? new ZaTrainersWorkflowService(this.fileSource);
    }

    public ZaTrainersEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int trainerId,
        int? slot,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = trainersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits, diagnostics);

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.TrainersDomain,
                diagnostics))
        {
            return new ZaTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
        if (trainer is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainer {trainerId} is not present in the loaded Trainers workflow.",
                ZaEditSessionSupport.TrainersDomain,
                field: "trainerId",
                expected: "Existing trainer record"));
            return new ZaTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, trainer, slot, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        var projectedWorkflow = OverlayPendingEdits(
            project,
            loadedWorkflow,
            updatedSession.PendingEdits,
            diagnostics);
        ValidateFinalSpeciesFormPairs(loadedWorkflow, projectedWorkflow, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaTrainersEditResult(workflow, currentSession, diagnostics);
        }

        return new ZaTrainersEditResult(projectedWorkflow, updatedSession, diagnostics);
    }

    public ZaTrainersEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaTrainerFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = trainersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits, diagnostics);

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.TrainersDomain,
                diagnostics))
        {
            return new ZaTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trainer batch update is missing a field or value.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "updates",
                    expected: "Complete trainer field update"));
                continue;
            }

            var trainer = effectiveWorkflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == update.TrainerId);
            if (trainer is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {update.TrainerId} is not present in the loaded Trainers workflow.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "trainerId",
                    expected: "Existing trainer record"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(
                effectiveWorkflow,
                trainer,
                update.Slot,
                update.Field,
                update.Value,
                diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
        }

        var projectedWorkflow = OverlayPendingEdits(
            project,
            loadedWorkflow,
            updatedSession.PendingEdits,
            diagnostics);
        ValidateFinalSpeciesFormPairs(loadedWorkflow, projectedWorkflow, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaTrainersEditResult(workflow, currentSession, diagnostics);
        }

        return new ZaTrainersEditResult(projectedWorkflow, updatedSession, diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = trainersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.TrainersDomain,
            diagnostics);

        var effectiveWorkflow = workflow;
        var validEdits = new List<PendingEdit>();
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(effectiveWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                validEdits.Add(edit);
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        var projectedWorkflow = OverlayPendingEdits(project, workflow, validEdits, diagnostics);
        ValidateFinalSpeciesFormPairs(workflow, projectedWorkflow, diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Trainers change is valid.",
                ZaEditSessionSupport.TrainersDomain));
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

        var validation = Validate(paths, session);
        return ZaEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            ZaEditSessionSupport.TrainersDomain,
            ZaDataPaths.TrainerDataArray,
            "Trainers",
            validation.Diagnostics,
            outputMode);
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
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                ZaEditSessionSupport.TrainersDomain,
                expected: "Current reviewed Trainers change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, ZaDataPaths.TrainerDataArray);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, diagnostics);
            }

            foreach (var row in rows)
            {
                row.NormalizeEmptyPokemon();
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(paths, ZaDataPaths.TrainerDataArray, WriteRows(rows), outputMode);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.TrainerDataArray, outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Trainers", outputMode),
                ZaEditSessionSupport.TrainersDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainers output could not be written: {exception.Message}",
                ZaEditSessionSupport.TrainersDomain,
                file: $"romfs/{ZaDataPaths.TrainerDataArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaTrainersWorkflow workflow,
        ZaTrainerRecord trainer,
        int? slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, normalizedField, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.TrainersDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (IsPokemonField(normalizedField))
        {
            if (slot is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trainer Pokemon edits require a Pokemon slot.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Existing trainer Pokemon slot"));
                return null;
            }

            var pokemon = trainer.Team.FirstOrDefault(candidate => candidate.Slot == slot.Value);
            if (pokemon is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {trainer.TrainerId} does not have Pokemon slot {slot.Value}.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Existing trainer Pokemon slot"));
                return null;
            }

            if (pokemon.SpeciesId <= 0 && !IsSpeciesFormField(normalizedField))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trainer Pokemon slot is empty. Set a Pokemon species before editing slot details.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: normalizedField,
                    expected: "Occupied trainer Pokemon slot"));
                return null;
            }

            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidateTeamOrder(
                OverlayTrainerPokemon(
                    trainer,
                    slot.Value,
                    normalizedField,
                    parsedValue.Value,
                    workflow.PokemonAvailability),
                diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) != errorCount)
            {
                return null;
            }

            return ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.TrainersDomain,
                $"Set {trainer.Name} slot {slot.Value} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
                new ProjectFileReference(trainer.Provenance.TeamSourceLayer, trainer.Provenance.TeamSourceFile),
                CreateTeamRecordId(trainer.TrainerId, slot.Value),
                normalizedField,
                parsedValue.Value.ToString(CultureInfo.InvariantCulture));
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.TrainersDomain,
            $"Set {trainer.Name} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(trainer.Provenance.SourceLayer, trainer.Provenance.SourceFile),
            trainer.TrainerId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        ZaTrainersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.TrainersDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Trainers.",
                ZaEditSessionSupport.TrainersDomain,
                expected: ZaEditSessionSupport.TrainersDomain));
            return;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        ZaTrainerRecord? pokemonTrainer = null;
        int? pokemonSlot = null;
        if (IsPokemonField(edit.Field))
        {
            if (!TryParseTeamRecordId(edit.RecordId, out var trainerId, out var slot))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer Pokemon edit targets an invalid slot.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Trainer Pokemon slot"));
                return;
            }

            var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
            if (trainer is null || trainer.Team.All(pokemon => pokemon.Slot != slot))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer Pokemon edit targets a slot that is not loaded.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Existing trainer Pokemon slot"));
                return;
            }

            pokemonTrainer = trainer;
            pokemonSlot = slot;
        }
        else
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var trainerId)
                || workflow.Trainers.All(candidate => candidate.TrainerId != trainerId))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer edit targets a record that is not loaded.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "trainerId",
                    expected: "Existing trainer record"));
                return;
            }
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.TrainersDomain,
            diagnostics);
        if (parsedValue is not null)
        {
            if (pokemonTrainer is not null && pokemonSlot is not null)
            {
                if (pokemonTrainer.Team.First(candidate => candidate.Slot == pokemonSlot.Value).SpeciesId <= 0
                    && !IsSpeciesFormField(edit.Field))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Trainer Pokemon slot is empty. Set a Pokemon species before editing slot details.",
                        ZaEditSessionSupport.TrainersDomain,
                        field: edit.Field,
                        expected: "Occupied trainer Pokemon slot"));
                    return;
                }

                ValidateTeamOrder(
                    OverlayTrainerPokemon(
                        pokemonTrainer,
                        pokemonSlot.Value,
                        edit.Field,
                        parsedValue.Value,
                        workflow.PokemonAvailability),
                    diagnostics);
            }
        }
    }

    private static void ValidateFinalSpeciesFormPairs(
        ZaTrainersWorkflow sourceWorkflow,
        ZaTrainersWorkflow projectedWorkflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var projectedTrainersById = projectedWorkflow.Trainers.ToDictionary(trainer => trainer.TrainerId);

        foreach (var sourceTrainer in sourceWorkflow.Trainers)
        {
            if (!projectedTrainersById.TryGetValue(sourceTrainer.TrainerId, out var projectedTrainer))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {sourceTrainer.TrainerId} is missing from the projected Trainers workflow.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "trainerId",
                    expected: "Stable projected trainer identity"));
                continue;
            }

            var projectedPokemonBySlot = projectedTrainer.Team.ToDictionary(pokemon => pokemon.Slot);
            foreach (var sourcePokemon in sourceTrainer.Team)
            {
                if (!projectedPokemonBySlot.TryGetValue(sourcePokemon.Slot, out var projectedPokemon))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{sourceTrainer.Name} slot {sourcePokemon.Slot} is missing from the projected Trainers workflow.",
                        ZaEditSessionSupport.TrainersDomain,
                        file: sourceTrainer.Provenance.TeamSourceFile,
                        field: "slot",
                        expected: "Stable projected trainer Pokémon slot identity"));
                    continue;
                }

                ZaSpeciesFormPairValidation.ValidateChangedPair(
                    sourceWorkflow.PokemonAvailability,
                    sourcePokemon.SpeciesId,
                    sourcePokemon.Form,
                    projectedPokemon.SpeciesId,
                    projectedPokemon.Form,
                    ZaEditSessionSupport.TrainersDomain,
                    $"{sourceTrainer.Name} slot {sourcePokemon.Slot}",
                    diagnostics,
                    sourceTrainer.Provenance.TeamSourceFile,
                    ZaTrainersWorkflowService.FormField);
            }
        }
    }

    private ZaTrainersWorkflow OverlayPendingEdits(
        OpenedProject project,
        ZaTrainersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        var pendingEdits = edits
            .Where(edit =>
                string.Equals(edit.Domain, ZaEditSessionSupport.TrainersDomain, StringComparison.Ordinal)
                && int.TryParse(
                    edit.NewValue,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out _))
            .ToArray();

        if (pendingEdits.Length == 0)
        {
            return workflow;
        }

        try
        {
            var overlayDiagnostics = new List<ValidationDiagnostic>();
            var source = fileSource.Read(project, ZaDataPaths.TrainerDataArray);
            var labels = ZaTextLabelLookup.Load(project, fileSource, overlayDiagnostics, project.Paths);
            var spriteLabels = ZaTextLabelLookup.Load(project, fileSource, overlayDiagnostics);
            var abilityResolver = ZaTrainerAbilityResolver.Load(project, fileSource, labels, overlayDiagnostics);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in pendingEdits)
            {
                ApplyEdit(rows, edit, overlayDiagnostics);
            }

            if (overlayDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                if (diagnostics is not null)
                {
                    foreach (var diagnostic in overlayDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    {
                        diagnostics.Add(diagnostic);
                    }
                }

                return workflow;
            }

            var overlaySource = source with { Bytes = WriteRows(rows) };
            var trainersById = ZaTrainersWorkflowService
                .LoadRecords(overlaySource, labels, spriteLabels, abilityResolver)
                .Select(trainer => ZaTrainersWorkflowService.WithPokemonFormOptions(
                    trainer,
                    workflow.PokemonAvailability))
                .ToDictionary(trainer => trainer.TrainerId);

            return workflow with
            {
                Trainers = workflow.Trainers
                    .Select(trainer => trainersById.TryGetValue(trainer.TrainerId, out var updatedTrainer) ? updatedTrainer : trainer)
                    .ToArray(),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics?.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainers pending changes could not be previewed: {exception.Message}",
                ZaEditSessionSupport.TrainersDomain,
                file: $"romfs/{ZaDataPaths.TrainerDataArray}",
                expected: "Readable Pokemon Legends Z-A trainer source"));
            return workflow;
        }
    }

    private static ZaTrainersWorkflow OverlayPendingEdit(ZaTrainersWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.TrainersDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        if (IsPokemonField(edit.Field))
        {
            if (!TryParseTeamRecordId(edit.RecordId, out var trainerId, out var slot))
            {
                return workflow;
            }

            return workflow with
            {
                Trainers = workflow.Trainers
                    .Select(trainer => trainer.TrainerId == trainerId
                        ? OverlayTrainerPokemon(
                            trainer,
                            slot,
                            edit.Field,
                            value,
                            workflow.PokemonAvailability)
                        : trainer)
                    .ToArray(),
            };
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var targetTrainerId))
        {
            return workflow;
        }

        return workflow with
        {
            Trainers = workflow.Trainers
                .Select(trainer => trainer.TrainerId == targetTrainerId ? OverlayTrainer(trainer, edit.Field, value) : trainer)
                .ToArray(),
        };
    }

    private static ZaTrainerRecord OverlayTrainer(ZaTrainerRecord trainer, string? field, int value)
    {
        return field switch
        {
            ZaTrainersWorkflowService.RankField => trainer with { Rank = value },
            ZaTrainersWorkflowService.MoneyField => trainer with { Money = value },
            ZaTrainersWorkflowService.MegaEvolutionField => trainer with
            {
                MegaEvolution = value != 0,
                BattleType = value != 0 ? "Mega Evolution" : "Trainer Battle",
            },
            ZaTrainersWorkflowService.LastHandField => trainer with { LastHand = value != 0 },
            ZaTrainersWorkflowService.AiFlagsField => trainer with
            {
                AiFlags = value,
                AiFlagStates = ZaTrainersWorkflowService.CreateAiStates(value),
            },
            _ => trainer,
        };
    }

    private static ZaTrainerRecord OverlayTrainerPokemon(
        ZaTrainerRecord trainer,
        int slot,
        string? field,
        int value,
        ZaPokemonAvailability pokemonAvailability)
    {
        return trainer with
        {
            Team = trainer.Team
                .Select(pokemon => pokemon.Slot == slot
                    ? OverlayPokemon(pokemon, field, value, pokemonAvailability)
                    : pokemon)
                .ToArray(),
        };
    }

    private static ZaTrainerPokemonRecord OverlayPokemon(
        ZaTrainerPokemonRecord pokemon,
        string? field,
        int value,
        ZaPokemonAvailability pokemonAvailability)
    {
        ZaTrainerPokemonRecord projectedPokemon;
        if (string.Equals(field, ZaTrainersWorkflowService.SpeciesIdField, StringComparison.Ordinal) && value == 0)
        {
            projectedPokemon = CreateEmptyPokemonRecord(pokemon.Slot);
        }
        else
        {
            projectedPokemon = field switch
            {
                ZaTrainersWorkflowService.SpeciesIdField => pokemon with
                {
                    SpeciesId = value,
                    Species = value == 0 ? "None" : ZaLabels.Pokemon(value),
                },
                ZaTrainersWorkflowService.FormField => pokemon with { Form = value },
                ZaTrainersWorkflowService.LevelField => pokemon with { Level = value },
                ZaTrainersWorkflowService.HeldItemIdField => pokemon with
                {
                    HeldItemId = value,
                    HeldItem = value > 0 ? ZaLabels.Item(value) : null,
                },
                ZaTrainersWorkflowService.Move1IdField => OverlayMove(pokemon, 0, value),
                ZaTrainersWorkflowService.Move2IdField => OverlayMove(pokemon, 1, value),
                ZaTrainersWorkflowService.Move3IdField => OverlayMove(pokemon, 2, value),
                ZaTrainersWorkflowService.Move4IdField => OverlayMove(pokemon, 3, value),
                ZaTrainersWorkflowService.GenderField => pokemon with { Gender = value, GenderLabel = ZaTrainersWorkflowService.FormatGender(value) },
                ZaTrainersWorkflowService.AbilityField => pokemon with
                {
                    Ability = value,
                    AbilityLabel = pokemon.AbilityOptions.FirstOrDefault(option => option.Value == value)?.Label
                        ?? $"Ability mode {value.ToString(CultureInfo.InvariantCulture)}",
                },
                ZaTrainersWorkflowService.NatureField => pokemon with { Nature = value, NatureLabel = ZaTrainersWorkflowService.FormatNature(value) },
                ZaTrainersWorkflowService.EvHpField => pokemon with { Evs = pokemon.Evs with { HP = value } },
                ZaTrainersWorkflowService.EvAttackField => pokemon with { Evs = pokemon.Evs with { Attack = value } },
                ZaTrainersWorkflowService.EvDefenseField => pokemon with { Evs = pokemon.Evs with { Defense = value } },
                ZaTrainersWorkflowService.EvSpecialAttackField => pokemon with { Evs = pokemon.Evs with { SpecialAttack = value } },
                ZaTrainersWorkflowService.EvSpecialDefenseField => pokemon with { Evs = pokemon.Evs with { SpecialDefense = value } },
                ZaTrainersWorkflowService.EvSpeedField => pokemon with { Evs = pokemon.Evs with { Speed = value } },
                ZaTrainersWorkflowService.IvHpField => pokemon with { Ivs = pokemon.Ivs with { HP = value } },
                ZaTrainersWorkflowService.IvAttackField => pokemon with { Ivs = pokemon.Ivs with { Attack = value } },
                ZaTrainersWorkflowService.IvDefenseField => pokemon with { Ivs = pokemon.Ivs with { Defense = value } },
                ZaTrainersWorkflowService.IvSpecialAttackField => pokemon with { Ivs = pokemon.Ivs with { SpecialAttack = value } },
                ZaTrainersWorkflowService.IvSpecialDefenseField => pokemon with { Ivs = pokemon.Ivs with { SpecialDefense = value } },
                ZaTrainersWorkflowService.IvSpeedField => pokemon with { Ivs = pokemon.Ivs with { Speed = value } },
                ZaTrainersWorkflowService.ShinyField => pokemon with { Shiny = value != 0 },
                _ => pokemon,
            };
        }

        return IsSpeciesFormField(field)
            ? ZaTrainersWorkflowService.WithFormOptions(projectedPokemon, pokemonAvailability)
            : projectedPokemon;
    }

    private static ZaTrainerPokemonRecord CreateEmptyPokemonRecord(int slot)
    {
        return new ZaTrainerPokemonRecord(
            slot,
            0,
            "None",
            0,
            1,
            0,
            null,
            [0, 0, 0, 0],
            ["None", "None", "None", "None"],
            -1,
            ZaTrainersWorkflowService.FormatGender(-1),
            0,
            "Game default / random",
            -1,
            ZaTrainersWorkflowService.FormatNature(-1),
            new ZaTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0),
            new ZaTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0),
            false)
        {
            AbilityOptions = Array.Empty<ZaTrainerEditableFieldOption>(),
        };
    }

    private static ZaTrainerPokemonRecord OverlayMove(ZaTrainerPokemonRecord pokemon, int moveIndex, int value)
    {
        var moveIds = pokemon.MoveIds.ToList();
        var moves = pokemon.Moves.ToList();
        while (moveIds.Count <= moveIndex)
        {
            moveIds.Add(0);
            moves.Add("None");
        }

        moveIds[moveIndex] = value;
        moves[moveIndex] = value == 0 ? "None" : ZaLabels.Move(value);

        return pokemon with
        {
            MoveIds = moveIds,
            Moves = moves,
        };
    }

    private static void ValidateTeamOrder(
        ZaTrainerRecord trainer,
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
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trainer Pokemon slots must be filled in order. Fill the previous slot before adding this Pokemon, or clear later slots first.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: ZaTrainersWorkflowService.SpeciesIdField,
                    expected: "Contiguous trainer Pokemon slots"));
                return;
            }
        }
    }

    private static void ApplyEdit(
        IReadOnlyList<TrainerRow> rows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.TrainersDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit is not valid for apply.",
                ZaEditSessionSupport.TrainersDomain,
                expected: "Valid trainer edit"));
            return;
        }

        if (IsPokemonField(edit.Field))
        {
            if (!TryParseTeamRecordId(edit.RecordId, out var trainerId, out var slot))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer Pokemon edit target is invalid.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Trainer Pokemon slot"));
                return;
            }

            var trainerRow = rows.ElementAtOrDefault(trainerId);
            if (trainerRow is null || (uint)slot >= (uint)TrainerRow.MaximumPartySize)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer Pokemon edit target is not present in the source array.",
                    ZaEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Existing source trainer Pokemon slot"));
                return;
            }

            var pokemon = trainerRow.GetOrCreatePokemon(slot);
            ApplyPokemonField(pokemon, edit.Field, value);
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var targetTrainerId))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit target is invalid.",
                ZaEditSessionSupport.TrainersDomain,
                field: "trainerId",
                expected: "Trainer ID"));
            return;
        }

        var row = rows.ElementAtOrDefault(targetTrainerId);
        if (row is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit target is not present in the source array.",
                ZaEditSessionSupport.TrainersDomain,
                field: "trainerId",
                expected: "Existing source trainer row"));
            return;
        }

        ApplyTrainerField(row, edit.Field, value);
    }

    private static void ApplyTrainerField(TrainerRow row, string? field, int value)
    {
        switch (field)
        {
            case ZaTrainersWorkflowService.RankField:
                row.Rank = checked((sbyte)value);
                break;
            case ZaTrainersWorkflowService.MoneyField:
                row.MoneyRate = checked((byte)value);
                break;
            case ZaTrainersWorkflowService.MegaEvolutionField:
                row.MegaEvolution = value != 0;
                break;
            case ZaTrainersWorkflowService.LastHandField:
                row.LastHand = value != 0;
                break;
            case ZaTrainersWorkflowService.AiFlagsField:
                row.SetAiFlags(value);
                break;
        }
    }

    private static void ApplyPokemonField(PokemonRow row, string? field, int value)
    {
        switch (field)
        {
            case ZaTrainersWorkflowService.SpeciesIdField:
                if (value == 0)
                {
                    row.Clear();
                    break;
                }

                row.SpeciesId = checked((ushort)value);
                break;
            case ZaTrainersWorkflowService.FormField:
                row.FormId = checked((short)value);
                break;
            case ZaTrainersWorkflowService.LevelField:
                row.Level = value;
                break;
            case ZaTrainersWorkflowService.HeldItemIdField:
                row.Item = value;
                break;
            case ZaTrainersWorkflowService.Move1IdField:
                row.SetMove(0, value);
                break;
            case ZaTrainersWorkflowService.Move2IdField:
                row.SetMove(1, value);
                break;
            case ZaTrainersWorkflowService.Move3IdField:
                row.SetMove(2, value);
                break;
            case ZaTrainersWorkflowService.Move4IdField:
                row.SetMove(3, value);
                break;
            case ZaTrainersWorkflowService.GenderField:
                row.Sex = value;
                break;
            case ZaTrainersWorkflowService.AbilityField:
                row.Ability = value;
                break;
            case ZaTrainersWorkflowService.NatureField:
                row.Nature = value;
                break;
            case ZaTrainersWorkflowService.EvHpField:
                row.Evs = (row.Evs ?? StatRow.Zero) with { Hp = value };
                break;
            case ZaTrainersWorkflowService.EvAttackField:
                row.Evs = (row.Evs ?? StatRow.Zero) with { Atk = value };
                break;
            case ZaTrainersWorkflowService.EvDefenseField:
                row.Evs = (row.Evs ?? StatRow.Zero) with { Def = value };
                break;
            case ZaTrainersWorkflowService.EvSpecialAttackField:
                row.Evs = (row.Evs ?? StatRow.Zero) with { SpAtk = value };
                break;
            case ZaTrainersWorkflowService.EvSpecialDefenseField:
                row.Evs = (row.Evs ?? StatRow.Zero) with { SpDef = value };
                break;
            case ZaTrainersWorkflowService.EvSpeedField:
                row.Evs = (row.Evs ?? StatRow.Zero) with { Agi = value };
                break;
            case ZaTrainersWorkflowService.IvHpField:
                row.Ivs = (row.Ivs ?? StatRow.Zero) with { Hp = value };
                break;
            case ZaTrainersWorkflowService.IvAttackField:
                row.Ivs = (row.Ivs ?? StatRow.Zero) with { Atk = value };
                break;
            case ZaTrainersWorkflowService.IvDefenseField:
                row.Ivs = (row.Ivs ?? StatRow.Zero) with { Def = value };
                break;
            case ZaTrainersWorkflowService.IvSpecialAttackField:
                row.Ivs = (row.Ivs ?? StatRow.Zero) with { SpAtk = value };
                break;
            case ZaTrainersWorkflowService.IvSpecialDefenseField:
                row.Ivs = (row.Ivs ?? StatRow.Zero) with { SpDef = value };
                break;
            case ZaTrainersWorkflowService.IvSpeedField:
                row.Ivs = (row.Ivs ?? StatRow.Zero) with { Agi = value };
                break;
            case ZaTrainersWorkflowService.ShinyField:
                row.RareType = value == 0 ? 0 : 2;
                break;
        }
    }

    private static IReadOnlyList<TrainerRow> ReadRows(byte[] bytes)
    {
        var table = ZaTrainerTable.GetRootAsZaTrainerTable(new ByteBuffer(bytes));
        var rows = new List<TrainerRow>();
        for (var index = 0; index < table.ValueLength; index++)
        {
            var row = table.Value(index);
            if (row is not null)
            {
                rows.Add(TrainerRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteRows(IReadOnlyList<TrainerRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = ZaTrainerTable.CreateValueVector(builder, offsets);
        var root = ZaTrainerTable.Create(builder, vector);
        ZaTrainerTable.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static bool IsPokemonField(string? field)
    {
        return field is
            ZaTrainersWorkflowService.SpeciesIdField or
            ZaTrainersWorkflowService.FormField or
            ZaTrainersWorkflowService.LevelField or
            ZaTrainersWorkflowService.HeldItemIdField or
            ZaTrainersWorkflowService.Move1IdField or
            ZaTrainersWorkflowService.Move2IdField or
            ZaTrainersWorkflowService.Move3IdField or
            ZaTrainersWorkflowService.Move4IdField or
            ZaTrainersWorkflowService.GenderField or
            ZaTrainersWorkflowService.AbilityField or
            ZaTrainersWorkflowService.NatureField or
            ZaTrainersWorkflowService.EvHpField or
            ZaTrainersWorkflowService.EvAttackField or
            ZaTrainersWorkflowService.EvDefenseField or
            ZaTrainersWorkflowService.EvSpecialAttackField or
            ZaTrainersWorkflowService.EvSpecialDefenseField or
            ZaTrainersWorkflowService.EvSpeedField or
            ZaTrainersWorkflowService.IvHpField or
            ZaTrainersWorkflowService.IvAttackField or
            ZaTrainersWorkflowService.IvDefenseField or
            ZaTrainersWorkflowService.IvSpecialAttackField or
            ZaTrainersWorkflowService.IvSpecialDefenseField or
            ZaTrainersWorkflowService.IvSpeedField or
            ZaTrainersWorkflowService.ShinyField;
    }

    private static bool IsSpeciesFormField(string? field)
    {
        return field is
            ZaTrainersWorkflowService.SpeciesIdField or
            ZaTrainersWorkflowService.FormField;
    }

    private static string CreateTeamRecordId(int trainerId, int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{trainerId}:{slot}");
    }

    private static bool TryParseTeamRecordId(string? recordId, out int trainerId, out int slot)
    {
        trainerId = -1;
        slot = -1;

        var parts = recordId?.Split(':');
        return parts is { Length: 2 }
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out trainerId)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && trainerId >= 0
            && slot >= 0;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Trainer field '{field}' is not supported by Pokemon Legends Z-A Trainers yet.",
            ZaEditSessionSupport.TrainersDomain,
            field: "field",
            expected: "Supported Pokemon Legends Z-A trainer or trainer Pokemon field");
    }

    private sealed class TrainerRow
    {
        public const int MaximumPartySize = 6;

        public string? TrainerId { get; init; }
        public ulong TrainerType { get; init; }
        public ulong TrainerType2 { get; init; }
        public sbyte Rank { get; set; }
        public byte MoneyRate { get; set; }
        public bool MegaEvolution { get; set; }
        public bool LastHand { get; set; }
        public IReadOnlyList<PokemonRow?> Pokemon { get; private set; } = Array.Empty<PokemonRow?>();
        public bool AiBasic { get; set; }
        public bool AiHigh { get; set; }
        public bool AiExpert { get; set; }
        public bool AiDouble { get; set; }
        public bool AiRaid { get; set; }
        public bool AiWeak { get; set; }
        public bool AiItem { get; set; }
        public bool AiChange { get; set; }
        public float ViewHorizontalAngle { get; init; }
        public float ViewVerticalAngle { get; init; }
        public float ViewRange { get; init; }
        public float HearingRange { get; init; }

        public static TrainerRow From(ZaTrainerRow row)
        {
            return new TrainerRow
            {
                TrainerId = row.TrainerId,
                TrainerType = row.TrainerType,
                TrainerType2 = row.TrainerType2,
                Rank = row.Rank,
                MoneyRate = row.MoneyRate,
                MegaEvolution = row.MegaEvolution,
                LastHand = row.LastHand,
                Pokemon =
                [
                    row.Pokemon1 is { } pokemon1 ? PokemonRow.From(pokemon1) : null,
                    row.Pokemon2 is { } pokemon2 ? PokemonRow.From(pokemon2) : null,
                    row.Pokemon3 is { } pokemon3 ? PokemonRow.From(pokemon3) : null,
                    row.Pokemon4 is { } pokemon4 ? PokemonRow.From(pokemon4) : null,
                    row.Pokemon5 is { } pokemon5 ? PokemonRow.From(pokemon5) : null,
                    row.Pokemon6 is { } pokemon6 ? PokemonRow.From(pokemon6) : null,
                ],
                AiBasic = row.AiBasic,
                AiHigh = row.AiHigh,
                AiExpert = row.AiExpert,
                AiDouble = row.AiDouble,
                AiRaid = row.AiRaid,
                AiWeak = row.AiWeak,
                AiItem = row.AiItem,
                AiChange = row.AiChange,
                ViewHorizontalAngle = row.ViewHorizontalAngle,
                ViewVerticalAngle = row.ViewVerticalAngle,
                ViewRange = row.ViewRange,
                HearingRange = row.HearingRange,
            };
        }

        public void SetAiFlags(int flags)
        {
            AiBasic = (flags & (1 << 0)) != 0;
            AiHigh = (flags & (1 << 1)) != 0;
            AiExpert = (flags & (1 << 2)) != 0;
            AiDouble = (flags & (1 << 3)) != 0;
            AiRaid = (flags & (1 << 4)) != 0;
            AiWeak = (flags & (1 << 5)) != 0;
            AiItem = (flags & (1 << 6)) != 0;
            AiChange = (flags & (1 << 7)) != 0;
        }

        public PokemonRow GetOrCreatePokemon(int slot)
        {
            var slots = Pokemon.Take(MaximumPartySize).ToArray();
            if (slots.Length < MaximumPartySize)
            {
                Array.Resize(ref slots, MaximumPartySize);
            }

            var pokemon = slots[slot];
            if (pokemon is null)
            {
                pokemon = PokemonRow.CreateDefault();
                slots[slot] = pokemon;
                Pokemon = slots;
            }

            return pokemon;
        }

        public void NormalizeEmptyPokemon()
        {
            Pokemon = Pokemon
                .Take(MaximumPartySize)
                .Concat(Enumerable.Repeat<PokemonRow?>(null, MaximumPartySize))
                .Take(MaximumPartySize)
                .Select(pokemon => pokemon is null || pokemon.SpeciesId == 0 ? null : pokemon)
                .ToArray();
        }

        public Offset<ZaTrainerRow> Write(FlatBufferBuilder builder)
        {
            var pokemonOffsets = Pokemon
                .Select(pokemon => pokemon?.Write(builder) ?? default)
                .ToArray();
            var trainerIdOffset = string.IsNullOrEmpty(TrainerId) ? default : builder.CreateString(TrainerId);

            return ZaTrainerRow.Create(
                builder,
                trainerIdOffset,
                TrainerType,
                TrainerType2,
                Rank,
                MoneyRate,
                MegaEvolution,
                LastHand,
                pokemonOffsets.ElementAtOrDefault(0),
                pokemonOffsets.ElementAtOrDefault(1),
                pokemonOffsets.ElementAtOrDefault(2),
                pokemonOffsets.ElementAtOrDefault(3),
                pokemonOffsets.ElementAtOrDefault(4),
                pokemonOffsets.ElementAtOrDefault(5),
                AiBasic,
                AiHigh,
                AiExpert,
                AiDouble,
                AiRaid,
                AiWeak,
                AiItem,
                AiChange,
                ViewHorizontalAngle,
                ViewVerticalAngle,
                ViewRange,
                HearingRange);
        }
    }

    private sealed class PokemonRow
    {
        public ushort SpeciesId { get; set; }
        public short FormId { get; set; }
        public int Sex { get; set; }
        public int Item { get; set; }
        public int Level { get; set; }
        public byte BallId { get; init; }
        public MoveRow?[] Moves { get; } = new MoveRow?[4];
        public int Nature { get; set; }
        public int Ability { get; set; }
        public StatRow? Ivs { get; set; }
        public StatRow? Evs { get; set; }
        public int RareType { get; set; }
        public short ScaleValue { get; init; }
        public bool IsOriginalTrainerByName { get; init; }

        public static PokemonRow From(ZaTrainerPokemon row)
        {
            var result = new PokemonRow
            {
                SpeciesId = row.SpeciesId,
                FormId = row.FormId,
                Sex = row.Sex,
                Item = row.Item,
                Level = row.Level,
                BallId = row.BallId,
                Nature = row.Nature,
                Ability = row.Ability,
                Ivs = row.Ivs is { } ivs ? StatRow.From(ivs) : null,
                Evs = row.Evs is { } evs ? StatRow.From(evs) : null,
                RareType = row.RareType,
                ScaleValue = row.ScaleValue,
                IsOriginalTrainerByName = row.IsOriginalTrainerByName,
            };
            result.Moves[0] = row.Move1 is { } move1 ? MoveRow.From(move1) : null;
            result.Moves[1] = row.Move2 is { } move2 ? MoveRow.From(move2) : null;
            result.Moves[2] = row.Move3 is { } move3 ? MoveRow.From(move3) : null;
            result.Moves[3] = row.Move4 is { } move4 ? MoveRow.From(move4) : null;
            return result;
        }

        public static PokemonRow CreateDefault()
        {
            return new PokemonRow
            {
                SpeciesId = 0,
                FormId = 0,
                Sex = -1,
                Item = 0,
                Level = 1,
                BallId = 0,
                Nature = -1,
                Ability = 0,
                Ivs = StatRow.Zero,
                Evs = StatRow.Zero,
                RareType = 0,
                ScaleValue = 0,
                IsOriginalTrainerByName = false,
            };
        }

        public void Clear()
        {
            SpeciesId = 0;
            FormId = 0;
            Sex = -1;
            Item = 0;
            Level = 1;
            Array.Clear(Moves);
            Nature = -1;
            Ability = 0;
            Ivs = StatRow.Zero;
            Evs = StatRow.Zero;
            RareType = 0;
        }

        public void SetMove(int index, int value)
        {
            Moves[index] = (Moves[index] ?? new MoveRow(0, IsPlusMove: false)) with
            {
                MoveId = checked((ushort)value),
            };
        }

        public Offset<ZaTrainerPokemon> Write(FlatBufferBuilder builder)
        {
            var moveOffsets = Moves
                .Select(move => move?.Write(builder) ?? default)
                .ToArray();
            var ivsOffset = Ivs?.Write(builder) ?? default;
            var evsOffset = Evs?.Write(builder) ?? default;

            return ZaTrainerPokemon.Create(
                builder,
                SpeciesId,
                FormId,
                Sex,
                Item,
                Level,
                BallId,
                moveOffsets.ElementAtOrDefault(0),
                moveOffsets.ElementAtOrDefault(1),
                moveOffsets.ElementAtOrDefault(2),
                moveOffsets.ElementAtOrDefault(3),
                Nature,
                Ability,
                ivsOffset,
                evsOffset,
                RareType,
                ScaleValue,
                IsOriginalTrainerByName);
        }
    }

    private sealed record MoveRow(ushort MoveId, bool IsPlusMove)
    {
        public static MoveRow From(ZaTrainerMove row)
        {
            return new MoveRow(row.MoveId, row.IsPlusMove);
        }

        public Offset<ZaTrainerMove> Write(FlatBufferBuilder builder)
        {
            return ZaTrainerMove.Create(builder, MoveId, IsPlusMove);
        }
    }

    private sealed record StatRow(int Hp, int Atk, int Def, int SpAtk, int SpDef, int Agi)
    {
        public static readonly StatRow Zero = new(0, 0, 0, 0, 0, 0);

        public static StatRow From(ZaTrainerStats row)
        {
            return new StatRow(row.Hp, row.Atk, row.Def, row.SpAtk, row.SpDef, row.Agi);
        }

        public Offset<ZaTrainerStats> Write(FlatBufferBuilder builder)
        {
            return ZaTrainerStats.Create(builder, Hp, Atk, Def, SpAtk, SpDef, Agi);
        }
    }
}
