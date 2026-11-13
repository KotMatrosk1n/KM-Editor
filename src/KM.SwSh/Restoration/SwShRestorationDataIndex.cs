// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using System.IO.Compression;
using KM.Core.Projects;

namespace KM.SwSh.Restoration;

public static partial class SwShRestorationStateStore
{
    private const string DataIndexKind = "pokemon-restoration-data";
    private const int MaximumDataIndexBytes = 128 * 1024 * 1024;
    private const int MaximumEncodedDataIndexBytes = 64 * 1024 * 1024;
    internal static readonly IReadOnlySet<string> DataPaths = new HashSet<string>(StringComparer.Ordinal)
        { SwShRestorationComposer.PersonalPath, SwShRestorationComposer.LearnsetPath };
    private sealed record DataEntry(bool IsBundle, byte[] Prepared);
    private sealed record DataIndex(string Kind, int Schema, IReadOnlyDictionary<string, DataEntry> Entries);

    // The committed state pins this independent data artifact. Reading defaults
    // never uses an unverified side cache or needs the retained model package.
    private static SwShRestorationState IndexData(ProjectPaths paths, SwShRestorationState state, CancellationToken token)
    {
        var scope = SwShRestorationReadScope.Find(paths)!;
        scope.BindDataIndex(state.DataHash);
        var selections = state.Selections.Where(s => s.RemovalId is null).ToArray();
        var hashes = selections.SelectMany(s => s.CustomizationHash is { } custom
            ? new[] { s.PreparedHash, custom } : [s.PreparedHash]).Distinct(StringComparer.Ordinal).ToArray();
        if (hashes.Length == 0) return state with { DataHash = null };
        foreach (var batch in hashes.Chunk(468)) scope.PrefetchContributions(batch, DataPaths);
        var entries = new Dictionary<string, DataEntry>(StringComparer.Ordinal);
        foreach (var hash in hashes)
        {
            token.ThrowIfCancellationRequested();
            var prepared = scope.ReadPreparedContributions(hash, DataPaths);
            if (!selections.Any(s => s.DefinitionId == prepared.DefinitionId && (s.PreparedHash == hash || s.CustomizationHash == hash)))
                throw new InvalidDataException("Restoration data does not match its selection.");
            entries.Add(hash, new(scope.IsPreparedBundle(hash), prepared.Encode()));
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new DataIndex(DataIndexKind, 1, entries));
        if (bytes.Length > MaximumDataIndexBytes) throw new InvalidDataException("Restoration data index exceeds its size limit.");
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("data.json", CompressionLevel.Fastest);
            entry.LastWriteTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var destination = entry.Open();
            destination.Write(bytes);
        }
        if (output.Length > MaximumEncodedDataIndexBytes) throw new InvalidDataException("Restoration data index exceeds its encoded size limit.");
        var stored = StoreBytes(paths, output.ToArray(), token);
        using (stored.Lease) { }
        scope.BindDataIndex(stored.Hash);
        return state with { DataHash = stored.Hash };
    }

    internal static IReadOnlyDictionary<string, (bool IsBundle, SwShPreparedRestoration Prepared)> DecodeDataIndex(byte[] bytes)
    {
        if (bytes.Length > MaximumEncodedDataIndexBytes) throw new InvalidDataException("Restoration data index exceeds its encoded size limit.");
        using var input = new MemoryStream(bytes, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count != 1 || zip.Entries[0].FullName != "data.json" || zip.Entries[0].Length > MaximumDataIndexBytes)
            throw new InvalidDataException("Restoration data index exceeds its size limit.");
        using var metadata = zip.Entries[0].Open();
        var index = JsonSerializer.Deserialize<DataIndex>(metadata) ?? throw new InvalidDataException("Restoration data index is empty.");
        if (index.Kind != DataIndexKind || index.Schema != 1 || index.Entries is null
            || index.Entries.Count > SwShRestorationDefinitions.All.Count * 2)
            throw new InvalidDataException("Restoration data index is invalid.");
        return index.Entries.ToDictionary(pair =>
        {
            ValidateHash(pair.Key);
            return pair.Key;
        }, pair =>
        {
            if (pair.Value is null || pair.Value.Prepared is null) throw new InvalidDataException("Restoration data entry is empty.");
            using var payload = new MemoryStream(pair.Value.Prepared, writable: false);
            using var bundle = new ZipArchive(payload, ZipArchiveMode.Read);
            if (bundle.Entries.Count > 4097 || bundle.Entries.Sum(e => e.Length) > 2 * 1024 * 1024)
                throw new InvalidDataException("Restoration data entry exceeds its size limit.");
            var prepared = SwShPreparedRestoration.Decode(pair.Value.Prepared);
            if (prepared.Contributions.Any(c => !DataPaths.Contains(c.Path)))
                throw new InvalidDataException("Restoration data index contains assets outside its data tables.");
            return (pair.Value.IsBundle, prepared);
        }, StringComparer.Ordinal);
    }

}
