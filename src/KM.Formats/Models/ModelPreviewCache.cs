// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text.Json;

namespace KM.Formats.Models;

/// <summary>Reuses derived previews only after checking every dependency against fresh source bytes.</summary>
public static class ModelPreviewCache
{
    private sealed record Entry(string Key, Dictionary<string, string?> Sources, PreviewScene Scene, long Size);
    private static readonly object Gate = new();
    private static readonly LinkedList<Entry> Entries = new();
    private const long Limit = 192L * 1024 * 1024;
    private static long size;

    public static PreviewScene Load(string family, string id, PreviewMaterialVariant? variant,
        Func<string, byte[]> read, Func<Func<string, byte[]>, PreviewScene> build)
    {
        var key = family + ":" + id + ":" + JsonSerializer.Serialize(variant);
        Entry? candidate;
        lock (Gate) candidate = Entries.FirstOrDefault(e => e.Key == key);
        var sources = new Dictionary<string, string?>(StringComparer.Ordinal);
        var loaded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[] Read(string path)
        {
            if (loaded.TryGetValue(path, out var value)) return value;
            try
            {
                value = read(path);
                sources[path] = Convert.ToHexString(SHA256.HashData(value));
                loaded[path] = value;
                return value;
            }
            catch (FileNotFoundException) { sources[path] = null; throw; }
            catch (DirectoryNotFoundException) { sources[path] = null; throw; }
        }
        if (candidate is not null)
        {
            var unchanged = true;
            foreach (var dependency in candidate.Sources)
            {
                try { _ = Read(dependency.Key); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (sources[dependency.Key] != dependency.Value) unchanged = false;
            }
            if (unchanged)
            {
                lock (Gate)
                {
                    if (Entries.Remove(candidate)) Entries.AddLast(candidate);
                }
                return candidate.Scene;
            }
        }
        var scene = build(Read);
        var bytes = scene.Primitives.Sum(p => (long)p.Vertices.Length * 4 + (long)p.Indices.Length * 4)
            + scene.Textures.Sum(t => t.ByteLength) + JsonSerializer.SerializeToUtf8Bytes(scene.Rig).LongLength
            + scene.Primitives.Count * 8192L + sources.Count * 1024L;
        lock (Gate)
        {
            var old = Entries.FirstOrDefault(e => e.Key == key);
            if (old is not null) { Entries.Remove(old); size -= old.Size; }
            if (bytes <= Limit)
            {
                while ((size + bytes > Limit || Entries.Count >= 32) && Entries.First is { } first)
                { size -= first.Value.Size; Entries.RemoveFirst(); }
                Entries.AddLast(new Entry(key, sources, scene, bytes)); size += bytes;
            }
        }
        return scene;
    }
}
