// SPDX-License-Identifier: GPL-3.0-only

using Xunit;

namespace KM.Formats.Tests;

public sealed class ByteBudgetLruCacheTests
{
    [Fact]
    public void SetEvictsLeastRecentlyUsedValuesUntilWithinBudget()
    {
        var cache = new ByteBudgetLruCache<string, string>(maxRetainedBytes: 10);
        cache.Set("first", "one", retainedBytes: 4);
        cache.Set("second", "two", retainedBytes: 4);

        Assert.True(cache.TryGetValue("first", out _));

        cache.Set("third", "three", retainedBytes: 4);

        Assert.Equal(2, cache.Count);
        Assert.Equal(8, cache.RetainedBytes);
        Assert.True(cache.TryGetValue("first", out var first));
        Assert.Equal("one", first);
        Assert.False(cache.TryGetValue("second", out _));
        Assert.True(cache.TryGetValue("third", out var third));
        Assert.Equal("three", third);
    }

    [Fact]
    public void SetDoesNotRetainAValueLargerThanTheBudget()
    {
        var cache = new ByteBudgetLruCache<int, byte[]>(maxRetainedBytes: 4);
        cache.Set(1, [0x01, 0x02], retainedBytes: 2);

        cache.Set(2, new byte[8], retainedBytes: 8);

        Assert.Equal(1, cache.Count);
        Assert.Equal(2, cache.RetainedBytes);
        Assert.True(cache.TryGetValue(1, out _));
        Assert.False(cache.TryGetValue(2, out _));
    }

    [Fact]
    public void ClearReleasesAllRetainedValues()
    {
        var cache = new ByteBudgetLruCache<int, byte[]>(maxRetainedBytes: 16);
        cache.Set(1, new byte[8], retainedBytes: 8);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.RetainedBytes);
        Assert.False(cache.TryGetValue(1, out _));
    }
}
