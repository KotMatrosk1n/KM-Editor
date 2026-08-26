// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Output;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;
using KM.Tools.Bridge;

namespace KM.Tools.Application;

/// <summary>
/// Owns bounded bridge review state for output recovery, cleanup, history, and checkpoints.
/// Durable truth remains in the coordinator under the selected output root.
/// </summary>
public sealed class OutputSafetyApplicationService
{
    private const int MaximumCachedIntegrityScopes = 8;
    private const int MaximumCachedPlans = 16;
    private const long MaximumBaselineHashBytes = 2L * 1024L * 1024L * 1024L;
    private static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(10);
    private static readonly OutputTransactionCoordinatorOptions CoordinatorOptions = new();
    private static readonly ProjectFileGraphBuilder BaselineGraphBuilder = new(
        new ProjectFileGraphBuilderOptions
        {
            MaximumFileSystemEntries = OutputLimits.MaximumIntegrityEntries,
            MaximumDirectories = OutputLimits.MaximumInventoryDirectories,
            MaximumTraversalDepth = OutputLimits.MaximumOutputPathDepth,
            MaximumGraphEntries = OutputLimits.MaximumIntegrityEntries,
        });
    private readonly object reviewSyncRoot = new();
    private readonly Dictionary<string, CachedIntegrityReview> integrityReviews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CachedCleanupPlan> cleanupPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CachedRestorePlan> restorePlans = new(StringComparer.Ordinal);

    public async Task<GetOutputRecoveryStatusResponse> GetRecoveryStatusAsync(
        GetOutputRecoveryStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveScope(request?.Scope);
        var report = await context.Coordinator.InspectRecoveryAsync(cancellationToken).ConfigureAwait(false);
        return new GetOutputRecoveryStatusResponse(ToDto(report));
    }

    internal async Task EnsureRecoveryReadyAsync(
        OutputScopeDto scope,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveScope(scope);
        var report = await context.Coordinator.InspectRecoveryAsync(cancellationToken).ConfigureAwait(false);
        if (report.Transactions.Any(
                transaction => transaction.Disposition != OutputRecoveryDisposition.NoAction))
        {
            throw new OutputRecoveryRequiredException(report);
        }
    }

