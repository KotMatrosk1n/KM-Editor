// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.ZA.Workflows;

/// <summary>
/// Adds fresh source, effective-layer, target, descriptor, and output-mode
/// evidence to every Z-A write while preserving richer workflow fingerprints.
/// </summary>
internal static class ZaChangePlanSourceGuard
{
    private const string SourceBindingFingerprintVersion =
        "KM.ZA.ChangePlanSourceGuard.Binding.v1";
    private const string RichSourceBindingFingerprintVersion =
        "KM.ZA.ChangePlanSourceGuard.RichBinding.v1";
    private const string ExistingSourceBindingFingerprintVersion =
        "KM.ZA.ChangePlanSourceGuard.ExistingBinding.v1";
    private const string ReviewFingerprintVersion =
        "KM.ZA.ChangePlanSourceGuard.Review.v1";

    public static ChangePlan Capture(
        ProjectPaths paths,
        EditSession session,
        Func<ChangePlan> createPlan,
        ZaOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(createPlan);
        return CaptureCore(
            paths,
            session,
            _ => createPlan(),
            outputMode,
            normalizeSession: null);
    }

    public static ChangePlan Capture(
        ProjectPaths paths,
        EditSession session,
        Func<EditSession, ChangePlan> createPlan,
        ZaOutputMode outputMode,
        Func<EditSession, EditSession> normalizeSession)
    {
        ArgumentNullException.ThrowIfNull(createPlan);
        ArgumentNullException.ThrowIfNull(normalizeSession);
        return CaptureCore(paths, session, createPlan, outputMode, normalizeSession);
    }

    private static ChangePlan CaptureCore(
        ProjectPaths paths,
        EditSession session,
        Func<EditSession, ChangePlan> createPlan,
        ZaOutputMode outputMode,
        Func<EditSession, EditSession>? normalizeSession)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(createPlan);

