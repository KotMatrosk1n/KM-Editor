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
    OutputFileState OutputMainState);

public interface IInGameSettingsStaticSettingsGuard
{
    Task<InGameSettingsStaticSettingsGuardResult> InspectAsync(
        OutputScopeDto scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reviews and applies an exact editor-shipped in-game settings package through
/// the durable output transaction coordinator. Bridge callers can select an
/// operation, but can never supply package bytes or expand bundle authority.
/// </summary>
public sealed class InGameSettingsPackageApplicationService
{
    private const int MaximumTargetBytes = GameplayBundleArchive.MaximumEntryBytes;
    private const string BundleOwnerId = "gameplay-bundle";
    private const string BundlePreservationRule = "whole-file-gameplay-bundle";
    private static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(10);

    private readonly object syncRoot = new();
    private readonly Dictionary<string, CachedReview> reviews = new(StringComparer.Ordinal);
    private readonly InGameSettingsBundleCatalog catalog;
    private readonly GameplaySettingsBundleAuthority authority;
    private readonly TimeProvider timeProvider;
    private readonly IInGameSettingsStaticSettingsGuard staticSettingsGuard;

    public InGameSettingsPackageApplicationService(
        InGameSettingsBundleCatalog? catalog = null,
        GameplaySettingsBundleAuthority? authority = null,
        TimeProvider? timeProvider = null,
        IInGameSettingsStaticSettingsGuard? staticSettingsGuard = null)
    {
        this.catalog = catalog ?? InGameSettingsBundleCatalog.Empty;
        this.authority = authority ?? GameplaySettingsBundleAuthority.DenyAll;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.staticSettingsGuard = staticSettingsGuard ?? new DefaultStaticSettingsGuard();
    }

    public async Task<InspectInGameSettingsPackageResponse> InspectAsync(
        InspectInGameSettingsPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        var loaded = await LoadWithCoexistenceAsync(
                request.Scope,
                context,
                cancellationToken)
            .ConfigureAwait(false);
        return new InspectInGameSettingsPackageResponse(loaded.Snapshot);
    }

    public async Task<PreviewInGameSettingsPackageResponse> PreviewAsync(
        PreviewInGameSettingsPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Operation))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Operation));
        }

        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        var loaded = await LoadWithCoexistenceAsync(
                request.Scope,
                context,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                loaded.Snapshot.Revision,
                request.ExpectedRevision,
                StringComparison.Ordinal))
        {
            throw new InGameSettingsPackageStateConflictException();
        }

        var staticDependency = request.Operation is InGameSettingsPackageOperationDto.Install
            or InGameSettingsPackageOperationDto.Upgrade
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
        if (loaded.CheatDirectoryMembership is { } cheatDirectoryMembership)
        {
            plan = BindDirectoryDependency(
                plan,
                cheatDirectoryMembership.ToDependency());
        }
        var expiresAtUtc = timeProvider.GetUtcNow().Add(ReviewLifetime);
        var reviewId = Guid.NewGuid().ToString("N");
        lock (syncRoot)
        {
            PruneReviewsLocked(timeProvider.GetUtcNow());
            if (reviews.Count == InGameSettingsPackageContract.MaximumCachedReviews)
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
                    loaded.Snapshot.Revision,
                    request.Operation,
                    expiresAtUtc,
                    plan));
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
        return new PreviewInGameSettingsPackageResponse(
            reviewId,
            expiresAtUtc,
            request.Operation,
            loaded.Snapshot,
            targets,
            targets.Length != plan.Mutations.Length);
    }

    public async Task<ApplyInGameSettingsPackageResponse> ApplyAsync(
        ApplyInGameSettingsPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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
            PruneReviewsLocked(timeProvider.GetUtcNow());
            if (!reviews.Remove(request.ReviewId, out review!)
                || review.ScopeKey != context.ScopeKey
                || review.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                throw new InGameSettingsPackageReviewExpiredException();
            }
        }


        if (review.Operation is InGameSettingsPackageOperationDto.Install
            or InGameSettingsPackageOperationDto.Upgrade)
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
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                current.Snapshot.Revision,
                review.ExpectedRevision,
                StringComparison.Ordinal))
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
                "An exact editor-shipped package is not ready for initial installation.");
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
            var isVanilla = staticState.State == GameplaySettingsStateDto.Ready
                && staticState.Values is
                {
                    ExperienceShareEnabled: true,
                    ExperienceRateBasisPoints: 10_000,
                    LevelCapEnabled: false,
                    LevelCap: 100,
                };
            return new InGameSettingsStaticSettingsGuardResult(
                isVanilla,
                staticState.TargetState ?? OutputFileState.Missing);
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
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
        if (loaded.Snapshot.State is not (
                InGameSettingsPackageStateDto.Installed or
                InGameSettingsPackageStateDto.UpgradeAvailable))
        {
            return loaded;
        }

        var inspection = await staticSettingsGuard
            .InspectAsync(scope, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = loaded.Snapshot with
        {
            State = inspection.IsVanilla
                ? loaded.Snapshot.State
                : InGameSettingsPackageStateDto.CoexistenceConflict,
            Revision = ComputeCoexistenceRevision(
                loaded.Snapshot.Revision,
                inspection),
            Detail = inspection.IsVanilla
                ? loaded.Snapshot.Detail
                : "The KM-managed package files are intact, but the executable or selected base build no longer matches the vanilla state reviewed for safe coexistence. Package removal remains available.",
        };
        return loaded with { Snapshot = snapshot };
    }

    private async Task<LoadedPackageState> LoadAsync(
        OutputScopeContext context,
        CancellationToken cancellationToken)
    {
        var selectedGame = context.Paths.SelectedGame
            ?? throw new OutputScopeMismatchException();
        var titleId = ProjectGameMetadata.Get(selectedGame).TitleId;
        var authorizedEntries = catalog.GetEntries(context.GameFamily, titleId)
            .Where(entry => authority.IsAuthorized(entry.AuthorityKey))
            .ToImmutableArray();
        var ownership = await context.Coordinator
            .GetOwnershipInventorySnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        if (authorizedEntries.IsEmpty)
        {
            var unavailableRevision = ComputeRevision(
                context,
                ownership,
                availableEntry: null,
                installedEntry: null,
                []);
            return new LoadedPackageState(
                new InGameSettingsPackageSnapshotDto(
                    InGameSettingsPackageStateDto.Unavailable,
                    unavailableRevision,
                    PackageAvailable: false,
                    InstalledPackage: null,
                    AvailablePackage: null,
                    "No authorized editor-shipped package is available for this exact title."),
                null,
                null,
                ownership,
                ImmutableDictionary<string, ReviewedTarget>.Empty);
        }

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

        var knownBundles = authorizedEntries.ToDictionary(
            entry => entry.Manifest.BundleId,
            ReadEntry,
            StringComparer.Ordinal);
        var manifestPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{titleId:X16}/bundle.manifest");
        var manifestReview = await ReviewTargetAsync(
                context.Paths.OutputRootPath!,
                manifestPath,
                GameplayBundleIdentity.MaximumSerializationBytes,
                cancellationToken)
            .ConfigureAwait(false);

        InGameSettingsBundleCatalogEntry? installedEntry = null;
        if (manifestReview.Exists)
        {
            installedEntry = authorizedEntries.SingleOrDefault(entry =>
                manifestReview.Bytes.AsSpan().SequenceEqual(
                    knownBundles[entry.Manifest.BundleId].ManifestBytes.AsSpan()));
        }

        var pathInventory = new Dictionary<string, RelativeOutputPath>(StringComparer.Ordinal)
        {
            [manifestPath.CanonicalKey] = manifestPath,
        };
        foreach (var entry in new[] { installedEntry, availableEntry }.Where(entry => entry is not null))
        {
            foreach (var path in knownBundles[entry!.Manifest.BundleId].Entries)
            {
                var outputPath = new RelativeOutputPath(path);
                pathInventory.TryAdd(outputPath.CanonicalKey, outputPath);
            }
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
                        MaximumTargetBytes,
                        cancellationToken)
                    .ConfigureAwait(false));
        }

        var cheatDirectory = new RelativeOutputPath(
            $"atmosphere/contents/{titleId:X16}/cheats");
        var cheatDirectoryMembership = await context.Coordinator
            .CaptureDirectoryMembershipAsync(cheatDirectory, cancellationToken)
            .ConfigureAwait(false);
        var allowedBuildCheatPaths = pathInventory.Values
            .Where(path => IsBuildCheatFile(cheatDirectory, path))
            .Select(path => path.CanonicalKey)
            .ToHashSet(StringComparer.Ordinal);
        var foreignBuildCheatFiles = cheatDirectoryMembership.Entries
            .Where(entry => !entry.IsDirectory
                && IsBuildCheatFile(cheatDirectory, entry.Path)
                && !allowedBuildCheatPaths.Contains(entry.Path.CanonicalKey))
            .Select(entry => entry.Path)
            .OrderBy(path => path.CanonicalKey, StringComparer.Ordinal)
            .ToArray();

        var revision = ComputeRevision(
            context,
            ownership,
            availableEntry,
            installedEntry,
            targets.Values,
            cheatDirectoryMembership);
        var availableDto = availableEntry is null ? null : ToDto(availableEntry);
        if (!manifestReview.Exists)
        {
            var collision = targets.Values.Any(target => target.Exists)
                || foreignBuildCheatFiles.Length > 0;
            var state = collision
                ? InGameSettingsPackageStateDto.Conflict
                : availableEntry is null
                    ? InGameSettingsPackageStateDto.Unavailable
                    : InGameSettingsPackageStateDto.NotInstalled;
            return new LoadedPackageState(
                new InGameSettingsPackageSnapshotDto(
                    state,
                    revision,
                    PackageAvailable: availableEntry is not null,
                    InstalledPackage: null,
                    AvailablePackage: availableDto,
                    collision
                        ? foreignBuildCheatFiles.Length > 0
                            ? "Another build-specific cheat file already uses this title's shared selection document. Remove other build cheat files before installing the KM package."
                            : "One or more exact package targets already exist without the package manifest."
                        : state == InGameSettingsPackageStateDto.Unavailable
                            ? "No authorized current package is available for this exact title."
                            : null),
                null,
                availableEntry,
                ownership,
                targets.ToImmutableDictionary(StringComparer.Ordinal),
                cheatDirectoryMembership);
        }

        if (installedEntry is null)
        {
            var state = TryParseManifest(manifestReview.Bytes.AsSpan(), out _)
                ? InGameSettingsPackageStateDto.Unmanaged
                : InGameSettingsPackageStateDto.Corrupt;
            return new LoadedPackageState(
                new InGameSettingsPackageSnapshotDto(
                    state,
                    revision,
                    PackageAvailable: availableEntry is not null,
                    InstalledPackage: null,
                    AvailablePackage: availableDto,
                    state == InGameSettingsPackageStateDto.Unmanaged
                        ? "The existing package manifest is not in the authorized editor catalog."
                        : "The existing package manifest is malformed or noncanonical."),
                null,
                availableEntry,
                ownership,
                targets.ToImmutableDictionary(StringComparer.Ordinal),
                cheatDirectoryMembership);
        }

        var installedBundle = knownBundles[installedEntry.Manifest.BundleId];
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
                    revision,
                    PackageAvailable: availableEntry is not null,
                    InstalledPackage: ToDto(installedEntry),
                    AvailablePackage: availableDto,
                    invalidState == InGameSettingsPackageStateDto.Conflict
                        && foreignBuildCheatFiles.Length > 0
                        ? "Another build-specific cheat file can replace this title's shared selection document. Remove the other build cheat files and restore the KM selection document before continuing."
                        : validation.Detail),
                installedEntry,
                availableEntry,
                ownership,
                targets.ToImmutableDictionary(StringComparer.Ordinal),
                cheatDirectoryMembership);
        }

        if (foreignBuildCheatFiles.Length > 0)
        {
            return new LoadedPackageState(
                new InGameSettingsPackageSnapshotDto(
                    InGameSettingsPackageStateDto.Conflict,
                    revision,
                    PackageAvailable: availableEntry is not null,
                    InstalledPackage: ToDto(installedEntry),
                    AvailablePackage: availableDto,
                    "Another build-specific cheat file shares this title's selection document. Remove other build cheat files before changing or removing the KM package."),
                installedEntry,
                availableEntry,
                ownership,
                targets.ToImmutableDictionary(StringComparer.Ordinal),
                cheatDirectoryMembership);
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
                revision,
                PackageAvailable: availableEntry is not null,
                InstalledPackage: ToDto(installedEntry),
                AvailablePackage: availableDto),
            installedEntry,
            availableEntry,
            ownership,
            targets.ToImmutableDictionary(StringComparer.Ordinal),
            cheatDirectoryMembership);
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
        var togglesPath = new RelativeOutputPath(
            $"atmosphere/contents/{bundle.Manifest.TitleId:X16}/cheats/toggles.txt");
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

        foreach (var component in bundle.RuntimeMutableComponents)
        {
            var path = new RelativeOutputPath(component.Key);
            if (!targets.TryGetValue(path.CanonicalKey, out var target) || !target.Exists)
            {
                return new PackageValidationFailure(
                    InGameSettingsPackageStateDto.Incomplete,
                    "The package cheat selection document is missing.");
            }

            var identity = AtmosphereCheatToggleDocument.ComputeInventoryIdentity(
                component.Value.AsSpan());
            if (!AtmosphereCheatToggleDocument.HasExactInventory(
                    target.Bytes.AsSpan(),
                    identity))
            {
                return new PackageValidationFailure(
                    InGameSettingsPackageStateDto.Conflict,
                    "The package cheat selection document contains foreign or malformed entries.");
            }
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
                if (!bundle.RuntimeMutableComponents.TryGetValue(path.Value, out var defaults)
                    || record.RuntimeMutableDescriptor is not { } descriptor
                    || descriptor.Kind != OutputRuntimeMutableKind.BooleanToggleListV1
                    || descriptor.TitleId != bundle.Manifest.TitleId
                    || !string.Equals(
                        descriptor.SemanticIdentity,
                        AtmosphereCheatToggleDocument.ComputeInventoryIdentity(defaults.AsSpan()),
                        StringComparison.Ordinal))
                {
                    return new PackageValidationFailure(
                        InGameSettingsPackageStateDto.Unmanaged,
                        "The cheat selection document does not have exact runtime-mutable ownership.");
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

    private static OutputApplyPlan BindDirectoryDependency(
        OutputApplyPlan plan,
        OutputDirectoryMembershipDependency dependency)
    {
        if (plan.DirectoryMembershipDependencies.Any(existing =>
                existing.Directory.CanonicalKey == dependency.Directory.CanonicalKey))
        {
            throw new InvalidDataException(
                "The in-game settings package plan already contains the cheat directory dependency.");
        }

        return new OutputApplyPlan(
            plan.ProjectId,
            plan.GameFamily,
            plan.OutputMode,
            plan.SemanticReviewHash,
            plan.Origins,
            plan.Mutations,
            plan.ReadDependencies,
            plan.DirectoryMembershipDependencies.Append(dependency),
            plan.OwnershipInventoryRevision);
    }

    private static bool IsBuildCheatFile(
        RelativeOutputPath cheatDirectory,
        RelativeOutputPath candidate)
    {
        var prefix = cheatDirectory.CanonicalKey + "/";
        if (!candidate.CanonicalKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = candidate.Value[(cheatDirectory.Value.Length + 1)..];
        return !fileName.Contains('/', StringComparison.Ordinal)
            && fileName.Length == 20
            && fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && fileName.AsSpan(0, 16).ToArray().All(Uri.IsHexDigit);
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
        OutputOwnershipInventorySnapshot ownership,
        InGameSettingsBundleCatalogEntry? availableEntry,
        InGameSettingsBundleCatalogEntry? installedEntry,
        IEnumerable<ReviewedTarget> targets,
        OutputDirectoryMembershipSnapshot? cheatDirectoryMembership = null)
    {
        var builder = new StringBuilder();
        builder.Append("in-game-settings-package-review-v1\n")
            .Append(context.ScopeKey).Append('\n')
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
        builder.Append("cheat-directory\t")
            .Append(cheatDirectoryMembership?.Revision.Value ?? "unobserved")
            .Append('\n');

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string ComputeCoexistenceRevision(
        string packageRevision,
        InGameSettingsStaticSettingsGuardResult inspection)
    {
        var state = inspection.OutputMainState;
        var builder = new StringBuilder()
            .Append("in-game-settings-package-coexistence-v1\n")
            .Append(packageRevision).Append('\n')
            .Append(inspection.IsVanilla ? '1' : '0').Append('\n')
            .Append(state.Exists ? '1' : '0').Append('\n')
            .Append(state.LengthBytes.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(state.Sha256 ?? "missing").Append('\n');
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private void PruneReviewsLocked(DateTimeOffset now)
    {
        foreach (var reviewId in reviews.Values
                     .Where(review => review.ExpiresAtUtc <= now)
                     .Select(review => review.ReviewId)
                     .ToArray())
        {
            reviews.Remove(reviewId);
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

    private sealed record LoadedPackageState(
        InGameSettingsPackageSnapshotDto Snapshot,
        InGameSettingsBundleCatalogEntry? InstalledEntry,
        InGameSettingsBundleCatalogEntry? AvailableEntry,
        OutputOwnershipInventorySnapshot Ownership,
        ImmutableDictionary<string, ReviewedTarget> Targets,
        OutputDirectoryMembershipSnapshot? CheatDirectoryMembership = null);

    private sealed record CachedReview(
        string ReviewId,
        string ScopeKey,
        string ExpectedRevision,
        InGameSettingsPackageOperationDto Operation,
        DateTimeOffset ExpiresAtUtc,
        OutputApplyPlan ApplyPlan);
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
