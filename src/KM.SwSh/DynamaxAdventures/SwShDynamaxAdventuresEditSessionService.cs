// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresEditSessionService
{
    internal const string RepairExecutableProjectionSummary = "Repair Dynamax Adventures executable projection.";
    internal const string RestoreVanillaTableSummary = "Restore the vanilla Dynamax Adventures table.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShDynamaxAdventuresWorkflowService dynamaxAdventuresWorkflowService;

    public SwShDynamaxAdventuresEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShDynamaxAdventuresWorkflowService? dynamaxAdventuresWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.dynamaxAdventuresWorkflowService = dynamaxAdventuresWorkflowService ?? new SwShDynamaxAdventuresWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShDynamaxAdventuresEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int entryIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ValidatePendingSessionStructure(workflow, currentSession, diagnostics))
        {
            return new SwShDynamaxAdventuresEditResult(
                workflow,
                currentSession with { PendingEdits = [] },
                diagnostics);
        }

        if (!CanEditDynamaxAdventures(project, workflow, diagnostics))
        {
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        var existingRecordIds = currentSession.PendingEdits
            .Where(edit => string.Equals(
                edit.Domain,
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
                StringComparison.Ordinal))
            .Select(edit => edit.RecordId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var requestedRecordId = SwShDynamaxAdventuresWorkflowService.CreateEncounterRecordId(entryIndex);
        if (existingRecordIds.Any(recordId => !string.Equals(recordId, requestedRecordId, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "A Dynamax Adventures edit session can contain changes for only one Pokemon row. Apply or discard the current row before editing another row.",
                field: "entryIndex",
                expected: "The row already selected by this edit session"));
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        var effectiveWorkflow = RefreshDynamicEncounterOptions(
            project,
            OverlayPendingEdits(workflow, currentSession.PendingEdits));
        var encounter = effectiveWorkflow.Encounters.FirstOrDefault(candidate => candidate.EntryIndex == entryIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventure entry index {entryIndex} is not present in the loaded workflow.",
                field: "entryIndex",
                expected: "Existing Dynamax Adventure record"));
            return new SwShDynamaxAdventuresEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(
            encounter,
            field,
            value,
            effectiveWorkflow.Encounters,
            effectiveWorkflow.SafeNormalSpeciesOptions,
            diagnostics);
        if (pendingEdit is null)
        {
            return new SwShDynamaxAdventuresEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var sessionForUpdate = RemoveAutoDependentEditsForVanillaSpeciesRestore(
            currentSession,
            encounter,
            pendingEdit);
        var updatedSession = ReplacePendingEncounterEdits(
            sessionForUpdate,
            CreateRelatedPendingEdits(encounter, pendingEdit));

        return new SwShDynamaxAdventuresEditResult(
            RefreshDynamicEncounterOptions(
                project,
                OverlayPendingEdits(workflow, updatedSession.PendingEdits)),
            updatedSession,
            diagnostics);
    }

    public SwShDynamaxAdventuresEditResult StageRepair(
        ProjectPaths paths,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ValidatePendingSessionStructure(workflow, currentSession, diagnostics))
        {
            return new SwShDynamaxAdventuresEditResult(
                workflow,
                currentSession with { PendingEdits = [] },
                diagnostics);
        }

        if (!CanEditDynamaxAdventures(
                project,
                workflow,
                diagnostics,
                allowLegacyBossTargetCleanup: true))
        {
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Count != 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures executable repair must be staged in an empty edit session.",
                expected: "Apply or discard the current Dynamax Adventures row edits first"));
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        if (!string.Equals(workflow.InstallStatus, "repairable", StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures executable repair is available only when legacy owned state is detected as repairable.",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Repairable Dynamax Adventures executable state"));
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate =>
            candidate.IsEditable
            && candidate.LayoutWritableFields.Contains(
                SwShDynamaxAdventuresWorkflowService.LevelField,
                StringComparer.Ordinal));
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures executable repair could not find a safe layout-preserving row anchor.",
                field: SwShDynamaxAdventuresWorkflowService.LevelField,
                expected: "At least one editable row with stored level metadata"));
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        var repairEdit = new PendingEdit(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            RepairExecutableProjectionSummary,
            [new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile)],
            RecordId: SwShDynamaxAdventuresWorkflowService.CreateEncounterRecordId(encounter.EntryIndex),
            Field: SwShDynamaxAdventuresWorkflowService.LevelField,
            NewValue: encounter.Level.ToString(CultureInfo.InvariantCulture));
        var updatedSession = currentSession with { PendingEdits = [repairEdit] };

        diagnostics.Add(CreateDiagnostic(
            workflow.HasLegacyBossTargetPatch
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Info,
            workflow.HasLegacyBossTargetPatch
                ? "Staged destructive cleanup of unsupported legacy final-boss target remap code. The Adventure table values remain unchanged."
                : "Staged Dynamax Adventures executable repair without changing Adventure table values."));
        return new SwShDynamaxAdventuresEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShDynamaxAdventuresEditResult StageVanillaTableRestore(
        ProjectPaths paths,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ValidatePendingSessionRuntimeShape(currentSession, diagnostics))
        {
            return new SwShDynamaxAdventuresEditResult(
                workflow,
                SanitizeFailedVanillaTableRestoreRetry(currentSession),
                diagnostics);
        }

        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures vanilla-table restore requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return new SwShDynamaxAdventuresEditResult(
                workflow,
                SanitizeFailedVanillaTableRestoreRetry(currentSession),
                diagnostics);
        }

        if (currentSession.PendingEdits.Count != 0)
        {
            if (IsVanillaTableRestoreSession(project, workflow, currentSession))
            {
                var validation = Validate(paths, currentSession);
                if (validation.IsValid)
                {
                    diagnostics.AddRange(validation.Diagnostics);
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Info,
                        "Dynamax Adventures vanilla-table restore is already staged in this edit session."));
                    return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
                }

                diagnostics.AddRange(validation.Diagnostics);
                return new SwShDynamaxAdventuresEditResult(
                    workflow,
                    SanitizeFailedVanillaTableRestoreRetry(currentSession),
                    diagnostics);
            }

            if (currentSession.PendingEdits.Count != 1
                && currentSession.PendingEdits.Any(edit =>
                    edit.Summary is RepairExecutableProjectionSummary or RestoreVanillaTableSummary))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures recovery actions cannot be combined with Pokemon row edits.",
                    expected: "One canonical repair or vanilla-table restore action"));
                return new SwShDynamaxAdventuresEditResult(
                    workflow,
                    SanitizeFailedVanillaTableRestoreRetry(currentSession),
                    diagnostics);
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures vanilla-table restore must be staged in an empty edit session.",
                expected: "Apply or discard the current Dynamax Adventures row edits first"));
            return new SwShDynamaxAdventuresEditResult(
                workflow,
                SanitizeFailedVanillaTableRestoreRetry(currentSession),
                diagnostics);
        }

        if (!ValidatePendingSessionStructure(workflow, currentSession, diagnostics))
        {
            return new SwShDynamaxAdventuresEditResult(
                workflow,
                SanitizeFailedVanillaTableRestoreRetry(currentSession),
                diagnostics);
        }

        if (!workflow.CanRestoreVanillaTable)
        {
            foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(diagnostic);
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures vanilla-table restore is not available for this source state.",
                file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                expected: "A recoverable layered Adventure table with a verified base and non-conflicting executable"));
            return new SwShDynamaxAdventuresEditResult(
                workflow,
                SanitizeFailedVanillaTableRestoreRetry(currentSession),
                diagnostics);
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate =>
            candidate.Provenance.SourceLayer == ProjectFileLayer.Layered);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures vanilla-table restore could not find a canonical layered table-row anchor.",
                field: SwShDynamaxAdventuresWorkflowService.LevelField,
                expected: "First row from the layered Adventure table"));
            return new SwShDynamaxAdventuresEditResult(
                workflow,
                SanitizeFailedVanillaTableRestoreRetry(currentSession),
                diagnostics);
        }

        var restoreEdit = new PendingEdit(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            RestoreVanillaTableSummary,
            [new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile)],
            RecordId: SwShDynamaxAdventuresWorkflowService.CreateEncounterRecordId(encounter.EntryIndex),
            Field: SwShDynamaxAdventuresWorkflowService.LevelField,
            NewValue: encounter.Level.ToString(CultureInfo.InvariantCulture));
        var updatedSession = currentSession with { PendingEdits = [restoreEdit] };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Warning,
            workflow.HasLegacyBossTargetPatch
                ? "Staged removal of all layered Dynamax Adventures table changes and destructive cleanup of unsupported legacy final-boss target remap code. Review the plan before restoring the verified vanilla state."
                : "Staged removal of all layered Dynamax Adventures table changes. Review the plan before restoring the verified vanilla table."));
        return new SwShDynamaxAdventuresEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShDynamaxAdventureDefaultPreview PreviewDefaults(
        ProjectPaths paths,
        EditSession? session,
        int entryIndex,
        int species,
        int form,
        int level)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session is not null && !ValidatePendingSessionStructure(workflow, session, diagnostics))
        {
            return new SwShDynamaxAdventureDefaultPreview([], [], [], [], diagnostics);
        }

        if (!CanEditDynamaxAdventures(project, workflow, diagnostics))
        {
            return new SwShDynamaxAdventureDefaultPreview([], [], [], [], diagnostics);
        }

        var effectiveWorkflow = RefreshDynamicEncounterOptions(
            project,
            OverlayPendingEdits(workflow, session?.PendingEdits ?? []));
        var encounter = effectiveWorkflow.Encounters.FirstOrDefault(candidate => candidate.EntryIndex == entryIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventure entry index {entryIndex.ToString(CultureInfo.InvariantCulture)} is not present in the loaded workflow.",
                field: "entryIndex",
                expected: "Existing Dynamax Adventure record"));
            return new SwShDynamaxAdventureDefaultPreview([], [], [], [], diagnostics);
        }

        if (!encounter.IsEditable)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} is hidden from the safe Dynamax Adventures editor because this row class is not live-proven safe.",
                field: "entryIndex",
                expected: "A visible ordinary normal-route Dynamax Adventures row"));
            return new SwShDynamaxAdventureDefaultPreview([], [], [], [], diagnostics);
        }

        ValidateDefaultPreviewTarget(
            effectiveWorkflow,
            encounter,
            species,
            form,
            level,
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShDynamaxAdventureDefaultPreview([], [], [], [], diagnostics);
        }


        if (form != encounter.Form
            && !encounter.LayoutWritableFields.Contains(
                SwShDynamaxAdventuresWorkflowService.FormField,
                StringComparer.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures defaults cannot change this row's omitted form field without rebuilding the FlatBuffer layout.",
                field: SwShDynamaxAdventuresWorkflowService.FormField,
                expected: "A source row with stored form metadata"));
            return new SwShDynamaxAdventureDefaultPreview([], [], [], [], diagnostics);
        }

        var moveFields = Enumerable.Range(0, 4).Select(GetMoveField).ToArray();
        var omittedMoveField = moveFields.FirstOrDefault(field =>
            !encounter.LayoutWritableFields.Contains(field, StringComparer.Ordinal));
        if (omittedMoveField is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures defaults cannot populate a move slot stored as an omitted FlatBuffer default.",
                field: omittedMoveField,
                expected: "Four source-table move fields that can be updated in place"));
            return new SwShDynamaxAdventureDefaultPreview([], [], [], [], diagnostics);
        }

        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);
        var personalRecords = LoadPersonalRecords(project);
        var learnsetRecords = LoadLearnsetRecords(project);
        var allAbilityOptions = CreateAbilityOptions(
            SwShPokemonAbilityOptionResolver.Load(project),
            species,
            form);
        var abilityOptions = encounter.LayoutWritableFields.Contains(
            SwShDynamaxAdventuresWorkflowService.AbilityField,
            StringComparer.Ordinal)
                ? allAbilityOptions
                : allAbilityOptions.Where(option => option.Value == encounter.Ability).ToArray();
        var allGigantamaxOptions = SwShDynamaxAdventuresWorkflowService.CreateGigantamaxOptions(
            species,
            currentState: 1);
        var gigantamaxOptions = encounter.LayoutWritableFields.Contains(
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
            StringComparer.Ordinal)
                ? allGigantamaxOptions
                : encounter.GigantamaxState == 0
                    ? [new SwShDynamaxAdventureEditableFieldOption(0, "Unknown")]
                    : [];
        var defaultGigantamax = gigantamaxOptions.Any(option => option.Value == 1)
            ? 1
            : gigantamaxOptions.FirstOrDefault(option => option.Value == encounter.GigantamaxState)?.Value;
        if (!abilityOptions.Any(option => option.Value == 0) || defaultGigantamax is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures defaults could not map layout-writable ability and Gigantamax values for this row.",
                expected: "Stored or default values that are valid for the selected species and form"));
            return new SwShDynamaxAdventureDefaultPreview([], abilityOptions, gigantamaxOptions, [], diagnostics);
        }
        var targetPersonal = SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(
            species,
            form,
            personalRecords);
        if (usableMoveIds.Count == 0 || personalRecords.Count == 0 || learnsetRecords.Count == 0 || targetPersonal is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures could not build a legal default moveset because move, personal, or learnset data is unavailable.",
                field: SwShDynamaxAdventuresWorkflowService.Move0Field,
                expected: "Readable Sword/Shield move, personal, and learnset data"));
            return new SwShDynamaxAdventureDefaultPreview([], abilityOptions, gigantamaxOptions, [], diagnostics);
        }

        var targetEncounter = encounter with
        {
            SpeciesId = species,
            Species = GetOptionLabel(
                effectiveWorkflow,
                SwShDynamaxAdventuresWorkflowService.SpeciesField,
                species,
                "Species"),
            Form = form,
            Level = level,
            Ability = 0,
            GigantamaxState = defaultGigantamax.Value,
            Moves = [],
        };
        var moveOptions = CreateMoveOptions(
            effectiveWorkflow,
            targetEncounter,
            usableMoveIds,
            personalRecords,
            learnsetRecords);
        var targetLearnset = (uint)targetPersonal.PersonalId < (uint)learnsetRecords.Count
            ? learnsetRecords[targetPersonal.PersonalId]
            : null;
        var defaultMoveIds = CreateDefaultMoveIds(
            moveOptions,
            targetLearnset,
            usableMoveIds,
            level);
        if (defaultMoveIds.Count < 4)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{targetEncounter.Species} has fewer than four legal usable moves at level {level.ToString(CultureInfo.InvariantCulture)}.",
                field: SwShDynamaxAdventuresWorkflowService.Move0Field,
                expected: "At least four compatible usable moves"));
            return new SwShDynamaxAdventureDefaultPreview([], abilityOptions, gigantamaxOptions, moveOptions, diagnostics);
        }

        var changes = new List<SwShDynamaxAdventureDefaultField>
        {
            new(SwShDynamaxAdventuresWorkflowService.FormField, form.ToString(CultureInfo.InvariantCulture)),
            new(SwShDynamaxAdventuresWorkflowService.AbilityField, "0"),
            new(SwShDynamaxAdventuresWorkflowService.GigantamaxStateField, defaultGigantamax.Value.ToString(CultureInfo.InvariantCulture)),
        };
        for (var slot = 0; slot < 4; slot++)
        {
            changes.Add(new SwShDynamaxAdventureDefaultField(
                GetMoveField(slot),
                defaultMoveIds[slot].ToString(CultureInfo.InvariantCulture)));
        }

        return new SwShDynamaxAdventureDefaultPreview(changes, abilityOptions, gigantamaxOptions, moveOptions, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        var hasValidSessionStructure = ValidatePendingSessionStructure(workflow, session, diagnostics);
        if (!hasValidSessionStructure)
        {
            return new SwShEditSessionValidation(
                session with { PendingEdits = [] },
                IsValid: false,
                diagnostics);
        }

        var isVanillaTableRestore = IsVanillaTableRestoreSession(project, workflow, session);
        var isExecutableRepair = IsExecutableRepairSession(workflow, session);
        var canUseWorkflow = isVanillaTableRestore
            || CanEditDynamaxAdventures(
                project,
                workflow,
                diagnostics,
                allowLegacyBossTargetCleanup: isExecutableRepair);
        if (!canUseWorkflow)
        {
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (isVanillaTableRestore)
        {
            var source = SwShDynamaxAdventuresWorkflowService.ResolveDynamaxAdventureDataSource(project);
            var state = source is null
                ? null
                : CreateApplyState(paths, project, source, session, diagnostics);
            if (source is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures vanilla-table restore could not resolve the layered source table.",
                    file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                    expected: "Layered Adventure table and verified base table"));
            }
            else if (state is not null
                && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    "Pending Dynamax Adventures vanilla-table restore is valid and will discard all layered table changes."));
            }

            return new SwShEditSessionValidation(
                session,
                state is not null && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
                diagnostics);
        }

        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);
        var personalRecords = LoadPersonalRecords(project);
        var learnsetRecords = LoadLearnsetRecords(project);
        var effectiveWorkflow = RefreshDynamicEncounterOptions(
            project,
            OverlayPendingEdits(workflow, session.PendingEdits));
        foreach (var edit in session.PendingEdits)
        {
            ValidateDynamicPendingOption(effectiveWorkflow, edit, diagnostics);
        }
        ValidateDynamaxAdventureCompatibility(
            effectiveWorkflow,
            usableMoveIds,
            personalRecords,
            learnsetRecords,
            diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Dynamax Adventures change is valid."));
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
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();

        if (!validation.IsValid
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Dynamax Adventures edit before reviewing a change plan.",
                expected: "Pending Dynamax Adventures edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var source = SwShDynamaxAdventuresWorkflowService.ResolveDynamaxAdventureDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures change plan could not resolve the source table.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(paths, source.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures apply target must stay inside the configured output root.",
                file: source.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        var applyState = CreateApplyState(paths, project, source, session, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) || applyState is null)
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var tableRestoresVanilla = applyState.HasTableEdits && applyState.MatchesBaseBytes;
        if (applyState.HasTableEdits && !tableRestoresVanilla && !applyState.SourceLayoutMatchesBase)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures source table byte layout differs from the vanilla table. Restore the Adventure table from a clean dump before making new Pokemon edits.",
                file: source.GraphEntry.RelativePath,
                expected: "Vanilla byte layout, prior KM in-place edits, or a restore-to-vanilla change"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        if (applyState.HasTableEdits)
        {
            var planSources = IsRecoveryAction(session)
                ? CreateRecoveryPlanSources(project, session)
                : CreateCanonicalPlanSources(project, session, includeMain: true);
            var tableWrite = new PlannedFileWrite(
                source.GraphEntry.RelativePath,
                planSources,
                File.Exists(targetPath),
                tableRestoresVanilla && IsVanillaTableRestoreAction(session)
                    ? "Remove all layered Dynamax Adventures table changes and restore the verified vanilla table."
                    : tableRestoresVanilla
                    ? "Restore vanilla Dynamax Adventures Pokemon by removing the generated Adventure table."
                    : session.PendingEdits.Count == 1
                    ? $"Apply pending Dynamax Adventures edit: {session.PendingEdits[0].Summary}"
                    : $"Apply {session.PendingEdits.Count} pending Dynamax Adventures edits.");
            writes.Add(tableWrite);
        }

        var mainTargetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(
            paths,
            SwShExeFsPatchWorkflowService.ExeFsMainPath);
        var removeRedundantGeneratedMain = tableRestoresVanilla
            && ShouldRemoveRedundantGeneratedMain(paths, project);
        var shouldReconcileMain = applyState.MainAnalysis.Kind == SwShDynamaxAdventuresMainKind.Stale
            || removeRedundantGeneratedMain;
        if (shouldReconcileMain)
        {
            mainTargetPath = ResolveOutputPath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath, diagnostics);
            if (mainTargetPath is null)
            {
                return new ChangePlan(session.Id, [], diagnostics);
            }

            writes.Add(new PlannedFileWrite(
                SwShExeFsPatchWorkflowService.ExeFsMainPath,
                IsRecoveryAction(session)
                    ? CreateRecoveryPlanSources(project, session)
                    : CreateCanonicalPlanSources(project, session, includeMain: true),
                File.Exists(mainTargetPath),
                IsRecoveryAction(session) && applyState.MainAnalysis.HasLegacyBossTargetPatch
                    ? IsVanillaTableRestoreAction(session)
                        ? "Remove unsupported legacy final-boss target remap code while restoring the verified vanilla Dynamax Adventures state."
                        : "Remove unsupported legacy final-boss target remap code and restore the owned executable projection."
                    : removeRedundantGeneratedMain
                    ? "Remove redundant generated Dynamax Adventures exefs/main after restoring the vanilla table."
                    : applyState.MainAnalysis.RequiresSummaryMirror
                    || applyState.MainAnalysis.RequiresCommandValidatorPatch
                    ? "Synchronize Dynamax Adventures ExeFS mirrors with the final effective Adventure table."
                    : "Restore stale Dynamax Adventures-owned ExeFS mirror state while preserving other executable patches."));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Change plan preview contains {writes.Count:N0} target file(s).")));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics),
            preserveExplicitSourceLayers: true);
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
                "Reviewed Dynamax Adventures change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Dynamax Adventures change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var applyScope,
                out var acquireDiagnostics,
                preserveExplicitSourceLayers: true))
        {
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                acquireDiagnostics);
        }

        using var verifiedScope = applyScope!;
        var snapshotPlan = CreateChangePlan(verifiedScope.ApplyPaths, session);
        if (!verifiedScope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedPlan))
        {
            var staleDiagnostics = preparedPlan.Diagnostics.ToList();
            staleDiagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Dynamax Adventures change plan"));
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                staleDiagnostics);
        }

        var snapshotResult = ApplyPreparedPlan(
            verifiedScope.ApplyPaths,
            session,
            preparedPlan,
            applyId,
            appliedAt);
        return verifiedScope.Commit(snapshotResult);
    }

    private ApplyResult ApplyPreparedPlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan preparedPlan,
        string applyId,
        DateTimeOffset appliedAt)
    {
        projectWorkspaceService.ClearMemoryCache();
        var diagnostics = preparedPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        var project = projectWorkspaceService.Open(paths);
        var source = SwShDynamaxAdventuresWorkflowService.ResolveDynamaxAdventureDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures verified apply could not resolve the source table.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var state = CreateApplyState(paths, project, source, session, diagnostics);
        var tableTargetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
        if (state is null
            || tableTargetPath is null
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        try
        {
            if (state.HasTableEdits)
            {
                if (state.MatchesBaseBytes)
                {
                    if (File.Exists(tableTargetPath))
                    {
                        File.Delete(tableTargetPath);
                        writtenFiles.Add(new ProjectFileReference(
                            ProjectFileLayer.Generated,
                            source.GraphEntry.RelativePath));
                    }

                    if (File.Exists(tableTargetPath))
                    {
                        throw new IOException("Dynamax Adventures table cleanup verification found the generated file still present.");
                    }
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(tableTargetPath)!);
                    File.WriteAllBytes(tableTargetPath, state.OutputBytes);
                    writtenFiles.Add(new ProjectFileReference(
                        ProjectFileLayer.Generated,
                        source.GraphEntry.RelativePath));
                    var writtenTable = SwShDynamaxAdventuresWorkflowService.ReadBoundedDynamaxAdventureTable(tableTargetPath);
                    if (!writtenTable.SequenceEqual(state.OutputBytes))
                    {
                        throw new IOException("Dynamax Adventures table readback did not match the staged output bytes.");
                    }

                    _ = SwShDynamaxAdventureArchive.Parse(writtenTable);
                }
            }

            var removeRedundantGeneratedMain = state.MatchesBaseBytes
                && ShouldRemoveRedundantGeneratedMain(paths, project);
            if (state.MainAnalysis.Kind == SwShDynamaxAdventuresMainKind.Stale
                || removeRedundantGeneratedMain)
            {
                var mainSource = ResolveWorkflowFile(project, SwShExeFsPatchWorkflowService.ExeFsMainPath);
                var baseMainPath = ResolveBaseSourcePath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath);
                var mainTargetPath = ResolveOutputPath(
                    paths,
                    SwShExeFsPatchWorkflowService.ExeFsMainPath,
                    diagnostics);
                if (mainSource is null
                    || baseMainPath is null
                    || !File.Exists(baseMainPath)
                    || mainTargetPath is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Dynamax Adventures verified apply could not resolve base, effective, or target exefs/main.",
                        file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                        expected: "Verified selected-game executable sources and output target"));
                    return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
                }

                var baseMainBytes = File.ReadAllBytes(baseMainPath);
                var currentMainBytes = File.ReadAllBytes(mainSource.AbsolutePath);
                var reconciledMain = state.MainAnalysis.Kind == SwShDynamaxAdventuresMainKind.Stale
                    ? SwShDynamaxAdventuresMainPatcher.Reconcile(
                        currentMainBytes,
                        baseMainBytes,
                        state.FinalArchive,
                        state.BaseArchive,
                        paths.SelectedGame,
                        state.RecognizedMainSourceArchive)
                    : currentMainBytes;
                if (SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(reconciledMain, baseMainBytes))
                {
                    if (File.Exists(mainTargetPath))
                    {
                        File.Delete(mainTargetPath);
                        writtenFiles.Add(new ProjectFileReference(
                            ProjectFileLayer.Generated,
                            SwShExeFsPatchWorkflowService.ExeFsMainPath));
                    }
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(mainTargetPath)!);
                    File.WriteAllBytes(mainTargetPath, reconciledMain);
                    writtenFiles.Add(new ProjectFileReference(
                        ProjectFileLayer.Generated,
                        SwShExeFsPatchWorkflowService.ExeFsMainPath));
                    if (!File.ReadAllBytes(mainTargetPath).SequenceEqual(reconciledMain))
                    {
                        throw new IOException("Dynamax Adventures exefs/main readback did not match the staged output bytes.");
                    }
                }

                var verifiedMainBytes = File.Exists(mainTargetPath)
                    ? File.ReadAllBytes(mainTargetPath)
                    : baseMainBytes;
                var verifiedMainAnalysis = SwShDynamaxAdventuresMainPatcher.Analyze(
                    verifiedMainBytes,
                    baseMainBytes,
                    state.FinalArchive,
                    state.BaseArchive,
                    paths.SelectedGame,
                    state.RecognizedMainSourceArchive);
                if (verifiedMainAnalysis.Kind is not (
                    SwShDynamaxAdventuresMainKind.Vanilla
                    or SwShDynamaxAdventuresMainKind.Synchronized))
                {
                    throw new InvalidDataException(
                        "Dynamax Adventures staged exefs/main did not re-analyze as the final required semantic state.");
                }
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                state.HasTableEdits && state.MatchesBaseBytes
                    ? "Restored the verified vanilla Dynamax Adventures table and removed all layered Adventure-table changes atomically."
                    : "Applied the verified Dynamax Adventures table and executable projection atomically."));
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures verified output could not be prepared: {exception.Message}",
                expected: "Layout-preserving table edit and owned executable reconciliation"));
        }

        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
    }

    private static bool ShouldRemoveRedundantGeneratedMain(
        ProjectPaths paths,
        OpenedProject project)
    {
        try
        {
            var mainTargetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(
                paths,
                SwShExeFsPatchWorkflowService.ExeFsMainPath);
            var mainSource = ResolveWorkflowFile(project, SwShExeFsPatchWorkflowService.ExeFsMainPath);
            var baseMainPath = ResolveBaseSourcePath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath);
            return mainTargetPath is not null
                && mainSource is not null
                && baseMainPath is not null
                && File.Exists(mainTargetPath)
                && File.Exists(baseMainPath)
                && string.Equals(
                    Path.GetFullPath(mainSource.AbsolutePath),
                    Path.GetFullPath(mainTargetPath),
                    StringComparison.OrdinalIgnoreCase)
                && SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(
                    File.ReadAllBytes(mainSource.AbsolutePath),
                    File.ReadAllBytes(baseMainPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static PendingEdit? CreatePendingEdit(
        SwShDynamaxAdventureEntry encounter,
        string field,
        string value,
        IReadOnlyList<SwShDynamaxAdventureEntry> encounters,
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> safeNormalSpeciesOptions,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SwShDynamaxAdventuresWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (!encounter.IsEditable)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} is hidden from the safe Dynamax Adventures editor because boss, legendary, mythical, Ultra Beast, special, and unsupported rows are not live-proven safe to edit.",
                field: "entryIndex",
                expected: "A visible ordinary normal-route Dynamax Adventures row"));
            return null;
        }

        var parsedValue = TryParseFieldValue(editableField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (normalizedField == SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField
            && encounter.Ivs.Hp >= 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} stores a fixed HP IV of {encounter.Ivs.Hp.ToString(CultureInfo.InvariantCulture)}, which cannot be represented by the guaranteed-perfect-IV control.",
                field: normalizedField,
                expected: "Preserve the fixed HP IV"));
            return null;
        }

        if (!encounter.LayoutWritableFields.Contains(normalizedField, StringComparer.Ordinal)
            && GetEncounterFieldValue(encounter, normalizedField) != parsedValue.Value)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} stores {editableField.Label} as an omitted FlatBuffer default, so that field cannot be changed without rebuilding the table layout.",
                field: normalizedField,
                expected: "The existing default value for an omitted field"));
            return null;
        }

        if (normalizedField == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && !CanStageSafeNormalSpeciesValue(encounter, parsedValue.Value, safeNormalSpeciesOptions))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use species {parsedValue.Value.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: normalizedField,
                expected: "A species from the safe non-duplicate normal-route replacement list"));
            return null;
        }

        if (normalizedField == SwShDynamaxAdventuresWorkflowService.FormField
            && !CanStageSafeNormalFormValue(encounter, parsedValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use form {parsedValue.Value.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: normalizedField,
                expected: "Form 0 or the row's original vanilla form"));
            return null;
        }

        if (normalizedField is SwShDynamaxAdventuresWorkflowService.SpeciesField or SwShDynamaxAdventuresWorkflowService.FormField)
        {
            var targetSpecies = normalizedField == SwShDynamaxAdventuresWorkflowService.SpeciesField
                ? parsedValue.Value
                : encounter.SpeciesId;
            var targetForm = normalizedField == SwShDynamaxAdventuresWorkflowService.SpeciesField
                && parsedValue.Value != encounter.SpeciesId
                    ? 0
                    : normalizedField == SwShDynamaxAdventuresWorkflowService.FormField
                        ? parsedValue.Value
                        : encounter.Form;
            if (CreatesDuplicateNormalSpeciesForm(encounters, encounter, targetSpecies, targetForm))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} cannot use duplicate normal-route species/form {targetSpecies.ToString(CultureInfo.InvariantCulture)}/{targetForm.ToString(CultureInfo.InvariantCulture)}.",
                    field: normalizedField,
                    expected: "Unique species/form identities inside the normal Dynamax Adventures pool"));
                return null;
            }
        }

        if (normalizedField == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            && parsedValue.Value == SwShDynamaxAdventureArchive.MaximumGigantamaxState
            && !SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(encounter.SpeciesId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Species} cannot use the Dynamax Adventures Gigantamax state.",
                field: normalizedField,
                expected: "Gigantamax-capable species or Normal Gigantamax state"));
            return null;
        }

        if (normalizedField == SwShDynamaxAdventuresWorkflowService.AbilityField
            && (encounter.AbilityOptions.Count == 0
                || !encounter.AbilityOptions.Any(option => option.Value == parsedValue.Value)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use ability roll {parsedValue.Value.ToString(CultureInfo.InvariantCulture)} because no verified species/form ability option maps to it.",
                field: normalizedField,
                expected: "A nonempty verified ability option for the current species and form"));
            return null;
        }

        if (IsMoveField(normalizedField)
            && parsedValue.Value == 0
            && !CanStageVanillaZeroMoveSlot(encounter, normalizedField))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures requires all four move slots to be populated.",
                field: normalizedField,
                expected: "A usable nonzero move"));
            return null;
        }

        if (IsMoveField(normalizedField)
            && parsedValue.Value != 0
            && (encounter.MoveOptions.Count == 0
                || !encounter.MoveOptions.Any(option => option.Value == parsedValue.Value)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use move {parsedValue.Value.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: normalizedField,
                expected: "A usable move compatible with the row's current species/form and level"));
            return null;
        }

        AddLinkedUsageWarning(normalizedField, diagnostics);

        return new PendingEdit(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            FormatPendingSummary(encounter.EntryIndex, editableField.Label, parsedValue.Value),
            [new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile)],
            RecordId: SwShDynamaxAdventuresWorkflowService.CreateEncounterRecordId(encounter.EntryIndex),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShDynamaxAdventuresWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Dynamax Adventures workflow.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain));
            return;
        }

        var editableField = SwShDynamaxAdventuresWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit targets an invalid record.",
                field: "entryIndex",
                expected: "Dynamax Adventure record"));
            return;
        }

        var encounter = workflow.Encounters.FirstOrDefault(encounter => encounter.EntryIndex == entryIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit targets a record that is not loaded.",
                field: "entryIndex",
                expected: "Existing Dynamax Adventure record"));
            return;
        }

        var isRestoreOwner = IsVanillaTableRestoreOwner(workflow, encounter, edit);
        if (!encounter.IsEditable && !isRestoreOwner)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Dynamax Adventures edit targets {encounter.Label}, which is hidden from the safe editor because this row class is not live-proven safe.",
                field: "entryIndex",
                expected: "A visible ordinary normal-route Dynamax Adventures row"));
            return;
        }

        var parsedValue = TryParseFieldValue(editableField, edit.NewValue, diagnostics);
        if (parsedValue is not null
            && !string.Equals(
                edit.NewValue,
                parsedValue.Value.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit value is not in canonical integer form.",
                field: edit.Field,
                expected: parsedValue.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (parsedValue is not null)
        {
            var expectedSummary = FormatPendingSummary(encounter.EntryIndex, editableField.Label, parsedValue.Value);
            var expectedAutoSummary = FormatAutoPendingSummary(
                encounter.EntryIndex,
                editableField.Label,
                parsedValue.Value);
            var isRepairAction = IsRepairPendingEdit(workflow, encounter, edit, parsedValue.Value);
            var isRestoreAction = IsVanillaTableRestorePendingEdit(workflow, encounter, edit, parsedValue.Value);
            if (!string.Equals(edit.Summary, expectedSummary, StringComparison.Ordinal)
                && !string.Equals(edit.Summary, expectedAutoSummary, StringComparison.Ordinal)
                && !isRepairAction
                && !isRestoreAction)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Dynamax Adventures edit summary does not match its canonical action identity.",
                    field: edit.Field,
                    expected: expectedSummary));
            }


            if (!isRestoreAction
                && !encounter.LayoutWritableFields.Contains(editableField.Field, StringComparer.Ordinal)
                && GetEncounterFieldValue(encounter, editableField.Field) != parsedValue.Value)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Dynamax Adventures edit changes a field stored as an omitted FlatBuffer default.",
                    field: edit.Field,
                    expected: "The existing default value for an omitted field"));
            }


            if (editableField.Field == SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField
                && encounter.Ivs.Hp >= 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Dynamax Adventures edit cannot replace a fixed HP IV through the guaranteed-perfect-IV control.",
                    field: edit.Field,
                    expected: $"Preserve fixed HP IV {encounter.Ivs.Hp.ToString(CultureInfo.InvariantCulture)}"));
            }
        }

        if (edit.Sources is not { Count: 1 }
            || edit.Sources[0] is null
            || edit.Sources[0].Layer != encounter.Provenance.SourceLayer
            || !string.Equals(edit.Sources[0].RelativePath, encounter.Provenance.SourceFile, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit sources do not match the selected row's effective source.",
                field: edit.Field,
                expected: "Canonical effective Adventure table source"));
        }
        if (edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var species)
            && !CanStageSafeNormalSpeciesValue(encounter, species, workflow.SafeNormalSpeciesOptions))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use species {species.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: edit.Field,
                expected: "A species from the safe non-duplicate normal-route replacement list"));
        }

        AddLinkedUsageWarning(edit.Field, diagnostics);
    }

    private static bool ValidatePendingSessionRuntimeShape(
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures edit session is missing its pending action list.",
                expected: "A canonical pending action array"));
            return false;
        }

        foreach (var edit in session.PendingEdits)
        {
            if (edit is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures edit session contains a null pending action.",
                    expected: "Canonical pending actions"));
                continue;
            }

            if (edit.Sources is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures pending action is missing its source list.",
                    expected: "A canonical source array"));
            }
            else if (edit.Sources.Any(source => source is null))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures pending action contains a null source.",
                    expected: "Canonical source records"));
            }
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool ValidatePendingSessionStructure(
        SwShDynamaxAdventuresWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!ValidatePendingSessionRuntimeShape(session, diagnostics))
        {
            return false;
        }

        var distinctRecordIds = session.PendingEdits
            .Where(edit => edit is not null && string.Equals(
                edit.Domain,
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
                StringComparison.Ordinal))
            .Select(edit => edit.RecordId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (session.PendingEdits.Count(edit => edit is not null
                && edit.Summary is RepairExecutableProjectionSummary or RestoreVanillaTableSummary) is > 0
            && session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures recovery actions cannot be combined with Pokemon row edits.",
                expected: "One canonical repair or vanilla-table restore action"));
        }
        if (distinctRecordIds.Length > 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "A Dynamax Adventures change plan can target only one Pokemon row at a time.",
                field: "entryIndex",
                expected: "Multiple fields on one Dynamax Adventures row"));
        }

        foreach (var duplicate in session.PendingEdits
            .Where(edit => edit is not null)
            .GroupBy(edit => (edit.Domain, edit.RecordId, edit.Field))
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "A Dynamax Adventures edit session contains duplicate actions for the same row field.",
                field: duplicate.Key.Field,
                expected: "One canonical pending action per row field"));
        }

        foreach (var edit in session.PendingEdits)
        {
            if (edit is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures edit session contains a null pending action.",
                    expected: "Canonical pending actions"));
                continue;
            }

            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool IsRepairPendingEdit(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        PendingEdit edit,
        int value)
    {
        return string.Equals(edit.Summary, RepairExecutableProjectionSummary, StringComparison.Ordinal)
            && string.Equals(edit.Field, SwShDynamaxAdventuresWorkflowService.LevelField, StringComparison.Ordinal)
            && value == encounter.Level
            && encounter.LayoutWritableFields.Contains(
                SwShDynamaxAdventuresWorkflowService.LevelField,
                StringComparer.Ordinal);
    }

    private static void ValidateDynamicPendingOption(
        SwShDynamaxAdventuresWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return;
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EntryIndex == entryIndex);
        if (encounter is null)
        {
            return;
        }

        if (edit.Field == SwShDynamaxAdventuresWorkflowService.AbilityField
            && (encounter.AbilityOptions.Count == 0
                || !encounter.AbilityOptions.Any(option => option.Value == value)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures ability value is not in the verified option set for the final species and form.",
                field: edit.Field,
                expected: "Verified nonempty ability option"));
        }


        if (edit.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            && (encounter.GigantamaxOptions.Count == 0
                || !encounter.GigantamaxOptions.Any(option => option.Value == value)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures Gigantamax value is not in the verified option set for the final species and source-table layout.",
                field: edit.Field,
                expected: "Verified nonempty layout-writable Gigantamax option"));
        }

        if (IsMoveField(edit.Field ?? string.Empty)
            && value != 0
            && (encounter.MoveOptions.Count == 0
                || !encounter.MoveOptions.Any(option => option.Value == value)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures move value is not in the verified option set for the final species, form, and level.",
                field: edit.Field,
                expected: "Verified nonempty compatible move option"));
        }
    }

    private static string FormatPendingSummary(int entryIndex, string fieldLabel, int value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Set Dynamax Adventure row {entryIndex} {fieldLabel} to {value}.");
    }

    private static string FormatAutoPendingSummary(int entryIndex, string fieldLabel, int value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Automatically reset Dynamax Adventure row {entryIndex} {fieldLabel} to {value} after species change.");
    }

    private static bool CanStageSafeNormalSpeciesValue(
        SwShDynamaxAdventureEntry encounter,
        int species,
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> safeNormalSpeciesOptions)
    {
        return species == encounter.SpeciesId
            || species == encounter.VanillaPokemon?.SpeciesId
            || safeNormalSpeciesOptions.Any(option => option.Value == species);
    }

    private static bool CanStageSafeNormalFormValue(
        SwShDynamaxAdventureEntry encounter,
        int form)
    {
        return form == 0
            || form == encounter.Form
            || (encounter.SpeciesId == encounter.VanillaPokemon?.SpeciesId
                && form == encounter.VanillaPokemon.Form);
    }

    private static void ValidateDefaultPreviewTarget(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        int species,
        int form,
        int level,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!CanStageSafeNormalSpeciesValue(encounter, species, workflow.SafeNormalSpeciesOptions))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use species {species.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                expected: "A species from the safe non-duplicate normal-route replacement list"));
        }

        if (!CanStageSafeNormalFormValue(encounter, form))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use form {form.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: SwShDynamaxAdventuresWorkflowService.FormField,
                expected: "Form 0 or the row's original vanilla form"));
        }

        if (CreatesDuplicateNormalSpeciesForm(workflow.Encounters, encounter, species, form))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use duplicate normal-route species/form {species.ToString(CultureInfo.InvariantCulture)}/{form.ToString(CultureInfo.InvariantCulture)}.",
                field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                expected: "Unique species/form identities inside the normal Dynamax Adventures pool"));
        }

        if (level is < 1 or > 100)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot preview a default moveset at level {level.ToString(CultureInfo.InvariantCulture)}.",
                field: SwShDynamaxAdventuresWorkflowService.LevelField,
                expected: "Level 1 through 100"));
        }
    }

    private static IReadOnlyList<int> CreateDefaultMoveIds(
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> moveOptions,
        SwShPokemonLearnsetRecord? learnset,
        IReadOnlySet<int> usableMoveIds,
        int level)
    {
        var legalMoveIds = moveOptions
            .Select(option => option.Value)
            .Where(moveId => moveId > 0)
            .ToHashSet();
        var selectedMoveIds = new List<int>();

        if (learnset is not null)
        {
            foreach (var move in learnset.Moves
                .Where(move =>
                    move.MoveId > 0
                    && move.Level <= level
                    && usableMoveIds.Contains(move.MoveId)
                    && legalMoveIds.Contains(move.MoveId))
                .OrderByDescending(move => move.Level)
                .ThenByDescending(move => move.Slot))
            {
                AddDefaultMoveId(selectedMoveIds, move.MoveId);
            }
        }

        foreach (var option in moveOptions.Where(option => option.Value > 0))
        {
            AddDefaultMoveId(selectedMoveIds, option.Value);
        }

        return selectedMoveIds.Take(4).ToArray();
    }

    private static void AddDefaultMoveId(ICollection<int> selectedMoveIds, int moveId)
    {
        if (!selectedMoveIds.Contains(moveId))
        {
            selectedMoveIds.Add(moveId);
        }
    }

    private static bool CreatesDuplicateNormalSpeciesForm(
        IReadOnlyList<SwShDynamaxAdventureEntry> encounters,
        SwShDynamaxAdventureEntry target,
        int species,
        int form)
    {
        if (!IsNormalEncounter(target))
        {
            return false;
        }

        return encounters.Any(encounter =>
            encounter.EntryIndex != target.EntryIndex
            && IsNormalEncounter(encounter)
            && encounter.SpeciesId == species
            && encounter.Form == form);
    }

    private static void ValidateDynamaxAdventureCompatibility(
        SwShDynamaxAdventuresWorkflow workflow,
        IReadOnlySet<int> usableMoveIds,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        IReadOnlyList<SwShPokemonLearnsetRecord> learnsetRecords,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (personalRecords.Count == 0 || usableMoveIds.Count == 0 || learnsetRecords.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures validation requires readable personal, move, and learnset data; missing dependency data never disables safety checks.",
                expected: "Readable Sword/Shield personal, move, and learnset data"));
            return;
        }

        var vanillaBossSpeciesForms = GetVanillaBossSpeciesForms(workflow);
        ValidateDistinctSpeciesForms(workflow.Encounters.Where(IsNormalEncounter), "normal route", diagnostics);
        ValidateDistinctSpeciesForms(workflow.Encounters.Where(IsBossEncounter), "boss", diagnostics);

        foreach (var encounter in workflow.Encounters)
        {
            var personalRecord = SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(encounter.SpeciesId, encounter.Form, personalRecords);
            var learnsetRecord = personalRecord is not null && (uint)personalRecord.PersonalId < (uint)learnsetRecords.Count
                ? learnsetRecords[personalRecord.PersonalId]
                : null;
            if (!encounter.IsEditable && HasAnyPokemonChangeFromVanilla(encounter))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} is outside the live-proven editable row class and must remain base-identical.",
                    field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                    expected: "Verified base values for every hidden normal and boss row"));
            }
            if (personalRecord?.IsPresentInGame != true)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses species {encounter.SpeciesId.ToString(CultureInfo.InvariantCulture)}, which is not marked present in Sword/Shield personal data.",
                    field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                    expected: "Species present in Sword/Shield personal data"));
            }

            if (personalRecord?.CanNotDynamax == true)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses a species/form that personal data marks as unable to Dynamax.",
                    field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                    expected: "Species/form allowed to Dynamax"));
            }

            if (encounter.Level is < 1 or > 100)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses level {encounter.Level.ToString(CultureInfo.InvariantCulture)}. Dynamax Adventures Pokemon should stay in the normal battle level range.",
                    field: SwShDynamaxAdventuresWorkflowService.LevelField,
                    expected: "Level 1 through 100"));
            }

            if (IsNormalEncounter(encounter))
            {
                if (IntroducesNonVanillaNormalForm(encounter))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} introduces form {encounter.Form.ToString(CultureInfo.InvariantCulture)} in a normal route row. Only form 0 normal-route replacements are currently live-proven safe.",
                        field: SwShDynamaxAdventuresWorkflowService.FormField,
                        expected: "Form 0, or the row's original vanilla form"));
                }

                if (IntroducesNormalRouteIdentity(encounter)
                    && (encounter.SpeciesId <= 0
                        || encounter.SpeciesId > SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses species {encounter.SpeciesId.ToString(CultureInfo.InvariantCulture)} outside the verified Dynamax Adventures normal-route replacement range.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: $"Species 1 through {SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies.ToString(CultureInfo.InvariantCulture)}"));
                }

                if (personalRecord is not null
                    && personalRecord.Form != 0
                    && IntroducesNormalRouteIdentity(encounter))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses a non-base personal form as a normal route replacement. Only base personal-form replacements are currently in the verified safe list.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Base personal form for normal route rows"));
                }

                if (vanillaBossSpeciesForms.Contains((encounter.SpeciesId, encounter.Form)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses a vanilla Dynamax Adventures boss species/form in a normal route row. Live testing returned to the lobby for this category.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Non-boss species/form for normal route rows"));
                }

                if (SwShDynamaxAdventureSafetyRules.IsSpecialNormalRouteSpecies(encounter.SpeciesId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses a legendary, mythical, Ultra Beast, or special boss-category species in a normal route row. Live testing returned to the lobby for this category.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Ordinary non-legendary species for normal route rows"));
                }

                if (encounter.Form != 0 && SwShDynamaxAdventureSafetyRules.IsBattleFusionSpecies(encounter.SpeciesId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses a battle-fusion form in a normal route row. Live testing returned to the lobby for this category.",
                        field: SwShDynamaxAdventuresWorkflowService.FormField,
                        expected: "Non-fusion normal route identity"));
                }
            }
            else
            {
                if (vanillaBossSpeciesForms.Count > 0 && !vanillaBossSpeciesForms.Contains((encounter.SpeciesId, encounter.Form)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} changes a boss row to a species/form that is not part of the vanilla Dynamax Adventures boss roster. Keep boss edits inside the vanilla boss pool until boss metadata is fully mapped.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Species/form from the vanilla Dynamax Adventures boss roster"));
                }

                if (encounter.VanillaPokemon is not null
                    && (encounter.SpeciesId != encounter.VanillaPokemon.SpeciesId
                        || encounter.Form != encounter.VanillaPokemon.Form))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} changes a boss identity without moving its capture flag and message metadata. Boss replacements need a dedicated metadata-copy operation before they are safe in the editor.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Vanilla boss identity until boss metadata-copy support is implemented"));
                }

                if (HasUnprovenBossRuntimeFieldChange(encounter))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} changes boss runtime fields before boss metadata-copy support has live final-boss proof.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Vanilla boss runtime fields until the dedicated boss row-copy operation is implemented"));
                }
            }

            if (encounter.GigantamaxState == SwShDynamaxAdventureArchive.MaximumGigantamaxState
                && !SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(encounter.SpeciesId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} is marked Gigantamax, but {encounter.Species} does not have a Gigantamax form.",
                    field: SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
                    expected: "Gigantamax-capable species or Normal Gigantamax state"));
            }

            if (encounter.Moves.Count < 4 || encounter.Moves.Any(move => move.MoveId == 0))
            {
                if (!PreservesVanillaZeroMoveSlots(encounter))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} introduces empty Dynamax Adventure move slots. Vanilla Ditto keeps its omitted slots, but edited Adventure Pokemon should not add new zero moves.",
                        field: SwShDynamaxAdventuresWorkflowService.Move0Field,
                        expected: "Four nonzero Adventure moves, or preserved vanilla zero slots"));
                }
            }

            foreach (var move in encounter.Moves.Where(move => move.MoveId != 0 && !usableMoveIds.Contains(move.MoveId)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses move {move.MoveId.ToString(CultureInfo.InvariantCulture)} ({move.Move}), but that move is not marked usable in Sword/Shield move data.",
                    field: GetMoveField(move.Slot - 1),
                    expected: "Move with CanUseMove enabled"));
            }

            if (personalRecord is not null)
            {
                foreach (var move in encounter.Moves.Where(move => move.MoveId != 0))
                {
                    if (SwShDynamaxAdventureSafetyRules.CanLearnMove(personalRecord, learnsetRecord, move.MoveId, encounter.Level)
                        || PreservesVanillaMoveCompatibilityException(encounter, move))
                    {
                        continue;
                    }

                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses move {move.MoveId.ToString(CultureInfo.InvariantCulture)} ({move.Move}), but {encounter.Species} cannot normally learn it from Sword/Shield level-up, TM, TR, or tutor data.",
                        field: GetMoveField(move.Slot - 1),
                        expected: "A move compatible with that species/form, or the row's original vanilla move exception"));
                }
            }
        }
    }

    private static bool PreservesVanillaMoveCompatibilityException(
        SwShDynamaxAdventureEntry encounter,
        SwShDynamaxAdventureMoveRecord move)
    {
        var vanilla = encounter.VanillaPokemon;
        var slotIndex = move.Slot - 1;
        return vanilla is not null
            && encounter.SpeciesId == vanilla.SpeciesId
            && encounter.Form == vanilla.Form
            && encounter.Level >= vanilla.Level
            && (uint)slotIndex < (uint)vanilla.Moves.Count
            && vanilla.Moves[slotIndex].MoveId == move.MoveId;
    }

    private static bool PreservesVanillaZeroMoveSlots(SwShDynamaxAdventureEntry encounter)
    {
        if (encounter.VanillaPokemon is null
            || encounter.SpeciesId != encounter.VanillaPokemon.SpeciesId
            || encounter.Form != encounter.VanillaPokemon.Form
            || encounter.Moves.Count != encounter.VanillaPokemon.Moves.Count)
        {
            return false;
        }

        for (var index = 0; index < encounter.Moves.Count; index++)
        {
            if (encounter.Moves[index].MoveId == 0 && encounter.VanillaPokemon.Moves[index].MoveId != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanStageVanillaZeroMoveSlot(
        SwShDynamaxAdventureEntry encounter,
        string field)
    {
        var slot = GetMoveSlot(field);
        var vanilla = encounter.VanillaPokemon;
        return vanilla is not null
            && encounter.SpeciesId == vanilla.SpeciesId
            && encounter.Form == vanilla.Form
            && (uint)slot < (uint)vanilla.Moves.Count
            && vanilla.Moves[slot].MoveId == 0;
    }

    private static bool HasUnprovenBossRuntimeFieldChange(SwShDynamaxAdventureEntry encounter)
    {
        var vanilla = encounter.VanillaPokemon;
        return vanilla is not null
            && (encounter.GigantamaxState != vanilla.GigantamaxState
                || encounter.Level != vanilla.Level
                || encounter.Ability != vanilla.Ability
                || encounter.GuaranteedPerfectIvs != vanilla.GuaranteedPerfectIvs
                || encounter.Ivs != vanilla.Ivs
                || !MoveIdsEqual(encounter.Moves, vanilla.Moves));
    }

    private static bool MoveIdsEqual(
        IReadOnlyList<SwShDynamaxAdventureMoveRecord> left,
        IReadOnlyList<SwShDynamaxAdventureMoveRecord> right)
    {
        return left.Count == right.Count
            && left.Select(move => move.MoveId).SequenceEqual(right.Select(move => move.MoveId));
    }

    private static bool IntroducesNonVanillaNormalForm(SwShDynamaxAdventureEntry encounter)
    {
        return encounter.Form != 0
            && IntroducesNormalRouteIdentity(encounter);
    }

    private static bool IntroducesNormalRouteIdentity(SwShDynamaxAdventureEntry encounter)
    {
        return encounter.VanillaPokemon is null
            || encounter.SpeciesId != encounter.VanillaPokemon.SpeciesId
            || encounter.Form != encounter.VanillaPokemon.Form;
    }

    private static IReadOnlyList<SwShPersonalRecord> LoadPersonalRecords(OpenedProject project)
    {
        var source = ResolveWorkflowFile(project, SwShPersonalTable.PersonalDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SwShPokemonLearnsetRecord> LoadLearnsetRecords(OpenedProject project)
    {
        var source = ResolveWorkflowFile(project, SwShPokemonLearnsetTable.LearnsetDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShPokemonLearnsetTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlySet<(int Species, int Form)> GetVanillaBossSpeciesForms(SwShDynamaxAdventuresWorkflow workflow)
    {
        return workflow.Encounters
            .Where(IsBossEncounter)
            .Select(encounter => encounter.VanillaPokemon is null
                ? (encounter.SpeciesId, encounter.Form)
                : (encounter.VanillaPokemon.SpeciesId, encounter.VanillaPokemon.Form))
            .ToHashSet();
    }

    private static void ValidateDistinctSpeciesForms(
        IEnumerable<SwShDynamaxAdventureEntry> encounters,
        string poolName,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var duplicateGroup in encounters
            .GroupBy(encounter => (encounter.SpeciesId, encounter.Form))
            .Where(group => group.Count() > 1))
        {
            var rows = string.Join(
                ", ",
                duplicateGroup.Select(encounter => encounter.EntryIndex.ToString(CultureInfo.InvariantCulture)));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures {poolName} pool contains duplicate species/form {duplicateGroup.Key.SpeciesId}/{duplicateGroup.Key.Form} on rows {rows}. Live stress testing showed collapsed or duplicate pools can return to the lobby or freeze.",
                field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                expected: "Unique species/form identities inside each Dynamax Adventures pool"));
        }
    }

    private static bool IsNormalEncounter(SwShDynamaxAdventureEntry encounter)
    {
        return SwShDynamaxAdventureSafetyRules.IsNormalEntryIndex(encounter.EntryIndex);
    }

    private static bool IsBossEncounter(SwShDynamaxAdventureEntry encounter)
    {
        return SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(encounter.EntryIndex);
    }

    private static int? TryParseFieldValue(
        SwShDynamaxAdventureEditableField editableField,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
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

        if ((editableField.MinimumValue is not null && parsedValue < editableField.MinimumValue.Value)
            || (editableField.MaximumValue is not null && parsedValue > editableField.MaximumValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: "Supported Dynamax Adventures field value"));
            return null;
        }

        if (editableField.Field == SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField
            && parsedValue == 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Guaranteed perfect IVs supports Random IVs or 2-6 guaranteed perfect IVs; 1 is not representable in this table.",
                field: editableField.Field,
                expected: "0, 2, 3, 4, 5, or 6"));
            return null;
        }

        return parsedValue;
    }

    private static bool IsMoveField(string field)
    {
        return field is
            SwShDynamaxAdventuresWorkflowService.Move0Field
            or SwShDynamaxAdventuresWorkflowService.Move1Field
            or SwShDynamaxAdventuresWorkflowService.Move2Field
            or SwShDynamaxAdventuresWorkflowService.Move3Field;
    }

    private static int GetMoveSlot(string field)
    {
        return field switch
        {
            SwShDynamaxAdventuresWorkflowService.Move0Field => 0,
            SwShDynamaxAdventuresWorkflowService.Move1Field => 1,
            SwShDynamaxAdventuresWorkflowService.Move2Field => 2,
            SwShDynamaxAdventuresWorkflowService.Move3Field => 3,
            _ => -1,
        };
    }

    private static void AddLinkedUsageWarning(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShDynamaxAdventuresWorkflowService.SpeciesField
            or SwShDynamaxAdventuresWorkflowService.FormField
            or SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            or SwShDynamaxAdventuresWorkflowService.Move0Field
            or SwShDynamaxAdventuresWorkflowService.Move1Field
            or SwShDynamaxAdventuresWorkflowService.Move2Field
            or SwShDynamaxAdventuresWorkflowService.Move3Field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Dynamax Adventures Pokemon edits only affect routes that select the edited row; identity edits also require the matching ExeFS mirror patch.",
                field: field,
                expected: "Generated Adventure table, matching route seed, and ExeFS mirrors for identity edits"));
        }
    }

    private static bool CanEditDynamaxAdventures(
        OpenedProject project,
        SwShDynamaxAdventuresWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics,
        bool allowLegacyBossTargetCleanup = false)
    {
        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.HasLegacyBossTargetPatch && !allowLegacyBossTargetCleanup)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Ordinary Dynamax Adventures edits are blocked while unsupported legacy final-boss target remap code is installed. Use Stage Repair or a full vanilla table restore to remove it explicitly.",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Vanilla final-boss target call sites"));
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        if (workflow.Summary.Availability != SwShWorkflowAvailability.Available
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures edit sessions require an available editable workflow.",
                expected: "Dynamax Adventures workflow without blocking diagnostics"));
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool IsVanillaTableRestoreSession(
        OpenedProject project,
        SwShDynamaxAdventuresWorkflow workflow,
        EditSession session)
    {
        if (!project.Health.CanOpenEditableWorkflows
            || !workflow.CanRestoreVanillaTable
            || session.PendingEdits is not { Count: 1 })
        {
            return false;
        }

        var edit = session.PendingEdits[0];
        if (edit is null
            || !SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EntryIndex == entryIndex);
        return encounter is not null
            && IsVanillaTableRestorePendingEdit(workflow, encounter, edit, value);
    }

    private static bool IsExecutableRepairSession(
        SwShDynamaxAdventuresWorkflow workflow,
        EditSession session)
    {
        if (!string.Equals(workflow.InstallStatus, "repairable", StringComparison.Ordinal)
            || session.PendingEdits is not { Count: 1 })
        {
            return false;
        }

        var edit = session.PendingEdits[0];
        if (edit is null
            || !SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EntryIndex == entryIndex);
        return encounter is not null
            && IsRepairPendingEdit(workflow, encounter, edit, value);
    }

    private static EditSession SanitizeFailedVanillaTableRestoreRetry(EditSession session)
    {
        return session with { PendingEdits = [] };
    }

    private static bool IsVanillaTableRestorePendingEdit(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        PendingEdit edit,
        int value)
    {
        return IsVanillaTableRestoreOwner(workflow, encounter, edit)
            && value == encounter.Level;
    }

    private static bool IsVanillaTableRestoreOwner(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        PendingEdit edit)
    {
        var firstLayeredEntry = workflow.Encounters.FirstOrDefault(candidate =>
            candidate.Provenance.SourceLayer == ProjectFileLayer.Layered);
        return string.Equals(edit.Summary, RestoreVanillaTableSummary, StringComparison.Ordinal)
            && string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal)
            && workflow.CanRestoreVanillaTable
            && firstLayeredEntry?.EntryIndex == encounter.EntryIndex
            && string.Equals(edit.Field, SwShDynamaxAdventuresWorkflowService.LevelField, StringComparison.Ordinal);
    }

    private static IReadOnlyList<PendingEdit> CreateRelatedPendingEdits(
        SwShDynamaxAdventureEntry encounter,
        PendingEdit pendingEdit)
    {
        if (pendingEdit.Field != SwShDynamaxAdventuresWorkflowService.SpeciesField)
        {
            return [pendingEdit];
        }

        var relatedEdits = new List<PendingEdit> { pendingEdit };
        if (!int.TryParse(pendingEdit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var speciesId))
        {
            return relatedEdits;
        }

        if (encounter.Form != 0 && speciesId != encounter.SpeciesId)
        {
            var formField = SwShDynamaxAdventuresWorkflowService.GetEditableField(
                SwShDynamaxAdventuresWorkflowService.FormField)!;
            relatedEdits.Add(new PendingEdit(
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
                FormatAutoPendingSummary(encounter.EntryIndex, formField.Label, 0),
                pendingEdit.Sources,
                RecordId: pendingEdit.RecordId,
                Field: SwShDynamaxAdventuresWorkflowService.FormField,
                NewValue: "0"));
        }

        if (encounter.GigantamaxState == SwShDynamaxAdventureArchive.MaximumGigantamaxState
            && !SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(speciesId))
        {
            var gigantamaxField = SwShDynamaxAdventuresWorkflowService.GetEditableField(
                SwShDynamaxAdventuresWorkflowService.GigantamaxStateField)!;
            var resetGigantamax = new PendingEdit(
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
                FormatAutoPendingSummary(encounter.EntryIndex, gigantamaxField.Label, 1),
                pendingEdit.Sources,
                RecordId: pendingEdit.RecordId,
                Field: SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
                NewValue: "1");

            relatedEdits.Add(resetGigantamax);
        }

        return relatedEdits;
    }

    private static EditSession RemoveAutoDependentEditsForVanillaSpeciesRestore(
        EditSession session,
        SwShDynamaxAdventureEntry encounter,
        PendingEdit pendingEdit)
    {
        if (pendingEdit.Field != SwShDynamaxAdventuresWorkflowService.SpeciesField
            || encounter.VanillaPokemon is null
            || !int.TryParse(pendingEdit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var speciesId)
            || speciesId != encounter.VanillaPokemon.SpeciesId)
        {
            return session;
        }

        var restored = session.PendingEdits.Where(edit =>
        {
            if (!string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal)
                || !string.Equals(edit.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
                || edit.Field is not (SwShDynamaxAdventuresWorkflowService.FormField
                    or SwShDynamaxAdventuresWorkflowService.GigantamaxStateField)
                || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var dependentValue))
            {
                return true;
            }

            var dependentField = SwShDynamaxAdventuresWorkflowService.GetEditableField(edit.Field);
            return dependentField is null
                || !string.Equals(
                    edit.Summary,
                    FormatAutoPendingSummary(encounter.EntryIndex, dependentField.Label, dependentValue),
                    StringComparison.Ordinal);
        }).ToArray();

        return session with { PendingEdits = restored };
    }

    private static EditSession ReplacePendingEncounterEdits(
        EditSession session,
        IReadOnlyList<PendingEdit> pendingEdits)
    {
        var updatedPendingEdits = session.PendingEdits
            .Where(edit => pendingEdits.All(pendingEdit => !IsSameEncounterEdit(edit, pendingEdit)))
            .Concat(pendingEdits)
            .ToArray();

        return session with { PendingEdits = updatedPendingEdits };
    }

    private static bool IsSameEncounterEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShDynamaxAdventuresWorkflow OverlayPendingEdits(
        SwShDynamaxAdventuresWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow with
        {
            Stats = updatedWorkflow.Stats with
            {
                TotalEncounterCount = updatedWorkflow.Encounters.Count,
                SingleCaptureCount = updatedWorkflow.Encounters.Count(encounter => encounter.IsSingleCapture),
                StoryGatedCount = updatedWorkflow.Encounters.Count(encounter => encounter.IsStoryProgressGated),
                GuaranteedPerfectIvEncounterCount = updatedWorkflow.Encounters.Count(encounter => encounter.GuaranteedPerfectIvs > 0),
            },
        };
    }

    private static SwShDynamaxAdventuresWorkflow RefreshDynamicEncounterOptions(
        OpenedProject project,
        SwShDynamaxAdventuresWorkflow workflow)
    {
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);
        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);
        var personalRecords = LoadPersonalRecords(project);
        var learnsetRecords = LoadLearnsetRecords(project);

        return workflow with
        {
            Encounters = workflow.Encounters
                .Select(encounter => RefreshEncounterOptions(
                    workflow,
                    encounter,
                    abilityResolver,
                    usableMoveIds,
                    personalRecords,
                    learnsetRecords))
                .ToArray(),
        };
    }

    private static SwShDynamaxAdventureEntry RefreshEncounterOptions(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        SwShPokemonAbilityOptionResolver abilityResolver,
        IReadOnlySet<int> usableMoveIds,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        IReadOnlyList<SwShPokemonLearnsetRecord> learnsetRecords)
    {
        var allAbilityOptions = CreateAbilityOptions(abilityResolver, encounter.SpeciesId, encounter.Form);
        var abilityOptions = SwShDynamaxAdventuresWorkflowService.FilterAbilityOptionsForLayout(
            allAbilityOptions,
            encounter.LayoutWritableFields.Contains(
                SwShDynamaxAdventuresWorkflowService.AbilityField,
                StringComparer.Ordinal)
                ? allAbilityOptions
                : [new SwShDynamaxAdventureEditableFieldOption(0, string.Empty)]);
        var allGigantamaxOptions = SwShDynamaxAdventuresWorkflowService.CreateGigantamaxOptions(
            encounter.SpeciesId,
            encounter.GigantamaxState);
        var gigantamaxOptions = encounter.LayoutWritableFields.Contains(
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
            StringComparer.Ordinal)
                ? allGigantamaxOptions
                : allGigantamaxOptions.Where(option => option.Value == 0).ToArray();
        return encounter with
        {
            AbilityLabel = allAbilityOptions.FirstOrDefault(option => option.Value == encounter.Ability)?.Label
                ?? GetOptionLabel(workflow, SwShDynamaxAdventuresWorkflowService.AbilityField, encounter.Ability, "Ability roll"),
            AbilityOptions = abilityOptions,
            GigantamaxOptions = gigantamaxOptions,
            MoveOptions = CreateMoveOptions(workflow, encounter, usableMoveIds, personalRecords, learnsetRecords),
        };
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateAbilityOptions(
        SwShPokemonAbilityOptionResolver abilityResolver,
        int speciesId,
        int form)
    {
        return abilityResolver
            .CreateOptions(speciesId, form, SwShAbilityOptionMode.ZeroBasedSlots)
            .Select(option => new SwShDynamaxAdventureEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateMoveOptions(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        IReadOnlySet<int> usableMoveIds,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        IReadOnlyList<SwShPokemonLearnsetRecord> learnsetRecords)
    {
        if (usableMoveIds.Count == 0 || personalRecords.Count == 0 || learnsetRecords.Count == 0)
        {
            return encounter.MoveOptions;
        }

        var personal = SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(
            encounter.SpeciesId,
            encounter.Form,
            personalRecords);
        if (personal is null)
        {
            return encounter.MoveOptions;
        }

        var learnset = (uint)personal.PersonalId < (uint)learnsetRecords.Count
            ? learnsetRecords[personal.PersonalId]
            : null;
        var moveIds = new SortedSet<int>();
        foreach (var moveId in usableMoveIds)
        {
            if (moveId <= SwShDynamaxAdventuresWorkflowService.MaximumSwordShieldMoveId
                && SwShDynamaxAdventureSafetyRules.CanLearnMove(personal, learnset, moveId, encounter.Level))
            {
                moveIds.Add(moveId);
            }
        }

        foreach (var move in encounter.Moves)
        {
            moveIds.Add(move.MoveId);
        }

        var vanilla = encounter.VanillaPokemon;
        if (vanilla is not null
            && vanilla.SpeciesId == encounter.SpeciesId
            && vanilla.Form == encounter.Form
            && encounter.Level >= vanilla.Level)
        {
            foreach (var move in vanilla.Moves)
            {
                moveIds.Add(move.MoveId);
            }
        }

        return moveIds
            .Select(moveId => new SwShDynamaxAdventureEditableFieldOption(
                moveId,
                GetMoveOptionLabel(workflow, encounter, moveId)))
            .ToArray();
    }

    private static string GetMoveOptionLabel(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        int moveId)
    {
        var globalMoveOption = workflow.EditableFields
            .FirstOrDefault(field => field.Field == SwShDynamaxAdventuresWorkflowService.Move0Field)
            ?.Options
            .FirstOrDefault(option => option.Value == moveId);
        if (globalMoveOption is not null)
        {
            return globalMoveOption.Label;
        }

        var currentMove = encounter.Moves.FirstOrDefault(move => move.MoveId == moveId);
        if (currentMove is not null && !string.IsNullOrWhiteSpace(currentMove.Move))
        {
            var prefix = moveId.ToString("000", CultureInfo.InvariantCulture);
            return currentMove.Move.StartsWith(prefix, StringComparison.Ordinal)
                ? currentMove.Move
                : $"{prefix} {currentMove.Move}";
        }

        return moveId == 0
            ? "000 None"
            : $"{moveId.ToString("000", CultureInfo.InvariantCulture)} Move {moveId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static SwShDynamaxAdventuresWorkflow OverlayPendingEdit(
        SwShDynamaxAdventuresWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal)
            || !SwShDynamaxAdventuresWorkflowService.IsEditableField(edit.Field)
            || !SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Encounters = workflow.Encounters
                .Select(encounter => encounter.EntryIndex == entryIndex
                    ? OverlayEncounterField(workflow, encounter, edit.Field!, value)
                    : encounter)
                .ToArray(),
        };
    }

    private static SwShDynamaxAdventureEntry OverlayEncounterField(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        string field,
        int value)
    {
        var updatedEncounter = field switch
        {
            SwShDynamaxAdventuresWorkflowService.SpeciesField => encounter with
            {
                SpeciesId = value,
                Species = GetSpeciesLabel(workflow, encounter, value),
            },
            SwShDynamaxAdventuresWorkflowService.FormField => encounter with { Form = value },
            SwShDynamaxAdventuresWorkflowService.LevelField => encounter with { Level = value },
            SwShDynamaxAdventuresWorkflowService.BallItemIdField => encounter with
            {
                BallItemId = value,
                BallItem = GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShDynamaxAdventuresWorkflowService.AbilityField => encounter with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, field, value, "Ability roll"),
            },
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField => encounter with
            {
                GigantamaxState = value,
                GigantamaxLabel = GetOptionLabel(workflow, field, value, "Gigantamax"),
            },
            SwShDynamaxAdventuresWorkflowService.VersionField => encounter with
            {
                Version = value,
                VersionLabel = GetOptionLabel(workflow, field, value, "Version"),
            },
            SwShDynamaxAdventuresWorkflowService.ShinyRollField => encounter with
            {
                ShinyRoll = value,
                ShinyRollLabel = GetOptionLabel(workflow, field, value, "Shiny roll"),
            },
            SwShDynamaxAdventuresWorkflowService.Move0Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 0, value) },
            SwShDynamaxAdventuresWorkflowService.Move1Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 1, value) },
            SwShDynamaxAdventuresWorkflowService.Move2Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 2, value) },
            SwShDynamaxAdventuresWorkflowService.Move3Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 3, value) },
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField => encounter with
            {
                GuaranteedPerfectIvs = value,
                Ivs = encounter.Ivs with { Hp = value == 0 ? SwShDynamaxAdventureArchive.RandomIvValue : -value },
            },
            SwShDynamaxAdventuresWorkflowService.IvAttackField => encounter with { Ivs = encounter.Ivs with { Attack = value } },
            SwShDynamaxAdventuresWorkflowService.IvDefenseField => encounter with { Ivs = encounter.Ivs with { Defense = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpeedField => encounter with { Ivs = encounter.Ivs with { Speed = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpecialAttackField => encounter with { Ivs = encounter.Ivs with { SpecialAttack = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpecialDefenseField => encounter with { Ivs = encounter.Ivs with { SpecialDefense = value } },
            SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField => encounter with { IsSingleCapture = value != 0 },
            SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField => encounter with { IsStoryProgressGated = value != 0 },
            SwShDynamaxAdventuresWorkflowService.OtGenderField => encounter with
            {
                OtGender = value,
                OtGenderLabel = GetOptionLabel(workflow, field, value, "OT gender"),
            },
            _ => encounter,
        };

        updatedEncounter = updatedEncounter with
        {
            IvSummary = FormatIvSummary(updatedEncounter.Ivs, updatedEncounter.GuaranteedPerfectIvs),
            Label = FormatEncounterLabel(updatedEncounter),
        };

        return updatedEncounter;
    }

    private static IReadOnlyList<SwShDynamaxAdventureMoveRecord> SetMove(
        SwShDynamaxAdventuresWorkflow workflow,
        IReadOnlyList<SwShDynamaxAdventureMoveRecord> moves,
        int slot,
        int value)
    {
        return moves
            .Select(move => move.Slot == slot + 1
                ? move with
                {
                    MoveId = value,
                    Move = GetOptionLabel(workflow, GetMoveField(slot), value, "Move"),
                }
                : move)
            .ToArray();
    }

    private static string GetMoveField(int slot)
    {
        return slot switch
        {
            0 => SwShDynamaxAdventuresWorkflowService.Move0Field,
            1 => SwShDynamaxAdventuresWorkflowService.Move1Field,
            2 => SwShDynamaxAdventuresWorkflowService.Move2Field,
            3 => SwShDynamaxAdventuresWorkflowService.Move3Field,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    private static string FormatEncounterLabel(SwShDynamaxAdventureEntry encounter)
    {
        var speciesLabel = SwShSpeciesFormLabels.FormatSpeciesFormLabel(
            encounter.Species,
            encounter.SpeciesId,
            encounter.Form);
        var versionSuffix = string.Equals(encounter.VersionLabel, "Both", StringComparison.Ordinal)
            ? string.Empty
            : $" [{encounter.VersionLabel}]";
        return $"{encounter.EntryIndex.ToString("000", CultureInfo.InvariantCulture)} / {encounter.AdventureIndex.ToString("000", CultureInfo.InvariantCulture)} - {speciesLabel}{versionSuffix}";
    }

    private static string FormatIvSummary(SwShDynamaxAdventureIvsRecord ivs, int guaranteedPerfectIvs)
    {
        return string.Join(
            ", ",
            [
                guaranteedPerfectIvs > 0
                    ? $"{guaranteedPerfectIvs.ToString(CultureInfo.InvariantCulture)} guaranteed perfect"
                    : ivs.Hp == SwShDynamaxAdventureArchive.RandomIvValue
                        ? "random HP"
                        : $"HP {ivs.Hp.ToString(CultureInfo.InvariantCulture)}",
                $"Atk {FormatIvValue(ivs.Attack)}",
                $"Def {FormatIvValue(ivs.Defense)}",
                $"SpA {FormatIvValue(ivs.SpecialAttack)}",
                $"SpD {FormatIvValue(ivs.SpecialDefense)}",
                $"Spe {FormatIvValue(ivs.Speed)}",
            ]);
    }

    private static string FormatIvValue(int value)
    {
        return value == SwShDynamaxAdventureArchive.RandomIvValue
            ? "random"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetOptionLabel(
        SwShDynamaxAdventuresWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];

        return SwShDynamaxAdventuresWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private DynamaxAdventureApplyState? CreateApplyState(
        ProjectPaths paths,
        OpenedProject project,
        SwShDynamaxAdventuresWorkflowService.WorkflowFileSource source,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var sourceBytes = SwShDynamaxAdventuresWorkflowService.ReadBoundedDynamaxAdventureTable(source.AbsolutePath);
            var archive = SwShDynamaxAdventureArchive.Parse(sourceBytes);
            var baseBytes = TryLoadBaseDynamaxAdventureBytes(paths);
            if (baseBytes is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures change planning requires the verified base Adventure table.",
                    file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                    expected: "Verified canonical base Adventure table"));
                return null;
            }

            var baseArchive = SwShDynamaxAdventureArchive.Parse(baseBytes);
            if (!dynamaxAdventuresWorkflowService.AcceptsBaseTable(baseBytes, baseArchive))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures change planning rejected the base Adventure table identity.",
                    file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                    expected: "Verified canonical base Adventure table"));
                return null;
            }

            var isVanillaTableRestore = IsVanillaTableRestoreAction(session);
            var edits = isVanillaTableRestore
                ? []
                : session.PendingEdits
                    .Select(edit => ToDynamaxAdventureEdit(edit, diagnostics))
                    .Where(edit => edit is not null)
                    .Select(edit => edit!)
                    .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return null;
            }

            var output = isVanillaTableRestore
                ? baseBytes.ToArray()
                : archive.WriteEditsPreservingLayout(edits);
            var finalArchive = isVanillaTableRestore
                ? baseArchive
                : SwShDynamaxAdventureArchive.Parse(output);

            var sourceLayoutMatchesBase = SwShDynamaxAdventuresWorkflowService.IsDynamaxAdventureTableLayoutCompatible(
                baseArchive,
                baseBytes,
                archive,
                sourceBytes);

            var mainSource = ResolveWorkflowFile(project, SwShExeFsPatchWorkflowService.ExeFsMainPath);
            var baseMainPath = ResolveBaseSourcePath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath);
            if (mainSource is null || baseMainPath is null || !File.Exists(baseMainPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures change planning requires readable base and effective exefs/main sources for mirror verification.",
                    file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                    expected: "Selected-game base and effective Sword/Shield exefs/main"));
                return null;
            }

            var baseMainBytes = File.ReadAllBytes(baseMainPath);
            var effectiveMainBytes = File.ReadAllBytes(mainSource.AbsolutePath);
            var recognizedMainSourceArchive = isVanillaTableRestore
                ? dynamaxAdventuresWorkflowService.CanRecognizeSourceMainProjection(archive, baseArchive)
                    ? archive
                    : null
                : archive;
            var mainAnalysis = SwShDynamaxAdventuresMainPatcher.Analyze(
                effectiveMainBytes,
                baseMainBytes,
                finalArchive,
                baseArchive,
                paths.SelectedGame,
                recognizedMainSourceArchive);
            if (mainAnalysis.HasLegacyBossTargetPatch && !IsRecoveryAction(session))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures change planning detected unsupported legacy final-boss target remap code after session validation. Ordinary edits cannot remove it implicitly; use Stage Repair or a full vanilla table restore.",
                    file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                    expected: "Explicit legacy cleanup recovery action"));
                return null;
            }

            if (mainAnalysis.Kind is SwShDynamaxAdventuresMainKind.UnsupportedBuild
                or SwShDynamaxAdventuresMainKind.GameMismatch
                or SwShDynamaxAdventuresMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    mainAnalysis.Message,
                    file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                    expected: "Supported selected-game executable with base, source, or final DA-owned state"));
                return null;
            }

            return new DynamaxAdventureApplyState(
                archive,
                finalArchive,
                baseArchive,
                recognizedMainSourceArchive,
                output,
                baseBytes,
                output.SequenceEqual(baseBytes),
                sourceLayoutMatchesBase,
                HasTableEdits: !output.SequenceEqual(sourceBytes),
                mainAnalysis);
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("omitted FlatBuffer default", StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change would require rebuilding the table byte layout: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "A field already stored in the source table, or the existing default value"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change plan could not decode the source table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Dynamax Adventures table"));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change plan rejected an out-of-domain field value: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Values in the verified Dynamax Adventures field domains"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change plan could not read the source table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable source table"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change plan could not read the source table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable source table"));
        }

        return null;
    }

    private static bool IsVanillaTableRestoreAction(EditSession session)
    {
        if (session.PendingEdits is not { Count: 1 })
        {
            return false;
        }

        var edit = session.PendingEdits[0];
        return edit is not null
            && string.Equals(edit.Summary, RestoreVanillaTableSummary, StringComparison.Ordinal)
            && string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.Field, SwShDynamaxAdventuresWorkflowService.LevelField, StringComparison.Ordinal)
            && SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out _)
            && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && edit.Sources is { Count: 1 }
            && edit.Sources[0] is not null
            && edit.Sources[0].Layer == ProjectFileLayer.Layered
            && string.Equals(
                edit.Sources[0].RelativePath,
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                StringComparison.Ordinal);
    }

    private static bool IsExecutableRepairAction(EditSession session)
    {
        if (session.PendingEdits is not { Count: 1 })
        {
            return false;
        }

        var edit = session.PendingEdits[0];
        return edit is not null
            && string.Equals(edit.Summary, RepairExecutableProjectionSummary, StringComparison.Ordinal)
            && string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.Field, SwShDynamaxAdventuresWorkflowService.LevelField, StringComparison.Ordinal)
            && SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out _)
            && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && edit.Sources is { Count: 1 }
            && edit.Sources[0] is not null
            && string.Equals(
                edit.Sources[0].RelativePath,
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                StringComparison.Ordinal);
    }

    private static bool IsRecoveryAction(EditSession session)
    {
        return IsVanillaTableRestoreAction(session)
            || IsExecutableRepairAction(session);
    }

    private static SwShDynamaxAdventureEdit? ToDynamaxAdventureEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Dynamax Adventures edit"));
            return null;
        }

        return new SwShDynamaxAdventureEdit(entryIndex, field, value);
    }

    private static SwShDynamaxAdventureField? MapField(string? field)
    {
        return field switch
        {
            SwShDynamaxAdventuresWorkflowService.SpeciesField => SwShDynamaxAdventureField.Species,
            SwShDynamaxAdventuresWorkflowService.FormField => SwShDynamaxAdventureField.Form,
            SwShDynamaxAdventuresWorkflowService.LevelField => SwShDynamaxAdventureField.Level,
            SwShDynamaxAdventuresWorkflowService.BallItemIdField => SwShDynamaxAdventureField.BallItemId,
            SwShDynamaxAdventuresWorkflowService.AbilityField => SwShDynamaxAdventureField.Ability,
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField => SwShDynamaxAdventureField.GigantamaxState,
            SwShDynamaxAdventuresWorkflowService.VersionField => SwShDynamaxAdventureField.Version,
            SwShDynamaxAdventuresWorkflowService.ShinyRollField => SwShDynamaxAdventureField.ShinyRoll,
            SwShDynamaxAdventuresWorkflowService.Move0Field => SwShDynamaxAdventureField.Move0,
            SwShDynamaxAdventuresWorkflowService.Move1Field => SwShDynamaxAdventureField.Move1,
            SwShDynamaxAdventuresWorkflowService.Move2Field => SwShDynamaxAdventureField.Move2,
            SwShDynamaxAdventuresWorkflowService.Move3Field => SwShDynamaxAdventureField.Move3,
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField => SwShDynamaxAdventureField.GuaranteedPerfectIvs,
            SwShDynamaxAdventuresWorkflowService.IvAttackField => SwShDynamaxAdventureField.IvAttack,
            SwShDynamaxAdventuresWorkflowService.IvDefenseField => SwShDynamaxAdventureField.IvDefense,
            SwShDynamaxAdventuresWorkflowService.IvSpeedField => SwShDynamaxAdventureField.IvSpeed,
            SwShDynamaxAdventuresWorkflowService.IvSpecialAttackField => SwShDynamaxAdventureField.IvSpecialAttack,
            SwShDynamaxAdventuresWorkflowService.IvSpecialDefenseField => SwShDynamaxAdventureField.IvSpecialDefense,
            SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField => SwShDynamaxAdventureField.IsSingleCapture,
            SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField => SwShDynamaxAdventureField.IsStoryProgressGated,
            SwShDynamaxAdventuresWorkflowService.OtGenderField => SwShDynamaxAdventureField.OtGender,
            _ => null,
        };
    }

    private static string GetSpeciesLabel(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        int speciesId)
    {
        if (encounter.SpeciesId == speciesId)
        {
            return encounter.Species;
        }

        if (encounter.VanillaPokemon?.SpeciesId == speciesId)
        {
            return encounter.VanillaPokemon.Species;
        }

        return GetOptionLabel(
            workflow,
            SwShDynamaxAdventuresWorkflowService.SpeciesField,
            speciesId,
            "Species");
    }

    private static int GetEncounterFieldValue(SwShDynamaxAdventureEntry encounter, string field)
    {
        return field switch
        {
            SwShDynamaxAdventuresWorkflowService.SpeciesField => encounter.SpeciesId,
            SwShDynamaxAdventuresWorkflowService.FormField => encounter.Form,
            SwShDynamaxAdventuresWorkflowService.LevelField => encounter.Level,
            SwShDynamaxAdventuresWorkflowService.AbilityField => encounter.Ability,
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField => encounter.GigantamaxState,
            SwShDynamaxAdventuresWorkflowService.Move0Field => encounter.Moves[0].MoveId,
            SwShDynamaxAdventuresWorkflowService.Move1Field => encounter.Moves[1].MoveId,
            SwShDynamaxAdventuresWorkflowService.Move2Field => encounter.Moves[2].MoveId,
            SwShDynamaxAdventuresWorkflowService.Move3Field => encounter.Moves[3].MoveId,
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField => encounter.GuaranteedPerfectIvs,
            SwShDynamaxAdventuresWorkflowService.IvAttackField => encounter.Ivs.Attack,
            SwShDynamaxAdventuresWorkflowService.IvDefenseField => encounter.Ivs.Defense,
            SwShDynamaxAdventuresWorkflowService.IvSpeedField => encounter.Ivs.Speed,
            SwShDynamaxAdventuresWorkflowService.IvSpecialAttackField => encounter.Ivs.SpecialAttack,
            SwShDynamaxAdventuresWorkflowService.IvSpecialDefenseField => encounter.Ivs.SpecialDefense,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported Dynamax Adventures editable field."),
        };
    }

    private static bool ArchiveRecordsEqual(
        SwShDynamaxAdventureArchive left,
        SwShDynamaxAdventureArchive right)
    {
        if (left.Entries.Count != right.Entries.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Entries.Count; index++)
        {
            if (!RecordsEqual(left.Entries[index], right.Entries[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RecordsEqual(
        SwShDynamaxAdventureRecord left,
        SwShDynamaxAdventureRecord right)
    {
        return left.EntryIndex == right.EntryIndex
            && left.IsSingleCapture == right.IsSingleCapture
            && left.SingleCaptureFlagBlock == right.SingleCaptureFlagBlock
            && left.Field02 == right.Field02
            && left.Form == right.Form
            && left.GigantamaxState == right.GigantamaxState
            && left.BallItemId == right.BallItemId
            && left.AdventureIndex == right.AdventureIndex
            && left.Level == right.Level
            && left.Species == right.Species
            && left.UiMessageId == right.UiMessageId
            && left.OtGender == right.OtGender
            && left.Version == right.Version
            && left.ShinyRoll == right.ShinyRoll
            && left.Ivs == right.Ivs
            && left.Ability == right.Ability
            && left.IsStoryProgressGated == right.IsStoryProgressGated
            && left.Moves.SequenceEqual(right.Moves);
    }

    private static SwShDynamaxAdventureArchive? TryLoadBaseDynamaxAdventureArchive(ProjectPaths paths)
    {
        var baseBytes = TryLoadBaseDynamaxAdventureBytes(paths);
        return baseBytes is not null
            ? SwShDynamaxAdventureArchive.Parse(baseBytes)
            : null;
    }

    private static byte[]? TryLoadBaseDynamaxAdventureBytes(ProjectPaths paths)
    {
        var basePath = ResolveBaseSourcePath(paths, SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        return basePath is not null && File.Exists(basePath)
            ? SwShDynamaxAdventuresWorkflowService.ReadBoundedDynamaxAdventureTable(basePath)
            : null;
    }

    private static bool BaseSourceExists(ProjectPaths paths, string targetRelativePath)
    {
        var basePath = ResolveBaseSourcePath(paths, targetRelativePath);
        return basePath is not null && File.Exists(basePath);
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
                "Dynamax Adventures apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static bool HasAnyPokemonChangeFromVanilla(SwShDynamaxAdventureEntry encounter)
    {
        var vanilla = encounter.VanillaPokemon;
        return vanilla is null
            || encounter.SpeciesId != vanilla.SpeciesId
            || encounter.Form != vanilla.Form
            || encounter.Level != vanilla.Level
            || encounter.Ability != vanilla.Ability
            || encounter.GigantamaxState != vanilla.GigantamaxState
            || encounter.GuaranteedPerfectIvs != vanilla.GuaranteedPerfectIvs
            || encounter.Ivs != vanilla.Ivs
            || !MoveIdsEqual(encounter.Moves, vanilla.Moves);
    }

    private static IReadOnlyList<ProjectFileReference> CreateCanonicalPlanSources(
        OpenedProject project,
        EditSession session,
        bool includeMain)
    {
        var exactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShPersonalTable.PersonalDataRelativePath,
            SwShPokemonLearnsetTable.LearnsetDataRelativePath,
        };
        if (includeMain)
        {
            exactPaths.Add(SwShExeFsPatchWorkflowService.ExeFsMainPath);
        }

        var movePrefix = SwShMoveDataFile.MoveDataRelativeDirectory.TrimEnd('/') + "/";
        var sources = new List<ProjectFileReference>();
        var dependencyPaths = exactPaths
            .Concat(project.FileGraph.Entries
                .Where(entry => entry.RelativePath.StartsWith(movePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var relativePath in dependencyPaths)
        {
            var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            AddPlanSourceReferences(entry, relativePath, sources);
        }

        sources.Add(CreatePendingActionSource(session));

        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ProjectFileReference> CreateRecoveryPlanSources(
        OpenedProject project,
        EditSession session)
    {
        var recoveryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShExeFsPatchWorkflowService.ExeFsMainPath,
        };
        var sources = new List<ProjectFileReference>();
        foreach (var relativePath in recoveryPaths.Order(StringComparer.Ordinal))
        {
            var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            AddPlanSourceReferences(entry, relativePath, sources);
        }

        sources.Add(CreatePendingActionSource(session));
        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddPlanSourceReferences(
        ProjectFileGraphEntry? entry,
        string relativePath,
        ICollection<ProjectFileReference> sources)
    {
        if (entry?.BaseFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, relativePath));
        }

        if (entry?.LayeredFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Layered, relativePath));
        }
        else
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Generated, relativePath));
        }
    }

    private static ProjectFileReference CreatePendingActionSource(EditSession session)
    {
        var canonical = new StringBuilder("dynamax-adventures-actions-v1\n");
        foreach (var edit in session.PendingEdits
            .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
            .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
            .ThenBy(edit => edit.Field, StringComparer.Ordinal)
            .ThenBy(edit => edit.NewValue, StringComparer.Ordinal)
            .ThenBy(edit => edit.Summary, StringComparer.Ordinal))
        {
            AppendCanonicalPart(canonical, edit.Domain);
            AppendCanonicalPart(canonical, edit.RecordId);
            AppendCanonicalPart(canonical, edit.Field);
            AppendCanonicalPart(canonical, edit.NewValue);
            AppendCanonicalPart(canonical, edit.Summary);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/dynamax-adventures/{hash}");
    }

    private static void AppendCanonicalPart(StringBuilder target, string? value)
    {
        var normalized = value ?? string.Empty;
        target.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(normalized);
        target.Append('\n');
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (targetRelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, targetRelativePath["romfs/".Length..]);
        }

        if (targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed record WorkflowFileSource(ProjectFileGraphEntry GraphEntry, string AbsolutePath);

    private sealed record DynamaxAdventureApplyState(
        SwShDynamaxAdventureArchive SourceArchive,
        SwShDynamaxAdventureArchive FinalArchive,
        SwShDynamaxAdventureArchive BaseArchive,
        SwShDynamaxAdventureArchive? RecognizedMainSourceArchive,
        byte[] OutputBytes,
        byte[] BaseBytes,
        bool MatchesBaseBytes,
        bool SourceLayoutMatchesBase,
        bool HasTableEdits,
        SwShDynamaxAdventuresMainAnalysis MainAnalysis);

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

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Dynamax Adventures field '{field}' is not supported by the workflow.",
            field: "field",
            expected: "Supported Dynamax Adventures Pokemon field");
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
            Domain: SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            Field: field,
            Expected: expected);
    }
}
