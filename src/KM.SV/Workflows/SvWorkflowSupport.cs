// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Workflows;

namespace KM.SV.Workflows;

internal static class SvWorkflowSupport
{
    public static SvWorkflowSummary CreateSummary(
        OpenedProject project,
        string id,
        string label,
        string description,
        IReadOnlyList<ValidationDiagnostic>? diagnostics = null)
    {
        var availability = project.Health.CanOpenEditableWorkflows
            ? SvWorkflowAvailability.Available
            : project.Health.CanOpenReadOnlyWorkflows
                ? SvWorkflowAvailability.ReadOnly
                : SvWorkflowAvailability.Disabled;

        return new SvWorkflowSummary(
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

    public static ValidationDiagnostic Warning(string message, string? file = null, string? field = null, string? expected = null)
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Warning,
            message,
            file,
            Domain: "sv.editor",
            Field: field,
            Expected: expected);
    }
}
