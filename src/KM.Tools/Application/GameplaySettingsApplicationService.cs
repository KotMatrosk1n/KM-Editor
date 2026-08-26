// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using KM.Api.Output;
using KM.Api.RuntimeSettings;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;

namespace KM.Tools.Application;

/// <summary>
/// Owns short-lived, single-use desktop reviews for the exact title-scoped gameplay settings
/// journal. Durable package and transaction truth remains below the selected output root.
/// </summary>
public sealed class GameplaySettingsApplicationService
{
    private static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(10);
    private readonly GameplaySettingsBundleAuthority bundleAuthority;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, CachedReview> reviews = new(StringComparer.Ordinal);

    public GameplaySettingsApplicationService(
        GameplaySettingsBundleAuthority? bundleAuthority = null)
    {
        this.bundleAuthority = bundleAuthority ?? GameplaySettingsBundleAuthority.DenyAll;
    }

    public async Task<GetGameplaySettingsResponse> GetAsync(
        GetGameplaySettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        var loaded = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
        return new GetGameplaySettingsResponse(loaded.State, loaded.Dto);
    }

    public async Task<PreviewGameplaySettingsUpdateResponse> PreviewUpdateAsync(
        PreviewGameplaySettingsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        var loaded = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
        if (loaded.State is not (GameplaySettingsStateDto.Ready or GameplaySettingsStateDto.Repairable)
            || loaded.Snapshot is null
            || loaded.Manifest is null
            || loaded.JournalBytes is null
            || loaded.AuthorityKey is null
            || loaded.Dto is null)
        {
            throw new GameplaySettingsUnavailableException(loaded.State);
        }

        var expectedGeneration = ParseGeneration(request.ExpectedGeneration);
        if (loaded.Snapshot.Generation != expectedGeneration)
        {
            throw new GameplaySettingsStateConflictException();
        }

        var plan = GameplaySettingsEditPlanner.CreateUpdate(
            loaded.JournalBytes,
            context.ProjectId,
            context.GameFamily,
            loaded.Manifest.TitleId,
            new GameplaySettingsEditRequest(
                request.ExperienceShareEnabled,
                request.ExperienceRateBasisPoints,
                request.LevelCapEnabled,
                request.LevelCap));
        var reviewId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(ReviewLifetime);
        var before = ToDto(loaded.Manifest, plan.Before);
        var after = ToDto(loaded.Manifest, plan.After);
        var applyPlan = WithPackageReadDependencies(
            plan.ApplyPlan,
            loaded.PackageReadDependencies);
        lock (syncRoot)
        {
            PruneReviews(DateTimeOffset.UtcNow);
            if (reviews.Count == GameplaySettingsContract.MaximumCachedReviews)
            {
                var oldest = reviews.Values
                    .OrderBy(review => review.ExpiresAtUtc)
                    .ThenBy(review => review.ReviewId, StringComparer.Ordinal)
                    .First();
                reviews.Remove(oldest.ReviewId);
            }

            reviews.Add(
                reviewId,
                new CachedReview(
                    reviewId,
                    context.ScopeKey,
                    expiresAtUtc,
                    loaded.AuthorityKey,
                    loaded.Snapshot.Generation,
                    applyPlan));
        }

        return new PreviewGameplaySettingsUpdateResponse(
            reviewId,
            expiresAtUtc,
            before,
            after);
    }

