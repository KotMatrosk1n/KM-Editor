// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;

namespace KM.Core.Editing;

public sealed record ChangePlan(
    EditSessionId SessionId,
    IReadOnlyList<PlannedFileWrite> Writes,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool CanApply => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

    // Internal application-service binding evidence. Bridge mapping deliberately
    // serializes only the native plan fields above so family apply services still
    // review and rederive their exact native PlannedFileWrite fingerprints.
    public string? GeneratedSourceBindingFingerprint { get; init; }

    // Authenticated planner result after source-equivalent edits have been
    // removed under the workflow's source guard. Null means the workflow does
    // not expose this capability. Bridge mapping deliberately omits this list.
    // A non-null value must only be published when normalization and source
    // inspection completed successfully.
    public IReadOnlyList<PendingEdit>? EffectivePendingEdits { get; init; }

    public static ChangePlan Empty(EditSessionId sessionId)
    {
        return new ChangePlan(sessionId, Array.Empty<PlannedFileWrite>(), Array.Empty<ValidationDiagnostic>());
    }
}

public sealed record PlannedFileWrite(
    string TargetRelativePath,
    IReadOnlyList<ProjectFileReference> Sources,
    bool ReplacesExistingOutput,
    string Reason,
    string? SourceFingerprint = null)
{
    // Internal source-boundary evidence for durable named operations. Native
    // plan review continues to use SourceFingerprint, which may additionally
    // bind the exact pending-edit intent. Bridge DTOs intentionally omit this.
    public string? SourceBindingFingerprint { get; init; }
}
