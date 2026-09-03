// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.ZA;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.EvolutionItems;
using KM.ZA.ExeFs;
using KM.ZA.Items;
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
    private const string DexPlacementRecordId = "dex-placement";
    private const string DexLayoutRecordId = "dex-layout";
    private const string VanillaDexPlacementRecordId = "dex-placement-vanilla";
    private const string MegaDexSyncRecordId = "dex-mega-sync";
    private const string MegaDexSyncValue = "sync-current-membership";
    private const string AlphaMoveRecordIdPrefix = "alpha:";
    private const string GlobalRecordId = "all";
    private const string GlobalEvYieldField = "evYieldAll";
    private const string GlobalExpYieldField = "expYieldAll";
    private const string RemoveYieldValue = "remove";
    private const string RestoreYieldValue = "restore";
    private const string DexPlacementPayloadV1Prefix = "v1|";
    private const string DexPlacementPayloadV2Prefix = "v2|";
    private const int PersonalTableEntryFieldIndex = 0;
    private const int PersonalSpeciesFieldIndex = 0;
    private const int PersonalIsPresentFieldIndex = 1;
    private const int PersonalZaDexOrderFieldIndex = 2;
    private const int PersonalType1FieldIndex = 3;
    private const int PersonalType2FieldIndex = 4;
    private const int PersonalAbility1FieldIndex = 5;
    private const int PersonalAbility2FieldIndex = 6;
    private const int PersonalHiddenAbilityFieldIndex = 7;
    private const int PersonalXpGrowthFieldIndex = 8;
    private const int PersonalCatchRateFieldIndex = 9;
    private const int PersonalGenderFieldIndex = 10;
    private const int PersonalEggGroup1FieldIndex = 11;
    private const int PersonalEggGroup2FieldIndex = 12;
    private const int PersonalEggHatchFieldIndex = 13;
    private const int PersonalEggHatchCyclesFieldIndex = 14;
    private const int PersonalBaseFriendshipFieldIndex = 15;
    private const int PersonalEvolutionStageFieldIndex = 17;
    private const int PersonalEvYieldFieldIndex = 19;
    private const int PersonalBaseStatsFieldIndex = 20;
    private const int PersonalEvolutionsFieldIndex = 21;
    private const int PersonalTmMovesFieldIndex = 22;
    private const int PersonalEggMovesFieldIndex = 23;
    private const int PersonalReminderMovesFieldIndex = 24;
    private const int PersonalLevelupMovesFieldIndex = 25;
    private const int EvolutionDataSize = 16;
    private const int LevelupMoveDataSize = 4;

    private static readonly HashSet<string> EvYieldFields =
    [
        ZaPokemonWorkflowService.EVYieldHPField,
        ZaPokemonWorkflowService.EVYieldAttackField,
        ZaPokemonWorkflowService.EVYieldDefenseField,
        ZaPokemonWorkflowService.EVYieldSpecialAttackField,
        ZaPokemonWorkflowService.EVYieldSpecialDefenseField,
        ZaPokemonWorkflowService.EVYieldSpeedField,
    ];

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

    public ZaPokemonEditResult ReadEffective(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();
        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.PokemonDomain,
            diagnostics);
        return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
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

        if (IsGlobalYieldField(field))
        {
            var globalPendingEdit = CreateGlobalYieldPendingEdit(workflow, field, value, diagnostics);
            if (globalPendingEdit is null)
            {
                return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
            }

            var globalUpdatedSession = ReplacePendingPokemonEdit(currentSession, globalPendingEdit);
            var globalInteractionDiagnostics = new List<ValidationDiagnostic>();
            ValidateGlobalYieldSessionInteractions(globalUpdatedSession, globalInteractionDiagnostics);
            if (globalInteractionDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.AddRange(globalInteractionDiagnostics);
                return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
            }

            return new ZaPokemonEditResult(
                OverlayPendingEdits(loadedWorkflow, globalUpdatedSession.PendingEdits),
                globalUpdatedSession,
                diagnostics);
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

        var updatedSession = ReplaceOrRemoveFieldNoOp(
            loadedWorkflow,
            currentSession,
            pendingEdit);
        var interactionDiagnostics = new List<ValidationDiagnostic>();
        ValidateAlphaSessionInteractions(loadedWorkflow, updatedSession, interactionDiagnostics);
        ValidateGlobalYieldSessionInteractions(updatedSession, interactionDiagnostics);
        if (interactionDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.AddRange(interactionDiagnostics);
            return new ZaPokemonEditResult(
                OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits),
                currentSession,
                diagnostics);
        }

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
        var effectiveOverlay = new PokemonWorkflowOverlay(workflow);
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

            PendingEdit? pendingEdit;
            if (IsGlobalYieldField(update.Field))
            {
                pendingEdit = CreateGlobalYieldPendingEdit(
                    effectiveOverlay.Workflow,
                    update.Field,
                    update.Value,
                    diagnostics);
            }
            else
            {
                var pokemon = effectiveOverlay.FindPokemon(update.PersonalId);
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

                pendingEdit = CreateFieldPendingEdit(
                    effectiveOverlay.Workflow,
                    pokemon,
                    update.Field,
                    update.Value,
                    diagnostics);
            }

            if (pendingEdit is null)
            {
                continue;
            }

            var previousSession = updatedSession;
            updatedSession = IsGlobalYieldEdit(pendingEdit)
                ? ReplacePendingPokemonEdit(previousSession, pendingEdit)
                : ReplaceOrRemoveFieldNoOp(loadedWorkflow, previousSession, pendingEdit);
            var removesBroaderGlobalYield = !IsGlobalYieldEdit(pendingEdit)
                && previousSession.PendingEdits.Any(candidate =>
                    IsGlobalYieldEdit(candidate)
                    && ShouldReplacePendingEdit(candidate, pendingEdit));
            if (removesBroaderGlobalYield)
            {
                effectiveOverlay = CreatePokemonWorkflowOverlay(
                    loadedWorkflow,
                    updatedSession.PendingEdits);
            }
            else
            {
                effectiveOverlay.Apply(pendingEdit);
            }
        }

        var interactionDiagnostics = new List<ValidationDiagnostic>();
        ValidateAlphaSessionInteractions(loadedWorkflow, updatedSession, interactionDiagnostics);
        ValidateGlobalYieldSessionInteractions(updatedSession, interactionDiagnostics);
        if (interactionDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.AddRange(interactionDiagnostics);
            return new ZaPokemonEditResult(
                OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits),
                currentSession,
                diagnostics);
        }

        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPokemonEditResult UpdateComposite(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPokemonFieldUpdate> fieldUpdates,
        IReadOnlyList<ZaPokemonEvolutionOperation> evolutionUpdates,
        IReadOnlyList<ZaPokemonLearnsetUpdate> learnsetUpdates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(fieldUpdates);
        ArgumentNullException.ThrowIfNull(evolutionUpdates);
        ArgumentNullException.ThrowIfNull(learnsetUpdates);

        var originalSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(loadedWorkflow, originalSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaPokemonEditResult RollBack()
        {
            return new ZaPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (!ZaEditSessionSupport.CanEdit(
                project,
                originalWorkflow.Summary,
                originalWorkflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return RollBack();
        }

        var workingSession = originalSession;
        var effectiveOverlay = new PokemonWorkflowOverlay(originalWorkflow);
        foreach (var update in fieldUpdates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon Data batch update is missing a field or value.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "fieldUpdates",
                    expected: "Complete Pokemon Data field update"));
                continue;
            }

            PendingEdit? pendingEdit;
            if (IsGlobalYieldField(update.Field))
            {
                pendingEdit = CreateGlobalYieldPendingEdit(
                    effectiveOverlay.Workflow,
                    update.Field,
                    update.Value,
                    diagnostics);
            }
            else
            {
                var pokemon = effectiveOverlay.FindPokemon(update.PersonalId);
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

                pendingEdit = CreateFieldPendingEdit(
                    effectiveOverlay.Workflow,
                    pokemon,
                    update.Field,
                    update.Value,
                    diagnostics);
            }

            if (pendingEdit is null)
            {
                continue;
            }

            var previousSession = workingSession;
            workingSession = IsGlobalYieldEdit(pendingEdit)
                ? ReplacePendingPokemonEdit(previousSession, pendingEdit)
                : ReplaceOrRemoveFieldNoOp(loadedWorkflow, previousSession, pendingEdit);
            var removesBroaderGlobalYield = !IsGlobalYieldEdit(pendingEdit)
                && previousSession.PendingEdits.Any(candidate =>
                    IsGlobalYieldEdit(candidate)
                    && ShouldReplacePendingEdit(candidate, pendingEdit));
            if (removesBroaderGlobalYield)
            {
                effectiveOverlay = CreatePokemonWorkflowOverlay(
                    loadedWorkflow,
                    workingSession.PendingEdits);
            }
            else
            {
                effectiveOverlay.Apply(pendingEdit);
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return RollBack();
        }

        foreach (var update in evolutionUpdates)
        {
            if (string.IsNullOrWhiteSpace(update.Action))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon evolution batch update is missing an action.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "evolutionUpdates",
                    expected: "Complete Pokemon evolution operation"));
                break;
            }

            var pokemon = effectiveOverlay.FindPokemon(update.PersonalId);
            if (pokemon is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon personal record {update.PersonalId} is not present in the loaded Pokemon Data workflow.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "personalId",
                    expected: "Existing Pokemon personal record"));
                break;
            }

            var operation = CreateEvolutionOperation(
                pokemon,
                update.Action,
                update.Slot,
                update.Method,
                update.Argument,
                update.Species,
                update.Form,
                update.Level,
                diagnostics);
            if (operation is null)
            {
                break;
            }

            var pendingEdit = ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                CreateEvolutionSummary(pokemon, operation),
                new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
                pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
                CreateOperationField(EvolutionFieldPrefix, operation.Action, operation.Slot),
                FormatEvolutionValue(operation));
            workingSession = ReplacePendingPokemonEdit(workingSession, pendingEdit);
            effectiveOverlay.Apply(pendingEdit);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return RollBack();
        }

        foreach (var update in learnsetUpdates)
        {
            if (string.IsNullOrWhiteSpace(update.Action))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset batch update is missing an action.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "learnsetUpdates",
                    expected: "Complete Pokemon learnset operation"));
                break;
            }

            var pokemon = effectiveOverlay.FindPokemon(update.PersonalId);
            if (pokemon is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon personal record {update.PersonalId} is not present in the loaded Pokemon Data workflow.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "personalId",
                    expected: "Existing Pokemon personal record"));
                break;
            }

            var operation = CreateLearnsetOperation(
                pokemon,
                update.Action,
                update.Slot,
                update.MoveId,
                update.Level,
                diagnostics);
            if (operation is null)
            {
                break;
            }

            var pendingEdit = ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                CreateLearnsetSummary(pokemon, operation),
                new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile),
                pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
                CreateOperationField(LearnsetFieldPrefix, operation.Action, operation.Slot),
                FormatOperationValue(operation.MoveId, operation.RawLevel));
            workingSession = ReplacePendingPokemonEdit(workingSession, pendingEdit);
            effectiveOverlay.Apply(pendingEdit);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return RollBack();
        }

        var dexPlacementEditCount = workingSession.PendingEdits.Count(IsDexPlacementEdit);
        if (dexPlacementEditCount > 1)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "An edit session can contain only one complete Pokédex placement state.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "One canonical Pokédex placement edit"));
        }

        if (workingSession.PendingEdits.Any(IsScopedDexLayoutEdit)
            && workingSession.PendingEdits.Any(edit => !IsDexPlacementEdit(edit)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dex Layout pending changes cannot share an edit session with ordinary Pokemon Data changes.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "A Dex Layout-only edit session"));
        }

        var pokemonEdits = workingSession.PendingEdits
            .Where(edit => string.Equals(
                edit.Domain,
                ZaEditSessionSupport.PokemonDomain,
                StringComparison.Ordinal))
            .ToArray();
        var validationOverlay = new PokemonWorkflowOverlay(loadedWorkflow);
        foreach (var edit in OrderPersonalEditsForApply(pokemonEdits))
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(project, validationOverlay.Workflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                validationOverlay.Apply(edit);
            }
        }

        if (workingSession.PendingEdits.Any(IsDexPlacementEdit)
            && workingSession.PendingEdits.Any(IsDexPresenceEdit))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokédex placement swaps and Present In Game changes must be applied separately.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Apply or discard Present In Game changes before staging a Pokédex placement swap"));
        }

        ValidateAlphaSessionInteractions(loadedWorkflow, workingSession, diagnostics);
        ValidateGlobalYieldSessionInteractions(workingSession, diagnostics);

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? RollBack()
            : new ZaPokemonEditResult(validationOverlay.Workflow, workingSession, diagnostics);
    }

    public ZaPokemonEditResult SwapDexPlacement(
        ProjectPaths paths,
        EditSession? session,
        int sourceSpeciesId,
        int targetSpeciesId)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(IsScopedDexLayoutEdit))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Apply or discard pending Dex Layout changes before using the Pokemon editor Swap.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "No pending Dex Layout changes"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var editor = workflow.DexEditor;
        var loadedEditor = loadedWorkflow.DexEditor;
        if (editor is null
            || loadedEditor is null
            || !editor.CanEdit
            || editor.PersonalProvenance is null
            || editor.ContentsProvenance is null
            || editor.MegaContentsProvenance is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor?.BlockedReason ?? "Pokédex placement is not available for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified active Pokédex placement data"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (sourceSpeciesId == targetSpeciesId)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Choose a different Pokédex slot to stage a swap.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Two different active Pokédex species"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var sourcePlacement = editor.Placements
            .FirstOrDefault(placement => placement.SpeciesId == sourceSpeciesId);
        var targetPlacement = editor.Placements
            .FirstOrDefault(placement => placement.SpeciesId == targetSpeciesId);
        if (sourcePlacement is null || targetPlacement is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokédex placement swap targets a species that is not in the verified active Pokédex.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Two active Pokédex species"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (editor.RegularCount != loadedEditor.RegularCount
            && (!loadedEditor.CanEditAdvanced
                || loadedEditor.ExecutableProvenance is null))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                loadedEditor.AdvancedBlockedReason
                    ?? "The staged Pokédex boundary cannot be preserved because advanced Pokédex editing is unavailable.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified matching Pokemon Legends Z-A exefs/main"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var assignments = editor.Placements.ToDictionary(
            placement => placement.SpeciesId,
            placement => placement.InternalIndex);
        assignments[sourceSpeciesId] = targetPlacement.InternalIndex;
        assignments[targetSpeciesId] = sourcePlacement.InternalIndex;

        var baseAssignments = loadedEditor.Placements.ToDictionary(
            placement => placement.SpeciesId,
            placement => placement.InternalIndex);
        var pendingEdits = currentSession.PendingEdits
            .Where(edit => !IsDexPlacementEdit(edit))
            .ToList();
        if (!DexPlacementStatesEqual(
                assignments,
                editor.RegularCount,
                baseAssignments,
                loadedEditor.RegularCount))
        {
            var changedSpeciesCount = assignments.Count(pair =>
                !baseAssignments.TryGetValue(pair.Key, out var baseIndex)
                || baseIndex != pair.Value);
            var summary = changedSpeciesCount == 2
                ? $"Swap {GetSpeciesName(workflow, sourceSpeciesId)} from {FormatDexPlacement(sourcePlacement)} "
                    + $"with {GetSpeciesName(workflow, targetSpeciesId)} in {FormatDexPlacement(targetPlacement)}."
                : $"Stage Pokédex placement changes for {changedSpeciesCount.ToString(CultureInfo.InvariantCulture)} species.";
            pendingEdits.Add(new PendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                summary,
                CreateDexPlacementSources(
                    loadedEditor,
                    includeExecutable: editor.RegularCount != loadedEditor.RegularCount),
                DexPlacementRecordId,
                ZaPokemonWorkflowService.DexPlacementField,
                EncodeDexPlacementState(
                    assignments,
                    editor.RegularCount,
                    loadedEditor.RegularCount)));
        }

        var updatedSession = currentSession with { PendingEdits = pendingEdits };
        var interactionDiagnostics = new List<ValidationDiagnostic>();
        ValidateAlphaSessionInteractions(loadedWorkflow, updatedSession, interactionDiagnostics);
        if (interactionDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.AddRange(interactionDiagnostics);
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPokemonEditResult MoveDexPlacement(
        ProjectPaths paths,
        EditSession? session,
        int sourceSpeciesId,
        string destinationDexKind,
        int destinationDisplayedNumber)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDexKind);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanUseDexLayoutSession(currentSession, diagnostics)
            || !ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var editor = workflow.DexEditor;
        var loadedEditor = loadedWorkflow.DexEditor;
        if (editor is null
            || loadedEditor is null
            || !editor.CanEdit
            || editor.PersonalProvenance is null
            || editor.ContentsProvenance is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor?.BlockedReason ?? "Pokédex placement is not available for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified active Pokédex placement data"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var normalizedDexKind = destinationDexKind.Trim();
        if (!string.Equals(
                normalizedDexKind,
                ZaPokemonWorkflowService.RegularDexKind,
                StringComparison.Ordinal)
            && !string.Equals(
                normalizedDexKind,
                ZaPokemonWorkflowService.HyperspaceDexKind,
                StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokédex destination '{destinationDexKind}' is not supported.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "regular or hyperspace"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var sourcePlacement = editor.Placements
            .FirstOrDefault(placement => placement.SpeciesId == sourceSpeciesId);
        if (sourcePlacement is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokédex placement move targets a species that is not in the verified active Pokédex.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Active Pokédex species"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var regularPlacements = editor.Placements
            .Where(placement => string.Equals(
                placement.DexKind,
                ZaPokemonWorkflowService.RegularDexKind,
                StringComparison.Ordinal))
            .OrderBy(placement => placement.DisplayedNumber)
            .ToList();
        var hyperspacePlacements = editor.Placements
            .Where(placement => string.Equals(
                placement.DexKind,
                ZaPokemonWorkflowService.HyperspaceDexKind,
                StringComparison.Ordinal))
            .OrderBy(placement => placement.DisplayedNumber)
            .ToList();
        var sourceList = string.Equals(
            sourcePlacement.DexKind,
            ZaPokemonWorkflowService.RegularDexKind,
            StringComparison.Ordinal)
            ? regularPlacements
            : hyperspacePlacements;
        if (!sourceList.Remove(sourcePlacement))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokédex placement move could not resolve the source within its current Pokédex.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified source placement"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var destinationList = string.Equals(
            normalizedDexKind,
            ZaPokemonWorkflowService.RegularDexKind,
            StringComparison.Ordinal)
            ? regularPlacements
            : hyperspacePlacements;
        var maximumDestination = destinationList.Count + 1;
        if (destinationDisplayedNumber <= 0 || destinationDisplayedNumber > maximumDestination)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Pokédex destination number {destinationDisplayedNumber} is outside the available 1-{maximumDestination} range."),
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Displayed destination number from 1 through {maximumDestination}")));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        destinationList.Insert(destinationDisplayedNumber - 1, sourcePlacement);
        var orderedPlacements = regularPlacements
            .Concat(hyperspacePlacements)
            .ToArray();
        var assignments = orderedPlacements
            .Select((placement, index) => new
            {
                placement.SpeciesId,
                InternalIndex = index + 1,
            })
            .ToDictionary(pair => pair.SpeciesId, pair => pair.InternalIndex);
        var targetRegularCount = regularPlacements.Count;
        if (targetRegularCount <= 0 || targetRegularCount >= assignments.Count)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokédex placement moves must leave at least one species in both the Regular and Hyperspace Pokédexes.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Two non-empty Pokédexes"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var currentAssignments = editor.Placements.ToDictionary(
            placement => placement.SpeciesId,
            placement => placement.InternalIndex);
        if (DexPlacementStatesEqual(
                assignments,
                targetRegularCount,
                currentAssignments,
                editor.RegularCount))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{GetSpeciesName(workflow, sourceSpeciesId)} already occupies the requested Pokédex placement.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "A different Pokédex or displayed number"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (targetRegularCount != loadedEditor.RegularCount
            && (!loadedEditor.CanEditAdvanced
                || loadedEditor.ExecutableProvenance is null))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                loadedEditor.AdvancedBlockedReason
                    ?? "Changing the Regular and Hyperspace Pokédex sizes is unavailable for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified matching Pokemon Legends Z-A exefs/main"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var baseAssignments = loadedEditor.Placements.ToDictionary(
            placement => placement.SpeciesId,
            placement => placement.InternalIndex);
        var pendingEdits = currentSession.PendingEdits
            .Where(edit => !IsDexPlacementEdit(edit))
            .ToList();
        if (!DexPlacementStatesEqual(
                assignments,
                targetRegularCount,
                baseAssignments,
                loadedEditor.RegularCount))
        {
            var destinationName = string.Equals(
                normalizedDexKind,
                ZaPokemonWorkflowService.RegularDexKind,
                StringComparison.Ordinal)
                ? "Regular Dex"
                : "Hyperspace Dex";
            pendingEdits.Add(new PendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Move {GetSpeciesName(workflow, sourceSpeciesId)} from {FormatDexPlacement(sourcePlacement)} "
                    + $"to {destinationName} #{destinationDisplayedNumber}; shift occupied entries to keep Pokédex numbers contiguous."),
                CreateDexPlacementSources(
                    loadedEditor,
                    includeExecutable: targetRegularCount != loadedEditor.RegularCount),
                DexLayoutRecordId,
                ZaPokemonWorkflowService.DexPlacementField,
                EncodeDexPlacementState(
                    assignments,
                    targetRegularCount,
                    loadedEditor.RegularCount)));
        }

        var updatedSession = currentSession with { PendingEdits = pendingEdits };
        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPokemonEditResult ResizeDex(
        ProjectPaths paths,
        EditSession? session,
        int regularCount)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanUseDexLayoutSession(currentSession, diagnostics)
            || !ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var editor = workflow.DexEditor;
        var loadedEditor = loadedWorkflow.DexEditor;
        if (editor is null
            || loadedEditor is null
            || !editor.CanEdit
            || editor.PersonalProvenance is null
            || editor.ContentsProvenance is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor?.BlockedReason ?? "Pokédex resizing is not available for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified active Pokédex placement data"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (editor.Placements.Count != ZaDexLayoutMainPatcher.TotalDexSpeciesCount)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Pokédex resizing requires exactly {ZaDexLayoutMainPatcher.TotalDexSpeciesCount} active species, "
                    + $"but this project has {editor.Placements.Count}."),
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: string.Create(
                    CultureInfo.InvariantCulture,
                    $"{ZaDexLayoutMainPatcher.TotalDexSpeciesCount} active Pokédex species")));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var maximumRegularCount = ZaDexLayoutMainPatcher.TotalDexSpeciesCount - 1;
        if (regularCount <= 0 || regularCount > maximumRegularCount)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Regular Dex size {regularCount} is outside the supported 1-{maximumRegularCount} range."),
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Regular Dex size from 1 through {maximumRegularCount}; "
                    + $"Hyperspace Dex size is {ZaDexLayoutMainPatcher.TotalDexSpeciesCount} minus the Regular Dex size")));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (regularCount != loadedEditor.RegularCount
            && (!loadedEditor.CanEditAdvanced
                || loadedEditor.ExecutableProvenance is null))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                loadedEditor.AdvancedBlockedReason
                    ?? "Changing the Regular and Hyperspace Pokédex sizes is unavailable for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified matching Pokemon Legends Z-A exefs/main"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var assignments = editor.Placements.ToDictionary(
            placement => placement.SpeciesId,
            placement => placement.InternalIndex);
        var baseAssignments = loadedEditor.Placements.ToDictionary(
            placement => placement.SpeciesId,
            placement => placement.InternalIndex);
        var pendingEdits = currentSession.PendingEdits
            .Where(edit => !IsDexPlacementEdit(edit))
            .ToList();
        if (!DexPlacementStatesEqual(
                assignments,
                regularCount,
                baseAssignments,
                loadedEditor.RegularCount))
        {
            var hyperspaceCount = ZaDexLayoutMainPatcher.TotalDexSpeciesCount - regularCount;
            pendingEdits.Add(new PendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Resize the Regular Dex to {regularCount} entries and the Hyperspace Dex to {hyperspaceCount} entries; preserve the global Pokédex order."),
                CreateDexPlacementSources(
                    loadedEditor,
                    includeExecutable: regularCount != loadedEditor.RegularCount),
                DexLayoutRecordId,
                ZaPokemonWorkflowService.DexPlacementField,
                EncodeDexPlacementState(
                    assignments,
                    regularCount,
                    loadedEditor.RegularCount)));
        }

        var updatedSession = currentSession with { PendingEdits = pendingEdits };
        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPokemonEditResult StageVanillaDexLayout(
        ProjectPaths paths,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanUseDexLayoutSession(currentSession, diagnostics)
            || !ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var editor = workflow.DexEditor;
        var loadedEditor = loadedWorkflow.DexEditor;
        if (editor is null
            || loadedEditor is null
            || !editor.CanEdit
            || editor.PersonalProvenance is null
            || editor.ContentsProvenance is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor?.BlockedReason ?? "Returning Dex Layout to vanilla is not available for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified active and base Pokédex placement data"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (editor.IsVanillaLayout)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dex Layout already matches the verified vanilla ordering, membership, sizes, and Mega Pokédex availability.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "A modified Dex Layout"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (!editor.CanReturnToVanilla)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor.ReturnToVanillaBlockedReason
                    ?? "Returning Dex Layout to vanilla is unavailable for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified current and base Dex Layout sources"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        try
        {
            var vanilla = ReadVerifiedVanillaDexLayout(project, loadedEditor);
            var targetChangesRegularCount =
                vanilla.RegularCount != loadedEditor.RegularCount;
            if (targetChangesRegularCount
                && (!loadedEditor.CanEditAdvanced
                    || loadedEditor.ExecutableProvenance is null))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    loadedEditor.AdvancedBlockedReason
                        ?? "Returning Dex Layout to vanilla requires a verified matching Pokemon Legends Z-A exefs/main.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: ZaPokemonWorkflowService.DexPlacementField,
                    expected: "Verified matching Pokemon Legends Z-A exefs/main"));
                return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
            }

            var pendingEdits = currentSession.PendingEdits
                .Where(edit => !IsDexPlacementEdit(edit))
                .ToList();
            var stagesVanillaWrite = !loadedEditor.IsVanillaLayout;
            if (stagesVanillaWrite)
            {
                pendingEdits.Add(new PendingEdit(
                    ZaEditSessionSupport.PokemonDomain,
                    "Return Dex Layout to verified vanilla ordering, membership, sizes, and Mega Pokédex availability.",
                    CreateVanillaDexPlacementSources(
                        project,
                        loadedEditor,
                        vanilla,
                        targetChangesRegularCount),
                    VanillaDexPlacementRecordId,
                    ZaPokemonWorkflowService.DexPlacementField,
                    EncodeDexPlacementState(
                        vanilla.Assignments,
                        vanilla.RegularCount,
                        loadedEditor.RegularCount)));
            }

            var updatedSession = currentSession with { PendingEdits = pendingEdits };
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                stagesVanillaWrite
                    ? "Return to Vanilla is staged for change-plan review."
                    : "Pending Dex Layout changes were cleared because the effective layout already matches verified vanilla.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField));
            return new ZaPokemonEditResult(
                OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
                updatedSession,
                diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or OverflowException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Return to Vanilla could not be staged safely: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified current and base Dex Layout sources"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }
    }

    public ZaPokemonEditResult StageMegaDexSync(
        ProjectPaths paths,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanUseDexLayoutSession(currentSession, diagnostics)
            || !ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics))
        {
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var editor = workflow.DexEditor;
        var loadedEditor = loadedWorkflow.DexEditor;
        if (editor is null
            || loadedEditor is null
            || !editor.CanEdit
            || loadedEditor.ContentsProvenance is null
            || loadedEditor.MegaContentsProvenance is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor?.BlockedReason
                    ?? "Mega Pokédex synchronization is not available for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified current Pokédex and Mega Pokédex membership data"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        if (!editor.CanSyncMegasToRegular)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mega Pokédex membership already matches the current Regular and Hyperspace Pokédex membership.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "At least one Mega Pokédex membership mismatch"));
            return new ZaPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdits = currentSession.PendingEdits
            .Where(edit => !IsDexPlacementEdit(edit))
            .Append(new PendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                "Sync every Mega Pokédex entry to its species' current Regular or Hyperspace Pokédex membership.",
                CreateMegaDexSyncSources(loadedEditor),
                MegaDexSyncRecordId,
                ZaPokemonWorkflowService.DexPlacementField,
                MegaDexSyncValue))
            .ToArray();
        var updatedSession = currentSession with { PendingEdits = pendingEdits };
        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Mega Pokédex synchronization is staged for change-plan review.",
            ZaEditSessionSupport.PokemonDomain,
            field: ZaPokemonWorkflowService.DexPlacementField));
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
        var updatedSession = ReplacePendingPokemonEdit(currentSession, pendingEdit);

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
        var updatedSession = ReplacePendingPokemonEdit(currentSession, pendingEdit);

        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaPokemonEditResult UpdateEvolutions(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPokemonEvolutionUpdate> updates)
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
        var effectiveOverlay = new PokemonWorkflowOverlay(workflow);
        foreach (var update in updates)
        {
            var pokemon = effectiveOverlay.FindPokemon(update.PersonalId);
            if (pokemon is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon personal record {update.PersonalId} is not present in the loaded Pokemon Data workflow.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "personalId",
                    expected: "Existing Pokemon personal record"));
                break;
            }

            var operation = CreateEvolutionOperation(
                pokemon,
                UpsertAction,
                update.Slot,
                update.Method,
                update.Argument,
                update.Species,
                update.Form,
                update.Level,
                diagnostics);
            if (operation is null)
            {
                break;
            }

            var pendingEdit = ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                CreateEvolutionSummary(pokemon, operation),
                new ProjectFileReference(
                    pokemon.Provenance.SourceLayer,
                    pokemon.Provenance.SourceFile),
                pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
                CreateOperationField(EvolutionFieldPrefix, operation.Action, operation.Slot),
                FormatEvolutionValue(operation));
            updatedSession = ReplacePendingPokemonEdit(updatedSession, pendingEdit);
            effectiveOverlay.Apply(pendingEdit);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaPokemonEditResult(
                workflow,
                currentSession,
                diagnostics);
        }

        return new ZaPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        if (session.PendingEdits.Any(IsAlphaMoveEdit))
        {
            pokemonWorkflowService.ClearMemoryCache();
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = pokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.PokemonDomain,
            diagnostics);

        var dexPlacementEditCount = session.PendingEdits.Count(IsDexPlacementEdit);
        if (dexPlacementEditCount > 1)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "An edit session can contain only one complete Pokédex placement state.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "One canonical Pokédex placement edit"));
        }

        if (session.PendingEdits.Any(IsScopedDexLayoutEdit)
            && session.PendingEdits.Any(edit => !IsDexPlacementEdit(edit)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dex Layout pending changes cannot share an edit session with ordinary Pokemon Data changes.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "A Dex Layout-only edit session"));
        }

        var effectiveOverlay = new PokemonWorkflowOverlay(workflow);
        foreach (var edit in OrderPersonalEditsForApply(session.PendingEdits))
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(project, effectiveOverlay.Workflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                effectiveOverlay.Apply(edit);
            }
        }

        if (session.PendingEdits.Any(IsDexPlacementEdit)
            && session.PendingEdits.Any(IsDexPresenceEdit))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokédex placement swaps and Present In Game changes must be applied separately.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Apply or discard Present In Game changes before staging a Pokédex placement swap"));
        }

        ValidateAlphaSessionInteractions(workflow, session, diagnostics);
        ValidateGlobalYieldSessionInteractions(session, diagnostics);

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

    private static void ValidateAlphaSessionInteractions(
        ZaPokemonWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var alphaEdits = session.PendingEdits.Where(IsAlphaMoveEdit).ToArray();
        if (alphaEdits.Length == 0)
        {
            return;
        }

        if (session.PendingEdits.Any(edit => !string.Equals(
                edit.Domain,
                ZaEditSessionSupport.PokemonDomain,
                StringComparison.Ordinal)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Alpha-exclusive move changes must be applied separately from other editor domains.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "An alpha-exclusive move-only Pokemon editor session",
                code: ZaPokemonDiagnosticCodes.AlphaSessionConflict));
        }

        if (session.PendingEdits.Any(IsDexPlacementEdit))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokédex placement and alpha-exclusive move changes must be applied separately.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "An edit session without Pokédex placement changes",
                code: ZaPokemonDiagnosticCodes.AlphaSessionConflict));
        }

        var alphaTargets = new HashSet<(int SpeciesId, int FormId)>();
        foreach (var alphaEdit in alphaEdits)
        {
            if (TryParseAlphaMoveRecordId(alphaEdit.RecordId, out var speciesId, out var formId))
            {
                alphaTargets.Add((speciesId, formId));
            }
        }

        var hasBindingConflict = session.PendingEdits.Any(edit =>
        {
            if (!string.Equals(
                    edit.Domain,
                    ZaEditSessionSupport.PokemonDomain,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var changesForm = string.Equals(
                edit.Field,
                ZaPokemonWorkflowService.FormField,
                StringComparison.Ordinal);
            var changesTechnicalMachineCompatibility =
                TryParseCompatibilityField(edit.Field, out var groupId, out _)
                && string.Equals(
                    groupId,
                    ZaPokemonWorkflowService.TechnicalMachineCompatibilityGroupId,
                    StringComparison.Ordinal);
            if ((!changesForm && !changesTechnicalMachineCompatibility)
                || !int.TryParse(
                    edit.RecordId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var personalId))
            {
                return false;
            }

            var target = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
            if (target is null)
            {
                return false;
            }

            if (changesForm)
            {
                return alphaTargets.Any(alphaTarget => alphaTarget.SpeciesId == target.SpeciesId);
            }

            var effectiveForm = session.PendingEdits
                .Where(candidate =>
                    string.Equals(
                        candidate.Domain,
                        ZaEditSessionSupport.PokemonDomain,
                        StringComparison.Ordinal)
                    && string.Equals(candidate.RecordId, edit.RecordId, StringComparison.Ordinal)
                    && string.Equals(
                        candidate.Field,
                        ZaPokemonWorkflowService.FormField,
                        StringComparison.Ordinal))
                .Select(candidate => int.TryParse(
                    candidate.NewValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var stagedForm)
                        ? stagedForm
                        : target.Form)
                .LastOrDefault(target.Form);
            return alphaTargets.Contains((target.SpeciesId, effectiveForm));
        });
        if (hasBindingConflict)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Form, TM compatibility, and alpha-exclusive move changes for the same Pokemon must be applied separately.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "Apply Form or TM compatibility changes before staging the alpha-exclusive move",
                code: ZaPokemonDiagnosticCodes.AlphaSessionConflict));
        }
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        return ZaChangePlanSourceGuard.Capture(
            paths,
            session,
            () => CreateChangePlanCore(paths, session, outputMode),
            outputMode);
    }

    private ChangePlan CreateChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        if (session.PendingEdits.Any(IsAlphaMoveEdit))
        {
            return CreateAlphaAwareChangePlan(
                paths,
                session,
                validation.Diagnostics,
                outputMode);
        }

        var plan = session.PendingEdits.Any(IsDexPlacementEdit)
            ? CreateDexAwareChangePlan(
                paths,
                session,
                validation.Diagnostics,
                outputMode)
            : ZaEditSessionSupport.CreateSingleFileChangePlan(
                paths,
                session,
                ZaEditSessionSupport.PokemonDomain,
                ZaDataPaths.PersonalArray,
                "Pokemon Data",
                validation.Diagnostics,
                outputMode);
        if (!plan.CanApply)
        {
            return plan;
        }

        if (session.PendingEdits.Count == 1
            && IsMegaDexSyncEdit(session.PendingEdits[0]))
        {
            return plan;
        }

        OpenedProject project;
        IReadOnlyList<PersonalRow> rows;
        try
        {
            project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, ZaDataPaths.PersonalArray);
            if (NeedsBaseRows(session.PendingEdits))
            {
                var baseSource = fileSource.ReadBase(project, ZaDataPaths.PersonalArray);
                plan = plan with
                {
                    Writes = plan.Writes
                        .Select((write, index) => index == 0
                            ? write with
                            {
                                SourceFingerprint = CreateCombinedSourceFingerprint(
                                    source.Bytes,
                                    baseSource.Bytes),
                            }
                            : write)
                        .ToArray(),
                };
            }

            rows = ReadRows(project, source).Rows;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            var diagnostics = plan.Diagnostics
                .Append(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon Data could not read or recover personal data: {exception.Message}",
                    ZaEditSessionSupport.PokemonDomain,
                    file: $"romfs/{ZaDataPaths.PersonalArray}",
                    expected: "Readable current personal data and clean base data for legacy recovery"))
                .ToArray();
            return new ChangePlan(plan.SessionId, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var conversionState = ZaEvolutionItemConversionState.Load(project, fileSource);
            PrepareEvolutionItemConversions(rows, session.PendingEdits, conversionState);
            if (!conversionState.Modified)
            {
                return plan;
            }

            var isolateTrinityModManagerRomFs =
                outputMode == ZaOutputMode.TrinityModManager
                && plan.Writes.Any(write => string.Equals(
                    write.TargetRelativePath,
                    ZaExeFsReservedRegionLedger.ExeFsMainPath,
                    StringComparison.OrdinalIgnoreCase));
            var writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.EvolutionItemConversionArray,
                [conversionState.SourceReference()],
                outputMode,
                isolateTrinityModManagerRomFs);
            var conversionWrite = new PlannedFileWrite(
                writeInfo.TargetRelativePath,
                writeInfo.Sources,
                writeInfo.ReplacesExistingOutput,
                "Assign custom Pokemon evolution items to game conversion parameters.");
            return new ChangePlan(
                plan.SessionId,
                [conversionWrite, .. plan.Writes],
                plan.Diagnostics);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            var diagnostics = plan.Diagnostics
                .Append(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon Data could not prepare evolution item conversions: {exception.Message}",
                    ZaEditSessionSupport.PokemonDomain,
                    file: $"romfs/{ZaDataPaths.EvolutionItemConversionArray}",
                    expected: "Readable evolution item conversion table with an unused parameter slot"))
                .ToArray();
            return new ChangePlan(plan.SessionId, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
    }

    private ChangePlan CreateAlphaAwareChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<ValidationDiagnostic> validationDiagnostics,
        ZaOutputMode outputMode)
    {
        var diagnostics = validationDiagnostics.ToList();
        var alphaEdits = session.PendingEdits.Where(IsAlphaMoveEdit).ToArray();
        var ordinaryEdits = session.PendingEdits
            .Where(edit => !IsAlphaMoveEdit(edit) && !IsDexPlacementEdit(edit))
            .ToArray();
        if (alphaEdits.Length == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending alpha-exclusive move edit before reviewing this change plan.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "Pending alpha-exclusive move edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var writes = new List<PlannedFileWrite>();
            if (ordinaryEdits.Length > 0)
            {
                var personalSource = fileSource.Read(project, ZaDataPaths.PersonalArray);
                var rows = ReadRows(project, personalSource).Rows;
                var conversionState = ZaEvolutionItemConversionState.Load(project, fileSource);
                PrepareEvolutionItemConversions(rows, ordinaryEdits, conversionState);
                if (conversionState.Modified)
                {
                    var conversionWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                        paths,
                        ZaDataPaths.EvolutionItemConversionArray,
                        [conversionState.SourceReference()],
                        outputMode);
                    writes.Add(new PlannedFileWrite(
                        conversionWriteInfo.TargetRelativePath,
                        conversionWriteInfo.Sources,
                        conversionWriteInfo.ReplacesExistingOutput,
                        "Assign custom Pokemon evolution items to game conversion parameters."));
                }

                var personalWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    ZaDataPaths.PersonalArray,
                    ordinaryEdits.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                    outputMode);
                writes.Add(new PlannedFileWrite(
                    personalWriteInfo.TargetRelativePath,
                    personalWriteInfo.Sources,
                    personalWriteInfo.ReplacesExistingOutput,
                    ordinaryEdits.Length == 1
                        ? $"Apply pending Pokemon Data edit: {ordinaryEdits[0].Summary}"
                        : $"Apply {ordinaryEdits.Length.ToString(CultureInfo.InvariantCulture)} pending Pokemon Data edits."));
            }

            var alphaWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.AlphaMoveTable,
                alphaEdits.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                outputMode);
            writes.Add(new PlannedFileWrite(
                alphaWriteInfo.TargetRelativePath,
                alphaWriteInfo.Sources,
                alphaWriteInfo.ReplacesExistingOutput,
                alphaEdits.Length == 1
                    ? $"Apply pending alpha-exclusive move edit: {alphaEdits[0].Summary}"
                    : $"Apply {alphaEdits.Length.ToString(CultureInfo.InvariantCulture)} pending alpha-exclusive move edits.",
                CreateAlphaMovePlanFingerprint(
                    paths,
                    project,
                    session.PendingEdits,
                    ordinaryEdits.Length > 0,
                    outputMode)));

            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides."));
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Change plan preview contains {writes.Count.ToString(CultureInfo.InvariantCulture)} target files.",
                ZaEditSessionSupport.PokemonDomain));
            return new ChangePlan(session.Id, writes, diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or UnauthorizedAccessException
                or ArgumentException
                or OverflowException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Alpha-exclusive move change plan could not verify its sources and output target: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                file: $"romfs/{ZaDataPaths.AlphaMoveTable}",
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "Readable current and clean validation sources with a writable output root",
                code: ZaPokemonDiagnosticCodes.AlphaPlanVerificationFailed));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
    }

    private string CreateAlphaMovePlanFingerprint(
        ProjectPaths paths,
        OpenedProject project,
        IReadOnlyList<PendingEdit> pendingEdits,
        bool includeOrdinaryPokemonState,
        ZaOutputMode outputMode)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintText(hash, "KM alpha-exclusive move plan v1");
        AppendFingerprintText(hash, outputMode.ToString());
        AppendFingerprintBytes(
            hash,
            "active-alpha",
            fileSource.Read(project, ZaDataPaths.AlphaMoveTable).Bytes);
        AppendFingerprintBytes(
            hash,
            "base-alpha",
            fileSource.ReadBase(project, ZaDataPaths.AlphaMoveTable).Bytes);
        AppendFingerprintBytes(
            hash,
            "active-personal",
            fileSource.Read(project, ZaDataPaths.PersonalArray).Bytes);
        AppendFingerprintBytes(
            hash,
            "active-battle",
            fileSource.Read(project, ZaDataPaths.BattleMoveParameterArray).Bytes);
        AppendFingerprintBytes(
            hash,
            "base-battle",
            fileSource.ReadBase(project, ZaDataPaths.BattleMoveParameterArray).Bytes);
        AppendFingerprintBytes(
            hash,
            "active-items",
            fileSource.Read(project, ZaDataPaths.ItemDataArray).Bytes);
        AppendFingerprintBytes(
            hash,
            "base-items",
            fileSource.ReadBase(project, ZaDataPaths.ItemDataArray).Bytes);

        AppendFingerprintTarget(
            hash,
            paths,
            "target-alpha",
            ZaDataPaths.AlphaMoveTable,
            outputMode);
        if (outputMode == ZaOutputMode.Standalone)
        {
            AppendFingerprintTarget(
                hash,
                paths,
                "target-descriptor",
                ZaWorkflowFileSource.DescriptorVirtualPath,
                outputMode);
        }

        if (includeOrdinaryPokemonState)
        {
            AppendFingerprintBytes(
                hash,
                "base-personal",
                fileSource.ReadBase(project, ZaDataPaths.PersonalArray).Bytes);
            AppendFingerprintBytes(
                hash,
                "active-evolution-conversions",
                fileSource.Read(project, ZaDataPaths.EvolutionItemConversionArray).Bytes);
            AppendFingerprintTarget(
                hash,
                paths,
                "target-personal",
                ZaDataPaths.PersonalArray,
                outputMode);
            AppendFingerprintTarget(
                hash,
                paths,
                "target-evolution-conversions",
                ZaDataPaths.EvolutionItemConversionArray,
                outputMode);
        }

        foreach (var edit in pendingEdits
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                     .ThenBy(edit => edit.NewValue, StringComparer.Ordinal))
        {
            AppendFingerprintText(hash, edit.Domain);
            AppendFingerprintText(hash, edit.RecordId ?? string.Empty);
            AppendFingerprintText(hash, edit.Field ?? string.Empty);
            AppendFingerprintText(hash, edit.NewValue ?? string.Empty);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendFingerprintTarget(
        IncrementalHash hash,
        ProjectPaths paths,
        string label,
        string virtualPath,
        ZaOutputMode outputMode,
        bool isolateTrinityModManagerRomFs = false)
    {
        var outputPath = ZaWorkflowFileSource.ResolveOutputPath(
            paths,
            virtualPath,
            outputMode,
            isolateTrinityModManagerRomFs);
        var normalizedPath = Path.GetFullPath(outputPath);
        if (OperatingSystem.IsWindows())
        {
            normalizedPath = normalizedPath.ToUpperInvariant();
        }

        AppendFingerprintText(hash, $"{label}:path:{normalizedPath}");
        var outputExists = File.Exists(outputPath);
        AppendFingerprintText(hash, $"{label}:{(outputExists ? "present" : "missing")}");
        if (outputExists)
        {
            AppendFingerprintBytes(hash, label, File.ReadAllBytes(outputPath));
        }
    }

    private static void AppendFingerprintText(IncrementalHash hash, string value)
    {
        AppendFingerprintBytes(hash, "text", Encoding.UTF8.GetBytes(value));
    }

    private static void AppendFingerprintBytes(IncrementalHash hash, string label, byte[] value)
    {
        var labelBytes = Encoding.UTF8.GetBytes(label);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, labelBytes.Length);
        hash.AppendData(length);
        hash.AppendData(labelBytes);
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
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

        if (session.PendingEdits.Any(IsAlphaMoveEdit))
        {
            return ApplyAlphaAwareChangePlan(
                paths,
                session,
                reviewedPlan,
                outputMode);
        }

        if (session.PendingEdits.Count == 1
            && IsMegaDexSyncEdit(session.PendingEdits[0]))
        {
            return ApplyMegaDexSyncChangePlan(
                paths,
                session,
                reviewedPlan,
                outputMode);
        }

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        OutputApplyResult? outputTransaction = null;

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
            var isolateTrinityModManagerRomFs = ShouldIsolateTrinityModManagerRomFs(
                currentPlan,
                outputMode);

            var personalArray = ReadRows(
                project,
                source,
                includeBaseRows: NeedsBaseRows(session.PendingEdits));
            var rows = personalArray.Rows;
            var dexEdit = session.PendingEdits.SingleOrDefault(IsDexPlacementEdit);
            ZaWorkflowFile? contentsSource = null;
            ZaPokedexContentsTable? contentsTable = null;
            ZaWorkflowFile? megaContentsSource = null;
            ZaPokedexMegaContentsTable? megaContentsTable = null;
            ZaWorkflowFile? baseMegaContentsSource = null;
            if (dexEdit is not null)
            {
                contentsSource = fileSource.Read(project, ZaDataPaths.PokedexContentsData);
                contentsTable = ZaPokedexContentsTable.Read(contentsSource.Bytes);
                megaContentsSource = fileSource.Read(project, ZaDataPaths.PokedexMegaContentsData);
                megaContentsTable = ZaPokedexMegaContentsTable.Read(megaContentsSource.Bytes);
                if (IsVanillaDexPlacementEdit(dexEdit))
                {
                    baseMegaContentsSource = fileSource.ReadBase(
                        project,
                        ZaDataPaths.PokedexMegaContentsData);
                    _ = ZaPokedexMegaContentsTable.Read(baseMegaContentsSource.Bytes);
                }
            }

            var conversionState = ZaEvolutionItemConversionState.Load(project, fileSource);
            var migratedLegacyArguments = PrepareEvolutionItemConversions(
                rows,
                session.PendingEdits,
                conversionState);
            var dexApply = dexEdit is null
                ? DexPlacementApplyResult.None
                : ApplyDexPlacement(rows, contentsTable!, dexEdit, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var requiresRebuild = personalArray.RequiresLegacyDexOrderRepair
                || RequiresPersonalArrayRebuild(rows, session.PendingEdits)
                || migratedLegacyArguments
                || RequiresEncodedEvolutionRebuild(session.PendingEdits)
                || dexApply.RequiresPersonalRebuild;
            foreach (var edit in OrderPersonalEditsForApply(
                session.PendingEdits.Where(edit => !IsDexPlacementEdit(edit))))
            {
                ApplyEdit(
                    rows,
                    personalArray.BaseRows ?? Array.Empty<PersonalRow>(),
                    edit,
                    conversionState,
                    diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var binaryPatchEdits = session.PendingEdits
                .Where(edit => !IsDexPlacementEdit(edit))
                .Concat(dexApply.ChangedPersonalIds.Select(personalId =>
                    new PendingEdit(
                        ZaEditSessionSupport.PokemonDomain,
                        "Update Pokédex placement.",
                        Array.Empty<ProjectFileReference>(),
                        personalId.ToString(CultureInfo.InvariantCulture),
                        ZaPokemonWorkflowService.RegionalDexIndexField,
                        rows[personalId].ZADexOrder.ToString(CultureInfo.InvariantCulture))))
                .ToArray();
            var outputBytes = requiresRebuild
                ? WriteRows(rows)
                : ApplyPersonalArrayBinaryPatch(source.Bytes, binaryPatchEdits, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var conversionBytes = conversionState.Modified
                ? conversionState.Write()
                : null;
            var contentsBytes = dexApply.GroupUpdates.Count > 0
                ? contentsTable!.WriteSpeciesGroups(dexApply.GroupUpdates)
                : null;
            var megaContentsBytes = dexEdit is null
                ? null
                : baseMegaContentsSource?.Bytes.ToArray()
                    ?? megaContentsTable!.WriteSpeciesGroups(dexApply.TargetGroups);
            var outputWrites = new List<ZaWorkflowFileWrite>();
            if (conversionBytes is not null)
            {
                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.EvolutionItemConversionArray,
                    conversionBytes));
            }

            outputWrites.Add(new ZaWorkflowFileWrite(ZaDataPaths.PersonalArray, outputBytes));
            if (contentsBytes is not null)
            {
                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.PokedexContentsData,
                    contentsBytes));
            }

            if (megaContentsBytes is not null)
            {
                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.PokedexMegaContentsData,
                    megaContentsBytes));
            }

            if (dexApply.ChangesRegularCount)
            {
                var mainWrite = currentPlan.Writes
                    .Single(write => string.Equals(
                        write.TargetRelativePath,
                        ZaExeFsReservedRegionLedger.ExeFsMainPath,
                        StringComparison.OrdinalIgnoreCase));
                var executableOutputContext = new ZaOutputApplyContext(
                    OutputReviewFingerprint.FromChangePlan(currentPlan),
                    new OwnershipOwnerId("workflow.za.dex-layout"),
                    [new OutputApplyOrigin(
                        OutputApplyOriginKind.Workflow,
                        ZaEditSessionSupport.PokemonDomain)]);
                outputTransaction = ZaWorkflowFileSource.ApplyHybridMixedBatch(
                    paths,
                    outputMode,
                    isolateTrinityModManagerRomFs,
                    () =>
                    {
                        var currentProject = projectWorkspaceService.Open(paths);
                        string[] isolatedRomFsPaths =
                        [
                            ZaDataPaths.PersonalArray,
                            ZaDataPaths.PokedexContentsData,
                            ZaDataPaths.PokedexMegaContentsData,
                        ];
                        if (isolateTrinityModManagerRomFs
                            && fileSource.TryFindLegacyBareTrinityModManagerOutput(
                                currentProject,
                                isolatedRomFsPaths,
                                out _))
                        {
                            throw new InvalidDataException(
                                "The Output Root now contains legacy bare Trinity Mod Manager RomFS files. Choose a clean or container Output Root before applying this hybrid Dex Layout package.");
                        }

                        var layeredBypassMainPath = outputMode == ZaOutputMode.TrinityBypass
                            ? ZaExeFsMainFileResolver.ResolveOutputPath(paths)
                            : null;
                        if (outputMode == ZaOutputMode.TrinityBypass
                            && (layeredBypassMainPath is null
                                || !File.Exists(layeredBypassMainPath)))
                        {
                            throw new FileNotFoundException(
                                "The layered Trinity bypass exefs/main disappeared after change-plan review.");
                        }

                        var effectiveMain = ZaExeFsMainFileResolver.ResolveEffective(currentProject)
                            ?? throw new FileNotFoundException(
                                "Pokemon Legends Z-A exefs/main is no longer available.");
                        var pathComparison = OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal;
                        if (layeredBypassMainPath is not null
                            && !string.Equals(
                                Path.GetFullPath(effectiveMain.AbsolutePath),
                                Path.GetFullPath(layeredBypassMainPath),
                                pathComparison))
                        {
                            throw new InvalidDataException(
                                "Trinity bypass Dex Layout output must compose onto the existing layered exefs/main.");
                        }

                        var effectiveMainBytes = File.ReadAllBytes(effectiveMain.AbsolutePath);
                        if (!ZaChangePlanSourceGuard.MatchesCoreSourceFingerprint(
                                paths,
                                currentPlan,
                                mainWrite,
                                outputMode,
                                CreateSourceFingerprint(effectiveMainBytes)))
                        {
                            throw new InvalidDataException(
                                "Pokemon Legends Z-A exefs/main changed after change-plan review.");
                        }

                        var effectiveAnalysis = ZaDexLayoutMainPatcher.Analyze(
                            effectiveMainBytes,
                            paths.SelectedGame);
                        if (effectiveAnalysis.Kind is ZaDexLayoutMainKind.UnsupportedBuild
                            or ZaDexLayoutMainKind.GameMismatch
                            or ZaDexLayoutMainKind.Conflict
                            || effectiveAnalysis.RegularCount != dexApply.CurrentRegularCount)
                        {
                            throw new InvalidDataException(
                                "Pokemon Legends Z-A exefs/main no longer matches the verified current Pokédex boundary.");
                        }

                        var baseMain = ZaExeFsMainFileResolver.ResolveBase(currentProject)
                            ?? throw new FileNotFoundException(
                                "Pokemon Legends Z-A base exefs/main is no longer available.");
                        var baseMainBytes = File.ReadAllBytes(baseMain.AbsolutePath);
                        var baseAnalysis = ZaDexLayoutMainPatcher.Analyze(
                            baseMainBytes,
                            paths.SelectedGame);
                        if (baseAnalysis.Kind != ZaDexLayoutMainKind.Vanilla
                            || baseAnalysis.RegularCount
                                != ZaDexLayoutMainPatcher.VanillaRegularCount
                            || !string.Equals(
                                baseAnalysis.BuildId,
                                effectiveAnalysis.BuildId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                "Pokemon Legends Z-A base exefs/main is not the verified clean build.");
                        }

                        var patchedMain = ZaDexLayoutMainPatcher.ApplyRegularCount(
                            effectiveMainBytes,
                            dexApply.TargetRegularCount,
                            paths.SelectedGame);
                        var mainOutputBytes =
                            outputMode != ZaOutputMode.TrinityBypass
                            && ZaExeFsMainComparison.IsSemanticallyEquivalentToBase(
                                patchedMain,
                                baseMainBytes)
                                ? null
                                : patchedMain;
                        return new ZaStandaloneMixedBatch(
                            outputWrites,
                            Array.Empty<string>(),
                            [new ZaStandaloneOutputMutation(
                                ZaExeFsReservedRegionLedger.ExeFsMainPath,
                                mainOutputBytes,
                                DeleteFallbackBytes: mainOutputBytes is null ? baseMainBytes : null,
                                ApplyContext: executableOutputContext)]);
                    },
                    revalidateReviewedState: () =>
                        ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                            reviewedPlan,
                            CreateChangePlan(paths, session, outputMode)));
            }
            else
            {
                outputTransaction = ZaWorkflowFileSource.WriteBatch(
                    paths,
                    outputWrites,
                    outputMode,
                    revalidateReviewedState: () =>
                        ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                            reviewedPlan,
                            CreateChangePlan(paths, session, outputMode)));
            }

            if (conversionBytes is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.EvolutionItemConversionArray,
                    outputMode,
                    isolateTrinityModManagerRomFs:
                        dexApply.ChangesRegularCount
                        && outputMode == ZaOutputMode.TrinityModManager));
            }

            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                ZaDataPaths.PersonalArray,
                outputMode,
                isolateTrinityModManagerRomFs:
                    dexApply.ChangesRegularCount
                    && outputMode == ZaOutputMode.TrinityModManager));
            if (contentsBytes is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.PokedexContentsData,
                    outputMode,
                    isolateTrinityModManagerRomFs:
                        dexApply.ChangesRegularCount
                        && outputMode == ZaOutputMode.TrinityModManager));
            }

            if (megaContentsBytes is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.PokedexMegaContentsData,
                    outputMode,
                    isolateTrinityModManagerRomFs:
                        dexApply.ChangesRegularCount
                        && outputMode == ZaOutputMode.TrinityModManager));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            if (dexApply.ChangesRegularCount)
            {
                writtenFiles.Add(new ProjectFileReference(
                    ProjectFileLayer.Generated,
                    ZaExeFsReservedRegionLedger.ExeFsMainPath));
            }

            pokemonWorkflowService.ClearMemoryCache();
            var applyMessage = dexApply.ChangesRegularCount
                ? outputMode switch
                {
                    ZaOutputMode.Standalone =>
                        "Pokemon Data RomFS output was written as a standalone LayeredFS override with a patched descriptor, and executable output was written to exefs/main.",
                    ZaOutputMode.TrinityModManager =>
                        "Pokemon Data RomFS output was written under trinity-mod-manager-romfs for Trinity Mod Manager, and executable output was written to exefs/main.",
                    ZaOutputMode.TrinityBypass =>
                        "Pokemon Data RomFS output was written in Trinity bypass layout, and executable output was composed into the existing exefs/main.",
                    _ => ZaEditSessionSupport.CreateApplyOutputMessage(
                        "Pokemon Data",
                        outputMode),
                }
                : ZaEditSessionSupport.CreateApplyOutputMessage(
                    "Pokemon Data",
                    outputMode);
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                applyMessage,
                ZaEditSessionSupport.PokemonDomain));
        }
        catch (ZaOutputApplyNotCommittedException exception)
        {
            outputTransaction = exception.Result;
            writtenFiles.Clear();
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data output could not be committed atomically: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                expected: "A committed Pokemon Data output transaction"));
        }
        catch (Exception exception) when (!ZaEditSessionSupport.IsOutputSafetyException(exception))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data output could not be written: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                expected: "Readable Pokemon and Pokédex sources with a writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles,
            diagnostics,
            outputTransaction);
    }

    private ApplyResult ApplyMegaDexSyncChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaOutputMode outputMode)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        OutputApplyResult? outputTransaction = null;

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                ZaEditSessionSupport.PokemonDomain,
                expected: "Current reviewed Mega Pokédex synchronization plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var contentsSource = fileSource.Read(project, ZaDataPaths.PokedexContentsData);
            var megaContentsSource = fileSource.Read(project, ZaDataPaths.PokedexMegaContentsData);
            var contents = ZaPokedexContentsTable.Read(contentsSource.Bytes);
            var megaContents = ZaPokedexMegaContentsTable.Read(megaContentsSource.Bytes);
            var groupsBySpecies = contents.Rows.ToDictionary(
                row => row.Species,
                row => (ZaPokedexContentsGroup)row.Group);
            var output = megaContents.WriteSpeciesGroups(groupsBySpecies);

            outputTransaction = ZaWorkflowFileSource.WriteBatch(
                paths,
                [new ZaWorkflowFileWrite(ZaDataPaths.PokedexMegaContentsData, output)],
                outputMode,
                revalidateReviewedState: () =>
                    ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                        reviewedPlan,
                        CreateChangePlan(paths, session, outputMode)));
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                ZaDataPaths.PokedexMegaContentsData,
                outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            pokemonWorkflowService.ClearMemoryCache();
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage(
                    "Mega Pokédex synchronization",
                    outputMode),
                ZaEditSessionSupport.PokemonDomain));
        }
        catch (ZaOutputApplyNotCommittedException exception)
        {
            outputTransaction = exception.Result;
            writtenFiles.Clear();
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Mega Pokédex synchronization output could not be committed atomically: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                expected: "A committed Mega Pokédex synchronization output transaction"));
        }
        catch (Exception exception) when (!ZaEditSessionSupport.IsOutputSafetyException(exception))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Mega Pokédex synchronization output could not be written: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                expected: "Readable current Pokédex membership tables with a writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles,
            diagnostics,
            outputTransaction);
    }

    private ApplyResult ApplyAlphaAwareChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaOutputMode outputMode)
    {
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
                expected: "Current reviewed Pokemon Data change plan",
                code: ZaPokemonDiagnosticCodes.AlphaPlanStale));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        var alphaEdits = session.PendingEdits.Where(IsAlphaMoveEdit).ToArray();
        var ordinaryEdits = session.PendingEdits
            .Where(edit => !IsAlphaMoveEdit(edit) && !IsDexPlacementEdit(edit))
            .ToArray();
        var wrotePersonal = false;
        var wroteConversion = false;
        var planBecameStale = false;
        var outputVerificationFailed = false;
        try
        {
            ZaWorkflowFileSource.ApplyHybridMixedBatch(
                paths,
                outputMode,
                isolateTrinityModManagerRomFs: false,
                () =>
                {
                    var lockedPlan = CreateChangePlan(paths, session, outputMode);
                    if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, lockedPlan))
                    {
                        planBecameStale = true;
                        throw new InvalidDataException(
                            "Reviewed alpha-exclusive move change plan became stale before the output lock was acquired.");
                    }

                    currentPlan = lockedPlan;
                    var project = projectWorkspaceService.Open(paths);

                    var alphaSource = fileSource.Read(project, ZaDataPaths.AlphaMoveTable);
                    var alphaDocument = ZaAlphaMoveTableDocument.Parse(alphaSource.Bytes);
                    var replacements = new List<ZaAlphaMoveReplacement>(alphaEdits.Length);
                    foreach (var edit in alphaEdits)
                    {
                        if (!TryParseAlphaMoveRecordId(edit.RecordId, out var speciesId, out var formId)
                            || !ushort.TryParse(
                                edit.NewValue,
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out var moveId))
                        {
                            throw new InvalidDataException(
                                "A staged alpha-exclusive move replacement is malformed.");
                        }

                        replacements.Add(new ZaAlphaMoveReplacement(
                            checked((ushort)speciesId),
                            checked((ushort)formId),
                            moveId));
                    }

                    if (!alphaDocument.TryApplyReplacements(
                            replacements,
                            out var alphaOutput,
                            out var alphaError))
                    {
                        outputVerificationFailed = true;
                        throw new InvalidDataException(
                            alphaError ?? "The alpha-exclusive move table could not be patched safely.");
                    }

                    var outputWrites = new List<ZaWorkflowFileWrite>();
                    if (ordinaryEdits.Length > 0)
                    {
                        var personalSource = fileSource.Read(project, ZaDataPaths.PersonalArray);
                        var personalArray = ReadRows(
                            project,
                            personalSource,
                            includeBaseRows: NeedsBaseRows(ordinaryEdits));
                        var rows = personalArray.Rows;
                        var conversionState = ZaEvolutionItemConversionState.Load(project, fileSource);
                        var migratedLegacyArguments = PrepareEvolutionItemConversions(
                            rows,
                            ordinaryEdits,
                            conversionState);
                        var requiresRebuild = personalArray.RequiresLegacyDexOrderRepair
                            || RequiresPersonalArrayRebuild(rows, ordinaryEdits)
                            || migratedLegacyArguments
                            || RequiresEncodedEvolutionRebuild(ordinaryEdits);
                        foreach (var edit in OrderPersonalEditsForApply(ordinaryEdits))
                        {
                            ApplyEdit(
                                rows,
                                personalArray.BaseRows ?? Array.Empty<PersonalRow>(),
                                edit,
                                conversionState,
                                diagnostics);
                        }

                        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                        {
                            throw new InvalidDataException(
                                "A staged Pokemon Data edit failed final validation under the output lock.");
                        }

                        var personalOutput = requiresRebuild
                            ? WriteRows(rows)
                            : ApplyPersonalArrayBinaryPatch(
                                personalSource.Bytes,
                                ordinaryEdits,
                                diagnostics);
                        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                        {
                            throw new InvalidDataException(
                                "Pokemon personal data could not be patched under the output lock.");
                        }

                        if (conversionState.Modified)
                        {
                            outputWrites.Add(new ZaWorkflowFileWrite(
                                ZaDataPaths.EvolutionItemConversionArray,
                                conversionState.Write()));
                            wroteConversion = true;
                        }

                        outputWrites.Add(new ZaWorkflowFileWrite(
                            ZaDataPaths.PersonalArray,
                            personalOutput));
                        wrotePersonal = true;
                    }

                    outputWrites.Add(new ZaWorkflowFileWrite(
                        ZaDataPaths.AlphaMoveTable,
                        alphaOutput));
                    return new ZaStandaloneMixedBatch(
                        outputWrites,
                        Array.Empty<string>(),
                        Array.Empty<ZaStandaloneOutputMutation>());
                },
                revalidateReviewedState: () =>
                    ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                        reviewedPlan,
                        CreateChangePlan(paths, session, outputMode)));

            if (wroteConversion)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.EvolutionItemConversionArray,
                    outputMode));
            }

            if (wrotePersonal)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.PersonalArray,
                    outputMode));
            }

            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                ZaDataPaths.AlphaMoveTable,
                outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            pokemonWorkflowService.ClearMemoryCache();
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage(
                    ordinaryEdits.Length == 0
                        ? "Pokemon alpha-exclusive moves"
                        : "Pokemon Data and alpha-exclusive moves",
                    outputMode),
                ZaEditSessionSupport.PokemonDomain));
        }
        catch (Exception exception) when (!ZaEditSessionSupport.IsOutputSafetyException(exception))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Alpha-exclusive move output could not be written: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                file: $"romfs/{ZaDataPaths.AlphaMoveTable}",
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "Unchanged verified sources and a writable output root",
                code: planBecameStale
                    ? ZaPokemonDiagnosticCodes.AlphaPlanStale
                    : outputVerificationFailed
                        ? ZaPokemonDiagnosticCodes.AlphaOutputVerificationFailed
                        : ZaPokemonDiagnosticCodes.AlphaApplyFailed));
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles,
            diagnostics);
    }

    private static PendingEdit? CreateFieldPendingEdit(
        ZaPokemonWorkflow workflow,
        ZaPokemonRecord pokemon,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (string.Equals(
                normalizedField,
                ZaPokemonWorkflowService.AlphaMoveField,
                StringComparison.Ordinal))
        {
            var alphaMove = pokemon.AlphaMove;
            if (alphaMove is null || !alphaMove.HasMapping || !alphaMove.CanEdit)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    alphaMove?.BlockedReason
                        ?? "Alpha-exclusive move editing is not available for this Pokemon.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: normalizedField,
                    expected: "Verified existing alpha-exclusive move mapping",
                    code: ZaPokemonDiagnosticCodes.AlphaMappingUnavailable));
                return null;
            }

            var parsedMoveId = ZaEditSessionSupport.TryParseInt(
                value,
                1,
                ushort.MaxValue,
                normalizedField,
                ZaEditSessionSupport.PokemonDomain,
                diagnostics);
            if (parsedMoveId is null)
            {
                return null;
            }

            var selectedOption = alphaMove.Options.FirstOrDefault(option => option.Value == parsedMoveId.Value);
            if (selectedOption is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "The selected alpha-exclusive move is not a verified TM-compatible move with Plus data for this species and form.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: normalizedField,
                    expected: "One of the available alpha-exclusive move options",
                    code: ZaPokemonDiagnosticCodes.AlphaSelectionInvalid));
                return null;
            }

            if (alphaMove.MoveId == parsedMoveId.Value)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "The selected alpha-exclusive move is already active.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: normalizedField,
                    expected: "A different verified alpha-exclusive move",
                    code: ZaPokemonDiagnosticCodes.AlphaSelectionInvalid));
                return null;
            }

            return new PendingEdit(
                ZaEditSessionSupport.PokemonDomain,
                $"Set {pokemon.Name} alpha-exclusive move to {selectedOption.Label}.",
                alphaMove.EditSources,
                CreateAlphaMoveRecordId(pokemon.SpeciesId, pokemon.Form),
                normalizedField,
                parsedMoveId.Value.ToString(CultureInfo.InvariantCulture));
        }

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

        if (string.Equals(
                normalizedField,
                ZaPokemonWorkflowService.BaseExperienceField,
                StringComparison.Ordinal)
            && !ValidateBaseExperienceValue(pokemon, parsed.Value, diagnostics))
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

    private static void ValidateGlobalYieldSessionInteractions(
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!session.PendingEdits.Any(IsGlobalYieldRestoreEdit)
            || !session.PendingEdits.Any(edit => string.Equals(
                    edit.Domain,
                    ZaEditSessionSupport.PokemonDomain,
                    StringComparison.Ordinal)
                && string.Equals(
                    edit.Field,
                    ZaPokemonWorkflowService.FormField,
                    StringComparison.Ordinal)))
        {
            return;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pokemon Form changes and vanilla yield restores must be applied separately so every restored value remains bound to the verified species and form.",
            ZaEditSessionSupport.PokemonDomain,
            field: ZaPokemonWorkflowService.FormField,
            expected: "Apply or discard Form changes before staging Restore EXP Yield or Restore EV Yield"));
    }

    private static PendingEdit? CreateGlobalYieldPendingEdit(
        ZaPokemonWorkflow workflow,
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
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon yield action '{normalizedValue}' is not supported.",
                ZaEditSessionSupport.PokemonDomain,
                field: normalizedField,
                expected: "remove or restore"));
            return null;
        }

        if (string.Equals(normalizedValue, RestoreYieldValue, StringComparison.Ordinal)
            && workflow.Pokemon.Any(pokemon => pokemon.VanillaYieldDefaults is null))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon yield restore requires matching clean base yield data for every loaded Pokemon row.",
                ZaEditSessionSupport.PokemonDomain,
                field: normalizedField,
                expected: "Verified clean base Pokemon personal data with matching species and forms"));
            return null;
        }

        var sources = CreateGlobalYieldSources(workflow, normalizedValue);
        if (sources.Count == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon yield editing requires a verified personal-data source.",
                ZaEditSessionSupport.PokemonDomain,
                field: normalizedField,
                expected: "Verified current Pokemon personal data"));
            return null;
        }

        var target = string.Equals(normalizedField, GlobalEvYieldField, StringComparison.Ordinal)
            ? "EV yield"
            : "EXP yield";
        var action = string.Equals(normalizedValue, RemoveYieldValue, StringComparison.Ordinal)
            ? "Remove"
            : "Restore";

        return new PendingEdit(
            ZaEditSessionSupport.PokemonDomain,
            $"{action} all Pokemon {target}.",
            sources,
            GlobalRecordId,
            normalizedField,
            normalizedValue);
    }

    private static IReadOnlyList<ProjectFileReference> CreateGlobalYieldSources(
        ZaPokemonWorkflow workflow,
        string action)
    {
        var provenance = workflow.Pokemon.FirstOrDefault()?.Provenance;
        if (provenance is null)
        {
            return Array.Empty<ProjectFileReference>();
        }

        var effective = new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
        if (!string.Equals(action, RestoreYieldValue, StringComparison.Ordinal))
        {
            return [effective];
        }

        var baseSource = new ProjectFileReference(
            ProjectFileLayer.Base,
            $"romfs/{ZaDataPaths.PersonalArray}");
        return effective == baseSource ? [effective] : [effective, baseSource];
    }

    private static bool ValidateBaseExperienceValue(
        ZaPokemonRecord pokemon,
        int baseExperience,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (ZaPokemonExperience.TryCalculateExpAddend(
                pokemon.BaseStats.Total,
                pokemon.EvolutionStage,
                baseExperience,
                out _))
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Base EXP cannot be represented by Pokemon Legends Z-A's stored EXP addend for this Pokemon's current stats and evolution stage.",
            ZaEditSessionSupport.PokemonDomain,
            field: ZaPokemonWorkflowService.BaseExperienceField,
            expected: "Base EXP that maps to a signed 16-bit Z-A addend"));
        return false;
    }

    private void ValidateGlobalYieldEdit(
        OpenedProject project,
        ZaPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsGlobalYieldAction(edit.NewValue))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon yield edit uses an invalid action.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "remove or restore"));
            return;
        }

        var expectedSources = CreateGlobalYieldSources(workflow, edit.NewValue!);
        if (!edit.Sources.SequenceEqual(expectedSources))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon yield sources do not match the current personal data and required clean base data.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Current effective personal source and clean base source for restore"));
            return;
        }

        if (IsGlobalExpYieldEdit(edit)
            && string.Equals(edit.NewValue, RemoveYieldValue, StringComparison.Ordinal))
        {
            foreach (var pokemon in workflow.Pokemon)
            {
                ValidateBaseExperienceValue(pokemon, 0, diagnostics);
            }

            return;
        }

        if (!string.Equals(edit.NewValue, RestoreYieldValue, StringComparison.Ordinal))
        {
            return;
        }

        IReadOnlyList<PersonalRow> baseRows;
        try
        {
            baseRows = ReadVerifiedBaseRows(project, workflow);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or UnauthorizedAccessException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon yield restore could not verify clean base personal data: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                file: $"romfs/{ZaDataPaths.PersonalArray}",
                field: edit.Field,
                expected: "Matching clean base Pokemon personal data"));
            return;
        }

        if (!IsGlobalExpYieldEdit(edit))
        {
            return;
        }

        foreach (var pokemon in workflow.Pokemon)
        {
            var baseRow = baseRows[pokemon.PersonalId];
            var vanillaBaseExperience = ZaPokemonExperience.CalculateBaseExperience(
                CalculateBaseStatTotal(baseRow.BaseStats),
                baseRow.EvoStage,
                baseRow.ExpAddend);
            ValidateBaseExperienceValue(pokemon, vanillaBaseExperience, diagnostics);
        }
    }

    private void ValidatePendingEdit(
        OpenedProject project,
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

        if (IsGlobalYieldEdit(edit))
        {
            ValidateGlobalYieldEdit(project, workflow, edit, diagnostics);
            return;
        }

        if (IsMegaDexSyncEdit(edit))
        {
            ValidateMegaDexSyncEdit(workflow, edit, diagnostics);
            return;
        }

        if (IsDexPlacementEdit(edit))
        {
            ValidateDexPlacementEdit(project, workflow, edit, diagnostics);
            return;
        }

        if (IsAlphaMoveEdit(edit)
            || string.Equals(
                edit.Field,
                ZaPokemonWorkflowService.AlphaMoveField,
                StringComparison.Ordinal)
            || edit.RecordId?.StartsWith(AlphaMoveRecordIdPrefix, StringComparison.Ordinal) == true)
        {
            ValidateAlphaMoveEdit(workflow, edit, diagnostics);
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

        var parsed = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.PokemonDomain,
            diagnostics);
        if (parsed is not null
            && string.Equals(
                edit.Field,
                ZaPokemonWorkflowService.BaseExperienceField,
                StringComparison.Ordinal))
        {
            ValidateBaseExperienceValue(pokemon, parsed.Value, diagnostics);
        }
    }

    private static void ValidateAlphaMoveEdit(
        ZaPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(
                edit.Field,
                ZaPokemonWorkflowService.AlphaMoveField,
                StringComparison.Ordinal)
            || !TryParseAlphaMoveRecordId(edit.RecordId, out var speciesId, out var formId))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending alpha-exclusive move data is malformed or non-canonical.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "Canonical existing species and form mapping",
                code: ZaPokemonDiagnosticCodes.AlphaSelectionInvalid));
            return;
        }

        var pokemon = workflow.Pokemon
            .Where(candidate => candidate.SpeciesId == speciesId && candidate.Form == formId)
            .ToArray();
        var unavailable = pokemon.FirstOrDefault(candidate =>
            candidate.AlphaMove is null
            || !candidate.AlphaMove.HasMapping
            || !candidate.AlphaMove.CanEdit);
        if (pokemon.Length == 0 || unavailable is not null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                unavailable?.AlphaMove?.BlockedReason
                    ?? "The pending alpha-exclusive move mapping is not available in the loaded Pokemon Data.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "Verified existing alpha-exclusive move mapping",
                code: ZaPokemonDiagnosticCodes.AlphaMappingUnavailable));
            return;
        }

        var alphaMoves = pokemon.Select(candidate => candidate.AlphaMove!).ToArray();
        var actualSources = edit.Sources
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (alphaMoves.Any(alphaMove => !alphaMove.EditSources
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .SequenceEqual(actualSources)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending alpha-exclusive move sources do not exactly match the loaded mapping and validation data.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "Current alpha table, Pokemon compatibility, TM catalog, and Plus move sources",
                code: ZaPokemonDiagnosticCodes.AlphaPlanVerificationFailed));
            return;
        }

        if (alphaMoves.Select(alphaMove => alphaMove.MoveId).Distinct().Count() != 1)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The loaded Pokemon records disagree about the active alpha-exclusive move mapping for this species and form.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "One consistent active alpha-exclusive move mapping",
                code: ZaPokemonDiagnosticCodes.AlphaPlanVerificationFailed));
            return;
        }

        var moveId = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            1,
            ushort.MaxValue,
            edit.Field,
            ZaEditSessionSupport.PokemonDomain,
            diagnostics);
        if (moveId is null)
        {
            return;
        }

        if (alphaMoves.Any(alphaMove => !alphaMove.Options.Any(option => option.Value == moveId.Value)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The pending alpha-exclusive move is not a verified TM-compatible move with Plus data for this species and form.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "One of the available alpha-exclusive move options",
                code: ZaPokemonDiagnosticCodes.AlphaSelectionInvalid));
        }
        else if (alphaMoves[0].MoveId == moveId.Value)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The pending alpha-exclusive move does not change the active mapping.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.AlphaMoveField,
                expected: "A different verified alpha-exclusive move",
                code: ZaPokemonDiagnosticCodes.AlphaSelectionInvalid));
        }
    }

    private static ZaPokemonWorkflow OverlayPendingEdits(ZaPokemonWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        return CreatePokemonWorkflowOverlay(workflow, edits).Workflow;
    }

    private static PokemonWorkflowOverlay CreatePokemonWorkflowOverlay(
        ZaPokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var overlay = new PokemonWorkflowOverlay(workflow);
        foreach (var edit in OrderPersonalEditsForApply(edits))
        {
            overlay.Apply(edit);
        }

        return overlay;
    }

    private sealed class PokemonWorkflowOverlay
    {
        private ZaPokemonRecord[] pokemon;
        private Dictionary<int, int> pokemonIndices;

        public PokemonWorkflowOverlay(ZaPokemonWorkflow workflow)
        {
            pokemon = [];
            pokemonIndices = [];
            Reset(workflow);
        }

        public ZaPokemonWorkflow Workflow { get; private set; } = null!;

        public ZaPokemonRecord? FindPokemon(int personalId)
        {
            return pokemonIndices.TryGetValue(personalId, out var index)
                ? pokemon[index]
                : null;
        }

        public void Apply(PendingEdit edit)
        {
            if (string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
                && int.TryParse(
                    edit.RecordId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var personalId)
                && pokemonIndices.TryGetValue(personalId, out var index))
            {
                pokemon[index] = OverlayPokemon(Workflow, pokemon[index], edit);
                return;
            }

            var updatedWorkflow = OverlayPendingEdit(Workflow, edit);
            if (!ReferenceEquals(updatedWorkflow, Workflow))
            {
                Reset(updatedWorkflow);
            }
        }

        private void Reset(ZaPokemonWorkflow workflow)
        {
            pokemon = workflow.Pokemon.ToArray();
            pokemonIndices = new Dictionary<int, int>(pokemon.Length);
            for (var index = 0; index < pokemon.Length; index++)
            {
                pokemonIndices.TryAdd(pokemon[index].PersonalId, index);
            }

            Workflow = workflow with { Pokemon = pokemon };
        }
    }

    private static ZaPokemonWorkflow OverlayPendingEdit(ZaPokemonWorkflow workflow, PendingEdit edit)
    {
        if (IsMegaDexSyncEdit(edit))
        {
            return workflow.DexEditor is null
                ? workflow
                : workflow with
                {
                    DexEditor = workflow.DexEditor with
                    {
                        CanSyncMegasToRegular = false,
                        IsVanillaLayout = false,
                    },
                };
        }

        if (IsDexPlacementEdit(edit))
        {
            return OverlayDexPlacement(workflow, edit);
        }

        if (IsAlphaMoveEdit(edit)
            && TryParseAlphaMoveRecordId(edit.RecordId, out var speciesId, out var formId)
            && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId))
        {
            return workflow with
            {
                Pokemon = workflow.Pokemon
                    .Select(pokemon =>
                    {
                        if (pokemon.SpeciesId != speciesId
                            || pokemon.Form != formId
                            || pokemon.AlphaMove is not { } alphaMove)
                        {
                            return pokemon;
                        }

                        var option = alphaMove.Options.FirstOrDefault(candidate => candidate.Value == moveId);
                        if (option is null)
                        {
                            return pokemon;
                        }

                        var differsFromVanilla = alphaMove.VanillaMoveId is not null
                            && moveId != alphaMove.VanillaMoveId.Value;
                        var vanillaIsSafe = alphaMove.VanillaMoveId is not null
                            && alphaMove.Options.Any(candidate =>
                                candidate.Value == alphaMove.VanillaMoveId.Value);
                        return pokemon with
                        {
                            AlphaMove = alphaMove with
                            {
                                MoveId = moveId,
                                MoveName = option.Label,
                                DiffersFromVanilla = differsFromVanilla,
                                CanRevertToVanilla = differsFromVanilla && vanillaIsSafe,
                                RestoreBlockedReason = differsFromVanilla && vanillaIsSafe
                                    ? null
                                    : !differsFromVanilla
                                        ? "This mapping already matches vanilla."
                                        : "The vanilla move is not currently a verified TM-compatible move with Plus data.",
                            },
                        };
                    })
                    .ToArray(),
            };
        }

        if (!string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal))
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

    private static void ValidateMegaDexSyncEdit(
        ZaPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editor = workflow.DexEditor;
        if (editor is null
            || !editor.CanEdit
            || editor.ContentsProvenance is null
            || editor.MegaContentsProvenance is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor?.BlockedReason
                    ?? "Mega Pokédex synchronization is not available for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified current Pokédex and Mega Pokédex membership data"));
            return;
        }

        if (!string.Equals(edit.NewValue, MegaDexSyncValue, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Mega Pokédex synchronization data is malformed.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Canonical Mega Pokédex synchronization request"));
            return;
        }

        if (!editor.CanSyncMegasToRegular)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Mega Pokédex synchronization no longer changes any membership entry.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "At least one current Mega Pokédex membership mismatch"));
            return;
        }

        var expectedSources = CreateMegaDexSyncSources(editor)
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var actualSources = edit.Sources
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (!actualSources.SequenceEqual(expectedSources))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Mega Pokédex synchronization sources do not exactly match the loaded Pokédex membership tables.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Current effective Pokédex and Mega Pokédex membership sources"));
        }
    }

    private void ValidateDexPlacementEdit(
        OpenedProject project,
        ZaPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editor = workflow.DexEditor;
        if (editor is null
            || !editor.CanEdit
            || editor.PersonalProvenance is null
            || editor.ContentsProvenance is null
            || editor.MegaContentsProvenance is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor?.BlockedReason ?? "Pokédex placement is not available for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified active Pokédex placement data"));
            return;
        }

        var isVanillaRestore = IsVanillaDexPlacementEdit(edit);
        if ((!string.Equals(edit.RecordId, DexPlacementRecordId, StringComparison.Ordinal)
                && !string.Equals(edit.RecordId, DexLayoutRecordId, StringComparison.Ordinal)
                && !isVanillaRestore)
            || !TryDecodeDexPlacementPayload(edit.NewValue, out var payload)
            || !IsCanonicalDexPlacementPayload(edit.NewValue, payload))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement data is malformed or non-canonical.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Canonical complete Pokédex placement state"));
            return;
        }

        var targetRegularCount = payload.RegularCount ?? editor.RegularCount;
        var assignments = payload.Assignments;
        var targetChangesRegularCount = targetRegularCount != editor.RegularCount;
        if (targetChangesRegularCount
            && (!editor.CanEditAdvanced || editor.ExecutableProvenance is null))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                editor.AdvancedBlockedReason
                    ?? "Changing the Regular and Hyperspace Pokédex sizes is unavailable for this project.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Verified matching Pokemon Legends Z-A exefs/main"));
            return;
        }

        ZaDexLayoutState? vanilla = null;
        if (isVanillaRestore)
        {
            try
            {
                vanilla = ReadVerifiedVanillaDexLayout(project, editor);
            }
            catch (Exception exception) when (
                exception is IOException
                    or InvalidDataException
                    or InvalidOperationException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException
                    or OverflowException)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending Return to Vanilla could not verify the base Dex Layout: {exception.Message}",
                    ZaEditSessionSupport.PokemonDomain,
                    field: ZaPokemonWorkflowService.DexPlacementField,
                    expected: "Verified base Dex Layout"));
                return;
            }

            if (!DexPlacementStatesEqual(
                    assignments,
                    targetRegularCount,
                    vanilla.Assignments,
                    vanilla.RegularCount))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Return to Vanilla does not exactly match the verified base Dex Layout.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: ZaPokemonWorkflowService.DexPlacementField,
                    expected: "Exact base ordering, membership, and sizes"));
                return;
            }
        }

        var expectedSources = (isVanillaRestore
                ? CreateVanillaDexPlacementSources(
                    project,
                    editor,
                    vanilla!,
                    targetChangesRegularCount)
                : CreateDexPlacementSources(
                    editor,
                    includeExecutable: targetChangesRegularCount))
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var actualSources = edit.Sources
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (actualSources.Length != expectedSources.Length
            || !actualSources.SequenceEqual(expectedSources))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement sources do not exactly match the loaded personal, contents, Mega contents, and required executable data.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: targetChangesRegularCount
                    ? "Current effective personal, contents, Mega contents, and exefs/main sources"
                    : "Current effective personal, contents, and Mega contents sources"));
            return;
        }

        var expectedSpecies = editor.Placements
            .Select(placement => placement.SpeciesId)
            .Order()
            .ToArray();
        var actualSpecies = assignments.Keys.Order().ToArray();
        var expectedIndices = Enumerable.Range(1, expectedSpecies.Length).ToArray();
        var actualIndices = assignments.Values.Order().ToArray();
        if (!actualSpecies.SequenceEqual(expectedSpecies)
            || !actualIndices.SequenceEqual(expectedIndices)
            || expectedSpecies.Length > ushort.MaxValue
            || targetRegularCount <= 0
            || targetRegularCount >= expectedSpecies.Length)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement must preserve every active species, every contiguous unique slot, and two non-empty Pokédexes.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Complete one-to-one active Pokédex assignment with a Regular boundary from 1 through species count minus 1"));
            return;
        }

        var currentAssignments = editor.Placements.ToDictionary(
            placement => placement.SpeciesId,
            placement => placement.InternalIndex);
        if (!isVanillaRestore
            && DexPlacementStatesEqual(
                assignments,
                targetRegularCount,
                currentAssignments,
                editor.RegularCount))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement does not change any species slot or Pokédex boundary.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "At least one staged placement change"));
        }
    }

    private ChangePlan CreateDexAwareChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<ValidationDiagnostic> validationDiagnostics,
        ZaOutputMode outputMode)
    {
        var diagnostics = validationDiagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Pokemon Data edit before reviewing a change plan.",
                ZaEditSessionSupport.PokemonDomain,
                expected: "Pending Pokemon Data edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var workflow = pokemonWorkflowService.Load(project);
            var dexEdit = session.PendingEdits.Single(IsDexPlacementEdit);
            if (IsMegaDexSyncEdit(dexEdit))
            {
                var megaWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    ZaDataPaths.PokedexMegaContentsData,
                    dexEdit.Sources,
                    outputMode,
                    isolateTrinityModManagerRomFs: false);
                var megaWrite = new PlannedFileWrite(
                    megaWriteInfo.TargetRelativePath,
                    megaWriteInfo.Sources,
                    megaWriteInfo.ReplacesExistingOutput,
                    "Sync every Mega Pokédex entry to its species' current Regular or Hyperspace Pokédex membership.");
                if (outputMode != ZaOutputMode.Standalone)
                {
                    return new ChangePlan(session.Id, [megaWrite], diagnostics);
                }

                var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                var descriptorWrite = new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Update the standalone LayeredFS descriptor for the staged Mega Pokédex synchronization.");
                return new ChangePlan(session.Id, [megaWrite, descriptorWrite], diagnostics);
            }

            var changesRegularCount = DexPlacementChangesRegularCount(workflow, dexEdit);
            var isolateTrinityModManagerRomFs = changesRegularCount
                && outputMode == ZaOutputMode.TrinityModManager;
            if (isolateTrinityModManagerRomFs
                && fileSource.TryFindLegacyBareTrinityModManagerOutput(
                    project,
                    [
                        ZaDataPaths.PersonalArray,
                        ZaDataPaths.PokedexContentsData,
                        ZaDataPaths.PokedexMegaContentsData,
                    ],
                    out var legacyBareRelativePath))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "This Output Root contains legacy bare Trinity Mod Manager RomFS files and cannot safely contain the required exefs/main beside them. Choose a clean or container Output Root; KM writes the Trinity Mod Manager RomFS package inside trinity-mod-manager-romfs.",
                    ZaEditSessionSupport.PokemonDomain,
                    file: legacyBareRelativePath,
                    field: ZaPokemonWorkflowService.DexPlacementField,
                    expected: "Clean or container Output Root without legacy bare Trinity Mod Manager RomFS files"));
                return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
            }

            if (changesRegularCount && outputMode == ZaOutputMode.TrinityBypass)
            {
                var layeredBypassMainPath = ZaExeFsMainFileResolver.ResolveOutputPath(paths);
                if (layeredBypassMainPath is null || !File.Exists(layeredBypassMainPath))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Trinity bypass Dex Layout output requires an existing layered exefs/main in the configured Output Root so KM can preserve the installed bypass. Install the bypass first or choose another output mode.",
                        ZaEditSessionSupport.PokemonDomain,
                        file: ZaExeFsReservedRegionLedger.ExeFsMainPath,
                        field: ZaPokemonWorkflowService.DexPlacementField,
                        expected: "Existing Output Root exefs/main containing the installed Trinity bypass"));
                    return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
                }
            }

            var writes = new List<PlannedFileWrite>();
            var personalSources = session.PendingEdits
                .SelectMany(edit => edit.Sources)
                .Distinct()
                .ToArray();
            var personalWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.PersonalArray,
                personalSources,
                outputMode,
                isolateTrinityModManagerRomFs);
            writes.Add(new PlannedFileWrite(
                personalWriteInfo.TargetRelativePath,
                personalWriteInfo.Sources,
                personalWriteInfo.ReplacesExistingOutput,
                session.PendingEdits.Count == 1
                    ? $"Apply pending Pokemon Data edit: {dexEdit.Summary}"
                    : $"Apply {session.PendingEdits.Count.ToString(CultureInfo.InvariantCulture)} pending Pokemon Data edits."));

            if (DexPlacementChangesGroups(workflow, dexEdit))
            {
                var contentsWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    ZaDataPaths.PokedexContentsData,
                    dexEdit.Sources,
                    outputMode,
                    isolateTrinityModManagerRomFs);
                writes.Add(new PlannedFileWrite(
                    contentsWriteInfo.TargetRelativePath,
                    contentsWriteInfo.Sources,
                    contentsWriteInfo.ReplacesExistingOutput,
                    "Update Regular and Hyperspace Pokédex membership for the staged placement change."));
            }

            var megaContentsWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.PokedexMegaContentsData,
                dexEdit.Sources,
                outputMode,
                isolateTrinityModManagerRomFs);
            writes.Add(new PlannedFileWrite(
                megaContentsWriteInfo.TargetRelativePath,
                megaContentsWriteInfo.Sources,
                megaContentsWriteInfo.ReplacesExistingOutput,
                IsVanillaDexPlacementEdit(dexEdit)
                    ? "Restore the vanilla Mega Pokédex membership table."
                    : "Mirror Mega Pokédex availability to the staged Regular and Hyperspace Pokédex membership."));

            if (changesRegularCount)
            {
                var mainSource = ZaExeFsMainFileResolver.ResolveEffective(project)
                    ?? throw new FileNotFoundException(
                        "Pokemon Legends Z-A exefs/main is no longer available.");
                var outputPath = ZaExeFsMainFileResolver.ResolveOutputPath(paths)
                    ?? throw new InvalidOperationException(
                        "Pokemon Legends Z-A output exefs/main target could not be resolved.");
                var mainBytes = File.ReadAllBytes(mainSource.AbsolutePath);
                writes.Add(new PlannedFileWrite(
                    ZaExeFsReservedRegionLedger.ExeFsMainPath,
                    [mainSource.Reference],
                    File.Exists(outputPath),
                    "Update the runtime Regular Dex boundary used by every verified Pokédex number normalization site.",
                    CreateSourceFingerprint(mainBytes)));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides."));
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Change plan preview contains {writes.Count.ToString(CultureInfo.InvariantCulture)} target files.",
                ZaEditSessionSupport.PokemonDomain));
            return new ChangePlan(session.Id, writes, diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data change plan could not resolve Pokédex targets: {exception.Message}",
                ZaEditSessionSupport.PokemonDomain,
                expected: "Verified Pokédex sources and writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
    }

    private static bool DexPlacementChangesGroups(
        ZaPokemonWorkflow workflow,
        PendingEdit edit)
    {
        var editor = workflow.DexEditor;
        if (editor is null
            || !editor.CanEdit
            || !TryDecodeDexPlacementPayload(edit.NewValue, out var payload))
        {
            throw new InvalidDataException(
                "The staged Pokédex placement cannot be compared with the verified source mapping.");
        }

        var assignments = payload.Assignments;
        var targetRegularCount = payload.RegularCount ?? editor.RegularCount;
        var placementBySpecies = editor.Placements.ToDictionary(
            placement => placement.SpeciesId);
        if (assignments.Count != placementBySpecies.Count
            || assignments.Keys.Any(speciesId => !placementBySpecies.ContainsKey(speciesId))
            || !assignments.Values
                .Order()
                .SequenceEqual(Enumerable.Range(1, placementBySpecies.Count))
            || targetRegularCount <= 0
            || targetRegularCount >= placementBySpecies.Count)
        {
            throw new InvalidDataException(
                "The staged Pokédex placement does not preserve the verified active slot mapping.");
        }

        return assignments.Any(pair =>
            !string.Equals(
                placementBySpecies[pair.Key].DexKind,
                GetDexKindForIndex(pair.Value, targetRegularCount),
                StringComparison.Ordinal));
    }

    private static bool DexPlacementChangesRegularCount(
        ZaPokemonWorkflow workflow,
        PendingEdit edit)
    {
        var editor = workflow.DexEditor;
        if (editor is null
            || !editor.CanEdit
            || !TryDecodeDexPlacementPayload(edit.NewValue, out var payload))
        {
            throw new InvalidDataException(
                "The staged Pokédex placement boundary cannot be compared with the verified source mapping.");
        }

        return (payload.RegularCount ?? editor.RegularCount) != editor.RegularCount;
    }

    private static DexPlacementApplyResult ApplyDexPlacement(
        IReadOnlyList<PersonalRow> rows,
        ZaPokedexContentsTable contents,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryDecodeDexPlacementPayload(edit.NewValue, out var payload)
            || !IsCanonicalDexPlacementPayload(edit.NewValue, payload))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement data is malformed or non-canonical.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Canonical complete Pokédex placement state"));
            return DexPlacementApplyResult.None;
        }

        var assignments = payload.Assignments;
        var presentRows = rows
            .Select((row, personalId) => (Row: row, PersonalId: personalId))
            .Where(pair =>
                pair.Row.IsPresent
                && pair.Row.Species is { Species: > 0 })
            .ToArray();
        var currentIndexBySpecies = new Dictionary<int, int>();
        foreach (var speciesGroup in presentRows.GroupBy(pair => (int)pair.Row.Species!.Species))
        {
            var indices = speciesGroup
                .Select(pair => (int)pair.Row.ZADexOrder)
                .Distinct()
                .ToArray();
            if (indices.Length != 1 || indices[0] <= 0)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Species {speciesGroup.Key.ToString(CultureInfo.InvariantCulture)} does not have one shared active Pokédex slot across its present forms.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: ZaPokemonWorkflowService.DexPlacementField,
                    expected: "One positive shared Pokédex slot per active species"));
                return DexPlacementApplyResult.None;
            }

            currentIndexBySpecies.Add(speciesGroup.Key, indices[0]);
        }

        var contentRows = contents.Rows.ToArray();
        if (contentRows.Any(row => !row.HasKnownGroup))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The Pokédex contents table contains an unsupported membership group.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Regular or Hyperspace membership"));
            return DexPlacementApplyResult.None;
        }

        var expectedSpecies = currentIndexBySpecies.Keys.Order().ToArray();
        var contentSpecies = contentRows.Select(row => row.Species).Order().ToArray();
        var assignedSpecies = assignments.Keys.Order().ToArray();
        var expectedIndices = currentIndexBySpecies.Values.Order().ToArray();
        var assignedIndices = assignments.Values.Order().ToArray();
        if (!contentSpecies.SequenceEqual(expectedSpecies)
            || contentRows.Select(row => row.Species).Distinct().Count() != contentRows.Length
            || !assignedSpecies.SequenceEqual(expectedSpecies)
            || !expectedIndices.SequenceEqual(Enumerable.Range(1, expectedSpecies.Length))
            || !assignedIndices.SequenceEqual(Enumerable.Range(1, expectedSpecies.Length))
            || expectedSpecies.Length > ushort.MaxValue)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The active personal data, Pokédex contents, and staged slots no longer form the same complete one-to-one mapping.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Exact active species coverage with contiguous unique slots"));
            return DexPlacementApplyResult.None;
        }

        var groupBySpecies = contentRows.ToDictionary(
            row => row.Species,
            row => (ZaPokedexContentsGroup)row.Group);
        var regularIndices = currentIndexBySpecies
            .Where(pair => groupBySpecies[pair.Key] == ZaPokedexContentsGroup.Regular)
            .Select(pair => pair.Value)
            .Order()
            .ToArray();
        var hyperspaceIndices = currentIndexBySpecies
            .Where(pair => groupBySpecies[pair.Key] == ZaPokedexContentsGroup.Hyperspace)
            .Select(pair => pair.Value)
            .Order()
            .ToArray();
        if (regularIndices.Length == 0
            || hyperspaceIndices.Length == 0
            || !regularIndices.SequenceEqual(Enumerable.Range(1, regularIndices.Length))
            || !hyperspaceIndices.SequenceEqual(
                Enumerable.Range(regularIndices.Length + 1, hyperspaceIndices.Length)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Regular and Hyperspace species no longer occupy one verified contiguous slot range each.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Contiguous Regular slots followed by contiguous Hyperspace slots"));
            return DexPlacementApplyResult.None;
        }

        var currentRegularCount = regularIndices.Length;
        var targetRegularCount = payload.RegularCount ?? currentRegularCount;
        if (targetRegularCount <= 0 || targetRegularCount >= expectedSpecies.Length)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The staged Regular Dex boundary would leave one Pokédex empty or exceed the active species range.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Regular Dex count from 1 through {expectedSpecies.Length - 1}")));
            return DexPlacementApplyResult.None;
        }

        var groupUpdates = new Dictionary<int, ZaPokedexContentsGroup>();
        var targetGroups = new Dictionary<int, ZaPokedexContentsGroup>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var targetGroup = assignment.Value <= targetRegularCount
                ? ZaPokedexContentsGroup.Regular
                : ZaPokedexContentsGroup.Hyperspace;
            targetGroups.Add(assignment.Key, targetGroup);
            if (groupBySpecies[assignment.Key] != targetGroup)
            {
                groupUpdates.Add(assignment.Key, targetGroup);
            }
        }

        var changedPersonalIds = new HashSet<int>();
        var requiresPersonalRebuild = false;
        foreach (var (row, personalId) in presentRows)
        {
            var speciesId = (int)row.Species!.Species;
            var targetIndex = assignments[speciesId];
            if (row.ZADexOrder == targetIndex)
            {
                continue;
            }

            requiresPersonalRebuild |= !row.HasZADexOrder;
            row.HasZADexOrder = true;
            row.ZADexOrder = checked((ushort)targetIndex);
            changedPersonalIds.Add(personalId);
        }

        return new DexPlacementApplyResult(
            changedPersonalIds,
            groupUpdates,
            targetGroups,
            requiresPersonalRebuild,
            currentRegularCount,
            targetRegularCount);
    }

    private static ZaPokemonWorkflow OverlayDexPlacement(
        ZaPokemonWorkflow workflow,
        PendingEdit edit)
    {
        var editor = workflow.DexEditor;
        if (editor is null
            || !editor.CanEdit
            || !TryDecodeDexPlacementPayload(edit.NewValue, out var payload))
        {
            return workflow;
        }

        var assignments = payload.Assignments;
        var targetRegularCount = payload.RegularCount ?? editor.RegularCount;
        if (assignments.Count != editor.Placements.Count
            || targetRegularCount <= 0
            || targetRegularCount >= assignments.Count
            || !assignments.Values
                .Order()
                .SequenceEqual(Enumerable.Range(1, assignments.Count)))
        {
            return workflow;
        }

        var updatedPokemon = workflow.Pokemon
            .Select(pokemon =>
            {
                if (!pokemon.DexPresence.IsPresentInGame
                    || !assignments.TryGetValue(pokemon.SpeciesId, out var internalIndex))
                {
                    return pokemon;
                }

                return pokemon with
                {
                    DexPresence = pokemon.DexPresence with
                    {
                        IsInAnyDex = true,
                        RegionalDexIndex = internalIndex,
                    },
                    Personal = pokemon.Personal with { RegionalDexIndex = internalIndex },
                };
            })
            .ToArray();
        var representativeBySpecies = updatedPokemon
            .Where(pokemon =>
                pokemon.DexPresence.IsPresentInGame
                && assignments.ContainsKey(pokemon.SpeciesId))
            .GroupBy(pokemon => pokemon.SpeciesId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(pokemon => pokemon.Form == 0 ? 0 : 1)
                    .ThenBy(pokemon => pokemon.Form)
                    .ThenBy(pokemon => pokemon.PersonalId)
                    .First());
        var placements = assignments
            .Select(pair =>
            {
                if (!representativeBySpecies.TryGetValue(pair.Key, out var representative))
                {
                    return null;
                }

                var dexKind = GetDexKindForIndex(pair.Value, targetRegularCount);
                var displayedNumber = string.Equals(
                    dexKind,
                    ZaPokemonWorkflowService.RegularDexKind,
                    StringComparison.Ordinal)
                    ? pair.Value
                    : pair.Value - targetRegularCount;
                return new ZaPokemonDexPlacement(
                    pair.Key,
                    pair.Value,
                    dexKind,
                    displayedNumber,
                    representative.Name);
            })
            .Where(placement => placement is not null)
            .Select(placement => placement!)
            .OrderBy(placement => placement.InternalIndex)
            .ToArray();
        if (placements.Length != editor.Placements.Count)
        {
            return workflow;
        }

        var isVanillaLayout = IsVanillaDexPlacementEdit(edit);
        return workflow with
        {
            Pokemon = updatedPokemon,
            DexEditor = editor with
            {
                IsVanillaLayout = isVanillaLayout,
                CanReturnToVanilla = !isVanillaLayout
                    && editor.ReturnToVanillaBlockedReason is null,
                CanSyncMegasToRegular = false,
                RegularCount = targetRegularCount,
                HyperspaceCount = assignments.Count - targetRegularCount,
                Placements = placements,
            },
        };
    }

    private static bool IsDexPlacementEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && (string.Equals(edit.RecordId, DexPlacementRecordId, StringComparison.Ordinal)
                || string.Equals(edit.RecordId, DexLayoutRecordId, StringComparison.Ordinal)
                || string.Equals(edit.RecordId, VanillaDexPlacementRecordId, StringComparison.Ordinal)
                || string.Equals(edit.RecordId, MegaDexSyncRecordId, StringComparison.Ordinal))
            && string.Equals(edit.Field, ZaPokemonWorkflowService.DexPlacementField, StringComparison.Ordinal);
    }

    private static bool ShouldIsolateTrinityModManagerRomFs(
        ChangePlan plan,
        ZaOutputMode outputMode)
    {
        return outputMode == ZaOutputMode.TrinityModManager
            && plan.Writes.Any(write => string.Equals(
                write.TargetRelativePath,
                ZaExeFsReservedRegionLedger.ExeFsMainPath,
                StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsAlphaMoveEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && string.Equals(
                edit.Field,
                ZaPokemonWorkflowService.AlphaMoveField,
                StringComparison.Ordinal)
            && TryParseAlphaMoveRecordId(edit.RecordId, out _, out _);
    }

    private static EditSession ReplaceOrRemoveFieldNoOp(
        ZaPokemonWorkflow loadedWorkflow,
        EditSession session,
        PendingEdit pendingEdit)
    {
        var matchesLoadedSource = false;
        if (IsAlphaMoveEdit(pendingEdit)
            && TryParseAlphaMoveRecordId(
                pendingEdit.RecordId,
                out var speciesId,
                out var formId)
            && int.TryParse(
                pendingEdit.NewValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var moveId))
        {
            var sourceMoveId = loadedWorkflow.Pokemon
                .FirstOrDefault(pokemon =>
                    pokemon.SpeciesId == speciesId && pokemon.Form == formId)
                ?.AlphaMove
                ?.MoveId;
            matchesLoadedSource = sourceMoveId == moveId;
        }
        else if (TryParseCompatibilityField(pendingEdit.Field, out var groupId, out var slot)
            && int.TryParse(
                pendingEdit.RecordId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var personalId)
            && int.TryParse(
                pendingEdit.NewValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var compatibilityEnabled))
        {
            var sourceEntry = loadedWorkflow.Pokemon
                .FirstOrDefault(pokemon => pokemon.PersonalId == personalId)
                ?.Compatibility
                .FirstOrDefault(group => string.Equals(
                    group.GroupId,
                    groupId,
                    StringComparison.Ordinal))
                ?.Entries
                .FirstOrDefault(entry => entry.Slot == slot);
            matchesLoadedSource = sourceEntry is not null
                && sourceEntry.CanLearn == (compatibilityEnabled != 0);
        }

        if (!matchesLoadedSource)
        {
            return ReplacePendingPokemonEdit(session, pendingEdit);
        }

        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit =>
                    !string.Equals(edit.Domain, pendingEdit.Domain, StringComparison.Ordinal)
                    || !string.Equals(edit.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
                    || !string.Equals(edit.Field, pendingEdit.Field, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static string CreateAlphaMoveRecordId(int speciesId, int formId)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AlphaMoveRecordIdPrefix}{speciesId}:{formId}");
    }

    private static bool TryParseAlphaMoveRecordId(
        string? recordId,
        out int speciesId,
        out int formId)
    {
        speciesId = 0;
        formId = 0;
        if (recordId is null
            || !recordId.StartsWith(AlphaMoveRecordIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = recordId.AsSpan(AlphaMoveRecordIdPrefix.Length);
        var separator = payload.IndexOf(':');
        if (separator <= 0
            || separator == payload.Length - 1
            || payload[(separator + 1)..].Contains(':')
            || !int.TryParse(
                payload[..separator],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out speciesId)
            || !int.TryParse(
                payload[(separator + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out formId)
            || speciesId is < 0 or > ushort.MaxValue
            || formId is < 0 or > ushort.MaxValue)
        {
            speciesId = 0;
            formId = 0;
            return false;
        }

        return string.Equals(
            recordId,
            CreateAlphaMoveRecordId(speciesId, formId),
            StringComparison.Ordinal);
    }

    internal static bool IsScopedDexLayoutEdit(PendingEdit edit)
    {
        if (!string.Equals(
                edit.Domain,
                ZaEditSessionSupport.PokemonDomain,
                StringComparison.Ordinal)
            || !string.Equals(
                edit.Field,
                ZaPokemonWorkflowService.DexPlacementField,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(edit.RecordId, DexLayoutRecordId, StringComparison.Ordinal)
            || string.Equals(
                edit.RecordId,
                VanillaDexPlacementRecordId,
                StringComparison.Ordinal)
            || string.Equals(edit.RecordId, MegaDexSyncRecordId, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(edit.RecordId, DexPlacementRecordId, StringComparison.Ordinal)
            && (!TryDecodeDexPlacementPayload(edit.NewValue, out var payload)
                || payload.Version != 1
                || payload.RegularCount is not null);
    }

    private static bool IsVanillaDexPlacementEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, VanillaDexPlacementRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, ZaPokemonWorkflowService.DexPlacementField, StringComparison.Ordinal);
    }

    private static bool IsMegaDexSyncEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, MegaDexSyncRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, ZaPokemonWorkflowService.DexPlacementField, StringComparison.Ordinal);
    }

    private static bool IsDexPresenceEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && string.Equals(edit.Field, ZaPokemonWorkflowService.IsPresentInGameField, StringComparison.Ordinal);
    }

    private static bool CanUseDexLayoutSession(
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.All(IsDexPlacementEdit))
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Dex Layout needs its own edit session before staging.",
            ZaEditSessionSupport.PokemonDomain,
            field: ZaPokemonWorkflowService.DexPlacementField,
            expected: "Apply or discard ordinary Pokemon Data changes first"));
        return false;
    }

    private static string EncodeDexPlacementState(
        IReadOnlyDictionary<int, int> assignments,
        int regularCount,
        int baseRegularCount)
    {
        return regularCount == baseRegularCount
            ? EncodeDexPlacementPayload(new DexPlacementPayload(
                Version: 1,
                RegularCount: null,
                assignments))
            : EncodeDexPlacementPayload(new DexPlacementPayload(
                Version: 2,
                regularCount,
                assignments));
    }

    private static string EncodeDexPlacementPayload(DexPlacementPayload payload)
    {
        var assignments = string.Join(
            ",",
            payload.Assignments
                .OrderBy(pair => pair.Key)
                .Select(pair => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{pair.Key}:{pair.Value}")));
        return payload.Version switch
        {
            1 when payload.RegularCount is null =>
                DexPlacementPayloadV1Prefix + assignments,
            2 when payload.RegularCount is > 0 =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DexPlacementPayloadV2Prefix}{payload.RegularCount.Value}|{assignments}"),
            _ => string.Empty,
        };
    }

    private static bool TryDecodeDexPlacementPayload(
        string? payload,
        out DexPlacementPayload decoded)
    {
        decoded = DexPlacementPayload.Invalid;
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        if (payload.StartsWith(DexPlacementPayloadV1Prefix, StringComparison.Ordinal))
        {
            if (!TryDecodeDexAssignments(
                    payload.AsSpan(DexPlacementPayloadV1Prefix.Length),
                    out var assignments))
            {
                return false;
            }

            decoded = new DexPlacementPayload(
                Version: 1,
                RegularCount: null,
                assignments);
            return true;
        }

        if (!payload.StartsWith(DexPlacementPayloadV2Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var versionedPayload = payload.AsSpan(DexPlacementPayloadV2Prefix.Length);
        var regularCountSeparator = versionedPayload.IndexOf('|');
        if (regularCountSeparator <= 0
            || regularCountSeparator == versionedPayload.Length - 1
            || !int.TryParse(
                versionedPayload[..regularCountSeparator],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var regularCount)
            || regularCount <= 0
            || !TryDecodeDexAssignments(
                versionedPayload[(regularCountSeparator + 1)..],
                out var versionedAssignments))
        {
            return false;
        }

        decoded = new DexPlacementPayload(
            Version: 2,
            regularCount,
            versionedAssignments);
        return true;
    }

    private static bool TryDecodeDexAssignments(
        ReadOnlySpan<char> payload,
        out Dictionary<int, int> assignments)
    {
        assignments = [];
        if (payload.IsEmpty)
        {
            return false;
        }

        foreach (var assignment in payload.ToString().Split(','))
        {
            var separator = assignment.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0
                || separator == assignment.Length - 1
                || assignment.IndexOf(':', separator + 1) >= 0
                || !int.TryParse(
                    assignment.AsSpan(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var speciesId)
                || !int.TryParse(
                    assignment.AsSpan(separator + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var internalIndex)
                || speciesId <= 0
                || internalIndex <= 0
                || !assignments.TryAdd(speciesId, internalIndex))
            {
                assignments = [];
                return false;
            }
        }

        return assignments.Count > 0;
    }

    private static bool IsCanonicalDexPlacementPayload(
        string? value,
        DexPlacementPayload payload)
    {
        return string.Equals(value, EncodeDexPlacementPayload(payload), StringComparison.Ordinal);
    }

    private static bool DexAssignmentsEqual(
        IReadOnlyDictionary<int, int> left,
        IReadOnlyDictionary<int, int> right)
    {
        return left.Count == right.Count
            && left.All(pair =>
                right.TryGetValue(pair.Key, out var rightIndex)
                && rightIndex == pair.Value);
    }

    private static bool DexPlacementStatesEqual(
        IReadOnlyDictionary<int, int> leftAssignments,
        int leftRegularCount,
        IReadOnlyDictionary<int, int> rightAssignments,
        int rightRegularCount)
    {
        return leftRegularCount == rightRegularCount
            && DexAssignmentsEqual(leftAssignments, rightAssignments);
    }

    private static IReadOnlyList<ProjectFileReference> CreateDexPlacementSources(
        ZaPokemonDexEditor editor,
        bool includeExecutable)
    {
        if (editor.PersonalProvenance is null
            || editor.ContentsProvenance is null
            || editor.MegaContentsProvenance is null)
        {
            throw new InvalidDataException(
                "Verified Pokédex placement sources are unavailable.");
        }

        var sources = new List<ProjectFileReference>
        {
            ToSourceReference(editor.PersonalProvenance),
            ToSourceReference(editor.ContentsProvenance),
            ToSourceReference(editor.MegaContentsProvenance),
        };
        if (includeExecutable)
        {
            if (editor.ExecutableProvenance is null)
            {
                throw new InvalidDataException(
                    "Verified exefs/main provenance is unavailable for a Pokédex boundary change.");
            }

            sources.Add(ToSourceReference(editor.ExecutableProvenance));
        }

        return sources;
    }

    private static IReadOnlyList<ProjectFileReference> CreateMegaDexSyncSources(
        ZaPokemonDexEditor editor)
    {
        if (editor.ContentsProvenance is null
            || editor.MegaContentsProvenance is null)
        {
            throw new InvalidDataException(
                "Verified Mega Pokédex synchronization sources are unavailable.");
        }

        return
        [
            ToSourceReference(editor.ContentsProvenance),
            ToSourceReference(editor.MegaContentsProvenance),
        ];
    }

    private IReadOnlyList<ProjectFileReference> CreateVanillaDexPlacementSources(
        OpenedProject project,
        ZaPokemonDexEditor editor,
        ZaDexLayoutState vanilla,
        bool includeExecutable)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(vanilla);

        var sources = CreateDexPlacementSources(editor, includeExecutable)
            .Concat(vanilla.SourceReferences)
            .ToList();
        if (includeExecutable)
        {
            var baseMain = ZaExeFsMainFileResolver.ResolveBase(project)
                ?? throw new FileNotFoundException(
                    "Verified base exefs/main is unavailable for Return to Vanilla.");
            sources.Add(baseMain.Reference);
        }

        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private ZaDexLayoutState ReadVerifiedVanillaDexLayout(
        OpenedProject project,
        ZaPokemonDexEditor editor)
    {
        var vanilla = ZaDexLayoutStateReader.ReadBase(project, fileSource);
        if (vanilla.RegularCount != ZaDexLayoutMainPatcher.VanillaRegularCount)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The verified base Pokédex uses Regular boundary {vanilla.RegularCount}, "
                    + $"not the expected vanilla boundary {ZaDexLayoutMainPatcher.VanillaRegularCount}."));
        }

        var currentSpecies = editor.Placements
            .Select(placement => placement.SpeciesId)
            .Order()
            .ToArray();
        if (!currentSpecies.SequenceEqual(vanilla.Assignments.Keys.Order()))
        {
            throw new InvalidDataException(
                "The effective and base Pokédexes do not contain the same active species.");
        }

        if (editor.VanillaLayoutFingerprint is null
            || !string.Equals(
                editor.VanillaLayoutFingerprint,
                vanilla.Fingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The verified base Dex Layout changed after the workflow was loaded.");
        }

        return vanilla;
    }

    private static string GetDexKindForIndex(int internalIndex, int regularCount)
    {
        return internalIndex <= regularCount
            ? ZaPokemonWorkflowService.RegularDexKind
            : ZaPokemonWorkflowService.HyperspaceDexKind;
    }

    private static string CreateSourceFingerprint(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string CreateCombinedSourceFingerprint(byte[] effectiveBytes, byte[] baseBytes)
    {
        ArgumentNullException.ThrowIfNull(effectiveBytes);
        ArgumentNullException.ThrowIfNull(baseBytes);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, effectiveBytes.LongLength);
        hash.AppendData(lengthBytes);
        hash.AppendData(effectiveBytes);
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, baseBytes.LongLength);
        hash.AppendData(lengthBytes);
        hash.AppendData(baseBytes);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static ProjectFileReference ToSourceReference(ZaPokemonProvenance provenance)
    {
        return new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
    }

    private static string GetSpeciesName(ZaPokemonWorkflow workflow, int speciesId)
    {
        return workflow.Pokemon
            .Where(pokemon => pokemon.SpeciesId == speciesId)
            .OrderBy(pokemon => pokemon.Form == 0 ? 0 : 1)
            .ThenBy(pokemon => pokemon.Form)
            .Select(pokemon => pokemon.Name)
            .FirstOrDefault()
            ?? $"Species {speciesId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatDexPlacement(ZaPokemonDexPlacement placement)
    {
        var dexName = string.Equals(
            placement.DexKind,
            ZaPokemonWorkflowService.RegularDexKind,
            StringComparison.Ordinal)
            ? "Regular Dex"
            : "Hyperspace Dex";
        return $"{dexName} #{placement.DisplayedNumber.ToString("000", CultureInfo.InvariantCulture)}";
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
            return ApplyEvolutionOperation(workflow, pokemon, evolutionOperation);
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
        var currentExpAddend = pokemon.BaseExperience
            - ZaPokemonExperience.CalculateFormulaBase(stats.Total, pokemon.EvolutionStage);
        var updated = field switch
        {
            ZaPokemonWorkflowService.HPField => OverlayBaseStats(pokemon, stats with { HP = value }, currentExpAddend),
            ZaPokemonWorkflowService.AttackField => OverlayBaseStats(pokemon, stats with { Attack = value }, currentExpAddend),
            ZaPokemonWorkflowService.DefenseField => OverlayBaseStats(pokemon, stats with { Defense = value }, currentExpAddend),
            ZaPokemonWorkflowService.SpecialAttackField => OverlayBaseStats(pokemon, stats with { SpecialAttack = value }, currentExpAddend),
            ZaPokemonWorkflowService.SpecialDefenseField => OverlayBaseStats(pokemon, stats with { SpecialDefense = value }, currentExpAddend),
            ZaPokemonWorkflowService.SpeedField => OverlayBaseStats(pokemon, stats with { Speed = value }, currentExpAddend),
            ZaPokemonWorkflowService.Type1Field => pokemon with { Type1 = FormatType(value) },
            ZaPokemonWorkflowService.Type2Field => pokemon with { Type2 = FormatType(value) },
            ZaPokemonWorkflowService.Ability1Field => pokemon with { Abilities = abilities with { Ability1 = value, Ability1Label = ResolveFieldOptionLabel(labels, ZaPokemonWorkflowService.Ability1Field, value, ZaLabels.Ability(value)) } },
            ZaPokemonWorkflowService.Ability2Field => pokemon with { Abilities = abilities with { Ability2 = value, Ability2Label = ResolveFieldOptionLabel(labels, ZaPokemonWorkflowService.Ability2Field, value, ZaLabels.Ability(value)) } },
            ZaPokemonWorkflowService.HiddenAbilityField => pokemon with { Abilities = abilities with { HiddenAbility = value, HiddenAbilityLabel = ResolveFieldOptionLabel(labels, ZaPokemonWorkflowService.HiddenAbilityField, value, ZaLabels.Ability(value)) } },
            ZaPokemonWorkflowService.CatchRateField => pokemon with { CatchRate = value },
            ZaPokemonWorkflowService.EvolutionStageField => pokemon with
            {
                EvolutionStage = value,
                BaseExperience = ZaPokemonExperience.CalculateBaseExperience(stats.Total, value, currentExpAddend),
            },
            ZaPokemonWorkflowService.BaseExperienceField => pokemon with { BaseExperience = value },
            ZaPokemonWorkflowService.GenderRatioField => pokemon with { GenderRatio = value, GenderRatioLabel = FormatGender(value) },
            ZaPokemonWorkflowService.HeightField => pokemon with { Height = value },
            ZaPokemonWorkflowService.WeightField => pokemon with { Weight = value },
            ZaPokemonWorkflowService.IsPresentInGameField => pokemon with { DexPresence = dex with { IsPresentInGame = value != 0 } },
            ZaPokemonWorkflowService.RegionalDexIndexField => pokemon with { DexPresence = dex with { RegionalDexIndex = value, IsInAnyDex = value > 0 } },
            _ => pokemon,
        };

        var updatedPersonal = OverlayPersonalDetails(personal, field, value);
        if (field is ZaPokemonWorkflowService.HPField
            or ZaPokemonWorkflowService.AttackField
            or ZaPokemonWorkflowService.DefenseField
            or ZaPokemonWorkflowService.SpecialAttackField
            or ZaPokemonWorkflowService.SpecialDefenseField
            or ZaPokemonWorkflowService.SpeedField
            or ZaPokemonWorkflowService.EvolutionStageField)
        {
            updatedPersonal = updatedPersonal with { BaseExperience = updated.BaseExperience };
        }

        return updated with { Personal = updatedPersonal };
    }

    private static bool IsGlobalYieldField(string? field)
    {
        return string.Equals(field?.Trim(), GlobalEvYieldField, StringComparison.Ordinal)
            || string.Equals(field?.Trim(), GlobalExpYieldField, StringComparison.Ordinal);
    }

    private static bool IsGlobalYieldEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, GlobalRecordId, StringComparison.Ordinal)
            && IsGlobalYieldField(edit.Field);
    }

    private static bool IsGlobalEvYieldEdit(PendingEdit edit)
    {
        return IsGlobalYieldEdit(edit)
            && string.Equals(edit.Field, GlobalEvYieldField, StringComparison.Ordinal);
    }

    private static bool IsGlobalExpYieldEdit(PendingEdit edit)
    {
        return IsGlobalYieldEdit(edit)
            && string.Equals(edit.Field, GlobalExpYieldField, StringComparison.Ordinal);
    }

    private static bool IsGlobalYieldRestoreEdit(PendingEdit edit)
    {
        return IsGlobalYieldEdit(edit)
            && string.Equals(edit.NewValue, RestoreYieldValue, StringComparison.Ordinal);
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

    private static ZaPokemonRecord OverlayBaseStats(
        ZaPokemonRecord pokemon,
        ZaPokemonBaseStats stats,
        int expAddend)
    {
        var recalculated = stats with { Total = RecalculateTotal(stats) };
        return pokemon with
        {
            BaseStats = recalculated,
            BaseExperience = ZaPokemonExperience.CalculateBaseExperience(
                recalculated.Total,
                pokemon.EvolutionStage,
                expAddend),
        };
    }

    private static EditSession ReplacePendingPokemonEdit(EditSession session, PendingEdit pendingEdit)
    {
        if (IsOrderedRowOperation(pendingEdit))
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Append(pendingEdit)
                    .ToArray(),
            };
        }

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
                    && string.Equals(
                        candidate.Field,
                        ZaPokemonWorkflowService.BaseExperienceField,
                        StringComparison.Ordinal))
                || (IsGlobalEvYieldEdit(pendingEdit)
                    && EvYieldFields.Contains(candidate.Field ?? string.Empty));
        }

        if (string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal))
        {
            return true;
        }

        if (IsGlobalExpYieldEdit(candidate)
            && string.Equals(
                pendingEdit.Field,
                ZaPokemonWorkflowService.BaseExperienceField,
                StringComparison.Ordinal))
        {
            return true;
        }

        return IsGlobalEvYieldEdit(candidate)
            && EvYieldFields.Contains(pendingEdit.Field ?? string.Empty);
    }

    private static bool IsLearnsetEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && edit.Field?.StartsWith($"{LearnsetFieldPrefix}:", StringComparison.Ordinal) == true;
    }

    private static bool IsEvolutionEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && edit.Field?.StartsWith($"{EvolutionFieldPrefix}:", StringComparison.Ordinal) == true;
    }

    private static bool IsOrderedRowOperation(PendingEdit edit)
    {
        return IsLearnsetEdit(edit) || IsEvolutionEdit(edit);
    }

    private static IEnumerable<PendingEdit> OrderPersonalEditsForApply(IEnumerable<PendingEdit> edits)
    {
        return edits.OrderBy(edit =>
            IsGlobalYieldRestoreEdit(edit)
                ? 2
                : IsGlobalExpYieldEdit(edit)
                || string.Equals(
                    edit.Field,
                    ZaPokemonWorkflowService.BaseExperienceField,
                    StringComparison.Ordinal)
                ? 1
                : 0);
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
            ZaPokemonWorkflowService.BaseExperienceField => personal with { BaseExperience = value },
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

    private static ZaPokemonWorkflow OverlayGlobalYieldEdit(ZaPokemonWorkflow workflow, PendingEdit edit)
    {
        var removing = string.Equals(edit.NewValue, RemoveYieldValue, StringComparison.Ordinal);
        var restoring = string.Equals(edit.NewValue, RestoreYieldValue, StringComparison.Ordinal);
        if (!removing && !restoring)
        {
            return workflow;
        }

        return workflow with
        {
            Pokemon = workflow.Pokemon
                .Select(pokemon => edit.Field switch
                {
                    GlobalEvYieldField when removing => OverlayAllEvYields(pokemon, 0, 0, 0, 0, 0, 0),
                    GlobalExpYieldField when removing => OverlayPersonalField(
                        workflow,
                        pokemon,
                        ZaPokemonWorkflowService.BaseExperienceField,
                        0),
                    GlobalEvYieldField when pokemon.VanillaYieldDefaults is { } vanilla =>
                        OverlayAllEvYields(
                            pokemon,
                            vanilla.EVYieldHP,
                            vanilla.EVYieldAttack,
                            vanilla.EVYieldDefense,
                            vanilla.EVYieldSpecialAttack,
                            vanilla.EVYieldSpecialDefense,
                            vanilla.EVYieldSpeed),
                    GlobalExpYieldField when pokemon.VanillaYieldDefaults is { } vanilla =>
                        OverlayPersonalField(
                            workflow,
                            pokemon,
                            ZaPokemonWorkflowService.BaseExperienceField,
                            vanilla.BaseExperience),
                    _ => pokemon,
                })
                .ToArray(),
        };
    }

    private static ZaPokemonRecord OverlayAllEvYields(
        ZaPokemonRecord pokemon,
        int hp,
        int attack,
        int defense,
        int specialAttack,
        int specialDefense,
        int speed)
    {
        return pokemon with
        {
            Personal = pokemon.Personal with
            {
                EVYieldHP = hp,
                EVYieldAttack = attack,
                EVYieldDefense = defense,
                EVYieldSpecialAttack = specialAttack,
                EVYieldSpecialDefense = specialDefense,
                EVYieldSpeed = speed,
            },
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
                learnset = MoveLearnsetMoveIdsKeepingSlotLevels(learnset, targetSlot, targetSlot - 1);
                break;
            case MoveDownAction when targetSlot >= 0 && targetSlot < learnset.Count - 1:
                learnset = MoveLearnsetMoveIdsKeepingSlotLevels(learnset, targetSlot, targetSlot + 1);
                break;
            case MoveToAction when operation.MoveId is { } destination && targetSlot >= 0 && targetSlot < learnset.Count && destination >= 0 && destination < learnset.Count:
                learnset = MoveLearnsetMoveIdsKeepingSlotLevels(learnset, targetSlot, destination);
                break;
        }

        return pokemon with
        {
            Learnset = learnset.Select((move, index) => move with { Slot = index }).ToArray(),
        };
    }

    private static List<ZaPokemonLearnsetMove> MoveLearnsetMoveIdsKeepingSlotLevels(
        IReadOnlyList<ZaPokemonLearnsetMove> learnset,
        int sourceSlot,
        int destinationSlot)
    {
        var moveIdentities = learnset
            .Select(move => (move.MoveId, move.MoveName))
            .ToList();
        var movedIdentity = moveIdentities[sourceSlot];
        moveIdentities.RemoveAt(sourceSlot);
        moveIdentities.Insert(destinationSlot, movedIdentity);

        return moveIdentities
            .Select((identity, index) => learnset[index] with
            {
                Slot = index,
                MoveId = identity.MoveId,
                MoveName = identity.MoveName,
            })
            .ToList();
    }

    private static ZaPokemonRecord ApplyEvolutionOperation(
        ZaPokemonWorkflow workflow,
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
                var argument = operation.Argument ?? 0;
                var row = new ZaPokemonEvolutionRecord(
                    targetSlot,
                    operation.Method ?? 0,
                    argument,
                    operation.Species ?? 0,
                    operation.Form ?? 0,
                    operation.Level ?? 0,
                    definition.Name,
                    definition.ArgumentKind,
                    definition.ArgumentLabel,
                    string.Equals(definition.ArgumentKind, "item", StringComparison.Ordinal)
                        ? FormatEvolutionItemArgumentValue(workflow, operation.Method ?? 0, argument)
                        : argument.ToString(CultureInfo.InvariantCulture));
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

    private static string FormatEvolutionItemArgumentValue(
        ZaPokemonWorkflow workflow,
        int method,
        int argument)
    {
        var option = workflow.EvolutionMethodOptions
            .FirstOrDefault(candidate => candidate.Value == method)
            ?.ArgumentOptions
            .FirstOrDefault(candidate => candidate.Value == argument);
        if (option is null)
        {
            option = workflow.EvolutionMethodOptions
                .Where(candidate => string.Equals(candidate.ArgumentKind, "item", StringComparison.Ordinal))
                .SelectMany(candidate => candidate.ArgumentOptions)
                .FirstOrDefault(candidate => candidate.Value == argument);
        }

        return option?.Label ?? argument.ToString(CultureInfo.InvariantCulture);
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
        IReadOnlyList<PersonalRow> baseRows,
        PendingEdit edit,
        ZaEvolutionItemConversionState conversionState,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (IsGlobalYieldEdit(edit))
        {
            ApplyGlobalYieldEdit(rows, baseRows, edit, diagnostics);
            return;
        }

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
                row.HasLevelupMoves = true;
                ApplyLearnsetOperation(row.LevelupMoves, operation);
            }

            return;
        }

        if (TryParseEvolutionField(edit.Field, out _, out _))
        {
            var operation = ParseEvolutionOperation(edit, null, diagnostics);
            if (operation is not null)
            {
                operation = EncodeEvolutionOperation(operation, conversionState);
                row.HasEvolutions = true;
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

    private static void ApplyGlobalYieldEdit(
        IReadOnlyList<PersonalRow> rows,
        IReadOnlyList<PersonalRow> baseRows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsGlobalYieldAction(edit.NewValue))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon yield edit uses an invalid action.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "remove or restore"));
            return;
        }

        var restoring = string.Equals(edit.NewValue, RestoreYieldValue, StringComparison.Ordinal);
        if (restoring && baseRows.Count != rows.Count)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon yield restore requires a matching clean base personal table.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Base and effective personal tables with identical row counts"));
            return;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if ((row.Species?.Species ?? 0) <= 0)
            {
                continue;
            }

            var baseRow = restoring ? baseRows[index] : null;
            if (baseRow is not null && !PersonalRowIdentityMatches(row, baseRow))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon yield restore base identity does not match personal row {index.ToString(CultureInfo.InvariantCulture)}.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: edit.Field,
                    expected: "Matching species and form at every restored row"));
                return;
            }

            switch (edit.Field)
            {
                case GlobalEvYieldField when !restoring:
                    row.HasEvYield = true;
                    row.EvYield = StatInfoRow.Zero;
                    break;
                case GlobalEvYieldField:
                    row.HasEvYield = baseRow!.HasEvYield;
                    row.EvYield = baseRow.EvYield;
                    break;
                case GlobalExpYieldField when !restoring:
                    ApplyBaseExperience(row, 0);
                    break;
                case GlobalExpYieldField:
                    var vanillaBaseExperience = ZaPokemonExperience.CalculateBaseExperience(
                        CalculateBaseStatTotal(baseRow!.BaseStats),
                        baseRow.EvoStage,
                        baseRow.ExpAddend);
                    ApplyBaseExperience(row, vanillaBaseExperience);
                    break;
            }
        }
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
                    row.HasBaseStats = true;
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Hp = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.AttackField:
                    row.HasBaseStats = true;
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Atk = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.DefenseField:
                    row.HasBaseStats = true;
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Def = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.SpecialAttackField:
                    row.HasBaseStats = true;
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spa = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.SpecialDefenseField:
                    row.HasBaseStats = true;
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spd = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.SpeedField:
                    row.HasBaseStats = true;
                    row.BaseStats = (row.BaseStats ?? StatInfoRow.Zero) with { Spe = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.Type1Field:
                    row.HasType1 = true;
                    row.Type1 = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.Type2Field:
                    row.HasType2 = true;
                    row.Type2 = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.Ability1Field:
                    row.HasAbility1 = true;
                    row.Ability1 = ToUshort(value);
                    break;
                case ZaPokemonWorkflowService.Ability2Field:
                    row.HasAbility2 = true;
                    row.Ability2 = ToUshort(value);
                    break;
                case ZaPokemonWorkflowService.HiddenAbilityField:
                    row.HasAbilityHidden = true;
                    row.AbilityHidden = ToUshort(value);
                    break;
                case ZaPokemonWorkflowService.CatchRateField:
                    row.HasCatchRate = true;
                    row.CatchRate = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.EvolutionStageField:
                    row.HasEvoStage = true;
                    row.EvoStage = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.EVYieldHPField:
                    row.HasEvYield = true;
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Hp = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldAttackField:
                    row.HasEvYield = true;
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Atk = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldDefenseField:
                    row.HasEvYield = true;
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Def = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldSpecialAttackField:
                    row.HasEvYield = true;
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spa = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldSpecialDefenseField:
                    row.HasEvYield = true;
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spd = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.EVYieldSpeedField:
                    row.HasEvYield = true;
                    row.EvYield = (row.EvYield ?? StatInfoRow.Zero) with { Spe = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.GenderRatioField:
                    row.HasGender = true;
                    row.Gender = (row.Gender ?? new GenderInfoRow(0, 0)) with { Ratio = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.HatchCyclesField:
                    row.HasEggHatchCycles = true;
                    row.EggHatchCycles = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.BaseFriendshipField:
                    row.HasBaseFriendship = true;
                    row.BaseFriendship = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.BaseExperienceField:
                    ApplyBaseExperience(row, value);
                    break;
                case ZaPokemonWorkflowService.ExpGrowthField:
                    row.HasXpGrowth = true;
                    row.XpGrowth = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.EggGroup1Field:
                    row.HasEggGroup1 = true;
                    row.EggGroup1 = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.EggGroup2Field:
                    row.HasEggGroup2 = true;
                    row.EggGroup2 = ToByte(value);
                    break;
                case ZaPokemonWorkflowService.FormField:
                    row.HasSpecies = true;
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Form = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.ModelIdField:
                    row.HasSpecies = true;
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Model = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.ColorField:
                    row.HasSpecies = true;
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Color = ToByte(value) };
                    break;
                case ZaPokemonWorkflowService.HeightField:
                    row.HasSpecies = true;
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Height = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.WeightField:
                    row.HasSpecies = true;
                    row.Species = (row.Species ?? SpeciesInfoRow.Zero) with { Weight = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.HatchedSpeciesField:
                    row.HasEggHatch = true;
                    row.EggHatch = (row.EggHatch ?? EggHatchInfoRow.Zero) with { Species = ToUshort(value) };
                    break;
                case ZaPokemonWorkflowService.IsPresentInGameField:
                    row.HasIsPresent = true;
                    row.IsPresent = value != 0;
                    break;
                case ZaPokemonWorkflowService.RegionalDexIndexField:
                    row.HasZADexOrder = true;
                    row.ZADexOrder = ToUshort(value);
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
        if (string.Equals(groupId, ZaPokemonWorkflowService.TechnicalMachineCompatibilityGroupId, StringComparison.Ordinal))
        {
            row.HasTmMoves = true;
            var move = (ushort)slot;
            row.TmMoves.RemoveAll(candidate => candidate == move);
            if (enabled)
            {
                row.TmMoves.Add(move);
                row.TmMoves.Sort();
            }

            return;
        }

        var target = groupId switch
        {
            ZaPokemonWorkflowService.EggMoveCompatibilityGroupId => row.EggMoves,
            ZaPokemonWorkflowService.ReminderMoveCompatibilityGroupId => row.ReminderMoves,
            _ => null,
        };
        if (target is null || (uint)slot >= (uint)target.Count)
        {
            return;
        }

        if (string.Equals(groupId, ZaPokemonWorkflowService.EggMoveCompatibilityGroupId, StringComparison.Ordinal))
        {
            row.HasEggMoves = true;
        }
        else if (string.Equals(groupId, ZaPokemonWorkflowService.ReminderMoveCompatibilityGroupId, StringComparison.Ordinal))
        {
            row.HasReminderMoves = true;
        }

        if (!enabled)
        {
            target.RemoveAt(slot);
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
                MoveLearnsetMoveIdsKeepingSlotLevels(learnset, targetSlot, targetSlot - 1);
                break;
            case MoveDownAction when targetSlot >= 0 && targetSlot < learnset.Count - 1:
                MoveLearnsetMoveIdsKeepingSlotLevels(learnset, targetSlot, targetSlot + 1);
                break;
            case MoveToAction when operation.MoveId is { } destination && targetSlot >= 0 && targetSlot < learnset.Count && destination >= 0 && destination < learnset.Count:
                MoveLearnsetMoveIdsKeepingSlotLevels(learnset, targetSlot, destination);
                break;
        }
    }

    private static void MoveLearnsetMoveIdsKeepingSlotLevels(
        IList<LevelupMoveRow> learnset,
        int sourceSlot,
        int destinationSlot)
    {
        var moveIds = learnset.Select(move => move.Move).ToList();
        var movedMoveId = moveIds[sourceSlot];
        moveIds.RemoveAt(sourceSlot);
        moveIds.Insert(destinationSlot, movedMoveId);

        for (var index = 0; index < learnset.Count; index++)
        {
            learnset[index] = learnset[index] with { Move = moveIds[index] };
        }
    }

    private static bool PrepareEvolutionItemConversions(
        IReadOnlyList<PersonalRow> rows,
        IEnumerable<PendingEdit> edits,
        ZaEvolutionItemConversionState conversionState)
    {
        var migrated = false;
        foreach (var row in rows)
        {
            for (var index = 0; index < row.Evolutions.Count; index++)
            {
                var evolution = row.Evolutions[index];
                if (!ZaPokemonWorkflowService.UsesEvolutionItemConversion(evolution.Condition)
                    || !conversionState.TryMigrateLegacyArgument(evolution.Parameter, out var encodedArgument))
                {
                    continue;
                }

                row.Evolutions[index] = evolution with { Parameter = checked((ushort)encodedArgument) };
                migrated = true;
            }
        }

        foreach (var edit in edits)
        {
            var operation = ParseEvolutionOperation(edit, pokemon: null, new List<ValidationDiagnostic>());
            if (operation is not null
                && operation.Action is AddAction or UpsertAction
                && operation.Method is { } method
                && operation.Argument is { } argument
                && ZaPokemonWorkflowService.UsesEvolutionItemConversion(method))
            {
                _ = conversionState.Encode(argument);
            }
        }

        return migrated;
    }

    private static EvolutionOperation EncodeEvolutionOperation(
        EvolutionOperation operation,
        ZaEvolutionItemConversionState conversionState)
    {
        return operation.Action is AddAction or UpsertAction
            && operation.Method is { } method
            && operation.Argument is { } argument
            && ZaPokemonWorkflowService.UsesEvolutionItemConversion(method)
                ? operation with { Argument = conversionState.Encode(argument) }
                : operation;
    }

    private static bool RequiresEncodedEvolutionRebuild(IEnumerable<PendingEdit> edits)
    {
        foreach (var edit in edits)
        {
            var operation = ParseEvolutionOperation(edit, pokemon: null, new List<ValidationDiagnostic>());
            if (operation is not null
                && operation.Action is AddAction or UpsertAction
                && operation.Method is { } method
                && ZaPokemonWorkflowService.UsesEvolutionItemConversion(method))
            {
                return true;
            }
        }

        return false;
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

    private PersonalArrayRows ReadRows(
        OpenedProject project,
        ZaWorkflowFile source,
        bool includeBaseRows = false)
    {
        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(source.Bytes));
        var requiresLegacyDexOrderRepair = table.HasLegacyByteZADexOrderLayout;
        ZaPersonalTable? baseTable = null;
        if (requiresLegacyDexOrderRepair || includeBaseRows)
        {
            var baseSource = fileSource.ReadBase(project, ZaDataPaths.PersonalArray);
            baseTable = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(baseSource.Bytes));
            if (baseTable.Value.HasLegacyByteZADexOrderLayout)
            {
                throw new InvalidDataException(
                    "Legacy Pokemon personal output cannot be repaired because the configured base table also uses the malformed byte layout.");
            }
        }

        var baseRowsBySpecies = baseTable is { } vanilla
            ? ZaPersonalLegacyRecovery.CreateUniqueBaseRowsBySpecies(vanilla)
            : null;

        var rows = new List<PersonalRow>();
        for (var index = 0; index < table.EntryLength; index++)
        {
            var row = table.Entry(index);
            var indexedBaseRow = baseTable is { } vanillaTable && index < vanillaTable.EntryLength
                ? vanillaTable.Entry(index)
                : null;
            var baseRow = ZaPersonalLegacyRecovery.FindBaseRow(row, indexedBaseRow, baseRowsBySpecies);
            if (requiresLegacyDexOrderRepair && row?.Species is not null && baseRow is null)
            {
                throw new InvalidDataException(
                    $"Legacy Pokemon personal row {index} cannot recover its missing species metadata from base data.");
            }

            rows.Add(row is null
                ? PersonalRow.Empty()
                : PersonalRow.From(row.Value, baseRow, requiresLegacyDexOrderRepair));
        }

        IReadOnlyList<PersonalRow>? baseRows = null;
        if (includeBaseRows)
        {
            if (baseTable is not { } verifiedBase || verifiedBase.EntryLength != table.EntryLength)
            {
                throw new InvalidDataException(
                    "Pokemon yield restore requires base and effective personal tables with identical row counts.");
            }

            baseRows = ReadTableRows(verifiedBase);
            for (var index = 0; index < rows.Count; index++)
            {
                if ((rows[index].Species?.Species ?? 0) > 0
                    && !PersonalRowIdentityMatches(rows[index], baseRows[index]))
                {
                    throw new InvalidDataException(
                        $"Pokemon yield restore base identity does not match personal row {index.ToString(CultureInfo.InvariantCulture)}.");
                }
            }
        }

        return new PersonalArrayRows(rows, requiresLegacyDexOrderRepair, baseRows);
    }

    private IReadOnlyList<PersonalRow> ReadVerifiedBaseRows(
        OpenedProject project,
        ZaPokemonWorkflow workflow)
    {
        var baseSource = fileSource.ReadBase(project, ZaDataPaths.PersonalArray);
        var baseTable = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(baseSource.Bytes));
        if (baseTable.HasLegacyByteZADexOrderLayout)
        {
            throw new InvalidDataException(
                "The configured clean base personal table uses the malformed legacy Pokédex layout.");
        }

        var rows = ReadTableRows(baseTable);
        foreach (var pokemon in workflow.Pokemon)
        {
            if (pokemon.PersonalId < 0
                || pokemon.PersonalId >= rows.Count
                || rows[pokemon.PersonalId].Species is not { } species
                || species.Species != pokemon.SpeciesId
                || species.Form != pokemon.Form)
            {
                throw new InvalidDataException(
                    $"Clean base personal row {pokemon.PersonalId.ToString(CultureInfo.InvariantCulture)} does not match the loaded Pokemon identity.");
            }
        }

        return rows;
    }

    private static IReadOnlyList<PersonalRow> ReadTableRows(ZaPersonalTable table)
    {
        var rows = new List<PersonalRow>(table.EntryLength);
        for (var index = 0; index < table.EntryLength; index++)
        {
            var row = table.Entry(index);
            rows.Add(row is null
                ? PersonalRow.Empty()
                : PersonalRow.From(row.Value, baseRow: null, hasLegacyByteDexOrderLayout: false));
        }

        return rows;
    }

    private static byte[] WriteRows(IReadOnlyList<PersonalRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        builder.ForceDefaults = true;
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = ZaPersonalTable.CreateEntryVector(builder, offsets);
        ZaPersonalTable.Start(builder);
        ZaPersonalTable.AddEntry(builder, vector);
        var root = ZaPersonalTable.End(builder);
        ZaPersonalTable.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static bool RequiresPersonalArrayRebuild(
        IReadOnlyList<PersonalRow> rows,
        IEnumerable<PendingEdit> edits)
    {
        var evolutionLengths = new Dictionary<int, int>();
        var learnsetLengths = new Dictionary<int, int>();
        foreach (var edit in edits)
        {
            if (IsGlobalYieldEdit(edit))
            {
                return true;
            }

            if (!string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
                || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
                || personalId < 0
                || personalId >= rows.Count)
            {
                continue;
            }

            var row = rows[personalId];
            if (TryParseEvolutionField(edit.Field, out var evolutionAction, out var evolutionSlot)
                && TryParseEvolutionValue(edit.NewValue, out _))
            {
                var length = evolutionLengths.TryGetValue(personalId, out var currentLength)
                    ? currentLength
                    : row.Evolutions.Count;
                if (!row.HasEvolutions || ((evolutionAction == AddAction || evolutionAction == UpsertAction) && evolutionSlot >= length))
                {
                    return true;
                }

                evolutionLengths[personalId] = ApplyVectorLengthOverlay(length, evolutionAction, evolutionSlot);
                continue;
            }

            if (TryParseLearnsetField(edit.Field, out var learnsetAction, out var learnsetSlot)
                && TryParseOperationValue(edit.NewValue, out _, out _))
            {
                var length = learnsetLengths.TryGetValue(personalId, out var currentLength)
                    ? currentLength
                    : row.LevelupMoves.Count;
                if (!row.HasLevelupMoves || ((learnsetAction == AddAction || learnsetAction == UpsertAction) && learnsetSlot >= length))
                {
                    return true;
                }

                learnsetLengths[personalId] = ApplyVectorLengthOverlay(length, learnsetAction, learnsetSlot);
                continue;
            }

            if (TryParseCompatibilityField(edit.Field, out var groupId, out var slot)
                && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var compatibilityValue))
            {
                if (!RequiresCompatibilityRebuild(row, groupId, slot, compatibilityValue != 0))
                {
                    continue;
                }

                return true;
            }

            if (RequiresPersonalFieldRebuild(row, edit.Field))
            {
                return true;
            }
        }

        return false;
    }

    private static int ApplyVectorLengthOverlay(int length, string action, int slot)
    {
        return action switch
        {
            AddAction or UpsertAction when slot >= length => slot + 1,
            RemoveAction when slot >= 0 && slot < length => length - 1,
            _ => length,
        };
    }

    private static bool RequiresCompatibilityRebuild(PersonalRow row, string groupId, int slot, bool enabled)
    {
        if (string.Equals(groupId, ZaPokemonWorkflowService.TechnicalMachineCompatibilityGroupId, StringComparison.Ordinal))
        {
            if (!row.HasTmMoves)
            {
                return true;
            }

            if (slot is < 0 or > ushort.MaxValue)
            {
                return false;
            }

            var move = (ushort)slot;
            return enabled && !row.TmMoves.Contains(move) && !row.TmMoves.Contains(0);
        }

        if (string.Equals(groupId, ZaPokemonWorkflowService.EggMoveCompatibilityGroupId, StringComparison.Ordinal))
        {
            return !row.HasEggMoves;
        }

        if (string.Equals(groupId, ZaPokemonWorkflowService.ReminderMoveCompatibilityGroupId, StringComparison.Ordinal))
        {
            return !row.HasReminderMoves;
        }

        return false;
    }

    private static bool RequiresPersonalFieldRebuild(PersonalRow row, string? field)
    {
        return field switch
        {
            ZaPokemonWorkflowService.HPField or
            ZaPokemonWorkflowService.AttackField or
            ZaPokemonWorkflowService.DefenseField or
            ZaPokemonWorkflowService.SpecialAttackField or
            ZaPokemonWorkflowService.SpecialDefenseField or
            ZaPokemonWorkflowService.SpeedField => !row.HasBaseStats,
            ZaPokemonWorkflowService.Type1Field => !row.HasType1,
            ZaPokemonWorkflowService.Type2Field => !row.HasType2,
            ZaPokemonWorkflowService.Ability1Field => !row.HasAbility1,
            ZaPokemonWorkflowService.Ability2Field => !row.HasAbility2,
            ZaPokemonWorkflowService.HiddenAbilityField => !row.HasAbilityHidden,
            ZaPokemonWorkflowService.CatchRateField => !row.HasCatchRate,
            ZaPokemonWorkflowService.EvolutionStageField => !row.HasEvoStage,
            ZaPokemonWorkflowService.EVYieldHPField or
            ZaPokemonWorkflowService.EVYieldAttackField or
            ZaPokemonWorkflowService.EVYieldDefenseField or
            ZaPokemonWorkflowService.EVYieldSpecialAttackField or
            ZaPokemonWorkflowService.EVYieldSpecialDefenseField or
            ZaPokemonWorkflowService.EVYieldSpeedField => !row.HasEvYield,
            ZaPokemonWorkflowService.GenderRatioField => !row.HasGender,
            ZaPokemonWorkflowService.HatchCyclesField => !row.HasEggHatchCycles,
            ZaPokemonWorkflowService.BaseFriendshipField => !row.HasBaseFriendship,
            ZaPokemonWorkflowService.BaseExperienceField => true,
            ZaPokemonWorkflowService.ExpGrowthField => !row.HasXpGrowth,
            ZaPokemonWorkflowService.EggGroup1Field => !row.HasEggGroup1,
            ZaPokemonWorkflowService.EggGroup2Field => !row.HasEggGroup2,
            ZaPokemonWorkflowService.FormField or
            ZaPokemonWorkflowService.ModelIdField or
            ZaPokemonWorkflowService.ColorField or
            ZaPokemonWorkflowService.HeightField or
            ZaPokemonWorkflowService.WeightField => !row.HasSpecies,
            ZaPokemonWorkflowService.HatchedSpeciesField => !row.HasEggHatch,
            ZaPokemonWorkflowService.IsPresentInGameField => !row.HasIsPresent,
            ZaPokemonWorkflowService.RegionalDexIndexField => !row.HasZADexOrder,
            _ => false,
        };
    }

    private static byte[] ApplyPersonalArrayBinaryPatch(
        byte[] sourceBytes,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var patchedBytes = sourceBytes.ToArray();
        foreach (var edit in edits)
        {
            if (!string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Pokemon Data.",
                    ZaEditSessionSupport.PokemonDomain,
                    expected: ZaEditSessionSupport.PokemonDomain));
                continue;
            }

            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon Data edit targets an invalid personal record.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: "personalId",
                    expected: "Existing Pokemon personal record"));
                continue;
            }

            if (!TryGetPersonalRowTableOffset(patchedBytes, personalId, edit.Field, diagnostics, out var personalOffset))
            {
                continue;
            }

            if (TryParseCompatibilityField(edit.Field, out var compatibilityGroupId, out var compatibilitySlot))
            {
                ApplyCompatibilityBinaryPatch(patchedBytes, personalOffset, edit, compatibilityGroupId, compatibilitySlot, diagnostics);
                continue;
            }

            if (TryParseLearnsetField(edit.Field, out _, out _))
            {
                ApplyLearnsetBinaryPatch(patchedBytes, personalOffset, edit, diagnostics);
                continue;
            }

            if (TryParseEvolutionField(edit.Field, out _, out _))
            {
                ApplyEvolutionBinaryPatch(patchedBytes, personalOffset, edit, diagnostics);
                continue;
            }

            if (!int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon Data edit value is invalid.",
                    ZaEditSessionSupport.PokemonDomain,
                    field: edit.Field,
                    expected: "Integer value"));
                continue;
            }

            if (!TryApplyPersonalFieldBinaryPatch(patchedBytes, personalOffset, edit.Field, value, diagnostics))
            {
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            }
        }

        return patchedBytes;
    }

    private static bool TryApplyPersonalFieldBinaryPatch(
        byte[] data,
        int personalOffset,
        string? field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            switch (field)
            {
                case ZaPokemonWorkflowService.HPField:
                    return TryPatchStructByteField(data, personalOffset, PersonalBaseStatsFieldIndex, 0, value, field, diagnostics);
                case ZaPokemonWorkflowService.AttackField:
                    return TryPatchStructByteField(data, personalOffset, PersonalBaseStatsFieldIndex, 1, value, field, diagnostics);
                case ZaPokemonWorkflowService.DefenseField:
                    return TryPatchStructByteField(data, personalOffset, PersonalBaseStatsFieldIndex, 2, value, field, diagnostics);
                case ZaPokemonWorkflowService.SpecialAttackField:
                    return TryPatchStructByteField(data, personalOffset, PersonalBaseStatsFieldIndex, 3, value, field, diagnostics);
                case ZaPokemonWorkflowService.SpecialDefenseField:
                    return TryPatchStructByteField(data, personalOffset, PersonalBaseStatsFieldIndex, 4, value, field, diagnostics);
                case ZaPokemonWorkflowService.SpeedField:
                    return TryPatchStructByteField(data, personalOffset, PersonalBaseStatsFieldIndex, 5, value, field, diagnostics);
                case ZaPokemonWorkflowService.Type1Field:
                    return TryPatchByteTableField(data, personalOffset, PersonalType1FieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.Type2Field:
                    return TryPatchByteTableField(data, personalOffset, PersonalType2FieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.Ability1Field:
                    return TryPatchUShortTableField(data, personalOffset, PersonalAbility1FieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.Ability2Field:
                    return TryPatchUShortTableField(data, personalOffset, PersonalAbility2FieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.HiddenAbilityField:
                    return TryPatchUShortTableField(data, personalOffset, PersonalHiddenAbilityFieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.CatchRateField:
                    return TryPatchByteTableField(data, personalOffset, PersonalCatchRateFieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.EvolutionStageField:
                    return TryPatchByteTableField(data, personalOffset, PersonalEvolutionStageFieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.EVYieldHPField:
                    return TryPatchStructByteField(data, personalOffset, PersonalEvYieldFieldIndex, 0, value, field, diagnostics);
                case ZaPokemonWorkflowService.EVYieldAttackField:
                    return TryPatchStructByteField(data, personalOffset, PersonalEvYieldFieldIndex, 1, value, field, diagnostics);
                case ZaPokemonWorkflowService.EVYieldDefenseField:
                    return TryPatchStructByteField(data, personalOffset, PersonalEvYieldFieldIndex, 2, value, field, diagnostics);
                case ZaPokemonWorkflowService.EVYieldSpecialAttackField:
                    return TryPatchStructByteField(data, personalOffset, PersonalEvYieldFieldIndex, 3, value, field, diagnostics);
                case ZaPokemonWorkflowService.EVYieldSpecialDefenseField:
                    return TryPatchStructByteField(data, personalOffset, PersonalEvYieldFieldIndex, 4, value, field, diagnostics);
                case ZaPokemonWorkflowService.EVYieldSpeedField:
                    return TryPatchStructByteField(data, personalOffset, PersonalEvYieldFieldIndex, 5, value, field, diagnostics);
                case ZaPokemonWorkflowService.GenderRatioField:
                    return TryPatchStructByteField(data, personalOffset, PersonalGenderFieldIndex, 1, value, field, diagnostics);
                case ZaPokemonWorkflowService.HatchCyclesField:
                    return TryPatchByteTableField(data, personalOffset, PersonalEggHatchCyclesFieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.BaseFriendshipField:
                    return TryPatchByteTableField(data, personalOffset, PersonalBaseFriendshipFieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.ExpGrowthField:
                    return TryPatchByteTableField(data, personalOffset, PersonalXpGrowthFieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.EggGroup1Field:
                    return TryPatchByteTableField(data, personalOffset, PersonalEggGroup1FieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.EggGroup2Field:
                    return TryPatchByteTableField(data, personalOffset, PersonalEggGroup2FieldIndex, value, field, diagnostics);
                case ZaPokemonWorkflowService.FormField:
                    return TryPatchStructUShortField(data, personalOffset, PersonalSpeciesFieldIndex, 2, value, field, diagnostics);
                case ZaPokemonWorkflowService.ModelIdField:
                    return TryPatchStructUShortField(data, personalOffset, PersonalSpeciesFieldIndex, 4, value, field, diagnostics);
                case ZaPokemonWorkflowService.ColorField:
                    return TryPatchStructByteField(data, personalOffset, PersonalSpeciesFieldIndex, 6, value, field, diagnostics);
                case ZaPokemonWorkflowService.HeightField:
                    return TryPatchStructUShortField(data, personalOffset, PersonalSpeciesFieldIndex, 8, value, field, diagnostics);
                case ZaPokemonWorkflowService.WeightField:
                    return TryPatchStructUShortField(data, personalOffset, PersonalSpeciesFieldIndex, 10, value, field, diagnostics);
                case ZaPokemonWorkflowService.HatchedSpeciesField:
                    return TryPatchStructUShortField(data, personalOffset, PersonalEggHatchFieldIndex, 0, value, field, diagnostics);
                case ZaPokemonWorkflowService.IsPresentInGameField:
                    return TryPatchBoolTableField(data, personalOffset, PersonalIsPresentFieldIndex, value != 0, field, diagnostics);
                case ZaPokemonWorkflowService.RegionalDexIndexField:
                    return TryPatchUShortTableField(data, personalOffset, PersonalZaDexOrderFieldIndex, value, field, diagnostics);
                default:
                    return false;
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
            return true;
        }
    }

    private static void ApplyEvolutionBinaryPatch(
        byte[] data,
        int personalOffset,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var operation = ParseEvolutionOperation(edit, null, diagnostics);
        if (operation is null
            || !TryGetTableVector(data, personalOffset, PersonalEvolutionsFieldIndex, edit.Field, diagnostics, out var vectorOffset, out var length))
        {
            return;
        }

        try
        {
            switch (operation.Action)
            {
                case AddAction:
                case UpsertAction:
                    if (!TryGetStructVectorElementOffset(data, vectorOffset, length, operation.Slot, EvolutionDataSize, edit.Field, diagnostics, out var elementOffset))
                    {
                        return;
                    }

                    WriteUShort(data, elementOffset, ToUshort(operation.Level ?? 0));
                    WriteUShort(data, elementOffset + 2, ToUshort(operation.Method ?? 0));
                    WriteUShort(data, elementOffset + 4, ToUshort(operation.Argument ?? 0));
                    WriteUShort(data, elementOffset + 12, ToUshort(operation.Species ?? 0));
                    WriteUShort(data, elementOffset + 14, ToUshort(operation.Form ?? 0));
                    break;
                case RemoveAction:
                    if (TryGetStructVectorElementOffset(data, vectorOffset, length, operation.Slot, EvolutionDataSize, edit.Field, diagnostics, out _)
                        && TryGetStructVectorElementOffset(data, vectorOffset, length, length - 1, EvolutionDataSize, edit.Field, diagnostics, out _))
                    {
                        RemoveStructVectorElement(data, vectorOffset, length, operation.Slot, EvolutionDataSize);
                    }

                    break;
                case MoveUpAction:
                    MoveStructVectorElement(data, vectorOffset, length, operation.Slot, operation.Slot - 1, EvolutionDataSize, edit.Field, diagnostics);
                    break;
                case MoveDownAction:
                    MoveStructVectorElement(data, vectorOffset, length, operation.Slot, operation.Slot + 1, EvolutionDataSize, edit.Field, diagnostics);
                    break;
                case MoveToAction:
                    MoveStructVectorElement(data, vectorOffset, length, operation.Slot, operation.Method ?? -1, EvolutionDataSize, edit.Field, diagnostics);
                    break;
                default:
                    diagnostics.Add(OperationDiagnostic($"Evolution action '{operation.Action}' is not supported.", "action"));
                    break;
            }
        }
        catch (OverflowException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon evolution edit value is outside the target field range.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Safe evolution value"));
        }
    }

    private static void ApplyLearnsetBinaryPatch(
        byte[] data,
        int personalOffset,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var operation = ParseLearnsetOperation(edit, null, diagnostics);
        if (operation is null
            || !TryGetTableVector(data, personalOffset, PersonalLevelupMovesFieldIndex, edit.Field, diagnostics, out var vectorOffset, out var length))
        {
            return;
        }

        try
        {
            switch (operation.Action)
            {
                case AddAction:
                case UpsertAction:
                    if (!TryGetStructVectorElementOffset(data, vectorOffset, length, operation.Slot, LevelupMoveDataSize, edit.Field, diagnostics, out var elementOffset))
                    {
                        return;
                    }

                    WriteUShort(data, elementOffset, ToUshort(operation.MoveId ?? 0));
                    WriteUShort(data, elementOffset + 2, ToUshort(operation.RawLevel ?? operation.Level ?? 1));
                    break;
                case RemoveAction:
                    if (TryGetStructVectorElementOffset(data, vectorOffset, length, operation.Slot, LevelupMoveDataSize, edit.Field, diagnostics, out _)
                        && TryGetStructVectorElementOffset(data, vectorOffset, length, length - 1, LevelupMoveDataSize, edit.Field, diagnostics, out _))
                    {
                        RemoveStructVectorElement(data, vectorOffset, length, operation.Slot, LevelupMoveDataSize);
                    }

                    break;
                case MoveUpAction:
                    MoveLearnsetMoveIdsKeepingSlotLevels(data, vectorOffset, length, operation.Slot, operation.Slot - 1, edit.Field, diagnostics);
                    break;
                case MoveDownAction:
                    MoveLearnsetMoveIdsKeepingSlotLevels(data, vectorOffset, length, operation.Slot, operation.Slot + 1, edit.Field, diagnostics);
                    break;
                case MoveToAction:
                    MoveLearnsetMoveIdsKeepingSlotLevels(data, vectorOffset, length, operation.Slot, operation.MoveId ?? -1, edit.Field, diagnostics);
                    break;
                default:
                    diagnostics.Add(OperationDiagnostic($"Learnset action '{operation.Action}' is not supported.", "action"));
                    break;
            }
        }
        catch (OverflowException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon learnset edit value is outside the target field range.",
                ZaEditSessionSupport.PokemonDomain,
                field: edit.Field,
                expected: "Safe learnset value"));
        }
    }

    private static void ApplyCompatibilityBinaryPatch(
        byte[] data,
        int personalOffset,
        PendingEdit edit,
        string groupId,
        int slot,
        ICollection<ValidationDiagnostic> diagnostics)
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

        var enabled = parsed != 0;
        if (string.Equals(groupId, ZaPokemonWorkflowService.TechnicalMachineCompatibilityGroupId, StringComparison.Ordinal))
        {
            if (!TryGetTableVector(data, personalOffset, PersonalTmMovesFieldIndex, edit.Field, diagnostics, out var vectorOffset, out var length))
            {
                return;
            }

            if (enabled)
            {
                AddUShortVectorValue(data, vectorOffset, length, ToUshort(slot), edit.Field, diagnostics);
            }
            else
            {
                RemoveUShortVectorValue(data, vectorOffset, length, ToUshort(slot), edit.Field, diagnostics);
            }

            return;
        }

        var fieldIndex = groupId switch
        {
            ZaPokemonWorkflowService.EggMoveCompatibilityGroupId => PersonalEggMovesFieldIndex,
            ZaPokemonWorkflowService.ReminderMoveCompatibilityGroupId => PersonalReminderMovesFieldIndex,
            _ => -1,
        };
        if (fieldIndex < 0)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!TryGetTableVector(data, personalOffset, fieldIndex, edit.Field, diagnostics, out var moveVectorOffset, out var moveCount)
            || !TryGetUShortVectorElementOffset(data, moveVectorOffset, moveCount, slot, edit.Field, diagnostics, out _))
        {
            return;
        }

        if (!enabled)
        {
            RemoveUShortVectorElement(data, moveVectorOffset, moveCount, slot);
        }
    }

    private static bool TryPatchBoolTableField(
        byte[] data,
        int tableOffset,
        int fieldIndex,
        bool value,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryGetTableFieldLocation(data, tableOffset, fieldIndex, field, diagnostics, out var location)
            || !TryEnsureRange(data, location, 1, field, diagnostics))
        {
            return false;
        }

        data[location] = value ? (byte)1 : (byte)0;
        return true;
    }

    private static bool TryPatchByteTableField(
        byte[] data,
        int tableOffset,
        int fieldIndex,
        int value,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryGetTableFieldLocation(data, tableOffset, fieldIndex, field, diagnostics, out var location)
            || !TryEnsureRange(data, location, 1, field, diagnostics))
        {
            return false;
        }

        data[location] = ToByte(value);
        return true;
    }

    private static bool TryPatchUShortTableField(
        byte[] data,
        int tableOffset,
        int fieldIndex,
        int value,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryGetTableFieldLocation(data, tableOffset, fieldIndex, field, diagnostics, out var location)
            || !TryEnsureRange(data, location, sizeof(ushort), field, diagnostics))
        {
            return false;
        }

        WriteUShort(data, location, ToUshort(value));
        return true;
    }

    private static bool TryPatchStructByteField(
        byte[] data,
        int tableOffset,
        int fieldIndex,
        int structFieldOffset,
        int value,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryGetTableFieldLocation(data, tableOffset, fieldIndex, field, diagnostics, out var structOffset))
        {
            return false;
        }

        var location = structOffset + structFieldOffset;
        if (!TryEnsureRange(data, location, 1, field, diagnostics))
        {
            return false;
        }

        data[location] = ToByte(value);
        return true;
    }

    private static bool TryPatchStructUShortField(
        byte[] data,
        int tableOffset,
        int fieldIndex,
        int structFieldOffset,
        int value,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryGetTableFieldLocation(data, tableOffset, fieldIndex, field, diagnostics, out var structOffset))
        {
            return false;
        }

        var location = structOffset + structFieldOffset;
        if (!TryEnsureRange(data, location, sizeof(ushort), field, diagnostics))
        {
            return false;
        }

        WriteUShort(data, location, ToUshort(value));
        return true;
    }

    private static bool TryGetPersonalRowTableOffset(
        byte[] data,
        int personalId,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics,
        out int personalOffset)
    {
        personalOffset = 0;
        if (!TryGetRootTableOffset(data, field, diagnostics, out var rootOffset)
            || !TryGetTableVector(data, rootOffset, PersonalTableEntryFieldIndex, field, diagnostics, out var entryVectorOffset, out var entryCount))
        {
            return false;
        }

        if (personalId < 0 || personalId >= entryCount)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets a record outside the personal table.",
                ZaEditSessionSupport.PokemonDomain,
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return false;
        }

        var entryOffsetLocation = entryVectorOffset + sizeof(int) + personalId * sizeof(int);
        return TryReadUOffsetTarget(data, entryOffsetLocation, field, diagnostics, out personalOffset);
    }

    private static bool TryGetRootTableOffset(
        byte[] data,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics,
        out int rootOffset)
    {
        rootOffset = 0;
        if (!TryEnsureRange(data, 0, sizeof(int), field, diagnostics))
        {
            return false;
        }

        rootOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, sizeof(int)));
        if (!TryEnsureRange(data, rootOffset, sizeof(int), field, diagnostics))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetTableVector(
        byte[] data,
        int tableOffset,
        int fieldIndex,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics,
        out int vectorOffset,
        out int length)
    {
        vectorOffset = 0;
        length = 0;
        if (!TryGetTableFieldLocation(data, tableOffset, fieldIndex, field, diagnostics, out var fieldLocation)
            || !TryReadUOffsetTarget(data, fieldLocation, field, diagnostics, out vectorOffset)
            || !TryEnsureRange(data, vectorOffset, sizeof(int), field, diagnostics))
        {
            return false;
        }

        length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(vectorOffset, sizeof(int)));
        if (length < 0)
        {
            AddBinaryPatchDiagnostic(diagnostics, "Z-A Pokemon Data vector length is invalid.", field);
            return false;
        }

        return true;
    }

    private static bool TryGetTableFieldLocation(
        byte[] data,
        int tableOffset,
        int fieldIndex,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics,
        out int location)
    {
        location = 0;
        if (!TryEnsureRange(data, tableOffset, sizeof(int), field, diagnostics))
        {
            return false;
        }

        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        if (!TryEnsureRange(data, vtableOffset, sizeof(ushort) * 2, field, diagnostics))
        {
            return false;
        }

        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(vtableOffset, sizeof(ushort)));
        var fieldOffsetLocation = vtableOffset + sizeof(ushort) * 2 + fieldIndex * sizeof(ushort);
        if (fieldOffsetLocation + sizeof(ushort) > vtableOffset + vtableLength)
        {
            AddBinaryPatchDiagnostic(
                diagnostics,
                "Z-A Pokemon Data edit could not be written safely because the target FlatBuffer field is not present in the original personal record.",
                field);
            return false;
        }

        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(fieldOffsetLocation, sizeof(ushort)));
        if (fieldOffset == 0)
        {
            AddBinaryPatchDiagnostic(
                diagnostics,
                "Z-A Pokemon Data edit could not be written safely because the target FlatBuffer field is not present in the original personal record.",
                field);
            return false;
        }

        location = tableOffset + fieldOffset;
        if (!TryEnsureRange(data, location, 1, field, diagnostics))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadUOffsetTarget(
        byte[] data,
        int offsetLocation,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics,
        out int targetOffset)
    {
        targetOffset = 0;
        if (!TryEnsureRange(data, offsetLocation, sizeof(uint), field, diagnostics))
        {
            return false;
        }

        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offsetLocation, sizeof(uint)));
        if (relativeOffset == 0 || relativeOffset > int.MaxValue)
        {
            AddBinaryPatchDiagnostic(diagnostics, "Z-A Pokemon Data contains an invalid FlatBuffer offset.", field);
            return false;
        }

        var calculatedOffset = (long)offsetLocation + relativeOffset;
        if (calculatedOffset > int.MaxValue)
        {
            AddBinaryPatchDiagnostic(diagnostics, "Z-A Pokemon Data contains an invalid FlatBuffer offset.", field);
            return false;
        }

        targetOffset = (int)calculatedOffset;
        return TryEnsureRange(data, targetOffset, 1, field, diagnostics);
    }

    private static bool TryGetStructVectorElementOffset(
        byte[] data,
        int vectorOffset,
        int length,
        int slot,
        int structSize,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics,
        out int elementOffset)
    {
        elementOffset = 0;
        if (slot < 0 || slot >= length)
        {
            AddBinaryPatchDiagnostic(
                diagnostics,
                "Z-A Pokemon Data edit needs an existing vector slot so the personal table can be patched without rebuilding it.",
                field);
            return false;
        }

        var calculatedOffset = (long)vectorOffset + sizeof(int) + (long)slot * structSize;
        if (calculatedOffset > int.MaxValue)
        {
            AddBinaryPatchDiagnostic(diagnostics, "Z-A Pokemon Data vector slot is outside the source file.", field);
            return false;
        }

        elementOffset = (int)calculatedOffset;
        return TryEnsureRange(data, elementOffset, structSize, field, diagnostics);
    }

    private static void RemoveStructVectorElement(byte[] data, int vectorOffset, int length, int slot, int structSize)
    {
        var elementStart = vectorOffset + sizeof(int);
        var destination = elementStart + slot * structSize;
        var source = destination + structSize;
        var bytesToMove = (length - slot - 1) * structSize;
        if (bytesToMove > 0)
        {
            Buffer.BlockCopy(data, source, data, destination, bytesToMove);
        }

        Array.Clear(data, elementStart + (length - 1) * structSize, structSize);
    }

    private static void MoveStructVectorElement(
        byte[] data,
        int vectorOffset,
        int length,
        int sourceSlot,
        int destinationSlot,
        int structSize,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryGetStructVectorElementOffset(data, vectorOffset, length, sourceSlot, structSize, field, diagnostics, out var sourceOffset)
            || !TryGetStructVectorElementOffset(data, vectorOffset, length, destinationSlot, structSize, field, diagnostics, out var destinationOffset)
            || sourceSlot == destinationSlot)
        {
            return;
        }

        var moved = data.AsSpan(sourceOffset, structSize).ToArray();
        if (destinationSlot < sourceSlot)
        {
            Buffer.BlockCopy(data, destinationOffset, data, destinationOffset + structSize, (sourceSlot - destinationSlot) * structSize);
        }
        else
        {
            Buffer.BlockCopy(data, sourceOffset + structSize, data, sourceOffset, (destinationSlot - sourceSlot) * structSize);
        }

        moved.CopyTo(data.AsSpan(destinationOffset, structSize));
    }

    private static void MoveLearnsetMoveIdsKeepingSlotLevels(
        byte[] data,
        int vectorOffset,
        int length,
        int sourceSlot,
        int destinationSlot,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryGetStructVectorElementOffset(
                data,
                vectorOffset,
                length,
                sourceSlot,
                LevelupMoveDataSize,
                field,
                diagnostics,
                out var sourceOffset)
            || !TryGetStructVectorElementOffset(
                data,
                vectorOffset,
                length,
                destinationSlot,
                LevelupMoveDataSize,
                field,
                diagnostics,
                out var destinationOffset)
            || sourceSlot == destinationSlot)
        {
            return;
        }

        var elementStart = vectorOffset + sizeof(int);
        var movedMoveId = ReadUShort(data, sourceOffset);
        if (destinationSlot < sourceSlot)
        {
            for (var slot = sourceSlot; slot > destinationSlot; slot--)
            {
                var currentOffset = elementStart + slot * LevelupMoveDataSize;
                WriteUShort(data, currentOffset, ReadUShort(data, currentOffset - LevelupMoveDataSize));
            }
        }
        else
        {
            for (var slot = sourceSlot; slot < destinationSlot; slot++)
            {
                var currentOffset = elementStart + slot * LevelupMoveDataSize;
                WriteUShort(data, currentOffset, ReadUShort(data, currentOffset + LevelupMoveDataSize));
            }
        }

        WriteUShort(data, destinationOffset, movedMoveId);
    }

    private static void AddUShortVectorValue(
        byte[] data,
        int vectorOffset,
        int length,
        ushort value,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var values = ReadUShortVector(data, vectorOffset, length, field, diagnostics);
        if (values is null || values.Contains(value))
        {
            return;
        }

        var emptyIndex = Array.IndexOf(values, (ushort)0);
        if (emptyIndex < 0)
        {
            AddBinaryPatchDiagnostic(
                diagnostics,
                "Z-A Pokemon compatibility edit needs an existing empty move slot so the personal table can be patched without rebuilding it.",
                field);
            return;
        }

        values[emptyIndex] = value;
        var sorted = values
            .Where(candidate => candidate != 0)
            .Order()
            .Concat(values.Where(candidate => candidate == 0))
            .ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            WriteUShortVectorElement(data, vectorOffset, index, sorted[index]);
        }
    }

    private static void RemoveUShortVectorValue(
        byte[] data,
        int vectorOffset,
        int length,
        ushort value,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var values = ReadUShortVector(data, vectorOffset, length, field, diagnostics);
        if (values is null)
        {
            return;
        }

        var index = Array.IndexOf(values, value);
        if (index >= 0)
        {
            RemoveUShortVectorElement(data, vectorOffset, length, index);
        }
    }

    private static ushort[]? ReadUShortVector(
        byte[] data,
        int vectorOffset,
        int length,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var values = new ushort[length];
        for (var index = 0; index < values.Length; index++)
        {
            if (!TryGetUShortVectorElementOffset(data, vectorOffset, length, index, field, diagnostics, out var elementOffset))
            {
                return null;
            }

            values[index] = ReadUShort(data, elementOffset);
        }

        return values;
    }

    private static bool TryGetUShortVectorElementOffset(
        byte[] data,
        int vectorOffset,
        int length,
        int slot,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics,
        out int elementOffset)
    {
        elementOffset = 0;
        if (slot < 0 || slot >= length)
        {
            AddBinaryPatchDiagnostic(
                diagnostics,
                "Z-A Pokemon compatibility edit targets a move slot that is not loaded.",
                field);
            return false;
        }

        elementOffset = vectorOffset + sizeof(int) + slot * sizeof(ushort);
        return TryEnsureRange(data, elementOffset, sizeof(ushort), field, diagnostics);
    }

    private static void RemoveUShortVectorElement(byte[] data, int vectorOffset, int length, int slot)
    {
        var elementStart = vectorOffset + sizeof(int);
        var destination = elementStart + slot * sizeof(ushort);
        var source = destination + sizeof(ushort);
        var bytesToMove = (length - slot - 1) * sizeof(ushort);
        if (bytesToMove > 0)
        {
            Buffer.BlockCopy(data, source, data, destination, bytesToMove);
        }

        WriteUShortVectorElement(data, vectorOffset, length - 1, 0);
    }

    private static ushort ReadUShort(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
    }

    private static void WriteUShortVectorElement(byte[] data, int vectorOffset, int index, ushort value)
    {
        WriteUShort(data, vectorOffset + sizeof(int) + index * sizeof(ushort), value);
    }

    private static bool TryEnsureRange(
        byte[] data,
        int offset,
        int length,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (offset >= 0 && length >= 0 && offset <= data.Length - length)
        {
            return true;
        }

        AddBinaryPatchDiagnostic(diagnostics, "Z-A Pokemon Data edit points outside the source file.", field);
        return false;
    }

    private static void WriteUShort(byte[] data, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), value);
    }

    private static void AddBinaryPatchDiagnostic(
        ICollection<ValidationDiagnostic> diagnostics,
        string message,
        string? field)
    {
        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.PokemonDomain,
            field: field,
            expected: "Existing in-place Z-A personal table data"));
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

                if (operation.Action == AddAction && operation.Slot != pokemon.Learnset.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset add must target the next empty row.", "slot"));
                }
                else if (operation.Action == UpsertAction
                    && (operation.Slot < 0 || operation.Slot > pokemon.Learnset.Count))
                {
                    diagnostics.Add(OperationDiagnostic("Learnset upsert must target an existing row or the next empty row.", "slot"));
                }

                break;
            case RemoveAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Learnset.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset remove must target an existing row.", "slot"));
                }

                break;
            case MoveUpAction:
                if (operation.Slot <= 0 || operation.Slot >= pokemon.Learnset.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset move-up must target a row below the first row.", "slot"));
                }

                break;
            case MoveDownAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Learnset.Count - 1)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset move-down must target a row above the last row.", "slot"));
                }

                break;
            case MoveToAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Learnset.Count || operation.MoveId is null or < 0 || operation.MoveId >= pokemon.Learnset.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset move-to requires loaded source and destination slots.", "slot"));
                }
                else if (operation.Slot == operation.MoveId)
                {
                    diagnostics.Add(OperationDiagnostic("Learnset move-to source and destination rows must be different.", "slot"));
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
                else
                {
                    var method = ZaPokemonWorkflowService.GetEvolutionMethodDefinition(operation.Method.Value);
                    var preservesExistingIgnoredLevel = operation.Action == UpsertAction
                        && operation.Slot >= 0
                        && operation.Slot < pokemon.Evolutions.Count
                        && pokemon.Evolutions[operation.Slot].Method == operation.Method.Value
                        && pokemon.Evolutions[operation.Slot].Level == operation.Level.Value;
                    if (!method.UsesLevel
                        && operation.Level.Value != 0
                        && !preservesExistingIgnoredLevel)
                    {
                        diagnostics.Add(OperationDiagnostic(
                            $"Evolution method '{method.Name}' does not use the Level field. Set Level to 0, or choose a level-up method such as Level Up Held Item Day/Night.",
                            "level"));
                    }
                }

                if (operation.Action == AddAction && operation.Slot != pokemon.Evolutions.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution add must target the next empty row.", "slot"));
                }
                else if (operation.Action == UpsertAction
                    && (operation.Slot < 0 || operation.Slot > pokemon.Evolutions.Count))
                {
                    diagnostics.Add(OperationDiagnostic("Evolution upsert must target an existing row or the next empty row.", "slot"));
                }

                break;
            case RemoveAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Evolutions.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution remove must target an existing row.", "slot"));
                }

                break;
            case MoveUpAction:
                if (operation.Slot <= 0 || operation.Slot >= pokemon.Evolutions.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution move-up must target a row below the first row.", "slot"));
                }

                break;
            case MoveDownAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Evolutions.Count - 1)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution move-down must target a row above the last row.", "slot"));
                }

                break;
            case MoveToAction:
                if (operation.Slot < 0 || operation.Slot >= pokemon.Evolutions.Count || operation.Method is null or < 0 || operation.Method >= pokemon.Evolutions.Count)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution move-to requires loaded source and destination slots.", "slot"));
                }
                else if (operation.Slot == operation.Method)
                {
                    diagnostics.Add(OperationDiagnostic("Evolution move-to source and destination rows must be different.", "slot"));
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

    private static int CalculateBaseStatTotal(StatInfoRow? stats)
    {
        return stats is null
            ? 0
            : stats.Hp + stats.Atk + stats.Def + stats.Spa + stats.Spd + stats.Spe;
    }

    private static void ApplyBaseExperience(PersonalRow row, int baseExperience)
    {
        if (!ZaPokemonExperience.TryCalculateExpAddend(
                CalculateBaseStatTotal(row.BaseStats),
                row.EvoStage,
                baseExperience,
                out var expAddend))
        {
            throw new InvalidDataException(
                $"Base EXP {baseExperience.ToString(CultureInfo.InvariantCulture)} cannot be represented by Pokemon Legends Z-A's EXP addend.");
        }

        row.HasExpAddend = true;
        row.ExpAddend = expAddend;
    }

    private static bool PersonalRowIdentityMatches(PersonalRow row, PersonalRow baseRow)
    {
        return row.Species is { } species
            && baseRow.Species is { } baseSpecies
            && species.Species == baseSpecies.Species
            && species.Form == baseSpecies.Form;
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
        public bool HasSpecies { get; set; }
        public bool IsPresent { get; set; }
        public bool HasIsPresent { get; set; }
        public ushort ZADexOrder { get; set; }
        public bool HasZADexOrder { get; set; }
        public byte Type1 { get; set; }
        public bool HasType1 { get; set; }
        public byte Type2 { get; set; }
        public bool HasType2 { get; set; }
        public ushort Ability1 { get; set; }
        public bool HasAbility1 { get; set; }
        public ushort Ability2 { get; set; }
        public bool HasAbility2 { get; set; }
        public ushort AbilityHidden { get; set; }
        public bool HasAbilityHidden { get; set; }
        public byte XpGrowth { get; set; }
        public bool HasXpGrowth { get; set; }
        public byte CatchRate { get; set; }
        public bool HasCatchRate { get; set; }
        public GenderInfoRow? Gender { get; set; }
        public bool HasGender { get; set; }
        public byte EggGroup1 { get; set; }
        public bool HasEggGroup1 { get; set; }
        public byte EggGroup2 { get; set; }
        public bool HasEggGroup2 { get; set; }
        public EggHatchInfoRow? EggHatch { get; set; }
        public bool HasEggHatch { get; set; }
        public byte EggHatchCycles { get; set; }
        public bool HasEggHatchCycles { get; set; }
        public byte BaseFriendship { get; set; }
        public bool HasBaseFriendship { get; set; }
        public short ExpAddend { get; set; }
        public bool HasExpAddend { get; set; }
        public byte EvoStage { get; set; }
        public bool HasEvoStage { get; set; }
        public ushort Unknown18 { get; set; }
        public bool HasUnknown18 { get; set; }
        public StatInfoRow? EvYield { get; set; }
        public bool HasEvYield { get; set; }
        public StatInfoRow? BaseStats { get; set; }
        public bool HasBaseStats { get; set; }
        public bool HasEvolutions { get; set; }
        public bool HasTmMoves { get; set; }
        public bool HasEggMoves { get; set; }
        public bool HasReminderMoves { get; set; }
        public bool HasLevelupMoves { get; set; }
        public List<EvolutionRow> Evolutions { get; } = [];
        public List<ushort> TmMoves { get; } = [];
        public List<ushort> EggMoves { get; } = [];
        public List<ushort> ReminderMoves { get; } = [];
        public List<LevelupMoveRow> LevelupMoves { get; } = [];

        public static PersonalRow Empty()
        {
            return new PersonalRow();
        }

        public static PersonalRow From(
            ZaPersonal row,
            ZaPersonal? baseRow,
            bool hasLegacyByteDexOrderLayout)
        {
            var recoveredSpeciesReserved3 = ZaPersonalLegacyRecovery.ResolveSpeciesReserved3(
                row,
                baseRow,
                hasLegacyByteDexOrderLayout);
            var result = new PersonalRow
            {
                Species = row.Species is { } species
                    ? SpeciesInfoRow.From(species, recoveredSpeciesReserved3)
                    : null,
                HasSpecies = row.HasSpecies,
                IsPresent = row.IsPresent,
                HasIsPresent = row.HasIsPresent,
                ZADexOrder = ZaPersonalLegacyRecovery.ResolveZADexOrder(
                    row,
                    baseRow,
                    hasLegacyByteDexOrderLayout),
                HasZADexOrder = row.HasZADexOrder,
                Type1 = row.Type1,
                HasType1 = row.HasType1,
                Type2 = row.Type2,
                HasType2 = row.HasType2,
                Ability1 = row.Ability1,
                HasAbility1 = row.HasAbility1,
                Ability2 = row.Ability2,
                HasAbility2 = row.HasAbility2,
                AbilityHidden = row.AbilityHidden,
                HasAbilityHidden = row.HasAbilityHidden,
                XpGrowth = row.XpGrowth,
                HasXpGrowth = row.HasXpGrowth,
                CatchRate = row.CatchRate,
                HasCatchRate = row.HasCatchRate,
                Gender = row.Gender is { } gender ? GenderInfoRow.From(gender) : null,
                HasGender = row.HasGender,
                EggGroup1 = row.EggGroup1,
                HasEggGroup1 = row.HasEggGroup1,
                EggGroup2 = row.EggGroup2,
                HasEggGroup2 = row.HasEggGroup2,
                EggHatch = row.EggHatch is { } eggHatch ? EggHatchInfoRow.From(eggHatch) : null,
                HasEggHatch = row.HasEggHatch,
                EggHatchCycles = row.EggHatchCycles,
                HasEggHatchCycles = row.HasEggHatchCycles,
                BaseFriendship = row.BaseFriendship,
                HasBaseFriendship = row.HasBaseFriendship,
                ExpAddend = row.ExpAddend,
                HasExpAddend = row.HasExpAddend,
                EvoStage = row.EvoStage,
                HasEvoStage = row.HasEvoStage,
                Unknown18 = row.Unknown18,
                HasUnknown18 = row.HasUnknown18,
                EvYield = row.EvYield is { } evYield ? StatInfoRow.From(evYield) : null,
                HasEvYield = row.HasEvYield,
                BaseStats = row.BaseStats is { } baseStats ? StatInfoRow.From(baseStats) : null,
                HasBaseStats = row.HasBaseStats,
                HasEvolutions = row.HasEvolutions,
                HasTmMoves = row.HasTmMoves,
                HasEggMoves = row.HasEggMoves,
                HasReminderMoves = row.HasReminderMoves,
                HasLevelupMoves = row.HasLevelupMoves,
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
            var evolutionsOffset = HasEvolutions || Evolutions.Count > 0 ? CreateEvolutionsVector(builder, Evolutions) : default(VectorOffset);
            var tmMovesOffset = HasTmMoves || TmMoves.Count > 0 ? ZaPersonal.CreateUshortVector(builder, TmMoves) : default(VectorOffset);
            var eggMovesOffset = HasEggMoves || EggMoves.Count > 0 ? ZaPersonal.CreateUshortVector(builder, EggMoves) : default(VectorOffset);
            var reminderMovesOffset = HasReminderMoves || ReminderMoves.Count > 0 ? ZaPersonal.CreateUshortVector(builder, ReminderMoves) : default(VectorOffset);
            var levelupMovesOffset = HasLevelupMoves || LevelupMoves.Count > 0 ? CreateLevelupMovesVector(builder, LevelupMoves) : default(VectorOffset);

            ZaPersonal.Start(builder);
            if (HasLevelupMoves || LevelupMoves.Count > 0)
            {
                ZaPersonal.AddLevelupMoves(builder, levelupMovesOffset);
            }

            if (HasReminderMoves || ReminderMoves.Count > 0)
            {
                ZaPersonal.AddReminderMoves(builder, reminderMovesOffset);
            }

            if (HasEggMoves || EggMoves.Count > 0)
            {
                ZaPersonal.AddEggMoves(builder, eggMovesOffset);
            }

            if (HasTmMoves || TmMoves.Count > 0)
            {
                ZaPersonal.AddTmMoves(builder, tmMovesOffset);
            }

            if (HasEvolutions || Evolutions.Count > 0)
            {
                ZaPersonal.AddEvolutions(builder, evolutionsOffset);
            }

            if (HasBaseStats && BaseStats is not null)
            {
                ZaPersonal.AddBaseStats(builder, BaseStats.Write(builder));
            }

            if (HasEvYield && EvYield is not null)
            {
                ZaPersonal.AddEvYield(builder, EvYield.Write(builder));
            }

            if (HasUnknown18)
            {
                ZaPersonal.AddUnknown18(builder, Unknown18);
            }

            if (HasEvoStage || EvoStage != 0)
            {
                ZaPersonal.AddEvoStage(builder, EvoStage);
            }

            if (HasExpAddend)
            {
                ZaPersonal.AddExpAddend(builder, ExpAddend);
            }

            if (HasBaseFriendship || BaseFriendship != 0)
            {
                ZaPersonal.AddBaseFriendship(builder, BaseFriendship);
            }

            if (HasEggHatchCycles || EggHatchCycles != 0)
            {
                ZaPersonal.AddEggHatchCycles(builder, EggHatchCycles);
            }

            if (HasEggHatch && EggHatch is not null)
            {
                ZaPersonal.AddEggHatch(builder, EggHatch.Write(builder));
            }

            if (HasEggGroup2 || EggGroup2 != 0)
            {
                ZaPersonal.AddEggGroup2(builder, EggGroup2);
            }

            if (HasEggGroup1 || EggGroup1 != 0)
            {
                ZaPersonal.AddEggGroup1(builder, EggGroup1);
            }

            if (HasGender && Gender is not null)
            {
                ZaPersonal.AddGender(builder, Gender.Write(builder));
            }

            if (HasCatchRate || CatchRate != 0)
            {
                ZaPersonal.AddCatchRate(builder, CatchRate);
            }

            if (HasXpGrowth || XpGrowth != 0)
            {
                ZaPersonal.AddXpGrowth(builder, XpGrowth);
            }

            if (HasAbilityHidden || AbilityHidden != 0)
            {
                ZaPersonal.AddAbilityHidden(builder, AbilityHidden);
            }

            if (HasAbility2 || Ability2 != 0)
            {
                ZaPersonal.AddAbility2(builder, Ability2);
            }

            if (HasAbility1 || Ability1 != 0)
            {
                ZaPersonal.AddAbility1(builder, Ability1);
            }

            if (HasType2 || Type2 != 0)
            {
                ZaPersonal.AddType2(builder, Type2);
            }

            if (HasType1 || Type1 != 0)
            {
                ZaPersonal.AddType1(builder, Type1);
            }

            if (HasZADexOrder || ZADexOrder != 0)
            {
                ZaPersonal.AddZADexOrder(builder, ZADexOrder);
            }

            if (HasIsPresent || IsPresent)
            {
                ZaPersonal.AddIsPresent(builder, IsPresent);
            }

            if (HasSpecies && Species is not null)
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

    private sealed record PersonalArrayRows(
        IReadOnlyList<PersonalRow> Rows,
        bool RequiresLegacyDexOrderRepair,
        IReadOnlyList<PersonalRow>? BaseRows);

    private sealed record DexPlacementPayload(
        int Version,
        int? RegularCount,
        IReadOnlyDictionary<int, int> Assignments)
    {
        public static readonly DexPlacementPayload Invalid = new(
            Version: 0,
            RegularCount: null,
            new Dictionary<int, int>());
    }

    private sealed record DexPlacementApplyResult(
        IReadOnlySet<int> ChangedPersonalIds,
        IReadOnlyDictionary<int, ZaPokedexContentsGroup> GroupUpdates,
        IReadOnlyDictionary<int, ZaPokedexContentsGroup> TargetGroups,
        bool RequiresPersonalRebuild,
        int CurrentRegularCount,
        int TargetRegularCount)
    {
        public bool ChangesRegularCount => CurrentRegularCount != TargetRegularCount;

        public static readonly DexPlacementApplyResult None = new(
            new HashSet<int>(),
            new Dictionary<int, ZaPokedexContentsGroup>(),
            new Dictionary<int, ZaPokedexContentsGroup>(),
            false,
            CurrentRegularCount: 0,
            TargetRegularCount: 0);
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
        byte Reserved2,
        uint Reserved3)
    {
        public static readonly SpeciesInfoRow Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public static SpeciesInfoRow From(ZaSpeciesInfo row, uint reserved3) =>
            new(row.Species, row.Form, row.Model, row.Color, row.BodyType, row.Height, row.Weight, row.Reserved, row.Reserved1, row.Reserved2, reserved3);

        public Offset<ZaSpeciesInfo> Write(FlatBufferBuilder builder) =>
            ZaSpeciesInfo.Create(builder, Species, Form, Model, Color, BodyType, Height, Weight, Reserved, Reserved1, Reserved2, Reserved3);
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
