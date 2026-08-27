// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.RuntimeSettings;

public enum GameplayBundleSettingsRemoval
{
    Retain = 1,
    Remove = 2,
}

public sealed class GameplayBundleRemovalTargetReview
{
    private GameplayBundleRemovalTargetReview(
        RelativeOutputPath path,
        bool exists,
        ImmutableArray<byte> bytes)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        if (bytes.IsDefault || (!exists && !bytes.IsEmpty))
        {
            throw new ArgumentException(
                "A gameplay bundle removal target review has invalid byte state.",
                nameof(bytes));
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

    public static GameplayBundleRemovalTargetReview Missing(RelativeOutputPath path)
    {
        return new GameplayBundleRemovalTargetReview(path, exists: false, []);
    }

    public static GameplayBundleRemovalTargetReview Existing(
        RelativeOutputPath path,
        ReadOnlySpan<byte> bytes)
    {
        return new GameplayBundleRemovalTargetReview(
            path,
            exists: true,
            ImmutableArray.CreateRange(bytes.ToArray()));
    }
}

public sealed record GameplayBundleRemovalPlan(
    string BundleId,
    string ArchiveSha256,
    GameplayBundleSettingsRemoval SettingsRemoval,
    ImmutableArray<RelativeOutputPath> ReviewedTargets,
    ImmutableArray<RelativeOutputPath> DeletedTargets,
    ImmutableArray<RelativeOutputPath> RetainedTargets,
    OutputApplyPlan ApplyPlan);

public static class GameplayBundleRemovalPlanner
{
    private const string OriginId = "gameplay-bundle-removal";

    private sealed record BundlePayload(
        RelativeOutputPath Path,
        ImmutableArray<byte> Bytes,
        BundlePayloadKind Kind);

    private enum BundlePayloadKind
    {
        Immutable = 1,
        SettingsJournal = 2,
        CheatToggles = 3,
    }

    public static GameplayBundleRemovalPlan CreateRemoval(
        ReadOnlyMemory<byte> archiveBytes,
        ProjectId projectId,
        GameFamily gameFamily,
        IEnumerable<GameplayBundleRemovalTargetReview> reviewedTargets,
        OutputOwnershipInventorySnapshot ownershipSnapshot,
        GameplayBundleSettingsRemoval settingsRemoval = GameplayBundleSettingsRemoval.Retain)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        ArgumentNullException.ThrowIfNull(ownershipSnapshot);
        if (!Enum.IsDefined(settingsRemoval))
        {
            throw new ArgumentOutOfRangeException(nameof(settingsRemoval));
        }

        var family = GameplayBundleDeploymentPlanner.ToSettingsFamily(gameFamily);
        var bundle = GameplayBundleArchive.Read(archiveBytes, family);
        var payloads = CreatePayloadMap(bundle);
        var expectedPaths = payloads.Values
            .OrderBy(payload => payload.Path.CanonicalKey, StringComparer.Ordinal)
            .Select(payload => payload.Path)
            .ToImmutableArray();
        var reviews = ValidateReviews(reviewedTargets, expectedPaths);
        var ownership = ValidateOwnership(
            ownershipSnapshot,
            expectedPaths,
            projectId,
            gameFamily);

        var settingsPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/settings.bin");
        ValidateReviewedTargets(
            payloads,
            reviews,
            ownership,
            settingsPath.CanonicalKey,
            GetTogglesPath(bundle.Manifest.TitleId).CanonicalKey,
            family,
            bundle.Manifest.TitleId,
            gameFamily);

