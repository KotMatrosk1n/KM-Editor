// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats;

/// <summary>
/// Retains recently used values without allowing large binary buffers to grow without bound.
/// Values larger than the configured budget are returned by their caller but are not retained.
/// </summary>
internal sealed class ByteBudgetLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly long maxRetainedBytes;
    private readonly Dictionary<TKey, CacheEntry> entries = [];
    private readonly LinkedList<TKey> recency = [];

    public ByteBudgetLruCache(long maxRetainedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRetainedBytes);
        this.maxRetainedBytes = maxRetainedBytes;
    }

    public int Count => entries.Count;

    public long RetainedBytes { get; private set; }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!entries.TryGetValue(key, out var entry))
        {
            value = default!;
            return false;
        }

        recency.Remove(entry.RecencyNode);
        recency.AddLast(entry.RecencyNode);
        value = entry.Value;
        return true;
    }

    public void Set(TKey key, TValue value, long retainedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retainedBytes);

        if (entries.TryGetValue(key, out var existing))
        {
            Remove(key, existing);
        }

        if (retainedBytes > maxRetainedBytes)
        {
            return;
        }

        while (RetainedBytes > maxRetainedBytes - retainedBytes && recency.First is { } oldestNode)
        {
            Remove(oldestNode.Value, entries[oldestNode.Value]);
        }

        var recencyNode = recency.AddLast(key);
        entries.Add(key, new CacheEntry(value, retainedBytes, recencyNode));
        RetainedBytes += retainedBytes;
    }

    public void Clear()
    {
        entries.Clear();
        recency.Clear();
        RetainedBytes = 0;
    }

    private void Remove(TKey key, CacheEntry entry)
    {
        entries.Remove(key);
        recency.Remove(entry.RecencyNode);
        RetainedBytes -= entry.RetainedBytes;
    }

    private sealed record CacheEntry(
        TValue Value,
        long RetainedBytes,
        LinkedListNode<TKey> RecencyNode);
}
