// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.RuntimeSettings;

public sealed class GameplayBundleUpgradeTargetReview
{
    private GameplayBundleUpgradeTargetReview(
        RelativeOutputPath path,
        bool exists,
        ImmutableArray<byte> bytes)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        if (bytes.IsDefault || !exists && !bytes.IsEmpty)
        {
            throw new ArgumentException("A gameplay bundle target review has invalid byte state.", nameof(bytes));
        }

        Exists = exists;
        Bytes = bytes;
        State = exists
            ? OutputFileState.Existing(
                Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())),
                bytes.Length)
            : OutputFileState.Missing;
    }

    public RelativeOutputPath Path { get; }

    public bool Exists { get; }

    public ImmutableArray<byte> Bytes { get; }

    public OutputFileState State { get; }

    public static GameplayBundleUpgradeTargetReview Missing(RelativeOutputPath path)
    {
        return new GameplayBundleUpgradeTargetReview(path, exists: false, []);
    }

    public static GameplayBundleUpgradeTargetReview Existing(
        RelativeOutputPath path,
        ReadOnlySpan<byte> bytes)
    {
        return new GameplayBundleUpgradeTargetReview(
            path,
            exists: true,
            ImmutableArray.CreateRange(bytes.ToArray()));
    }
}

public sealed record GameplayBundleUpgradePlan(
    string PreviousBundleId,
    string BundleId,
    string ArchiveSha256,
    ImmutableArray<RelativeOutputPath> ReviewedTargets,
    OutputApplyPlan ApplyPlan);

public static class GameplayBundleUpgradePlanner
{
    private const string OriginId = "gameplay-bundle-upgrade";

    private sealed record BundlePayload(
        RelativeOutputPath Path,
        ImmutableArray<byte> Bytes);

    public static GameplayBundleUpgradePlan CreateUpgrade(
        ReadOnlyMemory<byte> previousArchiveBytes,
        ReadOnlyMemory<byte> archiveBytes,
        ProjectId projectId,
        GameFamily gameFamily,
        IEnumerable<GameplayBundleUpgradeTargetReview> reviewedTargets)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        var family = GameplayBundleDeploymentPlanner.ToSettingsFamily(gameFamily);
        var previous = GameplayBundleArchive.Read(previousArchiveBytes, family);
        var next = GameplayBundleArchive.Read(archiveBytes, family);
        if (previous.Manifest.TitleId != next.Manifest.TitleId)
        {
            throw new InvalidDataException("A gameplay bundle upgrade cannot change its title identity.");
        }

        var previousPayloads = CreatePayloadMap(previous);
        var nextPayloads = CreatePayloadMap(next);
        ValidatePathSpelling(previousPayloads, nextPayloads);
        var expectedPaths = previousPayloads.Keys
            .Concat(nextPayloads.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => nextPayloads.TryGetValue(path, out var nextPayload)
                ? nextPayload.Path
                : previousPayloads[path].Path)
            .ToImmutableArray();
        var reviews = ValidateReviews(reviewedTargets, expectedPaths);
        var settingsPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{next.Manifest.TitleId:X16}/settings.bin");
        ValidateReviewedBaseline(
            previousPayloads,
            nextPayloads,
            reviews,
            settingsPath.CanonicalKey);

        var mutations = ImmutableArray.CreateBuilder<OutputMutation>();
        foreach (var path in expectedPaths)
        {
            var key = path.CanonicalKey;
            var review = reviews[key];
            var claim = GameplayBundleDeploymentPlanner.CreateWholeFileClaim(path, gameFamily);
            if (string.Equals(key, settingsPath.CanonicalKey, StringComparison.Ordinal))
            {
                AddSettingsTransition(
                    mutations,
                    path,
                    review,
                    next,
                    family,
                    gameFamily,
                    claim);
                continue;
            }

            var hasPrevious = previousPayloads.TryGetValue(key, out var previousPayload);
            var hasNext = nextPayloads.TryGetValue(key, out var nextPayload);
            if (hasPrevious && !hasNext)
            {
                mutations.Add(OutputMutation.Delete(path, review.State, [claim]));
                continue;
            }

            if (hasNext
                && (!hasPrevious
                    || !nextPayload!.Bytes.AsSpan().SequenceEqual(previousPayload!.Bytes.AsSpan())))
            {
                mutations.Add(OutputMutation.Write(
                    path,
                    nextPayload!.Bytes.ToArray(),
                    review.State,
                    [claim]));
            }
        }

        var orderedMutations = mutations
            .OrderBy(mutation => mutation.Path.CanonicalKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (orderedMutations.IsEmpty)
        {
            throw new InvalidOperationException("The reviewed gameplay bundle upgrade has no effective changes.");
        }

        var applyPlan = new OutputApplyPlan(
            projectId,
            gameFamily,
            GameplayBundleDeploymentPlanner.OutputMode,
            OutputReviewFingerprint.FromMutations(orderedMutations),
            [new OutputApplyOrigin(OutputApplyOriginKind.Generator, OriginId)],
            orderedMutations);
        return new GameplayBundleUpgradePlan(
            previous.Manifest.BundleId,
            next.Manifest.BundleId,
            next.Sha256,
            expectedPaths,
            applyPlan);
    }

