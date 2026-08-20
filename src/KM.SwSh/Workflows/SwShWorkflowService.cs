// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SwSh.Behavior;
using KM.SwSh.BagHook;
using KM.SwSh.CatchCap;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.FashionUnlock;
using KM.SwSh.Flagwork;
using KM.SwSh.Gifts;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.IvScreen;
using KM.SwSh.ModMerger;
using KM.SwSh.Moves;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.ShinyRate;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.StartingItems;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.TypeChart;
using KM.SwSh.Trainers;
using KM.SwSh.Trades;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Workflows;

public sealed class SwShWorkflowService
{
    private const int MaximumSemanticSourceFiles = 20_000;
    private const long MaximumSemanticSourceBytesPerFile = 64L * 1024L * 1024L;
    private const long MaximumSemanticSourceBytes = 512L * 1024L * 1024L;
    private readonly SwShItemsWorkflowService itemsWorkflowService;
    private readonly SwShPokemonWorkflowService pokemonWorkflowService;
    private readonly SwShMovesWorkflowService movesWorkflowService;
    private readonly SwShEncountersWorkflowService encountersWorkflowService;
    private readonly SwShExeFsPatchWorkflowService exeFsPatchWorkflowService;
    private readonly SwShBagHookWorkflowService bagHookWorkflowService;
    private readonly SwShCatchCapWorkflowService catchCapWorkflowService;
    private readonly SwShHyperTrainingWorkflowService hyperTrainingWorkflowService;
    private readonly SwShFairyGymBoostsWorkflowService fairyGymBoostsWorkflowService;
    private readonly SwShGymUniformRemovalWorkflowService gymUniformRemovalWorkflowService;
    private readonly SwShFashionUnlockWorkflowService fashionUnlockWorkflowService;
    private readonly SwShIvScreenWorkflowService ivScreenWorkflowService;
    private readonly SwShShinyRateWorkflowService shinyRateWorkflowService;
    private readonly SwShTypeChartWorkflowService typeChartWorkflowService;
    private readonly SwShFlagworkSaveWorkflowService flagworkSaveWorkflowService;
    private readonly SwShGiftPokemonWorkflowService giftPokemonWorkflowService;
    private readonly SwShTradePokemonWorkflowService tradePokemonWorkflowService;
    private readonly SwShStaticEncountersWorkflowService staticEncountersWorkflowService;
    private readonly SwShRentalPokemonWorkflowService rentalPokemonWorkflowService;
    private readonly SwShDynamaxAdventuresWorkflowService dynamaxAdventuresWorkflowService;
    private readonly SwShPlacementWorkflowService placementWorkflowService;
    private readonly SwShBehaviorWorkflowService behaviorWorkflowService;
    private readonly SwShRaidBattlesWorkflowService raidBattlesWorkflowService;
    private readonly SwShRaidRewardsWorkflowService raidRewardsWorkflowService;
    private readonly SwShRoyalCandyWorkflowService royalCandyWorkflowService;
    private readonly SwShStartingItemsWorkflowService startingItemsWorkflowService;
    private readonly SwShNpcItemGiftWorkflowService npcItemGiftWorkflowService;
    private readonly SwShShopsWorkflowService shopsWorkflowService;
    private readonly SwShSpreadsheetImportWorkflowService spreadsheetImportWorkflowService;
    private readonly SwShModMergerWorkflowService modMergerWorkflowService;
    private readonly SwShTextWorkflowService textWorkflowService;
    private readonly SwShTrainersWorkflowService trainersWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShParsedDataCache parsedDataCache;
    private readonly SwShCacheManager cacheManager;
    private readonly object cacheWarmupSyncRoot = new();
    private readonly HashSet<string> warmedCacheKeys = new(StringComparer.Ordinal);
    private ProjectId? activeCacheWarmupProjectId;

