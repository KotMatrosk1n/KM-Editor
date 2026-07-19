// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;

namespace KM.ZA.Workflows;

internal static class ZaEditSessionSupport
{
    public const string PokemonDomain = "workflow.pokemon";
    public const string ItemsDomain = "workflow.items";
    public const string MovesDomain = "workflow.moves";
    public const string TextDomain = "workflow.text";
    public const string ShopsDomain = "workflow.shops";
    public const string TrainersDomain = "workflow.trainers";
    public const string PlacementDomain = "workflow.placement";
    public const string EncountersDomain = "workflow.encounters";
    public const string StaticEncountersDomain = "workflow.staticEncounters";
    public const string GiftPokemonDomain = "workflow.giftPokemon";
    public const string TradePokemonDomain = "workflow.tradePokemon";
    public const string TypeChartDomain = "workflow.typeChart";
    public const string AngeFightDomain = "workflow.angeFight";

    public static bool CanEdit(
        OpenedProject project,
        ZaWorkflowSummary summary,
        IEnumerable<ValidationDiagnostic> workflowDiagnostics,
        string domain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (project.Paths.SelectedGame is not ProjectGame.ZA)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon Legends Z-A edit sessions require a Pokemon Legends Z-A project.",
                domain,
                expected: "Pokemon Legends Z-A project"));
            return false;
        }

        if (!project.Health.CanOpenEditableWorkflows || summary.Availability != ZaWorkflowAvailability.Available)
        {
            var supportDiagnostics = summary.Diagnostics
                .Where(diagnostic => diagnostic.Field == "pokemonLegendsZASupportFolderPath")
                .ToArray();
            foreach (var diagnostic in supportDiagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            if (supportDiagnostics.Length > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon Legends Z-A data edits require the support folder to be configured in Project Setup.",
                    domain,
                    field: "pokemonLegendsZASupportFolderPath",
                    expected: "Folder containing oo2core_8_win64.dll"));
                return false;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon Legends Z-A edit sessions require valid base paths and a valid output root.",
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

    public static bool ValidateOptionValue(
        int value,
        IEnumerable<int> allowedValues,
        string domain,
        string? field,
        string message,
        string expected,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (allowedValues.Contains(value))
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            domain,
            field: field,
            expected: expected));
        return false;
    }

    public static ChangePlan CreateSingleFileChangePlan(
        ProjectPaths paths,
        EditSession session,
        string domain,
        string virtualPath,
        string workflowName,
        IReadOnlyList<ValidationDiagnostic> validationDiagnostics,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
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
            writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
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
        if (outputMode == ZaOutputMode.Standalone)
        {
            var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
            writes.Add(new PlannedFileWrite(
                descriptorWriteInfo.TargetRelativePath,
                descriptorWriteInfo.Sources,
                descriptorWriteInfo.ReplacesExistingOutput,
                "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides."));
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
            || !currentPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedWrites = reviewedPlan.Writes
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
        var currentWrites = currentPlan.Writes
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < currentWrites.Length; index++)
        {
            var reviewed = reviewedWrites[index];
            var current = currentWrites[index];
            if (!string.Equals(
                    reviewed.TargetRelativePath,
                    current.TargetRelativePath,
                    StringComparison.Ordinal)
                || reviewed.ReplacesExistingOutput != current.ReplacesExistingOutput
                || !string.Equals(reviewed.Reason, current.Reason, StringComparison.Ordinal)
                || !string.Equals(
                    reviewed.SourceFingerprint,
                    current.SourceFingerprint,
                    StringComparison.Ordinal)
                || !reviewed.Sources
                    .OrderBy(source => source.Layer)
                    .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                    .SequenceEqual(
                        current.Sources
                            .OrderBy(source => source.Layer)
                            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)))
            {
                return false;
            }
        }

        return true;
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
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        var relativePath = outputMode == ZaOutputMode.TrinityModManager
            ? virtualPath
            : $"romfs/{virtualPath}";
        return new ProjectFileReference(ProjectFileLayer.Generated, relativePath);
    }

    public static ProjectFileReference GeneratedDescriptorReference()
    {
        return GeneratedReference(ZaWorkflowFileSource.DescriptorVirtualPath);
    }

    public static string CreateApplyOutputMessage(string workflowName, ZaOutputMode outputMode)
    {
        return outputMode switch
        {
            ZaOutputMode.Standalone => $"{workflowName} output was written as a standalone LayeredFS override with a patched descriptor.",
            ZaOutputMode.TrinityModManager => $"{workflowName} output was written in Trinity Mod Manager layout.",
            ZaOutputMode.TrinityBypass => $"{workflowName} output was written in Trinity bypass layout.",
            _ => $"{workflowName} output was written.",
        };
    }

    public static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string domain,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            file,
            domain,
            field,
            expected);
    }

    private static bool IsSamePendingEdit(PendingEdit existing, PendingEdit candidate)
    {
        return string.Equals(existing.Domain, candidate.Domain, StringComparison.Ordinal)
            && string.Equals(existing.RecordId, candidate.RecordId, StringComparison.Ordinal)
            && string.Equals(existing.Field, candidate.Field, StringComparison.Ordinal);
    }
}
