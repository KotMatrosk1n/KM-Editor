// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Items;
using KM.SV.Trainers;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Trainers;

internal sealed class SvTrainersEditSessionService
{
    private const string IsStrongField = "isStrong";
    private const string ChangeGemField = "changeGem";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvTrainersWorkflowService trainersWorkflowService;

    public SvTrainersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvTrainersWorkflowService? trainersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.trainersWorkflowService = trainersWorkflowService ?? new SvTrainersWorkflowService(this.fileSource);
    }

    public SvTrainersEditResult UpdateField(
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
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.TrainersDomain,
                diagnostics))
        {
            return new SvTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
        if (trainer is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainer {trainerId} is not present in the loaded Trainers workflow.",
                SvEditSessionSupport.TrainersDomain,
                field: "trainerId",
                expected: "Existing trainer record"));
            return new SvTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, trainer, slot, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvTrainersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SvTrainersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = trainersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.TrainersDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Trainers change is valid.",
                SvEditSessionSupport.TrainersDomain));
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
        return SvEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            SvEditSessionSupport.TrainersDomain,
            SvDataPaths.TrainerDataArray,
            "Trainers",
            validation.Diagnostics,
            outputMode);
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

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                SvEditSessionSupport.TrainersDomain,
                expected: "Current reviewed Trainers change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, SvDataPaths.TrainerDataArray);
            var moveResolver = SvTrainerMoveResolver.Load(project, fileSource, diagnostics);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, moveResolver, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            SvWorkflowFileSource.Write(paths, SvDataPaths.TrainerDataArray, WriteRows(rows), outputMode);
            writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.TrainerDataArray, outputMode));
            if (outputMode == SvOutputMode.Standalone)
            {
                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                outputMode == SvOutputMode.Standalone
                    ? "Applied Trainers change plan as standalone Scarlet/Violet output and patched the Trinity descriptor."
                    : "Applied Trainers change plan for Trinity Mod Manager. Run this output folder through Trinity Mod Manager before installing.",
                SvEditSessionSupport.TrainersDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainers output could not be written: {exception.Message}",
                SvEditSessionSupport.TrainersDomain,
                file: $"romfs/{SvDataPaths.TrainerDataArray}",
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SvTrainersWorkflow workflow,
        SvTrainerRecord trainer,
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

        var parsedValue = SvEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            SvEditSessionSupport.TrainersDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (IsPokemonField(normalizedField))
        {
            if (slot is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trainer Pokemon edits require a Pokemon slot.",
                    SvEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Existing trainer Pokemon slot"));
                return null;
            }

            var pokemon = trainer.Team.FirstOrDefault(candidate => candidate.Slot == slot.Value);
            if (pokemon is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {trainer.TrainerId} does not have Pokemon slot {slot.Value}.",
                    SvEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Existing trainer Pokemon slot"));
                return null;
            }

            return SvEditSessionSupport.CreatePendingEdit(
                SvEditSessionSupport.TrainersDomain,
                $"Set {trainer.Name} slot {slot.Value} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
                new ProjectFileReference(trainer.Provenance.TeamSourceLayer, trainer.Provenance.TeamSourceFile),
                CreateTeamRecordId(trainer.TrainerId, slot.Value),
                normalizedField,
                parsedValue.Value.ToString(CultureInfo.InvariantCulture));
        }

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.TrainersDomain,
            $"Set {trainer.Name} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(trainer.Provenance.SourceLayer, trainer.Provenance.SourceFile),
            trainer.TrainerId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SvTrainersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TrainersDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Trainers.",
                SvEditSessionSupport.TrainersDomain,
                expected: SvEditSessionSupport.TrainersDomain));
            return;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        SvTrainerRecord? trainer;
        if (IsPokemonField(edit.Field))
        {
            if (!TryParseTeamRecordId(edit.RecordId, out var trainerId, out var slot))
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer Pokemon edit targets an invalid slot.",
                    SvEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Trainer Pokemon slot"));
                return;
            }

            trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
            if (trainer is null || trainer.Team.All(pokemon => pokemon.Slot != slot))
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer Pokemon edit targets a slot that is not loaded.",
                    SvEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Existing trainer Pokemon slot"));
                return;
            }
        }
        else
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var trainerId))
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer edit targets an invalid trainer.",
                    SvEditSessionSupport.TrainersDomain,
                    field: "trainerId",
                    expected: "Existing trainer record"));
                return;
            }

            trainer = workflow.Trainers.FirstOrDefault(candidate => candidate.TrainerId == trainerId);
            if (trainer is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer edit targets a record that is not loaded.",
                    SvEditSessionSupport.TrainersDomain,
                    field: "trainerId",
                    expected: "Existing trainer record"));
                return;
            }
        }

        _ = SvEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            SvEditSessionSupport.TrainersDomain,
            diagnostics);
    }

    private static SvTrainersWorkflow OverlayPendingEdits(SvTrainersWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SvTrainersWorkflow OverlayPendingEdit(SvTrainersWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TrainersDomain, StringComparison.Ordinal)
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
                    .Select(trainer => trainer.TrainerId == trainerId ? OverlayTrainerPokemon(trainer, slot, edit.Field, value) : trainer)
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

    private static SvTrainerRecord OverlayTrainer(SvTrainerRecord trainer, string? field, int value)
    {
        return field switch
        {
            SvTrainersWorkflowService.BattleTypeField => trainer with
            {
                BattleTypeValue = value,
                BattleType = SvTrainersWorkflowService.FormatBattleType((global::trainer.BattleType)value),
            },
            SvTrainersWorkflowService.MoneyField => trainer with { Money = value },
            SvTrainersWorkflowService.AiFlagsField => trainer with
            {
                AiFlags = value,
                AiFlagStates = CreateAiStates(value),
            },
            ChangeGemField => WithTeraTarget(trainer with { CanTerastallize = value != 0 }),
            _ => trainer,
        };
    }

    private static SvTrainerRecord OverlayTrainerPokemon(SvTrainerRecord trainer, int slot, string? field, int value)
    {
        var updatedTrainer = trainer with
        {
            Team = trainer.Team
                .Select(pokemon => pokemon.Slot == slot ? OverlayPokemon(pokemon, field, value) : pokemon)
                .ToArray(),
        };

        return WithTeraTarget(updatedTrainer);
    }

    private static SvTrainerPokemonRecord OverlayPokemon(SvTrainerPokemonRecord pokemon, string? field, int value)
    {
        return field switch
        {
            SvTrainersWorkflowService.SpeciesIdField => pokemon with
            {
                SpeciesId = value,
                Species = value == 0 ? "None" : SvLabels.Pokemon(value),
            },
            SvTrainersWorkflowService.FormField => pokemon with { Form = value },
            SvTrainersWorkflowService.LevelField => pokemon with { Level = value },
            SvTrainersWorkflowService.HeldItemIdField => pokemon with
            {
                HeldItemId = value,
                HeldItem = value > 0 ? SvLabels.Item(value) : null,
            },
            SvTrainersWorkflowService.Move1IdField => OverlayMove(pokemon, 0, value),
            SvTrainersWorkflowService.Move2IdField => OverlayMove(pokemon, 1, value),
            SvTrainersWorkflowService.Move3IdField => OverlayMove(pokemon, 2, value),
            SvTrainersWorkflowService.Move4IdField => OverlayMove(pokemon, 3, value),
            SvTrainersWorkflowService.GenderField => pokemon with { Gender = value, GenderLabel = SvTrainersWorkflowService.FormatGender((global::SexType)value) },
            SvTrainersWorkflowService.AbilityField => pokemon with { Ability = value, AbilityLabel = SvTrainersWorkflowService.FormatAbilityMode((global::TokuseiType)value) },
            SvTrainersWorkflowService.NatureField => pokemon with { Nature = value, NatureLabel = SvTrainersWorkflowService.FormatNature((global::SeikakuType)value) },
            SvTrainersWorkflowService.TeraTypeField => pokemon with { TeraType = value, TeraTypeLabel = SvTrainersWorkflowService.FormatTeraType((global::GemType)value) },
            SvTrainersWorkflowService.EvHpField => pokemon with { Evs = pokemon.Evs with { HP = value } },
            SvTrainersWorkflowService.EvAttackField => pokemon with { Evs = pokemon.Evs with { Attack = value } },
            SvTrainersWorkflowService.EvDefenseField => pokemon with { Evs = pokemon.Evs with { Defense = value } },
            SvTrainersWorkflowService.EvSpecialAttackField => pokemon with { Evs = pokemon.Evs with { SpecialAttack = value } },
            SvTrainersWorkflowService.EvSpecialDefenseField => pokemon with { Evs = pokemon.Evs with { SpecialDefense = value } },
            SvTrainersWorkflowService.EvSpeedField => pokemon with { Evs = pokemon.Evs with { Speed = value } },
            SvTrainersWorkflowService.IvHpField => pokemon with { Ivs = pokemon.Ivs with { HP = value } },
            SvTrainersWorkflowService.IvAttackField => pokemon with { Ivs = pokemon.Ivs with { Attack = value } },
            SvTrainersWorkflowService.IvDefenseField => pokemon with { Ivs = pokemon.Ivs with { Defense = value } },
            SvTrainersWorkflowService.IvSpecialAttackField => pokemon with { Ivs = pokemon.Ivs with { SpecialAttack = value } },
            SvTrainersWorkflowService.IvSpecialDefenseField => pokemon with { Ivs = pokemon.Ivs with { SpecialDefense = value } },
            SvTrainersWorkflowService.IvSpeedField => pokemon with { Ivs = pokemon.Ivs with { Speed = value } },
            SvTrainersWorkflowService.ShinyField => pokemon with { Shiny = value != 0 },
            _ => pokemon,
        };
    }

    private static SvTrainerPokemonRecord OverlayMove(SvTrainerPokemonRecord pokemon, int moveIndex, int value)
    {
        var moveIds = pokemon.MoveIds.ToList();
        var moves = pokemon.Moves.ToList();
        while (moveIds.Count <= moveIndex)
        {
            moveIds.Add(0);
            moves.Add("None");
        }

        moveIds[moveIndex] = value;
        moves[moveIndex] = value == 0 ? "None" : SvLabels.Move(value);

        return pokemon with
        {
            MoveIds = moveIds,
            Moves = moves,
        };
    }

    private static SvTrainerRecord WithTeraTarget(SvTrainerRecord trainer)
    {
        return trainer with
        {
            TeraTarget = SvTrainersWorkflowService.FormatTeraTarget(
                trainer.CanTerastallize,
                trainer.Team),
        };
    }

    private static void ApplyEdit(
        IReadOnlyList<TrainerRow> rows,
        PendingEdit edit,
        SvTrainerMoveResolver moveResolver,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TrainersDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit is not valid for apply.",
                SvEditSessionSupport.TrainersDomain,
                expected: "Valid trainer edit"));
            return;
        }

        TrainerRow? row;
        if (IsPokemonField(edit.Field))
        {
            if (!TryParseTeamRecordId(edit.RecordId, out var trainerId, out var slot))
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer Pokemon edit target is invalid.",
                    SvEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Trainer Pokemon slot"));
                return;
            }

            row = rows.ElementAtOrDefault(trainerId);
            var pokemon = row?.Pokemon.ElementAtOrDefault(slot);
            if (pokemon is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending trainer Pokemon edit target is not present in the source array.",
                    SvEditSessionSupport.TrainersDomain,
                    field: "slot",
                    expected: "Existing source trainer Pokemon slot"));
                return;
            }

            ApplyPokemonField(pokemon, edit.Field, value, moveResolver);
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var targetTrainerId))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit target is invalid.",
                SvEditSessionSupport.TrainersDomain,
                field: "trainerId",
                expected: "Trainer ID"));
            return;
        }

        row = rows.ElementAtOrDefault(targetTrainerId);
        if (row is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trainer edit target is not present in the source array.",
                SvEditSessionSupport.TrainersDomain,
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
            case SvTrainersWorkflowService.BattleTypeField:
                row.BattleType = (global::trainer.BattleType)value;
                break;
            case SvTrainersWorkflowService.MoneyField:
                row.MoneyRate = checked((sbyte)value);
                break;
            case SvTrainersWorkflowService.AiFlagsField:
                row.SetAiFlags(value);
                break;
            case IsStrongField:
                row.IsStrong = value != 0;
                break;
            case ChangeGemField:
                row.ChangeGem = value != 0;
                break;
        }
    }

    private static void ApplyPokemonField(
        PokemonRow row,
        string? field,
        int value,
        SvTrainerMoveResolver moveResolver)
    {
        switch (field)
        {
            case SvTrainersWorkflowService.SpeciesIdField:
                row.DevId = (global::pml.common.DevID)checked((ushort)value);
                break;
            case SvTrainersWorkflowService.FormField:
                row.FormId = checked((short)value);
                break;
            case SvTrainersWorkflowService.LevelField:
                row.Level = value;
                break;
            case SvTrainersWorkflowService.HeldItemIdField:
                row.Item = (global::ItemID)value;
                break;
            case SvTrainersWorkflowService.Move1IdField:
                row.SetMove(0, value, moveResolver);
                break;
            case SvTrainersWorkflowService.Move2IdField:
                row.SetMove(1, value, moveResolver);
                break;
            case SvTrainersWorkflowService.Move3IdField:
                row.SetMove(2, value, moveResolver);
                break;
            case SvTrainersWorkflowService.Move4IdField:
                row.SetMove(3, value, moveResolver);
                break;
            case SvTrainersWorkflowService.GenderField:
                row.Sex = (global::SexType)value;
                break;
            case SvTrainersWorkflowService.AbilityField:
                row.Tokusei = (global::TokuseiType)value;
                break;
            case SvTrainersWorkflowService.NatureField:
                row.Seikaku = (global::SeikakuType)value;
                break;
            case SvTrainersWorkflowService.EvHpField:
                row.EffortValue = (row.EffortValue ?? ParamSetRow.Zero) with { Hp = value };
                break;
            case SvTrainersWorkflowService.EvAttackField:
                row.EffortValue = (row.EffortValue ?? ParamSetRow.Zero) with { Atk = value };
                break;
            case SvTrainersWorkflowService.EvDefenseField:
                row.EffortValue = (row.EffortValue ?? ParamSetRow.Zero) with { Def = value };
                break;
            case SvTrainersWorkflowService.EvSpecialAttackField:
                row.EffortValue = (row.EffortValue ?? ParamSetRow.Zero) with { SpAtk = value };
                break;
            case SvTrainersWorkflowService.EvSpecialDefenseField:
                row.EffortValue = (row.EffortValue ?? ParamSetRow.Zero) with { SpDef = value };
                break;
            case SvTrainersWorkflowService.EvSpeedField:
                row.EffortValue = (row.EffortValue ?? ParamSetRow.Zero) with { Agi = value };
                break;
            case SvTrainersWorkflowService.IvHpField:
                row.TalentValue = (row.TalentValue ?? ParamSetRow.Zero) with { Hp = value };
                break;
            case SvTrainersWorkflowService.IvAttackField:
                row.TalentValue = (row.TalentValue ?? ParamSetRow.Zero) with { Atk = value };
                break;
            case SvTrainersWorkflowService.IvDefenseField:
                row.TalentValue = (row.TalentValue ?? ParamSetRow.Zero) with { Def = value };
                break;
            case SvTrainersWorkflowService.IvSpecialAttackField:
                row.TalentValue = (row.TalentValue ?? ParamSetRow.Zero) with { SpAtk = value };
                break;
            case SvTrainersWorkflowService.IvSpecialDefenseField:
                row.TalentValue = (row.TalentValue ?? ParamSetRow.Zero) with { SpDef = value };
                break;
            case SvTrainersWorkflowService.IvSpeedField:
                row.TalentValue = (row.TalentValue ?? ParamSetRow.Zero) with { Agi = value };
                break;
            case SvTrainersWorkflowService.ShinyField:
                row.RareType = value == 0 ? global::RareType.DEFAULT : global::RareType.RARE;
                break;
            case SvTrainersWorkflowService.TeraTypeField:
                row.GemType = (global::GemType)value;
                break;
        }
    }

    private static IReadOnlyList<TrainerRow> ReadRows(byte[] bytes)
    {
        var table = global::trainer.TrdataMainArray.GetRootAsTrdataMainArray(new ByteBuffer(bytes));
        var rows = new List<TrainerRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
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
        var vector = global::trainer.TrdataMainArray.CreateValuesVector(builder, offsets);
        var root = global::trainer.TrdataMainArray.CreateTrdataMainArray(builder, vector);
        global::trainer.TrdataMainArray.FinishTrdataMainArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static bool IsPokemonField(string? field)
    {
        return field is
            SvTrainersWorkflowService.SpeciesIdField or
            SvTrainersWorkflowService.FormField or
            SvTrainersWorkflowService.LevelField or
            SvTrainersWorkflowService.HeldItemIdField or
            SvTrainersWorkflowService.Move1IdField or
            SvTrainersWorkflowService.Move2IdField or
            SvTrainersWorkflowService.Move3IdField or
            SvTrainersWorkflowService.Move4IdField or
            SvTrainersWorkflowService.GenderField or
            SvTrainersWorkflowService.AbilityField or
            SvTrainersWorkflowService.NatureField or
            SvTrainersWorkflowService.EvHpField or
            SvTrainersWorkflowService.EvAttackField or
            SvTrainersWorkflowService.EvDefenseField or
            SvTrainersWorkflowService.EvSpecialAttackField or
            SvTrainersWorkflowService.EvSpecialDefenseField or
            SvTrainersWorkflowService.EvSpeedField or
            SvTrainersWorkflowService.IvHpField or
            SvTrainersWorkflowService.IvAttackField or
            SvTrainersWorkflowService.IvDefenseField or
            SvTrainersWorkflowService.IvSpecialAttackField or
            SvTrainersWorkflowService.IvSpecialDefenseField or
            SvTrainersWorkflowService.IvSpeedField or
            SvTrainersWorkflowService.ShinyField or
            SvTrainersWorkflowService.TeraTypeField;
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
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Trainer field '{field}' is not supported by Scarlet/Violet Trainers yet.",
            SvEditSessionSupport.TrainersDomain,
            field: "field",
            expected: "Supported S/V trainer or trainer Pokemon field");
    }

    private static IReadOnlyList<SvTrainerAiFlagState> CreateAiStates(int flags)
    {
        var definitions = new[]
        {
            (0, "Basic", "Enables baseline move selection and battle decisions."),
            (1, "High", "Uses stronger scoring for move choice, targets, and matchup checks."),
            (2, "Expert", "Enables the highest trainer AI tier for advanced battle decisions."),
            (3, "Double", "Uses double-battle-aware partner, target, and spread move logic."),
            (4, "Raid", "Uses raid-style AI checks for encounters that share raid battle behavior."),
            (5, "Weak", "Allows weakness-aware choices against the opponent's active Pokemon."),
            (6, "Item", "Allows the trainer AI to consider configured battle item usage."),
            (7, "Change", "Allows the trainer AI to consider switching Pokemon during battle."),
        };

        return definitions
            .Select(definition =>
            {
                var mask = 1 << definition.Item1;
                return new SvTrainerAiFlagState(
                    definition.Item1,
                    mask,
                    definition.Item2,
                    definition.Item3,
                    (flags & mask) != 0);
            })
            .ToArray();
    }

    private sealed class TrainerRow
    {
        public string? Trid { get; init; }
        public string? TrNameLabel { get; init; }
        public string? TrainerType { get; init; }
        public bool IsStrong { get; set; }
        public global::trainer.BattleType BattleType { get; set; }
        public global::trainer.DataType DataType { get; init; }
        public sbyte MoneyRate { get; set; }
        public bool ChangeGem { get; set; }
        public IReadOnlyList<PokemonRow?> Pokemon { get; init; } = Array.Empty<PokemonRow?>();
        public bool AiBasic { get; set; }
        public bool AiHigh { get; set; }
        public bool AiExpert { get; set; }
        public bool AiDouble { get; set; }
        public bool AiRaid { get; set; }
        public bool AiWeak { get; set; }
        public bool AiItem { get; set; }
        public bool AiChange { get; set; }
        public string? PopupLabelNormal1 { get; init; }
        public string? PopupLabelNormal2 { get; init; }
        public string? PopupLabelPinch1 { get; init; }
        public string? PopupLabelPinch2 { get; init; }

        public static TrainerRow From(global::trainer.TrdataMain row)
        {
            return new TrainerRow
            {
                Trid = row.Trid,
                TrNameLabel = row.TrNameLabel,
                TrainerType = row.TrainerType,
                IsStrong = row.IsStrong,
                BattleType = row.BattleType,
                DataType = row.DataType,
                MoneyRate = row.MoneyRate,
                ChangeGem = row.ChangeGem,
                Pokemon =
                [
                    row.Poke1 is { } poke1 ? PokemonRow.From(poke1) : null,
                    row.Poke2 is { } poke2 ? PokemonRow.From(poke2) : null,
                    row.Poke3 is { } poke3 ? PokemonRow.From(poke3) : null,
                    row.Poke4 is { } poke4 ? PokemonRow.From(poke4) : null,
                    row.Poke5 is { } poke5 ? PokemonRow.From(poke5) : null,
                    row.Poke6 is { } poke6 ? PokemonRow.From(poke6) : null,
                ],
                AiBasic = row.AiBasic,
                AiHigh = row.AiHigh,
                AiExpert = row.AiExpert,
                AiDouble = row.AiDouble,
                AiRaid = row.AiRaid,
                AiWeak = row.AiWeak,
                AiItem = row.AiItem,
                AiChange = row.AiChange,
                PopupLabelNormal1 = row.PopupLabelNormal1,
                PopupLabelNormal2 = row.PopupLabelNormal2,
                PopupLabelPinch1 = row.PopupLabelPinch1,
                PopupLabelPinch2 = row.PopupLabelPinch2,
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

        public Offset<global::trainer.TrdataMain> Write(FlatBufferBuilder builder)
        {
            var pokemonOffsets = Pokemon
                .Select(pokemon => pokemon?.Write(builder) ?? default)
                .ToArray();
            var tridOffset = string.IsNullOrEmpty(Trid) ? default : builder.CreateString(Trid);
            var nameOffset = string.IsNullOrEmpty(TrNameLabel) ? default : builder.CreateString(TrNameLabel);
            var typeOffset = string.IsNullOrEmpty(TrainerType) ? default : builder.CreateString(TrainerType);
            var normal1Offset = string.IsNullOrEmpty(PopupLabelNormal1) ? default : builder.CreateString(PopupLabelNormal1);
            var normal2Offset = string.IsNullOrEmpty(PopupLabelNormal2) ? default : builder.CreateString(PopupLabelNormal2);
            var pinch1Offset = string.IsNullOrEmpty(PopupLabelPinch1) ? default : builder.CreateString(PopupLabelPinch1);
            var pinch2Offset = string.IsNullOrEmpty(PopupLabelPinch2) ? default : builder.CreateString(PopupLabelPinch2);

            return global::trainer.TrdataMain.CreateTrdataMain(
                builder,
                tridOffset,
                nameOffset,
                typeOffset,
                IsStrong,
                BattleType,
                DataType,
                MoneyRate,
                ChangeGem,
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
                normal1Offset,
                normal2Offset,
                pinch1Offset,
                pinch2Offset);
        }
    }

    private sealed class PokemonRow
    {
        public global::pml.common.DevID DevId { get; set; }
        public short FormId { get; set; }
        public global::SexType Sex { get; set; }
        public global::ItemID Item { get; set; }
        public int Level { get; set; }
        public global::BallType BallId { get; init; }
        public global::WazaType WazaType { get; set; }
        public WazaSetRow?[] Waza { get; } = new WazaSetRow?[4];
        public global::GemType GemType { get; set; }
        public global::SeikakuType Seikaku { get; set; }
        public global::TokuseiType Tokusei { get; set; }
        public global::TalentType TalentType { get; init; }
        public ParamSetRow? TalentValue { get; set; }
        public sbyte TalentVnum { get; init; }
        public ParamSetRow? EffortValue { get; set; }
        public global::RareType RareType { get; set; }
        public global::SizeType ScaleType { get; init; }
        public short ScaleValue { get; init; }

        public static PokemonRow From(global::PokeDataBattle row)
        {
            var result = new PokemonRow
            {
                DevId = row.DevId,
                FormId = row.FormId,
                Sex = row.Sex,
                Item = row.Item,
                Level = row.Level,
                BallId = row.BallId,
                WazaType = row.WazaType,
                GemType = row.GemType,
                Seikaku = row.Seikaku,
                Tokusei = row.Tokusei,
                TalentType = row.TalentType,
                TalentValue = row.TalentValue is { } talentValue ? ParamSetRow.From(talentValue) : null,
                TalentVnum = row.TalentVnum,
                EffortValue = row.EffortValue is { } effortValue ? ParamSetRow.From(effortValue) : null,
                RareType = row.RareType,
                ScaleType = row.ScaleType,
                ScaleValue = row.ScaleValue,
            };

            result.Waza[0] = row.Waza1 is { } waza1 ? WazaSetRow.From(waza1) : null;
            result.Waza[1] = row.Waza2 is { } waza2 ? WazaSetRow.From(waza2) : null;
            result.Waza[2] = row.Waza3 is { } waza3 ? WazaSetRow.From(waza3) : null;
            result.Waza[3] = row.Waza4 is { } waza4 ? WazaSetRow.From(waza4) : null;
            return result;
        }

        public void SetMove(int index, int moveId, SvTrainerMoveResolver moveResolver)
        {
            if (WazaType == global::WazaType.DEFAULT)
            {
                var currentMoves = Waza
                    .Select(waza => waza is null ? 0 : (int)waza.WazaId)
                    .ToArray();
                var defaultMoves = currentMoves.Any(move => move != 0)
                    ? currentMoves
                    : moveResolver.Resolve((int)DevId, FormId, Level);

                for (var defaultIndex = 0; defaultIndex < Waza.Length; defaultIndex++)
                {
                    var defaultMove = defaultMoves.ElementAtOrDefault(defaultIndex);
                    Waza[defaultIndex] = defaultMove == 0
                        ? null
                        : new WazaSetRow((global::pml.common.WazaID)checked((ushort)defaultMove), 0);
                }

                WazaType = global::WazaType.MANUAL;
            }

            Waza[index] = moveId == 0
                ? null
                : (Waza[index] ?? new WazaSetRow((global::pml.common.WazaID)0, 0)) with
                {
                    WazaId = (global::pml.common.WazaID)checked((ushort)moveId),
                };
        }

        public Offset<global::PokeDataBattle> Write(FlatBufferBuilder builder)
        {
            var wazaOffsets = Waza
                .Select(waza => waza?.Write(builder) ?? default)
                .ToArray();
            var talentOffset = TalentValue?.Write(builder) ?? default;
            var effortOffset = EffortValue?.Write(builder) ?? default;

            return global::PokeDataBattle.CreatePokeDataBattle(
                builder,
                DevId,
                FormId,
                Sex,
                Item,
                Level,
                BallId,
                WazaType,
                wazaOffsets[0],
                wazaOffsets[1],
                wazaOffsets[2],
                wazaOffsets[3],
                GemType,
                Seikaku,
                Tokusei,
                TalentType,
                talentOffset,
                TalentVnum,
                effortOffset,
                RareType,
                ScaleType,
                ScaleValue);
        }
    }

    private sealed record WazaSetRow(global::pml.common.WazaID WazaId, sbyte PointUp)
    {
        public static WazaSetRow From(global::WazaSet row) => new(row.WazaId, row.PointUp);

        public Offset<global::WazaSet> Write(FlatBufferBuilder builder) =>
            global::WazaSet.CreateWazaSet(builder, WazaId, PointUp);
    }

    private sealed record ParamSetRow(int Hp, int Atk, int Def, int SpAtk, int SpDef, int Agi)
    {
        public static readonly ParamSetRow Zero = new(0, 0, 0, 0, 0, 0);

        public static ParamSetRow From(global::ParamSet row) =>
            new(row.Hp, row.Atk, row.Def, row.SpAtk, row.SpDef, row.Agi);

        public Offset<global::ParamSet> Write(FlatBufferBuilder builder) =>
            global::ParamSet.CreateParamSet(builder, Hp, Atk, Def, SpAtk, SpDef, Agi);
    }
}
