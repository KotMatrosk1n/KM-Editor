// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Pokemon;

public sealed class SwShPokemonEditSessionService
{
    private const string PokemonEditDomain = "workflow.pokemon";
    private const string LearnsetFieldPrefix = "learnset";
    private const string LearnsetAddAction = "add";
    private const string LearnsetUpsertAction = "upsert";
    private const string LearnsetRemoveAction = "remove";
    private const string LearnsetMoveUpAction = "moveUp";
    private const string LearnsetMoveDownAction = "moveDown";
    private const string LearnsetMoveToAction = "moveTo";
    private const string EvolutionFieldPrefix = "evolution";
    private const string EvolutionAddAction = "add";
    private const string EvolutionUpsertAction = "upsert";
    private const string EvolutionRemoveAction = "remove";
    private const string EvolutionMoveUpAction = "moveUp";
    private const string EvolutionMoveDownAction = "moveDown";
    private const string GlobalEvYieldRecordId = "all";
    private const string GlobalEvYieldField = "evYieldAll";
    private const string GlobalEvYieldRemoveValue = "remove";
    private const string GlobalEvYieldRestoreValue = "restore";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShPokemonWorkflowService pokemonWorkflowService;

    public SwShPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShPokemonWorkflowService? pokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.pokemonWorkflowService = pokemonWorkflowService ?? new SwShPokemonWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShPokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditPokemon(project, workflow, diagnostics))
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (IsGlobalEvYieldField(field))
        {
            var globalPendingEdit = CreateGlobalEvYieldPendingEdit(project, field, value, diagnostics);
            if (globalPendingEdit is null)
            {
                return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
            }

            var globalUpdatedSession = ReplacePendingPokemonEdit(currentSession, globalPendingEdit);

            return new SwShPokemonEditResult(
                OverlayPendingEdits(loadedWorkflow, globalUpdatedSession.PendingEdits),
                globalUpdatedSession,
                diagnostics);
        }

        var selectedPokemon = workflow.Pokemon.FirstOrDefault(pokemon => pokemon.PersonalId == personalId);
        if (selectedPokemon is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedPokemon, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingPokemonEdit(currentSession, pendingEdit);

        return new SwShPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShPokemonEditResult UpdateLearnset(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string action,
        int? slot,
        int? moveId,
        int? level)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(action);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditPokemon(project, workflow, diagnostics)
            || !CanEditLearnsetData(project, diagnostics))
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var selectedPokemon = workflow.Pokemon.FirstOrDefault(pokemon => pokemon.PersonalId == personalId);
        if (selectedPokemon is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var source = SwShPokemonWorkflowService.ResolveLearnsetDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset data source could not be resolved for editing.",
                file: SwShPokemonWorkflowService.LearnsetDataPath,
                expected: "Loaded Sword/Shield wazaoboe_total.bin"));
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreateLearnsetPendingEdit(
            workflow,
            selectedPokemon,
            source,
            action,
            slot,
            moveId,
            level,
            diagnostics);
        if (pendingEdit is null)
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingPokemonEdit(currentSession, pendingEdit);

        return new SwShPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShPokemonEditResult UpdateEvolution(
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
        ArgumentNullException.ThrowIfNull(action);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditPokemon(project, workflow, diagnostics)
            || !CanEditEvolutionData(project, personalId, diagnostics))
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var selectedPokemon = workflow.Pokemon.FirstOrDefault(pokemon => pokemon.PersonalId == personalId);
        if (selectedPokemon is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var source = SwShPokemonWorkflowService.ResolveEvolutionDataSource(project, personalId);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon evolution data source could not be resolved for editing.",
                file: SwShPokemonWorkflowService.CreateEvolutionDataPath(personalId),
                expected: "Loaded Sword/Shield evo_###.bin"));
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreateEvolutionPendingEdit(
            selectedPokemon,
            source,
            action,
            slot,
            method,
            argument,
            species,
            form,
            level,
            diagnostics);
        if (pendingEdit is null)
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingPokemonEdit(currentSession, pendingEdit);

        return new SwShPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = pokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditPokemon(project, workflow, diagnostics);
        if (session.PendingEdits.Any(IsLearnsetEdit))
        {
            CanEditLearnsetData(project, diagnostics);
        }

        if (session.PendingEdits.Any(IsGlobalEvYieldRestoreEdit)
            && SwShPokemonWorkflowService.ResolveBasePersonalDataSource(project) is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Restore EV Yield requires the base personal data file so vanilla EV yields can be copied back.",
                field: GlobalEvYieldField,
                expected: "Readable base personal_total.bin"));
        }

        foreach (var edit in session.PendingEdits.Where(IsEvolutionEdit))
        {
            if (int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
            {
                CanEditEvolutionData(project, personalId, diagnostics);
            }
        }

        var validationWorkflow = workflow;
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(validationWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                validationWorkflow = OverlayPendingEdits(validationWorkflow, [edit]);
            }
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Pokemon Data change is valid."));
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
                "Create a pending Pokemon Data edit before reviewing a change plan.",
                expected: "Pending Pokemon Data edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var reason = session.PendingEdits.Count == 1
            ? $"Apply pending Pokemon Data edit: {session.PendingEdits[0].Summary}"
            : $"Apply {session.PendingEdits.Count} pending Pokemon Data edits.";

        var writes = new List<PlannedFileWrite>();
        var personalEdits = session.PendingEdits.Where(IsPersonalDataEdit).ToArray();
        if (personalEdits.Length > 0)
        {
            var personalWrite = CreatePlannedWrite(
                paths,
                SwShPokemonWorkflowService.PersonalDataPath,
                personalEdits,
                reason,
                diagnostics);
            if (personalWrite is not null)
            {
                writes.Add(personalWrite);
            }
        }

        var learnsetEdits = session.PendingEdits.Where(IsLearnsetEdit).ToArray();
        if (learnsetEdits.Length > 0)
        {
            var learnsetWrite = CreatePlannedWrite(
                paths,
                SwShPokemonWorkflowService.LearnsetDataPath,
                learnsetEdits,
                reason,
                diagnostics);
            if (learnsetWrite is not null)
            {
                writes.Add(learnsetWrite);
            }
        }

        var evolutionEdits = session.PendingEdits.Where(IsEvolutionEdit).ToArray();
        foreach (var evolutionGroup in evolutionEdits.GroupBy(edit => edit.RecordId, StringComparer.Ordinal))
        {
            if (!int.TryParse(evolutionGroup.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon Data edit targets an evolution record that is not loaded.",
                    field: "personalId",
                    expected: "Existing Pokemon evolution record"));
                continue;
            }

            var evolutionWrite = CreatePlannedWrite(
                paths,
                SwShPokemonWorkflowService.CreateEvolutionDataPath(personalId),
                evolutionGroup.ToArray(),
                reason,
                diagnostics);
            if (evolutionWrite is not null)
            {
                writes.Add(evolutionWrite);
            }
        }

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
                expected: "Current reviewed Pokemon Data change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var personalEdits = session.PendingEdits.Where(IsPersonalDataEdit).ToArray();
        if (personalEdits.Length > 0)
        {
            ApplyPersonalDataEdits(paths, project, personalEdits, writtenFiles, diagnostics);
        }

        var learnsetEdits = session.PendingEdits.Where(IsLearnsetEdit).ToArray();
        if (learnsetEdits.Length > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            ApplyLearnsetEdits(paths, project, learnsetEdits, writtenFiles, diagnostics);
        }

        var evolutionEdits = session.PendingEdits.Where(IsEvolutionEdit).ToArray();
        if (evolutionEdits.Length > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            ApplyEvolutionEdits(paths, project, evolutionEdits, writtenFiles, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error) && writtenFiles.Count > 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Pokemon Data change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditPokemon(
        OpenedProject project,
        SwShPokemonWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon Data edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanEditLearnsetData(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (SwShPokemonWorkflowService.ResolveLearnsetDataSource(project) is not null)
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pokemon learnset edit sessions require a supported learnset data file.",
            file: SwShPokemonWorkflowService.LearnsetDataPath,
            expected: "Loaded Sword/Shield wazaoboe_total.bin"));
        return false;
    }

    private static bool CanEditEvolutionData(
        OpenedProject project,
        int personalId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (SwShPokemonWorkflowService.ResolveEvolutionDataSource(project, personalId) is not null)
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pokemon evolution edit sessions require a supported evolution data file.",
            file: SwShPokemonWorkflowService.CreateEvolutionDataPath(personalId),
            expected: "Loaded Sword/Shield evo_###.bin"));
        return false;
    }

    private static void ValidatePendingEdit(
        SwShPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Pokemon Data workflow.",
                expected: PokemonEditDomain));
            return;
        }

        if (IsGlobalEvYieldEdit(edit))
        {
            ValidateGlobalEvYieldPendingEdit(edit, diagnostics);
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets a record that is not loaded.",
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return;
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(pokemon => pokemon.PersonalId == personalId);
        if (pokemon is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets a record that is not loaded.",
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return;
        }

        if (IsLearnsetEdit(edit))
        {
            TryParseLearnsetPendingEdit(edit, pokemon, diagnostics);
            return;
        }

        if (IsEvolutionEdit(edit))
        {
            TryParseEvolutionPendingEdit(edit, pokemon, diagnostics);
            return;
        }

        TryParseEditableValue(pokemon, edit.Field, edit.NewValue, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShPokemonRecord selectedPokemon,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var parsedValue = TryParseEditableValue(selectedPokemon, normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return new PendingEdit(
            PokemonEditDomain,
            CreatePendingEditSummary(selectedPokemon, normalizedField, parsedValue.Value),
            [new ProjectFileReference(selectedPokemon.Provenance.SourceLayer, selectedPokemon.Provenance.SourceFile)],
            RecordId: selectedPokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static PendingEdit? CreateGlobalEvYieldPendingEdit(
        OpenedProject project,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!IsGlobalEvYieldField(normalizedField))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var normalizedValue = value.Trim();
        if (!string.Equals(normalizedValue, GlobalEvYieldRemoveValue, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalizedValue, GlobalEvYieldRestoreValue, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "EV Yield bulk action must be remove or restore.",
                field: GlobalEvYieldField,
                expected: "remove or restore"));
            return null;
        }

        normalizedValue = string.Equals(normalizedValue, GlobalEvYieldRemoveValue, StringComparison.OrdinalIgnoreCase)
            ? GlobalEvYieldRemoveValue
            : GlobalEvYieldRestoreValue;

        var source = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon personal data source could not be resolved for EV Yield bulk editing.",
                file: SwShPokemonWorkflowService.PersonalDataPath,
                expected: "Loaded Sword/Shield personal_total.bin"));
            return null;
        }

        var sources = new List<ProjectFileReference> { CreateSourceReference(source) };
        if (normalizedValue == GlobalEvYieldRestoreValue)
        {
            var baseSource = SwShPokemonWorkflowService.ResolveBasePersonalDataSource(project);
            if (baseSource is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Restore EV Yield requires the base personal data file so vanilla EV yields can be copied back.",
                    field: GlobalEvYieldField,
                    expected: "Readable base personal_total.bin"));
                return null;
            }

            sources.Add(CreateSourceReference(baseSource));
        }

        var summary = normalizedValue == GlobalEvYieldRemoveValue
            ? "Remove EV yields from all Pokemon."
            : "Restore all Pokemon EV yields from vanilla personal data.";

        return new PendingEdit(
            PokemonEditDomain,
            summary,
            sources,
            RecordId: GlobalEvYieldRecordId,
            Field: GlobalEvYieldField,
            NewValue: normalizedValue);
    }

    private static PendingEdit? CreateLearnsetPendingEdit(
        SwShPokemonWorkflow workflow,
        SwShPokemonRecord selectedPokemon,
        SwShPokemonWorkflowService.WorkflowFileSource source,
        string action,
        int? slot,
        int? moveId,
        int? level,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedAction = NormalizeLearnsetAction(action);
        if (normalizedAction is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon learnset action '{action}' is not supported.",
                field: "action",
                expected: "add, upsert, remove, moveUp, moveDown, or moveTo"));
            return null;
        }

        var normalizedSlot = normalizedAction == LearnsetAddAction
            ? selectedPokemon.Learnset.Count
            : slot;
        var fieldAction = normalizedAction == LearnsetAddAction
            ? LearnsetUpsertAction
            : normalizedAction;
        var field = CreateLearnsetFieldId(fieldAction, normalizedSlot);
        var newValue = normalizedAction switch
        {
            LearnsetAddAction or LearnsetUpsertAction => CreateLearnsetValue(moveId, level),
            LearnsetMoveToAction => moveId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            _ => "1",
        };

        var pendingEdit = new PendingEdit(
            PokemonEditDomain,
            Summary: string.Empty,
            Sources: [CreateSourceReference(source)],
            RecordId: selectedPokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            Field: field,
            NewValue: newValue);
        var operation = TryParseLearnsetPendingEdit(pendingEdit, selectedPokemon, diagnostics);
        if (operation is null)
        {
            return null;
        }

        var moveName = operation.Action == LearnsetMoveToAction || operation.MoveId is null
            ? null
            : ResolveMoveName(workflow, selectedPokemon, operation.MoveId.Value);

        return pendingEdit with
        {
            Summary = CreateLearnsetPendingEditSummary(selectedPokemon, operation, moveName),
        };
    }

    private static PendingEdit? CreateEvolutionPendingEdit(
        SwShPokemonRecord selectedPokemon,
        SwShPokemonWorkflowService.WorkflowFileSource source,
        string action,
        int? slot,
        int? method,
        int? argument,
        int? species,
        int? form,
        int? level,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedAction = NormalizeEvolutionAction(action);
        if (normalizedAction is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon evolution action '{action}' is not supported.",
                field: "action",
                expected: "add, upsert, remove, moveUp, or moveDown"));
            return null;
        }

        var normalizedSlot = normalizedAction == EvolutionAddAction
            ? selectedPokemon.Evolutions.Count
            : slot;
        var fieldAction = normalizedAction == EvolutionAddAction
            ? EvolutionUpsertAction
            : normalizedAction;
        var pendingEdit = new PendingEdit(
            PokemonEditDomain,
            Summary: string.Empty,
            Sources: [CreateSourceReference(source)],
            RecordId: selectedPokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            Field: CreateEvolutionFieldId(fieldAction, normalizedSlot),
            NewValue: normalizedAction is EvolutionAddAction or EvolutionUpsertAction
                ? CreateEvolutionValue(method, argument, species, form, level)
                : "1");
        var operation = TryParseEvolutionPendingEdit(pendingEdit, selectedPokemon, diagnostics);
        if (operation is null)
        {
            return null;
        }

        return pendingEdit with
        {
            Summary = CreateEvolutionPendingEditSummary(selectedPokemon, operation),
        };
    }

    private static int? TryParseEditableValue(
        SwShPokemonRecord? pokemon,
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (SwShPokemonWorkflowService.TryParseCompatibilityField(field, out var groupId, out var slot))
        {
            var entry = pokemon is null ? null : GetCompatibilityEntry(pokemon, groupId, slot);
            if (pokemon is not null && entry is null)
            {
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
                return null;
            }

            if (!TryParseBooleanValue(value, out var compatibilityBooleanValue))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{entry?.Label ?? "Compatibility flag"} must be enabled or disabled.",
                    field: field,
                    expected: "Safe Pokemon compatibility flag"));
                return null;
            }

            return compatibilityBooleanValue;
        }

        var editableField = SwShPokemonWorkflowService.GetEditableField(field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
            return null;
        }

        var parsedValue = editableField.ValueKind == "boolean"
            ? TryParseBooleanValue(value, out var booleanValue) ? booleanValue : (int?)null
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue)
                ? integerValue
                : (int?)null;

        if (parsedValue is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be a valid {editableField.ValueKind} value.",
                field: editableField.Field,
                expected: $"Safe Pokemon {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (parsedValue.Value < (editableField.MinimumValue ?? int.MinValue)
            || parsedValue.Value > (editableField.MaximumValue ?? int.MaxValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: $"Safe Pokemon {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        return parsedValue.Value;
    }

    private static LearnsetPendingOperation? TryParseLearnsetPendingEdit(
        PendingEdit edit,
        SwShPokemonRecord? pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseLearnsetFieldId(edit.Field, out var action, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon learnset field '{edit.Field ?? "(missing)"}' is not supported.",
                field: "field",
                expected: "Supported Pokemon learnset row field"));
            return null;
        }

        if (slot < 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset slot must be zero or greater.",
                field: "slot",
                expected: "Safe Pokemon learnset slot"));
            return null;
        }

        var moveId = (int?)null;
        var level = (int?)null;
        if (action == LearnsetUpsertAction)
        {
            if (!TryParseLearnsetValue(edit.NewValue, out var parsedMoveId, out var parsedLevel))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset rows require a valid move ID and level.",
                    field: "learnset",
                    expected: "moveId:level"));
                return null;
            }

            moveId = parsedMoveId;
            level = parsedLevel;
        }
        else if (action == LearnsetMoveToAction)
        {
            if (!int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var targetSlot))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset move-to rows require a valid destination slot.",
                    field: "learnset",
                    expected: "Safe Pokemon learnset destination slot"));
                return null;
            }

            moveId = targetSlot;
        }

        var operation = new LearnsetPendingOperation(action, slot, moveId, level);
        var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        ValidateLearnsetOperation(pokemon, operation, diagnostics);
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount
            ? null
            : operation;
    }

    private static void ValidateLearnsetOperation(
        SwShPokemonRecord? pokemon,
        LearnsetPendingOperation operation,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (operation.Action != LearnsetMoveToAction
            && operation.MoveId is not null
            && (uint)operation.MoveId.Value > ushort.MaxValue)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset move ID must be between 0 and 65535.",
                field: "moveId",
                expected: "Safe Pokemon learnset move ID"));
        }

        if (operation.Level is not null && (uint)operation.Level.Value > ushort.MaxValue)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset level must be between 0 and 65535.",
                field: "level",
                expected: "Safe Pokemon learnset level"));
        }

        if (pokemon is null)
        {
            return;
        }

        var count = pokemon.Learnset.Count;
        switch (operation.Action)
        {
            case LearnsetUpsertAction when operation.Slot > count:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset row edits must target an existing row or the next empty row.",
                    field: "slot",
                    expected: "Existing or next Pokemon learnset row"));
                break;
            case LearnsetUpsertAction when operation.Slot == count && count >= SwShPokemonLearnsetTable.MaxMovesPerRecord:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon learnsets support at most {SwShPokemonLearnsetTable.MaxMovesPerRecord} moves.",
                    field: "slot",
                    expected: "Pokemon learnset with room for another row"));
                break;
            case LearnsetRemoveAction when operation.Slot >= count:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset remove must target an existing row.",
                    field: "slot",
                    expected: "Existing Pokemon learnset row"));
                break;
            case LearnsetMoveUpAction when operation.Slot <= 0 || operation.Slot >= count:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset move-up must target a row below the first row.",
                    field: "slot",
                    expected: "Pokemon learnset row that can move up"));
                break;
            case LearnsetMoveDownAction when operation.Slot < 0 || operation.Slot >= count - 1:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset move-down must target a row above the last row.",
                    field: "slot",
                    expected: "Pokemon learnset row that can move down"));
                break;
            case LearnsetMoveToAction when operation.MoveId is null
                || operation.Slot < 0
                || operation.Slot >= count
                || operation.MoveId.Value < 0
                || operation.MoveId.Value >= count:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset move-to must target existing source and destination rows.",
                    field: "slot",
                    expected: "Existing Pokemon learnset source and destination rows"));
                break;
        }
    }

    private static bool TryParseLearnsetFieldId(string? field, out string action, out int slot)
    {
        action = string.Empty;
        slot = -1;

        if (string.IsNullOrWhiteSpace(field))
        {
            return false;
        }

        var parts = field.Split(':');
        if (parts.Length != 3
            || !string.Equals(parts[0], LearnsetFieldPrefix, StringComparison.Ordinal)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out slot))
        {
            return false;
        }

        var normalizedAction = NormalizeLearnsetAction(parts[1]);
        if (normalizedAction is not (LearnsetUpsertAction or LearnsetRemoveAction or LearnsetMoveUpAction or LearnsetMoveDownAction or LearnsetMoveToAction))
        {
            return false;
        }

        action = normalizedAction;
        return true;
    }

    private static bool TryParseLearnsetValue(string? value, out int moveId, out int level)
    {
        moveId = 0;
        level = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out moveId)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out level);
    }

    private static string? NormalizeLearnsetAction(string action)
    {
        var trimmed = action.Trim();
        if (string.Equals(trimmed, LearnsetAddAction, StringComparison.OrdinalIgnoreCase))
        {
            return LearnsetAddAction;
        }

        if (string.Equals(trimmed, LearnsetUpsertAction, StringComparison.OrdinalIgnoreCase))
        {
            return LearnsetUpsertAction;
        }

        if (string.Equals(trimmed, LearnsetRemoveAction, StringComparison.OrdinalIgnoreCase))
        {
            return LearnsetRemoveAction;
        }

        if (string.Equals(trimmed, LearnsetMoveUpAction, StringComparison.OrdinalIgnoreCase))
        {
            return LearnsetMoveUpAction;
        }

        if (string.Equals(trimmed, LearnsetMoveDownAction, StringComparison.OrdinalIgnoreCase))
        {
            return LearnsetMoveDownAction;
        }

        return string.Equals(trimmed, LearnsetMoveToAction, StringComparison.OrdinalIgnoreCase)
            ? LearnsetMoveToAction
            : null;
    }

    private static string CreateLearnsetFieldId(string action, int? slot)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{LearnsetFieldPrefix}:{action}:{slot?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}");
    }

    private static string CreateLearnsetValue(int? moveId, int? level)
    {
        return moveId is null || level is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{moveId.Value}:{level.Value}");
    }

    private static EvolutionPendingOperation? TryParseEvolutionPendingEdit(
        PendingEdit edit,
        SwShPokemonRecord? pokemon,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseEvolutionFieldId(edit.Field, out var action, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon evolution field '{edit.Field ?? "(missing)"}' is not supported.",
                field: "field",
                expected: "Supported Pokemon evolution row field"));
            return null;
        }

        if (slot < 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon evolution slot must be zero or greater.",
                field: "slot",
                expected: "Safe Pokemon evolution slot"));
            return null;
        }

        var method = (int?)null;
        var argument = (int?)null;
        var species = (int?)null;
        var form = (int?)null;
        var level = (int?)null;
        if (action == EvolutionUpsertAction)
        {
            if (!TryParseEvolutionValue(edit.NewValue, out var parsedMethod, out var parsedArgument, out var parsedSpecies, out var parsedForm, out var parsedLevel))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution rows require valid method, argument, target species, form, and level values.",
                    field: "evolution",
                    expected: "method:argument:species:form:level"));
                return null;
            }

            method = parsedMethod;
            argument = parsedArgument;
            species = parsedSpecies;
            form = parsedForm;
            level = parsedLevel;
        }

        var operation = new EvolutionPendingOperation(action, slot, method, argument, species, form, level);
        var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        ValidateEvolutionOperation(pokemon, operation, diagnostics);
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount
            ? null
            : operation;
    }

    private static void ValidateEvolutionOperation(
        SwShPokemonRecord? pokemon,
        EvolutionPendingOperation operation,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (operation.Method is not null && (uint)operation.Method.Value > ushort.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("method", "method", ushort.MaxValue));
        }

        if (operation.Argument is not null && (uint)operation.Argument.Value > ushort.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("argument", "argument", ushort.MaxValue));
        }

        if (operation.Species is not null && (uint)operation.Species.Value > ushort.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("target species", "species", ushort.MaxValue));
        }

        if (operation.Form is not null && (uint)operation.Form.Value > byte.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("target form", "form", byte.MaxValue));
        }

        if (operation.Level is not null && (uint)operation.Level.Value > byte.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("level", "level", byte.MaxValue));
        }

        if (pokemon is null)
        {
            return;
        }

        var count = pokemon.Evolutions.Count;
        switch (operation.Action)
        {
            case EvolutionUpsertAction when operation.Slot > count:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution row edits must target an existing row or the next empty row.",
                    field: "slot",
                    expected: "Existing or next Pokemon evolution row"));
                break;
            case EvolutionUpsertAction when operation.Slot == count && count >= SwShEvolutionSet.MaxEvolutionCount:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon evolution files support at most {SwShEvolutionSet.MaxEvolutionCount} rows.",
                    field: "slot",
                    expected: "Pokemon evolution file with room for another row"));
                break;
            case EvolutionRemoveAction when operation.Slot >= count:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution remove must target an existing row.",
                    field: "slot",
                    expected: "Existing Pokemon evolution row"));
                break;
            case EvolutionMoveUpAction when operation.Slot <= 0 || operation.Slot >= count:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution move-up must target a row below the first row.",
                    field: "slot",
                    expected: "Pokemon evolution row that can move up"));
                break;
            case EvolutionMoveDownAction when operation.Slot < 0 || operation.Slot >= count - 1:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution move-down must target a row above the last row.",
                    field: "slot",
                    expected: "Pokemon evolution row that can move down"));
                break;
        }
    }

    private static ValidationDiagnostic CreateEvolutionRangeDiagnostic(string label, string field, int maximum)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Pokemon evolution {label} must be between 0 and {maximum}.",
            field: field,
            expected: $"Safe Pokemon evolution {label}");
    }

    private static bool TryParseEvolutionFieldId(string? field, out string action, out int slot)
    {
        action = string.Empty;
        slot = -1;

        if (string.IsNullOrWhiteSpace(field))
        {
            return false;
        }

        var parts = field.Split(':');
        if (parts.Length != 3
            || !string.Equals(parts[0], EvolutionFieldPrefix, StringComparison.Ordinal)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out slot))
        {
            return false;
        }

        var normalizedAction = NormalizeEvolutionAction(parts[1]);
        if (normalizedAction is not (EvolutionUpsertAction or EvolutionRemoveAction or EvolutionMoveUpAction or EvolutionMoveDownAction))
        {
            return false;
        }

        action = normalizedAction;
        return true;
    }

    private static bool TryParseEvolutionValue(
        string? value,
        out int method,
        out int argument,
        out int species,
        out int form,
        out int level)
    {
        method = 0;
        argument = 0;
        species = 0;
        form = 0;
        level = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':');
        return parts.Length == 5
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out method)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out argument)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out species)
            && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out form)
            && int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out level);
    }

    private static string? NormalizeEvolutionAction(string action)
    {
        var trimmed = action.Trim();
        if (string.Equals(trimmed, EvolutionAddAction, StringComparison.OrdinalIgnoreCase))
        {
            return EvolutionAddAction;
        }

        if (string.Equals(trimmed, EvolutionUpsertAction, StringComparison.OrdinalIgnoreCase))
        {
            return EvolutionUpsertAction;
        }

        if (string.Equals(trimmed, EvolutionRemoveAction, StringComparison.OrdinalIgnoreCase))
        {
            return EvolutionRemoveAction;
        }

        if (string.Equals(trimmed, EvolutionMoveUpAction, StringComparison.OrdinalIgnoreCase))
        {
            return EvolutionMoveUpAction;
        }

        return string.Equals(trimmed, EvolutionMoveDownAction, StringComparison.OrdinalIgnoreCase)
            ? EvolutionMoveDownAction
            : null;
    }

    private static string CreateEvolutionFieldId(string action, int? slot)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{EvolutionFieldPrefix}:{action}:{slot?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}");
    }

    private static string CreateEvolutionValue(
        int? method,
        int? argument,
        int? species,
        int? form,
        int? level)
    {
        return method is null || argument is null || species is null || form is null || level is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{method.Value}:{argument.Value}:{species.Value}:{form.Value}:{level.Value}");
    }

    private static bool TryParseBooleanValue(string? value, out int parsedValue)
    {
        parsedValue = 0;
        if (string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = 1;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = 0;
            return true;
        }

        return false;
    }

    private static EditSession ReplacePendingPokemonEdit(EditSession session, PendingEdit pendingEdit)
    {
        if (IsGlobalEvYieldEdit(pendingEdit))
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit => !(
                        string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
                        && (IsGlobalEvYieldEdit(edit) || IsEvYieldField(edit.Field))))
                    .Append(pendingEdit)
                    .ToArray(),
            };
        }

        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !(
                    string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
                    && string.Equals(edit.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
                    && string.Equals(edit.Field, pendingEdit.Field, StringComparison.Ordinal)))
                .Append(pendingEdit)
                .ToArray(),
        };
    }

    public static SwShPokemonWorkflow OverlayPendingEdits(
        SwShPokemonWorkflow workflow,
        IReadOnlyList<PendingEdit> edits)
    {
        if (edits.Count == 0)
        {
            return workflow;
        }

        var overlaid = workflow.Pokemon.ToDictionary(pokemon => pokemon.PersonalId);
        foreach (var edit in edits.Where(edit => string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)))
        {
            if (IsGlobalEvYieldEdit(edit))
            {
                OverlayGlobalEvYieldEdit(overlaid, edit);
                continue;
            }

            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
                || !overlaid.TryGetValue(personalId, out var pokemon))
            {
                continue;
            }

            if (IsLearnsetEdit(edit))
            {
                var parseDiagnostics = new List<ValidationDiagnostic>();
                var operation = TryParseLearnsetPendingEdit(edit, pokemon, parseDiagnostics);
                if (operation is not null)
                {
                    var moveName = operation.MoveId is null
                        ? null
                        : ResolveMoveName(workflow, pokemon, operation.MoveId.Value);
                    overlaid[personalId] = ApplyPokemonLearnsetViewOperation(pokemon, operation, moveName);
                }

                continue;
            }

            if (IsEvolutionEdit(edit))
            {
                var parseDiagnostics = new List<ValidationDiagnostic>();
                var operation = TryParseEvolutionPendingEdit(edit, pokemon, parseDiagnostics);
                if (operation is not null)
                {
                    overlaid[personalId] = ApplyPokemonEvolutionViewOperation(workflow, pokemon, operation);
                }

                continue;
            }

            if (!int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            overlaid[personalId] = ApplyPokemonViewField(pokemon, edit.Field!, value);
        }

        return workflow with
        {
            Pokemon = workflow.Pokemon
                .Select(pokemon => overlaid.TryGetValue(pokemon.PersonalId, out var updated) ? updated : pokemon)
                .ToArray(),
        };
    }

    private static void OverlayGlobalEvYieldEdit(
        IDictionary<int, SwShPokemonRecord> overlaid,
        PendingEdit edit)
    {
        if (!string.Equals(edit.NewValue, GlobalEvYieldRemoveValue, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var personalId in overlaid.Keys.ToArray())
        {
            overlaid[personalId] = ClearPokemonViewEvYield(overlaid[personalId]);
        }
    }

    private static SwShPokemonRecord ClearPokemonViewEvYield(SwShPokemonRecord pokemon)
    {
        return pokemon with
        {
            Personal = ClearPokemonPersonalDetailsEvYield(pokemon.Personal),
        };
    }

    private static SwShPokemonPersonalDetails ClearPokemonPersonalDetailsEvYield(
        SwShPokemonPersonalDetails personal)
    {
        return personal with
        {
            EVYieldHP = 0,
            EVYieldAttack = 0,
            EVYieldDefense = 0,
            EVYieldSpecialAttack = 0,
            EVYieldSpecialDefense = 0,
            EVYieldSpeed = 0,
        };
    }

    private static SwShPokemonRecord ApplyPokemonEvolutionViewOperation(
        SwShPokemonWorkflow workflow,
        SwShPokemonRecord pokemon,
        EvolutionPendingOperation operation)
    {
        var updatedEvolutions = ApplyEvolutionOperation(
            new SwShEvolutionSet(
                pokemon.Evolutions
                    .Select(evolution => new SwShEvolutionRecord(
                        evolution.Slot,
                        evolution.Method,
                        evolution.Argument,
                        evolution.Species,
                        evolution.Form,
                        evolution.Level))
                    .ToArray()),
            operation)
            .Evolutions
            .Select(evolution => CreatePokemonEvolutionViewRecord(evolution, workflow))
            .ToArray();

        return pokemon with
        {
            Evolutions = updatedEvolutions,
        };
    }

    private static SwShPokemonEvolutionRecord CreatePokemonEvolutionViewRecord(
        SwShEvolutionRecord evolution,
        SwShPokemonWorkflow workflow)
    {
        var methodOption = workflow.EvolutionMethodOptions
            .FirstOrDefault(option => option.Value == evolution.Method);
        var argumentKind = methodOption?.ArgumentKind ?? "value";
        var argumentLabel = methodOption?.ArgumentLabel ?? "Argument";

        return new SwShPokemonEvolutionRecord(
            evolution.Slot,
            evolution.Method,
            evolution.Argument,
            evolution.Species,
            evolution.Form,
            evolution.Level,
            FormatEvolutionMethodName(methodOption, evolution.Method),
            argumentKind,
            argumentLabel,
            FormatEvolutionArgumentValue(methodOption, argumentKind, evolution.Argument));
    }

    private static string FormatEvolutionMethodName(
        SwShPokemonEvolutionMethodOption? methodOption,
        int method)
    {
        if (methodOption is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Method {method}");
        }

        var prefix = string.Create(CultureInfo.InvariantCulture, $"{method:000} ");
        return methodOption.Label.StartsWith(prefix, StringComparison.Ordinal)
            ? methodOption.Label[prefix.Length..]
            : methodOption.Label;
    }

    private static string FormatEvolutionArgumentValue(
        SwShPokemonEvolutionMethodOption? methodOption,
        string argumentKind,
        int argument)
    {
        if (string.Equals(argumentKind, "none", StringComparison.Ordinal)
            || string.Equals(argumentKind, "level", StringComparison.Ordinal))
        {
            return "None";
        }

        return methodOption
            ?.ArgumentOptions
            .FirstOrDefault(option => option.Value == argument)
            ?.Label
            ?? argument.ToString(CultureInfo.InvariantCulture);
    }

    private static SwShPokemonRecord ApplyPokemonLearnsetViewOperation(
        SwShPokemonRecord pokemon,
        LearnsetPendingOperation operation,
        string? moveName)
    {
        var updatedMoves = ApplyLearnsetOperation(
            new SwShPokemonLearnsetRecord(
                pokemon.PersonalId,
                pokemon.Learnset
                    .Select(move => new SwShPokemonLearnsetMoveRecord(move.Slot, move.MoveId, move.Level))
                    .ToArray()),
            operation)
            .Moves
            .Select(move => new SwShPokemonLearnsetMove(
                move.Slot,
                move.MoveId,
                operation.MoveId == move.MoveId && moveName is not null
                    ? moveName
                    : ResolveMoveName(pokemon, move.MoveId),
                move.Level))
            .ToArray();

        return pokemon with
        {
            Learnset = updatedMoves,
        };
    }

    private static SwShPokemonRecord ApplyPokemonViewField(SwShPokemonRecord pokemon, string field, int value)
    {
        if (SwShPokemonWorkflowService.TryParseCompatibilityField(field, out var groupId, out var slot))
        {
            return pokemon with
            {
                Compatibility = UpdateCompatibilityGroups(pokemon.Compatibility, groupId, slot, value != 0),
            };
        }

        return field switch
        {
            SwShPokemonWorkflowService.HPField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, hp: value) },
            SwShPokemonWorkflowService.AttackField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, attack: value) },
            SwShPokemonWorkflowService.DefenseField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, defense: value) },
            SwShPokemonWorkflowService.SpecialAttackField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, specialAttack: value) },
            SwShPokemonWorkflowService.SpecialDefenseField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, specialDefense: value) },
            SwShPokemonWorkflowService.SpeedField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, speed: value) },
            SwShPokemonWorkflowService.Type1Field => pokemon with { Type1 = FormatType(value), Personal = pokemon.Personal with { Type1 = value } },
            SwShPokemonWorkflowService.Type2Field => pokemon with { Type2 = FormatType(value), Personal = pokemon.Personal with { Type2 = value } },
            SwShPokemonWorkflowService.CatchRateField => pokemon with { CatchRate = value, Personal = pokemon.Personal with { CatchRate = value } },
            SwShPokemonWorkflowService.EvolutionStageField => pokemon with { EvolutionStage = value, Personal = pokemon.Personal with { EvolutionStage = value } },
            SwShPokemonWorkflowService.EVYieldHPField => pokemon with { Personal = pokemon.Personal with { EVYieldHP = value } },
            SwShPokemonWorkflowService.EVYieldAttackField => pokemon with { Personal = pokemon.Personal with { EVYieldAttack = value } },
            SwShPokemonWorkflowService.EVYieldDefenseField => pokemon with { Personal = pokemon.Personal with { EVYieldDefense = value } },
            SwShPokemonWorkflowService.EVYieldSpecialAttackField => pokemon with { Personal = pokemon.Personal with { EVYieldSpecialAttack = value } },
            SwShPokemonWorkflowService.EVYieldSpecialDefenseField => pokemon with { Personal = pokemon.Personal with { EVYieldSpecialDefense = value } },
            SwShPokemonWorkflowService.EVYieldSpeedField => pokemon with { Personal = pokemon.Personal with { EVYieldSpeed = value } },
            SwShPokemonWorkflowService.HeldItem1Field => pokemon with { Personal = pokemon.Personal with { HeldItem1 = value } },
            SwShPokemonWorkflowService.HeldItem2Field => pokemon with { Personal = pokemon.Personal with { HeldItem2 = value } },
            SwShPokemonWorkflowService.HeldItem3Field => pokemon with { Personal = pokemon.Personal with { HeldItem3 = value } },
            SwShPokemonWorkflowService.GenderRatioField => pokemon with { GenderRatio = value, Personal = pokemon.Personal with { GenderRatio = value } },
            SwShPokemonWorkflowService.HatchCyclesField => pokemon with { Personal = pokemon.Personal with { HatchCycles = value } },
            SwShPokemonWorkflowService.BaseFriendshipField => pokemon with { Personal = pokemon.Personal with { BaseFriendship = value } },
            SwShPokemonWorkflowService.ExpGrowthField => pokemon with { Personal = pokemon.Personal with { ExpGrowth = value } },
            SwShPokemonWorkflowService.EggGroup1Field => pokemon with { Personal = pokemon.Personal with { EggGroup1 = value } },
            SwShPokemonWorkflowService.EggGroup2Field => pokemon with { Personal = pokemon.Personal with { EggGroup2 = value } },
            SwShPokemonWorkflowService.Ability1Field => pokemon with { Abilities = pokemon.Abilities with { Ability1 = value } },
            SwShPokemonWorkflowService.Ability2Field => pokemon with { Abilities = pokemon.Abilities with { Ability2 = value } },
            SwShPokemonWorkflowService.HiddenAbilityField => pokemon with { Abilities = pokemon.Abilities with { HiddenAbility = value } },
            SwShPokemonWorkflowService.FormStatsIndexField => pokemon with { Personal = pokemon.Personal with { FormStatsIndex = value } },
            SwShPokemonWorkflowService.FormCountField => pokemon with { Personal = pokemon.Personal with { FormCount = value } },
            SwShPokemonWorkflowService.ColorField => pokemon with { Personal = pokemon.Personal with { Color = value } },
            SwShPokemonWorkflowService.IsPresentInGameField => pokemon with
            {
                DexPresence = pokemon.DexPresence with { IsPresentInGame = value != 0 },
                Personal = pokemon.Personal with { IsPresentInGame = value != 0 },
            },
            SwShPokemonWorkflowService.HasSpriteFormField => pokemon with { Personal = pokemon.Personal with { HasSpriteForm = value != 0 } },
            SwShPokemonWorkflowService.BaseExperienceField => pokemon with { BaseExperience = value, Personal = pokemon.Personal with { BaseExperience = value } },
            SwShPokemonWorkflowService.HeightField => pokemon with { Height = value, Personal = pokemon.Personal with { Height = value } },
            SwShPokemonWorkflowService.WeightField => pokemon with { Weight = value, Personal = pokemon.Personal with { Weight = value } },
            SwShPokemonWorkflowService.ModelIdField => pokemon with { Personal = pokemon.Personal with { ModelId = checked((uint)value) } },
            SwShPokemonWorkflowService.HatchedSpeciesField => pokemon with { Personal = pokemon.Personal with { HatchedSpecies = value } },
            SwShPokemonWorkflowService.LocalFormIndexField => pokemon with { Personal = pokemon.Personal with { LocalFormIndex = value } },
            SwShPokemonWorkflowService.IsRegionalFormField => pokemon with { Personal = pokemon.Personal with { IsRegionalForm = value != 0 } },
            SwShPokemonWorkflowService.CanNotDynamaxField => pokemon with { Personal = pokemon.Personal with { CanNotDynamax = value != 0 } },
            SwShPokemonWorkflowService.RegionalDexIndexField => pokemon with
            {
                DexPresence = pokemon.DexPresence with { RegionalDexIndex = value, IsInAnyDex = value != 0 || pokemon.DexPresence.ArmorDexIndex != 0 || pokemon.DexPresence.CrownDexIndex != 0 },
                Personal = pokemon.Personal with { RegionalDexIndex = value },
            },
            SwShPokemonWorkflowService.FormField => pokemon with { Form = value, Personal = pokemon.Personal with { Form = value } },
            SwShPokemonWorkflowService.ArmorDexIndexField => pokemon with
            {
                DexPresence = pokemon.DexPresence with { ArmorDexIndex = value, IsInAnyDex = pokemon.DexPresence.RegionalDexIndex != 0 || value != 0 || pokemon.DexPresence.CrownDexIndex != 0 },
                Personal = pokemon.Personal with { ArmorDexIndex = value },
            },
            SwShPokemonWorkflowService.CrownDexIndexField => pokemon with
            {
                DexPresence = pokemon.DexPresence with { CrownDexIndex = value, IsInAnyDex = pokemon.DexPresence.RegionalDexIndex != 0 || pokemon.DexPresence.ArmorDexIndex != 0 || value != 0 },
                Personal = pokemon.Personal with { CrownDexIndex = value },
            },
            _ => pokemon,
        };
    }

    private static SwShPokemonBaseStats UpdateStats(
        SwShPokemonBaseStats stats,
        int? hp = null,
        int? attack = null,
        int? defense = null,
        int? specialAttack = null,
        int? specialDefense = null,
        int? speed = null)
    {
        var updated = stats with
        {
            HP = hp ?? stats.HP,
            Attack = attack ?? stats.Attack,
            Defense = defense ?? stats.Defense,
            SpecialAttack = specialAttack ?? stats.SpecialAttack,
            SpecialDefense = specialDefense ?? stats.SpecialDefense,
            Speed = speed ?? stats.Speed,
        };

        return updated with
        {
            Total = updated.HP + updated.Attack + updated.Defense + updated.SpecialAttack + updated.SpecialDefense + updated.Speed,
        };
    }

    private static SwShPersonalRecord ApplyPersonalDataField(SwShPersonalRecord record, string field, int value)
    {
        if (SwShPokemonWorkflowService.TryParseCompatibilityField(field, out var groupId, out var slot))
        {
            return UpdatePersonalCompatibility(record, groupId, slot, value != 0);
        }

        return field switch
        {
            SwShPokemonWorkflowService.HPField => record with { HP = value },
            SwShPokemonWorkflowService.AttackField => record with { Attack = value },
            SwShPokemonWorkflowService.DefenseField => record with { Defense = value },
            SwShPokemonWorkflowService.SpecialAttackField => record with { SpecialAttack = value },
            SwShPokemonWorkflowService.SpecialDefenseField => record with { SpecialDefense = value },
            SwShPokemonWorkflowService.SpeedField => record with { Speed = value },
            SwShPokemonWorkflowService.Type1Field => record with { Type1 = value },
            SwShPokemonWorkflowService.Type2Field => record with { Type2 = value },
            SwShPokemonWorkflowService.CatchRateField => record with { CatchRate = value },
            SwShPokemonWorkflowService.EvolutionStageField => record with { EvolutionStage = value },
            SwShPokemonWorkflowService.EVYieldHPField => record with { EVYieldHP = value },
            SwShPokemonWorkflowService.EVYieldAttackField => record with { EVYieldAttack = value },
            SwShPokemonWorkflowService.EVYieldDefenseField => record with { EVYieldDefense = value },
            SwShPokemonWorkflowService.EVYieldSpecialAttackField => record with { EVYieldSpecialAttack = value },
            SwShPokemonWorkflowService.EVYieldSpecialDefenseField => record with { EVYieldSpecialDefense = value },
            SwShPokemonWorkflowService.EVYieldSpeedField => record with { EVYieldSpeed = value },
            SwShPokemonWorkflowService.HeldItem1Field => record with { HeldItem1 = value },
            SwShPokemonWorkflowService.HeldItem2Field => record with { HeldItem2 = value },
            SwShPokemonWorkflowService.HeldItem3Field => record with { HeldItem3 = value },
            SwShPokemonWorkflowService.GenderRatioField => record with { GenderRatio = value },
            SwShPokemonWorkflowService.HatchCyclesField => record with { HatchCycles = value },
            SwShPokemonWorkflowService.BaseFriendshipField => record with { BaseFriendship = value },
            SwShPokemonWorkflowService.ExpGrowthField => record with { ExpGrowth = value },
            SwShPokemonWorkflowService.EggGroup1Field => record with { EggGroup1 = value },
            SwShPokemonWorkflowService.EggGroup2Field => record with { EggGroup2 = value },
            SwShPokemonWorkflowService.Ability1Field => record with { Ability1 = value },
            SwShPokemonWorkflowService.Ability2Field => record with { Ability2 = value },
            SwShPokemonWorkflowService.HiddenAbilityField => record with { HiddenAbility = value },
            SwShPokemonWorkflowService.FormStatsIndexField => record with { FormStatsIndex = value },
            SwShPokemonWorkflowService.FormCountField => record with { FormCount = value },
            SwShPokemonWorkflowService.ColorField => record with { Color = value },
            SwShPokemonWorkflowService.IsPresentInGameField => record with { IsPresentInGame = value != 0 },
            SwShPokemonWorkflowService.HasSpriteFormField => record with { HasSpriteForm = value != 0 },
            SwShPokemonWorkflowService.BaseExperienceField => record with { BaseExperience = value },
            SwShPokemonWorkflowService.HeightField => record with { Height = value },
            SwShPokemonWorkflowService.WeightField => record with { Weight = value },
            SwShPokemonWorkflowService.ModelIdField => record with { ModelId = checked((uint)value) },
            SwShPokemonWorkflowService.HatchedSpeciesField => record with { HatchedSpecies = value },
            SwShPokemonWorkflowService.LocalFormIndexField => record with { LocalFormIndex = value },
            SwShPokemonWorkflowService.IsRegionalFormField => record with { IsRegionalForm = value != 0 },
            SwShPokemonWorkflowService.CanNotDynamaxField => record with { CanNotDynamax = value != 0 },
            SwShPokemonWorkflowService.RegionalDexIndexField => record with { RegionalDexIndex = value },
            SwShPokemonWorkflowService.FormField => record with { Form = value },
            SwShPokemonWorkflowService.ArmorDexIndexField => record with { ArmorDexIndex = value },
            SwShPokemonWorkflowService.CrownDexIndexField => record with { CrownDexIndex = value },
            _ => record,
        };
    }

    private static IReadOnlyList<SwShPokemonCompatibilityGroup> UpdateCompatibilityGroups(
        IReadOnlyList<SwShPokemonCompatibilityGroup> groups,
        string groupId,
        int slot,
        bool enabled)
    {
        return groups
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
    }

    private static SwShPersonalRecord UpdatePersonalCompatibility(
        SwShPersonalRecord record,
        string groupId,
        int slot,
        bool enabled)
    {
        return groupId switch
        {
            SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId => record with
            {
                TechnicalMachines = SetFlag(record.TechnicalMachines, slot, enabled),
            },
            SwShPokemonWorkflowService.TechnicalRecordCompatibilityGroupId => record with
            {
                TechnicalRecords = SetFlag(record.TechnicalRecords, slot, enabled),
            },
            SwShPokemonWorkflowService.TypeTutorCompatibilityGroupId => record with
            {
                TypeTutors = SetFlag(record.TypeTutors, slot, enabled),
            },
            SwShPokemonWorkflowService.ArmorTutorCompatibilityGroupId => record with
            {
                ArmorTutors = SetFlag(record.ArmorTutors, slot, enabled),
            },
            _ => record,
        };
    }

    private static IReadOnlyList<bool> SetFlag(IReadOnlyList<bool> flags, int slot, bool enabled)
    {
        var updated = flags.ToArray();
        updated[slot] = enabled;
        return updated;
    }

    private static SwShEvolutionSet ApplyEvolutionOperation(
        SwShEvolutionSet record,
        EvolutionPendingOperation operation)
    {
        var evolutions = record.Evolutions
            .Select(evolution => new SwShEvolutionRecord(
                evolution.Slot,
                evolution.Method,
                evolution.Argument,
                evolution.Species,
                evolution.Form,
                evolution.Level))
            .ToList();

        switch (operation.Action)
        {
            case EvolutionUpsertAction
                when operation.Method is not null
                    && operation.Argument is not null
                    && operation.Species is not null
                    && operation.Form is not null
                    && operation.Level is not null
                    && operation.Slot < evolutions.Count:
                evolutions[operation.Slot] = new SwShEvolutionRecord(
                    operation.Slot,
                    operation.Method.Value,
                    operation.Argument.Value,
                    operation.Species.Value,
                    operation.Form.Value,
                    operation.Level.Value);
                break;
            case EvolutionUpsertAction
                when operation.Method is not null
                    && operation.Argument is not null
                    && operation.Species is not null
                    && operation.Form is not null
                    && operation.Level is not null
                    && operation.Slot == evolutions.Count:
                evolutions.Add(new SwShEvolutionRecord(
                    operation.Slot,
                    operation.Method.Value,
                    operation.Argument.Value,
                    operation.Species.Value,
                    operation.Form.Value,
                    operation.Level.Value));
                break;
            case EvolutionRemoveAction when operation.Slot < evolutions.Count:
                evolutions.RemoveAt(operation.Slot);
                break;
            case EvolutionMoveUpAction when operation.Slot > 0 && operation.Slot < evolutions.Count:
                (evolutions[operation.Slot - 1], evolutions[operation.Slot]) = (evolutions[operation.Slot], evolutions[operation.Slot - 1]);
                break;
            case EvolutionMoveDownAction when operation.Slot >= 0 && operation.Slot < evolutions.Count - 1:
                (evolutions[operation.Slot + 1], evolutions[operation.Slot]) = (evolutions[operation.Slot], evolutions[operation.Slot + 1]);
                break;
        }

        return record with
        {
            Evolutions = NormalizeEvolutionSlots(evolutions),
        };
    }

    private static IReadOnlyList<SwShEvolutionRecord> NormalizeEvolutionSlots(
        IReadOnlyList<SwShEvolutionRecord> evolutions)
    {
        return evolutions
            .Select((evolution, index) => new SwShEvolutionRecord(
                index,
                evolution.Method,
                evolution.Argument,
                evolution.Species,
                evolution.Form,
                evolution.Level))
            .ToArray();
    }

    private static SwShPokemonLearnsetRecord ApplyLearnsetOperation(
        SwShPokemonLearnsetRecord record,
        LearnsetPendingOperation operation)
    {
        var moves = record.Moves
            .Select(move => new SwShPokemonLearnsetMoveRecord(move.Slot, move.MoveId, move.Level))
            .ToList();

        switch (operation.Action)
        {
            case LearnsetUpsertAction when operation.MoveId is not null && operation.Level is not null && operation.Slot < moves.Count:
                moves[operation.Slot] = new SwShPokemonLearnsetMoveRecord(
                    operation.Slot,
                    operation.MoveId.Value,
                    operation.Level.Value);
                break;
            case LearnsetUpsertAction when operation.MoveId is not null && operation.Level is not null && operation.Slot == moves.Count:
                moves.Add(new SwShPokemonLearnsetMoveRecord(
                    operation.Slot,
                    operation.MoveId.Value,
                    operation.Level.Value));
                break;
            case LearnsetRemoveAction when operation.Slot < moves.Count:
                moves.RemoveAt(operation.Slot);
                break;
            case LearnsetMoveUpAction when operation.Slot > 0 && operation.Slot < moves.Count:
                (moves[operation.Slot - 1], moves[operation.Slot]) = (moves[operation.Slot], moves[operation.Slot - 1]);
                break;
            case LearnsetMoveDownAction when operation.Slot >= 0 && operation.Slot < moves.Count - 1:
                (moves[operation.Slot + 1], moves[operation.Slot]) = (moves[operation.Slot], moves[operation.Slot + 1]);
                break;
            case LearnsetMoveToAction when operation.MoveId is not null
                && operation.Slot >= 0
                && operation.Slot < moves.Count
                && operation.MoveId.Value >= 0
                && operation.MoveId.Value < moves.Count:
                var moved = moves[operation.Slot];
                moves.RemoveAt(operation.Slot);
                moves.Insert(operation.MoveId.Value, moved);
                break;
        }

        return record with
        {
            Moves = NormalizeLearnsetSlots(moves),
        };
    }

    private static IReadOnlyList<SwShPokemonLearnsetMoveRecord> NormalizeLearnsetSlots(
        IReadOnlyList<SwShPokemonLearnsetMoveRecord> moves)
    {
        return moves
            .Select((move, index) => new SwShPokemonLearnsetMoveRecord(index, move.MoveId, move.Level))
            .ToArray();
    }

    private static void ApplyPersonalDataEdits(
        ProjectPaths paths,
        OpenedProject project,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon personal data source could not be resolved for apply.",
                file: SwShPokemonWorkflowService.PersonalDataPath,
                expected: "Loaded Sword/Shield personal_total.bin"));
            return;
        }

        var targetPath = ResolveOutputPath(paths, SwShPokemonWorkflowService.PersonalDataPath, diagnostics);
        if (targetPath is null)
        {
            return;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var table = SwShPersonalTable.Parse(sourceBytes);
            var records = table.Records.ToArray();

            foreach (var edit in edits)
            {
                if (IsGlobalEvYieldEdit(edit))
                {
                    ApplyGlobalEvYieldPersonalDataEdit(project, records, edit, diagnostics);
                    continue;
                }

                if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
                    || (uint)personalId >= (uint)records.Length)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending Pokemon Data edit targets a record that is not loaded.",
                        field: "personalId",
                        expected: "Existing Pokemon personal record"));
                    continue;
                }

                if (TryParseEditableValue(null, edit.Field, edit.NewValue, diagnostics) is not { } value)
                {
                    continue;
                }

                records[personalId] = ApplyPersonalDataField(records[personalId], edit.Field!, value);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            var outputBytes = SwShPersonalTable.Write(records, sourceBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, outputBytes);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShPokemonWorkflowService.PersonalDataPath));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal data source could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield personal_total.bin"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data output file could not be read or written: {exception.Message}",
                file: SwShPokemonWorkflowService.PersonalDataPath,
                expected: "Readable source and writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data output file could not be read or written: {exception.Message}",
                file: SwShPokemonWorkflowService.PersonalDataPath,
                expected: "Readable source and writable output root"));
        }
    }

    private static void ApplyGlobalEvYieldPersonalDataEdit(
        OpenedProject project,
        SwShPersonalRecord[] records,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.Equals(edit.NewValue, GlobalEvYieldRemoveValue, StringComparison.Ordinal))
        {
            for (var index = 0; index < records.Length; index++)
            {
                records[index] = ClearPersonalEvYield(records[index]);
            }

            return;
        }

        if (!string.Equals(edit.NewValue, GlobalEvYieldRestoreValue, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "EV Yield bulk action must be remove or restore.",
                field: GlobalEvYieldField,
                expected: "remove or restore"));
            return;
        }

        var baseSource = SwShPokemonWorkflowService.ResolveBasePersonalDataSource(project);
        if (baseSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Restore EV Yield requires the base personal data file so vanilla EV yields can be copied back.",
                field: GlobalEvYieldField,
                expected: "Readable base personal_total.bin"));
            return;
        }

        var baseRecords = SwShPersonalTable.Parse(File.ReadAllBytes(baseSource.AbsolutePath)).Records;
        var count = Math.Min(records.Length, baseRecords.Count);
        for (var index = 0; index < count; index++)
        {
            records[index] = CopyPersonalEvYield(records[index], baseRecords[index]);
        }
    }

    private static SwShPersonalRecord ClearPersonalEvYield(SwShPersonalRecord record)
    {
        return record with
        {
            EVYieldHP = 0,
            EVYieldAttack = 0,
            EVYieldDefense = 0,
            EVYieldSpecialAttack = 0,
            EVYieldSpecialDefense = 0,
            EVYieldSpeed = 0,
        };
    }

    private static SwShPersonalRecord CopyPersonalEvYield(
        SwShPersonalRecord target,
        SwShPersonalRecord source)
    {
        return target with
        {
            EVYieldHP = source.EVYieldHP,
            EVYieldAttack = source.EVYieldAttack,
            EVYieldDefense = source.EVYieldDefense,
            EVYieldSpecialAttack = source.EVYieldSpecialAttack,
            EVYieldSpecialDefense = source.EVYieldSpecialDefense,
            EVYieldSpeed = source.EVYieldSpeed,
        };
    }

    private static void ApplyLearnsetEdits(
        ProjectPaths paths,
        OpenedProject project,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShPokemonWorkflowService.ResolveLearnsetDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset data source could not be resolved for apply.",
                file: SwShPokemonWorkflowService.LearnsetDataPath,
                expected: "Loaded Sword/Shield wazaoboe_total.bin"));
            return;
        }

        var targetPath = ResolveOutputPath(paths, SwShPokemonWorkflowService.LearnsetDataPath, diagnostics);
        if (targetPath is null)
        {
            return;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var table = SwShPokemonLearnsetTable.Parse(sourceBytes);
            var records = table.Records.ToArray();

            foreach (var edit in edits)
            {
                if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
                    || (uint)personalId >= (uint)records.Length)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending Pokemon Data edit targets a learnset record that is not loaded.",
                        field: "personalId",
                        expected: "Existing Pokemon learnset record"));
                    continue;
                }

                var operation = TryParseLearnsetPendingEdit(edit, pokemon: null, diagnostics: diagnostics);
                if (operation is null)
                {
                    continue;
                }

                records[personalId] = ApplyLearnsetOperation(records[personalId], operation);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            var outputBytes = SwShPokemonLearnsetTable.Write(records, sourceBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, outputBytes);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShPokemonWorkflowService.LearnsetDataPath));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon learnset data source could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield wazaoboe_total.bin"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon learnset output file could not be read or written: {exception.Message}",
                file: SwShPokemonWorkflowService.LearnsetDataPath,
                expected: "Readable source and writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon learnset output file could not be read or written: {exception.Message}",
                file: SwShPokemonWorkflowService.LearnsetDataPath,
            expected: "Readable source and writable output root"));
        }
    }

    private static void ApplyEvolutionEdits(
        ProjectPaths paths,
        OpenedProject project,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var evolutionGroup in edits.GroupBy(edit => edit.RecordId, StringComparer.Ordinal))
        {
            if (!int.TryParse(evolutionGroup.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon Data edit targets an evolution record that is not loaded.",
                    field: "personalId",
                    expected: "Existing Pokemon evolution record"));
                continue;
            }

            var targetRelativePath = SwShPokemonWorkflowService.CreateEvolutionDataPath(personalId);
            var source = SwShPokemonWorkflowService.ResolveEvolutionDataSource(project, personalId);
            if (source is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution data source could not be resolved for apply.",
                    file: targetRelativePath,
                    expected: "Loaded Sword/Shield evo_###.bin"));
                continue;
            }

            var targetPath = ResolveOutputPath(paths, targetRelativePath, diagnostics);
            if (targetPath is null)
            {
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
                var record = SwShEvolutionSet.Parse(sourceBytes);

                foreach (var edit in evolutionGroup)
                {
                    var operation = TryParseEvolutionPendingEdit(edit, pokemon: null, diagnostics: diagnostics);
                    if (operation is null)
                    {
                        continue;
                    }

                    record = ApplyEvolutionOperation(record, operation);
                }

                if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    return;
                }

                var outputBytes = SwShEvolutionSet.Write(record.Evolutions);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllBytes(targetPath, outputBytes);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon evolution data source could not be decoded: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Sword/Shield evo_###.bin"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon evolution output file could not be read or written: {exception.Message}",
                    file: targetRelativePath,
                    expected: "Readable source and writable output root"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon evolution output file could not be read or written: {exception.Message}",
                    file: targetRelativePath,
                    expected: "Readable source and writable output root"));
            }
        }
    }

    private static PlannedFileWrite? CreatePlannedWrite(
        ProjectPaths paths,
        string targetRelativePath,
        IReadOnlyList<PendingEdit> edits,
        string reason,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, targetRelativePath, diagnostics);
        if (targetPath is null)
        {
            return null;
        }

        var sources = edits
            .SelectMany(edit => edit.Sources)
            .Distinct()
            .ToArray();

        return new PlannedFileWrite(
            targetRelativePath,
            sources,
            File.Exists(targetPath),
            reason);
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
                "Pokemon Data apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShPokemonWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon Data apply target must stay inside the configured output root.",
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

    private static string CreatePendingEditSummary(SwShPokemonRecord pokemon, string field, int value)
    {
        if (SwShPokemonWorkflowService.TryParseCompatibilityField(field, out var groupId, out var slot))
        {
            var entry = GetCompatibilityEntry(pokemon, groupId, slot);
            var compatibilityLabel = entry?.Label ?? field;
            return value == 0
                ? $"Disable {pokemon.Name} {compatibilityLabel} compatibility."
                : $"Enable {pokemon.Name} {compatibilityLabel} compatibility.";
        }

        var editableField = SwShPokemonWorkflowService.GetEditableField(field);
        var label = editableField?.Label ?? field;
        var displayValue = editableField?.ValueKind == "boolean"
            ? value == 0 ? "disabled" : "enabled"
            : value.ToString(CultureInfo.InvariantCulture);

        return $"Set {pokemon.Name} {label.ToLowerInvariant()} to {displayValue}.";
    }

    private static string CreateEvolutionPendingEditSummary(
        SwShPokemonRecord pokemon,
        EvolutionPendingOperation operation)
    {
        var existingEvolution = pokemon.Evolutions.FirstOrDefault(evolution => evolution.Slot == operation.Slot);
        return operation.Action switch
        {
            EvolutionUpsertAction when operation.Slot >= pokemon.Evolutions.Count =>
                $"Add {pokemon.Name} evolution to species {operation.Species} at level {operation.Level}.",
            EvolutionUpsertAction =>
                $"Set {pokemon.Name} evolution slot {operation.Slot} to species {operation.Species} at level {operation.Level}.",
            EvolutionRemoveAction =>
                $"Remove {pokemon.Name} evolution slot {operation.Slot}{FormatEvolutionSuffix(existingEvolution)}.",
            EvolutionMoveUpAction =>
                $"Move {pokemon.Name} evolution slot {operation.Slot} up.",
            EvolutionMoveDownAction =>
                $"Move {pokemon.Name} evolution slot {operation.Slot} down.",
            _ => $"Update {pokemon.Name} evolution slot {operation.Slot}.",
        };
    }

    private static string FormatEvolutionSuffix(SwShPokemonEvolutionRecord? evolution)
    {
        return evolution is null
            ? string.Empty
            : $" (method {evolution.Method}, species {evolution.Species}, level {evolution.Level})";
    }

    private static string CreateLearnsetPendingEditSummary(
        SwShPokemonRecord pokemon,
        LearnsetPendingOperation operation,
        string? moveName)
    {
        var existingMove = pokemon.Learnset.FirstOrDefault(move => move.Slot == operation.Slot);
        return operation.Action switch
        {
            LearnsetUpsertAction when operation.Slot >= pokemon.Learnset.Count =>
                $"Add {pokemon.Name} learnset move Lv. {operation.Level} {moveName ?? $"Move {operation.MoveId}"}.",
            LearnsetUpsertAction =>
                $"Set {pokemon.Name} learnset slot {operation.Slot} to Lv. {operation.Level} {moveName ?? $"Move {operation.MoveId}"}.",
            LearnsetRemoveAction =>
                $"Remove {pokemon.Name} learnset slot {operation.Slot}{FormatLearnsetMoveSuffix(existingMove)}.",
            LearnsetMoveUpAction =>
                $"Move {pokemon.Name} learnset slot {operation.Slot} up.",
            LearnsetMoveDownAction =>
                $"Move {pokemon.Name} learnset slot {operation.Slot} down.",
            LearnsetMoveToAction =>
                $"Move {pokemon.Name} learnset slot {operation.Slot} to slot {operation.MoveId}.",
            _ => $"Update {pokemon.Name} learnset slot {operation.Slot}.",
        };
    }

    private static string FormatLearnsetMoveSuffix(SwShPokemonLearnsetMove? move)
    {
        return move is null
            ? string.Empty
            : $" (Lv. {move.Level} {move.MoveName})";
    }

    private static bool IsPersonalDataEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
            && !IsLearnsetEdit(edit)
            && !IsEvolutionEdit(edit);
    }

    private static bool IsGlobalEvYieldEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, GlobalEvYieldRecordId, StringComparison.Ordinal)
            && IsGlobalEvYieldField(edit.Field);
    }

    private static bool IsGlobalEvYieldRestoreEdit(PendingEdit edit)
    {
        return IsGlobalEvYieldEdit(edit)
            && string.Equals(edit.NewValue, GlobalEvYieldRestoreValue, StringComparison.Ordinal);
    }

    private static bool IsGlobalEvYieldField(string? field)
    {
        return string.Equals(field, GlobalEvYieldField, StringComparison.Ordinal);
    }

    private static bool IsEvYieldField(string? field)
    {
        return field is
            SwShPokemonWorkflowService.EVYieldHPField or
            SwShPokemonWorkflowService.EVYieldAttackField or
            SwShPokemonWorkflowService.EVYieldDefenseField or
            SwShPokemonWorkflowService.EVYieldSpecialAttackField or
            SwShPokemonWorkflowService.EVYieldSpecialDefenseField or
            SwShPokemonWorkflowService.EVYieldSpeedField;
    }

    private static void ValidateGlobalEvYieldPendingEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.NewValue, GlobalEvYieldRemoveValue, StringComparison.Ordinal)
            && !string.Equals(edit.NewValue, GlobalEvYieldRestoreValue, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "EV Yield bulk action must be remove or restore.",
                field: GlobalEvYieldField,
                expected: "remove or restore"));
        }
    }

    private static bool IsLearnsetEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
            && edit.Field?.StartsWith($"{LearnsetFieldPrefix}:", StringComparison.Ordinal) == true;
    }

    private static bool IsEvolutionEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
            && edit.Field?.StartsWith($"{EvolutionFieldPrefix}:", StringComparison.Ordinal) == true;
    }

    private static ProjectFileReference CreateSourceReference(
        SwShPokemonWorkflowService.WorkflowFileSource source)
    {
        var layer = source.GraphEntry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new ProjectFileReference(layer, source.GraphEntry.RelativePath);
    }

    private static string ResolveMoveName(
        SwShPokemonWorkflow workflow,
        SwShPokemonRecord pokemon,
        int moveId)
    {
        var localName = ResolveMoveName(pokemon, moveId);
        if (!localName.StartsWith("Move ", StringComparison.Ordinal))
        {
            return localName;
        }

        return workflow.Pokemon
            .SelectMany(record => record.Learnset)
            .FirstOrDefault(move => move.MoveId == moveId)
            ?.MoveName
            ?? workflow.Pokemon
                .SelectMany(record => record.Compatibility)
                .SelectMany(group => group.Entries)
                .FirstOrDefault(entry => entry.MoveId == moveId)
                ?.MoveName
            ?? localName;
    }

    private static string ResolveMoveName(SwShPokemonRecord pokemon, int moveId)
    {
        return pokemon.Learnset.FirstOrDefault(move => move.MoveId == moveId)?.MoveName
            ?? pokemon.Compatibility
                .SelectMany(group => group.Entries)
                .FirstOrDefault(entry => entry.MoveId == moveId)
                ?.MoveName
            ?? $"Move {moveId}";
    }

    private static SwShPokemonCompatibilityEntry? GetCompatibilityEntry(
        SwShPokemonRecord pokemon,
        string groupId,
        int slot)
    {
        return pokemon.Compatibility
            .FirstOrDefault(group => string.Equals(group.GroupId, groupId, StringComparison.Ordinal))
            ?.Entries
            .FirstOrDefault(entry => entry.Slot == slot);
    }

    private static string FormatType(int value)
    {
        return value switch
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
            _ => $"Type {value}",
        };
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Pokemon field '{field}' is not supported by the Pokemon Data workflow yet.",
            field: "field",
            expected: "Supported Pokemon personal data or compatibility field");
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
            Domain: PokemonEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record LearnsetPendingOperation(
        string Action,
        int Slot,
        int? MoveId,
        int? Level);

    private sealed record EvolutionPendingOperation(
        string Action,
        int Slot,
        int? Method,
        int? Argument,
        int? Species,
        int? Form,
        int? Level);
}
