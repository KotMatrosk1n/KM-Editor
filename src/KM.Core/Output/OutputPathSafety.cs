// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using KM.Core.Semantics;

namespace KM.Core.Output;

internal sealed class OutputPathSafety
{
    private const UnixFileMode PrivateDirectoryUnixMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileUnixMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const string MetadataMarkerName = "output-store.marker";
    private const string MetadataMarkerPendingName = ".output-store.marker.pending.tmp";
    private const int MaximumRecognizedManifestBytes = 4 * 1024 * 1024;
    private const int MaximumRecognizedManifestEntries = 32_768;
    private static readonly string[] RecognizedManifestNames =
    [
        "sv-mod-merger-manifest.json",
        "za-mod-merger-manifest.json",
    ];
    private static readonly byte[] MetadataMarkerContent =
        Encoding.ASCII.GetBytes("KM output metadata store v1\n");
    private readonly StringComparison pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly SecurityIdentifier? currentUserSid;
    private bool metadataClaimVerified;

    public OutputPathSafety(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || !Path.IsPathFullyQualified(outputRoot))
        {
            throw new ArgumentException("The output root must be a fully qualified path.", nameof(outputRoot));
        }

        OutputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        ValidateExistingAncestorChain(OutputRoot);
        if (OperatingSystem.IsWindows())
        {
            currentUserSid = GetCurrentWindowsUserSid();
        }

