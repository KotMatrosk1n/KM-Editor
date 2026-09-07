// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;

namespace KM.Formats.Models;

/// <summary>Bounded, content-addressed storage for expensive derived model data.</summary>
public static class ModelDerivedCache<T> where T : class
{
    private sealed record Entry(string Key, T Value, long Size);
    private static readonly object Gate = new();
    private static readonly LinkedList<Entry> Entries = new();
    private static long size;
    public static T Get(ReadOnlySpan<byte> source, string parameters, Func<T> create, Func<T, long> measure)
    {
        var key = Convert.ToHexString(SHA256.HashData(source)) + parameters;
        lock (Gate)
        {
            var entry = Entries.FirstOrDefault(e => e.Key == key);
            if (entry is not null) { Entries.Remove(entry); Entries.AddLast(entry); return entry.Value; }
        }
        var value = create(); var bytes = measure(value) + key.Length * 2L;
        const long limit = 64L * 1024 * 1024;
        if (bytes > limit) return value;
        lock (Gate)
        {
            var old = Entries.FirstOrDefault(e => e.Key == key);
            if (old is not null) { Entries.Remove(old); size -= old.Size; }
            while ((size + bytes > limit || Entries.Count >= 128) && Entries.First is { } first)
            { size -= first.Value.Size; Entries.RemoveFirst(); }
            Entries.AddLast(new Entry(key, value, bytes)); size += bytes;
        }
        return value;
    }
}