    public async Task<ApplyGameplaySettingsUpdateResponse> ApplyUpdateAsync(
        ApplyGameplaySettingsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ReviewId)
            || request.ReviewId.Length > GameplaySettingsContract.MaximumReviewIdLength
            || request.ReviewId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new GameplaySettingsReviewExpiredException();
        }

        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        CachedReview review;
        lock (syncRoot)
        {
            PruneReviews(DateTimeOffset.UtcNow);
            if (!reviews.Remove(request.ReviewId, out review!)
                || review.ScopeKey != context.ScopeKey
                || review.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                throw new GameplaySettingsReviewExpiredException();
            }
        }

        var loaded = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
        if (loaded.State is not (GameplaySettingsStateDto.Ready or GameplaySettingsStateDto.Repairable)
            || loaded.AuthorityKey != review.AuthorityKey
            || loaded.Snapshot?.Generation != review.Generation)
        {
            throw new GameplaySettingsStateConflictException();
        }

        var result = await context.Coordinator
            .ApplyAsync(review.ApplyPlan, cancellationToken)
            .ConfigureAwait(false);
        var outcome = result.Outcome switch
        {
            OutputApplyOutcome.Committed => GameplaySettingsApplyOutcomeDto.Committed,
            OutputApplyOutcome.RolledBack => GameplaySettingsApplyOutcomeDto.RolledBack,
            OutputApplyOutcome.RecoveryRequired => GameplaySettingsApplyOutcomeDto.RecoveryRequired,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        GameplaySettingsSnapshotDto? snapshot = null;
        if (result.Outcome is OutputApplyOutcome.Committed or OutputApplyOutcome.RolledBack)
        {
            try
            {
                var afterApply = await LoadAsync(context, CancellationToken.None).ConfigureAwait(false);
                snapshot = afterApply.Dto;
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                // The durable transaction outcome is authoritative. A best-effort
                // refresh must never turn a successful commit into a reported failure.
                snapshot = null;
            }
        }

        return new ApplyGameplaySettingsUpdateResponse(
            result.TransactionId.Value,
            outcome,
            snapshot);
    }

    private async Task<LoadedState> LoadAsync(
        OutputScopeContext context,
        CancellationToken cancellationToken)
    {
        var selectedGame = context.Paths.SelectedGame
            ?? throw new OutputScopeMismatchException();
        var titleId = ProjectGameMetadata.Get(selectedGame).TitleId;
        var settingsFamily = GameplayBundleDeploymentPlanner.ToSettingsFamily(context.GameFamily);
        var settingsPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{titleId:X16}/settings.bin");
        var manifestPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{titleId:X16}/bundle.manifest");
        var outputRoot = context.Paths.OutputRootPath!;
        var settingsAbsolutePath = ToAbsolutePath(outputRoot, settingsPath);
        var manifestAbsolutePath = ToAbsolutePath(outputRoot, manifestPath);
        var settingsExists = File.Exists(settingsAbsolutePath);
        var manifestExists = File.Exists(manifestAbsolutePath);
        var inventory = await context.Coordinator
            .GetOwnershipInventoryAsync(cancellationToken)
            .ConfigureAwait(false);
        var packageOwnership = inventory.Files
            .Where(record => record.ProjectId == context.ProjectId
                && record.GameFamily == context.GameFamily
                && record.OutputMode == GameplayBundleDeploymentPlanner.OutputMode)
            .ToImmutableArray();
        if (!settingsExists && !manifestExists && packageOwnership.IsEmpty)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Missing);
        }

        if (!settingsExists || !manifestExists)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Incomplete);
        }

        if (packageOwnership.IsEmpty)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Unmanaged);
        }

        var recovery = await context.Coordinator
            .InspectRecoveryAsync(cancellationToken)
            .ConfigureAwait(false);
        if (recovery.RequiresRecovery)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Conflict);
        }

        var integrity = await context.Coordinator
            .ScanIntegrityAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (packageOwnership.Any(record =>
                integrity.Entries.FirstOrDefault(entry => entry.Path == record.Path)?.Classification
                    != OutputIntegrityClassification.KmOwnedCurrent))
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Conflict);
        }

        byte[] manifestBytes;
        byte[] journalBytes;
        try
        {
            manifestBytes = await ReadExactBoundedAsync(
                    outputRoot,
                    manifestAbsolutePath,
                    expectedLength: null,
                    GameplayBundleIdentity.MaximumSerializationBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            journalBytes = await ReadExactBoundedAsync(
                    outputRoot,
                    settingsAbsolutePath,
                    GameplaySettingsJournal.JournalSize,
                    GameplaySettingsJournal.JournalSize,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnavailableFileSystemException(exception))
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Conflict);
        }

        GameplayBundleManifest manifest;
        try
        {
            manifest = GameplayBundleIdentity.ParseManifest(manifestBytes);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Corrupt);
        }

        if (manifest.TitleId != titleId)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Conflict);
        }

        var packageVersion = manifest.PackageVersion;
        if (packageVersion.Major > ushort.MaxValue
            || packageVersion.Minor > ushort.MaxValue
            || packageVersion.Patch > ushort.MaxValue)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Unsupported);
        }

        var authorityKey = GameplaySettingsBundleAuthorityKey.FromManifest(
            context.GameFamily,
            manifest,
            manifestBytes);
        if (!bundleAuthority.IsAuthorized(authorityKey))
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Unsupported);
        }

        var expectedPaths = manifest.Components
            .Select(component => new RelativeOutputPath(component.Path).CanonicalKey)
            .Append(manifestPath.CanonicalKey)
            .Append(settingsPath.CanonicalKey)
            .ToHashSet(StringComparer.Ordinal);
        if (packageOwnership.Length != expectedPaths.Count
            || packageOwnership.Any(record => !expectedPaths.Contains(record.Path.CanonicalKey)))
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Conflict);
        }

        var ownershipByPath = packageOwnership.ToDictionary(
            record => record.Path.CanonicalKey,
            StringComparer.Ordinal);
        if (!ownershipByPath.TryGetValue(settingsPath.CanonicalKey, out var settingsOwnership)
            || settingsOwnership.RuntimeMutableDescriptor is not { } runtimeDescriptor
            || runtimeDescriptor.TitleId != titleId
            || !ownershipByPath.TryGetValue(manifestPath.CanonicalKey, out var manifestOwnership)
            || manifestOwnership.RuntimeMutableDescriptor is not null
            || ComputeState(manifestBytes) != manifestOwnership.CurrentState)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Conflict);
        }

        foreach (var component in manifest.Components)
        {
            var path = new RelativeOutputPath(component.Path);
            if (!ownershipByPath.TryGetValue(path.CanonicalKey, out var ownership)
                || ownership.RuntimeMutableDescriptor is not null
                || ownership.CurrentState.LengthBytes != checked((long)component.Length)
                || !string.Equals(
                    ownership.CurrentState.Sha256,
                    component.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return LoadedState.Unavailable(GameplaySettingsStateDto.Conflict);
            }
        }

        var inspection = GameplaySettingsJournal.Inspect(journalBytes, settingsFamily, titleId);
        var state = inspection.Disposition switch
        {
            GameplaySettingsJournalDisposition.Ready => GameplaySettingsStateDto.Ready,
            GameplaySettingsJournalDisposition.ReadyWithRepairableCompanion =>
                GameplaySettingsStateDto.Repairable,
            GameplaySettingsJournalDisposition.UnsupportedSchema => GameplaySettingsStateDto.Unsupported,
            GameplaySettingsJournalDisposition.Corrupt => GameplaySettingsStateDto.Corrupt,
            GameplaySettingsJournalDisposition.ReadOnlyForeignConflict or
                GameplaySettingsJournalDisposition.ReadOnlyGenerationConflict =>
                GameplaySettingsStateDto.Conflict,
            _ => GameplaySettingsStateDto.Incomplete,
        };
        if (state is not (GameplaySettingsStateDto.Ready or GameplaySettingsStateDto.Repairable)
            || !inspection.WritesAllowed
            || inspection.ActiveSnapshot is null)
        {
            return LoadedState.Unavailable(state);
        }

        if (!HasSupportedMenuValues(inspection.ActiveSnapshot))
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Unsupported);
        }

        var expectedWriter = new GameplaySettingsWriterVersion(
            (ushort)packageVersion.Major,
            (ushort)packageVersion.Minor,
            (ushort)packageVersion.Patch);
        if (inspection.ActiveSnapshot.WriterVersion != expectedWriter
            || runtimeDescriptor.MinimumGeneration is null
            || inspection.ActiveSnapshot.Generation < runtimeDescriptor.MinimumGeneration.Value)
        {
            return LoadedState.Unavailable(GameplaySettingsStateDto.Conflict);
        }

        return new LoadedState(
            state,
            manifest,
            inspection.ActiveSnapshot,
            journalBytes,
            authorityKey,
            ownershipByPath.Values
                .Where(record => record.Path != settingsPath)
                .OrderBy(record => record.Path.CanonicalKey, StringComparer.Ordinal)
                .Select(record => new OutputReadDependency(record.Path, record.CurrentState))
                .ToImmutableArray(),
            ToDto(manifest, inspection.ActiveSnapshot));
    }

    private static bool HasSupportedMenuValues(GameplaySettingsSnapshot snapshot)
    {
        return !snapshot.Presence.HasFlag(GameplaySettingPresence.ExperienceRate)
            || snapshot.Values.ExperienceRateBasisPoints
                <= GameplaySettingsEditPlanner.MaximumExperienceRateBasisPoints
            && snapshot.Values.ExperienceRateBasisPoints
                % GameplaySettingsEditPlanner.ExperienceRateStepBasisPoints == 0;
    }

    private static OutputApplyPlan WithPackageReadDependencies(
        OutputApplyPlan plan,
        ImmutableArray<OutputReadDependency> packageReadDependencies)
    {
        return new OutputApplyPlan(
            plan.ProjectId,
            plan.GameFamily,
            plan.OutputMode,
            plan.SemanticReviewHash,
            plan.Origins,
            plan.Mutations,
            plan.ReadDependencies.Concat(packageReadDependencies),
            plan.DirectoryMembershipDependencies);
    }

    private static GameplaySettingsSnapshotDto ToDto(
        GameplayBundleManifest manifest,
        GameplaySettingsSnapshot snapshot)
    {
        return new GameplaySettingsSnapshotDto(
            snapshot.TitleId.ToString("X16", CultureInfo.InvariantCulture),
            manifest.BundleId,
            manifest.PackageVersion.ToString(),
            snapshot.Generation.ToString(CultureInfo.InvariantCulture),
            snapshot.Presence.HasFlag(GameplaySettingPresence.ExperienceShare),
            snapshot.Presence.HasFlag(GameplaySettingPresence.ExperienceRate),
            snapshot.Presence.HasFlag(GameplaySettingPresence.LevelCap),
            new GameplaySettingsValuesDto(
                snapshot.Values.ExperienceShareEnabled,
                snapshot.Values.ExperienceRateBasisPoints,
                snapshot.Values.LevelCapEnabled,
                snapshot.Values.LevelCap));
    }

    private static ulong ParseGeneration(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            || value != generation.ToString(CultureInfo.InvariantCulture))
        {
            throw new ArgumentException(
                "The expected gameplay settings generation is not canonical.",
                nameof(value));
        }

        return generation;
    }

    private static string ToAbsolutePath(string outputRoot, RelativeOutputPath path)
    {
        return Path.GetFullPath(Path.Combine(
            outputRoot,
            path.Value.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static async Task<byte[]> ReadExactBoundedAsync(
        string rootPath,
        string filePath,
        int? expectedLength,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        if (!HasSafePhysicalFileChain(rootPath, filePath))
        {
            throw new IOException("The gameplay settings package path is not a safe regular file.");
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumLength
            || expectedLength is { } exactLength && stream.Length != exactLength)
        {
            throw new IOException("The gameplay settings package file has an invalid size.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!HasSafePhysicalFileChain(rootPath, filePath))
        {
            throw new IOException("The gameplay settings package path changed during review.");
        }

        return bytes;
    }

    private static bool HasSafePhysicalFileChain(string rootPath, string filePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedFile = Path.GetFullPath(filePath);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedFile);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = normalizedRoot;
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            FileSystemInfo entry = index == segments.Length - 1
                ? new FileInfo(current)
                : new DirectoryInfo(current);
            entry.Refresh();
            if (!entry.Exists
                || !string.IsNullOrEmpty(entry.LinkTarget)
                || entry.Attributes.HasFlag(FileAttributes.Directory)
                    != (index < segments.Length - 1))
            {
                return false;
            }
        }

        return segments.Length > 0;
    }

    private static OutputFileState ComputeState(ReadOnlySpan<byte> bytes)
    {
        return OutputFileState.Existing(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes.Length);
    }

    private static bool IsUnavailableFileSystemException(Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException;
    }

    private static bool IsFatal(Exception exception)
    {
        return exception is
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException or
            AppDomainUnloadedException
            || exception is AggregateException aggregate
                && aggregate.InnerExceptions.Any(IsFatal)
            || exception.InnerException is not null
                && IsFatal(exception.InnerException);
    }

    private void PruneReviews(DateTimeOffset now)
    {
        foreach (var reviewId in reviews.Values
                     .Where(review => review.ExpiresAtUtc <= now)
                     .Select(review => review.ReviewId)
                     .ToArray())
        {
            reviews.Remove(reviewId);
        }
    }

    private sealed record CachedReview(
        string ReviewId,
        string ScopeKey,
        DateTimeOffset ExpiresAtUtc,
        GameplaySettingsBundleAuthorityKey AuthorityKey,
        ulong Generation,
        OutputApplyPlan ApplyPlan);

    private sealed record LoadedState(
        GameplaySettingsStateDto State,
        GameplayBundleManifest? Manifest,
        GameplaySettingsSnapshot? Snapshot,
        byte[]? JournalBytes,
        GameplaySettingsBundleAuthorityKey? AuthorityKey,
        ImmutableArray<OutputReadDependency> PackageReadDependencies,
        GameplaySettingsSnapshotDto? Dto)
    {
        public static LoadedState Unavailable(GameplaySettingsStateDto state)
        {
            return new LoadedState(
                state,
                null,
                null,
                null,
                null,
                ImmutableArray<OutputReadDependency>.Empty,
                null);
        }
    }
}

public sealed class GameplaySettingsUnavailableException : Exception
{
    public GameplaySettingsUnavailableException(GameplaySettingsStateDto state)
        : base("The gameplay settings package is not available for editing in its current state.")
    {
        State = state;
    }

    public GameplaySettingsStateDto State { get; }
}

public sealed class GameplaySettingsStateConflictException : Exception
{
    public GameplaySettingsStateConflictException()
        : base("The gameplay settings state changed after it was loaded.")
    {
    }
}

public sealed class GameplaySettingsReviewExpiredException : Exception
{
    public GameplaySettingsReviewExpiredException()
        : base("The reviewed gameplay settings update is no longer available.")
    {
    }
}
