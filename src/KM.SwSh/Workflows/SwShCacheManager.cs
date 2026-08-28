// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Core.Concurrency;
using KM.Core.Files;
using KM.Core.Projects;

namespace KM.SwSh.Workflows;

public enum SwShCacheMode
{
    Minimal,
    Balanced,
    Performance,
}

public enum SwShCacheArtifactPolicy
{
    Balanced,
    Performance,
}

public sealed record SwShCacheSettings(
    SwShCacheMode Mode,
    long MaxCacheSizeBytes);

public sealed record SwShCacheStatus(
    SwShCacheSettings Settings,
    long CacheSizeBytes,
    int WarmupCompleted,
    int WarmupTotal,
    int ProgressPercent,
    string Phase,
    string Message,
    bool IsActiveProjectPreserved);

public sealed record SwShCacheSourceFile(
    string FilePath,
    ProjectFileLayer SourceLayer);

public sealed record SwShCacheFileStamp(
    string FullPath,
    ProjectFileLayer SourceLayer,
    long Length,
    DateTime LastWriteTimeUtc,
    string Sha256);

public sealed record SwShCacheSourceIdentity(
    int CacheSchemaVersion,
    string ParserVersion,
    ProjectGame SelectedGame,
    IReadOnlyList<SwShCacheFileStamp> Sources);

public sealed record SwShCacheArtifactDescriptor(
    string Kind,
    string Key,
    SwShCacheArtifactPolicy Policy);

/// <summary>
/// Provides bounded, versioned storage for reusable Sword/Shield workflow artifacts.
/// LayeredFS-derived artifacts are intentionally retained in memory only.
/// </summary>
public sealed class SwShCacheManager
{
    public const int CacheSchemaVersion = 1;
    public const string ParserVersion = "swsh-cache-parser-v1";
    public const long DefaultMaxCacheSizeBytes = 512L * 1024 * 1024;
    public const long MinimumMaxCacheSizeBytes = 128L * 1024 * 1024;
    public const long MaximumMaxCacheSizeBytes = 2L * 1024 * 1024 * 1024;

