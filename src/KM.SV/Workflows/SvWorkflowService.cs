// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.ExceptionServices;
using KM.Core.Concurrency;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.SV;
using KM.Formats.SV.Habitat;
using KM.SV.Data;
using KM.SV.DumpImport;
using KM.SV.Encounters;
using KM.SV.FashionUnlock;
using KM.SV.Gifts;
using KM.SV.GameModules;
using KM.SV.HabitatCoordinates;
using KM.SV.HyperspaceBypass;
using KM.SV.Items;
using KM.SV.ModMerger;
using KM.SV.Moves;
using KM.SV.Placement;
using KM.SV.Pokemon;
using KM.SV.Raids;
using KM.SV.Shops;
using KM.SV.StaticEncounters;
using KM.SV.Text;
using KM.SV.TmMachine;
using KM.SV.Trainers;
using KM.SV.Trades;
using KM.SV.TypeChart;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SV.Workflows;

public sealed class SvWorkflowService
{
    private const int MaximumSemanticSourceFiles = 128;
    private const int MaximumSemanticSourceBytesPerFile = 64 * 1024 * 1024;
    private const long MaximumSemanticSourceBytes = 512L * 1024L * 1024L;
    private const int MaximumSemanticFingerprintParallelism = 8;
    private const long EstimatedSemanticFingerprintWorkerBytes = 256L * 1024L * 1024L;
    private static readonly BoundedConcurrencyPolicy SemanticFingerprintPolicy = new(
        "sv-semantic-source-fingerprint",
        BoundedWorkloadKind.Hash,
        EstimatedSemanticFingerprintWorkerBytes,
        MaximumSemanticFingerprintParallelism,
        memoryBudgetDivisor: 8,
        degreeOfParallelismWhenMemoryUnknown: 4);

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvCacheManager cacheManager;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvItemsWorkflowService itemsWorkflowService;
    private readonly SvMovesWorkflowService movesWorkflowService;
    private readonly SvTextWorkflowService textWorkflowService;
    private readonly SvTmMachineControlsWorkflowService tmMachineControlsWorkflowService;
    private readonly SvHabitatCoordinatesWorkflowService habitatCoordinatesWorkflowService;
    private readonly SvPokemonWorkflowService pokemonWorkflowService;
    private readonly SvTrainersWorkflowService trainersWorkflowService;
    private readonly SvEncountersWorkflowService encountersWorkflowService;
    private readonly SvTeraRaidsWorkflowService teraRaidsWorkflowService;
    private readonly SvStaticEncountersWorkflowService staticEncountersWorkflowService;
    private readonly SvShopsWorkflowService shopsWorkflowService;
    private readonly SvGiftPokemonWorkflowService giftPokemonWorkflowService;
    private readonly SvTradePokemonWorkflowService tradePokemonWorkflowService;
    private readonly SvPlacementWorkflowService placementWorkflowService;
    private readonly SvTypeChartWorkflowService typeChartWorkflowService;
    private readonly SvFashionUnlockWorkflowService fashionUnlockWorkflowService;
    private readonly SvHyperspaceBypassWorkflowService hyperspaceBypassWorkflowService;
    private readonly SvDumpImportWorkflowService dumpImportWorkflowService;
    private readonly SvDumpImportExecutionService dumpImportExecutionService;
    private readonly SvModMergerWorkflowService modMergerWorkflowService;
    private readonly SvItemsEditSessionService itemsEditSessionService;
    private readonly SvMovesEditSessionService movesEditSessionService;
    private readonly SvTextEditSessionService textEditSessionService;
    private readonly SvTmMachineControlsEditSessionService tmMachineControlsEditSessionService;
    private readonly SvHabitatCoordinatesEditSessionService habitatCoordinatesEditSessionService;
    private readonly SvPokemonEditSessionService pokemonEditSessionService;
    private readonly SvTrainersEditSessionService trainersEditSessionService;
    private readonly SvEncountersEditSessionService encountersEditSessionService;
    private readonly SvTeraRaidsEditSessionService teraRaidsEditSessionService;
    private readonly SvStaticEncountersEditSessionService staticEncountersEditSessionService;
    private readonly SvShopsEditSessionService shopsEditSessionService;
    private readonly SvGiftPokemonEditSessionService giftPokemonEditSessionService;
    private readonly SvTradePokemonEditSessionService tradePokemonEditSessionService;
    private readonly SvPlacementEditSessionService placementEditSessionService;
    private readonly SvTypeChartEditSessionService typeChartEditSessionService;
    private readonly SvFashionUnlockEditSessionService fashionUnlockEditSessionService;
    private readonly SvHyperspaceBypassEditSessionService hyperspaceBypassEditSessionService;

