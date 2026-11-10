// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

public sealed partial class SwShGfPackFile
{
    public static SwShGfPackFile CreateWithPaths(IReadOnlyDictionary<string, byte[]> members) =>
        new SwShGfPackFile(0x1000, 0, [], [], []).WithMembers(members);

    /// <summary>Repaths a complete named archive without changing its member data or order.</summary>
    public SwShGfPackFile WithRenamedMembers(IReadOnlyDictionary<string, string> paths)
    {
        var byHash = paths.ToDictionary(pair => HashMemberPath(pair.Key));
        if (byHash.Count != FileCount || AbsoluteHashes.Any(entry => !byHash.ContainsKey(entry.HashFnv1aPathFull)))
            throw new InvalidDataException("Archive member paths do not match the archive.");
        var names = AbsoluteHashes.Select(entry => byHash[entry.HashFnv1aPathFull]).ToArray();
        var hashes = names.Select(pair => new FileHashAbsoluteEntry(HashMemberPath(pair.Value))).ToList();
        if (hashes.Distinct().Count() != FileCount)
            throw new InvalidDataException("Renamed archive member paths overlap.");

        static ulong FolderHash(string path) => HashFnv1a64(path.AsSpan(0, path.LastIndexOf('/') + 1));
        static ulong NameHash(string path) => HashFnv1a64(path.AsSpan(path.LastIndexOf('/') + 1));
        var nextFolders = new List<FolderEntry>(folders.Count);
        foreach (var folder in folders)
        {
            var files = new List<FileHashIndexEntry>(folder.Files.Count);
            var newFolderHashes = new HashSet<ulong>();
            foreach (var file in folder.Files)
            {
                var (before, after) = names[file.Index];
                if (FolderHash(before) != folder.HashFnv1aPathFolderName || NameHash(before) != file.HashFnv1aPathFileName)
                    throw new InvalidDataException("Archive member folder or filename does not match.");
                files.Add(file with { HashFnv1aPathFileName = NameHash(after) });
                newFolderHashes.Add(FolderHash(after));
            }
            if (newFolderHashes.Count > 1 || files.Select(file => file.HashFnv1aPathFileName).Distinct().Count() != files.Count)
                throw new InvalidDataException("Renamed archive members must retain their folder grouping.");
            nextFolders.Add(folder with { HashFnv1aPathFolderName = newFolderHashes.SingleOrDefault(folder.HashFnv1aPathFolderName), Files = files });
        }
        if (nextFolders.Select(folder => folder.HashFnv1aPathFolderName).Distinct().Count() != nextFolders.Count)
            throw new InvalidDataException("Renamed archive folders overlap.");
        return new(Version, IsRelocated, hashes, nextFolders, new(entries));
    }

    /// <summary>Changes named members while preserving the metadata and compressed bytes of other members.</summary>
    public SwShGfPackFile WithMembers(IReadOnlyDictionary<string, byte[]> replacements,
        IReadOnlyCollection<string>? removedPaths = null)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        removedPaths ??= [];
        if (replacements.Count + removedPaths.Count > 16384) throw new InvalidDataException("Too many archive member changes.");
        foreach (var path in replacements.Keys.Concat(removedPaths)) ValidateMemberPath(path);
        var removeHashes = removedPaths.Select(path => HashFnv1a64(path)).ToHashSet();
        if (removeHashes.Count != removedPaths.Count || replacements.Keys.Any(path => removeHashes.Contains(HashFnv1a64(path))))
            throw new InvalidDataException("Archive member changes overlap.");
        foreach (var hash in removeHashes) _ = FindHashIndex(hash);
        var oldToNew = new Dictionary<int, int>();
        var nextHashes = new List<FileHashAbsoluteEntry>();
        var nextEntries = new List<FileEntry>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (removeHashes.Contains(AbsoluteHashes[i].HashFnv1aPathFull)) continue;
            oldToNew.Add(i, nextEntries.Count); nextHashes.Add(AbsoluteHashes[i]); nextEntries.Add(entries[i]);
        }
        var nextFolders = folders.Select(folder => folder with
        {
            Files = folder.Files.Where(file => oldToNew.ContainsKey(file.Index))
                .Select(file => file with { Index = oldToNew[file.Index] }).ToList(),
        }).ToList();
        foreach (var (path, bytes) in replacements.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Archive member size is unsupported.");
            var hash = HashFnv1a64(path);
            var matches = nextHashes.Select((item, index) => (item, index)).Where(pair => pair.item.HashFnv1aPathFull == hash).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("Archive member identity is ambiguous.");
            if (matches.Length == 1)
            {
                var index = matches[0].index; var original = nextEntries[index];
                var unchanged = bytes.AsSpan().SequenceEqual(GetOriginalFile(original));
                EnsureCanCompress(original.CompressionType);
                nextEntries[index] = original with { DecompressedData = bytes.ToArray(), Modified = !unchanged };
                continue;
            }
            var separator = path.LastIndexOf('/');
            var folderHash = HashFnv1a64(separator < 0 ? "" : path[..(separator + 1)]);
            var nameHash = HashFnv1a64(path.AsSpan(separator + 1));
            var matchingFolders = nextFolders.Where(folder => folder.HashFnv1aPathFolderName == folderHash).ToArray();
            if (matchingFolders.Length > 1) throw new InvalidDataException("Archive folder identity is ambiguous.");
            var folder = matchingFolders.SingleOrDefault();
            if (folder is null) { folder = new FolderEntry(folderHash, []); nextFolders.Add(folder); }
            if (folder.Files.Any(file => file.HashFnv1aPathFileName == nameHash)) throw new InvalidDataException("Archive basename hash collides.");
            folder.Files.Add(new(nameHash, nextEntries.Count, folder.Files.FirstOrDefault()?.Padding ?? 0xCCCCCCCC));
            nextHashes.Add(new(hash)); nextEntries.Add(FileEntry.Create(bytes, SwShGfPackCompressionType.Lz4));
        }
        return new(Version, IsRelocated, nextHashes, nextFolders, nextEntries);
    }

    public static ulong HashMemberPath(string path) { ValidateMemberPath(path); return HashFnv1a64(path); }
    private static void ValidateMemberPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Contains('\\') || path.Contains(':')
            || path.Any(char.IsControl) || path.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException("Archive member path is invalid.");
    }
}