    private static Dictionary<string, BundlePayload> CreatePayloadMap(
        GameplayBundleArchiveReadResult bundle)
    {
        var result = new Dictionary<string, BundlePayload>(StringComparer.Ordinal);
        foreach (var component in bundle.ImmutableComponents)
        {
            AddPayload(result, component.Key, component.Value);
        }

        var manifestPath =
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/bundle.manifest";
        var settingsPath =
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/settings.bin";
        AddPayload(result, manifestPath, bundle.ManifestBytes);
        AddPayload(result, settingsPath, bundle.SettingsJournal);
        return result;
    }

    private static void AddPayload(
        IDictionary<string, BundlePayload> payloads,
        string value,
        ImmutableArray<byte> bytes)
    {
        var path = new RelativeOutputPath(value);
        if (!payloads.TryAdd(path.CanonicalKey, new BundlePayload(path, bytes)))
        {
            throw new InvalidDataException(
                "A gameplay bundle contains colliding canonical output paths.");
        }
    }

    private static void ValidatePathSpelling(
        IReadOnlyDictionary<string, BundlePayload> previousPayloads,
        IReadOnlyDictionary<string, BundlePayload> nextPayloads)
    {
        foreach (var key in previousPayloads.Keys.Intersect(nextPayloads.Keys, StringComparer.Ordinal))
        {
            if (!string.Equals(
                    previousPayloads[key].Path.Value,
                    nextPayloads[key].Path.Value,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A gameplay bundle upgrade cannot change only the spelling of an output path.");
            }
        }
    }

    private static Dictionary<string, GameplayBundleUpgradeTargetReview> ValidateReviews(
        IEnumerable<GameplayBundleUpgradeTargetReview> reviewedTargets,
        ImmutableArray<RelativeOutputPath> expectedPaths)
    {
        ArgumentNullException.ThrowIfNull(reviewedTargets);
        var expectedKeys = expectedPaths
            .Select(path => path.CanonicalKey)
            .ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, GameplayBundleUpgradeTargetReview>(StringComparer.Ordinal);
        foreach (var review in reviewedTargets)
        {
            if (review is null
                || !expectedKeys.Contains(review.Path.CanonicalKey)
                || !result.TryAdd(review.Path.CanonicalKey, review))
            {
                throw new ArgumentException(
                    "A gameplay bundle upgrade requires one review for every exact old or new target.",
                    nameof(reviewedTargets));
            }
        }

        if (result.Count != expectedKeys.Count)
        {
            throw new ArgumentException(
                "The gameplay bundle upgrade target review is incomplete.",
                nameof(reviewedTargets));
        }

        return result;
    }

    private static void ValidateReviewedBaseline(
        IReadOnlyDictionary<string, BundlePayload> previousPayloads,
        IReadOnlyDictionary<string, BundlePayload> nextPayloads,
        IReadOnlyDictionary<string, GameplayBundleUpgradeTargetReview> reviews,
        string settingsPath)
    {
        foreach (var (path, previousPayload) in previousPayloads)
        {
            if (string.Equals(path, settingsPath, StringComparison.Ordinal))
            {
                continue;
            }

            var review = reviews[path];
            if (!review.Exists
                || !review.Bytes.AsSpan().SequenceEqual(previousPayload.Bytes.AsSpan()))
            {
                throw new InvalidDataException(
                    "An existing gameplay bundle target no longer matches the verified previous bundle.");
            }
        }

        foreach (var path in nextPayloads.Keys.Except(previousPayloads.Keys, StringComparer.Ordinal))
        {
            if (reviews[path].Exists)
            {
                throw new InvalidDataException(
                    "A new gameplay bundle target already exists outside the verified previous bundle.");
            }
        }
    }

    private static void AddSettingsTransition(
        ICollection<OutputMutation> mutations,
        RelativeOutputPath path,
        GameplayBundleUpgradeTargetReview review,
        GameplayBundleArchiveReadResult next,
        GameplaySettingsFamily family,
        GameFamily gameFamily,
        OwnedTarget claim)
    {
        if (!review.Exists)
        {
            throw new InvalidDataException(
                "A gameplay bundle upgrade requires the existing runtime settings journal.");
        }

        var currentBytes = review.Bytes.AsSpan().ToArray();
        var current = GameplaySettingsJournal.Inspect(
            currentBytes,
            family,
            next.Manifest.TitleId);
        var nextBootstrap = GameplaySettingsJournal.Inspect(
            next.SettingsJournal.AsSpan().ToArray(),
            family,
            next.Manifest.TitleId);
        if (!current.WritesAllowed
            || current.ActiveSnapshot is null
            || nextBootstrap.ActiveSnapshot is null)
        {
            throw new InvalidDataException(
                "The existing gameplay settings journal cannot be safely upgraded.");
        }

        var packageVersion = next.Manifest.PackageVersion;
        var writerVersion = new GameplaySettingsWriterVersion(
            checked((ushort)packageVersion.Major),
            checked((ushort)packageVersion.Minor),
            checked((ushort)packageVersion.Patch));
        var desiredPresence = nextBootstrap.ActiveSnapshot.Presence;
        if (current.ActiveSnapshot.WriterVersion == writerVersion
            && current.ActiveSnapshot.Presence == desiredPresence)
        {
            return;
        }

        var updated = GameplaySettingsJournal.CreatePresenceTransition(
            currentBytes,
            family,
            next.Manifest.TitleId,
            writerVersion,
            desiredPresence);
        mutations.Add(OutputMutation.WriteRuntimeMutableTransition(
            path,
            currentBytes,
            updated.JournalBytes,
            review.State,
            [claim],
            gameFamily,
            next.Manifest.TitleId));
    }
}