        var deletedTargets = expectedPaths
            .Where(path => settingsRemoval == GameplayBundleSettingsRemoval.Remove
                || !string.Equals(path.CanonicalKey, settingsPath.CanonicalKey, StringComparison.Ordinal))
            .ToImmutableArray();
        var retainedTargets = settingsRemoval == GameplayBundleSettingsRemoval.Retain
            ? ImmutableArray.Create(settingsPath)
            : ImmutableArray<RelativeOutputPath>.Empty;
        var mutations = deletedTargets
            .Select(path => CreateDelete(
                path,
                reviews[path.CanonicalKey],
                ownership[path.CanonicalKey],
                settingsPath.CanonicalKey,
                GetTogglesPath(bundle.Manifest.TitleId).CanonicalKey,
                gameFamily,
                bundle.Manifest.TitleId))
            .OrderBy(mutation => mutation.Path.CanonicalKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (mutations.IsEmpty
            || !mutations.Select(mutation => mutation.Path.CanonicalKey)
                .SequenceEqual(
                    deletedTargets.Select(path => path.CanonicalKey),
                    StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The gameplay bundle removal plan changed its exact verified deletion inventory.");
        }

        var applyPlan = new OutputApplyPlan(
            projectId,
            gameFamily,
            GameplayBundleDeploymentPlanner.OutputMode,
            OutputReviewFingerprint.FromMutations(mutations),
            [new OutputApplyOrigin(OutputApplyOriginKind.Generator, OriginId)],
            mutations,
            ownershipInventoryRevision: ownershipSnapshot.Revision);
        return new GameplayBundleRemovalPlan(
            bundle.Manifest.BundleId,
            bundle.Sha256,
            settingsRemoval,
            expectedPaths,
            deletedTargets,
            retainedTargets,
            applyPlan);
    }

    private static Dictionary<string, BundlePayload> CreatePayloadMap(
        GameplayBundleArchiveReadResult bundle)
    {
        var result = new Dictionary<string, BundlePayload>(StringComparer.Ordinal);
        foreach (var component in bundle.ImmutableComponents)
        {
            AddPayload(result, component.Key, component.Value, BundlePayloadKind.Immutable);
        }

        foreach (var component in bundle.RuntimeMutableComponents)
        {
            AddPayload(result, component.Key, component.Value, BundlePayloadKind.CheatToggles);
        }

        AddPayload(
            result,
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/bundle.manifest",
            bundle.ManifestBytes,
            BundlePayloadKind.Immutable);
        AddPayload(
            result,
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/settings.bin",
            bundle.SettingsJournal,
            BundlePayloadKind.SettingsJournal);
        if (result.Count != bundle.Entries.Length
            || !result.Values
                .Select(payload => payload.Path.Value)
                .OrderBy(path => path, StringComparer.Ordinal)
                .SequenceEqual(
                    bundle.Entries.OrderBy(path => path, StringComparer.Ordinal),
                    StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The gameplay bundle removal inventory differs from the verified archive.");
        }

        return result;
    }

    private static void AddPayload(
        IDictionary<string, BundlePayload> payloads,
        string value,
        ImmutableArray<byte> bytes,
        BundlePayloadKind kind)
    {
        var path = new RelativeOutputPath(value);
        if (!payloads.TryAdd(
                path.CanonicalKey,
                new BundlePayload(path, bytes, kind)))
        {
            throw new InvalidDataException(
                "A gameplay bundle contains colliding canonical removal paths.");
        }
    }

    private static Dictionary<string, GameplayBundleRemovalTargetReview> ValidateReviews(
        IEnumerable<GameplayBundleRemovalTargetReview> reviewedTargets,
        ImmutableArray<RelativeOutputPath> expectedPaths)
    {
        ArgumentNullException.ThrowIfNull(reviewedTargets);
        var expectedKeys = expectedPaths
            .Select(path => path.CanonicalKey)
            .ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, GameplayBundleRemovalTargetReview>(StringComparer.Ordinal);
        foreach (var review in reviewedTargets)
        {
            if (review is null
                || !expectedKeys.Contains(review.Path.CanonicalKey)
                || !result.TryAdd(review.Path.CanonicalKey, review))
            {
                throw new ArgumentException(
                    "A gameplay bundle removal requires one review for every exact archive target.",
                    nameof(reviewedTargets));
            }
        }

        if (result.Count != expectedKeys.Count)
        {
            throw new ArgumentException(
                "The gameplay bundle removal target review is incomplete.",
                nameof(reviewedTargets));
        }

        return result;
    }

    private static Dictionary<string, OutputOwnershipRecord> ValidateOwnership(
        OutputOwnershipInventorySnapshot ownershipSnapshot,
        ImmutableArray<RelativeOutputPath> expectedPaths,
        ProjectId projectId,
        GameFamily gameFamily)
    {
        var records = ownershipSnapshot.Inventory.Files.ToDictionary(
            record => record.Path.CanonicalKey,
            StringComparer.Ordinal);
        var result = new Dictionary<string, OutputOwnershipRecord>(StringComparer.Ordinal);
        foreach (var path in expectedPaths)
        {
            if (!records.TryGetValue(path.CanonicalKey, out var record)
                || !string.Equals(record.Path.Value, path.Value, StringComparison.Ordinal)
                || record.ProjectId != projectId
                || record.GameFamily != gameFamily
                || !string.Equals(
                    record.OutputMode,
                    GameplayBundleDeploymentPlanner.OutputMode,
                    StringComparison.Ordinal)
                || !record.FileDeleteEligible)
            {
                throw new InvalidDataException(
                    "A gameplay bundle removal target is missing exact KM ownership.");
            }

            var expectedClaim = GameplayBundleDeploymentPlanner.CreateWholeFileClaim(path, gameFamily);
            if (record.Claims.Length != 1 || record.Claims[0] != expectedClaim)
            {
                throw new InvalidDataException(
                    "A gameplay bundle removal target has foreign or conflicting ownership claims.");
            }

            result.Add(path.CanonicalKey, record);
        }

        return result;
    }

    private static void ValidateReviewedTargets(
        IReadOnlyDictionary<string, BundlePayload> payloads,
        IReadOnlyDictionary<string, GameplayBundleRemovalTargetReview> reviews,
        IReadOnlyDictionary<string, OutputOwnershipRecord> ownership,
        string settingsPath,
        string togglesPath,
        GameplaySettingsFamily settingsFamily,
        ulong titleId,
        GameFamily gameFamily)
    {
        foreach (var (path, payload) in payloads)
        {
            var review = reviews[path];
            var record = ownership[path];
            if (!review.Exists
                || !string.Equals(review.Path.Value, payload.Path.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A gameplay bundle removal target is missing or has changed path spelling.");
            }

            if (string.Equals(path, settingsPath, StringComparison.Ordinal))
            {
                if (payload.Kind != BundlePayloadKind.SettingsJournal
                    || record.RuntimeMutableDescriptor is not { } descriptor
                    || descriptor.Kind != OutputRuntimeMutableKind.GameplaySettingsJournalV1
                    || descriptor.TitleId != titleId
                    || descriptor.MinimumGeneration is null
                    || !GameplaySettingsJournal.CanDeleteOwned(
                        review.Bytes.AsSpan().ToArray(),
                        settingsFamily,
                        titleId))
                {
                    throw new InvalidDataException(
                        "The gameplay settings journal is not proven to belong to this exact bundle title.");
                }

                descriptor.ValidateIdentity(payload.Path, gameFamily);
                continue;
            }

            if (string.Equals(path, togglesPath, StringComparison.Ordinal))
            {
                var expectedIdentity = AtmosphereCheatToggleDocument.ComputeInventoryIdentity(
                    payload.Bytes.AsSpan());
                if (payload.Kind != BundlePayloadKind.CheatToggles
                    || record.RuntimeMutableDescriptor is not { } descriptor
                    || descriptor.Kind != OutputRuntimeMutableKind.BooleanToggleListV1
                    || descriptor.TitleId != titleId
                    || !string.Equals(
                        descriptor.SemanticIdentity,
                        expectedIdentity,
                        StringComparison.Ordinal)
                    || !AtmosphereCheatToggleDocument.HasExactInventory(
                        review.Bytes.AsSpan(),
                        expectedIdentity))
                {
                    throw new InvalidDataException(
                        "The cheat selection document is not proven to belong to this exact bundle title.");
                }

                descriptor.ValidateIdentity(payload.Path, gameFamily);
                continue;
            }

            if (payload.Kind != BundlePayloadKind.Immutable
                || record.RuntimeMutableDescriptor is not null
                || record.CurrentState != review.State
                || !review.Bytes.AsSpan().SequenceEqual(payload.Bytes.AsSpan()))
            {
                throw new InvalidDataException(
                    "An immutable gameplay bundle removal target no longer matches the verified archive.");
            }
        }
    }

    private static OutputMutation CreateDelete(
        RelativeOutputPath path,
        GameplayBundleRemovalTargetReview review,
        OutputOwnershipRecord ownership,
        string settingsPath,
        string togglesPath,
        GameFamily gameFamily,
        ulong titleId)
    {
        if (string.Equals(path.CanonicalKey, settingsPath, StringComparison.Ordinal))
        {
            return OutputMutation.DeleteRuntimeMutable(
                path,
                review.Bytes.AsMemory(),
                review.State,
                ownership.Claims,
                gameFamily,
                titleId,
                GameplayBundleDeploymentPlanner.OutputMode);
        }

        return string.Equals(path.CanonicalKey, togglesPath, StringComparison.Ordinal)
            ? OutputMutation.DeleteRuntimeMutableToggle(
                path,
                review.Bytes.AsMemory(),
                review.State,
                ownership.Claims,
                gameFamily,
                titleId,
                GameplayBundleDeploymentPlanner.OutputMode)
            : OutputMutation.Delete(
                path,
                review.State,
                ownership.Claims,
                GameplayBundleDeploymentPlanner.OutputMode);
    }

    private static RelativeOutputPath GetTogglesPath(ulong titleId) => new(
        $"atmosphere/contents/{titleId:X16}/cheats/toggles.txt");
}