        MetadataRoot = GetContainedPath(OutputRoot, ".km");
        TransactionsRoot = GetContainedPath(MetadataRoot, "transactions");
        CheckpointsRoot = GetContainedPath(MetadataRoot, "checkpoints");
    }

    public string OutputRoot { get; }

    public string MetadataRoot { get; }

    public string TransactionsRoot { get; }

    public string CheckpointsRoot { get; }

    public void EnsureMetadataLayout()
    {
        ValidateExistingAncestorChain(OutputRoot);
        ValidatePortableChildIdentity(OutputRoot, ".km");
        EnsureMetadataRootClaimed();
        EnsurePrivateMetadataDirectory(TransactionsRoot, MetadataRoot);
        EnsurePrivateMetadataDirectory(CheckpointsRoot, MetadataRoot);
        metadataClaimVerified = true;
    }

    public string ResolveTarget(RelativeOutputPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ValidatePortablePathIdentity(path);
        if (path.Value.Count(character => character == '/') >= OutputLimits.MaximumOutputPathDepth)
        {
            throw new OutputLimitExceededException("An output path exceeds the supported directory depth.");
        }

        var firstSeparator = path.Value.IndexOf('/');
        var firstSegment = firstSeparator < 0 ? path.Value : path.Value[..firstSeparator];
        if (string.Equals(firstSegment, ".km", StringComparison.OrdinalIgnoreCase))
        {
            throw new OutputPathSecurityException();
        }

        var result = Path.GetFullPath(Path.Combine(
            OutputRoot,
            path.Value.Replace('/', Path.DirectorySeparatorChar)));
        EnsureContained(result, OutputRoot, allowRoot: false);
        return result;
    }

    public string ResolveTransactionDirectory(OutputTransactionId transactionId)
    {
        return GetContainedPath(TransactionsRoot, transactionId.Value);
    }

    public string ResolveTransactionPreparationDirectory(OutputTransactionId transactionId)
    {
        return GetContainedPath(TransactionsRoot, "preparing-" + transactionId.Value);
    }

    public string ResolveTransactionTombstoneDirectory(OutputTransactionId transactionId)
    {
        return GetContainedPath(TransactionsRoot, "retired-" + transactionId.Value);
    }

    public string ResolveCheckpointDirectory(OutputCheckpointId checkpointId)
    {
        return GetContainedPath(CheckpointsRoot, checkpointId.Value);
    }

    public string ResolveCheckpointTombstoneDirectory(OutputCheckpointId checkpointId)
    {
        return GetContainedPath(CheckpointsRoot, "retired-" + checkpointId.Value);
    }

    public string GetContainedMetadataPath(string parent, string child)
    {
        EnsureContained(parent, MetadataRoot, allowRoot: true);
        if (string.IsNullOrWhiteSpace(child)
            || child is "." or ".."
            || child.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || child.Any(char.IsControl))
        {
            throw new OutputPathSecurityException();
        }

        return GetContainedPath(parent, child);
    }

    public void EnsureMetadataDirectory(string path, string parent)
    {
        EnsureContained(path, MetadataRoot, allowRoot: false);
        EnsureContained(parent, MetadataRoot, allowRoot: true);
        EnsurePrivateMetadataDirectory(path, parent);
    }

    public void CreateMetadataDirectory(string path, string parent)
    {
        EnsureContained(path, MetadataRoot, allowRoot: false);
        EnsureContained(parent, MetadataRoot, allowRoot: true);
        var directory = new DirectoryInfo(path);
        directory.Refresh();
        if (directory.Exists || File.Exists(path) || HasLinkTarget(directory))
        {
            throw new OutputPathSecurityException();
        }

        EnsurePrivateMetadataDirectory(path, parent);
    }

    public void MoveMetadataDirectory(string source, string destination, string parent)
    {
        EnsureContained(source, parent, allowRoot: false);
        EnsureContained(destination, parent, allowRoot: false);
        ValidateMetadataDirectory(source);
        var destinationInfo = new DirectoryInfo(destination);
        destinationInfo.Refresh();
        if (destinationInfo.Exists || File.Exists(destination) || HasLinkTarget(destinationInfo))
        {
            throw new OutputPathSecurityException();
        }

        OutputFileSystemDurability.MoveDirectory(source, destination);
    }

    public void ValidateTarget(RelativeOutputPath path)
    {
        ValidatePortablePathIdentity(path);
        var targetPath = ResolveTarget(path);
        ValidateDirectoryChain(Path.GetDirectoryName(targetPath)!, OutputRoot);
        ValidateFileDestination(targetPath);
    }

    public void ValidateMetadataFile(string path)
    {
        EnsureContained(path, MetadataRoot, allowRoot: false);
        ValidateDirectoryChain(Path.GetDirectoryName(path)!, MetadataRoot);
        ValidateFileDestination(path);
    }

    public void ValidateMetadataDirectory(string path)
    {
        EnsureContained(path, MetadataRoot, allowRoot: true);
        ValidateDirectoryChain(path, MetadataRoot);
        ValidateDirectory(path);
    }

    public void EnsurePrivateMetadataFile(FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateMetadataFile(stream.Name);
        if (OperatingSystem.IsWindows())
        {
            EnsurePrivateWindowsFile(stream);
        }
        else
        {
            File.SetUnixFileMode(stream.SafeFileHandle, PrivateFileUnixMode);
        }
    }

    private void EnsurePrivateMetadataFile(string path)
    {
        ValidateMetadataFile(path);
        if (OperatingSystem.IsWindows())
        {
            EnsurePrivateWindowsFile(path);
        }
        else
        {
            File.SetUnixFileMode(path, PrivateFileUnixMode);
        }
    }

    public ImmutableArray<RelativeOutputPath> EnsureTargetParentDirectories(RelativeOutputPath target)
    {
        ValidatePortablePathIdentity(target);
        var segments = target.Value.Split('/');
        var current = OutputRoot;
        var relativeSegments = new List<string>();
        var created = ImmutableArray.CreateBuilder<RelativeOutputPath>();
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            relativeSegments.Add(segment);
            current = GetContainedPath(current, segment);
            var directory = new DirectoryInfo(current);
            directory.Refresh();
            if (directory.Exists)
            {
                ValidateDirectory(current);
                continue;
            }

            if (File.Exists(current) || HasLinkTarget(directory))
            {
                throw new OutputPathSecurityException();
            }

            Directory.CreateDirectory(current);
            OutputFileSystemDurability.FlushDirectory(Path.GetDirectoryName(current)!);
            ValidateDirectory(current);
            created.Add(new RelativeOutputPath(string.Join('/', relativeSegments)));
        }

        return created.ToImmutable();
    }

    public IEnumerable<RelativeOutputPath> EnumerateOrdinaryFiles(int maximumCount)
    {
        if (!metadataClaimVerified)
        {
            throw new OutputPathSecurityException();
        }

        var count = 0;
        var directoryCount = 0;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((OutputRoot, 0));
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Pop();
            if (depth > OutputLimits.MaximumOutputPathDepth
                || directoryCount == OutputLimits.MaximumInventoryDirectories)
            {
                throw new OutputLimitExceededException("The output directory tree exceeds its traversal limits.");
            }

            directoryCount++;
            ValidateDirectory(directory);
            ValidatePortableSiblingNames(directory);
            foreach (var child in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(child);
                if (string.Equals(directory, OutputRoot, pathComparison)
                    && string.Equals(name, ".km", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = new FileInfo(child);
                info.Refresh();
                if (HasLinkTarget(info))
                {
                    throw new OutputPathSecurityException();
                }

                if (Directory.Exists(child))
                {
                    pending.Push((child, depth + 1));
                    continue;
                }

                if (!File.Exists(child))
                {
                    throw new OutputPathSecurityException();
                }

                if (count == maximumCount)
                {
                    throw new OutputLimitExceededException(
                        $"The output inventory cannot contain more than {maximumCount} files.");
                }

                count++;
                var relative = Path.GetRelativePath(OutputRoot, child)
                    .Replace(Path.DirectorySeparatorChar, '/');
                yield return new RelativeOutputPath(relative);
            }
        }
    }

    public IEnumerable<OutputDirectoryMembershipEntry> EnumerateDirectoryMembership(
        RelativeOutputPath directory,
        int maximumCount)
    {
        if (!metadataClaimVerified)
        {
            throw new OutputPathSecurityException();
        }

        return EnumerateDirectoryMembershipReadOnly(directory, maximumCount);
    }

    internal IEnumerable<OutputDirectoryMembershipEntry> EnumerateDirectoryMembershipReadOnly(
        RelativeOutputPath directory,
        int maximumCount)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (maximumCount <= 0 || maximumCount > OutputLimits.MaximumIntegrityEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        if (!OwnedDirectoryExists(directory))
        {
            yield break;
        }

        var root = ResolveTarget(directory);
        var count = 0;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            if (depth > OutputLimits.MaximumOutputPathDepth)
            {
                throw new OutputLimitExceededException(
                    "A dependency directory exceeds the supported traversal depth.");
            }

            ValidateDirectory(current);
            ValidatePortableSiblingNames(current);
            foreach (var child in Directory.EnumerateFileSystemEntries(current))
            {
                if (count == maximumCount)
                {
                    throw new OutputLimitExceededException(
                        "A directory membership dependency contains too many entries.");
                }

                var info = new FileInfo(child);
                info.Refresh();
                if (HasLinkTarget(info))
                {
                    throw new OutputPathSecurityException();
                }

                var isDirectory = Directory.Exists(child);
                if (!isDirectory && !File.Exists(child))
                {
                    throw new OutputPathSecurityException();
                }

                count++;
                var relative = new RelativeOutputPath(
                    Path.GetRelativePath(OutputRoot, child)
                        .Replace(Path.DirectorySeparatorChar, '/'));
                yield return new OutputDirectoryMembershipEntry(relative, isDirectory);
                if (isDirectory)
                {
                    pending.Push((child, depth + 1));
                }
            }
        }
    }

    public void DeleteEmptyOwnedDirectories(IEnumerable<RelativeOutputPath> directories)
    {
        foreach (var directory in directories
                     .OrderByDescending(path => path.Value.Count(character => character == '/'))
                     .ThenByDescending(path => path.Value.Length))
        {
            var path = ResolveTarget(directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            ValidateDirectoryChain(path, OutputRoot);
            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
    }

    public bool OwnedDirectoryExists(RelativeOutputPath directory)
    {
        ValidatePortablePathIdentity(directory);
        var path = ResolveTarget(directory);
        if (!Directory.Exists(path))
        {
            if (File.Exists(path) || HasLinkTarget(new DirectoryInfo(path)))
            {
                throw new OutputPathSecurityException();
            }

            return false;
        }

        ValidateDirectoryChain(path, OutputRoot);
        ValidateDirectory(path);
        return true;
    }

    public void DeleteMetadataTree(string directory)
    {
        EnsureContained(directory, MetadataRoot, allowRoot: false);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var visitedEntries = 0;
        DeleteMetadataTreeCore(directory, depth: 0, ref visitedEntries);
    }

    private void DeleteMetadataTreeCore(string directory, int depth, ref int visitedEntries)
    {
        if (depth > OutputLimits.MaximumMetadataTreeDepth)
        {
            throw new OutputPathSecurityException();
        }

        ValidateDirectory(directory);
        foreach (var child in Directory.EnumerateFileSystemEntries(directory))
        {
            if (visitedEntries == OutputLimits.MaximumMetadataTreeEntries)
            {
                throw new OutputLimitExceededException("An output metadata tree exceeds its cleanup limit.");
            }

            visitedEntries++;
            var info = new FileInfo(child);
            info.Refresh();
            if (HasLinkTarget(info))
            {
                throw new OutputPathSecurityException();
            }

            if (Directory.Exists(child))
            {
                DeleteMetadataTreeCore(child, depth + 1, ref visitedEntries);
            }
            else if (File.Exists(child))
            {
                File.Delete(child);
            }
            else
            {
                throw new OutputPathSecurityException();
            }
        }

        Directory.Delete(directory);
    }

    private void EnsurePrivateMetadataDirectory(string path, string parent)
    {
        ValidateDirectoryChain(parent, OutputRoot);
        var directory = new DirectoryInfo(path);
        directory.Refresh();
        if (HasLinkTarget(directory) || (File.Exists(path) && !directory.Exists))
        {
            throw new OutputPathSecurityException();
        }

        var existed = directory.Exists;
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path, PrivateDirectoryUnixMode);
        }
        else
        {
            if (!Directory.Exists(path))
            {
                CreatePrivateWindowsDirectory(path);
            }
        }

        ValidateDirectory(path);
        EnsurePrivateMetadataDirectoryPermissions(path);
        if (!existed)
        {
            OutputFileSystemDurability.FlushDirectory(parent);
        }
    }

    private void EnsureMetadataRootClaimed()
    {
        var metadataDirectory = new DirectoryInfo(MetadataRoot);
        metadataDirectory.Refresh();
        if (File.Exists(MetadataRoot) || HasLinkTarget(metadataDirectory))
        {
            throw new OutputPathSecurityException();
        }

        if (!metadataDirectory.Exists)
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(MetadataRoot, PrivateDirectoryUnixMode);
            }
            else
            {
                CreatePrivateWindowsDirectory(MetadataRoot);
            }

            OutputFileSystemDurability.FlushDirectory(OutputRoot);
        }

        ValidateDirectory(MetadataRoot);
        var markerPath = GetContainedPath(MetadataRoot, MetadataMarkerName);
        var pendingPath = GetContainedPath(MetadataRoot, MetadataMarkerPendingName);
        if (File.Exists(markerPath))
        {
            ValidateMetadataMarker(markerPath);
            if (File.Exists(pendingPath))
            {
                CleanupRecognizedPendingMarker(pendingPath);
            }

            if (Directory.Exists(pendingPath) || HasLinkTarget(new FileInfo(pendingPath)))
            {
                throw new OutputPathSecurityException();
            }
        }
        else
        {
            ValidateUnclaimedMetadataRoot(pendingPath);
            EnsurePrivateMetadataDirectoryPermissions(MetadataRoot);
            PublishMetadataMarker(markerPath, pendingPath);
            ValidateMetadataMarker(markerPath);
        }

        EnsurePrivateMetadataDirectoryPermissions(MetadataRoot);
        EnsurePrivateMetadataFile(markerPath);
        EnsurePrivateRecognizedManifestFiles();
        ValidateMetadataMarker(markerPath);
    }

    private void ValidateUnclaimedMetadataRoot(string pendingPath)
    {
        var entries = Directory.EnumerateFileSystemEntries(MetadataRoot).Take(4).ToArray();
        if (entries.Length == 0)
        {
            return;
        }

        if (entries.Length > RecognizedManifestNames.Length + 1)
        {
            throw new OutputPathSecurityException();
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasPendingMarker = false;
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (!names.Add(name) || Directory.Exists(entry) || HasLinkTarget(new FileInfo(entry)))
            {
                throw new OutputPathSecurityException();
            }

            if (string.Equals(entry, pendingPath, pathComparison))
            {
                hasPendingMarker = true;
                continue;
            }

            if (!RecognizedManifestNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new OutputPathSecurityException();
            }

            ValidateRecognizedManifest(entry);
            EnsurePrivateMetadataFile(entry);
        }

        if (hasPendingMarker)
        {
            ValidateRecognizedPendingMarkerWithRetry(pendingPath);
        }
    }

    private void EnsurePrivateRecognizedManifestFiles()
    {
        foreach (var name in RecognizedManifestNames)
        {
            var path = GetContainedPath(MetadataRoot, name);
            if (!File.Exists(path))
            {
                continue;
            }

            if (Directory.Exists(path) || HasLinkTarget(new FileInfo(path)))
            {
                throw new OutputPathSecurityException();
            }

            EnsurePrivateMetadataFile(path);
        }
    }

    private static void ValidateRecognizedManifest(string path)
    {
        try
        {
            var info = new FileInfo(path);
            info.Refresh();
            if (!info.Exists
                || HasLinkTarget(info)
                || info.Length is < 2 or > MaximumRecognizedManifestBytes)
            {
                throw new OutputPathSecurityException();
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            using var document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Array
                || entries.GetArrayLength() > MaximumRecognizedManifestEntries)
            {
                throw new OutputPathSecurityException();
            }

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("relativePath", out var relativePathProperty)
                    || relativePathProperty.ValueKind != JsonValueKind.String
                    || !entry.TryGetProperty("sha256", out var sha256Property)
                    || sha256Property.ValueKind != JsonValueKind.String)
                {
                    throw new OutputPathSecurityException();
                }

                var relativePath = relativePathProperty.GetString();
                var sha256 = sha256Property.GetString();
                if (relativePath is null
                    || !relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
                    || OutputMetadataNamespace.ContainsReservedSegment(relativePath)
                    || sha256 is null
                    || sha256.Length != 64
                    || sha256.Any(character => !Uri.IsHexDigit(character)))
                {
                    throw new OutputPathSecurityException();
                }

                var validatedPath = new RelativeOutputPath(relativePath);
                if (!paths.Add(validatedPath.CanonicalKey))
                {
                    throw new OutputPathSecurityException();
                }
            }
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            ArgumentException or
            NotSupportedException or
            OverflowException)
        {
            throw new OutputPathSecurityException();
        }
    }

    private static void ValidateRecognizedPendingMarkerWithRetry(string pendingPath)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                _ = ValidateRecognizedPendingMarker(pendingPath, requireComplete: false);
                return;
            }
            catch (Exception exception) when (
                attempt < 39
                && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(10));
            }
        }

        throw new OutputPathSecurityException();
    }

    private static void CleanupRecognizedPendingMarker(string pendingPath)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                ValidateRecognizedPendingMarker(pendingPath, requireComplete: false);
                File.Delete(pendingPath);
                return;
            }
            catch (Exception exception) when (
                attempt < 39
                && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(10));
            }
        }

        throw new OutputPathSecurityException();
    }

    private void PublishMetadataMarker(string markerPath, string pendingPath)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (File.Exists(markerPath))
            {
                ValidateMetadataMarker(markerPath);
                if (File.Exists(pendingPath))
                {
                    CleanupRecognizedPendingMarker(pendingPath);
                }

                return;
            }

            try
            {
                if (File.Exists(pendingPath))
                {
                    var isComplete = ValidateRecognizedPendingMarker(pendingPath, requireComplete: false);
                    if (isComplete)
                    {
                        EnsurePrivateMetadataFile(pendingPath);
                        OutputFileSystemDurability.Move(pendingPath, markerPath, overwrite: false);
                        return;
                    }

                    File.Delete(pendingPath);
                }

                var streamOptions = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                };
                if (!OperatingSystem.IsWindows())
                {
                    streamOptions.UnixCreateMode = PrivateFileUnixMode;
                }

                using (var marker = new FileStream(pendingPath, streamOptions))
                {
                    EnsurePrivateMetadataFile(marker);
                    marker.Write(MetadataMarkerContent);
                    marker.Flush(flushToDisk: true);
                }

                ValidateRecognizedPendingMarker(pendingPath, requireComplete: true);
                OutputFileSystemDurability.Move(pendingPath, markerPath, overwrite: false);
                return;
            }
            catch (Exception exception) when (
                attempt < 39
                && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(10));
            }
        }

        throw new OutputPathSecurityException();
    }

    private static void ValidateMetadataMarker(string markerPath)
    {
        var markerInfo = new FileInfo(markerPath);
        markerInfo.Refresh();
        if (!markerInfo.Exists || HasLinkTarget(markerInfo) || markerInfo.Length != MetadataMarkerContent.Length)
        {
            throw new OutputPathSecurityException();
        }

        Span<byte> content = stackalloc byte[MetadataMarkerContent.Length];
        using var marker = new FileStream(
            markerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: MetadataMarkerContent.Length,
            FileOptions.SequentialScan);
        marker.ReadExactly(content);
        markerInfo.Refresh();
        if (HasLinkTarget(markerInfo) || !content.SequenceEqual(MetadataMarkerContent))
        {
            throw new OutputPathSecurityException();
        }
    }

    private static bool ValidateRecognizedPendingMarker(string pendingPath, bool requireComplete)
    {
        var pending = new FileInfo(pendingPath);
        pending.Refresh();
        if (!pending.Exists
            || HasLinkTarget(pending)
            || pending.Length > MetadataMarkerContent.Length)
        {
            throw new OutputPathSecurityException();
        }

        var content = new byte[checked((int)pending.Length)];
        using (var stream = new FileStream(
                   pendingPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.None,
                   bufferSize: MetadataMarkerContent.Length,
                   FileOptions.SequentialScan))
        {
            stream.ReadExactly(content);
        }

        var complete = content.Length == MetadataMarkerContent.Length;
        if (!content.AsSpan().SequenceEqual(MetadataMarkerContent.AsSpan(0, content.Length))
            || requireComplete && !complete)
        {
            throw new OutputPathSecurityException();
        }

        return complete;
    }

    private void ValidateDirectoryChain(string targetDirectory, string boundary)
    {
        EnsureContained(targetDirectory, boundary, allowRoot: true);
        ValidateDirectory(boundary);
        var relative = Path.GetRelativePath(boundary, targetDirectory);
        if (relative == ".")
        {
            return;
        }

        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > OutputLimits.MaximumOutputPathDepth)
        {
            throw new OutputLimitExceededException("An output path exceeds the supported directory depth.");
        }

        var current = boundary;
        foreach (var segment in segments)
        {
            current = GetContainedPath(current, segment);
            if (!Directory.Exists(current))
            {
                if (File.Exists(current) || HasLinkTarget(new DirectoryInfo(current)))
                {
                    throw new OutputPathSecurityException();
                }

                return;
            }

            ValidateDirectory(current);
        }
    }

    private string GetContainedPath(string parent, string child)
    {
        var result = Path.GetFullPath(Path.Combine(parent, child));
        EnsureContained(result, OutputRoot, allowRoot: false);
        return result;
    }

    private void EnsureContained(string candidate, string boundary, bool allowRoot)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var fullBoundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundary));
        var relative = Path.GetRelativePath(fullBoundary, fullCandidate);
        if ((!allowRoot && relative == ".")
            || Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new OutputPathSecurityException();
        }
    }

    private static void ValidateExistingAncestorChain(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new OutputPathSecurityException();
        }

        var current = Path.TrimEndingDirectorySeparator(root);
        if (current.Length == 0)
        {
            current = root;
        }

        ValidateDirectory(current);
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".")
        {
            return;
        }

        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            ValidateDirectory(current);
        }
    }

    private void ValidatePortablePathIdentity(RelativeOutputPath path)
    {
        var current = OutputRoot;
        foreach (var segment in path.Value.Split('/'))
        {
            if (!Directory.Exists(current))
            {
                return;
            }

            ValidatePortableChildIdentity(current, segment);
            current = Path.Combine(current, segment);
        }
    }

    private static void ValidatePortableChildIdentity(string directory, string expectedName)
    {
        var expectedKey = PortableNameKey(expectedName);
        var matches = 0;
        var inspected = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (inspected == OutputLimits.MaximumInventoryDirectories)
            {
                throw new OutputLimitExceededException(
                    "An output directory contains too many entries for portable path validation.");
            }

            inspected++;
            var name = Path.GetFileName(entry);
            if (!string.Equals(PortableNameKey(name), expectedKey, StringComparison.Ordinal))
            {
                continue;
            }

            matches++;
            if (matches > 1 || !string.Equals(name, expectedName, StringComparison.Ordinal))
            {
                throw new OutputPathSecurityException();
            }
        }

        // On Windows, a DOS 8.3 alias can resolve to an existing child without
        // appearing as that name in directory enumeration. Treat any such
        // unenumerated-but-resolvable name as an alias and reject it.
        if (matches == 0)
        {
            var expectedPath = Path.Combine(directory, expectedName);
            var expectedEntry = new FileInfo(expectedPath);
            expectedEntry.Refresh();
            if (expectedEntry.Exists
                || Directory.Exists(expectedPath)
                || HasLinkTarget(expectedEntry))
            {
                throw new OutputPathSecurityException();
            }
        }
    }

    private static void ValidatePortableSiblingNames(string directory)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inspected = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (inspected == OutputLimits.MaximumInventoryDirectories)
            {
                throw new OutputLimitExceededException(
                    "An output directory contains too many entries for portable path validation.");
            }

            inspected++;
            if (!seen.Add(PortableNameKey(Path.GetFileName(entry))))
            {
                throw new OutputPathSecurityException();
            }
        }
    }

    private static string PortableNameKey(string value)
    {
        try
        {
            return value.Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            throw new OutputPathSecurityException();
        }
    }

    private void EnsurePrivateMetadataDirectoryPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            EnsurePrivateWindowsDirectory(path);
        }
        else
        {
            File.SetUnixFileMode(path, PrivateDirectoryUnixMode);
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier GetCurrentWindowsUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User ?? throw new OutputPathSecurityException();
        }
        catch (Exception exception) when (IsAclFailure(exception))
        {
            throw new OutputPathSecurityException();
        }
    }

    [SupportedOSPlatform("windows")]
    private void EnsurePrivateWindowsDirectory(string path)
    {
        try
        {
            var security = CreatePrivateWindowsDirectorySecurity();
            var directory = new DirectoryInfo(path);
            directory.SetAccessControl(security);
            ValidatePrivateWindowsAcl(
                directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
        }
        catch (Exception exception) when (IsAclFailure(exception))
        {
            throw new OutputPathSecurityException();
        }
    }

    [SupportedOSPlatform("windows")]
    private void CreatePrivateWindowsDirectory(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            directory.Create(CreatePrivateWindowsDirectorySecurity());
            ValidatePrivateWindowsAcl(
                directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
        }
        catch (Exception exception) when (IsAclFailure(exception))
        {
            throw new OutputPathSecurityException();
        }
    }

    [SupportedOSPlatform("windows")]
    private DirectorySecurity CreatePrivateWindowsDirectorySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUserSid!);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUserSid!,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    [SupportedOSPlatform("windows")]
    private void EnsurePrivateWindowsFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var security = CreatePrivateWindowsFileSecurity();
            file.SetAccessControl(security);
            ValidatePrivateWindowsAcl(
                file.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
        }
        catch (Exception exception) when (IsAclFailure(exception))
        {
            throw new OutputPathSecurityException();
        }
    }

    [SupportedOSPlatform("windows")]
    private void EnsurePrivateWindowsFile(FileStream stream)
    {
        EnsurePrivateWindowsFile(stream.Name);
    }

    [SupportedOSPlatform("windows")]
    private FileSecurity CreatePrivateWindowsFileSecurity()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUserSid!);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUserSid!,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    [SupportedOSPlatform("windows")]
    private void ValidatePrivateWindowsAcl(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        if (!security.AreAccessRulesProtected
            || owner != currentUserSid
            || rules.Length != 1
            || rules[0].IdentityReference != currentUserSid
            || rules[0].AccessControlType != AccessControlType.Allow
            || (rules[0].FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl)
        {
            throw new OutputPathSecurityException();
        }
    }

    private static bool IsAclFailure(Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            PlatformNotSupportedException or
            NotSupportedException or
            InvalidOperationException or
            ArgumentException or
            IdentityNotMappedException or
            PrivilegeNotHeldException;
    }

    private static void ValidateDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        directory.Refresh();
        if (!directory.Exists
            || HasLinkTarget(directory))
        {
            throw new OutputPathSecurityException();
        }
    }

    private static void ValidateFileDestination(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (HasLinkTarget(file) || Directory.Exists(path))
        {
            throw new OutputPathSecurityException();
        }
    }

    private static bool HasLinkTarget(FileSystemInfo entry)
    {
        try
        {
            // Cloud Files placeholders are reparse entries without a link target.
            // Symbolic links and junctions expose a target and remain forbidden.
            return !string.IsNullOrEmpty(entry.LinkTarget);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            throw new OutputPathSecurityException();
        }
    }
}
