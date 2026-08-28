// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Core.Concurrency;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.SV.Data;

namespace KM.SV.Workflows;

public enum SvCacheMode
{
    Minimal,
    Balanced,
    Performance,
}

public sealed class SvCacheManager
{
    public const int CacheSchemaVersion = 2;
    public const string ParserVersion = "sv-cache-parser-v1";
    public const string DecompressorVersion = "sv-cache-decompressor-v1";

    private const long DefaultMaxCacheSizeBytes = 512L * 1024 * 1024;
    private const long MinimumMaxCacheSizeBytes = 128L * 1024 * 1024;
    private const long MaximumMaxCacheSizeBytes = 2L * 1024 * 1024 * 1024;
    private const string SettingsFileName = "settings.json";
    private const string ProjectsDirectoryName = "projects";
    private const string TempDirectoryName = "tmp";
    private const string IndexFileName = "index.json";
    private const string SourceFileName = "source.json";
    private const string WarmupPathsFileName = "warmup-paths.json";
    private const string WarmupStateFileName = "warmup-state.json";
    private const string PayloadDirectoryName = "payloads";
    private const string ArtifactsDirectoryName = "artifacts";
    private const string MetadataDirectoryName = "metadata";
    private const int WarmupCandidateBatchSize = 256;
    private const int MaximumPerformanceWarmupParallelism = 8;
    private const int MaximumArchiveIndexBytes = 64 * 1024 * 1024;
    private const int MaximumPerformanceWarmupFileBytes = 64 * 1024 * 1024;
    private const long MaximumPerformanceWarmupPackBytes = 128L * 1024L * 1024L;
    // A worker can retain a 64 MiB cached pack while loading a 128 MiB pack,
    // plus five 64 MiB compressed, native, and returned-payload buffers.
    // Reserve another 64 MiB above that roughly 512 MiB legal allocation peak.
    private const long PerformanceWarmupWorkerMemoryBudgetBytes = 576L * 1024L * 1024L;
    private const int MaximumCacheTraversalEntries = 500_000;
    private const int MaximumCacheTraversalDepth = 128;
    private const long MaximumCacheJsonFileBytes = 16L * 1024L * 1024L;
    private const long MaximumPersistedIndexFileBytes = 256L * 1024L * 1024L;
    private static readonly TimeSpan BalancedWarmupStepTimeBudget = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PerformanceWarmupStepTimeBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan OrphanTempFileAge = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly BoundedConcurrencyPolicy PerformanceWarmupPolicy = new(
        "sv-cache-performance-warmup",
        BoundedWorkloadKind.Decode,
        PerformanceWarmupWorkerMemoryBudgetBytes,
        MaximumPerformanceWarmupParallelism,
        memoryBudgetDivisor: 8);
    private static readonly BoundedConcurrencyPolicy WarmupVerificationPolicy = new(
        "sv-cache-warmup-verification",
        BoundedWorkloadKind.Read,
        maximumBytesPerWorker: 32L * 1024L * 1024L,
        maximumDegreeOfParallelism: MaximumPerformanceWarmupParallelism,
        degreeOfParallelismWhenMemoryUnknown: 4);
    private static readonly EnumerationOptions CacheDirectoryEnumeration = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };
    private readonly string cacheRoot;
    private readonly bool isReadWorker;
    private readonly object syncRoot = new();
    private SvCacheSourceFingerprint? retainedIndexSource;
    private SvTrinityArchiveIndex? retainedIndex;
    private HashSet<ulong>? retainedFileHashes;
    private IReadOnlyList<string>? retainedPackNames;
    private SvCacheSourceFingerprint? retainedWarmupPathsSource;
    private IReadOnlyList<string>? retainedWarmupVirtualPaths;
    private SvCacheSourceFingerprint? retainedBalancedWarmupPathsSource;
    private IReadOnlyList<string>? retainedBalancedWarmupVirtualPaths;
    private SvCacheSourceFingerprint? retainedWarmupProgressSource;
    private SvCacheMode? retainedWarmupProgressMode;
    private IReadOnlyList<string>? retainedWarmupProgressPaths;
    private HashSet<string>? retainedCompletedWarmupPaths;
    private long? retainedPersistentCacheSizeBytes;
    private string? lastObsoleteProjectCleanupKey;
    private bool tempCleanupCompleted;

    public SvCacheManager(string? cacheRoot = null)
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot ?? ResolveDefaultCacheRoot());
        isReadWorker = BoundedConcurrencyHostBudget.IsReadWorker;
    }

    internal bool HasRetainedIndex
    {
        get
        {
            lock (syncRoot)
            {
                return retainedIndex is not null;
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter<SvCacheMode>(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static readonly IReadOnlyList<string> CoreWarmupVirtualPaths = CreateCoreWarmupVirtualPaths();
    private static readonly IReadOnlyList<string> LabelWarmupVirtualPaths = CreateLabelWarmupVirtualPaths();

    public static IReadOnlyList<string> WarmupVirtualPaths { get; } = CreateOrderedWarmupVirtualPaths();

    public IReadOnlyList<string> GetWarmupVirtualPaths(ProjectPaths? paths = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            var context = TryCreateActiveProjectContext(paths);
            if (context is null)
            {
                return WarmupVirtualPaths;
            }

            var settings = ReadSettings();
            DeleteObsoleteProjectCaches(context);
            var warmupPaths = GetWarmupVirtualPaths(
                context,
                persistToDisk: !isReadWorker && settings.Mode != SvCacheMode.Minimal,
                out var cacheChanged);
            if (cacheChanged)
            {
                PruneIfNeeded(settings, context);
            }

            return warmupPaths;
        }
    }

    public SvCacheSettings GetSettings()
    {
        lock (syncRoot)
        {
            EnsureRoot();
            return ReadSettings();
        }
    }

    private static IReadOnlyList<string> CreateCoreWarmupVirtualPaths()
    {
        return
        [
            SvDataPaths.PersonalArray,
            SvDataPaths.MoveDataArray,
            SvDataPaths.ItemDataArray,
            SvDataPaths.EvolutionItemConversionArray,
            SvDataPaths.FriendlyShopLineupDataArray,
            SvDataPaths.ShopWazaMachineDataArray,
            SvDataPaths.VisibleItemScenePaldeaScarlet,
            SvDataPaths.VisibleItemScenePaldeaViolet,
            SvDataPaths.VisibleItemSceneKitakamiScarlet,
            SvDataPaths.VisibleItemSceneKitakamiViolet,
            SvDataPaths.VisibleItemSceneBlueberryScarlet,
            SvDataPaths.VisibleItemSceneBlueberryViolet,
            SvDataPaths.TrainerDataArray,
            SvDataPaths.WildEncounterArray,
            SvDataPaths.FixedSymbolTableArray,
            SvDataPaths.EventBattlePokemonArray,
            SvDataPaths.EventAddPokemonArray,
            SvDataPaths.EventTradeListArray,
            SvDataPaths.EventTradePokemonArray,
            SvDataPaths.TeraRaidEnemyPaldea1,
            SvDataPaths.TeraRaidEnemyPaldea2,
            SvDataPaths.TeraRaidEnemyPaldea3,
            SvDataPaths.TeraRaidEnemyPaldea4,
            SvDataPaths.TeraRaidEnemyPaldea5,
            SvDataPaths.TeraRaidEnemyPaldea6,
            SvDataPaths.TeraRaidEnemyKitakami1,
            SvDataPaths.TeraRaidEnemyKitakami2,
            SvDataPaths.TeraRaidEnemyKitakami3,
            SvDataPaths.TeraRaidEnemyKitakami4,
            SvDataPaths.TeraRaidEnemyKitakami5,
            SvDataPaths.TeraRaidEnemyKitakami6,
            SvDataPaths.TeraRaidEnemyBlueberry1,
            SvDataPaths.TeraRaidEnemyBlueberry2,
            SvDataPaths.TeraRaidEnemyBlueberry3,
            SvDataPaths.TeraRaidEnemyBlueberry4,
            SvDataPaths.TeraRaidEnemyBlueberry5,
            SvDataPaths.TeraRaidEnemyBlueberry6,
            SvDataPaths.TeraRaidEnemyDelivery,
            SvDataPaths.TeraRaidFixedRewardItemArray,
            SvDataPaths.TeraRaidLotteryRewardItemArray,
            SvDataPaths.HiddenItemDataTableArray,
            SvDataPaths.HiddenItemDataTableSu1Array,
            SvDataPaths.HiddenItemDataTableSu2Array,
            SvDataPaths.HiddenItemDataTableLcArray,
            SvDataPaths.RummagingItemDataTableArray,
        ];
    }

    private static IReadOnlyList<string> CreateLabelWarmupVirtualPaths()
    {
        return CreateWarmupLabelTextPaths()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> CreateWarmupLabelTextPaths()
    {
        foreach (var language in SvGameTextLanguage.SupportedMessageLanguages)
        {
            yield return SvDataPaths.ItemNames(language);
            yield return SvDataPaths.MoveNames(language);
            yield return SvDataPaths.MoveDescriptions(language);
            yield return SvDataPaths.PokemonNames(language);
            yield return SvDataPaths.AbilityNames(language);
            yield return SvDataPaths.PlaceNames(language);
            yield return SvDataPaths.PlaceNameKeys(language);
            yield return SvDataPaths.TrainerNames(language);
            yield return SvDataPaths.TrainerNameKeys(language);
            yield return SvDataPaths.TrainerTypes(language);
            yield return SvDataPaths.TrainerTypeKeys(language);
        }
    }

    private static IReadOnlyList<string> CreateOrderedWarmupVirtualPaths(
        IEnumerable<string>? discoveredTextEditorPaths = null)
    {
        return CoreWarmupVirtualPaths
            .Concat(LabelWarmupVirtualPaths)
            .Concat(discoveredTextEditorPaths ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<string> GetWarmupVirtualPaths(
        SvCacheProjectContext context,
        bool persistToDisk = true)
    {
        return GetWarmupVirtualPaths(context, persistToDisk, out _);
    }

    private IReadOnlyList<string> GetWarmupVirtualPaths(
        SvCacheProjectContext context,
        bool persistToDisk,
        out bool cacheChanged)
    {
        persistToDisk &= !isReadWorker;
        var index = GetOrBuildIndex(context, persistToDisk, out cacheChanged);
        var warmupPaths = GetOrCreateWarmupVirtualPaths(context, index);
        if (persistToDisk)
        {
            cacheChanged |= EnsureWarmupPathsManifestIsPersisted(context, warmupPaths);
        }

        return warmupPaths;
    }

    private static IEnumerable<string> CreateDiscoveredMessageWarmupPaths(
        IReadOnlyList<string> packNames)
    {
        foreach (var language in SvGameTextLanguage.SupportedMessageLanguages)
        {
            foreach (var packName in packNames)
            {
                var virtualPath = SvMessagePathResolver.TryCreateMessageDatPathFromPackName(packName, language);
                if (!string.IsNullOrWhiteSpace(virtualPath))
                {
                    yield return virtualPath;
                    yield return Path.ChangeExtension(virtualPath, ".tbl")
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                }
            }
        }
    }

    public SvCacheSettings UpdateSettings(SvCacheMode mode, long maxCacheSizeBytes, ProjectPaths? activePaths = null)
    {
        lock (syncRoot)
        {
            EnsureOwnerCacheMutation("update S/V cache settings");
            EnsureRoot();
            var previousSettings = ReadSettings();
            var settings = new SvCacheSettings(
                mode,
                ClampMaxCacheSize(maxCacheSizeBytes));
            WriteJsonAtomic(SettingsPath, settings);
            if (previousSettings.Mode != settings.Mode)
            {
                ClearMemoryCacheCore();
                DeleteDirectoryIfExists(ProjectsPath);
                DeleteDirectoryIfExists(TempPath);
                Directory.CreateDirectory(ProjectsPath);
                retainedPersistentCacheSizeBytes = 0;
            }
            else
            {
                var activeContext = TryCreateActiveProjectContext(activePaths);
                if (activeContext is not null)
                {
                    DeleteObsoleteProjectCaches(activeContext);
                    if (settings.MaxCacheSizeBytes != previousSettings.MaxCacheSizeBytes)
                    {
                        retainedWarmupProgressSource = null;
                        retainedWarmupProgressMode = null;
                        retainedWarmupProgressPaths = null;
                        retainedCompletedWarmupPaths = null;
                        if (settings.MaxCacheSizeBytes > previousSettings.MaxCacheSizeBytes)
                        {
                            TryDeleteFile(GetWarmupStatePath(activeContext));
                        }
                    }
                }

                PruneIfNeeded(settings, activeContext, forceSizeRefresh: true);
            }

            return settings;
        }
    }

    public SvCacheStatus GetStatus(ProjectPaths? paths = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            var settings = ReadSettings();
            var context = TryCreateActiveProjectContext(paths);
            IReadOnlyList<string>? warmupPlan = null;
            if (context is not null)
            {
                DeleteObsoleteProjectCaches(context);
                if (settings.Mode != SvCacheMode.Minimal)
                {
                    warmupPlan = SelectWarmupVirtualPaths(
                        settings.Mode,
                        context,
                        GetWarmupVirtualPaths(context, persistToDisk: !isReadWorker));
                }
                else if (Directory.Exists(context.ProjectDirectory))
                {
                    EnsureSourceManifestIsPersisted(context);
                }
            }

            PruneIfNeeded(settings, context);
            return CreateStatus(
                settings,
                context,
                activeProjectPreserved: false,
                warmupPlan);
        }
    }

    public SvCacheStatus Clear(ProjectPaths? activePaths = null)
    {
        lock (syncRoot)
        {
            EnsureOwnerCacheMutation("clear the S/V persistent cache");
            EnsureRoot();
            var settings = ReadSettings();
            var activeContext = TryCreateActiveProjectContext(activePaths);

            ClearMemoryCacheCore();
            DeleteDirectoryIfExists(ProjectsPath);
            DeleteDirectoryIfExists(TempPath);
            Directory.CreateDirectory(ProjectsPath);
            retainedPersistentCacheSizeBytes = 0;
            var warmupPlan = activeContext is not null && settings.Mode != SvCacheMode.Minimal
                ? SelectWarmupVirtualPaths(
                    settings.Mode,
                    activeContext,
                    GetWarmupVirtualPaths(activeContext, persistToDisk: false))
                : null;
            return CreateStatus(
                settings,
                activeContext,
                activeProjectPreserved: false,
                warmupPlan);
        }
    }

    public void ClearMemoryCache()
    {
        lock (syncRoot)
        {
            ClearMemoryCacheCore();
        }
    }

    public SvCacheStatus WarmupStep(ProjectPaths paths, int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(paths);

        lock (syncRoot)
        {
            EnsureOwnerCacheMutation("warm the S/V persistent cache");
            EnsureRoot();
            var settings = ReadSettings();
            var context = TryCreateActiveProjectContext(paths);
            if (context is null || settings.Mode == SvCacheMode.Minimal)
            {
                return CreateStatus(settings, context, activeProjectPreserved: false);
            }

            DeleteObsoleteProjectCaches(context);
            var warmupVirtualPaths = SelectWarmupVirtualPaths(
                settings.Mode,
                context,
                GetWarmupVirtualPaths(context));
            PruneIfNeeded(settings, context);
            if (IsWarmupCapacityLimited(settings, context))
            {
                return CreateStatus(
                    settings,
                    context,
                    activeProjectPreserved: false,
                    warmupVirtualPaths);
            }

            if (warmupVirtualPaths.Count == 0)
            {
                return CreateStatus(
                    settings,
                    context,
                    activeProjectPreserved: false,
                    warmupVirtualPaths);
            }

            var completedPaths = GetOrCreateCompletedWarmupPaths(settings, context, warmupVirtualPaths);
            var batch = GetWarmupBatch(warmupVirtualPaths, completedPaths, stepIndex);
            if (batch.Count == 0)
            {
                return CreateStatus(
                    settings,
                    context,
                    activeProjectPreserved: false,
                    warmupVirtualPaths);
            }

            IReadOnlyList<string> processedPaths;
            if (settings.Mode == SvCacheMode.Performance)
            {
                processedPaths = WarmupPerformanceBatch(settings, paths, context, batch);
            }
            else
            {
                var stopwatch = Stopwatch.StartNew();
                var processed = new List<string>(batch.Count);
                for (var index = 0; index < batch.Count; index++)
                {
                    if (index > 0 && stopwatch.Elapsed >= BalancedWarmupStepTimeBudget)
                    {
                        break;
                    }

                    var virtualPath = batch[index];
                    WriteVirtualMetadata(context, virtualPath);
                    if (IsWarmupEntryComplete(settings, context, virtualPath))
                    {
                        processed.Add(virtualPath);
                    }
                }

                processedPaths = processed;
            }

            var activeEntriesEvicted = PruneIfNeeded(settings, context);
            var survivingPaths = processedPaths
                .Where(path => IsWarmupEntryComplete(settings, context, path))
                .Select(NormalizeVirtualPath)
                .ToArray();
            foreach (var survivingPath in survivingPaths)
            {
                completedPaths.Add(survivingPath);
            }

            if (!activeEntriesEvicted && processedPaths.Count > 0 && survivingPaths.Length == 0)
            {
                retainedWarmupProgressSource = null;
                retainedWarmupProgressMode = null;
                retainedWarmupProgressPaths = null;
                retainedCompletedWarmupPaths = null;
                WriteWarmupCapacityState(settings, context);
            }

            return CreateStatus(
                settings,
                context,
                activeProjectPreserved: false,
                warmupVirtualPaths);
        }
    }

    public byte[] ReadBaseTrinityFile(ProjectPaths paths, string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        lock (syncRoot)
        {
            EnsureRoot();
            var settings = ReadSettings();
            var context = CreateProjectContext(paths);
            DeleteObsoleteProjectCaches(context);
            var normalizedVirtualPath = NormalizeVirtualPath(virtualPath);

            if (settings.Mode == SvCacheMode.Performance
                && TryReadPayload(context, normalizedVirtualPath, out var cachedBytes))
            {
                if (!isReadWorker)
                {
                    EnsureSourceManifestIsPersisted(context);
                    TouchProjectDirectory(context);
                }

                return cachedBytes;
            }

            var index = GetOrBuildIndex(
                context,
                persistToDisk: !isReadWorker && settings.Mode != SvCacheMode.Minimal,
                out var cacheChanged);

            using var archive = SvTrinityArchive.Open(
                paths.BaseRomFsPath!,
                paths.ScarletVioletSupportFolderPath,
                index: index);
            var bytes = archive.ReadFile(normalizedVirtualPath);

            if (!isReadWorker && settings.Mode == SvCacheMode.Performance)
            {
                WritePayload(context, normalizedVirtualPath, bytes);
                cacheChanged = true;
            }

            if (cacheChanged)
            {
                PruneIfNeeded(settings, context);
            }

            return bytes;
        }
    }

    public bool ContainsBaseTrinityFile(ProjectPaths paths, string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        lock (syncRoot)
        {
            EnsureRoot();
            var index = GetBaseTrinityIndex(paths, out var context);
            var fileHash = SvTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath));
            return GetOrCreateFileHashes(context, index).Contains(fileHash);
        }
    }

    public IReadOnlyList<string> ListBaseTrinityPackNames(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        lock (syncRoot)
        {
            EnsureRoot();
            var index = GetBaseTrinityIndex(paths, out var context);
            return GetOrCreatePackNames(context, index);
        }
    }

    private IReadOnlyList<string> SelectWarmupVirtualPaths(
        SvCacheMode mode,
        SvCacheProjectContext context,
        IReadOnlyList<string> allPaths)
    {
        if (mode == SvCacheMode.Performance)
        {
            return allPaths;
        }

        if (mode == SvCacheMode.Minimal)
        {
            return [];
        }

        if (retainedBalancedWarmupVirtualPaths is not null
            && retainedBalancedWarmupPathsSource == context.Source)
        {
            return retainedBalancedWarmupVirtualPaths;
        }

        var balancedPaths = allPaths
            .Where(path => !path.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        retainedBalancedWarmupPathsSource = context.Source;
        retainedBalancedWarmupVirtualPaths = balancedPaths;
        return balancedPaths;
    }

    internal bool TryReadTextArtifact(
        ProjectPaths paths,
        string artifactKey,
        string artifactParserVersion,
        IReadOnlyList<string> baseVirtualPaths,
        out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var descriptor = CreateTextArtifactDescriptor(
            artifactKey,
            artifactParserVersion,
            baseVirtualPaths);

        lock (syncRoot)
        {
            try
            {
                EnsureRoot();
                var settings = ReadSettings();
                if (settings.Mode != SvCacheMode.Performance
                    || !AreArtifactSourcesArchiveBacked(paths, descriptor.BaseVirtualPaths))
                {
                    payload = [];
                    return false;
                }

                var context = CreateProjectContext(paths);
                DeleteObsoleteProjectCaches(context);
                if (!TryReadTextArtifactCore(
                    context,
                    descriptor,
                    settings.MaxCacheSizeBytes,
                    out payload))
                {
                    return false;
                }

                if (!isReadWorker)
                {
                    EnsureSourceManifestIsPersisted(context);
                    TouchProjectDirectory(context);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException
                or JsonException
                or UnauthorizedAccessException
                or InvalidDataException)
            {
                payload = [];
                return false;
            }
        }
    }

    internal bool WriteTextArtifact(
        ProjectPaths paths,
        string artifactKey,
        string artifactParserVersion,
        IReadOnlyList<string> baseVirtualPaths,
        byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(payload);
        var descriptor = CreateTextArtifactDescriptor(
            artifactKey,
            artifactParserVersion,
            baseVirtualPaths);

        lock (syncRoot)
        {
            if (isReadWorker)
            {
                return false;
            }

            SvCacheProjectContext? context = null;
            try
            {
                EnsureRoot();
                var settings = ReadSettings();
                if (settings.Mode != SvCacheMode.Performance
                    || payload.LongLength > settings.MaxCacheSizeBytes
                    || !AreArtifactSourcesArchiveBacked(paths, descriptor.BaseVirtualPaths))
                {
                    return false;
                }

                context = CreateProjectContext(paths);
                DeleteObsoleteProjectCaches(context);
                EnsureSourceManifestIsPersisted(context);
                WriteTextArtifactCore(context, descriptor, payload);

                if (!TryReadTextArtifactCore(
                        context,
                        descriptor,
                        settings.MaxCacheSizeBytes,
                        out var verifiedPayload)
                    || !verifiedPayload.AsSpan().SequenceEqual(payload))
                {
                    DeleteTextArtifactPair(context, descriptor);
                    return false;
                }

                TouchProjectDirectory(context);
                PruneIfNeeded(settings, context);
                return TryReadTextArtifactCore(
                    context,
                    descriptor,
                    settings.MaxCacheSizeBytes,
                    out _);
            }
            catch (Exception exception) when (exception is IOException
                or JsonException
                or UnauthorizedAccessException
                or InvalidDataException)
            {
                if (context is not null)
                {
                    DeleteTextArtifactPair(context, descriptor);
                }

                return false;
            }
        }
    }

    internal void InvalidateTextArtifact(
        ProjectPaths paths,
        string artifactKey,
        string artifactParserVersion,
        IReadOnlyList<string> baseVirtualPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var descriptor = CreateTextArtifactDescriptor(
            artifactKey,
            artifactParserVersion,
            baseVirtualPaths);

        lock (syncRoot)
        {
            if (isReadWorker)
            {
                return;
            }

            try
            {
                EnsureRoot();
                var context = CreateProjectContext(paths);
                DeleteTextArtifactPair(context, descriptor);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
            {
            }
        }
    }

    private SvTrinityArchiveIndex GetOrBuildIndex(SvCacheProjectContext context)
    {
        return GetOrBuildIndex(context, persistToDisk: true);
    }

    private SvTrinityArchiveIndex GetOrBuildIndex(
        SvCacheProjectContext context,
        bool persistToDisk)
    {
        return GetOrBuildIndex(context, persistToDisk, out _);
    }

    private SvTrinityArchiveIndex GetOrBuildIndex(
        SvCacheProjectContext context,
        bool persistToDisk,
        out bool cacheChanged)
    {
        persistToDisk &= !isReadWorker;
        if (TryGetRetainedIndex(context, out var retained))
        {
            cacheChanged = persistToDisk && EnsureRetainedIndexIsPersisted(context, retained);

            return retained;
        }

        if (!persistToDisk)
        {
            if (isReadWorker && TryReadCachedIndex(context, out var readOnlyCachedIndex))
            {
                cacheChanged = false;
                return readOnlyCachedIndex;
            }

            var transientIndex = CompactIndex(SvTrinityArchive.BuildIndex(
                context.RomFsRootPath,
                MaximumArchiveIndexBytes));
            RetainIndex(context, transientIndex);
            cacheChanged = false;
            return transientIndex;
        }

        Directory.CreateDirectory(context.ProjectDirectory);
        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);
        if (TryReadCachedIndex(context, out var cachedIndex))
        {
            var sourceChanged = EnsureSourceManifestIsPersisted(context);
            TouchProjectDirectory(context);
            cacheChanged = sourceChanged;
            return cachedIndex;
        }

        var index = CompactIndex(SvTrinityArchive.BuildIndex(
            context.RomFsRootPath,
            MaximumArchiveIndexBytes));
        var indexFile = new SvCacheIndexFile(
            CacheSchemaVersion,
            context.Source,
            index);
        WriteJsonAtomic(indexPath, indexFile);
        WriteSourceManifest(context);
        RetainIndex(context, index);
        TouchProjectDirectory(context);
        cacheChanged = true;
        return index;
    }

    private bool TryReadCachedIndex(SvCacheProjectContext context, out SvTrinityArchiveIndex index)
    {
        if (TryGetRetainedIndex(context, out index))
        {
            return true;
        }

        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);
        if (TryReadCacheIndexFile(indexPath, out var cached)
            && cached.Source == context.Source
            && cached.Index.SchemaVersion == SvTrinityArchive.IndexSchemaVersion)
        {
            index = CompactIndex(cached.Index);
            RetainIndex(context, index);
            return true;
        }

        index = default!;
        return false;
    }

    private SvTrinityArchiveIndex GetBaseTrinityIndex(
        ProjectPaths paths,
        out SvCacheProjectContext context)
    {
        var settings = ReadSettings();
        context = CreateProjectContext(paths);
        DeleteObsoleteProjectCaches(context);
        var index = GetOrBuildIndex(
            context,
            persistToDisk: !isReadWorker && settings.Mode != SvCacheMode.Minimal,
            out var cacheChanged);
        if (cacheChanged)
        {
            PruneIfNeeded(settings, context);
        }

        return index;
    }

    private bool TryGetRetainedIndex(SvCacheProjectContext context, out SvTrinityArchiveIndex index)
    {
        if (retainedIndex is not null && retainedIndexSource == context.Source)
        {
            index = retainedIndex;
            return true;
        }

        index = default!;
        return false;
    }

    private void RetainIndex(SvCacheProjectContext context, SvTrinityArchiveIndex index)
    {
        retainedIndexSource = context.Source;
        retainedIndex = index;
        retainedFileHashes = null;
        retainedPackNames = null;
        retainedWarmupPathsSource = null;
        retainedWarmupVirtualPaths = null;
    }

    private bool EnsureRetainedIndexIsPersisted(
        SvCacheProjectContext context,
        SvTrinityArchiveIndex index)
    {
        if (isReadWorker)
        {
            return false;
        }

        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);
        var changed = false;
        if (!File.Exists(indexPath))
        {
            Directory.CreateDirectory(context.ProjectDirectory);
            WriteJsonAtomic(
                indexPath,
                new SvCacheIndexFile(CacheSchemaVersion, context.Source, index));
            changed = true;
        }

        changed |= EnsureSourceManifestIsPersisted(context);
        if (changed)
        {
            TouchProjectDirectory(context);
        }

        return changed;
    }

    private bool EnsureSourceManifestIsPersisted(SvCacheProjectContext context)
    {
        if (isReadWorker)
        {
            return false;
        }

        var sourcePath = GetSourcePath(context);
        if (File.Exists(sourcePath))
        {
            return false;
        }

        WriteSourceManifest(context);
        return true;
    }

    private void WriteSourceManifest(SvCacheProjectContext context)
    {
        WriteJsonAtomic(
            GetSourcePath(context),
            new SvCacheSourceFile(CacheSchemaVersion, context.Source));
    }

    private bool EnsureWarmupPathsManifestIsPersisted(
        SvCacheProjectContext context,
        IReadOnlyList<string> virtualPaths)
    {
        if (isReadWorker)
        {
            return false;
        }

        var manifestPath = GetWarmupPathsPath(context);
        if (File.Exists(manifestPath))
        {
            try
            {
                using var stream = OpenJsonReadStream(manifestPath);
                var existing = JsonSerializer.Deserialize<SvCacheWarmupPathsFile>(stream, JsonOptions);
                if (existing is not null
                    && existing.CacheSchemaVersion == CacheSchemaVersion
                    && existing.Source == context.Source
                    && IsValidWarmupPathList(existing.VirtualPaths)
                    && existing.VirtualPaths.SequenceEqual(virtualPaths, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (Exception exception) when (exception is IOException
                or JsonException
                or UnauthorizedAccessException)
            {
            }
        }

        WriteJsonAtomic(
            manifestPath,
            new SvCacheWarmupPathsFile(CacheSchemaVersion, context.Source, virtualPaths));
        retainedWarmupProgressSource = null;
        retainedWarmupProgressMode = null;
        retainedWarmupProgressPaths = null;
        retainedCompletedWarmupPaths = null;
        retainedBalancedWarmupPathsSource = null;
        retainedBalancedWarmupVirtualPaths = null;
        TryDeleteFile(GetWarmupStatePath(context));
        return true;
    }

    private void ClearMemoryCacheCore()
    {
        retainedIndexSource = null;
        retainedIndex = null;
        retainedFileHashes = null;
        retainedPackNames = null;
        retainedWarmupPathsSource = null;
        retainedWarmupVirtualPaths = null;
        retainedBalancedWarmupPathsSource = null;
        retainedBalancedWarmupVirtualPaths = null;
        retainedWarmupProgressSource = null;
        retainedWarmupProgressMode = null;
        retainedWarmupProgressPaths = null;
        retainedCompletedWarmupPaths = null;
    }

    private HashSet<ulong> GetOrCreateFileHashes(
        SvCacheProjectContext context,
        SvTrinityArchiveIndex index)
    {
        if (retainedFileHashes is not null
            && retainedIndexSource == context.Source
            && ReferenceEquals(retainedIndex, index))
        {
            return retainedFileHashes;
        }

        var fileHashes = index.Files
            .Select(file => file.FileHash)
            .ToHashSet();
        if (retainedIndexSource == context.Source && ReferenceEquals(retainedIndex, index))
        {
            retainedFileHashes = fileHashes;
        }

        return fileHashes;
    }

    private IReadOnlyList<string> GetOrCreatePackNames(
        SvCacheProjectContext context,
        SvTrinityArchiveIndex index)
    {
        if (retainedPackNames is not null
            && retainedIndexSource == context.Source
            && ReferenceEquals(retainedIndex, index))
        {
            return retainedPackNames;
        }

        var packNames = Array.AsReadOnly(index.Files
            .Select(file => file.PackName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray());
        if (retainedIndexSource == context.Source && ReferenceEquals(retainedIndex, index))
        {
            retainedPackNames = packNames;
        }

        return packNames;
    }

    private IReadOnlyList<string> GetOrCreateWarmupVirtualPaths(
        SvCacheProjectContext context,
        SvTrinityArchiveIndex index)
    {
        if (retainedWarmupVirtualPaths is not null
            && retainedWarmupPathsSource == context.Source
            && ReferenceEquals(retainedIndex, index))
        {
            return retainedWarmupVirtualPaths;
        }

        var fileHashes = GetOrCreateFileHashes(context, index);
        var packNames = GetOrCreatePackNames(context, index);
        var paths = CreateOrderedWarmupVirtualPaths(CreateDiscoveredMessageWarmupPaths(packNames))
            .Where(virtualPath => fileHashes.Contains(
                SvTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath))))
            .ToArray();
        if (retainedIndexSource == context.Source && ReferenceEquals(retainedIndex, index))
        {
            retainedWarmupPathsSource = context.Source;
            retainedWarmupVirtualPaths = paths;
        }

        return paths;
    }

    private static SvTrinityArchiveIndex CompactIndex(SvTrinityArchiveIndex index)
    {
        var packNames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (index.Files is List<SvTrinityArchiveFileIndexEntry> mutableFiles)
        {
            for (var fileIndex = 0; fileIndex < mutableFiles.Count; fileIndex++)
            {
                var file = mutableFiles[fileIndex];
                if (!packNames.TryGetValue(file.PackName, out var packName))
                {
                    packName = file.PackName;
                    packNames.Add(packName, packName);
                }
                else if (!ReferenceEquals(file.PackName, packName))
                {
                    mutableFiles[fileIndex] = file with { PackName = packName };
                }
            }

            return index;
        }

        var compactedFiles = new SvTrinityArchiveFileIndexEntry[index.Files.Count];
        var changed = false;
        for (var fileIndex = 0; fileIndex < index.Files.Count; fileIndex++)
        {
            var file = index.Files[fileIndex];
            if (!packNames.TryGetValue(file.PackName, out var packName))
            {
                packName = file.PackName;
                packNames.Add(packName, packName);
            }

            changed |= !ReferenceEquals(file.PackName, packName);
            compactedFiles[fileIndex] = ReferenceEquals(file.PackName, packName)
                ? file
                : file with { PackName = packName };
        }

        return changed
            ? new SvTrinityArchiveIndex(index.SchemaVersion, compactedFiles, index.Packs)
            : index;
    }

    private void WriteVirtualMetadata(SvCacheProjectContext context, string virtualPath)
    {
        Directory.CreateDirectory(GetMetadataDirectory(context));
        var normalized = NormalizeVirtualPath(virtualPath);
        var metadataPath = GetMetadataPath(context, normalized);
        var metadata = new SvCacheVirtualFileMetadata(
            CacheSchemaVersion,
            context.Source,
            normalized,
            DateTimeOffset.UtcNow);
        WriteJsonAtomic(metadataPath, metadata);
        TouchProjectDirectory(context);
    }

    private bool TryReadPayload(SvCacheProjectContext context, string virtualPath, out byte[] bytes)
    {
        var metadataPath = GetPayloadMetadataPath(context, virtualPath);
        var payloadPath = GetPayloadPath(context, virtualPath);
        if (!File.Exists(metadataPath) || !File.Exists(payloadPath))
        {
            bytes = [];
            return false;
        }

        try
        {
            SvCachePayloadMetadata? metadata;
            using (var stream = OpenJsonReadStream(metadataPath))
            {
                metadata = JsonSerializer.Deserialize<SvCachePayloadMetadata>(stream, JsonOptions);
            }

            if (metadata is null
                || metadata.CacheSchemaVersion != CacheSchemaVersion
                || metadata.Source != context.Source
                || !string.Equals(metadata.VirtualPath, virtualPath, StringComparison.Ordinal)
                || metadata.DecompressedSize < 0
                || metadata.DecompressedSize > MaximumPerformanceWarmupFileBytes)
            {
                bytes = [];
                return false;
            }

            bytes = ReadAllBytesShared(payloadPath, MaximumPerformanceWarmupFileBytes);
            if (bytes.LongLength != metadata.DecompressedSize)
            {
                bytes = [];
                return false;
            }

            if (!isReadWorker)
            {
                TouchCacheFile(payloadPath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            bytes = [];
            return false;
        }
    }

    private bool TryReadTextArtifactCore(
        SvCacheProjectContext context,
        SvTextArtifactDescriptor descriptor,
        long maximumPayloadSize,
        out byte[] payload)
    {
        var payloadPath = GetTextArtifactPayloadPath(context, descriptor);
        var metadataPath = GetTextArtifactMetadataPath(context, descriptor);
        if (!File.Exists(payloadPath) || !File.Exists(metadataPath))
        {
            if (!isReadWorker)
            {
                DeleteTextArtifactPair(context, descriptor);
            }

            payload = [];
            return false;
        }

        try
        {
            SvCacheTextArtifactMetadata? metadata;
            using (var stream = OpenJsonReadStream(metadataPath))
            {
                metadata = JsonSerializer.Deserialize<SvCacheTextArtifactMetadata>(stream, JsonOptions);
            }

            if (metadata is null
                || metadata.CacheSchemaVersion != CacheSchemaVersion
                || metadata.Source != context.Source
                || !string.Equals(metadata.ArtifactKey, descriptor.ArtifactKey, StringComparison.Ordinal)
                || !string.Equals(
                    metadata.ArtifactParserVersion,
                    descriptor.ArtifactParserVersion,
                    StringComparison.Ordinal)
                || metadata.BaseVirtualPaths is null
                || !metadata.BaseVirtualPaths.SequenceEqual(
                    descriptor.BaseVirtualPaths,
                    StringComparer.Ordinal)
                || metadata.PayloadSize < 0
                || metadata.PayloadSize > maximumPayloadSize)
            {
                if (!isReadWorker)
                {
                    DeleteTextArtifactPair(context, descriptor);
                }

                payload = [];
                return false;
            }

            var payloadInfo = new FileInfo(payloadPath);
            if (!payloadInfo.Exists || payloadInfo.Length != metadata.PayloadSize)
            {
                if (!isReadWorker)
                {
                    DeleteTextArtifactPair(context, descriptor);
                }

                payload = [];
                return false;
            }

            payload = ReadAllBytesShared(payloadPath, maximumPayloadSize);
            var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            if (!string.Equals(payloadHash, metadata.PayloadSha256, StringComparison.Ordinal))
            {
                if (!isReadWorker)
                {
                    DeleteTextArtifactPair(context, descriptor);
                }

                payload = [];
                return false;
            }

            if (!isReadWorker)
            {
                TouchCacheFile(payloadPath);
                TouchCacheFile(metadataPath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException
            or JsonException
            or UnauthorizedAccessException)
        {
            if (!isReadWorker)
            {
                DeleteTextArtifactPair(context, descriptor);
            }

            payload = [];
            return false;
        }
    }

    private void WriteTextArtifactCore(
        SvCacheProjectContext context,
        SvTextArtifactDescriptor descriptor,
        byte[] payload)
    {
        var metadata = new SvCacheTextArtifactMetadata(
            CacheSchemaVersion,
            context.Source,
            descriptor.ArtifactKey,
            descriptor.ArtifactParserVersion,
            descriptor.BaseVirtualPaths,
            payload.LongLength,
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            DateTimeOffset.UtcNow);

        WriteBytesAtomic(GetTextArtifactPayloadPath(context, descriptor), payload);
        WriteJsonAtomic(GetTextArtifactMetadataPath(context, descriptor), metadata);
    }

    private void DeleteTextArtifactPair(
        SvCacheProjectContext context,
        SvTextArtifactDescriptor descriptor)
    {
        if (isReadWorker)
        {
            return;
        }

        TryDeleteFile(GetTextArtifactPayloadPath(context, descriptor));
        TryDeleteFile(GetTextArtifactMetadataPath(context, descriptor));
    }

    private IReadOnlyList<string> GetWarmupBatch(
        IReadOnlyList<string> warmupVirtualPaths,
        IReadOnlySet<string> completedPaths,
        int stepIndex)
    {
        var firstIndex = FindNextIncompleteWarmupIndex(warmupVirtualPaths, completedPaths, stepIndex);
        if (firstIndex < 0)
        {
            return Array.Empty<string>();
        }

        var batch = new List<string>(WarmupCandidateBatchSize);
        var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var offset = 0;
             offset < warmupVirtualPaths.Count && batch.Count < WarmupCandidateBatchSize;
             offset++)
        {
            var index = (firstIndex + offset) % warmupVirtualPaths.Count;
            var virtualPath = NormalizeVirtualPath(warmupVirtualPaths[index]);
            if (!completedPaths.Contains(virtualPath) && selectedPaths.Add(virtualPath))
            {
                batch.Add(virtualPath);
            }
        }

        return batch;
    }

    private int FindNextIncompleteWarmupIndex(
        IReadOnlyList<string> warmupVirtualPaths,
        IReadOnlySet<string> completedPaths,
        int stepIndex)
    {
        var safeStepIndex = Math.Clamp(stepIndex, 0, Math.Max(0, warmupVirtualPaths.Count - 1));
        for (var offset = 0; offset < warmupVirtualPaths.Count; offset++)
        {
            var index = (safeStepIndex + offset) % warmupVirtualPaths.Count;
            var virtualPath = NormalizeVirtualPath(warmupVirtualPaths[index]);
            if (!completedPaths.Contains(virtualPath))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsWarmupEntryComplete(
        SvCacheSettings settings,
        SvCacheProjectContext context,
        string virtualPath)
    {
        if (!IsVirtualMetadataComplete(context, virtualPath))
        {
            return false;
        }

        return settings.Mode != SvCacheMode.Performance || IsWarmupPayloadComplete(context, virtualPath);
    }

    private static bool IsWarmupPayloadComplete(SvCacheProjectContext context, string virtualPath)
    {
        var metadataPath = GetPayloadMetadataPath(context, virtualPath);
        var payloadPath = GetPayloadPath(context, virtualPath);
        if (!File.Exists(metadataPath) || !File.Exists(payloadPath))
        {
            return false;
        }

        try
        {
            using var stream = OpenJsonReadStream(metadataPath);
            var metadata = JsonSerializer.Deserialize<SvCachePayloadMetadata>(stream, JsonOptions);
            return metadata is not null
                && metadata.CacheSchemaVersion == CacheSchemaVersion
                && metadata.Source == context.Source
                && string.Equals(metadata.VirtualPath, virtualPath, StringComparison.Ordinal)
                && metadata.DecompressedSize >= 0
                && new FileInfo(payloadPath).Length == metadata.DecompressedSize;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsVirtualMetadataComplete(SvCacheProjectContext context, string virtualPath)
    {
        var metadataPath = GetMetadataPath(context, virtualPath);
        if (!File.Exists(metadataPath))
        {
            return false;
        }

        try
        {
            using var stream = OpenJsonReadStream(metadataPath);
            var metadata = JsonSerializer.Deserialize<SvCacheVirtualFileMetadata>(stream, JsonOptions);
            return metadata is not null
                && metadata.CacheSchemaVersion == CacheSchemaVersion
                && metadata.Source == context.Source
                && string.Equals(metadata.VirtualPath, virtualPath, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private IReadOnlyList<string> WarmupPerformanceBatch(
        SvCacheSettings settings,
        ProjectPaths paths,
        SvCacheProjectContext context,
        IReadOnlyList<string> virtualPaths)
    {
        var stopwatch = Stopwatch.StartNew();
        var index = GetOrBuildIndex(context);
        var processed = new List<string>(virtualPaths.Count);
        var pendingPaths = new List<string>(virtualPaths.Count);

        foreach (var path in virtualPaths)
        {
            var virtualPath = NormalizeVirtualPath(path);
            if (IsWarmupEntryComplete(settings, context, virtualPath))
            {
                processed.Add(virtualPath);
            }
            else
            {
                pendingPaths.Add(virtualPath);
            }
        }

        if (pendingPaths.Count == 0)
        {
            return processed;
        }

        using (SvTrinityArchive.Open(
            paths.BaseRomFsPath!,
            paths.ScarletVioletSupportFolderPath,
            index: index,
            maximumPackBytes: MaximumPerformanceWarmupPackBytes))
        {
            // Compile the shared immutable lookup once before worker archives fan out.
        }

        var maximumParallelism = BoundedParallel
            .Plan(pendingPaths.Count, PerformanceWarmupPolicy)
            .DegreeOfParallelism;
        var archives = new SvTrinityArchive?[maximumParallelism];
        try
        {
            for (var archiveIndex = 0; archiveIndex < archives.Length; archiveIndex++)
            {
                archives[archiveIndex] = SvTrinityArchive.Open(
                    paths.BaseRomFsPath!,
                    paths.ScarletVioletSupportFolderPath,
                    index: index,
                    maximumPackBytes: MaximumPerformanceWarmupPackBytes);
            }

            for (var waveStart = 0; waveStart < pendingPaths.Count; waveStart += maximumParallelism)
            {
                var waveCount = Math.Min(maximumParallelism, pendingPaths.Count - waveStart);
                var extractedPayloads = new byte[]?[waveCount];
                var extractionFailures = new ExceptionDispatchInfo?[waveCount];
                _ = BoundedParallel.For(
                    waveCount,
                    PerformanceWarmupPolicy,
                    waveIndex =>
                    {
                        try
                        {
                            var virtualPath = pendingPaths[waveStart + waveIndex];
                            if (archives[waveIndex]!.TryReadFile(
                                    virtualPath,
                                    MaximumPerformanceWarmupFileBytes,
                                    out var bytes))
                            {
                                extractedPayloads[waveIndex] = bytes;
                            }
                        }
                        catch (Exception exception)
                        {
                            extractionFailures[waveIndex] = ExceptionDispatchInfo.Capture(exception);
                        }
                    });

                for (var waveIndex = 0; waveIndex < waveCount; waveIndex++)
                {
                    if (extractionFailures[waveIndex] is not { } extractionFailure)
                    {
                        continue;
                    }

                    var failedPath = pendingPaths[waveStart + waveIndex];
                    try
                    {
                        extractionFailure.Throw();
                    }
                    catch (OutOfMemoryException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidDataException(
                            $"The indexed S/V cache source failed while reading '{failedPath}'. Verify the configured Base RomFS and support files, then retry.",
                            exception);
                    }
                }

                for (var waveIndex = 0; waveIndex < waveCount; waveIndex++)
                {
                    if (extractedPayloads[waveIndex] is null)
                    {
                        var failedPath = pendingPaths[waveStart + waveIndex];
                        throw new InvalidDataException(
                            $"The indexed S/V cache source could not read '{failedPath}'. Verify the configured Base RomFS and support files, then retry.");
                    }
                }

                for (var waveIndex = 0; waveIndex < waveCount; waveIndex++)
                {
                    var virtualPath = pendingPaths[waveStart + waveIndex];
                    var bytes = extractedPayloads[waveIndex]!;
                    WriteVirtualMetadata(context, virtualPath);
                    WritePayload(context, virtualPath, bytes);
                    extractedPayloads[waveIndex] = null;

                    if (!IsWarmupEntryComplete(settings, context, virtualPath))
                    {
                        throw new InvalidDataException(
                            $"The S/V cache could not verify the completed warmup entry '{virtualPath}'. Retry the cache build.");
                    }

                    processed.Add(virtualPath);
                }

                if (stopwatch.Elapsed >= PerformanceWarmupStepTimeBudget)
                {
                    break;
                }
            }
        }
        finally
        {
            foreach (var archive in archives)
            {
                archive?.Dispose();
            }
        }

        return processed;
    }

    private void WritePayload(SvCacheProjectContext context, string virtualPath, byte[] bytes)
    {
        Directory.CreateDirectory(GetPayloadDirectory(context));
        var payloadPath = GetPayloadPath(context, virtualPath);
        var metadataPath = GetPayloadMetadataPath(context, virtualPath);
        var metadata = new SvCachePayloadMetadata(
            CacheSchemaVersion,
            context.Source,
            virtualPath,
            bytes.LongLength,
            DateTimeOffset.UtcNow);

        WriteBytesAtomic(payloadPath, bytes);
        WriteJsonAtomic(metadataPath, metadata);
        TouchProjectDirectory(context);
    }

    private SvCacheStatus CreateStatus(
        SvCacheSettings settings,
        SvCacheProjectContext? context,
        bool activeProjectPreserved,
        IReadOnlyList<string>? exactWarmupPlan = null)
    {
        var cacheSize = GetCacheContentSize();
        var warmupVirtualPaths = context is not null && settings.Mode != SvCacheMode.Minimal
            ? exactWarmupPlan ?? SelectWarmupVirtualPaths(
                    settings.Mode,
                    context,
                    GetWarmupVirtualPaths(context))
            : Array.Empty<string>();
        var total = warmupVirtualPaths.Count;
        var capacityLimited = context is not null && IsWarmupCapacityLimited(settings, context);
        var completed = capacityLimited
            ? total
            : context is not null && total > 0
                ? GetOrCreateCompletedWarmupPaths(settings, context, warmupVirtualPaths).Count
                : 0;
        var percent = total == 0
            ? 0
            : (int)Math.Clamp(completed * 100L / total, 0, 100);
        var phase = settings.Mode == SvCacheMode.Minimal
            ? "Minimal mode"
            : capacityLimited || completed >= total && total > 0
                ? "Cache ready"
                : completed == 0
                    ? "Checking cache"
                    : settings.Mode == SvCacheMode.Performance
                        ? "Caching Trinity payloads"
                        : "Indexing Trinity files";
        var message = settings.Mode switch
        {
            SvCacheMode.Minimal => "Session only cache mode is active.",
            _ when capacityLimited => "The configured cache limit is ready with a bounded working set; uncached files load on demand.",
            SvCacheMode.Balanced when total > 0 && completed >= total => "Balanced cache metadata is ready.",
            SvCacheMode.Balanced => "Building Scarlet/Violet cache metadata.",
            SvCacheMode.Performance when total > 0 && completed >= total => "Performance cache payloads are ready.",
            SvCacheMode.Performance => "Building Scarlet/Violet decompressed payload cache.",
            _ => "Scarlet/Violet cache is idle.",
        };

        return new SvCacheStatus(
            settings,
            cacheSize,
            completed,
            total,
            Math.Clamp(percent, 0, 100),
            phase,
            message,
            activeProjectPreserved);
    }

    private static bool IsValidWarmupPathList(IReadOnlyList<string>? virtualPaths)
    {
        return virtualPaths is { Count: > 0 }
            && virtualPaths.All(path =>
            {
                if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                {
                    return false;
                }

                var segments = path.Replace('\\', '/').Split('/');
                return segments.All(segment => !string.IsNullOrWhiteSpace(segment)
                    && !string.Equals(segment, ".", StringComparison.Ordinal)
                    && !string.Equals(segment, "..", StringComparison.Ordinal));
            });
    }

    private HashSet<string> GetOrCreateCompletedWarmupPaths(
        SvCacheSettings settings,
        SvCacheProjectContext context,
        IReadOnlyList<string> warmupVirtualPaths)
    {
        if (retainedCompletedWarmupPaths is not null
            && retainedWarmupProgressSource == context.Source
            && retainedWarmupProgressMode == settings.Mode
            && ReferenceEquals(retainedWarmupProgressPaths, warmupVirtualPaths))
        {
            return retainedCompletedWarmupPaths;
        }

        retainedWarmupProgressSource = context.Source;
        retainedWarmupProgressMode = settings.Mode;
        retainedWarmupProgressPaths = warmupVirtualPaths;
        var normalizedPaths = warmupVirtualPaths.Select(NormalizeVirtualPath).ToArray();
        var completed = BoundedParallel.MapOrdered(
            normalizedPaths,
            WarmupVerificationPolicy,
            (virtualPath, _) => IsWarmupEntryComplete(settings, context, virtualPath));
        retainedCompletedWarmupPaths = Enumerable.Range(0, normalizedPaths.Length)
            .Where(index => completed[index])
            .Select(index => normalizedPaths[index])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return retainedCompletedWarmupPaths;
    }

    private bool IsWarmupCapacityLimited(SvCacheSettings settings, SvCacheProjectContext context)
    {
        var statePath = GetWarmupStatePath(context);
        if (!File.Exists(statePath))
        {
            return false;
        }

        try
        {
            using var stream = OpenJsonReadStream(statePath);
            var state = JsonSerializer.Deserialize<SvCacheWarmupStateFile>(stream, JsonOptions);
            return state is not null
                && state.CacheSchemaVersion == CacheSchemaVersion
                && state.Source == context.Source
                && state.Mode == settings.Mode
                && state.MaxCacheSizeBytes == settings.MaxCacheSizeBytes
                && state.CapacityLimited;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteWarmupCapacityState(SvCacheSettings settings, SvCacheProjectContext context)
    {
        WriteJsonAtomic(
            GetWarmupStatePath(context),
            new SvCacheWarmupStateFile(
                CacheSchemaVersion,
                context.Source,
                settings.Mode,
                settings.MaxCacheSizeBytes,
                CapacityLimited: true));
        TouchProjectDirectory(context);
    }

    private bool PruneIfNeeded(
        SvCacheSettings settings,
        SvCacheProjectContext? activeContext,
        bool forceSizeRefresh = false)
    {
        if (isReadWorker)
        {
            return false;
        }

        var activeProjectKey = activeContext?.ProjectKey;
        CleanupTempDirectory();
        var currentSize = GetCacheContentSize(forceSizeRefresh);
        if (currentSize <= settings.MaxCacheSizeBytes || !Directory.Exists(ProjectsPath))
        {
            return false;
        }

        foreach (var directory in Directory
            .EnumerateDirectories(ProjectsPath)
            .Select(path => new DirectoryInfo(path))
            .OrderBy(info => info.LastWriteTimeUtc))
        {
            if (string.Equals(directory.Name, activeProjectKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteDirectory(directory.FullName);
            currentSize = GetCacheContentSize();
            if (currentSize <= settings.MaxCacheSizeBytes)
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(activeProjectKey))
        {
            return false;
        }

        var activeEntriesEvicted = false;
        var warmupEntriesEvicted = false;
        var activeProjectDirectory = Path.Combine(ProjectsPath, activeProjectKey);
        foreach (var candidate in GetActiveProjectEvictionCandidates(activeProjectDirectory))
        {
            var removedAny = false;
            foreach (var path in candidate.Paths)
            {
                var removed = TryDeleteFile(path);
                removedAny |= removed;
            }

            activeEntriesEvicted |= removedAny;
            warmupEntriesEvicted |= removedAny && candidate.AffectsWarmupCapacity;

            currentSize = GetCacheContentSize();
            if (currentSize <= settings.MaxCacheSizeBytes)
            {
                MarkWarmupCapacityLimitedAfterEviction(settings, activeContext, warmupEntriesEvicted);
                return activeEntriesEvicted;
            }
        }

        if (currentSize > settings.MaxCacheSizeBytes && Directory.Exists(activeProjectDirectory))
        {
            if (TryDeleteDirectory(activeProjectDirectory))
            {
                ClearMemoryCacheCore();
                activeEntriesEvicted = true;
                warmupEntriesEvicted = true;
            }
        }

        MarkWarmupCapacityLimitedAfterEviction(settings, activeContext, warmupEntriesEvicted);
        return activeEntriesEvicted;
    }

    private void MarkWarmupCapacityLimitedAfterEviction(
        SvCacheSettings settings,
        SvCacheProjectContext? activeContext,
        bool activeEntriesEvicted)
    {
        if (!activeEntriesEvicted)
        {
            return;
        }

        retainedWarmupProgressSource = null;
        retainedWarmupProgressMode = null;
        retainedWarmupProgressPaths = null;
        retainedCompletedWarmupPaths = null;
        if (activeContext is not null && settings.Mode != SvCacheMode.Minimal)
        {
            WriteWarmupCapacityState(settings, activeContext);
        }
    }

    private static IReadOnlyList<CacheEvictionCandidate> GetActiveProjectEvictionCandidates(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory))
        {
            return [];
        }

        var candidates = new List<CacheEvictionCandidate>();
        var metadataDirectory = Path.Combine(projectDirectory, MetadataDirectoryName);
        var virtualMetadataByKey = Directory.Exists(metadataDirectory)
            ? Directory
                .EnumerateFiles(metadataDirectory, "*.json")
                .ToDictionary(
                    path => Path.GetFileNameWithoutExtension(path)!,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var payloadDirectory = Path.Combine(projectDirectory, PayloadDirectoryName);
        var pairedPayloadMetadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(payloadDirectory))
        {
            foreach (var payloadPath in Directory.EnumerateFiles(payloadDirectory, "*.bin"))
            {
                var metadataPath = Path.ChangeExtension(payloadPath, ".json");
                var paths = new List<string> { payloadPath };
                if (File.Exists(metadataPath))
                {
                    paths.Add(metadataPath);
                }

                var key = Path.GetFileNameWithoutExtension(payloadPath);
                if (virtualMetadataByKey.Remove(key, out var virtualMetadataPath))
                {
                    paths.Add(virtualMetadataPath);
                }

                pairedPayloadMetadata.Add(metadataPath);
                candidates.Add(CreateEvictionCandidate(paths));
            }

            foreach (var metadataPath in Directory.EnumerateFiles(payloadDirectory, "*.json"))
            {
                if (!pairedPayloadMetadata.Contains(metadataPath))
                {
                    var paths = new List<string> { metadataPath };
                    var key = Path.GetFileNameWithoutExtension(metadataPath);
                    if (virtualMetadataByKey.Remove(key, out var virtualMetadataPath))
                    {
                        paths.Add(virtualMetadataPath);
                    }

                    candidates.Add(CreateEvictionCandidate(paths));
                }
            }
        }

        var artifactDirectory = Path.Combine(projectDirectory, ArtifactsDirectoryName);
        if (Directory.Exists(artifactDirectory))
        {
            var pairedArtifactMetadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifactPath in Directory.EnumerateFiles(artifactDirectory, "*.bin"))
            {
                var metadataPath = Path.ChangeExtension(artifactPath, ".json");
                var paths = new List<string> { artifactPath };
                if (File.Exists(metadataPath))
                {
                    paths.Add(metadataPath);
                }

                pairedArtifactMetadata.Add(metadataPath);
                candidates.Add(CreateEvictionCandidate(paths, affectsWarmupCapacity: false));
            }

            foreach (var metadataPath in Directory.EnumerateFiles(artifactDirectory, "*.json"))
            {
                if (!pairedArtifactMetadata.Contains(metadataPath))
                {
                    candidates.Add(CreateEvictionCandidate(
                        [metadataPath],
                        affectsWarmupCapacity: false));
                }
            }
        }

        candidates.AddRange(virtualMetadataByKey.Values.Select(path => CreateEvictionCandidate([path])));

        return candidates
            .OrderBy(candidate => candidate.AffectsWarmupCapacity)
            .ThenBy(candidate => candidate.LastUsedUtc)
            .ToArray();
    }

    private static CacheEvictionCandidate CreateEvictionCandidate(
        IReadOnlyList<string> paths,
        bool affectsWarmupCapacity = true)
    {
        var files = paths.Select(path => new FileInfo(path)).Where(file => file.Exists).ToArray();
        return new CacheEvictionCandidate(
            files.Select(file => file.FullName).ToArray(),
            files.Length == 0 ? DateTime.MinValue : files.Max(file => file.LastWriteTimeUtc),
            files.Sum(file => file.Length),
            affectsWarmupCapacity);
    }

    private void DeleteObsoleteProjectCaches(SvCacheProjectContext activeContext)
    {
        if (isReadWorker)
        {
            return;
        }

        if (string.Equals(
                lastObsoleteProjectCleanupKey,
                activeContext.ProjectKey,
                StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(ProjectsPath))
        {
            return;
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(ProjectsPath))
        {
            var directory = new DirectoryInfo(directoryPath);
            if (string.Equals(directory.Name, activeContext.ProjectKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourcePath = Path.Combine(directory.FullName, SourceFileName);
            if (!TryReadCacheSourceFile(sourcePath, out var cached)
                || !HasSameProjectIdentity(cached.Source, activeContext.Source))
            {
                continue;
            }

            TryDeleteDirectory(directory.FullName);
        }

        lastObsoleteProjectCleanupKey = activeContext.ProjectKey;
    }

    private static bool HasSameProjectIdentity(
        SvCacheSourceFingerprint cached,
        SvCacheSourceFingerprint active)
    {
        return string.Equals(cached.SelectedGame, active.SelectedGame, StringComparison.Ordinal)
            && string.Equals(cached.Descriptor.FullPath, active.Descriptor.FullPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(cached.FileSystem.FullPath, active.FileSystem.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadCacheSourceFile(string sourcePath, out SvCacheSourceFile sourceFile)
    {
        if (!File.Exists(sourcePath))
        {
            sourceFile = default!;
            return false;
        }

        try
        {
            using var stream = OpenJsonReadStream(sourcePath);
            var cached = JsonSerializer.Deserialize<SvCacheSourceFile>(stream, JsonOptions);
            if (cached is not null && cached.CacheSchemaVersion == CacheSchemaVersion)
            {
                sourceFile = cached;
                return true;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
        }

        sourceFile = default!;
        return false;
    }

    private static bool TryReadCacheIndexFile(string indexPath, out SvCacheIndexFile cacheIndex)
    {
        if (!File.Exists(indexPath))
        {
            cacheIndex = default!;
            return false;
        }

        try
        {
            using var stream = OpenJsonReadStream(indexPath, MaximumPersistedIndexFileBytes);
            var cached = JsonSerializer.Deserialize<SvCacheIndexFile>(stream, JsonOptions);
            if (cached is not null && cached.CacheSchemaVersion == CacheSchemaVersion)
            {
                cacheIndex = cached;
                return true;
            }
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Corrupt or inaccessible cache files are disposable and ignored here.
        }

        cacheIndex = default!;
        return false;
    }

    private bool TryDeleteDirectory(string path)
    {
        if (isReadWorker)
        {
            return false;
        }

        try
        {
            DeleteDirectoryIfExists(path);
            var removed = !Directory.Exists(path);
            if (IsPersistentCachePath(path))
            {
                retainedPersistentCacheSizeBytes = null;
            }

            return removed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (IsPersistentCachePath(path))
            {
                retainedPersistentCacheSizeBytes = null;
            }
            return false;
        }
    }

    private SvCacheProjectContext? TryCreateActiveProjectContext(ProjectPaths? paths)
    {
        if (paths is null
            || paths.SelectedGame is not ProjectGame.Scarlet and not ProjectGame.Violet
            || string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            || !HasTrinityArchive(paths.BaseRomFsPath))
        {
            return null;
        }

        return CreateProjectContext(paths);
    }

    private SvCacheProjectContext CreateProjectContext(ProjectPaths paths)
    {
        if (paths.SelectedGame is not ProjectGame.Scarlet and not ProjectGame.Violet)
        {
            throw new InvalidOperationException("Scarlet/Violet cache requires a Scarlet or Violet project.");
        }

        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException("Scarlet/Violet cache requires a base RomFS path.");
        }

        var romFsRoot = ResolveRomFsRoot(paths.BaseRomFsPath);
        var descriptorPath = Path.Combine(romFsRoot, "arc", "data.trpfd");
        var fileSystemPath = Path.Combine(romFsRoot, "arc", "data.trpfs");
        var runtimePath = SvCompressionRuntime.TryResolveRequiredFilePath(
            paths.ScarletVioletSupportFolderPath,
            out var resolvedRuntimePath)
            ? resolvedRuntimePath
            : null;
        var source = new SvCacheSourceFingerprint(
            CacheSchemaVersion,
            ParserVersion,
            DecompressorVersion,
            paths.SelectedGame.Value.ToString(),
            CreateFileStamp(descriptorPath),
            CreateFileStamp(fileSystemPath),
            runtimePath is null ? null : CreateFileStamp(runtimePath),
            OutputRoot: null);
        var projectKey = CreateProjectKey(source);
        return new SvCacheProjectContext(
            romFsRoot,
            projectKey,
            Path.Combine(ProjectsPath, projectKey),
            source);
    }

    private static string CreateProjectKey(SvCacheSourceFingerprint source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static SvCacheFileStamp CreateFileStamp(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Scarlet/Violet cache source file was not found.", fileInfo.FullName);
        }

        return new SvCacheFileStamp(
            fileInfo.FullName,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
    }

    private static string ResolveDefaultCacheRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "KM Editor", "ScarletVioletCache");
    }

    private static string ResolveRomFsRoot(string path)
    {
        var descriptorPath = Path.Combine(path, "arc", "data.trpfd");
        if (File.Exists(descriptorPath))
        {
            return Path.GetFullPath(path);
        }

        var nestedRomFsPath = Path.Combine(path, "romfs");
        descriptorPath = Path.Combine(nestedRomFsPath, "arc", "data.trpfd");
        if (File.Exists(descriptorPath))
        {
            return Path.GetFullPath(nestedRomFsPath);
        }

        return Path.GetFullPath(path);
    }

    private static bool HasTrinityArchive(string rootPath)
    {
        return HasTrinityArchiveAt(rootPath)
            || HasTrinityArchiveAt(Path.Combine(rootPath, "romfs"));
    }

    private static bool HasTrinityArchiveAt(string romFsRoot)
    {
        return File.Exists(Path.Combine(romFsRoot, "arc", "data.trpfd"))
            && File.Exists(Path.Combine(romFsRoot, "arc", "data.trpfs"));
    }

    private SvCacheSettings ReadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaultSettings = new SvCacheSettings(SvCacheMode.Balanced, DefaultMaxCacheSizeBytes);
            if (!isReadWorker)
            {
                WriteJsonAtomic(SettingsPath, defaultSettings);
            }

            return defaultSettings;
        }

        try
        {
            using var stream = OpenJsonReadStream(SettingsPath);
            var settings = JsonSerializer.Deserialize<SvCacheSettings>(stream, JsonOptions);
            if (settings is null)
            {
                throw new JsonException("Cache settings file was empty.");
            }

            return settings with { MaxCacheSizeBytes = ClampMaxCacheSize(settings.MaxCacheSizeBytes) };
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            var defaultSettings = new SvCacheSettings(SvCacheMode.Balanced, DefaultMaxCacheSizeBytes);
            if (!isReadWorker)
            {
                WriteJsonAtomic(SettingsPath, defaultSettings);
            }

            return defaultSettings;
        }
    }

    private static long ClampMaxCacheSize(long value)
    {
        return Math.Clamp(value, MinimumMaxCacheSizeBytes, MaximumMaxCacheSizeBytes);
    }

    private void EnsureRoot()
    {
        if (isReadWorker)
        {
            return;
        }

        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(ProjectsPath);
        if (!tempCleanupCompleted)
        {
            CleanupTempDirectory();
            tempCleanupCompleted = true;
        }
    }

    private string SettingsPath => Path.Combine(cacheRoot, SettingsFileName);

    private string ProjectsPath => Path.Combine(cacheRoot, ProjectsDirectoryName);

    private string TempPath => Path.Combine(cacheRoot, TempDirectoryName);

    private long GetCacheContentSize(bool forceRefresh = false)
    {
        if (forceRefresh || retainedPersistentCacheSizeBytes is null)
        {
            retainedPersistentCacheSizeBytes = GetDirectorySize(ProjectsPath);
        }

        return retainedPersistentCacheSizeBytes.Value;
    }

    private static string NormalizeVirtualPath(string virtualPath)
    {
        var normalized = virtualPath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["romfs/".Length..];
        }

        return normalized;
    }

    private static SvTextArtifactDescriptor CreateTextArtifactDescriptor(
        string artifactKey,
        string artifactParserVersion,
        IReadOnlyList<string> baseVirtualPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactParserVersion);
        ArgumentNullException.ThrowIfNull(baseVirtualPaths);
        if (artifactKey.Length > 2048)
        {
            throw new ArgumentException("S/V Text artifact keys cannot exceed 2048 characters.", nameof(artifactKey));
        }

        if (artifactParserVersion.Length > 256)
        {
            throw new ArgumentException(
                "S/V Text artifact parser versions cannot exceed 256 characters.",
                nameof(artifactParserVersion));
        }

        if (baseVirtualPaths.Count is < 1 or > 2)
        {
            throw new ArgumentException(
                "S/V Text artifacts require one DAT source and may include one TBL source.",
                nameof(baseVirtualPaths));
        }

        var normalizedPaths = baseVirtualPaths
            .Select(NormalizeTextArtifactSourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dataPathCount = normalizedPaths.Count(
            path => path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));
        var tablePathCount = normalizedPaths.Count(
            path => path.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase));
        if (normalizedPaths.Length != baseVirtualPaths.Count
            || dataPathCount != 1
            || tablePathCount > 1)
        {
            throw new ArgumentException(
                "S/V Text artifact sources must identify one unique DAT and an optional unique TBL.",
                nameof(baseVirtualPaths));
        }

        return new SvTextArtifactDescriptor(
            artifactKey.Trim(),
            artifactParserVersion.Trim(),
            normalizedPaths);
    }

    private static string NormalizeTextArtifactSourcePath(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        var normalized = NormalizeVirtualPath(virtualPath.Trim());
        var segments = normalized.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
            || !normalized.StartsWith(
                $"{SvMessagePathResolver.MessageRootPath}/",
                StringComparison.OrdinalIgnoreCase)
            || (!normalized.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "S/V Text artifact sources must be safe message DAT or TBL virtual paths.",
                nameof(virtualPath));
        }

        return normalized;
    }

    private static bool AreArtifactSourcesArchiveBacked(
        ProjectPaths paths,
        IReadOnlyList<string> baseVirtualPaths)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            return false;
        }

        try
        {
            var baseRoot = Path.GetFullPath(paths.BaseRomFsPath);
            foreach (var virtualPath in baseVirtualPaths)
            {
                var loosePath = Path.GetFullPath(Path.Combine(
                    baseRoot,
                    virtualPath.Replace('/', Path.DirectorySeparatorChar)));
                var relativePath = Path.GetRelativePath(baseRoot, loosePath);
                if (relativePath.StartsWith("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(relativePath)
                    || IsLooseArtifactSourceFile(loosePath))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsLooseArtifactSourceFile(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) == 0;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string GetVirtualPathKey(string virtualPath)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(virtualPath))).ToLowerInvariant();
    }

    private static string GetPayloadDirectory(SvCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, PayloadDirectoryName);
    }

    private static string GetTextArtifactDirectory(SvCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, ArtifactsDirectoryName);
    }

    private static string GetTextArtifactPayloadPath(
        SvCacheProjectContext context,
        SvTextArtifactDescriptor descriptor)
    {
        return Path.Combine(GetTextArtifactDirectory(context), $"{GetTextArtifactStorageKey(descriptor)}.bin");
    }

    private static string GetTextArtifactMetadataPath(
        SvCacheProjectContext context,
        SvTextArtifactDescriptor descriptor)
    {
        return Path.Combine(GetTextArtifactDirectory(context), $"{GetTextArtifactStorageKey(descriptor)}.json");
    }

    private static string GetTextArtifactStorageKey(SvTextArtifactDescriptor descriptor)
    {
        var identity = string.Join(
            '\n',
            descriptor.ArtifactParserVersion,
            descriptor.ArtifactKey,
            string.Join('\n', descriptor.BaseVirtualPaths));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static string GetPayloadPath(SvCacheProjectContext context, string virtualPath)
    {
        return Path.Combine(GetPayloadDirectory(context), $"{GetVirtualPathKey(virtualPath)}.bin");
    }

    private static string GetPayloadMetadataPath(SvCacheProjectContext context, string virtualPath)
    {
        return Path.Combine(GetPayloadDirectory(context), $"{GetVirtualPathKey(virtualPath)}.json");
    }

    private static string GetMetadataDirectory(SvCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, MetadataDirectoryName);
    }

    private static string GetMetadataPath(SvCacheProjectContext context, string virtualPath)
    {
        return Path.Combine(GetMetadataDirectory(context), $"{GetVirtualPathKey(virtualPath)}.json");
    }

    private static string GetSourcePath(SvCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, SourceFileName);
    }

    private static string GetWarmupPathsPath(SvCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, WarmupPathsFileName);
    }

    private static string GetWarmupStatePath(SvCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, WarmupStateFileName);
    }

    private void TouchProjectDirectory(SvCacheProjectContext context)
    {
        if (isReadWorker)
        {
            return;
        }

        Directory.CreateDirectory(context.ProjectDirectory);
        Directory.SetLastWriteTimeUtc(context.ProjectDirectory, DateTime.UtcNow);
    }

    private void WriteJsonAtomic<TValue>(string path, TValue value)
    {
        EnsureOwnerCacheMutation("publish S/V persistent cache data");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(TempPath);
        var tempPath = Path.Combine(
            TempPath,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.SequentialScan))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
            }

            var previousLength = GetTrackedFileLength(path);
            File.Move(tempPath, path, overwrite: true);
            TrackPersistentFileReplacement(path, previousLength);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static FileStream OpenJsonReadStream(
        string path,
        long maximumBytes = MaximumCacheJsonFileBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        try
        {
            if (stream.Length < 0 || stream.Length > maximumBytes)
            {
                throw new IOException(
                    $"Cache JSON file exceeds the safe read limit of {maximumBytes} bytes.");
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static byte[] ReadAllBytesShared(string path, long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length < 0 || stream.Length > maximumBytes || stream.Length > int.MaxValue)
        {
            throw new InvalidDataException(
                $"The S/V cache payload exceeds its safe read limit of {maximumBytes} bytes.");
        }

        var bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void WriteBytesAtomic(string path, byte[] bytes)
    {
        EnsureOwnerCacheMutation("publish S/V persistent cache data");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(TempPath);
        var tempPath = Path.Combine(
            TempPath,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            var previousLength = GetTrackedFileLength(path);
            File.Move(tempPath, path, overwrite: true);
            TrackPersistentFileReplacement(path, previousLength);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private void CleanupTempDirectory()
    {
        if (isReadWorker)
        {
            return;
        }

        if (!Directory.Exists(TempPath))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - OrphanTempFileAge;
        foreach (var path in Directory.EnumerateFiles(TempPath))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) <= cutoff)
                {
                    File.Delete(path);
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

    private static void TouchCacheFile(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private bool TryDeleteFile(string path)
    {
        if (isReadWorker)
        {
            return false;
        }

        var previousLength = GetTrackedFileLength(path);
        try
        {
            File.Delete(path);
            var removed = !File.Exists(path);
            if (removed && previousLength > 0 && retainedPersistentCacheSizeBytes is not null)
            {
                retainedPersistentCacheSizeBytes = Math.Max(
                    0,
                    retainedPersistentCacheSizeBytes.Value - previousLength);
            }

            return removed;
        }
        catch (IOException)
        {
            retainedPersistentCacheSizeBytes = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            retainedPersistentCacheSizeBytes = null;
            return false;
        }
    }

    private long GetTrackedFileLength(string path)
    {
        if (retainedPersistentCacheSizeBytes is null || !IsPersistentCachePath(path))
        {
            return 0;
        }

        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            retainedPersistentCacheSizeBytes = null;
            return 0;
        }
    }

    private void TrackPersistentFileReplacement(string path, long previousLength)
    {
        if (retainedPersistentCacheSizeBytes is null || !IsPersistentCachePath(path))
        {
            return;
        }

        try
        {
            var currentLength = new FileInfo(path).Length;
            retainedPersistentCacheSizeBytes = Math.Max(
                0,
                retainedPersistentCacheSizeBytes.Value - previousLength + currentLength);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            retainedPersistentCacheSizeBytes = null;
        }
    }

    private bool IsPersistentCachePath(string path)
    {
        var projectsRoot = Path.GetFullPath(ProjectsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(projectsRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureOwnerCacheMutation(string operation)
    {
        if (isReadWorker)
        {
            throw new InvalidOperationException(
                $"An isolated managed read worker cannot {operation}.");
        }
    }

    private static long GetDirectorySize(string path)
    {
        FileAttributes rootAttributes;
        try
        {
            rootAttributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return 0;
        }
        catch (Exception exception) when (IsCacheTraversalFailure(exception))
        {
            throw new IOException("The S/V cache directory could not be inspected safely.", exception);
        }

        if ((rootAttributes & FileAttributes.Directory) == 0)
        {
            throw new InvalidDataException("The configured S/V cache directory is not a directory.");
        }

        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The configured S/V cache directory cannot be a symbolic link or reparse point.");
        }

        var pendingDirectories = new Stack<(DirectoryInfo Directory, int Depth)>();
        pendingDirectories.Push((new DirectoryInfo(path), 0));
        var entryCount = 0;
        long total = 0;
        while (pendingDirectories.TryPop(out var pendingDirectory))
        {
            var remainingEntryCapacity = MaximumCacheTraversalEntries - entryCount;
            FileSystemInfo[] entries;
            try
            {
                entries = pendingDirectory.Directory
                    .EnumerateFileSystemInfos("*", CacheDirectoryEnumeration)
                    .Take(remainingEntryCapacity + 1)
                    .ToArray();
            }
            catch (Exception exception) when (IsCacheTraversalFailure(exception))
            {
                throw new IOException(
                    $"The S/V cache directory tree could not be enumerated safely at depth {pendingDirectory.Depth}.",
                    exception);
            }

            if (entries.Length > remainingEntryCapacity)
            {
                throw new InvalidDataException(
                    $"The S/V cache directory contains more than the supported {MaximumCacheTraversalEntries:N0} entries.");
            }

            entryCount += entries.Length;
            Array.Sort(entries, static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            var childDirectories = new List<DirectoryInfo>();
            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = entry.Attributes;
                }
                catch (Exception exception) when (IsCacheTraversalFailure(exception))
                {
                    throw new IOException(
                        $"An entry in the S/V cache directory could not be inspected safely at depth {pendingDirectory.Depth}.",
                        exception);
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (pendingDirectory.Depth >= MaximumCacheTraversalDepth)
                    {
                        throw new InvalidDataException(
                            $"The S/V cache directory exceeds the supported traversal depth of {MaximumCacheTraversalDepth}.");
                    }

                    childDirectories.Add(entry as DirectoryInfo ?? new DirectoryInfo(entry.FullName));
                    continue;
                }

                long fileLength;
                try
                {
                    fileLength = (entry as FileInfo ?? new FileInfo(entry.FullName)).Length;
                }
                catch (Exception exception) when (IsCacheTraversalFailure(exception))
                {
                    throw new IOException(
                        $"A file in the S/V cache directory could not be measured safely at depth {pendingDirectory.Depth}.",
                        exception);
                }

                try
                {
                    total = checked(total + fileLength);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException("The S/V cache directory size exceeds the supported range.", exception);
                }
            }

            for (var index = childDirectories.Count - 1; index >= 0; index--)
            {
                pendingDirectories.Push((childDirectories[index], pendingDirectory.Depth + 1));
            }
        }

        return total;
    }

    private static bool IsCacheTraversalFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or ArgumentException
            or NotSupportedException;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }

    private sealed record SvCacheIndexFile(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source,
        SvTrinityArchiveIndex Index);

    private sealed record SvCacheSourceFile(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source);

    private sealed record SvCacheWarmupPathsFile(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source,
        IReadOnlyList<string> VirtualPaths);

    private sealed record SvCacheWarmupStateFile(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source,
        SvCacheMode Mode,
        long MaxCacheSizeBytes,
        bool CapacityLimited);

    private sealed record SvCachePayloadMetadata(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source,
        string VirtualPath,
        long DecompressedSize,
        DateTimeOffset CreatedAtUtc);

    private sealed record SvCacheTextArtifactMetadata(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source,
        string ArtifactKey,
        string ArtifactParserVersion,
        IReadOnlyList<string> BaseVirtualPaths,
        long PayloadSize,
        string PayloadSha256,
        DateTimeOffset CreatedAtUtc);

    private sealed record SvCacheVirtualFileMetadata(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source,
        string VirtualPath,
        DateTimeOffset CreatedAtUtc);

    private sealed record SvCacheProjectContext(
        string RomFsRootPath,
        string ProjectKey,
        string ProjectDirectory,
        SvCacheSourceFingerprint Source);

    private sealed record SvTextArtifactDescriptor(
        string ArtifactKey,
        string ArtifactParserVersion,
        IReadOnlyList<string> BaseVirtualPaths);

    private sealed record CacheEvictionCandidate(
        IReadOnlyList<string> Paths,
        DateTime LastUsedUtc,
        long SizeBytes,
        bool AffectsWarmupCapacity);
}

public sealed record SvCacheSettings(
    SvCacheMode Mode,
    long MaxCacheSizeBytes);

public sealed record SvCacheStatus(
    SvCacheSettings Settings,
    long CacheSizeBytes,
    int WarmupCompleted,
    int WarmupTotal,
    int ProgressPercent,
    string Phase,
    string Message,
    bool IsActiveProjectPreserved);

public sealed record SvCacheSourceFingerprint(
    int CacheSchemaVersion,
    string ParserVersion,
    string DecompressorVersion,
    string SelectedGame,
    SvCacheFileStamp Descriptor,
    SvCacheFileStamp FileSystem,
    SvCacheFileStamp? CompressionRuntime,
    SvCacheDirectoryStamp? OutputRoot);

public sealed record SvCacheFileStamp(
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record SvCacheDirectoryStamp(
    string FullPath,
    bool Exists,
    long FileCount,
    long TotalSizeBytes,
    DateTime LastWriteTimeUtc,
    string ContentFingerprint,
    int InaccessibleEntryCount);