    internal async Task<TResult> ExecuteExclusiveOutputOperationAsync<TResult>(
        OutputScopeDto scope,
        Func<TResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var context = ResolveScope(scope);
        return await context.Coordinator
            .ExecuteExclusiveOutputOperationAsync(operation, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<TResult> ExecuteExclusiveOutputOperationAsync<TResult>(
        OutputScopeDto scope,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var context = ResolveScope(scope);
        return await context.Coordinator
            .ExecuteExclusiveOutputOperationAsync(operation, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReconcileOutputRecoveryResponse> ReconcileRecoveryAsync(
        ReconcileOutputRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ResolveScope(request.Scope);
        var expectedRevision = new OutputStateRevision(request.ExpectedRevision);
        var before = await context.Coordinator.InspectRecoveryAsync(cancellationToken).ConfigureAwait(false);
        if (before.Revision != expectedRevision)
        {
            throw new OutputStateRevisionConflictException(expectedRevision, before.Revision);
        }

        var candidates = before.Transactions
            .Where(transaction => transaction.Disposition is
                OutputRecoveryDisposition.FinalizeCommit or OutputRecoveryDisposition.RollBack)
            .Select(transaction => transaction.TransactionId)
            .ToHashSet();
        var after = await context.Coordinator
            .RecoverAsync(expectedRevision, cancellationToken)
            .ConfigureAwait(false);
        var unresolved = after.Transactions
            .Where(transaction => transaction.Disposition != OutputRecoveryDisposition.NoAction)
            .Select(transaction => transaction.TransactionId)
            .ToHashSet();
        var reconciledCount = candidates.Count(transactionId => !unresolved.Contains(transactionId));
        InvalidateReviews(context.ScopeKey);
        return new ReconcileOutputRecoveryResponse(ToDto(after), reconciledCount);
    }

    public async Task<ScanOutputIntegrityResponse> ScanIntegrityAsync(
        ScanOutputIntegrityRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveScope(request?.Scope);
        var (report, inventory, baselineUnknownPaths) = await ScanConsistentIntegrityAsync(
                context,
                cancellationToken)
            .ConfigureAwait(false);
        var scopedOwnershipByPath = inventory.Inventory.Files
            .Where(file => file.ProjectId == context.ProjectId && file.GameFamily == context.GameFamily)
            .ToDictionary(
            file => file.Path.CanonicalKey,
            file => file,
            StringComparer.Ordinal);

        var orderedEntries = report.Entries
            .Select(entry => CreateScopedIntegrityEntry(
                entry,
                scopedOwnershipByPath,
                baselineUnknownPaths.Contains(entry.Path.CanonicalKey)))
            .OrderBy(entry => entry.CleanupCandidate ? 0 : 1)
            .ThenBy(entry => entry.Entry.Path.CanonicalKey, StringComparer.Ordinal)
            .ToArray();
        var returnedEntries = orderedEntries
            .Take(OutputSafetyContract.MaximumReturnedEntries)
            .Select(entry =>
            {
                var targetId = CreateTargetId(report.Revision, entry.Entry.Path);
                return new OutputIntegrityEntryDto(
                    targetId,
                    entry.Entry.Path.Value,
                    ToDto(entry.Classification),
                    entry.CleanupCandidate,
                    entry.Entry.CurrentState?.Exists == true
                        ? entry.Entry.CurrentState.LengthBytes.ToString(CultureInfo.InvariantCulture)
                        : null,
                    entry.OwnerIds);
            })
            .ToArray();
        var diagnostics = CreateIntegrityDiagnostics(orderedEntries, orderedEntries.Length > returnedEntries.Length);
        var scanId = Guid.NewGuid().ToString("N");
        var review = new CachedIntegrityReview(
            context.ScopeKey,
            scanId,
            report.Revision,
            returnedEntries.ToDictionary(
                entry => entry.TargetId,
                entry => new CachedIntegrityTarget(
                    new RelativeOutputPath(entry.RelativePath),
                    entry.Classification,
                    entry.CleanupEligible,
                    entry.SizeBytes,
                    entry.OwnerIds),
                StringComparer.Ordinal),
            DateTimeOffset.UtcNow.Add(ReviewLifetime));
        StoreIntegrityReview(review);

        return new ScanOutputIntegrityResponse(
            scanId,
            report.Revision.Value,
            report.ScannedAtUtc,
            ToCounts(orderedEntries.Select(entry => entry.Classification)),
            returnedEntries,
            orderedEntries.Length > returnedEntries.Length,
            diagnostics);
    }

    public Task<PreviewOutputCleanupResponse> PreviewCleanupAsync(
        PreviewOutputCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var context = ResolveScope(request.Scope);
        ValidatePlanId(request.ScanId);
        ValidateTargetIds(request.TargetIds);
        var expectedRevision = new OutputStateRevision(request.IntegrityRevision);
        CachedIntegrityReview review;
        lock (reviewSyncRoot)
        {
            PruneExpiredReviewsLocked();
            review = integrityReviews.Values.FirstOrDefault(candidate =>
                    candidate.ScopeKey == context.ScopeKey
                    && candidate.ScanId == request.ScanId
                    && candidate.Revision == expectedRevision)
                ?? throw new OutputReviewExpiredException();
        }

        var candidates = new List<OutputCleanupCandidateDto>();
        var paths = ImmutableArray.CreateBuilder<RelativeOutputPath>();
        long totalBytes = 0;
        foreach (var targetId in request.TargetIds.Distinct(StringComparer.Ordinal))
        {
            if (!review.Targets.TryGetValue(targetId, out var target)
                || target.Classification is not (
                    OutputIntegrityClassificationDto.KmOwnedCurrent or
                    OutputIntegrityClassificationDto.KmOwnedStale)
                || !target.CleanupEligible
                || target.OwnerIds.Count == 0)
            {
                throw new OutputOwnershipUnprovenException();
            }

            var size = target.SizeBytes is null
                ? 0
                : long.Parse(target.SizeBytes, NumberStyles.None, CultureInfo.InvariantCulture);
            totalBytes = checked(totalBytes + size);
            paths.Add(target.Path);
            candidates.Add(new OutputCleanupCandidateDto(targetId, target.Path.Value, target.SizeBytes));
        }

        if (candidates.Count == 0)
        {
            throw new OutputOwnershipUnprovenException();
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        var planId = Guid.NewGuid().ToString("N");
        var plan = new CachedCleanupPlan(
            planId,
            context.ScopeKey,
            expectedRevision,
            paths.ToImmutable(),
            candidates.ToImmutableArray(),
            createdAtUtc.Add(ReviewLifetime));
        StoreCleanupPlan(plan);
        return Task.FromResult(new PreviewOutputCleanupResponse(
            planId,
            expectedRevision.Value,
            createdAtUtc,
            candidates,
            totalBytes.ToString(CultureInfo.InvariantCulture),
            []));
    }

    public async Task<ApplyOutputCleanupResponse> ApplyCleanupAsync(
        ApplyOutputCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ResolveScope(request.Scope);
        ValidatePlanId(request.PlanId);
        var expectedRevision = new OutputStateRevision(request.ExpectedRevision);
        CachedCleanupPlan plan;
        lock (reviewSyncRoot)
        {
            PruneExpiredReviewsLocked();
            if (!cleanupPlans.Remove(request.PlanId, out plan!)
                || plan.ScopeKey != context.ScopeKey
                || plan.ExpectedRevision != expectedRevision)
            {
                throw new OutputReviewExpiredException();
            }
        }

        var baseline = await BuildOutputBaselineAsync(context.Paths, cancellationToken).ConfigureAwait(false);
        var current = await context.Coordinator.ScanIntegrityAsync(
                baseline.Entries,
                cancellationToken)
            .ConfigureAwait(false);
        if (current.Revision != expectedRevision)
        {
            throw new OutputStateRevisionConflictException(expectedRevision, current.Revision);
        }

        var result = await context.Coordinator.CleanupOwnedAsync(
                context.ProjectId,
                context.GameFamily,
                "owned-output-cleanup",
                plan.Paths,
                cancellationToken)
            .ConfigureAwait(false);
        InvalidateReviews(context.ScopeKey);
        var candidateByPath = plan.Candidates.ToDictionary(
            candidate => new RelativeOutputPath(candidate.RelativePath).CanonicalKey,
            StringComparer.Ordinal);
        var entries = result.Entries.Select(entry =>
        {
            var candidate = candidateByPath.GetValueOrDefault(entry.Path.CanonicalKey);
            return new OutputCleanupEntryDto(
                candidate?.TargetId ?? CreateTargetId(expectedRevision, entry.Path),
                entry.Path.Value,
                ToDto(entry.Disposition));
        }).ToArray();
        var removedCount = entries.Count(entry => entry.Disposition == OutputCleanupDispositionDto.Removed);
        return new ApplyOutputCleanupResponse(
            removedCount,
            entries.Length - removedCount,
            entries,
            result.ApplyResult is null ? null : ToDto(result.ApplyResult),
            CreateCleanupDiagnostics(entries));
    }

    public async Task<ListOutputHistoryResponse> ListHistoryAsync(
        ListOutputHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ResolveScope(request.Scope);
        if (request.Limit is < 1 or > OutputSafetyContract.MaximumHistoryPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Limit));
        }

        var history = await context.Coordinator.GetHistorySnapshotAsync(cancellationToken).ConfigureAwait(false);
        var receipts = history.Receipts
            .Where(receipt => receipt.ProjectId == context.ProjectId && receipt.GameFamily == context.GameFamily)
            .OrderByDescending(receipt => receipt.CompletedAtUtc)
            .ThenByDescending(receipt => receipt.TransactionId.Value, StringComparer.Ordinal)
            .ToArray();
        var start = 0;
        if (request.Cursor is not null)
        {
            ValidateTransactionId(request.Cursor);
            var cursorIndex = Array.FindIndex(
                receipts,
                receipt => receipt.TransactionId.Value == request.Cursor);
            if (cursorIndex < 0)
            {
                throw new OutputReviewExpiredException();
            }

            start = cursorIndex + 1;
        }

        var page = receipts.Skip(start).Take(request.Limit).Select(ToHistoryDto).ToArray();
        var hasMore = start + page.Length < receipts.Length;
        return new ListOutputHistoryResponse(
            page,
            hasMore && page.Length > 0 ? page[^1].TransactionId : null,
            hasMore);
    }

    public async Task<ListOutputCheckpointsResponse> ListCheckpointsAsync(
        ListOutputCheckpointsRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveScope(request?.Scope);
        var list = await context.Coordinator.ListCheckpointsAsync(cancellationToken).ConfigureAwait(false);
        var integrity = await context.Coordinator.ScanIntegrityAsync(
                baseline: null,
                cancellationToken)
            .ConfigureAwait(false);
        return new ListOutputCheckpointsResponse(
            list.Revision.Value,
            integrity.Revision.Value,
            list.Checkpoints
                .Where(checkpoint => checkpoint.ProjectId == context.ProjectId
                    && checkpoint.GameFamily == context.GameFamily)
                .OrderByDescending(checkpoint => checkpoint.CreatedAtUtc)
                .Select(ToDto)
                .ToArray());
    }

    public async Task<CreateOutputCheckpointResponse> CreateCheckpointAsync(
        CreateOutputCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ResolveScope(request.Scope);
        ValidateCheckpointLabel(request.Label);
        var expectedRevision = new OutputStateRevision(request.ExpectedOutputRevision);
        var outputMode = await GetCurrentOutputModeAsync(context, cancellationToken).ConfigureAwait(false);
        var checkpoint = await context.Coordinator.CreateCheckpointAsync(
                context.ProjectId,
                context.GameFamily,
                outputMode,
                request.Label,
                expectedRevision,
                cancellationToken)
            .ConfigureAwait(false);
        var list = await context.Coordinator.ListCheckpointsAsync(cancellationToken).ConfigureAwait(false);
        var scopedCheckpoints = list.Checkpoints
            .Where(existing => existing.ProjectId == context.ProjectId
                && existing.GameFamily == context.GameFamily)
            .OrderByDescending(existing => existing.CreatedAtUtc)
            .Select(ToDto)
            .ToArray();
        return new CreateOutputCheckpointResponse(
            list.Revision.Value,
            expectedRevision.Value,
            ToDto(checkpoint),
            scopedCheckpoints);
    }

    public async Task<PreviewOutputCheckpointRestoreResponse> PreviewCheckpointRestoreAsync(
        PreviewOutputCheckpointRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ResolveScope(request.Scope);
        var checkpointId = new OutputCheckpointId(request.CheckpointId);
        ValidateSha256(request.ManifestFingerprint, nameof(request.ManifestFingerprint));
        await RequireOwnedCheckpointAsync(context, checkpointId, request.ManifestFingerprint, cancellationToken)
            .ConfigureAwait(false);
        var preview = await context.Coordinator.PreviewCheckpointRestoreAsync(
                checkpointId,
                request.ManifestFingerprint,
                cancellationToken)
            .ConfigureAwait(false);
        var planId = Guid.NewGuid().ToString("N");
        if (!preview.IsCurrent)
        {
            StoreRestorePlan(new CachedRestorePlan(
                planId,
                context.ScopeKey,
                checkpointId,
                preview.ManifestFingerprint,
                preview.OutputRevision,
                DateTimeOffset.UtcNow.Add(ReviewLifetime)));
        }

        var targets = preview.Targets
            .Take(OutputSafetyContract.MaximumReturnedEntries)
            .Select(path => path.Value)
            .ToArray();
        var diagnostics = preview.Targets.Length > targets.Length
            ? new[]
            {
                CreateDiagnostic(
                    ApiDiagnosticSeverity.Warning,
                    "The checkpoint restore preview is larger than the displayed target list.",
                    "KM-OUTPUT-HISTORY-TRUNCATED",
                    "output.checkpoint"),
            }
            : [];
        return new PreviewOutputCheckpointRestoreResponse(
            planId,
            CanRestore: !preview.IsCurrent,
            preview.Targets.Length,
            preview.WriteBytes.ToString(CultureInfo.InvariantCulture),
            targets,
            diagnostics);
    }

    public async Task<RestoreOutputCheckpointResponse> RestoreCheckpointAsync(
        RestoreOutputCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ResolveScope(request.Scope);
        ValidatePlanId(request.PlanId);
        CachedRestorePlan plan;
        lock (reviewSyncRoot)
        {
            PruneExpiredReviewsLocked();
            if (!restorePlans.Remove(request.PlanId, out plan!) || plan.ScopeKey != context.ScopeKey)
            {
                throw new OutputReviewExpiredException();
            }
        }

        var result = await context.Coordinator.RestoreCheckpointAsync(
                plan.CheckpointId,
                plan.ManifestFingerprint,
                plan.OutputRevision,
                cancellationToken)
            .ConfigureAwait(false);
        InvalidateReviews(context.ScopeKey);
        if (result.ApplyResult is null)
        {
            throw new OutputCheckpointAlreadyCurrentException();
        }

        return new RestoreOutputCheckpointResponse(ToDto(result.ApplyResult), []);
    }

    public async Task<DeleteOutputCheckpointResponse> DeleteCheckpointAsync(
        DeleteOutputCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ResolveScope(request.Scope);
        var checkpointId = new OutputCheckpointId(request.CheckpointId);
        await RequireOwnedCheckpointAsync(context, checkpointId, request.ManifestFingerprint, cancellationToken)
            .ConfigureAwait(false);
        var result = await context.Coordinator.DeleteCheckpointAsync(
                checkpointId,
                request.ManifestFingerprint,
                new OutputStateRevision(request.ExpectedRevision),
                cancellationToken)
            .ConfigureAwait(false);
        return new DeleteOutputCheckpointResponse(result.Deleted, result.Revision.Value);
    }

    public async Task<BuildSupportReportResponse> BuildSupportReportAsync(
        BuildSupportReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveScope(request?.Scope);
        var outputMode = await GetCurrentOutputModeAsync(
                context,
                cancellationToken,
                fallback: "not-recorded")
            .ConfigureAwait(false);
        var report = await context.Coordinator.CreateSupportReportAsync(
            context.ProjectId,
            GetApplicationVersion(),
            context.GameFamily,
            outputMode,
            ["KM-OUTPUT-SUPPORT-REPORT-REDACTED"],
            cancellationToken).ConfigureAwait(false);
        return new BuildSupportReportResponse(new OutputSupportReportDto(
            SchemaVersion: 1,
            report.ApplicationVersion,
            ToGameFamilyKey(report.GameFamily),
            report.OutputMode,
            report.DiagnosticCodes,
            report.TransactionPhases.Select(ToDto).ToArray(),
            report.IntegrityCounts.Select(count => new OutputIntegrityCountDto(
                ToDto(count.Classification),
                count.Count)).ToArray(),
            report.OwnershipFileCount,
            report.CheckpointCount,
            report.HistoryReceiptCount,
            report.CreatedAtUtc));
    }

    private static async Task<(
        OutputIntegrityReport Report,
        OutputOwnershipInventorySnapshot Inventory,
        IReadOnlySet<string> BaselineUnknownPaths)>
        ScanConsistentIntegrityAsync(OutputScopeContext context, CancellationToken cancellationToken)
    {
        var baseline = await BuildOutputBaselineAsync(context.Paths, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var before = await context.Coordinator
                .GetOwnershipInventorySnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            var report = await context.Coordinator
                .ScanIntegrityAsync(baseline.Entries, cancellationToken)
                .ConfigureAwait(false);
            var after = await context.Coordinator
                .GetOwnershipInventorySnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            if (before.Revision == after.Revision)
            {
                return (report, after, baseline.UnknownPathKeys);
            }
        }

        throw new OutputReviewExpiredException();
    }

    private static async Task<OutputBaselineSnapshot> BuildOutputBaselineAsync(
        ProjectPaths paths,
        CancellationToken cancellationToken)
    {
        ProjectFileGraph graph;
        try
        {
            graph = BaselineGraphBuilder.Build(paths, cancellationToken);
        }
        catch (ProjectFileGraphDiscoveryException)
        {
            return OutputBaselineSnapshot.Empty;
        }

        var candidates = graph.Entries
            .Where(entry => entry.State == ProjectFileGraphEntryState.LayeredOverride
                && entry.BaseFile is not null
                && entry.LayeredFile is not null)
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var baseline = new List<OutputBaselineEntry>(candidates.Length);
        var unknownPathKeys = new HashSet<string>(StringComparer.Ordinal);
        var buffer = new byte[81920];
        long hashedBytes = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RelativeOutputPath outputPath;
            try
            {
                outputPath = new RelativeOutputPath(candidate.LayeredFile!.RelativePath);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (baseline.Count == CoordinatorOptions.MaximumIntegrityEntries)
            {
                unknownPathKeys.Add(outputPath.CanonicalKey);
                continue;
            }

            if (!TryResolvePhysicalBasePath(
                    paths,
                    candidate.BaseFile!,
                    out var sourceRoot,
                    out var sourcePath))
            {
                unknownPathKeys.Add(outputPath.CanonicalKey);
                continue;
            }

            var remainingBytes = MaximumBaselineHashBytes - hashedBytes;
            if (remainingBytes <= 0)
            {
                unknownPathKeys.Add(outputPath.CanonicalKey);
                continue;
            }

            var state = await TryFingerprintBaselineFileAsync(
                    sourceRoot,
                    sourcePath,
                    Math.Min(CoordinatorOptions.MaximumFingerprintFileBytes, remainingBytes),
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);
            if (state is null)
            {
                unknownPathKeys.Add(outputPath.CanonicalKey);
                continue;
            }

            baseline.Add(new OutputBaselineEntry(outputPath, state));
            hashedBytes = checked(hashedBytes + state.LengthBytes);
        }

        return new OutputBaselineSnapshot(baseline, unknownPathKeys);
    }

    private static bool TryResolvePhysicalBasePath(
        ProjectPaths paths,
        ProjectFileReference baseFile,
        out string normalizedSourceRoot,
        out string sourcePath)
    {
        const string romFsPrefix = "romfs/";
        const string exeFsPrefix = "exefs/";
        string? sourceRoot;
        string relativePath;
        if (baseFile.RelativePath.StartsWith(romFsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            sourceRoot = paths.BaseRomFsPath;
            relativePath = baseFile.RelativePath[romFsPrefix.Length..];
        }
        else if (baseFile.RelativePath.StartsWith(exeFsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            sourceRoot = paths.BaseExeFsPath;
            relativePath = baseFile.RelativePath[exeFsPrefix.Length..];
        }
        else
        {
            normalizedSourceRoot = string.Empty;
            sourcePath = string.Empty;
            return false;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
            {
                normalizedSourceRoot = string.Empty;
                sourcePath = string.Empty;
                return false;
            }

            normalizedSourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
            sourcePath = Path.GetFullPath(Path.Combine(
                normalizedSourceRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return !string.Equals(sourcePath, normalizedSourceRoot, GetPathComparison())
                && IsContainedOrEqual(normalizedSourceRoot, sourcePath)
                && HasSafePhysicalFileChain(normalizedSourceRoot, sourcePath);
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            normalizedSourceRoot = string.Empty;
            sourcePath = string.Empty;
            return false;
        }
    }

    private static bool HasSafePhysicalFileChain(string rootPath, string filePath)
    {
        var root = new DirectoryInfo(rootPath);
        root.Refresh();
        if (!root.Exists || !string.IsNullOrEmpty(root.LinkTarget))
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(rootPath, filePath);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var currentPath = rootPath;
        for (var index = 0; index < segments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, segments[index]);
            FileSystemInfo entry = index == segments.Length - 1
                ? new FileInfo(currentPath)
                : new DirectoryInfo(currentPath);
            entry.Refresh();
            if (!entry.Exists || !string.IsNullOrEmpty(entry.LinkTarget))
            {
                return false;
            }

            var isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
            if (isDirectory != (index < segments.Length - 1))
            {
                return false;
            }
        }

        return segments.Length > 0;
    }

    private static async Task<OutputFileState?> TryFingerprintBaselineFileAsync(
        string rootPath,
        string path,
        long maximumBytes,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > maximumBytes)
            {
                return null;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total = checked(total + read);
                if (total > maximumBytes)
                {
                    return null;
                }

                hash.AppendData(buffer, 0, read);
            }

            return HasSafePhysicalFileChain(rootPath, path)
                ? OutputFileState.Existing(Convert.ToHexStringLower(hash.GetHashAndReset()), total)
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            return null;
        }
    }

    private static bool IsSkippableFileSystemException(Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static async Task RequireOwnedCheckpointAsync(
        OutputScopeContext context,
        OutputCheckpointId checkpointId,
        string manifestFingerprint,
        CancellationToken cancellationToken)
    {
        ValidateSha256(manifestFingerprint, nameof(manifestFingerprint));
        var list = await context.Coordinator.ListCheckpointsAsync(cancellationToken).ConfigureAwait(false);
        var match = list.Checkpoints.FirstOrDefault(checkpoint => checkpoint.Id == checkpointId);
        if (match is null
            || match.ProjectId != context.ProjectId
            || match.GameFamily != context.GameFamily)
        {
            throw new OutputCheckpointNotFoundException(checkpointId);
        }

        if (!string.Equals(match.ManifestFingerprint, manifestFingerprint, StringComparison.Ordinal))
        {
            throw new OutputStateRevisionConflictException(
                new OutputStateRevision(manifestFingerprint),
                new OutputStateRevision(match.ManifestFingerprint));
        }
    }

    private static async Task<string> GetCurrentOutputModeAsync(
        OutputScopeContext context,
        CancellationToken cancellationToken,
        string fallback = "workspace")
    {
        var history = await context.Coordinator.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
        return history
            .Where(receipt => receipt.ProjectId == context.ProjectId && receipt.GameFamily == context.GameFamily)
            .Where(IsCurrentOutputModeReceipt)
            .OrderByDescending(receipt => receipt.CompletedAtUtc)
            .Select(receipt => receipt.OutputMode)
            .FirstOrDefault() ?? fallback;
    }

    private static bool IsCurrentOutputModeReceipt(OutputApplyReceipt receipt)
    {
        return receipt.Outcome == OutputApplyOutcome.Committed
            && !receipt.Origins.All(origin => origin.Kind == OutputApplyOriginKind.Cleanup);
    }

    internal static OutputScopeContext ResolveScope(OutputScopeDto? scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(scope.Paths);
        if (scope.ProjectId is null
            || scope.ProjectId.Length > ProjectRelocationService.MaximumProjectIdLength
            || string.IsNullOrWhiteSpace(scope.ProjectId)
            || scope.ProjectId != scope.ProjectId.Trim()
            || scope.ProjectId.Any(char.IsControl))
        {
            throw new OutputScopeMismatchException();
        }

        var paths = ProjectBridgeMapper.ToCore(scope.Paths);
        if (!HasBoundedProjectPathStrings(paths))
        {
            throw new OutputScopeMismatchException();
        }

        var projectId = ProjectIdentity.FromPaths(paths);
        if (projectId.Value != scope.ProjectId
            || paths.SelectedGame is not { } selectedGame
            || string.IsNullOrWhiteSpace(paths.OutputRootPath)
            || !Path.IsPathFullyQualified(paths.OutputRootPath))
        {
            throw new OutputScopeMismatchException();
        }

        string outputRoot;
        try
        {
            outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.OutputRootPath));
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            throw new OutputScopeMismatchException();
        }

        if (!IsSafeOutputRoot(outputRoot)
            || OverlapsSourceRoot(outputRoot, paths.BaseRomFsPath)
            || OverlapsSourceRoot(outputRoot, paths.BaseExeFsPath))
        {
            throw new OutputScopeMismatchException();
        }

        var outputRootKey = OperatingSystem.IsWindows()
            ? outputRoot.ToUpperInvariant()
            : outputRoot;
        var scopeKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"output-scope-v1\n{projectId.Value}\n{outputRootKey}")));
        return new OutputScopeContext(
            scopeKey,
            projectId,
            selectedGame.ToGameFamily(),
            paths,
            new OutputTransactionCoordinator(outputRoot, CoordinatorOptions));
    }

