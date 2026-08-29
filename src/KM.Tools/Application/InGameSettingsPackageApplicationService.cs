// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Output;
using KM.Api.RuntimeSettings;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;

namespace KM.Tools.Application;

public sealed record InGameSettingsStaticSettingsGuardResult(
    bool IsVanilla,
    bool OutputMainPresent,
    bool OutputMainMatchesBase,
    OutputFileState BaseMainState,
    OutputFileState OutputMainState);

public interface IInGameSettingsStaticSettingsGuard
{
    Task<InGameSettingsStaticSettingsGuardResult> InspectAsync(
        OutputScopeDto scope,
        CancellationToken cancellationToken = default);
}

public sealed record InGameSettingsBundleResolution(
    InGameSettingsBundleCatalog Catalog,
    GameplaySettingsBundleAuthority Authority,
    string? UnavailableDetail = null,
    IReadOnlyList<OutputReadDependency>? SourceDependencies = null,
    bool UsesComposedMain = false,
    bool UsesComposedMainNpdm = false,
    bool RequiresOwnedMainSource = false,
    bool RequiresOwnedMainNpdmSource = false,
    RelativeOutputPath? AttemptedSourcePath = null,
    bool SemanticallyVerifiedMainSource = false,
    IReadOnlyList<InGameSettingsExternalSourceDependency>? ExternalSourceDependencies = null);

public sealed record InGameSettingsExternalSourceDependency
{
    public InGameSettingsExternalSourceDependency(
        string absolutePath,
        long expectedLength,
        string? expectedSha256,
        long maximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath)
            || absolutePath.Length > 32_768)
        {
            throw new ArgumentException(
                "An external package source path must be a bounded absolute path.",
                nameof(absolutePath));
        }
        if (maximumBytes is < 1 or > OutputLimits.MaximumFingerprintFileBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
        if (expectedLength is < 1 || expectedLength > maximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLength));
        }
        if (expectedSha256 is not null
            && (expectedSha256.Length != SHA256.HashSizeInBytes * 2
                || expectedSha256.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException(
                "An external package source fingerprint must be SHA-256.",
                nameof(expectedSha256));
        }

        AbsolutePath = Path.GetFullPath(absolutePath);
        ExpectedLength = expectedLength;
        ExpectedSha256 = expectedSha256?.ToLowerInvariant();
        MaximumBytes = maximumBytes;
    }

    public string AbsolutePath { get; }

    public long ExpectedLength { get; }

    public string? ExpectedSha256 { get; }

    public long MaximumBytes { get; }
}

public interface IInGameSettingsBundleProvider
{
    Task<InGameSettingsBundleResolution> ResolveAsync(
        ProjectPaths paths,
        ProjectGame game,
        CancellationToken cancellationToken = default);

    Task<InGameSettingsBundleResolution> ResolveAsync(
        ProjectPaths paths,
        ProjectGame game,
        InGameSettingsInstallationTargetDto installationTarget,
        CancellationToken cancellationToken = default)
    {
        if (installationTarget != InGameSettingsInstallationTargetDto.Atmosphere)
        {
            throw new NotSupportedException(
                "This gameplay bundle provider supports only the default Atmosphere installation target.");
        }

        return ResolveAsync(paths, game, cancellationToken);
    }
}

/// <summary>
/// Reviews and applies an exact KM-generated in-game settings package through
/// the durable output transaction coordinator. Bridge callers can select an
/// operation, but can never supply package bytes or expand bundle authority.
/// </summary>
public sealed class InGameSettingsPackageApplicationService : IDisposable
{
    private const int MaximumTargetBytes = GameplayBundleArchive.MaximumEntryBytes;
    private const int MaximumComponentMismatchProbeBytes = 64 * 1024;
    private const string BundleOwnerId = "gameplay-bundle";
    private const string BundlePreservationRule = "whole-file-gameplay-bundle";
    private static readonly RelativeOutputPath StandaloneMainPath = new("exefs/main");
    private static readonly RelativeOutputPath StandaloneMainNpdmPath = new("exefs/main.npdm");
    private static readonly RelativeOutputPath StandaloneRuntimeSlotPath = new("exefs/subsdk9");
    private static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReviewSweepInterval = TimeSpan.FromMinutes(1);
    private static readonly StringComparer ExternalSourcePathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private const int MaximumExternalSourceDependencies = 64;
    private const long MaximumRetainedReviewBytes = OutputLimits.MaximumWriteBytesPerApply;

    private readonly object syncRoot = new();
    private readonly Dictionary<string, CachedReview> reviews = new(StringComparer.Ordinal);
    private readonly InGameSettingsBundleCatalog catalog;
    private readonly GameplaySettingsBundleAuthority authority;
    private readonly IInGameSettingsBundleProvider? bundleProvider;
    private readonly TimeProvider timeProvider;
    private readonly IInGameSettingsStaticSettingsGuard staticSettingsGuard;
    private readonly ITimer reviewPruneTimer;
    private long retainedReviewBytes;
    private bool disposed;

    public InGameSettingsPackageApplicationService(
        InGameSettingsBundleCatalog? catalog = null,
        GameplaySettingsBundleAuthority? authority = null,
        TimeProvider? timeProvider = null,
        IInGameSettingsStaticSettingsGuard? staticSettingsGuard = null)
    {
        this.catalog = catalog ?? InGameSettingsBundleCatalog.Empty;
        this.authority = authority ?? GameplaySettingsBundleAuthority.DenyAll;
        bundleProvider = null;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.staticSettingsGuard = staticSettingsGuard ?? new DefaultStaticSettingsGuard();
        reviewPruneTimer = CreateReviewPruneTimer();
    }

    public InGameSettingsPackageApplicationService(
        IInGameSettingsBundleProvider bundleProvider,
        TimeProvider? timeProvider = null,
        IInGameSettingsStaticSettingsGuard? staticSettingsGuard = null)
    {
        this.bundleProvider = bundleProvider
            ?? throw new ArgumentNullException(nameof(bundleProvider));
        catalog = InGameSettingsBundleCatalog.Empty;
        authority = GameplaySettingsBundleAuthority.DenyAll;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.staticSettingsGuard = staticSettingsGuard ?? new DefaultStaticSettingsGuard();
        reviewPruneTimer = CreateReviewPruneTimer();
    }

    public async Task<InspectInGameSettingsPackageResponse> InspectAsync(
        InspectInGameSettingsPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ValidateInstallationTarget(request.InstallationTarget);
        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        var loaded = await LoadWithCoexistenceAsync(
                request.Scope,
                context,
                request.InstallationTarget,
                cancellationToken)
            .ConfigureAwait(false);
        return new InspectInGameSettingsPackageResponse(loaded.Snapshot);
    }

