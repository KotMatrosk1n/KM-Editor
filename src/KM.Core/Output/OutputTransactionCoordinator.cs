// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Output;

/// <summary>
/// Serializes reviewed output mutations through a durable same-volume journal.
/// Callers retain ownership of game-specific preparation and postimage meaning.
/// </summary>
public sealed class OutputTransactionCoordinator
{
    private readonly OutputPathSafety paths;
    private readonly OutputMetadataStore metadata;
    private readonly OutputTransactionCoordinatorOptions options;
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public OutputTransactionCoordinator(
        string outputRoot,
        OutputTransactionCoordinatorOptions? options = null)
    {
        this.options = options ?? new OutputTransactionCoordinatorOptions();
        this.options.Validate();
        paths = new OutputPathSafety(outputRoot);
        metadata = new OutputMetadataStore(paths);
    }

    public async Task<OutputApplyResult> ApplyAsync(
        OutputApplyPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePlanLimits(plan);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await RecoverCoreAsync(expectedRevision: null, CancellationToken.None).ConfigureAwait(false);
            if (HasBlockingRecoveryMaterial(recovery))
            {
                throw new OutputRecoveryRequiredException(recovery);
            }

            return await ExecutePlanCoreAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    /// <summary>
    /// Runs one synchronous output operation while holding the recovery and output-root lock.
    /// The operation starts only when no interrupted transaction remains actionable or blocked.
    /// </summary>
    public async Task<TResult> ExecuteExclusiveOutputOperationAsync<TResult>(
        Func<TResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await RecoverCoreAsync(expectedRevision: null, CancellationToken.None).ConfigureAwait(false);
            if (HasBlockingRecoveryMaterial(recovery))
            {
                throw new OutputRecoveryRequiredException(recovery);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return operation();
        }
        finally
        {
            operationGate.Release();
        }
    }

    /// <summary>
    /// Runs one asynchronous output operation while holding the recovery and output-root lock.
    /// The operation starts only when no interrupted transaction remains actionable or blocked.
    /// </summary>
    public async Task<TResult> ExecuteExclusiveOutputOperationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await RecoverCoreAsync(expectedRevision: null, CancellationToken.None).ConfigureAwait(false);
            if (HasBlockingRecoveryMaterial(recovery))
            {
                throw new OutputRecoveryRequiredException(recovery);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    /// <summary>
    /// Inspects interrupted transactions without changing journal phases or output files.
    /// </summary>
    public async Task<OutputRecoveryReport> InspectRecoveryAsync(
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            return await InspectRecoveryCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task<OutputRecoveryReport> GetRecoveryStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return InspectRecoveryAsync(cancellationToken);
    }

    /// <summary>
    /// Finalizes all-postimage transactions and rolls back known pre/post mixtures.
    /// Unknown target states are retained and reported without being overwritten.
    /// </summary>
    public async Task<OutputRecoveryReport> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        return await RecoverAsync(expectedRevision: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OutputRecoveryReport> RecoverAsync(
        OutputStateRevision expectedRevision,
        CancellationToken cancellationToken = default)
    {
        return await RecoverAsync((OutputStateRevision?)expectedRevision, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OutputRecoveryReport> RecoverAsync(
        OutputStateRevision? expectedRevision,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            return await RecoverCoreAsync(expectedRevision, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<ImmutableArray<OutputApplyReceipt>> GetHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            return (await ReadHistoryAsync(cancellationToken).ConfigureAwait(false)).Receipts;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputApplyHistory> GetHistorySnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var history = await ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
            return new OutputApplyHistory(ComputeHistoryRevision(history), history.Receipts);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputOwnershipInventory> GetOwnershipInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            return await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputOwnershipInventorySnapshot> GetOwnershipInventorySnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var inventory = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
            return new OutputOwnershipInventorySnapshot(ComputeInventoryRevision(inventory), inventory);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputIntegrityReport> ScanIntegrityAsync(
        IEnumerable<OutputBaselineEntry>? baseline = null,
        CancellationToken cancellationToken = default)
    {
        var baselineByPath = ValidateBaseline(baseline);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            return await ScanIntegrityCoreAsync(baselineByPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputDirectoryMembershipSnapshot> CaptureDirectoryMembershipAsync(
        RelativeOutputPath directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            return CaptureDirectoryMembershipCore(directory);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private OutputDirectoryMembershipSnapshot CaptureDirectoryMembershipCore(
        RelativeOutputPath directory)
    {
        var exists = paths.OwnedDirectoryExists(directory);
        var entries = exists
            ? paths.EnumerateDirectoryMembership(directory, options.MaximumIntegrityEntries)
                .OrderBy(entry => entry.Path.CanonicalKey, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<OutputDirectoryMembershipEntry>.Empty;
        var tokens = new List<string?>
        {
            directory.CanonicalKey,
            exists ? "1" : "0",
        };
        foreach (var entry in entries)
        {
            tokens.Add(entry.Path.CanonicalKey);
            tokens.Add(entry.IsDirectory ? "D" : "F");
        }

        return new OutputDirectoryMembershipSnapshot(
            directory,
            exists,
            OutputRevisionCalculator.FromTokens("output-directory-membership-v1", tokens),
            entries);
    }

    private async Task<OutputIntegrityReport> ScanIntegrityCoreAsync(
        IReadOnlyDictionary<string, OutputBaselineEntry> baselineByPath,
        CancellationToken cancellationToken)
    {
        var inventory = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
        var interruptedPaths = await GetInterruptedPathKeysAsync(cancellationToken).ConfigureAwait(false);
        var ownershipByPath = inventory.Files.ToDictionary(
            record => record.Path.CanonicalKey,
            StringComparer.Ordinal);
        var entries = ImmutableArray.CreateBuilder<OutputIntegrityEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths.EnumerateOrdinaryFiles(options.MaximumIntegrityEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(path.CanonicalKey))
            {
                throw new OutputPathSecurityException();
            }
            if (interruptedPaths.Contains(path.CanonicalKey))
            {
                entries.Add(new OutputIntegrityEntry(
                    path,
                    OutputIntegrityClassification.Interrupted,
                    await TryFingerprintTargetAsync(path, cancellationToken).ConfigureAwait(false),
                    ownershipByPath.GetValueOrDefault(path.CanonicalKey)?.CurrentState));
                continue;
            }

            var current = await TryFingerprintTargetAsync(path, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                entries.Add(new OutputIntegrityEntry(path, OutputIntegrityClassification.Unknown, null, null));
                continue;
            }

            if (ownershipByPath.TryGetValue(path.CanonicalKey, out var owned))
            {
                entries.Add(new OutputIntegrityEntry(
                    path,
                    current == owned.CurrentState
                        ? OutputIntegrityClassification.KmOwnedCurrent
                        : OutputIntegrityClassification.Conflicted,
                    current,
                    owned.CurrentState));
                continue;
            }

            var classification = baselineByPath.TryGetValue(path.CanonicalKey, out var baselineState)
                                 && current == baselineState.State
                ? OutputIntegrityClassification.BaseEquivalent
                : OutputIntegrityClassification.Foreign;
            entries.Add(new OutputIntegrityEntry(path, classification, current, null));
        }

        foreach (var owned in inventory.Files)
        {
            if (seen.Contains(owned.Path.CanonicalKey))
            {
                continue;
            }

            if (entries.Count == options.MaximumIntegrityEntries)
            {
                throw new OutputLimitExceededException(
                    $"The output integrity report cannot exceed {options.MaximumIntegrityEntries} entries.");
            }

            entries.Add(new OutputIntegrityEntry(
                owned.Path,
                interruptedPaths.Contains(owned.Path.CanonicalKey)
                    ? OutputIntegrityClassification.Interrupted
                    : OutputIntegrityClassification.KmOwnedStale,
                OutputFileState.Missing,
                owned.CurrentState));
        }

        var ordered = entries
            .OrderBy(entry => entry.Path.CanonicalKey, StringComparer.Ordinal)
            .ToImmutableArray();
        return new OutputIntegrityReport(
            ComputeIntegrityRevision(ordered),
            ordered,
            DateTimeOffset.UtcNow);
    }

    public async Task<OutputCleanupResult> CleanupOwnedAsync(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        IEnumerable<RelativeOutputPath> targets,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId.Value))
        {
            throw new ArgumentException("Cleanup requires a project id.", nameof(projectId));
        }

        SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        var requested = ValidateRequestedPaths(targets);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await RecoverCoreAsync(expectedRevision: null, CancellationToken.None).ConfigureAwait(false);
            if (HasBlockingRecoveryMaterial(recovery))
            {
                throw new OutputRecoveryRequiredException(recovery);
            }

            var inventory = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
            var ownedByPath = inventory.Files
                .Where(record => OwnershipScopeMatches(record, projectId, gameFamily))
                .ToDictionary(record => record.Path.CanonicalKey, StringComparer.Ordinal);
            var entries = new List<OutputCleanupEntry>();
            var mutations = new List<OutputMutation>();
            var missingOwnedPaths = new List<RelativeOutputPath>();
            foreach (var path in requested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ownedByPath.TryGetValue(path.CanonicalKey, out var owned))
                {
                    entries.Add(new OutputCleanupEntry(path, OutputCleanupDisposition.NotOwned));
                    continue;
                }

                if (RequiresProviderCoordinatedMutation(owned.OutputMode, owned.Path))
                {
                    entries.Add(new OutputCleanupEntry(path, OutputCleanupDisposition.NotOwned));
                    continue;
                }

                var current = await ComputeTargetStateAsync(path, cancellationToken).ConfigureAwait(false);
                if (!current.Exists)
                {
                    missingOwnedPaths.Add(path);
                    continue;
                }

                if (current != owned.CurrentState)
                {
                    entries.Add(new OutputCleanupEntry(path, OutputCleanupDisposition.FingerprintMismatch));
                    continue;
                }

                if (!owned.FileDeleteEligible || !HasWholeFileOwnership(owned.Claims))
                {
                    entries.Add(new OutputCleanupEntry(path, OutputCleanupDisposition.NotOwned));
                    continue;
                }

                mutations.Add(OutputMutation.Delete(
                    path,
                    current,
                    owned.Claims,
                    owned.OutputMode));
            }

            if (mutations.Count == 0)
            {
                var forgotten = await RemoveMissingOwnershipRecordsAsync(
                        projectId,
                        gameFamily,
                        missingOwnedPaths,
                        cancellationToken)
                    .ConfigureAwait(false);
                entries.AddRange(missingOwnedPaths.Select(path => new OutputCleanupEntry(
                    path,
                    forgotten.Contains(path.CanonicalKey)
                        ? OutputCleanupDisposition.ForgotMissing
                        : OutputCleanupDisposition.FingerprintMismatch)));
                await PruneOwnedDirectoriesAsync(
                        projectId,
                        gameFamily,
                        missingOwnedPaths.Where(path => forgotten.Contains(path.CanonicalKey)),
                        cancellationToken)
                    .ConfigureAwait(false);
                return new OutputCleanupResult(entries, applyResult: null);
            }

            var plan = new OutputApplyPlan(
                projectId,
                gameFamily,
                outputMode,
                OutputReviewFingerprint.FromMutations(mutations),
                [new OutputApplyOrigin(OutputApplyOriginKind.Cleanup, "owned-output-cleanup")],
                mutations);
            var result = await ExecutePlanCoreAsync(plan, cancellationToken).ConfigureAwait(false);
            var disposition = result.Outcome == OutputApplyOutcome.Committed
                ? OutputCleanupDisposition.Removed
                : OutputCleanupDisposition.ApplyNotCommitted;
            if (result.Outcome == OutputApplyOutcome.Committed)
            {
                var forgotten = await RemoveMissingOwnershipRecordsAsync(
                        projectId,
                        gameFamily,
                        missingOwnedPaths,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                entries.AddRange(missingOwnedPaths.Select(path => new OutputCleanupEntry(
                    path,
                    forgotten.Contains(path.CanonicalKey)
                        ? OutputCleanupDisposition.ForgotMissing
                        : OutputCleanupDisposition.FingerprintMismatch)));
                await PruneOwnedDirectoriesAsync(
                        projectId,
                        gameFamily,
                        mutations.Select(mutation => mutation.Path).Concat(
                            missingOwnedPaths.Where(path => forgotten.Contains(path.CanonicalKey))),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                entries.AddRange(missingOwnedPaths.Select(path => new OutputCleanupEntry(
                    path,
                    OutputCleanupDisposition.Missing)));
            }

            entries.AddRange(mutations.Select(mutation => new OutputCleanupEntry(mutation.Path, disposition)));
            return new OutputCleanupResult(entries, result);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task<OutputCheckpointSummary> CreateCheckpointAsync(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        return CreateCheckpointAsync(
            projectId,
            gameFamily,
            outputMode,
            label,
            expectedOutputRevision: null,
            cancellationToken);
    }

    public Task<OutputCheckpointSummary> CreateCheckpointAsync(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        string? label,
        OutputStateRevision expectedOutputRevision,
        CancellationToken cancellationToken = default)
    {
        return CreateCheckpointAsync(
            projectId,
            gameFamily,
            outputMode,
            label,
            (OutputStateRevision?)expectedOutputRevision,
            cancellationToken);
    }

    private async Task<OutputCheckpointSummary> CreateCheckpointAsync(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        string? label,
        OutputStateRevision? expectedOutputRevision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId.Value))
        {
            throw new ArgumentException("Checkpoint creation requires a project id.", nameof(projectId));
        }

        SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        if (label is { Length: > 256 } || label?.Any(char.IsControl) is true)
        {
            throw new ArgumentException("A checkpoint label is invalid or too large.", nameof(label));
        }

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await RecoverCoreAsync(expectedRevision: null, CancellationToken.None).ConfigureAwait(false);
            if (HasBlockingRecoveryMaterial(recovery))
            {
                throw new OutputRecoveryRequiredException(recovery);
            }

            var integrity = await ScanIntegrityCoreAsync(
                    new Dictionary<string, OutputBaselineEntry>(StringComparer.Ordinal),
                    cancellationToken)
                .ConfigureAwait(false);
            if (expectedOutputRevision is { } expected && expected != integrity.Revision)
            {
                throw new OutputStateRevisionConflictException(expected, integrity.Revision);
            }

            var inventory = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
            return await CreateCheckpointCoreAsync(
                    projectId,
                    gameFamily,
                    outputMode,
                    label,
                    inventory,
                    integrity,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputCheckpointList> ListCheckpointsAsync(
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            return await ListCheckpointsCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task<OutputCheckpointRestorePreview> PreviewCheckpointRestoreAsync(
        OutputCheckpointId checkpointId,
        CancellationToken cancellationToken = default)
    {
        return PreviewCheckpointRestoreEntryAsync(
            checkpointId,
            expectedManifestFingerprint: null,
            cancellationToken);
    }

    public Task<OutputCheckpointRestorePreview> PreviewCheckpointRestoreAsync(
        OutputCheckpointId checkpointId,
        string expectedManifestFingerprint,
        CancellationToken cancellationToken = default)
    {
        return PreviewCheckpointRestoreEntryAsync(
            checkpointId,
            (string?)expectedManifestFingerprint,
            cancellationToken);
    }

    private async Task<OutputCheckpointRestorePreview> PreviewCheckpointRestoreEntryAsync(
        OutputCheckpointId checkpointId,
        string? expectedManifestFingerprint,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var manifest = await ReadCheckpointManifestAsync(checkpointId, cancellationToken).ConfigureAwait(false);
            ValidateExpectedManifestFingerprint(manifest, expectedManifestFingerprint);
            var restore = await BuildCheckpointRestoreAsync(manifest, includePostimages: false, cancellationToken)
                .ConfigureAwait(false);
            return restore.Preview;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputCheckpointRestoreResult> RestoreCheckpointAsync(
        OutputCheckpointId checkpointId,
        string expectedManifestFingerprint,
        OutputStateRevision expectedOutputRevision,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await RecoverCoreAsync(expectedRevision: null, CancellationToken.None).ConfigureAwait(false);
            if (HasBlockingRecoveryMaterial(recovery))
            {
                throw new OutputRecoveryRequiredException(recovery);
            }

            var manifest = await ReadCheckpointManifestAsync(checkpointId, cancellationToken).ConfigureAwait(false);
            ValidateExpectedManifestFingerprint(manifest, expectedManifestFingerprint);
            var restore = await BuildCheckpointRestoreAsync(manifest, includePostimages: true, cancellationToken)
                .ConfigureAwait(false);
            if (restore.Preview.OutputRevision != expectedOutputRevision)
            {
                throw new OutputCheckpointConflictException(checkpointId);
            }

            if (restore.Mutations.IsEmpty)
            {
                return new OutputCheckpointRestoreResult(restore.Preview, ApplyResult: null);
            }

            var plan = new OutputApplyPlan(
                manifest.Summary.ProjectId,
                manifest.Summary.GameFamily,
                manifest.Summary.OutputMode,
                OutputReviewFingerprint.FromMutations(restore.Mutations),
                [new OutputApplyOrigin(OutputApplyOriginKind.Checkpoint, checkpointId.Value)],
                restore.Mutations,
                restore.ReadDependencies);
            var result = await ExecutePlanCoreAsync(plan, cancellationToken).ConfigureAwait(false);
            return new OutputCheckpointRestoreResult(restore.Preview, result);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputCheckpointDeleteResult> DeleteCheckpointAsync(
        OutputCheckpointId checkpointId,
        string expectedManifestFingerprint,
        OutputStateRevision expectedListRevision,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await RecoverCoreAsync(expectedRevision: null, CancellationToken.None)
                .ConfigureAwait(false);
            if (HasBlockingRecoveryMaterial(recovery))
            {
                throw new OutputRecoveryRequiredException(recovery);
            }

            var current = await ListCheckpointsCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedListRevision)
            {
                throw new OutputCheckpointConflictException(checkpointId);
            }

            var summary = current.Checkpoints.FirstOrDefault(item => item.Id == checkpointId);
            if (summary is null)
            {
                return new OutputCheckpointDeleteResult(Deleted: false, current.Revision);
            }

            var expectedFingerprint = SemanticContractGuards.Sha256Fingerprint(
                expectedManifestFingerprint,
                nameof(expectedManifestFingerprint));
            if (!string.Equals(summary.ManifestFingerprint, expectedFingerprint, StringComparison.Ordinal))
            {
                throw new OutputCheckpointConflictException(checkpointId);
            }

            RetireCheckpointDirectory(
                checkpointId,
                paths.ResolveCheckpointDirectory(checkpointId));
            var updated = await ListCheckpointsCoreAsync(CancellationToken.None).ConfigureAwait(false);
            return new OutputCheckpointDeleteResult(Deleted: true, updated.Revision);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OutputSupportReport> CreateSupportReportAsync(
        ProjectId projectId,
        string applicationVersion,
        GameFamily gameFamily,
        string outputMode,
        IEnumerable<string> diagnosticCodes,
        CancellationToken cancellationToken = default)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureMetadataLayout();
            await using var outputLock = await AcquireOutputRootLockAsync(cancellationToken).ConfigureAwait(false);
            var scopedRecoveryPhases = ImmutableArray.CreateBuilder<OutputTransactionPhase>();
            var scopedRecoveryRequiresManualReview = false;
            foreach (var transaction in await DiscoverTransactionsAsync(
                         scavengeRetiredMaterial: false,
                         cancellationToken).ConfigureAwait(false))
            {
                if (transaction.Journal is not { } journal)
                {
                    scopedRecoveryPhases.Add(OutputTransactionPhase.RecoveryRequired);
                    scopedRecoveryRequiresManualReview = true;
                    continue;
                }

                if (journal.ProjectId != projectId
                    || journal.GameFamily != gameFamily)
                {
                    continue;
                }

                scopedRecoveryPhases.Add(journal.Phase);
                var classification = await ClassifyRecoveryAsync(
                        journal,
                        transaction.Directory,
                        cancellationToken)
                    .ConfigureAwait(false);
                scopedRecoveryRequiresManualReview |=
                    classification.Status.Disposition == OutputRecoveryDisposition.RecoveryRequired;
            }

            var integrity = await ScanIntegrityCoreAsync(
                    new Dictionary<string, OutputBaselineEntry>(StringComparer.Ordinal),
                    cancellationToken)
                .ConfigureAwait(false);
            var inventory = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
            var checkpoints = await ListCheckpointsCoreAsync(cancellationToken).ConfigureAwait(false);
            var history = await ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
            var ownedPathKeys = inventory.Files
                .Where(record => OwnershipScopeMatches(record, projectId, gameFamily))
                .Select(record => record.Path.CanonicalKey)
                .ToHashSet(StringComparer.Ordinal);
            var scopedIntegrity = integrity.Entries
                .Where(entry => ownedPathKeys.Contains(entry.Path.CanonicalKey))
                .ToImmutableArray();
            var derivedCodes = new List<string>();
            if (scopedIntegrity.Any(entry => entry.Classification is
                    OutputIntegrityClassification.KmOwnedStale or
                    OutputIntegrityClassification.Conflicted or
                    OutputIntegrityClassification.Unknown))
            {
                derivedCodes.Add(OutputSupportDiagnosticCodes.IntegrityStale);
            }

            if (scopedRecoveryRequiresManualReview
                || scopedIntegrity.Any(entry =>
                    entry.Classification == OutputIntegrityClassification.Interrupted))
            {
                derivedCodes.Add(OutputSupportDiagnosticCodes.RecoveryManualRequired);
            }

            var integrityCounts = Enum.GetValues<OutputIntegrityClassification>()
                .Select(classification => new OutputIntegrityCount(
                    classification,
                    scopedIntegrity.Count(entry => entry.Classification == classification)));
            return new OutputSupportReport(
                applicationVersion,
                gameFamily,
                outputMode,
                diagnosticCodes.Concat(derivedCodes),
                scopedRecoveryPhases,
                integrityCounts,
                inventory.Files.Count(record => OwnershipScopeMatches(record, projectId, gameFamily)),
                checkpoints.Checkpoints.Count(checkpoint =>
                    checkpoint.ProjectId == projectId && checkpoint.GameFamily == gameFamily),
                history.Receipts.Count(receipt =>
                    receipt.ProjectId == projectId && receipt.GameFamily == gameFamily),
                DateTimeOffset.UtcNow);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task<OutputCheckpointSummary> CreateCheckpointCoreAsync(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        string? label,
        OutputOwnershipInventory inventory,
        OutputIntegrityReport integrity,
        CancellationToken cancellationToken)
    {
        var scopeFiles = inventory.Files
            .Where(record => OwnershipScopeMatches(record, projectId, gameFamily))
            .OrderBy(record => record.Path.CanonicalKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (scopeFiles.IsEmpty)
        {
            throw new OutputCoordinatorException(
                "A checkpoint requires at least one managed output file in the requested scope.");
        }

        if (scopeFiles.Length > options.MaximumMutationsPerApply)
        {
            throw new OutputLimitExceededException(
                "The managed output contains too many files for one restorable checkpoint.");
        }

        var integrityByPath = integrity.Entries.ToDictionary(
            entry => entry.Path.CanonicalKey,
            StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var owned in scopeFiles)
        {
            if (!integrityByPath.TryGetValue(owned.Path.CanonicalKey, out var entry)
                || entry.Classification != OutputIntegrityClassification.KmOwnedCurrent
                || entry.CurrentState != owned.CurrentState)
            {
                throw new OutputPreimageConflictException(owned.Path);
            }

            if (owned.CurrentState.LengthBytes > options.MaximumWriteBytesPerMutation)
            {
                throw new OutputLimitExceededException(
                    "A managed output file is too large for a restorable checkpoint.");
            }

            totalBytes = checked(totalBytes + owned.CurrentState.LengthBytes);
            if (totalBytes > options.MaximumCheckpointBytes
                || totalBytes > options.MaximumWriteBytesPerApply)
            {
                throw new OutputLimitExceededException(
                    "The managed output exceeds the configured checkpoint restore limit.");
            }
        }

        var existing = await ReadCheckpointManifestsAsync(
                removeIncomplete: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing.Length == OutputLimits.MaximumCheckpoints)
        {
            throw new OutputLimitExceededException(
                "Global checkpoint capacity cannot safely reserve a new checkpoint.");
        }

        var existingInScope = existing
            .Where(manifest => manifest.Summary.ProjectId == projectId
                               && manifest.Summary.GameFamily == gameFamily)
            .OrderBy(manifest => manifest.Summary.CreatedAtUtc)
            .ThenBy(manifest => manifest.Summary.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        var checkpointId = OutputCheckpointId.New();
        var checkpointDirectory = paths.ResolveCheckpointDirectory(checkpointId);
        paths.CreateMetadataDirectory(checkpointDirectory, paths.CheckpointsRoot);
        var contentDirectory = paths.GetContainedMetadataPath(checkpointDirectory, "content");
        paths.EnsureMetadataDirectory(contentDirectory, checkpointDirectory);
        var manifestPublished = false;
        try
        {
            var entries = ImmutableArray.CreateBuilder<OutputCheckpointEntry>(scopeFiles.Length);
            var index = 0;
            foreach (var owned in scopeFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var contentFileName = $"{index:D6}.content.bin";
                var contentPath = paths.GetContainedMetadataPath(contentDirectory, contentFileName);
                await CopyTargetToCheckpointAsync(owned, contentPath, cancellationToken).ConfigureAwait(false);
                entries.Add(new OutputCheckpointEntry(
                    owned.Path,
                    owned.CurrentState,
                    owned.Claims,
                    owned.OutputMode,
                    owned.FileDeleteEligible,
                    contentFileName));
                index++;
            }

            var createdAtUtc = DateTimeOffset.UtcNow;
            var immutableEntries = entries.ToImmutable();
            var manifestFingerprint = ComputeCheckpointManifestFingerprint(
                checkpointId,
                projectId,
                gameFamily,
                outputMode,
                createdAtUtc,
                label,
                immutableEntries);
            var summary = new OutputCheckpointSummary(
                checkpointId,
                projectId,
                gameFamily,
                outputMode,
                createdAtUtc,
                immutableEntries.Length,
                totalBytes,
                manifestFingerprint,
                OutputCheckpointCoverage.OwnedFiles,
                label);
            var manifest = new OutputCheckpointManifest(
                OutputCheckpointManifest.CurrentSchemaVersion,
                summary,
                immutableEntries);
            var manifestPath = paths.GetContainedMetadataPath(checkpointDirectory, "manifest.json");
            await metadata.WriteJsonAtomicAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
            manifestPublished = true;
            ValidateCheckpointManifest(manifest, checkpointId, checkpointDirectory);
            foreach (var expired in existingInScope.Take(
                         Math.Max(0, existingInScope.Length - options.MaximumCheckpoints + 1)))
            {
                RetireCheckpointAfterPublicationBestEffort(
                    expired.Summary.Id,
                    paths.ResolveCheckpointDirectory(expired.Summary.Id));
            }

            return summary;
        }
        catch
        {
            if (!manifestPublished)
            {
                ValidateIncompleteCheckpointForCleanup(checkpointDirectory);
                RetireCheckpointDirectory(checkpointId, checkpointDirectory);
            }

            throw;
        }
    }

    private async Task<OutputCheckpointList> ListCheckpointsCoreAsync(CancellationToken cancellationToken)
    {
        var manifests = await ReadCheckpointManifestsAsync(removeIncomplete: true, cancellationToken)
            .ConfigureAwait(false);
        var summaries = manifests
            .Select(manifest => manifest.Summary)
            .OrderByDescending(summary => summary.CreatedAtUtc)
            .ThenBy(summary => summary.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        return new OutputCheckpointList(ComputeCheckpointListRevision(summaries), summaries);
    }

    private async Task<ImmutableArray<OutputCheckpointManifest>> ReadCheckpointManifestsAsync(
        bool removeIncomplete,
        CancellationToken cancellationToken)
    {
        paths.EnsureMetadataLayout();
        var manifests = ImmutableArray.CreateBuilder<OutputCheckpointManifest>();
        var inspectedDirectories = 0;
        foreach (var directory in Directory.EnumerateDirectories(
                     paths.CheckpointsRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inspectedDirectories == OutputLimits.MaximumCheckpoints + 1)
            {
                throw new OutputLimitExceededException("Too many output checkpoint directories were discovered.");
            }

            inspectedDirectories++;
            var directoryName = Path.GetFileName(directory);
            if (TryParseRetiredCheckpointName(directoryName, out _))
            {
                if (removeIncomplete)
                {
                    paths.DeleteMetadataTree(directory);
                }

                continue;
            }

            OutputCheckpointId checkpointId;
            try
            {
                checkpointId = new OutputCheckpointId(directoryName);
            }
            catch (ArgumentException)
            {
                throw new OutputPathSecurityException();
            }

            var manifestPath = paths.GetContainedMetadataPath(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                if (removeIncomplete)
                {
                    ValidateIncompleteCheckpointForCleanup(directory);
                    RetireCheckpointDirectory(checkpointId, directory);
                    continue;
                }

                throw new OutputCheckpointConflictException(checkpointId);
            }

            OutputCheckpointManifest? manifest;
            try
            {
                manifest = await metadata.ReadJsonAsync<OutputCheckpointManifest>(manifestPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                throw new OutputCheckpointConflictException(checkpointId);
            }

            if (manifest is null)
            {
                throw new OutputCheckpointConflictException(checkpointId);
            }

            ValidateCheckpointManifest(manifest, checkpointId, directory);
            if (manifests.Count == OutputLimits.MaximumCheckpoints)
            {
                throw new OutputLimitExceededException("The output checkpoint retention limit was exceeded.");
            }

            manifests.Add(manifest);
        }

        if (Directory.EnumerateFiles(paths.CheckpointsRoot, "*", SearchOption.TopDirectoryOnly).Any())
        {
            throw new OutputPathSecurityException();
        }

        return manifests.ToImmutable();
    }

    private void ValidateIncompleteCheckpointForCleanup(string checkpointDirectory)
    {
        paths.ValidateMetadataDirectory(checkpointDirectory);
        var rootEntries = Directory.EnumerateFileSystemEntries(checkpointDirectory).Take(3).ToArray();
        if (rootEntries.Length > 2)
        {
            throw new OutputCheckpointConflictException(
                new OutputCheckpointId(Path.GetFileName(checkpointDirectory)));
        }

        if (rootEntries.Length == 0)
        {
            return;
        }

        var contentDirectory = paths.GetContainedMetadataPath(checkpointDirectory, "content");
        var pendingManifest = paths.GetContainedMetadataPath(
            checkpointDirectory,
            ".manifest.json.pending.tmp");
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach (var entry in rootEntries)
        {
            if (string.Equals(entry, contentDirectory, comparison))
            {
                paths.ValidateMetadataDirectory(contentDirectory);
                continue;
            }

            if (string.Equals(entry, pendingManifest, comparison))
            {
                paths.ValidateMetadataFile(pendingManifest);
                if (new FileInfo(pendingManifest).Length > OutputLimits.MaximumMetadataDocumentBytes)
                {
                    throw new OutputCheckpointConflictException(
                        new OutputCheckpointId(Path.GetFileName(checkpointDirectory)));
                }

                continue;
            }

            throw new OutputCheckpointConflictException(
                new OutputCheckpointId(Path.GetFileName(checkpointDirectory)));
        }

        if (!Directory.Exists(contentDirectory))
        {
            return;
        }

        if (Directory.EnumerateDirectories(contentDirectory, "*", SearchOption.TopDirectoryOnly).Any())
        {
            throw new OutputCheckpointConflictException(
                new OutputCheckpointId(Path.GetFileName(checkpointDirectory)));
        }

        var files = Directory.EnumerateFiles(contentDirectory, "*", SearchOption.TopDirectoryOnly)
            .Take(OutputLimits.MaximumMutationsPerApply + 1)
            .ToArray();
        if (files.Length > OutputLimits.MaximumMutationsPerApply)
        {
            throw new OutputLimitExceededException("An incomplete checkpoint exceeds its cleanup limit.");
        }

        Array.Sort(files, StringComparer.Ordinal);

        long totalBytes = 0;
        for (var index = 0; index < files.Length; index++)
        {
            var expectedName = $"{index:D6}.content.bin";
            if (!string.Equals(Path.GetFileName(files[index]), expectedName, StringComparison.Ordinal))
            {
                throw new OutputCheckpointConflictException(
                    new OutputCheckpointId(Path.GetFileName(checkpointDirectory)));
            }

            paths.ValidateMetadataFile(files[index]);
            totalBytes = checked(totalBytes + new FileInfo(files[index]).Length);
            if (totalBytes > OutputLimits.MaximumCheckpointBytes)
            {
                throw new OutputLimitExceededException("An incomplete checkpoint exceeds its cleanup limit.");
            }
        }
    }

    private void RetireCheckpointDirectory(
        OutputCheckpointId checkpointId,
        string checkpointDirectory)
    {
        var tombstone = paths.ResolveCheckpointTombstoneDirectory(checkpointId);
        if (Directory.Exists(tombstone))
        {
            paths.DeleteMetadataTree(tombstone);
        }

        paths.MoveMetadataDirectory(checkpointDirectory, tombstone, paths.CheckpointsRoot);
        try
        {
            paths.DeleteMetadataTree(tombstone);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The atomic rename removed this checkpoint from the active namespace.
            // A later list/create operation will retry bounded tombstone cleanup.
        }
    }

    private void RetireCheckpointAfterPublicationBestEffort(
        OutputCheckpointId checkpointId,
        string checkpointDirectory)
    {
        try
        {
            RetireCheckpointDirectory(checkpointId, checkpointDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The new checkpoint is already verified and published. Operational
            // retirement failures leave the recognized older checkpoint active;
            // a later create retries same-scope retention without reporting the
            // successful creation as failed.
        }
    }

    private static bool TryParseRetiredCheckpointName(
        string directoryName,
        out OutputCheckpointId checkpointId)
    {
        const string prefix = "retired-";
        if (directoryName.StartsWith(prefix, StringComparison.Ordinal))
        {
            try
            {
                checkpointId = new OutputCheckpointId(directoryName[prefix.Length..]);
                return true;
            }
            catch (ArgumentException)
            {
            }
        }

        checkpointId = default;
        return false;
    }

    private async Task<OutputCheckpointManifest> ReadCheckpointManifestAsync(
        OutputCheckpointId checkpointId,
        CancellationToken cancellationToken)
    {
        var manifests = await ReadCheckpointManifestsAsync(removeIncomplete: false, cancellationToken)
            .ConfigureAwait(false);
        return manifests.FirstOrDefault(manifest => manifest.Summary.Id == checkpointId)
               ?? throw new OutputCheckpointNotFoundException(checkpointId);
    }

    private void ValidateCheckpointManifest(
        OutputCheckpointManifest manifest,
        OutputCheckpointId expectedId,
        string checkpointDirectory)
    {
        if (manifest.SchemaVersion != OutputCheckpointManifest.CurrentSchemaVersion
            || manifest.Summary is null
            || manifest.Entries.IsDefault
            || manifest.Summary.Id != expectedId
            || manifest.Summary.Coverage != OutputCheckpointCoverage.OwnedFiles
            || manifest.Entries.Length != manifest.Summary.FileCount
            || manifest.Entries.Length > OutputLimits.MaximumMutationsPerApply)
        {
            throw new OutputCheckpointConflictException(expectedId);
        }

        var expectedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "manifest.json",
        };
        var contentDirectory = paths.GetContainedMetadataPath(checkpointDirectory, "content");
        paths.ValidateMetadataDirectory(checkpointDirectory);
        paths.ValidateMetadataDirectory(contentDirectory);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        for (var index = 0; index < manifest.Entries.Length; index++)
        {
            var entry = manifest.Entries[index];
            var expectedContentName = $"{index:D6}.content.bin";
            if (entry is null
                || entry.Path is null
                || entry.State is null
                || !entry.State.Exists
                || !seenPaths.Add(entry.Path.CanonicalKey)
                || !string.Equals(entry.ContentFileName, expectedContentName, StringComparison.Ordinal)
                || entry.OwnershipClaims.IsDefaultOrEmpty
                || entry.OwnershipClaims.Length > OutputLimits.MaximumOwnershipClaimsPerMutation
                || entry.OwnershipClaims.Distinct().Count() != entry.OwnershipClaims.Length
                || entry.OwnershipClaims.Any(claim =>
                    claim is null
                    || claim.Address is null
                    || claim.Address.File is null
                    || claim.Address.File != entry.Path
                    || claim.GameFamily != manifest.Summary.GameFamily
                    || claim.Address.ByteRange is { } range
                    && range.EndExclusive > entry.State.LengthBytes)
                || string.IsNullOrWhiteSpace(entry.OutputMode))
            {
                throw new OutputCheckpointConflictException(expectedId);
            }

            _ = SemanticContractGuards.ContractKey(entry.OutputMode, nameof(manifest));

            paths.ValidateTarget(entry.Path);
            totalBytes = checked(totalBytes + entry.State.LengthBytes);
            if (entry.State.LengthBytes > OutputLimits.MaximumWriteBytesPerMutation
                || totalBytes > OutputLimits.MaximumCheckpointBytes
                || totalBytes > OutputLimits.MaximumWriteBytesPerApply)
            {
                throw new OutputLimitExceededException("The output checkpoint exceeds its restore limits.");
            }

            expectedFiles.Add(expectedContentName);
        }

        if (totalBytes != manifest.Summary.TotalBytes)
        {
            throw new OutputCheckpointConflictException(expectedId);
        }

        var computedFingerprint = ComputeCheckpointManifestFingerprint(
            manifest.Summary.Id,
            manifest.Summary.ProjectId,
            manifest.Summary.GameFamily,
            manifest.Summary.OutputMode,
            manifest.Summary.CreatedAtUtc,
            manifest.Summary.Label,
            manifest.Entries);
        if (!string.Equals(
                computedFingerprint,
                manifest.Summary.ManifestFingerprint,
                StringComparison.Ordinal))
        {
            throw new OutputCheckpointConflictException(expectedId);
        }

        var rootEntries = Directory.EnumerateFileSystemEntries(checkpointDirectory)
            .Take(3)
            .Select(Path.GetFileName)
            .ToArray();
        if (rootEntries.Length != 2
            || !rootEntries.Contains("manifest.json", StringComparer.Ordinal)
            || !rootEntries.Contains("content", StringComparer.Ordinal)
            || !Directory.Exists(contentDirectory))
        {
            throw new OutputPathSecurityException();
        }

        var expectedContent = expectedFiles
            .Where(name => name != "manifest.json")
            .ToHashSet(StringComparer.Ordinal);
        var actualContent = Directory.EnumerateFiles(contentDirectory, "*", SearchOption.TopDirectoryOnly)
            .Take(expectedContent.Count + 1)
            .Select(path => Path.GetFileName(path) ?? throw new OutputPathSecurityException())
            .ToHashSet(StringComparer.Ordinal);
        if (actualContent.Count != expectedContent.Count
            || !actualContent.SetEquals(expectedContent)
            || Directory.EnumerateDirectories(contentDirectory, "*", SearchOption.TopDirectoryOnly).Any())
        {
            throw new OutputPathSecurityException();
        }

        foreach (var name in actualContent)
        {
            paths.ValidateMetadataFile(paths.GetContainedMetadataPath(contentDirectory, name));
        }
    }

    private async Task<CheckpointRestoreMaterial> BuildCheckpointRestoreAsync(
        OutputCheckpointManifest manifest,
        bool includePostimages,
        CancellationToken cancellationToken)
    {
        var inventory = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
        var allOwnedByPath = inventory.Files.ToDictionary(
            record => record.Path.CanonicalKey,
            StringComparer.Ordinal);
        var ownedByPath = inventory.Files
            .Where(record => OwnershipScopeMatches(
                record,
                manifest.Summary.ProjectId,
                manifest.Summary.GameFamily))
            .ToDictionary(record => record.Path.CanonicalKey, StringComparer.Ordinal);
        var checkpointByPath = manifest.Entries.ToDictionary(entry => entry.Path.CanonicalKey, StringComparer.Ordinal);
        if (manifest.Entries.Any(entry =>
                RequiresProviderCoordinatedMutation(entry.OutputMode, entry.Path))
            || ownedByPath.Values.Any(record =>
                RequiresProviderCoordinatedMutation(record.OutputMode, record.Path)))
        {
            // Some standalone layouts maintain a directory-wide derived index.
            // Generic file restore cannot prove that index remains coherent until
            // a format provider supplies coordinated recomposition semantics.
            throw new OutputCheckpointConflictException(manifest.Summary.Id);
        }

        var allPaths = ownedByPath.Keys
            .Concat(checkpointByPath.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (allPaths.Length > options.MaximumMutationsPerApply)
        {
            throw new OutputLimitExceededException("The checkpoint restore exceeds the mutation limit.");
        }

        var mutations = ImmutableArray.CreateBuilder<OutputMutation>();
        var readDependencies = ImmutableArray.CreateBuilder<OutputReadDependency>();
        var targets = ImmutableArray.CreateBuilder<RelativeOutputPath>();
        var revisionTokens = new List<string?>();
        var writeCount = 0;
        var deleteCount = 0;
        long writeBytes = 0;
        foreach (var key in allPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ownedByPath.TryGetValue(key, out var owned);
            checkpointByPath.TryGetValue(key, out var checkpoint);
            var path = checkpoint?.Path ?? owned!.Path;
            if (checkpoint is not null
                && allOwnedByPath.TryGetValue(key, out var anyOwner)
                && !OwnershipScopeMatches(
                    anyOwner,
                    manifest.Summary.ProjectId,
                    manifest.Summary.GameFamily))
            {
                throw new OutputOwnershipConflictException(path);
            }

            var current = await TryFingerprintTargetAsync(path, cancellationToken).ConfigureAwait(false)
                          ?? throw new OutputCoordinatorException("An output target could not be fingerprinted for restore.");
            readDependencies.Add(new OutputReadDependency(path, current));

            revisionTokens.Add(path.CanonicalKey);
            revisionTokens.Add(owned is null ? "unowned" : "owned");
            revisionTokens.Add(owned?.FileDeleteEligible == true ? "delete-eligible" : "delete-ineligible");
            revisionTokens.AddRange(OutputRevisionCalculator.FileStateTokens(current));

            if (owned is not null && current != owned.CurrentState)
            {
                throw new OutputPreimageConflictException(path);
            }

            if (checkpoint is not null)
            {
                var contentPath = GetCheckpointContentPath(manifest, checkpoint);
                var contentState = await ComputeMetadataFileStateAsync(
                        contentPath,
                        options.MaximumWriteBytesPerMutation,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (contentState != checkpoint.State)
                {
                    throw new OutputCheckpointConflictException(manifest.Summary.Id);
                }

                if (owned is null && current.Exists)
                {
                    throw new OutputPreimageConflictException(path);
                }

                if (current == checkpoint.State)
                {
                    continue;
                }

                writeCount++;
                writeBytes = checked(writeBytes + checkpoint.State.LengthBytes);
                targets.Add(path);
                if (includePostimages)
                {
                    var postimage = await ReadCheckpointPostimageAsync(
                            manifest.Summary.Id,
                            contentPath,
                            checkpoint.State,
                            cancellationToken)
                        .ConfigureAwait(false);
                    mutations.Add(OutputMutation.WriteCheckpointRestore(
                        path,
                        postimage,
                        current,
                        checkpoint.OwnershipClaims,
                        checkpoint.OutputMode,
                        checkpoint.FileDeleteEligible));
                }

                continue;
            }

            if (!current.Exists)
            {
                continue;
            }

            if (!owned!.FileDeleteEligible || !HasWholeFileOwnership(owned.Claims))
            {
                throw new OutputOwnershipConflictException(path);
            }

            deleteCount++;
            targets.Add(path);
            if (includePostimages)
            {
                mutations.Add(OutputMutation.Delete(
                    path,
                    current,
                    owned!.Claims,
                    owned.OutputMode));
            }
        }

        if (writeBytes > options.MaximumWriteBytesPerApply)
        {
            throw new OutputLimitExceededException("The checkpoint restore exceeds the aggregate byte limit.");
        }

        var outputRevision = OutputRevisionCalculator.FromTokens(
            "checkpoint-restore-output-v1",
            revisionTokens);
        var preview = new OutputCheckpointRestorePreview(
            manifest.Summary.Id,
            manifest.Summary.ManifestFingerprint,
            outputRevision,
            targets,
            writeCount,
            deleteCount,
            writeBytes);
        return new CheckpointRestoreMaterial(
            preview,
            mutations.ToImmutable(),
            readDependencies.ToImmutable());
    }

    private async Task CopyTargetToCheckpointAsync(
        OutputOwnershipRecord owned,
        string contentPath,
        CancellationToken cancellationToken)
    {
        paths.ValidateTarget(owned.Path);
        var targetPath = paths.ResolveTarget(owned.Path);
        await using var source = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length != owned.CurrentState.LengthBytes)
        {
            throw new OutputPreimageConflictException(owned.Path);
        }

        await using (var destination = metadata.OpenPrivateFile(
                         contentPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }
        OutputFileSystemDurability.FlushParent(contentPath);

        var copied = await ComputeFileStateAsync(
                contentPath,
                options.MaximumWriteBytesPerMutation,
                cancellationToken)
            .ConfigureAwait(false);
        var current = await ComputeTargetStateAsync(owned.Path, cancellationToken).ConfigureAwait(false);
        if (copied != owned.CurrentState || current != owned.CurrentState)
        {
            throw new OutputPreimageConflictException(owned.Path);
        }
    }

    private string GetCheckpointContentPath(
        OutputCheckpointManifest manifest,
        OutputCheckpointEntry entry)
    {
        var checkpointDirectory = paths.ResolveCheckpointDirectory(manifest.Summary.Id);
        var contentDirectory = paths.GetContainedMetadataPath(checkpointDirectory, "content");
        return paths.GetContainedMetadataPath(contentDirectory, entry.ContentFileName);
    }

    private async Task<ReadOnlyMemory<byte>> ReadCheckpointPostimageAsync(
        OutputCheckpointId checkpointId,
        string contentPath,
        OutputFileState expectedState,
        CancellationToken cancellationToken)
    {
        if (expectedState.LengthBytes > int.MaxValue)
        {
            throw new OutputLimitExceededException("Checkpoint content is too large to restore.");
        }

        paths.ValidateMetadataFile(contentPath);
        await using var source = metadata.OpenPrivateFile(
            contentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length != expectedState.LengthBytes)
        {
            throw new OutputCheckpointConflictException(checkpointId);
        }

        var bytes = new byte[checked((int)source.Length)];
        await source.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength != expectedState.LengthBytes
            || !string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                expectedState.Sha256,
                StringComparison.Ordinal))
        {
            throw new OutputCheckpointConflictException(checkpointId);
        }

        return bytes;
    }

    private async Task<OutputFileState> ComputeMetadataFileStateAsync(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        paths.ValidateMetadataFile(path);
        if (!File.Exists(path))
        {
            return OutputFileState.Missing;
        }

        await using var stream = metadata.OpenPrivateFile(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ComputeStreamStateAsync(stream, maximumBytes, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateExpectedManifestFingerprint(
        OutputCheckpointManifest manifest,
        string? expectedManifestFingerprint)
    {
        if (expectedManifestFingerprint is null)
        {
            return;
        }

        var expected = SemanticContractGuards.Sha256Fingerprint(
            expectedManifestFingerprint,
            nameof(expectedManifestFingerprint));
        if (!string.Equals(manifest.Summary.ManifestFingerprint, expected, StringComparison.Ordinal))
        {
            throw new OutputCheckpointConflictException(manifest.Summary.Id);
        }
    }

    private async Task<OutputApplyResult> ExecutePlanCoreAsync(
        OutputApplyPlan plan,
        CancellationToken cancellationToken)
    {
        await ValidatePlanDependenciesAsync(plan, cancellationToken).ConfigureAwait(false);
        var inventoryAtStart = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
        var inventoryByPath = inventoryAtStart.Files.ToDictionary(
            record => record.Path.CanonicalKey,
            StringComparer.Ordinal);
        foreach (var mutation in plan.Mutations)
        {
            var hasOwnedRecord = inventoryByPath.TryGetValue(mutation.Path.CanonicalKey, out var owned);
            if (hasOwnedRecord
                && !OwnershipScopeMatches(owned!, plan.ProjectId, plan.GameFamily))
            {
                throw new OutputOwnershipConflictException(mutation.Path);
            }

            if (mutation.Kind == OutputMutationKind.Delete
                && (!hasOwnedRecord
                    || owned!.CurrentState != mutation.ExpectedPreimage
                    || !owned.FileDeleteEligible
                    || !HasWholeFileOwnership(owned.Claims)
                    || !HasWholeFileOwnership(mutation.OwnershipClaims)))
            {
                throw new OutputOwnershipConflictException(mutation.Path);
            }
        }

        var transactionId = OutputTransactionId.New();
        var transactionDirectory = paths.ResolveTransactionPreparationDirectory(transactionId);
        var stageDirectory = paths.GetContainedMetadataPath(transactionDirectory, "stage");
        var backupDirectory = paths.GetContainedMetadataPath(transactionDirectory, "backup");
        var captureDirectory = paths.GetContainedMetadataPath(transactionDirectory, "capture");
        var discardDirectory = paths.GetContainedMetadataPath(transactionDirectory, "discard");
        var journalPath = paths.GetContainedMetadataPath(transactionDirectory, "journal.json");
        var startedAtUtc = DateTimeOffset.UtcNow;

        var journalEntries = plan.Mutations
            .Select((mutation, index) => new OutputJournalEntry(
                mutation.Path,
                mutation.Kind,
                mutation.ExpectedPreimage,
                mutation.PlannedPostimage,
                mutation.OwnershipClaims,
                mutation.OwnershipOutputMode ?? plan.OutputMode,
                mutation.RestoredFileDeleteEligibility,
                mutation.Kind == OutputMutationKind.Write ? $"{index:D6}.stage.bin" : null,
                mutation.ExpectedPreimage.Exists ? $"{index:D6}.backup.bin" : null))
            .ToImmutableArray();
        var journal = new OutputTransactionJournal(
            OutputTransactionJournal.CurrentSchemaVersion,
            transactionId,
            OutputTransactionPhase.Preparing,
            plan.ProjectId,
            plan.GameFamily,
            plan.OutputMode,
            plan.SemanticReviewHash,
            plan.Origins,
            journalEntries,
            ImmutableArray<RelativeOutputPath>.Empty,
            PublishedEntryCount: 0,
            startedAtUtc,
            OutcomeCode: null);

        EnsureFinalizationMetadataCapacity(inventoryAtStart, journal);

        paths.CreateMetadataDirectory(transactionDirectory, paths.TransactionsRoot);
        try
        {
            paths.EnsureMetadataDirectory(stageDirectory, transactionDirectory);
            paths.EnsureMetadataDirectory(backupDirectory, transactionDirectory);
            paths.EnsureMetadataDirectory(captureDirectory, transactionDirectory);
            paths.EnsureMetadataDirectory(discardDirectory, transactionDirectory);
            await metadata.WriteJsonAtomicAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < plan.Mutations.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mutation = plan.Mutations[index];
                paths.ValidateTarget(mutation.Path);
                var current = await ComputeTargetStateAsync(mutation.Path, cancellationToken).ConfigureAwait(false);
                if (current != mutation.ExpectedPreimage)
                {
                    throw new OutputPreimageConflictException(mutation.Path);
                }

                if (mutation.Kind == OutputMutationKind.Write)
                {
                    var stagePath = paths.GetContainedMetadataPath(stageDirectory, journalEntries[index].StageFileName!);
                    await WritePostimageAsync(stagePath, mutation.Postimage, cancellationToken).ConfigureAwait(false);
                    var stagedState = await ComputeFileStateAsync(
                            stagePath,
                            options.MaximumWriteBytesPerMutation,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (stagedState != mutation.PlannedPostimage)
                    {
                        throw new OutputCoordinatorException("A staged postimage failed exact fingerprint verification.");
                    }
                }

                if (mutation.ExpectedPreimage.Exists)
                {
                    var backupPath = paths.GetContainedMetadataPath(backupDirectory, journalEntries[index].BackupFileName!);
                    await CopyTargetToBackupAsync(mutation.Path, backupPath, cancellationToken).ConfigureAwait(false);
                    var backupState = await ComputeFileStateAsync(
                            backupPath,
                            options.MaximumFingerprintFileBytes,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (backupState != mutation.ExpectedPreimage)
                    {
                        throw new OutputPreimageConflictException(mutation.Path);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            journal = journal with { Phase = OutputTransactionPhase.Prepared };
            await metadata.WriteJsonAtomicAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
            var activeTransactionDirectory = paths.ResolveTransactionDirectory(transactionId);
            paths.MoveMetadataDirectory(
                transactionDirectory,
                activeTransactionDirectory,
                paths.TransactionsRoot);
            transactionDirectory = activeTransactionDirectory;
            stageDirectory = paths.GetContainedMetadataPath(transactionDirectory, "stage");
            backupDirectory = paths.GetContainedMetadataPath(transactionDirectory, "backup");
            captureDirectory = paths.GetContainedMetadataPath(transactionDirectory, "capture");
            discardDirectory = paths.GetContainedMetadataPath(transactionDirectory, "discard");
            journalPath = paths.GetContainedMetadataPath(transactionDirectory, "journal.json");
        }
        catch
        {
            RetireTransactionDirectory(transactionId, transactionDirectory);
            throw;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ValidatePlanDependenciesAsync(plan, CancellationToken.None).ConfigureAwait(false);
            foreach (var mutation in plan.Mutations)
            {
                paths.ValidateTarget(mutation.Path);
                var current = await ComputeTargetStateAsync(
                        mutation.Path,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (current != mutation.ExpectedPreimage)
                {
                    throw new OutputPreimageConflictException(mutation.Path);
                }
            }

            journal = journal with { Phase = OutputTransactionPhase.Committing };
            await metadata.WriteJsonAtomicAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // No target mutation is permitted before the committing journal is
            // published. The prepared material can therefore be retired without
            // interpreting later external target changes as recovery evidence.
            RetireTransactionDirectory(transactionId, transactionDirectory);
            throw;
        }

        try
        {
            var createdDirectoryKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var created in journal.CreatedDirectories)
            {
                createdDirectoryKeys.Add(created.CanonicalKey);
            }

            for (var index = 0; index < journal.Entries.Length; index++)
            {
                var entry = journal.Entries[index];
                paths.ValidateTarget(entry.Path);
                var current = await ComputeTargetStateAsync(entry.Path, CancellationToken.None).ConfigureAwait(false);
                if (current != entry.Preimage)
                {
                    throw new OutputPreimageConflictException(entry.Path);
                }

                var newlyCreated = paths.EnsureTargetParentDirectories(entry.Path);
                if (!newlyCreated.IsEmpty)
                {
                    var createdBuilder = journal.CreatedDirectories.ToBuilder();
                    foreach (var directory in newlyCreated)
                    {
                        if (createdDirectoryKeys.Add(directory.CanonicalKey))
                        {
                            createdBuilder.Add(directory);
                        }
                    }

                    if (createdBuilder.Count > OutputLimits.MaximumCreatedDirectoriesPerApply)
                    {
                        throw new OutputLimitExceededException("An output apply created too many directories.");
                    }

                    journal = journal with { CreatedDirectories = createdBuilder.ToImmutable() };
                    await metadata.WriteJsonAtomicAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);
                }

                await CommitTargetEntryAsync(
                        entry,
                        index,
                        stageDirectory,
                        captureDirectory)
                    .ConfigureAwait(false);

                var postCommitState = await ComputeTargetStateAsync(entry.Path, CancellationToken.None).ConfigureAwait(false);
                if (postCommitState != entry.Postimage)
                {
                    throw new OutputCoordinatorException("An output target failed post-commit fingerprint verification.");
                }

                journal = journal with { PublishedEntryCount = index + 1 };
                await metadata.WriteJsonAtomicAsync(
                        journalPath,
                        journal,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                DeleteCaptureFile(captureDirectory, index);
            }

        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return await RollBackAsync(
                    journal,
                    transactionDirectory,
                    journalPath,
                    OutputOutcomeCodes.CommitFailed)
                .ConfigureAwait(false);
        }

        try
        {
            return await FinalizeCommittedAsync(journal, transactionDirectory, journalPath).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // Output postimages are already complete. Rolling them back after any
            // ownership/history publication could make metadata and files diverge.
            return await MarkRecoveryRequiredAsync(
                    journal,
                    transactionDirectory,
                    journalPath,
                    OutputOutcomeCodes.FinalizationFailed)
                .ConfigureAwait(false);
        }
    }

    private async Task ValidatePlanDependenciesAsync(
        OutputApplyPlan plan,
        CancellationToken cancellationToken)
    {
        foreach (var dependency in plan.ReadDependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await ComputeTargetStateAsync(dependency.Path, cancellationToken).ConfigureAwait(false);
            if (current != dependency.ExpectedState)
            {
                throw new OutputPreimageConflictException(dependency.Path);
            }
        }

        foreach (var dependency in plan.DirectoryMembershipDependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = CaptureDirectoryMembershipCore(dependency.Directory);
            if (current.Revision != dependency.ExpectedRevision)
            {
                throw new OutputStateRevisionConflictException(
                    dependency.ExpectedRevision,
                    current.Revision);
            }
        }
    }

    private async Task CommitTargetEntryAsync(
        OutputJournalEntry entry,
        int index,
        string stageDirectory,
        string captureDirectory)
    {
        paths.ValidateTarget(entry.Path);
        var targetPath = paths.ResolveTarget(entry.Path);
        var capturePath = paths.GetContainedMetadataPath(
            captureDirectory,
            $"{index:D6}.capture.bin");
        paths.ValidateMetadataFile(capturePath);
        if (File.Exists(capturePath) || Directory.Exists(capturePath))
        {
            throw new OutputPathSecurityException();
        }

        if (entry.Preimage.Exists)
        {
            try
            {
                OutputFileSystemDurability.Move(targetPath, capturePath, overwrite: false);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
                throw new OutputPreimageConflictException(entry.Path);
            }

            paths.ValidateMetadataFile(capturePath);
            var capturedState = await ComputeFileStateAsync(
                    capturePath,
                    options.MaximumFingerprintFileBytes,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (capturedState != entry.Preimage)
            {
                if (!TryRestoreCapturedFile(entry.Path, capturePath, targetPath)
                    && File.Exists(capturePath))
                {
                    metadata.ProtectExistingFile(capturePath);
                }

                throw new OutputPreimageConflictException(entry.Path);
            }

            metadata.ProtectExistingFile(capturePath);
        }
        else
        {
            paths.ValidateTarget(entry.Path);
            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                throw new OutputPreimageConflictException(entry.Path);
            }
        }

        if (entry.Kind == OutputMutationKind.Write)
        {
            var stagePath = paths.GetContainedMetadataPath(stageDirectory, entry.StageFileName!);
            paths.ValidateMetadataFile(stagePath);
            try
            {
                OutputFileSystemDurability.Move(stagePath, targetPath, overwrite: false);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
                throw new OutputPreimageConflictException(entry.Path);
            }
        }
    }

    private bool TryRestoreCapturedFile(
        RelativeOutputPath path,
        string capturePath,
        string targetPath)
    {
        try
        {
            paths.ValidateTarget(path);
            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                return false;
            }

            OutputFileSystemDurability.Move(capturePath, targetPath, overwrite: false);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or OutputCoordinatorException)
        {
            return false;
        }
    }

    private async Task<bool> TryConditionallyRollBackTargetAsync(
        OutputJournalEntry entry,
        int index,
        string? backupPath,
        string captureDirectory,
        string discardDirectory)
    {
        paths.ValidateTarget(entry.Path);
        var targetPath = paths.ResolveTarget(entry.Path);
        var capturePath = paths.GetContainedMetadataPath(
            captureDirectory,
            $"{index:D6}.capture.bin");
        var discardPath = paths.GetContainedMetadataPath(
            discardDirectory,
            $"{index:D6}.discard.bin");
        paths.ValidateMetadataFile(capturePath);
        paths.ValidateMetadataFile(discardPath);
        if (Directory.Exists(capturePath) || Directory.Exists(discardPath))
        {
            return false;
        }

        var current = await ComputeTargetStateAsync(entry.Path, CancellationToken.None).ConfigureAwait(false);
        var capturedState = await ComputeFileStateAsync(
                capturePath,
                options.MaximumFingerprintFileBytes,
                CancellationToken.None)
            .ConfigureAwait(false);
        var discardedState = await ComputeFileStateAsync(
                discardPath,
                options.MaximumFingerprintFileBytes,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (capturedState.Exists && capturedState != entry.Preimage
            || !entry.Preimage.Exists && capturedState.Exists
            || discardedState.Exists && discardedState != entry.Postimage)
        {
            return false;
        }

        if (current == entry.Preimage)
        {
            // A preimage capture must be consumed by the restore move. Seeing the
            // same bytes at both names cannot prove which file an external writer
            // published, so preserve both for manual recovery.
            if (capturedState.Exists)
            {
                return false;
            }

            if (discardedState.Exists)
            {
                DeleteRecoveryArtifact(discardPath);
            }

            return true;
        }

        if (discardedState.Exists)
        {
            if (current.Exists
                || !await TryRestoreRollbackPreimageAsync(
                    entry,
                    targetPath,
                    capturePath,
                    capturedState,
                    backupPath)
                    .ConfigureAwait(false))
            {
                return false;
            }

            var resumed = await ComputeTargetStateAsync(entry.Path, CancellationToken.None).ConfigureAwait(false);
            if (resumed != entry.Preimage)
            {
                return false;
            }

            DeleteRecoveryArtifact(discardPath);
            return true;
        }

        if (current != entry.Postimage)
        {
            return false;
        }

        if (!entry.Postimage.Exists)
        {
            return await TryRestoreRollbackPreimageAsync(
                    entry,
                    targetPath,
                    capturePath,
                    capturedState,
                    backupPath)
                .ConfigureAwait(false);
        }

        try
        {
            OutputFileSystemDurability.Move(targetPath, discardPath, overwrite: false);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            return false;
        }

        paths.ValidateMetadataFile(discardPath);
        discardedState = await ComputeFileStateAsync(
                discardPath,
                options.MaximumFingerprintFileBytes,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (discardedState != entry.Postimage)
        {
            if (!TryRestoreCapturedFile(entry.Path, discardPath, targetPath)
                && File.Exists(discardPath))
            {
                metadata.ProtectExistingFile(discardPath);
            }

            return false;
        }

        metadata.ProtectExistingFile(discardPath);
        if (!await TryRestoreRollbackPreimageAsync(
                entry,
                targetPath,
                capturePath,
                capturedState,
                backupPath)
                .ConfigureAwait(false))
        {
            return false;
        }

        var restored = await ComputeTargetStateAsync(entry.Path, CancellationToken.None).ConfigureAwait(false);
        if (restored != entry.Preimage)
        {
            return false;
        }

        DeleteRecoveryArtifact(discardPath);
        return true;
    }

    private async Task<bool> TryRestoreRollbackPreimageAsync(
        OutputJournalEntry entry,
        string targetPath,
        string capturePath,
        OutputFileState capturedState,
        string? backupPath)
    {
        if (!entry.Preimage.Exists)
        {
            return !File.Exists(targetPath) && !Directory.Exists(targetPath);
        }

        var sourcePath = capturedState == entry.Preimage
            ? capturePath
            : backupPath;
        if (sourcePath is null || !File.Exists(sourcePath)
            || File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            return false;
        }

        var sourceState = await ComputeFileStateAsync(
                sourcePath,
                options.MaximumFingerprintFileBytes,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (sourceState != entry.Preimage)
        {
            return false;
        }

        _ = paths.EnsureTargetParentDirectories(entry.Path);
        try
        {
            OutputFileSystemDurability.Move(sourcePath, targetPath, overwrite: false);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void DeleteRecoveryArtifact(string artifactPath)
    {
        paths.ValidateMetadataFile(artifactPath);
        File.Delete(artifactPath);
    }

    private void DeleteCaptureFile(string captureDirectory, int index)
    {
        var capturePath = paths.GetContainedMetadataPath(
            captureDirectory,
            $"{index:D6}.capture.bin");
        DeleteRecoveryArtifact(capturePath);
    }

    private void DeleteDiscardFile(string discardDirectory, int index)
    {
        var discardPath = paths.GetContainedMetadataPath(
            discardDirectory,
            $"{index:D6}.discard.bin");
        DeleteRecoveryArtifact(discardPath);
    }

    private async Task<OutputApplyResult> FinalizeCommittedAsync(
        OutputTransactionJournal journal,
        string transactionDirectory,
        string journalPath)
    {
        foreach (var entry in journal.Entries)
        {
            var current = await ComputeTargetStateAsync(entry.Path, CancellationToken.None).ConfigureAwait(false);
            if (current != entry.Postimage)
            {
                return await MarkRecoveryRequiredAsync(
                        journal,
                        transactionDirectory,
                        journalPath,
                        OutputOutcomeCodes.PostimageChanged)
                .ConfigureAwait(false);
            }
        }

        // Once this phase is durable, recovery must never choose rollback: the
        // ownership inventory may be published by the next operation.
        journal = journal with
        {
            Phase = OutputTransactionPhase.Finalizing,
            OutcomeCode = null,
        };
        await metadata.WriteJsonAtomicAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);

        var completedAtUtc = DateTimeOffset.UtcNow;
        var inventory = await ReadInventoryAsync(CancellationToken.None).ConfigureAwait(false);
        var updatedInventory = BuildUpdatedInventory(
            inventory,
            journal,
            completedAtUtc,
            journal.CreatedDirectories);
        await WriteInventoryAsync(updatedInventory, CancellationToken.None).ConfigureAwait(false);

        var receipt = BuildReceipt(journal, OutputApplyOutcome.Committed, completedAtUtc, outcomeCode: null);
        await AppendHistoryAsync(receipt, CancellationToken.None).ConfigureAwait(false);
        journal = journal with
        {
            Phase = OutputTransactionPhase.Committed,
            OutcomeCode = null,
        };
        await metadata.WriteJsonAtomicAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);
        RetireTransactionDirectory(journal.TransactionId, transactionDirectory);
        return new OutputApplyResult(OutputApplyOutcome.Committed, journal.TransactionId, receipt);
    }

    private void EnsureFinalizationMetadataCapacity(
        OutputOwnershipInventory inventory,
        OutputTransactionJournal journal)
    {
        var possibleCreatedDirectories = GetMissingTargetParentDirectories(journal.Entries);
        var projectedInventory = BuildUpdatedInventory(
            inventory,
            journal,
            journal.StartedAtUtc,
            possibleCreatedDirectories);
        _ = metadata.GetJsonByteCount(projectedInventory);

        var largestReceipt = BuildReceipt(
            journal,
            OutputApplyOutcome.RecoveryRequired,
            journal.StartedAtUtc,
            OutputOutcomeCodes.RollbackVerificationFailed);
        _ = metadata.GetJsonByteCount(new OutputApplyHistoryDocument(
            OutputApplyHistoryDocument.CurrentSchemaVersion,
            [largestReceipt]));
    }

    private ImmutableArray<RelativeOutputPath> GetMissingTargetParentDirectories(
        IEnumerable<OutputJournalEntry> entries)
    {
        var result = ImmutableArray.CreateBuilder<RelativeOutputPath>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var segments = entry.Path.Value.Split('/');
            for (var count = 1; count < segments.Length; count++)
            {
                var directory = new RelativeOutputPath(string.Join('/', segments.Take(count)));
                if (!seen.Add(directory.CanonicalKey) || paths.OwnedDirectoryExists(directory))
                {
                    continue;
                }

                if (result.Count == OutputLimits.MaximumCreatedDirectoriesPerApply)
                {
                    throw new OutputLimitExceededException(
                        "An output apply could create too many directories.");
                }

                result.Add(directory);
            }
        }

        return result.ToImmutable();
    }

    private static OutputOwnershipInventory BuildUpdatedInventory(
        OutputOwnershipInventory inventory,
        OutputTransactionJournal journal,
        DateTimeOffset completedAtUtc,
        IEnumerable<RelativeOutputPath> createdDirectories)
    {
        var fileRecords = inventory.Files.ToDictionary(
            record => record.Path.CanonicalKey,
            StringComparer.Ordinal);
        var entryEligibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var entry in journal.Entries)
        {
            fileRecords.TryGetValue(entry.Path.CanonicalKey, out var previous);
            var fileDeleteEligible = ResolveFileDeleteEligibility(entry, previous);
            entryEligibility[entry.Path.CanonicalKey] = fileDeleteEligible;
            if (entry.Postimage.Exists)
            {
                fileRecords[entry.Path.CanonicalKey] = new OutputOwnershipRecord(
                    entry.Path,
                    entry.Postimage,
                    entry.OwnershipClaims,
                    journal.ProjectId,
                    journal.GameFamily,
                    entry.OwnershipOutputMode,
                    fileDeleteEligible,
                    journal.TransactionId,
                    completedAtUtc);
            }
            else
            {
                fileRecords.Remove(entry.Path.CanonicalKey);
            }
        }

        var directoryRecords = inventory.CreatedDirectories.ToDictionary(
            record => record.Path.CanonicalKey,
            StringComparer.Ordinal);
        foreach (var directory in createdDirectories)
        {
            var authorizationEntry = journal.Entries
                .Where(entry => entry.Postimage.Exists
                                && entryEligibility.GetValueOrDefault(entry.Path.CanonicalKey)
                                && HasWholeFileOwnership(entry.OwnershipClaims)
                                && IsStrictAncestor(directory, entry.Path))
                .OrderBy(entry => entry.Path.CanonicalKey, StringComparer.Ordinal)
                .FirstOrDefault();
            if (authorizationEntry is null)
            {
                continue;
            }

            directoryRecords.TryAdd(
                directory.CanonicalKey,
                new OutputCreatedDirectoryOwnership(
                    directory,
                    authorizationEntry.Path,
                    journal.ProjectId,
                    journal.GameFamily,
                    journal.OutputMode,
                    journal.TransactionId,
                    completedAtUtc));
        }

        return new OutputOwnershipInventory(
            OutputOwnershipInventory.CurrentSchemaVersion,
            fileRecords.Values.OrderBy(record => record.Path.CanonicalKey, StringComparer.Ordinal),
            directoryRecords.Values.OrderBy(record => record.Path.CanonicalKey, StringComparer.Ordinal));
    }

    private static bool ResolveFileDeleteEligibility(
        OutputJournalEntry entry,
        OutputOwnershipRecord? previous)
    {
        return entry.RestoredFileDeleteEligibility
               ?? (previous?.FileDeleteEligible == true
                   || !entry.Preimage.Exists && HasWholeFileOwnership(entry.OwnershipClaims));
    }

    private static bool HasWholeFileOwnership(IEnumerable<OwnedTarget> claims)
    {
        return claims.Any(claim => claim.Address.ScopeKind == OwnedTargetScopeKind.File);
    }

    private static bool RequiresProviderCoordinatedMutation(
        string outputMode,
        RelativeOutputPath path)
    {
        return string.Equals(outputMode, "za.standalone", StringComparison.Ordinal)
               && path.CanonicalKey.StartsWith("ROMFS/", StringComparison.Ordinal);
    }

    private static bool IsKnownOutcomeCode(string outcomeCode)
    {
        return outcomeCode is
            OutputOutcomeCodes.CommitFailed or
            OutputOutcomeCodes.FinalizationFailed or
            OutputOutcomeCodes.PostimageChanged or
            OutputOutcomeCodes.RollbackTargetChanged or
            OutputOutcomeCodes.BackupInvalid or
            OutputOutcomeCodes.RollbackVerificationFailed or
            OutputOutcomeCodes.RollbackFailed or
            OutputOutcomeCodes.StartupRecovery or
            OutputOutcomeCodes.UnknownTargetState;
    }

    private static bool IsStrictAncestor(RelativeOutputPath ancestor, RelativeOutputPath target)
    {
        return target.CanonicalKey.StartsWith(
            ancestor.CanonicalKey + "/",
            StringComparison.Ordinal);
    }

    private async Task<OutputApplyResult> RollBackAsync(
        OutputTransactionJournal journal,
        string transactionDirectory,
        string journalPath,
        string outcomeCode)
    {
        journal = journal with
        {
            Phase = OutputTransactionPhase.RollingBack,
            OutcomeCode = outcomeCode,
        };
        try
        {
            await metadata.WriteJsonAtomicAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);
            var backupDirectory = paths.GetContainedMetadataPath(transactionDirectory, "backup");
            var captureDirectory = paths.GetContainedMetadataPath(transactionDirectory, "capture");
            var discardDirectory = paths.GetContainedMetadataPath(transactionDirectory, "discard");
            for (var index = journal.PublishedEntryCount; index < journal.Entries.Length; index++)
            {
                var entry = journal.Entries[index];
                var current = await ComputeTargetStateAsync(
                        entry.Path,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                var capturePath = paths.GetContainedMetadataPath(
                    captureDirectory,
                    $"{index:D6}.capture.bin");
                var discardPath = paths.GetContainedMetadataPath(
                    discardDirectory,
                    $"{index:D6}.discard.bin");
                paths.ValidateMetadataFile(capturePath);
                paths.ValidateMetadataFile(discardPath);
                var captured = await ComputeFileStateAsync(
                        capturePath,
                        options.MaximumFingerprintFileBytes,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (File.Exists(discardPath)
                    || (current != entry.Preimage
                        && !(entry.Preimage.Exists
                             && !current.Exists
                             && captured == entry.Preimage)))
                {
                    return await MarkRecoveryRequiredAsync(
                            journal,
                            transactionDirectory,
                            journalPath,
                            OutputOutcomeCodes.RollbackTargetChanged)
                        .ConfigureAwait(false);
                }

                if (captured.Exists)
                {
                    if (!TryRestoreCapturedFile(entry.Path, capturePath, paths.ResolveTarget(entry.Path)))
                    {
                        return await MarkRecoveryRequiredAsync(
                                journal,
                                transactionDirectory,
                                journalPath,
                                OutputOutcomeCodes.RollbackTargetChanged)
                            .ConfigureAwait(false);
                    }

                    var restored = await ComputeTargetStateAsync(
                            entry.Path,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    if (restored != entry.Preimage)
                    {
                        return await MarkRecoveryRequiredAsync(
                                journal,
                                transactionDirectory,
                                journalPath,
                                OutputOutcomeCodes.RollbackVerificationFailed)
                            .ConfigureAwait(false);
                    }
                }
            }

            for (var index = journal.PublishedEntryCount - 1; index >= 0; index--)
            {
                var entry = journal.Entries[index];
                var current = await ComputeTargetStateAsync(entry.Path, CancellationToken.None).ConfigureAwait(false);

                string? backupPath = null;
                if (entry.Preimage.Exists && current != entry.Preimage)
                {
                    backupPath = paths.GetContainedMetadataPath(
                        backupDirectory,
                        entry.BackupFileName!);
                }

                if (!await TryConditionallyRollBackTargetAsync(
                        entry,
                        index,
                        backupPath,
                        captureDirectory,
                        discardDirectory)
                    .ConfigureAwait(false))
                {
                    return await MarkRecoveryRequiredAsync(
                            journal,
                            transactionDirectory,
                            journalPath,
                            OutputOutcomeCodes.RollbackTargetChanged)
                        .ConfigureAwait(false);
                }

                var restored = await ComputeTargetStateAsync(entry.Path, CancellationToken.None).ConfigureAwait(false);
                if (restored != entry.Preimage)
                {
                    return await MarkRecoveryRequiredAsync(
                            journal,
                            transactionDirectory,
                            journalPath,
                            OutputOutcomeCodes.RollbackVerificationFailed)
                        .ConfigureAwait(false);
                }

                DeleteCaptureFile(captureDirectory, index);
                DeleteDiscardFile(discardDirectory, index);
            }

            paths.DeleteEmptyOwnedDirectories(journal.CreatedDirectories);
            var completedAtUtc = DateTimeOffset.UtcNow;
            var receipt = BuildReceipt(journal, OutputApplyOutcome.RolledBack, completedAtUtc, outcomeCode);
            await AppendHistoryAsync(receipt, CancellationToken.None).ConfigureAwait(false);
            journal = journal with { Phase = OutputTransactionPhase.RolledBack };
            await metadata.WriteJsonAtomicAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);
            RetireTransactionDirectory(journal.TransactionId, transactionDirectory);
            return new OutputApplyResult(OutputApplyOutcome.RolledBack, journal.TransactionId, receipt);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return await MarkRecoveryRequiredAsync(
                    journal,
                    transactionDirectory,
                    journalPath,
                    OutputOutcomeCodes.RollbackFailed)
                .ConfigureAwait(false);
        }
    }

    private async Task<OutputApplyResult> MarkRecoveryRequiredAsync(
        OutputTransactionJournal journal,
        string transactionDirectory,
        string journalPath,
        string outcomeCode)
    {
        journal = journal with
        {
            Phase = OutputTransactionPhase.RecoveryRequired,
            OutcomeCode = outcomeCode,
        };
        try
        {
            await metadata.WriteJsonAtomicAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // The transaction directory is deliberately retained even if its phase
            // cannot be advanced; discovery still treats it as blocking material.
        }

        var receipt = BuildReceipt(
            journal,
            OutputApplyOutcome.RecoveryRequired,
            DateTimeOffset.UtcNow,
            outcomeCode);
        try
        {
            await AppendHistoryAsync(receipt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
        }

        _ = transactionDirectory;
        return new OutputApplyResult(OutputApplyOutcome.RecoveryRequired, journal.TransactionId, receipt);
    }

    private async Task<OutputRecoveryReport> InspectRecoveryCoreAsync(CancellationToken cancellationToken)
    {
        var discovered = await DiscoverTransactionsAsync(
                scavengeRetiredMaterial: false,
                cancellationToken)
            .ConfigureAwait(false);
        var statuses = ImmutableArray.CreateBuilder<OutputRecoveryTransactionStatus>();
        var revisionTokens = new List<string?>();
        foreach (var transaction in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            revisionTokens.Add(transaction.Id.Value);
            if (transaction.Journal is null)
            {
                var invalidStatus = new OutputRecoveryTransactionStatus(
                    transaction.Id,
                    OutputTransactionPhase.RecoveryRequired,
                    OutputRecoveryDisposition.RecoveryRequired,
                    journalReadable: false);
                statuses.Add(invalidStatus);
                AppendRecoveryRevisionTokens(revisionTokens, invalidStatus, currentStates: null);
                continue;
            }

            revisionTokens.Add(ComputeRecoveryJournalFingerprint(transaction.Journal));
            var classification = await ClassifyRecoveryAsync(
                    transaction.Journal,
                    transaction.Directory,
                    cancellationToken)
                .ConfigureAwait(false);
            statuses.Add(classification.Status);
            AppendRecoveryRevisionTokens(revisionTokens, classification.Status, classification.CurrentStates);
        }

        return new OutputRecoveryReport(
            OutputRevisionCalculator.FromTokens("output-recovery-v1", revisionTokens),
            statuses);
    }

    private async Task<OutputRecoveryReport> RecoverCoreAsync(
        OutputStateRevision? expectedRevision,
        CancellationToken cancellationToken)
    {
        var discovered = await DiscoverTransactionsAsync(
                scavengeRetiredMaterial: true,
                cancellationToken)
            .ConfigureAwait(false);
        var statuses = ImmutableArray.CreateBuilder<OutputRecoveryTransactionStatus>();
        var classified = new List<(DiscoveredTransaction Transaction, RecoveryClassification? Classification)>();
        var revisionTokens = new List<string?>();
        foreach (var transaction in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            revisionTokens.Add(transaction.Id.Value);
            if (transaction.Journal is null)
            {
                var invalidStatus = new OutputRecoveryTransactionStatus(
                    transaction.Id,
                    OutputTransactionPhase.RecoveryRequired,
                    OutputRecoveryDisposition.RecoveryRequired,
                    journalReadable: false);
                statuses.Add(invalidStatus);
                AppendRecoveryRevisionTokens(revisionTokens, invalidStatus, currentStates: null);
                classified.Add((transaction, null));
                continue;
            }

            revisionTokens.Add(ComputeRecoveryJournalFingerprint(transaction.Journal));
            var classification = await ClassifyRecoveryAsync(
                    transaction.Journal,
                    transaction.Directory,
                    cancellationToken)
                .ConfigureAwait(false);
            statuses.Add(classification.Status);
            AppendRecoveryRevisionTokens(revisionTokens, classification.Status, classification.CurrentStates);
            classified.Add((transaction, classification));
        }

        var report = new OutputRecoveryReport(
            OutputRevisionCalculator.FromTokens("output-recovery-v1", revisionTokens),
            statuses);
        if (expectedRevision is { } expected && expected != report.Revision)
        {
            throw new OutputStateRevisionConflictException(expected, report.Revision);
        }

        foreach (var item in classified)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Classification is null || item.Transaction.Journal is null)
            {
                continue;
            }

            var transaction = item.Transaction;
            var status = item.Classification.Status;
            var journalPath = paths.GetContainedMetadataPath(transaction.Directory, "journal.json");
            switch (status.Disposition)
            {
                case OutputRecoveryDisposition.FinalizeCommit:
                    if (transaction.Journal.Phase == OutputTransactionPhase.RecoveryRequired
                        && expectedRevision is null)
                    {
                        break;
                    }

                    _ = await FinalizeCommittedAsync(transaction.Journal, transaction.Directory, journalPath)
                        .ConfigureAwait(false);
                    break;
                case OutputRecoveryDisposition.RollBack:
                    if (transaction.Journal.Phase == OutputTransactionPhase.RecoveryRequired
                        && expectedRevision is null)
                    {
                        break;
                    }

                    _ = await RollBackAsync(
                            transaction.Journal,
                            transaction.Directory,
                            journalPath,
                            OutputOutcomeCodes.StartupRecovery)
                        .ConfigureAwait(false);
                    break;
                case OutputRecoveryDisposition.RecoveryRequired:
                    if (transaction.Journal.Phase is not (
                        OutputTransactionPhase.RecoveryRequired or OutputTransactionPhase.Finalizing))
                    {
                        _ = await MarkRecoveryRequiredAsync(
                                transaction.Journal,
                                transaction.Directory,
                                journalPath,
                                OutputOutcomeCodes.UnknownTargetState)
                            .ConfigureAwait(false);
                    }

                    break;
                case OutputRecoveryDisposition.NoAction:
                    if (status.Phase is OutputTransactionPhase.Committed
                        or OutputTransactionPhase.RolledBack
                        or OutputTransactionPhase.Prepared)
                    {
                        try
                        {
                            RetireTransactionDirectory(transaction.Id, transaction.Directory);
                        }
                        catch (Exception exception) when (!IsFatal(exception))
                        {
                            // The terminal journal is safe to leave for a bounded
                            // cleanup retry on the next operation.
                        }
                    }

                    break;
                default:
                    throw new InvalidOperationException("Unknown output recovery disposition.");
            }
        }

        // Recovery actions can themselves discover a changed target or fail while
        // publishing final metadata. Reclassify the surviving journals so callers
        // never proceed from the optimistic pre-action snapshot.
        return await InspectRecoveryCoreAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<RecoveryClassification> ClassifyRecoveryAsync(
        OutputTransactionJournal journal,
        string transactionDirectory,
        CancellationToken cancellationToken)
    {
        if (journal.Phase == OutputTransactionPhase.Prepared)
        {
            return new RecoveryClassification(
                new OutputRecoveryTransactionStatus(
                    journal.TransactionId,
                    journal.Phase,
                    OutputRecoveryDisposition.NoAction,
                    []),
                []);
        }

        var unknown = ImmutableArray.CreateBuilder<RelativeOutputPath>();
        var currentStates = ImmutableArray.CreateBuilder<OutputFileState?>();
        var allPostimages = journal.PublishedEntryCount == journal.Entries.Length;
        var stageDirectory = paths.GetContainedMetadataPath(transactionDirectory, "stage");
        var captureDirectory = paths.GetContainedMetadataPath(transactionDirectory, "capture");
        var discardDirectory = paths.GetContainedMetadataPath(transactionDirectory, "discard");
        var classifyAsRollingBack = journal.Phase == OutputTransactionPhase.RollingBack
            || journal.Phase == OutputTransactionPhase.RecoveryRequired
            && journal.OutcomeCode is
                OutputOutcomeCodes.RollbackTargetChanged or
                OutputOutcomeCodes.RollbackVerificationFailed or
                OutputOutcomeCodes.RollbackFailed;
        for (var index = 0; index < journal.Entries.Length; index++)
        {
            var entry = journal.Entries[index];
            cancellationToken.ThrowIfCancellationRequested();
            var current = await TryFingerprintTargetAsync(entry.Path, cancellationToken).ConfigureAwait(false);
            currentStates.Add(current);
            var wasPublished = index < journal.PublishedEntryCount;

            var stagePath = entry.StageFileName is null
                ? null
                : paths.GetContainedMetadataPath(stageDirectory, entry.StageFileName);
            var staged = stagePath is null
                ? OutputFileState.Missing
                : await ComputeFileStateAsync(
                        stagePath,
                        options.MaximumWriteBytesPerMutation,
                        cancellationToken)
                    .ConfigureAwait(false);
            currentStates.Add(staged);

            var capturePath = paths.GetContainedMetadataPath(
                captureDirectory,
                $"{index:D6}.capture.bin");
            paths.ValidateMetadataFile(capturePath);
            var captured = await ComputeFileStateAsync(
                    capturePath,
                    options.MaximumFingerprintFileBytes,
                cancellationToken)
                .ConfigureAwait(false);
            currentStates.Add(captured);

            var discardPath = paths.GetContainedMetadataPath(
                discardDirectory,
                $"{index:D6}.discard.bin");
            paths.ValidateMetadataFile(discardPath);
            var discarded = await ComputeFileStateAsync(
                    discardPath,
                    options.MaximumFingerprintFileBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            currentStates.Add(discarded);

            var stageIsExpected = entry.Kind == OutputMutationKind.Write
                ? wasPublished
                    ? !staged.Exists
                    : staged == entry.Postimage
                : !staged.Exists;
            var captureIsExpectedPreimage = captured == entry.Preimage && entry.Preimage.Exists;
            var knownState = false;
            if (!wasPublished)
            {
                allPostimages = false;
                knownState = stageIsExpected
                    && !discarded.Exists
                    && (current == entry.Preimage && !captured.Exists
                        || entry.Preimage.Exists
                        && current is { Exists: false }
                        && captureIsExpectedPreimage);
            }
            else if (classifyAsRollingBack)
            {
                allPostimages = false;
                var captureIsSafe = !captured.Exists || captureIsExpectedPreimage;
                var discardIsSafe = !discarded.Exists
                    || entry.Postimage.Exists && discarded == entry.Postimage;
                knownState = stageIsExpected
                    && captureIsSafe
                    && discardIsSafe
                    && (current == entry.Preimage && !captured.Exists
                        || current == entry.Postimage && !discarded.Exists
                        || current is { Exists: false }
                        && discarded == entry.Postimage
                        && entry.Postimage.Exists);
            }
            else
            {
                knownState = stageIsExpected
                    && !discarded.Exists
                    && (!captured.Exists || captureIsExpectedPreimage)
                    && current is not null
                    && (current == entry.Preimage || current == entry.Postimage)
                    && !(captured.Exists && current == entry.Preimage);
                if (current != entry.Postimage)
                {
                    allPostimages = false;
                }
            }

            if (!knownState)
            {
                if (!unknown.Contains(entry.Path))
                {
                    unknown.Add(entry.Path);
                }

                allPostimages = false;
            }
        }

        OutputRecoveryDisposition disposition;
        if (journal.Phase is OutputTransactionPhase.Committed or OutputTransactionPhase.RolledBack)
        {
            disposition = OutputRecoveryDisposition.NoAction;
        }
        else if (journal.Phase == OutputTransactionPhase.Finalizing
                 || (journal.Phase == OutputTransactionPhase.RecoveryRequired
                     && string.Equals(
                         journal.OutcomeCode,
                         OutputOutcomeCodes.FinalizationFailed,
                         StringComparison.Ordinal)))
        {
            disposition = unknown.Count == 0 && allPostimages
                ? OutputRecoveryDisposition.FinalizeCommit
                : OutputRecoveryDisposition.RecoveryRequired;
        }
        else if (journal.Phase == OutputTransactionPhase.RecoveryRequired)
        {
            disposition = unknown.Count > 0
                ? OutputRecoveryDisposition.RecoveryRequired
                : allPostimages
                    ? OutputRecoveryDisposition.FinalizeCommit
                    : OutputRecoveryDisposition.RollBack;
        }
        else
        {
            disposition = unknown.Count > 0
                ? OutputRecoveryDisposition.RecoveryRequired
                : allPostimages
                    ? OutputRecoveryDisposition.FinalizeCommit
                    : OutputRecoveryDisposition.RollBack;
        }

        if (disposition == OutputRecoveryDisposition.RecoveryRequired && unknown.Count == 0)
        {
            unknown.AddRange(journal.Entries.Select(entry => entry.Path));
        }

        return new RecoveryClassification(
            new OutputRecoveryTransactionStatus(
                journal.TransactionId,
                journal.Phase,
                disposition,
                unknown),
            currentStates.ToImmutable());
    }

    private static bool HasBlockingRecoveryMaterial(OutputRecoveryReport report)
    {
        return report.Transactions.Any(
            status => status.Disposition != OutputRecoveryDisposition.NoAction);
    }

    private async Task<ImmutableArray<DiscoveredTransaction>> DiscoverTransactionsAsync(
        bool scavengeRetiredMaterial,
        CancellationToken cancellationToken)
    {
        paths.EnsureMetadataLayout();
        var builder = ImmutableArray.CreateBuilder<DiscoveredTransaction>();
        var inspectedDirectories = 0;
        foreach (var directory in Directory.EnumerateDirectories(paths.TransactionsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inspectedDirectories == OutputLimits.MaximumRecoveryTransactions + 2)
            {
                throw new OutputLimitExceededException("Too many interrupted output transactions were discovered.");
            }

            inspectedDirectories++;
            var directoryName = Path.GetFileName(directory);
            if (TryParsePreparingTransactionName(directoryName, out _))
            {
                if (scavengeRetiredMaterial)
                {
                    paths.DeleteMetadataTree(directory);
                }

                continue;
            }

            if (TryParseRetiredTransactionName(directoryName, out _))
            {
                if (scavengeRetiredMaterial)
                {
                    paths.DeleteMetadataTree(directory);
                }

                continue;
            }

            if (builder.Count == OutputLimits.MaximumRecoveryTransactions)
            {
                throw new OutputLimitExceededException("Too many interrupted output transactions were discovered.");
            }

            OutputTransactionId id;
            try
            {
                id = new OutputTransactionId(directoryName);
            }
            catch (ArgumentException)
            {
                throw new OutputPathSecurityException();
            }

            var journalPath = paths.GetContainedMetadataPath(directory, "journal.json");
            OutputTransactionJournal? journal = null;
            try
            {
                journal = await metadata.ReadJsonAsync<OutputTransactionJournal>(journalPath, cancellationToken)
                    .ConfigureAwait(false);
                if (journal is not null)
                {
                    ValidateJournal(journal, id);
                    ValidateTransactionMaterial(journal, directory);
                }
            }
            catch (Exception exception) when (exception is
                JsonException or
                ArgumentException or
                IOException or
                UnauthorizedAccessException or
                OutputCoordinatorException)
            {
                journal = null;
            }

            builder.Add(new DiscoveredTransaction(id, directory, journal));
        }

        if (Directory.EnumerateFiles(paths.TransactionsRoot, "*", SearchOption.TopDirectoryOnly).Any())
        {
            throw new OutputPathSecurityException();
        }

        return builder
            .OrderBy(transaction => transaction.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private void ValidateJournal(OutputTransactionJournal journal, OutputTransactionId expectedId)
    {
        if (journal.SchemaVersion != OutputTransactionJournal.CurrentSchemaVersion
            || journal.TransactionId != expectedId
            || !Enum.IsDefined(journal.Phase)
            || journal.Entries.IsDefaultOrEmpty
            || journal.Entries.Length > OutputLimits.MaximumMutationsPerApply
            || journal.Origins.IsDefaultOrEmpty
            || journal.Origins.Length > OutputLimits.MaximumOriginsPerApply
            || journal.CreatedDirectories.IsDefault
            || journal.CreatedDirectories.Length > OutputLimits.MaximumCreatedDirectoriesPerApply
            || journal.PublishedEntryCount < 0
            || journal.PublishedEntryCount > journal.Entries.Length
            || journal.Phase is OutputTransactionPhase.Preparing or OutputTransactionPhase.Prepared
               && journal.PublishedEntryCount != 0
            || journal.Phase is OutputTransactionPhase.Finalizing or OutputTransactionPhase.Committed
               && journal.PublishedEntryCount != journal.Entries.Length
            || journal.StartedAtUtc == default)
        {
            throw new ArgumentException("The output transaction journal is invalid.", nameof(journal));
        }

        _ = SemanticContractGuards.StableId(journal.ProjectId.Value, nameof(journal));
        _ = SemanticContractGuards.DefinedEnum(journal.GameFamily, nameof(journal));
        _ = SemanticContractGuards.ContractKey(journal.OutputMode, nameof(journal));
        _ = SemanticContractGuards.Sha256Fingerprint(journal.SemanticReviewHash, nameof(journal));
        if (journal.OutcomeCode is not null && !IsKnownOutcomeCode(journal.OutcomeCode))
        {
            throw new ArgumentException("The output transaction outcome code is invalid.", nameof(journal));
        }

        foreach (var origin in journal.Origins)
        {
            if (origin is null || !Enum.IsDefined(origin.Kind))
            {
                throw new ArgumentException("The output transaction origin is invalid.", nameof(journal));
            }

            _ = SemanticContractGuards.StableId(origin.Id, nameof(journal));
        }

        if (journal.Origins.Distinct().Count() != journal.Origins.Length)
        {
            throw new ArgumentException("The output transaction origins are not distinct.", nameof(journal));
        }

        var pathsSeen = new HashSet<string>(StringComparer.Ordinal);
        long backupBytes = 0;
        long writeBytes = 0;
        for (var index = 0; index < journal.Entries.Length; index++)
        {
            var entry = journal.Entries[index];
            if (entry is null)
            {
                throw new ArgumentException(
                    "The output transaction journal contains a null entry.",
                    nameof(journal));
            }

            if (entry.Path is null || entry.Preimage is null || entry.Postimage is null)
            {
                throw new ArgumentException(
                    "The output transaction journal contains incomplete entry state.",
                    nameof(journal));
            }

            var expectedStageName = entry.Kind == OutputMutationKind.Write
                ? $"{index:D6}.stage.bin"
                : null;
            var expectedBackupName = entry.Preimage.Exists
                ? $"{index:D6}.backup.bin"
                : null;
            if (!pathsSeen.Add(entry.Path.CanonicalKey)
                || !Enum.IsDefined(entry.Kind)
                || entry.Preimage == entry.Postimage
                || entry.Kind == OutputMutationKind.Write && !entry.Postimage.Exists
                || entry.Kind == OutputMutationKind.Delete && entry.Postimage.Exists
                || entry.Kind == OutputMutationKind.Delete && !entry.Preimage.Exists
                || entry.OwnershipClaims.IsDefaultOrEmpty
                || entry.OwnershipClaims.Length > OutputLimits.MaximumOwnershipClaimsPerMutation
                || entry.OwnershipClaims.Distinct().Count() != entry.OwnershipClaims.Length
                || entry.OwnershipClaims.Any(claim =>
                    claim is null
                    || claim.Address is null
                    || claim.Address.File is null
                    || claim.Address.File != entry.Path
                    || claim.GameFamily != journal.GameFamily
                    || claim.Address.ByteRange is { } range
                    && range.EndExclusive > (entry.Postimage.Exists
                        ? entry.Postimage.LengthBytes
                        : entry.Preimage.LengthBytes))
                || string.IsNullOrWhiteSpace(entry.OwnershipOutputMode)
                || !string.Equals(entry.StageFileName, expectedStageName, StringComparison.Ordinal)
                || !string.Equals(entry.BackupFileName, expectedBackupName, StringComparison.Ordinal))
            {
                throw new ArgumentException("The output transaction journal contains an invalid entry.", nameof(journal));
            }

            _ = SemanticContractGuards.ContractKey(entry.OwnershipOutputMode, nameof(journal));
            if (entry.Preimage.LengthBytes > OutputLimits.MaximumFingerprintFileBytes)
            {
                throw new OutputLimitExceededException("A transaction preimage exceeds its fingerprint limit.");
            }

            if (entry.Preimage.LengthBytes > OutputLimits.MaximumBackupBytesPerApply - backupBytes)
            {
                throw new OutputLimitExceededException("Transaction backups exceed their aggregate size limit.");
            }

            backupBytes += entry.Preimage.LengthBytes;
            if (entry.Postimage.LengthBytes > OutputLimits.MaximumWriteBytesPerMutation
                || entry.Postimage.LengthBytes > OutputLimits.MaximumWriteBytesPerApply - writeBytes)
            {
                throw new OutputLimitExceededException(
                    "Transaction postimages exceed their aggregate size limit.");
            }

            writeBytes += entry.Postimage.LengthBytes;

            paths.ValidateTarget(entry.Path);
        }

        if (journal.Entries.Any(entry => entry.RestoredFileDeleteEligibility.HasValue)
            && !journal.Origins.Any(origin => origin.Kind == OutputApplyOriginKind.Checkpoint))
        {
            throw new ArgumentException(
                "Only checkpoint recovery may carry restored cleanup provenance.",
                nameof(journal));
        }

        var createdDirectoryKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in journal.CreatedDirectories)
        {
            if (directory is null
                || !createdDirectoryKeys.Add(directory.CanonicalKey)
                || !journal.Entries.Any(entry => IsStrictAncestor(directory, entry.Path)))
            {
                throw new ArgumentException(
                    "The output transaction created-directory set is invalid.",
                    nameof(journal));
            }

            _ = paths.OwnedDirectoryExists(directory);
        }
    }

    private void ValidateTransactionMaterial(
        OutputTransactionJournal journal,
        string transactionDirectory)
    {
        paths.ValidateMetadataDirectory(transactionDirectory);
        var stageDirectory = paths.GetContainedMetadataPath(transactionDirectory, "stage");
        var backupDirectory = paths.GetContainedMetadataPath(transactionDirectory, "backup");
        var captureDirectory = paths.GetContainedMetadataPath(transactionDirectory, "capture");
        var discardDirectory = paths.GetContainedMetadataPath(transactionDirectory, "discard");
        paths.ValidateMetadataDirectory(stageDirectory);
        paths.ValidateMetadataDirectory(backupDirectory);
        paths.ValidateMetadataDirectory(captureDirectory);
        paths.ValidateMetadataDirectory(discardDirectory);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var expectedRootNames = new HashSet<string>(comparison)
        {
            "journal.json",
            "stage",
            "backup",
            "capture",
            "discard",
        };
        foreach (var entry in Directory.EnumerateFileSystemEntries(transactionDirectory))
        {
            var name = Path.GetFileName(entry);
            if (string.Equals(name, ".journal.json.pending.tmp", StringComparison.Ordinal))
            {
                paths.ValidateMetadataFile(entry);
                if (new FileInfo(entry).Length > OutputLimits.MaximumMetadataDocumentBytes)
                {
                    throw new OutputLimitExceededException("A transaction journal temporary file is too large.");
                }

                continue;
            }

            if (!expectedRootNames.Remove(name))
            {
                throw new OutputPathSecurityException();
            }
        }

        if (expectedRootNames.Count != 0)
        {
            throw new OutputPathSecurityException();
        }

        ValidateTransactionMaterialDirectory(
            stageDirectory,
            journal.Entries
                .Where(entry => entry.StageFileName is not null)
                .Select(entry => entry.StageFileName!),
            OutputLimits.MaximumWriteBytesPerApply);
        ValidateTransactionMaterialDirectory(
            backupDirectory,
            journal.Entries
                .Where(entry => entry.BackupFileName is not null)
                .Select(entry => entry.BackupFileName!),
            OutputLimits.MaximumBackupBytesPerApply);
        ValidateTransactionMaterialDirectory(
            captureDirectory,
            journal.Entries.Select((_, index) => $"{index:D6}.capture.bin"),
            options.MaximumBackupBytesPerApply);
        ValidateTransactionMaterialDirectory(
            discardDirectory,
            journal.Entries.Select((_, index) => $"{index:D6}.discard.bin"),
            options.MaximumWriteBytesPerApply);
    }

    private void ValidateTransactionMaterialDirectory(
        string directory,
        IEnumerable<string> allowedNames,
        long maximumTotalBytes)
    {
        if (Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).Any())
        {
            throw new OutputPathSecurityException();
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var allowed = allowedNames.ToHashSet(comparer);
        long totalBytes = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            if (count == OutputLimits.MaximumMutationsPerApply
                || !allowed.Contains(Path.GetFileName(file)))
            {
                throw new OutputPathSecurityException();
            }

            count++;
            paths.ValidateMetadataFile(file);
            totalBytes = checked(totalBytes + new FileInfo(file).Length);
            if (totalBytes > maximumTotalBytes)
            {
                throw new OutputLimitExceededException("Transaction material exceeds its aggregate size limit.");
            }
        }
    }

    private void RetireTransactionDirectory(
        OutputTransactionId transactionId,
        string transactionDirectory)
    {
        var tombstone = paths.ResolveTransactionTombstoneDirectory(transactionId);
        if (Directory.Exists(tombstone))
        {
            paths.DeleteMetadataTree(tombstone);
        }

        paths.MoveMetadataDirectory(transactionDirectory, tombstone, paths.TransactionsRoot);
        try
        {
            paths.DeleteMetadataTree(tombstone);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // The atomic rename removed this transaction from the recovery namespace.
            // Discovery retries bounded tombstone cleanup on the next operation.
        }
    }

    private static bool TryParseRetiredTransactionName(
        string directoryName,
        out OutputTransactionId transactionId)
    {
        const string prefix = "retired-";
        if (directoryName.StartsWith(prefix, StringComparison.Ordinal))
        {
            try
            {
                transactionId = new OutputTransactionId(directoryName[prefix.Length..]);
                return true;
            }
            catch (ArgumentException)
            {
            }
        }

        transactionId = default;
        return false;
    }

    private static bool TryParsePreparingTransactionName(
        string directoryName,
        out OutputTransactionId transactionId)
    {
        const string prefix = "preparing-";
        if (directoryName.StartsWith(prefix, StringComparison.Ordinal))
        {
            try
            {
                transactionId = new OutputTransactionId(directoryName[prefix.Length..]);
                return true;
            }
            catch (ArgumentException)
            {
            }
        }

        transactionId = default;
        return false;
    }

    private async Task<HashSet<string>> GetInterruptedPathKeysAsync(CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var discovered in await DiscoverTransactionsAsync(
                     scavengeRetiredMaterial: false,
                     cancellationToken).ConfigureAwait(false))
        {
            if (discovered.Journal is null)
            {
                continue;
            }

            foreach (var entry in discovered.Journal.Entries)
            {
                result.Add(entry.Path.CanonicalKey);
            }
        }

        return result;
    }

    private async Task<OutputFileState> ComputeTargetStateAsync(
        RelativeOutputPath relativePath,
        CancellationToken cancellationToken)
    {
        paths.ValidateTarget(relativePath);
        var path = paths.ResolveTarget(relativePath);
        var state = await ComputeFileStateAsync(path, options.MaximumFingerprintFileBytes, cancellationToken)
            .ConfigureAwait(false);
        paths.ValidateTarget(relativePath);
        return state;
    }

    private async Task<OutputFileState?> TryFingerprintTargetAsync(
        RelativeOutputPath relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ComputeTargetStateAsync(relativePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            OutputCoordinatorException)
        {
            return null;
        }
    }

    private static async Task<OutputFileState> ComputeFileStateAsync(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return OutputFileState.Missing;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ComputeStreamStateAsync(stream, maximumBytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OutputFileState> ComputeStreamStateAsync(
        FileStream stream,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (stream.Length > maximumBytes)
        {
            throw new OutputLimitExceededException(
                $"An output file exceeds the configured fingerprint limit of {maximumBytes} bytes.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        long total = 0;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new OutputLimitExceededException(
                    $"An output file exceeds the configured fingerprint limit of {maximumBytes} bytes.");
            }

            hash.AppendData(buffer, 0, read);
        }

        return OutputFileState.Existing(Convert.ToHexStringLower(hash.GetHashAndReset()), total);
    }

    private async Task WritePostimageAsync(
        string stagePath,
        ImmutableArray<byte> postimage,
        CancellationToken cancellationToken)
    {
        await using var destination = metadata.OpenPrivateFile(
            stagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await destination.WriteAsync(postimage.AsMemory(), cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
        OutputFileSystemDurability.FlushParent(stagePath);
    }

    private async Task CopyTargetToBackupAsync(
        RelativeOutputPath relativePath,
        string backupPath,
        CancellationToken cancellationToken)
    {
        paths.ValidateTarget(relativePath);
        var targetPath = paths.ResolveTarget(relativePath);
        await using var source = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length > options.MaximumFingerprintFileBytes)
        {
            throw new OutputLimitExceededException("An output preimage exceeds the configured backup limit.");
        }

        await using var destination = metadata.OpenPrivateFile(
            backupPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
        OutputFileSystemDurability.FlushParent(backupPath);
        paths.ValidateTarget(relativePath);
    }

    private async Task<OutputOwnershipInventory> ReadInventoryAsync(CancellationToken cancellationToken)
    {
        var path = paths.GetContainedMetadataPath(paths.MetadataRoot, "ownership.json");
        var inventory = await metadata.ReadJsonAsync<OutputOwnershipInventory>(path, cancellationToken)
            .ConfigureAwait(false);
        if (inventory is null)
        {
            return OutputOwnershipInventory.Empty;
        }

        ValidateInventoryPaths(inventory);
        return inventory;
    }

    private void ValidateInventoryPaths(OutputOwnershipInventory inventory)
    {
        foreach (var record in inventory.Files)
        {
            paths.ValidateTarget(record.Path);
        }

        foreach (var directory in inventory.CreatedDirectories)
        {
            _ = paths.OwnedDirectoryExists(directory.Path);
            paths.ValidateTarget(directory.AuthorizationTarget);
            if (!IsStrictAncestor(directory.Path, directory.AuthorizationTarget))
            {
                throw new OutputPathSecurityException();
            }
        }
    }

    private async Task PruneOwnedDirectoriesAsync(
        ProjectId projectId,
        GameFamily gameFamily,
        IEnumerable<RelativeOutputPath> provenRemovedTargets,
        CancellationToken cancellationToken)
    {
        var provenKeys = provenRemovedTargets
            .Select(path => path.CanonicalKey)
            .ToHashSet(StringComparer.Ordinal);
        if (provenKeys.Count == 0)
        {
            return;
        }

        var inventory = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
        var scoped = inventory.CreatedDirectories
            .Where(record => record.ProjectId == projectId
                             && record.GameFamily == gameFamily
                             && provenKeys.Contains(record.AuthorizationTarget.CanonicalKey))
            .ToImmutableArray();
        paths.DeleteEmptyOwnedDirectories(scoped.Select(record => record.Path));
        var retained = inventory.CreatedDirectories
            .Where(record => !scoped.Contains(record) || paths.OwnedDirectoryExists(record.Path))
            .ToImmutableArray();
        if (retained.Length == inventory.CreatedDirectories.Length)
        {
            return;
        }

        await WriteInventoryAsync(
                new OutputOwnershipInventory(
                    OutputOwnershipInventory.CurrentSchemaVersion,
                    inventory.Files,
                    retained),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HashSet<string>> RemoveMissingOwnershipRecordsAsync(
        ProjectId projectId,
        GameFamily gameFamily,
        IEnumerable<RelativeOutputPath> candidates,
        CancellationToken cancellationToken)
    {
        var candidateKeys = candidates
            .Select(path => path.CanonicalKey)
            .ToHashSet(StringComparer.Ordinal);
        if (candidateKeys.Count == 0)
        {
            return [];
        }

        var inventory = await ReadInventoryAsync(cancellationToken).ConfigureAwait(false);
        var removableKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in inventory.Files)
        {
            if (!candidateKeys.Contains(record.Path.CanonicalKey)
                || !OwnershipScopeMatches(record, projectId, gameFamily))
            {
                continue;
            }

            var current = await ComputeTargetStateAsync(record.Path, cancellationToken).ConfigureAwait(false);
            if (!current.Exists)
            {
                removableKeys.Add(record.Path.CanonicalKey);
            }
        }

        if (removableKeys.Count == 0)
        {
            return removableKeys;
        }

        await WriteInventoryAsync(
                new OutputOwnershipInventory(
                    OutputOwnershipInventory.CurrentSchemaVersion,
                    inventory.Files.Where(record => !removableKeys.Contains(record.Path.CanonicalKey)),
                    inventory.CreatedDirectories),
                cancellationToken)
            .ConfigureAwait(false);
        return removableKeys;
    }

    private Task WriteInventoryAsync(
        OutputOwnershipInventory inventory,
        CancellationToken cancellationToken)
    {
        var path = paths.GetContainedMetadataPath(paths.MetadataRoot, "ownership.json");
        return metadata.WriteJsonAtomicAsync(path, inventory, cancellationToken);
    }

    private async Task<OutputApplyHistoryDocument> ReadHistoryAsync(CancellationToken cancellationToken)
    {
        var path = paths.GetContainedMetadataPath(paths.MetadataRoot, "history.json");
        var history = await metadata.ReadJsonAsync<OutputApplyHistoryDocument>(path, cancellationToken)
            .ConfigureAwait(false);
        if (history is null)
        {
            return new OutputApplyHistoryDocument(
                OutputApplyHistoryDocument.CurrentSchemaVersion,
                ImmutableArray<OutputApplyReceipt>.Empty);
        }

        if (history.SchemaVersion != OutputApplyHistoryDocument.CurrentSchemaVersion
            || history.Receipts.IsDefault
            || history.Receipts.Length > OutputLimits.MaximumHistoryReceipts
            || history.Receipts.Any(receipt => receipt is null)
            || history.Receipts.Select(receipt => receipt.TransactionId).Distinct().Count()
            != history.Receipts.Length)
        {
            throw new OutputCoordinatorException("The output apply history is invalid or unsupported.");
        }

        return history;
    }

    private async Task AppendHistoryAsync(OutputApplyReceipt receipt, CancellationToken cancellationToken)
    {
        var history = await ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
        var candidates = history.Receipts
            .Where(existing => existing.TransactionId != receipt.TransactionId)
            .OrderBy(existing => existing.CompletedAtUtc)
            .ThenBy(existing => existing.TransactionId.Value, StringComparer.Ordinal)
            .TakeLast(options.MaximumHistoryReceipts - 1)
            .ToImmutableArray();
        var empty = new OutputApplyHistoryDocument(
            OutputApplyHistoryDocument.CurrentSchemaVersion,
            ImmutableArray<OutputApplyReceipt>.Empty);
        var receiptBytes = metadata.GetJsonByteCount(receipt);
        long serializedBytes = metadata.GetJsonByteCount(empty) + receiptBytes;
        if (serializedBytes > OutputLimits.MaximumMetadataDocumentBytes)
        {
            throw new OutputLimitExceededException(
                "An output apply receipt exceeds the metadata document limit.");
        }

        var retainedNewestFirst = ImmutableArray.CreateBuilder<OutputApplyReceipt>();
        retainedNewestFirst.Add(receipt);
        foreach (var candidate in candidates.Reverse())
        {
            var candidateBytes = metadata.GetJsonByteCount(candidate);
            const int separatorBytes = 1;
            if (serializedBytes > OutputLimits.MaximumMetadataDocumentBytes - candidateBytes - separatorBytes)
            {
                break;
            }

            serializedBytes += candidateBytes + separatorBytes;
            retainedNewestFirst.Add(candidate);
        }

        var receipts = retainedNewestFirst
            .OrderBy(existing => existing.CompletedAtUtc)
            .ThenBy(existing => existing.TransactionId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var updated = new OutputApplyHistoryDocument(
            OutputApplyHistoryDocument.CurrentSchemaVersion,
            receipts);
        _ = metadata.GetJsonByteCount(updated);
        var path = paths.GetContainedMetadataPath(paths.MetadataRoot, "history.json");
        await metadata.WriteJsonAtomicAsync(path, updated, cancellationToken).ConfigureAwait(false);
    }

    private static OutputStateRevision ComputeHistoryRevision(OutputApplyHistoryDocument history)
    {
        var tokens = new List<string?>
        {
            history.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var receipt in history.Receipts.OrderBy(item => item.TransactionId.Value, StringComparer.Ordinal))
        {
            tokens.Add(receipt.TransactionId.Value);
            tokens.Add(receipt.ProjectId.Value);
            tokens.Add(((int)receipt.GameFamily).ToString(System.Globalization.CultureInfo.InvariantCulture));
            tokens.Add(receipt.OutputMode);
            tokens.Add(receipt.SemanticReviewHash);
            tokens.Add(((int)receipt.Outcome).ToString(System.Globalization.CultureInfo.InvariantCulture));
            tokens.Add(receipt.StartedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            tokens.Add(receipt.CompletedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            tokens.Add(receipt.OutcomeCode);
            foreach (var target in receipt.Targets)
            {
                tokens.Add(target.Path.CanonicalKey);
                tokens.AddRange(OutputRevisionCalculator.FileStateTokens(target.Preimage));
                tokens.AddRange(OutputRevisionCalculator.FileStateTokens(target.Postimage));
            }
        }

        return OutputRevisionCalculator.FromTokens("output-history-v1", tokens);
    }

    private static OutputStateRevision ComputeInventoryRevision(OutputOwnershipInventory inventory)
    {
        var tokens = new List<string?>
        {
            inventory.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var record in inventory.Files.OrderBy(item => item.Path.CanonicalKey, StringComparer.Ordinal))
        {
            tokens.Add(record.Path.CanonicalKey);
            tokens.AddRange(OutputRevisionCalculator.FileStateTokens(record.CurrentState));
            tokens.Add(record.ProjectId.Value);
            tokens.Add(((int)record.GameFamily).ToString(System.Globalization.CultureInfo.InvariantCulture));
            tokens.Add(record.OutputMode);
            tokens.Add(record.FileDeleteEligible ? "1" : "0");
            tokens.Add(record.TransactionId.Value);
            foreach (var claim in record.Claims)
            {
                AppendOwnershipRevisionTokens(tokens, claim);
            }
        }

        foreach (var directory in inventory.CreatedDirectories
                     .OrderBy(item => item.Path.CanonicalKey, StringComparer.Ordinal))
        {
            tokens.Add(directory.Path.CanonicalKey);
            tokens.Add(directory.AuthorizationTarget.CanonicalKey);
            tokens.Add(directory.ProjectId.Value);
            tokens.Add(((int)directory.GameFamily).ToString(System.Globalization.CultureInfo.InvariantCulture));
            tokens.Add(directory.OutputMode);
            tokens.Add(directory.TransactionId.Value);
        }

        return OutputRevisionCalculator.FromTokens("output-ownership-v1", tokens);
    }

    private static OutputStateRevision ComputeIntegrityRevision(
        IEnumerable<OutputIntegrityEntry> entries)
    {
        var tokens = new List<string?>();
        foreach (var entry in entries.OrderBy(item => item.Path.CanonicalKey, StringComparer.Ordinal))
        {
            tokens.Add(entry.Path.CanonicalKey);
            tokens.Add(((int)entry.Classification).ToString(System.Globalization.CultureInfo.InvariantCulture));
            tokens.AddRange(OutputRevisionCalculator.FileStateTokens(entry.CurrentState));
            tokens.AddRange(OutputRevisionCalculator.FileStateTokens(entry.ExpectedOwnedState));
        }

        return OutputRevisionCalculator.FromTokens("output-integrity-v1", tokens);
    }

    private static string ComputeCheckpointManifestFingerprint(
        OutputCheckpointId id,
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        DateTimeOffset createdAtUtc,
        string? label,
        IEnumerable<OutputCheckpointEntry> entries)
    {
        var tokens = new List<string?>
        {
            OutputCheckpointManifest.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            id.Value,
            projectId.Value,
            ((int)gameFamily).ToString(System.Globalization.CultureInfo.InvariantCulture),
            outputMode,
            createdAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            label,
            ((int)OutputCheckpointCoverage.OwnedFiles).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var entry in entries)
        {
            tokens.Add(entry.Path.CanonicalKey);
            tokens.AddRange(OutputRevisionCalculator.FileStateTokens(entry.State));
            tokens.Add(entry.OutputMode);
            tokens.Add(entry.FileDeleteEligible ? "1" : "0");
            tokens.Add(entry.ContentFileName);
            foreach (var claim in entry.OwnershipClaims)
            {
                AppendOwnershipRevisionTokens(tokens, claim);
            }
        }

        return OutputRevisionCalculator.FromTokens("output-checkpoint-manifest-v1", tokens).Value;
    }

    private static OutputStateRevision ComputeCheckpointListRevision(
        IEnumerable<OutputCheckpointSummary> summaries)
    {
        var tokens = summaries
            .OrderBy(summary => summary.Id.Value, StringComparer.Ordinal)
            .SelectMany(summary => new string?[]
            {
                summary.Id.Value,
                summary.ManifestFingerprint,
            });
        return OutputRevisionCalculator.FromTokens("output-checkpoint-list-v1", tokens);
    }

    private static void AppendOwnershipRevisionTokens(List<string?> tokens, OwnedTarget claim)
    {
        tokens.Add(((int)claim.GameFamily).ToString(System.Globalization.CultureInfo.InvariantCulture));
        tokens.Add(claim.Address.File.CanonicalKey);
        tokens.Add(((int)claim.Address.ScopeKind).ToString(System.Globalization.CultureInfo.InvariantCulture));
        tokens.Add(claim.Address.ArchiveMember?.Value);
        tokens.Add(claim.Address.Record?.Domain.Value);
        tokens.Add(claim.Address.Record?.RecordKind.Key);
        tokens.Add(claim.Address.Record?.RecordKind.SchemaVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        tokens.Add(claim.Address.Record?.RecordId.Value);
        tokens.Add(claim.Address.Record?.SubrecordId?.Value);
        tokens.Add(claim.Address.ByteRange?.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        tokens.Add(claim.Address.ByteRange?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        tokens.Add(claim.OwnerId.Value);
        tokens.Add(claim.PreservationRule.Key);
        tokens.Add(claim.PreservationRule.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        tokens.Add(claim.PreservationRule.PreservesUnownedData ? "1" : "0");
        tokens.Add(claim.PreservationRule.RequiresPreimage ? "1" : "0");
    }

    private static string ComputeRecoveryJournalFingerprint(OutputTransactionJournal journal)
    {
        return OutputRevisionCalculator.FromTokens(
            "output-recovery-journal-v1",
            EnumerateRecoveryJournalTokens(journal),
            OutputLimits.MaximumJournalRevisionTokens).Value;
    }

    private static IEnumerable<string?> EnumerateRecoveryJournalTokens(OutputTransactionJournal journal)
    {
        yield return journal.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return journal.TransactionId.Value;
        yield return ((int)journal.Phase).ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return journal.ProjectId.Value;
        yield return ((int)journal.GameFamily).ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return journal.OutputMode;
        yield return journal.SemanticReviewHash;
        yield return journal.StartedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        yield return journal.OutcomeCode;
        yield return journal.PublishedEntryCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var origin in journal.Origins)
        {
            yield return ((int)origin.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return origin.Id;
        }

        foreach (var entry in journal.Entries)
        {
            yield return entry.Path.CanonicalKey;
            yield return ((int)entry.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var token in OutputRevisionCalculator.FileStateTokens(entry.Preimage))
            {
                yield return token;
            }

            foreach (var token in OutputRevisionCalculator.FileStateTokens(entry.Postimage))
            {
                yield return token;
            }

            yield return entry.OwnershipOutputMode;
            yield return entry.RestoredFileDeleteEligibility?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return entry.StageFileName;
            yield return entry.BackupFileName;
            foreach (var claim in entry.OwnershipClaims)
            {
                foreach (var token in EnumerateOwnershipRevisionTokens(claim))
                {
                    yield return token;
                }
            }
        }

        foreach (var directory in journal.CreatedDirectories)
        {
            yield return directory.CanonicalKey;
        }
    }

    private static IEnumerable<string?> EnumerateOwnershipRevisionTokens(OwnedTarget claim)
    {
        yield return ((int)claim.GameFamily).ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return claim.Address.File.CanonicalKey;
        yield return ((int)claim.Address.ScopeKind).ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return claim.Address.ArchiveMember?.Value;
        yield return claim.Address.Record?.Domain.Value;
        yield return claim.Address.Record?.RecordKind.Key;
        yield return claim.Address.Record?.RecordKind.SchemaVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        yield return claim.Address.Record?.RecordId.Value;
        yield return claim.Address.Record?.SubrecordId?.Value;
        yield return claim.Address.ByteRange?.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return claim.Address.ByteRange?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return claim.OwnerId.Value;
        yield return claim.PreservationRule.Key;
        yield return claim.PreservationRule.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return claim.PreservationRule.PreservesUnownedData ? "1" : "0";
        yield return claim.PreservationRule.RequiresPreimage ? "1" : "0";
    }

    private static void AppendRecoveryRevisionTokens(
        List<string?> tokens,
        OutputRecoveryTransactionStatus status,
        ImmutableArray<OutputFileState?>? currentStates)
    {
        tokens.Add(((int)status.Phase).ToString(System.Globalization.CultureInfo.InvariantCulture));
        tokens.Add(((int)status.Disposition).ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var path in status.UnknownTargets)
        {
            tokens.Add(path.CanonicalKey);
        }

        if (currentStates is null)
        {
            tokens.Add("invalid-journal");
            return;
        }

        tokens.Add(OutputRevisionCalculator.FromTokens(
            "output-recovery-current-states-v1",
            currentStates.Value.SelectMany(OutputRevisionCalculator.FileStateTokens)).Value);
    }

    private static OutputApplyReceipt BuildReceipt(
        OutputTransactionJournal journal,
        OutputApplyOutcome outcome,
        DateTimeOffset completedAtUtc,
        string? outcomeCode)
    {
        return new OutputApplyReceipt(
            journal.TransactionId,
            journal.ProjectId,
            journal.GameFamily,
            journal.OutputMode,
            journal.SemanticReviewHash,
            outcome,
            journal.StartedAtUtc,
            completedAtUtc,
            journal.Origins,
            journal.Entries.Select(entry => new OutputApplyReceiptTarget(
                entry.Path,
                entry.Kind,
                entry.Preimage,
                entry.Postimage,
                entry.OwnershipClaims)),
            outcomeCode);
    }

    private async Task<FileStream> AcquireOutputRootLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = paths.GetContainedMetadataPath(paths.MetadataRoot, "output.lock");
        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return metadata.OpenPrivateFile(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    FileOptions.None);
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(startedAt) < options.WriterLockTimeout)
            {
                var remaining = options.WriterLockTimeout - Stopwatch.GetElapsedTime(startedAt);
                var delay = remaining < options.WriterLockRetryDelay ? remaining : options.WriterLockRetryDelay;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                throw new OutputRootLockTimeoutException(options.WriterLockTimeout);
            }
        }
    }

    private void ValidatePlanLimits(OutputApplyPlan plan)
    {
        if (plan.Mutations.Length > options.MaximumMutationsPerApply
            || plan.ReadDependencies.Length > options.MaximumIntegrityEntries
            || plan.DirectoryMembershipDependencies.Length > options.MaximumIntegrityEntries
            || plan.Mutations.Any(mutation =>
                mutation.OwnershipClaims.Length > options.MaximumOwnershipClaimsPerMutation
                || mutation.Postimage.Length > options.MaximumWriteBytesPerMutation))
        {
            throw new OutputLimitExceededException("The output apply exceeds the configured mutation limits.");
        }

        var total = plan.Mutations.Sum(mutation => (long)mutation.Postimage.Length);
        if (total > options.MaximumWriteBytesPerApply)
        {
            throw new OutputLimitExceededException("The output apply exceeds the configured aggregate byte limit.");
        }

        long backupBytes = 0;
        foreach (var mutation in plan.Mutations)
        {
            if (mutation.ExpectedPreimage.LengthBytes > options.MaximumBackupBytesPerApply - backupBytes)
            {
                throw new OutputLimitExceededException("The output apply exceeds the configured aggregate backup limit.");
            }

            backupBytes += mutation.ExpectedPreimage.LengthBytes;
        }

        foreach (var mutation in plan.Mutations)
        {
            paths.ValidateTarget(mutation.Path);
        }

        foreach (var dependency in plan.ReadDependencies)
        {
            paths.ValidateTarget(dependency.Path);
        }

        foreach (var dependency in plan.DirectoryMembershipDependencies)
        {
            _ = paths.OwnedDirectoryExists(dependency.Directory);
        }
    }

    private Dictionary<string, OutputBaselineEntry> ValidateBaseline(IEnumerable<OutputBaselineEntry>? baseline)
    {
        var result = new Dictionary<string, OutputBaselineEntry>(StringComparer.Ordinal);
        if (baseline is null)
        {
            return result;
        }

        foreach (var entry in baseline)
        {
            if (entry is null || !result.TryAdd(entry.Path.CanonicalKey, entry))
            {
                throw new ArgumentException("Output baseline entries must be non-null and distinct.", nameof(baseline));
            }

            paths.ValidateTarget(entry.Path);

            if (result.Count > options.MaximumIntegrityEntries)
            {
                throw new OutputLimitExceededException("The output baseline exceeds the configured entry limit.");
            }
        }

        return result;
    }

    private ImmutableArray<RelativeOutputPath> ValidateRequestedPaths(IEnumerable<RelativeOutputPath> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var builder = ImmutableArray.CreateBuilder<RelativeOutputPath>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in requested)
        {
            if (path is null || !seen.Add(path.CanonicalKey))
            {
                throw new ArgumentException("Requested output paths must be non-null and distinct.", nameof(requested));
            }

            if (builder.Count == options.MaximumMutationsPerApply)
            {
                throw new OutputLimitExceededException("Too many output paths were requested.");
            }

            paths.ValidateTarget(path);
            builder.Add(path);
        }

        return builder.ToImmutable();
    }

    private static bool OwnershipScopeMatches(
        OutputOwnershipRecord record,
        ProjectId projectId,
        GameFamily gameFamily)
    {
        return record.ProjectId == projectId
               && record.GameFamily == gameFamily;
    }

    private static bool IsFatal(Exception exception)
    {
        return exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            || exception.InnerException is not null && IsFatal(exception.InnerException);
    }

    private sealed record DiscoveredTransaction(
        OutputTransactionId Id,
        string Directory,
        OutputTransactionJournal? Journal);

    private sealed record RecoveryClassification(
        OutputRecoveryTransactionStatus Status,
        ImmutableArray<OutputFileState?> CurrentStates);

    private sealed record CheckpointRestoreMaterial(
        OutputCheckpointRestorePreview Preview,
        ImmutableArray<OutputMutation> Mutations,
        ImmutableArray<OutputReadDependency> ReadDependencies);
}
