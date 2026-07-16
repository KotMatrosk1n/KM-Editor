// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;

namespace KM.SwSh.Editing;

public static class SwShChangePlanSourceGuard
{
    private const string DiagnosticDomain = "workflow.changePlan";

    public static ChangePlan Capture(ProjectPaths paths, ChangePlan plan)
    {
        return Capture(paths, plan, preserveExplicitSourceLayers: false);
    }

    public static ChangePlan Capture(
        ProjectPaths paths,
        ChangePlan plan,
        bool preserveExplicitSourceLayers)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(plan);

        var diagnostics = plan.Diagnostics.ToList();
        var writes = plan.Writes
            .Select(write => CaptureWrite(
                paths,
                write,
                diagnostics,
                preserveExplicitSourceLayers))
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
        bool preserveExplicitSourceLayers)
    {
        return CaptureWriteFingerprint(
            paths,
            NormalizeWrite(paths, write, preserveExplicitSourceLayers),
            null,
            diagnostics);
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
        ProjectPaths? sourceResolutionPaths = null)
    {
        return TryComputeFingerprint(
                paths,
                write.Sources,
                out var fingerprint,
                diagnostics,
                write.TargetRelativePath,
                sourceStreams,
                sourceResolutionPaths)
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
        ProjectPaths? sourceResolutionPaths = null)
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
                    AppendStream(hash, heldStream, buffer);
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
                AppendStream(hash, stream, buffer);
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

    private static void AppendStream(IncrementalHash hash, FileStream stream, byte[] buffer)
    {
        stream.Position = 0;
        AppendText(hash, $"length:{stream.Length}\n");
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }

        stream.Position = 0;
        AppendText(hash, "\n");
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

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pendingDirectories.Push(entryPath);
                    continue;
                }

                var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, entryPath));
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

            var commits = new List<VerifiedOutputCommit>(changedTargets.Count);
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

                commits.Add(new VerifiedOutputCommit(
                    relativePath,
                    snapshotPath,
                    targetPath,
                    StagingPath: null,
                    ExpectedMissing: !initialSnapshotFileStates.ContainsKey(relativePath),
                    CreatedDirectories: Array.Empty<string>()));
            }

            try
            {
                for (var index = 0; index < commits.Count; index++)
                {
                    var commit = commits[index];
                    commits[index] = PrepareOutputCommit(
                        commit.RelativePath,
                        commit.SnapshotPath,
                        commit.TargetPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var cleanupFailed = false;
                if (exception is VerifiedOutputPreparationException preparationException)
                {
                    cleanupFailed |= !preparationException.Commit.TryDeleteStagingArtifacts();
                }

                foreach (var commit in commits.AsEnumerable().Reverse())
                {
                    cleanupFailed |= !commit.TryDeleteStagingArtifacts();
                }

                diagnostics.Add(CreateReadDiagnostic(
                    string.Empty,
                    $"Verified outputs could not be prepared for commit: {SanitizeDiagnosticText(exception.Message, snapshotRootPath)}"));
                if (cleanupFailed)
                {
                    diagnostics.Add(CreateReadDiagnostic(
                        string.Empty,
                        "Temporary verified output staging artifacts could not be deleted."));
                }

                return result with
                {
                    WrittenFiles = Array.Empty<ProjectFileReference>(),
                    Diagnostics = diagnostics,
                };
            }

            var completedRollbacks = new List<SwShOutputRollbackScope>();
            var activeRelativePath = string.Empty;
            try
            {
                for (var index = 0; index < commits.Count; index++)
                {
                    var commit = commits[index];
                    activeRelativePath = commit.RelativePath;
                    if (!OutputPreimageMatches(commit.RelativePath, commit.TargetPath))
                    {
                        throw new IOException("The Output Root target changed before verified promotion.");
                    }

                    if (!SwShOutputRollbackScope.TryCapture(
                        SourcePaths,
                        [commit.RelativePath],
                        out var rollbackScope,
                        out var captureFailure))
                    {
                        throw new IOException(
                            $"The current Output Root target could not be snapshotted before promotion: {captureFailure?.Message ?? "Unknown snapshot error."}");
                    }

                    ReleaseOutputSourceStream(commit.RelativePath);
                    try
                    {
                        // The source handle must be released for an atomic replacement on Windows.
                        // Recheck the full preimage immediately afterward so a late replacement is
                        // preserved instead of being overwritten or restored from our snapshot.
                        beforePromotion?.Invoke(index, commit.RelativePath);
                        if (!OutputPreimageMatches(commit.RelativePath, commit.TargetPath))
                        {
                            throw new IOException("The Output Root target changed before verified promotion.");
                        }

                        CommitOutput(commit);
                        completedRollbacks.Add(rollbackScope!);
                    }
                    catch
                    {
                        // Every promotion primitive is atomic. A failed primitive has not changed this
                        // target, so discard its snapshot instead of deleting a concurrent collision.
                        rollbackScope!.Commit();
                        rollbackScope.Dispose();
                        throw;
                    }
                }

                foreach (var rollback in completedRollbacks)
                {
                    rollback.Commit();
                    rollback.Dispose();
                }

                completedRollbacks.Clear();
                return result;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateReadDiagnostic(
                    activeRelativePath,
                    $"Verified outputs could not be committed: {SanitizeDiagnosticText(exception.Message, snapshotRootPath)}"));
                var rollbackFailures = new List<SwShOutputRollbackFailure>();
                foreach (var rollback in completedRollbacks.AsEnumerable().Reverse())
                {
                    rollbackFailures.AddRange(rollback.Rollback());
                    rollback.Dispose();
                }

                completedRollbacks.Clear();
                foreach (var failure in rollbackFailures)
                {
                    diagnostics.Add(CreateReadDiagnostic(
                        failure.RelativePath,
                        $"Verified output rollback failed: {failure.Message}"));
                }

                return result with
                {
                    WrittenFiles = rollbackFailures
                        .Where(failure => !string.IsNullOrWhiteSpace(failure.RelativePath))
                        .Select(failure => new ProjectFileReference(
                            ProjectFileLayer.Generated,
                            failure.RelativePath))
                        .Distinct()
                        .ToArray(),
                    Diagnostics = diagnostics,
                };
            }
            finally
            {
                var cleanupFailed = false;
                foreach (var commit in commits.AsEnumerable().Reverse())
                {
                    cleanupFailed |= !commit.TryDeleteStagingArtifacts();
                }

                if (cleanupFailed)
                {
                    diagnostics.Add(CreateReadDiagnostic(
                        activeRelativePath,
                        "Temporary verified output staging artifacts could not be deleted."));
                }
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

        private VerifiedOutputCommit PrepareOutputCommit(
            string relativePath,
            string snapshotPath,
            string targetPath)
        {
            string? stagingPath = null;
            var createdDirectories = new List<string>();
            if (File.Exists(snapshotPath))
            {
                var targetDirectory = Path.GetDirectoryName(targetPath)!;
                var currentDirectory = targetDirectory;
                while (!Directory.Exists(currentDirectory)
                    && PathContainment.IsWithinRoot(Path.GetRelativePath(SourcePaths.OutputRootPath!, currentDirectory)))
                {
                    createdDirectories.Add(currentDirectory);
                    currentDirectory = Path.GetDirectoryName(currentDirectory)!;
                }

                try
                {
                    Directory.CreateDirectory(targetDirectory);
                    stagingPath = Path.Combine(
                        targetDirectory,
                        $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.km-verified.tmp");
                    File.Copy(snapshotPath, stagingPath, overwrite: false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new VerifiedOutputPreparationException(
                        new VerifiedOutputCommit(
                            relativePath,
                            snapshotPath,
                            targetPath,
                            stagingPath,
                            ExpectedMissing: !initialSnapshotFileStates.ContainsKey(relativePath),
                            createdDirectories),
                        exception);
                }
            }

            return new VerifiedOutputCommit(
                relativePath,
                snapshotPath,
                targetPath,
                stagingPath,
                ExpectedMissing: !initialSnapshotFileStates.ContainsKey(relativePath),
                createdDirectories);
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

        private static void CommitOutput(VerifiedOutputCommit commit)
        {
            if (!File.Exists(commit.SnapshotPath))
            {
                if (Directory.Exists(commit.TargetPath))
                {
                    throw new IOException($"Output target '{commit.RelativePath}' is a directory and cannot be deleted as a file.");
                }

                File.Delete(commit.TargetPath);
                return;
            }

            if (Directory.Exists(commit.TargetPath))
            {
                throw new IOException($"Output target '{commit.RelativePath}' is a directory and cannot be replaced as a file.");
            }

            File.Move(
                commit.StagingPath!,
                commit.TargetPath,
                overwrite: !commit.ExpectedMissing);
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

        private sealed record VerifiedOutputCommit(
            string RelativePath,
            string SnapshotPath,
            string TargetPath,
            string? StagingPath,
            bool ExpectedMissing,
            IReadOnlyList<string> CreatedDirectories)
        {
            public bool TryDeleteStagingArtifacts()
            {
                return VerifiedApplyScope.TryDeleteStagingArtifacts(StagingPath, CreatedDirectories);
            }
        }

        private sealed class VerifiedOutputPreparationException(
            VerifiedOutputCommit commit,
            Exception innerException)
            : IOException(innerException.Message, innerException)
        {
            public VerifiedOutputCommit Commit { get; } = commit;
        }

        private static bool TryDeleteStagingArtifacts(
            string? stagingPath,
            IEnumerable<string> createdDirectories)
        {
            var succeeded = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(stagingPath) && File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                succeeded = false;
            }

            foreach (var directory in createdDirectories)
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    succeeded = false;
                }
            }

            return succeeded;
        }
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
