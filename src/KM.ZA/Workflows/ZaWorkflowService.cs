// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.AngeFight;
using KM.ZA.DumpImport;
using KM.ZA.Encounters;
using KM.ZA.Gifts;
using KM.ZA.Items;
using KM.ZA.ModMerger;
using KM.ZA.Moves;
using KM.ZA.Placement;
using KM.ZA.Pokemon;
using KM.ZA.Shops;
using KM.ZA.StaticEncounters;
using KM.ZA.Text;
using KM.ZA.TypeChart;
using KM.ZA.Trainers;
using KM.ZA.Trades;

namespace KM.ZA.Workflows;

public sealed class ZaWorkflowService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaCacheManager cacheManager;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaItemsWorkflowService itemsWorkflowService;
    private readonly ZaPokemonWorkflowService pokemonWorkflowService;
    private readonly ZaMovesWorkflowService movesWorkflowService;
    private readonly ZaTextWorkflowService textWorkflowService;
    private readonly ZaShopsWorkflowService shopsWorkflowService;
    private readonly ZaTrainersWorkflowService trainersWorkflowService;
    private readonly ZaPlacementWorkflowService placementWorkflowService;
    private readonly ZaEncountersWorkflowService encountersWorkflowService;
    private readonly ZaStaticEncountersWorkflowService staticEncountersWorkflowService;
    private readonly ZaGiftPokemonWorkflowService giftPokemonWorkflowService;
    private readonly ZaTradePokemonWorkflowService tradePokemonWorkflowService;
    private readonly ZaTypeChartWorkflowService typeChartWorkflowService;
    private readonly ZaAngeFightWorkflowService angeFightWorkflowService;
    private readonly ZaDumpImportWorkflowService dumpImportWorkflowService;
    private readonly ZaDumpImportExecutionService dumpImportExecutionService;
    private readonly ZaModMergerWorkflowService modMergerWorkflowService;
    private readonly ZaItemsEditSessionService itemsEditSessionService;
    private readonly ZaPokemonEditSessionService pokemonEditSessionService;
    private readonly ZaMovesEditSessionService movesEditSessionService;
    private readonly ZaTextEditSessionService textEditSessionService;
    private readonly ZaShopsEditSessionService shopsEditSessionService;
    private readonly ZaTrainersEditSessionService trainersEditSessionService;
    private readonly ZaPlacementEditSessionService placementEditSessionService;
    private readonly ZaEncountersEditSessionService encountersEditSessionService;
    private readonly ZaStaticEncountersEditSessionService staticEncountersEditSessionService;
    private readonly ZaGiftPokemonEditSessionService giftPokemonEditSessionService;
    private readonly ZaTradePokemonEditSessionService tradePokemonEditSessionService;
    private readonly ZaTypeChartEditSessionService typeChartEditSessionService;
    private readonly ZaAngeFightEditSessionService angeFightEditSessionService;

    public ZaWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaCacheManager? cacheManager = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.cacheManager = cacheManager ?? new ZaCacheManager();
        fileSource = new ZaWorkflowFileSource(this.cacheManager);
        itemsWorkflowService = new ZaItemsWorkflowService(fileSource);
        pokemonWorkflowService = new ZaPokemonWorkflowService(fileSource);
        movesWorkflowService = new ZaMovesWorkflowService(fileSource);
        textWorkflowService = new ZaTextWorkflowService(fileSource);
        shopsWorkflowService = new ZaShopsWorkflowService(fileSource, itemsWorkflowService);
        trainersWorkflowService = new ZaTrainersWorkflowService(fileSource);
        placementWorkflowService = new ZaPlacementWorkflowService(fileSource);
        encountersWorkflowService = new ZaEncountersWorkflowService(fileSource);
        staticEncountersWorkflowService = new ZaStaticEncountersWorkflowService(fileSource);
        giftPokemonWorkflowService = new ZaGiftPokemonWorkflowService(fileSource);
        tradePokemonWorkflowService = new ZaTradePokemonWorkflowService(fileSource);
        typeChartWorkflowService = new ZaTypeChartWorkflowService();
        angeFightWorkflowService = new ZaAngeFightWorkflowService(fileSource);
        dumpImportWorkflowService = new ZaDumpImportWorkflowService(itemsWorkflowService);
        modMergerWorkflowService = new ZaModMergerWorkflowService(this.projectWorkspaceService);
        itemsEditSessionService = new ZaItemsEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            itemsWorkflowService);
        pokemonEditSessionService = new ZaPokemonEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            pokemonWorkflowService);
        movesEditSessionService = new ZaMovesEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            movesWorkflowService);
        textEditSessionService = new ZaTextEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            textWorkflowService);
        shopsEditSessionService = new ZaShopsEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            shopsWorkflowService);
        trainersEditSessionService = new ZaTrainersEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            trainersWorkflowService);
        placementEditSessionService = new ZaPlacementEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            placementWorkflowService);
        encountersEditSessionService = new ZaEncountersEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            encountersWorkflowService);
        staticEncountersEditSessionService = new ZaStaticEncountersEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            staticEncountersWorkflowService);
        giftPokemonEditSessionService = new ZaGiftPokemonEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            giftPokemonWorkflowService);
        tradePokemonEditSessionService = new ZaTradePokemonEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            tradePokemonWorkflowService);
        typeChartEditSessionService = new ZaTypeChartEditSessionService(
            this.projectWorkspaceService,
            typeChartWorkflowService);
        angeFightEditSessionService = new ZaAngeFightEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            angeFightWorkflowService);
        dumpImportExecutionService = new ZaDumpImportExecutionService(
            this.projectWorkspaceService,
            itemsWorkflowService,
            itemsEditSessionService,
            dumpImportWorkflowService);
    }

    public ZaCacheStatus GetCacheStatus(ProjectPaths? paths = null)
    {
        return cacheManager.GetStatus(paths);
    }

    public ZaCacheStatus UpdateCacheSettings(
        ZaCacheMode mode,
        long maxCacheSizeBytes,
        ProjectPaths? activePaths = null)
    {
        cacheManager.UpdateSettings(mode, maxCacheSizeBytes, activePaths);
        return cacheManager.GetStatus(activePaths);
    }

    public ZaCacheStatus ClearCache(ProjectPaths? activePaths = null)
    {
        return cacheManager.Clear(activePaths);
    }

    public ZaCacheStatus WarmupCacheStep(ProjectPaths paths, int stepIndex)
    {
        return cacheManager.WarmupStep(paths, stepIndex);
    }

    public void ClearMemoryCaches(bool clearReusableDataCaches = true)
    {
        projectWorkspaceService.ClearMemoryCache();
        pokemonWorkflowService.ClearMemoryCache();
        if (clearReusableDataCaches)
        {
            cacheManager.ClearMemoryCache();
        }
    }

    public ZaWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.SelectedGame is not ProjectGame.ZA)
        {
            return new ZaWorkflowList([]);
        }

        var project = projectWorkspaceService.Open(paths);
        return new ZaWorkflowList(
        [
            pokemonWorkflowService.CreateSummary(project),
            trainersWorkflowService.CreateSummary(project),
            placementWorkflowService.CreateSummary(project),
            encountersWorkflowService.CreateSummary(project),
            staticEncountersWorkflowService.CreateSummary(project),
            giftPokemonWorkflowService.CreateSummary(project),
            tradePokemonWorkflowService.CreateSummary(project),
            movesWorkflowService.CreateSummary(project),
            textWorkflowService.CreateSummary(project),
            itemsWorkflowService.CreateSummary(project),
            shopsWorkflowService.CreateSummary(project),
            typeChartWorkflowService.CreateSummary(project),
            angeFightWorkflowService.CreateSummary(project),
            dumpImportWorkflowService.CreateSummary(project),
            modMergerWorkflowService.CreateSummary(project),
        ]);
    }

    public ZaItemsWorkflow LoadItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return itemsWorkflowService.Load(project);
    }

    public ZaPokemonWorkflow LoadPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return pokemonWorkflowService.Load(project);
    }

    public ZaMovesWorkflow LoadMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return movesWorkflowService.Load(project);
    }

    public ZaTextWorkflow LoadText(ProjectPaths paths, ZaTextWorkflowQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return textWorkflowService.Load(project, query);
    }

    public ZaDumpImportWorkflow LoadDumpImport(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return dumpImportWorkflowService.Load(project);
    }

    public ZaDumpImportExecutionResult PreviewDumpImport(
        ProjectPaths paths,
        string profileId,
        string sourcePath,
        EditSession? session)
    {
        return dumpImportExecutionService.Preview(paths, profileId, sourcePath, session);
    }

    public ZaShopsWorkflow LoadShops(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return shopsWorkflowService.Load(project);
    }

    public ZaTrainersWorkflow LoadTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return trainersWorkflowService.Load(project);
    }

    public ZaPlacementWorkflow LoadPlacement(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return placementWorkflowService.Load(project);
    }

    public ZaGiftPokemonWorkflow LoadGiftPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return giftPokemonWorkflowService.Load(project);
    }

    public ZaEncountersWorkflow LoadEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return encountersWorkflowService.Load(project);
    }

    public ZaStaticEncountersWorkflow LoadStaticEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return staticEncountersWorkflowService.Load(project);
    }

    public ZaTradePokemonWorkflow LoadTradePokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return tradePokemonWorkflowService.Load(project);
    }

    public ZaTypeChartWorkflow LoadTypeChart(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return typeChartWorkflowService.Load(project);
    }

    public ZaAngeFightWorkflow LoadAngeFight(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return angeFightWorkflowService.Load(project);
    }

    public ZaModMergerWorkflow LoadModMerger(
        ProjectPaths paths,
        IReadOnlyList<ZaModMergerSourceRequest> modSources)
    {
        return modMergerWorkflowService.Load(paths, modSources);
    }

    public ZaModMergerStageResult StageModMerge(
        ProjectPaths paths,
        IReadOnlyList<ZaModMergerSourceRequest> modSources)
    {
        return modMergerWorkflowService.Stage(paths, modSources);
    }

    public ZaModMergerApplyResult ApplyModMerge(
        ProjectPaths paths,
        IReadOnlyList<ZaModMergerSourceRequest> modSources)
    {
        return modMergerWorkflowService.Apply(paths, modSources);
    }

    public ZaPokemonEditResult UpdatePokemonField(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string field,
        string value)
    {
        return pokemonEditSessionService.UpdateField(paths, session, personalId, field, value);
    }

    public ZaItemsEditResult UpdateItemField(
        ProjectPaths paths,
        EditSession? session,
        int itemId,
        string field,
        string value)
    {
        return itemsEditSessionService.UpdateField(paths, session, itemId, field, value);
    }

    public ZaItemsEditResult UpdateItemFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaItemFieldUpdate> updates)
    {
        return itemsEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaPokemonEditResult UpdatePokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPokemonFieldUpdate> updates)
    {
        return pokemonEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaPokemonEditResult UpdatePokemonLearnset(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string action,
        int? slot,
        int? moveId,
        int? level)
    {
        return pokemonEditSessionService.UpdateLearnset(paths, session, personalId, action, slot, moveId, level);
    }

    public ZaPokemonEditResult UpdatePokemonEvolution(
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
        return pokemonEditSessionService.UpdateEvolution(paths, session, personalId, action, slot, method, argument, species, form, level);
    }

    public ZaPokemonEditResult SwapPokemonDexPlacement(
        ProjectPaths paths,
        EditSession? session,
        int sourceSpeciesId,
        int targetSpeciesId)
    {
        return pokemonEditSessionService.SwapDexPlacement(
            paths,
            session,
            sourceSpeciesId,
            targetSpeciesId);
    }

    public ZaMovesEditResult UpdateMoveField(
        ProjectPaths paths,
        EditSession? session,
        int moveId,
        string field,
        string value)
    {
        return movesEditSessionService.UpdateField(paths, session, moveId, field, value);
    }

    public ZaMovesEditResult UpdateMoveFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaMoveFieldUpdate> updates)
    {
        return movesEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaTextEditResult UpdateTextEntry(
        ProjectPaths paths,
        EditSession? session,
        string textKey,
        string value,
        ZaTextWorkflowQuery? query = null)
    {
        return textEditSessionService.UpdateEntry(paths, session, textKey, value, query);
    }

    public ZaShopsEditResult UpdateShopInventoryItem(
        ProjectPaths paths,
        EditSession? session,
        string shopId,
        int slot,
        string field,
        string value)
    {
        return shopsEditSessionService.UpdateInventoryItem(paths, session, shopId, slot, field, value);
    }

    public ZaTrainersEditResult UpdateTrainerField(
        ProjectPaths paths,
        EditSession? session,
        int trainerId,
        int? slot,
        string field,
        string value)
    {
        return trainersEditSessionService.UpdateField(paths, session, trainerId, slot, field, value);
    }

    public ZaTrainersEditResult UpdateTrainerFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaTrainerFieldUpdate> updates)
    {
        return trainersEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaPlacementEditResult UpdatePlacementObjectField(
        ProjectPaths paths,
        EditSession? session,
        string objectId,
        string field,
        string value)
    {
        return placementEditSessionService.UpdateObjectField(paths, session, objectId, field, value);
    }

    public ZaPlacementEditResult UpdatePlacementObjectFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPlacementObjectFieldUpdate> updates)
    {
        return placementEditSessionService.UpdateObjectFields(paths, session, updates);
    }

    public ZaGiftPokemonEditResult UpdateGiftPokemonField(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex,
        string field,
        string value)
    {
        return giftPokemonEditSessionService.UpdateField(paths, session, giftIndex, field, value);
    }

    public ZaEncountersEditResult UpdateEncounterSlotField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        return encountersEditSessionService.UpdateSlotField(paths, session, tableId, slot, field, value);
    }

    public ZaEncountersEditResult UpdateEncounterSlotFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaEncounterSlotFieldUpdate> updates)
    {
        return encountersEditSessionService.UpdateSlotFields(paths, session, updates);
    }

    public ZaStaticEncountersEditResult UpdateStaticEncounterField(
        ProjectPaths paths,
        EditSession? session,
        int encounterIndex,
        string field,
        string value)
    {
        return staticEncountersEditSessionService.UpdateField(paths, session, encounterIndex, field, value);
    }

    public ZaStaticEncountersEditResult UpdateStaticEncounterFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaStaticEncounterFieldUpdate> updates)
    {
        return staticEncountersEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaGiftPokemonEditResult UpdateGiftPokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaGiftPokemonFieldUpdate> updates)
    {
        return giftPokemonEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaTradePokemonEditResult UpdateTradePokemonField(
        ProjectPaths paths,
        EditSession? session,
        int tradeIndex,
        string field,
        string value)
    {
        return tradePokemonEditSessionService.UpdateField(paths, session, tradeIndex, field, value);
    }

    public ZaTradePokemonEditResult UpdateTradePokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaTradePokemonFieldUpdate> updates)
    {
        return tradePokemonEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaTypeChartEditResult StageTypeChart(
        ProjectPaths paths,
        IReadOnlyList<int> values,
        EditSession? session = null)
    {
        return typeChartEditSessionService.StageChart(paths, values, session);
    }

    public ZaTypeChartEditResult StageTypeChartUninstall(
        ProjectPaths paths,
        EditSession? session = null)
    {
        return typeChartEditSessionService.StageUninstall(paths, session);
    }

    public ZaAngeFightEditResult StageAngeFight(
        ProjectPaths paths,
        ZaAngeFightSettings settings,
        EditSession? session = null)
    {
        return angeFightEditSessionService.StageSettings(paths, settings, session);
    }

    public ZaAngeFightEditResult StageAngeFightUninstall(
        ProjectPaths paths,
        EditSession? session = null)
    {
        return angeFightEditSessionService.StageUninstall(paths, session);
    }

    public ZaEditSessionValidation ValidateEditSession(ProjectPaths paths, EditSession session)
    {
        var domain = GetDomain(session);
        return domain == ZaEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? ValidateNormalDomains(paths, session, domains)
            : ValidateSingleDomain(paths, session, domain);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode outputMode)
    {
        var domain = GetDomain(session);
        return domain == ZaEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? CreateNormalDomainChangePlan(paths, session, domains, outputMode)
            : CreateSingleDomainChangePlan(paths, session, domain, outputMode);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan, ZaOutputMode outputMode)
    {
        var domain = GetDomain(session);
        return domain == ZaEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? ApplyNormalDomainChangePlan(paths, session, reviewedPlan, domains, outputMode)
            : ApplySingleDomainChangePlan(paths, session, reviewedPlan, domain, outputMode);
    }

    private ZaEditSessionValidation ValidateSingleDomain(
        ProjectPaths paths,
        EditSession session,
        ZaEditSessionDomain domain)
    {
        return domain switch
        {
            ZaEditSessionDomain.Items => itemsEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Moves => movesEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Text => textEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Pokemon => pokemonEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Shops => shopsEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Trainers => trainersEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Placement => placementEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.StaticEncounters => staticEncountersEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.TradePokemon => tradePokemonEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.TypeChart => typeChartEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.AngeFight => angeFightEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
            _ => pokemonEditSessionService.Validate(paths, session),
        };
    }

    private ChangePlan CreateSingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaEditSessionDomain domain,
        ZaOutputMode outputMode)
    {
        return domain switch
        {
            ZaEditSessionDomain.Items => itemsEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Moves => movesEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Text => textEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Pokemon => pokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Shops => shopsEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Trainers => trainersEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Placement => placementEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.StaticEncounters => staticEncountersEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.TradePokemon => tradePokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.TypeChart => typeChartEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.AngeFight => angeFightEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
            _ => pokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
        };
    }

    private ApplyResult ApplySingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaEditSessionDomain domain,
        ZaOutputMode outputMode)
    {
        return domain switch
        {
            ZaEditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Moves => movesEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Text => textEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Shops => shopsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Placement => placementEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.StaticEncounters => staticEncountersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.TradePokemon => tradePokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.TypeChart => typeChartEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.AngeFight => angeFightEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
            _ => pokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
        };
    }

    private ZaEditSessionValidation ValidateNormalDomains(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<ZaEditSessionDomain> domains)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var domain in domains)
        {
            var validation = ValidateSingleDomain(paths, SliceSession(session, domain), domain);
            diagnostics.AddRange(validation.Diagnostics);
        }

        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    private ChangePlan CreateNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<ZaEditSessionDomain> domains,
        ZaOutputMode outputMode)
    {
        var validation = ValidateNormalDomains(paths, session, domains);
        var diagnostics = validation.Diagnostics.ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        foreach (var domain in domains)
        {
            var domainPlan = CreateSingleDomainChangePlan(paths, SliceSession(session, domain), domain, outputMode);
            diagnostics.AddRange(domainPlan.Diagnostics);
            writes.AddRange(domainPlan.Writes);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        return new ChangePlan(session.Id, CombinePlannedWrites(writes), diagnostics);
    }

    private ApplyResult ApplyNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        IReadOnlyList<ZaEditSessionDomain> domains,
        ZaOutputMode outputMode)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateNormalDomainChangePlan(paths, session, domains, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                "za.editor",
                expected: "Current reviewed Pokemon Legends Z-A change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        foreach (var domain in domains)
        {
            var domainSession = SliceSession(session, domain);
            var domainPlan = CreateSingleDomainChangePlan(paths, domainSession, domain, outputMode);
            var result = ApplySingleDomainChangePlan(paths, domainSession, domainPlan, domain, outputMode);
            diagnostics.AddRange(result.Diagnostics);
            writtenFiles.AddRange(result.WrittenFiles);

            if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                break;
            }
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
    }

    private static ZaEditSessionDomain GetDomain(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return domains switch
        {
            [] => ZaEditSessionDomain.None,
            [ZaEditSessionSupport.PokemonDomain] => ZaEditSessionDomain.Pokemon,
            [ZaEditSessionSupport.ItemsDomain] => ZaEditSessionDomain.Items,
            [ZaEditSessionSupport.MovesDomain] => ZaEditSessionDomain.Moves,
            [ZaEditSessionSupport.TextDomain] => ZaEditSessionDomain.Text,
            [ZaEditSessionSupport.ShopsDomain] => ZaEditSessionDomain.Shops,
            [ZaEditSessionSupport.TrainersDomain] => ZaEditSessionDomain.Trainers,
            [ZaEditSessionSupport.PlacementDomain] => ZaEditSessionDomain.Placement,
            [ZaEditSessionSupport.EncountersDomain] => ZaEditSessionDomain.Encounters,
            [ZaEditSessionSupport.StaticEncountersDomain] => ZaEditSessionDomain.StaticEncounters,
            [ZaEditSessionSupport.GiftPokemonDomain] => ZaEditSessionDomain.GiftPokemon,
            [ZaEditSessionSupport.TradePokemonDomain] => ZaEditSessionDomain.TradePokemon,
            [ZaEditSessionSupport.TypeChartDomain] => ZaEditSessionDomain.TypeChart,
            [ZaEditSessionSupport.AngeFightDomain] => ZaEditSessionDomain.AngeFight,
            _ => ZaEditSessionDomain.Mixed,
        };
    }

    private static bool TryGetNormalDomains(
        EditSession session,
        out IReadOnlyList<ZaEditSessionDomain> domains)
    {
        var orderedDomains = session.PendingEdits
            .Select(edit => GetDomain(edit.Domain))
            .Where(domain => domain != ZaEditSessionDomain.None)
            .Distinct()
            .ToArray();

        var itemsIndex = Array.IndexOf(orderedDomains, ZaEditSessionDomain.Items);
        var pokemonIndex = Array.IndexOf(orderedDomains, ZaEditSessionDomain.Pokemon);
        if (itemsIndex > pokemonIndex && pokemonIndex >= 0)
        {
            (orderedDomains[pokemonIndex], orderedDomains[itemsIndex]) =
                (orderedDomains[itemsIndex], orderedDomains[pokemonIndex]);
        }

        domains = orderedDomains;
        return orderedDomains.Length > 1 && orderedDomains.All(IsNormalDomain);
    }

    private static ZaEditSessionDomain GetDomain(string? domain)
    {
        return domain switch
        {
            ZaEditSessionSupport.PokemonDomain => ZaEditSessionDomain.Pokemon,
            ZaEditSessionSupport.ItemsDomain => ZaEditSessionDomain.Items,
            ZaEditSessionSupport.MovesDomain => ZaEditSessionDomain.Moves,
            ZaEditSessionSupport.TextDomain => ZaEditSessionDomain.Text,
            ZaEditSessionSupport.ShopsDomain => ZaEditSessionDomain.Shops,
            ZaEditSessionSupport.TrainersDomain => ZaEditSessionDomain.Trainers,
            ZaEditSessionSupport.PlacementDomain => ZaEditSessionDomain.Placement,
            ZaEditSessionSupport.EncountersDomain => ZaEditSessionDomain.Encounters,
            ZaEditSessionSupport.StaticEncountersDomain => ZaEditSessionDomain.StaticEncounters,
            ZaEditSessionSupport.GiftPokemonDomain => ZaEditSessionDomain.GiftPokemon,
            ZaEditSessionSupport.TradePokemonDomain => ZaEditSessionDomain.TradePokemon,
            ZaEditSessionSupport.TypeChartDomain => ZaEditSessionDomain.TypeChart,
            ZaEditSessionSupport.AngeFightDomain => ZaEditSessionDomain.AngeFight,
            null or "" => ZaEditSessionDomain.None,
            _ => ZaEditSessionDomain.Mixed,
        };
    }

    private static bool IsNormalDomain(ZaEditSessionDomain domain)
    {
        return domain is ZaEditSessionDomain.Items
            or ZaEditSessionDomain.Pokemon
            or ZaEditSessionDomain.Moves
            or ZaEditSessionDomain.Text
            or ZaEditSessionDomain.Shops
            or ZaEditSessionDomain.Trainers
            or ZaEditSessionDomain.Placement
            or ZaEditSessionDomain.Encounters
            or ZaEditSessionDomain.StaticEncounters
            or ZaEditSessionDomain.GiftPokemon
            or ZaEditSessionDomain.TradePokemon;
    }

    private static EditSession SliceSession(EditSession session, ZaEditSessionDomain domain)
    {
        var domainName = GetDomainName(domain);
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => string.Equals(edit.Domain, domainName, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static string GetDomainName(ZaEditSessionDomain domain)
    {
        return domain switch
        {
            ZaEditSessionDomain.Items => ZaEditSessionSupport.ItemsDomain,
            ZaEditSessionDomain.Pokemon => ZaEditSessionSupport.PokemonDomain,
            ZaEditSessionDomain.Moves => ZaEditSessionSupport.MovesDomain,
            ZaEditSessionDomain.Text => ZaEditSessionSupport.TextDomain,
            ZaEditSessionDomain.Shops => ZaEditSessionSupport.ShopsDomain,
            ZaEditSessionDomain.Trainers => ZaEditSessionSupport.TrainersDomain,
            ZaEditSessionDomain.Placement => ZaEditSessionSupport.PlacementDomain,
            ZaEditSessionDomain.Encounters => ZaEditSessionSupport.EncountersDomain,
            ZaEditSessionDomain.StaticEncounters => ZaEditSessionSupport.StaticEncountersDomain,
            ZaEditSessionDomain.GiftPokemon => ZaEditSessionSupport.GiftPokemonDomain,
            ZaEditSessionDomain.TradePokemon => ZaEditSessionSupport.TradePokemonDomain,
            ZaEditSessionDomain.TypeChart => ZaEditSessionSupport.TypeChartDomain,
            ZaEditSessionDomain.AngeFight => ZaEditSessionSupport.AngeFightDomain,
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<PlannedFileWrite> CombinePlannedWrites(IEnumerable<PlannedFileWrite> writes)
    {
        return writes
            .GroupBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .Select(group =>
            {
                var groupedWrites = group.ToArray();
                if (groupedWrites.Length == 1)
                {
                    return groupedWrites[0];
                }

                return new PlannedFileWrite(
                    group.Key,
                    groupedWrites
                        .SelectMany(write => write.Sources)
                        .Distinct()
                        .ToArray(),
                    groupedWrites.Any(write => write.ReplacesExistingOutput),
                    string.Join(
                        " ",
                        groupedWrites
                            .Select(write => write.Reason)
                            .Where(reason => !string.IsNullOrWhiteSpace(reason))
                            .Distinct(StringComparer.Ordinal)),
                    CombineSourceFingerprints(groupedWrites));
            })
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? CombineSourceFingerprints(
        IReadOnlyList<PlannedFileWrite> writes)
    {
        if (writes.All(write => string.IsNullOrWhiteSpace(write.SourceFingerprint)))
        {
            return null;
        }

        var components = writes
            .Select(write => write.SourceFingerprint ?? "<none>")
            .Order(StringComparer.Ordinal);
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', components));
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static ZaEditSessionValidation CreateUnsupportedMixedValidation(EditSession session)
    {
        return new ZaEditSessionValidation(session, IsValid: false, [CreateMixedDiagnostic()]);
    }

    private static ChangePlan CreateUnsupportedMixedChangePlan(EditSession session)
    {
        return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), [CreateMixedDiagnostic()]);
    }

    private static ApplyResult CreateUnsupportedMixedApplyResult(EditSession session)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var plan = CreateUnsupportedMixedChangePlan(session);
        return new ApplyResult(
            applyId,
            appliedAt,
            Array.Empty<ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, plan.Writes),
            plan.Diagnostics);
    }

    private static ValidationDiagnostic CreateMixedDiagnostic()
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pokemon Legends Z-A edit sessions cannot mix unsupported workflow domains in one change plan yet.",
            "za.editor",
            expected: "Pending edits from supported Z-A editor domains");
    }

    private enum ZaEditSessionDomain
    {
        None,
        Items,
        Pokemon,
        Moves,
        Text,
        Shops,
        Trainers,
        Placement,
        Encounters,
        StaticEncounters,
        GiftPokemon,
        TradePokemon,
        TypeChart,
        AngeFight,
        Mixed,
    }
}
