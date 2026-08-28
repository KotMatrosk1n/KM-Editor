// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using KM.Core.Concurrency;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.ZA;
using KM.ZA.Data;
using KM.ZA.AngeFight;
using KM.ZA.DumpImport;
using KM.ZA.Encounters;
using KM.ZA.ExeFs;
using KM.ZA.FashionCatalog;
using KM.ZA.GameModules;
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
using KM.ZA.TrainerPools;
using KM.ZA.Trades;

namespace KM.ZA.Workflows;

public sealed class ZaWorkflowService
{
    private const int MaximumSemanticSourceFiles = 128;
    private const int MaximumSemanticSourceBytesPerFile = 64 * 1024 * 1024;
    private const long MaximumSemanticSourceBytes = 512L * 1024L * 1024L;
    private const int MaximumSemanticFingerprintParallelism = 8;
    private const long EstimatedSemanticFingerprintWorkerBytes = 256L * 1024L * 1024L;
    private const int MaximumGameModuleCapabilityParallelism = 4;
    private const long EstimatedGameModuleCapabilityWorkerBytes = 256L * 1024L * 1024L;
    private static readonly BoundedConcurrencyPolicy SemanticFingerprintPolicy = new(
        "za-semantic-source-fingerprint",
        BoundedWorkloadKind.Hash,
        EstimatedSemanticFingerprintWorkerBytes,
        MaximumSemanticFingerprintParallelism,
        memoryBudgetDivisor: 8,
        degreeOfParallelismWhenMemoryUnknown: 4);
    private static readonly BoundedConcurrencyPolicy GameModuleCapabilityPolicy = new(
        "za-game-module-capability-load",
        BoundedWorkloadKind.Decode,
        EstimatedGameModuleCapabilityWorkerBytes,
        MaximumGameModuleCapabilityParallelism,
        memoryBudgetDivisor: 8,
        degreeOfParallelismWhenMemoryUnknown: 4);

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaCacheManager cacheManager;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaItemsWorkflowService itemsWorkflowService;
    private readonly ZaPokemonWorkflowService pokemonWorkflowService;
    private readonly ZaMovesWorkflowService movesWorkflowService;
    private readonly ZaTextWorkflowService textWorkflowService;
    private readonly ZaShopsWorkflowService shopsWorkflowService;
    private readonly ZaTrainersWorkflowService trainersWorkflowService;
    private readonly ZaTrainerPoolsWorkflowService trainerPoolsWorkflowService;
    private readonly ZaFashionCatalogWorkflowService fashionCatalogWorkflowService;
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
    private readonly ZaTrainerPoolsEditSessionService trainerPoolsEditSessionService;
    private readonly ZaFashionCatalogEditSessionService fashionCatalogEditSessionService;
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
        trainerPoolsWorkflowService = new ZaTrainerPoolsWorkflowService(fileSource);
        fashionCatalogWorkflowService = new ZaFashionCatalogWorkflowService(fileSource);
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
        trainerPoolsEditSessionService = new ZaTrainerPoolsEditSessionService(
            this.projectWorkspaceService,
            trainerPoolsWorkflowService);
        fashionCatalogEditSessionService = new ZaFashionCatalogEditSessionService(
            this.projectWorkspaceService,
            fashionCatalogWorkflowService);
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

