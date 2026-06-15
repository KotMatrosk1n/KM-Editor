// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.Workflows;

namespace KM.SV.Workflows;

internal static class SvWorkflowSupport
{
    public static SwShWorkflowSummary CreateSummary(
        OpenedProject project,
        string id,
        string label,
        string description,
        IReadOnlyList<ValidationDiagnostic>? diagnostics = null)
    {
        var availability = project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : project.Health.CanOpenReadOnlyWorkflows
                ? SwShWorkflowAvailability.ReadOnly
                : SwShWorkflowAvailability.Disabled;

        return new SwShWorkflowSummary(
            id,
            label,
            description,
            availability,
            diagnostics ?? project.Health.Diagnostics);
    }

    public static ValidationDiagnostic Error(string message, string? file = null, string? field = null, string? expected = null)
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Error,
            message,
            file,
            Domain: "sv.editor",
            Field: field,
            Expected: expected);
    }
}
