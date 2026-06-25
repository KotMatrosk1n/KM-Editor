// SPDX-License-Identifier: GPL-3.0-only

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
    private const string PayloadDirectoryName = "payloads";
    private const string MetadataDirectoryName = "metadata";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly IReadOnlyList<string> WarmupTextLanguages =
        ZaGameTextLanguage.SupportedMessageLanguages;

    private readonly string cacheRoot;
    private readonly object syncRoot = new();

    public ZaCacheManager(string? cacheRoot = null)
    {
        this.cacheRoot = cacheRoot ?? ResolveDefaultCacheRoot();
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

    public static IReadOnlyList<string> WarmupVirtualPaths { get; } = CreateWarmupVirtualPaths();

    public ZaCacheSettings GetSettings()
    {
        lock (syncRoot)
        {
            EnsureRoot();
            return ReadSettings();
        }
    }

    private static IReadOnlyList<string> CreateWarmupVirtualPaths()
    {
        return new[]
            {
                ZaDataPaths.PersonalArray,
                ZaDataPaths.MoveDataArray,
                ZaDataPaths.ItemDataArray,
                ZaDataPaths.TrainerDataArray,
                ZaDataPaths.PokemonDataArray,
                ZaDataPaths.EncountDataArray,
                ZaDataPaths.PokemonSpawnerDataArray,
                ZaDataPaths.PokemonSpawnerTransformArray,
                ZaDataPaths.ItemBallSpawnerDataArray,
                ZaDataPaths.ItemBallSpawnerTransformArray,
                ZaDataPaths.RandomPopItemSpawnerDataArray,
                ZaDataPaths.BattleTrainerSpawnerDataArray,
                ZaDataPaths.ShopItemArray,
                ZaDataPaths.ShopItemLineupArray,
                ZaDataPaths.ShopDressUpArray,
                ZaDataPaths.ShopDressUpLineupArray,
                ZaDataPaths.ShopHairMakeLineupArray,
                ZaDataPaths.DressUpDataArray,
                ZaDataPaths.HairMakeDataArray,
            }
            .Concat(CreateWarmupTextPaths())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> CreateWarmupTextPaths()
    {
        foreach (var language in WarmupTextLanguages)
        {
            yield return ZaDataPaths.ItemNames(language);
            yield return ZaDataPaths.MoveNames(language);
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
                DeleteDirectoryIfExists(ProjectsPath);
                DeleteDirectoryIfExists(TempPath);
                Directory.CreateDirectory(ProjectsPath);
            }
            else
            {
                var activeContext = TryCreateActiveProjectContext(activePaths);
                if (activeContext is not null)
                {
                    DeleteObsoleteProjectCaches(activeContext);
                }

                PruneIfNeeded(settings, activeContext?.ProjectKey);
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
            }

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

            DeleteDirectoryIfExists(ProjectsPath);
            DeleteDirectoryIfExists(TempPath);
            Directory.CreateDirectory(ProjectsPath);
            return CreateStatus(settings, activeContext, activeProjectPreserved: false);
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

            var safeStepIndex = Math.Clamp(stepIndex, 0, Math.Max(0, WarmupVirtualPaths.Count - 1));
            var virtualPath = WarmupVirtualPaths[safeStepIndex];

            _ = GetOrBuildIndex(context);
            WriteVirtualMetadata(context, virtualPath);

            if (settings.Mode == ZaCacheMode.Performance)
            {
                _ = ReadBaseTrinityFile(paths, virtualPath);
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
            DeleteObsoleteProjectCaches(context);
            var normalizedVirtualPath = NormalizeVirtualPath(virtualPath);

            if (settings.Mode == ZaCacheMode.Performance
                && TryReadPayload(context, normalizedVirtualPath, out var cachedBytes))
            {
                TouchProjectDirectory(context);
                return cachedBytes;
            }

            var index = settings.Mode == ZaCacheMode.Minimal
                ? null
                : GetOrBuildIndex(context);

            using var archive = ZaTrinityArchive.Open(
                paths.BaseRomFsPath!,
                paths.PokemonLegendsZASupportFolderPath,
                index: index);
            var bytes = archive.ReadFile(normalizedVirtualPath);

            if (settings.Mode == ZaCacheMode.Performance)
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
            var fileHash = ZaTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath));
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

    private ZaTrinityArchiveIndex GetOrBuildIndex(ZaCacheProjectContext context)
    {
        Directory.CreateDirectory(context.ProjectDirectory);
        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);

        if (File.Exists(indexPath))
        {
            if (TryReadCacheIndexFile(indexPath, out var cached)
                && cached.Source == context.Source
                && cached.Index.SchemaVersion == ZaTrinityArchive.IndexSchemaVersion)
            {
                TouchProjectDirectory(context);
                return cached.Index;
            }
        }

        var index = ZaTrinityArchive.BuildIndex(context.RomFsRootPath);
        var indexFile = new ZaCacheIndexFile(
            CacheSchemaVersion,
            context.Source,
            index);
        WriteJsonAtomic(indexPath, indexFile);
        TouchProjectDirectory(context);
        return index;
    }

    private ZaTrinityArchiveIndex GetBaseTrinityIndex(ProjectPaths paths)
    {
        var settings = ReadSettings();
        var context = CreateProjectContext(paths);
        DeleteObsoleteProjectCaches(context);
        return settings.Mode == ZaCacheMode.Minimal
            ? ZaTrinityArchive.BuildIndex(context.RomFsRootPath)
            : GetOrBuildIndex(context);
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
            var metadata = JsonSerializer.Deserialize<ZaCachePayloadMetadata>(
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
        var total = context is not null && settings.Mode != ZaCacheMode.Minimal
            ? WarmupVirtualPaths.Count
            : 0;
        var completed = context is not null && total > 0
            ? CountCompletedWarmupEntries(settings, context)
            : 0;
        var percent = total == 0
            ? 0
            : (int)Math.Round(completed * 100.0 / total, MidpointRounding.AwayFromZero);
        var phase = settings.Mode == ZaCacheMode.Minimal
            ? "Minimal mode"
            : completed >= total && total > 0
                ? "Cache ready"
                : completed == 0
                    ? "Checking cache"
                    : settings.Mode == ZaCacheMode.Performance
                        ? "Caching Trinity payloads"
                        : "Indexing Trinity files";
        var message = settings.Mode switch
        {
            ZaCacheMode.Minimal => "Session only cache mode is active.",
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

    private int CountCompletedWarmupEntries(ZaCacheSettings settings, ZaCacheProjectContext context)
    {
        var completed = 0;
        foreach (var virtualPath in WarmupVirtualPaths.Select(NormalizeVirtualPath))
        {
            if (!File.Exists(GetMetadataPath(context, virtualPath)))
            {
                continue;
            }

            if (settings.Mode == ZaCacheMode.Performance && !File.Exists(GetPayloadPath(context, virtualPath)))
            {
                continue;
            }

            completed++;
        }

        return completed;
    }

    private void PruneIfNeeded(ZaCacheSettings settings, string? activeProjectKey)
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

    private void DeleteObsoleteProjectCaches(ZaCacheProjectContext activeContext)
    {
        if (!Directory.Exists(ProjectsPath))
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

            var indexPath = Path.Combine(directory.FullName, IndexFileName);
            if (!TryReadCacheIndexFile(indexPath, out var cached)
                || !HasSameProjectIdentity(cached.Source, activeContext.Source))
            {
                continue;
            }

            TryDeleteDirectory(directory.FullName);
        }
    }

    private static bool HasSameProjectIdentity(
        ZaCacheSourceFingerprint cached,
        ZaCacheSourceFingerprint active)
    {
        return cached.CacheSchemaVersion == active.CacheSchemaVersion
            && string.Equals(cached.ParserVersion, active.ParserVersion, StringComparison.Ordinal)
            && string.Equals(cached.DecompressorVersion, active.DecompressorVersion, StringComparison.Ordinal)
            && string.Equals(cached.SelectedGame, active.SelectedGame, StringComparison.Ordinal)
            && cached.Descriptor == active.Descriptor
            && cached.FileSystem == active.FileSystem
            && cached.CompressionRuntime == active.CompressionRuntime;
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
            var cached = JsonSerializer.Deserialize<ZaCacheIndexFile>(
                File.ReadAllBytes(indexPath),
                JsonOptions);
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectoryIfExists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
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
            CreateDirectoryStamp(paths.OutputRootPath));
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

    private static ZaCacheDirectoryStamp? CreateDirectoryStamp(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(directoryPath);
        var directoryInfo = new DirectoryInfo(fullPath);
        if (!directoryInfo.Exists)
        {
            return new ZaCacheDirectoryStamp(
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
        return new ZaCacheDirectoryStamp(
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
            var settings = JsonSerializer.Deserialize<ZaCacheSettings>(File.ReadAllBytes(SettingsPath), JsonOptions);
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

    private static void TouchProjectDirectory(ZaCacheProjectContext context)
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

    private sealed record ZaCacheIndexFile(
        int CacheSchemaVersion,
        ZaCacheSourceFingerprint Source,
        ZaTrinityArchiveIndex Index);

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
