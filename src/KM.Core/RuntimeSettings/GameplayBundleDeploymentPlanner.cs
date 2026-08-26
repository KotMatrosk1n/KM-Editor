// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.RuntimeSettings;

public sealed record GameplayBundleInitialInstallPlan(
    string BundleId,
    string ArchiveSha256,
    ImmutableArray<RelativeOutputPath> Targets,
    OutputApplyPlan ApplyPlan);

public static class GameplayBundleDeploymentPlanner
{
    public const string OutputMode = "gameplay-bundle";
    internal const string OwnerId = "gameplay-bundle";
    internal const string PreservationRule = "whole-file-gameplay-bundle";
    private const string OriginId = "gameplay-bundle-initial-install";

    public static GameplayBundleInitialInstallPlan CreateInitialInstall(
        ReadOnlyMemory<byte> archiveBytes,
        ProjectId projectId,
        GameFamily gameFamily,
        IEnumerable<OutputBaselineEntry> reviewedTargets)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        var family = ToSettingsFamily(gameFamily);
        var bundle = GameplayBundleArchive.Read(archiveBytes, family);
        var expectedPaths = bundle.Entries
            .Select(path => new RelativeOutputPath(path))
            .OrderBy(path => path.CanonicalKey, StringComparer.Ordinal)
            .ToImmutableArray();
        var reviewedByPath = ValidateInitialTargets(reviewedTargets, expectedPaths);

        var mutations = ImmutableArray.CreateBuilder<OutputMutation>(expectedPaths.Length);
        foreach (var component in bundle.ImmutableComponents
                     .OrderBy(component => component.Key, StringComparer.Ordinal))
        {
            var path = new RelativeOutputPath(component.Key);
            mutations.Add(OutputMutation.Write(
                path,
                component.Value.AsMemory(),
                reviewedByPath[path.CanonicalKey],
                [CreateWholeFileClaim(path, gameFamily)]));
        }

        var manifestPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/bundle.manifest");
        mutations.Add(OutputMutation.Write(
            manifestPath,
            bundle.ManifestBytes.AsMemory(),
            reviewedByPath[manifestPath.CanonicalKey],
            [CreateWholeFileClaim(manifestPath, gameFamily)]));

        var settingsPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{bundle.Manifest.TitleId:X16}/settings.bin");
        mutations.Add(OutputMutation.WriteRuntimeMutableBootstrap(
            settingsPath,
            bundle.SettingsJournal.AsMemory(),
            reviewedByPath[settingsPath.CanonicalKey],
            [CreateWholeFileClaim(settingsPath, gameFamily)],
            gameFamily,
            bundle.Manifest.TitleId));

        var orderedMutations = mutations
            .OrderBy(mutation => mutation.Path.CanonicalKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (orderedMutations.Length != expectedPaths.Length
            || !orderedMutations.Select(mutation => mutation.Path.CanonicalKey)
                .SequenceEqual(expectedPaths.Select(path => path.CanonicalKey), StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The gameplay bundle install plan changed its exact verified target inventory.");
        }

        var applyPlan = new OutputApplyPlan(
            projectId,
            gameFamily,
            OutputMode,
            OutputReviewFingerprint.FromMutations(orderedMutations),
            [new OutputApplyOrigin(OutputApplyOriginKind.Generator, OriginId)],
            orderedMutations);
        return new GameplayBundleInitialInstallPlan(
            bundle.Manifest.BundleId,
            bundle.Sha256,
            expectedPaths,
            applyPlan);
    }

    private static Dictionary<string, OutputFileState> ValidateInitialTargets(
        IEnumerable<OutputBaselineEntry> reviewedTargets,
        ImmutableArray<RelativeOutputPath> expectedPaths)
    {
        ArgumentNullException.ThrowIfNull(reviewedTargets);
        var expectedKeys = expectedPaths
            .Select(path => path.CanonicalKey)
            .ToHashSet(StringComparer.Ordinal);
        var reviewed = new Dictionary<string, OutputFileState>(StringComparer.Ordinal);
        foreach (var target in reviewedTargets)
        {
            if (target is null
                || target.Path is null
                || target.State is null
                || !expectedKeys.Contains(target.Path.CanonicalKey)
                || !reviewed.TryAdd(target.Path.CanonicalKey, target.State)
                || target.State.Exists)
            {
                throw new ArgumentException(
                    "An initial gameplay bundle install requires every exact target to be reviewed as missing.",
                    nameof(reviewedTargets));
            }
        }

        if (reviewed.Count != expectedKeys.Count)
        {
            throw new ArgumentException(
                "The initial gameplay bundle target review is incomplete.",
                nameof(reviewedTargets));
        }

        return reviewed;
    }

    internal static OwnedTarget CreateWholeFileClaim(
        RelativeOutputPath path,
        GameFamily gameFamily)
    {
        return new OwnedTarget(
            gameFamily,
            new OwnedTargetAddress(path),
            new OwnershipOwnerId(OwnerId),
            new PreservationRuleDescriptor(
                PreservationRule,
                schemaVersion: 1,
                preservesUnownedData: false,
                requiresPreimage: false));
    }

    public static GameplaySettingsFamily ToSettingsFamily(GameFamily gameFamily)
    {
        return SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily)) switch
        {
            GameFamily.SwordShield => GameplaySettingsFamily.SwordShield,
            GameFamily.ScarletViolet => GameplaySettingsFamily.ScarletViolet,
            GameFamily.LegendsZA => GameplaySettingsFamily.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(gameFamily)),
        };
    }
}
