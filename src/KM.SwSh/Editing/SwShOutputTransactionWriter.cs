// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Editing;

internal static class SwShOutputTransactionWriter
{
    internal const int MaximumFilesPerTransaction = OutputLimits.StandardMaximumMutationsPerApply;
    internal const int MaximumFpsPatchFilesPerTransaction = OutputLimits.MaximumMutationsPerApply;

    private const string OutputMode = "sword-shield-layered-output";
    private const string OutputOwner = "sword-shield-verified-editor";
    private const string PreservationRule = "verified-whole-file-postimage";
    private const string ComposedExeFsMainPath = "exefs/main";

    private static readonly OutputTransactionCoordinatorOptions CoordinatorOptions = new()
    {
        MaximumMutationsPerApply = OutputLimits.StandardMaximumMutationsPerApply,
        MaximumWriteBytesPerMutation = OutputLimits.MaximumWriteBytesPerMutation,
        MaximumWriteBytesPerApply = OutputLimits.MaximumWriteBytesPerApply,
        MaximumFingerprintFileBytes = OutputLimits.MaximumFingerprintFileBytes,
        MaximumBackupBytesPerApply = OutputLimits.MaximumBackupBytesPerApply,
    };

    private static readonly OutputTransactionCoordinatorOptions FpsPatchCoordinatorOptions = new()
    {
        MaximumMutationsPerApply = OutputLimits.MaximumMutationsPerApply,
        MaximumWriteBytesPerMutation = OutputLimits.MaximumWriteBytesPerMutation,
        MaximumWriteBytesPerApply = OutputLimits.MaximumWriteBytesPerApply,
        MaximumFingerprintFileBytes = OutputLimits.MaximumFingerprintFileBytes,
        MaximumBackupBytesPerApply = OutputLimits.MaximumBackupBytesPerApply,
    };

    public static bool TryCapturePreimage(
        ProjectPaths paths,
        string relativePath,
        out OutputFileState? state,
        out SwShOutputTransactionFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(paths);

        state = null;
        failure = null;
        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(
                paths,
                out var stablePaths,
                out var stableRootFailure)
            || string.IsNullOrWhiteSpace(stablePaths.OutputRootPath))
        {
            failure = new SwShOutputTransactionFailure(
                string.Empty,
                stableRootFailure ?? "Output Root is not configured.");
            return false;
        }

        try
        {
            var path = new RelativeOutputPath(relativePath);
            var targetPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
                stablePaths.OutputRootPath,
                path.Value);
            if (targetPath is null || Directory.Exists(targetPath))
            {
                failure = new SwShOutputTransactionFailure(
                    path.Value,
                    "The output target is not a physical file path inside Output Root.");
                return false;
            }

