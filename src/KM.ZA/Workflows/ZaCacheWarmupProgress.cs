// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KM.ZA.Workflows;

public sealed partial class ZaCacheManager
{
    private const string ProgressFileName = "warmup-progress.json";
    private const int ProgressVersion = 1;
    private DateTime nextProgressCheckpointUtc;

    // A progress receipt avoids opening thousands of metadata documents on restart.
    // It is only preparation bookkeeping. Data reads still validate their artifacts.
    private sealed record WarmupProgressReceipt(int Version, ZaCacheSourceFingerprint Source,
        ZaCacheMode Mode, string PlanHash, IReadOnlyList<WarmupProgressEntry> Entries);
    private sealed record WarmupProgressEntry(string Path, ArtifactStamp Metadata,
        ArtifactStamp? PayloadMetadata, ArtifactStamp? Payload);
    private sealed record ArtifactStamp(long Length, long LastWriteTicks, long CreationTicks)
    {
        public static ArtifactStamp From(FileInfo file) =>
            new(file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks);
    }

    private bool TryReadWarmupPlan(ZaCacheProjectContext context, out IReadOnlyList<string> paths)
    {
        paths = [];
        try
        {
            using var stream = OpenJsonReadStream(GetWarmupPathsPath(context));
            var manifest = JsonSerializer.Deserialize<ZaCacheWarmupPathsFile>(stream, JsonOptions);
            if (manifest is null || manifest.CacheSchemaVersion != CacheSchemaVersion
                || manifest.Source != context.Source || !IsValidWarmupPathList(manifest.VirtualPaths)
                || manifest.PlanHash != WarmupPlanHash(manifest.VirtualPaths))
                return false;
            paths = manifest.VirtualPaths;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return false; }
    }

    // Change the revision when discovery rules change. Changes to the built-in
    // editor inputs invalidate the plan automatically, without dropping payloads.
    private static string WarmupPlanHash(IReadOnlyList<string> paths) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "warmup-plan-v1\n" + string.Join('\n', WarmupVirtualPaths) + "\n" + string.Join('\n', paths))));

    private static bool VerifyPayloadHash(string path, string expected) =>
        expected.Length == 64 && string.Equals(expected,
            Convert.ToHexString(SHA256.HashData(ReadAllBytesShared(path, MaximumPerformanceWarmupFileBytes))),
            StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, ArtifactStamp> CaptureArtifactDirectory(string directory)
    {
        var result = new Dictionary<string, ArtifactStamp>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory)) return result;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A cache directory is not a physical directory.");
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", CacheDirectoryEnumeration))
        {
            if (result.Count >= MaximumCacheTraversalEntries
                || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The cache inventory exceeds its supported bounds.");
            result.Add(file.Name, ArtifactStamp.From(file));
        }
        return result;
    }

    private Dictionary<string, WarmupProgressEntry> ObserveWarmupArtifacts(
        ZaCacheSettings settings, ZaCacheProjectContext context, IReadOnlyList<string> paths)
    {
        var metadata = CaptureArtifactDirectory(GetMetadataDirectory(context));
        var payloads = settings.Mode == ZaCacheMode.Performance
            ? CaptureArtifactDirectory(GetPayloadDirectory(context)) : [];
        var entries = new Dictionary<string, WarmupProgressEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var key = GetVirtualPathKey(path);
            if (!metadata.TryGetValue(key + ".json", out var item)) continue;
            ArtifactStamp? payloadMetadata = null;
            ArtifactStamp? payload = null;
            if (settings.Mode == ZaCacheMode.Performance
                && (!payloads.TryGetValue(key + ".json", out payloadMetadata)
                    || !payloads.TryGetValue(key + ".bin", out payload))) continue;
            entries.Add(path, new(path, item, payloadMetadata, payload));
        }
        return entries;
    }

    private HashSet<string> RestoreWarmupProgress(ZaCacheSettings settings,
        ZaCacheProjectContext context, IReadOnlyList<string> paths)
    {
        var observed = ObserveWarmupArtifacts(settings, context, paths);
        var recorded = new Dictionary<string, WarmupProgressEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = OpenJsonReadStream(Path.Combine(context.ProjectDirectory, ProgressFileName));
            var receipt = JsonSerializer.Deserialize<WarmupProgressReceipt>(stream, JsonOptions);
            if (receipt is not null && receipt.Version == ProgressVersion && receipt.Source == context.Source
                && receipt.Mode == settings.Mode && receipt.PlanHash == WarmupPlanHash(paths)
                && receipt.Entries is not null && receipt.Entries.Count <= paths.Count)
            {
                foreach (var entry in receipt.Entries)
                {
                    if (entry is null || string.IsNullOrWhiteSpace(entry.Path) || entry.Metadata is null
                        || !recorded.TryAdd(entry.Path, entry))
                    { recorded.Clear(); break; }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { }

        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!observed.TryGetValue(path, out var current)) continue;
            if ((recorded.TryGetValue(path, out var previous) && current == previous)
                || IsWarmupEntryComplete(settings, context, path, verifyPayload: true))
                completed.Add(path);
        }
        if (recorded.Count != completed.Count || completed.Any(path =>
                !recorded.TryGetValue(path, out var previous) || previous != observed[path]))
            SaveWarmupProgress(settings, context, paths, completed, observed, force: true);
        return completed;
    }

    private void SaveWarmupProgress(ZaCacheSettings settings, ZaCacheProjectContext context,
        IReadOnlyList<string> paths, HashSet<string> completed,
        Dictionary<string, WarmupProgressEntry>? observed = null, bool force = false)
    {
        if (isReadWorker || completed.Count == 0
            || (!force && completed.Count < paths.Count && DateTime.UtcNow < nextProgressCheckpointUtc))
            return;
        try
        {
            observed ??= ObserveWarmupArtifacts(settings, context, paths);
            var entries = paths.Where(path => completed.Contains(path) && observed.ContainsKey(path))
                .Select(path => observed[path]).ToArray();
            var receiptPath = Path.Combine(context.ProjectDirectory, ProgressFileName);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                new WarmupProgressReceipt(ProgressVersion, context.Source, settings.Mode, WarmupPlanHash(paths), entries),
                JsonOptions);
            // Optional bookkeeping must not evict useful data or exceed the cache budget.
            if (bytes.LongLength > MaximumCacheJsonFileBytes
                || GetCacheContentSize() + bytes.LongLength - GetTrackedFileLength(receiptPath) > settings.MaxCacheSizeBytes)
                return;
            WriteBytesAtomic(receiptPath, bytes);
            nextProgressCheckpointUtc = DateTime.UtcNow.AddSeconds(2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed optional checkpoint must not discard verified cache data.
        }
    }

    private void RefreshWarmupProgress()
    {
        retainedWarmupProgressSource = null;
        retainedWarmupProgressMode = null;
        retainedWarmupProgressPaths = null;
        retainedCompletedWarmupPaths = null;
    }
}
