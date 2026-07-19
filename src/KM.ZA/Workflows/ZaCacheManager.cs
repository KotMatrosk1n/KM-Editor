// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Core.Projects;
using KM.Formats.ZA;
using KM.ZA.Data;

namespace KM.ZA.Workflows;

public enum ZaCacheMode
{
    Minimal,
    Balanced,
    Performance,
}

public sealed class ZaCacheManager
{
    public const int CacheSchemaVersion = 1;
    public const string ParserVersion = "za-cache-parser-v1";
    public const string DecompressorVersion = "za-cache-decompressor-v1";

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
    private const string MetadataDirectoryName = "metadata";
    private const int TextWarmupBatchSize = 8;
    private static readonly TimeSpan WarmupStepTimeBudget = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan OrphanTempFileAge = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly EnumerationOptions RecursiveCacheEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };
    private static readonly IReadOnlyList<string> WarmupTextLanguages =
        ZaGameTextLanguage.SupportedMessageLanguages;
    private static readonly IReadOnlyList<string> CoreWarmupVirtualPaths = CreateCoreWarmupVirtualPaths();
    private static readonly IReadOnlyList<string> LabelWarmupVirtualPaths = CreateLabelWarmupVirtualPaths();

    private readonly string cacheRoot;
    private readonly object syncRoot = new();
    private ZaCacheSourceFingerprint? retainedIndexSource;
    private ZaTrinityArchiveIndex? retainedIndex;
    private HashSet<ulong>? retainedFileHashes;
    private IReadOnlyList<string>? retainedPackNames;
    private ZaCacheSourceFingerprint? retainedWarmupPathsSource;
    private IReadOnlyList<string>? retainedWarmupVirtualPaths;
    private ZaCacheSourceFingerprint? retainedWarmupProgressSource;
    private ZaCacheMode? retainedWarmupProgressMode;
    private IReadOnlyList<string>? retainedWarmupProgressPaths;
    private HashSet<string>? retainedCompletedWarmupPaths;
    private long? retainedPersistentCacheSizeBytes;
    private string? lastObsoleteProjectCleanupKey;
    private bool tempCleanupCompleted;

    public ZaCacheManager(string? cacheRoot = null)
    {
        this.cacheRoot = cacheRoot ?? ResolveDefaultCacheRoot();
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
        options.Converters.Add(new JsonStringEnumConverter<ZaCacheMode>(JsonNamingPolicy.CamelCase));
        return options;
    }

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
                persistToDisk: settings.Mode != ZaCacheMode.Minimal,
                out var cacheChanged);
            if (cacheChanged)
            {
                PruneIfNeeded(settings, context);
            }

            return warmupPaths;
        }
    }

    public ZaCacheSettings GetSettings()
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
            ZaDataPaths.PersonalArray,
            ZaDataPaths.MoveDataArray,
            ZaDataPaths.ItemDataArray,
            ZaDataPaths.EvolutionItemConversionArray,
            ZaDataPaths.TrainerDataArray,
            ZaDataPaths.PokemonDataArray,
            ZaDataPaths.EncountDataArray,
            ZaDataPaths.PokemonSpawnerDataArray,
            ZaDataPaths.PokemonSpawnerTransformArray,
            ZaDataPaths.ItemBallSpawnerDataArray,
            ZaDataPaths.ItemBallSpawnerTransformArray,
            ZaDataPaths.RandomPopItemSpawnerDataArray,
            ZaDataPaths.BattleTrainerSpawnerDataArray,
            ZaDataPaths.FieldWazagimmickPublic,
            ZaDataPaths.AiAttackParamArray,
            ZaDataPaths.AiBulletParamArray,
            ZaDataPaths.ShopItemArray,
            ZaDataPaths.ShopItemLineupArray,
            ZaDataPaths.ShopDressUpArray,
            ZaDataPaths.ShopDressUpLineupArray,
            ZaDataPaths.ShopHairMakeLineupArray,
            ZaDataPaths.DressUpDataArray,
            ZaDataPaths.HairMakeDataArray,
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
        foreach (var language in WarmupTextLanguages)
        {
            yield return ZaDataPaths.ItemNames(language);
            yield return ZaDataPaths.MoveNames(language);
            yield return ZaDataPaths.MoveDescriptions(language);
            yield return ZaDataPaths.PokemonNames(language);
            yield return ZaDataPaths.AbilityNames(language);
            yield return ZaDataPaths.PlaceNames(language);
            yield return ZaDataPaths.PlaceNameKeys(language);
            yield return ZaDataPaths.TrainerNames(language);
            yield return ZaDataPaths.TrainerNameKeys(language);
            yield return ZaDataPaths.TrainerTypes(language);
            yield return ZaDataPaths.TrainerTypeKeys(language);
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
        ZaCacheProjectContext context,
        bool persistToDisk = true)
    {
        return GetWarmupVirtualPaths(context, persistToDisk, out _);
    }

    private IReadOnlyList<string> GetWarmupVirtualPaths(
        ZaCacheProjectContext context,
        bool persistToDisk,
        out bool cacheChanged)
    {
        try
        {
            var index = GetOrBuildIndex(context, persistToDisk, out cacheChanged);
            var warmupPaths = GetOrCreateWarmupVirtualPaths(context, index);
            if (persistToDisk)
            {
                cacheChanged |= EnsureWarmupPathsManifestIsPersisted(context, warmupPaths);
            }

            return warmupPaths;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            cacheChanged = false;
            return WarmupVirtualPaths;
        }
    }

    private static IEnumerable<string> CreateDiscoveredMessageWarmupPaths(
        IReadOnlyList<string> packNames)
    {
        foreach (var language in WarmupTextLanguages)
        {
            foreach (var packName in packNames)
            {
                var virtualPath = ZaMessagePathResolver.TryCreateMessageDatPathFromPackName(packName, language);
                if (!string.IsNullOrWhiteSpace(virtualPath))
                {
                    yield return virtualPath;
                }
            }
        }
    }

    public ZaCacheSettings UpdateSettings(ZaCacheMode mode, long maxCacheSizeBytes, ProjectPaths? activePaths = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            var previousSettings = ReadSettings();
            var settings = new ZaCacheSettings(
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

    public ZaCacheStatus GetStatus(ProjectPaths? paths = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            var settings = ReadSettings();
            var context = TryCreateActiveProjectContext(paths);
            if (context is not null)
            {
                DeleteObsoleteProjectCaches(context);
                if (Directory.Exists(context.ProjectDirectory))
                {
                    EnsureSourceManifestIsPersisted(context);
                }
            }

            PruneIfNeeded(settings, context, forceSizeRefresh: true);
            return CreateStatus(settings, context, activeProjectPreserved: false);
        }
    }

    public ZaCacheStatus Clear(ProjectPaths? activePaths = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            var settings = ReadSettings();
            var activeContext = TryCreateActiveProjectContext(activePaths);

            ClearMemoryCacheCore();
            DeleteDirectoryIfExists(ProjectsPath);
            DeleteDirectoryIfExists(TempPath);
            Directory.CreateDirectory(ProjectsPath);
            retainedPersistentCacheSizeBytes = 0;
            return CreateStatus(settings, activeContext, activeProjectPreserved: false);
        }
    }

    public void ClearMemoryCache()
    {
        lock (syncRoot)
        {
            ClearMemoryCacheCore();
        }
    }

    public ZaCacheStatus WarmupStep(ProjectPaths paths, int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(paths);

        lock (syncRoot)
        {
            EnsureRoot();
            var settings = ReadSettings();
            var context = TryCreateActiveProjectContext(paths);
            if (context is null || settings.Mode == ZaCacheMode.Minimal)
            {
                return CreateStatus(settings, context, activeProjectPreserved: false);
            }

            DeleteObsoleteProjectCaches(context);
            if (IsWarmupCapacityLimited(settings, context))
            {
                return CreateStatus(settings, context, activeProjectPreserved: false);
            }

            var stopwatch = Stopwatch.StartNew();
            var warmupVirtualPaths = GetWarmupVirtualPaths(context);
            if (warmupVirtualPaths.Count == 0)
            {
                return CreateStatus(settings, context, activeProjectPreserved: false);
            }

            var completedPaths = GetOrCreateCompletedWarmupPaths(settings, context, warmupVirtualPaths);
            var batch = GetWarmupBatch(warmupVirtualPaths, completedPaths, stepIndex);
            if (batch.Count == 0)
            {
                return CreateStatus(settings, context, activeProjectPreserved: false);
            }

            IReadOnlyList<string> processedPaths;
            if (settings.Mode == ZaCacheMode.Performance)
            {
                processedPaths = WarmupPerformanceBatch(paths, context, batch, stopwatch);
            }
            else
            {
                var processed = new List<string>(batch.Count);
                for (var index = 0; index < batch.Count; index++)
                {
                    if (index > 0 && stopwatch.Elapsed >= WarmupStepTimeBudget)
                    {
                        break;
                    }

                    WriteVirtualMetadata(context, batch[index]);
                    processed.Add(batch[index]);
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

            return CreateStatus(settings, context, activeProjectPreserved: false);
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

            if (settings.Mode == ZaCacheMode.Performance
                && TryReadPayload(context, normalizedVirtualPath, out var cachedBytes))
            {
                EnsureSourceManifestIsPersisted(context);
                TouchProjectDirectory(context);
                return cachedBytes;
            }

            var index = GetOrBuildIndex(
                context,
                persistToDisk: settings.Mode != ZaCacheMode.Minimal,
                out var cacheChanged);

            using var archive = ZaTrinityArchive.Open(
                paths.BaseRomFsPath!,
                paths.PokemonLegendsZASupportFolderPath,
                index: index);
            var bytes = archive.ReadFile(normalizedVirtualPath);

            if (settings.Mode == ZaCacheMode.Performance)
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
            var fileHash = ZaTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath));
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

    private ZaTrinityArchiveIndex GetOrBuildIndex(ZaCacheProjectContext context)
    {
        return GetOrBuildIndex(context, persistToDisk: true);
    }

    private ZaTrinityArchiveIndex GetOrBuildIndex(
        ZaCacheProjectContext context,
        bool persistToDisk)
    {
        return GetOrBuildIndex(context, persistToDisk, out _);
    }

    private ZaTrinityArchiveIndex GetOrBuildIndex(
        ZaCacheProjectContext context,
        bool persistToDisk,
        out bool cacheChanged)
    {
        if (TryGetRetainedIndex(context, out var retained))
        {
            cacheChanged = persistToDisk && EnsureRetainedIndexIsPersisted(context, retained);

            return retained;
        }

        if (!persistToDisk)
        {
            var transientIndex = CompactIndex(ZaTrinityArchive.BuildIndex(context.RomFsRootPath));
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

        var index = CompactIndex(ZaTrinityArchive.BuildIndex(context.RomFsRootPath));
        var indexFile = new ZaCacheIndexFile(
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

    private bool TryReadCachedIndex(ZaCacheProjectContext context, out ZaTrinityArchiveIndex index)
    {
        if (TryGetRetainedIndex(context, out index))
        {
            return true;
        }

        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);
        if (TryReadCacheIndexFile(indexPath, out var cached)
            && cached.Source == context.Source
            && cached.Index.SchemaVersion == ZaTrinityArchive.IndexSchemaVersion)
        {
            index = CompactIndex(cached.Index);
            RetainIndex(context, index);
            return true;
        }

        index = default!;
        return false;
    }

    private ZaTrinityArchiveIndex GetBaseTrinityIndex(
        ProjectPaths paths,
        out ZaCacheProjectContext context)
    {
        var settings = ReadSettings();
        context = CreateProjectContext(paths);
        DeleteObsoleteProjectCaches(context);
        var index = GetOrBuildIndex(
            context,
            persistToDisk: settings.Mode != ZaCacheMode.Minimal,
            out var cacheChanged);
        if (cacheChanged)
        {
            PruneIfNeeded(settings, context);
        }

        return index;
    }

    private bool TryGetRetainedIndex(ZaCacheProjectContext context, out ZaTrinityArchiveIndex index)
    {
        if (retainedIndex is not null && retainedIndexSource == context.Source)
        {
            index = retainedIndex;
            return true;
        }

        index = default!;
        return false;
    }

    private void RetainIndex(ZaCacheProjectContext context, ZaTrinityArchiveIndex index)
    {
        retainedIndexSource = context.Source;
        retainedIndex = index;
        retainedFileHashes = null;
        retainedPackNames = null;
        retainedWarmupPathsSource = null;
        retainedWarmupVirtualPaths = null;
    }

    private bool EnsureRetainedIndexIsPersisted(
        ZaCacheProjectContext context,
        ZaTrinityArchiveIndex index)
    {
        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);
        var changed = false;
        if (!File.Exists(indexPath))
        {
            Directory.CreateDirectory(context.ProjectDirectory);
            WriteJsonAtomic(
                indexPath,
                new ZaCacheIndexFile(CacheSchemaVersion, context.Source, index));
            changed = true;
        }

        changed |= EnsureSourceManifestIsPersisted(context);
        if (changed)
        {
            TouchProjectDirectory(context);
        }

        return changed;
    }

    private bool EnsureSourceManifestIsPersisted(ZaCacheProjectContext context)
    {
        var sourcePath = GetSourcePath(context);
        if (File.Exists(sourcePath))
        {
            return false;
        }

        WriteSourceManifest(context);
        return true;
    }

    private void WriteSourceManifest(ZaCacheProjectContext context)
    {
        WriteJsonAtomic(
            GetSourcePath(context),
            new ZaCacheSourceFile(CacheSchemaVersion, context.Source));
    }

    private bool EnsureWarmupPathsManifestIsPersisted(
        ZaCacheProjectContext context,
        IReadOnlyList<string> virtualPaths)
    {
        var manifestPath = GetWarmupPathsPath(context);
        if (File.Exists(manifestPath))
        {
            return false;
        }

        WriteJsonAtomic(
            manifestPath,
            new ZaCacheWarmupPathsFile(CacheSchemaVersion, context.Source, virtualPaths));
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
        retainedWarmupProgressSource = null;
        retainedWarmupProgressMode = null;
        retainedWarmupProgressPaths = null;
        retainedCompletedWarmupPaths = null;
    }

    private HashSet<ulong> GetOrCreateFileHashes(
        ZaCacheProjectContext context,
        ZaTrinityArchiveIndex index)
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
        ZaCacheProjectContext context,
        ZaTrinityArchiveIndex index)
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
        ZaCacheProjectContext context,
        ZaTrinityArchiveIndex index)
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
                ZaTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath))))
            .ToArray();
        if (retainedIndexSource == context.Source && ReferenceEquals(retainedIndex, index))
        {
            retainedWarmupPathsSource = context.Source;
            retainedWarmupVirtualPaths = paths;
        }

        return paths;
    }

    private static ZaTrinityArchiveIndex CompactIndex(ZaTrinityArchiveIndex index)
    {
        var packNames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (index.Files is List<ZaTrinityArchiveFileIndexEntry> mutableFiles)
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

        var compactedFiles = new ZaTrinityArchiveFileIndexEntry[index.Files.Count];
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
            ? new ZaTrinityArchiveIndex(index.SchemaVersion, compactedFiles, index.Packs)
            : index;
    }

    private void WriteVirtualMetadata(ZaCacheProjectContext context, string virtualPath)
    {
        Directory.CreateDirectory(GetMetadataDirectory(context));
        var normalized = NormalizeVirtualPath(virtualPath);
        var metadataPath = GetMetadataPath(context, normalized);
        var metadata = new ZaCacheVirtualFileMetadata(
            CacheSchemaVersion,
            context.Source,
            normalized,
            DateTimeOffset.UtcNow);
        WriteJsonAtomic(metadataPath, metadata);
        TouchProjectDirectory(context);
    }

    private bool TryReadPayload(ZaCacheProjectContext context, string virtualPath, out byte[] bytes)
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
            ZaCachePayloadMetadata? metadata;
            using (var stream = OpenJsonReadStream(metadataPath))
            {
                metadata = JsonSerializer.Deserialize<ZaCachePayloadMetadata>(stream, JsonOptions);
            }

            if (metadata is null
                || metadata.CacheSchemaVersion != CacheSchemaVersion
                || metadata.Source != context.Source
                || !string.Equals(metadata.VirtualPath, virtualPath, StringComparison.Ordinal))
            {
                bytes = [];
                return false;
            }

            bytes = File.ReadAllBytes(payloadPath);
            if (bytes.LongLength != metadata.DecompressedSize)
            {
                bytes = [];
                return false;
            }

            TouchCacheFile(payloadPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            bytes = [];
            return false;
        }
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

        var firstPath = NormalizeVirtualPath(warmupVirtualPaths[firstIndex]);
        if (!IsTextMessagePath(firstPath))
        {
            return [firstPath];
        }

        var batch = new List<string>(TextWarmupBatchSize);
        for (var offset = 0; offset < warmupVirtualPaths.Count && batch.Count < TextWarmupBatchSize; offset++)
        {
            var index = (firstIndex + offset) % warmupVirtualPaths.Count;
            var virtualPath = NormalizeVirtualPath(warmupVirtualPaths[index]);
            if (!IsTextMessagePath(virtualPath))
            {
                continue;
            }

            if (!completedPaths.Contains(virtualPath))
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
        ZaCacheSettings settings,
        ZaCacheProjectContext context,
        string virtualPath)
    {
        if (!File.Exists(GetMetadataPath(context, virtualPath)))
        {
            return false;
        }

        return settings.Mode != ZaCacheMode.Performance || File.Exists(GetPayloadPath(context, virtualPath));
    }

    private static bool IsWarmupPayloadComplete(ZaCacheProjectContext context, string virtualPath)
    {
        return File.Exists(GetMetadataPath(context, virtualPath))
            && File.Exists(GetPayloadPath(context, virtualPath));
    }

    private IReadOnlyList<string> WarmupPerformanceBatch(
        ProjectPaths paths,
        ZaCacheProjectContext context,
        IReadOnlyList<string> virtualPaths,
        Stopwatch stopwatch)
    {
        var processed = new List<string>(virtualPaths.Count);
        var index = GetOrBuildIndex(context);
        using var archive = ZaTrinityArchive.Open(
            paths.BaseRomFsPath!,
            paths.PokemonLegendsZASupportFolderPath,
            index: index);

        for (var pathIndex = 0; pathIndex < virtualPaths.Count; pathIndex++)
        {
            if (pathIndex > 0 && stopwatch.Elapsed >= WarmupStepTimeBudget)
            {
                break;
            }

            var virtualPath = virtualPaths[pathIndex];
            if (IsWarmupPayloadComplete(context, virtualPath))
            {
                processed.Add(virtualPath);
                continue;
            }

            if (!archive.TryReadFile(virtualPath, out var bytes))
            {
                continue;
            }

            WriteVirtualMetadata(context, virtualPath);
            WritePayload(context, virtualPath, bytes);
            processed.Add(virtualPath);
        }

        return processed;
    }

    private void WritePayload(ZaCacheProjectContext context, string virtualPath, byte[] bytes)
    {
        Directory.CreateDirectory(GetPayloadDirectory(context));
        var payloadPath = GetPayloadPath(context, virtualPath);
        var metadataPath = GetPayloadMetadataPath(context, virtualPath);
        var metadata = new ZaCachePayloadMetadata(
            CacheSchemaVersion,
            context.Source,
            virtualPath,
            bytes.LongLength,
            DateTimeOffset.UtcNow);

        WriteBytesAtomic(payloadPath, bytes);
        WriteJsonAtomic(metadataPath, metadata);
        TouchProjectDirectory(context);
    }

    private ZaCacheStatus CreateStatus(
        ZaCacheSettings settings,
        ZaCacheProjectContext? context,
        bool activeProjectPreserved)
    {
        var cacheSize = GetCacheContentSize();
        var warmupVirtualPaths = context is not null && settings.Mode != ZaCacheMode.Minimal
            ? GetWarmupVirtualPathsForStatus(context)
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
            : (int)Math.Round(completed * 100.0 / total, MidpointRounding.AwayFromZero);
        var phase = settings.Mode == ZaCacheMode.Minimal
            ? "Minimal mode"
            : capacityLimited || completed >= total && total > 0
                ? "Cache ready"
                : completed == 0
                    ? "Checking cache"
                    : settings.Mode == ZaCacheMode.Performance
                        ? "Caching Trinity payloads"
                        : "Indexing Trinity files";
        var message = settings.Mode switch
        {
            ZaCacheMode.Minimal => "Session only cache mode is active.",
            _ when capacityLimited => "The configured cache limit is ready with a bounded working set; uncached files load on demand.",
            ZaCacheMode.Balanced when total > 0 && completed >= total => "Balanced cache metadata is ready.",
            ZaCacheMode.Balanced => "Building Pokemon Legends Z-A cache metadata.",
            ZaCacheMode.Performance when total > 0 && completed >= total => "Performance cache payloads are ready.",
            ZaCacheMode.Performance => "Building Pokemon Legends Z-A decompressed payload cache.",
            _ => "Pokemon Legends Z-A cache is idle.",
        };

        return new ZaCacheStatus(
            settings,
            cacheSize,
            completed,
            total,
            Math.Clamp(percent, 0, 100),
            phase,
            message,
            activeProjectPreserved);
    }

    private IReadOnlyList<string> GetWarmupVirtualPathsForStatus(ZaCacheProjectContext context)
    {
        if (retainedWarmupVirtualPaths is not null && retainedWarmupPathsSource == context.Source)
        {
            return retainedWarmupVirtualPaths;
        }

        var manifestPath = GetWarmupPathsPath(context);
        try
        {
            if (File.Exists(manifestPath))
            {
                using var stream = OpenJsonReadStream(manifestPath);
                var manifest = JsonSerializer.Deserialize<ZaCacheWarmupPathsFile>(stream, JsonOptions);
                if (manifest is not null
                    && manifest.CacheSchemaVersion == CacheSchemaVersion
                    && manifest.Source == context.Source)
                {
                    retainedWarmupPathsSource = context.Source;
                    retainedWarmupVirtualPaths = manifest.VirtualPaths;
                    return manifest.VirtualPaths;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
        }

        return WarmupVirtualPaths;
    }

    private bool IsWarmupCapacityLimited(ZaCacheSettings settings, ZaCacheProjectContext context)
    {
        var statePath = GetWarmupStatePath(context);
        if (!File.Exists(statePath))
        {
            return false;
        }

        try
        {
            using var stream = OpenJsonReadStream(statePath);
            var state = JsonSerializer.Deserialize<ZaCacheWarmupStateFile>(stream, JsonOptions);
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

    private void WriteWarmupCapacityState(ZaCacheSettings settings, ZaCacheProjectContext context)
    {
        WriteJsonAtomic(
            GetWarmupStatePath(context),
            new ZaCacheWarmupStateFile(
                CacheSchemaVersion,
                context.Source,
                settings.Mode,
                settings.MaxCacheSizeBytes,
                CapacityLimited: true));
        TouchProjectDirectory(context);
    }

    private HashSet<string> GetOrCreateCompletedWarmupPaths(
        ZaCacheSettings settings,
        ZaCacheProjectContext context,
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
        retainedCompletedWarmupPaths = warmupVirtualPaths
            .Select(NormalizeVirtualPath)
            .Where(path => IsWarmupEntryComplete(settings, context, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return retainedCompletedWarmupPaths;
    }

    private bool PruneIfNeeded(
        ZaCacheSettings settings,
        ZaCacheProjectContext? activeContext,
        bool forceSizeRefresh = false)
    {
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

            currentSize = GetCacheContentSize();
            if (currentSize <= settings.MaxCacheSizeBytes)
            {
                MarkWarmupCapacityLimitedAfterEviction(settings, activeContext, activeEntriesEvicted);
                return activeEntriesEvicted;
            }
        }

        if (currentSize > settings.MaxCacheSizeBytes && Directory.Exists(activeProjectDirectory))
        {
            if (TryDeleteDirectory(activeProjectDirectory))
            {
                ClearMemoryCacheCore();
                activeEntriesEvicted = true;
            }
        }

        MarkWarmupCapacityLimitedAfterEviction(settings, activeContext, activeEntriesEvicted);
        return activeEntriesEvicted;
    }

    private void MarkWarmupCapacityLimitedAfterEviction(
        ZaCacheSettings settings,
        ZaCacheProjectContext? activeContext,
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
        if (activeContext is not null && settings.Mode != ZaCacheMode.Minimal)
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

        candidates.AddRange(virtualMetadataByKey.Values.Select(path => CreateEvictionCandidate([path])));

        return candidates.OrderBy(candidate => candidate.LastUsedUtc).ToArray();
    }

    private static CacheEvictionCandidate CreateEvictionCandidate(IReadOnlyList<string> paths)
    {
        var files = paths.Select(path => new FileInfo(path)).Where(file => file.Exists).ToArray();
        return new CacheEvictionCandidate(
            files.Select(file => file.FullName).ToArray(),
            files.Length == 0 ? DateTime.MinValue : files.Max(file => file.LastWriteTimeUtc),
            files.Sum(file => file.Length));
    }

    private void DeleteObsoleteProjectCaches(ZaCacheProjectContext activeContext)
    {
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
        ZaCacheSourceFingerprint cached,
        ZaCacheSourceFingerprint active)
    {
        return string.Equals(cached.SelectedGame, active.SelectedGame, StringComparison.Ordinal)
            && string.Equals(cached.Descriptor.FullPath, active.Descriptor.FullPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(cached.FileSystem.FullPath, active.FileSystem.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadCacheSourceFile(string sourcePath, out ZaCacheSourceFile sourceFile)
    {
        if (!File.Exists(sourcePath))
        {
            sourceFile = default!;
            return false;
        }

        try
        {
            using var stream = OpenJsonReadStream(sourcePath);
            var cached = JsonSerializer.Deserialize<ZaCacheSourceFile>(stream, JsonOptions);
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

    private static bool TryReadCacheIndexFile(string indexPath, out ZaCacheIndexFile cacheIndex)
    {
        if (!File.Exists(indexPath))
        {
            cacheIndex = default!;
            return false;
        }

        try
        {
            using var stream = OpenJsonReadStream(indexPath);
            var cached = JsonSerializer.Deserialize<ZaCacheIndexFile>(stream, JsonOptions);
            if (cached is not null && cached.CacheSchemaVersion == CacheSchemaVersion)
            {
                cacheIndex = cached;
                return true;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or inaccessible cache files are disposable and ignored here.
        }

        cacheIndex = default!;
        return false;
    }

    private bool TryDeleteDirectory(string path)
    {
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

    private ZaCacheProjectContext? TryCreateActiveProjectContext(ProjectPaths? paths)
    {
        if (paths is null
            || paths.SelectedGame is not ProjectGame.ZA
            || string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            || !HasTrinityArchive(paths.BaseRomFsPath))
        {
            return null;
        }

        try
        {
            return CreateProjectContext(paths);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    private ZaCacheProjectContext CreateProjectContext(ProjectPaths paths)
    {
        if (paths.SelectedGame is not ProjectGame.ZA)
        {
            throw new InvalidOperationException("Pokemon Legends Z-A cache requires a Pokemon Legends Z-A project.");
        }

        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A cache requires a base RomFS path.");
        }

        var romFsRoot = ResolveRomFsRoot(paths.BaseRomFsPath);
        var descriptorPath = Path.Combine(romFsRoot, "arc", "data.trpfd");
        var fileSystemPath = Path.Combine(romFsRoot, "arc", "data.trpfs");
        var runtimePath = ZaCompressionRuntime.TryResolveRequiredFilePath(
            paths.PokemonLegendsZASupportFolderPath,
            out var resolvedRuntimePath)
            ? resolvedRuntimePath
            : null;
        var source = new ZaCacheSourceFingerprint(
            CacheSchemaVersion,
            ParserVersion,
            DecompressorVersion,
            paths.SelectedGame.Value.ToString(),
            CreateFileStamp(descriptorPath),
            CreateFileStamp(fileSystemPath),
            runtimePath is null ? null : CreateFileStamp(runtimePath),
            OutputRoot: null);
        var projectKey = CreateProjectKey(source);
        return new ZaCacheProjectContext(
            romFsRoot,
            projectKey,
            Path.Combine(ProjectsPath, projectKey),
            source);
    }

    private static string CreateProjectKey(ZaCacheSourceFingerprint source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ZaCacheFileStamp CreateFileStamp(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Pokemon Legends Z-A cache source file was not found.", fileInfo.FullName);
        }

        return new ZaCacheFileStamp(
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

        return Path.Combine(localAppData, "KM Editor", "PokemonLegendsZACache");
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

    private ZaCacheSettings ReadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaultSettings = new ZaCacheSettings(ZaCacheMode.Balanced, DefaultMaxCacheSizeBytes);
            WriteJsonAtomic(SettingsPath, defaultSettings);
            return defaultSettings;
        }

        try
        {
            using var stream = OpenJsonReadStream(SettingsPath);
            var settings = JsonSerializer.Deserialize<ZaCacheSettings>(stream, JsonOptions);
            if (settings is null)
            {
                throw new JsonException("Cache settings file was empty.");
            }

            return settings with { MaxCacheSizeBytes = ClampMaxCacheSize(settings.MaxCacheSizeBytes) };
        }
        catch (JsonException)
        {
            var defaultSettings = new ZaCacheSettings(ZaCacheMode.Balanced, DefaultMaxCacheSizeBytes);
            WriteJsonAtomic(SettingsPath, defaultSettings);
            return defaultSettings;
        }
    }

    private static long ClampMaxCacheSize(long value)
    {
        return Math.Clamp(value, MinimumMaxCacheSizeBytes, MaximumMaxCacheSizeBytes);
    }

    private void EnsureRoot()
    {
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

    private static string GetVirtualPathKey(string virtualPath)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(virtualPath))).ToLowerInvariant();
    }

    private static string GetPayloadDirectory(ZaCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, PayloadDirectoryName);
    }

    private static string GetPayloadPath(ZaCacheProjectContext context, string virtualPath)
    {
        return Path.Combine(GetPayloadDirectory(context), $"{GetVirtualPathKey(virtualPath)}.bin");
    }

    private static string GetPayloadMetadataPath(ZaCacheProjectContext context, string virtualPath)
    {
        return Path.Combine(GetPayloadDirectory(context), $"{GetVirtualPathKey(virtualPath)}.json");
    }

    private static string GetMetadataDirectory(ZaCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, MetadataDirectoryName);
    }

    private static string GetMetadataPath(ZaCacheProjectContext context, string virtualPath)
    {
        return Path.Combine(GetMetadataDirectory(context), $"{GetVirtualPathKey(virtualPath)}.json");
    }

    private static string GetSourcePath(ZaCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, SourceFileName);
    }

    private static string GetWarmupPathsPath(ZaCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, WarmupPathsFileName);
    }

    private static string GetWarmupStatePath(ZaCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, WarmupStateFileName);
    }

    private static bool IsTextMessagePath(string virtualPath)
    {
        return NormalizeVirtualPath(virtualPath)
            .StartsWith($"{ZaMessagePathResolver.MessageRootPath}/", StringComparison.OrdinalIgnoreCase);
    }

    private static void TouchProjectDirectory(ZaCacheProjectContext context)
    {
        Directory.CreateDirectory(context.ProjectDirectory);
        Directory.SetLastWriteTimeUtc(context.ProjectDirectory, DateTime.UtcNow);
    }

    private void WriteJsonAtomic<TValue>(string path, TValue value)
    {
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

    private static FileStream OpenJsonReadStream(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
    }

    private void WriteBytesAtomic(string path, byte[] bytes)
    {
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

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", RecursiveCacheEnumeration))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }

    private sealed record ZaCacheIndexFile(
        int CacheSchemaVersion,
        ZaCacheSourceFingerprint Source,
        ZaTrinityArchiveIndex Index);

    private sealed record ZaCacheSourceFile(
        int CacheSchemaVersion,
        ZaCacheSourceFingerprint Source);

    private sealed record ZaCacheWarmupPathsFile(
        int CacheSchemaVersion,
        ZaCacheSourceFingerprint Source,
        IReadOnlyList<string> VirtualPaths);

    private sealed record ZaCacheWarmupStateFile(
        int CacheSchemaVersion,
        ZaCacheSourceFingerprint Source,
        ZaCacheMode Mode,
        long MaxCacheSizeBytes,
        bool CapacityLimited);

    private sealed record ZaCachePayloadMetadata(
        int CacheSchemaVersion,
        ZaCacheSourceFingerprint Source,
        string VirtualPath,
        long DecompressedSize,
        DateTimeOffset CreatedAtUtc);

    private sealed record ZaCacheVirtualFileMetadata(
        int CacheSchemaVersion,
        ZaCacheSourceFingerprint Source,
        string VirtualPath,
        DateTimeOffset CreatedAtUtc);

    private sealed record ZaCacheProjectContext(
        string RomFsRootPath,
        string ProjectKey,
        string ProjectDirectory,
        ZaCacheSourceFingerprint Source);

    private sealed record CacheEvictionCandidate(
        IReadOnlyList<string> Paths,
        DateTime LastUsedUtc,
        long SizeBytes);
}

public sealed record ZaCacheSettings(
    ZaCacheMode Mode,
    long MaxCacheSizeBytes);

public sealed record ZaCacheStatus(
    ZaCacheSettings Settings,
    long CacheSizeBytes,
    int WarmupCompleted,
    int WarmupTotal,
    int ProgressPercent,
    string Phase,
    string Message,
    bool IsActiveProjectPreserved);

public sealed record ZaCacheSourceFingerprint(
    int CacheSchemaVersion,
    string ParserVersion,
    string DecompressorVersion,
    string SelectedGame,
    ZaCacheFileStamp Descriptor,
    ZaCacheFileStamp FileSystem,
    ZaCacheFileStamp? CompressionRuntime,
    ZaCacheDirectoryStamp? OutputRoot);

public sealed record ZaCacheFileStamp(
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record ZaCacheDirectoryStamp(
    string FullPath,
    bool Exists,
    long FileCount,
    long TotalSizeBytes,
    DateTime LastWriteTimeUtc,
    string ContentFingerprint,
    int InaccessibleEntryCount);
