// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Collections.Immutable;
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
using KM.SV.RuntimeSettings;
using KM.SwSh.RuntimeSettings;
using KM.ZA.RuntimeSettings;

namespace KM.Tools.Application;

/// <summary>
/// Owns short-lived, single-use reviews for the exact-build beta gameplay
/// editors. Available settings are encoded directly into a composed exefs/main;
/// no inert settings sidecar or unverified runtime hook is emitted.
/// </summary>
public sealed class GameplaySettingsApplicationService
{
    private const int MaximumMainBytes = 128 * 1024 * 1024;
    private const string WorkflowId = "workflow.gameplay-settings";
    private static readonly RelativeOutputPath MainPath = new("exefs/main");
    private static readonly OwnershipOwnerId OwnerId = new(WorkflowId);
    private static readonly PreservationRuleDescriptor PreservationRule = new(
        "preserve-unowned-exefs-main",
        schemaVersion: 1,
        preservesUnownedData: true,
        requiresPreimage: true);
    private static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(10);

    private readonly object syncRoot = new();
    private readonly Dictionary<string, CachedReview> reviews = new(StringComparer.Ordinal);

    public async Task<GetGameplaySettingsResponse> GetAsync(
        GetGameplaySettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        var loaded = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
        return new GetGameplaySettingsResponse(loaded.State, loaded.Dto, loaded.Detail);
    }

    public async Task<PreviewGameplaySettingsUpdateResponse> PreviewUpdateAsync(
        PreviewGameplaySettingsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = OutputSafetyApplicationService.ResolveScope(request.Scope);
        var loaded = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
        if (loaded.State != GameplaySettingsStateDto.Ready
            || loaded.Dto is null
            || loaded.Analysis is null
            || loaded.BaseBytes is null
            || loaded.CurrentBytes is null
            || loaded.BaseState is null
            || loaded.TargetState is null)
        {
            throw new GameplaySettingsUnavailableException(loaded.State, loaded.Detail);
        }

        var expectedGeneration = ParseGeneration(request.ExpectedGeneration);
        if (expectedGeneration != ParseGeneration(loaded.Dto.Generation))
        {
            throw new GameplaySettingsStateConflictException();
        }

        var requestedValues = new GameplaySettingsValues(
            request.ExperienceShareEnabled ?? loaded.Analysis.Values.ExperienceShareEnabled,
            request.ExperienceRateBasisPoints ?? loaded.Analysis.Values.ExperienceRateBasisPoints,
            request.LevelCapEnabled ?? loaded.Analysis.Values.LevelCapEnabled,
            request.LevelCap ?? loaded.Analysis.Values.LevelCap);
        EnsureRequestedCapabilities(loaded.Analysis, requestedValues);

        byte[] postimage;
        GameplayAnalysis afterAnalysis;
        try
        {
            postimage = ApplyForGame(
                loaded.Game,
                loaded.BaseBytes,
                loaded.CurrentBytes,
                requestedValues);
            if (postimage.AsSpan().SequenceEqual(loaded.CurrentBytes))
            {
                throw new ArgumentException(
                    "The reviewed gameplay settings do not contain an effective change.",
                    nameof(request));
            }

            var analyzed = AnalyzeForGame(loaded.Game, loaded.BaseBytes, postimage);
            if (analyzed.State != GameplaySettingsStateDto.Ready
                || analyzed.Analysis is null
                || analyzed.Analysis.Values != requestedValues)
            {
                throw new InvalidDataException(
                    "The reviewed gameplay settings output did not round-trip to the requested values.");
            }

            afterAnalysis = analyzed.Analysis;
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException)
        {
            throw new GameplaySettingsUnavailableException(
                GameplaySettingsStateDto.Conflict,
                exception.Message);
        }

        OutputApplyPlan plan;
        try
        {
            var currentClaims = loaded.Ownership?.Claims
                ?? ImmutableArray<OwnedTarget>.Empty;
            var returningToVanilla = requestedValues == GameplaySettingsValues.Vanilla;
            var gameplayClaims = currentClaims
                .Where(claim => claim.OwnerId == OwnerId)
                .ToImmutableArray();
            var foreignClaims = currentClaims
                .Where(claim => claim.OwnerId != OwnerId)
                .ToImmutableArray();
            var needsLegacyCreatorProvenance = loaded.Ownership is { FileDeleteEligible: true }
                && !currentClaims.Any(claim =>
                    claim.Address.ScopeKind == OwnedTargetScopeKind.File);
            var adoptVanillaStewardship = returningToVanilla
                && gameplayClaims.IsEmpty;
            var retainExistingVanillaStewardship = returningToVanilla
                && foreignClaims.IsEmpty
                && loaded.Ownership is { FileDeleteEligible: false }
                && !gameplayClaims.IsEmpty;
            var needsCreatorProvenance = returningToVanilla
                && loaded.Ownership is { FileDeleteEligible: true }
                && !gameplayClaims.IsEmpty
                && foreignClaims.Any(claim => !OutputCreatorProvenance.IsClaim(claim))
                && !foreignClaims.Any(claim =>
                    claim.Address.ScopeKind == OwnedTargetScopeKind.File
                    && !OutputCreatorProvenance.IsClaim(claim));
            ImmutableArray<OwnedTarget> claims;
            if (!returningToVanilla)
            {
                claims = currentClaims
                    .Append(CreateRecordOwnershipClaim(context.GameFamily, loaded.TitleId))
                    .Concat(loaded.TargetState.Exists
                        ? []
                        : [CreateFileOwnershipClaim(context.GameFamily)])
                    .Concat(needsLegacyCreatorProvenance
                        ? [OutputCreatorProvenance.Create(context.GameFamily, MainPath)]
                        : [])
                    .Distinct()
                    .ToImmutableArray();
            }
            else if (adoptVanillaStewardship)
            {
                claims = foreignClaims
                    .Append(CreateRecordOwnershipClaim(context.GameFamily, loaded.TitleId))
                    .Concat(needsLegacyCreatorProvenance
                        ? [OutputCreatorProvenance.Create(context.GameFamily, MainPath)]
                        : [])
                    .Distinct()
                    .ToImmutableArray();
            }
            else if (retainExistingVanillaStewardship)
            {
                claims = gameplayClaims;
            }
            else
            {
                claims = foreignClaims
                    .Concat(needsCreatorProvenance
                        ? [OutputCreatorProvenance.Create(context.GameFamily, MainPath)]
                        : [])
                    .Distinct()
                    .ToImmutableArray();
            }
            var outputMode = loaded.Ownership?.OutputMode ?? GetDefaultOutputMode(loaded.Game);
            OutputMutation mutation;
            if (returningToVanilla
                && postimage.AsSpan().SequenceEqual(loaded.BaseBytes)
                && !gameplayClaims.IsEmpty
                && foreignClaims.All(OutputCreatorProvenance.IsClaim)
                && loaded.TargetState.Exists
                && loaded.Ownership is { FileDeleteEligible: true })
            {
                var authority = new OutputVerifiedBaseDeleteAuthority(
                    context.ProjectId,
                    context.GameFamily,
                    OwnerId,
                    outputMode,
                    MainPath,
                    loaded.TargetState,
                    loaded.BaseState,
                    loaded.Ownership.Claims);
                mutation = OutputMutation.DeleteVerifiedBase(
                    MainPath,
                    loaded.TargetState,
                    loaded.Ownership.Claims,
                    authority);
            }
            else
            {
                if (claims.IsEmpty)
                {
                    throw new ArgumentException(
                        "Vanilla restoration cannot discard the executable's remaining ownership contract.");
                }

                mutation = OutputMutation.Write(
                    MainPath,
                    postimage,
                    loaded.TargetState,
                    claims,
                    outputMode,
                    ownershipActor: OwnerId);
            }

            plan = new OutputApplyPlan(
                context.ProjectId,
                context.GameFamily,
                outputMode,
                OutputReviewFingerprint.FromMutations([mutation]),
                [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, WorkflowId)],
                [mutation],
                ownershipInventoryRevision: loaded.OwnershipInventoryRevision);
        }
        catch (ArgumentException exception)
        {
            throw new GameplaySettingsUnavailableException(
                GameplaySettingsStateDto.Conflict,
                exception.Message);
        }

