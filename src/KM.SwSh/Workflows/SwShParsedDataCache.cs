// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.Workflows;

public sealed class SwShParsedDataCache
{
    private readonly object syncRoot = new();
    private readonly Dictionary<SwShParsedDataCacheKey, SwShParsedDataCacheEntry> entries = new(SwShParsedDataCacheKey.Comparer);
    private long hitCount;
    private long missCount;

    public SwShParsedDataCacheResult<TValue> GetOrAdd<TValue>(
        string filePath,
        Func<string, TValue> loader)
        where TValue : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(loader);

        var identity = SwShParsedFileIdentity.Create(filePath);
        var key = new SwShParsedDataCacheKey(identity.FullPath, typeof(TValue));

        lock (syncRoot)
        {
            if (entries.TryGetValue(key, out var entry)
                && entry.Identity.Length == identity.Length
                && entry.Identity.LastWriteTimeUtc == identity.LastWriteTimeUtc
                && entry.Value is TValue cachedValue)
            {
                hitCount++;
                return new SwShParsedDataCacheResult<TValue>(cachedValue, identity, WasCacheHit: true);
            }

            missCount++;
        }

        var value = loader(identity.FullPath);

        lock (syncRoot)
        {
            entries[key] = new SwShParsedDataCacheEntry(identity, value);
        }

        return new SwShParsedDataCacheResult<TValue>(value, identity, WasCacheHit: false);
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            entries.Clear();
            hitCount = 0;
            missCount = 0;
        }
    }

    public SwShParsedDataCacheSnapshot Snapshot()
    {
        lock (syncRoot)
        {
            return new SwShParsedDataCacheSnapshot(entries.Count, hitCount, missCount);
        }
    }

    private sealed record SwShParsedDataCacheEntry(
        SwShParsedFileIdentity Identity,
        object Value);

    private sealed record SwShParsedDataCacheKey(string FullPath, Type ValueType)
    {
        public static IEqualityComparer<SwShParsedDataCacheKey> Comparer { get; } = new KeyComparer();

        private sealed class KeyComparer : IEqualityComparer<SwShParsedDataCacheKey>
        {
            public bool Equals(SwShParsedDataCacheKey? x, SwShParsedDataCacheKey? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                return string.Equals(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase)
                    && x.ValueType == y.ValueType;
            }

            public int GetHashCode(SwShParsedDataCacheKey obj)
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FullPath),
                    obj.ValueType);
            }
        }
    }
}

public sealed record SwShParsedDataCacheResult<TValue>(
    TValue Value,
    SwShParsedFileIdentity Identity,
    bool WasCacheHit)
    where TValue : class;

public sealed record SwShParsedDataCacheSnapshot(
    int EntryCount,
    long HitCount,
    long MissCount);

public sealed record SwShParsedFileIdentity(
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc)
{
    public static SwShParsedFileIdentity Create(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The source file could not be found.", fileInfo.FullName);
        }

        return new SwShParsedFileIdentity(
            fileInfo.FullName,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
    }
}