    public SvWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvCacheManager? cacheManager = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.cacheManager = cacheManager ?? new SvCacheManager();
        fileSource = new SvWorkflowFileSource(this.cacheManager);
        var habitatFileSource = new SvWorkflowFileSource(
            this.cacheManager,
            bypassReusableBaseCache: true,
            maximumReadBytes: SvHabitatDistributionDocument.MaximumSourceBytes);
        itemsWorkflowService = new SvItemsWorkflowService(fileSource);
        movesWorkflowService = new SvMovesWorkflowService(fileSource);
        textWorkflowService = new SvTextWorkflowService(fileSource, this.cacheManager);
        tmMachineControlsWorkflowService = new SvTmMachineControlsWorkflowService(fileSource);
        habitatCoordinatesWorkflowService = new SvHabitatCoordinatesWorkflowService(
            habitatFileSource,
            fileSource);
        pokemonWorkflowService = new SvPokemonWorkflowService(fileSource);
        trainersWorkflowService = new SvTrainersWorkflowService(fileSource);
        encountersWorkflowService = new SvEncountersWorkflowService(fileSource);
        teraRaidsWorkflowService = new SvTeraRaidsWorkflowService(fileSource);
        placementWorkflowService = new SvPlacementWorkflowService(fileSource);
        staticEncountersWorkflowService = new SvStaticEncountersWorkflowService(placementWorkflowService);
        shopsWorkflowService = new SvShopsWorkflowService(fileSource);
        giftPokemonWorkflowService = new SvGiftPokemonWorkflowService(fileSource);
        tradePokemonWorkflowService = new SvTradePokemonWorkflowService(fileSource);
        typeChartWorkflowService = new SvTypeChartWorkflowService();
        fashionUnlockWorkflowService = new SvFashionUnlockWorkflowService();
        hyperspaceBypassWorkflowService = new SvHyperspaceBypassWorkflowService();
        dumpImportWorkflowService = new SvDumpImportWorkflowService(itemsWorkflowService);
        modMergerWorkflowService = new SvModMergerWorkflowService(this.projectWorkspaceService, this.cacheManager);
        var editFileSource = new SvWorkflowFileSource(
            this.cacheManager,
            bypassReusableBaseCache: true);
        var editItemsWorkflowService = new SvItemsWorkflowService(editFileSource);
        var editMovesWorkflowService = new SvMovesWorkflowService(editFileSource);
        var editTextWorkflowService = new SvTextWorkflowService(editFileSource);
        var editTmMachineControlsWorkflowService = new SvTmMachineControlsWorkflowService(editFileSource);
        var editPokemonWorkflowService = new SvPokemonWorkflowService(editFileSource);
        var editTrainersWorkflowService = new SvTrainersWorkflowService(editFileSource);
        var editEncountersWorkflowService = new SvEncountersWorkflowService(editFileSource);
        var editTeraRaidsWorkflowService = new SvTeraRaidsWorkflowService(editFileSource);
        var editPlacementWorkflowService = new SvPlacementWorkflowService(editFileSource);
        var editStaticEncountersWorkflowService = new SvStaticEncountersWorkflowService(
            editPlacementWorkflowService);
        var editShopsWorkflowService = new SvShopsWorkflowService(editFileSource);
        var editGiftPokemonWorkflowService = new SvGiftPokemonWorkflowService(editFileSource);
        var editTradePokemonWorkflowService = new SvTradePokemonWorkflowService(editFileSource);
        itemsEditSessionService = new SvItemsEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editItemsWorkflowService);
        movesEditSessionService = new SvMovesEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editMovesWorkflowService);
        textEditSessionService = new SvTextEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editTextWorkflowService);
        tmMachineControlsEditSessionService = new SvTmMachineControlsEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editTmMachineControlsWorkflowService);
        habitatCoordinatesEditSessionService = new SvHabitatCoordinatesEditSessionService(
            this.projectWorkspaceService,
            habitatFileSource,
            habitatCoordinatesWorkflowService);
        pokemonEditSessionService = new SvPokemonEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editPokemonWorkflowService);
        trainersEditSessionService = new SvTrainersEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editTrainersWorkflowService);
        encountersEditSessionService = new SvEncountersEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editEncountersWorkflowService);
        teraRaidsEditSessionService = new SvTeraRaidsEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editTeraRaidsWorkflowService);
        staticEncountersEditSessionService = new SvStaticEncountersEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editStaticEncountersWorkflowService,
            editPlacementWorkflowService);
        shopsEditSessionService = new SvShopsEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editShopsWorkflowService);
        giftPokemonEditSessionService = new SvGiftPokemonEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editGiftPokemonWorkflowService);
        tradePokemonEditSessionService = new SvTradePokemonEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editTradePokemonWorkflowService);
        placementEditSessionService = new SvPlacementEditSessionService(
            this.projectWorkspaceService,
            editFileSource,
            editPlacementWorkflowService);
        typeChartEditSessionService = new SvTypeChartEditSessionService(
            this.projectWorkspaceService,
            typeChartWorkflowService);
        fashionUnlockEditSessionService = new SvFashionUnlockEditSessionService(
            this.projectWorkspaceService,
            fashionUnlockWorkflowService);
        hyperspaceBypassEditSessionService = new SvHyperspaceBypassEditSessionService(
            this.projectWorkspaceService,
            hyperspaceBypassWorkflowService);
        dumpImportExecutionService = new SvDumpImportExecutionService(
            this.projectWorkspaceService,
            itemsWorkflowService,
            itemsEditSessionService,
            dumpImportWorkflowService);
    }

    public SvCacheStatus GetCacheStatus(ProjectPaths? paths = null)
    {
        return cacheManager.GetStatus(paths);
    }

    public SvCacheStatus UpdateCacheSettings(
        SvCacheMode mode,
        long maxCacheSizeBytes,
        ProjectPaths? activePaths = null)
    {
        cacheManager.UpdateSettings(mode, maxCacheSizeBytes, activePaths);
        textWorkflowService.ClearMemoryCache();
        return cacheManager.GetStatus(activePaths);
    }

    public SvCacheStatus ClearCache(ProjectPaths? activePaths = null)
    {
        var status = cacheManager.Clear(activePaths);
        textWorkflowService.ClearMemoryCache();
        return status;
    }

    public SvCacheStatus WarmupCacheStep(ProjectPaths paths, int stepIndex)
    {
        return cacheManager.WarmupStep(paths, stepIndex);
    }

    public SvPackedLooseSourceComparison LoadPackedLooseSourceComparison(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SvPackedLooseSourceComparisonService().LoadFreshBounded(paths);
    }

    public SvEventDataComparison LoadEventDataComparison(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SvEventDataComparisonService(cacheManager).LoadFreshBounded(paths);
    }

    public SvScenePlacementProjection LoadScenePlacementProjection(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SvScenePlacementProjectionService(cacheManager).LoadFreshBounded(paths);
    }

    public SvTypeEffectivenessStateProjection LoadTypeEffectivenessStateProjection(
        ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SvTypeEffectivenessStateProjectionService().LoadFreshBounded(paths);
    }

    public void ClearMemoryCaches(bool clearReusableDataCaches = true)
    {
        projectWorkspaceService.ClearMemoryCache();
        pokemonWorkflowService.ClearMemoryCache();
        textWorkflowService.ClearMemoryCache();
        if (clearReusableDataCaches)
        {
            cacheManager.ClearMemoryCache();
        }
    }

    public string CaptureSemanticExploreSourceFingerprint(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
        return CaptureSemanticExploreSourceFingerprintLocked(paths);
    }

    /// <summary>
    /// Keeps the game-family output mutex outside any caller-owned durable output boundary.
    /// The supplied fingerprint callback is valid only while <paramref name="observation"/>
    /// is running under that mutex.
    /// </summary>
    public (string Fingerprint, string Token) ExecuteSemanticExploreSourceObservation(
        ProjectPaths paths,
        Func<Func<string>, (string Fingerprint, string Token)> observation)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(observation);
        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
        return observation(() => CaptureSemanticExploreSourceFingerprintLocked(paths));
    }

    private string CaptureSemanticExploreSourceFingerprintLocked(ProjectPaths paths)
    {
        var language = SvGameTextLanguage.Resolve(paths);
        var virtualPaths = new List<string>
        {
            SvDataPaths.ItemDataArray,
            SvDataPaths.PersonalArray,
            SvDataPaths.EvolutionItemConversionArray,
            SvDataPaths.MoveDataArray,
            SvDataPaths.TrainerDataArray,
            SvDataPaths.WildEncounterArray,
            SvWorkflowFileSource.DescriptorVirtualPath,
            SvDataPaths.ItemNames(language),
            SvDataPaths.MoveNames(language),
            SvDataPaths.MoveDescriptions(language),
            SvDataPaths.PokemonNames(language),
            SvDataPaths.AbilityNames(language),
            SvDataPaths.PlaceNames(language),
            SvDataPaths.PlaceNameKeys(language),
            SvDataPaths.TrainerNames(language),
            SvDataPaths.TrainerNameKeys(language),
            SvDataPaths.TrainerTypes(language),
            SvDataPaths.TrainerTypeKeys(language),
            SvDataPaths.ItemNames(SvGameTextLanguage.English),
            SvDataPaths.MoveNames(SvGameTextLanguage.English),
            SvDataPaths.MoveDescriptions(SvGameTextLanguage.English),
            SvDataPaths.PokemonNames(SvGameTextLanguage.English),
            SvDataPaths.AbilityNames(SvGameTextLanguage.English),
            SvDataPaths.PlaceNames(SvGameTextLanguage.English),
            SvDataPaths.PlaceNameKeys(SvGameTextLanguage.English),
            SvDataPaths.TrainerNames(SvGameTextLanguage.English),
            SvDataPaths.TrainerNameKeys(SvGameTextLanguage.English),
            SvDataPaths.TrainerTypes(SvGameTextLanguage.English),
            SvDataPaths.TrainerTypeKeys(SvGameTextLanguage.English),
        };
        virtualPaths.AddRange(SvTeraRaidsWorkflowService.EnemySourceDefinitions
            .Select(definition => definition.VirtualPath));
        virtualPaths.Add(SvDataPaths.TeraRaidFixedRewardItemArray);
        virtualPaths.Add(SvDataPaths.TeraRaidLotteryRewardItemArray);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendSemanticSourceHash(hash, "sv-semantic-source-v4");
        AppendSemanticSourceHash(hash, SemanticProjectBuildIdentity.Capture(paths));
        if (SvCompressionRuntime.TryResolveRequiredFilePath(
                paths.ScarletVioletSupportFolderPath,
                out var supportRuntimePath))
        {
            AppendSemanticSourceHash(hash, "support-runtime-present");
            AppendSemanticSourceHash(
                hash,
                SemanticProjectBuildIdentity.CaptureBoundedFile(
                    supportRuntimePath,
                    "sv-compression-runtime",
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

    public SvItemsWorkflow LoadSemanticExploreItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new SvItemsWorkflowService(freshFileSource).Load(project);
    }

    public bool CanLoadSemanticExploreCorporaConcurrently =>
        !SvWorkflowFileSource.HasActiveDeferredOutputBatch;

    public (
        Func<SvItemsWorkflow> Items,
        Func<SvPokemonWorkflow> Pokemon,
        Func<SvMovesWorkflow> Moves) PrepareSemanticExploreCorpora(
            ProjectPaths paths,
            int maximumParallelism)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelism);

        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var effectiveParallelism = SvWorkflowFileSource.HasActiveDeferredOutputBatch
            ? 1
            : Math.Clamp(maximumParallelism, 1, 3);
        var readerCount = effectiveParallelism > 1 ? 3 : 1;
        CapturedSemanticWorkflow<SvItemsWorkflow>? items = null;
        CapturedSemanticWorkflow<SvPokemonWorkflow>? pokemon = null;
        CapturedSemanticWorkflow<SvMovesWorkflow>? moves = null;

        using (var readerPool = SvWorkflowFileSource.CreateFreshSemanticReaderPool(
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
                            () => new SvItemsWorkflowService(source).Load(project));
                        break;
                    case 1:
                        pokemon = CaptureSemanticWorkflow(
                            () => new SvPokemonWorkflowService(source).Load(project));
                        break;
                    case 2:
                        moves = CaptureSemanticWorkflow(
                            () => new SvMovesWorkflowService(source).Load(project));
                        break;
                }
            }

            _ = BoundedParallel.For(
                3,
                CreateSourceLoadPolicy("sv-semantic-corpus-source-load", effectiveParallelism),
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
        Func<SvTrainersWorkflow> Trainers,
        Func<SvEncountersWorkflow> Encounters,
        Func<SvItemsWorkflow> Items,
        Func<SvPokemonWorkflow> Pokemon) PrepareGuidedDesignSources(
            ProjectPaths paths,
            int maximumParallelism)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelism);

        const int sourceCount = 4;
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var effectiveParallelism = SvWorkflowFileSource.HasActiveDeferredOutputBatch
            ? 1
            : Math.Clamp(maximumParallelism, 1, sourceCount);
        var readerCount = effectiveParallelism > 1 ? sourceCount : 1;
        CapturedSemanticWorkflow<SvTrainersWorkflow>? trainers = null;
        CapturedSemanticWorkflow<SvEncountersWorkflow>? encounters = null;
        CapturedSemanticWorkflow<SvItemsWorkflow>? items = null;
        CapturedSemanticWorkflow<SvPokemonWorkflow>? pokemon = null;
        var fatalFailures = new ExceptionDispatchInfo?[sourceCount];

        using (var readerPool = SvWorkflowFileSource.CreateFreshSemanticReaderPool(
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
                                () => new SvTrainersWorkflowService(source).Load(project));
                            break;
                        case 1:
                            encounters = CaptureSemanticWorkflow(
                                () => new SvEncountersWorkflowService(source).Load(project));
                            break;
                        case 2:
                            items = CaptureSemanticWorkflow(
                                () => new SvItemsWorkflowService(source).Load(project));
                            break;
                        case 3:
                            pokemon = CaptureSemanticWorkflow(
                                () => new SvPokemonWorkflowService(source).Load(project));
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
                CreateSourceLoadPolicy("sv-guided-design-source-load", effectiveParallelism),
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

    public SvPokemonWorkflow LoadSemanticExplorePokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new SvPokemonWorkflowService(freshFileSource).Load(project);
    }

    public SvMovesWorkflow LoadSemanticExploreMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new SvMovesWorkflowService(freshFileSource).Load(project);
    }

    public SvTrainersWorkflow LoadBalanceLabTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new SvTrainersWorkflowService(freshFileSource).Load(project);
    }

    public SvEncountersWorkflow LoadBalanceLabEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return new SvEncountersWorkflowService(freshFileSource).Load(project);
    }

    public SvTeraRaidsWorkflow LoadGameModuleTeraRaids(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var freshFileSource = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile,
            MaximumSemanticSourceFiles,
            MaximumSemanticSourceBytes);
        return new SvTeraRaidsWorkflowService(freshFileSource).LoadGameModuleReadOnly(project);
    }

    public SvItemsEditResult UpdateItemFieldsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvItemFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new SvItemsWorkflowService(source);
        return new SvItemsEditSessionService(workspace, source, workflow)
            .UpdateFields(paths, session, updates);
    }

    public SvPokemonEditResult UpdatePokemonFieldsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvPokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new SvPokemonWorkflowService(source);
        return new SvPokemonEditSessionService(workspace, source, workflow)
            .UpdateFields(paths, session, updates);
    }

    public SvTrainersEditResult UpdateTrainerFieldsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvTrainerFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new SvTrainersWorkflowService(source);
        return new SvTrainersEditSessionService(workspace, source, workflow)
            .UpdateFields(paths, session, updates);
    }

    public SvPokemonEditResult ReadPokemonEffectiveFreshBounded(
        ProjectPaths paths,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var workspace = new ProjectWorkspaceService();
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new SvPokemonWorkflowService(source);
        return new SvPokemonEditSessionService(workspace, source, workflow)
            .ReadEffective(paths, session);
    }

    public SvPokemonEditResult UpdatePokemonLearnsetFreshBounded(
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
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new SvPokemonWorkflowService(source);
        return new SvPokemonEditSessionService(workspace, source, workflow)
            .UpdateLearnset(paths, session, personalId, action, slot, moveId, level);
    }

    public SvEncountersEditResult UpdateEncounterSlotFieldsFreshBounded(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvEncounterSlotFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);
        var workspace = new ProjectWorkspaceService();
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        var workflow = new SvEncountersWorkflowService(source);
        return new SvEncountersEditSessionService(workspace, source, workflow)
            .UpdateSlotFields(paths, session, updates);
    }

    public ChangePlan CreateGuidedChangePlanFreshBounded(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        var domain = session.PendingEdits
            .Select(edit => edit.Domain)
            .Distinct(StringComparer.Ordinal)
            .SingleOrDefault();
        var workspace = new ProjectWorkspaceService();
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
        return domain switch
        {
            SvEditSessionSupport.ItemsDomain => new SvItemsEditSessionService(
                    workspace,
                    source,
                    new SvItemsWorkflowService(source))
                .CreateChangePlan(paths, session, outputMode),
            SvEditSessionSupport.PokemonDomain => new SvPokemonEditSessionService(
                    workspace,
                    source,
                    new SvPokemonWorkflowService(source))
                .CreateChangePlan(paths, session, outputMode),
            SvEditSessionSupport.TrainersDomain => new SvTrainersEditSessionService(
                    workspace,
                    source,
                    new SvTrainersWorkflowService(source))
                .CreateChangePlan(paths, session, outputMode),
            SvEditSessionSupport.EncountersDomain => new SvEncountersEditSessionService(
                    workspace,
                    source,
                    new SvEncountersWorkflowService(source))
                .CreateChangePlan(paths, session, outputMode),
            _ => throw new InvalidOperationException(
                "Generated review supports exactly one verified Scarlet/Violet workflow domain per plan."),
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
        var readerCount = SvWorkflowFileSource.HasActiveDeferredOutputBatch
            ? 1
            : Math.Min(maximumParallelism, Math.Max(1, virtualPaths.Count));
        using var readerPool = SvWorkflowFileSource.CreateFreshSemanticReaderPool(
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
        AppendSemanticSourceHash(hash, observation.Length.ToString(CultureInfo.InvariantCulture));
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
        hash.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture)));
        hash.AppendData("\n"u8);
        hash.AppendData(bytes);
        hash.AppendData("\n"u8);
    }

    public SvWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame))
        {
            return new SvWorkflowList([]);
        }

        var project = projectWorkspaceService.Open(paths);
        return new SvWorkflowList(
        [
            itemsWorkflowService.CreateSummary(project),
            movesWorkflowService.CreateSummary(project),
            textWorkflowService.CreateSummary(project),
            pokemonWorkflowService.CreateSummary(project),
            trainersWorkflowService.CreateSummary(project),
            encountersWorkflowService.CreateSummary(project),
            teraRaidsWorkflowService.CreateSummary(project),
            staticEncountersWorkflowService.CreateSummary(project),
            shopsWorkflowService.CreateSummary(project),
            tmMachineControlsWorkflowService.CreateSummary(project),
            habitatCoordinatesWorkflowService.CreateSummary(project),
            giftPokemonWorkflowService.CreateSummary(project),
            tradePokemonWorkflowService.CreateSummary(project),
            placementWorkflowService.CreateSummary(project),
            typeChartWorkflowService.CreateSummary(project),
            fashionUnlockWorkflowService.CreateSummary(project),
            hyperspaceBypassWorkflowService.CreateSummary(project),
            dumpImportWorkflowService.CreateSummary(project),
            modMergerWorkflowService.CreateSummary(project),
        ]);
    }

    public SvItemsWorkflow LoadItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return itemsWorkflowService.Load(project);
    }

    public SvMovesWorkflow LoadMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return movesWorkflowService.Load(project);
    }

    public SvTextWorkflow LoadText(ProjectPaths paths, SvTextWorkflowQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return textWorkflowService.Load(project, query);
    }

    public SvPokemonWorkflow LoadPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return pokemonWorkflowService.Load(project);
    }

    public SvTrainersWorkflow LoadTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return trainersWorkflowService.Load(project);
    }

    public SvEncountersWorkflow LoadEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return encountersWorkflowService.Load(project);
    }

    public SvTeraRaidsWorkflow LoadTeraRaids(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return teraRaidsWorkflowService.Load(project);
    }

    public SvStaticEncountersWorkflow LoadStaticEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return staticEncountersWorkflowService.Load(project);
    }

    public SvShopsWorkflow LoadShops(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return shopsWorkflowService.Load(project);
    }

    public SvTmMachineControlsWorkflow LoadTmMachineControls(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return tmMachineControlsWorkflowService.Load(project);
    }

    public SvHabitatCoordinatesWorkflow LoadHabitatCoordinates(
        ProjectPaths paths,
        SvHabitatCoordinatesQuery? query = null,
        EditSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return habitatCoordinatesWorkflowService.Load(project, query, session);
    }

    public SvGiftPokemonWorkflow LoadGiftPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return giftPokemonWorkflowService.Load(project);
    }

    public SvTradePokemonWorkflow LoadTradePokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return tradePokemonWorkflowService.Load(project);
    }

    public SvPlacementWorkflow LoadPlacement(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return placementWorkflowService.Load(project);
    }

    public SvHyperspaceBypassWorkflow LoadHyperspaceBypass(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return hyperspaceBypassWorkflowService.Load(project);
    }

    public SvFashionUnlockWorkflow LoadFashionUnlock(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return fashionUnlockWorkflowService.Load(project);
    }

    public SvTypeChartWorkflow LoadTypeChart(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return typeChartWorkflowService.Load(project);
    }

    public SvDumpImportWorkflow LoadDumpImport(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return dumpImportWorkflowService.Load(project);
    }

    public SvDumpImportExecutionResult PreviewDumpImport(
        ProjectPaths paths,
        string profileId,
        string sourcePath,
        EditSession? session)
    {
        return dumpImportExecutionService.Preview(paths, profileId, sourcePath, session);
    }

    public SvModMergerWorkflow LoadModMerger(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        return modMergerWorkflowService.Load(paths, modSources);
    }

    public SvModMergerStageResult StageModMerge(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        return modMergerWorkflowService.Stage(paths, modSources);
    }

    public SvModMergerApplyResult ApplyModMerge(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        return modMergerWorkflowService.Apply(paths, modSources);
    }

    public SvItemsEditResult UpdateItemField(
        ProjectPaths paths,
        EditSession? session,
        int itemId,
        string field,
        string value)
    {
        return itemsEditSessionService.UpdateField(paths, session, itemId, field, value);
    }

    public SvItemsEditResult UpdateItemFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvItemFieldUpdate> updates)
    {
        return itemsEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvMovesEditResult UpdateMoveField(
        ProjectPaths paths,
        EditSession? session,
        int moveId,
        string field,
        string value)
    {
        return movesEditSessionService.UpdateField(paths, session, moveId, field, value);
    }

    public SvMovesEditResult UpdateMoveFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvMoveFieldUpdate> updates)
    {
        return movesEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvTextEditResult UpdateTextEntry(
        ProjectPaths paths,
        EditSession? session,
        string textKey,
        string value,
        SvTextWorkflowQuery? query = null)
    {
        return textEditSessionService.UpdateEntry(paths, session, textKey, value, query);
    }

    public SvPokemonEditResult UpdatePokemonField(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string field,
        string value)
    {
        return pokemonEditSessionService.UpdateField(paths, session, personalId, field, value);
    }

    public SvPokemonEditResult UpdatePokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvPokemonFieldUpdate> updates)
    {
        return pokemonEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvPokemonEditResult UpdatePokemonComposite(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvPokemonFieldUpdate> fieldUpdates,
        IReadOnlyList<SvPokemonEvolutionUpdate> evolutionUpdates,
        IReadOnlyList<SvPokemonLearnsetUpdate> learnsetUpdates)
    {
        return pokemonEditSessionService.UpdateComposite(
            paths,
            session,
            fieldUpdates,
            evolutionUpdates,
            learnsetUpdates);
    }

    public SvPokemonEditResult UpdatePokemonLearnset(
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

    public SvPokemonEditResult UpdatePokemonEvolution(
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
        return pokemonEditSessionService.UpdateEvolution(
            paths,
            session,
            personalId,
            action,
            slot,
            method,
            argument,
            species,
            form,
            level);
    }

    public SvTrainersEditResult UpdateTrainerField(
        ProjectPaths paths,
        EditSession? session,
        int trainerId,
        int? slot,
        string field,
        string value)
    {
        return trainersEditSessionService.UpdateField(paths, session, trainerId, slot, field, value);
    }

    public SvTrainersEditResult UpdateTrainerFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvTrainerFieldUpdate> updates)
    {
        return trainersEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvEncountersEditResult UpdateEncounterSlotField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        return encountersEditSessionService.UpdateSlotField(paths, session, tableId, slot, field, value);
    }

    public SvEncountersEditResult UpdateEncounterSlotFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvEncounterSlotFieldUpdate> updates)
    {
        return encountersEditSessionService.UpdateSlotFields(paths, session, updates);
    }

    public SvTeraRaidsEditResult UpdateTeraRaidField(
        ProjectPaths paths,
        EditSession? session,
        string recordId,
        string field,
        string value)
    {
        return teraRaidsEditSessionService.UpdateField(paths, session, recordId, field, value);
    }

    public SvTeraRaidsEditResult UpdateTeraRaidFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvTeraRaidFieldUpdate> updates)
    {
        return teraRaidsEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvStaticEncountersEditResult UpdateStaticEncounterField(
        ProjectPaths paths,
        EditSession? session,
        int encounterIndex,
        string field,
        string value,
        string? expectedEncounterId = null)
    {
        return staticEncountersEditSessionService.UpdateField(
            paths,
            session,
            encounterIndex,
            field,
            value,
            expectedEncounterId);
    }

    public SvStaticEncountersEditResult UpdateStaticEncounterFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvStaticEncounterFieldUpdate> updates)
    {
        return staticEncountersEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvGiftPokemonEditResult UpdateGiftPokemonField(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex,
        string field,
        string value)
    {
        return giftPokemonEditSessionService.UpdateField(paths, session, giftIndex, field, value);
    }

    public SvGiftPokemonEditResult UpdateGiftPokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvGiftPokemonFieldUpdate> updates)
    {
        return giftPokemonEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvTradePokemonEditResult UpdateTradePokemonField(
        ProjectPaths paths,
        EditSession? session,
        int tradeIndex,
        string field,
        string value)
    {
        return tradePokemonEditSessionService.UpdateField(paths, session, tradeIndex, field, value);
    }

    public SvTradePokemonEditResult UpdateTradePokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvTradePokemonFieldUpdate> updates)
    {
        return tradePokemonEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvShopsEditResult UpdateShopInventoryItem(
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

    public SvShopsEditResult UpdateShopInventoryItems(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvShopInventoryItemUpdate?>? updates)
    {
        return shopsEditSessionService.UpdateInventoryItems(paths, session, updates);
    }

    public SvTmMachineControlsEditResult StageTmRecipeAvailability(
        ProjectPaths paths,
        EditSession? session,
        bool allAvailable)
    {
        return tmMachineControlsEditSessionService.StageRecipeAvailability(paths, session, allAvailable);
    }

    public SvTmMachineControlsEditResult StageTmMaterialVisibility(
        ProjectPaths paths,
        EditSession? session,
        bool alwaysVisible)
    {
        return tmMachineControlsEditSessionService.StageMaterialVisibility(paths, session, alwaysVisible);
    }

    public SvHabitatCoordinatesEditResult StageHabitatCoordinate(
        ProjectPaths paths,
        EditSession? session,
        SvHabitatCoordinatesQuery? query,
        string region,
        SvHabitatRowBinding binding,
        SvHabitatCoordinateChoice coordinate)
    {
        return habitatCoordinatesEditSessionService.StageCoordinate(
            paths,
            session,
            query,
            region,
            binding,
            coordinate);
    }

    public SvPlacementEditResult UpdatePlacementObjectField(
        ProjectPaths paths,
        EditSession? session,
        string objectId,
        string field,
        string value)
    {
        return placementEditSessionService.UpdateObjectField(paths, session, objectId, field, value);
    }

    public SvPlacementEditResult UpdatePlacementObjectFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvPlacementObjectFieldUpdate> updates)
    {
        return placementEditSessionService.UpdateObjectFields(paths, session, updates);
    }

    public SvHyperspaceBypassEditResult StageHyperspaceBypassInstall(
        ProjectPaths paths,
        EditSession? session)
    {
        return hyperspaceBypassEditSessionService.StageInstall(paths, session);
    }

    public SvFashionUnlockEditResult StageFashionUnlockInstall(
        ProjectPaths paths,
        EditSession? session)
    {
        return fashionUnlockEditSessionService.StageInstall(paths, session);
    }

    public SvTypeChartEditResult StageTypeChart(
        ProjectPaths paths,
        IReadOnlyList<int> values,
        EditSession? session)
    {
        return typeChartEditSessionService.StageChart(paths, values, session);
    }

    public SvTypeChartEditResult StageTypeChartUninstall(
        ProjectPaths paths,
        EditSession? session)
    {
        return typeChartEditSessionService.StageUninstall(paths, session);
    }

    public SvHyperspaceBypassEditResult StageHyperspaceBypassUninstall(
        ProjectPaths paths,
        EditSession? session)
    {
        return hyperspaceBypassEditSessionService.StageUninstall(paths, session);
    }

    public SvFashionUnlockEditResult StageFashionUnlockUninstall(
        ProjectPaths paths,
        EditSession? session)
    {
        return fashionUnlockEditSessionService.StageUninstall(paths, session);
    }

    public SvEditSessionValidation ValidateEditSession(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        using var freshReads = SvWorkflowFileSource.BeginFreshReadScope(paths);

        var domain = GetDomain(session);
        return domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? ValidateNormalDomains(paths, session, domains)
            : ValidateSingleDomain(paths, session, domain);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
        using var freshReads = SvWorkflowFileSource.BeginFreshReadScope(paths);
        projectWorkspaceService.ClearMemoryCache();
        var domain = GetDomain(session);
        return domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? CreateNormalDomainChangePlan(paths, session, domains, outputMode)
            : CreateSingleDomainChangePlan(paths, session, domain, outputMode);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan changePlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        try
        {
            using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
            using var freshReads = SvWorkflowFileSource.BeginFreshReadScope(paths);
            projectWorkspaceService.ClearMemoryCache();
            var domain = GetDomain(session);
            if (domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains))
            {
                return ApplyNormalDomainChangePlan(paths, session, changePlan, domains, outputMode);
            }

            return IsNormalDomain(domain)
                ? ApplyTransactionalSingleDomainChangePlan(paths, session, changePlan, domain, outputMode)
                : ApplySingleDomainChangePlan(paths, session, changePlan, domain, outputMode);
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
            pokemonWorkflowService.ClearMemoryCache();
            textWorkflowService.ClearMemoryCache();
        }
    }

    private SvEditSessionValidation ValidateSingleDomain(
        ProjectPaths paths,
        EditSession session,
        SvEditSessionDomain domain)
    {
        return domain switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Moves => movesEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Text => textEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Trainers => trainersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.TeraRaids => teraRaidsEditSessionService.Validate(paths, session),
            SvEditSessionDomain.StaticEncounters => staticEncountersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Shops => shopsEditSessionService.Validate(paths, session),
            SvEditSessionDomain.TmMachineControls => tmMachineControlsEditSessionService.Validate(paths, session),
            SvEditSessionDomain.HabitatCoordinates => habitatCoordinatesEditSessionService.Validate(paths, session),
            SvEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.Validate(paths, session),
            SvEditSessionDomain.TradePokemon => tradePokemonEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Placement => placementEditSessionService.Validate(paths, session),
            SvEditSessionDomain.TypeChart => typeChartEditSessionService.Validate(paths, session),
            SvEditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.Validate(paths, session),
            SvEditSessionDomain.HyperspaceBypass => hyperspaceBypassEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
            _ => itemsEditSessionService.Validate(paths, session),
        };
    }

    private ChangePlan CreateSingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvEditSessionDomain domain,
        SvOutputMode outputMode)
    {
        return domain switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Moves => movesEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Text => textEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Trainers => trainersEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.TeraRaids => teraRaidsEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.StaticEncounters => staticEncountersEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Shops => shopsEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.TmMachineControls => tmMachineControlsEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.HabitatCoordinates => habitatCoordinatesEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.TradePokemon => tradePokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Placement => placementEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.TypeChart => typeChartEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.HyperspaceBypass => hyperspaceBypassEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
            _ => itemsEditSessionService.CreateChangePlan(paths, session, outputMode),
        };
    }

    private ApplyResult ApplySingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan changePlan,
        SvEditSessionDomain domain,
        SvOutputMode outputMode)
    {
        return domain switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Moves => movesEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Text => textEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.TeraRaids => teraRaidsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.StaticEncounters => staticEncountersEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Shops => shopsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.TmMachineControls => tmMachineControlsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.HabitatCoordinates => habitatCoordinatesEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.TradePokemon => tradePokemonEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Placement => placementEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.TypeChart => typeChartEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.HyperspaceBypass => hyperspaceBypassEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
            _ => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
        };
    }

    private ApplyResult ApplyTransactionalSingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvEditSessionDomain domain,
        SvOutputMode outputMode)
    {
        ApplyResult? result = null;
        OutputApplyResult? outputTransaction = null;
        try
        {
            var context = new SvOutputApplyContext(
                OutputReviewFingerprint.FromChangePlan(reviewedPlan),
                GetSingleDomainOutputOwnerId(domain),
                [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, GetDomainName(domain))])
            {
                HistoryDetails = OutputHistoryDetails.Capture(reviewedPlan.EffectivePendingEdits ?? session.PendingEdits),
            };
            using var outputBatch = SvWorkflowFileSource.BeginDeferredOutputBatch(
                paths,
                outputMode,
                reviewedPlan,
                context);
            result = ApplySingleDomainChangePlan(
                paths,
                session,
                reviewedPlan,
                domain,
                outputMode);
            var hasErrors = result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
            if (!hasErrors && outputBatch.HasPendingMutations)
            {
                outputTransaction = outputBatch.Commit();
            }

            return result with
            {
                WrittenFiles = hasErrors || outputTransaction is null
                    ? Array.Empty<ProjectFileReference>()
                    : result.WrittenFiles,
                OutputTransaction = outputTransaction,
            };
        }
        catch (Exception exception) when (exception is OutputCoordinatorException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            if (exception is SvOutputApplyNotCommittedException notCommitted)
            {
                outputTransaction = notCommitted.Result;
            }

            var diagnostics = (result?.Diagnostics ?? reviewedPlan.Diagnostics).ToList();
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Scarlet/Violet output could not be committed: {exception.Message}",
                GetDomainName(domain),
                expected: "Current reviewed output targets and a writable output root"));

            return result is null
                ? SvEditSessionSupport.CreateApplyResult(
                    Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow,
                    reviewedPlan,
                    Array.Empty<ProjectFileReference>(),
                    diagnostics,
                    outputTransaction)
                : result with
                {
                    WrittenFiles = Array.Empty<ProjectFileReference>(),
                    Diagnostics = diagnostics,
                    OutputTransaction = outputTransaction,
                };
        }
    }

    private SvEditSessionValidation ValidateNormalDomains(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<SvEditSessionDomain> domains)
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

        return new SvEditSessionValidation(
            effectiveSession,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    private ChangePlan CreateNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<SvEditSessionDomain> domains,
        SvOutputMode outputMode)
    {
        return CreateNormalDomainChangePlanSnapshot(paths, session, domains, outputMode).CombinedPlan;
    }

    private NormalDomainChangePlanSnapshot CreateNormalDomainChangePlanSnapshot(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<SvEditSessionDomain> domains,
        SvOutputMode outputMode)
    {
        var validation = ValidateNormalDomains(paths, session, domains);
        var diagnostics = validation.Diagnostics.ToList();
        var effectiveSession = validation.Session;
        var effectiveDomains = domains
            .Where(domain => SliceSession(effectiveSession, domain).PendingEdits.Count > 0)
            .ToArray();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new NormalDomainChangePlanSnapshot(
                new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics),
                new Dictionary<SvEditSessionDomain, ChangePlan>(),
                effectiveSession,
                effectiveDomains);
        }

        if (effectiveDomains.Length == 0)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Scarlet/Violet edit before reviewing a change plan.",
                "sv.editor",
                expected: "Pending Scarlet/Violet edit"));
            return new NormalDomainChangePlanSnapshot(
                new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics),
                new Dictionary<SvEditSessionDomain, ChangePlan>(),
                effectiveSession,
                effectiveDomains);
        }

        var writes = new List<PlannedFileWrite>();
        var domainPlans = new Dictionary<SvEditSessionDomain, ChangePlan>();
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

        if (domainPlans.Values.Any(plan => plan.EffectivePendingEdits is null))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Scarlet/Violet mixed change-plan source authentication did not complete.",
                "sv.editor",
                expected: "Fresh authenticated domain plans"));
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
                EffectivePendingEdits = CreateEffectivePendingEditEvidence(effectiveSession),
            },
            domainPlans,
            effectiveSession,
            effectiveDomains);
    }

    private ApplyResult ApplyNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        IReadOnlyList<SvEditSessionDomain> domains,
        SvOutputMode outputMode)
    {
        try
        {
            using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
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
        IReadOnlyList<SvEditSessionDomain> domains,
        SvOutputMode outputMode)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentSnapshot = CreateNormalDomainChangePlanSnapshot(paths, session, domains, outputMode);
        var currentPlan = currentSnapshot.CombinedPlan;
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        OutputApplyResult? outputTransaction = null;

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                "sv.editor",
                expected: "Current reviewed Scarlet/Violet change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var context = new SvOutputApplyContext(
                OutputReviewFingerprint.FromChangePlan(currentPlan),
                new OwnershipOwnerId("workflow.sv.mixed"),
                currentSnapshot.EffectiveDomains
                    .Select(domain => new OutputApplyOrigin(
                        OutputApplyOriginKind.Workflow,
                        GetDomainName(domain)))
                    .ToArray())
            {
                HistoryDetails = OutputHistoryDetails.Capture(currentSnapshot.EffectiveSession.PendingEdits),
            };
            using var outputBatch = SvWorkflowFileSource.BeginDeferredOutputBatch(
                paths,
                outputMode,
                currentPlan,
                context);
            foreach (var domain in currentSnapshot.EffectiveDomains)
            {
                var domainSession = SliceSession(currentSnapshot.EffectiveSession, domain);
                // Earlier domains in this verified atomic batch may have created an
                // output preimage (especially the shared standalone descriptor).
                // Review the domain against that intentional in-batch state while
                // the outer combined plan remains the user-reviewed boundary.
                var domainPlan = CreateSingleDomainChangePlan(
                    paths,
                    domainSession,
                    domain,
                    outputMode);
                var result = ApplySingleDomainChangePlan(paths, domainSession, domainPlan, domain, outputMode);
                diagnostics.AddRange(result.Diagnostics);
                writtenFiles.AddRange(result.WrittenFiles);

                if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    writtenFiles.Clear();
                    break;
                }
            }

            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error)
                && outputBatch.HasPendingMutations)
            {
                outputTransaction = outputBatch.Commit();
                if (outputTransaction is null)
                {
                    writtenFiles.Clear();
                }
            }
        }
        catch (Exception exception) when (exception is OutputCoordinatorException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            if (exception is SvOutputApplyNotCommittedException notCommitted)
            {
                outputTransaction = notCommitted.Result;
            }

            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Scarlet/Violet mixed change plan could not be applied: {exception.Message}",
                "sv.editor",
                expected: "Readable sources and writable output targets"));
            writtenFiles.Clear();
        }

        return SvEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics,
            outputTransaction);
    }

    private static SvEditSessionDomain GetDomain(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return domains switch
        {
            [] => SvEditSessionDomain.None,
            [SvEditSessionSupport.ItemsDomain] => SvEditSessionDomain.Items,
            [SvEditSessionSupport.MovesDomain] => SvEditSessionDomain.Moves,
            [SvEditSessionSupport.TextDomain] => SvEditSessionDomain.Text,
            [SvEditSessionSupport.PokemonDomain] => SvEditSessionDomain.Pokemon,
            [SvEditSessionSupport.TrainersDomain] => SvEditSessionDomain.Trainers,
            [SvEditSessionSupport.EncountersDomain] => SvEditSessionDomain.Encounters,
            [SvEditSessionSupport.TeraRaidsDomain] => SvEditSessionDomain.TeraRaids,
            [SvEditSessionSupport.StaticEncountersDomain] => SvEditSessionDomain.StaticEncounters,
            [SvEditSessionSupport.ShopsDomain] => SvEditSessionDomain.Shops,
            [SvTmMachineControlsEditSessionService.EditDomain] => SvEditSessionDomain.TmMachineControls,
            [SvHabitatCoordinatesEditSessionService.EditDomain] => SvEditSessionDomain.HabitatCoordinates,
            [SvEditSessionSupport.GiftPokemonDomain] => SvEditSessionDomain.GiftPokemon,
            [SvEditSessionSupport.TradePokemonDomain] => SvEditSessionDomain.TradePokemon,
            [SvEditSessionSupport.PlacementDomain] => SvEditSessionDomain.Placement,
            [SvTypeChartEditSessionService.TypeChartEditDomain] => SvEditSessionDomain.TypeChart,
            [SvFashionUnlockEditSessionService.FashionUnlockEditDomain] => SvEditSessionDomain.FashionUnlock,
            [SvHyperspaceBypassEditSessionService.HyperspaceBypassEditDomain] => SvEditSessionDomain.HyperspaceBypass,
            _ => SvEditSessionDomain.Mixed,
        };
    }

    private static bool TryGetNormalDomains(
        EditSession session,
        out IReadOnlyList<SvEditSessionDomain> domains)
    {
        var orderedDomains = session.PendingEdits
            .Select(edit => GetDomain(edit.Domain))
            .Where(domain => domain != SvEditSessionDomain.None)
            .Distinct()
            .ToArray();

        var textIndex = Array.IndexOf(orderedDomains, SvEditSessionDomain.Text);
        if (textIndex > 0)
        {
            (orderedDomains[0], orderedDomains[textIndex]) =
                (orderedDomains[textIndex], orderedDomains[0]);
        }

        var itemsIndex = Array.IndexOf(orderedDomains, SvEditSessionDomain.Items);
        var pokemonIndex = Array.IndexOf(orderedDomains, SvEditSessionDomain.Pokemon);
        if (itemsIndex > pokemonIndex && pokemonIndex >= 0)
        {
            (orderedDomains[pokemonIndex], orderedDomains[itemsIndex]) =
                (orderedDomains[itemsIndex], orderedDomains[pokemonIndex]);
        }

        domains = orderedDomains;
        return orderedDomains.Length > 1 && orderedDomains.All(IsNormalDomain);
    }

    private static SvEditSessionDomain GetDomain(string? domain)
    {
        return domain switch
        {
            SvEditSessionSupport.ItemsDomain => SvEditSessionDomain.Items,
            SvEditSessionSupport.MovesDomain => SvEditSessionDomain.Moves,
            SvEditSessionSupport.TextDomain => SvEditSessionDomain.Text,
            SvEditSessionSupport.PokemonDomain => SvEditSessionDomain.Pokemon,
            SvEditSessionSupport.TrainersDomain => SvEditSessionDomain.Trainers,
            SvEditSessionSupport.EncountersDomain => SvEditSessionDomain.Encounters,
            SvEditSessionSupport.TeraRaidsDomain => SvEditSessionDomain.TeraRaids,
            SvEditSessionSupport.StaticEncountersDomain => SvEditSessionDomain.StaticEncounters,
            SvEditSessionSupport.ShopsDomain => SvEditSessionDomain.Shops,
            SvTmMachineControlsEditSessionService.EditDomain => SvEditSessionDomain.TmMachineControls,
            SvHabitatCoordinatesEditSessionService.EditDomain => SvEditSessionDomain.HabitatCoordinates,
            SvEditSessionSupport.GiftPokemonDomain => SvEditSessionDomain.GiftPokemon,
            SvEditSessionSupport.TradePokemonDomain => SvEditSessionDomain.TradePokemon,
            SvEditSessionSupport.PlacementDomain => SvEditSessionDomain.Placement,
            SvTypeChartEditSessionService.TypeChartEditDomain => SvEditSessionDomain.TypeChart,
            SvFashionUnlockEditSessionService.FashionUnlockEditDomain => SvEditSessionDomain.FashionUnlock,
            SvHyperspaceBypassEditSessionService.HyperspaceBypassEditDomain => SvEditSessionDomain.HyperspaceBypass,
            null or "" => SvEditSessionDomain.None,
            _ => SvEditSessionDomain.Mixed,
        };
    }

    private static bool IsNormalDomain(SvEditSessionDomain domain)
    {
        return domain is
            SvEditSessionDomain.Items or
            SvEditSessionDomain.Moves or
            SvEditSessionDomain.Text or
            SvEditSessionDomain.Pokemon or
            SvEditSessionDomain.Trainers or
            SvEditSessionDomain.Encounters or
            SvEditSessionDomain.TeraRaids or
            SvEditSessionDomain.StaticEncounters or
            SvEditSessionDomain.Shops or
            SvEditSessionDomain.TmMachineControls or
            SvEditSessionDomain.HabitatCoordinates or
            SvEditSessionDomain.GiftPokemon or
            SvEditSessionDomain.TradePokemon or
            SvEditSessionDomain.Placement;
    }

    private static OwnershipOwnerId GetSingleDomainOutputOwnerId(SvEditSessionDomain domain)
    {
        return domain switch
        {
            SvEditSessionDomain.Text => new OwnershipOwnerId("workflow.sv.text"),
            SvEditSessionDomain.TmMachineControls => new OwnershipOwnerId("workflow.sv.tm-machine-controls"),
            SvEditSessionDomain.HabitatCoordinates => new OwnershipOwnerId("workflow.sv.habitat-coordinates"),
            _ => new OwnershipOwnerId("workflow.sv.output"),
        };
    }

    private static EditSession SliceSession(EditSession session, SvEditSessionDomain domain)
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
        SvEditSessionDomain domain,
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

    private static string GetDomainName(SvEditSessionDomain domain)
    {
        return domain switch
        {
            SvEditSessionDomain.Items => SvEditSessionSupport.ItemsDomain,
            SvEditSessionDomain.Moves => SvEditSessionSupport.MovesDomain,
            SvEditSessionDomain.Text => SvEditSessionSupport.TextDomain,
            SvEditSessionDomain.Pokemon => SvEditSessionSupport.PokemonDomain,
            SvEditSessionDomain.Trainers => SvEditSessionSupport.TrainersDomain,
            SvEditSessionDomain.Encounters => SvEditSessionSupport.EncountersDomain,
            SvEditSessionDomain.TeraRaids => SvEditSessionSupport.TeraRaidsDomain,
            SvEditSessionDomain.StaticEncounters => SvEditSessionSupport.StaticEncountersDomain,
            SvEditSessionDomain.Shops => SvEditSessionSupport.ShopsDomain,
            SvEditSessionDomain.TmMachineControls => SvTmMachineControlsEditSessionService.EditDomain,
            SvEditSessionDomain.HabitatCoordinates => SvHabitatCoordinatesEditSessionService.EditDomain,
            SvEditSessionDomain.GiftPokemon => SvEditSessionSupport.GiftPokemonDomain,
            SvEditSessionDomain.TradePokemon => SvEditSessionSupport.TradePokemonDomain,
            SvEditSessionDomain.Placement => SvEditSessionSupport.PlacementDomain,
            SvEditSessionDomain.TypeChart => SvTypeChartEditSessionService.TypeChartEditDomain,
            SvEditSessionDomain.FashionUnlock => SvFashionUnlockEditSessionService.FashionUnlockEditDomain,
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<PlannedFileWrite> CombinePlannedWrites(
        IEnumerable<PlannedFileWrite> writes,
        string pendingEditFingerprint)
    {
        return writes
            .GroupBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .Select(group =>
            {
                var groupedWrites = group.ToArray();
                var combined = groupedWrites.Length == 1
                    ? groupedWrites[0]
                    : new PlannedFileWrite(
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
                        CombineFingerprintValues(groupedWrites.Select(write => write.SourceFingerprint)));
                return combined with
                {
                    SourceBindingFingerprint = CombineFingerprintValues(
                        groupedWrites.Select(write => write.SourceBindingFingerprint)),
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
                "KM.SV.CombinedChangePlan.v1\n" + string.Join('\n', fingerprints)))),
        };
    }

    private static string CreatePendingEditFingerprint(IReadOnlyList<PendingEdit> edits)
    {
        var canonical = new StringBuilder("KM.SV.PendingEdits.v1|");
        AppendFingerprintComponent(canonical, edits.Count.ToString(CultureInfo.InvariantCulture));
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
                edit.Sources.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintComponent(
                    canonical,
                    ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintComponent(canonical, source.RelativePath);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static IReadOnlyList<PendingEdit> CreateEffectivePendingEditEvidence(
        EditSession session)
    {
        return session.PendingEdits
            .Select(edit => edit.Association is null
                ? edit
                : edit with { Association = null })
            .ToArray();
    }

    private static void AppendFingerprintComponent(StringBuilder destination, string? value)
    {
        destination.Append(value?.Length ?? -1)
            .Append(':')
            .Append(value)
            .Append('|');
    }

    private sealed record NormalDomainChangePlanSnapshot(
        ChangePlan CombinedPlan,
        IReadOnlyDictionary<SvEditSessionDomain, ChangePlan> DomainPlans,
        EditSession EffectiveSession,
        IReadOnlyList<SvEditSessionDomain> EffectiveDomains);

    private static SvEditSessionValidation CreateUnsupportedMixedValidation(EditSession session)
    {
        return new SvEditSessionValidation(session, IsValid: false, [CreateMixedDiagnostic()]);
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
            Array.Empty<KM.Core.Files.ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, plan.Writes),
            plan.Diagnostics);
    }

    private static KM.Core.Diagnostics.ValidationDiagnostic CreateMixedDiagnostic()
    {
        return SvEditSessionSupport.CreateDiagnostic(
            KM.Core.Diagnostics.DiagnosticSeverity.Error,
            "Scarlet/Violet edit sessions cannot mix workflow domains in one change plan yet.",
            "sv.editor",
            expected: "Pending edits from one workflow domain");
    }

    private enum SvEditSessionDomain
    {
        None,
        Items,
        Moves,
        Text,
        Pokemon,
        Trainers,
        Encounters,
        TeraRaids,
        StaticEncounters,
        Shops,
        TmMachineControls,
        HabitatCoordinates,
        GiftPokemon,
        TradePokemon,
        Placement,
        TypeChart,
        FashionUnlock,
        HyperspaceBypass,
        Mixed,
    }
}
