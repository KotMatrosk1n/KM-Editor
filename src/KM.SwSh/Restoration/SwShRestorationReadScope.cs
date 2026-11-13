// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.SwSh.Editing;

namespace KM.SwSh.Restoration;

/// <summary>Reuses verified immutable artifacts during one operation, never mutable output state.</summary>
public sealed class SwShRestorationReadScope : IDisposable
{
    private const long MaximumCachedBytes = 512L * 1024 * 1024;
    // Leave room for one artifact at the coordinator's per-file limit. Stop before
    // prefetching would evict the prefix that the caller is about to consume.
    private const long PrefetchStopBytes = MaximumCachedBytes - 192L * 1024 * 1024;
    private static readonly AsyncLocal<SwShRestorationReadScope?> Ambient = new();
    private readonly SwShRestorationReadScope? parent;
    private readonly SwShRestorationReadScope owner;
    private readonly ProjectPaths paths;
    private readonly string? outputRoot;
    private readonly Dictionary<string, Artifact> artifacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> dataIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (bool IsBundle, SwShPreparedRestoration Prepared)> data = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private long cachedBytes;
    private bool disposed;

    private sealed class Artifact(byte[] bytes, FileStream lease)
    {
        internal byte[]? Bytes = bytes;
        internal readonly int EncodedLength = bytes.Length;
        internal readonly bool IsBundle = bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K';
        internal readonly FileStream Lease = lease;
        internal SwShPreparedRestoration? Prepared;
        internal string? ProjectionKey;
        internal IReadOnlySet<string>? ProjectionPaths;
        internal SwShPreparedRestoration? Projection;
        internal long Size = bytes.LongLength;
    }

    private SwShRestorationReadScope(ProjectPaths project)
    {
        paths = SwShChangePlanSourceGuard.OriginalPaths(project);
        outputRoot = NormalizeRoot(paths.OutputRootPath);
        parent = Ambient.Value;
        owner = Find(paths) ?? this;
        Ambient.Value = this;
    }

    public static SwShRestorationReadScope Begin(ProjectPaths paths) => new(paths);