    public SwShWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null,
        SwShPokemonWorkflowService? pokemonWorkflowService = null,
        SwShMovesWorkflowService? movesWorkflowService = null,
        SwShTextWorkflowService? textWorkflowService = null,
        SwShTrainersWorkflowService? trainersWorkflowService = null,
        SwShShopsWorkflowService? shopsWorkflowService = null,
        SwShEncountersWorkflowService? encountersWorkflowService = null,
        SwShRaidBattlesWorkflowService? raidBattlesWorkflowService = null,
        SwShRaidRewardsWorkflowService? raidRewardsWorkflowService = null,
        SwShPlacementWorkflowService? placementWorkflowService = null,
        SwShBehaviorWorkflowService? behaviorWorkflowService = null,
        SwShFlagworkSaveWorkflowService? flagworkSaveWorkflowService = null,
        SwShGiftPokemonWorkflowService? giftPokemonWorkflowService = null,
        SwShTradePokemonWorkflowService? tradePokemonWorkflowService = null,
        SwShStaticEncountersWorkflowService? staticEncountersWorkflowService = null,
        SwShRentalPokemonWorkflowService? rentalPokemonWorkflowService = null,
        SwShDynamaxAdventuresWorkflowService? dynamaxAdventuresWorkflowService = null,
        SwShExeFsPatchWorkflowService? exeFsPatchWorkflowService = null,
        SwShBagHookWorkflowService? bagHookWorkflowService = null,
        SwShCatchCapWorkflowService? catchCapWorkflowService = null,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService = null,
        SwShFairyGymBoostsWorkflowService? fairyGymBoostsWorkflowService = null,
        SwShGymUniformRemovalWorkflowService? gymUniformRemovalWorkflowService = null,
        SwShFashionUnlockWorkflowService? fashionUnlockWorkflowService = null,
        SwShIvScreenWorkflowService? ivScreenWorkflowService = null,
        SwShShinyRateWorkflowService? shinyRateWorkflowService = null,
        SwShTypeChartWorkflowService? typeChartWorkflowService = null,
        SwShRoyalCandyWorkflowService? royalCandyWorkflowService = null,
        SwShStartingItemsWorkflowService? startingItemsWorkflowService = null,
        SwShNpcItemGiftWorkflowService? npcItemGiftWorkflowService = null,
        SwShSpreadsheetImportWorkflowService? spreadsheetImportWorkflowService = null,
        SwShModMergerWorkflowService? modMergerWorkflowService = null,
        SwShParsedDataCache? parsedDataCache = null,
        SwShCacheManager? cacheManager = null)
    {
        this.parsedDataCache = parsedDataCache ?? new SwShParsedDataCache();
        this.cacheManager = cacheManager ?? new SwShCacheManager();
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
        this.pokemonWorkflowService = pokemonWorkflowService ?? new SwShPokemonWorkflowService();
        this.movesWorkflowService = movesWorkflowService ?? new SwShMovesWorkflowService();
        this.encountersWorkflowService = encountersWorkflowService ?? new SwShEncountersWorkflowService();
        this.exeFsPatchWorkflowService = exeFsPatchWorkflowService ?? new SwShExeFsPatchWorkflowService(this.parsedDataCache);
        this.bagHookWorkflowService = bagHookWorkflowService ?? new SwShBagHookWorkflowService(this.itemsWorkflowService);
        this.catchCapWorkflowService = catchCapWorkflowService ?? new SwShCatchCapWorkflowService();
        this.hyperTrainingWorkflowService = hyperTrainingWorkflowService ?? new SwShHyperTrainingWorkflowService();
        this.fairyGymBoostsWorkflowService = fairyGymBoostsWorkflowService ?? new SwShFairyGymBoostsWorkflowService();
        this.gymUniformRemovalWorkflowService = gymUniformRemovalWorkflowService ?? new SwShGymUniformRemovalWorkflowService();
        this.fashionUnlockWorkflowService = fashionUnlockWorkflowService ?? new SwShFashionUnlockWorkflowService();
        this.ivScreenWorkflowService = ivScreenWorkflowService ?? new SwShIvScreenWorkflowService();
        this.shinyRateWorkflowService = shinyRateWorkflowService ?? new SwShShinyRateWorkflowService();
        this.typeChartWorkflowService = typeChartWorkflowService ?? new SwShTypeChartWorkflowService();
        this.flagworkSaveWorkflowService = flagworkSaveWorkflowService ?? new SwShFlagworkSaveWorkflowService();
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new SwShGiftPokemonWorkflowService();
        this.tradePokemonWorkflowService = tradePokemonWorkflowService ?? new SwShTradePokemonWorkflowService();
        this.staticEncountersWorkflowService = staticEncountersWorkflowService ?? new SwShStaticEncountersWorkflowService();
        this.rentalPokemonWorkflowService = rentalPokemonWorkflowService ?? new SwShRentalPokemonWorkflowService();
        this.dynamaxAdventuresWorkflowService = dynamaxAdventuresWorkflowService ?? new SwShDynamaxAdventuresWorkflowService();
        this.placementWorkflowService = placementWorkflowService ?? new SwShPlacementWorkflowService(this.cacheManager);
        this.behaviorWorkflowService = behaviorWorkflowService ?? new SwShBehaviorWorkflowService();
        this.raidBattlesWorkflowService = raidBattlesWorkflowService ?? new SwShRaidBattlesWorkflowService();
        this.raidRewardsWorkflowService = raidRewardsWorkflowService ?? new SwShRaidRewardsWorkflowService();
        this.royalCandyWorkflowService = royalCandyWorkflowService ?? new SwShRoyalCandyWorkflowService(this.exeFsPatchWorkflowService, this.bagHookWorkflowService);
        this.startingItemsWorkflowService = startingItemsWorkflowService ?? new SwShStartingItemsWorkflowService(this.bagHookWorkflowService, this.itemsWorkflowService);
        this.npcItemGiftWorkflowService = npcItemGiftWorkflowService ?? new SwShNpcItemGiftWorkflowService(this.bagHookWorkflowService, this.itemsWorkflowService);
        this.shopsWorkflowService = shopsWorkflowService ?? new SwShShopsWorkflowService();
        this.spreadsheetImportWorkflowService = spreadsheetImportWorkflowService ?? new SwShSpreadsheetImportWorkflowService();
        this.modMergerWorkflowService = modMergerWorkflowService ?? new SwShModMergerWorkflowService(this.projectWorkspaceService);
        this.textWorkflowService = textWorkflowService ?? new SwShTextWorkflowService(this.cacheManager);
        this.trainersWorkflowService = trainersWorkflowService ?? new SwShTrainersWorkflowService();
    }

    public SwShWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!ProjectGameMetadata.IsSwordShield(paths.SelectedGame))
        {
            return new SwShWorkflowList([]);
        }

        var project = projectWorkspaceService.Open(paths);

        var summaries = new[]
        {
            itemsWorkflowService.CreateSummary(project),
            pokemonWorkflowService.CreateSummary(project),
            movesWorkflowService.CreateSummary(project),
            textWorkflowService.CreateSummary(project),
            trainersWorkflowService.CreateSummary(project),
            giftPokemonWorkflowService.CreateSummary(project),
            tradePokemonWorkflowService.CreateSummary(project),
            staticEncountersWorkflowService.CreateSummary(project),
            rentalPokemonWorkflowService.CreateSummary(project),
            dynamaxAdventuresWorkflowService.CreateSummary(project),
            shopsWorkflowService.CreateSummary(project),
            encountersWorkflowService.CreateSummary(project),
            raidBattlesWorkflowService.CreateSummary(project),
            raidRewardsWorkflowService.CreateSummary(project),
            raidRewardsWorkflowService.CreateBonusSummary(project),
            placementWorkflowService.CreateSummary(project),
            behaviorWorkflowService.CreateSummary(project),
            flagworkSaveWorkflowService.CreateSummary(project),
            bagHookWorkflowService.CreateSummary(project),
            catchCapWorkflowService.CreateSummary(project),
            hyperTrainingWorkflowService.CreateSummary(project),
            shinyRateWorkflowService.CreateSummary(project),
            typeChartWorkflowService.CreateSummary(project),
            fairyGymBoostsWorkflowService.CreateSummary(project),
            fashionUnlockWorkflowService.CreateSummary(project),
            gymUniformRemovalWorkflowService.CreateSummary(project),
            ivScreenWorkflowService.CreateSummary(project),
            royalCandyWorkflowService.CreateSummary(project),
            startingItemsWorkflowService.CreateSummary(project),
            npcItemGiftWorkflowService.CreateSummary(project),
            spreadsheetImportWorkflowService.CreateSummary(project),
            modMergerWorkflowService.CreateSummary(project),
        };

        return new SwShWorkflowList(
            summaries
                .Select(summary => SwShWorkflowDependencyValidator.Apply(project, summary))
                .ToArray());
    }

    public SwShItemsWorkflow LoadItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return itemsWorkflowService.Load(project);
    }

    public SwShPokemonWorkflow LoadPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return pokemonWorkflowService.Load(project);
    }

    public SwShPokemonWorkflowService SharedPokemonWorkflowService => pokemonWorkflowService;

    public SwShPlacementWorkflowService SharedPlacementWorkflowService => placementWorkflowService;

    public SwShTextWorkflowService SharedTextWorkflowService => textWorkflowService;

    public SwShCacheManager SharedCacheManager => cacheManager;

    public string CaptureSemanticExploreSourceFingerprint(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var graph = new ProjectFileGraphBuilder(new ProjectFileGraphBuilderOptions
        {
            MaximumFileSystemEntries = 500_000,
            MaximumDirectories = 100_000,
            MaximumTraversalDepth = 128,
            MaximumGraphEntries = 250_000,
        }).Build(paths);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendSemanticSourceHash(hash, "swsh-semantic-source-v3");
        AppendSemanticSourceHash(hash, SemanticProjectBuildIdentity.Capture(paths));
        AppendSemanticSourceHash(hash, SwShGameTextLanguage.Resolve(paths));
        var sourceCount = 0;
        long sourceBytes = 0;
        foreach (var entry in graph.Entries
                     .Where(entry => IsSemanticExploreSource(entry.RelativePath))
                     .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal))
        {
            AppendSemanticSourceHash(hash, entry.RelativePath);
            AppendSemanticGraphSource(
                hash,
                paths,
                entry.RelativePath,
                entry.BaseFile is not null,
                layered: false,
                ref sourceCount,
                ref sourceBytes);
            AppendSemanticGraphSource(
                hash,
                paths,
                entry.RelativePath,
                entry.LayeredFile is not null,
                layered: true,
                ref sourceCount,
                ref sourceBytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public SwShItemsWorkflow LoadSemanticExploreItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return new SwShItemsWorkflowService(ReadSemanticSourceBytes).Load(project);
    }

    public SwShPokemonWorkflow LoadSemanticExplorePokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return new SwShPokemonWorkflowService(ReadSemanticSourceBytes).Load(project);
    }

    public SwShMovesWorkflow LoadSemanticExploreMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return new SwShMovesWorkflowService(ReadSemanticSourceBytes).Load(project);
    }

    public SwShTrainersWorkflow LoadBalanceLabTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return SwShTrainersWorkflowService.CreateBounded(ReadSemanticSourceBytes).Load(project);
    }

    public SwShEncountersWorkflow LoadBalanceLabEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        return SwShEncountersWorkflowService.CreateBounded(
            ReadSemanticSourceBytes,
            checked((int)MaximumSemanticSourceBytesPerFile)).Load(project);
    }

    private static bool IsSemanticExploreSource(string relativePath)
    {
        return string.Equals(relativePath, SwShItemsWorkflowService.ItemDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShPokemonWorkflowService.PersonalDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShPokemonWorkflowService.LearnsetDataPath, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                SwShPokemonWorkflowService.EvolutionDataDirectory.TrimEnd('/') + '/',
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                SwShMovesWorkflowService.MoveDataDirectory.TrimEnd('/') + '/',
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                SwShTrainersWorkflowService.TrainerDataRootPath.TrimEnd('/') + '/',
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                SwShTrainersWorkflowService.TrainerPokeRootPath.TrimEnd('/') + '/',
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                SwShTrainersWorkflowService.TrainerClassRootPath.TrimEnd('/') + '/',
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                relativePath,
                SwShEncountersWorkflowService.WildDataPath,
                StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("/common/itemname.dat", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("/common/wazaname.dat", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("/common/wazainfo.dat", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("/common/monsname.dat", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("/common/tokusei.dat", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("/common/typename.dat", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("/common/trname.dat", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("/common/trtype.dat", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendSemanticGraphSource(
        IncrementalHash hash,
        ProjectPaths paths,
        string relativePath,
        bool exists,
        bool layered,
        ref int sourceCount,
        ref long sourceBytes)
    {
        AppendSemanticSourceHash(hash, layered ? "layered" : "base");
        if (!exists)
        {
            AppendSemanticSourceHash(hash, "missing");
            return;
        }

        if (++sourceCount > MaximumSemanticSourceFiles)
        {
            throw new InvalidDataException("The semantic source file count exceeds its bounded limit.");
        }

        var root = layered ? paths.OutputRootPath : paths.BaseRomFsPath;
        var child = layered
            ? relativePath
            : relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
                ? relativePath["romfs/".Length..]
                : throw new InvalidDataException("A semantic base source path is outside RomFS.");
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidDataException("A semantic source root is unavailable.");
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            child.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(normalizedRoot, fullPath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("A semantic source path escapes its configured root.");
        }

        var file = new FileInfo(fullPath);
        file.Refresh();
        if (!file.Exists
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || !string.IsNullOrEmpty(file.LinkTarget))
        {
            throw new InvalidDataException("A semantic source file is missing or linked.");
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1_024,
            FileOptions.SequentialScan);
        var observedLength = stream.Length;
        if (observedLength < 0
            || observedLength > MaximumSemanticSourceBytesPerFile
            || observedLength > MaximumSemanticSourceBytes - sourceBytes)
        {
            throw new InvalidDataException("The semantic source bytes exceed their bounded limit.");
        }

        AppendSemanticSourceHash(hash, observedLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendSemanticSourceHash(hash, Convert.ToHexStringLower(SHA256.HashData(stream)));
        if (stream.Length != observedLength)
        {
            throw new InvalidDataException("The semantic source changed while it was observed.");
        }

        sourceBytes = checked(sourceBytes + observedLength);
    }

    private static byte[] ReadSemanticSourceBytes(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1_024,
            FileOptions.SequentialScan);
        var observedLength = stream.Length;
        if (observedLength < 0 || observedLength > MaximumSemanticSourceBytesPerFile)
        {
            throw new InvalidDataException("The semantic source file exceeds its bounded limit.");
        }

        var bytes = new byte[checked((int)observedLength)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1 || stream.Length != observedLength)
        {
            throw new InvalidDataException("The semantic source file changed while it was read.");
        }

        return bytes;
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

    public SwShCacheStatus GetCacheStatus(ProjectPaths? paths = null)
    {
        var project = paths is null ? null : projectWorkspaceService.Open(paths);
        var activeSource = CapturePlacementCacheSourceIdentity(project);
        return AddCacheWarmupStatus(cacheManager.GetStatus(activeSource), project, activeSource);
    }

    public SwShCacheStatus UpdateCacheSettings(
        SwShCacheMode mode,
        long maxCacheSizeBytes,
        ProjectPaths? activePaths = null)
    {
        var project = activePaths is null ? null : projectWorkspaceService.Open(activePaths);
        var activeSource = CapturePlacementCacheSourceIdentity(project);
        var previousSettings = cacheManager.GetSettings();
        var status = cacheManager.UpdateSettings(mode, maxCacheSizeBytes, activeSource);
        if (previousSettings.Mode != status.Settings.Mode)
        {
            ClearMemoryCaches(clearReusableDataCaches: true);
        }
        else if (previousSettings.MaxCacheSizeBytes != status.Settings.MaxCacheSizeBytes)
        {
            ClearCacheWarmupState();
        }

        return AddCacheWarmupStatus(status, project, activeSource);
    }

    public SwShCacheStatus ClearCache(ProjectPaths? activePaths = null)
    {
        var project = activePaths is null ? null : projectWorkspaceService.Open(activePaths);
        var activeSource = CapturePlacementCacheSourceIdentity(project);
        var status = cacheManager.Clear(activeSource);
        ClearMemoryCaches(clearReusableDataCaches: true);
        return AddCacheWarmupStatus(status, project, activeSource);
    }

    public SwShCacheStatus WarmupCacheStep(ProjectPaths paths, int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var status = cacheManager.GetStatus();
        if (status.Settings.Mode == SwShCacheMode.Minimal)
        {
            return AddCacheWarmupStatus(status, project: null, activeSource: null);
        }

        var project = projectWorkspaceService.Open(paths);
        EnsureCacheWarmupProject(project);
        var activeSource = placementWorkflowService.CaptureCatalogCacheSourceIdentity(project);
        var targets = CreateCacheWarmupTargets(project, activeSource, status.Settings.Mode);
        if (stepIndex < 0 || stepIndex >= targets.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepIndex),
                stepIndex,
                $"Sword/Shield cache warmup step must be between 0 and {Math.Max(0, targets.Count - 1)}.");
        }

        CacheWarmupTarget? selectedTarget = null;
        lock (cacheWarmupSyncRoot)
        {
            for (var offset = 0; offset < targets.Count; offset++)
            {
                var candidate = targets[(stepIndex + offset) % targets.Count];
                if (!warmedCacheKeys.Contains(candidate.Key))
                {
                    selectedTarget = candidate;
                    break;
                }
            }
        }

        if (selectedTarget is not null)
        {
            if (selectedTarget.TextTarget is null)
            {
                OpenPlacementCatalog(project);
            }
            else
            {
                if (textWorkflowService.WarmupCache(project, selectedTarget.TextTarget))
                {
                    MarkCacheTargetWarmed(selectedTarget.Key);
                }
            }
        }

        return AddCacheWarmupStatus(cacheManager.GetStatus(activeSource), project, activeSource);
    }

    public SwShPlacementCatalog OpenPlacementCatalog(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        EnsureCacheWarmupProject(project);

        var catalog = placementWorkflowService.OpenCatalog(project);
        var activeSource = placementWorkflowService.CaptureCatalogCacheSourceIdentity(project);
        if (activeSource is not null)
        {
            MarkCacheTargetWarmed(CreatePlacementWarmupKey(project, activeSource));
        }

        return catalog;
    }

    public void ClearMemoryCaches(bool clearReusableDataCaches = true)
    {
        projectWorkspaceService.ClearMemoryCache();
        pokemonWorkflowService.ClearMemoryCache();
        placementWorkflowService.ClearMemoryCache(clearReusableDataCaches);
        textWorkflowService.ClearMemoryCache();
        if (clearReusableDataCaches)
        {
            parsedDataCache.Clear();
        }

        ClearCacheWarmupState();
    }

    private SwShCacheSourceIdentity? CapturePlacementCacheSourceIdentity(OpenedProject? project)
    {
        return project is null ? null : placementWorkflowService.CaptureCatalogCacheSourceIdentity(project);
    }

    private SwShCacheStatus AddCacheWarmupStatus(
        SwShCacheStatus status,
        OpenedProject? project,
        SwShCacheSourceIdentity? activeSource)
    {
        if (status.Settings.Mode == SwShCacheMode.Minimal || project is null)
        {
            return status with
            {
                WarmupCompleted = 0,
                WarmupTotal = 0,
                ProgressPercent = 0,
            };
        }

        EnsureCacheWarmupProject(project);
        var targets = CreateCacheWarmupTargets(project, activeSource, status.Settings.Mode);
        int completed;
        lock (cacheWarmupSyncRoot)
        {
            completed = targets.Count(target => warmedCacheKeys.Contains(target.Key));
        }

        var isWarmed = targets.Count > 0 && completed >= targets.Count;
        var progressPercent = targets.Count == 0
            ? 0
            : (int)Math.Clamp(completed * 100L / targets.Count, 0, 100);

        return status with
        {
            WarmupCompleted = completed,
            WarmupTotal = targets.Count,
            ProgressPercent = progressPercent,
            Phase = isWarmed ? "Cache ready" : "Ready to cache",
            Message = isWarmed
                ? "Sword/Shield Placement and Text cache is ready."
                : "Sword/Shield Placement and Text cache is ready to warm.",
        };
    }

    private IReadOnlyList<CacheWarmupTarget> CreateCacheWarmupTargets(
        OpenedProject project,
        SwShCacheSourceIdentity? placementSource,
        SwShCacheMode mode)
    {
        var targets = new List<CacheWarmupTarget>();
        if (placementSource is not null)
        {
            targets.Add(new CacheWarmupTarget(
                CreatePlacementWarmupKey(project, placementSource),
                TextTarget: null));
        }

        targets.AddRange(textWorkflowService
            .CreateCacheWarmupTargets(project, mode)
            .Select(target => new CacheWarmupTarget(
                CreateTextWarmupKey(project, target),
                target)));
        return targets;
    }

    private void MarkCacheTargetWarmed(string key)
    {
        lock (cacheWarmupSyncRoot)
        {
            warmedCacheKeys.Add(key);
        }
    }

    private void ClearCacheWarmupState()
    {
        lock (cacheWarmupSyncRoot)
        {
            warmedCacheKeys.Clear();
            activeCacheWarmupProjectId = null;
        }
    }

    private void EnsureCacheWarmupProject(OpenedProject project)
    {
        lock (cacheWarmupSyncRoot)
        {
            if (activeCacheWarmupProjectId == project.Id)
            {
                return;
            }

            warmedCacheKeys.Clear();
            activeCacheWarmupProjectId = project.Id;
        }
    }

    private static string CreatePlacementWarmupKey(
        OpenedProject project,
        SwShCacheSourceIdentity source)
    {
        return $"{project.Id}:placement:{CreateCacheIdentityKey(source)}";
    }

    private static string CreateTextWarmupKey(
        OpenedProject project,
        SwShTextWorkflowService.SwShTextCacheWarmupTarget target)
    {
        return $"{project.Id}:text:{target.Language}:{target.CategoryId}";
    }

    private sealed record CacheWarmupTarget(
        string Key,
        SwShTextWorkflowService.SwShTextCacheWarmupTarget? TextTarget);

    private static string CreateCacheIdentityKey(SwShCacheSourceIdentity source)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendCacheIdentityValue(hash, source.CacheSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendCacheIdentityValue(hash, source.ParserVersion);
        AppendCacheIdentityValue(hash, ((int)source.SelectedGame).ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var file in source.Sources)
        {
            AppendCacheIdentityValue(hash, file.FullPath);
            AppendCacheIdentityValue(hash, ((int)file.SourceLayer).ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendCacheIdentityValue(hash, file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendCacheIdentityValue(hash, file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendCacheIdentityValue(hash, file.Sha256);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendCacheIdentityValue(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    public SwShMovesWorkflow LoadMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return movesWorkflowService.Load(project);
    }

    public SwShTextWorkflow LoadText(
        ProjectPaths paths,
        SwShTextWorkflowQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return textWorkflowService.Load(project, query);
    }

    public SwShTrainersWorkflow LoadTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return trainersWorkflowService.Load(project);
    }

    public SwShGiftPokemonWorkflow LoadGiftPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return giftPokemonWorkflowService.Load(project);
    }

    public SwShStaticEncountersWorkflow LoadStaticEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return staticEncountersWorkflowService.Load(project);
    }

    public SwShTradePokemonWorkflow LoadTradePokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return tradePokemonWorkflowService.Load(project);
    }

    public SwShRentalPokemonWorkflow LoadRentalPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return rentalPokemonWorkflowService.Load(project);
    }

    public SwShDynamaxAdventuresWorkflow LoadDynamaxAdventures(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return dynamaxAdventuresWorkflowService.Load(project);
    }

    public SwShShopsWorkflow LoadShops(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return shopsWorkflowService.Load(project);
    }

    public SwShEncountersWorkflow LoadEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return encountersWorkflowService.Load(project);
    }

    public SwShRaidRewardsWorkflow LoadRaidRewards(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return raidRewardsWorkflowService.Load(project);
    }

    public SwShRaidRewardsWorkflow LoadRaidBonusRewards(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return raidRewardsWorkflowService.LoadBonus(project);
    }

    public SwShRaidBattlesWorkflow LoadRaidBattles(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return raidBattlesWorkflowService.Load(project);
    }

    public SwShPlacementWorkflow LoadPlacement(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return placementWorkflowService.Load(project);
    }

    public SwShBehaviorWorkflow LoadBehavior(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return behaviorWorkflowService.Load(project);
    }

    public SwShFlagworkSaveWorkflow LoadFlagworkSave(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return flagworkSaveWorkflowService.Load(project);
    }

    public SwShExeFsPatchWorkflow LoadExeFsPatches(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return exeFsPatchWorkflowService.Load(project);
    }

    public SwShBagHookWorkflow LoadBagHook(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return bagHookWorkflowService.Load(project);
    }

    public SwShCatchCapWorkflow LoadCatchCap(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return catchCapWorkflowService.Load(project);
    }

    public SwShHyperTrainingWorkflow LoadHyperTraining(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return hyperTrainingWorkflowService.Load(project);
    }

    public SwShIvScreenWorkflow LoadIvScreen(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return ivScreenWorkflowService.Load(project);
    }

    public SwShTypeChartWorkflow LoadTypeChart(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return typeChartWorkflowService.Load(project);
    }

    public SwShShinyRateWorkflow LoadShinyRate(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return shinyRateWorkflowService.Load(project);
    }

    public SwShFairyGymBoostsWorkflow LoadFairyGymBoosts(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return fairyGymBoostsWorkflowService.Load(project);
    }

    public SwShGymUniformRemovalWorkflow LoadGymUniformRemoval(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return gymUniformRemovalWorkflowService.Load(project);
    }

    public SwShFashionUnlockWorkflow LoadFashionUnlock(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return fashionUnlockWorkflowService.Load(project);
    }

    public SwShRoyalCandyWorkflow LoadRoyalCandy(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return royalCandyWorkflowService.Load(project);
    }

    public SwShStartingItemsWorkflow LoadStartingItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return startingItemsWorkflowService.Load(project);
    }

    public SwShNpcItemGiftWorkflow LoadNpcItemGift(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return npcItemGiftWorkflowService.Load(project);
    }

    public SwShSpreadsheetImportWorkflow LoadSpreadsheetImport(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return spreadsheetImportWorkflowService.Load(project);
    }

    public SwShModMergerWorkflow LoadModMerger(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return modMergerWorkflowService.Load(paths, modDirectory1, modDirectory2);
    }
}