    public async Task<PreviewInGameSettingsPackageResponse> PreviewAsync(
        PreviewInGameSettingsPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ValidateInstallationTarget(request.InstallationTarget);
        if (!Enum.IsDefined(request.Operation))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Operation));
        }

        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        var loaded = await LoadWithCoexistenceAsync(
                request.Scope,
                context,
                request.InstallationTarget,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                loaded.Snapshot.Revision,
                request.ExpectedRevision,
                StringComparison.Ordinal))
        {
            throw new InGameSettingsPackageStateConflictException();
        }

        var staticDependency = bundleProvider is null
            && request.Operation is (
                InGameSettingsPackageOperationDto.Install or
                InGameSettingsPackageOperationDto.Upgrade)
            ? await GetStaticSettingsDependencyAsync(request.Scope, cancellationToken)
                .ConfigureAwait(false)
            : null;

        var plan = request.Operation switch
        {
            InGameSettingsPackageOperationDto.Install => CreateInstallPlan(context, loaded),
            InGameSettingsPackageOperationDto.Upgrade => CreateUpgradePlan(context, loaded),
            InGameSettingsPackageOperationDto.Remove => CreateRemovalPlan(context, loaded),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Operation)),
        };
        if (staticDependency is not null)
        {
            plan = BindReadDependency(plan, staticDependency);
        }
        if (request.Operation is InGameSettingsPackageOperationDto.Install
            or InGameSettingsPackageOperationDto.Upgrade)
        {
            foreach (var dependency in loaded.SourceDependencies)
            {
                plan = BindReadDependency(plan, dependency);
            }
        }
        var expiresAtUtc = timeProvider.GetUtcNow().Add(ReviewLifetime);
        var reviewId = Guid.NewGuid().ToString("N");
        var retainedBytes = CalculateRetainedReviewBytes(plan);
        if (retainedBytes > MaximumRetainedReviewBytes)
        {
            throw new InGameSettingsPackageUnavailableException(
                "The reviewed native-menu package exceeds the bounded in-memory review budget.");
        }
        var externalSourceDependencies = request.Operation is
            InGameSettingsPackageOperationDto.Install or
            InGameSettingsPackageOperationDto.Upgrade
                ? loaded.ExternalSourceDependencies
                : ImmutableArray<InGameSettingsExternalSourceDependency>.Empty;
        lock (syncRoot)
        {
            ThrowIfDisposedLocked();
            PruneReviewsLocked(timeProvider.GetUtcNow());
            while (reviews.Count >= InGameSettingsPackageContract.MaximumCachedReviews
                   || retainedReviewBytes > MaximumRetainedReviewBytes - retainedBytes)
            {
                var oldest = reviews.Values
                    .OrderBy(review => review.ExpiresAtUtc)
                    .ThenBy(review => review.ReviewId, StringComparer.Ordinal)
                    .First();
                RemoveReviewLocked(oldest.ReviewId, out _);
            }

            var review = new CachedReview(
                reviewId,
                context.ScopeKey,
                loaded.Snapshot.Revision,
                request.Operation,
                request.InstallationTarget,
                expiresAtUtc,
                plan,
                externalSourceDependencies,
                retainedBytes);
            reviews.Add(reviewId, review);
            retainedReviewBytes = checked(retainedReviewBytes + retainedBytes);
        }

        var targets = plan.Mutations
            .OrderBy(mutation => mutation.Path.CanonicalKey, StringComparer.Ordinal)
            .Take(InGameSettingsPackageContract.MaximumReturnedTargets)
            .Select(mutation => new InGameSettingsPackageTargetDto(
                mutation.Path.Value,
                mutation.Kind switch
                {
                    OutputMutationKind.Write => InGameSettingsPackageTargetOperationDto.Write,
                    OutputMutationKind.Delete => InGameSettingsPackageTargetOperationDto.Delete,
                    _ => throw new ArgumentOutOfRangeException(nameof(mutation.Kind)),
                }))
            .ToArray();
        var readDependencies = plan.ReadDependencies
            .OrderBy(dependency => dependency.Path.CanonicalKey, StringComparer.Ordinal)
            .Take(InGameSettingsPackageContract.MaximumReturnedReadDependencies)
            .Select(dependency => new InGameSettingsPackageReadDependencyDto(
                dependency.Path.Value,
                IsExecutableCompositionSource(dependency)
                    ? InGameSettingsPackageReadDependencyRoleDto.ExecutableCompositionSource
                    : InGameSettingsPackageReadDependencyRoleDto.StaticExecutableGuard,
                dependency.ExpectedState.Exists,
                dependency.ExpectedState.Sha256,
                dependency.ExpectedState.Exists
                    ? dependency.ExpectedState.LengthBytes
                    : null,
                Preserved: true))
            .ToArray();
        var composition = request.Operation is (
                InGameSettingsPackageOperationDto.Install or
                InGameSettingsPackageOperationDto.Upgrade)
            ? CreateCompositionDto(context, loaded, request.InstallationTarget)
            : null;
        return new PreviewInGameSettingsPackageResponse(
            reviewId,
            expiresAtUtc,
            request.Operation,
            loaded.Snapshot,
            targets,
            targets.Length != plan.Mutations.Length,
            readDependencies,
            readDependencies.Length != plan.ReadDependencies.Length,
            composition);

        bool IsExecutableCompositionSource(OutputReadDependency dependency)
        {
            return bundleProvider is not null
                && dependency.ExpectedState.Exists
                && (loaded.UsesComposedMain
                    && dependency.Path.CanonicalKey == StandaloneMainPath.CanonicalKey
                    || loaded.UsesComposedMainNpdm
                    && dependency.Path.CanonicalKey == StandaloneMainNpdmPath.CanonicalKey);
        }
    }

    public async Task<ApplyInGameSettingsPackageResponse> ApplyAsync(
        ApplyInGameSettingsPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ValidateInstallationTarget(request.InstallationTarget);
        if (string.IsNullOrWhiteSpace(request.ReviewId)
            || request.ReviewId.Length > InGameSettingsPackageContract.MaximumReviewIdLength
            || request.ReviewId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InGameSettingsPackageReviewExpiredException();
        }

        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        CachedReview review;
        lock (syncRoot)
        {
            ThrowIfDisposedLocked();
            PruneReviewsLocked(timeProvider.GetUtcNow());
            if (!RemoveReviewLocked(request.ReviewId, out review!)
                || review.ScopeKey != context.ScopeKey
                || review.InstallationTarget != request.InstallationTarget
                || review.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                throw new InGameSettingsPackageReviewExpiredException();
            }
        }

        using var externalSourceLeases = review.ExternalSourceDependencies.IsDefaultOrEmpty
            ? ExternalSourceLeaseSet.Empty
            : await AcquireExternalSourceLeasesAsync(
                    review.ExternalSourceDependencies,
                    cancellationToken)
                .ConfigureAwait(false);

        if (bundleProvider is null
            && review.Operation is (
                InGameSettingsPackageOperationDto.Install or
                InGameSettingsPackageOperationDto.Upgrade))
        {
            var currentStaticDependency = await GetStaticSettingsDependencyAsync(
                    request.Scope,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!review.ApplyPlan.ReadDependencies.Contains(currentStaticDependency))
            {
                throw new InGameSettingsPackageStateConflictException();
            }
        }

        var current = await LoadWithCoexistenceAsync(
                request.Scope,
                context,
                request.InstallationTarget,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                current.Snapshot.Revision,
                review.ExpectedRevision,
                StringComparison.Ordinal)
            || review.Operation is (
                    InGameSettingsPackageOperationDto.Install or
                    InGameSettingsPackageOperationDto.Upgrade)
                && !HaveSameExternalSourceDependencies(
                    current.ExternalSourceDependencies,
                    review.ExternalSourceDependencies))
        {
            throw new InGameSettingsPackageStateConflictException();
        }

        var result = await context.Coordinator
            .ApplyAsync(review.ApplyPlan, cancellationToken)
            .ConfigureAwait(false);
        InGameSettingsPackageSnapshotDto? snapshot = null;
        if (result.Outcome is OutputApplyOutcome.Committed or OutputApplyOutcome.RolledBack)
        {
            try
            {
                snapshot = (await LoadWithCoexistenceAsync(
                        request.Scope,
                        context,
                        request.InstallationTarget,
                        CancellationToken.None)
                    .ConfigureAwait(false)).Snapshot;
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                // The durable receipt remains authoritative when a display refresh fails.
                snapshot = null;
            }
        }

        return new ApplyInGameSettingsPackageResponse(
            result.TransactionId.Value,
            result.Outcome switch
            {
                OutputApplyOutcome.Committed => InGameSettingsPackageApplyOutcomeDto.Committed,
                OutputApplyOutcome.RolledBack => InGameSettingsPackageApplyOutcomeDto.RolledBack,
                OutputApplyOutcome.RecoveryRequired => InGameSettingsPackageApplyOutcomeDto.RecoveryRequired,
                _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome)),
            },
            snapshot);
    }

    private static OutputApplyPlan CreateInstallPlan(
        OutputScopeContext context,
        LoadedPackageState loaded)
    {
        if (loaded.Snapshot.State != InGameSettingsPackageStateDto.NotInstalled
            || loaded.AvailableEntry is null)
        {
            throw new InGameSettingsPackageUnavailableException(
                "An exact KM-generated package is not ready for initial installation.");
        }

        var bundle = ReadEntry(loaded.AvailableEntry);
        var reviewedTargets = bundle.Entries.Select(path =>
        {
            var review = loaded.Targets[new RelativeOutputPath(path).CanonicalKey];
            return new OutputBaselineEntry(review.Path, review.State);
        });
        var installation = GameplayBundleDeploymentPlanner.CreateInitialInstall(
            loaded.AvailableEntry.ArchiveBytes,
            context.ProjectId,
            context.GameFamily,
            reviewedTargets);
        return BindInventoryRevision(installation.ApplyPlan, loaded.Ownership.Revision);
    }

    private async Task<OutputReadDependency> GetStaticSettingsDependencyAsync(
        OutputScopeDto scope,
        CancellationToken cancellationToken)
    {
        var inspection = await staticSettingsGuard
            .InspectAsync(scope, cancellationToken)
            .ConfigureAwait(false);
        if (!inspection.IsVanilla)
        {
            throw new InGameSettingsPackageUnavailableException(
                "Set static Gameplay Settings to vanilla before installing or upgrading the in-game settings package.");
        }
        if (inspection.OutputMainPresent
            && !inspection.OutputMainMatchesBase)
        {
            throw new InGameSettingsPackageUnavailableException(
                "Remove the standalone exefs/main output before installing or upgrading the native in-game menu. Two executable replacements cannot be composed safely, and KM will not discard either one.");
        }

        return new OutputReadDependency(
            new RelativeOutputPath("exefs/main"),
            inspection.OutputMainState);
    }

    private sealed class DefaultStaticSettingsGuard : IInGameSettingsStaticSettingsGuard
    {
        public async Task<InGameSettingsStaticSettingsGuardResult> InspectAsync(
            OutputScopeDto scope,
            CancellationToken cancellationToken = default)
        {
            var context = OutputSafetyApplicationService.ResolveScope(scope);
            var staticState = await GameplaySettingsApplicationService
                .InspectStaticValuesForInGamePackageAsync(context, cancellationToken)
                .ConfigureAwait(false);
            var baseMainState = staticState.BaseState ?? OutputFileState.Missing;
            var outputMainState = staticState.OutputPresent
                ? staticState.TargetState ?? OutputFileState.Missing
                : OutputFileState.Missing;
            var outputMainMatchesBase = staticState.OutputPresent
                && staticState.OutputMatchesBase
                && baseMainState.Exists
                && outputMainState == baseMainState;
            var isVanilla = outputMainMatchesBase
                || staticState.State == GameplaySettingsStateDto.Ready
                && staticState.Values is
                {
                    ExperienceShareEnabled: true,
                    ExperienceRateBasisPoints: 10_000,
                    LevelCapEnabled: false,
                    LevelCap: 100,
                };
            return new InGameSettingsStaticSettingsGuardResult(
                isVanilla,
                staticState.OutputPresent,
                outputMainMatchesBase,
                baseMainState,
                outputMainState);
        }
    }

    private static OutputApplyPlan CreateUpgradePlan(
        OutputScopeContext context,
        LoadedPackageState loaded)
    {
        if (loaded.Snapshot.State != InGameSettingsPackageStateDto.UpgradeAvailable
            || loaded.InstalledEntry is null
            || loaded.AvailableEntry is null)
        {
            throw new InGameSettingsPackageUnavailableException(
                "An exact owned package upgrade is not available.");
        }

        var previous = ReadEntry(loaded.InstalledEntry);
        var next = ReadEntry(loaded.AvailableEntry);
        var expectedPaths = previous.Entries
            .Concat(next.Entries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new RelativeOutputPath(path));
        var targetReviews = expectedPaths.Select(path =>
        {
            var review = loaded.Targets[path.CanonicalKey];
            return review.Exists
                ? GameplayBundleUpgradeTargetReview.Existing(path, review.Bytes.AsSpan())
                : GameplayBundleUpgradeTargetReview.Missing(path);
        });
        var upgrade = GameplayBundleUpgradePlanner.CreateUpgrade(
            loaded.InstalledEntry.ArchiveBytes,
            loaded.AvailableEntry.ArchiveBytes,
            context.ProjectId,
            context.GameFamily,
            targetReviews);
        return BindInventoryRevision(upgrade.ApplyPlan, loaded.Ownership.Revision);
    }

    private static OutputApplyPlan CreateRemovalPlan(
        OutputScopeContext context,
        LoadedPackageState loaded)
    {
        if (loaded.Snapshot.State is not (
                InGameSettingsPackageStateDto.Installed or
                InGameSettingsPackageStateDto.UpgradeAvailable or
                InGameSettingsPackageStateDto.CoexistenceConflict)
            || loaded.InstalledEntry is null)
        {
            throw new InGameSettingsPackageUnavailableException(
                "An exact owned package is not ready for removal.");
        }

        var installed = ReadEntry(loaded.InstalledEntry);
        var targetReviews = installed.Entries.Select(path =>
        {
            var outputPath = new RelativeOutputPath(path);
            var review = loaded.Targets[outputPath.CanonicalKey];
            return review.Exists
                ? GameplayBundleRemovalTargetReview.Existing(outputPath, review.Bytes.AsSpan())
                : GameplayBundleRemovalTargetReview.Missing(outputPath);
        });
        return GameplayBundleRemovalPlanner.CreateRemoval(
                loaded.InstalledEntry.ArchiveBytes,
                context.ProjectId,
                context.GameFamily,
                targetReviews,
                loaded.Ownership,
                GameplayBundleSettingsRemoval.Remove)
            .ApplyPlan;
    }

    private async Task<LoadedPackageState> LoadWithCoexistenceAsync(
        OutputScopeDto scope,
        OutputScopeContext context,
        InGameSettingsInstallationTargetDto installationTarget,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(context, installationTarget, cancellationToken)
            .ConfigureAwait(false);
        if (bundleProvider is not null)
        {
            // Production native-menu bundles are derived from a reviewed
            // standalone executable source. The provider proves compatibility,
            // and the source fingerprints are bound to preview/apply below.
            return loaded;
        }
        if (loaded.Snapshot.State == InGameSettingsPackageStateDto.NotInstalled)
        {
            var initialInspection = await staticSettingsGuard
                .InspectAsync(scope, cancellationToken)
                .ConfigureAwait(false);
            if (!initialInspection.OutputMainPresent)
            {
                return loaded;
            }

            if (initialInspection.OutputMainMatchesBase)
            {
                return loaded with
                {
                    Snapshot = loaded.Snapshot with
                    {
                        Revision = ComputeCoexistenceRevision(
                            loaded.Snapshot.Revision,
                            initialInspection),
                        Detail = "A redundant exefs/main output exactly matches the selected Base executable and will be left unchanged.",
                    },
                };
            }

            return loaded with
            {
                Snapshot = loaded.Snapshot with
                {
                    State = InGameSettingsPackageStateDto.Conflict,
                    Revision = ComputeCoexistenceRevision(
                        loaded.Snapshot.Revision,
                        initialInspection),
                    Detail = "A separate exefs/main output already exists. KM will not install a second executable replacement or discard the existing one.",
                },
            };
        }
        if (loaded.Snapshot.State is not (
                InGameSettingsPackageStateDto.Installed or
                InGameSettingsPackageStateDto.UpgradeAvailable))
        {
            return loaded;
        }

        var inspection = await staticSettingsGuard
            .InspectAsync(scope, cancellationToken)
            .ConfigureAwait(false);
        var canCoexist = inspection.IsVanilla
            && (!inspection.OutputMainPresent
                || inspection.OutputMainMatchesBase);
        var snapshot = loaded.Snapshot with
        {
            State = canCoexist
                ? loaded.Snapshot.State
                : InGameSettingsPackageStateDto.CoexistenceConflict,
            Revision = ComputeCoexistenceRevision(
                loaded.Snapshot.Revision,
                inspection),
            Detail = canCoexist
                ? loaded.Snapshot.Detail
                : inspection.OutputMainPresent
                    ? "The KM-managed package files are intact, but a separate exefs/main output also exists. KM will not choose between two executable replacements. Package removal remains available."
                    : "The KM-managed package files are intact, but the executable or selected base build no longer matches the vanilla state reviewed for safe coexistence. Package removal remains available.",
        };
        return loaded with { Snapshot = snapshot };
    }

    private async Task<LoadedPackageState> LoadAsync(
        OutputScopeContext context,
        InGameSettingsInstallationTargetDto installationTarget,
        CancellationToken cancellationToken)
    {
        var selectedGame = context.Paths.SelectedGame
            ?? throw new OutputScopeMismatchException();
        var titleId = ProjectGameMetadata.Get(selectedGame).TitleId;
        var resolution = bundleProvider is null
            ? new InGameSettingsBundleResolution(catalog, authority)
            : await bundleProvider.ResolveAsync(
                    context.Paths,
                    selectedGame,
                    installationTarget,
                    cancellationToken)
                .ConfigureAwait(false);
        var authorizedEntries = resolution.Catalog
            .GetEntries(context.GameFamily, titleId)
            .Where(entry => resolution.Authority.IsAuthorized(entry.AuthorityKey))
            .Where(entry => bundleProvider is null
                || NativeGameplayMenuBundleFactory.IsNativeMenuManifestForTarget(
                    selectedGame,
                    entry.Manifest,
                    installationTarget))
            .ToImmutableArray();
        var sourceDependencies = (resolution.SourceDependencies ?? [])
            .OrderBy(dependency => dependency.Path.CanonicalKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (sourceDependencies.Length > 8
            || sourceDependencies.Select(dependency => dependency.Path.CanonicalKey)
                .Distinct(StringComparer.Ordinal).Count() != sourceDependencies.Length)
        {
            throw new InvalidDataException(
                "The native-menu executable source dependency inventory is invalid.");
        }
        var externalSourceDependencies = (resolution.ExternalSourceDependencies ?? [])
            .OrderBy(
                dependency => dependency.AbsolutePath,
                ExternalSourcePathComparer)
            .ToImmutableArray();
        if (externalSourceDependencies.Length > MaximumExternalSourceDependencies
            || externalSourceDependencies.Select(dependency => dependency.AbsolutePath)
                .Distinct(ExternalSourcePathComparer).Count()
                != externalSourceDependencies.Length)
        {
            throw new InvalidDataException(
                "The native-menu Base source dependency inventory is invalid.");
        }
        var ownership = await context.Coordinator
            .GetOwnershipInventorySnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        var providerOfferedCompatiblePackage = !authorizedEntries.IsDefaultOrEmpty;
        var sourceOwnershipFailure = bundleProvider is not null
            && providerOfferedCompatiblePackage
            ? ValidateCompositionSourceOwnership(
                context,
                ownership,
                resolution,
                sourceDependencies)
            : null;
        var unavailableDetail = resolution.UnavailableDetail;
        if (sourceOwnershipFailure is not null)
        {
            authorizedEntries = ImmutableArray<InGameSettingsBundleCatalogEntry>.Empty;
            unavailableDetail = sourceOwnershipFailure.Detail;
        }
        var executableInput = CreateExecutableInputAssessment(
            resolution,
            sourceDependencies,
            providerOfferedCompatiblePackage,
            sourceOwnershipFailure,
            bundleProvider is not null);

        var baseBuildId = await TryReadBaseBuildIdAsync(
                context.Paths.BaseExeFsPath,
                cancellationToken)
            .ConfigureAwait(false);
        var availableEntry = authorizedEntries.SingleOrDefault(entry =>
            entry.IsCurrent
            && string.Equals(
                entry.Manifest.BuildId,
                baseBuildId,
                StringComparison.Ordinal));
        var availableBundle = availableEntry is null
            ? null
            : ReadEntry(availableEntry);
        var manifestPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{titleId:X16}/bundle.manifest");
        var manifestReview = await ReviewTargetAsync(
                context.Paths.OutputRootPath!,
                manifestPath,
                GameplayBundleIdentity.MaximumSerializationBytes,
                cancellationToken)
            .ConfigureAwait(false);

        GameplayBundleManifest? installedManifest = null;
        var manifestIsCanonical = manifestReview.Exists
            && TryParseManifest(manifestReview.Bytes.AsSpan(), out installedManifest);
        var manifestIsNative = manifestIsCanonical
            && installedManifest is not null
            && NativeGameplayMenuBundleFactory.IsNativeMenuManifest(
                selectedGame,
                installedManifest);
        var manifestIsRetiredExternalControl = manifestIsCanonical
            && installedManifest is not null
            && NativeGameplayMenuBundleFactory.IsRetiredExternalControlManifest(
                selectedGame,
                installedManifest);
        var manifestIsManagedGameplay = manifestIsNative
            || manifestIsRetiredExternalControl;

        var pathInventory = new Dictionary<string, RelativeOutputPath>(StringComparer.Ordinal)
        {
            [manifestPath.CanonicalKey] = manifestPath,
        };
        var reviewLimits = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [manifestPath.CanonicalKey] = GameplayBundleIdentity.MaximumSerializationBytes,
        };
        if (availableBundle is not null)
        {
            foreach (var component in availableBundle.Manifest.Components)
            {
                foreach (var equivalentPath in NativeGameplayMenuBundleFactory
                             .GetEquivalentRuntimePackagePaths(component.Path, titleId))
                {
                    AddReviewTarget(
                        pathInventory,
                        reviewLimits,
                        equivalentPath,
                        GetComponentReviewLimit(component));
                }
            }
            AddReviewTarget(
                pathInventory,
                reviewLimits,
                $"config/km-editor/gameplay-settings/{titleId:X16}/settings.bin",
                GameplaySettingsJournal.JournalSize);
        }
        if (manifestIsManagedGameplay)
        {
            foreach (var component in installedManifest!.Components)
            {
                foreach (var equivalentPath in NativeGameplayMenuBundleFactory
                             .GetEquivalentRuntimePackagePaths(component.Path, titleId))
                {
                    AddReviewTarget(
                        pathInventory,
                        reviewLimits,
                        equivalentPath,
                        GetComponentReviewLimit(component));
                }
            }
            AddReviewTarget(
                pathInventory,
                reviewLimits,
                $"config/km-editor/gameplay-settings/{titleId:X16}/settings.bin",
                GameplaySettingsJournal.JournalSize);
        }

        var targets = new Dictionary<string, ReviewedTarget>(StringComparer.Ordinal)
        {
            [manifestPath.CanonicalKey] = manifestReview,
        };
        foreach (var path in pathInventory.Values
                     .Where(path => path.CanonicalKey != manifestPath.CanonicalKey)
                     .OrderBy(path => path.CanonicalKey, StringComparer.Ordinal))
        {
            targets.Add(
                path.CanonicalKey,
                await ReviewTargetAsync(
                        context.Paths.OutputRootPath!,
                        path,
                        reviewLimits[path.CanonicalKey],
                        cancellationToken)
                    .ConfigureAwait(false));
        }

        InGameSettingsBundleCatalogEntry? installedEntry = null;
        GameplayBundleArchiveReadResult? installedBundle = null;
        if (manifestIsManagedGameplay
            && TryReconstructInstalledEntry(
                context.GameFamily,
                installedManifest!,
                targets,
                manifestIsRetiredExternalControl,
                out installedEntry))
        {
            installedBundle = ReadEntry(installedEntry);
        }

        var revision = ComputeRevision(
            context,
            installationTarget,
            ownership,
            availableEntry,
            installedEntry,
            targets.Values,
            sourceDependencies,
            externalSourceDependencies);
        var availableDto = availableEntry is null ? null : ToDto(availableEntry);
        var blocksStaticEditor = targets.Values.Any(target =>
            target.Exists
            && IsRuntimePackageComponentPath(target.Path, titleId));
        if (!manifestReview.Exists)
        {
            var collision = targets.Values.Any(target => target.Exists);
            var state = collision
                ? InGameSettingsPackageStateDto.Conflict
                : availableEntry is null
                    ? InGameSettingsPackageStateDto.Unavailable
                    : InGameSettingsPackageStateDto.NotInstalled;
            return new LoadedPackageState(
                new InGameSettingsPackageSnapshotDto(
                    state,
                    BlocksStaticEditor: blocksStaticEditor,
                    revision,
                    PackageAvailable: availableEntry is not null,
                    InstalledPackage: null,
                    AvailablePackage: availableDto,
                    ExecutableInput: executableInput,
                    Detail: collision
                        ? "One or more exact native package targets already exist without the KM package manifest."
                        : state == InGameSettingsPackageStateDto.Unavailable
                            ? unavailableDetail
                              ?? "No current native package is available for this exact title and build."
                            : null),
                null,
                availableEntry,
                ownership,
                targets.ToImmutableDictionary(StringComparer.Ordinal),
                sourceDependencies,
                externalSourceDependencies,
                resolution.UsesComposedMain,
                resolution.UsesComposedMainNpdm);
        }

        if (installedEntry is null || installedBundle is null)
        {
            var state = !manifestIsCanonical
                ? InGameSettingsPackageStateDto.Corrupt
                : !manifestIsManagedGameplay
                    ? InGameSettingsPackageStateDto.Unmanaged
                    : ClassifyUnreconstructableInstalledPackage(
                        context.GameFamily,
                        installedManifest!,
                        targets,
                        titleId,
                        manifestIsRetiredExternalControl);
            return new LoadedPackageState(
                new InGameSettingsPackageSnapshotDto(
                    state,
                    BlocksStaticEditor: blocksStaticEditor,
                    revision,
                    PackageAvailable: availableEntry is not null,
                    InstalledPackage: null,
                    AvailablePackage: availableDto,
                    ExecutableInput: executableInput,
                    Detail: state switch
                    {
                        InGameSettingsPackageStateDto.Unmanaged =>
                            "The existing package manifest is not a KM native-menu package for this exact title and build.",
                        InGameSettingsPackageStateDto.Incomplete =>
                            "The installed native-menu package is missing one or more exact owned files.",
                        InGameSettingsPackageStateDto.Conflict =>
                            "An installed native-menu package file no longer matches its reviewed manifest.",
                        _ => "The existing package manifest or settings journal is malformed or noncanonical.",
                    }),
                null,
                availableEntry,
                ownership,
                targets.ToImmutableDictionary(StringComparer.Ordinal),
                sourceDependencies,
                externalSourceDependencies,
                resolution.UsesComposedMain,
                resolution.UsesComposedMainNpdm);
        }

        var validation = ValidateInstalledPackage(
            context,
            installedBundle,
            targets,
            ownership);
        if (validation.State is { } invalidState)
        {
            return new LoadedPackageState(
                new InGameSettingsPackageSnapshotDto(
                    invalidState,
                    BlocksStaticEditor: blocksStaticEditor,
                    revision,
                    PackageAvailable: availableEntry is not null,
                    InstalledPackage: ToDto(installedEntry),
                    AvailablePackage: availableDto,
                    ExecutableInput: executableInput,
                    Detail: validation.Detail),
                installedEntry,
                availableEntry,
                ownership,
                targets.ToImmutableDictionary(StringComparer.Ordinal),
                sourceDependencies,
                externalSourceDependencies,
                resolution.UsesComposedMain,
                resolution.UsesComposedMainNpdm);
        }

        var upgradeAvailable = availableEntry is not null
            && !string.Equals(
                availableEntry.Manifest.BundleId,
                installedEntry.Manifest.BundleId,
                StringComparison.Ordinal);
        return new LoadedPackageState(
            new InGameSettingsPackageSnapshotDto(
                upgradeAvailable
                    ? InGameSettingsPackageStateDto.UpgradeAvailable
                    : InGameSettingsPackageStateDto.Installed,
                BlocksStaticEditor: true,
                revision,
                PackageAvailable: availableEntry is not null,
                InstalledPackage: ToDto(installedEntry),
                AvailablePackage: availableDto,
                ExecutableInput: executableInput),
            installedEntry,
            availableEntry,
            ownership,
            targets.ToImmutableDictionary(StringComparer.Ordinal),
            sourceDependencies,
            externalSourceDependencies,
            resolution.UsesComposedMain,
            resolution.UsesComposedMainNpdm);
    }

    private static bool TryReconstructInstalledEntry(
        GameFamily gameFamily,
        GameplayBundleManifest manifest,
        IReadOnlyDictionary<string, ReviewedTarget> targets,
        bool allowRetiredToggleSelections,
        out InGameSettingsBundleCatalogEntry entry)
    {
        try
        {
            var components = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var expected in manifest.Components)
            {
                var path = new RelativeOutputPath(expected.Path);
                if (!targets.TryGetValue(path.CanonicalKey, out var target)
                    || !target.Exists)
                {
                    entry = null!;
                    return false;
                }
                var bytes = target.Bytes.ToArray();
                if (allowRetiredToggleSelections
                    && IsToggleSelectionPath(expected.Path, manifest.TitleId))
                {
                    bytes = AtmosphereCheatToggleDocument.Create(
                        AtmosphereCheatToggleDocument.Parse(bytes)
                            .Select(item => new KeyValuePair<string, bool>(
                                item.Key,
                                false)));
                }
                components.Add(expected.Path, bytes);
            }

            GameplayBundleIdentity.VerifyComponents(manifest, components);
            var family = GameplayBundleDeploymentPlanner.ToSettingsFamily(gameFamily);
            var bootstrap = GameplaySettingsJournal.CreateBootstrap(
                family,
                manifest.TitleId,
                new GameplaySettingsWriterVersion(
                    checked((ushort)manifest.PackageVersion.Major),
                    checked((ushort)manifest.PackageVersion.Minor),
                    checked((ushort)manifest.PackageVersion.Patch)),
                GameplaySettingPresence.ExperienceShare
                | GameplaySettingPresence.ExperienceRate
                | GameplaySettingPresence.LevelCap);
            var archive = GameplayBundleArchive.Build(
                manifest,
                components,
                family,
                bootstrap);
            entry = new InGameSettingsBundleCatalogEntry(
                gameFamily,
                archive.Bytes,
                isCurrent: false);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            entry = null!;
            return false;
        }
    }

    private static InGameSettingsPackageStateDto ClassifyUnreconstructableInstalledPackage(
        GameFamily gameFamily,
        GameplayBundleManifest manifest,
        IReadOnlyDictionary<string, ReviewedTarget> targets,
        ulong titleId,
        bool allowRetiredToggleSelections)
    {
        foreach (var expected in manifest.Components)
        {
            var path = new RelativeOutputPath(expected.Path);
            if (!targets.TryGetValue(path.CanonicalKey, out var target)
                || !target.Exists)
            {
                return InGameSettingsPackageStateDto.Incomplete;
            }
            if (allowRetiredToggleSelections
                && IsToggleSelectionPath(expected.Path, titleId))
            {
                try
                {
                    var canonical = AtmosphereCheatToggleDocument.Create(
                        AtmosphereCheatToggleDocument.Parse(target.Bytes.AsSpan())
                            .Select(item => new KeyValuePair<string, bool>(
                                item.Key,
                                false)));
                    if ((ulong)canonical.LongLength == expected.Length
                        && string.Equals(
                            Convert.ToHexString(SHA256.HashData(canonical)),
                            expected.Sha256,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (exception is
                    ArgumentException or
                    InvalidDataException or
                    OverflowException)
                {
                    // Classify the target as a conflict below.
                }
            }
            if ((ulong)target.Bytes.Length != expected.Length
                || !string.Equals(
                    Convert.ToHexString(SHA256.HashData(target.Bytes.AsSpan())),
                    expected.Sha256,
                    StringComparison.Ordinal))
            {
                return InGameSettingsPackageStateDto.Conflict;
            }
        }

        var settingsPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{titleId:X16}/settings.bin");
        if (!targets.TryGetValue(settingsPath.CanonicalKey, out var settings)
            || !settings.Exists)
        {
            return InGameSettingsPackageStateDto.Incomplete;
        }

        try
        {
            var inspection = GameplaySettingsJournal.Inspect(
                settings.Bytes.AsSpan().ToArray(),
                GameplayBundleDeploymentPlanner.ToSettingsFamily(gameFamily),
                titleId);
            return inspection.WritesAllowed && inspection.ActiveSnapshot is not null
                ? InGameSettingsPackageStateDto.Conflict
                : InGameSettingsPackageStateDto.Corrupt;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            return InGameSettingsPackageStateDto.Corrupt;
        }
    }

    private static bool IsToggleSelectionPath(string path, ulong titleId) =>
        string.Equals(
            path,
            $"atmosphere/contents/{titleId:X16}/cheats/toggles.txt",
            StringComparison.Ordinal);

    private static bool IsRuntimePackageComponentPath(
        RelativeOutputPath path,
        ulong titleId)
    {
        return NativeGameplayMenuBundleFactory.IsRuntimePackageComponentPath(
                path.Value,
                titleId)
            || path.Value.StartsWith(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"atmosphere/contents/{titleId:X16}/cheats/"),
                StringComparison.Ordinal);
    }

    private static int GetComponentReviewLimit(
        GameplayBundleOutputComponent component)
    {
        if (component.Length > MaximumTargetBytes)
        {
            throw new InvalidDataException(
                "A gameplay package component exceeds its bounded target size.");
        }

        return component.Path.EndsWith("/cheats/toggles.txt", StringComparison.Ordinal)
            ? AtmosphereCheatToggleDocument.MaximumDocumentBytes
            : checked((int)Math.Min(
                MaximumTargetBytes,
                component.Length + MaximumComponentMismatchProbeBytes));
    }

    private static void AddReviewTarget(
        IDictionary<string, RelativeOutputPath> paths,
        IDictionary<string, int> limits,
        string value,
        int maximumBytes)
    {
        if (maximumBytes is < 0 or > MaximumTargetBytes)
        {
            throw new InvalidDataException(
                "A gameplay package review limit is out of bounds.");
        }

        var path = new RelativeOutputPath(value);
        if (paths.TryGetValue(path.CanonicalKey, out var existing)
            && !string.Equals(existing.Value, path.Value, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A gameplay package contains case-colliding target paths.");
        }

        paths.TryAdd(path.CanonicalKey, path);
        if (limits.TryGetValue(path.CanonicalKey, out var existingLimit))
        {
            limits[path.CanonicalKey] = Math.Max(existingLimit, maximumBytes);
        }
        else
        {
            limits.Add(path.CanonicalKey, maximumBytes);
        }
    }

    private static PackageValidationFailure ValidateInstalledPackage(
        OutputScopeContext context,
        GameplayBundleArchiveReadResult bundle,
        IReadOnlyDictionary<string, ReviewedTarget> targets,
        OutputOwnershipInventorySnapshot ownership)
    {
        var settingsPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/settings.bin");
        var manifestPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/bundle.manifest");
        var expected = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (var component in bundle.ImmutableComponents)
        {
            expected.Add(new RelativeOutputPath(component.Key).CanonicalKey, component.Value);
        }

        expected.Add(manifestPath.CanonicalKey, bundle.ManifestBytes);
        foreach (var (path, bytes) in expected)
        {
            if (!targets.TryGetValue(path, out var target) || !target.Exists)
            {
                return new PackageValidationFailure(
                    InGameSettingsPackageStateDto.Incomplete,
                    "An immutable package target is missing.");
            }

            if (!target.Bytes.AsSpan().SequenceEqual(bytes.AsSpan()))
            {
                return new PackageValidationFailure(
                    InGameSettingsPackageStateDto.Conflict,
                    "An immutable package target no longer matches its authorized package.");
            }
        }

        var togglesPath = new RelativeOutputPath(
            $"atmosphere/contents/{bundle.Manifest.TitleId:X16}/cheats/toggles.txt");
        if (bundle.RuntimeMutableComponents.Count > 1
            || bundle.RuntimeMutableComponents.Count == 1
            && !bundle.RuntimeMutableComponents.ContainsKey(togglesPath.Value))
        {
            return new PackageValidationFailure(
                InGameSettingsPackageStateDto.Conflict,
                "The installed package has an unsupported runtime-mutable component inventory.");
        }

        if (!targets.TryGetValue(settingsPath.CanonicalKey, out var settings)
            || !settings.Exists)
        {
            return new PackageValidationFailure(
                InGameSettingsPackageStateDto.Incomplete,
                "The package settings journal is missing.");
        }

        GameplaySettingsJournalInspection inspection;
        try
        {
            inspection = GameplaySettingsJournal.Inspect(
                settings.Bytes.AsSpan().ToArray(),
                GameplayBundleDeploymentPlanner.ToSettingsFamily(context.GameFamily),
                bundle.Manifest.TitleId);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            return new PackageValidationFailure(
                InGameSettingsPackageStateDto.Corrupt,
                "The package settings journal is malformed or noncanonical.");
        }

        if (!inspection.WritesAllowed || inspection.ActiveSnapshot is null)
        {
            return new PackageValidationFailure(
                InGameSettingsPackageStateDto.Corrupt,
                "The package settings journal is not safe to update or remove.");
        }

        var records = ownership.Inventory.Files.ToDictionary(
            record => record.Path.CanonicalKey,
            StringComparer.Ordinal);
        foreach (var path in bundle.Entries.Select(path => new RelativeOutputPath(path)))
        {
            if (!records.TryGetValue(path.CanonicalKey, out var record)
                || !string.Equals(record.Path.Value, path.Value, StringComparison.Ordinal)
                || record.ProjectId != context.ProjectId
                || record.GameFamily != context.GameFamily
                || !string.Equals(
                    record.OutputMode,
                    GameplayBundleDeploymentPlanner.OutputMode,
                    StringComparison.Ordinal)
                || !record.FileDeleteEligible
                || record.Claims.Length != 1
                || !IsBundleClaim(record.Claims[0], path, context.GameFamily))
            {
                return new PackageValidationFailure(
                    InGameSettingsPackageStateDto.Unmanaged,
                    "The package output does not have exact editor ownership for every target.");
            }

            if (path.CanonicalKey == settingsPath.CanonicalKey)
            {
                if (record.RuntimeMutableDescriptor is not { } descriptor
                    || descriptor.Kind != OutputRuntimeMutableKind.GameplaySettingsJournalV1
                    || descriptor.TitleId != bundle.Manifest.TitleId
                    || descriptor.MinimumGeneration is null)
                {
                    return new PackageValidationFailure(
                        InGameSettingsPackageStateDto.Unmanaged,
                        "The settings journal does not have exact runtime-mutable ownership.");
                }
            }
            else if (path.CanonicalKey == togglesPath.CanonicalKey)
            {
                if (!bundle.RuntimeMutableComponents.TryGetValue(
                        togglesPath.Value,
                        out var bootstrap)
                    || record.RuntimeMutableDescriptor is not { } descriptor
                    || descriptor.Kind != OutputRuntimeMutableKind.BooleanToggleListV1
                    || descriptor.TitleId != bundle.Manifest.TitleId
                    || descriptor.SemanticIdentity is not { } semanticIdentity
                    || !string.Equals(
                        semanticIdentity,
                        AtmosphereCheatToggleDocument.ComputeInventoryIdentity(
                            bootstrap.AsSpan()),
                        StringComparison.Ordinal)
                    || !AtmosphereCheatToggleDocument.HasExactInventory(
                        targets[path.CanonicalKey].Bytes.AsSpan(),
                        semanticIdentity))
                {
                    return new PackageValidationFailure(
                        InGameSettingsPackageStateDto.Unmanaged,
                        "The retired control selection file does not have exact editor ownership and inventory.");
                }
            }
            else if (record.RuntimeMutableDescriptor is not null
                     || record.CurrentState != targets[path.CanonicalKey].State)
            {
                return new PackageValidationFailure(
                    InGameSettingsPackageStateDto.Conflict,
                    "The package ownership inventory no longer matches an immutable target.");
            }
        }

        if (targets.Values.Any(target =>
                target.Exists
                && IsRuntimePackageComponentPath(target.Path, bundle.Manifest.TitleId)
                && !expected.ContainsKey(target.Path.CanonicalKey)))
        {
            return new PackageValidationFailure(
                InGameSettingsPackageStateDto.CoexistenceConflict,
                "A second installation-target layout contains native package files that are not part of the installed package inventory.");
        }

        return new PackageValidationFailure(null, null);
    }

    private static bool IsBundleClaim(
        OwnedTarget claim,
        RelativeOutputPath path,
        GameFamily gameFamily)
    {
        return claim.GameFamily == gameFamily
            && claim.Address.File == path
            && claim.Address.ScopeKind == OwnedTargetScopeKind.File
            && string.Equals(claim.OwnerId.Value, BundleOwnerId, StringComparison.Ordinal)
            && string.Equals(
                claim.PreservationRule.Key,
                BundlePreservationRule,
                StringComparison.Ordinal)
            && claim.PreservationRule.SchemaVersion == 1
            && !claim.PreservationRule.PreservesUnownedData
            && !claim.PreservationRule.RequiresPreimage;
    }

    private static CompositionSourceOwnershipFailure? ValidateCompositionSourceOwnership(
        OutputScopeContext context,
        OutputOwnershipInventorySnapshot ownership,
        InGameSettingsBundleResolution resolution,
        IReadOnlyList<OutputReadDependency> sourceDependencies)
    {
        var main = FindSourceDependency(sourceDependencies, StandaloneMainPath);
        var mainNpdm = FindSourceDependency(sourceDependencies, StandaloneMainNpdmPath);
        var runtimeSlot = FindSourceDependency(sourceDependencies, StandaloneRuntimeSlotPath);
        if (main is null
            || mainNpdm is null
            || runtimeSlot is null
            || resolution.UsesComposedMain != main.ExpectedState.Exists
            || resolution.UsesComposedMainNpdm != mainNpdm.ExpectedState.Exists
            || resolution.RequiresOwnedMainSource && !resolution.UsesComposedMain
            || resolution.RequiresOwnedMainNpdmSource && !resolution.UsesComposedMainNpdm
            || resolution.SemanticallyVerifiedMainSource
                && (!resolution.UsesComposedMain
                    || !resolution.RequiresOwnedMainSource)
            || runtimeSlot.ExpectedState.Exists)
        {
            return new CompositionSourceOwnershipFailure(
                InGameSettingsExecutableCompatibilityDto.UnreadableOrAmbiguous,
                "source-inventory-inconsistent",
                "The native-menu provider did not return a complete and internally consistent executable source inventory. KM changed no project file.");
        }

        var mainFailure = ValidateCompositionSourceOwnership(
            context,
            ownership,
            sourceDependencies,
            StandaloneMainPath,
            "exefs/main",
            resolution.RequiresOwnedMainSource,
            resolution.SemanticallyVerifiedMainSource);
        if (mainFailure is not null)
        {
            return mainFailure;
        }

        return ValidateCompositionSourceOwnership(
            context,
            ownership,
            sourceDependencies,
            StandaloneMainNpdmPath,
            "exefs/main.npdm",
            resolution.RequiresOwnedMainNpdmSource,
            semanticallyVerifiedSource: false);
    }

    private static CompositionSourceOwnershipFailure? ValidateCompositionSourceOwnership(
        OutputScopeContext context,
        OutputOwnershipInventorySnapshot ownership,
        IReadOnlyList<OutputReadDependency> sourceDependencies,
        RelativeOutputPath path,
        string displayPath,
        bool requiresPreservationAwareOwnership,
        bool semanticallyVerifiedSource)
    {
        var dependency = sourceDependencies.SingleOrDefault(candidate =>
            candidate.Path.CanonicalKey == path.CanonicalKey);
        var record = ownership.Inventory.Files.SingleOrDefault(candidate =>
            candidate.Path.CanonicalKey == path.CanonicalKey);
        if (record is not null
            && (dependency is null
                || record.ProjectId != context.ProjectId
                || record.GameFamily != context.GameFamily
                || record.CurrentState != dependency.ExpectedState
                || record.RuntimeMutableDescriptor is not null))
        {
            return new CompositionSourceOwnershipFailure(
                InGameSettingsExecutableCompatibilityDto.OwnershipUnverified,
                "source-ledger-stale-or-inconsistent",
                $"The standalone {displayPath} no longer matches its current KM ownership record. KM left it untouched and will not create a combined executable while the source ledger is stale or belongs to another project.");
        }

        if (!requiresPreservationAwareOwnership)
        {
            return null;
        }

        if (semanticallyVerifiedSource)
        {
            return null;
        }

        var preservableClaims = record?.Claims
            .Where(claim => !OutputCreatorProvenance.IsClaim(claim))
            .ToArray();
        if (dependency is null
            || !dependency.ExpectedState.Exists
            || record is null
            || preservableClaims is null
            || preservableClaims.Length == 0
            || preservableClaims.Any(claim =>
                !claim.PreservationRule.PreservesUnownedData
                || !claim.PreservationRule.RequiresPreimage))
        {
            return new CompositionSourceOwnershipFailure(
                InGameSettingsExecutableCompatibilityDto.OwnershipUnverified,
                record is null
                    ? "standalone-output-not-ledger-owned"
                    : "source-ledger-preservation-contract-invalid",
                $"The compatible standalone {displayPath} is not backed by the current KM ownership ledger and a preservation-aware preimage contract. KM left it untouched and will not create an unverifiable combined executable.");
        }

        return null;
    }

    private static InGameSettingsExecutableInputAssessmentDto
        CreateExecutableInputAssessment(
            InGameSettingsBundleResolution resolution,
            IReadOnlyList<OutputReadDependency> sourceDependencies,
            bool providerOfferedCompatiblePackage,
            CompositionSourceOwnershipFailure? sourceOwnershipFailure,
            bool usesDynamicProvider)
    {
        if (resolution.SourceDependencies is null)
        {
            if (usesDynamicProvider && resolution.AttemptedSourcePath is { } attemptedSourcePath)
            {
                return new InGameSettingsExecutableInputAssessmentDto(
                    InGameSettingsExecutableInputSourceDto.StandaloneOutput,
                    InGameSettingsExecutableCompatibilityDto.UnreadableOrAmbiguous,
                    "source-review-unavailable",
                    attemptedSourcePath.Value,
                    null,
                    null);
            }

            return new InGameSettingsExecutableInputAssessmentDto(
                InGameSettingsExecutableInputSourceDto.Base,
                usesDynamicProvider
                    ? InGameSettingsExecutableCompatibilityDto.UnreadableOrAmbiguous
                    : InGameSettingsExecutableCompatibilityDto.Absent,
                usesDynamicProvider
                    ? "source-review-unavailable"
                    : "no-standalone-output",
                null,
                null,
                null);
        }

        var main = FindSourceDependency(sourceDependencies, StandaloneMainPath);
        var mainNpdm = FindSourceDependency(sourceDependencies, StandaloneMainNpdmPath);
        var runtimeSlot = FindSourceDependency(sourceDependencies, StandaloneRuntimeSlotPath);
        var primary = runtimeSlot?.ExpectedState.Exists == true
            ? runtimeSlot
            : resolution.RequiresOwnedMainSource
                ? main
                : resolution.RequiresOwnedMainNpdmSource
                    ? mainNpdm
                    : main?.ExpectedState.Exists == true
                        ? main
                        : mainNpdm?.ExpectedState.Exists == true
                            ? mainNpdm
                            : null;
        if (primary is null)
        {
            if (sourceOwnershipFailure is not null)
            {
                return new InGameSettingsExecutableInputAssessmentDto(
                    InGameSettingsExecutableInputSourceDto.None,
                    sourceOwnershipFailure.Compatibility,
                    sourceOwnershipFailure.ReasonCode,
                    null,
                    null,
                    null);
            }

            return new InGameSettingsExecutableInputAssessmentDto(
                InGameSettingsExecutableInputSourceDto.Base,
                providerOfferedCompatiblePackage
                    ? InGameSettingsExecutableCompatibilityDto.Absent
                    : InGameSettingsExecutableCompatibilityDto.UnsupportedBuild,
                providerOfferedCompatiblePackage
                    ? "no-standalone-output"
                    : "unsupported-base-input",
                null,
                null,
                null);
        }

        var compatibility = runtimeSlot?.ExpectedState.Exists == true
            || !providerOfferedCompatiblePackage
                && (resolution.RequiresOwnedMainSource
                    || resolution.RequiresOwnedMainNpdmSource)
            ? InGameSettingsExecutableCompatibilityDto.IncompatibleOwnedRegion
            : sourceOwnershipFailure is not null
                ? sourceOwnershipFailure.Compatibility
                : resolution.RequiresOwnedMainSource
                    || resolution.RequiresOwnedMainNpdmSource
                    ? InGameSettingsExecutableCompatibilityDto.CompatiblePreservable
                    : InGameSettingsExecutableCompatibilityDto.RetailEquivalent;
        var reasonCode = runtimeSlot?.ExpectedState.Exists == true
            ? "runtime-slot-occupied"
            : sourceOwnershipFailure is not null
                ? sourceOwnershipFailure.ReasonCode
                : compatibility == InGameSettingsExecutableCompatibilityDto.IncompatibleOwnedRegion
                    ? "verified-native-region-conflict"
                    : compatibility == InGameSettingsExecutableCompatibilityDto.CompatiblePreservable
                        ? resolution.SemanticallyVerifiedMainSource
                            ? "registered-compatible-exefs-output"
                            : "ledger-owned-preservable-output"
                        : "standalone-matches-base";
        return new InGameSettingsExecutableInputAssessmentDto(
            InGameSettingsExecutableInputSourceDto.StandaloneOutput,
            compatibility,
            reasonCode,
            primary.Path.Value,
            primary.ExpectedState.Sha256,
            primary.ExpectedState.LengthBytes);
    }

    private static OutputReadDependency? FindSourceDependency(
        IReadOnlyList<OutputReadDependency> sourceDependencies,
        RelativeOutputPath path)
    {
        return sourceDependencies.SingleOrDefault(candidate =>
            candidate.Path.CanonicalKey == path.CanonicalKey);
    }

    private static InGameSettingsExecutableCompositionDto CreateCompositionDto(
        OutputScopeContext context,
        LoadedPackageState loaded,
        InGameSettingsInstallationTargetDto installationTarget)
    {
        var strategy = loaded.Snapshot.ExecutableInput.Compatibility switch
        {
            InGameSettingsExecutableCompatibilityDto.CompatiblePreservable =>
                InGameSettingsExecutableCompositionStrategyDto.CompatibleStandalone,
            InGameSettingsExecutableCompatibilityDto.RetailEquivalent
                when loaded.Snapshot.ExecutableInput.Source
                    == InGameSettingsExecutableInputSourceDto.StandaloneOutput =>
                InGameSettingsExecutableCompositionStrategyDto.RetailEquivalentStandalone,
            _ => InGameSettingsExecutableCompositionStrategyDto.StockPackage,
        };
        var selectedGame = context.Paths.SelectedGame
            ?? throw new OutputScopeMismatchException();
        var titleId = ProjectGameMetadata.Get(selectedGame).TitleId;
        return new InGameSettingsExecutableCompositionDto(
            strategy,
            NativeGameplayMenuBundleFactory.GetExecutableDestinationPath(
                titleId,
                installationTarget),
            SourcePreserved: true,
            PreservesBytesOutsideOwnedRegions: true,
            OwnedRegionCount: GetNativeExecutableOwnedRegionCount(selectedGame));
    }

    private static int GetNativeExecutableOwnedRegionCount(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword or ProjectGame.Shield => 9,
            ProjectGame.Scarlet or ProjectGame.Violet => 5,
            ProjectGame.ZA => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    private static GameplayBundleArchiveReadResult ReadEntry(
        InGameSettingsBundleCatalogEntry entry)
    {
        return GameplayBundleArchive.Read(
            entry.ArchiveBytes,
            GameplayBundleDeploymentPlanner.ToSettingsFamily(entry.GameFamily));
    }

    private static InGameSettingsPackageDescriptorDto ToDto(
        InGameSettingsBundleCatalogEntry entry)
    {
        var manifest = entry.Manifest;
        return new InGameSettingsPackageDescriptorDto(
            manifest.TitleId.ToString("X16", CultureInfo.InvariantCulture),
            manifest.UpdateVersion.ToString(),
            manifest.BuildId,
            new InGameSettingsPackageVersionDto(
                manifest.PackageVersion.Major,
                manifest.PackageVersion.Minor,
                manifest.PackageVersion.Patch),
            manifest.BundleId,
            entry.ArchiveSha256.ToLowerInvariant(),
            entry.TargetCount);
    }

    private static OutputApplyPlan BindInventoryRevision(
        OutputApplyPlan plan,
        OutputStateRevision ownershipRevision)
    {
        return new OutputApplyPlan(
            plan.ProjectId,
            plan.GameFamily,
            plan.OutputMode,
            plan.SemanticReviewHash,
            plan.Origins,
            plan.Mutations,
            plan.ReadDependencies,
            plan.DirectoryMembershipDependencies,
            ownershipRevision);
    }

    private static OutputApplyPlan BindReadDependency(
        OutputApplyPlan plan,
        OutputReadDependency dependency)
    {
        if (plan.ReadDependencies.Any(existing =>
                existing.Path.CanonicalKey == dependency.Path.CanonicalKey))
        {
            throw new InvalidDataException(
                "The in-game settings package plan already contains the static executable dependency.");
        }

        return new OutputApplyPlan(
            plan.ProjectId,
            plan.GameFamily,
            plan.OutputMode,
            plan.SemanticReviewHash,
            plan.Origins,
            plan.Mutations,
            plan.ReadDependencies.Append(dependency),
            plan.DirectoryMembershipDependencies,
            plan.OwnershipInventoryRevision);
    }

    private static bool TryParseManifest(
        ReadOnlySpan<byte> bytes,
        out GameplayBundleManifest? manifest)
    {
        try
        {
            manifest = GameplayBundleIdentity.ParseManifest(bytes);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            manifest = null;
            return false;
        }
    }

    private static async Task<ReviewedTarget> ReviewTargetAsync(
        string outputRoot,
        RelativeOutputPath path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        var absolutePath = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            path.Value.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(normalizedRoot, absolutePath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException("A package target escapes the selected output root.");
        }

        EnsureSafeExistingChain(normalizedRoot, relative);
        var file = new FileInfo(absolutePath);
        file.Refresh();
        if (!file.Exists)
        {
            if (Directory.Exists(absolutePath))
            {
                throw new IOException("A package file target is occupied by a directory.");
            }

            return new ReviewedTarget(path, false, [], OutputFileState.Missing);
        }

        if (!string.IsNullOrEmpty(file.LinkTarget)
            || file.Attributes.HasFlag(FileAttributes.Directory)
            || file.Length < 0
            || file.Length > maximumBytes)
        {
            throw new IOException("A package target is not a safe bounded regular file.");
        }

        await using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != file.Length || stream.Length > maximumBytes)
        {
            throw new IOException("A package target changed while it was being reviewed.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        EnsureSafeExistingChain(normalizedRoot, relative);
        file.Refresh();
        if (!file.Exists
            || !string.IsNullOrEmpty(file.LinkTarget)
            || file.Length != bytes.LongLength)
        {
            throw new IOException("A package target changed while it was being reviewed.");
        }

        var immutable = ImmutableArray.CreateRange(bytes);
        return new ReviewedTarget(
            path,
            true,
            immutable,
            OutputFileState.Existing(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                bytes.LongLength));
    }

    private static async Task<string?> TryReadBaseBuildIdAsync(
        string? baseExeFsRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseExeFsRoot)
            || !Path.IsPathFullyQualified(baseExeFsRoot))
        {
            return null;
        }

        try
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(baseExeFsRoot));
            var mainPath = Path.GetFullPath(Path.Combine(normalizedRoot, "main"));
            var relative = Path.GetRelativePath(normalizedRoot, mainPath);
            EnsureSafeExistingChain(normalizedRoot, relative);
            var file = new FileInfo(mainPath);
            file.Refresh();
            const int identityBytes = 0x60;
            if (!file.Exists
                || !string.IsNullOrEmpty(file.LinkTarget)
                || file.Attributes.HasFlag(FileAttributes.Directory)
                || file.Length < identityBytes)
            {
                return null;
            }

            await using var stream = new FileStream(
                mainPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: identityBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var header = new byte[identityBytes];
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            EnsureSafeExistingChain(normalizedRoot, relative);
            file.Refresh();
            if (!file.Exists
                || !string.IsNullOrEmpty(file.LinkTarget)
                || file.Length != stream.Length
                || BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x304F534E)
            {
                return null;
            }

            return Convert.ToHexString(header.AsSpan(0x40, 0x20));
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return null;
        }
    }

    private static void EnsureSafeExistingChain(string root, string relative)
    {
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = Path.Combine(current, segments[index]);
            var directory = new DirectoryInfo(current);
            directory.Refresh();
            if (!directory.Exists)
            {
                break;
            }

            if (!string.IsNullOrEmpty(directory.LinkTarget))
            {
                throw new IOException("A package target traverses a linked directory.");
            }
        }
    }

    private static string ComputeRevision(
        OutputScopeContext context,
        InGameSettingsInstallationTargetDto installationTarget,
        OutputOwnershipInventorySnapshot ownership,
        InGameSettingsBundleCatalogEntry? availableEntry,
        InGameSettingsBundleCatalogEntry? installedEntry,
        IEnumerable<ReviewedTarget> targets,
        IEnumerable<OutputReadDependency> sourceDependencies,
        IEnumerable<InGameSettingsExternalSourceDependency> externalSourceDependencies)
    {
        var builder = new StringBuilder();
        builder.Append("in-game-settings-package-review-v2\n")
            .Append(context.ScopeKey).Append('\n')
            .Append(installationTarget).Append('\n')
            .Append(ownership.Revision.Value).Append('\n')
            .Append(availableEntry?.ArchiveSha256 ?? "none").Append('\n')
            .Append(installedEntry?.ArchiveSha256 ?? "none").Append('\n');
        foreach (var target in targets.OrderBy(
                     target => target.Path.CanonicalKey,
                     StringComparer.Ordinal))
        {
            builder.Append(target.Path.Value).Append('\t')
                .Append(target.State.Exists ? '1' : '0').Append('\t')
                .Append(target.State.LengthBytes.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(target.State.Sha256 ?? "missing").Append('\n');
        }
        foreach (var dependency in sourceDependencies.OrderBy(
                     dependency => dependency.Path.CanonicalKey,
                     StringComparer.Ordinal))
        {
            builder.Append("source\t").Append(dependency.Path.Value).Append('\t')
                .Append(dependency.ExpectedState.Exists ? '1' : '0').Append('\t')
                .Append(dependency.ExpectedState.LengthBytes.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(dependency.ExpectedState.Sha256 ?? "missing").Append('\n');
        }
        foreach (var dependency in externalSourceDependencies.OrderBy(
                     dependency => dependency.AbsolutePath,
                     ExternalSourcePathComparer))
        {
            var pathFingerprint = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(dependency.AbsolutePath)));
            builder.Append("base-source\t").Append(pathFingerprint).Append('\t')
                .Append(dependency.ExpectedLength.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(dependency.ExpectedSha256 ?? "length-bound").Append('\t')
                .Append(dependency.MaximumBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string ComputeCoexistenceRevision(
        string packageRevision,
        InGameSettingsStaticSettingsGuardResult inspection)
    {
        var baseState = inspection.BaseMainState;
        var state = inspection.OutputMainState;
        var builder = new StringBuilder()
            .Append("in-game-settings-package-coexistence-v2\n")
            .Append(packageRevision).Append('\n')
            .Append(inspection.IsVanilla ? '1' : '0').Append('\n')
            .Append(inspection.OutputMainPresent ? '1' : '0').Append('\n')
            .Append(inspection.OutputMainMatchesBase ? '1' : '0').Append('\n')
            .Append(baseState.Exists ? '1' : '0').Append('\n')
            .Append(baseState.LengthBytes.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(baseState.Sha256 ?? "missing").Append('\n')
            .Append(state.Exists ? '1' : '0').Append('\n')
            .Append(state.LengthBytes.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(state.Sha256 ?? "missing").Append('\n');
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private ITimer CreateReviewPruneTimer() => timeProvider.CreateTimer(
        static state => ((InGameSettingsPackageApplicationService)state!).PruneExpiredReviews(),
        this,
        ReviewSweepInterval,
        ReviewSweepInterval);

    private void PruneExpiredReviews()
    {
        lock (syncRoot)
        {
            if (!disposed)
            {
                PruneReviewsLocked(timeProvider.GetUtcNow());
            }
        }
    }

    private static long CalculateRetainedReviewBytes(OutputApplyPlan plan)
    {
        long result = 0;
        foreach (var mutation in plan.Mutations)
        {
            result = checked(result + mutation.Postimage.Length);
        }
        return result;
    }

    private bool RemoveReviewLocked(string reviewId, out CachedReview review)
    {
        if (!reviews.Remove(reviewId, out review!))
        {
            return false;
        }

        retainedReviewBytes = checked(retainedReviewBytes - review.RetainedBytes);
        if (retainedReviewBytes < 0)
        {
            throw new InvalidOperationException(
                "The native-menu review cache byte accounting is invalid.");
        }
        return true;
    }

    private void PruneReviewsLocked(DateTimeOffset now)
    {
        foreach (var reviewId in reviews.Values
                     .Where(review => review.ExpiresAtUtc <= now)
                     .Select(review => review.ReviewId)
                     .ToArray())
        {
            RemoveReviewLocked(reviewId, out _);
        }
    }

    private static bool HaveSameExternalSourceDependencies(
        ImmutableArray<InGameSettingsExternalSourceDependency> current,
        ImmutableArray<InGameSettingsExternalSourceDependency> reviewed)
    {
        if (current.Length != reviewed.Length)
        {
            return false;
        }
        for (var index = 0; index < current.Length; index++)
        {
            var left = current[index];
            var right = reviewed[index];
            if (!ExternalSourcePathComparer.Equals(left.AbsolutePath, right.AbsolutePath)
                || left.ExpectedLength != right.ExpectedLength
                || !string.Equals(left.ExpectedSha256, right.ExpectedSha256, StringComparison.Ordinal)
                || left.MaximumBytes != right.MaximumBytes)
            {
                return false;
            }
        }
        return true;
    }

    private static async Task<ExternalSourceLeaseSet> AcquireExternalSourceLeasesAsync(
        ImmutableArray<InGameSettingsExternalSourceDependency> dependencies,
        CancellationToken cancellationToken)
    {
        var streams = new List<FileStream>(dependencies.Length);
        try
        {
            foreach (var dependency in dependencies.OrderBy(
                         dependency => dependency.AbsolutePath,
                         ExternalSourcePathComparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSafeExternalSource(dependency);
                var stream = new FileStream(
                    dependency.AbsolutePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                streams.Add(stream);
                if (stream.Length != dependency.ExpectedLength
                    || stream.Length > dependency.MaximumBytes)
                {
                    throw new IOException(
                        "A reviewed Base source changed before the package could be applied.");
                }

                if (dependency.ExpectedSha256 is not null)
                {
                    var actual = Convert.ToHexStringLower(
                        await SHA256.HashDataAsync(stream, cancellationToken)
                            .ConfigureAwait(false));
                    if (!string.Equals(
                            actual,
                            dependency.ExpectedSha256,
                            StringComparison.Ordinal))
                    {
                        throw new IOException(
                            "A reviewed Base source changed before the package could be applied.");
                    }
                }
                EnsureSafeExternalSource(dependency);
            }

            return new ExternalSourceLeaseSet(streams);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DisposeStreams(streams);
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            DisposeStreams(streams);
            throw new InGameSettingsPackageStateConflictException();
        }
        catch
        {
            DisposeStreams(streams);
            throw;
        }
    }

    private static void EnsureSafeExternalSource(
        InGameSettingsExternalSourceDependency dependency)
    {
        var file = new FileInfo(dependency.AbsolutePath);
        file.Refresh();
        if (!file.Exists
            || !string.IsNullOrEmpty(file.LinkTarget)
            || file.Attributes.HasFlag(FileAttributes.Directory)
            || file.Length != dependency.ExpectedLength
            || file.Length > dependency.MaximumBytes)
        {
            throw new IOException("A reviewed Base source is no longer a safe regular file.");
        }

        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
        {
            directory.Refresh();
            if (!directory.Exists || !string.IsNullOrEmpty(directory.LinkTarget))
            {
                throw new IOException("A reviewed Base source traverses a linked directory.");
            }
        }
    }

    private static void DisposeStreams(IEnumerable<FileStream> streams)
    {
        foreach (var stream in streams)
        {
            stream.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void ThrowIfDisposedLocked()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            reviews.Clear();
            retainedReviewBytes = 0;
        }
        reviewPruneTimer.Dispose();
    }

    private static void ValidateInstallationTarget(
        InGameSettingsInstallationTargetDto installationTarget)
    {
        if (!Enum.IsDefined(installationTarget))
        {
            throw new ArgumentOutOfRangeException(
                nameof(installationTarget),
                installationTarget,
                null);
        }
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

    private sealed record ReviewedTarget(
        RelativeOutputPath Path,
        bool Exists,
        ImmutableArray<byte> Bytes,
        OutputFileState State);

    private sealed record PackageValidationFailure(
        InGameSettingsPackageStateDto? State,
        string? Detail);

    private sealed record CompositionSourceOwnershipFailure(
        InGameSettingsExecutableCompatibilityDto Compatibility,
        string ReasonCode,
        string Detail);

    private sealed record LoadedPackageState(
        InGameSettingsPackageSnapshotDto Snapshot,
        InGameSettingsBundleCatalogEntry? InstalledEntry,
        InGameSettingsBundleCatalogEntry? AvailableEntry,
        OutputOwnershipInventorySnapshot Ownership,
        ImmutableDictionary<string, ReviewedTarget> Targets,
        ImmutableArray<OutputReadDependency> SourceDependencies = default,
        ImmutableArray<InGameSettingsExternalSourceDependency> ExternalSourceDependencies = default,
        bool UsesComposedMain = false,
        bool UsesComposedMainNpdm = false);

    private sealed record CachedReview(
        string ReviewId,
        string ScopeKey,
        string ExpectedRevision,
        InGameSettingsPackageOperationDto Operation,
        InGameSettingsInstallationTargetDto InstallationTarget,
        DateTimeOffset ExpiresAtUtc,
        OutputApplyPlan ApplyPlan,
        ImmutableArray<InGameSettingsExternalSourceDependency> ExternalSourceDependencies,
        long RetainedBytes);

    private sealed class ExternalSourceLeaseSet : IDisposable
    {
        internal static ExternalSourceLeaseSet Empty { get; } = new([]);

        private readonly IReadOnlyList<FileStream> streams;

        internal ExternalSourceLeaseSet(IReadOnlyList<FileStream> streams)
        {
            this.streams = streams;
        }

        public void Dispose() => DisposeStreams(streams);
    }
}

public sealed class InGameSettingsPackageUnavailableException : Exception
{
    public InGameSettingsPackageUnavailableException(string detail)
        : base(detail)
    {
    }
}

public sealed class InGameSettingsPackageStateConflictException : Exception
{
    public InGameSettingsPackageStateConflictException()
        : base("The in-game settings package state changed after it was reviewed.")
    {
    }
}

public sealed class InGameSettingsPackageReviewExpiredException : Exception
{
    public InGameSettingsPackageReviewExpiredException()
        : base("The reviewed in-game settings package operation is no longer available.")
    {
    }
}
