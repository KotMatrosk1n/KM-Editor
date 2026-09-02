// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.SwSh.Editing;

public static class SwShChangePlanSourceGuard
{
    private const string DiagnosticDomain = "workflow.changePlan";
    private const string OutputMode = "sword-shield-layered-output";
    private const string OutputOwner = "sword-shield-verified-editor";
    private const string OutputPreservationRule = "verified-whole-file-postimage";
    private const string OutputOrigin = "workflow.sword-shield.change-plan";
    private const string ComposedExeFsMainPath = "exefs/main";

    public static ChangePlan Capture(ProjectPaths paths, ChangePlan plan)
    {
        return Capture(paths, plan, preserveExplicitSourceLayers: false);
    }

    public static ChangePlan Capture(
        ProjectPaths paths,
        ChangePlan plan,
        bool preserveExplicitSourceLayers)
    {
        return CaptureCore(
            paths,
            plan,
            preserveExplicitSourceLayers,
            sourceReadBudget: null);
    }

    public static ChangePlan CaptureBounded(
        ProjectPaths paths,
        ChangePlan plan,
        long maximumSourceBytesPerFile,
        long maximumTotalSourceBytes,
        bool preserveExplicitSourceLayers = false)
    {
        if (maximumSourceBytesPerFile <= 0
            || maximumTotalSourceBytes <= 0
            || maximumSourceBytesPerFile > maximumTotalSourceBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSourceBytesPerFile));
        }

        return CaptureCore(
            paths,
            plan,
            preserveExplicitSourceLayers,
            new SourceReadBudget(maximumSourceBytesPerFile, maximumTotalSourceBytes));
    }

    private static ChangePlan CaptureCore(
        ProjectPaths paths,
        ChangePlan plan,
        bool preserveExplicitSourceLayers,
        SourceReadBudget? sourceReadBudget)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(plan);

        var diagnostics = plan.Diagnostics.ToList();
        var writes = plan.Writes
            .Select(write => CaptureWrite(
                paths,
                write,
                diagnostics,
                preserveExplicitSourceLayers,
                sourceReadBudget))
            .ToArray();

        return plan with
        {
            Writes = writes,
            Diagnostics = diagnostics,
        };
    }

    public static IReadOnlyList<ValidationDiagnostic> Validate(ProjectPaths paths, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var write in reviewedPlan.Writes)
        {
            if (string.IsNullOrWhiteSpace(write.SourceFingerprint))
            {
                diagnostics.Add(CreateStaleDiagnostic(
                    write.TargetRelativePath,
                    "Reviewed change plan does not include source-content verification."));
                continue;
            }

            if (!TryComputeFingerprint(paths, write.Sources, out var currentFingerprint, diagnostics, write.TargetRelativePath))
            {
                continue;
            }

            if (!string.Equals(write.SourceFingerprint, currentFingerprint, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateStaleDiagnostic(
                    write.TargetRelativePath,
                    "A source file changed after the change plan was reviewed."));
            }
        }

        return diagnostics;
    }

    public static bool TryAcquireApplyScope(
        ProjectPaths paths,
        ChangePlan currentPlan,
        out VerifiedApplyScope? scope,
        out IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return TryAcquireApplyScope(
            paths,
            currentPlan,
            out scope,
            out diagnostics,
            preserveExplicitSourceLayers: false);
    }

    public static bool TryAcquireApplyScope(
        ProjectPaths paths,
        ChangePlan currentPlan,
        out VerifiedApplyScope? scope,
        out IReadOnlyList<ValidationDiagnostic> diagnostics,
        bool preserveExplicitSourceLayers)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(currentPlan);

        var scopeDiagnostics = currentPlan.Diagnostics.ToList();
        var sourceStreams = new Dictionary<SourceIdentity, FileStream>();
        var currentTarget = string.Empty;
        string? snapshotRootPath = null;

        try
        {
            // Resolve configurable root links once before inspecting any source. All subsequent
            // file access uses these stable physical roots, while fingerprint metadata retains
            // the user-configured lexical paths so a reviewed plan remains comparable.
            var stablePaths = paths with
            {
                BaseRomFsPath = ResolveStableConfiguredRoot(paths.BaseRomFsPath),
                BaseExeFsPath = ResolveStableConfiguredRoot(paths.BaseExeFsPath),
                OutputRootPath = ResolveStableConfiguredRoot(paths.OutputRootPath),
            };
            var normalizedWrites = currentPlan.Writes
                .Select(write => NormalizeWrite(
                    stablePaths,
                    write,
                    preserveExplicitSourceLayers))
                .ToArray();

            foreach (var write in normalizedWrites)
            {
                currentTarget = write.TargetRelativePath;
                foreach (var source in write.Sources)
                {
                    var identity = SourceIdentity.Create(source);
                    if (identity.Layer == ProjectFileLayer.Pending || sourceStreams.ContainsKey(identity))
                    {
                        continue;
                    }

                    var sourcePath = ResolveSourcePath(stablePaths, source);
                    if (sourcePath is null)
                    {
                        scopeDiagnostics.Add(CreateReadDiagnostic(
                            write.TargetRelativePath,
                            $"Source '{source.RelativePath}' in the {source.Layer} layer does not resolve to a safe file inside its configured project root."));
                        continue;
                    }

                    if (Directory.Exists(sourcePath))
                    {
                        scopeDiagnostics.Add(CreateReadDiagnostic(
                            write.TargetRelativePath,
                            $"Required {source.Layer} source '{source.RelativePath}' is not a file."));
                        continue;
                    }

                    if (!File.Exists(sourcePath))
                    {
                        if (source.Layer != ProjectFileLayer.Generated)
                        {
                            scopeDiagnostics.Add(CreateReadDiagnostic(
                                write.TargetRelativePath,
                                $"Required {source.Layer} source '{source.RelativePath}' does not exist."));
                        }

                        continue;
                    }

                    sourceStreams.Add(identity, new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 64 * 1024,
                        FileOptions.SequentialScan));
                }
            }

            if (scopeDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                DisposeStreams(sourceStreams.Values);
                scope = null;
                diagnostics = scopeDiagnostics;
                return false;
            }

            var capturedWrites = normalizedWrites
                .Select(write => CaptureWriteFingerprint(
                    paths,
                    write,
                    sourceStreams,
                    scopeDiagnostics,
                    stablePaths))
                .ToArray();
            var capturedPlan = currentPlan with
            {
                Writes = capturedWrites,
                Diagnostics = scopeDiagnostics,
            };
            if (scopeDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                DisposeStreams(sourceStreams.Values);
                scope = null;
                diagnostics = scopeDiagnostics;
                return false;
            }

            var inputHasCapturedFingerprints = currentPlan.Writes.Count > 0
                && currentPlan.Writes.All(write => !string.IsNullOrWhiteSpace(write.SourceFingerprint));
            if (inputHasCapturedFingerprints
                && !ChangePlanReview.Matches(currentPlan, capturedPlan))
            {
                DisposeStreams(sourceStreams.Values);
                scopeDiagnostics.Add(CreateStaleDiagnostic(
                    currentTarget,
                    "A source changed while verified apply handles were being acquired."));
                scope = null;
                diagnostics = scopeDiagnostics;
                return false;
            }

            snapshotRootPath = Path.Combine(
                Path.GetTempPath(),
                "km-editor-swsh-verified-apply",
                Guid.NewGuid().ToString("N"));
            var snapshotOutputRootPath = Directory.CreateDirectory(
                Path.Combine(snapshotRootPath, "output")).FullName;
            CopyOutputSourcesToSnapshot(snapshotOutputRootPath, sourceStreams);
            var snapshotFileStates = CaptureSnapshotFileStates(snapshotOutputRootPath);

            scope = new VerifiedApplyScope(
                stablePaths,
                capturedPlan,
                stablePaths with { OutputRootPath = snapshotOutputRootPath },
                snapshotRootPath,
                snapshotOutputRootPath,
                sourceStreams,
                snapshotFileStates,
                preserveExplicitSourceLayers);
            diagnostics = scopeDiagnostics;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DisposeStreams(sourceStreams.Values);
            TryDeleteSnapshotDirectory(snapshotRootPath);
            scopeDiagnostics.Add(CreateReadDiagnostic(
                currentTarget,
                SanitizeDiagnosticText(exception.Message, snapshotRootPath)));
            scope = null;
            diagnostics = scopeDiagnostics;
            return false;
        }
    }

    private static PlannedFileWrite CaptureWrite(
        ProjectPaths paths,
        PlannedFileWrite write,
        ICollection<ValidationDiagnostic> diagnostics,
        bool preserveExplicitSourceLayers,
        SourceReadBudget? sourceReadBudget = null)
    {
        return CaptureWriteFingerprint(
            paths,
            NormalizeWrite(paths, write, preserveExplicitSourceLayers),
            null,
            diagnostics,
            sourceReadBudget: sourceReadBudget);
    }

    private static PlannedFileWrite NormalizeWrite(
        ProjectPaths paths,
        PlannedFileWrite write,
        bool preserveExplicitSourceLayers)
    {
        var normalizedSources = NormalizeSources(
            paths,
            write.Sources,
            preserveExplicitSourceLayers).ToList();
        AddAuthoritativeTargetSource(
            paths,
            write.TargetRelativePath,
            normalizedSources,
            preserveExplicitSourceLayers);
        return write with { Sources = normalizedSources };
    }

    private static PlannedFileWrite CaptureWriteFingerprint(
        ProjectPaths paths,
        PlannedFileWrite write,
        IReadOnlyDictionary<SourceIdentity, FileStream>? sourceStreams,
        ICollection<ValidationDiagnostic> diagnostics,
        ProjectPaths? sourceResolutionPaths = null,
        SourceReadBudget? sourceReadBudget = null)
    {
        return TryComputeFingerprint(
                paths,
                write.Sources,
                out var fingerprint,
                diagnostics,
                write.TargetRelativePath,
                sourceStreams,
                sourceResolutionPaths,
                sourceReadBudget)
            ? write with { SourceFingerprint = fingerprint }
            : write;
    }

    private static void AddAuthoritativeTargetSource(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ProjectFileReference> sources,
        bool preserveExplicitSourceLayers)
    {
        var normalizedTarget = NormalizeRelativePath(targetRelativePath);
        if (preserveExplicitSourceLayers
            && sources.Any(source => source.Layer == ProjectFileLayer.Generated
                && string.Equals(
                    NormalizeRelativePath(source.RelativePath),
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var effectiveLayer = ResolveEffectiveLayer(paths, normalizedTarget)
            ?? ProjectFileLayer.Generated;
        if (sources.Any(source => source.Layer == effectiveLayer
            && string.Equals(
                NormalizeRelativePath(source.RelativePath),
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        sources.Add(new ProjectFileReference(effectiveLayer, normalizedTarget));
    }

    private static IReadOnlyList<ProjectFileReference> NormalizeSources(
        ProjectPaths paths,
        IReadOnlyList<ProjectFileReference> sources,
        bool preserveExplicitSourceLayers)
    {
        var normalizedSources = new List<ProjectFileReference>(sources.Count);
        foreach (var group in sources.GroupBy(
            source => NormalizeRelativePath(source.RelativePath),
            StringComparer.OrdinalIgnoreCase))
        {
            var groupedSources = group.ToArray();
            if (!preserveExplicitSourceLayers
                && groupedSources.Length == 1
                && groupedSources[0].Layer is ProjectFileLayer.Base or ProjectFileLayer.Layered)
            {
                var effectiveLayer = ResolveEffectiveLayer(paths, groupedSources[0].RelativePath);
                normalizedSources.Add(effectiveLayer is null
                    ? groupedSources[0]
                    : groupedSources[0] with { Layer = effectiveLayer.Value });
                continue;
            }

            normalizedSources.AddRange(groupedSources);
        }

        return normalizedSources;
    }

    private static ProjectFileLayer? ResolveEffectiveLayer(ProjectPaths paths, string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        if (!IsSafeRelativePath(normalizedPath))
        {
            return null;
        }

        var layeredPath = ResolveContainedPath(paths.OutputRootPath, normalizedPath);
        if (layeredPath is not null && File.Exists(layeredPath))
        {
            return ProjectFileLayer.Layered;
        }

        var basePath = normalizedPath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            ? ResolveContainedPath(paths.BaseRomFsPath, normalizedPath["romfs/".Length..])
            : normalizedPath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
                ? ResolveContainedPath(paths.BaseExeFsPath, normalizedPath["exefs/".Length..])
                : null;

        return basePath is not null && File.Exists(basePath)
            ? ProjectFileLayer.Base
            : null;
    }

    private static bool TryComputeFingerprint(
        ProjectPaths paths,
        IReadOnlyList<ProjectFileReference> sources,
        out string fingerprint,
        ICollection<ValidationDiagnostic> diagnostics,
        string targetRelativePath,
        IReadOnlyDictionary<SourceIdentity, FileStream>? sourceStreams = null,
        ProjectPaths? sourceResolutionPaths = null,
        SourceReadBudget? sourceReadBudget = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];

        try
        {
            AppendText(hash, "swsh-change-plan-source-v2\n");
            AppendText(hash, $"game:{paths.SelectedGame}\n");
            AppendText(hash, $"base-romfs:{NormalizeRootPath(paths.BaseRomFsPath)}\n");
            AppendText(hash, $"base-exefs:{NormalizeRootPath(paths.BaseExeFsPath)}\n");
            AppendText(hash, $"output-root:{NormalizeRootPath(paths.OutputRootPath)}\n");
            foreach (var source in sources
                .OrderBy(candidate => candidate.Layer)
                .ThenBy(candidate => candidate.RelativePath, StringComparer.Ordinal))
            {
                AppendText(hash, $"{source.Layer}:{NormalizeRelativePath(source.RelativePath)}\n");
                if (sourceStreams is not null
                    && sourceStreams.TryGetValue(SourceIdentity.Create(source), out var heldStream))
                {
                    AppendStream(hash, heldStream, buffer, sourceReadBudget);
                    continue;
                }

                var sourcePath = ResolveSourcePath(sourceResolutionPaths ?? paths, source);
                if (sourcePath is null)
                {
                    if (source.Layer == ProjectFileLayer.Pending)
                    {
                        AppendText(hash, "unresolved\n");
                        continue;
                    }

                    diagnostics.Add(CreateReadDiagnostic(
                        targetRelativePath,
                        $"Source '{source.RelativePath}' in the {source.Layer} layer does not resolve to a safe file inside its configured project root."));
                    fingerprint = string.Empty;
                    return false;
                }

                if (Directory.Exists(sourcePath))
                {
                    diagnostics.Add(CreateReadDiagnostic(
                        targetRelativePath,
                        $"Required {source.Layer} source '{source.RelativePath}' is not a file."));
                    fingerprint = string.Empty;
                    return false;
                }

                if (!File.Exists(sourcePath))
                {
                    if (source.Layer == ProjectFileLayer.Generated)
                    {
                        AppendText(hash, "missing\n");
                        continue;
                    }

                    diagnostics.Add(CreateReadDiagnostic(
                        targetRelativePath,
                        $"Required {source.Layer} source '{source.RelativePath}' does not exist."));
                    fingerprint = string.Empty;
                    return false;
                }

                using var stream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    buffer.Length,
                    FileOptions.SequentialScan);
                AppendStream(hash, stream, buffer, sourceReadBudget);
            }

            fingerprint = Convert.ToHexString(hash.GetHashAndReset());
            return true;
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateReadDiagnostic(targetRelativePath, exception.Message));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateReadDiagnostic(targetRelativePath, exception.Message));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateReadDiagnostic(targetRelativePath, exception.Message));
        }

        fingerprint = string.Empty;
        return false;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileReference source)
    {
        var relativePath = NormalizeRelativePath(source.RelativePath);
        if (!IsSafeRelativePath(relativePath))
        {
            return null;
        }

        return source.Layer switch
        {
            ProjectFileLayer.Base when relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase) =>
                ResolveContainedPath(paths.BaseRomFsPath, relativePath["romfs/".Length..]),
            ProjectFileLayer.Base when relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase) =>
                ResolveContainedPath(paths.BaseExeFsPath, relativePath["exefs/".Length..]),
            ProjectFileLayer.Layered or ProjectFileLayer.Generated =>
                ResolveContainedPath(paths.OutputRootPath, relativePath),
            _ => null,
        };
    }

    private static string NormalizeRootPath(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return "<missing>";
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath))
            .Replace('\\', '/');
        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    private static string? ResolveContainedPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !IsSafeRelativePath(relativePath))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relativeToRoot = Path.GetRelativePath(fullRoot, fullPath);
        return PathContainment.IsWithinRoot(relativeToRoot)
            && !TraversesLinkBelowRoot(fullRoot, relativeToRoot)
                ? fullPath
                : null;
    }

    private static bool TraversesLinkBelowRoot(string fullRoot, string relativePath)
    {
        var currentPath = fullRoot;
        foreach (var segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            try
            {
                if (!File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (HasLinkTarget(currentPath))
                {
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLinkTarget(string path)
    {
        FileSystemInfo fileSystemInfo = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        return !string.IsNullOrWhiteSpace(fileSystemInfo.LinkTarget);
    }

    private static string? ResolveStableConfiguredRoot(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return rootPath;
        }

        var fullRoot = Path.GetFullPath(rootPath);
        var root = new DirectoryInfo(fullRoot);
        if (string.IsNullOrWhiteSpace(root.LinkTarget))
        {
            return fullRoot;
        }

        return root.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullRoot;
    }

    private static void AppendStream(
        IncrementalHash hash,
        FileStream stream,
        byte[] buffer,
        SourceReadBudget? sourceReadBudget = null)
    {
        stream.Position = 0;
        var expectedLength = stream.Length;
        sourceReadBudget?.Reserve(expectedLength);
        AppendText(hash, $"length:{expectedLength}\n");
        long totalBytesRead = 0;
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalBytesRead = checked(totalBytesRead + bytesRead);
            if (totalBytesRead > expectedLength)
            {
                throw new InvalidDataException(
                    "A change-plan source changed or exceeded its bounded length while it was fingerprinted.");
            }

            hash.AppendData(buffer, 0, bytesRead);
        }

        if (totalBytesRead != expectedLength)
        {
            throw new InvalidDataException(
                "A change-plan source changed while it was fingerprinted.");
        }

        stream.Position = 0;
        AppendText(hash, "\n");
    }

    private sealed class SourceReadBudget(long maximumBytesPerFile, long maximumTotalBytes)
    {
        private long observedBytes;

        public void Reserve(long length)
        {
            if (length < 0 || length > maximumBytesPerFile)
            {
                throw new InvalidDataException(
                    "A change-plan source exceeds its bounded per-file read limit.");
            }

            observedBytes = checked(observedBytes + length);
            if (observedBytes > maximumTotalBytes)
            {
                throw new InvalidDataException(
                    "Change-plan sources exceed their bounded aggregate read limit.");
            }
        }
    }

    private static void CopyOutputSourcesToSnapshot(
        string snapshotOutputRootPath,
        IReadOnlyDictionary<SourceIdentity, FileStream> sourceStreams)
    {
        var copiedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, stream) in sourceStreams)
        {
            if (source.Layer is not (ProjectFileLayer.Layered or ProjectFileLayer.Generated)
                || !copiedPaths.Add(source.RelativePath))
            {
                continue;
            }

            var snapshotPath = ResolveContainedPath(snapshotOutputRootPath, source.RelativePath);
            if (snapshotPath is null)
            {
                throw new IOException(
                    $"Source '{source.RelativePath}' cannot be copied into the verified apply snapshot.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            stream.Position = 0;
            using var snapshotStream = new FileStream(
                snapshotPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            stream.CopyTo(snapshotStream);
            stream.Position = 0;
        }
    }

    private static IReadOnlyDictionary<string, SnapshotFileState> CaptureSnapshotFileStates(string rootPath)
    {
        var states = new Dictionary<string, SnapshotFileState>(StringComparer.OrdinalIgnoreCase);
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);
        while (pendingDirectories.Count > 0)
        {
            var directoryPath = pendingDirectories.Pop();
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(directoryPath))
            {
                var attributes = File.GetAttributes(entryPath);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new IOException("The verified apply snapshot contains a symbolic link or junction.");
                }

                var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, entryPath));
                if (OutputMetadataNamespace.ContainsReservedSegment(relativePath))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pendingDirectories.Push(entryPath);
                    continue;
                }

                states.Add(relativePath, CaptureFileState(entryPath));
            }
        }

        return states;
    }

    private static SnapshotFileState CaptureFileState(string path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }

        return new SnapshotFileState(stream.Length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void DisposeStreams(IEnumerable<FileStream> streams)
    {
        foreach (var stream in streams)
        {
            stream.Dispose();
        }
    }

    private static void TryDeleteSnapshotDirectory(string? snapshotRootPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotRootPath))
        {
            return;
        }

        try
        {
            if (Directory.Exists(snapshotRootPath))
            {
                Directory.Delete(snapshotRootPath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup. Apply diagnostics retain the actionable I/O failure.
        }
    }

    private static string SanitizeDiagnosticText(string value, string? privateRootPath)
    {
        return string.IsNullOrWhiteSpace(privateRootPath)
            ? value
            : value.Replace(
                privateRootPath,
                "<verified apply snapshot>",
                StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record SnapshotFileState(long Length, string Fingerprint);

    internal readonly record struct SourceIdentity(ProjectFileLayer Layer, string RelativePath)
    {
        public static SourceIdentity Create(ProjectFileReference source)
        {
            return new SourceIdentity(source.Layer, NormalizeRelativePath(source.RelativePath));
        }
    }

    public sealed class VerifiedApplyScope : IDisposable
    {
        private readonly string snapshotRootPath;
        private readonly string snapshotOutputRootPath;
        private readonly Dictionary<SourceIdentity, FileStream> sourceStreams;
        private readonly IReadOnlyDictionary<string, SnapshotFileState> initialSnapshotFileStates;
        private readonly bool preserveExplicitSourceLayers;
        private bool commitAttempted;
        private bool disposed;

        internal VerifiedApplyScope(
            ProjectPaths sourcePaths,
            ChangePlan currentPlan,
            ProjectPaths applyPaths,
            string snapshotRootPath,
            string snapshotOutputRootPath,
            Dictionary<SourceIdentity, FileStream> sourceStreams,
            IReadOnlyDictionary<string, SnapshotFileState> initialSnapshotFileStates,
            bool preserveExplicitSourceLayers)
        {
            SourcePaths = sourcePaths;
            CurrentPlan = currentPlan;
            ApplyPaths = applyPaths;
            this.snapshotRootPath = snapshotRootPath;
            this.snapshotOutputRootPath = snapshotOutputRootPath;
            this.sourceStreams = sourceStreams;
            this.initialSnapshotFileStates = initialSnapshotFileStates;
            this.preserveExplicitSourceLayers = preserveExplicitSourceLayers;
        }

        public ProjectPaths SourcePaths { get; }

        public ProjectPaths ApplyPaths { get; }

        public ChangePlan CurrentPlan { get; }

        public bool TryPrepareSnapshotPlan(ChangePlan snapshotPlan, out ChangePlan preparedPlan)
        {
            ArgumentNullException.ThrowIfNull(snapshotPlan);
            ThrowIfDisposed();

            preparedPlan = Capture(
                ApplyPaths,
                snapshotPlan,
                preserveExplicitSourceLayers);
            preparedPlan = preparedPlan with
            {
                Diagnostics = preparedPlan.Diagnostics
                    .Select(SanitizeSnapshotDiagnostic)
                    .ToArray(),
            };
            return ChangePlanReview.Matches(
                WithoutFingerprints(CurrentPlan),
                WithoutFingerprints(preparedPlan));
        }

        public ApplyResult Commit(ApplyResult snapshotResult)
        {
            return Commit(snapshotResult, beforePromotion: null);
        }

        internal ApplyResult Commit(
            ApplyResult snapshotResult,
            Action<int, string>? beforePromotion)
        {
            ArgumentNullException.ThrowIfNull(snapshotResult);
            ThrowIfDisposed();
            if (commitAttempted)
            {
                throw new InvalidOperationException("Verified apply output has already been committed.");
            }

            commitAttempted = true;
            var diagnostics = snapshotResult.Diagnostics
                .Select(SanitizeSnapshotDiagnostic)
                .ToList();
            var result = snapshotResult with
            {
                Manifest = snapshotResult.Manifest with { Writes = CurrentPlan.Writes },
                Diagnostics = diagnostics,
                OutputTransaction = null,
            };
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return result with { WrittenFiles = Array.Empty<ProjectFileReference>() };
            }

            var plannedTargets = CurrentPlan.Writes
                .Select(write => NormalizeRelativePath(write.TargetRelativePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var writtenTargets = snapshotResult.WrittenFiles
                .Select(file => NormalizeRelativePath(file.RelativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            IReadOnlyDictionary<string, SnapshotFileState> finalSnapshotFileStates;
            try
            {
                finalSnapshotFileStates = CaptureSnapshotFileStates(snapshotOutputRootPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateReadDiagnostic(
                    string.Empty,
                    $"Verified staged outputs could not be inspected safely: {SanitizeDiagnosticText(exception.Message, snapshotRootPath)}"));
                return result with
                {
                    WrittenFiles = Array.Empty<ProjectFileReference>(),
                    Diagnostics = diagnostics,
                };
            }


            var changedTargets = initialSnapshotFileStates.Keys
                .Concat(finalSnapshotFileStates.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(relativePath => !SnapshotStatesMatch(
                    initialSnapshotFileStates.GetValueOrDefault(relativePath),
                    finalSnapshotFileStates.GetValueOrDefault(relativePath)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reportedTargets = writtenTargets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var undeclaredTarget = changedTargets.FirstOrDefault(target => !reportedTargets.Contains(target));
            var unplannedTarget = changedTargets.FirstOrDefault(target => !plannedTargets.Contains(target));
            var unplannedReportedTarget = reportedTargets.FirstOrDefault(target => !plannedTargets.Contains(target));
            if (undeclaredTarget is not null || unplannedTarget is not null || unplannedReportedTarget is not null)
            {
                var diagnosticPath = unplannedTarget ?? unplannedReportedTarget ?? undeclaredTarget ?? string.Empty;
                var message = unplannedTarget is not null || unplannedReportedTarget is not null
                    ? "The editor changed an output that was not present in the reviewed change plan."
                    : "The editor changed an output without truthfully reporting it.";
                diagnostics.Add(CreateReadDiagnostic(diagnosticPath, message));
                return result with
                {
                    WrittenFiles = Array.Empty<ProjectFileReference>(),
                    Diagnostics = diagnostics,
                };
            }

            result = result with
            {
                WrittenFiles = snapshotResult.WrittenFiles
                    .Where(file => changedTargets.Contains(NormalizeRelativePath(file.RelativePath)))
                    .Distinct()
                    .ToArray(),
            };

            var projectId = ProjectIdentity.FromPaths(SourcePaths);
            var outputCoordinator = new OutputTransactionCoordinator(SourcePaths.OutputRootPath!);
            OutputOwnershipInventorySnapshot? ownershipSnapshot = null;
            if (changedTargets.Any(IsComposedExeFsMainPath))
            {
                try
                {
                    ownershipSnapshot = outputCoordinator
                        .GetOwnershipInventorySnapshotAsync()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception) when (exception is
                    OutputCoordinatorException or
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException)
                {
                    diagnostics.Add(CreateReadDiagnostic(
                        ComposedExeFsMainPath,
                        $"Verified exefs/main ownership could not be captured before composition: {SanitizeDiagnosticText(exception.Message, snapshotRootPath)}"));
                    return result with
                    {
                        WrittenFiles = Array.Empty<ProjectFileReference>(),
                        Diagnostics = diagnostics,
                    };
                }
            }

            var ownershipByPath = ownershipSnapshot?.Inventory.Files.ToDictionary(
                record => record.Path.CanonicalKey,
                StringComparer.Ordinal);
            var retainedNoOpTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mutations = new List<OutputMutation>(changedTargets.Count);
            foreach (var relativePath in changedTargets.Order(StringComparer.Ordinal))
            {
                var snapshotPath = ResolveContainedPath(snapshotOutputRootPath, relativePath);
                var targetPath = ResolveContainedPath(SourcePaths.OutputRootPath, relativePath);
                if (snapshotPath is null || targetPath is null)
                {
                    diagnostics.Add(CreateReadDiagnostic(
                        relativePath,
                        "The verified output target does not resolve safely inside Output Root."));
                    return result with
                    {
                        WrittenFiles = Array.Empty<ProjectFileReference>(),
                        Diagnostics = diagnostics,
                    };
                }

                if (Directory.Exists(snapshotPath))
                {
                    diagnostics.Add(CreateReadDiagnostic(
                        relativePath,
                        "The verified output target is a directory instead of a file."));
                    return result with
                    {
                        WrittenFiles = Array.Empty<ProjectFileReference>(),
                        Diagnostics = diagnostics,
                    };
                }

                if (!OutputPreimageMatches(relativePath, targetPath))
                {
                    diagnostics.Add(CreateStaleDiagnostic(
                        relativePath,
                        "An Output Root target changed while the verified edit was being prepared."));
                    return result with
                    {
                        WrittenFiles = Array.Empty<ProjectFileReference>(),
                        Diagnostics = diagnostics,
                    };
                }

                try
                {
                    var path = new RelativeOutputPath(relativePath);
                    var expectedPreimage = initialSnapshotFileStates.TryGetValue(relativePath, out var initialState)
                        ? OutputFileState.Existing(initialState.Fingerprint, initialState.Length)
                        : OutputFileState.Missing;
                    var ownership = new OwnedTarget(
                        GameFamily.SwordShield,
                        new OwnedTargetAddress(path),
                        new OwnershipOwnerId(OutputOwner),
                        new PreservationRuleDescriptor(
                            OutputPreservationRule,
                            schemaVersion: 1,
                            preservesUnownedData: true,
                            requiresPreimage: true));
                    var isComposedExeFsMain = IsComposedExeFsMainPath(relativePath);
                    OutputOwnershipRecord? existingOwnership = null;
                    IReadOnlyCollection<OwnedTarget> ownershipClaims = [ownership];
                    if (isComposedExeFsMain
                        && ownershipByPath is not null
                        && ownershipByPath.TryGetValue(path.CanonicalKey, out existingOwnership))
                    {
                        if (existingOwnership.ProjectId != projectId
                            || existingOwnership.GameFamily != GameFamily.SwordShield
                            || !string.Equals(existingOwnership.OutputMode, OutputMode, StringComparison.Ordinal))
                        {
                            diagnostics.Add(CreateReadDiagnostic(
                                relativePath,
                                "Verified exefs/main composition found ownership from a different project or output scope."));
                            return result with
                            {
                                WrittenFiles = Array.Empty<ProjectFileReference>(),
                                Diagnostics = diagnostics,
                            };
                        }

                        if (existingOwnership.CurrentState != expectedPreimage)
                        {
                            diagnostics.Add(CreateStaleDiagnostic(
                                relativePath,
                                "Verified exefs/main ownership no longer matches the exact effective preimage."));
                            return result with
                            {
                                WrittenFiles = Array.Empty<ProjectFileReference>(),
                                Diagnostics = diagnostics,
                            };
                        }

                        ownershipClaims = existingOwnership.Claims
                            .Append(ownership)
                            .Distinct()
                            .ToArray();
                    }

                    if (File.Exists(snapshotPath))
                    {
                        mutations.Add(OutputMutation.Write(
                            path,
                            File.ReadAllBytes(snapshotPath),
                            expectedPreimage,
                            ownershipClaims,
                            ownershipActor: isComposedExeFsMain
                                ? ownership.OwnerId
                                : null));
                    }
                    else if (isComposedExeFsMain && existingOwnership is not null)
                    {
                        var foreignClaims = existingOwnership.Claims
                            .Where(claim => claim.OwnerId != ownership.OwnerId)
                            .ToArray();
                        var activeForeignClaims = foreignClaims
                            .Where(claim => !OutputCreatorProvenance.IsClaim(claim))
                            .ToArray();
                        var canDeleteExactOwnedFile = activeForeignClaims.Length == 0
                            && foreignClaims.Length == 0
                            && existingOwnership.FileDeleteEligible
                            && existingOwnership.Claims.Any(claim =>
                                claim.Address.ScopeKind == OwnedTargetScopeKind.File);
                        if (canDeleteExactOwnedFile)
                        {
                            mutations.Add(OutputMutation.Delete(
                                path,
                                expectedPreimage,
                                existingOwnership.Claims));
                        }
                        else if (activeForeignClaims.Length > 0)
                        {
                            diagnostics.Add(CreateReadDiagnostic(
                                relativePath,
                                "Verified exefs/main composition refused to remove a target that still has ownership claims from another editor."));
                            return result with
                            {
                                WrittenFiles = Array.Empty<ProjectFileReference>(),
                                Diagnostics = diagnostics,
                            };
                        }
                        else if (!TryReadVerifiedBaseSource(relativePath, out var restoredFallback))
                        {
                            diagnostics.Add(CreateReadDiagnostic(
                                relativePath,
                                "Verified exefs/main composition requires the held vanilla base source before a retained-claim delete can become a fallback write."));
                            return result with
                            {
                                WrittenFiles = Array.Empty<ProjectFileReference>(),
                                Diagnostics = diagnostics,
                            };
                        }
                        else if (foreignClaims.Length > 0)
                        {
                            var baseState = OutputFileState.Existing(
                                Convert.ToHexStringLower(SHA256.HashData(restoredFallback)),
                                restoredFallback.LongLength);
                            var authority = new OutputVerifiedBaseDeleteAuthority(
                                projectId,
                                GameFamily.SwordShield,
                                ownership.OwnerId,
                                OutputMode,
                                path,
                                expectedPreimage,
                                baseState,
                                existingOwnership.Claims);
                            mutations.Add(OutputMutation.DeleteVerifiedBase(
                                path,
                                expectedPreimage,
                                existingOwnership.Claims,
                                authority));
                        }
                        else
                        {
                            var fallbackState = OutputFileState.Existing(
                                Convert.ToHexStringLower(SHA256.HashData(restoredFallback)),
                                restoredFallback.LongLength);
                            if (fallbackState == expectedPreimage)
                            {
                                retainedNoOpTargets.Add(relativePath);
                            }
                            else
                            {
                                mutations.Add(OutputMutation.Write(
                                    path,
                                    restoredFallback,
                                    expectedPreimage,
                                    existingOwnership.Claims,
                                    ownershipActor: ownership.OwnerId));
                            }
                        }
                    }
                    else if (isComposedExeFsMain)
                    {
                        diagnostics.Add(CreateReadDiagnostic(
                            relativePath,
                            "Verified exefs/main composition refused to delete an unmanaged executable without an ownership-backed restoration proof."));
                        return result with
                        {
                            WrittenFiles = Array.Empty<ProjectFileReference>(),
                            Diagnostics = diagnostics,
                        };
                    }
                    else
                    {
                        var adoptionAuthority = new OutputLegacyAdoptionDeleteAuthority(
                            projectId,
                            GameFamily.SwordShield,
                            OutputMode,
                            path,
                            ownership.OwnerId,
                            ownership.PreservationRule,
                            expectedPreimage);
                        mutations.Add(OutputMutation.DeleteLegacyAdoption(
                            path,
                            expectedPreimage,
                            [ownership],
                            adoptionAuthority));
                    }
                }
                catch (Exception exception) when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    ArgumentException or
                    OverflowException)
                {
                    diagnostics.Add(CreateReadDiagnostic(
                        relativePath,
                        $"Verified output could not be prepared for the shared transaction: {SanitizeDiagnosticText(exception.Message, snapshotRootPath)}"));
                    return result with
                    {
                        WrittenFiles = Array.Empty<ProjectFileReference>(),
                        Diagnostics = diagnostics,
                    };
                }
            }

            if (retainedNoOpTargets.Count > 0)
            {
                result = result with
                {
                    WrittenFiles = result.WrittenFiles
                        .Where(file => !retainedNoOpTargets.Contains(
                            NormalizeRelativePath(file.RelativePath)))
                        .ToArray(),
                };
            }

            if (mutations.Count == 0)
            {
                return result;
            }

            try
            {
                for (var index = 0; index < mutations.Count; index++)
                {
                    var mutation = mutations[index];
                    ReleaseOutputSourceStream(mutation.Path.Value);
                    beforePromotion?.Invoke(index, mutation.Path.Value);
                }

                var origins = mutations
                    .Select(mutation => mutation.OwnershipActor)
                    .Where(actor => actor is not null)
                    .Select(actor => new OutputApplyOrigin(
                        OutputApplyOriginKind.Workflow,
                        actor!.Value))
                    .Concat(mutations
                        .Select(mutation => mutation.VerifiedBaseDeleteAuthority?.ActingOwnerId)
                        .Where(actor => actor is not null)
                        .Select(actor => new OutputApplyOrigin(
                            OutputApplyOriginKind.Workflow,
                            actor!.Value)))
                    .Append(new OutputApplyOrigin(OutputApplyOriginKind.Workflow, OutputOrigin))
                    .Distinct()
                    .ToArray();
                var plan = new OutputApplyPlan(
                    projectId,
                    GameFamily.SwordShield,
                    OutputMode,
                    OutputReviewFingerprint.FromChangePlan(CurrentPlan),
                    origins,
                    mutations,
                    ownershipInventoryRevision: ownershipSnapshot?.Revision);
                var outputResult = outputCoordinator
                    .ApplyAsync(plan)
                    .GetAwaiter()
                    .GetResult();
                if (outputResult.Outcome == OutputApplyOutcome.Committed)
                {
                    return result with { OutputTransaction = outputResult };
                }

                diagnostics.Add(CreateReadDiagnostic(
                    string.Empty,
                    outputResult.Outcome == OutputApplyOutcome.RecoveryRequired
                        ? "Verified output transaction requires recovery before another write can begin."
                        : "Verified output transaction did not commit and all promoted targets were rolled back.") with
                {
                    Code = outputResult.Receipt.OutcomeCode,
                });
                return result with
                {
                    WrittenFiles = Array.Empty<ProjectFileReference>(),
                    Diagnostics = diagnostics,
                    OutputTransaction = outputResult,
                };
            }
            catch (Exception exception) when (exception is
                OutputCoordinatorException or
                IOException or
                UnauthorizedAccessException or
                ArgumentException or
                InvalidOperationException)
            {
                diagnostics.Add(CreateReadDiagnostic(
                    string.Empty,
                    $"Verified outputs could not be committed through the shared output transaction: {SanitizeDiagnosticText(exception.Message, snapshotRootPath)}"));
                return result with
                {
                    WrittenFiles = Array.Empty<ProjectFileReference>(),
                    Diagnostics = diagnostics,
                };
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DisposeStreams(sourceStreams.Values);
            sourceStreams.Clear();
            TryDeleteSnapshotDirectory(snapshotRootPath);
        }

        private bool TryReadVerifiedBaseSource(string relativePath, out byte[] contents)
        {
            var identity = new SourceIdentity(
                ProjectFileLayer.Base,
                NormalizeRelativePath(relativePath));
            if (!sourceStreams.TryGetValue(identity, out var stream)
                || stream.Length > int.MaxValue)
            {
                contents = [];
                return false;
            }

            contents = new byte[checked((int)stream.Length)];
            stream.Position = 0;
            stream.ReadExactly(contents);
            stream.Position = 0;
            return true;
        }

        private bool OutputPreimageMatches(string relativePath, string targetPath)
        {
            var resolvedTargetPath = ResolveContainedPath(SourcePaths.OutputRootPath, relativePath);
            if (resolvedTargetPath is null
                || !string.Equals(resolvedTargetPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!initialSnapshotFileStates.TryGetValue(relativePath, out var expectedState))
            {
                return !File.Exists(targetPath) && !Directory.Exists(targetPath);
            }

            if (!File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                return false;
            }

            try
            {
                return expectedState == CaptureFileState(targetPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void ReleaseOutputSourceStream(string relativePath)
        {
            foreach (var source in sourceStreams.Keys
                .Where(source => source.Layer is ProjectFileLayer.Layered or ProjectFileLayer.Generated
                    && string.Equals(source.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                .ToArray())
            {
                sourceStreams[source].Dispose();
                sourceStreams.Remove(source);
            }
        }

        private static bool SnapshotStatesMatch(
            SnapshotFileState? first,
            SnapshotFileState? second)
        {
            return first == second;
        }

        private static ChangePlan WithoutFingerprints(ChangePlan plan)
        {
            return plan with
            {
                Writes = plan.Writes
                    .Select(write => write with { SourceFingerprint = null })
                    .ToArray(),
            };
        }

        private ValidationDiagnostic SanitizeSnapshotDiagnostic(ValidationDiagnostic diagnostic)
        {
            return diagnostic with
            {
                Message = SanitizeDiagnosticText(diagnostic.Message, snapshotRootPath),
                File = diagnostic.File is null
                    ? null
                    : SanitizeDiagnosticText(diagnostic.File, snapshotRootPath),
                Expected = diagnostic.Expected is null
                    ? null
                    : SanitizeDiagnosticText(diagnostic.Expected, snapshotRootPath),
            };
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

    }

    private static bool IsComposedExeFsMainPath(string relativePath)
    {
        return string.Equals(
            NormalizeRelativePath(relativePath),
            ComposedExeFsMainPath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        return !string.IsNullOrWhiteSpace(relativePath)
            && !Path.IsPathRooted(relativePath)
            && relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .All(segment => !string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
    }

    private static ValidationDiagnostic CreateStaleDiagnostic(string targetRelativePath, string message)
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Error,
            $"Reviewed change plan is stale. {message}",
            File: targetRelativePath,
            Domain: DiagnosticDomain,
            Expected: "Review the current source files and apply the new change plan");
    }

    private static ValidationDiagnostic CreateReadDiagnostic(string targetRelativePath, string message)
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Error,
            $"Change-plan source verification failed: {message}",
            File: targetRelativePath,
            Domain: DiagnosticDomain,
            Expected: "Readable source files matching the reviewed change plan");
    }
}