            state = CaptureState(targetPath);
            return true;
        }
        catch (Exception exception) when (exception is
            OutputCoordinatorException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException)
        {
            failure = new SwShOutputTransactionFailure(relativePath, exception.Message);
            return false;
        }
    }

    public static bool TryApply(
        ProjectPaths paths,
        IEnumerable<SwShOutputFileMutation> requestedMutations,
        string operationId,
        out OutputApplyResult? result,
        out SwShOutputTransactionFailure? failure)
    {
        return TryApply(
            paths,
            requestedMutations,
            operationId,
            MaximumFilesPerTransaction,
            CoordinatorOptions,
            out result,
            out failure);
    }

    internal static bool TryApplyFpsPatchBatch(
        ProjectPaths paths,
        IEnumerable<SwShOutputFileMutation> requestedMutations,
        string operationId,
        out OutputApplyResult? result,
        out SwShOutputTransactionFailure? failure)
    {
        return TryApply(
            paths,
            requestedMutations,
            operationId,
            MaximumFpsPatchFilesPerTransaction,
            FpsPatchCoordinatorOptions,
            out result,
            out failure);
    }

    private static bool TryApply(
        ProjectPaths paths,
        IEnumerable<SwShOutputFileMutation> requestedMutations,
        string operationId,
        int maximumFilesPerTransaction,
        OutputTransactionCoordinatorOptions coordinatorOptions,
        out OutputApplyResult? result,
        out SwShOutputTransactionFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(requestedMutations);

        result = null;
        failure = null;
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            failure = new SwShOutputTransactionFailure(
                string.Empty,
                "The output transaction requires an exact Sword or Shield project identity.");
            return false;
        }

        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(
                paths,
                out var stablePaths,
                out var stableRootFailure)
            || string.IsNullOrWhiteSpace(stablePaths.OutputRootPath))
        {
            failure = new SwShOutputTransactionFailure(
                string.Empty,
                stableRootFailure ?? "Output Root is not configured.");
            return false;
        }

        try
        {
            var projectId = ProjectIdentity.FromPaths(stablePaths);
            var materialized = requestedMutations
                .Take(maximumFilesPerTransaction + 1)
                .ToArray();
            if (materialized.Length > maximumFilesPerTransaction)
            {
                failure = new SwShOutputTransactionFailure(
                    string.Empty,
                    $"The output transaction cannot contain more than {maximumFilesPerTransaction} files.");
                return false;
            }

            var requiresVerifiedBaseMain = materialized.Any(requested =>
                requested is
                {
                    Kind: OutputMutationKind.Delete,
                    ComposesEffectivePreimage: true,
                }
                && (requested.AllowLegacyAdoption
                    || requested.DeleteFallbackContents is not null));
            using var baseMainLease = requiresVerifiedBaseMain
                ? OpenVerifiedBaseMainLease(stablePaths)
                : null;
            var verifiedBaseMain = baseMainLease is null
                ? null
                : ReadBoundedContents(baseMainLease, "Base ExeFS main");

            var coordinator = OutputTransactionCoordinator.ForProject(
                stablePaths,
                coordinatorOptions);
            var hasComposedMutation = materialized.Any(requested =>
                requested?.ComposesEffectivePreimage == true);
            var ownershipSnapshot = hasComposedMutation
                ? coordinator.GetOwnershipInventorySnapshotAsync()
                    .GetAwaiter()
                    .GetResult()
                : null;
            var ownershipByPath = ownershipSnapshot?.Inventory.Files.ToDictionary(
                record => record.Path.CanonicalKey,
                StringComparer.Ordinal);
            var mutations = new List<OutputMutation>(materialized.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var requested in materialized)
            {
                if (requested is null)
                {
                    failure = new SwShOutputTransactionFailure(
                        string.Empty,
                        "The output transaction contains an invalid file mutation.");
                    return false;
                }

                var relativePath = new RelativeOutputPath(requested.RelativePath);
                if (requested.ComposesEffectivePreimage
                    && !string.Equals(
                        relativePath.CanonicalKey,
                        new RelativeOutputPath(ComposedExeFsMainPath).CanonicalKey,
                        StringComparison.Ordinal))
                {
                    failure = new SwShOutputTransactionFailure(
                        relativePath.Value,
                        "Composed output writes are restricted to the verified effective exefs/main target.");
                    return false;
                }

                if (!seen.Add(relativePath.CanonicalKey))
                {
                    failure = new SwShOutputTransactionFailure(
                        relativePath.Value,
                        "The output transaction contains the same target more than once.");
                    return false;
                }

                var targetPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
                    stablePaths.OutputRootPath,
                    relativePath.Value);
                if (targetPath is null || Directory.Exists(targetPath))
                {
                    failure = new SwShOutputTransactionFailure(
                        relativePath.Value,
                        "The output target is not a physical file path inside Output Root.");
                    return false;
                }

                var observedPreimage = CaptureState(targetPath);
                if (requested.ReviewedPreimage is { } reviewedPreimage
                    && reviewedPreimage != observedPreimage)
                {
                    failure = new SwShOutputTransactionFailure(
                        relativePath.Value,
                        "The output target changed after it was prepared for writing.");
                    return false;
                }

                var expectedPreimage = requested.ReviewedPreimage ?? observedPreimage;
                var ownership = new OwnedTarget(
                    GameFamily.SwordShield,
                    new OwnedTargetAddress(relativePath),
                    new OwnershipOwnerId(OutputOwner),
                    new PreservationRuleDescriptor(
                        PreservationRule,
                        schemaVersion: 1,
                        preservesUnownedData: true,
                        requiresPreimage: true));
                OutputOwnershipRecord? existingOwnership = null;
                IReadOnlyCollection<OwnedTarget> ownershipClaims = [ownership];
                if (requested.ComposesEffectivePreimage
                    && ownershipByPath is not null
                    && ownershipByPath.TryGetValue(relativePath.CanonicalKey, out existingOwnership))
                {
                    if (existingOwnership.ProjectId != projectId
                        || existingOwnership.GameFamily != GameFamily.SwordShield
                        || !string.Equals(existingOwnership.OutputMode, OutputMode, StringComparison.Ordinal))
                    {
                        failure = new SwShOutputTransactionFailure(
                            relativePath.Value,
                            "The composed output target is owned by a different project or output scope.");
                        return false;
                    }

                    if (existingOwnership.CurrentState != expectedPreimage)
                    {
                        failure = new SwShOutputTransactionFailure(
                            relativePath.Value,
                            "The composed output ownership record does not match the reviewed effective preimage.");
                        return false;
                    }

                    ownershipClaims = existingOwnership.Claims
                        .Append(ownership)
                        .Distinct()
                        .ToArray();
                }

                if (requested.Kind == OutputMutationKind.Delete)
                {
                    if (expectedPreimage.Exists)
                    {
                        if (requested.ComposesEffectivePreimage
                            && existingOwnership is not null)
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
                                    relativePath,
                                    expectedPreimage,
                                    existingOwnership.Claims));
                            }
                            else if (activeForeignClaims.Length == 0
                                     && foreignClaims.Length > 0
                                     && foreignClaims.All(OutputCreatorProvenance.IsClaim)
                                     && existingOwnership.FileDeleteEligible
                                     && requested.DeleteFallbackContents is { } verifiedFallback
                                     && verifiedBaseMain is not null
                                     && verifiedFallback.AsSpan().SequenceEqual(verifiedBaseMain))
                            {
                                var baseState = OutputFileState.Existing(
                                    Convert.ToHexStringLower(SHA256.HashData(verifiedBaseMain)),
                                    verifiedBaseMain.LongLength);
                                var authority = new OutputVerifiedBaseDeleteAuthority(
                                    projectId,
                                    GameFamily.SwordShield,
                                    ownership.OwnerId,
                                    OutputMode,
                                    relativePath,
                                    expectedPreimage,
                                    baseState,
                                    existingOwnership.Claims);
                                mutations.Add(OutputMutation.DeleteVerifiedBase(
                                    relativePath,
                                    expectedPreimage,
                                    existingOwnership.Claims,
                                    authority));
                            }
                            else
                            {
                                if (requested.DeleteFallbackContents is null)
                                {
                                    failure = new SwShOutputTransactionFailure(
                                        relativePath.Value,
                                        "A composed delete requires exact restored fallback contents when the file has retained ownership claims.");
                                    return false;
                                }

                                var retainedClaims = foreignClaims.Length > 0
                                    ? RetainCreatorProvenanceWhenRequired(
                                        existingOwnership,
                                        foreignClaims,
                                        ownership)
                                    : existingOwnership.Claims;
                                var fallbackState = OutputFileState.Existing(
                                    Convert.ToHexStringLower(SHA256.HashData(requested.DeleteFallbackContents)),
                                    requested.DeleteFallbackContents.LongLength);
                                if (fallbackState != expectedPreimage)
                                {
                                    mutations.Add(OutputMutation.Write(
                                        relativePath,
                                        requested.DeleteFallbackContents,
                                        expectedPreimage,
                                        retainedClaims,
                                        ownershipActor: ownership.OwnerId));
                                }
                            }
                        }
                        else if (requested.AllowLegacyAdoption)
                        {
                            if (requested.ComposesEffectivePreimage
                                && (requested.DeleteFallbackContents is null
                                    || verifiedBaseMain is null
                                    || !SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(
                                        requested.DeleteFallbackContents,
                                        verifiedBaseMain)))
                            {
                                failure = new SwShOutputTransactionFailure(
                                    relativePath.Value,
                                    "An unmanaged composed exefs/main can be deleted only after its restored candidate matches the held Base ExeFS main.");
                                return false;
                            }

                            var adoptionAuthority = new OutputLegacyAdoptionDeleteAuthority(
                                projectId,
                                GameFamily.SwordShield,
                                OutputMode,
                                relativePath,
                                ownership.OwnerId,
                                ownership.PreservationRule,
                                expectedPreimage);
                            mutations.Add(OutputMutation.DeleteLegacyAdoption(
                                relativePath,
                                expectedPreimage,
                                [ownership],
                                adoptionAuthority));
                        }
                        else
                        {
                            mutations.Add(OutputMutation.Delete(
                                relativePath,
                                expectedPreimage,
                                [ownership]));
                        }
                    }

                    continue;
                }

                if (requested.Contents is null)
                {
                    failure = new SwShOutputTransactionFailure(
                        relativePath.Value,
                        "An output write does not include file contents.");
                    return false;
                }

                var postimageState = OutputFileState.Existing(
                    Convert.ToHexStringLower(SHA256.HashData(requested.Contents)),
                    requested.Contents.LongLength);
                if (postimageState != expectedPreimage)
                {
                    mutations.Add(OutputMutation.Write(
                        relativePath,
                        requested.Contents,
                        expectedPreimage,
                        ownershipClaims,
                        ownershipActor: requested.ComposesEffectivePreimage
                            ? ownership.OwnerId
                            : null));
                }
            }

            if (mutations.Count == 0)
            {
                return true;
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
                .Append(new OutputApplyOrigin(OutputApplyOriginKind.Workflow, operationId))
                .Distinct()
                .ToArray();
            var plan = new OutputApplyPlan(
                projectId,
                GameFamily.SwordShield,
                OutputMode,
                OutputReviewFingerprint.FromMutations(mutations),
                origins,
                mutations,
                ownershipInventoryRevision: ownershipSnapshot?.Revision);
            result = coordinator
                .ApplyAsync(plan)
                .GetAwaiter()
                .GetResult();
            if (result.Outcome == OutputApplyOutcome.Committed)
            {
                return true;
            }

            failure = new SwShOutputTransactionFailure(
                string.Empty,
                result.Outcome == OutputApplyOutcome.RecoveryRequired
                    ? "Output recovery is required before another write can begin."
                    : "The output transaction did not commit and was rolled back.",
                result.Receipt.OutcomeCode,
                RecoveryRequired: result.Outcome == OutputApplyOutcome.RecoveryRequired);
            return false;
        }
        catch (OutputRecoveryRequiredException exception)
        {
            failure = new SwShOutputTransactionFailure(
                string.Empty,
                exception.Message,
                RecoveryRequired: true);
            return false;
        }
        catch (Exception exception) when (exception is
            OutputCoordinatorException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            failure = new SwShOutputTransactionFailure(string.Empty, exception.Message);
            return false;
        }
    }

    private static OutputFileState CaptureState(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return OutputFileState.Missing;
        }

        using var stream = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length > CoordinatorOptions.MaximumFingerprintFileBytes)
        {
            throw new OutputLimitExceededException(
                "An output preimage exceeds the configured fingerprint limit.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return OutputFileState.Existing(
            Convert.ToHexStringLower(hash.GetHashAndReset()),
            stream.Length);
    }

    private static FileStream OpenVerifiedBaseMainLease(ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            throw new IOException(
                "Base ExeFS is required to verify an unmanaged composed exefs/main restoration.");
        }

        var baseMainPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            paths.BaseExeFsPath,
            "main");
        if (baseMainPath is null || Directory.Exists(baseMainPath) || !File.Exists(baseMainPath))
        {
            throw new IOException(
                "Base ExeFS main is not a safe physical file inside the configured base root.");
        }

        return new FileStream(
            baseMainPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
    }

    private static byte[] ReadBoundedContents(FileStream stream, string label)
    {
        if (stream.Length <= 0
            || stream.Length > CoordinatorOptions.MaximumWriteBytesPerMutation
            || stream.Length > int.MaxValue)
        {
            throw new OutputLimitExceededException($"{label} exceeds the configured file limit.");
        }

        var contents = new byte[checked((int)stream.Length)];
        stream.Position = 0;
        stream.ReadExactly(contents);
        stream.Position = 0;
        return contents;
    }

    private static IReadOnlyCollection<OwnedTarget> RetainCreatorProvenanceWhenRequired(
        OutputOwnershipRecord existingOwnership,
        IReadOnlyCollection<OwnedTarget> foreignClaims,
        OwnedTarget ownership)
    {
        if (!existingOwnership.FileDeleteEligible
            || foreignClaims.Any(claim => claim.Address.ScopeKind == OwnedTargetScopeKind.File))
        {
            return foreignClaims;
        }

        return foreignClaims
            .Append(OutputCreatorProvenance.Create(
                GameFamily.SwordShield,
                ownership.Address.File))
            .Distinct()
            .ToArray();
    }
}

