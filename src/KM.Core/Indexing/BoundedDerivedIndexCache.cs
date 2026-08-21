// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Indexing;

/// <summary>
/// Coordinates conservative derived-entry size estimates; it does not measure physical memory.
/// </summary>
internal static class ProcessWideDerivedIndexCacheBudget
{
    private const int ProvisionMultiplier = 4;
    private const int CacheCeilingMultiplier = 2;
    private const long ExpectedAggregateEstimatedSizeBytes = checked(
        (3L * 128L + 256L + 192L) * 1024L * 1024L);
    internal const long MaximumEstimatedSizeBytes = checked(
        ExpectedAggregateEstimatedSizeBytes * ProvisionMultiplier * CacheCeilingMultiplier);

    private static readonly object SyncRoot = new();
    private static long currentSizeBytes;

    internal static bool TryResize(long releasedSizeBytes, long reservedSizeBytes)
    {
        if (releasedSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(releasedSizeBytes),
                releasedSizeBytes,
                null);
        }

        if (reservedSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedSizeBytes),
                reservedSizeBytes,
                null);
        }

        lock (SyncRoot)
        {
            if (releasedSizeBytes > currentSizeBytes)
            {
                throw new InvalidOperationException(
                    "The process-wide derived-index cache size accounting is inconsistent.");
            }

            var retainedSizeBytes = currentSizeBytes - releasedSizeBytes;
            if (reservedSizeBytes > MaximumEstimatedSizeBytes
                || retainedSizeBytes > MaximumEstimatedSizeBytes - reservedSizeBytes)
            {
                return false;
            }

            currentSizeBytes = checked(retainedSizeBytes + reservedSizeBytes);
            return true;
        }
    }

    internal static void Release(long sizeBytes)
    {
        if (!TryRelease(sizeBytes))
        {
            throw new InvalidOperationException(
                "The process-wide derived-index cache size accounting is inconsistent.");
        }
    }

    internal static bool TryRelease(long sizeBytes)
    {
        if (sizeBytes < 0)
        {
            return false;
        }

        lock (SyncRoot)
        {
            if (sizeBytes > currentSizeBytes)
            {
                return false;
            }

            currentSizeBytes -= sizeBytes;
            return true;
        }
    }
}

internal sealed class ProcessWideDerivedIndexCacheBudgetLease
{
    private long reservedSizeBytes;

    ~ProcessWideDerivedIndexCacheBudgetLease()
    {
        if (reservedSizeBytes > 0)
        {
            ProcessWideDerivedIndexCacheBudget.TryRelease(reservedSizeBytes);
        }
    }

    internal bool TryResize(long releasedSizeBytes, long reservedSizeBytes)
    {
        if (releasedSizeBytes < 0 || releasedSizeBytes > this.reservedSizeBytes)
        {
            throw new InvalidOperationException(
                "The derived-index cache budget lease accounting is inconsistent.");
        }

        if (!ProcessWideDerivedIndexCacheBudget.TryResize(
                releasedSizeBytes,
                reservedSizeBytes))
        {
            return false;
        }

        this.reservedSizeBytes = checked(
            this.reservedSizeBytes - releasedSizeBytes + reservedSizeBytes);
        return true;
    }

    internal void Release(long sizeBytes)
    {
        if (sizeBytes < 0 || sizeBytes > reservedSizeBytes)
        {
            throw new InvalidOperationException(
                "The derived-index cache budget lease accounting is inconsistent.");
        }

        reservedSizeBytes -= sizeBytes;
        ProcessWideDerivedIndexCacheBudget.Release(sizeBytes);
    }
}

public sealed record BoundedDerivedIndexCacheOptions
{
    public int MaximumEntryCount { get; init; } = 64;

    public long MaximumSizeBytes { get; init; } = 64L * 1024L * 1024L;

    internal void Validate()
    {
        if (MaximumEntryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumEntryCount),
                MaximumEntryCount,
                "The maximum entry count must be positive.");
        }

        if (MaximumSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumSizeBytes),
                MaximumSizeBytes,
                "The maximum cache size must be positive.");
        }
    }
}

public sealed record DerivedIndexCacheItem<TValue>(TValue Value, long SizeBytes)
{
    public DerivedIndexCacheItem<TValue> Validate()
    {
        ArgumentNullException.ThrowIfNull(Value);

        if (SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SizeBytes),
                SizeBytes,
                "A derived index cannot have a negative size.");
        }

        return this;
    }
}

