// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.SV.Workflows;

namespace KM.SV.GameModules;

public sealed class SvPackedLooseSourceComparisonService
{
    private const int MaximumVirtualIdentities = 500;
    private const int MaximumLooseFileSystemEntries = 5_000;
    private const int MaximumLooseDirectories = 2_000;
    private const int MaximumTraversalDepth = 64;
    // Leave room for the public "romfs/" prefix while keeping every identity
    // within the generic game-module text bound.
    private const int MaximumVirtualPathBytes = 480;
    private const int MaximumArchiveIndexBytes = 64 * 1024 * 1024;
    private const long MaximumArchivePackBytes = 128L * 1024L * 1024L;
    private const int MaximumCandidateBytes = 64 * 1024 * 1024;
    private const long MaximumObservationBytes = 512L * 1024L * 1024L;

    private static readonly IReadOnlyList<SvPackedLooseSourceKind> CandidateOrder =
    [
        SvPackedLooseSourceKind.BaseArchive,
        SvPackedLooseSourceKind.BaseLoose,
        SvPackedLooseSourceKind.StandaloneLooseOutput,
        SvPackedLooseSourceKind.ManagerLooseOutput,
        SvPackedLooseSourceKind.OutputArchive,
    ];

    public SvPackedLooseSourceComparison LoadFreshBounded(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame))
        {
            throw new InvalidDataException(
                "Packed and loose source comparison requires a Scarlet or Violet project.");
        }

        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidDataException(
                "Packed and loose source comparison requires a configured base RomFS.");
        }

        try
        {
            lock (SvWorkflowFileSource.OutputWriteSyncRoot)
            {
                var initial = CaptureObservation(paths);
                var final = CaptureObservation(paths);
                if (!ObservationsMatch(initial, final))
                {
                    throw new SvPackedLooseSourceObservationChangedException();
                }

                return Project(initial);
            }
        }
        catch (SecurityException exception)
        {
            throw new IOException(
                "A Scarlet/Violet source candidate could not be inspected safely.",
                exception);
        }
    }

    private static SourceObservation CaptureObservation(ProjectPaths paths)
    {
        var baseRoot = NormalizeRoot(paths.BaseRomFsPath!, "base RomFS");
        var outputRoot = string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? null
            : NormalizeRoot(paths.OutputRootPath, "output");

        var managerLoosePaths = outputRoot is null
            ? []
            : EnumerateLooseRoot(outputRoot, excludedTopLevelNames: [".km", "romfs"]);
        var standaloneRoot = outputRoot is null
            ? null
            : Path.Combine(outputRoot, "romfs");
        var standaloneLoosePaths = standaloneRoot is null || !Directory.Exists(standaloneRoot)
            ? []
            : EnumerateLooseRoot(standaloneRoot, excludedTopLevelNames: []);

        var virtualPathByHash = new Dictionary<ulong, string>();
        var interestingHashes = new HashSet<ulong>();
        foreach (var knownPath in ScarletVioletKnownRomFsFiles.Paths
                     .Append(SvWorkflowFileSource.DescriptorVirtualPath))
        {
            interestingHashes.Add(AddVirtualPath(
                virtualPathByHash,
                NormalizeVirtualPath(knownPath)));
        }

        AddLoosePaths(managerLoosePaths, virtualPathByHash, interestingHashes);
        AddLoosePaths(standaloneLoosePaths, virtualPathByHash, interestingHashes);

        using var baseArchive = OpenArchive(baseRoot, paths.ScarletVioletSupportFolderPath);
        using var outputArchive = outputRoot is null
            ? null
            : OpenArchive(outputRoot, paths.ScarletVioletSupportFolderPath);
        if (outputArchive is not null)
        {
            interestingHashes.UnionWith(outputArchive.FileHashes);
        }

        if (interestingHashes.Count > MaximumVirtualIdentities)
        {
            throw new InvalidDataException(
                "Packed and loose source comparison exceeds its bounded virtual-identity limit.");
        }

        var byteBudget = new ObservationByteBudget();
        var entries = new List<ObservedEntry>(interestingHashes.Count);
        foreach (var fileHash in interestingHashes.Order())
        {
            virtualPathByHash.TryGetValue(fileHash, out var virtualPath);
            var candidates = new Dictionary<SvPackedLooseSourceKind, ContentFingerprint?>
            {
                [SvPackedLooseSourceKind.BaseArchive] =
                    ReadArchiveCandidate(baseArchive, fileHash, byteBudget),
                [SvPackedLooseSourceKind.BaseLoose] = virtualPath is null
                    ? null
                    : ReadLooseCandidate(baseRoot, virtualPath, byteBudget),
                [SvPackedLooseSourceKind.StandaloneLooseOutput] =
                    virtualPath is null || standaloneRoot is null
                        ? null
                        : ReadLooseCandidate(standaloneRoot, virtualPath, byteBudget),
                [SvPackedLooseSourceKind.ManagerLooseOutput] =
                    virtualPath is null || outputRoot is null
                        ? null
                        : ReadLooseCandidate(outputRoot, virtualPath, byteBudget),
                [SvPackedLooseSourceKind.OutputArchive] =
                    ReadArchiveCandidate(outputArchive, fileHash, byteBudget),
            };

            var effective = ResolveEffectiveSource(
                outputRoot,
                standaloneRoot,
                virtualPath,
                candidates);
            var dualLoose = ResolveDualLooseOutputState(candidates);
            var identity = virtualPath is null
                ? $"trinity-hash:{fileHash.ToString("x16", CultureInfo.InvariantCulture)}"
                : $"romfs/{virtualPath}";
            entries.Add(new ObservedEntry(identity, effective, dualLoose, candidates));
        }

        return new SourceObservation(entries);
    }

    private static SvPackedLooseSourceComparison Project(SourceObservation observation)
    {
        var entries = observation.Entries
            .OrderBy(entry => entry.VirtualIdentity, StringComparer.Ordinal)
            .Select(entry =>
            {
                var effectiveFingerprint = EffectiveFingerprint(entry);
                var baseFingerprint = entry.Candidates[SvPackedLooseSourceKind.BaseArchive];
                return new SvPackedLooseSourceEntry(
                    entry.VirtualIdentity,
                    entry.EffectiveSource,
                    entry.DualLooseOutputState,
                    CandidateOrder.Select(kind =>
                    {
                        var fingerprint = entry.Candidates[kind];
                        return new SvPackedLooseSourceCandidate(
                            kind,
                            fingerprint is not null,
                            fingerprint?.ByteLength,
                            IsEffective(kind, entry.EffectiveSource),
                            fingerprint is null || effectiveFingerprint is null
                                ? null
                                : fingerprint == effectiveFingerprint,
                            fingerprint is null || baseFingerprint is null
                                ? null
                                : fingerprint == baseFingerprint);
                    }).ToArray());
            })
            .ToArray();

        return new SvPackedLooseSourceComparison(
            entries,
            entries.Count(entry =>
                entry.DualLooseOutputState == SvPackedLooseDualOutputState.Divergent));
    }

    private static bool ObservationsMatch(SourceObservation left, SourceObservation right)
    {
        if (left.Entries.Count != right.Entries.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Entries.Count; index++)
        {
            var leftEntry = left.Entries[index];
            var rightEntry = right.Entries[index];
            if (!string.Equals(
                    leftEntry.VirtualIdentity,
                    rightEntry.VirtualIdentity,
                    StringComparison.Ordinal)
                || leftEntry.EffectiveSource != rightEntry.EffectiveSource
                || leftEntry.DualLooseOutputState != rightEntry.DualLooseOutputState)
            {
                return false;
            }

            foreach (var kind in CandidateOrder)
            {
                if (leftEntry.Candidates[kind] != rightEntry.Candidates[kind])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static SvPackedLooseEffectiveSource ResolveEffectiveSource(
        string? outputRoot,
        string? standaloneRoot,
        string? virtualPath,
        IReadOnlyDictionary<SvPackedLooseSourceKind, ContentFingerprint?> candidates)
    {
        if (outputRoot is not null && standaloneRoot is not null && virtualPath is not null)
        {
            var managerPath = ResolveContainedPath(outputRoot, virtualPath);
            var standalonePath = ResolveContainedPath(standaloneRoot, virtualPath);
            var loose = SvWorkflowFileSource.SelectLatestLooseOutput(managerPath, standalonePath);
            if (loose is not null)
            {
                var selected = loose.Value.IsStandalone
                    ? SvPackedLooseEffectiveSource.StandaloneLooseOutput
                    : SvPackedLooseEffectiveSource.ManagerLooseOutput;
                var selectedKind = loose.Value.IsStandalone
                    ? SvPackedLooseSourceKind.StandaloneLooseOutput
                    : SvPackedLooseSourceKind.ManagerLooseOutput;
                if (candidates[selectedKind] is null)
                {
                    throw new InvalidDataException(
                        "A selected Scarlet/Violet loose source could not be observed exactly.");
                }

                return selected;
            }
        }

        if (candidates[SvPackedLooseSourceKind.OutputArchive] is not null)
        {
            return SvPackedLooseEffectiveSource.OutputArchive;
        }

        if (candidates[SvPackedLooseSourceKind.BaseLoose] is not null)
        {
            return SvPackedLooseEffectiveSource.BaseLoose;
        }

        return candidates[SvPackedLooseSourceKind.BaseArchive] is not null
            ? SvPackedLooseEffectiveSource.BaseArchive
            : SvPackedLooseEffectiveSource.None;
    }

    private static SvPackedLooseDualOutputState ResolveDualLooseOutputState(
        IReadOnlyDictionary<SvPackedLooseSourceKind, ContentFingerprint?> candidates)
    {
        var standalone = candidates[SvPackedLooseSourceKind.StandaloneLooseOutput];
        var manager = candidates[SvPackedLooseSourceKind.ManagerLooseOutput];
        if (standalone is null || manager is null)
        {
            return SvPackedLooseDualOutputState.NotComparable;
        }

        return standalone == manager
            ? SvPackedLooseDualOutputState.Identical
            : SvPackedLooseDualOutputState.Divergent;
    }

    private static ContentFingerprint? EffectiveFingerprint(ObservedEntry entry)
    {
        var kind = entry.EffectiveSource switch
        {
            SvPackedLooseEffectiveSource.BaseArchive => SvPackedLooseSourceKind.BaseArchive,
            SvPackedLooseEffectiveSource.BaseLoose => SvPackedLooseSourceKind.BaseLoose,
            SvPackedLooseEffectiveSource.StandaloneLooseOutput =>
                SvPackedLooseSourceKind.StandaloneLooseOutput,
            SvPackedLooseEffectiveSource.ManagerLooseOutput =>
                SvPackedLooseSourceKind.ManagerLooseOutput,
            SvPackedLooseEffectiveSource.OutputArchive => SvPackedLooseSourceKind.OutputArchive,
            SvPackedLooseEffectiveSource.None => (SvPackedLooseSourceKind?)null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(entry),
                entry.EffectiveSource,
                null),
        };
        return kind is null ? null : entry.Candidates[kind.Value];
    }

    private static bool IsEffective(
        SvPackedLooseSourceKind kind,
        SvPackedLooseEffectiveSource effective)
    {
        return (kind, effective) switch
        {
            (SvPackedLooseSourceKind.BaseArchive, SvPackedLooseEffectiveSource.BaseArchive) => true,
            (SvPackedLooseSourceKind.BaseLoose, SvPackedLooseEffectiveSource.BaseLoose) => true,
            (SvPackedLooseSourceKind.StandaloneLooseOutput,
                SvPackedLooseEffectiveSource.StandaloneLooseOutput) => true,
            (SvPackedLooseSourceKind.ManagerLooseOutput,
                SvPackedLooseEffectiveSource.ManagerLooseOutput) => true,
            (SvPackedLooseSourceKind.OutputArchive, SvPackedLooseEffectiveSource.OutputArchive) => true,
            _ => false,
        };
    }

    private static ArchiveObservation? OpenArchive(string root, string? supportFolder)
    {
        var archiveRoot = ResolveArchiveRoot(root);
        if (archiveRoot is null)
        {
            return null;
        }

        var index = SvTrinityArchive.BuildIndex(archiveRoot, MaximumArchiveIndexBytes);
        var hashes = index.Files.Select(file => file.FileHash).ToHashSet();
        var archive = SvTrinityArchive.Open(
            archiveRoot,
            supportFolder,
            index: index,
            maximumIndexBytes: MaximumArchiveIndexBytes,
            maximumPackBytes: MaximumArchivePackBytes);
        return new ArchiveObservation(archive, hashes);
    }

    private static string? ResolveArchiveRoot(string root)
    {
        if (HasArchiveAt(root))
        {
            return root;
        }

        var nestedRoot = Path.Combine(root, "romfs");
        return HasArchiveAt(nestedRoot) ? nestedRoot : null;
    }

    private static bool HasArchiveAt(string root)
    {
        if (!Directory.Exists(root)
            || !HasSafeExistingChain(root, root, isDirectory: true))
        {
            return false;
        }

        var descriptorPath = Path.Combine(root, "arc", "data.trpfd");
        var fileSystemPath = Path.Combine(root, "arc", "data.trpfs");
        return IsRegularFile(descriptorPath)
            && HasSafeExistingChain(root, descriptorPath, isDirectory: false)
            && IsRegularFile(fileSystemPath)
            && HasSafeExistingChain(root, fileSystemPath, isDirectory: false);
    }

    private static ContentFingerprint? ReadArchiveCandidate(
        ArchiveObservation? archive,
        ulong fileHash,
        ObservationByteBudget budget)
    {
        if (archive is null || !archive.FileHashes.Contains(fileHash))
        {
            return null;
        }

        if (!archive.Archive.TryReadFileHash(fileHash, MaximumCandidateBytes, out var bytes))
        {
            throw new InvalidDataException(
                "A Scarlet/Violet archive index and packed file lookup disagree.");
        }

        budget.Observe(bytes.LongLength);
        return Fingerprint(bytes);
    }

    private static ContentFingerprint? ReadLooseCandidate(
        string root,
        string virtualPath,
        ObservationByteBudget budget)
    {
        var path = ResolveContainedPath(root, virtualPath);
        FileInfo file;
        try
        {
            file = new FileInfo(path);
            file.Refresh();
            if (!file.Exists)
            {
                return null;
            }

            if (file.Attributes.HasFlag(FileAttributes.Directory)
                || file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || !HasSafeExistingChain(root, path, isDirectory: false))
            {
                throw new InvalidDataException(
                    "A Scarlet/Violet loose candidate is not a safe regular file.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("A Scarlet/Violet loose candidate could not be inspected.", exception);
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        var length = stream.Length;
        if (length < 0 || length > MaximumCandidateBytes)
        {
            throw new InvalidDataException(
                "A Scarlet/Violet loose candidate exceeds its bounded byte limit.");
        }

        budget.Observe(length);
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1 || stream.Length != length)
        {
            throw new SvPackedLooseSourceObservationChangedException();
        }

        return Fingerprint(bytes);
    }

    private static ContentFingerprint Fingerprint(byte[] bytes)
    {
        return new ContentFingerprint(
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static void AddLoosePaths(
        IEnumerable<string> paths,
        IDictionary<ulong, string> virtualPathByHash,
        ISet<ulong> interestingHashes)
    {
        foreach (var path in paths)
        {
            var hash = AddVirtualPath(virtualPathByHash, path);
            interestingHashes.Add(hash);
        }
    }

    private static ulong AddVirtualPath(
        IDictionary<ulong, string> virtualPathByHash,
        string virtualPath)
    {
        var hash = SvTrinityPathHasher.HashPath(virtualPath);
        if (virtualPathByHash.TryGetValue(hash, out var existing)
            && !string.Equals(existing, virtualPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Distinct Scarlet/Violet virtual paths share one archive identity.");
        }

        virtualPathByHash[hash] = virtualPath;
        return hash;
    }

    private static IReadOnlyList<string> EnumerateLooseRoot(
        string root,
        IReadOnlyCollection<string> excludedTopLevelNames)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var results = new List<string>();
        var pending = new Stack<(DirectoryInfo Directory, int Depth)>();
        pending.Push((new DirectoryInfo(root), 0));
        var entryCount = 0;
        var directoryCount = 0;
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Pop();
            if (depth > MaximumTraversalDepth
                || ++directoryCount > MaximumLooseDirectories)
            {
                throw new InvalidDataException(
                    "Scarlet/Violet loose source discovery exceeds its bounded directory limit.");
            }

            directory.Refresh();
            if (!directory.Exists
                || directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || !HasSafeExistingChain(root, directory.FullName, isDirectory: true))
            {
                throw new InvalidDataException(
                    "A Scarlet/Violet loose source directory is unavailable or linked.");
            }

            foreach (var child in directory.EnumerateFileSystemInfos())
            {
                if (++entryCount > MaximumLooseFileSystemEntries)
                {
                    throw new InvalidDataException(
                        "Scarlet/Violet loose source discovery exceeds its bounded entry limit.");
                }

                child.Refresh();
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException(
                        "A Scarlet/Violet loose source contains a linked entry.");
                }

                if (depth == 0 && excludedTopLevelNames.Any(name =>
                        string.Equals(name, child.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (child is DirectoryInfo childDirectory)
                {
                    pending.Push((childDirectory, checked(depth + 1)));
                    continue;
                }

                var relative = Path.GetRelativePath(root, child.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                var virtualPath = NormalizeVirtualPath(relative);
                // data.trpfs is the packed container backing the archive candidate,
                // not an independently addressable virtual game file.
                if (string.Equals(
                        virtualPath,
                        "arc/data.trpfs",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(virtualPath);
                if (results.Count > MaximumVirtualIdentities)
                {
                    throw new InvalidDataException(
                        "Scarlet/Violet loose sources exceed the bounded virtual-identity limit.");
                }
            }
        }

        return results.Order(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeRoot(string rootPath, string label)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(root)
            || !HasSafeExistingChain(root, root, isDirectory: true))
        {
            throw new InvalidDataException(
                $"The Scarlet/Violet {label} root is unavailable or linked.");
        }

        return root;
    }

    private static bool HasSafeExistingChain(string root, string path, bool isDirectory)
    {
        var rootInfo = new DirectoryInfo(root);
        rootInfo.Refresh();
        if (!rootInfo.Exists
            || rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        var relative = Path.GetRelativePath(root, path);
        if (PathContainment.IsOutsideRoot(relative))
        {
            return false;
        }

        var current = root;
        var segments = relative is "." or ""
            ? Array.Empty<string>()
            : relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            entry.Refresh();
            if (!entry.Exists
                || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }
        }

        return isDirectory ? Directory.Exists(path) : File.Exists(path);
    }

    private static string NormalizeVirtualPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["romfs/".Length..];
        }

        var segments = normalized.Split('/');
        if (normalized.Length == 0
            || normalized.Length > MaximumVirtualPathBytes
            || Encoding.UTF8.GetByteCount(normalized) > MaximumVirtualPathBytes
            || segments.Length > MaximumTraversalDepth
            || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."))
        {
            throw new InvalidDataException(
                "A Scarlet/Violet virtual source identity is not canonical and bounded.");
        }

        return normalized;
    }

    private static string ResolveContainedPath(string root, string virtualPath)
    {
        var path = Path.GetFullPath(Path.Combine(
            root,
            virtualPath.Replace('/', Path.DirectorySeparatorChar)));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(root, path)))
        {
            throw new InvalidDataException(
                "A Scarlet/Violet virtual source identity escapes its configured root.");
        }

        return path;
    }

    private static bool IsRegularFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return !attributes.HasFlag(FileAttributes.Directory)
                && !attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException
                or UnauthorizedAccessException
                or SecurityException)
        {
            return false;
        }
    }

    private sealed class ObservationByteBudget
    {
        private long observedBytes;

        public void Observe(long byteCount)
        {
            if (byteCount < 0 || byteCount > MaximumObservationBytes - observedBytes)
            {
                throw new InvalidDataException(
                    "Packed and loose source comparison exceeds its bounded aggregate byte limit.");
            }

            observedBytes = checked(observedBytes + byteCount);
        }
    }

    private sealed class ArchiveObservation : IDisposable
    {
        public ArchiveObservation(SvTrinityArchive archive, IReadOnlySet<ulong> fileHashes)
        {
            Archive = archive;
            FileHashes = fileHashes;
        }

        public SvTrinityArchive Archive { get; }

        public IReadOnlySet<ulong> FileHashes { get; }

        public void Dispose() => Archive.Dispose();
    }

    private sealed record ContentFingerprint(long ByteLength, string Sha256);

    private sealed record ObservedEntry(
        string VirtualIdentity,
        SvPackedLooseEffectiveSource EffectiveSource,
        SvPackedLooseDualOutputState DualLooseOutputState,
        IReadOnlyDictionary<SvPackedLooseSourceKind, ContentFingerprint?> Candidates);

    private sealed record SourceObservation(IReadOnlyList<ObservedEntry> Entries);
}