internal sealed record SwShOutputFileMutation(
    OutputMutationKind Kind,
    string RelativePath,
    byte[]? Contents,
    OutputFileState? ReviewedPreimage = null,
    bool AllowLegacyAdoption = false,
    bool ComposesEffectivePreimage = false,
    byte[]? DeleteFallbackContents = null)
{
    public static SwShOutputFileMutation Write(string relativePath, byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return new SwShOutputFileMutation(OutputMutationKind.Write, relativePath, contents);
    }

    public static SwShOutputFileMutation Write(
        string relativePath,
        byte[] contents,
        OutputFileState reviewedPreimage)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(reviewedPreimage);
        return new SwShOutputFileMutation(
            OutputMutationKind.Write,
            relativePath,
            contents,
            reviewedPreimage);
    }

    public static SwShOutputFileMutation WriteComposed(
        string relativePath,
        byte[] contents,
        OutputFileState reviewedPreimage)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(reviewedPreimage);
        return new SwShOutputFileMutation(
            OutputMutationKind.Write,
            relativePath,
            contents,
            reviewedPreimage,
            ComposesEffectivePreimage: true);
    }

    public static SwShOutputFileMutation Delete(string relativePath)
    {
        return new SwShOutputFileMutation(OutputMutationKind.Delete, relativePath, Contents: null);
    }

    public static SwShOutputFileMutation Delete(
        string relativePath,
        OutputFileState reviewedPreimage)
    {
        ArgumentNullException.ThrowIfNull(reviewedPreimage);
        return new SwShOutputFileMutation(
            OutputMutationKind.Delete,
            relativePath,
            Contents: null,
            reviewedPreimage);
    }

    public static SwShOutputFileMutation DeleteLegacyAdoption(
        string relativePath,
        OutputFileState reviewedPreimage)
    {
        ArgumentNullException.ThrowIfNull(reviewedPreimage);
        return new SwShOutputFileMutation(
            OutputMutationKind.Delete,
            relativePath,
            Contents: null,
            reviewedPreimage,
            AllowLegacyAdoption: true);
    }

    public static SwShOutputFileMutation DeleteComposed(
        string relativePath,
        OutputFileState reviewedPreimage,
        byte[] restoredFallbackContents)
    {
        ArgumentNullException.ThrowIfNull(reviewedPreimage);
        ArgumentNullException.ThrowIfNull(restoredFallbackContents);
        return new SwShOutputFileMutation(
            OutputMutationKind.Delete,
            relativePath,
            Contents: null,
            reviewedPreimage,
            AllowLegacyAdoption: true,
            ComposesEffectivePreimage: true,
            DeleteFallbackContents: restoredFallbackContents);
    }
}

internal sealed record SwShOutputTransactionFailure(
    string RelativePath,
    string Message,
    string? Code = null,
    bool RecoveryRequired = false);