    private static bool HasBoundedProjectPathStrings(ProjectPaths paths)
    {
        return IsBoundedOptionalString(paths.BaseRomFsPath, ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(paths.BaseExeFsPath, ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(paths.OutputRootPath, ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(paths.SaveFilePath, ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(
                paths.ScarletVioletSupportFolderPath,
                ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(
                paths.PokemonLegendsZASupportFolderPath,
                ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(
                paths.GameTextLanguage,
                ProjectRelocationService.MaximumGameTextLanguageLength);
    }

    private static bool IsBoundedOptionalString(string? value, int maximumLength)
    {
        return value is null
            || (value.Length <= maximumLength && !value.Any(char.IsControl));
    }

    private static bool IsSafeOutputRoot(string outputRoot)
    {
        try
        {
            var directory = new DirectoryInfo(outputRoot);
            directory.Refresh();
            return directory.Exists
                && string.IsNullOrEmpty(directory.LinkTarget);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    private static bool OverlapsSourceRoot(string outputRoot, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        try
        {
            var normalizedSourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
            return IsContainedOrEqual(outputRoot, normalizedSourceRoot)
                || IsContainedOrEqual(normalizedSourceRoot, outputRoot);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return true;
        }
    }

    private static bool IsContainedOrEqual(string parentPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(parentPath, candidatePath);
        return !Path.IsPathRooted(relativePath)
            && relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static OutputRecoveryStatusDto ToDto(OutputRecoveryReport report)
    {
        var transactionCount = report.Transactions.Length;
        var pendingReconciliationCount = report.Transactions.Count(transaction =>
            transaction.Disposition is
                OutputRecoveryDisposition.FinalizeCommit or OutputRecoveryDisposition.RollBack);
        var remainingUnknownTargetCount = OutputSafetyContract.MaximumReturnedRecoveryUnknownTargets;
        var remainingUnknownTargetUtf8Bytes = OutputSafetyContract.MaximumRecoveryUnknownTargetUtf8Bytes;
        var transactions = new List<OutputRecoveryTransactionDto>();
        var prioritizedTransactions = report.Transactions
            .OrderBy(GetRecoveryDisplayPriority)
            .ThenBy(transaction => transaction.TransactionId.Value, StringComparer.Ordinal)
            .Take(OutputSafetyContract.MaximumReturnedEntries);
        foreach (var transaction in prioritizedTransactions)
        {
            var unknownTargets = new List<string>();
            foreach (var path in transaction.UnknownTargets)
            {
                if (remainingUnknownTargetCount == 0)
                {
                    break;
                }

                var pathUtf8Bytes = Encoding.UTF8.GetByteCount(path.Value);
                if (pathUtf8Bytes > remainingUnknownTargetUtf8Bytes)
                {
                    break;
                }

                unknownTargets.Add(path.Value);
                remainingUnknownTargetCount -= 1;
                remainingUnknownTargetUtf8Bytes -= pathUtf8Bytes;
            }

            transactions.Add(new OutputRecoveryTransactionDto(
                transaction.TransactionId.Value,
                ToDto(transaction.Phase),
                ToDto(transaction.Disposition),
                transaction.JournalReadable,
                transaction.UnknownTargets.Length,
                unknownTargets,
                unknownTargets.Count != transaction.UnknownTargets.Length));
        }

        return new OutputRecoveryStatusDto(
            report.Revision.Value,
            report.RequiresRecovery,
            transactionCount,
            pendingReconciliationCount,
            transactions,
            transactions.Count != transactionCount,
            CreateRecoveryDiagnostics(report));
    }

    private static int GetRecoveryDisplayPriority(OutputRecoveryTransactionStatus transaction)
    {
        if (!transaction.JournalReadable)
        {
            return -1;
        }

        if (transaction.Disposition == OutputRecoveryDisposition.RecoveryRequired
            || transaction.UnknownTargets.Length > 0)
        {
            return 0;
        }

        return transaction.Disposition is
            OutputRecoveryDisposition.FinalizeCommit or OutputRecoveryDisposition.RollBack
                ? 1
                : 2;
    }

    private static OutputCheckpointDto ToDto(OutputCheckpointSummary checkpoint)
    {
        return new OutputCheckpointDto(
            checkpoint.Id.Value,
            checkpoint.CreatedAtUtc,
            checkpoint.Label,
            checkpoint.FileCount,
            checkpoint.TotalBytes.ToString(CultureInfo.InvariantCulture),
            checkpoint.ManifestFingerprint,
            checkpoint.OutputMode,
            OutputCheckpointCoverageDto.KmOwnedOnly);
    }

    private static OutputHistoryReceiptDto ToHistoryDto(OutputApplyReceipt receipt)
    {
        return new OutputHistoryReceiptDto(
            receipt.TransactionId.Value,
            ToDto(receipt.Outcome),
            receipt.CompletedAtUtc,
            receipt.OutputMode,
            receipt.SemanticReviewHash,
            receipt.Targets.Length,
            receipt.Origins.Select(origin => new OutputApplyOriginDto(
                origin.Kind.ToString(),
                origin.Id)).ToArray(),
            receipt.OutcomeCode);
    }

    private static OutputTransactionResultDto ToDto(OutputApplyResult result)
    {
        return new OutputTransactionResultDto(
            result.TransactionId.Value,
            ToDto(result.Outcome),
            result.Receipt.CompletedAtUtc,
            result.Receipt.Targets.Length,
            result.Receipt.OutcomeCode);
    }

    private static ScopedIntegrityEntry CreateScopedIntegrityEntry(
        OutputIntegrityEntry entry,
        IReadOnlyDictionary<string, OutputOwnershipRecord> scopedOwnershipByPath,
        bool baselineUnknown)
    {
        var hasScopedOwnership = scopedOwnershipByPath.TryGetValue(entry.Path.CanonicalKey, out var ownership);
        var ownerIds = hasScopedOwnership
            ? ownership!.Claims
                .Select(claim => claim.OwnerId.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        return new ScopedIntegrityEntry(
            entry,
            ResolveScopedClassification(entry, hasScopedOwnership, baselineUnknown),
            ownerIds,
            IsCleanupCandidate(entry, ownership));
    }

    private static bool IsCleanupCandidate(
        OutputIntegrityEntry entry,
        OutputOwnershipRecord? ownership)
    {
        if (ownership is null
            || string.Equals(
                ownership.OutputMode,
                GameplayBundleDeploymentPlanner.OutputMode,
                StringComparison.Ordinal)
            || string.Equals(ownership.OutputMode, "za.standalone", StringComparison.Ordinal)
            && ownership.Path.CanonicalKey.StartsWith("ROMFS/", StringComparison.Ordinal))
        {
            return false;
        }

        if (entry.Classification == OutputIntegrityClassification.KmOwnedStale
            && entry.CurrentState is { Exists: false })
        {
            return true;
        }

        return entry.Classification == OutputIntegrityClassification.KmOwnedCurrent
            && ownership.FileDeleteEligible
            && ownership.Claims.Any(claim => claim.Address.ScopeKind == OwnedTargetScopeKind.File);
    }

    private static OutputIntegrityClassification ResolveScopedClassification(
        OutputIntegrityEntry entry,
        bool hasScopedOwnership,
        bool baselineUnknown)
    {
        if (!hasScopedOwnership
            && baselineUnknown
            && entry.Classification == OutputIntegrityClassification.Foreign)
        {
            return OutputIntegrityClassification.Unknown;
        }

        return !hasScopedOwnership
               && entry.Classification is
                   OutputIntegrityClassification.KmOwnedCurrent or
                   OutputIntegrityClassification.KmOwnedStale or
                   OutputIntegrityClassification.Conflicted
            ? OutputIntegrityClassification.Foreign
            : entry.Classification;
    }

    private static OutputIntegrityCountsDto ToCounts(
        IEnumerable<OutputIntegrityClassification> classifications)
    {
        var counts = classifications
            .GroupBy(classification => classification)
            .ToDictionary(group => group.Key, group => group.Count());
        return new OutputIntegrityCountsDto(
            counts.GetValueOrDefault(OutputIntegrityClassification.BaseEquivalent),
            counts.GetValueOrDefault(OutputIntegrityClassification.KmOwnedCurrent),
            counts.GetValueOrDefault(OutputIntegrityClassification.KmOwnedStale),
            counts.GetValueOrDefault(OutputIntegrityClassification.Foreign),
            counts.GetValueOrDefault(OutputIntegrityClassification.Conflicted),
            counts.GetValueOrDefault(OutputIntegrityClassification.Interrupted),
            counts.GetValueOrDefault(OutputIntegrityClassification.Unknown));
    }

    private static IReadOnlyList<ApiDiagnostic> CreateRecoveryDiagnostics(OutputRecoveryReport report)
    {
        if (report.Transactions.Any(transaction => !transaction.JournalReadable))
        {
            return
            [
                CreateDiagnostic(
                    ApiDiagnosticSeverity.Error,
                    "Output recovery metadata is unavailable and must be reviewed before writes can continue.",
                    "KM-OUTPUT-RECOVERY-METADATA-UNAVAILABLE",
                    "output.recovery"),
            ];
        }

        if (report.RequiresRecovery)
        {
            return
            [
                CreateDiagnostic(
                    ApiDiagnosticSeverity.Error,
                    "Output recovery requires manual review because at least one target has an unknown state.",
                    "KM-OUTPUT-RECOVERY-MANUAL-REQUIRED",
                    "output.recovery"),
            ];
        }

        if (report.Transactions.Any(transaction => transaction.Disposition is
                OutputRecoveryDisposition.FinalizeCommit or OutputRecoveryDisposition.RollBack))
        {
            return
            [
                CreateDiagnostic(
                    ApiDiagnosticSeverity.Warning,
                    "An interrupted output transaction can be reconciled using its verified journal.",
                    "KM-OUTPUT-RECOVERY-PENDING",
                    "output.recovery"),
            ];
        }

        return [];
    }

    private static IReadOnlyList<ApiDiagnostic> CreateIntegrityDiagnostics(
        IReadOnlyList<ScopedIntegrityEntry> entries,
        bool truncated)
    {
        var diagnostics = new List<ApiDiagnostic>();
        if (entries.Any(entry => entry.Classification == OutputIntegrityClassification.Conflicted))
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "One or more owned output files changed outside the reviewed writer.",
                "KM-OUTPUT-INTEGRITY-STALE",
                "output.integrity"));
        }

        if (entries.Any(entry => entry.Classification == OutputIntegrityClassification.Foreign))
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Info,
                "The output root contains unmanaged files. Cleanup and restore will preserve them.",
                "KM-OUTPUT-FOREIGN-DATA-PRESENT",
                "output.integrity"));
        }

        if (truncated)
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Warning,
                "The integrity summary is complete, but its displayed target list is truncated.",
                "KM-OUTPUT-HISTORY-TRUNCATED",
                "output.integrity"));
        }

        return diagnostics;
    }

    private static IReadOnlyList<ApiDiagnostic> CreateCleanupDiagnostics(
        IReadOnlyList<OutputCleanupEntryDto> entries)
    {
        return entries.Any(entry => entry.Disposition is
            OutputCleanupDispositionDto.Removed or OutputCleanupDispositionDto.ForgotMissing)
            ? []
            :
            [
                CreateDiagnostic(
                    ApiDiagnosticSeverity.Info,
                    "No selected output file still had enough ownership evidence to remove safely.",
                    "KM-OUTPUT-CLEANUP-NOTHING-SAFE",
                    "output.cleanup"),
            ];
    }

    private static ApiDiagnostic CreateDiagnostic(
        ApiDiagnosticSeverity severity,
        string message,
        string code,
        string domain)
    {
        return new ApiDiagnostic(severity, message, Domain: domain)
        {
            Code = code,
        };
    }

    private static string CreateTargetId(OutputStateRevision revision, RelativeOutputPath path)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"output-target-v1\n{revision.Value}\n{path.CanonicalKey}")));
    }

    private static string GetApplicationVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? typeof(OutputSafetyApplicationService).Assembly.GetName().Version?.ToString(3)
            ?? "unknown";
    }

    private void StoreIntegrityReview(CachedIntegrityReview review)
    {
        lock (reviewSyncRoot)
        {
            PruneExpiredReviewsLocked();
            integrityReviews[review.ScanId] = review;
            TrimOldestLocked(integrityReviews, MaximumCachedIntegrityScopes, item => item.ExpiresAtUtc);
        }
    }

    private void StoreCleanupPlan(CachedCleanupPlan plan)
    {
        lock (reviewSyncRoot)
        {
            PruneExpiredReviewsLocked();
            cleanupPlans[plan.PlanId] = plan;
            TrimOldestLocked(cleanupPlans, MaximumCachedPlans, item => item.ExpiresAtUtc);
        }
    }

    private void StoreRestorePlan(CachedRestorePlan plan)
    {
        lock (reviewSyncRoot)
        {
            PruneExpiredReviewsLocked();
            restorePlans[plan.PlanId] = plan;
            TrimOldestLocked(restorePlans, MaximumCachedPlans, item => item.ExpiresAtUtc);
        }
    }

    private void InvalidateReviews(string scopeKey)
    {
        lock (reviewSyncRoot)
        {
            foreach (var key in integrityReviews
                         .Where(item => item.Value.ScopeKey == scopeKey)
                         .Select(item => item.Key)
                         .ToArray())
            {
                integrityReviews.Remove(key);
            }

            foreach (var key in cleanupPlans
                         .Where(item => item.Value.ScopeKey == scopeKey)
                         .Select(item => item.Key)
                         .ToArray())
            {
                cleanupPlans.Remove(key);
            }

            foreach (var key in restorePlans
                         .Where(item => item.Value.ScopeKey == scopeKey)
                         .Select(item => item.Key)
                         .ToArray())
            {
                restorePlans.Remove(key);
            }
        }
    }

    private void PruneExpiredReviewsLocked()
    {
        var now = DateTimeOffset.UtcNow;
        RemoveWhere(integrityReviews, item => item.ExpiresAtUtc <= now);
        RemoveWhere(cleanupPlans, item => item.ExpiresAtUtc <= now);
        RemoveWhere(restorePlans, item => item.ExpiresAtUtc <= now);
    }

    private static void RemoveWhere<T>(Dictionary<string, T> values, Func<T, bool> predicate)
    {
        foreach (var key in values.Where(item => predicate(item.Value)).Select(item => item.Key).ToArray())
        {
            values.Remove(key);
        }
    }

    private static void TrimOldestLocked<T>(
        Dictionary<string, T> values,
        int maximumCount,
        Func<T, DateTimeOffset> getExpiry)
    {
        while (values.Count > maximumCount)
        {
            var oldest = values.MinBy(item => getExpiry(item.Value));
            values.Remove(oldest.Key);
        }
    }

    private static void ValidateTargetIds(IReadOnlyList<string> targetIds)
    {
        ArgumentNullException.ThrowIfNull(targetIds);
        if (targetIds.Count is 0 or > OutputSafetyContract.MaximumRequestedTargetIds)
        {
            throw new ArgumentException("The cleanup target selection is empty or too large.", nameof(targetIds));
        }

        foreach (var targetId in targetIds)
        {
            ValidateSha256(targetId, nameof(targetIds));
        }
    }

    private static void ValidatePlanId(string planId)
    {
        if (planId is null
            || planId.Length != 32
            || planId.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new OutputReviewExpiredException();
        }
    }

    private static void ValidateTransactionId(string transactionId)
    {
        _ = new OutputTransactionId(transactionId);
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        _ = new OutputStateRevision(value ?? throw new ArgumentNullException(parameterName));
    }

    private static void ValidateCheckpointLabel(string? label)
    {
        if (label is not null
            && (label.Length == 0
                || label != label.Trim()
                || label.Length > OutputSafetyContract.MaximumCheckpointLabelLength
                || label.Any(char.IsControl)))
        {
            throw new ArgumentException("The checkpoint label is invalid.", nameof(label));
        }
    }

    private static OutputTransactionPhaseDto ToDto(OutputTransactionPhase value) => value switch
    {
        OutputTransactionPhase.Preparing => OutputTransactionPhaseDto.Preparing,
        OutputTransactionPhase.Prepared => OutputTransactionPhaseDto.Prepared,
        OutputTransactionPhase.Committing => OutputTransactionPhaseDto.Committing,
        OutputTransactionPhase.Committed => OutputTransactionPhaseDto.Committed,
        OutputTransactionPhase.RollingBack => OutputTransactionPhaseDto.RollingBack,
        OutputTransactionPhase.RolledBack => OutputTransactionPhaseDto.RolledBack,
        OutputTransactionPhase.RecoveryRequired => OutputTransactionPhaseDto.RecoveryRequired,
        OutputTransactionPhase.Finalizing => OutputTransactionPhaseDto.Finalizing,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static OutputRecoveryDispositionDto ToDto(OutputRecoveryDisposition value) => value switch
    {
        OutputRecoveryDisposition.NoAction => OutputRecoveryDispositionDto.NoAction,
        OutputRecoveryDisposition.FinalizeCommit => OutputRecoveryDispositionDto.FinalizeCommit,
        OutputRecoveryDisposition.RollBack => OutputRecoveryDispositionDto.RollBack,
        OutputRecoveryDisposition.RecoveryRequired => OutputRecoveryDispositionDto.RecoveryRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static OutputIntegrityClassificationDto ToDto(OutputIntegrityClassification value) => value switch
    {
        OutputIntegrityClassification.BaseEquivalent => OutputIntegrityClassificationDto.BaseEquivalent,
        OutputIntegrityClassification.KmOwnedCurrent => OutputIntegrityClassificationDto.KmOwnedCurrent,
        OutputIntegrityClassification.KmOwnedStale => OutputIntegrityClassificationDto.KmOwnedStale,
        OutputIntegrityClassification.Foreign => OutputIntegrityClassificationDto.Foreign,
        OutputIntegrityClassification.Conflicted => OutputIntegrityClassificationDto.Conflicted,
        OutputIntegrityClassification.Interrupted => OutputIntegrityClassificationDto.Interrupted,
        OutputIntegrityClassification.Unknown => OutputIntegrityClassificationDto.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static OutputCleanupDispositionDto ToDto(OutputCleanupDisposition value) => value switch
    {
        OutputCleanupDisposition.Removed => OutputCleanupDispositionDto.Removed,
        OutputCleanupDisposition.NotOwned => OutputCleanupDispositionDto.NotOwned,
        OutputCleanupDisposition.FingerprintMismatch => OutputCleanupDispositionDto.FingerprintMismatch,
        OutputCleanupDisposition.Missing => OutputCleanupDispositionDto.Missing,
        OutputCleanupDisposition.ApplyNotCommitted => OutputCleanupDispositionDto.ApplyNotCommitted,
        OutputCleanupDisposition.ForgotMissing => OutputCleanupDispositionDto.ForgotMissing,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static OutputApplyOutcomeDto ToDto(OutputApplyOutcome value) => value switch
    {
        OutputApplyOutcome.Committed => OutputApplyOutcomeDto.Committed,
        OutputApplyOutcome.RolledBack => OutputApplyOutcomeDto.RolledBack,
        OutputApplyOutcome.RecoveryRequired => OutputApplyOutcomeDto.RecoveryRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static string ToGameFamilyKey(GameFamily value) => value switch
    {
        GameFamily.SwordShield => "swordShield",
        GameFamily.ScarletViolet => "scarletViolet",
        GameFamily.LegendsZA => "pokemonLegendsZA",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private sealed record OutputBaselineSnapshot(
        IReadOnlyList<OutputBaselineEntry> Entries,
        IReadOnlySet<string> UnknownPathKeys)
    {
        public static OutputBaselineSnapshot Empty { get; } = new(
            [],
            new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed record ScopedIntegrityEntry(
        OutputIntegrityEntry Entry,
        OutputIntegrityClassification Classification,
        IReadOnlyList<string> OwnerIds,
        bool CleanupCandidate);

    private sealed record CachedIntegrityTarget(
        RelativeOutputPath Path,
        OutputIntegrityClassificationDto Classification,
        bool CleanupEligible,
        string? SizeBytes,
        IReadOnlyList<string> OwnerIds);

    private sealed record CachedIntegrityReview(
        string ScopeKey,
        string ScanId,
        OutputStateRevision Revision,
        IReadOnlyDictionary<string, CachedIntegrityTarget> Targets,
        DateTimeOffset ExpiresAtUtc);

    private sealed record CachedCleanupPlan(
        string PlanId,
        string ScopeKey,
        OutputStateRevision ExpectedRevision,
        ImmutableArray<RelativeOutputPath> Paths,
        ImmutableArray<OutputCleanupCandidateDto> Candidates,
        DateTimeOffset ExpiresAtUtc);

    private sealed record CachedRestorePlan(
        string PlanId,
        string ScopeKey,
        OutputCheckpointId CheckpointId,
        string ManifestFingerprint,
        OutputStateRevision OutputRevision,
        DateTimeOffset ExpiresAtUtc);
}

internal sealed record OutputScopeContext(
    string ScopeKey,
    ProjectId ProjectId,
    GameFamily GameFamily,
    ProjectPaths Paths,
    OutputTransactionCoordinator Coordinator);

public sealed class OutputScopeMismatchException : Exception
{
    public OutputScopeMismatchException()
        : base("The output request no longer matches the active project.")
    {
    }
}

public sealed class OutputReviewExpiredException : Exception
{
    public OutputReviewExpiredException()
        : base("The reviewed output state is no longer available. Review it again before applying.")
    {
    }
}

public sealed class OutputOwnershipUnprovenException : Exception
{
    public OutputOwnershipUnprovenException()
        : base("The selected output targets are not proven current and owned.")
    {
    }
}

public sealed class OutputCheckpointAlreadyCurrentException : Exception
{
    public OutputCheckpointAlreadyCurrentException()
        : base("The selected checkpoint already matches the managed output files.")
    {
    }
}
