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

namespace KM.SV.Workflows;

/// <summary>
/// Adds fresh semantic-source evidence to Scarlet/Violet change plans. The
/// source-only binding remains stable across equivalent single-edit sessions,
/// while the reviewed fingerprint also binds the exact pending intent.
/// </summary>
internal static class SvChangePlanSourceGuard
{
    private const string BoundaryVersion = "KM.SV.ChangePlanSourceBoundary.v1";
    private const string BindingVersion = "KM.SV.ChangePlanSourceBinding.v1";
    private const string ReviewVersion = "KM.SV.ChangePlanReview.v1";

    public static ChangePlan Capture(
        ProjectPaths paths,
        EditSession session,
        ChangePlan plan,
        SvOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.CanApply)
        {
            return plan with { EffectivePendingEdits = null };
        }

        var effectivePendingEdits = CreateEffectivePendingEditEvidence(session);
        if (plan.Writes.Count == 0)
        {
            return plan with { EffectivePendingEdits = effectivePendingEdits };
        }

        using (SvWorkflowFileSource.AcquireOutputLock(paths))
        {
            using var freshReads = SvWorkflowFileSource.BeginFreshReadScope(paths);
            var diagnostics = plan.Diagnostics.ToList();
            try
            {
                // ReadCurrentSourceFresh and ReadBaseBytesFresh bypass the reusable
                // archive cache. A file changed in place must produce new evidence.
                var fileSource = new SvWorkflowFileSource(bypassReusableBaseCache: true);
                var declaredSnapshots = new Dictionary<SourceSnapshotKey, SourceSnapshot>();
                var effectiveSnapshots = new Dictionary<string, SourceSnapshot>(StringComparer.Ordinal);
                var descriptorPreview = CreateDescriptorPreviewIfNeeded(paths, plan.Writes, outputMode);
                var writes = plan.Writes
                    .Select(write => CaptureWrite(
                        paths,
                        session,
                        write,
                        outputMode,
                        fileSource,
                        declaredSnapshots,
                        effectiveSnapshots,
                        descriptorPreview))
                    .ToArray();

                return plan with
                {
                    Writes = writes,
                    EffectivePendingEdits = effectivePendingEdits,
                };
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
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Scarlet/Violet change-plan source verification could not read the current sources or output preimages.",
                    "sv.editor",
                    expected: "Readable current semantic sources and output preimages"));
                return plan with
                {
                    Writes = Array.Empty<PlannedFileWrite>(),
                    Diagnostics = diagnostics,
                    EffectivePendingEdits = null,
                };
            }
        }
    }

    private static IReadOnlyList<PendingEdit> CreateEffectivePendingEditEvidence(
        EditSession session)
    {
        return session.PendingEdits
            .Select(edit => edit.Association is null
                ? edit
                : edit with { Association = null })
            .ToArray();
    }

    private static PlannedFileWrite CaptureWrite(
        ProjectPaths paths,
        EditSession session,
        PlannedFileWrite write,
        SvOutputMode outputMode,
        SvWorkflowFileSource fileSource,
        IDictionary<SourceSnapshotKey, SourceSnapshot> declaredSnapshots,
        IDictionary<string, SourceSnapshot> effectiveSnapshots,
        DescriptorPreview? descriptorPreview)
    {
        var genericBoundary = CreateGenericBoundary(
            paths,
            write,
            outputMode,
            fileSource,
            declaredSnapshots,
            effectiveSnapshots,
            descriptorPreview);
        var bindingFingerprint = CreateBindingFingerprint(
            genericBoundary,
            write.SourceBindingFingerprint,
            write.SourceFingerprint);
        var reviewedFingerprint = CreateReviewedFingerprint(bindingFingerprint, session);
        return write with
        {
            SourceBindingFingerprint = bindingFingerprint,
            SourceFingerprint = reviewedFingerprint,
        };
    }

    private static string CreateGenericBoundary(
        ProjectPaths paths,
        PlannedFileWrite write,
        SvOutputMode outputMode,
        SvWorkflowFileSource fileSource,
        IDictionary<SourceSnapshotKey, SourceSnapshot> declaredSnapshots,
        IDictionary<string, SourceSnapshot> effectiveSnapshots,
        DescriptorPreview? descriptorPreview)
    {
        var target = new RelativeOutputPath(write.TargetRelativePath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, BoundaryVersion);
        AppendText(hash, paths.SelectedGame?.ToString());
        AppendText(hash, outputMode.ToString());
        AppendText(hash, NormalizeRoot(paths.BaseRomFsPath));
        AppendText(hash, NormalizeRoot(paths.BaseExeFsPath));
        AppendText(hash, NormalizeRoot(paths.OutputRootPath));
        AppendText(hash, target.CanonicalKey);
        AppendText(hash, write.ReplacesExistingOutput ? "replace" : "create");

        var orderedSources = write.Sources
            .OrderBy(source => source.Layer)
            .ThenBy(source => new RelativeOutputPath(source.RelativePath).CanonicalKey, StringComparer.Ordinal)
            .ToArray();
        AppendInt32(hash, orderedSources.Length);
        foreach (var source in orderedSources)
        {
            var sourcePath = new RelativeOutputPath(source.RelativePath);
            AppendText(hash, source.Layer.ToString());
            AppendText(hash, sourcePath.CanonicalKey);

            var sourceKey = new SourceSnapshotKey(source.Layer, sourcePath.CanonicalKey);
            if (!declaredSnapshots.TryGetValue(sourceKey, out var declaredSnapshot))
            {
                declaredSnapshot = CaptureDeclaredSource(paths, source, fileSource);
                declaredSnapshots.Add(sourceKey, declaredSnapshot);
            }

            AppendText(hash, "declared-source");
            AppendSnapshot(hash, declaredSnapshot);
            if (source.Layer == ProjectFileLayer.Pending)
            {
                continue;
            }

            if (!effectiveSnapshots.TryGetValue(sourcePath.CanonicalKey, out var effectiveSnapshot))
            {
                effectiveSnapshot = CaptureEffectiveSource(paths, sourcePath.Value, fileSource);
                effectiveSnapshots.Add(sourcePath.CanonicalKey, effectiveSnapshot);
            }

            // Bind the effective source independently of the provenance layer. A
            // newly added override must invalidate a plan that originally cited Base.
            AppendText(hash, "effective-source");
            AppendSnapshot(hash, effectiveSnapshot);
        }

        AppendText(hash, "authoritative-target-source");
        AppendSnapshot(hash, CaptureEffectiveSource(paths, target.Value, fileSource));
        AppendText(hash, "output-target-preimage");
        AppendSnapshot(hash, CaptureOutputTarget(paths, target.Value));

        if (descriptorPreview is not null
            && string.Equals(target.CanonicalKey, descriptorPreview.TargetKey, StringComparison.Ordinal))
        {
            AppendText(hash, "standalone-descriptor-preview");
            AppendSnapshot(hash, descriptorPreview.Snapshot);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string CreateBindingFingerprint(
        string genericBoundary,
        string? existingBinding,
        string? existingSourceFingerprint)
    {
        var hasExistingBinding = !string.IsNullOrWhiteSpace(existingBinding);
        var richerFingerprint = hasExistingBinding
            ? existingBinding
            : existingSourceFingerprint;
        if (string.IsNullOrWhiteSpace(richerFingerprint))
        {
            return genericBoundary;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, BindingVersion);
        AppendText(hash, genericBoundary);
        AppendText(hash, hasExistingBinding ? "source-binding" : "legacy-source-fingerprint");
        AppendText(hash, richerFingerprint);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string CreateReviewedFingerprint(string bindingFingerprint, EditSession session)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, ReviewVersion);
        AppendText(hash, bindingFingerprint);
        AppendSessionIntent(hash, session);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static SourceSnapshot CaptureDeclaredSource(
        ProjectPaths paths,
        ProjectFileReference source,
        SvWorkflowFileSource fileSource)
    {
        if (source.Layer == ProjectFileLayer.Pending)
        {
            return SourceSnapshot.Empty("pending-intent");
        }

        var relativePath = new RelativeOutputPath(source.RelativePath).Value;
        if (IsExeFsPath(relativePath))
        {
            var sourcePath = source.Layer == ProjectFileLayer.Base
                ? ResolveBaseExeFsPath(paths, relativePath)
                : ResolveContainedPath(paths.OutputRootPath, relativePath);
            return CaptureFileSnapshot(sourcePath, $"declared-{source.Layer}");
        }

        if (source.Layer == ProjectFileLayer.Base)
        {
            return CaptureRomFsSnapshot(
                () => (fileSource.ReadBaseBytesFresh(paths, relativePath), ProjectFileLayer.Base),
                "declared-base");
        }

        return CaptureRomFsSnapshot(
            () => fileSource.ReadCurrentSourceFresh(paths, relativePath),
            $"declared-{source.Layer}");
    }

    private static SourceSnapshot CaptureEffectiveSource(
        ProjectPaths paths,
        string relativePath,
        SvWorkflowFileSource fileSource)
    {
        if (IsExeFsPath(relativePath))
        {
            var outputPath = ResolveContainedPath(paths.OutputRootPath, relativePath);
            if (outputPath is not null && File.Exists(outputPath))
            {
                return CaptureFileSnapshot(outputPath, "effective-layered");
            }

            return CaptureFileSnapshot(
                ResolveBaseExeFsPath(paths, relativePath),
                "effective-base");
        }

        return CaptureRomFsSnapshot(
            () => fileSource.ReadCurrentSourceFresh(paths, relativePath),
            "effective");
    }

    private static SourceSnapshot CaptureRomFsSnapshot(
        Func<(byte[] Bytes, ProjectFileLayer Layer)> read,
        string statePrefix)
    {
        try
        {
            var result = read();
            return SourceSnapshot.FromBytes($"{statePrefix}-{result.Layer}", result.Bytes);
        }
        catch (Exception exception) when (IsMissingFile(exception))
        {
            return SourceSnapshot.Empty($"{statePrefix}-missing");
        }
    }

    private static SourceSnapshot CaptureOutputTarget(ProjectPaths paths, string targetRelativePath)
    {
        var path = ResolveContainedPath(paths.OutputRootPath, targetRelativePath)
            ?? throw new InvalidOperationException("The Scarlet/Violet output target could not be resolved.");
        return CaptureFileSnapshot(path, "output");
    }

    private static SourceSnapshot CaptureFileSnapshot(string? path, string statePrefix)
    {
        if (path is null)
        {
            return SourceSnapshot.Empty($"{statePrefix}-unconfigured");
        }

        if (Directory.Exists(path))
        {
            return SourceSnapshot.Empty($"{statePrefix}-directory");
        }

        if (!File.Exists(path))
        {
            return SourceSnapshot.Empty($"{statePrefix}-missing");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        var length = stream.Length;
        var digest = SHA256.HashData(stream);
        if (stream.Position != length)
        {
            throw new InvalidDataException("A semantic source changed while it was inspected.");
        }

        return new SourceSnapshot($"{statePrefix}-present", length, digest);
    }

    private static DescriptorPreview? CreateDescriptorPreviewIfNeeded(
        ProjectPaths paths,
        IReadOnlyList<PlannedFileWrite> writes,
        SvOutputMode outputMode)
    {
        if (outputMode != SvOutputMode.Standalone)
        {
            return null;
        }

        var descriptorTarget = new RelativeOutputPath(
            $"romfs/{SvWorkflowFileSource.DescriptorVirtualPath}");
        if (!writes.Any(write => string.Equals(
                new RelativeOutputPath(write.TargetRelativePath).CanonicalKey,
                descriptorTarget.CanonicalKey,
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
                SvWorkflowFileSource.DescriptorVirtualPath,
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var bytes = SvWorkflowFileSource.CreateStandaloneDescriptorPreview(paths, plannedVirtualPaths);
        return new DescriptorPreview(
            descriptorTarget.CanonicalKey,
            SourceSnapshot.FromBytes("descriptor-preview", bytes));
    }

    private static void AppendSessionIntent(IncrementalHash hash, EditSession session)
    {
        var edits = session.PendingEdits
            .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
            .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
            .ThenBy(edit => edit.Field, StringComparer.Ordinal)
            .ThenBy(edit => edit.Owner, StringComparer.Ordinal)
            .ThenBy(edit => edit.NewValue, StringComparer.Ordinal)
            .ThenBy(edit => edit.Summary, StringComparer.Ordinal)
            .ToArray();
        AppendText(hash, "pending-edits");
        AppendInt32(hash, edits.Length);
        foreach (var edit in edits)
        {
            AppendText(hash, edit.Domain);
            AppendText(hash, edit.Summary);
            AppendText(hash, edit.RecordId);
            AppendText(hash, edit.Field);
            AppendText(hash, edit.NewValue);
            AppendText(hash, edit.Owner);
            var sources = edit.Sources
                .OrderBy(source => source.Layer)
                .ThenBy(source => new RelativeOutputPath(source.RelativePath).CanonicalKey, StringComparer.Ordinal)
                .ToArray();
            AppendInt32(hash, sources.Length);
            foreach (var source in sources)
            {
                AppendText(hash, source.Layer.ToString());
                AppendText(hash, new RelativeOutputPath(source.RelativePath).CanonicalKey);
            }
        }
    }

    private static void AppendSnapshot(IncrementalHash hash, SourceSnapshot snapshot)
    {
        AppendText(hash, snapshot.State);
        AppendInt64(hash, snapshot.Length);
        AppendBytes(hash, snapshot.Digest);
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
        return relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveBaseExeFsPath(ProjectPaths paths, string relativePath)
    {
        var normalized = new RelativeOutputPath(relativePath).Value;
        if (!normalized.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ResolveContainedPath(paths.BaseExeFsPath, normalized["exefs/".Length..]);
    }

    private static string? ResolveContainedPath(string? configuredRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return null;
        }

        var normalized = new RelativeOutputPath(relativePath).Value;
        var root = Path.GetFullPath(configuredRoot);
        var path = Path.GetFullPath(Path.Combine(
            root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        return PathContainment.IsOutsideRoot(Path.GetRelativePath(root, path))
            ? null
            : path;
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

    private sealed record DescriptorPreview(string TargetKey, SourceSnapshot Snapshot);

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
        string CanonicalRelativePath);
}