        var reviewId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(ReviewLifetime);
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
                    loaded.Dto.Generation,
                    loaded.BaseState,
                    loaded.OwnershipSignature,
                    plan));
        }

        return new PreviewGameplaySettingsUpdateResponse(
            reviewId,
            expiresAtUtc,
            loaded.Dto,
            ToDto(loaded.Game, loaded.TitleId, afterAnalysis, postimage));
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

        FileStream baseLease;
        try
        {
            baseLease = await OpenVerifiedBaseLeaseAsync(
                    context,
                    review.BaseState,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnavailableFileSystemException(exception))
        {
            throw new GameplaySettingsStateConflictException();
        }

        await using (baseLease.ConfigureAwait(false))
        {
            var loaded = await LoadAsync(context, cancellationToken).ConfigureAwait(false);
            if (loaded.State != GameplaySettingsStateDto.Ready
                || loaded.Dto?.Generation != review.Generation
                || loaded.BaseState != review.BaseState
                || !string.Equals(
                    loaded.OwnershipSignature,
                    review.OwnershipSignature,
                    StringComparison.Ordinal))
            {
                throw new GameplaySettingsStateConflictException();
            }

            var result = await context.Coordinator
                .ApplyAsync(review.ApplyPlan, cancellationToken)
                .ConfigureAwait(false);
            return await CreateApplyResponseAsync(context, result).ConfigureAwait(false);
        }
    }

    private static async Task<ApplyGameplaySettingsUpdateResponse> CreateApplyResponseAsync(
        OutputScopeContext context,
        OutputApplyResult result)
    {
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
                // The durable transaction result is authoritative. Refreshing the
                // display is best effort and cannot turn a commit into a failure.
                snapshot = null;
            }
        }

        return new ApplyGameplaySettingsUpdateResponse(
            result.TransactionId.Value,
            outcome,
            snapshot);
    }

    internal static async Task<(
        GameplaySettingsStateDto State,
        GameplaySettingsValuesDto? Values,
        string? Detail,
        OutputFileState? BaseState,
        OutputFileState? TargetState,
        bool OutputPresent,
        bool OutputMatchesBase)>
        InspectStaticValuesForInGamePackageAsync(
            OutputScopeContext context,
            CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(
                context,
                cancellationToken,
                allowInGamePackageManifest: true)
            .ConfigureAwait(false);
        var outputMatchesBase = loaded.BaseBytes is not null
            && loaded.CurrentBytes is not null
            && loaded.TargetState is { Exists: true }
            && loaded.BaseBytes.AsSpan().SequenceEqual(loaded.CurrentBytes);
        var outputPresent = IsOutputMainPresent(context.Paths.OutputRootPath);
        return (
            loaded.State,
            loaded.Dto?.Values,
            loaded.Detail,
            loaded.BaseState,
            loaded.TargetState,
            outputPresent,
            outputMatchesBase);
    }

    private static bool IsOutputMainPresent(string? outputRootPath)
    {
        if (string.IsNullOrWhiteSpace(outputRootPath)
            || !Path.IsPathFullyQualified(outputRootPath))
        {
            return true;
        }

        try
        {
            var outputRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(outputRootPath));
            var outputPath = ToAbsolutePath(outputRoot, MainPath.Value);
            var outputRootProbe = ProbeOutputPathEntry(outputRoot);
            if (outputRootProbe == OutputPathEntryProbe.Missing)
            {
                return false;
            }
            if (outputRootProbe != OutputPathEntryProbe.Directory)
            {
                return true;
            }

            var exefsPath = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(exefsPath))
            {
                return true;
            }

            var exefsProbe = ProbeOutputPathEntry(exefsPath);
            if (exefsProbe == OutputPathEntryProbe.Missing)
            {
                return false;
            }
            if (exefsProbe != OutputPathEntryProbe.Directory)
            {
                return true;
            }

            return ProbeOutputPathEntry(outputPath) != OutputPathEntryProbe.Missing;
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or
            SecurityException or
            IOException or
            ArgumentException or
            NotSupportedException)
        {
            return true;
        }
    }

    private static OutputPathEntryProbe ProbeOutputPathEntry(string path)
    {
        var fileLinkProbe = ProbeLinkTarget(new FileInfo(path));
        if (fileLinkProbe == LinkTargetProbe.Link)
        {
            return OutputPathEntryProbe.Link;
        }
        if (fileLinkProbe == LinkTargetProbe.Ambiguous)
        {
            return OutputPathEntryProbe.Ambiguous;
        }

        var directoryLinkProbe = ProbeLinkTarget(new DirectoryInfo(path));
        if (directoryLinkProbe == LinkTargetProbe.Link)
        {
            return OutputPathEntryProbe.Link;
        }
        if (directoryLinkProbe == LinkTargetProbe.Ambiguous)
        {
            return OutputPathEntryProbe.Ambiguous;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return OutputPathEntryProbe.Link;
            }

            return attributes.HasFlag(FileAttributes.Directory)
                ? OutputPathEntryProbe.Directory
                : OutputPathEntryProbe.File;
        }
        catch (Exception exception) when (exception is
            FileNotFoundException or
            DirectoryNotFoundException)
        {
            return OutputPathEntryProbe.Missing;
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or
            SecurityException or
            IOException or
            ArgumentException or
            NotSupportedException)
        {
            return OutputPathEntryProbe.Ambiguous;
        }
    }

    private static LinkTargetProbe ProbeLinkTarget(FileSystemInfo entry)
    {
        try
        {
            entry.Refresh();
            return string.IsNullOrEmpty(entry.LinkTarget)
                ? LinkTargetProbe.None
                : LinkTargetProbe.Link;
        }
        catch (Exception exception) when (exception is
            FileNotFoundException or
            DirectoryNotFoundException)
        {
            return LinkTargetProbe.None;
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or
            SecurityException or
            IOException or
            ArgumentException or
            NotSupportedException)
        {
            return LinkTargetProbe.Ambiguous;
        }
    }

    private enum OutputPathEntryProbe
    {
        Missing,
        File,
        Directory,
        Link,
        Ambiguous,
    }

    private enum LinkTargetProbe
    {
        None,
        Link,
        Ambiguous,
    }

    private static async Task<LoadedState> LoadAsync(
        OutputScopeContext context,
        CancellationToken cancellationToken,
        bool allowInGamePackageManifest = false)
    {
        var game = context.Paths.SelectedGame
            ?? throw new OutputScopeMismatchException();
        var titleId = ProjectGameMetadata.Get(game).TitleId;
        if (string.IsNullOrWhiteSpace(context.Paths.BaseExeFsPath)
            || !Path.IsPathFullyQualified(context.Paths.BaseExeFsPath)
            || string.IsNullOrWhiteSpace(context.Paths.OutputRootPath)
            || !Path.IsPathFullyQualified(context.Paths.OutputRootPath))
        {
            return LoadedState.Unavailable(game, titleId, GameplaySettingsStateDto.Missing);
        }

        string baseRoot;
        string basePath;
        string outputRoot;
        string outputPath;
        string inGamePackageManifestPath;
        try
        {
            baseRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(context.Paths.BaseExeFsPath));
            basePath = ToAbsolutePath(baseRoot, "main");
            outputRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(context.Paths.OutputRootPath!));
            outputPath = ToAbsolutePath(outputRoot, MainPath.Value);
            inGamePackageManifestPath = ToAbsolutePath(
                outputRoot,
                $"config/km-editor/gameplay-settings/{titleId:X16}/bundle.manifest");
        }
        catch (Exception exception) when (IsUnavailableFileSystemException(exception))
        {
            return LoadedState.Unavailable(game, titleId, GameplaySettingsStateDto.Missing);
        }

        if (Directory.Exists(basePath)
            || Directory.Exists(outputPath)
            || Directory.Exists(inGamePackageManifestPath))
        {
            return LoadedState.Unavailable(game, titleId, GameplaySettingsStateDto.Conflict);
        }

        if (File.Exists(inGamePackageManifestPath) && !allowInGamePackageManifest)
        {
            return LoadedState.Unavailable(
                game,
                titleId,
                GameplaySettingsStateDto.Conflict,
                "Remove the installed in-game settings package before changing static gameplay settings.");
        }

        if (!File.Exists(basePath))
        {
            return LoadedState.Unavailable(game, titleId, GameplaySettingsStateDto.Missing);
        }

        var recovery = await context.Coordinator
            .InspectRecoveryAsync(cancellationToken)
            .ConfigureAwait(false);
        if (recovery.RequiresRecovery)
        {
            return LoadedState.Unavailable(game, titleId, GameplaySettingsStateDto.Conflict);
        }

        byte[] baseBytes;
        try
        {
            baseBytes = await ReadBoundedAsync(
                    baseRoot,
                    basePath,
                    MaximumMainBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnavailableFileSystemException(exception))
        {
            return LoadedState.Unavailable(game, titleId, GameplaySettingsStateDto.Incomplete);
        }

        byte[] currentBytes;
        var outputExists = File.Exists(outputPath);
        try
        {
            currentBytes = outputExists
                ? await ReadBoundedAsync(
                        outputRoot,
                        outputPath,
                        MaximumMainBytes,
                        cancellationToken)
                    .ConfigureAwait(false)
                : baseBytes;
        }
        catch (Exception exception) when (IsUnavailableFileSystemException(exception))
        {
            return LoadedState.Unavailable(game, titleId, GameplaySettingsStateDto.Corrupt);
        }

        var baseState = ComputeState(baseBytes);
        var targetState = outputExists
            ? ComputeState(currentBytes)
            : OutputFileState.Missing;
        var inventorySnapshot = await context.Coordinator
            .GetOwnershipInventorySnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        var inventory = inventorySnapshot.Inventory;
        var ownership = inventory.Files.FirstOrDefault(record => record.Path == MainPath);
        if (ownership is not null
            && (ownership.ProjectId != context.ProjectId
                || ownership.GameFamily != context.GameFamily
                || ownership.RuntimeMutableDescriptor is not null
                || !outputExists
                || ownership.CurrentState != targetState))
        {
            return LoadedState.UnavailableAfterExecutableReview(
                game,
                titleId,
                GameplaySettingsStateDto.Conflict,
                baseBytes,
                currentBytes,
                baseState,
                targetState,
                ownership,
                inventorySnapshot.Revision);
        }

        var result = AnalyzeForGame(game, baseBytes, currentBytes);
        if (result.State != GameplaySettingsStateDto.Ready || result.Analysis is null)
        {
            // A pre-existing output is safe to compose only when the exact-build
            // analyzer recognizes the complete effective image. A later reviewed
            // write records bounded, non-delete-eligible ownership; an unknown
            // image remains unmanaged and is never overwritten.
            if (outputExists && ownership is null)
            {
                // Preserve the clean-base diagnostic when the project itself is
                // unsupported or corrupt. "Unmanaged" describes only a valid
                // project whose pre-existing effective executable is unknown.
                var baseResult = AnalyzeForGame(game, baseBytes, baseBytes);
                if (baseResult.State != GameplaySettingsStateDto.Ready
                    || baseResult.Analysis is null)
                {
                    return LoadedState.UnavailableAfterExecutableReview(
                        game,
                        titleId,
                        baseResult.State,
                        baseBytes,
                        currentBytes,
                        baseState,
                        targetState,
                        ownership,
                        inventorySnapshot.Revision,
                        baseResult.Detail);
                }

                return LoadedState.UnavailableAfterExecutableReview(
                    game,
                    titleId,
                    GameplaySettingsStateDto.Unmanaged,
                    baseBytes,
                    currentBytes,
                    baseState,
                    targetState,
                    ownership,
                    inventorySnapshot.Revision,
                    result.Detail);
            }

            return LoadedState.UnavailableAfterExecutableReview(
                game,
                titleId,
                result.State,
                baseBytes,
                currentBytes,
                baseState,
                targetState,
                ownership,
                inventorySnapshot.Revision,
                result.Detail);
        }

        var dto = ToDto(game, titleId, result.Analysis, currentBytes);
        return new LoadedState(
            GameplaySettingsStateDto.Ready,
            game,
            titleId,
            baseBytes,
            currentBytes,
            baseState,
            targetState,
            ownership,
            inventorySnapshot.Revision,
            ComputeOwnershipSignature(ownership),
            result.Analysis,
            dto,
            Detail: null);
    }

    private static GameAnalysisResult AnalyzeForGame(
        ProjectGame game,
        byte[] baseBytes,
        byte[] currentBytes)
    {
        return game switch
        {
            ProjectGame.Scarlet or ProjectGame.Violet => AnalyzeScarletViolet(
                game,
                baseBytes,
                currentBytes),
            ProjectGame.Sword or ProjectGame.Shield => AnalyzeSwordShield(
                game,
                baseBytes,
                currentBytes),
            ProjectGame.ZA => AnalyzeLegendsZa(baseBytes, currentBytes),
            _ => GameAnalysisResult.Unavailable(
                GameplaySettingsStateDto.Unsupported,
                "Beta Gameplay Settings is not implemented for the selected game."),
        };
    }

    private static GameAnalysisResult AnalyzeScarletViolet(
        ProjectGame game,
        byte[] baseBytes,
        byte[] currentBytes)
    {
        var edition = game == ProjectGame.Scarlet
            ? SvGameplayRuntimeEdition.Scarlet
            : SvGameplayRuntimeEdition.Violet;
        var cleanBase = SvGameplaySettingsMainPatcher.Analyze(baseBytes, edition);
        var baseFailure = MapScarletVioletFailure(cleanBase, baseImage: true);
        if (baseFailure is not null)
        {
            return GameAnalysisResult.Unavailable(baseFailure.Value, cleanBase.Message);
        }

        if (cleanBase.Kind != SvGameplaySettingsMainKind.Vanilla
            || !cleanBase.CanonicalTextIdentityMatches)
        {
            return GameAnalysisResult.Unavailable(
                GameplaySettingsStateDto.Conflict,
                "Gameplay Settings requires the exact clean selected-edition 4.0.0 Base ExeFS main.");
        }

        var current = ReferenceEquals(baseBytes, currentBytes)
            ? cleanBase
            : SvGameplaySettingsMainPatcher.Analyze(currentBytes, edition);
        var currentFailure = MapScarletVioletFailure(current, baseImage: false);
        if (currentFailure is not null)
        {
            return GameAnalysisResult.Unavailable(currentFailure.Value, current.Message);
        }

        return GameAnalysisResult.Ready(new GameplayAnalysis(
            current.BuildId,
            current.Values,
            ToCapabilities(current.Capabilities)));
    }

    private static GameplaySettingsStateDto? MapScarletVioletFailure(
        SvGameplaySettingsMainAnalysis analysis,
        bool baseImage)
    {
        return analysis.Kind switch
        {
            SvGameplaySettingsMainKind.Vanilla or SvGameplaySettingsMainKind.Modified => null,
            SvGameplaySettingsMainKind.UnsupportedBuild when baseImage => GameplaySettingsStateDto.Unsupported,
            SvGameplaySettingsMainKind.UnsupportedBuild => GameplaySettingsStateDto.Conflict,
            SvGameplaySettingsMainKind.EditionMismatch => GameplaySettingsStateDto.Conflict,
            SvGameplaySettingsMainKind.Conflict when baseImage => GameplaySettingsStateDto.Corrupt,
            SvGameplaySettingsMainKind.Conflict => GameplaySettingsStateDto.Conflict,
            _ => GameplaySettingsStateDto.Conflict,
        };
    }

    private static GameAnalysisResult AnalyzeSwordShield(
        ProjectGame game,
        byte[] baseBytes,
        byte[] currentBytes)
    {
        var cleanBase = SwShStaticGameplaySettingsMainPatcher.Analyze(baseBytes, baseBytes, game);
        var baseFailure = MapSwordShieldFailure(cleanBase, baseImage: true);
        if (baseFailure is not null)
        {
            return GameAnalysisResult.Unavailable(baseFailure.Value, cleanBase.Message);
        }

        if (cleanBase.Kind != SwShStaticGameplaySettingsMainKind.Vanilla)
        {
            return GameAnalysisResult.Unavailable(
                GameplaySettingsStateDto.Conflict,
                "Beta Gameplay Settings requires the exact clean selected-game 1.3.2 Base ExeFS main.");
        }

        var current = ReferenceEquals(baseBytes, currentBytes)
            ? cleanBase
            : SwShStaticGameplaySettingsMainPatcher.Analyze(baseBytes, currentBytes, game);
        var currentFailure = MapSwordShieldFailure(current, baseImage: false);
        if (currentFailure is not null
            || current.ExperienceShareEnabled is null
            || current.ExperienceRateBasisPoints is null)
        {
            return GameAnalysisResult.Unavailable(
                currentFailure ?? GameplaySettingsStateDto.Conflict,
                current.Message);
        }

        var values = new GameplaySettingsValues(
            current.ExperienceShareEnabled.Value,
            current.ExperienceRateBasisPoints.Value,
            current.LevelCapEnabled,
            current.LevelCap);
        return GameAnalysisResult.Ready(new GameplayAnalysis(
            current.BuildId,
            values,
            ToCapabilities(current.Features)));
    }

    private static GameplaySettingsStateDto? MapSwordShieldFailure(
        SwShStaticGameplaySettingsMainAnalysis analysis,
        bool baseImage)
    {
        return analysis.Kind switch
        {
            SwShStaticGameplaySettingsMainKind.Vanilla or
                SwShStaticGameplaySettingsMainKind.Configured => null,
            SwShStaticGameplaySettingsMainKind.UnsupportedBuild when baseImage =>
                GameplaySettingsStateDto.Unsupported,
            SwShStaticGameplaySettingsMainKind.UnsupportedBuild or
                SwShStaticGameplaySettingsMainKind.GameMismatch => GameplaySettingsStateDto.Conflict,
            SwShStaticGameplaySettingsMainKind.Conflict when baseImage =>
                GameplaySettingsStateDto.Corrupt,
            SwShStaticGameplaySettingsMainKind.Conflict => GameplaySettingsStateDto.Conflict,
            _ => GameplaySettingsStateDto.Conflict,
        };
    }

    private static GameAnalysisResult AnalyzeLegendsZa(
        byte[] baseBytes,
        byte[] currentBytes)
    {
        var cleanBase = ZaStaticGameplaySettingsMainPatcher.Analyze(
            baseBytes,
            baseBytes,
            ProjectGame.ZA);
        var baseFailure = MapLegendsZaFailure(cleanBase, baseImage: true);
        if (baseFailure is not null)
        {
            return GameAnalysisResult.Unavailable(baseFailure.Value, cleanBase.Message);
        }

        if (cleanBase.Kind != ZaStaticGameplaySettingsMainKind.Vanilla)
        {
            return GameAnalysisResult.Unavailable(
                GameplaySettingsStateDto.Conflict,
                "Beta Gameplay Settings requires the exact clean Z-A 2.0.2 Base ExeFS main.");
        }

        var current = ReferenceEquals(baseBytes, currentBytes)
            ? cleanBase
            : ZaStaticGameplaySettingsMainPatcher.Analyze(
                baseBytes,
                currentBytes,
                ProjectGame.ZA);
        var currentFailure = MapLegendsZaFailure(current, baseImage: false);
        if (currentFailure is not null
            || current.ExperienceShareEnabled is null
            || current.ExperienceRateBasisPoints is null)
        {
            return GameAnalysisResult.Unavailable(
                currentFailure ?? GameplaySettingsStateDto.Conflict,
                current.Message);
        }

        var values = new GameplaySettingsValues(
            current.ExperienceShareEnabled.Value,
            current.ExperienceRateBasisPoints.Value,
            current.LevelCapEnabled,
            current.LevelCap);
        return GameAnalysisResult.Ready(new GameplayAnalysis(
            current.BuildId,
            values,
            ToCapabilities(current.Features)));
    }

    private static GameplaySettingsStateDto? MapLegendsZaFailure(
        ZaStaticGameplaySettingsMainAnalysis analysis,
        bool baseImage)
    {
        return analysis.Kind switch
        {
            ZaStaticGameplaySettingsMainKind.Vanilla or
                ZaStaticGameplaySettingsMainKind.Configured => null,
            ZaStaticGameplaySettingsMainKind.UnsupportedBuild when baseImage =>
                GameplaySettingsStateDto.Unsupported,
            ZaStaticGameplaySettingsMainKind.UnsupportedBuild or
                ZaStaticGameplaySettingsMainKind.GameMismatch => GameplaySettingsStateDto.Conflict,
            ZaStaticGameplaySettingsMainKind.Conflict when baseImage =>
                GameplaySettingsStateDto.Corrupt,
            ZaStaticGameplaySettingsMainKind.Conflict => GameplaySettingsStateDto.Conflict,
            _ => GameplaySettingsStateDto.Conflict,
        };
    }

    private static byte[] ApplyForGame(
        ProjectGame game,
        byte[] baseBytes,
        byte[] currentBytes,
        GameplaySettingsValues values)
    {
        if (values == GameplaySettingsValues.Vanilla)
        {
            return game switch
            {
                ProjectGame.Scarlet => SvGameplaySettingsMainPatcher.RestoreFromBase(
                    currentBytes,
                    baseBytes,
                    SvGameplayRuntimeEdition.Scarlet),
                ProjectGame.Violet => SvGameplaySettingsMainPatcher.RestoreFromBase(
                    currentBytes,
                    baseBytes,
                    SvGameplayRuntimeEdition.Violet),
                ProjectGame.Sword or ProjectGame.Shield =>
                    SwShStaticGameplaySettingsMainPatcher.RestoreFromBase(
                        baseBytes,
                        currentBytes,
                        game),
                ProjectGame.ZA => ZaStaticGameplaySettingsMainPatcher.RestoreFromBase(
                    baseBytes,
                    currentBytes,
                    ProjectGame.ZA),
                _ => throw new InvalidDataException(
                    "Beta Gameplay Settings does not support the selected game build."),
            };
        }

        return game switch
        {
            ProjectGame.Scarlet => SvGameplaySettingsMainPatcher.Apply(
                currentBytes,
                SvGameplayRuntimeEdition.Scarlet,
                values),
            ProjectGame.Violet => SvGameplaySettingsMainPatcher.Apply(
                currentBytes,
                SvGameplayRuntimeEdition.Violet,
                values),
            ProjectGame.Sword or ProjectGame.Shield =>
                SwShStaticGameplaySettingsMainPatcher.Apply(
                    baseBytes,
                    currentBytes,
                    new SwShStaticGameplaySettingsRequest(
                        values.ExperienceShareEnabled,
                        values.ExperienceRateBasisPoints,
                        values.LevelCapEnabled,
                        values.LevelCap),
                    game),
            ProjectGame.ZA => ZaStaticGameplaySettingsMainPatcher.Apply(
                baseBytes,
                currentBytes,
                new ZaStaticGameplaySettingsRequest(
                    values.ExperienceShareEnabled,
                    values.ExperienceRateBasisPoints,
                    values.LevelCapEnabled,
                    values.LevelCap),
                ProjectGame.ZA),
            _ => throw new InvalidDataException(
                "Beta Gameplay Settings does not support the selected game build."),
        };
    }

    private static void EnsureRequestedCapabilities(
        GameplayAnalysis analysis,
        GameplaySettingsValues requested)
    {
        EnsureCapabilityChange(
            analysis.Capabilities.ExperienceShare,
            analysis.Values.ExperienceShareEnabled != requested.ExperienceShareEnabled,
            "EXP Share");
        EnsureCapabilityChange(
            analysis.Capabilities.ExperienceRate,
            analysis.Values.ExperienceRateBasisPoints != requested.ExperienceRateBasisPoints,
            "EXP rate");
        EnsureCapabilityChange(
            analysis.Capabilities.LevelCap,
            analysis.Values.LevelCapEnabled != requested.LevelCapEnabled
                || analysis.Values.LevelCap != requested.LevelCap,
            "level cap");
    }

    private static void EnsureCapabilityChange(
        GameplaySettingCapabilityDto capability,
        bool changed,
        string label)
    {
        if (changed && !capability.Available)
        {
            throw new GameplaySettingsUnavailableException(
                GameplaySettingsStateDto.Unsupported,
                $"{label} is unavailable for this exact game build ({capability.ReasonCode}).");
        }
    }

    private static GameplayCapabilitySet ToCapabilities(
        IReadOnlyList<SvGameplaySettingsStaticCapability> capabilities)
    {
        return new GameplayCapabilitySet(
            ToCapability(capabilities.Single(item =>
                item.Field == SvGameplaySettingsStaticField.ExperienceShare)),
            ToCapability(capabilities.Single(item =>
                item.Field == SvGameplaySettingsStaticField.ExperienceRate)),
            ToCapability(capabilities.Single(item =>
                item.Field == SvGameplaySettingsStaticField.LevelCap)));
    }

    private static GameplaySettingCapabilityDto ToCapability(
        SvGameplaySettingsStaticCapability capability)
    {
        return new GameplaySettingCapabilityDto(
            capability.Available,
            capability.ReasonCode,
            capability.Field switch
            {
                SvGameplaySettingsStaticField.ExperienceShare =>
                    "sv-exp-share-normal-battle-nonparticipants",
                SvGameplaySettingsStaticField.ExperienceRate =>
                    "sv-exp-rate-normal-battle-calculator",
                SvGameplaySettingsStaticField.LevelCap =>
                    capability.Available
                        ? "sv-level-cap-normal-battle-award"
                        : "comprehensive-level-cap-unavailable",
                _ => throw new ArgumentOutOfRangeException(nameof(capability)),
            });
    }

    private static GameplayCapabilitySet ToCapabilities(
        IReadOnlyList<SwShStaticGameplaySettingsFeatureAssessment> capabilities)
    {
        return new GameplayCapabilitySet(
            ToCapability(capabilities.Single(item =>
                item.Feature == SwShStaticGameplaySettingsFeature.ExperienceShare)),
            ToCapability(capabilities.Single(item =>
                item.Feature == SwShStaticGameplaySettingsFeature.ExperienceRate)),
            ToCapability(capabilities.Single(item =>
                item.Feature == SwShStaticGameplaySettingsFeature.LevelCap)));
    }

    private static GameplaySettingCapabilityDto ToCapability(
        SwShStaticGameplaySettingsFeatureAssessment capability)
    {
        return new GameplaySettingCapabilityDto(
            capability.Available,
            capability.Available
                ? "available-static-main-patch"
                : "unavailable-incomplete-recipient-and-source-contracts",
            capability.Feature switch
            {
                SwShStaticGameplaySettingsFeature.ExperienceShare =>
                    "swsh-exp-share-battle-catch-decision",
                SwShStaticGameplaySettingsFeature.ExperienceRate =>
                    "swsh-exp-rate-battle-catch-final-award",
                SwShStaticGameplaySettingsFeature.LevelCap =>
                    capability.Available
                        ? "swsh-level-cap-battle-catch-final-award"
                        : "comprehensive-level-cap-unavailable",
                _ => throw new ArgumentOutOfRangeException(nameof(capability)),
            });
    }

    private static GameplayCapabilitySet ToCapabilities(
        IReadOnlyList<ZaStaticGameplaySettingsFeatureAssessment> capabilities)
    {
        return new GameplayCapabilitySet(
            ToCapability(capabilities.Single(item =>
                item.Feature == ZaStaticGameplaySettingsFeature.ExperienceShare)),
            ToCapability(capabilities.Single(item =>
                item.Feature == ZaStaticGameplaySettingsFeature.ExperienceRate)),
            ToCapability(capabilities.Single(item =>
                item.Feature == ZaStaticGameplaySettingsFeature.LevelCap)));
    }

    private static GameplaySettingCapabilityDto ToCapability(
        ZaStaticGameplaySettingsFeatureAssessment capability)
    {
        return new GameplaySettingCapabilityDto(
            capability.Available,
            capability.Available
                ? "available-static-main-patch"
                : "unavailable-incomplete-recipient-and-source-contracts",
            capability.Feature switch
            {
                ZaStaticGameplaySettingsFeature.ExperienceShare =>
                    "za-exp-share-two-battle-award-builders",
                ZaStaticGameplaySettingsFeature.ExperienceRate =>
                    "za-exp-rate-two-battle-award-paths",
                ZaStaticGameplaySettingsFeature.LevelCap =>
                    capability.Available
                        ? "za-level-cap-two-battle-award-paths"
                        : "comprehensive-level-cap-unavailable",
                _ => throw new ArgumentOutOfRangeException(nameof(capability)),
            });
    }

    private static GameplaySettingsSnapshotDto ToDto(
        ProjectGame game,
        ulong titleId,
        GameplayAnalysis analysis,
        ReadOnlySpan<byte> currentBytes)
    {
        return new GameplaySettingsSnapshotDto(
            titleId.ToString("X16", CultureInfo.InvariantCulture),
            ComputeExecutableProfileId(game, analysis.BuildId),
            GetSupportedGameVersion(game),
            ComputeGeneration(currentBytes).ToString(CultureInfo.InvariantCulture),
            analysis.Capabilities.ExperienceShare.Available,
            analysis.Capabilities.ExperienceRate.Available,
            analysis.Capabilities.LevelCap.Available,
            analysis.Capabilities.ExperienceShare,
            analysis.Capabilities.ExperienceRate,
            analysis.Capabilities.LevelCap,
            new GameplaySettingsValuesDto(
                analysis.Values.ExperienceShareEnabled,
                analysis.Values.ExperienceRateBasisPoints,
                analysis.Values.LevelCapEnabled,
                analysis.Values.LevelCap));
    }

    private static OwnedTarget CreateRecordOwnershipClaim(GameFamily family, ulong titleId)
    {
        var record = new SemanticRecordRef(
            family,
            new SemanticDomainKey(WorkflowId),
            new SemanticRecordKind("static-exefs-settings", schemaVersion: 1),
            new SemanticRecordId(titleId.ToString("X16", CultureInfo.InvariantCulture)));
        return new OwnedTarget(
            family,
            new OwnedTargetAddress(MainPath, archiveMember: null, record),
            OwnerId,
            PreservationRule);
    }

    private static OwnedTarget CreateFileOwnershipClaim(GameFamily family)
    {
        return new OwnedTarget(
            family,
            new OwnedTargetAddress(MainPath),
            OwnerId,
            PreservationRule);
    }

    private static string GetDefaultOutputMode(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword or ProjectGame.Shield => "sword-shield-layered-output",
            ProjectGame.Scarlet or ProjectGame.Violet => "sv.standalone",
            ProjectGame.ZA => "za.standalone",
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    private static string GetSupportedGameVersion(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword or ProjectGame.Shield => "1.3.2",
            ProjectGame.Scarlet or ProjectGame.Violet => "4.0.0",
            ProjectGame.ZA => "2.0.2",
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    private static string ComputeExecutableProfileId(ProjectGame game, string buildId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"gameplay-settings-static-v2\n{game}\n{buildId}"));
        return Convert.ToHexString(bytes.AsSpan(0, 16));
    }

    private static ulong ComputeGeneration(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(bytes);
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }

    private static string ComputeOwnershipSignature(OutputOwnershipRecord? ownership)
    {
        if (ownership is null)
        {
            return "missing";
        }

        var builder = new StringBuilder();
        builder.Append("gameplay-settings-ownership-v1\n")
            .Append(ownership.Path.CanonicalKey).Append('\n')
            .Append(ownership.CurrentState.Sha256).Append('\n')
            .Append(ownership.CurrentState.LengthBytes.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(ownership.ProjectId.Value).Append('\n')
            .Append((int)ownership.GameFamily).Append('\n')
            .Append(ownership.OutputMode).Append('\n')
            .Append(ownership.FileDeleteEligible ? '1' : '0').Append('\n')
            .Append(ownership.RuntimeMutableDescriptor is null ? "static" : "runtime").Append('\n');
        foreach (var claim in ownership.Claims
                     .OrderBy(ToOwnershipToken, StringComparer.Ordinal))
        {
            builder.Append(ToOwnershipToken(claim)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string ToOwnershipToken(OwnedTarget claim)
    {
        var address = claim.Address;
        var record = address.Record;
        var range = address.ByteRange;
        return string.Join(
            '|',
            (int)claim.GameFamily,
            address.File.CanonicalKey,
            (int)address.ScopeKind,
            address.ArchiveMember?.Value ?? string.Empty,
            record?.Domain.Value ?? string.Empty,
            record?.RecordKind.Key ?? string.Empty,
            record?.RecordKind.SchemaVersion.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record?.RecordId.Value ?? string.Empty,
            record?.SubrecordId?.Value ?? string.Empty,
            range?.Offset.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            range?.Length.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            claim.OwnerId.Value,
            claim.PreservationRule.Key,
            claim.PreservationRule.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            claim.PreservationRule.PreservesUnownedData ? "1" : "0",
            claim.PreservationRule.RequiresPreimage ? "1" : "0");
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

    private static string ToAbsolutePath(string root, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(normalizedRoot, fullPath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException("The gameplay settings path escapes its selected root.");
        }

        return fullPath;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string rootPath,
        string filePath,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        if (!HasSafePhysicalFileChain(rootPath, filePath))
        {
            throw new IOException("The gameplay settings source is not a safe regular file.");
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > maximumLength)
        {
            throw new IOException("The gameplay settings executable has an invalid size.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!HasSafePhysicalFileChain(rootPath, filePath))
        {
            throw new IOException("The gameplay settings source changed during review.");
        }

        return bytes;
    }

    private static async Task<FileStream> OpenVerifiedBaseLeaseAsync(
        OutputScopeContext context,
        OutputFileState expectedState,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Paths.BaseExeFsPath)
            || !Path.IsPathFullyQualified(context.Paths.BaseExeFsPath))
        {
            throw new IOException("The reviewed base ExeFS root is unavailable.");
        }

        var baseRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(context.Paths.BaseExeFsPath));
        var basePath = ToAbsolutePath(baseRoot, "main");
        if (!HasSafePhysicalFileChain(baseRoot, basePath))
        {
            throw new IOException("The reviewed base executable is not a safe regular file.");
        }

        var stream = new FileStream(
            basePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length <= 0 || stream.Length > MaximumMainBytes)
            {
                throw new IOException("The reviewed base executable has an invalid size.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            var leasedState = OutputFileState.Existing(
                Convert.ToHexStringLower(hash.GetHashAndReset()),
                stream.Length);
            if (leasedState != expectedState || !HasSafePhysicalFileChain(baseRoot, basePath))
            {
                throw new IOException("The reviewed base executable changed before apply.");
            }

            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static bool HasSafePhysicalFileChain(string rootPath, string filePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedFile = Path.GetFullPath(filePath);
        if (!Directory.Exists(normalizedRoot))
        {
            return false;
        }

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

    private sealed record GameplayCapabilitySet(
        GameplaySettingCapabilityDto ExperienceShare,
        GameplaySettingCapabilityDto ExperienceRate,
        GameplaySettingCapabilityDto LevelCap);

    private sealed record GameplayAnalysis(
        string BuildId,
        GameplaySettingsValues Values,
        GameplayCapabilitySet Capabilities);

    private sealed record GameAnalysisResult(
        GameplaySettingsStateDto State,
        GameplayAnalysis? Analysis,
        string? Detail)
    {
        public static GameAnalysisResult Ready(GameplayAnalysis analysis) =>
            new(GameplaySettingsStateDto.Ready, analysis, Detail: null);

        public static GameAnalysisResult Unavailable(
            GameplaySettingsStateDto state,
            string? detail = null) =>
            new(state, null, detail);
    }

    private sealed record CachedReview(
        string ReviewId,
        string ScopeKey,
        DateTimeOffset ExpiresAtUtc,
        string Generation,
        OutputFileState BaseState,
        string OwnershipSignature,
        OutputApplyPlan ApplyPlan);

    private sealed record LoadedState(
        GameplaySettingsStateDto State,
        ProjectGame Game,
        ulong TitleId,
        byte[]? BaseBytes,
        byte[]? CurrentBytes,
        OutputFileState? BaseState,
        OutputFileState? TargetState,
        OutputOwnershipRecord? Ownership,
        OutputStateRevision? OwnershipInventoryRevision,
        string OwnershipSignature,
        GameplayAnalysis? Analysis,
        GameplaySettingsSnapshotDto? Dto,
        string? Detail)
    {
        public static LoadedState Unavailable(
            ProjectGame game,
            ulong titleId,
            GameplaySettingsStateDto state,
            string? detail = null)
        {
            return new LoadedState(
                state,
                game,
                titleId,
                null,
                null,
                null,
                null,
                null,
                null,
                "unavailable",
                null,
                null,
                detail);
        }

        public static LoadedState UnavailableAfterExecutableReview(
            ProjectGame game,
            ulong titleId,
            GameplaySettingsStateDto state,
            byte[] baseBytes,
            byte[] currentBytes,
            OutputFileState baseState,
            OutputFileState targetState,
            OutputOwnershipRecord? ownership,
            OutputStateRevision ownershipInventoryRevision,
            string? detail = null)
        {
            return new LoadedState(
                state,
                game,
                titleId,
                baseBytes,
                currentBytes,
                baseState,
                targetState,
                ownership,
                ownershipInventoryRevision,
                ComputeOwnershipSignature(ownership),
                null,
                null,
                detail);
        }
    }
}

public sealed class GameplaySettingsUnavailableException : Exception
{
    public GameplaySettingsUnavailableException(
        GameplaySettingsStateDto state,
        string? detail = null)
        : base(detail is null
            ? "Beta Gameplay Settings is not available for editing in its current state."
            : $"Beta Gameplay Settings is not available for editing. {detail}")
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