    public string CaptureSemanticExploreSourceFingerprint(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        using var outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
        var language = ZaGameTextLanguage.Resolve(paths);
        var virtualPaths = new List<string>
        {
            ZaDataPaths.ItemDataArray,
            ZaDataPaths.ShopItemLineupArray,
            ZaDataPaths.EvolutionItemConversionArray,
            ZaDataPaths.PersonalArray,
            ZaDataPaths.PokedexContentsData,
            ZaDataPaths.PokedexMegaContentsData,
            ZaDataPaths.AlphaMoveTable,
            ZaDataPaths.MoveDataArray,
            ZaDataPaths.BattleMoveParameterArray,
            ZaDataPaths.MoveTimingParameterArray,
            ZaDataPaths.BossMoveSelectorArray,
            ZaDataPaths.AiAttackParamArray,
            ZaDataPaths.AiBulletParamArray,
            ZaDataPaths.TrainerDataArray,
            ZaDataPaths.TrainerPoolTableDataArray,
            ZaDataPaths.TrainerPoolIdentityDataArray,
            ZaDataPaths.BattleTrainerSpawnerDataArray,
            ZaDataPaths.EncountDataArray,
            ZaDataPaths.PokemonSpawnerDataArray,
            ZaDataPaths.BossBattleDataGlobal,
            ZaDataPaths.PokemonDataArray,
            ZaWorkflowFileSource.DescriptorVirtualPath,
        };
        foreach (var textPath in new[]
                 {
                     ZaDataPaths.ItemNames(language),
                     ZaDataPaths.MoveNames(language),
                     ZaDataPaths.MoveDescriptions(language),
                     ZaDataPaths.PokemonNames(language),
                     ZaDataPaths.AbilityNames(language),
                     ZaDataPaths.MainMissionTitles(language),
                     ZaDataPaths.HyperspaceMissionTitles(language),
                     ZaDataPaths.SideMissionTitles(language),
                     ZaDataPaths.PlaceNames(language),
                     ZaDataPaths.PlaceNameKeys(language),
                     ZaDataPaths.TrainerNames(language),
                     ZaDataPaths.TrainerNameKeys(language),
                     ZaDataPaths.TrainerTypes(language),
                     ZaDataPaths.TrainerTypeKeys(language),
                     ZaDataPaths.ItemNames(ZaGameTextLanguage.English),
                     ZaDataPaths.MoveNames(ZaGameTextLanguage.English),
                     ZaDataPaths.MoveDescriptions(ZaGameTextLanguage.English),
                     ZaDataPaths.PokemonNames(ZaGameTextLanguage.English),
                     ZaDataPaths.AbilityNames(ZaGameTextLanguage.English),
                     ZaDataPaths.MainMissionTitles(ZaGameTextLanguage.English),
                     ZaDataPaths.HyperspaceMissionTitles(ZaGameTextLanguage.English),
                     ZaDataPaths.SideMissionTitles(ZaGameTextLanguage.English),
                     ZaDataPaths.PlaceNames(ZaGameTextLanguage.English),
                     ZaDataPaths.PlaceNameKeys(ZaGameTextLanguage.English),
                     ZaDataPaths.TrainerNames(ZaGameTextLanguage.English),
                     ZaDataPaths.TrainerNameKeys(ZaGameTextLanguage.English),
                     ZaDataPaths.TrainerTypes(ZaGameTextLanguage.English),
                     ZaDataPaths.TrainerTypeKeys(ZaGameTextLanguage.English),
                 })
        {
            virtualPaths.Add(textPath);
            var legacyPath = ZaDataPaths.TryCreateLegacyMessagePath(textPath);
            if (legacyPath is not null)
            {
                virtualPaths.Add(legacyPath);
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendSemanticSourceHash(hash, "za-semantic-source-v4");
        AppendSemanticSourceHash(hash, SemanticProjectBuildIdentity.Capture(paths));
        AppendSemanticExecutableSourceHash(
            hash,
            "dex-layout-base-main",
            ZaExeFsMainFileResolver.ResolveBasePath(paths));
        AppendSemanticExecutableSourceHash(
            hash,
            "dex-layout-layered-main",
            ZaExeFsMainFileResolver.ResolveOutputPath(paths));
        if (ZaCompressionRuntime.TryResolveRequiredFilePath(
                paths.PokemonLegendsZASupportFolderPath,
                out var supportRuntimePath))
        {
            AppendSemanticSourceHash(hash, "support-runtime-present");
            AppendSemanticSourceHash(
                hash,
                SemanticProjectBuildIdentity.CaptureBoundedFile(
                    supportRuntimePath,
                    "za-compression-runtime",
                    MaximumSemanticSourceBytesPerFile));
        }
        else
        {
            AppendSemanticSourceHash(hash, "support-runtime-missing");
        }
        var boundedVirtualPaths = virtualPaths
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (boundedVirtualPaths.Length > MaximumSemanticSourceFiles)
        {
            throw new InvalidDataException("The semantic source file count exceeds its bounded limit.");
        }

        var payloadCaptures = CaptureSemanticSourcePayloadPairs(paths, boundedVirtualPaths);
        long sourceBytes = 0;
        for (var index = 0; index < boundedVirtualPaths.Length; index++)
        {
            var virtualPath = boundedVirtualPaths[index];
            AppendSemanticSourceHash(hash, virtualPath);
            var capture = payloadCaptures[index]
                ?? throw new InvalidOperationException("A semantic source payload was not observed.");
            if (capture.Failure is not null)
            {
                capture.Failure.Throw();
            }

            AppendSemanticSourcePayloadPair(
                hash,
                capture.Observation
                    ?? throw new InvalidOperationException("A semantic source payload was not observed."),
                ref sourceBytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public ZaItemsWorkflow LoadSemanticExploreItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new ZaItemsWorkflowService(freshFileSource).Load(project);
    }

    public bool CanLoadSemanticExploreCorporaConcurrently =>
        !ZaWorkflowFileSource.HasActiveDeferredOutputBatch;

    public (
        Func<ZaItemsWorkflow> Items,
        Func<ZaPokemonWorkflow> Pokemon,
        Func<ZaMovesWorkflow> Moves) PrepareSemanticExploreCorpora(
            ProjectPaths paths,
            int maximumParallelism)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelism);

        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var effectiveParallelism = ZaWorkflowFileSource.HasActiveDeferredOutputBatch
            ? 1
            : Math.Clamp(maximumParallelism, 1, 3);
        var readerCount = effectiveParallelism > 1 ? 3 : 1;
        CapturedSemanticWorkflow<ZaItemsWorkflow>? items = null;
        CapturedSemanticWorkflow<ZaPokemonWorkflow>? pokemon = null;
        CapturedSemanticWorkflow<ZaMovesWorkflow>? moves = null;

        using (var readerPool = ZaWorkflowFileSource.CreateFreshSemanticReaderPool(
                   cacheManager,
                   paths,
                   MaximumSemanticSourceBytesPerFile,
                   readerCount))
        {
            void LoadAt(int index)
            {
                var source = readerPool.Readers[effectiveParallelism > 1 ? index : 0];
                switch (index)
                {
                    case 0:
                        items = CaptureSemanticWorkflow(
                            () => new ZaItemsWorkflowService(source).Load(project));
                        break;
                    case 1:
                        pokemon = CaptureSemanticWorkflow(
                            () => new ZaPokemonWorkflowService(source).Load(project));
                        break;
                    case 2:
                        moves = CaptureSemanticWorkflow(
                            () => new ZaMovesWorkflowService(source).Load(project));
                        break;
                }
            }

            _ = BoundedParallel.For(
                3,
                CreateSourceLoadPolicy("za-semantic-corpus-source-load", effectiveParallelism),
                LoadAt);
        }

        return (
            (items ?? throw new InvalidOperationException(
                "The semantic items workflow was not prepared.")).Get,
            (pokemon ?? throw new InvalidOperationException(
                "The semantic Pokemon workflow was not prepared.")).Get,
            (moves ?? throw new InvalidOperationException(
                "The semantic moves workflow was not prepared.")).Get);
    }

    public (
        Func<ZaTrainersWorkflow> Trainers,
        Func<ZaEncountersWorkflow> Encounters,
        Func<ZaItemsWorkflow> Items,
        Func<ZaPokemonWorkflow> Pokemon) PrepareGuidedDesignSources(
            ProjectPaths paths,
            int maximumParallelism)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelism);

        const int sourceCount = 4;
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var effectiveParallelism = ZaWorkflowFileSource.HasActiveDeferredOutputBatch
            ? 1
            : Math.Clamp(maximumParallelism, 1, sourceCount);
        var readerCount = effectiveParallelism > 1 ? sourceCount : 1;
        CapturedSemanticWorkflow<ZaTrainersWorkflow>? trainers = null;
        CapturedSemanticWorkflow<ZaEncountersWorkflow>? encounters = null;
        CapturedSemanticWorkflow<ZaItemsWorkflow>? items = null;
        CapturedSemanticWorkflow<ZaPokemonWorkflow>? pokemon = null;
        var fatalFailures = new ExceptionDispatchInfo?[sourceCount];

        using (var readerPool = ZaWorkflowFileSource.CreateFreshSemanticReaderPool(
                   cacheManager,
                   paths,
                   MaximumSemanticSourceBytesPerFile,
                   readerCount))
        {
            void LoadAt(int index)
            {
                try
                {
                    var source = readerPool.Readers[effectiveParallelism > 1 ? index : 0];
                    switch (index)
                    {
                        case 0:
                            trainers = CaptureSemanticWorkflow(
                                () => new ZaTrainersWorkflowService(source).Load(project));
                            break;
                        case 1:
                            encounters = CaptureSemanticWorkflow(
                                () => new ZaEncountersWorkflowService(source).Load(project));
                            break;
                        case 2:
                            items = CaptureSemanticWorkflow(
                                () => new ZaItemsWorkflowService(source).Load(project));
                            break;
                        case 3:
                            pokemon = CaptureSemanticWorkflow(
                                () => new ZaPokemonWorkflowService(source).Load(project));
                            break;
                    }
                }
                catch (Exception exception)
                {
                    fatalFailures[index] = ExceptionDispatchInfo.Capture(exception);
                }
            }

            _ = BoundedParallel.For(
                sourceCount,
                CreateSourceLoadPolicy("za-guided-design-source-load", effectiveParallelism),
                LoadAt);
        }

        foreach (var failure in fatalFailures)
        {
            failure?.Throw();
        }

        return (
            (trainers ?? throw new InvalidOperationException(
                "The Guided Design trainers workflow was not prepared.")).Get,
            (encounters ?? throw new InvalidOperationException(
                "The Guided Design encounters workflow was not prepared.")).Get,
            (items ?? throw new InvalidOperationException(
                "The Guided Design items workflow was not prepared.")).Get,
            (pokemon ?? throw new InvalidOperationException(
                "The Guided Design Pokemon workflow was not prepared.")).Get);
    }

