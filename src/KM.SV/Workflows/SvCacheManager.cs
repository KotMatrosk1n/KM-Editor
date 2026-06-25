// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public const int CacheSchemaVersion = 1;
    public const string ParserVersion = "sv-cache-parser-v1";
    public const string DecompressorVersion = "sv-cache-decompressor-v1";

    private const long DefaultMaxCacheSizeBytes = 512L * 1024 * 1024;
    private const long MinimumMaxCacheSizeBytes = 128L * 1024 * 1024;
    private const long MaximumMaxCacheSizeBytes = 2L * 1024 * 1024 * 1024;
    private const string SettingsFileName = "settings.json";
    private const string ProjectsDirectoryName = "projects";
    private const string TempDirectoryName = "tmp";
    private const string IndexFileName = "index.json";
    private const string PayloadDirectoryName = "payloads";
    private const string MetadataDirectoryName = "metadata";
    private const int TextWarmupBatchSize = 256;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string cacheRoot;
    private readonly object syncRoot = new();

    public SvCacheManager(string? cacheRoot = null)
    {
        this.cacheRoot = cacheRoot ?? ResolveDefaultCacheRoot();
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
            return context is null
                ? WarmupVirtualPaths
                : GetWarmupVirtualPaths(context);
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

    private IReadOnlyList<string> GetWarmupVirtualPaths(SvCacheProjectContext context)
    {
        try
        {
            var index = GetOrBuildIndex(context);
            var fileHashes = index.Files
                .Select(file => file.FileHash)
                .ToHashSet();

            return CreateOrderedWarmupVirtualPaths(CreateDiscoveredMessageWarmupPaths(index))
                .Where(virtualPath => fileHashes.Contains(SvTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath))))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return WarmupVirtualPaths;
        }
    }

    private static IEnumerable<string> CreateDiscoveredMessageWarmupPaths(SvTrinityArchiveIndex index)
    {
        var packNames = index.Files
            .Select(file => file.PackName)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var language in SvGameTextLanguage.SupportedMessageLanguages)
        {
            foreach (var packName in packNames)
            {
                var virtualPath = SvMessagePathResolver.TryCreateMessageDatPathFromPackName(packName, language);
                if (!string.IsNullOrWhiteSpace(virtualPath))
                {
                    yield return virtualPath;
                }
            }
        }
    }

    public SvCacheSettings UpdateSettings(SvCacheMode mode, long maxCacheSizeBytes, ProjectPaths? activePaths = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            var previousSettings = ReadSettings();
            var settings = new SvCacheSettings(
                mode,
                ClampMaxCacheSize(maxCacheSizeBytes));
            WriteJsonAtomic(SettingsPath, settings);
            if (previousSettings.Mode != settings.Mode)
            {
                DeleteDirectoryIfExists(ProjectsPath);
                DeleteDirectoryIfExists(TempPath);
                Directory.CreateDirectory(ProjectsPath);
            }
            else
            {
                PruneIfNeeded(settings, TryCreateActiveProjectContext(activePaths)?.ProjectKey);
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
            return CreateStatus(settings, TryCreateActiveProjectContext(paths), activeProjectPreserved: false);
        }
    }

    public SvCacheStatus Clear(ProjectPaths? activePaths = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            var settings = ReadSettings();
            var activeContext = TryCreateActiveProjectContext(activePaths);

            DeleteDirectoryIfExists(ProjectsPath);
            DeleteDirectoryIfExists(TempPath);
            Directory.CreateDirectory(ProjectsPath);
            return CreateStatus(settings, activeContext, activeProjectPreserved: false);
        }
    }

    public SvCacheStatus WarmupStep(ProjectPaths paths, int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(paths);

        lock (syncRoot)
        {
            EnsureRoot();
            var settings = ReadSettings();
            var context = TryCreateActiveProjectContext(paths);
            if (context is null || settings.Mode == SvCacheMode.Minimal)
            {
                return CreateStatus(settings, context, activeProjectPreserved: false);
            }

            var warmupVirtualPaths = GetWarmupVirtualPaths(context);
            if (warmupVirtualPaths.Count == 0)
            {
                return CreateStatus(settings, context, activeProjectPreserved: false);
            }

            var batch = GetWarmupBatch(settings, context, warmupVirtualPaths, stepIndex);
            if (batch.Count == 0)
            {
                return CreateStatus(settings, context, activeProjectPreserved: false);
            }

            if (settings.Mode == SvCacheMode.Performance)
            {
                WarmupPerformanceBatch(paths, context, batch);
            }
            else
            {
                foreach (var virtualPath in batch)
                {
                    WriteVirtualMetadata(context, virtualPath);
                }
            }

            PruneIfNeeded(settings, context.ProjectKey);
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
            var normalizedVirtualPath = NormalizeVirtualPath(virtualPath);

            if (settings.Mode == SvCacheMode.Performance
                && TryReadPayload(context, normalizedVirtualPath, out var cachedBytes))
            {
                TouchProjectDirectory(context);
                return cachedBytes;
            }

            var index = settings.Mode == SvCacheMode.Minimal
                ? null
                : GetOrBuildIndex(context);

            using var archive = SvTrinityArchive.Open(
                paths.BaseRomFsPath!,
                paths.ScarletVioletSupportFolderPath,
                index: index);
            var bytes = archive.ReadFile(normalizedVirtualPath);

            if (settings.Mode == SvCacheMode.Performance)
            {
                WritePayload(context, normalizedVirtualPath, bytes);
                PruneIfNeeded(settings, context.ProjectKey);
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
            var index = GetBaseTrinityIndex(paths);
            var fileHash = SvTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath));
            return index.Files.Any(file => file.FileHash == fileHash);
        }
    }

    public IReadOnlyList<string> ListBaseTrinityPackNames(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        lock (syncRoot)
        {
            EnsureRoot();
            var index = GetBaseTrinityIndex(paths);
            return index.Files
                .Select(file => file.PackName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private SvTrinityArchiveIndex GetOrBuildIndex(SvCacheProjectContext context)
    {
        Directory.CreateDirectory(context.ProjectDirectory);
        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);

        if (TryReadCachedIndex(context, out var cachedIndex))
        {
            TouchProjectDirectory(context);
            return cachedIndex;
        }

        var index = SvTrinityArchive.BuildIndex(context.RomFsRootPath);
        var indexFile = new SvCacheIndexFile(
            CacheSchemaVersion,
            context.Source,
            index);
        WriteJsonAtomic(indexPath, indexFile);
        TouchProjectDirectory(context);
        return index;
    }

    private bool TryReadCachedIndex(SvCacheProjectContext context, out SvTrinityArchiveIndex index)
    {
        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);
        if (!File.Exists(indexPath))
        {
            index = default!;
            return false;
        }

        try
        {
            var cached = JsonSerializer.Deserialize<SvCacheIndexFile>(
                File.ReadAllBytes(indexPath),
                JsonOptions);
            if (cached is not null
                && cached.CacheSchemaVersion == CacheSchemaVersion
                && cached.Source == context.Source
                && cached.Index.SchemaVersion == SvTrinityArchive.IndexSchemaVersion)
            {
                index = cached.Index;
                return true;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or inaccessible cache files are disposable and rebuilt by callers that need an index.
        }

        index = default!;
        return false;
    }

    private SvTrinityArchiveIndex GetBaseTrinityIndex(ProjectPaths paths)
    {
        var settings = ReadSettings();
        var context = CreateProjectContext(paths);
        return settings.Mode == SvCacheMode.Minimal
            ? SvTrinityArchive.BuildIndex(context.RomFsRootPath)
            : GetOrBuildIndex(context);
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
            var metadata = JsonSerializer.Deserialize<SvCachePayloadMetadata>(
                File.ReadAllBytes(metadataPath),
                JsonOptions);
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

            TouchProjectDirectory(context);
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            bytes = [];
            return false;
        }
    }

    private IReadOnlyList<string> GetWarmupBatch(
        SvCacheSettings settings,
        SvCacheProjectContext context,
        IReadOnlyList<string> warmupVirtualPaths,
        int stepIndex)
    {
        var firstIndex = FindNextIncompleteWarmupIndex(settings, context, warmupVirtualPaths, stepIndex);
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

            if (!IsWarmupEntryComplete(settings, context, virtualPath))
            {
                batch.Add(virtualPath);
            }
        }

        return batch;
    }

    private int FindNextIncompleteWarmupIndex(
        SvCacheSettings settings,
        SvCacheProjectContext context,
        IReadOnlyList<string> warmupVirtualPaths,
        int stepIndex)
    {
        var safeStepIndex = Math.Clamp(stepIndex, 0, Math.Max(0, warmupVirtualPaths.Count - 1));
        for (var offset = 0; offset < warmupVirtualPaths.Count; offset++)
        {
            var index = (safeStepIndex + offset) % warmupVirtualPaths.Count;
            var virtualPath = NormalizeVirtualPath(warmupVirtualPaths[index]);
            if (!IsWarmupEntryComplete(settings, context, virtualPath))
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
        if (!File.Exists(GetMetadataPath(context, virtualPath)))
        {
            return false;
        }

        return settings.Mode != SvCacheMode.Performance || File.Exists(GetPayloadPath(context, virtualPath));
    }

    private static bool IsWarmupPayloadComplete(SvCacheProjectContext context, string virtualPath)
    {
        return File.Exists(GetMetadataPath(context, virtualPath))
            && File.Exists(GetPayloadPath(context, virtualPath));
    }

    private void WarmupPerformanceBatch(
        ProjectPaths paths,
        SvCacheProjectContext context,
        IReadOnlyList<string> virtualPaths)
    {
        var index = GetOrBuildIndex(context);
        using var archive = SvTrinityArchive.Open(
            paths.BaseRomFsPath!,
            paths.ScarletVioletSupportFolderPath,
            index: index);

        foreach (var virtualPath in virtualPaths)
        {
            if (IsWarmupPayloadComplete(context, virtualPath))
            {
                continue;
            }

            if (!archive.TryReadFile(virtualPath, out var bytes))
            {
                continue;
            }

            WriteVirtualMetadata(context, virtualPath);
            WritePayload(context, virtualPath, bytes);
        }
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
        bool activeProjectPreserved)
    {
        var cacheSize = GetCacheContentSize();
        var warmupVirtualPaths = context is not null && settings.Mode != SvCacheMode.Minimal
            ? GetWarmupVirtualPathsForStatus(context)
            : Array.Empty<string>();
        var total = warmupVirtualPaths.Count;
        var completed = context is not null && total > 0
            ? CountCompletedWarmupEntries(settings, context, warmupVirtualPaths)
            : 0;
        var percent = total == 0
            ? 0
            : (int)Math.Round(completed * 100.0 / total, MidpointRounding.AwayFromZero);
        var phase = settings.Mode == SvCacheMode.Minimal
            ? "Minimal mode"
            : completed >= total && total > 0
                ? "Cache ready"
                : completed == 0
                    ? "Checking cache"
                    : settings.Mode == SvCacheMode.Performance
                        ? "Caching Trinity payloads"
                        : "Indexing Trinity files";
        var message = settings.Mode switch
        {
            SvCacheMode.Minimal => "Session only cache mode is active.",
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

    private IReadOnlyList<string> GetWarmupVirtualPathsForStatus(SvCacheProjectContext context)
    {
        try
        {
            if (!TryReadCachedIndex(context, out var index))
            {
                return WarmupVirtualPaths;
            }

            var fileHashes = index.Files
                .Select(file => file.FileHash)
                .ToHashSet();

            return CreateOrderedWarmupVirtualPaths(CreateDiscoveredMessageWarmupPaths(index))
                .Where(virtualPath => fileHashes.Contains(SvTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath))))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return WarmupVirtualPaths;
        }
    }

    private int CountCompletedWarmupEntries(
        SvCacheSettings settings,
        SvCacheProjectContext context,
        IReadOnlyList<string> warmupVirtualPaths)
    {
        var completed = 0;
        foreach (var virtualPath in warmupVirtualPaths.Select(NormalizeVirtualPath))
        {
            completed += IsWarmupEntryComplete(settings, context, virtualPath) ? 1 : 0;
        }

        return completed;
    }

    private void PruneIfNeeded(SvCacheSettings settings, string? activeProjectKey)
    {
        var currentSize = GetCacheContentSize();
        if (currentSize <= settings.MaxCacheSizeBytes || !Directory.Exists(ProjectsPath))
        {
            return;
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

            DeleteDirectoryIfExists(directory.FullName);
            currentSize = GetCacheContentSize();
            if (currentSize <= settings.MaxCacheSizeBytes)
            {
                return;
            }
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

        try
        {
            return CreateProjectContext(paths);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
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
            CreateDirectoryStamp(paths.OutputRootPath));
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

    private static SvCacheDirectoryStamp? CreateDirectoryStamp(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(directoryPath);
        var directoryInfo = new DirectoryInfo(fullPath);
        if (!directoryInfo.Exists)
        {
            return new SvCacheDirectoryStamp(
                fullPath,
                Exists: false,
                FileCount: 0,
                TotalSizeBytes: 0,
                LastWriteTimeUtc: DateTime.MinValue,
                ContentFingerprint: string.Empty,
                InaccessibleEntryCount: 0);
        }

        long fileCount = 0;
        long totalSize = 0;
        var latestWriteTimeUtc = directoryInfo.LastWriteTimeUtc;
        var inaccessibleEntryCount = 0;
        var entries = new List<string>();

        try
        {
            foreach (var childDirectoryPath in Directory.EnumerateDirectories(
                fullPath,
                "*",
                SearchOption.AllDirectories))
            {
                try
                {
                    var childDirectory = new DirectoryInfo(childDirectoryPath);
                    var relativePath = Path.GetRelativePath(fullPath, childDirectory.FullName).Replace('\\', '/');
                    latestWriteTimeUtc = latestWriteTimeUtc > childDirectory.LastWriteTimeUtc
                        ? latestWriteTimeUtc
                        : childDirectory.LastWriteTimeUtc;
                    entries.Add($"d\0{relativePath}\0{childDirectory.LastWriteTimeUtc.Ticks}");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    inaccessibleEntryCount++;
                }
            }

            foreach (var filePath in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var relativePath = Path.GetRelativePath(fullPath, fileInfo.FullName).Replace('\\', '/');
                    fileCount++;
                    totalSize += fileInfo.Length;
                    latestWriteTimeUtc = latestWriteTimeUtc > fileInfo.LastWriteTimeUtc
                        ? latestWriteTimeUtc
                        : fileInfo.LastWriteTimeUtc;
                    entries.Add($"f\0{relativePath}\0{fileInfo.Length}\0{fileInfo.LastWriteTimeUtc.Ticks}");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    inaccessibleEntryCount++;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            inaccessibleEntryCount++;
        }

        entries.Sort(StringComparer.OrdinalIgnoreCase);
        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', entries))))
            .ToLowerInvariant();
        return new SvCacheDirectoryStamp(
            fullPath,
            Exists: true,
            fileCount,
            totalSize,
            latestWriteTimeUtc,
            fingerprint,
            inaccessibleEntryCount);
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
            WriteJsonAtomic(SettingsPath, defaultSettings);
            return defaultSettings;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<SvCacheSettings>(File.ReadAllBytes(SettingsPath), JsonOptions);
            if (settings is null)
            {
                throw new JsonException("Cache settings file was empty.");
            }

            return settings with { MaxCacheSizeBytes = ClampMaxCacheSize(settings.MaxCacheSizeBytes) };
        }
        catch (JsonException)
        {
            var defaultSettings = new SvCacheSettings(SvCacheMode.Balanced, DefaultMaxCacheSizeBytes);
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
    }

    private string SettingsPath => Path.Combine(cacheRoot, SettingsFileName);

    private string ProjectsPath => Path.Combine(cacheRoot, ProjectsDirectoryName);

    private string TempPath => Path.Combine(cacheRoot, TempDirectoryName);

    private long GetCacheContentSize()
    {
        return GetDirectorySize(ProjectsPath) + GetDirectorySize(TempPath);
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

    private static string GetPayloadDirectory(SvCacheProjectContext context)
    {
        return Path.Combine(context.ProjectDirectory, PayloadDirectoryName);
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

    private static bool IsTextMessagePath(string virtualPath)
    {
        return NormalizeVirtualPath(virtualPath)
            .StartsWith($"{SvMessagePathResolver.MessageRootPath}/", StringComparison.OrdinalIgnoreCase);
    }

    private static void TouchProjectDirectory(SvCacheProjectContext context)
    {
        Directory.CreateDirectory(context.ProjectDirectory);
        Directory.SetLastWriteTimeUtc(context.ProjectDirectory, DateTime.UtcNow);
    }

    private void WriteJsonAtomic<TValue>(string path, TValue value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        WriteBytesAtomic(path, bytes);
    }

    private void WriteBytesAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(TempPath);
        var tempPath = Path.Combine(
            TempPath,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, path, overwrite: true);
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
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

    private sealed record SvCacheIndexFile(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source,
        SvTrinityArchiveIndex Index);

    private sealed record SvCachePayloadMetadata(
        int CacheSchemaVersion,
        SvCacheSourceFingerprint Source,
        string VirtualPath,
        long DecompressedSize,
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
