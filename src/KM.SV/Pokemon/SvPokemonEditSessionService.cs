// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Items;
using KM.SV.Pokemon;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Pokemon;

internal sealed class SvPokemonEditSessionService
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
    private const string GlobalRecordId = "all";
    private const string GlobalEvYieldField = "evYieldAll";
    private const string GlobalExpYieldField = "expYieldAll";
    private const string RemoveYieldValue = "remove";
    private const string RestoreYieldValue = "restore";

    private static readonly HashSet<string> EvYieldFields =
    [
        SvPokemonWorkflowService.EVYieldHPField,
        SvPokemonWorkflowService.EVYieldAttackField,
        SvPokemonWorkflowService.EVYieldDefenseField,
        SvPokemonWorkflowService.EVYieldSpecialAttackField,
        SvPokemonWorkflowService.EVYieldSpecialDefenseField,
        SvPokemonWorkflowService.EVYieldSpeedField,
    ];

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvPokemonWorkflowService pokemonWorkflowService;

    public SvPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvPokemonWorkflowService? pokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.pokemonWorkflowService = pokemonWorkflowService ?? new SvPokemonWorkflowService(this.fileSource);
    }

    public SvPokemonEditResult UpdateField(
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

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (IsGlobalYieldField(field))
        {
            var globalPendingEdit = CreateGlobalYieldPendingEdit(field, value, diagnostics);
            if (globalPendingEdit is null)
            {
                return new SvPokemonEditResult(workflow, currentSession, diagnostics);
            }

            var globalUpdatedSession = ReplacePendingPokemonEdit(currentSession, globalPendingEdit);
            return new SvPokemonEditResult(
                OverlayPendingEdits(loadedWorkflow, globalUpdatedSession.PendingEdits),
                globalUpdatedSession,
                diagnostics);
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                SvEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreateFieldPendingEdit(workflow, pokemon, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingPokemonEdit(currentSession, pendingEdit);
        return new SvPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvPokemonEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvPokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon Data batch update is missing a field or value.",
                    SvEditSessionSupport.PokemonDomain,
                    field: "updates",
                    expected: "Complete Pokemon Data field update"));
                continue;
            }

            PendingEdit? pendingEdit;
            if (IsGlobalYieldField(update.Field))
            {
                pendingEdit = CreateGlobalYieldPendingEdit(update.Field, update.Value, diagnostics);
            }
            else
            {
                var pokemon = effectiveWorkflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == update.PersonalId);
                if (pokemon is null)
                {
                    diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Pokemon personal record {update.PersonalId} is not present in the loaded Pokemon Data workflow.",
                        SvEditSessionSupport.PokemonDomain,
                        field: "personalId",
                        expected: "Existing Pokemon personal record"));
                    continue;
                }

                pendingEdit = CreateFieldPendingEdit(effectiveWorkflow, pokemon, update.Field, update.Value, diagnostics);
            }

            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ReplacePendingPokemonEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits);
        }

        return new SvPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvPokemonEditResult UpdateLearnset(
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

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                SvEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var operation = CreateLearnsetOperation(pokemon, action, slot, moveId, level, diagnostics);
        if (operation is null)
        {
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.PokemonDomain,
            CreateLearnsetSummary(pokemon, operation),
            new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
            pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            CreateOperationField(LearnsetFieldPrefix, operation.Action, operation.Slot),
            FormatOperationValue(operation.MoveId, operation.RawLevel));
        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);

        return new SvPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvPokemonEditResult UpdateEvolution(
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

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                SvEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var operation = CreateEvolutionOperation(
            pokemon,
            action,
            slot,
            method,
            argument,
            species,
            form,
            level,
            diagnostics);
        if (operation is null)
        {
            return new SvPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.PokemonDomain,
            CreateEvolutionSummary(pokemon, operation),
            new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
            pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            CreateOperationField(EvolutionFieldPrefix, operation.Action, operation.Slot),
            FormatEvolutionValue(operation));
        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);

        return new SvPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = pokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.PokemonDomain,
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
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Pokemon Data change is valid.",
                SvEditSessionSupport.PokemonDomain));
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
            SvEditSessionSupport.PokemonDomain,
            SvDataPaths.PersonalArray,
            "Pokemon Data",
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
                SvEditSessionSupport.PokemonDomain,
                expected: "Current reviewed Pokemon Data change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, SvDataPaths.PersonalArray);
            var rows = ReadRows(source.Bytes);
            var baseRows = NeedsBaseRows(session.PendingEdits)
                ? ReadRows(fileSource.ReadBase(project, SvDataPaths.PersonalArray).Bytes)
                : rows;
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, baseRows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            SvWorkflowFileSource.Write(paths, SvDataPaths.PersonalArray, WriteRows(rows), outputMode);
            writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.PersonalArray, outputMode));
            if (outputMode == SvOutputMode.Standalone)
            {
                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                SvEditSessionSupport.CreateApplyOutputMessage("Pokemon Data", outputMode),
                SvEditSessionSupport.PokemonDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data output could not be written: {exception.Message}",
                SvEditSessionSupport.PokemonDomain,
                file: $"romfs/{SvDataPaths.PersonalArray}",
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreateFieldPendingEdit(
        SvPokemonWorkflow workflow,
        SvPokemonRecord pokemon,
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
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon compatibility edit targets a move slot that is not loaded.",
                    SvEditSessionSupport.PokemonDomain,
                    field: normalizedField,
                    expected: "Existing compatibility move slot"));
                return null;
            }

            var parsedValue = SvEditSessionSupport.TryParseInt(
                value,
                0,
                1,
                normalizedField,
                SvEditSessionSupport.PokemonDomain,
                diagnostics);
            if (parsedValue is null)
            {
                return null;
            }

            return SvEditSessionSupport.CreatePendingEdit(
                SvEditSessionSupport.PokemonDomain,
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

        var valueKind = string.Equals(editableField.ValueKind, "boolean", StringComparison.Ordinal) ? "boolean" : "integer";
        var parsed = SvEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            SvEditSessionSupport.PokemonDomain,
            diagnostics);
        if (parsed is null)
        {
            return null;
        }

        if (string.Equals(normalizedField, SvPokemonWorkflowService.BaseExperienceField, StringComparison.Ordinal))
        {
            ValidateBaseExperienceValue(pokemon, parsed.Value, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return null;
            }
        }

        var displayValue = valueKind == "boolean"
            ? parsed.Value == 0 ? "disabled" : "enabled"
            : parsed.Value.ToString(CultureInfo.InvariantCulture);
        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.PokemonDomain,
            $"Set {pokemon.Name} {editableField.Label.ToLowerInvariant()} to {displayValue}.",
            new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
            pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsed.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static PendingEdit? CreateGlobalYieldPendingEdit(
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var normalizedValue = value.Trim();
        if (!IsGlobalYieldField(normalizedField))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (!IsGlobalYieldAction(normalizedValue))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon yield action '{normalizedValue}' is not supported.",
                SvEditSessionSupport.PokemonDomain,
                field: normalizedField,
                expected: "remove or restore"));
            return null;
        }

        var target = string.Equals(normalizedField, GlobalEvYieldField, StringComparison.Ordinal)
            ? "EV yield"
            : "EXP yield";
        var action = string.Equals(normalizedValue, RemoveYieldValue, StringComparison.Ordinal)
            ? "Remove"
            : "Restore";

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.PokemonDomain,
            $"{action} all Pokemon {target}.",
            new ProjectFileReference(ProjectFileLayer.Base, $"romfs/{SvDataPaths.PersonalArray}"),
            GlobalRecordId,
            normalizedField,
            normalizedValue);
    }

    private static void ValidateGlobalYieldEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsGlobalYieldAction(edit.NewValue))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon yield edit uses an invalid action.",
                SvEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "remove or restore"));
        }
    }

    private static void ValidateBaseExperienceValue(
        SvPokemonRecord pokemon,
        int baseExperience,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SvPokemonExperience.TryCalculateExpAddend(
                pokemon.BaseStats.Total,
                pokemon.EvolutionStage,
                baseExperience,
                out _))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Base EXP cannot be represented by Scarlet/Violet's stored EXP addend for this Pokemon's current stats and evolution stage.",
                SvEditSessionSupport.PokemonDomain,
                field: SvPokemonWorkflowService.BaseExperienceField,
                expected: "Base EXP that maps to a signed 16-bit S/V addend"));
        }
    }

    private static void ValidatePendingEdit(
        SvPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.PokemonDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Pokemon Data.",
                SvEditSessionSupport.PokemonDomain,
                expected: SvEditSessionSupport.PokemonDomain));
            return;
        }

        if (IsGlobalYieldEdit(edit))
        {
            ValidateGlobalYieldEdit(edit, diagnostics);
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets an invalid personal record.",
                SvEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return;
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets a record that is not loaded.",
                SvEditSessionSupport.PokemonDomain,
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
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon compatibility edit targets a move slot that is not loaded.",
                    SvEditSessionSupport.PokemonDomain,
                    field: edit.Field,
                    expected: "Existing compatibility move slot"));
                return;
            }

            _ = SvEditSessionSupport.TryParseInt(
                edit.NewValue,
                0,
                1,
                edit.Field,
                SvEditSessionSupport.PokemonDomain,
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

        var parsed = SvEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            SvEditSessionSupport.PokemonDomain,
            diagnostics);
        if (parsed is not null && string.Equals(edit.Field, SvPokemonWorkflowService.BaseExperienceField, StringComparison.Ordinal))
        {
            ValidateBaseExperienceValue(pokemon, parsed.Value, diagnostics);
        }
    }

    private static SvPokemonWorkflow OverlayPendingEdits(SvPokemonWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static EditSession ReplacePendingPokemonEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !ShouldReplacePendingEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool ShouldReplacePendingEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        if (!string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsGlobalYieldEdit(pendingEdit))
        {
            return IsSameGlobalYieldTarget(candidate, pendingEdit)
                || (IsGlobalExpYieldEdit(pendingEdit)
                    && string.Equals(candidate.Field, SvPokemonWorkflowService.BaseExperienceField, StringComparison.Ordinal))
                || (IsGlobalEvYieldEdit(pendingEdit)
                    && EvYieldFields.Contains(candidate.Field ?? string.Empty));
        }

        if (string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal))
        {
            return true;
        }

        if (IsGlobalExpYieldEdit(candidate)
            && string.Equals(pendingEdit.Field, SvPokemonWorkflowService.BaseExperienceField, StringComparison.Ordinal))
        {
            return true;
        }

        if (IsGlobalEvYieldEdit(candidate) && EvYieldFields.Contains(pendingEdit.Field ?? string.Empty))
        {
            return true;
        }

        return false;
    }

    private static SvPokemonWorkflow OverlayPendingEdit(SvPokemonWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.PokemonDomain, StringComparison.Ordinal))
        {
            return workflow;
        }

        if (IsGlobalYieldEdit(edit))
        {
            return OverlayGlobalYieldEdit(workflow, edit);
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
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

    private static SvPokemonRecord OverlayPokemon(
        SvPokemonWorkflow workflow,
        SvPokemonRecord pokemon,
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

        if (!int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return pokemon;
        }

        return OverlayPersonalField(workflow, pokemon, edit.Field, value);
    }

    private static SvPokemonRecord OverlayPersonalField(
        SvPokemonWorkflow workflow,
        SvPokemonRecord pokemon,
        string? field,
        int value)
    {
        var personal = pokemon.Personal;
        var stats = pokemon.BaseStats;
        var dex = pokemon.DexPresence;
        var abilities = pokemon.Abilities;
        var updated = field switch
        {
            SvPokemonWorkflowService.HPField => pokemon with { BaseStats = stats with { HP = value, Total = RecalculateTotal(stats with { HP = value }) } },
            SvPokemonWorkflowService.AttackField => pokemon with { BaseStats = stats with { Attack = value, Total = RecalculateTotal(stats with { Attack = value }) } },
            SvPokemonWorkflowService.DefenseField => pokemon with { BaseStats = stats with { Defense = value, Total = RecalculateTotal(stats with { Defense = value }) } },
            SvPokemonWorkflowService.SpecialAttackField => pokemon with { BaseStats = stats with { SpecialAttack = value, Total = RecalculateTotal(stats with { SpecialAttack = value }) } },
            SvPokemonWorkflowService.SpecialDefenseField => pokemon with { BaseStats = stats with { SpecialDefense = value, Total = RecalculateTotal(stats with { SpecialDefense = value }) } },
            SvPokemonWorkflowService.SpeedField => pokemon with { BaseStats = stats with { Speed = value, Total = RecalculateTotal(stats with { Speed = value }) } },
            SvPokemonWorkflowService.Type1Field => pokemon with { Type1 = FormatType(value) },
            SvPokemonWorkflowService.Type2Field => pokemon with { Type2 = FormatType(value) },
            SvPokemonWorkflowService.Ability1Field => pokemon with { Abilities = abilities with { Ability1 = value, Ability1Label = SvLabels.Ability(value) } },
            SvPokemonWorkflowService.Ability2Field => pokemon with { Abilities = abilities with { Ability2 = value, Ability2Label = SvLabels.Ability(value) } },
            SvPokemonWorkflowService.HiddenAbilityField => pokemon with { Abilities = abilities with { HiddenAbility = value, HiddenAbilityLabel = SvLabels.Ability(value) } },
            SvPokemonWorkflowService.CatchRateField => pokemon with { CatchRate = value },
            SvPokemonWorkflowService.EvolutionStageField => pokemon with { EvolutionStage = value },
            SvPokemonWorkflowService.GenderRatioField => pokemon with { GenderRatio = value, GenderRatioLabel = FormatGender(value) },
            SvPokemonWorkflowService.BaseExperienceField => pokemon with { BaseExperience = value },
            SvPokemonWorkflowService.HeightField => pokemon with { Height = value },
            SvPokemonWorkflowService.WeightField => pokemon with { Weight = value },
            SvPokemonWorkflowService.IsPresentInGameField => pokemon with { DexPresence = dex with { IsPresentInGame = value != 0 } },
            SvPokemonWorkflowService.RegionalDexIndexField => pokemon with { DexPresence = dex with { RegionalDexIndex = value } },
            SvPokemonWorkflowService.ArmorDexIndexField => pokemon with { DexPresence = dex with { ArmorDexIndex = value } },
            SvPokemonWorkflowService.CrownDexIndexField => pokemon with { DexPresence = dex with { CrownDexIndex = value } },
            _ => pokemon,
        };

        return updated with { Personal = OverlayPersonalDetails(personal, field, value) };
    }

    private static SvPokemonPersonalDetails OverlayPersonalDetails(
        SvPokemonPersonalDetails personal,
        string? field,
        int value)
    {
        return field switch
        {
            SvPokemonWorkflowService.Type1Field => personal with { Type1 = value },
            SvPokemonWorkflowService.Type2Field => personal with { Type2 = value },
            SvPokemonWorkflowService.CatchRateField => personal with { CatchRate = value },
            SvPokemonWorkflowService.EvolutionStageField => personal with { EvolutionStage = value },
            SvPokemonWorkflowService.EVYieldHPField => personal with { EVYieldHP = value },
            SvPokemonWorkflowService.EVYieldAttackField => personal with { EVYieldAttack = value },
            SvPokemonWorkflowService.EVYieldDefenseField => personal with { EVYieldDefense = value },
            SvPokemonWorkflowService.EVYieldSpecialAttackField => personal with { EVYieldSpecialAttack = value },
            SvPokemonWorkflowService.EVYieldSpecialDefenseField => personal with { EVYieldSpecialDefense = value },
            SvPokemonWorkflowService.EVYieldSpeedField => personal with { EVYieldSpeed = value },
            SvPokemonWorkflowService.GenderRatioField => personal with { GenderRatio = value },
            SvPokemonWorkflowService.HatchCyclesField => personal with { HatchCycles = value },
            SvPokemonWorkflowService.BaseFriendshipField => personal with { BaseFriendship = value },
            SvPokemonWorkflowService.ExpGrowthField => personal with { ExpGrowth = value },
            SvPokemonWorkflowService.EggGroup1Field => personal with { EggGroup1 = value },
            SvPokemonWorkflowService.EggGroup2Field => personal with { EggGroup2 = value },
            SvPokemonWorkflowService.BaseExperienceField => personal with { BaseExperience = value },
            SvPokemonWorkflowService.FormField => personal with { Form = value, FormStatsIndex = value },
            SvPokemonWorkflowService.ModelIdField => personal with { ModelId = (uint)value },
            SvPokemonWorkflowService.ColorField => personal with { Color = value },
            SvPokemonWorkflowService.HeightField => personal with { Height = value },
            SvPokemonWorkflowService.WeightField => personal with { Weight = value },
            SvPokemonWorkflowService.IsPresentInGameField => personal with { IsPresentInGame = value != 0 },
            SvPokemonWorkflowService.RegionalDexIndexField => personal with { RegionalDexIndex = value },
            SvPokemonWorkflowService.ArmorDexIndexField => personal with { ArmorDexIndex = value },
            SvPokemonWorkflowService.CrownDexIndexField => personal with { CrownDexIndex = value },
            _ => personal,
        };
    }

    private static SvPokemonWorkflow OverlayGlobalYieldEdit(SvPokemonWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.NewValue, RemoveYieldValue, StringComparison.Ordinal))
        {
            return workflow;
        }

        return workflow with
        {
            Pokemon = workflow.Pokemon
                .Select(pokemon => edit.Field switch
                {
                    GlobalEvYieldField => OverlayAllEvYields(pokemon, 0),
                    GlobalExpYieldField => OverlayPersonalField(workflow, pokemon, SvPokemonWorkflowService.BaseExperienceField, 0),
                    _ => pokemon,
                })
                .ToArray(),
        };
    }

    private static SvPokemonRecord OverlayAllEvYields(SvPokemonRecord pokemon, int value)
    {
        return pokemon with
        {
            Personal = pokemon.Personal with
            {
                EVYieldHP = value,
                EVYieldAttack = value,
                EVYieldDefense = value,
                EVYieldSpecialAttack = value,
                EVYieldSpecialDefense = value,
                EVYieldSpeed = value,
            },
        };
    }

    private static SvPokemonRecord ApplyLearnsetOperation(
        SvPokemonRecord pokemon,
        LearnsetOperation operation)
    {
        var learnset = pokemon.Learnset.ToList();
        var targetSlot = operation.Action == AddAction ? learnset.Count : operation.Slot;

        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                var row = new SvPokemonLearnsetMove(
                    targetSlot,
                    operation.MoveId ?? 0,
                    SvLabels.Move(operation.MoveId ?? 0),
                    operation.Level ?? 1,
                    operation.RawLevel ?? operation.Level ?? 1,
                    (operation.RawLevel ?? operation.Level ?? 1) == SvLearnsetLevel.EvolutionRawLevel
                        ? SvLearnsetLevel.EvolutionLabel
                        : null);
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

    private static SvPokemonRecord ApplyEvolutionOperation(
        SvPokemonRecord pokemon,
        EvolutionOperation operation)
    {
        var evolutions = pokemon.Evolutions.ToList();
        var targetSlot = operation.Action == AddAction ? evolutions.Count : operation.Slot;

        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                var row = new SvPokemonEvolutionRecord(
                    targetSlot,
                    operation.Method ?? 0,
                    operation.Argument ?? 0,
                    operation.Species ?? 0,
                    operation.Form ?? 0,
                    operation.Level ?? 0,
                    $"Method {operation.Method ?? 0}",
                    "value",
                    "Parameter",
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
        }

        return pokemon with
        {
            Evolutions = evolutions.Select((evolution, index) => evolution with { Slot = index }).ToArray(),
        };
    }

    private static SvPokemonRecord OverlayCompatibility(
        SvPokemonRecord pokemon,
        string groupId,
        int slot,
        bool enabled)
    {
        return pokemon with
        {
            Compatibility = pokemon.Compatibility
                .Select(group => group.GroupId == groupId
                    ? group with
                    {
                        Entries = group.Entries
                            .Select(entry => entry.Slot == slot ? entry with { CanLearn = enabled } : entry)
                            .ToArray(),
                        EnabledCount = group.Entries.Count(entry => entry.Slot == slot ? enabled : entry.CanLearn),
                    }
                    : group)
                .ToArray(),
        };
    }

    private static void ApplyEdit(
        IReadOnlyList<PersonalRow> rows,
        IReadOnlyList<PersonalRow> baseRows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.PokemonDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit is not valid for apply.",
                SvEditSessionSupport.PokemonDomain,
                expected: "Valid Pokemon Data edit"));
            return;
        }

        if (IsGlobalYieldEdit(edit))
        {
            ApplyGlobalYieldEdit(rows, baseRows, edit, diagnostics);
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets an invalid personal record.",
                SvEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return;
        }

        var row = rows.ElementAtOrDefault(personalId);
        if (row is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the source personal array.",
                SvEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing source personal record"));
            return;
        }

        if (TryParseLearnsetField(edit.Field, out _, out _)
            && ParseLearnsetOperation(edit, pokemon: null, diagnostics) is { } learnsetOperation)
        {
            ApplyLearnsetEdit(row, learnsetOperation);
            return;
        }

        if (TryParseEvolutionField(edit.Field, out _, out _)
            && ParseEvolutionOperation(edit, pokemon: null, diagnostics) is { } evolutionOperation)
        {
            ApplyEvolutionEdit(row, evolutionOperation);
            return;
        }

        if (TryParseCompatibilityField(edit.Field, out var groupId, out var slot))
        {
            if (int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var enabled))
            {
                ApplyCompatibilityEdit(row, groupId, slot, enabled != 0);
            }

            return;
        }

        if (!int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit value is invalid.",
                SvEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Integer value"));
            return;
        }

        ApplyPersonalField(row, edit.Field, value);
    }

    private static void ApplyGlobalYieldEdit(
        IReadOnlyList<PersonalRow> rows,
        IReadOnlyList<PersonalRow> baseRows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsGlobalYieldAction(edit.NewValue))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon yield edit uses an invalid action.",
                SvEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "remove or restore"));
            return;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!row.IsPresent)
            {
                continue;
            }

            var baseRow = index < baseRows.Count ? baseRows[index] : null;
            switch (edit.Field)
            {
                case GlobalEvYieldField when string.Equals(edit.NewValue, RemoveYieldValue, StringComparison.Ordinal):
                    row.EvYield = StatInfoRow.Zero;
                    break;
                case GlobalEvYieldField:
                    row.EvYield = baseRow?.EvYield ?? StatInfoRow.Zero;
                    break;
                case GlobalExpYieldField when string.Equals(edit.NewValue, RemoveYieldValue, StringComparison.Ordinal):
                    ApplyBaseExperience(row, 0);
                    break;
                case GlobalExpYieldField:
                    if (baseRow is not null)
                    {
                        var restoredBaseExperience = SvPokemonExperience.CalculateBaseExperience(
                            CalculateBaseStatTotal(baseRow.BaseStats),
                            baseRow.EvoStage,
                            baseRow.ExpAddend);
                        ApplyBaseExperience(row, restoredBaseExperience);
                    }

                    break;
            }
        }
    }

    private static void ApplyPersonalField(PersonalRow row, string? field, int value)
    {
        switch (field)
        {
            case SvPokemonWorkflowService.HPField:
                row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Hp = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.AttackField:
                row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Atk = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.DefenseField:
                row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Def = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.SpecialAttackField:
                row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spa = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.SpecialDefenseField:
                row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spd = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.SpeedField:
                row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spe = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.Type1Field:
                row.Type1 = checked((byte)value);
                break;
            case SvPokemonWorkflowService.Type2Field:
                row.Type2 = checked((byte)value);
                break;
            case SvPokemonWorkflowService.Ability1Field:
                row.Ability1 = checked((ushort)value);
                break;
            case SvPokemonWorkflowService.Ability2Field:
                row.Ability2 = checked((ushort)value);
                break;
            case SvPokemonWorkflowService.HiddenAbilityField:
                row.AbilityHidden = checked((ushort)value);
                break;
            case SvPokemonWorkflowService.CatchRateField:
                row.CatchRate = checked((byte)value);
                break;
            case SvPokemonWorkflowService.EvolutionStageField:
                row.EvoStage = checked((byte)value);
                break;
            case SvPokemonWorkflowService.EVYieldHPField:
                row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Hp = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.EVYieldAttackField:
                row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Atk = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.EVYieldDefenseField:
                row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Def = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.EVYieldSpecialAttackField:
                row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spa = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.EVYieldSpecialDefenseField:
                row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spd = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.EVYieldSpeedField:
                row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spe = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.GenderRatioField:
                row.Gender = (row.Gender ?? new GenderInfoRow(0, 0)) with { Ratio = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.HatchCyclesField:
                row.EggHatchSteps = checked((byte)value);
                break;
            case SvPokemonWorkflowService.BaseFriendshipField:
                row.BaseFriendship = checked((byte)value);
                break;
            case SvPokemonWorkflowService.ExpGrowthField:
                row.XpGrowth = checked((byte)value);
                break;
            case SvPokemonWorkflowService.EggGroup1Field:
                row.EggGroup1 = checked((byte)value);
                break;
            case SvPokemonWorkflowService.EggGroup2Field:
                row.EggGroup2 = checked((byte)value);
                break;
            case SvPokemonWorkflowService.BaseExperienceField:
                ApplyBaseExperience(row, value);
                break;
            case SvPokemonWorkflowService.FormField:
                row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Form = checked((ushort)value) };
                break;
            case SvPokemonWorkflowService.ModelIdField:
                row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Model = checked((ushort)value) };
                break;
            case SvPokemonWorkflowService.ColorField:
                row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Color = checked((byte)value) };
                break;
            case SvPokemonWorkflowService.HeightField:
                row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Height = checked((ushort)value) };
                break;
            case SvPokemonWorkflowService.WeightField:
                row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Weight = checked((ushort)value) };
                break;
            case SvPokemonWorkflowService.IsPresentInGameField:
                row.IsPresent = value != 0;
                break;
            case SvPokemonWorkflowService.RegionalDexIndexField:
                row.PaldeaDex = (row.PaldeaDex ?? DexDataRow.Zero) with { Index = checked((ushort)value) };
                break;
            case SvPokemonWorkflowService.ArmorDexIndexField:
                row.KitakamiDex = (row.KitakamiDex ?? DexDataRow.Zero) with { Index = checked((ushort)value) };
                break;
            case SvPokemonWorkflowService.CrownDexIndexField:
                row.BlueberryDex = (row.BlueberryDex ?? DexDataRow.Zero) with { Index = checked((ushort)value) };
                break;
        }
    }

    private static void ApplyLearnsetEdit(PersonalRow row, LearnsetOperation operation)
    {
        var targetSlot = operation.Action == AddAction ? row.LevelupMoves.Count : operation.Slot;
        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                var learnedMove = new LevelupMoveRow(
                    checked((ushort)(operation.MoveId ?? 0)),
                    checked((ushort)(operation.RawLevel ?? operation.Level ?? 1)));
                if (targetSlot < row.LevelupMoves.Count)
                {
                    row.LevelupMoves[targetSlot] = learnedMove;
                }
                else
                {
                    row.LevelupMoves.Add(learnedMove);
                }

                break;
            case RemoveAction when targetSlot >= 0 && targetSlot < row.LevelupMoves.Count:
                row.LevelupMoves.RemoveAt(targetSlot);
                break;
            case MoveUpAction when targetSlot > 0 && targetSlot < row.LevelupMoves.Count:
                (row.LevelupMoves[targetSlot - 1], row.LevelupMoves[targetSlot]) = (row.LevelupMoves[targetSlot], row.LevelupMoves[targetSlot - 1]);
                break;
            case MoveDownAction when targetSlot >= 0 && targetSlot < row.LevelupMoves.Count - 1:
                (row.LevelupMoves[targetSlot + 1], row.LevelupMoves[targetSlot]) = (row.LevelupMoves[targetSlot], row.LevelupMoves[targetSlot + 1]);
                break;
            case MoveToAction when operation.MoveId is { } destination && targetSlot >= 0 && targetSlot < row.LevelupMoves.Count && destination >= 0 && destination < row.LevelupMoves.Count:
                var moved = row.LevelupMoves[targetSlot];
                row.LevelupMoves.RemoveAt(targetSlot);
                row.LevelupMoves.Insert(destination, moved);
                break;
        }
    }

    private static void ApplyEvolutionEdit(PersonalRow row, EvolutionOperation operation)
    {
        var targetSlot = operation.Action == AddAction ? row.Evolutions.Count : operation.Slot;
        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                var evolution = new EvolutionRow(
                    checked((ushort)(operation.Level ?? 0)),
                    checked((ushort)(operation.Method ?? 0)),
                    checked((ushort)(operation.Argument ?? 0)),
                    Reserved3: 0,
                    Reserved4: 0,
                    Reserved5: 0,
                    checked((ushort)(operation.Species ?? 0)),
                    checked((ushort)(operation.Form ?? 0)));
                if (targetSlot < row.Evolutions.Count)
                {
                    var existing = row.Evolutions[targetSlot];
                    row.Evolutions[targetSlot] = evolution with
                    {
                        Reserved3 = existing.Reserved3,
                        Reserved4 = existing.Reserved4,
                        Reserved5 = existing.Reserved5,
                    };
                }
                else
                {
                    row.Evolutions.Add(evolution);
                }

                break;
            case RemoveAction when targetSlot >= 0 && targetSlot < row.Evolutions.Count:
                row.Evolutions.RemoveAt(targetSlot);
                break;
            case MoveUpAction when targetSlot > 0 && targetSlot < row.Evolutions.Count:
                (row.Evolutions[targetSlot - 1], row.Evolutions[targetSlot]) = (row.Evolutions[targetSlot], row.Evolutions[targetSlot - 1]);
                break;
            case MoveDownAction when targetSlot >= 0 && targetSlot < row.Evolutions.Count - 1:
                (row.Evolutions[targetSlot + 1], row.Evolutions[targetSlot]) = (row.Evolutions[targetSlot], row.Evolutions[targetSlot + 1]);
                break;
        }
    }

    private static void ApplyCompatibilityEdit(PersonalRow row, string groupId, int slot, bool enabled)
    {
        if (string.Equals(groupId, "tm", StringComparison.Ordinal))
        {
            var move = checked((ushort)slot);
            row.TmMoves.RemoveAll(candidate => candidate == move);
            if (enabled)
            {
                row.TmMoves.Add(move);
                row.TmMoves.Sort();
            }

            return;
        }

        var moves = groupId switch
        {
            "egg" => row.EggMoves,
            "reminder" => row.ReminderMoves,
            _ => null,
        };
        if (moves is null || (uint)slot >= (uint)moves.Count)
        {
            return;
        }

        if (!enabled)
        {
            moves.RemoveAt(slot);
        }
    }

    private static IReadOnlyList<PersonalRow> ReadRows(byte[] bytes)
    {
        var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(bytes));
        var rows = new List<PersonalRow>();
        for (var index = 0; index < table.EntryLength; index++)
        {
            var row = table.Entry(index);
            if (row is not null)
            {
                rows.Add(PersonalRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteRows(IReadOnlyList<PersonalRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = global::personal_table.CreateEntryVector(builder, offsets);
        var root = global::personal_table.Createpersonal_table(builder, vector);
        global::personal_table.Finishpersonal_tableBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static LearnsetOperation? CreateLearnsetOperation(
        SvPokemonRecord pokemon,
        string action,
        int? slot,
        int? moveId,
        int? level,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedAction = action == AddAction ? AddAction : action.Trim();
        var targetSlot = normalizedAction == AddAction ? pokemon.Learnset.Count : slot ?? -1;
        var operation = new LearnsetOperation(
            normalizedAction,
            targetSlot,
            moveId,
            level,
            ResolveLearnsetRawLevel(pokemon, normalizedAction, targetSlot, level));
        ValidateLearnsetOperation(operation, pokemon, diagnostics);
        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? null : operation;
    }

    private static int? ResolveLearnsetRawLevel(
        SvPokemonRecord pokemon,
        string action,
        int targetSlot,
        int? requestedLevel)
    {
        if (requestedLevel is null)
        {
            return null;
        }

        if (action == UpsertAction
            && targetSlot >= 0
            && targetSlot < pokemon.Learnset.Count
            && pokemon.Learnset[targetSlot] is { } existing)
        {
            return SvLearnsetLevel.PreserveRawLevel(requestedLevel.Value, existing.RawLevel, existing.Level);
        }

        return requestedLevel;
    }

    private static EvolutionOperation? CreateEvolutionOperation(
        SvPokemonRecord pokemon,
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
        SvPokemonRecord? pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseLearnsetField(edit.Field, out var action, out var slot)
            || !TryParseOperationValue(edit.NewValue, out var first, out var second))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon learnset edit is invalid.",
                SvEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Valid learnset operation"));
            return null;
        }

        int? rawLevel = second >= 0 ? second : null;
        var operation = new LearnsetOperation(
            action,
            slot,
            first >= 0 ? first : null,
            rawLevel is { } value ? SvLearnsetLevel.ToDisplayLevel(value) : null,
            rawLevel);
        if (pokemon is not null)
        {
            ValidateLearnsetOperation(operation, pokemon, diagnostics);
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ? null : operation;
    }

    private static EvolutionOperation? ParseEvolutionOperation(
        PendingEdit edit,
        SvPokemonRecord? pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseEvolutionField(edit.Field, out var action, out var slot)
            || !TryParseEvolutionValue(edit.NewValue, out var operation))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon evolution edit is invalid.",
                SvEditSessionSupport.PokemonDomain,
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
        SvPokemonRecord pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        switch (operation.Action)
        {
            case AddAction:
            case UpsertAction:
                if (operation.MoveId is null or < 0 or > ushort.MaxValue
                    || operation.Level is null or < 0 or > ushort.MaxValue
                    || operation.RawLevel is null or < 0 or > ushort.MaxValue)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset upserts require a move ID and level.", "moveId/level"));
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
        SvPokemonRecord pokemon,
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
            default:
                diagnostics.Add(OperationDiagnostic($"Evolution action '{operation.Action}' is not supported.", "action"));
                break;
        }
    }

    private static ValidationDiagnostic OperationDiagnostic(string message, string field)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            SvEditSessionSupport.PokemonDomain,
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

    private static string CreateLearnsetSummary(SvPokemonRecord pokemon, LearnsetOperation operation)
    {
        return operation.Action switch
        {
            AddAction or UpsertAction =>
                $"Set {pokemon.Name} learnset slot {operation.Slot} to {FormatLearnsetLevel(operation)} {SvLabels.Move(operation.MoveId ?? 0)}.",
            RemoveAction => $"Remove {pokemon.Name} learnset slot {operation.Slot}.",
            MoveUpAction => $"Move {pokemon.Name} learnset slot {operation.Slot} up.",
            MoveDownAction => $"Move {pokemon.Name} learnset slot {operation.Slot} down.",
            MoveToAction => $"Move {pokemon.Name} learnset slot {operation.Slot} to slot {operation.MoveId}.",
            _ => $"Update {pokemon.Name} learnset slot {operation.Slot}.",
        };
    }

    private static string FormatLearnsetLevel(LearnsetOperation operation)
    {
        return operation.RawLevel == SvLearnsetLevel.EvolutionRawLevel
            ? SvLearnsetLevel.EvolutionLabel
            : $"Lv. {operation.Level}";
    }

    private static string CreateEvolutionSummary(SvPokemonRecord pokemon, EvolutionOperation operation)
    {
        return operation.Action switch
        {
            AddAction or UpsertAction =>
                $"Set {pokemon.Name} evolution slot {operation.Slot} to species {operation.Species} at level {operation.Level}.",
            RemoveAction => $"Remove {pokemon.Name} evolution slot {operation.Slot}.",
            MoveUpAction => $"Move {pokemon.Name} evolution slot {operation.Slot} up.",
            MoveDownAction => $"Move {pokemon.Name} evolution slot {operation.Slot} down.",
            _ => $"Update {pokemon.Name} evolution slot {operation.Slot}.",
        };
    }

    private static int RecalculateTotal(SvPokemonBaseStats stats)
    {
        return stats.HP + stats.Attack + stats.Defense + stats.SpecialAttack + stats.SpecialDefense + stats.Speed;
    }

    private static int CalculateBaseStatTotal(StatInfoRow? stats)
    {
        return stats is null ? 0 : stats.Hp + stats.Atk + stats.Def + stats.Spa + stats.Spd + stats.Spe;
    }

    private static void ApplyBaseExperience(PersonalRow row, int baseExperience)
    {
        if (!SvPokemonExperience.TryCalculateExpAddend(
                CalculateBaseStatTotal(row.BaseStats),
                row.EvoStage,
                baseExperience,
                out var expAddend))
        {
            throw new InvalidDataException(
                $"Base EXP {baseExperience.ToString(CultureInfo.InvariantCulture)} cannot be represented by Scarlet/Violet's EXP addend.");
        }

        row.ExpAddend = expAddend;
    }

    private static bool IsGlobalYieldField(string? field)
    {
        return string.Equals(field?.Trim(), GlobalEvYieldField, StringComparison.Ordinal)
            || string.Equals(field?.Trim(), GlobalExpYieldField, StringComparison.Ordinal);
    }

    private static bool IsGlobalYieldEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, GlobalRecordId, StringComparison.Ordinal)
            && IsGlobalYieldField(edit.Field);
    }

    private static bool IsGlobalEvYieldEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, GlobalRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, GlobalEvYieldField, StringComparison.Ordinal);
    }

    private static bool IsGlobalExpYieldEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, GlobalRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, GlobalExpYieldField, StringComparison.Ordinal);
    }

    private static bool IsSameGlobalYieldTarget(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return IsGlobalYieldEdit(candidate)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static bool IsGlobalYieldAction(string? value)
    {
        return string.Equals(value, RemoveYieldValue, StringComparison.Ordinal)
            || string.Equals(value, RestoreYieldValue, StringComparison.Ordinal);
    }

    private static bool NeedsBaseRows(IEnumerable<PendingEdit> edits)
    {
        return edits.Any(edit => IsGlobalYieldEdit(edit)
            && string.Equals(edit.NewValue, RestoreYieldValue, StringComparison.Ordinal));
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
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Pokemon field '{field}' is not supported by Scarlet/Violet Pokemon Data yet.",
            SvEditSessionSupport.PokemonDomain,
            field: "field",
            expected: "Supported S/V Pokemon personal, learnset, evolution, or compatibility field");
    }

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
        public DexDataRow? PaldeaDex { get; set; }
        public DexDataRow? KitakamiDex { get; set; }
        public DexDataRow? BlueberryDex { get; set; }
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
        public EggHatchInfoRow? EggHatch { get; init; }
        public byte EggHatchSteps { get; set; }
        public byte BaseFriendship { get; set; }
        public short ExpAddend { get; set; }
        public byte EvoStage { get; set; }
        public bool TypeChangeDisallowed { get; init; }
        public StatInfoRow? EvYield { get; set; }
        public StatInfoRow? BaseStats { get; set; }
        public List<EvolutionRow> Evolutions { get; } = [];
        public List<ushort> TmMoves { get; } = [];
        public List<ushort> EggMoves { get; } = [];
        public List<ushort> ReminderMoves { get; } = [];
        public List<LevelupMoveRow> LevelupMoves { get; } = [];

        public static PersonalRow From(global::personal row)
        {
            var result = new PersonalRow
            {
                Species = row.Species is { } species ? SpeciesInfoRow.From(species) : null,
                IsPresent = row.IsPresent,
                PaldeaDex = row.PaldeaDex is { } paldea ? DexDataRow.From(paldea) : null,
                KitakamiDex = row.KitakamiDex is { } kitakami ? DexDataRow.From(kitakami) : null,
                BlueberryDex = row.BlueberryDex is { } blueberry ? DexDataRow.From(blueberry) : null,
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
                EggHatchSteps = row.EggHatchSteps,
                BaseFriendship = row.BaseFriendship,
                ExpAddend = row.ExpAddend,
                EvoStage = row.EvoStage,
                TypeChangeDisallowed = row.TypeChangeDisallowed,
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

            result.TmMoves.AddRange(row.GetTmMovesArray() ?? []);
            result.EggMoves.AddRange(row.GetEggMovesArray() ?? []);
            result.ReminderMoves.AddRange(row.GetReminderMovesArray() ?? []);
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

        public Offset<global::personal> Write(FlatBufferBuilder builder)
        {
            var evolutionsOffset = CreateEvolutionsVector(builder, Evolutions);
            var tmMovesOffset = global::personal.CreateTmMovesVector(builder, TmMoves.ToArray());
            var eggMovesOffset = global::personal.CreateEggMovesVector(builder, EggMoves.ToArray());
            var reminderMovesOffset = global::personal.CreateReminderMovesVector(builder, ReminderMoves.ToArray());
            var levelupMovesOffset = CreateLevelupMovesVector(builder, LevelupMoves);

            global::personal.Startpersonal(builder);
            global::personal.AddLevelupMoves(builder, levelupMovesOffset);
            global::personal.AddReminderMoves(builder, reminderMovesOffset);
            global::personal.AddEggMoves(builder, eggMovesOffset);
            global::personal.AddTmMoves(builder, tmMovesOffset);
            global::personal.AddEvolutions(builder, evolutionsOffset);
            if (BaseStats is not null)
            {
                global::personal.AddBaseStats(builder, BaseStats.Write(builder));
            }

            if (EvYield is not null)
            {
                global::personal.AddEvYield(builder, EvYield.Write(builder));
            }

            global::personal.AddTypeChangeDisallowed(builder, TypeChangeDisallowed);
            global::personal.AddEvoStage(builder, EvoStage);
            global::personal.AddExpAddend(builder, ExpAddend);
            global::personal.AddBaseFriendship(builder, BaseFriendship);
            global::personal.AddEggHatchSteps(builder, EggHatchSteps);
            if (EggHatch is not null)
            {
                global::personal.AddEggHatch(builder, EggHatch.Write(builder));
            }

            global::personal.AddEggGroup2(builder, EggGroup2);
            global::personal.AddEggGroup1(builder, EggGroup1);
            if (Gender is not null)
            {
                global::personal.AddGender(builder, Gender.Write(builder));
            }

            global::personal.AddCatchRate(builder, CatchRate);
            global::personal.AddXpGrowth(builder, XpGrowth);
            global::personal.AddAbilityHidden(builder, AbilityHidden);
            global::personal.AddAbility2(builder, Ability2);
            global::personal.AddAbility1(builder, Ability1);
            global::personal.AddType2(builder, Type2);
            global::personal.AddType1(builder, Type1);
            if (BlueberryDex is not null)
            {
                global::personal.AddBlueberryDex(builder, BlueberryDex.Write(builder));
            }

            if (KitakamiDex is not null)
            {
                global::personal.AddKitakamiDex(builder, KitakamiDex.Write(builder));
            }

            if (PaldeaDex is not null)
            {
                global::personal.AddPaldeaDex(builder, PaldeaDex.Write(builder));
            }

            global::personal.AddIsPresent(builder, IsPresent);
            if (Species is not null)
            {
                global::personal.AddSpecies(builder, Species.Write(builder));
            }

            return global::personal.Endpersonal(builder);
        }

        private static VectorOffset CreateEvolutionsVector(FlatBufferBuilder builder, IReadOnlyList<EvolutionRow> evolutions)
        {
            global::personal.StartEvolutionsVector(builder, evolutions.Count);
            for (var index = evolutions.Count - 1; index >= 0; index--)
            {
                evolutions[index].Write(builder);
            }

            return builder.EndVector();
        }

        private static VectorOffset CreateLevelupMovesVector(FlatBufferBuilder builder, IReadOnlyList<LevelupMoveRow> moves)
        {
            global::personal.StartLevelupMovesVector(builder, moves.Count);
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

        public static SpeciesInfoRow From(global::species_info row) =>
            new(row.Species, row.Form, row.Model, row.Color, row.BodyType, row.Height, row.Weight, row.Reserved, row.Reserved1, row.Reserved2);

        public Offset<global::species_info> Write(FlatBufferBuilder builder) =>
            global::species_info.Createspecies_info(builder, Species, Form, Model, Color, BodyType, Height, Weight, Reserved, Reserved1, Reserved2);
    }

    private sealed record DexDataRow(ushort Index, byte Group)
    {
        public static readonly DexDataRow Zero = new(0, 0);

        public static DexDataRow From(global::dex_data row) => new(row.Index, row.Group);

        public Offset<global::dex_data> Write(FlatBufferBuilder builder) =>
            global::dex_data.Createdex_data(builder, Index, Group);
    }

    private sealed record GenderInfoRow(byte Group, byte Ratio)
    {
        public static GenderInfoRow From(global::gender_info row) => new(row.Group, row.Ratio);

        public Offset<global::gender_info> Write(FlatBufferBuilder builder) =>
            global::gender_info.Creategender_info(builder, Group, Ratio);
    }

    private sealed record EggHatchInfoRow(ushort Species, ushort Form, ushort FormFlags, ushort FormEverstone)
    {
        public static EggHatchInfoRow From(global::egg_hatch_info row) =>
            new(row.Species, row.Form, row.FormFlags, row.FormEverstone);

        public Offset<global::egg_hatch_info> Write(FlatBufferBuilder builder) =>
            global::egg_hatch_info.Createegg_hatch_info(builder, Species, Form, FormFlags, FormEverstone);
    }

    private sealed record StatInfoRow(byte Hp, byte Atk, byte Def, byte Spa, byte Spd, byte Spe)
    {
        public static readonly StatInfoRow Zero = new(0, 0, 0, 0, 0, 0);

        public static StatInfoRow From(global::stat_info row) =>
            new(row.Hp, row.Atk, row.Def, row.Spa, row.Spd, row.Spe);

        public Offset<global::stat_info> Write(FlatBufferBuilder builder) =>
            global::stat_info.Createstat_info(builder, Hp, Atk, Def, Spa, Spd, Spe);
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
        public static EvolutionRow From(global::evo_data row) =>
            new(row.Level, row.Condition, row.Parameter, row.Reserved3, row.Reserved4, row.Reserved5, row.Species, row.Form);

        public Offset<global::evo_data> Write(FlatBufferBuilder builder) =>
            global::evo_data.Createevo_data(builder, Level, Condition, Parameter, Reserved3, Reserved4, Reserved5, Species, Form);
    }

    private sealed record LevelupMoveRow(ushort Move, ushort Level)
    {
        public static LevelupMoveRow From(global::levelup_move_data row) => new(row.Move, row.Level);

        public Offset<global::levelup_move_data> Write(FlatBufferBuilder builder) =>
            global::levelup_move_data.Createlevelup_move_data(builder, Move, Level);
    }
}
