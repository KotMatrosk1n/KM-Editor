// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.EvolutionItems;
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
    private const string DexPlacementPayloadPrefix = "v1|";
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
        if (!DexAssignmentsEqual(assignments, baseAssignments))
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
                [
                    ToSourceReference(loadedEditor.PersonalProvenance!),
                    ToSourceReference(loadedEditor.ContentsProvenance!),
                ],
                DexPlacementRecordId,
                ZaPokemonWorkflowService.DexPlacementField,
                EncodeDexAssignments(assignments)));
        }

        var updatedSession = currentSession with { PendingEdits = pendingEdits };
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

        OpenedProject project;
        IReadOnlyList<PersonalRow> rows;
        try
        {
            project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, ZaDataPaths.PersonalArray);
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

            var writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.EvolutionItemConversionArray,
                [conversionState.SourceReference()],
                outputMode);
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
            var personalArray = ReadRows(project, source);
            var rows = personalArray.Rows;
            var dexEdit = session.PendingEdits.SingleOrDefault(IsDexPlacementEdit);
            ZaWorkflowFile? contentsSource = null;
            ZaPokedexContentsTable? contentsTable = null;
            if (dexEdit is not null)
            {
                contentsSource = fileSource.Read(project, ZaDataPaths.PokedexContentsData);
                contentsTable = ZaPokedexContentsTable.Read(contentsSource.Bytes);
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
            foreach (var edit in session.PendingEdits.Where(edit => !IsDexPlacementEdit(edit)))
            {
                ApplyEdit(rows, edit, conversionState, diagnostics);
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

            ZaWorkflowFileSource.WriteBatch(paths, outputWrites, outputMode);
            if (conversionBytes is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.EvolutionItemConversionArray,
                    outputMode));
            }

            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.PersonalArray, outputMode));
            if (contentsBytes is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.PokedexContentsData,
                    outputMode));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            pokemonWorkflowService.ClearMemoryCache();
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
                expected: "Readable Pokemon and Pokédex sources with a writable output root"));
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

        if (IsDexPlacementEdit(edit))
        {
            ValidateDexPlacementEdit(workflow, edit, diagnostics);
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
        if (IsDexPlacementEdit(edit))
        {
            return OverlayDexPlacement(workflow, edit);
        }

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

    private static void ValidateDexPlacementEdit(
        ZaPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editor = workflow.DexEditor;
        if (editor is null
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
            return;
        }

        if (!string.Equals(edit.RecordId, DexPlacementRecordId, StringComparison.Ordinal)
            || !TryDecodeDexAssignments(edit.NewValue, out var assignments)
            || !string.Equals(edit.NewValue, EncodeDexAssignments(assignments), StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement data is malformed or non-canonical.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Canonical complete Pokédex placement map"));
            return;
        }

        var expectedSources = new[]
        {
            ToSourceReference(editor.PersonalProvenance),
            ToSourceReference(editor.ContentsProvenance),
        }
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var actualSources = edit.Sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (!actualSources.SequenceEqual(expectedSources))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement sources do not match the loaded personal and contents data.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Current effective Pokédex source pair"));
            return;
        }

        var expectedSpecies = editor.Placements
            .Select(placement => placement.SpeciesId)
            .Order()
            .ToArray();
        var actualSpecies = assignments.Keys.Order().ToArray();
        var expectedIndices = editor.Placements
            .Select(placement => placement.InternalIndex)
            .Order()
            .ToArray();
        var actualIndices = assignments.Values.Order().ToArray();
        if (!actualSpecies.SequenceEqual(expectedSpecies)
            || !actualIndices.SequenceEqual(expectedIndices))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement must preserve every active species and every unique slot.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Complete one-to-one active Pokédex slot assignment"));
            return;
        }

        var currentAssignments = editor.Placements.ToDictionary(
            placement => placement.SpeciesId,
            placement => placement.InternalIndex);
        if (DexAssignmentsEqual(assignments, currentAssignments))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement does not change any species slot.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "At least one staged slot swap"));
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
            var writes = new List<PlannedFileWrite>();
            var personalSources = session.PendingEdits
                .SelectMany(edit => edit.Sources)
                .Distinct()
                .ToArray();
            var personalWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.PersonalArray,
                personalSources,
                outputMode);
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
                    outputMode);
                writes.Add(new PlannedFileWrite(
                    contentsWriteInfo.TargetRelativePath,
                    contentsWriteInfo.Sources,
                    contentsWriteInfo.ReplacesExistingOutput,
                    "Update Regular and Hyperspace Pokédex membership for the staged slot swap."));
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
            || !TryDecodeDexAssignments(edit.NewValue, out var assignments))
        {
            throw new InvalidDataException(
                "The staged Pokédex placement cannot be compared with the verified source mapping.");
        }

        var placementBySpecies = editor.Placements.ToDictionary(
            placement => placement.SpeciesId);
        var slotByIndex = editor.Placements.ToDictionary(
            placement => placement.InternalIndex);
        if (assignments.Count != placementBySpecies.Count
            || assignments.Keys.Any(speciesId => !placementBySpecies.ContainsKey(speciesId))
            || assignments.Values.Any(internalIndex => !slotByIndex.ContainsKey(internalIndex)))
        {
            throw new InvalidDataException(
                "The staged Pokédex placement does not preserve the verified active slot mapping.");
        }

        return assignments.Any(pair =>
            !string.Equals(
                placementBySpecies[pair.Key].DexKind,
                slotByIndex[pair.Value].DexKind,
                StringComparison.Ordinal));
    }

    private static DexPlacementApplyResult ApplyDexPlacement(
        IReadOnlyList<PersonalRow> rows,
        ZaPokedexContentsTable contents,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryDecodeDexAssignments(edit.NewValue, out var assignments)
            || !string.Equals(edit.NewValue, EncodeDexAssignments(assignments), StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokédex placement data is malformed or non-canonical.",
                ZaEditSessionSupport.PokemonDomain,
                field: ZaPokemonWorkflowService.DexPlacementField,
                expected: "Canonical complete Pokédex placement map"));
            return DexPlacementApplyResult.None;
        }

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
            || !assignedSpecies.SequenceEqual(expectedSpecies)
            || !expectedIndices.SequenceEqual(Enumerable.Range(1, expectedSpecies.Length))
            || !assignedIndices.SequenceEqual(expectedIndices))
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

        var slotGroupByIndex = currentIndexBySpecies.ToDictionary(
            pair => pair.Value,
            pair => groupBySpecies[pair.Key]);
        var groupUpdates = new Dictionary<int, ZaPokedexContentsGroup>();
        foreach (var assignment in assignments)
        {
            var targetGroup = slotGroupByIndex[assignment.Value];
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
            requiresPersonalRebuild);
    }

    private static ZaPokemonWorkflow OverlayDexPlacement(
        ZaPokemonWorkflow workflow,
        PendingEdit edit)
    {
        var editor = workflow.DexEditor;
        if (editor is null
            || !editor.CanEdit
            || !TryDecodeDexAssignments(edit.NewValue, out var assignments))
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
        var slotByIndex = editor.Placements.ToDictionary(
            placement => placement.InternalIndex);
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
                if (!slotByIndex.TryGetValue(pair.Value, out var slot)
                    || !representativeBySpecies.TryGetValue(pair.Key, out var representative))
                {
                    return null;
                }

                return new ZaPokemonDexPlacement(
                    pair.Key,
                    pair.Value,
                    slot.DexKind,
                    slot.DisplayedNumber,
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

        return workflow with
        {
            Pokemon = updatedPokemon,
            DexEditor = editor with { Placements = placements },
        };
    }

    private static bool IsDexPlacementEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, DexPlacementRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, ZaPokemonWorkflowService.DexPlacementField, StringComparison.Ordinal);
    }

    private static bool IsDexPresenceEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.PokemonDomain, StringComparison.Ordinal)
            && string.Equals(edit.Field, ZaPokemonWorkflowService.IsPresentInGameField, StringComparison.Ordinal);
    }

    private static string EncodeDexAssignments(IReadOnlyDictionary<int, int> assignments)
    {
        return DexPlacementPayloadPrefix + string.Join(
            ",",
            assignments
                .OrderBy(pair => pair.Key)
                .Select(pair => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{pair.Key}:{pair.Value}")));
    }

    private static bool TryDecodeDexAssignments(
        string? payload,
        out Dictionary<int, int> assignments)
    {
        assignments = [];
        if (payload is null
            || !payload.StartsWith(DexPlacementPayloadPrefix, StringComparison.Ordinal)
            || payload.Length == DexPlacementPayloadPrefix.Length)
        {
            return false;
        }

        foreach (var assignment in payload[DexPlacementPayloadPrefix.Length..].Split(','))
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

    private static bool DexAssignmentsEqual(
        IReadOnlyDictionary<int, int> left,
        IReadOnlyDictionary<int, int> right)
    {
        return left.Count == right.Count
            && left.All(pair =>
                right.TryGetValue(pair.Key, out var rightIndex)
                && rightIndex == pair.Value);
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
        PendingEdit edit,
        ZaEvolutionItemConversionState conversionState,
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

    private PersonalArrayRows ReadRows(OpenedProject project, ZaWorkflowFile source)
    {
        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(source.Bytes));
        var requiresLegacyDexOrderRepair = table.HasLegacyByteZADexOrderLayout;
        ZaPersonalTable? baseTable = null;
        if (requiresLegacyDexOrderRepair)
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

        return new PersonalArrayRows(rows, requiresLegacyDexOrderRepair);
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
                    MoveStructVectorElement(data, vectorOffset, length, operation.Slot, operation.Slot - 1, LevelupMoveDataSize, edit.Field, diagnostics);
                    break;
                case MoveDownAction:
                    MoveStructVectorElement(data, vectorOffset, length, operation.Slot, operation.Slot + 1, LevelupMoveDataSize, edit.Field, diagnostics);
                    break;
                case MoveToAction:
                    MoveStructVectorElement(data, vectorOffset, length, operation.Slot, operation.MoveId ?? -1, LevelupMoveDataSize, edit.Field, diagnostics);
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
        public ushort Unknown16 { get; set; }
        public bool HasUnknown16 { get; set; }
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
                Unknown16 = row.Unknown16,
                HasUnknown16 = row.HasUnknown16,
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

            if (HasUnknown16)
            {
                ZaPersonal.AddUnknown16(builder, Unknown16);
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
        bool RequiresLegacyDexOrderRepair);

    private sealed record DexPlacementApplyResult(
        IReadOnlySet<int> ChangedPersonalIds,
        IReadOnlyDictionary<int, ZaPokedexContentsGroup> GroupUpdates,
        bool RequiresPersonalRebuild)
    {
        public static readonly DexPlacementApplyResult None = new(
            new HashSet<int>(),
            new Dictionary<int, ZaPokedexContentsGroup>(),
            false);
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
