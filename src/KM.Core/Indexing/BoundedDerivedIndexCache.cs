// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Indexing;

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
    /// Adds or replaces an entry. Returns false when the entry itself exceeds the cache budget.
    /// </summary>
    public bool Set(DerivedIndexCacheKey key, TValue value, long sizeBytes)
    {
        return Set(key, new DerivedIndexCacheItem<TValue>(value, sizeBytes));
    }

    /// <summary>
    /// Adds or replaces an entry. Returns false when the entry itself exceeds the cache budget.
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
        RemoveCore(key);

        if (item.SizeBytes > maximumSizeBytes)
        {
            return false;
        }

        while (entries.Count >= maximumEntryCount
               || currentSizeBytes > maximumSizeBytes - item.SizeBytes)
        {
            EvictLeastRecentlyUsed();
        }

        var usageNode = usageOrder.AddFirst(key);
        entries.Add(key, new CacheEntry(item.Value, item.SizeBytes, usageNode));
        currentSizeBytes += item.SizeBytes;
        return true;
    }

    private void AdvanceInvalidationEpoch()
    {
        invalidationEpoch = unchecked(invalidationEpoch + 1);
    }

    private bool RemoveCore(DerivedIndexCacheKey key)
    {
        if (!entries.Remove(key, out var entry))
        {
            return false;
        }

        usageOrder.Remove(entry.UsageNode);
        currentSizeBytes -= entry.SizeBytes;
        return true;
    }

    private void EvictLeastRecentlyUsed()
    {
        var leastRecentlyUsed = usageOrder.Last;
        if (leastRecentlyUsed is null)
        {
            throw new InvalidOperationException("The derived-index cache size accounting is inconsistent.");
        }

        RemoveCore(leastRecentlyUsed.Value);
        evictionCount++;
    }

    private sealed record CacheEntry(
        TValue Value,
        long SizeBytes,
        LinkedListNode<DerivedIndexCacheKey> UsageNode);
}