    internal static SwShRestorationReadScope? Find(ProjectPaths paths)
    {
        paths = SwShChangePlanSourceGuard.OriginalPaths(paths);
        var outputRoot = NormalizeRoot(paths.OutputRootPath);
        for (var scope = Ambient.Value; scope is not null; scope = scope.parent)
            if (!scope.disposed && !scope.owner.disposed && paths.SelectedGame == scope.paths.SelectedGame
                && outputRoot is not null && string.Equals(scope.outputRoot, outputRoot,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return scope.owner;
        return null;
    }

    private static string? NormalizeRoot(string? path) => string.IsNullOrWhiteSpace(path)
        ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal byte[] ReadBytes(string hash)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Ensure([hash]);
            var artifact = artifacts[hash];
            if (artifact.Bytes is not null) return artifact.Bytes.ToArray();
            // Decoded artifacts retain their protected file lease, not a second payload buffer.
            if (artifact.Lease.Length != artifact.EncodedLength)
                throw new InvalidDataException("Restoration artifact length has changed.");
            var bytes = new byte[artifact.EncodedLength];
            artifact.Lease.Position = 0;
            artifact.Lease.ReadExactly(bytes);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash)
                throw new InvalidDataException("Restoration artifact content does not match its identity.");
            return bytes;
        }
    }

    internal void Prefetch(IEnumerable<string> hashes)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cachedBytes >= PrefetchStopBytes) return;
            var missing = hashes.Distinct(StringComparer.Ordinal).Where(h => !artifacts.ContainsKey(h)).ToArray();
            OutputTransactionCoordinator.ForProject(paths).VisitPreservationArtifactsAsync(missing, (hash, bytes, lease) =>
            {
                artifacts.Add(hash, new(bytes, lease));
                cachedBytes += bytes.LongLength;
                Trim(hash);
                return cachedBytes < PrefetchStopBytes;
            }).GetAwaiter().GetResult();
        }
    }

    internal void PrefetchContributions(IEnumerable<string> hashes, IReadOnlySet<string> contributionPaths)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var dataOnly = contributionPaths.IsSubsetOf(SwShRestorationStateStore.DataPaths);
            var missing = hashes.Distinct(StringComparer.Ordinal).Where(h => !artifacts.ContainsKey(h) && !(dataOnly && data.ContainsKey(h))).ToArray();
            var key = string.Join('\n', contributionPaths.Order(StringComparer.Ordinal));
            OutputTransactionCoordinator.ForProject(paths).VisitPreservationArtifactsAsync(missing, (hash, bytes, lease) =>
            {
                // Descriptors resolve their base after the outer coordinator lease
                // has ended. Plain legacy bundles can be projected immediately.
                if (bytes.Length < 2 || bytes[0] != 'P' || bytes[1] != 'K')
                {
                    artifacts.Add(hash, new(bytes, lease));
                    cachedBytes += bytes.LongLength;
                }
                else
                {
                    var prepared = SwShPreparedRestoration.DecodeContributions(bytes, contributionPaths);
                    if (prepared.Game != paths.SelectedGame) throw new InvalidDataException("Prepared restoration belongs to another game edition.");
                    var size = prepared.Contributions.Sum(c => (c.Before?.LongLength ?? 0) + c.After.LongLength);
                    artifacts.Add(hash, new(bytes, lease) { Bytes = null, Projection = prepared, ProjectionKey = key, ProjectionPaths = contributionPaths.ToHashSet(StringComparer.Ordinal), Size = size });
                    cachedBytes += size;
                }
                Trim(hash);
                return true;
            }).GetAwaiter().GetResult();
        }
    }

    internal IReadOnlyDictionary<string, SwShPreparedRestoration> ReadPrepared(IReadOnlyCollection<string> hashes)
    {
        if (hashes.Count > 468) throw new OutputLimitExceededException("The restoration artifact set exceeds its supported size.");
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var result = new Dictionary<string, SwShPreparedRestoration>(StringComparer.Ordinal);
            long total = 0;
            // Bound each acquisition independently. Large valid sets can evict earlier entries.
            foreach (var hash in hashes)
            {
                Ensure([hash]);
                var artifact = artifacts[hash];
                total += artifact.EncodedLength;
                if (total > MaximumCachedBytes)
                    throw new OutputLimitExceededException("The restoration artifact set exceeds its supported size.");
                if (artifact.Prepared is null)
                {
                    var prepared = SwShRestorationStateStore.DecodePrepared(paths, ReadBytes(hash));
                    if (prepared.Game != paths.SelectedGame)
                        throw new InvalidDataException("Prepared restoration belongs to another game edition.");
                    artifact.Prepared = prepared;
                    var size = prepared.Contributions.SelectMany(c => c.Before is null ? new[] { c.After } : new[] { c.Before, c.After })
                        .Distinct<byte[]>(ReferenceEqualityComparer.Instance).Sum(b => b.LongLength);
                    cachedBytes += size - artifact.Size;
                    artifact.Size = size;
                    artifact.Bytes = null;
                    Trim(hash);
                }
                // Public callers may edit contribution buffers. Keep the verified copy private.
                result.Add(hash, Copy(artifact.Prepared));
            }
            return result;
        }
    }

    internal SwShPreparedRestoration ReadPreparedContributions(string hash, IReadOnlySet<string> contributionPaths)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (contributionPaths.IsSubsetOf(SwShRestorationStateStore.DataPaths) && data.TryGetValue(hash, out var indexed))
                return Copy(indexed.Prepared with { Contributions = indexed.Prepared.Contributions.Where(c => contributionPaths.Contains(c.Path)).ToArray() });
            Ensure([hash]);
            var artifact = artifacts[hash];
            if (artifact.Prepared is not null)
                return Copy(artifact.Prepared with { Contributions = artifact.Prepared.Contributions.Where(c => contributionPaths.Contains(c.Path)).ToArray() });
            if (artifact.ProjectionPaths is not null && contributionPaths.IsSubsetOf(artifact.ProjectionPaths))
                return Copy(artifact.Projection! with { Contributions = artifact.Projection!.Contributions.Where(c => contributionPaths.Contains(c.Path)).ToArray() });
            var key = string.Join('\n', contributionPaths.Order(StringComparer.Ordinal));
            if (artifact.ProjectionKey != key)
            {
                var prepared = SwShRestorationStateStore.DecodePrepared(paths, ReadBytes(hash), contributionPaths);
                if (prepared.Game != paths.SelectedGame)
                    throw new InvalidDataException("Prepared restoration belongs to another game edition.");
                artifact.Projection = prepared;
                artifact.ProjectionKey = key;
                artifact.ProjectionPaths = contributionPaths.ToHashSet(StringComparer.Ordinal);
                var size = prepared.Contributions.Sum(c => (c.Before?.LongLength ?? 0) + c.After.LongLength);
                cachedBytes += size - artifact.Size;
                artifact.Size = size;
                artifact.Bytes = null;
                Trim(hash);
            }
            return Copy(artifact.Projection!);
        }
    }

    internal void BindDataIndex(string? hash)
    {
        if (hash is null) return;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (dataIndexes.Contains(hash)) return;
            var index = SwShRestorationStateStore.DecodeDataIndex(ReadBytes(hash));
            foreach (var (identity, entry) in index)
            {
                if (entry.Prepared.Game != paths.SelectedGame) throw new InvalidDataException("Restoration data belongs to another game edition.");
                data[identity] = entry;
            }
            dataIndexes.Add(hash);
            if (data.Count > 1404) throw new InvalidDataException("Restoration data set exceeds its size limit.");
        }
    }

    internal bool IsPreparedBundle(string hash)
    {
        lock (sync)
        {
            if (data.TryGetValue(hash, out var indexed)) return indexed.IsBundle;
            Ensure([hash]);
            return artifacts[hash].IsBundle;
        }
    }

    internal SwShPreparedRestoration ReadBaseContributions(string hash, IReadOnlySet<string> contributionPaths)
    {
        lock (sync)
        {
            if (!IsPreparedBundle(hash)) throw new InvalidDataException("Restoration customization dependency is not a prepared bundle.");
            return ReadPreparedContributions(hash, contributionPaths);
        }
    }

    private void Ensure(IReadOnlyCollection<string> hashes)
    {
        var missing = hashes.Where(h => !artifacts.ContainsKey(h)).ToArray();
        if (missing.Length == 0) return;
        var acquired = OutputTransactionCoordinator.ForProject(paths).AcquirePreservationArtifactsAsync(missing).GetAwaiter().GetResult();
        try
        {
            foreach (var (hash, value) in acquired)
            {
                artifacts.Add(hash, new(value.Bytes, value.Lease));
                cachedBytes += value.Bytes.LongLength;
                Trim(hash);
            }
        }
        catch
        {
            foreach (var (hash, value) in acquired)
                if (!artifacts.ContainsKey(hash)) value.Lease.Dispose();
            throw;
        }
    }

    private void Trim(string retained)
    {
        foreach (var hash in artifacts.Keys.Where(h => h != retained).ToArray())
        {
            // A data customization retains a small descriptor and its original
            // bundle. Keep both for the registered set without evicting by count.
            if (cachedBytes <= MaximumCachedBytes && artifacts.Count <= 1404) break;
            var artifact = artifacts[hash];
            artifacts.Remove(hash);
            cachedBytes -= artifact.Size;
            artifact.Lease.Dispose();
        }
    }

    private static SwShPreparedRestoration Copy(SwShPreparedRestoration prepared) => prepared with
    {
        Sources = prepared.Sources.ToArray(),
        Contributions = prepared.Contributions.Select(c => c with { Before = c.Before?.ToArray(), After = c.After.ToArray() }).ToArray(),
        UnavailableSourceMoves = prepared.UnavailableSourceMoves.ToDictionary(p => p.Key, p => p.Value.ToArray()),
    };

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (Ambient.Value == this) Ambient.Value = parent;
            if (owner != this) return;
            foreach (var artifact in artifacts.Values) artifact.Lease.Dispose();
            artifacts.Clear();
            data.Clear();
            dataIndexes.Clear();
            cachedBytes = 0;
        }
    }
}
