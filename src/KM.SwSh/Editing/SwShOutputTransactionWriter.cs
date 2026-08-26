// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.SwSh.Editing;

internal static class SwShOutputTransactionWriter
{
    internal const int MaximumFilesPerTransaction = OutputLimits.MaximumMutationsPerApply;

    private const string OutputMode = "sword-shield-layered-output";
    private const string OutputOwner = "sword-shield-verified-editor";
    private const string PreservationRule = "verified-whole-file-postimage";

    private static readonly OutputTransactionCoordinatorOptions CoordinatorOptions = new()
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
                .Take(MaximumFilesPerTransaction + 1)
                .ToArray();
            if (materialized.Length > MaximumFilesPerTransaction)
            {
                failure = new SwShOutputTransactionFailure(
                    string.Empty,
                    $"The output transaction cannot contain more than {MaximumFilesPerTransaction} files.");
                return false;
            }

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
                if (requested.Kind == OutputMutationKind.Delete)
                {
                    if (expectedPreimage.Exists)
                    {
                        if (requested.AllowLegacyAdoption)
                        {
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
                        [ownership]));
                }
            }

            if (mutations.Count == 0)
            {
                return true;
            }

            var plan = new OutputApplyPlan(
                projectId,
                GameFamily.SwordShield,
                OutputMode,
                OutputReviewFingerprint.FromMutations(mutations),
                [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, operationId)],
                mutations);
            result = new OutputTransactionCoordinator(
                    stablePaths.OutputRootPath,
                    CoordinatorOptions)
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
                result.Receipt.OutcomeCode);
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
}

internal sealed record SwShOutputFileMutation(
    OutputMutationKind Kind,
    string RelativePath,
    byte[]? Contents,
    OutputFileState? ReviewedPreimage = null,
    bool AllowLegacyAdoption = false)
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
}

internal sealed record SwShOutputTransactionFailure(
    string RelativePath,
    string Message,
    string? Code = null);
