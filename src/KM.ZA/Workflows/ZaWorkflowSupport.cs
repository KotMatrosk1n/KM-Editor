// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA;

namespace KM.ZA.Workflows;

internal static class ZaWorkflowSupport
{
    public static ZaWorkflowSummary CreateSummary(
        OpenedProject project,
        string id,
        string label,
        string description,
        IReadOnlyList<ValidationDiagnostic>? diagnostics = null)
    {
        var diagnosticsList = diagnostics ?? project.Health.Diagnostics;
        var availability = project.Health.CanOpenEditableWorkflows
            ? ZaWorkflowAvailability.Available
            : project.Health.CanOpenReadOnlyWorkflows
                ? ZaWorkflowAvailability.ReadOnly
                : ZaWorkflowAvailability.Disabled;
        if (availability != ZaWorkflowAvailability.Disabled
            && !ZaCompressionRuntime.IsConfigured(project.Paths.PokemonLegendsZASupportFolderPath))
        {
            availability = ZaWorkflowAvailability.Disabled;
            diagnosticsList = diagnosticsList
                .Append(CreateMissingSupportDiagnostic())
                .ToArray();
        }

        return new ZaWorkflowSummary(
            id,
            label,
            description,
            availability,
            diagnosticsList);
    }

    public static bool HasCompressionSupport(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ZaCompressionRuntime.IsConfigured(project.Paths.PokemonLegendsZASupportFolderPath);
    }

    public static ValidationDiagnostic CreateMissingSupportDiagnostic()
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Warning,
            "oo2core_8_win64.dll folder is not configured. Set it in Project Setup to enable Z-A data editors.",
            File: null,
            Domain: "za.editor",
            Field: "pokemonLegendsZASupportFolderPath",
            Expected: "Folder containing oo2core_8_win64.dll");
    }

    public static ValidationDiagnostic Error(string message, string? file = null, string? field = null, string? expected = null)
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Error,
            message,
            file,
            Domain: "za.editor",
            Field: field,
            Expected: expected);
    }

    public static ValidationDiagnostic Warning(string message, string? file = null, string? field = null, string? expected = null)
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Warning,
            message,
            file,
            Domain: "za.editor",
            Field: field,
            Expected: expected);
    }
}
