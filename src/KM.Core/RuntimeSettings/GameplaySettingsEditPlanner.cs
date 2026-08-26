// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.RuntimeSettings;

public sealed record GameplaySettingsEditRequest(
    bool? ExperienceShareEnabled = null,
    uint? ExperienceRateBasisPoints = null,
    bool? LevelCapEnabled = null,
    byte? LevelCap = null);

public sealed record GameplaySettingsEditPlan(
    GameplaySettingsSnapshot Before,
    GameplaySettingsSnapshot After,
    OutputApplyPlan ApplyPlan);

public static class GameplaySettingsEditPlanner
{
    public const uint ExperienceRateStepBasisPoints = 1_000;
    public const uint MaximumExperienceRateBasisPoints = 50_000;

    private const string OriginId = "gameplay-settings-edit";

    public static GameplaySettingsEditPlan CreateUpdate(
        ReadOnlySpan<byte> reviewedJournal,
        ProjectId projectId,
        GameFamily gameFamily,
        ulong titleId,
        GameplaySettingsEditRequest request)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        ArgumentNullException.ThrowIfNull(request);
        ValidateTitle(gameFamily, titleId);
        if (reviewedJournal.Length != GameplaySettingsJournal.JournalSize)
        {
            throw new InvalidDataException(
                "A gameplay settings edit requires the complete reviewed settings journal.");
        }

        var family = GameplayBundleDeploymentPlanner.ToSettingsFamily(gameFamily);
        var reviewedBytes = reviewedJournal.ToArray();
        var inspection = GameplaySettingsJournal.Inspect(reviewedBytes, family, titleId);
        if (!inspection.WritesAllowed || inspection.ActiveSnapshot is null)
        {
            throw new InvalidDataException(
                "The reviewed gameplay settings journal is not safely writable.");
        }

        var before = inspection.ActiveSnapshot;
        var requestedFieldCount = CountRequestedFields(request);
        if (requestedFieldCount == 0)
        {
            throw new ArgumentException(
                "A gameplay settings edit must request at least one field change.",
                nameof(request));
        }

        RequirePresent(
            before.Presence,
            GameplaySettingPresence.ExperienceShare,
            request.ExperienceShareEnabled is not null,
            nameof(request.ExperienceShareEnabled));
        RequirePresent(
            before.Presence,
            GameplaySettingPresence.ExperienceRate,
            request.ExperienceRateBasisPoints is not null,
            nameof(request.ExperienceRateBasisPoints));
        RequirePresent(
            before.Presence,
            GameplaySettingPresence.LevelCap,
            request.LevelCapEnabled is not null || request.LevelCap is not null,
            nameof(request.LevelCap));

        if (request.ExperienceRateBasisPoints is { } requestedRate
            && (requestedRate > MaximumExperienceRateBasisPoints
                || requestedRate % ExperienceRateStepBasisPoints != 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The experience rate must use a supported value from 0 through 500 percent in 10 percent steps.");
        }

        if (request.LevelCap is { } requestedCap && requestedCap is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The level cap must be between 1 and 100.");
        }

        var capEnabled = request.LevelCapEnabled ?? before.Values.LevelCapEnabled;
        if (request.LevelCap is { } disabledCap && !capEnabled && disabledCap != 100)
        {
            throw new ArgumentException(
                "A disabled level cap cannot retain a hidden custom level.",
                nameof(request));
        }

        var desired = new GameplaySettingsValues(
            request.ExperienceShareEnabled ?? before.Values.ExperienceShareEnabled,
            request.ExperienceRateBasisPoints ?? before.Values.ExperienceRateBasisPoints,
            capEnabled,
            request.LevelCap ?? before.Values.LevelCap);
        var update = GameplaySettingsJournal.CreateUpdate(
            reviewedBytes,
            family,
            titleId,
            before.WriterVersion,
            before.Presence,
            desired);
        if (update.Snapshot.Values == before.Values)
        {
            throw new InvalidOperationException(
                "The reviewed gameplay settings edit has no effective value change.");
        }

        var path = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{titleId:X16}/settings.bin");
        var preimage = OutputFileState.Existing(
            Convert.ToHexStringLower(SHA256.HashData(reviewedBytes)),
            reviewedBytes.Length);
        var mutation = OutputMutation.WriteRuntimeMutableTransition(
            path,
            reviewedBytes,
            update.JournalBytes,
            preimage,
            [GameplayBundleDeploymentPlanner.CreateWholeFileClaim(path, gameFamily)],
            gameFamily,
            titleId);
        var mutations = ImmutableArray.Create(mutation);
        var applyPlan = new OutputApplyPlan(
            projectId,
            gameFamily,
            GameplayBundleDeploymentPlanner.OutputMode,
            OutputReviewFingerprint.FromMutations(mutations),
            [new OutputApplyOrigin(OutputApplyOriginKind.Generator, OriginId)],
            mutations);
        return new GameplaySettingsEditPlan(before, update.Snapshot, applyPlan);
    }

    private static int CountRequestedFields(GameplaySettingsEditRequest request)
    {
        return (request.ExperienceShareEnabled is null ? 0 : 1)
            + (request.ExperienceRateBasisPoints is null ? 0 : 1)
            + (request.LevelCapEnabled is null ? 0 : 1)
            + (request.LevelCap is null ? 0 : 1);
    }

    private static void RequirePresent(
        GameplaySettingPresence presence,
        GameplaySettingPresence feature,
        bool requested,
        string parameterName)
    {
        if (requested && !presence.HasFlag(feature))
        {
            throw new ArgumentException(
                "The installed gameplay package does not expose the requested setting.",
                parameterName);
        }
    }

    private static void ValidateTitle(GameFamily gameFamily, ulong titleId)
    {
        _ = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        var game = ProjectGameMetadata.DetectByTitleId(titleId);
        if (game is null || !gameFamily.Contains(game.Value))
        {
            throw new ArgumentException(
                "The gameplay settings title identity does not belong to the selected game family.",
                nameof(titleId));
        }
    }
}
