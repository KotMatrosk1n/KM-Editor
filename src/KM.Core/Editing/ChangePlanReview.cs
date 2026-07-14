// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.Core.Editing;

public static class ChangePlanReview
{
    public static bool Matches(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        ArgumentNullException.ThrowIfNull(currentPlan);

        if (!reviewedPlan.CanApply
            || !currentPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedWrites = OrderWrites(reviewedPlan.Writes);
        var currentWrites = OrderWrites(currentPlan.Writes);

        return reviewedWrites
            .Zip(currentWrites)
            .All(pair => WritesMatch(pair.First, pair.Second));
    }

    private static IReadOnlyList<PlannedFileWrite> OrderWrites(IReadOnlyList<PlannedFileWrite> writes)
    {
        return writes
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ThenBy(write => write.Reason, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool WritesMatch(PlannedFileWrite reviewedWrite, PlannedFileWrite currentWrite)
    {
        return string.Equals(
                reviewedWrite.TargetRelativePath,
                currentWrite.TargetRelativePath,
                StringComparison.Ordinal)
            && reviewedWrite.ReplacesExistingOutput == currentWrite.ReplacesExistingOutput
            && string.Equals(reviewedWrite.Reason, currentWrite.Reason, StringComparison.Ordinal)
            && string.Equals(
                reviewedWrite.SourceFingerprint,
                currentWrite.SourceFingerprint,
                StringComparison.Ordinal)
            && SourcesMatch(reviewedWrite.Sources, currentWrite.Sources);
    }

    private static bool SourcesMatch(
        IReadOnlyList<ProjectFileReference> reviewedSources,
        IReadOnlyList<ProjectFileReference> currentSources)
    {
        if (reviewedSources.Count != currentSources.Count)
        {
            return false;
        }

        var orderedReviewedSources = OrderSources(reviewedSources);
        var orderedCurrentSources = OrderSources(currentSources);

        return orderedReviewedSources
            .Zip(orderedCurrentSources)
            .All(pair => pair.First.Layer == pair.Second.Layer
                && string.Equals(
                    pair.First.RelativePath,
                    pair.Second.RelativePath,
                    StringComparison.Ordinal));
    }

    private static IReadOnlyList<ProjectFileReference> OrderSources(
        IReadOnlyList<ProjectFileReference> sources)
    {
        return sources
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }
}