        ChangePlan? plan = null;
        try
        {
            using var outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
            using var freshReads = ZaWorkflowFileSource.BeginFreshReadScope(paths);
            var effectiveSession = WithoutPendingEditAssociations(
                normalizeSession?.Invoke(session) ?? session);
            plan = createPlan(effectiveSession);
            if (plan.CanApply || IsAuthenticatedSourceSatisfiedNoOp(session, effectiveSession, plan))
            {
                plan = plan with
                {
                    EffectivePendingEdits = effectiveSession.PendingEdits,
                };
            }

            if (!plan.CanApply || plan.Writes.Count == 0)
            {
                return plan;
            }

            var fileSource = new ZaWorkflowFileSource(bypassReusableBaseCache: true);
            var sourceSnapshots = new Dictionary<SourceSnapshotKey, SourceSnapshot>();
            var effectiveSnapshots = new Dictionary<string, SourceSnapshot>(StringComparer.Ordinal);
            var descriptorPreview = CreateDescriptorPreviewIfNeeded(paths, plan.Writes, outputMode);
            var writes = plan.Writes
                .Select(write => CaptureWrite(
                    paths,
                    effectiveSession,
                    write,
                    fileSource,
                    sourceSnapshots,
                    effectiveSnapshots,
                    descriptorPreview,
                    outputMode))
                .ToArray();

            return plan with { Writes = writes };
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException or
            CryptographicException)
        {
            var diagnostics = plan?.Diagnostics.ToList() ?? [];
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon Legends Z-A change-plan source verification could not read the current sources or output preimages.",
                "za.editor",
                expected: "Readable current semantic sources and output preimages"));
            return new ChangePlan(
                plan?.SessionId ?? session.Id,
                Array.Empty<PlannedFileWrite>(),
                diagnostics)
            {
                GeneratedSourceBindingFingerprint =
                    plan?.GeneratedSourceBindingFingerprint,
            };
        }
    }

    internal static EditSession WithoutPendingEditAssociations(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session with
        {
            PendingEdits = session.PendingEdits
                .Select(edit => edit with { Association = null })
                .ToArray(),
        };
    }

    private static bool IsAuthenticatedSourceSatisfiedNoOp(
        EditSession requestedSession,
        EditSession effectiveSession,
        ChangePlan plan)
    {
        if (requestedSession.PendingEdits.Count == 0
            || effectiveSession.PendingEdits.Count != 0
            || plan.Writes.Count != 0)
        {
            return false;
        }

        var requestedDomains = requestedSession.PendingEdits
            .Select(edit => edit.Domain)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var errors = plan.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        return requestedDomains is [var domain]
            && !string.IsNullOrWhiteSpace(domain)
            && errors is [var error]
            && string.Equals(error.Domain, domain, StringComparison.Ordinal)
            && error.Message.StartsWith("Create a pending ", StringComparison.Ordinal)
            && error.Message.EndsWith(
                " edit before reviewing a change plan.",
                StringComparison.Ordinal)
            && error.Expected?.StartsWith("Pending ", StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Verifies a workflow's freshly captured semantic fingerprint against the
    /// source-only evidence retained on a guarded write. This preserves the
    /// workflow-specific check at the final read boundary without comparing it
    /// to the wire-visible review fingerprint, which also contains edit intent.
    /// </summary>
    internal static bool MatchesCoreSourceFingerprint(
        ProjectPaths paths,
        ChangePlan plan,
        PlannedFileWrite write,
        ZaOutputMode outputMode,
        string coreSourceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(write);
        if (!IsSha256(write.SourceBindingFingerprint)
            || !IsSha256(coreSourceFingerprint))
        {
            return false;
        }

        try
        {
            using var outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
            using var freshReads = ZaWorkflowFileSource.BeginIndependentFreshReadScope(paths);
            var fileSource = new ZaWorkflowFileSource(bypassReusableBaseCache: true);
            var descriptorPreview = CreateDescriptorPreviewIfNeeded(
                paths,
                plan.Writes,
                outputMode);
            var expectedBinding = CaptureSourceBindingFingerprint(
                paths,
                write with
                {
                    SourceFingerprint = coreSourceFingerprint,
                    SourceBindingFingerprint = null,
                },
                fileSource,
                new Dictionary<SourceSnapshotKey, SourceSnapshot>(),
                new Dictionary<string, SourceSnapshot>(StringComparer.Ordinal),
                descriptorPreview,
                outputMode,
                session: null);
            return string.Equals(
                write.SourceBindingFingerprint,
                expectedBinding.BindingFingerprint,
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException or
            CryptographicException)
        {
            return false;
        }
    }

    private static PlannedFileWrite CaptureWrite(
        ProjectPaths paths,
        EditSession session,
        PlannedFileWrite write,
        ZaWorkflowFileSource fileSource,
        IDictionary<SourceSnapshotKey, SourceSnapshot> sourceSnapshots,
        IDictionary<string, SourceSnapshot> effectiveSnapshots,
        DescriptorPreview? descriptorPreview,
        ZaOutputMode outputMode)
    {
        var sourceBinding = CaptureSourceBindingFingerprint(
            paths,
            write,
            fileSource,
            sourceSnapshots,
            effectiveSnapshots,
            descriptorPreview,
            outputMode,
            session);
        return write with
        {
            SourceFingerprint = CreateReviewedFingerprint(
                sourceBinding.BindingFingerprint,
                sourceBinding.BoundaryFingerprint,
                session),
            SourceBindingFingerprint = sourceBinding.BindingFingerprint,
        };
    }

    private static SourceBindingCapture CaptureSourceBindingFingerprint(
        ProjectPaths paths,
        PlannedFileWrite write,
        ZaWorkflowFileSource fileSource,
        IDictionary<SourceSnapshotKey, SourceSnapshot> sourceSnapshots,
        IDictionary<string, SourceSnapshot> effectiveSnapshots,
        DescriptorPreview? descriptorPreview,
        ZaOutputMode outputMode,
        EditSession? session)
    {
        if (!string.IsNullOrWhiteSpace(write.SourceFingerprint)
            && !IsSha256(write.SourceFingerprint))
        {
            throw new InvalidDataException(
                $"The planned write for '{write.TargetRelativePath}' has a malformed source fingerprint.");
        }
        if (!string.IsNullOrWhiteSpace(write.SourceBindingFingerprint)
            && !IsSha256(write.SourceBindingFingerprint))
        {
            throw new InvalidDataException(
                $"The planned write for '{write.TargetRelativePath}' has a malformed source-binding fingerprint.");
        }

        var target = new RelativeOutputPath(write.TargetRelativePath);
        using var boundaryHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(boundaryHash, SourceBindingFingerprintVersion);
        AppendText(boundaryHash, paths.SelectedGame?.ToString());
        AppendText(boundaryHash, outputMode.ToString());
        AppendText(boundaryHash, NormalizeRoot(paths.BaseRomFsPath));
        AppendText(boundaryHash, NormalizeRoot(paths.BaseExeFsPath));
        AppendText(boundaryHash, NormalizeRoot(paths.OutputRootPath));
        AppendText(boundaryHash, target.CanonicalKey);
        AppendText(boundaryHash, write.ReplacesExistingOutput ? "replace" : "create");

        var orderedSources = write.Sources
            .OrderBy(source => source.Layer)
            .ThenBy(
                source => new RelativeOutputPath(source.RelativePath).CanonicalKey,
                StringComparer.Ordinal)
            .ToArray();
        AppendInt32(boundaryHash, orderedSources.Length);
        foreach (var source in orderedSources)
        {
            var sourcePath = new RelativeOutputPath(source.RelativePath);
            AppendText(boundaryHash, source.Layer.ToString());
            AppendText(boundaryHash, sourcePath.CanonicalKey);
            var key = new SourceSnapshotKey(source.Layer, sourcePath.CanonicalKey);
            if (!sourceSnapshots.TryGetValue(key, out var snapshot))
            {
                snapshot = CaptureSourceSnapshot(paths, source, fileSource);
                sourceSnapshots.Add(key, snapshot);
            }

            AppendText(boundaryHash, "declared-source");
            AppendSnapshot(boundaryHash, snapshot);
            if (source.Layer == ProjectFileLayer.Pending)
            {
                continue;
            }

            if (!effectiveSnapshots.TryGetValue(sourcePath.CanonicalKey, out var effectiveSnapshot))
            {
                effectiveSnapshot = CaptureEffectiveSource(paths, sourcePath.Value, fileSource);
                effectiveSnapshots.Add(sourcePath.CanonicalKey, effectiveSnapshot);
            }

            AppendText(boundaryHash, "effective-source");
            AppendSnapshot(boundaryHash, effectiveSnapshot);
        }

        AppendText(boundaryHash, "authoritative-target-source");
        AppendSnapshot(
            boundaryHash,
            CaptureEffectiveSource(paths, ToSemanticSourcePath(target.Value), fileSource));
        AppendOutputTargetState(boundaryHash, paths, write.TargetRelativePath);
        if (descriptorPreview is not null
            && string.Equals(
                new RelativeOutputPath(write.TargetRelativePath).CanonicalKey,
                descriptorPreview.TargetKey,
                StringComparison.Ordinal))
        {
            AppendText(boundaryHash, "standalone-descriptor-preview");
            AppendSnapshot(
                boundaryHash,
                SourceSnapshot.FromBytes("descriptor-preview", descriptorPreview.Bytes));
        }

        var genericBoundaryFingerprint = Convert.ToHexStringLower(
            boundaryHash.GetHashAndReset());
        var sourceBindingFingerprint = !string.IsNullOrWhiteSpace(write.SourceBindingFingerprint)
            ? session is not null
                && string.Equals(
                    write.SourceFingerprint,
                    CreateReviewedFingerprint(
                        write.SourceBindingFingerprint!,
                        genericBoundaryFingerprint,
                        session),
                    StringComparison.Ordinal)
                    ? write.SourceBindingFingerprint!
                    : CombineFingerprints(
                        ExistingSourceBindingFingerprintVersion,
                        genericBoundaryFingerprint,
                        write.SourceBindingFingerprint!)
            : string.IsNullOrWhiteSpace(write.SourceFingerprint)
                ? genericBoundaryFingerprint
                : CombineFingerprints(
                    RichSourceBindingFingerprintVersion,
                    genericBoundaryFingerprint,
                    write.SourceFingerprint!);
        return new SourceBindingCapture(
            genericBoundaryFingerprint,
            sourceBindingFingerprint);
    }

    private static SourceSnapshot CaptureSourceSnapshot(
        ProjectPaths paths,
        ProjectFileReference source,
        ZaWorkflowFileSource fileSource)
    {
        if (source.Layer == ProjectFileLayer.Pending)
        {
            return SourceSnapshot.Empty("pending-intent");
        }

        if (IsExeFsPath(source.RelativePath))
        {
            return CaptureExeFsSource(paths, source);
        }

        try
        {
            if (source.Layer == ProjectFileLayer.Base)
            {
                return SourceSnapshot.FromBytes(
                    "base-present",
                    fileSource.ReadBaseBytesFresh(paths, source.RelativePath));
            }

            var current = fileSource.ReadCurrentSourceFresh(paths, source.RelativePath);
            return SourceSnapshot.FromBytes(
                $"current-{current.Layer}",
                current.Bytes);
        }
        catch (Exception exception) when (IsMissingFile(exception))
        {
            return SourceSnapshot.Empty("missing");
        }
    }

    private static SourceSnapshot CaptureExeFsSource(
        ProjectPaths paths,
        ProjectFileReference source)
    {
        var path = source.Layer switch
        {
            ProjectFileLayer.Base => ResolveBaseExeFsPath(paths, source.RelativePath),
            ProjectFileLayer.Layered or ProjectFileLayer.Generated =>
                ResolveOutputPath(paths, source.RelativePath),
            _ => null,
        };
        if (path is null || !File.Exists(path))
        {
            return SourceSnapshot.Empty("missing");
        }

        return CaptureFileSnapshot(path, $"{source.Layer}-present");
    }

    private static SourceSnapshot CaptureEffectiveSource(
        ProjectPaths paths,
        string relativePath,
        ZaWorkflowFileSource fileSource)
    {
        var normalized = new RelativeOutputPath(relativePath).Value;
        if (IsExeFsPath(normalized))
        {
            var outputPath = ResolveOptionalOutputPath(paths, normalized);
            if (outputPath is not null && File.Exists(outputPath))
            {
                return CaptureFileSnapshot(outputPath, "effective-layered");
            }

            var basePath = ResolveBaseExeFsPath(paths, normalized);
            return basePath is not null && File.Exists(basePath)
                ? CaptureFileSnapshot(basePath, "effective-base")
                : SourceSnapshot.Empty("effective-missing");
        }

        try
        {
            var current = fileSource.ReadCurrentSourceFresh(paths, normalized);
            return SourceSnapshot.FromBytes($"effective-{current.Layer}", current.Bytes);
        }
        catch (Exception exception) when (IsMissingFile(exception))
        {
            return SourceSnapshot.Empty("effective-missing");
        }
    }

    private static SourceSnapshot CaptureFileSnapshot(string path, string state)
    {
        if (Directory.Exists(path))
        {
            return SourceSnapshot.Empty($"{state}-directory");
        }

        if (!File.Exists(path))
        {
            return SourceSnapshot.Empty($"{state}-missing");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var length = stream.Length;
        var digest = SHA256.HashData(stream);
        if (stream.Position != length)
        {
            throw new InvalidDataException("A Z-A semantic source changed while it was inspected.");
        }

        return new SourceSnapshot(state, length, digest);
    }

    private static void AppendSessionIntent(IncrementalHash hash, EditSession session)
    {
        AppendText(hash, "pending-edits");
        AppendInt32(hash, session.PendingEdits.Count);
        foreach (var edit in session.PendingEdits
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Owner, StringComparer.Ordinal)
                     .ThenBy(edit => edit.NewValue, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Summary, StringComparer.Ordinal))
        {
            AppendText(hash, edit.Domain);
            AppendText(hash, edit.Summary);
            AppendText(hash, edit.RecordId);
            AppendText(hash, edit.Field);
            AppendText(hash, edit.NewValue);
            AppendText(hash, edit.Owner);
            AppendInt32(hash, edit.Sources.Count);
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendText(hash, source.Layer.ToString());
                AppendText(hash, new RelativeOutputPath(source.RelativePath).CanonicalKey);
            }
        }
    }

    private static void AppendOutputTargetState(
        IncrementalHash hash,
        ProjectPaths paths,
        string targetRelativePath)
    {
        var targetPath = ResolveOutputPath(paths, targetRelativePath)
            ?? throw new InvalidOperationException("The Z-A output target could not be resolved.");
        AppendText(hash, "output-preimage");
        if (!File.Exists(targetPath))
        {
            if (Directory.Exists(targetPath))
            {
                throw new InvalidDataException(
                    $"The planned output target '{targetRelativePath}' is a directory.");
            }

            AppendText(hash, "missing");
            return;
        }

        AppendText(hash, "present");
        using var stream = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        AppendInt64(hash, stream.Length);
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, count);
        }
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("A Z-A output preimage changed while it was inspected.");
        }
    }

    private static DescriptorPreview? CreateDescriptorPreviewIfNeeded(
        ProjectPaths paths,
        IReadOnlyList<PlannedFileWrite> writes,
        ZaOutputMode outputMode)
    {
        if (outputMode != ZaOutputMode.Standalone)
        {
            return null;
        }

        var descriptorTarget = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths)
            .TargetRelativePath;
        var descriptorTargetKey = new RelativeOutputPath(descriptorTarget).CanonicalKey;
        if (!writes.Any(write =>
                string.Equals(
                    new RelativeOutputPath(write.TargetRelativePath).CanonicalKey,
                    descriptorTargetKey,
                    StringComparison.Ordinal)))
        {
            return null;
        }

        var plannedVirtualPaths = writes
            .Select(write => TryGetStandaloneRomFsVirtualPath(write.TargetRelativePath))
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(path => !string.Equals(
                path,
                ZaWorkflowFileSource.DescriptorVirtualPath,
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var bytes = ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
            paths,
            plannedVirtualPaths);
        return new DescriptorPreview(descriptorTargetKey, bytes);
    }

    private static string? TryGetStandaloneRomFsVirtualPath(string targetRelativePath)
    {
        var normalized = new RelativeOutputPath(targetRelativePath).Value;
        return normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            ? normalized["romfs/".Length..]
            : null;
    }

    private static bool IsExeFsPath(string relativePath)
    {
        return new RelativeOutputPath(relativePath).Value.StartsWith(
            "exefs/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ToSemanticSourcePath(string targetRelativePath)
    {
        var normalized = new RelativeOutputPath(targetRelativePath).Value;
        var isolatedPrefix = $"{ZaWorkflowFileSource.TrinityModManagerRomFsDirectory}/";
        return normalized.StartsWith(isolatedPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[isolatedPrefix.Length..]
            : normalized;
    }

    private static string? ResolveBaseExeFsPath(ProjectPaths paths, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            return null;
        }

        var normalized = new RelativeOutputPath(relativePath).Value;
        var pathWithinExeFs = normalized["exefs/".Length..];
        var root = Path.GetFullPath(paths.BaseExeFsPath);
        var path = Path.GetFullPath(Path.Combine(
            root,
            pathWithinExeFs.Replace('/', Path.DirectorySeparatorChar)));
        return PathContainment.IsOutsideRoot(Path.GetRelativePath(root, path))
            ? null
            : path;
    }

    private static string? ResolveOutputPath(ProjectPaths paths, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return null;
        }

        return ZaWorkflowFileSource.ResolveReviewedOutputPath(paths, relativePath);
    }

    private static string? ResolveOptionalOutputPath(ProjectPaths paths, string relativePath)
    {
        return string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? null
            : ZaWorkflowFileSource.ResolveReviewedOutputPath(paths, relativePath);
    }

    private static string? NormalizeRoot(string? configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return null;
        }

        return Path.GetFullPath(configuredRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant();
    }

    private static bool IsMissingFile(Exception exception)
    {
        Exception? candidate = exception;
        for (var depth = 0; candidate is not null && depth < 8; depth++)
        {
            if (candidate is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }

            candidate = candidate.InnerException;
        }

        return false;
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }

    private static string CombineFingerprints(
        string version,
        params string[] fingerprints)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, version);
        foreach (var fingerprint in fingerprints)
        {
            AppendText(hash, fingerprint);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string CreateReviewedFingerprint(
        string bindingFingerprint,
        string boundaryFingerprint,
        EditSession session)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, ReviewFingerprintVersion);
        AppendText(hash, bindingFingerprint);
        AppendText(hash, boundaryFingerprint);
        AppendSessionIntent(hash, session);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendText(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            AppendInt32(hash, -1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        if (bytes.Length > 0)
        {
            hash.AppendData(bytes);
        }
    }

    private static void AppendBytes(IncrementalHash hash, byte[] bytes)
    {
        AppendInt64(hash, bytes.LongLength);
        if (bytes.Length > 0)
        {
            hash.AppendData(bytes);
        }
    }

    private static void AppendSnapshot(IncrementalHash hash, SourceSnapshot snapshot)
    {
        AppendText(hash, snapshot.State);
        AppendInt64(hash, snapshot.Length);
        AppendBytes(hash, snapshot.Digest);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private sealed record DescriptorPreview(string TargetKey, byte[] Bytes);

    private sealed record SourceBindingCapture(
        string BoundaryFingerprint,
        string BindingFingerprint);

    private sealed record SourceSnapshot(string State, long Length, byte[] Digest)
    {
        public static SourceSnapshot Empty(string state)
        {
            return new SourceSnapshot(state, 0, Array.Empty<byte>());
        }

        public static SourceSnapshot FromBytes(string state, byte[] bytes)
        {
            return new SourceSnapshot(state, bytes.LongLength, SHA256.HashData(bytes));
        }
    }

    private readonly record struct SourceSnapshotKey(
        ProjectFileLayer Layer,
        string RelativePath);
}
