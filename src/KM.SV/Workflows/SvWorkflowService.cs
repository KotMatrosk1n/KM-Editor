// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.SV;
using KM.SV.Data;
using KM.SV.DumpImport;
using KM.SV.Encounters;
using KM.SV.FashionUnlock;
using KM.SV.Gifts;
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

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvCacheManager cacheManager;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvItemsWorkflowService itemsWorkflowService;
    private readonly SvMovesWorkflowService movesWorkflowService;
    private readonly SvTextWorkflowService textWorkflowService;
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
        itemsWorkflowService = new SvItemsWorkflowService(fileSource);
        movesWorkflowService = new SvMovesWorkflowService(fileSource);
        textWorkflowService = new SvTextWorkflowService(fileSource, this.cacheManager);
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
        var semanticFileSource = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSemanticSourceBytesPerFile);
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

        long sourceBytes = 0;
        foreach (var virtualPath in boundedVirtualPaths)
        {
            AppendSemanticSourceHash(hash, virtualPath);
            AppendSemanticSourcePayload(
                hash,
                () => (semanticFileSource.ReadBaseBytesFresh(paths, virtualPath), "base"),
                ref sourceBytes);
            AppendSemanticSourcePayload(
                hash,
                () =>
                {
                    var source = semanticFileSource.ReadCurrentSourceFresh(paths, virtualPath);
                    return (source.Bytes, source.Layer.ToString());
                },
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

    private static void AppendSemanticSourcePayload(
        IncrementalHash hash,
        Func<(byte[] Bytes, string Origin)> read,
        ref long sourceBytes)
    {
        try
        {
            var payload = read();
            if (payload.Bytes.LongLength > MaximumSemanticSourceBytes - sourceBytes)
            {
                throw new InvalidDataException("The semantic source bytes exceed their bounded limit.");
            }

            sourceBytes = checked(sourceBytes + payload.Bytes.LongLength);
            AppendSemanticSourceHash(hash, payload.Origin);
            AppendSemanticSourceHash(hash, payload.Bytes.Length.ToString(CultureInfo.InvariantCulture));
            AppendSemanticSourceHash(hash, Convert.ToHexStringLower(SHA256.HashData(payload.Bytes)));
        }
        catch (ProjectFileOperationException exception) when (IsMissingSource(exception))
        {
            AppendSemanticSourceHash(hash, "missing");
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
        string value)
    {
        return staticEncountersEditSessionService.UpdateField(paths, session, encounterIndex, field, value);
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
        lock (SvWorkflowFileSource.OutputWriteSyncRoot)
        {
            projectWorkspaceService.ClearMemoryCache();
            var domain = GetDomain(session);
            return domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
                ? CreateNormalDomainChangePlan(paths, session, domains, outputMode)
                : CreateSingleDomainChangePlan(paths, session, domain, outputMode);
        }
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan changePlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        try
        {
            lock (SvWorkflowFileSource.OutputWriteSyncRoot)
            {
                projectWorkspaceService.ClearMemoryCache();
                var domain = GetDomain(session);
                return domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
                    ? ApplyNormalDomainChangePlan(paths, session, changePlan, domains, outputMode)
                    : ApplySingleDomainChangePlan(paths, session, changePlan, domain, outputMode);
            }
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
            lock (SvWorkflowFileSource.OutputWriteSyncRoot)
            {
                return ApplyNormalDomainChangePlanCore(
                    paths,
                    session,
                    reviewedPlan,
                    domains,
                    outputMode);
            }
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

        var snapshots = CaptureNormalDomainOutputSnapshots(paths, currentPlan.Writes, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
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
                    RestoreNormalDomainOutputSnapshots(snapshots, diagnostics);
                    writtenFiles.Clear();
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Scarlet/Violet mixed change plan could not be applied: {exception.Message}",
                "sv.editor",
                expected: "Readable sources and writable output targets"));
            RestoreNormalDomainOutputSnapshots(snapshots, diagnostics);
            writtenFiles.Clear();
        }

        return SvEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
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
            SvEditSessionDomain.GiftPokemon or
            SvEditSessionDomain.TradePokemon or
            SvEditSessionDomain.Placement;
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

    private static IReadOnlyList<NormalDomainOutputSnapshot> CaptureNormalDomainOutputSnapshots(
        ProjectPaths paths,
        IReadOnlyList<PlannedFileWrite> writes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            return writes
                .Select(write => ResolvePlannedOutputPath(paths, write.TargetRelativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => File.Exists(path)
                    ? new NormalDomainOutputSnapshot(
                        path,
                        Existed: true,
                        File.ReadAllBytes(path),
                        File.GetLastWriteTimeUtc(path))
                    : new NormalDomainOutputSnapshot(
                        path,
                        Existed: false,
                        Contents: null,
                        LastWriteTimeUtc: null))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Scarlet/Violet output rollback state could not be prepared: {exception.Message}",
                "sv.editor",
                expected: "Readable and writable output targets"));
            return [];
        }
    }

    private static void RestoreNormalDomainOutputSnapshots(
        IReadOnlyList<NormalDomainOutputSnapshot> snapshots,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var snapshot in snapshots.Reverse())
        {
            try
            {
                if (snapshot.Existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Path)!);
                    File.WriteAllBytes(snapshot.Path, snapshot.Contents!);
                    File.SetLastWriteTimeUtc(snapshot.Path, snapshot.LastWriteTimeUtc!.Value);
                }
                else if (File.Exists(snapshot.Path))
                {
                    File.Delete(snapshot.Path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Scarlet/Violet output rollback could not restore a target: {exception.Message}",
                    "sv.editor",
                    expected: "Original output state"));
            }
        }
    }

    private static string ResolvePlannedOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Set an output root before applying Scarlet/Violet edits.");
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            throw new InvalidOperationException("Scarlet/Violet output targets must be relative paths.");
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(outputRoot, targetPath)))
        {
            throw new InvalidOperationException("Scarlet/Violet output target escapes the output root.");
        }

        return targetPath;
    }

    private sealed record NormalDomainOutputSnapshot(
        string Path,
        bool Existed,
        byte[]? Contents,
        DateTime? LastWriteTimeUtc);

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
        GiftPokemon,
        TradePokemon,
        Placement,
        TypeChart,
        FashionUnlock,
        HyperspaceBypass,
        Mixed,
    }
}
