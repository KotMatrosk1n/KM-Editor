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

    private const long DefaultMaxCacheSizeBytes = 10L * 1024 * 1024 * 1024;
    private const long MinimumMaxCacheSizeBytes = 512L * 1024 * 1024;
    private const long MaximumMaxCacheSizeBytes = 500L * 1024 * 1024 * 1024;
    private const string SettingsFileName = "settings.json";
    private const string ProjectsDirectoryName = "projects";
    private const string TempDirectoryName = "tmp";
    private const string IndexFileName = "index.json";
    private const string PayloadDirectoryName = "payloads";
    private const string MetadataDirectoryName = "metadata";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly IReadOnlyList<string> WarmupTextLanguages =
    [
        SvGameTextLanguage.English,
        "Spanish",
        "French",
        "German",
    ];

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

    public static IReadOnlyList<string> WarmupVirtualPaths { get; } = CreateWarmupVirtualPaths();

    public SvCacheSettings GetSettings()
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
            }
            .Concat(CreateWarmupTextPaths())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> CreateWarmupTextPaths()
    {
        foreach (var language in WarmupTextLanguages)
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

    public SvCacheSettings UpdateSettings(SvCacheMode mode, long maxCacheSizeBytes, ProjectPaths? activePaths = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            var settings = new SvCacheSettings(
                mode,
                ClampMaxCacheSize(maxCacheSizeBytes));
            WriteJsonAtomic(SettingsPath, settings);
            PruneIfNeeded(settings, TryCreateActiveProjectContext(activePaths)?.ProjectKey);
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
            var preservedActiveProject = false;

            if (Directory.Exists(ProjectsPath))
            {
                foreach (var projectDirectory in Directory.EnumerateDirectories(ProjectsPath))
                {
                    if (activeContext is not null
                        && string.Equals(
                            Path.GetFileName(projectDirectory),
                            activeContext.ProjectKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        preservedActiveProject = true;
                        continue;
                    }

                    DeleteDirectoryIfExists(projectDirectory);
                }
            }

            DeleteDirectoryIfExists(TempPath);
            Directory.CreateDirectory(ProjectsPath);
            return CreateStatus(settings, activeContext, preservedActiveProject);
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

            var safeStepIndex = Math.Clamp(stepIndex, 0, Math.Max(0, WarmupVirtualPaths.Count - 1));
            var virtualPath = WarmupVirtualPaths[safeStepIndex];

            _ = GetOrBuildIndex(context);
            WriteVirtualMetadata(context, virtualPath);

            if (settings.Mode == SvCacheMode.Performance)
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

    private SvTrinityArchiveIndex GetOrBuildIndex(SvCacheProjectContext context)
    {
        Directory.CreateDirectory(context.ProjectDirectory);
        var indexPath = Path.Combine(context.ProjectDirectory, IndexFileName);

        if (File.Exists(indexPath))
        {
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
                    TouchProjectDirectory(context);
                    return cached.Index;
                }
            }
            catch (JsonException)
            {
                // Corrupt cache files are disposable and rebuilt below.
            }
            catch (IOException)
            {
                // Rebuild below.
            }
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
        var cacheSize = GetDirectorySize(cacheRoot);
        var total = context is not null && settings.Mode != SvCacheMode.Minimal
            ? WarmupVirtualPaths.Count
            : 0;
        var completed = context is not null && total > 0
            ? CountCompletedWarmupEntries(settings, context)
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

    private int CountCompletedWarmupEntries(SvCacheSettings settings, SvCacheProjectContext context)
    {
        var completed = 0;
        foreach (var virtualPath in WarmupVirtualPaths.Select(NormalizeVirtualPath))
        {
            if (!File.Exists(GetMetadataPath(context, virtualPath)))
            {
                continue;
            }

            if (settings.Mode == SvCacheMode.Performance && !File.Exists(GetPayloadPath(context, virtualPath)))
            {
                continue;
            }

            completed++;
        }

        return completed;
    }

    private void PruneIfNeeded(SvCacheSettings settings, string? activeProjectKey)
    {
        var currentSize = GetDirectorySize(cacheRoot);
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
            currentSize = GetDirectorySize(cacheRoot);
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
            runtimePath is null ? null : CreateFileStamp(runtimePath));
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
    SvCacheFileStamp? CompressionRuntime);

public sealed record SvCacheFileStamp(
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc);