    private const long MaximumMemoryCacheSizeBytes = 64L * 1024 * 1024;
    private const long MinimumMemoryCacheSizeBytes = 8L * 1024 * 1024;
    private const int MaximumMeasuredCacheFileCount = 200_000;
    private const int MaximumPruneDirectoryCount = 4_096;
    private const int MaximumPruneArtifactCount = 50_000;
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumMetadataBytes = 256 * 1024;
    private const int MaximumSettingsBytes = 64 * 1024;
    private const int MaximumSourceFileCount = 1_024;
    private const string SettingsFileName = "settings.json";
    private const string SizeLedgerFileName = "size-ledger.json";
    private const string ProjectsDirectoryName = "projects";
    private const string TempDirectoryName = "tmp";
    private const string SourceManifestFileName = "source.json";
    private const string AccessMetadataFileName = "access.json";
    private const string ArtifactsDirectoryName = "artifacts";
    private static readonly TimeSpan OrphanTempFileAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AccessTouchInterval = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly StringComparer FilePathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison FilePathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly EnumerationOptions RecursiveCacheEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };
    private static readonly EnumerationOptions TopLevelCacheEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private readonly string cacheRoot;
    private readonly bool isReadWorker;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, MemoryArtifact> memoryArtifacts = new(StringComparer.Ordinal);
    private readonly LinkedList<string> memoryLru = [];
    private readonly Dictionary<string, DateTime> retainedAccessTouches = new(StringComparer.Ordinal);
    private readonly HashSet<string> completedObsoleteCacheChecks = new(StringComparer.Ordinal);
    private long retainedMemorySizeBytes;
    private long? retainedPersistentCacheSizeBytes;
    private bool tempCleanupCompleted;

    public SwShCacheManager(string? cacheRoot = null)
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot ?? ResolveDefaultCacheRoot());
        isReadWorker = BoundedConcurrencyHostBudget.IsReadWorker;
    }

    public SwShCacheSettings GetSettings()
    {
        lock (syncRoot)
        {
            EnsureRoot();
            return ReadSettings();
        }
    }

    public SwShCacheStatus GetStatus(SwShCacheSourceIdentity? activeSource = null)
    {
        lock (syncRoot)
        {
            EnsureRoot();
            if (activeSource is not null)
            {
                ValidateSourceIdentity(activeSource);
                if (!isReadWorker)
                {
                    DeleteObsoleteSourceCaches(activeSource);
                }
            }

            var settings = ReadSettings();
            var activeProjectPreserved = isReadWorker
                ? activeSource is not null
                    && Directory.Exists(GetIdentityDirectory(GetIdentityKey(activeSource)))
                : PruneIfNeeded(settings, activeSource);
            return CreateStatus(settings, activeProjectPreserved);
        }
    }

    public SwShCacheStatus UpdateSettings(
        SwShCacheMode mode,
        long maxCacheSizeBytes,
        SwShCacheSourceIdentity? activeSource = null)
    {
        lock (syncRoot)
        {
            EnsureOwnerCacheMutation("update Sword/Shield cache settings");
            EnsureRoot();
            if (activeSource is not null)
            {
                ValidateSourceIdentity(activeSource);
            }

            var previous = ReadSettings();
            var settings = new SwShCacheSettings(mode, ClampMaxCacheSize(maxCacheSizeBytes));
            WriteJsonAtomic(SettingsPath, settings);

            if (previous.Mode != settings.Mode)
            {
                ClearMemoryCacheCore();
                ResetPersistentCache();
            }
            else
            {
                TrimMemoryCache(GetMemoryLimit(settings));
                if (activeSource is not null)
                {
                    DeleteObsoleteSourceCaches(activeSource);
                }
            }

            var activeProjectPreserved = PruneIfNeeded(settings, activeSource);
            return CreateStatus(settings, activeProjectPreserved);
        }
    }

    public SwShCacheStatus Clear(SwShCacheSourceIdentity? activeSource = null)
    {
        lock (syncRoot)
        {
            EnsureOwnerCacheMutation("clear the Sword/Shield persistent cache");
            EnsureRoot();
            if (activeSource is not null)
            {
                ValidateSourceIdentity(activeSource);
            }

            var settings = ReadSettings();
            ClearMemoryCacheCore();
            ResetPersistentCache();
            CleanupTempDirectory();
            return CreateStatus(settings, activeProjectPreserved: false);
        }
    }

    public SwShCacheSourceIdentity CaptureSourceIdentity(
        ProjectGame selectedGame,
        IEnumerable<SwShCacheSourceFile> sources,
        string parserVersion)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ValidateSelectedGame(selectedGame);
        parserVersion = ValidateText(parserVersion, nameof(parserVersion), maximumLength: 256);

        var captured = new List<SwShCacheFileStamp>();
        var seen = new HashSet<string>(FilePathComparer);
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!Enum.IsDefined(source.SourceLayer))
            {
                throw new ArgumentOutOfRangeException(nameof(sources), source.SourceLayer, "Unknown project source layer.");
            }

            var fullPath = Path.GetFullPath(source.FilePath);
            var duplicateKey = $"{(int)source.SourceLayer}:{fullPath}";
            if (!seen.Add(duplicateKey))
            {
                continue;
            }

            captured.Add(CaptureFileStamp(fullPath, source.SourceLayer));
            if (captured.Count > MaximumSourceFileCount)
            {
                throw new ArgumentException(
                    $"Sword/Shield cache identities cannot contain more than {MaximumSourceFileCount} source files.",
                    nameof(sources));
            }
        }

        if (captured.Count == 0)
        {
            throw new ArgumentException("At least one Sword/Shield source file is required.", nameof(sources));
        }

        captured.Sort(CompareFileStamps);
        return new SwShCacheSourceIdentity(CacheSchemaVersion, parserVersion, selectedGame, captured);
    }

    public bool TryGetArtifact<T>(
        SwShCacheSourceIdentity source,
        SwShCacheArtifactDescriptor artifact,
        out T value)
    {
        ArgumentNullException.ThrowIfNull(source);
        artifact = NormalizeArtifactDescriptor(artifact);
        ValidateSourceIdentity(source);
        var identityKey = GetIdentityKey(source);
        var artifactKey = GetArtifactKey(artifact);
        var memoryKey = GetMemoryKey(identityKey, artifactKey);
        var typeIdentity = GetTypeIdentity(typeof(T));

        lock (syncRoot)
        {
            if (TryGetMemoryArtifact(memoryKey, typeIdentity, out value))
            {
                return true;
            }

            try
            {
                EnsureRoot();
                var settings = ReadSettings();
                if (!CanPersist(source, artifact, settings))
                {
                    value = default!;
                    return false;
                }

                if (!isReadWorker)
                {
                    DeleteObsoleteSourceCaches(source);
                }
                if (!TryReadPersistentArtifact(
                        source,
                        artifact,
                        identityKey,
                        artifactKey,
                        typeIdentity,
                        settings,
                        out var payload,
                        out value))
                {
                    return false;
                }

                AddMemoryArtifact(memoryKey, typeIdentity, payload, GetMemoryLimit(settings));
                if (!isReadWorker)
                {
                    TryPrune(settings, source);
                }
                return true;
            }
            catch (Exception exception) when (IsDisposableCacheException(exception))
            {
                retainedPersistentCacheSizeBytes = null;
                value = default!;
                return false;
            }
        }
    }

    public T GetOrCreateArtifact<T>(
        SwShCacheSourceIdentity source,
        SwShCacheArtifactDescriptor artifact,
        Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (TryGetArtifact(source, artifact, out T value))
        {
            return value;
        }

        value = factory();
        SetArtifact(source, artifact, value);
        return value;
    }

    public bool IsArtifactPersisted<T>(
        SwShCacheSourceIdentity source,
        SwShCacheArtifactDescriptor artifact)
    {
        ArgumentNullException.ThrowIfNull(source);
        artifact = NormalizeArtifactDescriptor(artifact);
        ValidateSourceIdentity(source);
        var identityKey = GetIdentityKey(source);
        var artifactKey = GetArtifactKey(artifact);
        var typeIdentity = GetTypeIdentity(typeof(T));

        lock (syncRoot)
        {
            try
            {
                EnsureRoot();
                var settings = ReadSettings();
                if (!CanPersist(source, artifact, settings))
                {
                    return false;
                }

                if (!isReadWorker)
                {
                    DeleteObsoleteSourceCaches(source);
                }
                return TryReadPersistentArtifact(
                    source,
                    artifact,
                    identityKey,
                    artifactKey,
                    typeIdentity,
                    settings,
                    out _,
                    out T _);
            }
            catch (Exception exception) when (IsDisposableCacheException(exception))
            {
                retainedPersistentCacheSizeBytes = null;
                return false;
            }
        }
    }

    public void SetArtifact<T>(
        SwShCacheSourceIdentity source,
        SwShCacheArtifactDescriptor artifact,
        T value)
    {
        ArgumentNullException.ThrowIfNull(source);
        artifact = NormalizeArtifactDescriptor(artifact);
        ValidateSourceIdentity(source);

        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        var identityKey = GetIdentityKey(source);
        var artifactKey = GetArtifactKey(artifact);
        var memoryKey = GetMemoryKey(identityKey, artifactKey);
        var typeIdentity = GetTypeIdentity(typeof(T));

        lock (syncRoot)
        {
            var settings = new SwShCacheSettings(SwShCacheMode.Balanced, DefaultMaxCacheSizeBytes);
            try
            {
                EnsureRoot();
                settings = ReadSettings();
            }
            catch (Exception exception) when (IsDisposableCacheException(exception))
            {
                AddMemoryArtifact(memoryKey, typeIdentity, payload, GetMemoryLimit(settings));
                return;
            }

            AddMemoryArtifact(memoryKey, typeIdentity, payload, GetMemoryLimit(settings));
            if (isReadWorker)
            {
                return;
            }

            try
            {
                if (!CanPersist(source, artifact, settings) || payload.LongLength > settings.MaxCacheSizeBytes)
                {
                    RemovePersistentArtifact(identityKey, artifactKey);
                    return;
                }

                DeleteObsoleteSourceCaches(source);
                EnsureSourceManifest(source, identityKey);
                var artifactPath = GetArtifactPath(identityKey, artifactKey);
                var metadataPath = GetArtifactMetadataPath(identityKey, artifactKey);
                var now = DateTime.UtcNow;
                var createdUtc = TryReadJson<ArtifactMetadataDocument>(metadataPath, MaximumMetadataBytes)?.CreatedUtc ?? now;
                var metadata = new ArtifactMetadataDocument(
                    CacheSchemaVersion,
                    identityKey,
                    artifact.Kind,
                    artifact.Key,
                    artifact.Policy,
                    typeIdentity,
                    payload.LongLength,
                    ComputeSha256(payload),
                    createdUtc);

                WriteTrackedBytesAtomic(artifactPath, payload);
                WriteTrackedJsonAtomic(metadataPath, metadata);
                TouchIdentity(identityKey, now, force: true);
                TryPrune(settings, source);
            }
            catch (Exception exception) when (IsDisposableCacheException(exception))
            {
                retainedPersistentCacheSizeBytes = null;
            }
        }
    }

    public bool RemoveArtifact(
        SwShCacheSourceIdentity source,
        SwShCacheArtifactDescriptor artifact)
    {
        ArgumentNullException.ThrowIfNull(source);
        artifact = NormalizeArtifactDescriptor(artifact);
        ValidateSourceIdentity(source);
        var identityKey = GetIdentityKey(source);
        var artifactKey = GetArtifactKey(artifact);
        var memoryKey = GetMemoryKey(identityKey, artifactKey);

        lock (syncRoot)
        {
            var removed = RemoveMemoryArtifact(memoryKey);
            if (isReadWorker)
            {
                return removed;
            }

            try
            {
                EnsureRoot();
                return RemovePersistentArtifact(identityKey, artifactKey) || removed;
            }
            catch (Exception exception) when (IsDisposableCacheException(exception))
            {
                retainedPersistentCacheSizeBytes = null;
                return removed;
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static SwShCacheFileStamp CaptureFileStamp(string fullPath, ProjectFileLayer sourceLayer)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 128 * 1024,
                    FileOptions.SequentialScan);
                var lengthBefore = stream.Length;
                var lastWriteBefore = File.GetLastWriteTimeUtc(fullPath);
                var hash = SHA256.HashData(stream);
                var lengthAfter = stream.Length;
                var lastWriteAfter = File.GetLastWriteTimeUtc(fullPath);
                if (lengthBefore == lengthAfter && lastWriteBefore == lastWriteAfter)
                {
                    return new SwShCacheFileStamp(
                        new FileInfo(fullPath).FullName,
                        sourceLayer,
                        lengthAfter,
                        DateTime.SpecifyKind(lastWriteAfter, DateTimeKind.Utc),
                        Convert.ToHexString(hash));
                }
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (DirectoryNotFoundException)
            {
                throw new FileNotFoundException("Sword/Shield cache source file was not found.", fullPath);
            }
        }

        throw new IOException($"Sword/Shield cache source changed while it was being fingerprinted: {fullPath}");
    }

    private static int CompareFileStamps(SwShCacheFileStamp left, SwShCacheFileStamp right)
    {
        var layerComparison = left.SourceLayer.CompareTo(right.SourceLayer);
        return layerComparison != 0
            ? layerComparison
            : FilePathComparer.Compare(left.FullPath, right.FullPath);
    }

    private static SwShCacheArtifactDescriptor NormalizeArtifactDescriptor(SwShCacheArtifactDescriptor artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!Enum.IsDefined(artifact.Policy))
        {
            throw new ArgumentOutOfRangeException(nameof(artifact), artifact.Policy, "Unknown cache artifact policy.");
        }

        return new SwShCacheArtifactDescriptor(
            ValidateText(artifact.Kind, nameof(artifact.Kind), maximumLength: 256),
            ValidateText(artifact.Key, nameof(artifact.Key), maximumLength: 1024),
            artifact.Policy);
    }

    private static string ValidateText(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        value = value.Trim();
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return value;
    }

    private static void ValidateSourceIdentity(SwShCacheSourceIdentity source)
    {
        if (source.CacheSchemaVersion != CacheSchemaVersion)
        {
            throw new ArgumentException("Sword/Shield cache source uses an unsupported schema version.", nameof(source));
        }

        ValidateSelectedGame(source.SelectedGame);
        ValidateText(source.ParserVersion, nameof(source.ParserVersion), maximumLength: 256);
        if (source.Sources is null || source.Sources.Count == 0)
        {
            throw new ArgumentException("Sword/Shield cache source must contain at least one file stamp.", nameof(source));
        }

        foreach (var stamp in source.Sources)
        {
            ArgumentNullException.ThrowIfNull(stamp);
            if (!Enum.IsDefined(stamp.SourceLayer) || stamp.Length < 0)
            {
                throw new ArgumentException("Sword/Shield cache source contains an invalid file stamp.", nameof(source));
            }

            if (string.IsNullOrWhiteSpace(stamp.FullPath)
                || string.IsNullOrWhiteSpace(stamp.Sha256))
            {
                throw new ArgumentException("Sword/Shield cache source contains an incomplete file stamp.", nameof(source));
            }

            var fullPath = Path.GetFullPath(stamp.FullPath);
            if (!string.Equals(fullPath, stamp.FullPath, FilePathComparison)
                || stamp.Sha256.Length != 64
                || !stamp.Sha256.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("Sword/Shield cache source contains a non-canonical file stamp.", nameof(source));
            }
        }
    }

    private static void ValidateSelectedGame(ProjectGame selectedGame)
    {
        if (selectedGame is not ProjectGame.Sword and not ProjectGame.Shield)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedGame),
                selectedGame,
                "The Sword/Shield cache only accepts Sword or Shield projects.");
        }
    }

    private static bool CanPersist(
        SwShCacheSourceIdentity source,
        SwShCacheArtifactDescriptor artifact,
        SwShCacheSettings settings)
    {
        if (source.Sources.Any(static stamp => stamp.SourceLayer != ProjectFileLayer.Base))
        {
            return false;
        }

        return settings.Mode switch
        {
            SwShCacheMode.Minimal => false,
            SwShCacheMode.Balanced => artifact.Policy == SwShCacheArtifactPolicy.Balanced,
            SwShCacheMode.Performance => true,
            _ => false,
        };
    }

    private static string GetIdentityKey(SwShCacheSourceIdentity source)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", source.CacheSchemaVersion);
            writer.WriteString("parser", source.ParserVersion);
            writer.WriteNumber("game", (int)source.SelectedGame);
            writer.WriteStartArray("sources");
            foreach (var stamp in source.Sources.OrderBy(static stamp => stamp.SourceLayer)
                         .ThenBy(static stamp => NormalizePathForIdentity(stamp.FullPath), StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", NormalizePathForIdentity(stamp.FullPath));
                writer.WriteNumber("layer", (int)stamp.SourceLayer);
                writer.WriteNumber("length", stamp.Length);
                writer.WriteNumber("mtime", stamp.LastWriteTimeUtc.ToUniversalTime().Ticks);
                writer.WriteString("sha256", stamp.Sha256.ToUpperInvariant());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ComputeSha256(stream.ToArray());
    }

    private static string GetStableSourceKey(SwShCacheSourceIdentity source)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("game", (int)source.SelectedGame);
            writer.WriteString("parser", source.ParserVersion);
            writer.WriteStartArray("sources");
            foreach (var stamp in source.Sources.OrderBy(static stamp => stamp.SourceLayer)
                         .ThenBy(static stamp => NormalizePathForIdentity(stamp.FullPath), StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", NormalizePathForIdentity(stamp.FullPath));
                writer.WriteNumber("layer", (int)stamp.SourceLayer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ComputeSha256(stream.ToArray());
    }

    private static string GetArtifactKey(SwShCacheArtifactDescriptor artifact)
    {
        var text = $"{artifact.Kind.Length}:{artifact.Kind}{artifact.Key.Length}:{artifact.Key}:{(int)artifact.Policy}";
        return ComputeSha256(Encoding.UTF8.GetBytes(text));
    }

    private static string GetMemoryKey(string identityKey, string artifactKey) => $"{identityKey}:{artifactKey}";

    private static string NormalizePathForIdentity(string path)
    {
        var normalized = Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private static string ComputeSha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string GetTypeIdentity(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? "unknown";
        return $"{assemblyName}:{type.FullName ?? type.Name}";
    }

    private bool TryGetMemoryArtifact<T>(string memoryKey, string typeIdentity, out T value)
    {
        if (!memoryArtifacts.TryGetValue(memoryKey, out var entry)
            || !string.Equals(entry.TypeIdentity, typeIdentity, StringComparison.Ordinal))
        {
            value = default!;
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(entry.Payload, JsonOptions)!;
            memoryLru.Remove(entry.LruNode);
            memoryLru.AddLast(entry.LruNode);
            return true;
        }
        catch (JsonException)
        {
            RemoveMemoryArtifact(memoryKey);
            value = default!;
            return false;
        }
        catch (NotSupportedException)
        {
            RemoveMemoryArtifact(memoryKey);
            value = default!;
            return false;
        }
    }

    private void AddMemoryArtifact(string memoryKey, string typeIdentity, byte[] payload, long memoryLimit)
    {
        RemoveMemoryArtifact(memoryKey);
        if (payload.LongLength > memoryLimit)
        {
            return;
        }

        var node = memoryLru.AddLast(memoryKey);
        memoryArtifacts.Add(memoryKey, new MemoryArtifact(typeIdentity, payload, node));
        retainedMemorySizeBytes += payload.LongLength;
        TrimMemoryCache(memoryLimit);
    }

    private bool RemoveMemoryArtifact(string memoryKey)
    {
        if (!memoryArtifacts.Remove(memoryKey, out var entry))
        {
            return false;
        }

        memoryLru.Remove(entry.LruNode);
        retainedMemorySizeBytes = Math.Max(0, retainedMemorySizeBytes - entry.Payload.LongLength);
        return true;
    }

    private void TrimMemoryCache(long memoryLimit)
    {
        while (retainedMemorySizeBytes > memoryLimit && memoryLru.First is { } first)
        {
            RemoveMemoryArtifact(first.Value);
        }
    }

    private void ClearMemoryCacheCore()
    {
        memoryArtifacts.Clear();
        memoryLru.Clear();
        retainedMemorySizeBytes = 0;
        retainedAccessTouches.Clear();
        completedObsoleteCacheChecks.Clear();
    }

    private static long GetMemoryLimit(SwShCacheSettings settings)
    {
        return Math.Clamp(
            settings.MaxCacheSizeBytes / 8,
            MinimumMemoryCacheSizeBytes,
            MaximumMemoryCacheSizeBytes);
    }

    private bool TryReadPersistentArtifact<T>(
        SwShCacheSourceIdentity source,
        SwShCacheArtifactDescriptor artifact,
        string identityKey,
        string artifactKey,
        string typeIdentity,
        SwShCacheSettings settings,
        out byte[] payload,
        out T value)
    {
        payload = [];
        value = default!;
        var identityDirectory = GetIdentityDirectory(identityKey);
        if (!Directory.Exists(identityDirectory))
        {
            return false;
        }

        var manifest = TryReadJson<SourceManifestDocument>(
            Path.Combine(identityDirectory, SourceManifestFileName),
            MaximumManifestBytes);
        if (manifest is null
            || manifest.SchemaVersion != CacheSchemaVersion
            || !string.Equals(manifest.IdentityKey, identityKey, StringComparison.Ordinal)
            || !IsValidSourceIdentity(manifest.Source)
            || !SourceIdentityEquals(manifest.Source, source))
        {
            if (!isReadWorker)
            {
                DeleteTrackedDirectory(identityDirectory);
            }

            return false;
        }

        var artifactPath = GetArtifactPath(identityKey, artifactKey);
        var metadataPath = GetArtifactMetadataPath(identityKey, artifactKey);
        var metadata = TryReadJson<ArtifactMetadataDocument>(metadataPath, MaximumMetadataBytes);
        if (!File.Exists(artifactPath)
            || metadata is null
            || metadata.SchemaVersion != CacheSchemaVersion
            || !string.Equals(metadata.IdentityKey, identityKey, StringComparison.Ordinal)
            || !string.Equals(metadata.Kind, artifact.Kind, StringComparison.Ordinal)
            || !string.Equals(metadata.Key, artifact.Key, StringComparison.Ordinal)
            || metadata.Policy != artifact.Policy
            || !string.Equals(metadata.TypeIdentity, typeIdentity, StringComparison.Ordinal)
            || metadata.PayloadLength < 0
            || metadata.PayloadLength > settings.MaxCacheSizeBytes)
        {
            if (metadata is null || string.Equals(metadata.TypeIdentity, typeIdentity, StringComparison.Ordinal))
            {
                if (!isReadWorker)
                {
                    DeleteArtifactPair(artifactPath, metadataPath);
                }
            }

            return false;
        }

        try
        {
            var fileInfo = new FileInfo(artifactPath);
            if (fileInfo.Length != metadata.PayloadLength)
            {
                if (!isReadWorker)
                {
                    DeleteArtifactPair(artifactPath, metadataPath);
                }

                return false;
            }

            payload = ReadAllBytesShared(artifactPath, settings.MaxCacheSizeBytes);
            if (payload.LongLength != metadata.PayloadLength
                || !string.Equals(
                    ComputeSha256(payload),
                    metadata.PayloadSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!isReadWorker)
                {
                    DeleteArtifactPair(artifactPath, metadataPath);
                }

                payload = [];
                return false;
            }

            value = JsonSerializer.Deserialize<T>(payload, JsonOptions)!;
            var now = DateTime.UtcNow;
            if (!isReadWorker && now - fileInfo.LastWriteTimeUtc >= AccessTouchInterval)
            {
                TryTouchFile(artifactPath, now);
            }

            try
            {
                if (!isReadWorker)
                {
                    TouchIdentity(identityKey, now, force: false);
                }
            }
            catch (Exception exception) when (IsDisposableCacheException(exception))
            {
                retainedPersistentCacheSizeBytes = null;
            }

            return true;
        }
        catch (JsonException)
        {
            if (!isReadWorker)
            {
                DeleteArtifactPair(artifactPath, metadataPath);
            }

            payload = [];
            value = default!;
            return false;
        }
        catch (NotSupportedException)
        {
            payload = [];
            value = default!;
            return false;
        }
        catch (IOException)
        {
            payload = [];
            value = default!;
            return false;
        }
    }

    private void EnsureSourceManifest(SwShCacheSourceIdentity source, string identityKey)
    {
        var identityDirectory = GetIdentityDirectory(identityKey);
        var manifestPath = Path.Combine(identityDirectory, SourceManifestFileName);
        var existing = TryReadJson<SourceManifestDocument>(manifestPath, MaximumManifestBytes);
        if (existing is not null
            && existing.SchemaVersion == CacheSchemaVersion
            && string.Equals(existing.IdentityKey, identityKey, StringComparison.Ordinal)
            && IsValidSourceIdentity(existing.Source)
            && SourceIdentityEquals(existing.Source, source))
        {
            return;
        }

        if (Directory.Exists(identityDirectory))
        {
            DeleteTrackedDirectory(identityDirectory);
        }

        Directory.CreateDirectory(GetArtifactsDirectory(identityKey));
        WriteTrackedJsonAtomic(
            manifestPath,
            new SourceManifestDocument(CacheSchemaVersion, identityKey, source));
    }

    private void DeleteObsoleteSourceCaches(SwShCacheSourceIdentity activeSource)
    {
        if (isReadWorker)
        {
            return;
        }

        var activeIdentityKey = GetIdentityKey(activeSource);
        var stableSourceKey = GetStableSourceKey(activeSource);
        var cleanupKey = $"{stableSourceKey}:{activeIdentityKey}";
        if (completedObsoleteCacheChecks.Contains(cleanupKey))
        {
            return;
        }

        if (completedObsoleteCacheChecks.Count >= 256)
        {
            completedObsoleteCacheChecks.Clear();
        }

        foreach (var directory in EnumerateIdentityDirectories(MaximumPruneDirectoryCount))
        {
            var directoryName = Path.GetFileName(directory);
            if (string.Equals(directoryName, activeIdentityKey, StringComparison.Ordinal))
            {
                continue;
            }

            var manifest = TryReadJson<SourceManifestDocument>(
                Path.Combine(directory, SourceManifestFileName),
                MaximumManifestBytes);
            if (manifest is not null
                && manifest.SchemaVersion == CacheSchemaVersion
                && IsValidSourceIdentity(manifest.Source)
                && string.Equals(GetStableSourceKey(manifest.Source), stableSourceKey, StringComparison.Ordinal))
            {
                DeleteTrackedDirectory(directory);
            }
        }

        completedObsoleteCacheChecks.Add(cleanupKey);
    }

    private bool SourceIdentityEquals(SwShCacheSourceIdentity left, SwShCacheSourceIdentity right)
    {
        if (left.CacheSchemaVersion != right.CacheSchemaVersion
            || left.SelectedGame != right.SelectedGame
            || !string.Equals(left.ParserVersion, right.ParserVersion, StringComparison.Ordinal)
            || left.Sources.Count != right.Sources.Count)
        {
            return false;
        }

        var leftSources = left.Sources.OrderBy(static stamp => stamp.SourceLayer)
            .ThenBy(static stamp => NormalizePathForIdentity(stamp.FullPath), StringComparer.Ordinal)
            .ToArray();
        var rightSources = right.Sources.OrderBy(static stamp => stamp.SourceLayer)
            .ThenBy(static stamp => NormalizePathForIdentity(stamp.FullPath), StringComparer.Ordinal)
            .ToArray();
        for (var i = 0; i < leftSources.Length; i++)
        {
            var leftStamp = leftSources[i];
            var rightStamp = rightSources[i];
            if (leftStamp.SourceLayer != rightStamp.SourceLayer
                || leftStamp.Length != rightStamp.Length
                || leftStamp.LastWriteTimeUtc.ToUniversalTime() != rightStamp.LastWriteTimeUtc.ToUniversalTime()
                || !string.Equals(leftStamp.FullPath, rightStamp.FullPath, FilePathComparison)
                || !string.Equals(leftStamp.Sha256, rightStamp.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidSourceIdentity(SwShCacheSourceIdentity? source)
    {
        if (source is null)
        {
            return false;
        }

        try
        {
            ValidateSourceIdentity(source);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private void TouchIdentity(string identityKey, DateTime now, bool force)
    {
        if (!force
            && retainedAccessTouches.TryGetValue(identityKey, out var lastTouch)
            && now - lastTouch < AccessTouchInterval)
        {
            return;
        }

        var path = Path.Combine(GetIdentityDirectory(identityKey), AccessMetadataFileName);
        if (!force)
        {
            var existing = TryReadJson<AccessMetadataDocument>(path, MaximumMetadataBytes);
            if (existing is not null && now - existing.LastAccessUtc < AccessTouchInterval)
            {
                retainedAccessTouches[identityKey] = existing.LastAccessUtc;
                return;
            }
        }

        WriteTrackedJsonAtomic(path, new AccessMetadataDocument(CacheSchemaVersion, identityKey, now));
        if (retainedAccessTouches.Count >= 512)
        {
            retainedAccessTouches.Clear();
        }

        retainedAccessTouches[identityKey] = now;
    }

    private bool PruneIfNeeded(SwShCacheSettings settings, SwShCacheSourceIdentity? activeSource)
    {
        if (isReadWorker)
        {
            return activeSource is not null
                && Directory.Exists(GetIdentityDirectory(GetIdentityKey(activeSource)));
        }

        var activeIdentityKey = activeSource is null ? null : GetIdentityKey(activeSource);
        var activeDirectory = activeIdentityKey is null ? null : GetIdentityDirectory(activeIdentityKey);
        var activeExisted = activeDirectory is not null && Directory.Exists(activeDirectory);
        var size = GetPersistentCacheSize();
        if (size <= settings.MaxCacheSizeBytes)
        {
            return activeExisted;
        }

        var inactiveDirectories = EnumerateIdentityDirectories(MaximumPruneDirectoryCount)
            .Where(directory => !string.Equals(Path.GetFileName(directory), activeIdentityKey, StringComparison.Ordinal))
            .OrderBy(GetIdentityLastAccessUtc)
            .ToArray();
        foreach (var directory in inactiveDirectories)
        {
            DeleteTrackedDirectory(directory);
            size = GetPersistentCacheSize();
            if (size <= settings.MaxCacheSizeBytes)
            {
                return activeExisted && activeDirectory is not null && Directory.Exists(activeDirectory);
            }
        }

        var artifacts = EnumerateArtifactPayloads(MaximumPruneArtifactCount)
            .OrderBy(static path => GetFileLastWriteTimeUtcSafe(path))
            .ToArray();
        foreach (var artifactPath in artifacts)
        {
            var metadataPath = GetMetadataPathForArtifactPath(artifactPath);
            DeleteArtifactPair(artifactPath, metadataPath);
            size = GetPersistentCacheSize();
            if (size <= settings.MaxCacheSizeBytes)
            {
                return activeExisted && activeDirectory is not null && Directory.Exists(activeDirectory);
            }
        }

        foreach (var directory in EnumerateIdentityDirectories(MaximumPruneDirectoryCount)
                     .OrderBy(GetIdentityLastAccessUtc))
        {
            DeleteTrackedDirectory(directory);
            size = GetPersistentCacheSize();
            if (size <= settings.MaxCacheSizeBytes)
            {
                break;
            }
        }

        if (GetPersistentCacheSize() > settings.MaxCacheSizeBytes)
        {
            ResetPersistentCache();
        }

        return activeExisted && activeDirectory is not null && Directory.Exists(activeDirectory);
    }

    private void TryPrune(SwShCacheSettings settings, SwShCacheSourceIdentity? activeSource)
    {
        try
        {
            PruneIfNeeded(settings, activeSource);
        }
        catch (Exception exception) when (IsDisposableCacheException(exception))
        {
            retainedPersistentCacheSizeBytes = null;
        }
    }

    private IEnumerable<string> EnumerateIdentityDirectories(int maximumCount)
    {
        if (!Directory.Exists(ProjectsPath))
        {
            yield break;
        }

        var count = 0;
        foreach (var directory in Directory.EnumerateDirectories(ProjectsPath, "*", TopLevelCacheEnumeration))
        {
            if (++count > maximumCount)
            {
                yield break;
            }

            yield return directory;
        }
    }

    private IEnumerable<string> EnumerateArtifactPayloads(int maximumCount)
    {
        var count = 0;
        foreach (var identityDirectory in EnumerateIdentityDirectories(MaximumPruneDirectoryCount))
        {
            var artifactsDirectory = Path.Combine(identityDirectory, ArtifactsDirectoryName);
            if (!Directory.Exists(artifactsDirectory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(artifactsDirectory, "*.json", TopLevelCacheEnumeration))
            {
                if (path.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (++count > maximumCount)
                {
                    yield break;
                }

                yield return path;
            }
        }
    }

    private DateTime GetIdentityLastAccessUtc(string identityDirectory)
    {
        var access = TryReadJson<AccessMetadataDocument>(
            Path.Combine(identityDirectory, AccessMetadataFileName),
            MaximumMetadataBytes);
        return access?.LastAccessUtc ?? GetDirectoryLastWriteTimeUtcSafe(identityDirectory);
    }

    private static DateTime GetDirectoryLastWriteTimeUtcSafe(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static DateTime GetFileLastWriteTimeUtcSafe(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private bool RemovePersistentArtifact(string identityKey, string artifactKey)
    {
        return DeleteArtifactPair(
            GetArtifactPath(identityKey, artifactKey),
            GetArtifactMetadataPath(identityKey, artifactKey));
    }

    private bool DeleteArtifactPair(string artifactPath, string metadataPath)
    {
        var removedArtifact = DeleteTrackedFile(artifactPath);
        var removedMetadata = DeleteTrackedFile(metadataPath);
        return removedArtifact || removedMetadata;
    }

    private bool DeleteTrackedFile(string path)
    {
        if (isReadWorker)
        {
            return false;
        }

        EnsurePathUnderProjects(path);
        if (!File.Exists(path))
        {
            return false;
        }

        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (IOException)
        {
            retainedPersistentCacheSizeBytes = null;
            return false;
        }

        BeginTrackedMutation();
        try
        {
            File.Delete(path);
            CompleteTrackedMutation(-length);
            return true;
        }
        catch
        {
            retainedPersistentCacheSizeBytes = null;
            throw;
        }
    }

    private bool DeleteTrackedDirectory(string path)
    {
        if (isReadWorker)
        {
            return false;
        }

        EnsurePathUnderProjects(path);
        if (PathsEqual(path, ProjectsPath))
        {
            throw new InvalidOperationException("The projects cache root cannot be deleted as an artifact directory.");
        }

        if (!Directory.Exists(path))
        {
            return false;
        }

        var measured = TryMeasureDirectory(path, MaximumMeasuredCacheFileCount, out var size);
        if (measured)
        {
            BeginTrackedMutation();
        }
        else
        {
            WriteSizeLedger(new SizeLedgerDocument(
                CacheSchemaVersion,
                retainedPersistentCacheSizeBytes ?? 0,
                IsDirty: true,
                DateTime.UtcNow));
        }
        try
        {
            Directory.Delete(path, recursive: true);
            if (measured)
            {
                CompleteTrackedMutation(-size);
            }
            else
            {
                retainedPersistentCacheSizeBytes = null;
                ReconcilePersistentCacheSize();
            }

            return true;
        }
        catch
        {
            retainedPersistentCacheSizeBytes = null;
            throw;
        }
    }

    private void WriteTrackedJsonAtomic<T>(string destinationPath, T value)
    {
        WriteTrackedBytesAtomic(destinationPath, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    }

    private void WriteTrackedBytesAtomic(string destinationPath, byte[] bytes)
    {
        EnsurePathUnderProjects(destinationPath);
        var oldLength = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;
        BeginTrackedMutation();
        try
        {
            WriteBytesAtomic(destinationPath, bytes);
            CompleteTrackedMutation(bytes.LongLength - oldLength);
        }
        catch
        {
            retainedPersistentCacheSizeBytes = null;
            throw;
        }
    }

    private void BeginTrackedMutation()
    {
        var size = GetPersistentCacheSize();
        WriteSizeLedger(new SizeLedgerDocument(CacheSchemaVersion, size, IsDirty: true, DateTime.UtcNow));
    }

    private void CompleteTrackedMutation(long delta)
    {
        var current = retainedPersistentCacheSizeBytes ?? 0;
        retainedPersistentCacheSizeBytes = Math.Max(0, current + delta);
        WriteSizeLedger(new SizeLedgerDocument(
            CacheSchemaVersion,
            retainedPersistentCacheSizeBytes.Value,
            IsDirty: false,
            DateTime.UtcNow));
    }

    private long GetPersistentCacheSize()
    {
        if (retainedPersistentCacheSizeBytes is { } retained
            && IsProjectsLedgerShapePlausible(retained))
        {
            return retained;
        }

        retainedPersistentCacheSizeBytes = null;

        var ledger = TryReadJson<SizeLedgerDocument>(SizeLedgerPath, MaximumMetadataBytes);
        if (ledger is not null
            && ledger.SchemaVersion == CacheSchemaVersion
            && !ledger.IsDirty
            && ledger.ProjectsSizeBytes >= 0
            && IsProjectsLedgerShapePlausible(ledger.ProjectsSizeBytes))
        {
            retainedPersistentCacheSizeBytes = ledger.ProjectsSizeBytes;
            return ledger.ProjectsSizeBytes;
        }

        if (isReadWorker)
        {
            var measured = TryMeasureDirectory(
                ProjectsPath,
                MaximumMeasuredCacheFileCount,
                out var readOnlySize);
            return measured ? readOnlySize : 0;
        }

        return ReconcilePersistentCacheSize();
    }

    private bool IsProjectsLedgerShapePlausible(long ledgerSize)
    {
        try
        {
            var hasEntries = Directory.Exists(ProjectsPath)
                && Directory.EnumerateFileSystemEntries(
                    ProjectsPath,
                    "*",
                    TopLevelCacheEnumeration).Any();
            return ledgerSize == 0 ? !hasEntries : hasEntries;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private long ReconcilePersistentCacheSize()
    {
        if (!TryMeasureDirectory(ProjectsPath, MaximumMeasuredCacheFileCount, out var size))
        {
            ResetPersistentCache();
            return 0;
        }

        retainedPersistentCacheSizeBytes = size;
        WriteSizeLedger(new SizeLedgerDocument(CacheSchemaVersion, size, IsDirty: false, DateTime.UtcNow));
        return size;
    }

    private static bool TryMeasureDirectory(string path, int maximumFileCount, out long size)
    {
        size = 0;
        if (!Directory.Exists(path))
        {
            return true;
        }

        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", RecursiveCacheEnumeration))
            {
                if (++count > maximumFileCount)
                {
                    return false;
                }

                var length = new FileInfo(file).Length;
                size = length > long.MaxValue - size ? long.MaxValue : size + length;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ResetPersistentCache()
    {
        EnsureOwnerCacheMutation("reset the Sword/Shield persistent cache");
        Directory.CreateDirectory(ProjectsPath);
        var current = retainedPersistentCacheSizeBytes ?? 0;
        WriteSizeLedger(new SizeLedgerDocument(CacheSchemaVersion, current, IsDirty: true, DateTime.UtcNow));
        try
        {
            EnsurePathUnderCacheRoot(ProjectsPath);
            Directory.Delete(ProjectsPath, recursive: true);
            Directory.CreateDirectory(ProjectsPath);
            retainedPersistentCacheSizeBytes = 0;
            completedObsoleteCacheChecks.Clear();
            retainedAccessTouches.Clear();
            WriteSizeLedger(new SizeLedgerDocument(CacheSchemaVersion, 0, IsDirty: false, DateTime.UtcNow));
        }
        catch
        {
            retainedPersistentCacheSizeBytes = null;
            throw;
        }
    }

    private void WriteSizeLedger(SizeLedgerDocument ledger)
    {
        WriteJsonAtomic(SizeLedgerPath, ledger);
    }

    private SwShCacheSettings ReadSettings()
    {
        var settings = TryReadJson<SwShCacheSettings>(SettingsPath, MaximumSettingsBytes);
        if (settings is not null && Enum.IsDefined(settings.Mode))
        {
            var normalized = settings with { MaxCacheSizeBytes = ClampMaxCacheSize(settings.MaxCacheSizeBytes) };
            if (!isReadWorker && normalized != settings)
            {
                WriteJsonAtomic(SettingsPath, normalized);
            }

            return normalized;
        }

        var defaults = new SwShCacheSettings(SwShCacheMode.Balanced, DefaultMaxCacheSizeBytes);
        if (!isReadWorker)
        {
            WriteJsonAtomic(SettingsPath, defaults);
        }

        return defaults;
    }

    private static long ClampMaxCacheSize(long value)
    {
        return Math.Clamp(value, MinimumMaxCacheSizeBytes, MaximumMaxCacheSizeBytes);
    }

    private static bool IsDisposableCacheException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    private SwShCacheStatus CreateStatus(SwShCacheSettings settings, bool activeProjectPreserved)
    {
        var message = settings.Mode switch
        {
            SwShCacheMode.Minimal => "Sword/Shield cache is using bounded session memory only.",
            SwShCacheMode.Balanced => "Sword/Shield cache is ready for reusable core artifacts.",
            SwShCacheMode.Performance => "Sword/Shield cache is ready for the verified Placement catalog and future performance artifacts.",
            _ => "Sword/Shield cache is ready.",
        };
        return new SwShCacheStatus(
            settings,
            CacheSizeBytes: GetPersistentCacheSize(),
            WarmupCompleted: 0,
            WarmupTotal: 0,
            ProgressPercent: 0,
            Phase: settings.Mode == SwShCacheMode.Minimal ? "Minimal mode" : "Cache ready",
            Message: message,
            IsActiveProjectPreserved: activeProjectPreserved);
    }

    private T? TryReadJson<T>(string path, int maximumBytes)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            if (stream.Length < 0 || stream.Length > maximumBytes)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (IOException)
        {
            return default;
        }
    }

    private void WriteJsonAtomic<T>(string destinationPath, T value)
    {
        WriteBytesAtomic(destinationPath, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    }

    private void WriteBytesAtomic(string destinationPath, byte[] bytes)
    {
        EnsureOwnerCacheMutation("publish Sword/Shield persistent cache data");
        EnsurePathUnderCacheRoot(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        Directory.CreateDirectory(TempPath);
        var tempPath = Path.Combine(TempPath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private void EnsureRoot()
    {
        if (isReadWorker)
        {
            return;
        }

        var projectsDirectoryExisted = Directory.Exists(ProjectsPath);
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(ProjectsPath);
        Directory.CreateDirectory(TempPath);
        if (!projectsDirectoryExisted)
        {
            retainedPersistentCacheSizeBytes = 0;
            completedObsoleteCacheChecks.Clear();
            retainedAccessTouches.Clear();
            WriteSizeLedger(new SizeLedgerDocument(
                CacheSchemaVersion,
                ProjectsSizeBytes: 0,
                IsDirty: false,
                DateTime.UtcNow));
        }

        if (!tempCleanupCompleted)
        {
            CleanupTempDirectory();
            tempCleanupCompleted = true;
        }
    }

    private void CleanupTempDirectory()
    {
        if (isReadWorker)
        {
            return;
        }

        Directory.CreateDirectory(TempPath);
        var cutoff = DateTime.UtcNow - OrphanTempFileAge;
        foreach (var path in Directory.EnumerateFiles(TempPath, "*.tmp", TopLevelCacheEnumeration))
        {
            if (GetFileLastWriteTimeUtcSafe(path) <= cutoff)
            {
                TryDeleteFile(path);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
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
                $"The Sword/Shield cache payload exceeds its safe read limit of {maximumBytes} bytes.");
        }

        var bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void EnsureOwnerCacheMutation(string operation)
    {
        if (isReadWorker)
        {
            throw new InvalidOperationException(
                $"An isolated managed read worker cannot {operation}.");
        }
    }

    private static void TryTouchFile(string path, DateTime lastWriteTimeUtc)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ResolveDefaultCacheRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Local application data is unavailable for the Sword/Shield cache.");
        }

        return Path.Combine(localAppData, "KM Editor", "SwordShieldCache");
    }

    private void EnsurePathUnderProjects(string path)
    {
        EnsurePathUnderRoot(path, ProjectsPath, allowRoot: true);
    }

    private void EnsurePathUnderCacheRoot(string path)
    {
        EnsurePathUnderRoot(path, cacheRoot, allowRoot: true);
    }

    private static void EnsurePathUnderRoot(string path, string root, bool allowRoot)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (allowRoot && string.Equals(fullPath, fullRoot, FilePathComparison))
        {
            return;
        }

        var rootPrefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, FilePathComparison))
        {
            throw new InvalidOperationException("Cache operation resolved outside its designated cache root.");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            FilePathComparison);
    }

    private string GetIdentityDirectory(string identityKey) => Path.Combine(ProjectsPath, identityKey);

    private string GetArtifactsDirectory(string identityKey) =>
        Path.Combine(GetIdentityDirectory(identityKey), ArtifactsDirectoryName);

    private string GetArtifactPath(string identityKey, string artifactKey) =>
        Path.Combine(GetArtifactsDirectory(identityKey), $"{artifactKey}.json");

    private string GetArtifactMetadataPath(string identityKey, string artifactKey) =>
        Path.Combine(GetArtifactsDirectory(identityKey), $"{artifactKey}.meta.json");

    private static string GetMetadataPathForArtifactPath(string artifactPath)
    {
        return Path.Combine(
            Path.GetDirectoryName(artifactPath)!,
            $"{Path.GetFileNameWithoutExtension(artifactPath)}.meta.json");
    }

    private string SettingsPath => Path.Combine(cacheRoot, SettingsFileName);

    private string SizeLedgerPath => Path.Combine(cacheRoot, SizeLedgerFileName);

    private string ProjectsPath => Path.Combine(cacheRoot, ProjectsDirectoryName);

    private string TempPath => Path.Combine(cacheRoot, TempDirectoryName);

    private sealed record SourceManifestDocument(
        int SchemaVersion,
        string IdentityKey,
        SwShCacheSourceIdentity Source);

    private sealed record ArtifactMetadataDocument(
        int SchemaVersion,
        string IdentityKey,
        string Kind,
        string Key,
        SwShCacheArtifactPolicy Policy,
        string TypeIdentity,
        long PayloadLength,
        string PayloadSha256,
        DateTime CreatedUtc);

    private sealed record AccessMetadataDocument(
        int SchemaVersion,
        string IdentityKey,
        DateTime LastAccessUtc);

    private sealed record SizeLedgerDocument(
        int SchemaVersion,
        long ProjectsSizeBytes,
        bool IsDirty,
        DateTime UpdatedUtc);

    private sealed record MemoryArtifact(
        string TypeIdentity,
        byte[] Payload,
        LinkedListNode<string> LruNode);
}