public sealed record DerivedIndexCacheStatistics(
    int EntryCount,
    long SizeBytes,
    long HitCount,
    long MissCount,
    long EvictionCount);

/// <summary>
/// A thread-safe, size-bounded least-recently-used cache for immutable derived indexes.
/// </summary>
/// <remarks>
/// Every instance also reserves its estimated entry bytes against one process-wide hard ceiling.
/// Factories run outside the cache lock and may run more than once for the same key. This keeps
/// unrelated cache reads responsive and leaves cancellation ownership with each caller.
/// A result whose factory overlaps an explicit removal, invalidation, or clear is returned to its
/// caller but is not published back into the cache.
/// </remarks>
public sealed class BoundedDerivedIndexCache<TValue>
{
    private readonly object syncRoot = new();
    private readonly Dictionary<DerivedIndexCacheKey, CacheEntry> entries = new();
    private readonly LinkedList<DerivedIndexCacheKey> usageOrder = new();
    private readonly ProcessWideDerivedIndexCacheBudgetLease processWideBudgetLease = new();
    private readonly int maximumEntryCount;
    private readonly long maximumSizeBytes;
    private long currentSizeBytes;
    private long hitCount;
    private long missCount;
    private long evictionCount;
    private ulong invalidationEpoch;

    public BoundedDerivedIndexCache(BoundedDerivedIndexCacheOptions? options = null)
    {
        options ??= new BoundedDerivedIndexCacheOptions();
        options.Validate();

        maximumEntryCount = options.MaximumEntryCount;
        maximumSizeBytes = options.MaximumSizeBytes;
    }

    public int Count
    {
        get
        {
            lock (syncRoot)
            {
                return entries.Count;
            }
        }
    }

    public long CurrentSizeBytes
    {
        get
        {
            lock (syncRoot)
            {
                return currentSizeBytes;
            }
        }
    }

    public bool TryGet(DerivedIndexCacheKey key, out TValue value)
    {
        ValidateKey(key);

        lock (syncRoot)
        {
            return TryGetCore(key, out value);
        }
    }

    /// <summary>
    /// Adds or replaces an entry. Returns false when the entry exceeds a cache budget.
    /// </summary>
    public bool Set(DerivedIndexCacheKey key, TValue value, long sizeBytes)
    {
        return Set(key, new DerivedIndexCacheItem<TValue>(value, sizeBytes));
    }

    /// <summary>
    /// Adds or replaces an entry. Returns false when the entry exceeds a cache budget.
    /// </summary>
    public bool Set(DerivedIndexCacheKey key, DerivedIndexCacheItem<TValue> item)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(item);
        item.Validate();

