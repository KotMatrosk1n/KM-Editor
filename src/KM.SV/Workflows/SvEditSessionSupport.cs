// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Workflows;

internal static class SvEditSessionSupport
{
    public const string ItemsDomain = "workflow.items";
    public const string MovesDomain = "workflow.moves";
    public const string PokemonDomain = "workflow.pokemon";
    public const string TrainersDomain = "workflow.trainers";
    public const string EncountersDomain = "workflow.encounters";
    public const string TeraRaidsDomain = "workflow.teraRaids";
    public const string StaticEncountersDomain = "workflow.staticEncounters";
    public const string GiftPokemonDomain = "workflow.giftPokemon";
    public const string TradePokemonDomain = "workflow.tradePokemon";
    public const string PlacementDomain = "workflow.placement";

    public static bool CanEdit(
        OpenedProject project,
        SvWorkflowSummary summary,
        IEnumerable<ValidationDiagnostic> workflowDiagnostics,
        string domain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SvWorkflowFileSource.IsScarletViolet(project.Paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Scarlet/Violet edit sessions require a Scarlet or Violet project.",
                domain,
                expected: "Scarlet/Violet project"));
            return false;
        }

        if (!project.Health.CanOpenEditableWorkflows || summary.Availability != SvWorkflowAvailability.Available)
        {
            var supportDiagnostics = summary.Diagnostics
                .Where(diagnostic => diagnostic.Field == "scarletVioletSupportFolderPath")
                .ToArray();
            foreach (var diagnostic in supportDiagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            if (supportDiagnostics.Length > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "S/V data edits require the oo2core_8_win64.dll folder to be configured in Project Setup.",
                    domain,
                    field: "scarletVioletSupportFolderPath",
                    expected: "Configured oo2core_8_win64.dll folder"));
                return false;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Scarlet/Violet edit sessions require valid base paths and a valid output root.",
                domain,
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflowDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    public static PendingEdit CreatePendingEdit(
        string domain,
        string summary,
        ProjectFileReference source,
        string recordId,
        string field,
        string newValue)
    {
        return new PendingEdit(domain, summary, [source], recordId, field, newValue);
    }

    public static EditSession ReplacePendingEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSamePendingEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    public static int? TryParseInt(
        string? value,
        int? minimumValue,
        int? maximumValue,
        string? field,
        string domain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Edit value must be an integer.",
                domain,
                field: field,
                expected: "Integer value"));
            return null;
        }

        if (minimumValue is not null && parsedValue < minimumValue.Value
            || maximumValue is not null && parsedValue > maximumValue.Value)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Edit value must be between {minimumValue} and {maximumValue}.",
                domain,
                field: field,
                expected: "Safe editor value"));
            return null;
        }

        return parsedValue;
    }

    public static ChangePlan CreateSingleFileChangePlan(
        ProjectPaths paths,
        EditSession session,
        string domain,
        string virtualPath,
        string workflowName,
        IReadOnlyList<ValidationDiagnostic> validationDiagnostics,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        var diagnostics = validationDiagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Create a pending {workflowName} edit before reviewing a change plan.",
                domain,
                expected: $"Pending {workflowName} edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        PlannedWriteInfo writeInfo;
        try
        {
            writeInfo = SvWorkflowFileSource.CreatePlannedWrite(
                paths,
                virtualPath,
                session.PendingEdits.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                outputMode);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{workflowName} change plan could not resolve the output target: {exception.Message}",
                domain,
                file: $"romfs/{virtualPath}",
                expected: "Writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var reason = session.PendingEdits.Count == 1
            ? $"Apply pending {workflowName} edit: {session.PendingEdits[0].Summary}"
            : $"Apply {session.PendingEdits.Count} pending {workflowName} edits.";
        var dataWrite = new PlannedFileWrite(
            writeInfo.TargetRelativePath,
            writeInfo.Sources,
            writeInfo.ReplacesExistingOutput,
            reason);
        var writes = new List<PlannedFileWrite> { dataWrite };
        if (outputMode == SvOutputMode.Standalone)
        {
            var descriptorWriteInfo = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
            writes.Add(new PlannedFileWrite(
                descriptorWriteInfo.TargetRelativePath,
                descriptorWriteInfo.Sources,
                descriptorWriteInfo.ReplacesExistingOutput,
                "Patch Scarlet/Violet Trinity descriptor for standalone LayeredFS overrides."));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target files.",
            domain));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        if (!reviewedPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedTargets = reviewedPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentTargets = currentPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return reviewedTargets.SequenceEqual(currentTargets, StringComparer.Ordinal);
    }

    public static ApplyResult CreateApplyResult(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan currentPlan,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics);
    }

    public static ProjectFileReference GeneratedReference(
        string virtualPath,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        var relativePath = outputMode == SvOutputMode.TrinityModManager
            ? virtualPath
            : $"romfs/{virtualPath}";
        return new ProjectFileReference(ProjectFileLayer.Generated, relativePath);
    }

    public static ProjectFileReference GeneratedDescriptorReference()
    {
        return GeneratedReference(SvWorkflowFileSource.DescriptorVirtualPath);
    }

    public static string CreateApplyOutputMessage(string workflowName, SvOutputMode outputMode)
    {
        return outputMode switch
        {
            SvOutputMode.Standalone =>
                $"Applied {workflowName} change plan as standalone Scarlet/Violet output and patched the Trinity descriptor.",
            SvOutputMode.TrinityModManager =>
                $"Applied {workflowName} change plan for Trinity Mod Manager. Run this output folder through Trinity Mod Manager before installing.",
            SvOutputMode.TrinityBypass =>
                $"Applied {workflowName} change plan for Trinity Bypass. Install with Trinity Bypass already active; KM Editor did not patch the Trinity descriptor.",
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, null),
        };
    }

    public static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string domain,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: domain,
            Field: field,
            Expected: expected);
    }

    private static bool IsSamePendingEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }
}
