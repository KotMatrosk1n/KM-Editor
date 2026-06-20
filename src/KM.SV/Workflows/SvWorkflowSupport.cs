// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SV;

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
        var diagnosticsList = diagnostics ?? project.Health.Diagnostics;
        var availability = project.Health.CanOpenEditableWorkflows
            ? SvWorkflowAvailability.Available
            : project.Health.CanOpenReadOnlyWorkflows
                ? SvWorkflowAvailability.ReadOnly
                : SvWorkflowAvailability.Disabled;
        if (availability != SvWorkflowAvailability.Disabled
            && !SvCompressionRuntime.IsConfigured(project.Paths.ScarletVioletSupportFolderPath))
        {
            availability = SvWorkflowAvailability.Disabled;
            diagnosticsList = diagnosticsList
                .Append(CreateMissingSupportDiagnostic())
                .ToArray();
        }

        return new SvWorkflowSummary(
            id,
            label,
            description,
            availability,
            diagnosticsList);
    }

    public static bool HasCompressionSupport(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return SvCompressionRuntime.IsConfigured(project.Paths.ScarletVioletSupportFolderPath);
    }

    public static ValidationDiagnostic CreateMissingSupportDiagnostic()
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Warning,
            "oo2core_8_win64.dll folder is not configured. Set it in Project Setup to enable S/V data editors.",
            File: null,
            Domain: "sv.editor",
            Field: "scarletVioletSupportFolderPath",
            Expected: "Configured oo2core_8_win64.dll folder");
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