        lock (syncRoot)
        {
            return SetCore(key, item);
        }
    }

    public async ValueTask<TValue> GetOrCreateAsync(
        DerivedIndexCacheKey key,
        Func<CancellationToken, ValueTask<DerivedIndexCacheItem<TValue>>> factory,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(factory);

        ulong factoryEpoch;
        lock (syncRoot)
        {
            if (TryGetCore(key, out var cachedValue))
            {
                return cachedValue;
            }

            factoryEpoch = invalidationEpoch;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var created = await factory(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(created);
        created.Validate();
        lock (syncRoot)
        {
            if (factoryEpoch == invalidationEpoch)
            {
                SetCore(key, created);
            }
        }

        return created.Value;
    }

    public bool Remove(DerivedIndexCacheKey key)
    {
        ValidateKey(key);

        lock (syncRoot)
        {
            AdvanceInvalidationEpoch();
            return RemoveCore(key);
        }
    }

    public int InvalidateRevision(ProjectSourceRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return RemoveWhere(key => key.Revision == revision);
    }

    public int InvalidateProject(ProjectId projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId.Value))
        {
            throw new ArgumentException("A project id cannot be empty.", nameof(projectId));
        }

        return RemoveWhere(key => key.Revision.ProjectId == projectId);
    }

    public int InvalidateCallerKey(string callerKey)
    {
        var normalized = DerivedIndexCacheKey.NormalizeCallerKey(callerKey);

        return RemoveWhere(key => string.Equals(key.CallerKey, normalized, StringComparison.Ordinal));
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            AdvanceInvalidationEpoch();
            entries.Clear();
            usageOrder.Clear();
            processWideBudgetLease.Release(currentSizeBytes);
            currentSizeBytes = 0;
        }
    }

    public DerivedIndexCacheStatistics GetStatistics()
    {
        lock (syncRoot)
        {
            return new DerivedIndexCacheStatistics(
                entries.Count,
                currentSizeBytes,
                hitCount,
                missCount,
                evictionCount);
        }
    }

    private static void ValidateKey(DerivedIndexCacheKey key)
    {
        if (key.Revision is null || string.IsNullOrWhiteSpace(key.CallerKey))
        {
            throw new ArgumentException("A derived-index cache key must be initialized.", nameof(key));
        }
    }

    private int RemoveWhere(Func<DerivedIndexCacheKey, bool> predicate)
    {
        lock (syncRoot)
        {
            AdvanceInvalidationEpoch();
            var keys = entries.Keys.Where(predicate).ToArray();
            foreach (var key in keys)
            {
                RemoveCore(key);
            }

            return keys.Length;
        }
    }

    private bool TryGetCore(DerivedIndexCacheKey key, out TValue value)
    {
        if (!entries.TryGetValue(key, out var entry))
        {
            missCount++;
            value = default!;
            return false;
        }

        usageOrder.Remove(entry.UsageNode);
        usageOrder.AddFirst(entry.UsageNode);
        hitCount++;
        value = entry.Value;
        return true;
    }

    private bool SetCore(DerivedIndexCacheKey key, DerivedIndexCacheItem<TValue> item)
    {
        if (item.SizeBytes > maximumSizeBytes)
        {
            RemoveCore(key);
            return false;
        }

        var replacesExisting = entries.TryGetValue(key, out var replacedEntry);
        var releasedSizeBytes = replacedEntry?.SizeBytes ?? 0;
        var remainingEntryCount = entries.Count - (replacesExisting ? 1 : 0);
        var remainingSizeBytes = currentSizeBytes - releasedSizeBytes;
        List<DerivedIndexCacheKey>? evictionKeys = null;
        var candidate = usageOrder.Last;
        while (remainingEntryCount >= maximumEntryCount
               || remainingSizeBytes > maximumSizeBytes - item.SizeBytes)
        {
            while (candidate is not null && candidate.Value.Equals(key))
            {
                candidate = candidate.Previous;
            }

            if (candidate is null || !entries.TryGetValue(candidate.Value, out var candidateEntry))
            {
                throw new InvalidOperationException(
                    "The derived-index cache size accounting is inconsistent.");
            }

            evictionKeys ??= [];
            evictionKeys.Add(candidate.Value);
            releasedSizeBytes = checked(releasedSizeBytes + candidateEntry.SizeBytes);
            remainingSizeBytes -= candidateEntry.SizeBytes;
            remainingEntryCount--;
            candidate = candidate.Previous;
        }

        if (!processWideBudgetLease.TryResize(releasedSizeBytes, item.SizeBytes))
        {
            return false;
        }

        if (replacesExisting && !RemoveCore(key, releaseProcessWideBudget: false))
        {
            throw new InvalidOperationException(
                "The derived-index cache replacement accounting is inconsistent.");
        }

        if (evictionKeys is not null)
        {
            foreach (var evictionKey in evictionKeys)
            {
                if (!RemoveCore(evictionKey, releaseProcessWideBudget: false))
                {
                    throw new InvalidOperationException(
                        "The derived-index cache eviction accounting is inconsistent.");
                }

                evictionCount++;
            }
        }

        LinkedListNode<DerivedIndexCacheKey>? usageNode = null;
        try
        {
            usageNode = usageOrder.AddFirst(key);
            entries.Add(key, new CacheEntry(item.Value, item.SizeBytes, usageNode));
            currentSizeBytes += item.SizeBytes;
            return true;
        }
        catch
        {
            entries.Remove(key);
            if (usageNode?.List is not null)
            {
                usageOrder.Remove(usageNode);
            }

            processWideBudgetLease.Release(item.SizeBytes);
            throw;
        }
    }

    private void AdvanceInvalidationEpoch()
    {
        invalidationEpoch = unchecked(invalidationEpoch + 1);
    }

    private bool RemoveCore(
        DerivedIndexCacheKey key,
        bool releaseProcessWideBudget = true)
    {
        if (!entries.Remove(key, out var entry))
        {
            return false;
        }

        usageOrder.Remove(entry.UsageNode);
        currentSizeBytes -= entry.SizeBytes;
        if (releaseProcessWideBudget)
        {
            processWideBudgetLease.Release(entry.SizeBytes);
        }

        return true;
    }

    private sealed record CacheEntry(
        TValue Value,
        long SizeBytes,
        LinkedListNode<DerivedIndexCacheKey> UsageNode);
}
