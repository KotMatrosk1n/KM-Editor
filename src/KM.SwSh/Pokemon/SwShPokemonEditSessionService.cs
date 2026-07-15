// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
    private const string GlobalExpYieldRecordId = GlobalEvYieldRecordId;
    private const string GlobalExpYieldField = "expYieldAll";
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

        ClearMemoryCaches();
        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits);
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
                OverlayPendingEdits(project, loadedWorkflow, globalUpdatedSession.PendingEdits),
                globalUpdatedSession,
                diagnostics);
        }

        if (IsGlobalExpYieldField(field))
        {
            var globalPendingEdit = CreateGlobalExpYieldPendingEdit(project, field, value, diagnostics);
            if (globalPendingEdit is null)
            {
                return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
            }

            var globalUpdatedSession = ReplacePendingPokemonEdit(currentSession, globalPendingEdit);

            return new SwShPokemonEditResult(
                OverlayPendingEdits(project, loadedWorkflow, globalUpdatedSession.PendingEdits),
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

        var pendingEdit = CreatePendingEdit(project, workflow, selectedPokemon, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingPokemonEdit(currentSession, pendingEdit);

        return new SwShPokemonEditResult(
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShPokemonEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShPokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        ClearMemoryCaches();
        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditPokemon(project, originalWorkflow, diagnostics))
        {
            return new SwShPokemonEditResult(originalWorkflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = originalWorkflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon Data batch update is missing a field or value.",
                    field: "updates",
                    expected: "Complete Pokemon Data field update"));
                continue;
            }

            PendingEdit? pendingEdit;
            if (IsGlobalEvYieldField(update.Field))
            {
                pendingEdit = CreateGlobalEvYieldPendingEdit(project, update.Field, update.Value, diagnostics);
            }
            else if (IsGlobalExpYieldField(update.Field))
            {
                pendingEdit = CreateGlobalExpYieldPendingEdit(project, update.Field, update.Value, diagnostics);
            }
            else
            {
                var pokemon = effectiveWorkflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == update.PersonalId);
                if (pokemon is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Pokemon personal record {update.PersonalId} is not present in the loaded Pokemon Data workflow.",
                        field: "personalId",
                        expected: "Existing Pokemon personal record"));
                    continue;
                }

                pendingEdit = CreatePendingEdit(
                    project,
                    effectiveWorkflow,
                    pokemon,
                    update.Field,
                    update.Value,
                    diagnostics);
            }

            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ReplacePendingPokemonEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShPokemonEditResult(originalWorkflow, currentSession, diagnostics);
        }

        return new SwShPokemonEditResult(effectiveWorkflow, updatedSession, diagnostics);
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

        ClearMemoryCaches();
        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits);
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
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits),
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

        ClearMemoryCaches();
        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits);
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
            project,
            workflow,
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
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        ClearMemoryCaches();
        var project = projectWorkspaceService.Open(paths);
        var workflow = pokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditPokemon(project, workflow, diagnostics);
        if (session.PendingEdits.Any(IsLearnsetEdit))
        {
            CanEditLearnsetData(project, diagnostics);
        }

        if (session.PendingEdits.Any(IsGlobalEvYieldRestoreEdit))
        {
            ValidateRestorePersonalDataIdentity(project, GlobalEvYieldField, diagnostics);
        }

        if (session.PendingEdits.Any(IsGlobalExpYieldRestoreEdit))
        {
            ValidateRestorePersonalDataIdentity(project, GlobalExpYieldField, diagnostics);
        }

        foreach (var edit in session.PendingEdits.Where(IsEvolutionEdit))
        {
            if (int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
            {
                CanEditEvolutionData(project, personalId, diagnostics);
            }
        }

        var validationWorkflow = workflow;
        var pokemonEdits = session.PendingEdits.Where(IsPokemonDomainEdit).ToArray();
        foreach (var edit in pokemonEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(validationWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                validationWorkflow = OverlayPendingEdits(project, validationWorkflow, [edit]);
            }
        }

        var editedFormOwnerIds = pokemonEdits
            .Where(edit => edit.Field is SwShPokemonWorkflowService.FormStatsIndexField or SwShPokemonWorkflowService.FormCountField)
            .Select(edit => int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
                ? personalId
                : -1)
            .Where(personalId => personalId >= 0)
            .ToHashSet();
        ValidateEditedFormOwnership(workflow, validationWorkflow, editedFormOwnerIds, diagnostics);

        if (pokemonEdits.Length > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
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
        var pokemonEdits = session.PendingEdits.Where(IsPokemonDomainEdit).ToArray();

        if (pokemonEdits.Length == 0)
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

        var project = projectWorkspaceService.Open(paths);
        var editFingerprint = ComputePendingEditFingerprint(pokemonEdits);
        var reason = (pokemonEdits.Length == 1
            ? $"Apply pending Pokemon Data edit: {pokemonEdits[0].Summary}"
            : $"Apply {pokemonEdits.Length} pending Pokemon Data edits.")
            + $" Edit fingerprint: {editFingerprint}.";

        var writes = new List<PlannedFileWrite>();
        var personalEdits = pokemonEdits.Where(IsPersonalDataEdit).ToArray();
        if (personalEdits.Length > 0)
        {
            var personalWrite = CreatePlannedWrite(
                paths,
                project,
                SwShPokemonWorkflowService.PersonalDataPath,
                personalEdits,
                reason,
                diagnostics);
            if (personalWrite is not null)
            {
                writes.Add(personalWrite);
            }
        }

        var learnsetEdits = pokemonEdits.Where(IsLearnsetEdit).ToArray();
        if (learnsetEdits.Length > 0)
        {
            var learnsetWrite = CreatePlannedWrite(
                paths,
                project,
                SwShPokemonWorkflowService.LearnsetDataPath,
                learnsetEdits,
                reason,
                diagnostics);
            if (learnsetWrite is not null)
            {
                writes.Add(learnsetWrite);
            }
        }

        var evolutionEdits = pokemonEdits.Where(IsEvolutionEdit).ToArray();
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
                project,
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

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
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

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Pokemon Data change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));

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
                $"Pokemon Data could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var outputRollback = rollbackScope!;
        try
        {
            using (outputRollback)
            {
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

                if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
                }
                else
                {
                    outputRollback.Commit();
                }
            }

            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error) && writtenFiles.Count > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    "Applied Pokemon Data change plan to the configured LayeredFS output root."));
            }

            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }
        finally
        {
            ClearMemoryCaches();
        }
    }

    private void ClearMemoryCaches()
    {
        projectWorkspaceService.ClearMemoryCache();
        pokemonWorkflowService.ClearMemoryCache();
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
                "Pokemon Data apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
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
        var personalSource = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        var learnsetSource = SwShPokemonWorkflowService.ResolveLearnsetDataSource(project);
        if (personalSource is null || learnsetSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset edit sessions require supported personal and learnset data files.",
                file: SwShPokemonWorkflowService.LearnsetDataPath,
                expected: "Loaded Sword/Shield personal_total.bin and wazaoboe_total.bin"));
            return false;
        }

        try
        {
            var personalRecords = SwShPersonalTable.Parse(File.ReadAllBytes(personalSource.AbsolutePath)).Records;
            var learnsetRecords = SwShPokemonLearnsetTable.Parse(File.ReadAllBytes(learnsetSource.AbsolutePath)).Records;
            if (personalRecords.Count == learnsetRecords.Count
                && personalRecords.Select(record => record.PersonalId)
                    .SequenceEqual(learnsetRecords.Select(record => record.PersonalId)))
            {
                return true;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal and learnset identities must match exactly ({personalRecords.Count} personal, {learnsetRecords.Count} learnset).",
                file: SwShPokemonWorkflowService.LearnsetDataPath,
                expected: "Matching Pokemon personal and learnset record count and IDs"));
            return false;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon learnset edit sources could not be verified: {exception.Message}",
                file: SwShPokemonWorkflowService.LearnsetDataPath,
                expected: "Readable matching Pokemon personal and learnset data"));
            return false;
        }
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

    private static void ValidateRestorePersonalDataIdentity(
        OpenedProject project,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var currentSource = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        var baseSource = SwShPokemonWorkflowService.ResolveBasePersonalDataSource(project);
        if (currentSource is null || baseSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Restore {GetRestoreYieldLabel(field)} requires current and base personal data files.",
                field: field,
                expected: "Readable current and base personal_total.bin"));
            return;
        }

        try
        {
            var currentRecords = SwShPersonalTable.Parse(File.ReadAllBytes(currentSource.AbsolutePath)).Records;
            var baseRecords = SwShPersonalTable.Parse(File.ReadAllBytes(baseSource.AbsolutePath)).Records;
            if (currentRecords.Count == baseRecords.Count
                && currentRecords.Select(record => record.PersonalId)
                    .SequenceEqual(baseRecords.Select(record => record.PersonalId)))
            {
                return;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Restore {GetRestoreYieldLabel(field)} requires current and base personal records to match exactly.",
                field: field,
                expected: "Matching personal record count and IDs"));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Restore {GetRestoreYieldLabel(field)} sources could not be verified: {exception.Message}",
                field: field,
                expected: "Readable matching current and base personal data"));
        }
    }

    private static string GetRestoreYieldLabel(string field)
    {
        return string.Equals(field, GlobalEvYieldField, StringComparison.Ordinal)
            ? "EV Yield"
            : "EXP Yield";
    }

    private static void ValidateEditedFormOwnership(
        SwShPokemonWorkflow originalWorkflow,
        SwShPokemonWorkflow finalWorkflow,
        IReadOnlySet<int> editedOwnerIds,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (editedOwnerIds.Count == 0)
        {
            return;
        }

        var originalById = originalWorkflow.Pokemon.ToDictionary(pokemon => pokemon.PersonalId);
        var finalById = finalWorkflow.Pokemon.ToDictionary(pokemon => pokemon.PersonalId);
        var changedOwnerIds = editedOwnerIds
            .Where(personalId => originalById.TryGetValue(personalId, out var original)
                && finalById.TryGetValue(personalId, out var final)
                && (original.Personal.FormStatsIndex != final.Personal.FormStatsIndex
                    || original.Personal.FormCount != final.Personal.FormCount))
            .ToHashSet();
        if (changedOwnerIds.Count == 0)
        {
            return;
        }

        var claims = new Dictionary<int, List<int>>();
        foreach (var pokemon in finalWorkflow.Pokemon)
        {
            var pointer = pokemon.Personal.FormStatsIndex;
            var count = pokemon.Personal.FormCount;
            if (count <= 1)
            {
                if (changedOwnerIds.Contains(pokemon.PersonalId) && pointer != 0)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Pokemon personal record {pokemon.PersonalId} must clear its form stats index when it has one form.",
                        field: SwShPokemonWorkflowService.FormStatsIndexField,
                        expected: "Zero form stats index for a single-form Pokemon"));
                }

                continue;
            }

            var endExclusive = (long)pointer + count - 1;
            if (pointer == 0
                || endExclusive > finalWorkflow.Pokemon.Count
                || (pokemon.PersonalId >= pointer && pokemon.PersonalId < endExclusive))
            {
                if (changedOwnerIds.Contains(pokemon.PersonalId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Pokemon personal record {pokemon.PersonalId} has an out-of-range or self-referencing form block.",
                        field: SwShPokemonWorkflowService.FormStatsIndexField,
                        expected: "In-range alternate-form block that does not include its owner"));
                }

                continue;
            }

            for (var target = pointer; target < endExclusive; target++)
            {
                if (!claims.TryGetValue(target, out var owners))
                {
                    owners = [];
                    claims[target] = owners;
                }

                owners.Add(pokemon.PersonalId);
            }
        }

        foreach (var (target, owners) in claims.Where(entry => entry.Value.Distinct().Count() > 1))
        {
            if (!owners.Any(changedOwnerIds.Contains))
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {target} is claimed by multiple form owners ({string.Join(", ", owners.Distinct().Order())}).",
                field: SwShPokemonWorkflowService.FormStatsIndexField,
                expected: "One unambiguous alternate-form owner"));
        }
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

        if (IsGlobalExpYieldEdit(edit))
        {
            ValidateGlobalExpYieldPendingEdit(edit, diagnostics);
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
            TryParseLearnsetPendingEdit(workflow, edit, pokemon, diagnostics);
            return;
        }

        if (IsEvolutionEdit(edit))
        {
            TryParseEvolutionPendingEdit(workflow, edit, pokemon, diagnostics);
            return;
        }

        TryParseEditableValue(workflow, pokemon, edit.Field, edit.NewValue, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        OpenedProject project,
        SwShPokemonWorkflow workflow,
        SwShPokemonRecord selectedPokemon,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var parsedValue = TryParseEditableValue(workflow, selectedPokemon, normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        var sources = new List<ProjectFileReference>
        {
            new(selectedPokemon.Provenance.SourceLayer, selectedPokemon.Provenance.SourceFile),
        };
        if (RequiresItemMetadataSource(normalizedField)
            && SwShItemsWorkflowService.ResolveItemDataSource(project) is { } itemSource)
        {
            sources.Add(CreateSourceReference(itemSource.GraphEntry));
        }

        return new PendingEdit(
            PokemonEditDomain,
            CreatePendingEditSummary(selectedPokemon, normalizedField, parsedValue.Value),
            sources,
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

            sources.Add(new ProjectFileReference(
                ProjectFileLayer.Base,
                baseSource.GraphEntry.RelativePath));
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidateRestorePersonalDataIdentity(project, GlobalEvYieldField, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount)
            {
                return null;
            }
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

    private static PendingEdit? CreateGlobalExpYieldPendingEdit(
        OpenedProject project,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!IsGlobalExpYieldField(normalizedField))
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
                "EXP Yield bulk action must be remove or restore.",
                field: GlobalExpYieldField,
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
                "Pokemon personal data source could not be resolved for EXP Yield bulk editing.",
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
                    "Restore EXP Yield requires the base personal data file so vanilla EXP yields can be copied back.",
                    field: GlobalExpYieldField,
                    expected: "Readable base personal_total.bin"));
                return null;
            }

            sources.Add(new ProjectFileReference(
                ProjectFileLayer.Base,
                baseSource.GraphEntry.RelativePath));
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidateRestorePersonalDataIdentity(project, GlobalExpYieldField, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount)
            {
                return null;
            }
        }

        var summary = normalizedValue == GlobalEvYieldRemoveValue
            ? "Remove EXP yields from all Pokemon."
            : "Restore all Pokemon EXP yields from vanilla personal data.";

        return new PendingEdit(
            PokemonEditDomain,
            summary,
            sources,
            RecordId: GlobalExpYieldRecordId,
            Field: GlobalExpYieldField,
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
            Sources:
            [
                CreateSourceReference(source),
                new ProjectFileReference(selectedPokemon.Provenance.SourceLayer, selectedPokemon.Provenance.SourceFile),
            ],
            RecordId: selectedPokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            Field: field,
            NewValue: newValue);
        var operation = TryParseLearnsetPendingEdit(workflow, pendingEdit, selectedPokemon, diagnostics);
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
        OpenedProject project,
        SwShPokemonWorkflow workflow,
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
            ? FindFirstAvailableEvolutionSlot(selectedPokemon.Evolutions)
            : slot;
        var fieldAction = normalizedAction == EvolutionAddAction
            ? EvolutionUpsertAction
            : normalizedAction;
        var sources = new List<ProjectFileReference>
        {
            CreateSourceReference(source),
            new(selectedPokemon.Provenance.SourceLayer, selectedPokemon.Provenance.SourceFile),
        };
        if (SwShItemsWorkflowService.ResolveItemDataSource(project) is { } itemSource)
        {
            sources.Add(CreateSourceReference(itemSource.GraphEntry));
        }

        var pendingEdit = new PendingEdit(
            PokemonEditDomain,
            Summary: string.Empty,
            Sources: sources.Distinct().ToArray(),
            RecordId: selectedPokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            Field: CreateEvolutionFieldId(fieldAction, normalizedSlot),
            NewValue: normalizedAction is EvolutionAddAction or EvolutionUpsertAction
                ? CreateEvolutionValue(method, argument, species, form, level)
                : "1");
        var operation = TryParseEvolutionPendingEdit(workflow, pendingEdit, selectedPokemon, diagnostics);
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
        SwShPokemonWorkflow? workflow,
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

        var editableField = workflow?.EditableFields.FirstOrDefault(candidate =>
                string.Equals(candidate.Field, field, StringComparison.Ordinal))
            ?? SwShPokemonWorkflowService.GetEditableField(field);
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

        var isUnchangedLegacyValue = pokemon is not null
            && TryGetCurrentEditableValue(pokemon, editableField.Field) == parsedValue.Value;
        if (!isUnchangedLegacyValue
            && (parsedValue.Value < (editableField.MinimumValue ?? int.MinValue)
            || parsedValue.Value > (editableField.MaximumValue ?? int.MaxValue))
        )
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: $"Safe Pokemon {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (!isUnchangedLegacyValue
            && editableField.Options.Count > 0
            && editableField.Options.All(option => option.Value != parsedValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must use an available option from the loaded Pokemon Data sources.",
                field: editableField.Field,
                expected: $"Available Pokemon {editableField.Label.ToLowerInvariant()} option"));
            return null;
        }

        return parsedValue.Value;
    }

    private static int? TryGetCurrentEditableValue(SwShPokemonRecord pokemon, string field)
    {
        return field switch
        {
            SwShPokemonWorkflowService.HPField => pokemon.BaseStats.HP,
            SwShPokemonWorkflowService.AttackField => pokemon.BaseStats.Attack,
            SwShPokemonWorkflowService.DefenseField => pokemon.BaseStats.Defense,
            SwShPokemonWorkflowService.SpecialAttackField => pokemon.BaseStats.SpecialAttack,
            SwShPokemonWorkflowService.SpecialDefenseField => pokemon.BaseStats.SpecialDefense,
            SwShPokemonWorkflowService.SpeedField => pokemon.BaseStats.Speed,
            SwShPokemonWorkflowService.Type1Field => pokemon.Personal.Type1,
            SwShPokemonWorkflowService.Type2Field => pokemon.Personal.Type2,
            SwShPokemonWorkflowService.CatchRateField => pokemon.CatchRate,
            SwShPokemonWorkflowService.EvolutionStageField => pokemon.EvolutionStage,
            SwShPokemonWorkflowService.EVYieldHPField => pokemon.Personal.EVYieldHP,
            SwShPokemonWorkflowService.EVYieldAttackField => pokemon.Personal.EVYieldAttack,
            SwShPokemonWorkflowService.EVYieldDefenseField => pokemon.Personal.EVYieldDefense,
            SwShPokemonWorkflowService.EVYieldSpecialAttackField => pokemon.Personal.EVYieldSpecialAttack,
            SwShPokemonWorkflowService.EVYieldSpecialDefenseField => pokemon.Personal.EVYieldSpecialDefense,
            SwShPokemonWorkflowService.EVYieldSpeedField => pokemon.Personal.EVYieldSpeed,
            SwShPokemonWorkflowService.HeldItem1Field => pokemon.Personal.HeldItem1,
            SwShPokemonWorkflowService.HeldItem2Field => pokemon.Personal.HeldItem2,
            SwShPokemonWorkflowService.HeldItem3Field => pokemon.Personal.HeldItem3,
            SwShPokemonWorkflowService.GenderRatioField => pokemon.GenderRatio,
            SwShPokemonWorkflowService.HatchCyclesField => pokemon.Personal.HatchCycles,
            SwShPokemonWorkflowService.BaseFriendshipField => pokemon.Personal.BaseFriendship,
            SwShPokemonWorkflowService.ExpGrowthField => pokemon.Personal.ExpGrowth,
            SwShPokemonWorkflowService.EggGroup1Field => pokemon.Personal.EggGroup1,
            SwShPokemonWorkflowService.EggGroup2Field => pokemon.Personal.EggGroup2,
            SwShPokemonWorkflowService.Ability1Field => pokemon.Abilities.Ability1,
            SwShPokemonWorkflowService.Ability2Field => pokemon.Abilities.Ability2,
            SwShPokemonWorkflowService.HiddenAbilityField => pokemon.Abilities.HiddenAbility,
            SwShPokemonWorkflowService.FormStatsIndexField => pokemon.Personal.FormStatsIndex,
            SwShPokemonWorkflowService.FormCountField => pokemon.Personal.FormCount,
            SwShPokemonWorkflowService.ColorField => pokemon.Personal.Color,
            SwShPokemonWorkflowService.IsPresentInGameField => pokemon.Personal.IsPresentInGame ? 1 : 0,
            SwShPokemonWorkflowService.HasSpriteFormField => pokemon.Personal.HasSpriteForm ? 1 : 0,
            SwShPokemonWorkflowService.BaseExperienceField => pokemon.BaseExperience,
            SwShPokemonWorkflowService.HeightField => pokemon.Height,
            SwShPokemonWorkflowService.WeightField => pokemon.Weight,
            SwShPokemonWorkflowService.ModelIdField when pokemon.Personal.ModelId <= int.MaxValue => checked((int)pokemon.Personal.ModelId),
            SwShPokemonWorkflowService.HatchedSpeciesField => pokemon.Personal.HatchedSpecies,
            SwShPokemonWorkflowService.LocalFormIndexField => pokemon.Personal.LocalFormIndex,
            SwShPokemonWorkflowService.IsRegionalFormField => pokemon.Personal.IsRegionalForm ? 1 : 0,
            SwShPokemonWorkflowService.CanNotDynamaxField => pokemon.Personal.CanNotDynamax ? 1 : 0,
            SwShPokemonWorkflowService.RegionalDexIndexField => pokemon.Personal.RegionalDexIndex,
            SwShPokemonWorkflowService.FormField => pokemon.Form,
            SwShPokemonWorkflowService.ArmorDexIndexField => pokemon.Personal.ArmorDexIndex,
            SwShPokemonWorkflowService.CrownDexIndexField => pokemon.Personal.CrownDexIndex,
            _ => null,
        };
    }

    private static LearnsetPendingOperation? TryParseLearnsetPendingEdit(
        SwShPokemonWorkflow? workflow,
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
        ValidateLearnsetOperation(workflow, pokemon, operation, diagnostics);
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount
            ? null
            : operation;
    }

    private static void ValidateLearnsetOperation(
        SwShPokemonWorkflow? workflow,
        SwShPokemonRecord? pokemon,
        LearnsetPendingOperation operation,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var currentMove = pokemon?.Learnset.FirstOrDefault(move => move.Slot == operation.Slot);
        var isUnchangedMoveId = currentMove is not null && currentMove.MoveId == operation.MoveId;
        var isUnchangedLevel = currentMove is not null && currentMove.Level == operation.Level;
        if (operation.Action != LearnsetMoveToAction
            && operation.MoveId is not null
            && !isUnchangedMoveId
            && ((uint)operation.MoveId.Value > ushort.MaxValue
                || (pokemon is not null && operation.MoveId.Value == 0)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset move ID must identify a usable nonzero move.",
                field: "moveId",
                expected: "Available Pokemon learnset move"));
        }

        if (operation.Level is not null
            && !isUnchangedLevel
            && (uint)operation.Level.Value > (pokemon is null ? ushort.MaxValue : 100))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon learnset level must be between 0 and {(pokemon is null ? ushort.MaxValue : 100)}.",
                field: "level",
                expected: "Safe Pokemon learnset level"));
        }

        if (operation.Action == LearnsetUpsertAction
            && operation.MoveId is not null
            && operation.Level is not null
            && operation.MoveId.Value == ushort.MaxValue
            && operation.Level.Value == ushort.MaxValue)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset rows cannot use the end-of-record sentinel as a move.",
                field: "moveId",
                expected: "Non-sentinel Pokemon learnset row"));
        }

        if (workflow is not null
            && operation.Action == LearnsetUpsertAction
            && operation.MoveId is not null
            && !isUnchangedMoveId
            && workflow.LearnsetMoveOptions.Count > 0
            && workflow.LearnsetMoveOptions.All(option => option.Value != operation.MoveId.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset move must be usable in the loaded Sword/Shield move data.",
                field: "moveId",
                expected: "Available Pokemon learnset move"));
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
        SwShPokemonWorkflow? workflow,
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
        ValidateEvolutionOperation(workflow, pokemon, operation, diagnostics);
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount
            ? null
            : operation;
    }

    private static void ValidateEvolutionOperation(
        SwShPokemonWorkflow? workflow,
        SwShPokemonRecord? pokemon,
        EvolutionPendingOperation operation,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var currentEvolution = pokemon?.Evolutions.FirstOrDefault(evolution => evolution.Slot == operation.Slot);
        var isUnchangedMethod = currentEvolution is not null && currentEvolution.Method == operation.Method;
        var isUnchangedArgument = currentEvolution is not null && currentEvolution.Argument == operation.Argument;
        var isUnchangedSpecies = currentEvolution is not null && currentEvolution.Species == operation.Species;
        var isUnchangedForm = currentEvolution is not null && currentEvolution.Form == operation.Form;
        var isUnchangedLevel = currentEvolution is not null && currentEvolution.Level == operation.Level;

        if (operation.Method is not null
            && !isUnchangedMethod
            && (uint)operation.Method.Value > ushort.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("method", "method", ushort.MaxValue));
        }

        if (operation.Method is not null
            && !isUnchangedMethod
            && pokemon is not null
            && operation.Method.Value is 0 or 35)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon evolution rows must use an active evolution method.",
                field: "method",
                expected: "Supported nonzero Pokemon evolution method"));
        }

        if (operation.Argument is not null
            && !isUnchangedArgument
            && (uint)operation.Argument.Value > ushort.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("argument", "argument", ushort.MaxValue));
        }

        if (operation.Species is not null
            && !isUnchangedSpecies
            && (uint)operation.Species.Value > ushort.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("target species", "species", ushort.MaxValue));
        }

        if (operation.Species is not null
            && !isUnchangedSpecies
            && pokemon is not null
            && operation.Species.Value == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon evolution target species must be nonzero.",
                field: "species",
                expected: "Available nonzero Pokemon evolution target species"));
        }

        if (operation.Form is not null
            && !isUnchangedForm
            && (uint)operation.Form.Value > byte.MaxValue)
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("target form", "form", byte.MaxValue));
        }

        if (operation.Level is not null
            && !isUnchangedLevel
            && (uint)operation.Level.Value > (pokemon is null ? byte.MaxValue : 100))
        {
            diagnostics.Add(CreateEvolutionRangeDiagnostic("level", "level", pokemon is null ? byte.MaxValue : 100));
        }

        if (workflow is not null && operation.Action == EvolutionUpsertAction)
        {
            var methodOption = operation.Method is null
                ? null
                : workflow.EvolutionMethodOptions.FirstOrDefault(option => option.Value == operation.Method.Value);
            if (!isUnchangedMethod && methodOption is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution method must use a supported Sword/Shield method.",
                    field: "method",
                    expected: "Available Pokemon evolution method"));
            }

            if (operation.Argument is not null
                && !isUnchangedArgument
                && methodOption is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution argument cannot be edited for an unsupported evolution method.",
                    field: "argument",
                    expected: "Argument for an available Pokemon evolution method"));
            }

            if (operation.Argument is not null
                && (!isUnchangedArgument || !isUnchangedMethod)
                && methodOption is not null)
            {
                var argumentIsValid = methodOption.ArgumentKind is "none" or "level"
                    ? operation.Argument.Value == 0
                    : methodOption.ArgumentOptions.Count > 0
                        ? methodOption.ArgumentOptions.Any(option => option.Value == operation.Argument.Value)
                        : methodOption.ArgumentKind is "item" or "move" or "species"
                            ? operation.Argument.Value != 0
                            : true;
                if (!argumentIsValid)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pokemon evolution argument must match the selected method.",
                        field: "argument",
                        expected: "Available argument for the selected evolution method"));
                }
            }

            if (operation.Species is not null && !isUnchangedSpecies)
            {
                var speciesOptions = workflow.EditableFields
                    .FirstOrDefault(field => field.Field == SwShPokemonWorkflowService.HatchedSpeciesField)
                    ?.Options;
                if (speciesOptions is { Count: > 0 }
                    && speciesOptions.All(option => option.Value != operation.Species.Value))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pokemon evolution target must be present in the loaded Sword/Shield personal data.",
                        field: "species",
                        expected: "Available Pokemon evolution target species"));
                }
            }
        }

        if (operation.Slot >= SwShEvolutionSet.MaxEvolutionCount)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon evolution slot must be between 0 and {SwShEvolutionSet.MaxEvolutionCount - 1}.",
                field: "slot",
                expected: "Safe Pokemon evolution slot"));
        }

        if (pokemon is null)
        {
            return;
        }

        var orderedEvolutions = pokemon.Evolutions
            .OrderBy(evolution => evolution.Slot)
            .ToArray();
        var targetIndex = Array.FindIndex(
            orderedEvolutions,
            evolution => evolution.Slot == operation.Slot);
        var count = orderedEvolutions.Length;
        switch (operation.Action)
        {
            case EvolutionUpsertAction when targetIndex < 0 && count >= SwShEvolutionSet.MaxEvolutionCount:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon evolution files support at most {SwShEvolutionSet.MaxEvolutionCount} rows.",
                    field: "slot",
                    expected: "Pokemon evolution file with room for another row"));
                break;
            case EvolutionRemoveAction when targetIndex < 0:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution remove must target an existing row.",
                    field: "slot",
                    expected: "Existing Pokemon evolution row"));
                break;
            case EvolutionMoveUpAction when targetIndex <= 0:
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution move-up must target a row below the first row.",
                    field: "slot",
                    expected: "Pokemon evolution row that can move up"));
                break;
            case EvolutionMoveDownAction when targetIndex < 0 || targetIndex >= count - 1:
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

    private static int FindFirstAvailableEvolutionSlot(
        IReadOnlyList<SwShPokemonEvolutionRecord> evolutions)
    {
        var occupiedSlots = evolutions.Select(evolution => evolution.Slot).ToHashSet();
        for (var slot = 0; slot < SwShEvolutionSet.MaxEvolutionCount; slot++)
        {
            if (!occupiedSlots.Contains(slot))
            {
                return slot;
            }
        }

        return SwShEvolutionSet.MaxEvolutionCount;
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

        if (IsGlobalExpYieldEdit(pendingEdit))
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit => !(
                        string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
                        && (IsGlobalExpYieldEdit(edit)
                            || string.Equals(edit.Field, SwShPokemonWorkflowService.BaseExperienceField, StringComparison.Ordinal))))
                    .Append(pendingEdit)
                    .ToArray(),
            };
        }

        if (IsOrderedRowOperation(pendingEdit))
        {
            return session with
            {
                PendingEdits = session.PendingEdits
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
        return OverlayPendingEdits(workflow, edits, basePersonalById: null);
    }

    private static SwShPokemonWorkflow OverlayPendingEdits(
        OpenedProject project,
        SwShPokemonWorkflow workflow,
        IReadOnlyList<PendingEdit> edits)
    {
        return OverlayPendingEdits(
            workflow,
            edits,
            LoadBasePersonalRecordsForOverlay(project, workflow, edits));
    }

    private static SwShPokemonWorkflow OverlayPendingEdits(
        SwShPokemonWorkflow workflow,
        IReadOnlyList<PendingEdit> edits,
        IReadOnlyDictionary<int, SwShPersonalRecord>? basePersonalById)
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
                OverlayGlobalEvYieldEdit(overlaid, basePersonalById, edit);
                continue;
            }

            if (IsGlobalExpYieldEdit(edit))
            {
                OverlayGlobalExpYieldEdit(overlaid, basePersonalById, edit);
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
                var operation = TryParseLearnsetPendingEdit(workflow, edit, pokemon, parseDiagnostics);
                if (operation is not null)
                {
                    var moveName = operation.Action == LearnsetMoveToAction || operation.MoveId is null
                        ? null
                        : ResolveMoveName(workflow, pokemon, operation.MoveId.Value);
                    overlaid[personalId] = ApplyPokemonLearnsetViewOperation(pokemon, operation, moveName);
                }

                continue;
            }

            if (IsEvolutionEdit(edit))
            {
                var parseDiagnostics = new List<ValidationDiagnostic>();
                var operation = TryParseEvolutionPendingEdit(workflow, edit, pokemon, parseDiagnostics);
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

            overlaid[personalId] = ApplyPokemonViewField(workflow, pokemon, edit.Field!, value);
        }

        IReadOnlyList<SwShPokemonRecord> updatedPokemon = workflow.Pokemon
            .Select(pokemon => overlaid.TryGetValue(pokemon.PersonalId, out var updated) ? updated : pokemon)
            .ToArray();
        if (edits.Any(IsDisplayIdentityEdit))
        {
            updatedPokemon = SwShPokemonWorkflowService.RefreshDisplayIdentities(
                workflow.Pokemon,
                updatedPokemon);
        }

        return workflow with
        {
            Pokemon = updatedPokemon,
            Stats = workflow.Stats with
            {
                TotalPokemonCount = updatedPokemon.Count,
                PresentPokemonCount = updatedPokemon.Count(pokemon => pokemon.DexPresence.IsPresentInGame),
                TotalEvolutionCount = updatedPokemon.Sum(pokemon => pokemon.Evolutions.Count),
                TotalLearnsetMoveCount = updatedPokemon.Sum(pokemon => pokemon.Learnset.Count),
            },
        };
    }

    private static IReadOnlyDictionary<int, SwShPersonalRecord>? LoadBasePersonalRecordsForOverlay(
        OpenedProject project,
        SwShPokemonWorkflow workflow,
        IReadOnlyList<PendingEdit> edits)
    {
        if (!edits.Any(edit => IsGlobalEvYieldRestoreEdit(edit) || IsGlobalExpYieldRestoreEdit(edit))
            || SwShPokemonWorkflowService.ResolveBasePersonalDataSource(project) is not { } baseSource)
        {
            return null;
        }

        try
        {
            var baseRecords = SwShPersonalTable.Parse(File.ReadAllBytes(baseSource.AbsolutePath)).Records;
            if (baseRecords.Count != workflow.Pokemon.Count
                || !baseRecords.Select(record => record.PersonalId)
                    .SequenceEqual(workflow.Pokemon.Select(record => record.PersonalId)))
            {
                return null;
            }

            return baseRecords.ToDictionary(record => record.PersonalId);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void OverlayGlobalEvYieldEdit(
        IDictionary<int, SwShPokemonRecord> overlaid,
        IReadOnlyDictionary<int, SwShPersonalRecord>? basePersonalById,
        PendingEdit edit)
    {
        if (string.Equals(edit.NewValue, GlobalEvYieldRemoveValue, StringComparison.Ordinal))
        {
            foreach (var personalId in overlaid.Keys.ToArray())
            {
                overlaid[personalId] = ClearPokemonViewEvYield(overlaid[personalId]);
            }

            return;
        }

        if (!string.Equals(edit.NewValue, GlobalEvYieldRestoreValue, StringComparison.Ordinal)
            || basePersonalById is null)
        {
            return;
        }

        foreach (var personalId in overlaid.Keys.ToArray())
        {
            if (basePersonalById.TryGetValue(personalId, out var basePersonal))
            {
                overlaid[personalId] = RestorePokemonViewEvYield(overlaid[personalId], basePersonal);
            }
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

    private static SwShPokemonRecord RestorePokemonViewEvYield(
        SwShPokemonRecord pokemon,
        SwShPersonalRecord basePersonal)
    {
        return pokemon with
        {
            Personal = pokemon.Personal with
            {
                EVYieldHP = basePersonal.EVYieldHP,
                EVYieldAttack = basePersonal.EVYieldAttack,
                EVYieldDefense = basePersonal.EVYieldDefense,
                EVYieldSpecialAttack = basePersonal.EVYieldSpecialAttack,
                EVYieldSpecialDefense = basePersonal.EVYieldSpecialDefense,
                EVYieldSpeed = basePersonal.EVYieldSpeed,
            },
        };
    }

    private static void OverlayGlobalExpYieldEdit(
        IDictionary<int, SwShPokemonRecord> overlaid,
        IReadOnlyDictionary<int, SwShPersonalRecord>? basePersonalById,
        PendingEdit edit)
    {
        if (string.Equals(edit.NewValue, GlobalEvYieldRemoveValue, StringComparison.Ordinal))
        {
            foreach (var personalId in overlaid.Keys.ToArray())
            {
                overlaid[personalId] = ClearPokemonViewExpYield(overlaid[personalId]);
            }

            return;
        }

        if (!string.Equals(edit.NewValue, GlobalEvYieldRestoreValue, StringComparison.Ordinal)
            || basePersonalById is null)
        {
            return;
        }

        foreach (var personalId in overlaid.Keys.ToArray())
        {
            if (basePersonalById.TryGetValue(personalId, out var basePersonal))
            {
                overlaid[personalId] = RestorePokemonViewExpYield(overlaid[personalId], basePersonal);
            }
        }
    }

    private static SwShPokemonRecord ClearPokemonViewExpYield(SwShPokemonRecord pokemon)
    {
        return pokemon with
        {
            BaseExperience = 0,
            Personal = pokemon.Personal with { BaseExperience = 0 },
        };
    }

    private static SwShPokemonRecord RestorePokemonViewExpYield(
        SwShPokemonRecord pokemon,
        SwShPersonalRecord basePersonal)
    {
        return pokemon with
        {
            BaseExperience = basePersonal.BaseExperience,
            Personal = pokemon.Personal with { BaseExperience = basePersonal.BaseExperience },
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
            FormatEvolutionArgumentValue(workflow, methodOption, argumentKind, evolution.Argument));
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
        SwShPokemonWorkflow workflow,
        SwShPokemonEvolutionMethodOption? methodOption,
        string argumentKind,
        int argument)
    {
        if (string.Equals(argumentKind, "none", StringComparison.Ordinal)
            || string.Equals(argumentKind, "level", StringComparison.Ordinal))
        {
            return "None";
        }

        var option = methodOption
            ?.ArgumentOptions
            .FirstOrDefault(candidate => candidate.Value == argument);
        if (option is null && string.Equals(argumentKind, "item", StringComparison.Ordinal))
        {
            option = workflow.EvolutionMethodOptions
                .Where(candidate => string.Equals(candidate.ArgumentKind, "item", StringComparison.Ordinal))
                .SelectMany(candidate => candidate.ArgumentOptions)
                .FirstOrDefault(candidate => candidate.Value == argument);
        }

        return option?.Label ?? argument.ToString(CultureInfo.InvariantCulture);
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

    private static SwShPokemonRecord ApplyPokemonViewField(
        SwShPokemonWorkflow workflow,
        SwShPokemonRecord pokemon,
        string field,
        int value)
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
            SwShPokemonWorkflowService.GenderRatioField => pokemon with
            {
                GenderRatio = value,
                GenderRatioLabel = ResolveEditableOptionLabel(workflow, field, value, value.ToString(CultureInfo.InvariantCulture)),
                Personal = pokemon.Personal with { GenderRatio = value },
            },
            SwShPokemonWorkflowService.HatchCyclesField => pokemon with { Personal = pokemon.Personal with { HatchCycles = value } },
            SwShPokemonWorkflowService.BaseFriendshipField => pokemon with { Personal = pokemon.Personal with { BaseFriendship = value } },
            SwShPokemonWorkflowService.ExpGrowthField => pokemon with { Personal = pokemon.Personal with { ExpGrowth = value } },
            SwShPokemonWorkflowService.EggGroup1Field => pokemon with { Personal = pokemon.Personal with { EggGroup1 = value } },
            SwShPokemonWorkflowService.EggGroup2Field => pokemon with { Personal = pokemon.Personal with { EggGroup2 = value } },
            SwShPokemonWorkflowService.Ability1Field => pokemon with
            {
                Abilities = pokemon.Abilities with
                {
                    Ability1 = value,
                    Ability1Label = ResolveEditableOptionLabel(workflow, field, value, $"Ability {value}"),
                },
            },
            SwShPokemonWorkflowService.Ability2Field => pokemon with
            {
                Abilities = pokemon.Abilities with
                {
                    Ability2 = value,
                    Ability2Label = ResolveEditableOptionLabel(workflow, field, value, $"Ability {value}"),
                },
            },
            SwShPokemonWorkflowService.HiddenAbilityField => pokemon with
            {
                Abilities = pokemon.Abilities with
                {
                    HiddenAbility = value,
                    HiddenAbilityLabel = ResolveEditableOptionLabel(workflow, field, value, $"Ability {value}"),
                },
            },
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

    private static string ResolveEditableOptionLabel(
        SwShPokemonWorkflow workflow,
        string field,
        int value,
        string fallback)
    {
        return workflow.EditableFields
            .FirstOrDefault(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal))
            ?.Options
            .FirstOrDefault(option => option.Value == value)
            ?.Label
            ?? fallback;
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
            .OrderBy(evolution => evolution.Slot)
            .ToList();
        var targetIndex = evolutions.FindIndex(evolution => evolution.Slot == operation.Slot);

        switch (operation.Action)
        {
            case EvolutionUpsertAction
                when operation.Method is not null
                    && operation.Argument is not null
                    && operation.Species is not null
                    && operation.Form is not null
                    && operation.Level is not null
                    && targetIndex >= 0:
                evolutions[targetIndex] = new SwShEvolutionRecord(
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
                    && targetIndex < 0
                    && operation.Slot < SwShEvolutionSet.MaxEvolutionCount
                    && evolutions.Count < SwShEvolutionSet.MaxEvolutionCount:
                evolutions.Add(new SwShEvolutionRecord(
                    operation.Slot,
                    operation.Method.Value,
                    operation.Argument.Value,
                    operation.Species.Value,
                    operation.Form.Value,
                    operation.Level.Value));
                break;
            case EvolutionRemoveAction when targetIndex >= 0:
                evolutions.RemoveAt(targetIndex);
                break;
            case EvolutionMoveUpAction when targetIndex > 0:
                SwapEvolutionPayloads(evolutions, targetIndex, targetIndex - 1);
                break;
            case EvolutionMoveDownAction when targetIndex >= 0 && targetIndex < evolutions.Count - 1:
                SwapEvolutionPayloads(evolutions, targetIndex, targetIndex + 1);
                break;
        }

        return record with
        {
            Evolutions = evolutions.OrderBy(evolution => evolution.Slot).ToArray(),
        };
    }

    private static void SwapEvolutionPayloads(
        IList<SwShEvolutionRecord> evolutions,
        int sourceIndex,
        int destinationIndex)
    {
        var source = evolutions[sourceIndex];
        var destination = evolutions[destinationIndex];
        evolutions[sourceIndex] = destination with { Slot = source.Slot };
        evolutions[destinationIndex] = source with { Slot = destination.Slot };
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
                moves = MoveLearnsetMoveIdsKeepingSlotLevels(moves, operation.Slot, operation.Slot - 1);
                break;
            case LearnsetMoveDownAction when operation.Slot >= 0 && operation.Slot < moves.Count - 1:
                moves = MoveLearnsetMoveIdsKeepingSlotLevels(moves, operation.Slot, operation.Slot + 1);
                break;
            case LearnsetMoveToAction when operation.MoveId is not null
                && operation.Slot >= 0
                && operation.Slot < moves.Count
                && operation.MoveId.Value >= 0
                && operation.MoveId.Value < moves.Count:
                moves = MoveLearnsetMoveIdsKeepingSlotLevels(moves, operation.Slot, operation.MoveId.Value);
                break;
        }

        return record with
        {
            Moves = NormalizeLearnsetSlots(moves),
        };
    }

    private static List<SwShPokemonLearnsetMoveRecord> MoveLearnsetMoveIdsKeepingSlotLevels(
        IReadOnlyList<SwShPokemonLearnsetMoveRecord> moves,
        int sourceSlot,
        int destinationSlot)
    {
        var slotLevels = moves.Select(move => move.Level).ToArray();
        var moveIds = moves.Select(move => move.MoveId).ToList();
        var movedMoveId = moveIds[sourceSlot];
        moveIds.RemoveAt(sourceSlot);
        moveIds.Insert(destinationSlot, movedMoveId);

        return moveIds
            .Select((moveId, index) => new SwShPokemonLearnsetMoveRecord(index, moveId, slotLevels[index]))
            .ToList();
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

                if (IsGlobalExpYieldEdit(edit))
                {
                    ApplyGlobalExpYieldPersonalDataEdit(project, records, edit, diagnostics);
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

                if (!int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending Pokemon Data edit value is not a valid integer.",
                        field: edit.Field,
                        expected: "Reviewed Pokemon personal data value"));
                    continue;
                }

                records[personalId] = ApplyPersonalDataField(records[personalId], edit.Field!, value);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            var outputBytes = SwShPersonalTable.Write(records, sourceBytes);
            WriteAllBytesAtomically(targetPath, outputBytes, "Pokemon personal data");
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
        if (records.Length != baseRecords.Count
            || !records.Select(record => record.PersonalId)
                .SequenceEqual(baseRecords.Select(record => record.PersonalId)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Restore EV Yield requires current and base personal records to match exactly.",
                field: GlobalEvYieldField,
                expected: "Matching personal record count and IDs"));
            return;
        }

        for (var index = 0; index < records.Length; index++)
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

    private static void ApplyGlobalExpYieldPersonalDataEdit(
        OpenedProject project,
        SwShPersonalRecord[] records,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.Equals(edit.NewValue, GlobalEvYieldRemoveValue, StringComparison.Ordinal))
        {
            for (var index = 0; index < records.Length; index++)
            {
                records[index] = records[index] with { BaseExperience = 0 };
            }

            return;
        }

        if (!string.Equals(edit.NewValue, GlobalEvYieldRestoreValue, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "EXP Yield bulk action must be remove or restore.",
                field: GlobalExpYieldField,
                expected: "remove or restore"));
            return;
        }

        var baseSource = SwShPokemonWorkflowService.ResolveBasePersonalDataSource(project);
        if (baseSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Restore EXP Yield requires the base personal data file so vanilla EXP yields can be copied back.",
                field: GlobalExpYieldField,
                expected: "Readable base personal_total.bin"));
            return;
        }

        var baseRecords = SwShPersonalTable.Parse(File.ReadAllBytes(baseSource.AbsolutePath)).Records;
        if (records.Length != baseRecords.Count
            || !records.Select(record => record.PersonalId)
                .SequenceEqual(baseRecords.Select(record => record.PersonalId)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Restore EXP Yield requires current and base personal records to match exactly.",
                field: GlobalExpYieldField,
                expected: "Matching personal record count and IDs"));
            return;
        }

        for (var index = 0; index < records.Length; index++)
        {
            records[index] = records[index] with { BaseExperience = baseRecords[index].BaseExperience };
        }
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

                var operation = TryParseLearnsetPendingEdit(null, edit, pokemon: null, diagnostics: diagnostics);
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
            WriteAllBytesAtomically(targetPath, outputBytes, "Pokemon learnset data");
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
                    var operation = TryParseEvolutionPendingEdit(null, edit, pokemon: null, diagnostics: diagnostics);
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
                WriteAllBytesAtomically(targetPath, outputBytes, "Pokemon evolution data");
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
        OpenedProject project,
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
            .ToList();
        AddCurrentSemanticSources(project, edits, sources);

        return new PlannedFileWrite(
            targetRelativePath,
            sources.Distinct().ToArray(),
            File.Exists(targetPath),
            reason);
    }

    private static void AddCurrentSemanticSources(
        OpenedProject project,
        IReadOnlyList<PendingEdit> edits,
        List<ProjectFileReference> sources)
    {
        if (edits.Any(edit => IsGlobalEvYieldRestoreEdit(edit) || IsGlobalExpYieldRestoreEdit(edit)))
        {
            sources.RemoveAll(source => string.Equals(
                source.RelativePath,
                SwShPokemonWorkflowService.PersonalDataPath,
                StringComparison.OrdinalIgnoreCase));
            if (SwShPokemonWorkflowService.ResolvePersonalDataSource(project) is { } currentSource)
            {
                sources.Add(CreateSourceReference(currentSource));
            }

            if (SwShPokemonWorkflowService.ResolveBasePersonalDataSource(project) is { } baseSource)
            {
                sources.Add(new ProjectFileReference(
                    ProjectFileLayer.Base,
                    baseSource.GraphEntry.RelativePath));
            }
        }

        var requiresItemMetadata = edits.Any(edit =>
            IsEvolutionEdit(edit)
            || (edit.Field is not null && RequiresItemMetadataSource(edit.Field)));
        if (!requiresItemMetadata)
        {
            return;
        }

        sources.RemoveAll(source => string.Equals(
            source.RelativePath,
            SwShItemsWorkflowService.ItemDataPath,
            StringComparison.OrdinalIgnoreCase));
        if (SwShItemsWorkflowService.ResolveItemDataSource(project) is { } itemSource)
        {
            sources.Add(CreateSourceReference(itemSource.GraphEntry));
            return;
        }

        // Missing optional item metadata is itself reviewed state: an output item table
        // appearing later can remap which move a TM/TR compatibility bit represents.
        sources.Add(new ProjectFileReference(
            ProjectFileLayer.Generated,
            SwShItemsWorkflowService.ItemDataPath));
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

    private static void WriteAllBytesAtomically(string targetPath, byte[] contents, string label)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException($"{label} output has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, contents);
            if (!File.ReadAllBytes(temporaryPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException($"{label} temporary output verification failed.");
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string ComputePendingEditFingerprint(IReadOnlyList<PendingEdit> edits)
    {
        var canonical = new StringBuilder();
        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
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
        return IsPokemonDomainEdit(edit)
            && !IsLearnsetEdit(edit)
            && !IsEvolutionEdit(edit);
    }

    private static bool IsPokemonDomainEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal);
    }

    private static bool IsDisplayIdentityEdit(PendingEdit edit)
    {
        return IsPokemonDomainEdit(edit)
            && edit.Field is SwShPokemonWorkflowService.FormStatsIndexField
                or SwShPokemonWorkflowService.FormCountField
                or SwShPokemonWorkflowService.IsRegionalFormField;
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

    private static bool IsGlobalExpYieldEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, GlobalExpYieldRecordId, StringComparison.Ordinal)
            && IsGlobalExpYieldField(edit.Field);
    }

    private static bool IsGlobalExpYieldRestoreEdit(PendingEdit edit)
    {
        return IsGlobalExpYieldEdit(edit)
            && string.Equals(edit.NewValue, GlobalEvYieldRestoreValue, StringComparison.Ordinal);
    }

    private static bool IsGlobalExpYieldField(string? field)
    {
        return string.Equals(field, GlobalExpYieldField, StringComparison.Ordinal);
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

    private static void ValidateGlobalExpYieldPendingEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.NewValue, GlobalEvYieldRemoveValue, StringComparison.Ordinal)
            && !string.Equals(edit.NewValue, GlobalEvYieldRestoreValue, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "EXP Yield bulk action must be remove or restore.",
                field: GlobalExpYieldField,
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

    private static bool IsOrderedRowOperation(PendingEdit edit)
    {
        return IsLearnsetEdit(edit) || IsEvolutionEdit(edit);
    }

    private static ProjectFileReference CreateSourceReference(
        SwShPokemonWorkflowService.WorkflowFileSource source)
    {
        return CreateSourceReference(source.GraphEntry);
    }

    private static ProjectFileReference CreateSourceReference(ProjectFileGraphEntry graphEntry)
    {
        var layer = graphEntry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new ProjectFileReference(layer, graphEntry.RelativePath);
    }

    private static bool RequiresItemMetadataSource(string field)
    {
        if (field is SwShPokemonWorkflowService.HeldItem1Field
            or SwShPokemonWorkflowService.HeldItem2Field
            or SwShPokemonWorkflowService.HeldItem3Field)
        {
            return true;
        }

        return SwShPokemonWorkflowService.TryParseCompatibilityField(field, out var groupId, out _)
            && groupId is SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId
                or SwShPokemonWorkflowService.TechnicalRecordCompatibilityGroupId;
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