    public ZaPokemonWorkflow LoadSemanticExplorePokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new ZaPokemonWorkflowService(freshFileSource).Load(project);
    }

    public ZaMovesWorkflow LoadSemanticExploreMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new ZaMovesWorkflowService(freshFileSource).Load(project);
    }

    public ZaTrainersWorkflow LoadBalanceLabTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new ZaTrainersWorkflowService(freshFileSource).Load(project);
    }

    public ZaEncountersWorkflow LoadBalanceLabEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new ZaEncountersWorkflowService(freshFileSource).Load(project);
    }

    public (ZaEncountersWorkflow Encounters, ZaMovesWorkflow Moves) LoadGameModuleScriptedBossTimeline(
        ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = CreateGameModuleFileSource();
        return (
            new ZaEncountersWorkflowService(freshFileSource).LoadGameModuleReadOnly(
                project,
                includeScriptedBosses: true,
                includeEncounterTables: false),
            new ZaMovesWorkflowService(freshFileSource).LoadGameModuleReadOnly(project));
    }

    public (
        ZaEncountersWorkflow ScriptedBossEncounters,
        ZaEncountersWorkflow WildEncounters,
        ZaMovesWorkflow Moves,
        ZaTrainersWorkflow Trainers,
        ZaEncounterCompatibilityWorkflow EncounterCompatibility,
        ZaPokemonWorkflow Pokemon,
        ZaTrainerPoolsWorkflow TrainerPools,
        ZaTypeEffectivenessState TypeEffectivenessState)
        LoadGameModuleCapabilityBatch(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        const int groupCount = 4;
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var effectiveParallelism = GameModuleCapabilityBatchParallelism;
        var readerCount = effectiveParallelism > 1 ? groupCount : 1;
        CapturedSemanticWorkflow<ZaEncountersWorkflow>? scriptedBossEncounters = null;
        CapturedSemanticWorkflow<ZaEncountersWorkflow>? wildEncounters = null;
        CapturedSemanticWorkflow<ZaMovesWorkflow>? moves = null;
        CapturedSemanticWorkflow<ZaTrainersWorkflow>? trainers = null;
        CapturedSemanticWorkflow<ZaEncounterCompatibilityWorkflow>? encounterCompatibility = null;
        CapturedSemanticWorkflow<ZaPokemonWorkflow>? pokemon = null;
        CapturedSemanticWorkflow<ZaTrainerPoolsWorkflow>? trainerPools = null;
        CapturedSemanticWorkflow<ZaTypeEffectivenessState>? typeEffectivenessState = null;

        using (var readerPool = ZaWorkflowFileSource.CreateFreshSemanticReaderPool(
                   cacheManager,
                   paths,
                   MaximumSemanticSourceBytesPerFile,
                   readerCount,
                   MaximumSemanticSourceFiles,
                   MaximumSemanticSourceBytes))
        {
            void LoadGroup(int groupIndex)
            {
                var source = readerPool.Readers[effectiveParallelism > 1 ? groupIndex : 0];
                switch (groupIndex)
                {
                    case 0:
                        var encounterService = new ZaEncountersWorkflowService(source);
                        scriptedBossEncounters = CaptureSemanticWorkflow(
                            () => encounterService.LoadGameModuleReadOnly(
                                project,
                                includeScriptedBosses: true,
                                includeEncounterTables: false));
                        wildEncounters = CaptureSemanticWorkflow(
                            () => encounterService.LoadGameModuleReadOnly(
                                project,
                                includeScriptedBosses: false,
                                includeEncounterTables: true));
                        encounterCompatibility = CaptureSemanticWorkflow(
                            () => encounterService.LoadGameModuleCompatibility(project));
                        break;
                    case 1:
                        trainers = CaptureSemanticWorkflow(
                            () => new ZaTrainersWorkflowService(source)
                                .LoadGameModuleReadOnly(project));
                        trainerPools = CaptureSemanticWorkflow(
                            () => new ZaTrainerPoolsWorkflowService(source).Load(project));
                        break;
                    case 2:
                        var executableReader = new BoundedGameModuleExecutableReader(paths);
                        pokemon = CaptureSemanticWorkflow(
                            () => new ZaPokemonWorkflowService(source, executableReader.ReadAllBytes)
                                .LoadGameModuleReadOnly(project, includeDexEditor: true));
                        typeEffectivenessState = CaptureSemanticWorkflow(
                            () => new ZaTypeEffectivenessStateService(executableReader.ReadAllBytes)
                                .Load(project));
                        break;
                    case 3:
                        moves = CaptureSemanticWorkflow(
                            () => new ZaMovesWorkflowService(source).LoadGameModuleReadOnly(project));
                        break;
                }
            }

            _ = BoundedParallel.For(
                groupCount,
                GameModuleCapabilityPolicy,
                LoadGroup);
        }

        // Materialize in the original public tuple order so a failing source remains deterministic.
        var preparedScriptedBossEncounters = RequirePrepared(
            scriptedBossEncounters,
            "scripted boss encounters").Get();
        var preparedWildEncounters = RequirePrepared(wildEncounters, "wild encounters").Get();
        var preparedMoves = RequirePrepared(moves, "moves").Get();
        var preparedTrainers = RequirePrepared(trainers, "trainers").Get();
        var preparedEncounterCompatibility = RequirePrepared(
            encounterCompatibility,
            "encounter compatibility").Get();
        var preparedPokemon = RequirePrepared(pokemon, "Pokemon").Get();
        var preparedTrainerPools = RequirePrepared(trainerPools, "trainer pools").Get();
        var preparedTypeEffectivenessState = RequirePrepared(
            typeEffectivenessState,
            "type effectiveness").Get();
        return (
            preparedScriptedBossEncounters,
            preparedWildEncounters,
            preparedMoves,
            preparedTrainers,
            preparedEncounterCompatibility,
            preparedPokemon,
            preparedTrainerPools,
            preparedTypeEffectivenessState);
    }

    public int GameModuleCapabilityBatchParallelism =>
        ZaWorkflowFileSource.HasActiveDeferredOutputBatch
            ? 1
            : BoundedParallel
                .Plan(MaximumGameModuleCapabilityParallelism, GameModuleCapabilityPolicy)
                .DegreeOfParallelism;

    public ZaTrainersWorkflow LoadGameModuleTrainerArchetypes(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return new ZaTrainersWorkflowService(CreateGameModuleFileSource()).LoadGameModuleReadOnly(project);
    }

    public ZaEncountersWorkflow LoadGameModuleWildSpawns(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return new ZaEncountersWorkflowService(CreateGameModuleFileSource()).LoadGameModuleReadOnly(
            project,
            includeScriptedBosses: false,
            includeEncounterTables: true);
    }

    public ZaMovesWorkflow LoadGameModuleMoveVariants(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return new ZaMovesWorkflowService(CreateGameModuleFileSource()).LoadGameModuleReadOnly(project);
    }

    public ZaEncounterCompatibilityWorkflow LoadGameModuleEncounterCompatibility(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return new ZaEncountersWorkflowService(CreateGameModuleFileSource())
            .LoadGameModuleCompatibility(project);
    }

    public ZaPokemonWorkflow LoadGameModuleAlphaMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var executableReader = new BoundedGameModuleExecutableReader(paths);
        return new ZaPokemonWorkflowService(
                CreateGameModuleFileSource(),
                executableReader.ReadAllBytes)
            .LoadGameModuleReadOnly(project, includeDexEditor: true);
    }

    public ZaTrainerPoolsWorkflow LoadGameModuleTrainerPools(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return new ZaTrainerPoolsWorkflowService(CreateGameModuleFileSource()).Load(project);
    }

    public ZaTypeEffectivenessState LoadGameModuleTypeEffectivenessState(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var executableReader = new BoundedGameModuleExecutableReader(paths);
        return new ZaTypeEffectivenessStateService(executableReader.ReadAllBytes).Load(project);
    }

    private ZaWorkflowFileSource CreateGameModuleFileSource()
    {
        return new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile,
            MaximumSemanticSourceFiles,
            MaximumSemanticSourceBytes);
    }

    public ZaItemsEditResult UpdateItemFieldsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaItemFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new ZaItemsWorkflowService(source);
        return new ZaItemsEditSessionService(workspace, source, workflow)
            .UpdateFields(paths, session, updates);
    }

    public ZaPokemonEditResult UpdatePokemonFieldsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new ZaPokemonWorkflowService(source);
        return new ZaPokemonEditSessionService(workspace, source, workflow)
            .UpdateFields(paths, session, updates);
    }

    public ZaPokemonEditResult UpdatePokemonEvolutionsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPokemonEvolutionUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new ZaPokemonWorkflowService(source);
        return new ZaPokemonEditSessionService(workspace, source, workflow)
            .UpdateEvolutions(paths, session, updates);
    }

    public ZaTrainersEditResult UpdateTrainerFieldsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaTrainerFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new ZaTrainersWorkflowService(source);
        return new ZaTrainersEditSessionService(workspace, source, workflow)
            .UpdateFields(paths, session, updates);
    }

    public ZaPokemonEditResult ReadPokemonEffectiveFreshBounded(
        ProjectPaths paths,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var workspace = new ProjectWorkspaceService();
        var source = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new ZaPokemonWorkflowService(source);
        return new ZaPokemonEditSessionService(workspace, source, workflow)
            .ReadEffective(paths, session);
    }

    public ZaPokemonEditResult UpdatePokemonLearnsetFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string action,
        int? slot,
        int? moveId,
        int? level)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var workspace = new ProjectWorkspaceService();
        var source = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new ZaPokemonWorkflowService(source);
        return new ZaPokemonEditSessionService(workspace, source, workflow)
            .UpdateLearnset(paths, session, personalId, action, slot, moveId, level);
    }

    public ZaEncountersEditResult UpdateEncounterSlotFieldsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaEncounterSlotFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new ZaEncountersWorkflowService(source);
        return new ZaEncountersEditSessionService(workspace, source, workflow)
            .UpdateSlotFields(paths, session, updates);
    }

    public ChangePlan CreateGuidedChangePlanFreshBounded(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        var domain = session.PendingEdits
            .Select(edit => edit.Domain)
            .Distinct(StringComparer.Ordinal)
            .SingleOrDefault();
        var workspace = new ProjectWorkspaceService();
        var source = new ZaWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return domain switch
        {
            ZaEditSessionSupport.ItemsDomain => new ZaItemsEditSessionService(
                    workspace,
                    source,
                    new ZaItemsWorkflowService(source))
                .CreateChangePlan(paths, session, outputMode),
            ZaEditSessionSupport.PokemonDomain => new ZaPokemonEditSessionService(
                    workspace,
                    source,
                    new ZaPokemonWorkflowService(source))
                .CreateChangePlan(paths, session, outputMode),
            ZaEditSessionSupport.TrainersDomain => new ZaTrainersEditSessionService(
                    workspace,
                    source,
                    new ZaTrainersWorkflowService(source))
                .CreateChangePlan(paths, session, outputMode),
            ZaEditSessionSupport.EncountersDomain => new ZaEncountersEditSessionService(
                    workspace,
                    source,
                    new ZaEncountersWorkflowService(source))
                .CreateChangePlan(paths, session, outputMode),
            _ => throw new InvalidOperationException(
                "Generated review supports exactly one verified Pokemon Legends Z-A workflow domain per plan."),
        };
    }

    private SemanticSourcePayloadPairCapture?[] CaptureSemanticSourcePayloadPairs(
        ProjectPaths paths,
        IReadOnlyList<string> virtualPaths)
    {
        var captures = new SemanticSourcePayloadPairCapture?[virtualPaths.Count];
        var maximumParallelism = BoundedParallel
            .Plan(Math.Max(1, virtualPaths.Count), SemanticFingerprintPolicy)
            .DegreeOfParallelism;
        var readerCount = ZaWorkflowFileSource.HasActiveDeferredOutputBatch
            ? 1
            : Math.Min(maximumParallelism, Math.Max(1, virtualPaths.Count));
        using var readerPool = ZaWorkflowFileSource.CreateFreshSemanticReaderPool(
            cacheManager,
            paths,
            MaximumSemanticSourceBytesPerFile,
            readerCount);

        void CaptureAt(int readerIndex, int pathIndex)
        {
            var source = readerPool.Readers[readerIndex];
            try
            {
                var virtualPath = virtualPaths[pathIndex];
                captures[pathIndex] = new SemanticSourcePayloadPairCapture(
                    CaptureSemanticSourcePayloadPairFromReaders(
                        () => source.ReadBaseBytesFresh(paths, virtualPath),
                        observedBaseBytes => source.ReadCurrentSourceFreshUsingObservedBase(
                            paths,
                            virtualPath,
                            observedBaseBytes)),
                    Failure: null);
            }
            catch (Exception exception)
            {
                captures[pathIndex] = new SemanticSourcePayloadPairCapture(
                    Observation: null,
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception));
            }
        }

        long plannedSourceBytes = 0;
        for (var batchStart = 0; batchStart < virtualPaths.Count; batchStart += readerCount)
        {
            var batchCount = Math.Min(readerCount, virtualPaths.Count - batchStart);
            _ = BoundedParallel.For(
                batchCount,
                SemanticFingerprintPolicy,
                readerIndex => CaptureAt(readerIndex, batchStart + readerIndex));

            for (var pathIndex = batchStart; pathIndex < batchStart + batchCount; pathIndex++)
            {
                var capture = captures[pathIndex]
                    ?? throw new InvalidOperationException("A semantic source payload was not observed.");
                if (capture.Failure is not null)
                {
                    return captures;
                }

                var observation = capture.Observation
                    ?? throw new InvalidOperationException("A semantic source payload was not observed.");
                foreach (var payload in new[] { observation.Base, observation.Current })
                {
                    if (!payload.IsMissing)
                    {
                        if (payload.Length > MaximumSemanticSourceBytes - plannedSourceBytes)
                        {
                            captures[pathIndex] = new SemanticSourcePayloadPairCapture(
                                Observation: null,
                                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                                    new InvalidDataException(
                                        "The semantic source bytes exceed their bounded limit.")));
                            return captures;
                        }

                        plannedSourceBytes = checked(plannedSourceBytes + payload.Length);
                    }
                }
            }
        }

        return captures;
    }

    private static BoundedConcurrencyPolicy CreateSourceLoadPolicy(
        string name,
        int maximumParallelism)
    {
        return new BoundedConcurrencyPolicy(
            name,
            BoundedWorkloadKind.Decode,
            EstimatedSemanticFingerprintWorkerBytes,
            maximumParallelism,
            memoryBudgetDivisor: 8,
            degreeOfParallelismWhenMemoryUnknown: Math.Min(4, maximumParallelism));
    }

    private static CapturedSemanticWorkflow<T> RequirePrepared<T>(
        CapturedSemanticWorkflow<T>? captured,
        string label)
        where T : class
    {
        return captured ?? throw new InvalidOperationException(
            $"The Z-A Game Tools {label} workflow was not prepared.");
    }

    private static SemanticSourcePayloadPairObservation CaptureSemanticSourcePayloadPairFromReaders(
        Func<byte[]> readBase,
        Func<byte[]?, (byte[] Bytes, ProjectFileLayer Layer)> readCurrent)
    {
        byte[]? observedBaseBytes = null;
        SemanticSourcePayloadObservation baseObservation;
        try
        {
            observedBaseBytes = readBase();
            baseObservation = CaptureSemanticSourcePayload(observedBaseBytes, "base");
        }
        catch (ProjectFileOperationException exception) when (IsMissingSource(exception))
        {
            baseObservation = SemanticSourcePayloadObservation.Missing;
        }

        SemanticSourcePayloadObservation currentObservation;
        try
        {
            var current = readCurrent(observedBaseBytes);
            currentObservation = current.Layer == ProjectFileLayer.Base
                && ReferenceEquals(current.Bytes, observedBaseBytes)
                && !baseObservation.IsMissing
                    ? baseObservation with { Origin = current.Layer.ToString() }
                    : CaptureSemanticSourcePayload(
                        current.Bytes,
                        current.Layer.ToString());
        }
        catch (ProjectFileOperationException exception) when (IsMissingSource(exception))
        {
            currentObservation = SemanticSourcePayloadObservation.Missing;
        }

        return new SemanticSourcePayloadPairObservation(baseObservation, currentObservation);
    }

    private static SemanticSourcePayloadObservation CaptureSemanticSourcePayload(
        byte[] bytes,
        string origin)
    {
        return new SemanticSourcePayloadObservation(
            IsMissing: false,
            origin,
            bytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static void AppendSemanticSourcePayloadPair(
        IncrementalHash hash,
        SemanticSourcePayloadPairObservation observation,
        ref long sourceBytes)
    {
        AppendSemanticSourcePayload(hash, observation.Base, ref sourceBytes);
        AppendSemanticSourcePayload(hash, observation.Current, ref sourceBytes);
    }

    private static void AppendSemanticSourcePayload(
        IncrementalHash hash,
        SemanticSourcePayloadObservation observation,
        ref long sourceBytes)
    {
        if (observation.IsMissing)
        {
            AppendSemanticSourceHash(hash, "missing");
            return;
        }

        if (observation.Length > MaximumSemanticSourceBytes - sourceBytes)
        {
            throw new InvalidDataException("The semantic source bytes exceed their bounded limit.");
        }

        sourceBytes = checked(sourceBytes + observation.Length);
        AppendSemanticSourceHash(hash, observation.Origin);
        AppendSemanticSourceHash(
            hash,
            observation.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendSemanticSourceHash(hash, observation.Digest);
    }

    private sealed record SemanticSourcePayloadPairCapture(
        SemanticSourcePayloadPairObservation? Observation,
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? Failure);

    private sealed record SemanticSourcePayloadPairObservation(
        SemanticSourcePayloadObservation Base,
        SemanticSourcePayloadObservation Current);

    private sealed record SemanticSourcePayloadObservation(
        bool IsMissing,
        string Origin,
        int Length,
        string Digest)
    {
        public static SemanticSourcePayloadObservation Missing { get; } = new(
            IsMissing: true,
            Origin: string.Empty,
            Length: 0,
            Digest: string.Empty);
    }

    private sealed record CapturedSemanticWorkflow<T>(
        T? Value,
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? Failure)
        where T : class
    {
        public T Get()
        {
            Failure?.Throw();
            return Value ?? throw new InvalidOperationException(
                "The semantic workflow was not prepared.");
        }
    }

    private static CapturedSemanticWorkflow<T> CaptureSemanticWorkflow<T>(Func<T> load)
        where T : class
    {
        try
        {
            return new CapturedSemanticWorkflow<T>(load(), Failure: null);
        }
        catch (Exception exception)
        {
            return new CapturedSemanticWorkflow<T>(
                Value: null,
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception));
        }
    }

    private static void AppendSemanticExecutableSourceHash(
        IncrementalHash hash,
        string role,
        string? path)
    {
        AppendSemanticSourceHash(hash, role);
        if (path is null || !File.Exists(path))
        {
            AppendSemanticSourceHash(hash, "missing");
            return;
        }

        AppendSemanticSourceHash(
            hash,
            SemanticProjectBuildIdentity.CaptureBoundedFile(
                path,
                role,
                MaximumSemanticSourceBytesPerFile));
    }

    private static bool IsMissingSource(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendSemanticSourceHash(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(Encoding.UTF8.GetBytes(
            bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        hash.AppendData("\n"u8);
        hash.AppendData(bytes);
        hash.AppendData("\n"u8);
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
            trainerPoolsWorkflowService.CreateSummary(project),
            fashionCatalogWorkflowService.CreateSummary(project),
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

    public ZaTextWorkflow LoadTextUnpaged(ProjectPaths paths, string language)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var project = projectWorkspaceService.Open(paths);
        return textWorkflowService.LoadUnpaged(project, language);
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

    public ZaTrainerPoolsWorkflow LoadTrainerPools(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return trainerPoolsWorkflowService.Load(project);
    }

    public ZaFashionCatalogWorkflow LoadFashionCatalog(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return fashionCatalogWorkflowService.Load(project);
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

    public ZaItemsEditResult StageItemVanilla(
        ProjectPaths paths,
        EditSession? session,
        int itemId)
    {
        return itemsEditSessionService.StageItemVanilla(paths, session, itemId);
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

    public ZaPokemonEditResult MovePokemonDexPlacement(
        ProjectPaths paths,
        EditSession? session,
        int sourceSpeciesId,
        string destinationDexKind,
        int destinationDisplayedNumber)
    {
        return pokemonEditSessionService.MoveDexPlacement(
            paths,
            session,
            sourceSpeciesId,
            destinationDexKind,
            destinationDisplayedNumber);
    }

    public ZaPokemonEditResult ResizePokemonDex(
        ProjectPaths paths,
        EditSession? session,
        int regularCount)
    {
        return pokemonEditSessionService.ResizeDex(
            paths,
            session,
            regularCount);
    }

    public ZaPokemonEditResult StagePokemonDexVanilla(
        ProjectPaths paths,
        EditSession? session)
    {
        return pokemonEditSessionService.StageVanillaDexLayout(
            paths,
            session);
    }

    public ZaPokemonEditResult StagePokemonDexMegaSync(
        ProjectPaths paths,
        EditSession? session)
    {
        return pokemonEditSessionService.StageMegaDexSync(
            paths,
            session);
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

    public ZaMovesEditResult StageMoveVanilla(
        ProjectPaths paths,
        EditSession? session,
        int moveId)
    {
        return movesEditSessionService.StageMoveVanilla(paths, session, moveId);
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
        string value,
        string? rowId = null)
    {
        return shopsEditSessionService.UpdateInventoryItem(paths, session, shopId, slot, field, value, rowId);
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

    public ZaTrainerPoolsEditResult StageTrainerPoolFixedCountSwap(
        ProjectPaths paths,
        EditSession? session,
        ZaTrainerPoolFixedCountSwap operation)
    {
        return trainerPoolsEditSessionService.StageFixedCountSwap(paths, session, operation);
    }

    public ZaFashionCatalogStageResult StageFashionCatalogFieldEdit(
        ProjectPaths paths,
        EditSession? session,
        ZaFashionCatalogFieldEdit operation)
    {
        return fashionCatalogEditSessionService.StageFieldEdit(paths, session, operation);
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

    public ZaGiftPokemonEditResult StageGiftPokemonVanilla(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex)
    {
        return giftPokemonEditSessionService.StageGiftVanilla(paths, session, giftIndex);
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

    public ZaEncountersEditResult StageEncounterSlotVanilla(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot)
    {
        return encountersEditSessionService.StageSlotVanilla(
            paths,
            session,
            tableId,
            slot);
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
        if (IsMixedAlphaMoveSession(session))
        {
            return CreateAlphaMoveMixedValidation(session);
        }

        if (IsMixedScopedDexLayoutSession(session))
        {
            return CreateScopedDexLayoutMixedValidation(session);
        }

        var domain = GetDomain(session);
        return domain == ZaEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? ValidateNormalDomains(paths, session, domains)
            : ValidateSingleDomain(paths, session, domain);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        using var outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
        using var freshReads = ZaWorkflowFileSource.BeginFreshReadScope();
        projectWorkspaceService.ClearMemoryCache();
        pokemonWorkflowService.ClearMemoryCache();

        if (IsMixedAlphaMoveSession(session))
        {
            return CreateAlphaMoveMixedChangePlan(session);
        }

        if (IsMixedScopedDexLayoutSession(session))
        {
            return CreateScopedDexLayoutMixedChangePlan(session);
        }

        var domain = GetDomain(session);
        return domain == ZaEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? CreateNormalDomainChangePlan(paths, session, domains, outputMode)
            : CreateSingleDomainChangePlan(paths, session, domain, outputMode);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan, ZaOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        using var freshReads = ZaWorkflowFileSource.BeginFreshReadScope();
        projectWorkspaceService.ClearMemoryCache();
        pokemonWorkflowService.ClearMemoryCache();

        if (IsMixedAlphaMoveSession(session))
        {
            return CreateAlphaMoveMixedApplyResult(session);
        }

        if (IsMixedScopedDexLayoutSession(session))
        {
            return CreateScopedDexLayoutMixedApplyResult(session);
        }

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
            ZaEditSessionDomain.TrainerPools => trainerPoolsEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.FashionCatalog => fashionCatalogEditSessionService.Validate(paths, session),
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
            ZaEditSessionDomain.TrainerPools => trainerPoolsEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.FashionCatalog => fashionCatalogEditSessionService.CreateChangePlan(paths, session, outputMode),
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
            ZaEditSessionDomain.TrainerPools => trainerPoolsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.FashionCatalog => fashionCatalogEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
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
        var effectiveSession = session;
        foreach (var domain in domains)
        {
            var validation = ValidateSingleDomain(
                paths,
                SliceSession(effectiveSession, domain),
                domain);
            diagnostics.AddRange(validation.Diagnostics);
            effectiveSession = MergeValidatedDomainSession(
                effectiveSession,
                domain,
                validation.Session);
        }

        return new ZaEditSessionValidation(
            effectiveSession,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    private ChangePlan CreateNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<ZaEditSessionDomain> domains,
        ZaOutputMode outputMode)
    {
        return CreateNormalDomainChangePlanSnapshot(paths, session, domains, outputMode).CombinedPlan;
    }

    private NormalDomainChangePlanSnapshot CreateNormalDomainChangePlanSnapshot(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<ZaEditSessionDomain> domains,
        ZaOutputMode outputMode)
    {
        var validation = ValidateNormalDomains(paths, session, domains);
        var diagnostics = validation.Diagnostics.ToList();
        var effectiveSession = ZaChangePlanSourceGuard.WithoutPendingEditAssociations(
            validation.Session);
        var effectiveDomains = domains
            .Where(domain => SliceSession(effectiveSession, domain).PendingEdits.Count > 0)
            .ToArray();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new NormalDomainChangePlanSnapshot(
                new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics),
                new Dictionary<ZaEditSessionDomain, ChangePlan>(),
                effectiveSession,
                effectiveDomains);
        }

        if (effectiveDomains.Length == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Pokemon Legends Z-A edit before reviewing a change plan.",
                "za.editor",
                expected: "Pending Pokemon Legends Z-A edit"));
            return new NormalDomainChangePlanSnapshot(
                new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics)
                {
                    EffectivePendingEdits = effectiveSession.PendingEdits,
                },
                new Dictionary<ZaEditSessionDomain, ChangePlan>(),
                effectiveSession,
                effectiveDomains);
        }

        var writes = new List<PlannedFileWrite>();
        var domainPlans = new Dictionary<ZaEditSessionDomain, ChangePlan>();
        foreach (var domain in effectiveDomains)
        {
            var domainPlan = CreateSingleDomainChangePlan(
                paths,
                SliceSession(effectiveSession, domain),
                domain,
                outputMode);
            domainPlans.Add(domain, domainPlan);
            diagnostics.AddRange(domainPlan.Diagnostics);
            writes.AddRange(domainPlan.Writes);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new NormalDomainChangePlanSnapshot(
                new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics),
                domainPlans,
                effectiveSession,
                effectiveDomains);
        }

        return new NormalDomainChangePlanSnapshot(
            new ChangePlan(
                session.Id,
                CombinePlannedWrites(
                    writes,
                    CreatePendingEditFingerprint(effectiveSession.PendingEdits)),
                diagnostics)
            {
                EffectivePendingEdits = effectiveSession.PendingEdits,
            },
            domainPlans,
            effectiveSession,
            effectiveDomains);
    }

    private ApplyResult ApplyNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        IReadOnlyList<ZaEditSessionDomain> domains,
        ZaOutputMode outputMode)
    {
        try
        {
            using var outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
            return ApplyNormalDomainChangePlanCore(
                paths,
                session,
                reviewedPlan,
                domains,
                outputMode);
        }
        finally
        {
            ClearMemoryCaches(clearReusableDataCaches: false);
        }
    }

    private ApplyResult ApplyNormalDomainChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        IReadOnlyList<ZaEditSessionDomain> domains,
        ZaOutputMode outputMode)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentSnapshot = CreateNormalDomainChangePlanSnapshot(paths, session, domains, outputMode);
        var currentPlan = currentSnapshot.CombinedPlan;
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        OutputApplyResult? outputTransaction = null;

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

        try
        {
            using var deferredOutput = ZaWorkflowFileSource.BeginDeferredOutputBatch(
                paths,
                outputMode,
                currentPlan,
                new ZaOutputApplyContext(
                    OutputReviewFingerprint.FromChangePlan(currentPlan),
                    new OwnershipOwnerId("workflow.za.output"),
                    currentSnapshot.EffectiveDomains
                        .Select(domain => new OutputApplyOrigin(
                            OutputApplyOriginKind.Workflow,
                            GetDomainName(domain)))
                        .Distinct()
                        .ToArray()));
            foreach (var domain in currentSnapshot.EffectiveDomains)
            {
                var domainSession = SliceSession(currentSnapshot.EffectiveSession, domain);
                var domainPlan = CreateSingleDomainChangePlan(
                    paths,
                    domainSession,
                    domain,
                    outputMode);
                if (domainPlan.Diagnostics.Any(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    diagnostics.AddRange(domainPlan.Diagnostics);
                    writtenFiles.Clear();
                    break;
                }

                var result = ApplySingleDomainChangePlan(
                    paths,
                    domainSession,
                    domainPlan,
                    domain,
                    outputMode);
                diagnostics.AddRange(result.Diagnostics);
                writtenFiles.AddRange(result.WrittenFiles);

                if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    writtenFiles.Clear();
                    break;
                }

                ClearMemoryCaches(clearReusableDataCaches: false);
            }

            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                outputTransaction = deferredOutput.Commit();
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException
            or OutputCoordinatorException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Legends Z-A mixed change plan could not be applied: {exception.Message}",
                "za.editor",
                expected: "Readable sources and writable output targets"));
            if (exception is ZaOutputApplyNotCommittedException notCommitted)
            {
                outputTransaction = notCommitted.Result;
            }

            writtenFiles.Clear();
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics,
            outputTransaction);
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
            [ZaEditSessionSupport.TrainerPoolsDomain] => ZaEditSessionDomain.TrainerPools,
            [ZaEditSessionSupport.FashionCatalogDomain] => ZaEditSessionDomain.FashionCatalog,
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
            ZaEditSessionSupport.TrainerPoolsDomain => ZaEditSessionDomain.TrainerPools,
            ZaEditSessionSupport.FashionCatalogDomain => ZaEditSessionDomain.FashionCatalog,
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

    private static EditSession MergeValidatedDomainSession(
        EditSession session,
        ZaEditSessionDomain domain,
        EditSession validatedDomainSession)
    {
        var domainName = GetDomainName(domain);
        var remainingValidatedEdits = validatedDomainSession.PendingEdits
            .Where(edit => string.Equals(edit.Domain, domainName, StringComparison.Ordinal))
            .ToList();
        var mergedEdits = new List<PendingEdit>(session.PendingEdits.Count);
        var insertionIndex = -1;

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, domainName, StringComparison.Ordinal))
            {
                mergedEdits.Add(edit);
                continue;
            }

            insertionIndex = mergedEdits.Count;
            var validatedIndex = remainingValidatedEdits.FindIndex(candidate =>
                ReferenceEquals(candidate, edit) || candidate == edit);
            if (validatedIndex < 0)
            {
                validatedIndex = remainingValidatedEdits.FindIndex(candidate =>
                    HasSamePendingEditIdentity(candidate, edit));
            }

            if (validatedIndex < 0)
            {
                continue;
            }

            mergedEdits.Add(remainingValidatedEdits[validatedIndex]);
            remainingValidatedEdits.RemoveAt(validatedIndex);
            insertionIndex = mergedEdits.Count;
        }

        if (remainingValidatedEdits.Count > 0)
        {
            mergedEdits.InsertRange(
                insertionIndex < 0 ? mergedEdits.Count : insertionIndex,
                remainingValidatedEdits);
        }

        return session with { PendingEdits = mergedEdits.ToArray() };
    }

    private static bool HasSamePendingEditIdentity(PendingEdit left, PendingEdit right)
    {
        return string.Equals(left.Domain, right.Domain, StringComparison.Ordinal)
            && string.Equals(left.RecordId, right.RecordId, StringComparison.Ordinal)
            && string.Equals(left.Field, right.Field, StringComparison.Ordinal)
            && string.Equals(left.Owner, right.Owner, StringComparison.Ordinal)
            && left.Association == right.Association;
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
            ZaEditSessionDomain.TrainerPools => ZaEditSessionSupport.TrainerPoolsDomain,
            ZaEditSessionDomain.FashionCatalog => ZaEditSessionSupport.FashionCatalogDomain,
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

    private static IReadOnlyList<PlannedFileWrite> CombinePlannedWrites(
        IEnumerable<PlannedFileWrite> writes,
        string pendingEditFingerprint)
    {
        return writes
            .GroupBy(
                write => new RelativeOutputPath(write.TargetRelativePath).CanonicalKey,
                StringComparer.Ordinal)
            .Select(group =>
            {
                var groupedWrites = group
                    .OrderBy(write => new RelativeOutputPath(write.TargetRelativePath).Value, StringComparer.Ordinal)
                    .ToArray();
                var targetRelativePath = new RelativeOutputPath(
                    groupedWrites[0].TargetRelativePath).Value;
                var combined = groupedWrites.Length == 1
                    ? groupedWrites[0]
                    : new PlannedFileWrite(
                        targetRelativePath,
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
                        CombineSourceFingerprints(groupedWrites))
                    {
                        SourceBindingFingerprint = CombineSourceBindingFingerprints(groupedWrites),
                    };
                return combined with
                {
                    SourceFingerprint = CombineFingerprintValues(
                        [combined.SourceFingerprint, pendingEditFingerprint]),
                };
            })
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? CombineFingerprintValues(IEnumerable<string?> values)
    {
        var fingerprints = values
            .Where(fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return fingerprints.Length switch
        {
            0 => null,
            1 => fingerprints[0],
            _ => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    "KM.ZA.CombinedChangePlan.v1\n" + string.Join('\n', fingerprints))))
                .ToLowerInvariant(),
        };
    }

    private static string CreatePendingEditFingerprint(IReadOnlyList<PendingEdit> edits)
    {
        var canonical = new StringBuilder("KM.ZA.PendingEdits.v1|");
        AppendFingerprintComponent(canonical, edits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var edit in edits
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Owner, StringComparer.Ordinal)
                     .ThenBy(edit => edit.NewValue, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Summary, StringComparer.Ordinal))
        {
            AppendFingerprintComponent(canonical, edit.Domain);
            AppendFingerprintComponent(canonical, edit.Summary);
            AppendFingerprintComponent(canonical, edit.RecordId);
            AppendFingerprintComponent(canonical, edit.Field);
            AppendFingerprintComponent(canonical, edit.NewValue);
            AppendFingerprintComponent(canonical, edit.Owner);
            AppendFingerprintComponent(
                canonical,
                edit.Sources.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintComponent(
                    canonical,
                    ((int)source.Layer).ToString(System.Globalization.CultureInfo.InvariantCulture));
                AppendFingerprintComponent(canonical, source.RelativePath);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendFingerprintComponent(StringBuilder destination, string? value)
    {
        destination.Append(value?.Length ?? -1)
            .Append(':')
            .Append(value)
            .Append('|');
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

    private static string? CombineSourceBindingFingerprints(
        IReadOnlyList<PlannedFileWrite> writes)
    {
        if (writes.All(write => string.IsNullOrWhiteSpace(write.SourceBindingFingerprint)))
        {
            return null;
        }

        var components = writes
            .Select(write => write.SourceBindingFingerprint ?? "<none>")
            .Order(StringComparer.Ordinal);
        var payload = Encoding.UTF8.GetBytes(
            "KM.ZA.CombinedSourceBinding.v1\n" + string.Join('\n', components));
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private sealed record NormalDomainChangePlanSnapshot(
        ChangePlan CombinedPlan,
        IReadOnlyDictionary<ZaEditSessionDomain, ChangePlan> DomainPlans,
        EditSession EffectiveSession,
        IReadOnlyList<ZaEditSessionDomain> EffectiveDomains);

    private sealed class BoundedGameModuleExecutableReader
    {
        private const int MaximumExecutableCount = 2;
        private const long MaximumAggregateExecutableBytes =
            (long)MaximumExecutableCount * MaximumSemanticSourceBytesPerFile;

        private readonly IReadOnlyDictionary<string, string> allowedRootsByPath;
        private readonly Dictionary<string, byte[]> memo;
        private long observedBytes;

        public BoundedGameModuleExecutableReader(ProjectPaths paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            var comparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var candidates = new[]
            {
                (Path: ZaExeFsMainFileResolver.ResolveBasePath(paths), Root: paths.BaseExeFsPath),
                (Path: ZaExeFsMainFileResolver.ResolveOutputPath(paths), Root: paths.OutputRootPath),
            };
            var allowed = new Dictionary<string, string>(comparer);
            foreach (var candidate in candidates)
            {
                if (candidate.Path is null || string.IsNullOrWhiteSpace(candidate.Root))
                {
                    continue;
                }

                allowed[Path.GetFullPath(candidate.Path)] =
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate.Root));
            }

            allowedRootsByPath = allowed;
            memo = new Dictionary<string, byte[]>(comparer);
        }

        public byte[] ReadAllBytes(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var fullPath = Path.GetFullPath(path);
            if (!allowedRootsByPath.TryGetValue(fullPath, out var root)
                || PathContainment.IsOutsideRoot(Path.GetRelativePath(root, fullPath))
                || TraversesReparsePoint(root, fullPath))
            {
                throw new InvalidDataException(
                    "The Dex Layout executable source is outside its verified project roots or traverses a linked path.");
            }

            if (memo.TryGetValue(fullPath, out var cached))
            {
                return cached.ToArray();
            }

            if (memo.Count >= MaximumExecutableCount)
            {
                throw new InvalidDataException(
                    "The Dex Layout executable source count exceeds its bounded limit.");
            }

            var file = new FileInfo(fullPath);
            file.Refresh();
            if (!file.Exists
                || file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || !string.IsNullOrEmpty(file.LinkTarget)
                || file.Length < 0
                || file.Length > MaximumSemanticSourceBytesPerFile
                || file.Length > MaximumAggregateExecutableBytes - observedBytes)
            {
                throw new InvalidDataException(
                    "The Dex Layout executable source is missing, linked, or exceeds its bounded size.");
            }

            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.SequentialScan);
            var length = stream.Length;
            if (length != file.Length)
            {
                throw new InvalidDataException(
                    "The Dex Layout executable source changed before it could be read.");
            }

            var bytes = new byte[checked((int)length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1 || stream.Length != length)
            {
                throw new InvalidDataException(
                    "The Dex Layout executable source changed while it was read.");
            }

            observedBytes = checked(observedBytes + length);
            memo.Add(fullPath, bytes);
            return bytes.ToArray();
        }

        private static bool TraversesReparsePoint(string root, string fullPath)
        {
            var volumeRoot = Path.GetPathRoot(root);
            if (string.IsNullOrWhiteSpace(volumeRoot))
            {
                return true;
            }

            var currentRootSegment = volumeRoot;
            foreach (var segment in root[volumeRoot.Length..].Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                currentRootSegment = Path.Combine(currentRootSegment, segment);
                var ancestor = new DirectoryInfo(currentRootSegment);
                ancestor.Refresh();
                if (!ancestor.Exists
                    || ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || !string.IsNullOrEmpty(ancestor.LinkTarget))
                {
                    return true;
                }
            }

            var rootEntry = new DirectoryInfo(root);
            rootEntry.Refresh();
            if (!rootEntry.Exists
                || rootEntry.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || !string.IsNullOrEmpty(rootEntry.LinkTarget))
            {
                return true;
            }

            var relative = Path.GetRelativePath(root, fullPath);
            var current = root;
            foreach (var segment in relative.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                FileSystemInfo entry = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);
                entry.Refresh();
                if (entry.Exists
                    && (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
                        || !string.IsNullOrEmpty(entry.LinkTarget)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static ZaEditSessionValidation CreateUnsupportedMixedValidation(EditSession session)
    {
        return new ZaEditSessionValidation(session, IsValid: false, [CreateMixedDiagnostic()]);
    }

    private static bool IsMixedAlphaMoveSession(EditSession session)
    {
        return session.PendingEdits.Any(ZaPokemonEditSessionService.IsAlphaMoveEdit)
            && session.PendingEdits.Any(edit => !string.Equals(
                edit.Domain,
                ZaEditSessionSupport.PokemonDomain,
                StringComparison.Ordinal));
    }

    private static ZaEditSessionValidation CreateAlphaMoveMixedValidation(EditSession session)
    {
        return new ZaEditSessionValidation(
            session,
            IsValid: false,
            [CreateAlphaMoveMixedDiagnostic()]);
    }

    private static ChangePlan CreateAlphaMoveMixedChangePlan(EditSession session)
    {
        return new ChangePlan(
            session.Id,
            Array.Empty<PlannedFileWrite>(),
            [CreateAlphaMoveMixedDiagnostic()]);
    }

    private static ApplyResult CreateAlphaMoveMixedApplyResult(EditSession session)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var plan = CreateAlphaMoveMixedChangePlan(session);
        return new ApplyResult(
            applyId,
            appliedAt,
            Array.Empty<ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, plan.Writes),
            plan.Diagnostics);
    }

    private static ValidationDiagnostic CreateAlphaMoveMixedDiagnostic()
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Alpha-exclusive move changes must be applied separately from other editor domains.",
            ZaEditSessionSupport.PokemonDomain,
            field: ZaPokemonWorkflowService.AlphaMoveField,
            expected: "An alpha-exclusive move-only Pokemon editor session",
            code: ZaPokemonDiagnosticCodes.AlphaSessionConflict);
    }

    private static bool IsMixedScopedDexLayoutSession(EditSession session)
    {
        return session.PendingEdits.Count > 1
            && session.PendingEdits.Any(ZaPokemonEditSessionService.IsScopedDexLayoutEdit);
    }

    private static ZaEditSessionValidation CreateScopedDexLayoutMixedValidation(EditSession session)
    {
        return new ZaEditSessionValidation(
            session,
            IsValid: false,
            [CreateScopedDexLayoutMixedDiagnostic()]);
    }

    private static ChangePlan CreateScopedDexLayoutMixedChangePlan(EditSession session)
    {
        return new ChangePlan(
            session.Id,
            Array.Empty<PlannedFileWrite>(),
            [CreateScopedDexLayoutMixedDiagnostic()]);
    }

    private static ApplyResult CreateScopedDexLayoutMixedApplyResult(EditSession session)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var plan = CreateScopedDexLayoutMixedChangePlan(session);
        return new ApplyResult(
            applyId,
            appliedAt,
            Array.Empty<ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, plan.Writes),
            plan.Diagnostics);
    }

    private static ValidationDiagnostic CreateScopedDexLayoutMixedDiagnostic()
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Dex Layout pending changes cannot share an edit session with other changes.",
            ZaEditSessionSupport.PokemonDomain,
            field: ZaPokemonWorkflowService.DexPlacementField,
            expected: "A Dex Layout-only edit session");
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
        TrainerPools,
        FashionCatalog,
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
