// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using Xunit;

namespace KM.Core.Tests.Editing;

public sealed class ChangePlanReviewTests
{
    [Fact]
    public void MatchesRequiresTheSameSourceLayerAndWriteDetails()
    {
        var sessionId = EditSessionId.New();
        var reviewedPlan = CreatePlan(
            sessionId,
            ProjectFileLayer.Base,
            replacesExistingOutput: false,
            reason: "Apply edit",
            sourceFingerprint: "REVIEWED");

        Assert.True(ChangePlanReview.Matches(
            reviewedPlan,
            CreatePlan(
                sessionId,
                ProjectFileLayer.Base,
                replacesExistingOutput: false,
                reason: "Apply edit",
                sourceFingerprint: "REVIEWED")));
        Assert.False(ChangePlanReview.Matches(
            reviewedPlan,
            CreatePlan(
                sessionId,
                ProjectFileLayer.Base,
                replacesExistingOutput: false,
                reason: "Apply edit",
                sourceFingerprint: "CHANGED")));
        Assert.False(ChangePlanReview.Matches(
            reviewedPlan,
            CreatePlan(
                sessionId,
                ProjectFileLayer.Layered,
                replacesExistingOutput: false,
                reason: "Apply edit")));
        Assert.False(ChangePlanReview.Matches(
            reviewedPlan,
            CreatePlan(
                sessionId,
                ProjectFileLayer.Base,
                replacesExistingOutput: true,
                reason: "Apply edit")));
        Assert.False(ChangePlanReview.Matches(
            reviewedPlan,
            CreatePlan(
                sessionId,
                ProjectFileLayer.Base,
                replacesExistingOutput: false,
                reason: "Different edit")));
    }

    [Fact]
    public void MatchesRejectsPlansThatAreNotApplyable()
    {
        var sessionId = EditSessionId.New();
        var reviewedPlan = CreatePlan(
            sessionId,
            ProjectFileLayer.Base,
            replacesExistingOutput: false,
            reason: "Apply edit") with
        {
            Diagnostics =
            [
                new ValidationDiagnostic(DiagnosticSeverity.Error, "Blocked"),
            ],
        };

        Assert.False(ChangePlanReview.Matches(
            reviewedPlan,
            CreatePlan(
                sessionId,
                ProjectFileLayer.Base,
                replacesExistingOutput: false,
                reason: "Apply edit")));
    }

    private static ChangePlan CreatePlan(
        EditSessionId sessionId,
        ProjectFileLayer sourceLayer,
        bool replacesExistingOutput,
        string reason,
        string? sourceFingerprint = null)
    {
        return new ChangePlan(
            sessionId,
            [
                new PlannedFileWrite(
                    "romfs/data/example.bin",
                    [new ProjectFileReference(sourceLayer, "romfs/data/example.bin")],
                    replacesExistingOutput,
                    reason,
                    sourceFingerprint),
            ],
            Array.Empty<ValidationDiagnostic>());
    }
}
