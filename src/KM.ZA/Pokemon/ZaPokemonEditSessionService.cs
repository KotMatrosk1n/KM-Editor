// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.Pokemon;

internal sealed class ZaPokemonEditSessionService
{
    private const string LearnsetFieldPrefix = "learnset";
    private const string EvolutionFieldPrefix = "evolution";
    private const string CompatibilityFieldPrefix = "compatibility";
    private const string AddAction = "add";
    private const string UpsertAction = "upsert";
    private const string RemoveAction = "remove";
    private const string MoveUpAction = "moveUp";
    private const string MoveDownAction = "moveDown";
    private const string MoveToAction = "moveTo";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaPokemonWorkflowService pokemonWorkflowService;

    public ZaPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaPokemonWorkflowService? pokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.pokemonWorkflowService = pokemonWorkflowService ?? new ZaPokemonWorkflowService(this.fileSource);
    }

    public ZaPokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                ZaEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreateFieldPendingEdit(workflow, pokemon, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPokemonEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon Data batch update is missing a field or value.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "updates",
                    expected: "Complete Pokemon Data field update"));
                continue;
            }

            var pokemon = effectiveWorkflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == update.PersonalId);
            if (pokemon is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon personal record {update.PersonalId} is not present in the loaded Pokemon Data workflow.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "personalId",
                    expected: "Existing Pokemon personal record"));
                continue;
            }

            var pendingEdit = CreateFieldPendingEdit(effectiveWorkflow, pokemon, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits);
        }

        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPokemonEditResult UpdateLearnset(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string action,
        int? slot,
        int? moveId,
        int? level)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                ZaEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var operation = CreateLearnsetOperation(pokemon, action, slot, moveId, level, diagnostics);
        if (operation is null)
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.PokemonDomain,
            CreateLearnsetSummary(pokemon, operation),
            new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
            pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            CreateOperationField(LearnsetFieldPrefix, operation.Action, operation.Slot),
            FormatOperationValue(operation.MoveId, operation.RawLevel));
        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);

        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPokemonEditResult UpdateEvolution(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string action,
        int? slot,
        int? method,
        int? argument,
        int? species,
        int? form,
        int? level)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                ZaEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var operation = CreateEvolutionOperation(pokemon, action, slot, method, argument, species, form, level, diagnostics);
        if (operation is null)
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.PokemonDomain,
            CreateEvolutionSummary(pokemon, operation),
            new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
            pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            CreateOperationField(EvolutionFieldPrefix, operation.Action, operation.Slot),
            FormatEvolutionValue(operation));
        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);

        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = pokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.PokemonDomain,
            diagnostics);

        var effectiveWorkflow = workflow;
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(effectiveWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                effectiveWorkflow = OverlayPendingEdits(effectiveWorkflow, [edit]);
            }
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Pokemon Data change is valid.",
                ZaEditSessionSupport.PokemonDomain));
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
            ZaEditSessionSupport.PokemonDomain,
            ZaDataPaths.PersonalArray,
            "Pokemon Data",
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
                ZaEditSessionSupport.PokemonDomain,
                expected: "Current reviewed Pokemon Data change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, ZaDataPaths.PersonalArray);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(paths, ZaDataPaths.PersonalArray, WriteRows(rows), outputMode);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.PersonalArray, outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Pokemon Data", outputMode),
                ZaEditSessionSupport.PokemonDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data output could not be written: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                file: $"romfs/{ZaDataPaths.PersonalArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreateFieldPendingEdit(
        ZaPokemonWorkflow workflow,
        ZaPokemonRecord pokemon,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (TryParseCompatibilityField(normalizedField, out var groupId, out var slot))
        {
            var group = pokemon.Compatibility.FirstOrDefault(candidate => candidate.GroupId == groupId);
            var entry = group?.Entries.FirstOrDefault(candidate => candidate.Slot == slot);
            if (entry is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon compatibility edit targets a move slot that is not loaded.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: normalizedField,
                    expected: "Existing compatibility move slot"));
                return null;
            }

            var parsedValue = ZaEditSessionSupport.TryParseInt(
                value,
                0,
                1,
                normalizedField,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics);
            if (parsedValue is null)
            {
                return null;
            }

            return ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                parsedValue.Value == 0
                    ? $"Disable {pokemon.Name} {entry.Label} compatibility."
                    : $"Enable {pokemon.Name} {entry.Label} compatibility.",
                new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
                pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
                normalizedField,
                parsedValue.Value.ToString(CultureInfo.InvariantCulture));
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, normalizedField, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsed = ZaEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.PokemonDomain,
            diagnostics);
        if (parsed is null)
        {
            return null;
        }

        var displayValue = string.Equals(editableField.ValueKind, "boolean", StringComparison.Ordinal)
            ? parsed.Value == 0 ? "disabled" : "enabled"
            : parsed.Value.ToString(CultureInfo.InvariantCulture);
        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.PokemonDomain,
            $"Set {pokemon.Name} {editableField.Label.ToLowerInvariant()} to {displayValue}.",
            new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
            pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsed.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        ZaPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Pokemon Data.",
                ZaEditSessionSupport.PokemonDomain,
                expected: ZaEditSessionSupport.PokemonDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets an invalid personal record.",
                ZaEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return;
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets a record that is not loaded.",
                ZaEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return;
        }

        if (TryParseLearnsetField(edit.Field, out _, out _))
        {
            _ = ParseLearnsetOperation(edit, pokemon, diagnostics);
            return;
        }

        if (TryParseEvolutionField(edit.Field, out _, out _))
        {
            _ = ParseEvolutionOperation(edit, pokemon, diagnostics);
            return;
        }

        if (TryParseCompatibilityField(edit.Field, out var groupId, out var compatibilitySlot))
        {
            if (pokemon.Compatibility
                    .FirstOrDefault(group => group.GroupId == groupId)
                    ?.Entries
                    .All(entry => entry.Slot != compatibilitySlot) != false)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon compatibility edit targets a move slot that is not loaded.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: edit.Field,
                    expected: "Existing compatibility move slot"));
                return;
            }

            _ = ZaEditSessionSupport.TryParseInt(
                edit.NewValue,
                0,
                1,
                edit.Field,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics);
            return;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        _ = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.PokemonDomain,
            diagnostics);
    }

    private static ZaPokemonWorkflow OverlayPendingEdits(ZaPokemonWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updated = workflow;
        foreach (var edit in edits)
        {
            updated = OverlayPendingEdit(updated, edit);
        }

        return updated;
    }

    private static ZaPokemonWorkflow OverlayPendingEdit(ZaPokemonWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
        {
            return workflow;
        }

        return workflow with
        {
            Pokemon = workflow.Pokemon
                .Select(pokemon => pokemon.PersonalId == personalId ? OverlayPokemon(workflow, pokemon, edit) : pokemon)
                .ToArray(),
        };
    }

    private static ZaPokemonRecord OverlayPokemon(
        ZaPokemonWorkflow workflow,
        ZaPokemonRecord pokemon,
        PendingEdit edit)
    {
        if (TryParseLearnsetField(edit.Field, out _, out _)
            && ParseLearnsetOperation(edit, pokemon, new List<ValidationDiagnostic>()) is { } learnsetOperation)
        {
            return ApplyLearnsetOperation(pokemon, learnsetOperation);
        }

        if (TryParseEvolutionField(edit.Field, out _, out _)
            && ParseEvolutionOperation(edit, pokemon, new List<ValidationDiagnostic>()) is { } evolutionOperation)
        {
            return ApplyEvolutionOperation(pokemon, evolutionOperation);
        }

        if (TryParseCompatibilityField(edit.Field, out var groupId, out var slot)
            && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var compatibilityEnabled))
        {
            return OverlayCompatibility(pokemon, groupId, slot, compatibilityEnabled != 0);
        }

        return int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? OverlayPersonalField(workflow, pokemon, edit.Field, value)
            : pokemon;
    }

    private static ZaPokemonRecord OverlayPersonalField(
        ZaPokemonWorkflow workflow,
        ZaPokemonRecord pokemon,
        string? field,
        int value)
    {
        var personal = pokemon.Personal;
        var stats = pokemon.BaseStats;
        var dex = pokemon.DexPresence;
        var abilities = pokemon.Abilities;
        var labels = workflow.EditableFields;
        var updated = field switch
        {
            ZaPokemonWorkflowService.HPField => pokemon with { BaseStats = stats with { HP = value, Total = RecalculateTotal(stats with { HP = value }) } },
            ZaPokemonWorkflowService.AttackField => pokemon with { BaseStats = stats with { Attack = value, Total = RecalculateTotal(stats with { Attack = value }) } },
            ZaPokemonWorkflowService.DefenseField => pokemon with { BaseStats = stats with { Defense = value, Total = RecalculateTotal(stats with { Defense = value }) } },
            ZaPokemonWorkflowService.SpecialAttackField => pokemon with { BaseStats = stats with { SpecialAttack = value, Total = RecalculateTotal(stats with { SpecialAttack = value }) } },
            ZaPokemonWorkflowService.SpecialDefenseField => pokemon with { BaseStats = stats with { SpecialDefense = value, Total = RecalculateTotal(stats with { SpecialDefense = value }) } },
            ZaPokemonWorkflowService.SpeedField => pokemon with { BaseStats = stats with { Speed = value, Total = RecalculateTotal(stats with { Speed = value }) } },
            ZaPokemonWorkflowService.Type1Field => pokemon with { Type1 = FormatType(value) },
            ZaPokemonWorkflowService.Type2Field => pokemon with { Type2 = FormatType(value) },
            ZaPokemonWorkflowService.Ability1Field => pokemon with { Abilities = abilities with { Ability1 = value, Ability1Label = ResolveFieldOptionLabel(labels, ZaPokemonWorkflowService.Ability1Field, value, ZaLabels.Ability(value)) } },
            ZaPokemonWorkflowService.Ability2Field => pokemon with { Abilities = abilities with { Ability2 = value, Ability2Label = ResolveFieldOptionLabel(labels, ZaPokemonWorkflowService.Ability2Field, value, ZaLabels.Ability(value)) } },
            ZaPokemonWorkflowService.HiddenAbilityField => pokemon with { Abilities = abilities with { HiddenAbility = value, HiddenAbilityLabel = ResolveFieldOptionLabel(labels, ZaPokemonWorkflowService.HiddenAbilityField, value, ZaLabels.Ability(value)) } },
            ZaPokemonWorkflowService.CatchRateField => pokemon with { CatchRate = value },
            ZaPokemonWorkflowService.EvolutionStageField => pokemon with { EvolutionStage = value },
            ZaPokemonWorkflowService.GenderRatioField => pokemon with { GenderRatio = value, GenderRatioLabel = FormatGender(value) },
            ZaPokemonWorkflowService.HeightField => pokemon with { Height = value },
            ZaPokemonWorkflowService.WeightField => pokemon with { Weight = value },
            ZaPokemonWorkflowService.IsPresentInGameField => pokemon with { DexPresence = dex with { IsPresentInGame = value != 0 } },
            ZaPokemonWorkflowService.RegionalDexIndexField => pokemon with { DexPresence = dex with { RegionalDexIndex = value, IsInAnyDex = value > 0 } },
            _ => pokemon,
        };

        return updated with { Personal = OverlayPersonalDetails(personal, field, value) };
    }

    private static ZaPokemonPersonalDetails OverlayPersonalDetails(
        ZaPokemonPersonalDetails personal,
        string? field,
        int value)
    {
        return field switch
        {
            ZaPokemonWorkflowService.Type1Field => personal with { Type1 = value },
            ZaPokemonWorkflowService.Type2Field => personal with { Type2 = value },
            ZaPokemonWorkflowService.CatchRateField => personal with { CatchRate = value },
            ZaPokemonWorkflowService.EvolutionStageField => personal with { EvolutionStage = value },
            ZaPokemonWorkflowService.EVYieldHPField => personal with { EVYieldHP = value },
            ZaPokemonWorkflowService.EVYieldAttackField => personal with { EVYieldAttack = value },
            ZaPokemonWorkflowService.EVYieldDefenseField => personal with { EVYieldDefense = value },
            ZaPokemonWorkflowService.EVYieldSpecialAttackField => personal with { EVYieldSpecialAttack = value },
            ZaPokemonWorkflowService.EVYieldSpecialDefenseField => personal with { EVYieldSpecialDefense = value },
            ZaPokemonWorkflowService.EVYieldSpeedField => personal with { EVYieldSpeed = value },
            ZaPokemonWorkflowService.GenderRatioField => personal with { GenderRatio = value },
            ZaPokemonWorkflowService.HatchCyclesField => personal with { HatchCycles = value },
            ZaPokemonWorkflowService.BaseFriendshipField => personal with { BaseFriendship = value },
            ZaPokemonWorkflowService.ExpGrowthField => personal with { ExpGrowth = value },
            ZaPokemonWorkflowService.EggGroup1Field => personal with { EggGroup1 = value },
            ZaPokemonWorkflowService.EggGroup2Field => personal with { EggGroup2 = value },
            ZaPokemonWorkflowService.FormField => personal with { Form = value, FormStatsIndex = value },
            ZaPokemonWorkflowService.ModelIdField => personal with { ModelId = (uint)value },
            ZaPokemonWorkflowService.ColorField => personal with { Color = value },
            ZaPokemonWorkflowService.HeightField => personal with { Height = value },
            ZaPokemonWorkflowService.WeightField => personal with { Weight = value },
            ZaPokemonWorkflowService.HatchedSpeciesField => personal with { HatchedSpecies = value },
            ZaPokemonWorkflowService.IsPresentInGameField => personal with { IsPresentInGame = value != 0 },
            ZaPokemonWorkflowService.RegionalDexIndexField => personal with { RegionalDexIndex = value },
            _ => personal,
        };
    }

    private static ZaPokemonRecord ApplyLearnsetOperation(
        ZaPokemonRecord pokemon,
        LearnsetOperation operation)
    {
        var learnset = pokemon.Learnset.ToList();
        var targetSlot = operation.Action == AddAction ? learnset.Count : operation.Slot;

        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                var rawLevel = operation.RawLevel ?? operation.Level ?? 1;
                var displayLevel = operation.Level ?? ZaPokemonWorkflowService.DecodeLearnsetDisplayLevel(rawLevel);
                var row = new ZaPokemonLearnsetMove(
                    targetSlot,
                    operation.MoveId ?? 0,
                    ZaLabels.Move(operation.MoveId ?? 0),
                    displayLevel,
                    rawLevel,
                    ZaPokemonWorkflowService.FormatLearnsetLevelLabel(rawLevel));
                if (targetSlot < learnset.Count)
                {
                    learnset[targetSlot] = row;
                }
                else
                {
                    learnset.Add(row);
                }

                break;
            case RemoveAction when targetSlot >= 0 && targetSlot < learnset.Count:
                learnset.RemoveAt(targetSlot);
                break;
            case MoveUpAction when targetSlot > 0 && targetSlot < learnset.Count:
                (learnset[targetSlot - 1], learnset[targetSlot]) = (learnset[targetSlot], learnset[targetSlot - 1]);
                break;
            case MoveDownAction when targetSlot >= 0 && targetSlot < learnset.Count - 1:
                (learnset[targetSlot + 1], learnset[targetSlot]) = (learnset[targetSlot], learnset[targetSlot + 1]);
                break;
            case MoveToAction when operation.MoveId is { } destination && targetSlot >= 0 && targetSlot < learnset.Count && destination >= 0 && destination < learnset.Count:
                var moved = learnset[targetSlot];
                learnset.RemoveAt(targetSlot);
                learnset.Insert(destination, moved);
                break;
        }

        return pokemon with
        {
            Learnset = learnset.Select((move, index) => move with { Slot = index }).ToArray(),
        };
    }

    private static ZaPokemonRecord ApplyEvolutionOperation(
        ZaPokemonRecord pokemon,
        EvolutionOperation operation)
    {
        var evolutions = pokemon.Evolutions.ToList();
        var targetSlot = operation.Action == AddAction ? evolutions.Count : operation.Slot;

        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                var definition = ZaPokemonWorkflowService.GetEvolutionMethodDefinition(operation.Method ?? 0);
                var row = new ZaPokemonEvolutionRecord(
                    targetSlot,
                    operation.Method ?? 0,
                    operation.Argument ?? 0,
                    operation.Species ?? 0,
                    operation.Form ?? 0,
                    operation.Level ?? 0,
                    definition.Name,
                    definition.ArgumentKind,
                    definition.ArgumentLabel,
                    (operation.Argument ?? 0).ToString(CultureInfo.InvariantCulture));
                if (targetSlot < evolutions.Count)
                {
                    evolutions[targetSlot] = row;
                }
                else
                {
                    evolutions.Add(row);
                }

                break;
            case RemoveAction when targetSlot >= 0 && targetSlot < evolutions.Count:
                evolutions.RemoveAt(targetSlot);
                break;
            case MoveUpAction when targetSlot > 0 && targetSlot < evolutions.Count:
                (evolutions[targetSlot - 1], evolutions[targetSlot]) = (evolutions[targetSlot], evolutions[targetSlot - 1]);
                break;
            case MoveDownAction when targetSlot >= 0 && targetSlot < evolutions.Count - 1:
                (evolutions[targetSlot + 1], evolutions[targetSlot]) = (evolutions[targetSlot], evolutions[targetSlot + 1]);
                break;
            case MoveToAction when operation.Method is { } destination && targetSlot >= 0 && targetSlot < evolutions.Count && destination >= 0 && destination < evolutions.Count:
                var moved = evolutions[targetSlot];
                evolutions.RemoveAt(targetSlot);
                evolutions.Insert(destination, moved);
                break;
        }

        return pokemon with
        {
            Evolutions = evolutions.Select((evolution, index) => evolution with { Slot = index }).ToArray(),
        };
    }

    private static ZaPokemonRecord OverlayCompatibility(
        ZaPokemonRecord pokemon,
        string groupId,
        int slot,
        bool enabled)
    {
        var compatibility = pokemon.Compatibility
            .Select(group =>
            {
                if (!string.Equals(group.GroupId, groupId, StringComparison.Ordinal))
                {
                    return group;
                }

                var entries = group.Entries
                    .Select(entry => entry.Slot == slot ? entry with { CanLearn = enabled } : entry)
                    .ToArray();
                return group with
                {
                    EnabledCount = entries.Count(entry => entry.CanLearn),
                    Entries = entries,
                };
            })
            .ToArray();

        return pokemon with { Compatibility = compatibility };
    }

    private static void ApplyEdit(
        IReadOnlyList<PersonalRow> rows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
            || personalId < 0
            || personalId >= rows.Count)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets a record outside the personal table.",
                ZaEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return;
        }

        var row = rows[personalId];
        if (TryParseLearnsetField(edit.Field, out _, out _))
        {
            var operation = ParseLearnsetOperation(edit, null, diagnostics);
            if (operation is not null)
            {
                ApplyLearnsetOperation(row.LevelupMoves, operation);
            }

            return;
        }

        if (TryParseEvolutionField(edit.Field, out _, out _))
        {
            var operation = ParseEvolutionOperation(edit, null, diagnostics);
            if (operation is not null)
            {
                ApplyEvolutionOperation(row.Evolutions, operation);
            }

            return;
        }

        if (TryParseCompatibilityField(edit.Field, out var groupId, out var slot))
        {
            if (!int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon compatibility edit value is invalid.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: edit.Field,
                    expected: "0 or 1"));
                return;
            }

            ApplyCompatibility(row, groupId, slot, parsed != 0);
            return;
        }

        if (!int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit value is invalid.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Integer value"));
            return;
        }

        ApplyPersonalField(row, edit.Field, value, diagnostics);
    }

    private static void ApplyPersonalField(
        PersonalRow row,
        string? field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            switch (field)
            {
                case ZaPokemonWorkflowService.HPField:
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Hp = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.AttackField:
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Atk = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.DefenseField:
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Def = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.SpecialAttackField:
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spa = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.SpecialDefenseField:
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spd = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.SpeedField:
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spe = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.Type1Field:
                    row.Type1 = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.Type2Field:
                    row.Type2 = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.Ability1Field:
                    row.Ability1 = ToUshort(value);
                    break;
                case ZaPokemonWorkflowService.Ability2Field:
                    row.Ability2 = ToUshort(value);
                    break;
                case ZaPokemonWorkflowService.HiddenAbilityField:
                    row.AbilityHidden = ToUshort(value);
                    break;
                case ZaPokemonWorkflowService.CatchRateField:
                    row.CatchRate = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.EvolutionStageField:
                    row.EvoStage = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.EVYieldHPField:
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Hp = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldAttackField:
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Atk = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldDefenseField:
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Def = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldSpecialAttackField:
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spa = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldSpecialDefenseField:
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spd = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldSpeedField:
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spe = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.GenderRatioField:
                    row.Gender = (row.Gender ?? new GenderInfoRow(0, 0)) with { Ratio = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.HatchCyclesField:
                    row.EggHatchCycles = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.BaseFriendshipField:
                    row.BaseFriendship = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.ExpGrowthField:
                    row.XpGrowth = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.EggGroup1Field:
                    row.EggGroup1 = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.EggGroup2Field:
                    row.EggGroup2 = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.FormField:
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Form = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.ModelIdField:
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Model = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.ColorField:
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Color = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.HeightField:
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Height = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.WeightField:
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Weight = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.HatchedSpeciesField:
                    row.EggHatch = (row.EggHatch ?? EggHatchInfoRow.Zero) with { Species = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.IsPresentInGameField:
                    row.IsPresent = value != 0;
                    break;
                case ZaPokemonWorkflowService.RegionalDexIndexField:
                    row.ZADexOrder = ToByte(value);
                    break;
                default:
                    diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
                    break;
            }
        }
        catch (OverflowException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit value is outside the target field range.",
                ZaEditSessionSupport.PokemonDomain,
                field: field,
                expected: "Safe editor value"));
        }
    }

    private static void ApplyCompatibility(PersonalRow row, string groupId, int slot, bool enabled)
    {
        var target = groupId switch
        {
            ZaPokemonWorkflowService.TechnicalMachineCompatibilityGroupId => row.TmMoves,
            ZaPokemonWorkflowService.EggMoveCompatibilityGroupId => row.EggMoves,
            ZaPokemonWorkflowService.ReminderMoveCompatibilityGroupId => row.ReminderMoves,
            _ => null,
        };
        if (target is null)
        {
            return;
        }

        var move = (ushort)slot;
        if (enabled)
        {
            if (!target.Contains(move))
            {
                target.Add(move);
                target.Sort();
            }
        }
        else
        {
            target.Remove(move);
        }
    }

    private static void ApplyLearnsetOperation(IList<LevelupMoveRow> learnset, LearnsetOperation operation)
    {
        var targetSlot = operation.Action == AddAction ? learnset.Count : operation.Slot;
        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                var row = new LevelupMoveRow(ToUshort(operation.MoveId ?? 0), ToUshort(operation.RawLevel ?? operation.Level ?? 1));
                if (targetSlot < learnset.Count)
                {
                    learnset[targetSlot] = row;
                }
                else
                {
                    learnset.Add(row);
                }

                break;
            case RemoveAction when targetSlot >= 0 && targetSlot < learnset.Count:
                learnset.RemoveAt(targetSlot);
                break;
            case MoveUpAction when targetSlot > 0 && targetSlot < learnset.Count:
                (learnset[targetSlot - 1], learnset[targetSlot]) = (learnset[targetSlot], learnset[targetSlot - 1]);
                break;
            case MoveDownAction when targetSlot >= 0 && targetSlot < learnset.Count - 1:
                (learnset[targetSlot + 1], learnset[targetSlot]) = (learnset[targetSlot], learnset[targetSlot + 1]);
                break;
            case MoveToAction when operation.MoveId is { } destination && targetSlot >= 0 && targetSlot < learnset.Count && destination >= 0 && destination < learnset.Count:
                var moved = learnset[targetSlot];
                learnset.RemoveAt(targetSlot);
                learnset.Insert(destination, moved);
                break;
        }
    }

    private static void ApplyEvolutionOperation(IList<EvolutionRow> evolutions, EvolutionOperation operation)
    {
        var targetSlot = operation.Action == AddAction ? evolutions.Count : operation.Slot;
        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                var row = new EvolutionRow(
                    ToUshort(operation.Level ?? 0),
                    ToUshort(operation.Method ?? 0),
                    ToUshort(operation.Argument ?? 0),
                    0,
                    0,
                    0,
                    ToUshort(operation.Species ?? 0),
                    ToUshort(operation.Form ?? 0));
                if (targetSlot < evolutions.Count)
                {
                    evolutions[targetSlot] = row;
                }
                else
                {
                    evolutions.Add(row);
                }

                break;
            case RemoveAction when targetSlot >= 0 && targetSlot < evolutions.Count:
                evolutions.RemoveAt(targetSlot);
                break;
            case MoveUpAction when targetSlot > 0 && targetSlot < evolutions.Count:
                (evolutions[targetSlot - 1], evolutions[targetSlot]) = (evolutions[targetSlot], evolutions[targetSlot - 1]);
                break;
            case MoveDownAction when targetSlot >= 0 && targetSlot < evolutions.Count - 1:
                (evolutions[targetSlot + 1], evolutions[targetSlot]) = (evolutions[targetSlot], evolutions[targetSlot + 1]);
                break;
            case MoveToAction when operation.Method is { } destination && targetSlot >= 0 && targetSlot < evolutions.Count && destination >= 0 && destination < evolutions.Count:
                var moved = evolutions[targetSlot];
                evolutions.RemoveAt(targetSlot);
                evolutions.Insert(destination, moved);
                break;
        }
    }

    private static IReadOnlyList<PersonalRow> ReadRows(byte[] bytes)
    {
        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(bytes));
        var rows = new List<PersonalRow>();
        for (var index = 0; index < table.EntryLength; index++)
        {
            var row = table.Entry(index);
            rows.Add(row is null ? PersonalRow.Empty() : PersonalRow.From(row.Value));
        }

        return rows;
    }

    private static byte[] WriteRows(IReadOnlyList<PersonalRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = ZaPersonalTable.CreateEntryVector(builder, offsets);
        ZaPersonalTable.Start(builder);
        ZaPersonalTable.AddEntry(builder, vector);
        var root = ZaPersonalTable.End(builder);
        ZaPersonalTable.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static LearnsetOperation? CreateLearnsetOperation(
        ZaPokemonRecord pokemon,
        string action,
        int? slot,
        int? moveId,
        int? level,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedAction = action == AddAction ? AddAction : action.Trim();
        var targetSlot = normalizedAction == AddAction ? pokemon.Learnset.Count : slot ?? -1;
        int? existingRawLevel = targetSlot >= 0 && targetSlot < pokemon.Learnset.Count
            ? pokemon.Learnset[targetSlot].RawLevel
            : null;
        int? rawLevel = level is { } displayLevel
            ? ZaPokemonWorkflowService.EncodeLearnsetRawLevel(displayLevel, existingRawLevel)
            : null;
        var operation = new LearnsetOperation(normalizedAction, targetSlot, moveId, level, rawLevel);
        ValidateLearnsetOperation(operation, pokemon, diagnostics);
        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? null : operation;
    }

    private static EvolutionOperation? CreateEvolutionOperation(
        ZaPokemonRecord pokemon,
        string action,
        int? slot,
        int? method,
        int? argument,
        int? species,
        int? form,
        int? level,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedAction = action == AddAction ? AddAction : action.Trim();
        var targetSlot = normalizedAction == AddAction ? pokemon.Evolutions.Count : slot ?? -1;
        var operation = new EvolutionOperation(normalizedAction, targetSlot, method, argument, species, form, level);
        ValidateEvolutionOperation(operation, pokemon, diagnostics);
        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? null : operation;
    }

    private static LearnsetOperation? ParseLearnsetOperation(
        PendingEdit edit,
        ZaPokemonRecord? pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseLearnsetField(edit.Field, out var action, out var slot)
            || !TryParseOperationValue(edit.NewValue, out var first, out var second))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon learnset edit is invalid.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Valid learnset operation"));
            return null;
        }

        int? rawLevel = second >= 0 ? second : null;
        var operation = new LearnsetOperation(
            action,
            slot,
            first >= 0 ? first : null,
            rawLevel is { } value ? ZaPokemonWorkflowService.DecodeLearnsetDisplayLevel(value) : null,
            rawLevel);
        if (pokemon is not null)
        {
            ValidateLearnsetOperation(operation, pokemon, diagnostics);
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? null : operation;
    }

    private static EvolutionOperation? ParseEvolutionOperation(
        PendingEdit edit,
        ZaPokemonRecord? pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseEvolutionField(edit.Field, out var action, out var slot)
            || !TryParseEvolutionValue(edit.NewValue, out var operation))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon evolution edit is invalid.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Valid evolution operation"));
            return null;
        }

        operation = operation with { Action = action, Slot = slot };
        if (pokemon is not null)
        {
            ValidateEvolutionOperation(operation, pokemon, diagnostics);
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? null : operation;
    }

    private static void ValidateLearnsetOperation(
        LearnsetOperation operation,
        ZaPokemonRecord pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                if (operation.MoveId is null or < 0 or > ushort.MaxValue
                    || operation.Level is null or < 0 or > byte.MaxValue
                    || operation.RawLevel is null or < 0 or > ushort.MaxValue)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset upserts require a move ID and a level from 0 to 255.", "moveId/level"));
                }

                if (operation.Action == UpsertAction && operation.Slot < 0)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset upsert requires a target slot.", "slot"));
                }

                break;
            case RemoveAction:
            case MoveUpAction:
            case MoveDownAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Learnset.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset operation targets a slot that is not loaded.", "slot"));
                }

                break;
            case MoveToAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Learnset.Count || operation.MoveId is null or < 0 || operation.MoveId >= pokemon.Learnset.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset move-to requires loaded source and destination slots.", "slot"));
                }

                break;
            default:
                diagnostics.Add(OperationDiagnostic($"Learnset action '{operation.Action}' is not supported.", "action"));
                break;
        }
    }

    private static void ValidateEvolutionOperation(
        EvolutionOperation operation,
        ZaPokemonRecord pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                if (operation.Method is null or < 0 or > ushort.MaxValue
                    || operation.Argument is null or < 0 or > ushort.MaxValue
                    || operation.Species is null or < 0 or > ushort.MaxValue
                    || operation.Form is null or < 0 or > ushort.MaxValue
                    || operation.Level is null or < 0 or > ushort.MaxValue)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution upserts require method, argument, species, form, and level.", "evolution"));
                }

                if (operation.Action == UpsertAction && operation.Slot < 0)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution upsert requires a target slot.", "slot"));
                }

                break;
            case RemoveAction:
            case MoveUpAction:
            case MoveDownAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Evolutions.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution operation targets a slot that is not loaded.", "slot"));
                }

                break;
            case MoveToAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Evolutions.Count || operation.Method is null or < 0 || operation.Method >= pokemon.Evolutions.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution move-to requires loaded source and destination slots.", "slot"));
                }

                break;
            default:
                diagnostics.Add(OperationDiagnostic($"Evolution action '{operation.Action}' is not supported.", "action"));
                break;
        }
    }

    private static ValidationDiagnostic OperationDiagnostic(string message, string field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.PokemonDomain,
            field: field,
            expected: "Valid Pokemon Data operation");
    }

    private static bool TryParseCompatibilityField(string? field, out string groupId, out int slot)
    {
        groupId = string.Empty;
        slot = -1;
        var parts = field?.Split(':');
        return parts is { Length: 3 }
            && string.Equals(parts[0], CompatibilityFieldPrefix, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(parts[1])
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot >= 0
            && ((groupId = parts[1]).Length > 0);
    }

    private static bool TryParseLearnsetField(string? field, out string action, out int slot)
    {
        return TryParseOperationField(field, LearnsetFieldPrefix, out action, out slot);
    }

    private static bool TryParseEvolutionField(string? field, out string action, out int slot)
    {
        return TryParseOperationField(field, EvolutionFieldPrefix, out action, out slot);
    }

    private static bool TryParseOperationField(string? field, string prefix, out string action, out int slot)
    {
        action = string.Empty;
        slot = -1;
        var parts = field?.Split(':');
        if (parts is not { Length: 3 }
            || !string.Equals(parts[0], prefix, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(parts[1])
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out slot))
        {
            return false;
        }

        action = parts[1];
        return true;
    }

    private static string CreateOperationField(string prefix, string action, int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}:{(action == AddAction ? UpsertAction : action)}:{slot}");
    }

    private static string FormatOperationValue(int? first, int? second)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{first ?? -1}|{second ?? -1}");
    }

    private static bool TryParseOperationValue(string? value, out int first, out int second)
    {
        first = -1;
        second = -1;
        var parts = value?.Split('|');
        return parts is { Length: 2 }
            && int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out first)
            && int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out second);
    }

    private static string FormatEvolutionValue(EvolutionOperation operation)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{operation.Method ?? -1}|{operation.Argument ?? -1}|{operation.Species ?? -1}|{operation.Form ?? -1}|{operation.Level ?? -1}");
    }

    private static bool TryParseEvolutionValue(string? value, out EvolutionOperation operation)
    {
        operation = new EvolutionOperation(string.Empty, -1, null, null, null, null, null);
        var parts = value?.Split('|');
        if (parts is not { Length: 5 }
            || !int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var method)
            || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var argument)
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var species)
            || !int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var form)
            || !int.TryParse(parts[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var level))
        {
            return false;
        }

        operation = new EvolutionOperation(
            string.Empty,
            -1,
            method >= 0 ? method : null,
            argument >= 0 ? argument : null,
            species >= 0 ? species : null,
            form >= 0 ? form : null,
            level >= 0 ? level : null);
        return true;
    }

    private static string CreateLearnsetSummary(ZaPokemonRecord pokemon, LearnsetOperation operation)
    {
        return operation.Action switch
        {
            AddAction or UpsertAction =>
                $"Set {pokemon.Name} learnset slot {operation.Slot} to Lv. {operation.Level} {ZaLabels.Move(operation.MoveId ?? 0)}.",
            RemoveAction => $"Remove {pokemon.Name} learnset slot {operation.Slot}.",
            MoveUpAction => $"Move {pokemon.Name} learnset slot {operation.Slot} up.",
            MoveDownAction => $"Move {pokemon.Name} learnset slot {operation.Slot} down.",
            MoveToAction => $"Move {pokemon.Name} learnset slot {operation.Slot} to slot {operation.MoveId}.",
            _ => $"Update {pokemon.Name} learnset slot {operation.Slot}.",
        };
    }

    private static string CreateEvolutionSummary(ZaPokemonRecord pokemon, EvolutionOperation operation)
    {
        return operation.Action switch
        {
            AddAction or UpsertAction =>
                $"Set {pokemon.Name} evolution slot {operation.Slot} to species {operation.Species} at level {operation.Level}.",
            RemoveAction => $"Remove {pokemon.Name} evolution slot {operation.Slot}.",
            MoveUpAction => $"Move {pokemon.Name} evolution slot {operation.Slot} up.",
            MoveDownAction => $"Move {pokemon.Name} evolution slot {operation.Slot} down.",
            MoveToAction => $"Move {pokemon.Name} evolution slot {operation.Slot} to slot {operation.Method}.",
            _ => $"Update {pokemon.Name} evolution slot {operation.Slot}.",
        };
    }

    private static int RecalculateTotal(ZaPokemonBaseStats stats)
    {
        return stats.HP + stats.Attack + stats.Defense + stats.SpecialAttack + stats.SpecialDefense + stats.Speed;
    }

    private static string ResolveFieldOptionLabel(
        IReadOnlyList<ZaPokemonEditableField> fields,
        string field,
        int value,
        string fallback)
    {
        var option = fields
            .FirstOrDefault(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal))
            ?.Options
            .FirstOrDefault(candidate => candidate.Value == value);
        return option?.Label is { } label
            ? StripNumericPrefix(label, value)
            : fallback;
    }

    private static string StripNumericPrefix(string label, int value)
    {
        var prefix = $"{value.ToString(CultureInfo.InvariantCulture)} ";
        return label.StartsWith(prefix, StringComparison.Ordinal) ? label[prefix.Length..] : label;
    }

    private static string FormatType(int type)
    {
        return type switch
        {
            0 => "Normal",
            1 => "Fighting",
            2 => "Flying",
            3 => "Poison",
            4 => "Ground",
            5 => "Rock",
            6 => "Bug",
            7 => "Ghost",
            8 => "Steel",
            9 => "Fire",
            10 => "Water",
            11 => "Grass",
            12 => "Electric",
            13 => "Psychic",
            14 => "Ice",
            15 => "Dragon",
            16 => "Dark",
            17 => "Fairy",
            _ => $"Type {type}",
        };
    }

    private static string FormatGender(int ratio)
    {
        return ratio switch
        {
            0 => "Always male or genderless",
            254 => "Always female",
            255 => "Genderless",
            _ => $"{ratio}/254 female",
        };
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Pokemon field '{field}' is not supported by Pokemon Legends Z-A Pokemon Data yet.",
            ZaEditSessionSupport.PokemonDomain,
            field: "field",
            expected: "Supported Z-A Pokemon personal, learnset, evolution, or compatibility field");
    }

    private static byte ToByte(int value) => checked((byte)value);

    private static ushort ToUshort(int value) => checked((ushort)value);

    private sealed record LearnsetOperation(string Action, int Slot, int? MoveId, int? Level, int? RawLevel);

    private sealed record EvolutionOperation(
        string Action,
        int Slot,
        int? Method,
        int? Argument,
        int? Species,
        int? Form,
        int? Level);

    private sealed class PersonalRow
    {
        public SpeciesInfoRow? Species { get; set; }
        public bool IsPresent { get; set; }
        public byte ZADexOrder { get; set; }
        public byte Type1 { get; set; }
        public byte Type2 { get; set; }
        public ushort Ability1 { get; set; }
        public ushort Ability2 { get; set; }
        public ushort AbilityHidden { get; set; }
        public byte XpGrowth { get; set; }
        public byte CatchRate { get; set; }
        public GenderInfoRow? Gender { get; set; }
        public byte EggGroup1 { get; set; }
        public byte EggGroup2 { get; set; }
        public EggHatchInfoRow? EggHatch { get; set; }
        public byte EggHatchCycles { get; set; }
        public byte BaseFriendship { get; set; }
        public ushort Unknown16 { get; set; }
        public bool HasUnknown16 { get; set; }
        public byte EvoStage { get; set; }
        public ushort Unknown18 { get; set; }
        public bool HasUnknown18 { get; set; }
        public StatInfoRow? EvYield { get; set; }
        public StatInfoRow? BaseStats { get; set; }
        public List<EvolutionRow> Evolutions { get; } = [];
        public List<ushort> TmMoves { get; } = [];
        public List<ushort> EggMoves { get; } = [];
        public List<ushort> ReminderMoves { get; } = [];
        public List<LevelupMoveRow> LevelupMoves { get; } = [];

        public static PersonalRow Empty()
        {
            return new PersonalRow();
        }

        public static PersonalRow From(ZaPersonal row)
        {
            var result = new PersonalRow
            {
                Species = row.Species is { } species ? SpeciesInfoRow.From(species) : null,
                IsPresent = row.IsPresent,
                ZADexOrder = row.ZADexOrder,
                Type1 = row.Type1,
                Type2 = row.Type2,
                Ability1 = row.Ability1,
                Ability2 = row.Ability2,
                AbilityHidden = row.AbilityHidden,
                XpGrowth = row.XpGrowth,
                CatchRate = row.CatchRate,
                Gender = row.Gender is { } gender ? GenderInfoRow.From(gender) : null,
                EggGroup1 = row.EggGroup1,
                EggGroup2 = row.EggGroup2,
                EggHatch = row.EggHatch is { } eggHatch ? EggHatchInfoRow.From(eggHatch) : null,
                EggHatchCycles = row.EggHatchCycles,
                BaseFriendship = row.BaseFriendship,
                Unknown16 = row.Unknown16,
                HasUnknown16 = row.HasUnknown16,
                EvoStage = row.EvoStage,
                Unknown18 = row.Unknown18,
                HasUnknown18 = row.HasUnknown18,
                EvYield = row.EvYield is { } evYield ? StatInfoRow.From(evYield) : null,
                BaseStats = row.BaseStats is { } baseStats ? StatInfoRow.From(baseStats) : null,
            };

            for (var index = 0; index < row.EvolutionsLength; index++)
            {
                var evolution = row.Evolutions(index);
                if (evolution is not null)
                {
                    result.Evolutions.Add(EvolutionRow.From(evolution.Value));
                }
            }

            result.TmMoves.AddRange(row.GetTmMovesArray());
            result.EggMoves.AddRange(row.GetEggMovesArray());
            result.ReminderMoves.AddRange(row.GetReminderMovesArray());
            for (var index = 0; index < row.LevelupMovesLength; index++)
            {
                var learnedMove = row.LevelupMoves(index);
                if (learnedMove is not null)
                {
                    result.LevelupMoves.Add(LevelupMoveRow.From(learnedMove.Value));
                }
            }

            return result;
        }

        public Offset<ZaPersonal> Write(FlatBufferBuilder builder)
        {
            var evolutionsOffset = CreateEvolutionsVector(builder, Evolutions);
            var tmMovesOffset = ZaPersonal.CreateUshortVector(builder, TmMoves);
            var eggMovesOffset = ZaPersonal.CreateUshortVector(builder, EggMoves);
            var reminderMovesOffset = ZaPersonal.CreateUshortVector(builder, ReminderMoves);
            var levelupMovesOffset = CreateLevelupMovesVector(builder, LevelupMoves);

            ZaPersonal.Start(builder);
            ZaPersonal.AddLevelupMoves(builder, levelupMovesOffset);
            ZaPersonal.AddReminderMoves(builder, reminderMovesOffset);
            ZaPersonal.AddEggMoves(builder, eggMovesOffset);
            ZaPersonal.AddTmMoves(builder, tmMovesOffset);
            ZaPersonal.AddEvolutions(builder, evolutionsOffset);
            if (BaseStats is not null)
            {
                ZaPersonal.AddBaseStats(builder, BaseStats.Write(builder));
            }

            if (EvYield is not null)
            {
                ZaPersonal.AddEvYield(builder, EvYield.Write(builder));
            }

            if (HasUnknown18)
            {
                ZaPersonal.AddUnknown18(builder, Unknown18);
            }

            ZaPersonal.AddEvoStage(builder, EvoStage);
            if (HasUnknown16)
            {
                ZaPersonal.AddUnknown16(builder, Unknown16);
            }

            ZaPersonal.AddBaseFriendship(builder, BaseFriendship);
            ZaPersonal.AddEggHatchCycles(builder, EggHatchCycles);
            if (EggHatch is not null)
            {
                ZaPersonal.AddEggHatch(builder, EggHatch.Write(builder));
            }

            ZaPersonal.AddEggGroup2(builder, EggGroup2);
            ZaPersonal.AddEggGroup1(builder, EggGroup1);
            if (Gender is not null)
            {
                ZaPersonal.AddGender(builder, Gender.Write(builder));
            }

            ZaPersonal.AddCatchRate(builder, CatchRate);
            ZaPersonal.AddXpGrowth(builder, XpGrowth);
            ZaPersonal.AddAbilityHidden(builder, AbilityHidden);
            ZaPersonal.AddAbility2(builder, Ability2);
            ZaPersonal.AddAbility1(builder, Ability1);
            ZaPersonal.AddType2(builder, Type2);
            ZaPersonal.AddType1(builder, Type1);
            ZaPersonal.AddZADexOrder(builder, ZADexOrder);
            ZaPersonal.AddIsPresent(builder, IsPresent);
            if (Species is not null)
            {
                ZaPersonal.AddSpecies(builder, Species.Write(builder));
            }

            return ZaPersonal.End(builder);
        }

        private static VectorOffset CreateEvolutionsVector(FlatBufferBuilder builder, IReadOnlyList<EvolutionRow> evolutions)
        {
            ZaPersonal.StartEvolutionsVector(builder, evolutions.Count);
            for (var index = evolutions.Count - 1; index >= 0; index--)
            {
                evolutions[index].Write(builder);
            }

            return builder.EndVector();
        }

        private static VectorOffset CreateLevelupMovesVector(FlatBufferBuilder builder, IReadOnlyList<LevelupMoveRow> moves)
        {
            ZaPersonal.StartLevelupMovesVector(builder, moves.Count);
            for (var index = moves.Count - 1; index >= 0; index--)
            {
                moves[index].Write(builder);
            }

            return builder.EndVector();
        }
    }

    private sealed record SpeciesInfoRow(
        ushort Species,
        ushort Form,
        ushort Model,
        byte Color,
        byte BodyType,
        ushort Height,
        ushort Weight,
        byte Reserved,
        byte Reserved1,
        byte Reserved2)
    {
        public static readonly SpeciesInfoRow Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public static SpeciesInfoRow From(ZaSpeciesInfo row) =>
            new(row.Species, row.Form, row.Model, row.Color, row.BodyType, row.Height, row.Weight, row.Reserved, row.Reserved1, row.Reserved2);

        public Offset<ZaSpeciesInfo> Write(FlatBufferBuilder builder) =>
            ZaSpeciesInfo.Create(builder, Species, Form, Model, Color, BodyType, Height, Weight, Reserved, Reserved1, Reserved2);
    }

    private sealed record GenderInfoRow(byte Group, byte Ratio)
    {
        public static GenderInfoRow From(ZaGenderInfo row) => new(row.Group, row.Ratio);

        public Offset<ZaGenderInfo> Write(FlatBufferBuilder builder) =>
            ZaGenderInfo.Create(builder, Group, Ratio);
    }

    private sealed record EggHatchInfoRow(ushort Species, ushort Form, ushort FormFlags, ushort FormEverstone)
    {
        public static readonly EggHatchInfoRow Zero = new(0, 0, 0, 0);

        public static EggHatchInfoRow From(ZaEggHatchInfo row) =>
            new(row.Species, row.Form, row.FormFlags, row.FormEverstone);

        public Offset<ZaEggHatchInfo> Write(FlatBufferBuilder builder) =>
            ZaEggHatchInfo.Create(builder, Species, Form, FormFlags, FormEverstone);
    }

    private sealed record StatInfoRow(byte Hp, byte Atk, byte Def, byte Spa, byte Spd, byte Spe)
    {
        public static readonly StatInfoRow Zero = new(0, 0, 0, 0, 0, 0);

        public static StatInfoRow From(ZaStatInfo row) =>
            new(row.Hp, row.Atk, row.Def, row.Spa, row.Spd, row.Spe);

        public Offset<ZaStatInfo> Write(FlatBufferBuilder builder) =>
            ZaStatInfo.Create(builder, Hp, Atk, Def, Spa, Spd, Spe);
    }

    private sealed record EvolutionRow(
        ushort Level,
        ushort Condition,
        ushort Parameter,
        ushort Reserved3,
        ushort Reserved4,
        ushort Reserved5,
        ushort Species,
        ushort Form)
    {
        public static EvolutionRow From(ZaEvolutionData row) =>
            new(row.Level, row.Condition, row.Parameter, row.Reserved3, row.Reserved4, row.Reserved5, row.Species, row.Form);

        public Offset<ZaEvolutionData> Write(FlatBufferBuilder builder) =>
            ZaEvolutionData.Create(builder, Level, Condition, Parameter, Reserved3, Reserved4, Reserved5, Species, Form);
    }

    private sealed record LevelupMoveRow(ushort Move, ushort Level)
    {
        public static LevelupMoveRow From(ZaLevelUpMoveData row) => new(row.Move, row.Level);

        public Offset<ZaLevelUpMoveData> Write(FlatBufferBuilder builder) =>
            ZaLevelUpMoveData.Create(builder, Move, Level);
    }
}
