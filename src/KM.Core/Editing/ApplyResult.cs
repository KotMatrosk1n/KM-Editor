// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Output;

namespace KM.Core.Editing;

public sealed record ApplyResult(
    string ApplyId,
    DateTimeOffset AppliedAt,
    IReadOnlyList<ProjectFileReference> WrittenFiles,
    WriteManifest Manifest,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    OutputApplyResult? OutputTransaction = null)
{
    public ApplyResult Complete(ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        if (WrittenFiles.Count == 0
            || Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || OutputTransaction is { Outcome: not OutputApplyOutcome.Committed })
        {
            return this;
        }

        // Validation and preview information describes a pending plan. Remove
        // every echoed copy after success, while retaining warnings and new
        // apply information, including output installation instructions.
        var reviewedInformation = reviewedPlan.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Info)
            .ToHashSet();
        var seenInformation = new HashSet<ValidationDiagnostic>();
        return this with
        {
            Diagnostics = Diagnostics.Where(diagnostic => !reviewedInformation.Contains(diagnostic))
                .Where(diagnostic => diagnostic.Severity != DiagnosticSeverity.Info || seenInformation.Add(diagnostic))
                .ToArray(),
        };
    }
}

public sealed record WriteManifest(
    string ApplyId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PlannedFileWrite> Writes);
