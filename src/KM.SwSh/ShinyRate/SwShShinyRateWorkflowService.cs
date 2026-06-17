// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.HyperTraining;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.ShinyRate;

public sealed class SwShShinyRateWorkflowService
{
    public const string ExeFsMainPath = "exefs/main";

    private static readonly SwShShinyRatePreset[] Presets =
    [
        CreateDisabledPreset(
            "gen3",
            "Gen 3",
            targetDenominator: 8192,
            "Not available yet. The current patch can make odds more common, but 1/8192 needs a separate shiny-threshold patch."),
        CreateDefaultPreset(),
        CreateFixedPreset("shinyCharm", "Shiny Charm", rollCount: 3, targetDenominator: 1366),
        CreateFixedPreset("masuda", "Masuda", rollCount: 6, targetDenominator: 683),
        CreateFixedPreset("masudaCharm", "Masuda + Shiny Charm", rollCount: 8, targetDenominator: 512),
        CreateAlwaysPreset(),
    ];

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shiny Rate requires a valid base ExeFS path before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShShinyRateWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                "disabled",
                "Shiny Rate cannot load until project paths validate.",
                CreateDefaultAnalysis(),
                source: null,
                diagnostics);
        }

        var mainSource = ResolveWorkflowFile(project, ExeFsMainPath);
        if (mainSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main is missing. Shiny Rate needs it to inspect the shiny reroll loop.",
                file: ExeFsMainPath,
                expected: "exefs/main"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Shiny Rate cannot inspect the reroll loop because exefs/main is missing.",
                CreateDefaultAnalysis(),
                CreateMissingSource(),
                diagnostics);
        }

        var source = CreateSource(mainSource.Entry, "available");
        try
        {
            var analysis = SwShShinyRateMainPatcher.Analyze(
                File.ReadAllBytes(mainSource.AbsolutePath),
                project.Paths.SelectedGame);

            if (analysis.Kind is SwShShinyRateMainKind.UnsupportedBuild
                or SwShShinyRateMainKind.GameMismatch
                or SwShShinyRateMainKind.MissingFunction
                or SwShShinyRateMainKind.AmbiguousFunction
                or SwShShinyRateMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: ExeFsMainPath,
                    expected: "Selected-game Sword/Shield 1.3.2 exefs/main with the verified shiny reroll loop"));
            }

            var isBlocked = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                || analysis.Kind is SwShShinyRateMainKind.UnsupportedBuild
                    or SwShShinyRateMainKind.GameMismatch
                    or SwShShinyRateMainKind.MissingFunction
                    or SwShShinyRateMainKind.AmbiguousFunction
                    or SwShShinyRateMainKind.Conflict;
            var installStatus = isBlocked
                ? "blocked"
                : analysis.Kind switch
                {
                    SwShShinyRateMainKind.FixedRolls => "fixed",
                    SwShShinyRateMainKind.AlwaysShiny => "always",
                    _ => summary.Availability == SwShWorkflowAvailability.Available
                        ? "available"
                        : "readOnly",
                };

            return CreateWorkflow(summary, installStatus, analysis.Message, analysis, source, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shiny Rate could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable Sword/Shield exefs/main"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Shiny Rate cannot inspect the reroll loop because exefs/main could not be read.",
                CreateDefaultAnalysis(),
                source,
                diagnostics);
        }
    }

    internal static SwShHyperTrainingWorkflowService.WorkflowFileSource? ResolveWorkflowFile(
        OpenedProject project,
        string relativePath)
    {
        return SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, relativePath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        return SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, targetRelativePath);
    }

    public static IReadOnlyList<SwShShinyRatePreset> PresetDefinitions => Presets;

    private static SwShShinyRateWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        SwShShinyRateMainAnalysis analysis,
        SwShShinyRateSourceRecord? source,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShShinyRateWorkflow(
            summary,
            installStatus,
            installMessage,
            analysis.BuildId,
            analysis.FunctionOffsetHex,
            analysis.CompareOffsetHex,
            analysis.BreakOffsetHex,
            analysis.DetectedGame,
            source,
            CreateRule(analysis),
            Presets,
            new SwShShinyRateWorkflowStats(source?.Status == "available" ? 1 : 0, 1, Presets.Length),
            diagnostics);
    }

    private static SwShShinyRateRule CreateRule(SwShShinyRateMainAnalysis analysis)
    {
        var mode = analysis.Kind switch
        {
            SwShShinyRateMainKind.FixedRolls => "fixed",
            SwShShinyRateMainKind.AlwaysShiny => "always",
            SwShShinyRateMainKind.Default => "default",
            _ => "blocked",
        };
        var chance = analysis.Kind == SwShShinyRateMainKind.AlwaysShiny
            ? 1
            : analysis.RollCount is null
                ? 0
                : SwShShinyRateMainPatcher.CalculateChance(analysis.RollCount.Value);
        var oddsDenominator = analysis.Kind == SwShShinyRateMainKind.AlwaysShiny
            ? 1
            : analysis.OddsDenominator;

        return new SwShShinyRateRule(
            mode,
            analysis.RollCount,
            SwShShinyRateMainPatcher.MinimumFixedRollCount,
            SwShShinyRateMainPatcher.MaximumFixedRollCount,
            SwShShinyRateMainPatcher.MinimumCustomDenominator,
            SwShShinyRateMainPatcher.MaximumCustomDenominator,
            oddsDenominator,
            chance * 100,
            FormatOdds(oddsDenominator),
            FormatPercent(chance),
            analysis.Kind == SwShShinyRateMainKind.Default
                ? "Default restores the game's original reroll count calculation."
                : analysis.Kind == SwShShinyRateMainKind.FixedRolls
                    ? "Fixed writes a global PID roll count for random shiny checks."
                    : analysis.Kind == SwShShinyRateMainKind.AlwaysShiny
                        ? "Always Shiny NOPs the loop break branch."
                        : "Runtime shiny rate is unavailable until exefs/main can be inspected.");
    }

    private static SwShShinyRateMainAnalysis CreateDefaultAnalysis()
    {
        var chance = SwShShinyRateMainPatcher.CalculateChance(1);
        return new SwShShinyRateMainAnalysis(
            SwShShinyRateMainKind.Default,
            "Shiny Rate is unavailable until exefs/main can be inspected.",
            "unknown",
            "unknown",
            "unknown",
            "unknown",
            RollCount: 1,
            chance,
            SwShShinyRateMainPatcher.CalculateOddsDenominator(chance),
            DetectedGame: null);
    }

    private static SwShShinyRateSourceRecord CreateSource(ProjectFileGraphEntry entry, string status)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShShinyRateSourceRecord(
            "exefs-main",
            "ExeFS main",
            entry.RelativePath,
            status,
            new SwShShinyRateProvenance(entry.RelativePath, sourceLayer, entry.State));
    }

    private static SwShShinyRateSourceRecord CreateMissingSource()
    {
        return new SwShShinyRateSourceRecord(
            "exefs-main",
            "ExeFS main",
            ExeFsMainPath,
            "missing",
            new SwShShinyRateProvenance(
                ExeFsMainPath,
                ProjectFileLayer.Generated,
                ProjectFileGraphEntryState.BaseOnly));
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.ShinyRate,
            "Shiny Rate",
            "Advanced editor for the Sword/Shield shiny reroll count in exefs/main.",
            availability,
            diagnostics);
    }

    private static SwShShinyRatePreset CreateDefaultPreset()
    {
        var chance = SwShShinyRateMainPatcher.CalculateChance(1);
        return new SwShShinyRatePreset(
            "default",
            "Default",
            "default",
            RollCount: null,
            TargetDenominator: 4096,
            IsEnabled: true,
            FormatOdds(SwShShinyRateMainPatcher.CalculateOddsDenominator(chance)),
            FormatPercent(chance),
            "Restores the game's original shiny reroll logic.");
    }

    private static SwShShinyRatePreset CreateFixedPreset(
        string presetId,
        string label,
        int rollCount,
        int targetDenominator)
    {
        var chance = SwShShinyRateMainPatcher.CalculateChance(rollCount);
        return new SwShShinyRatePreset(
            presetId,
            label,
            "fixed",
            rollCount,
            targetDenominator,
            IsEnabled: true,
            FormatOdds(SwShShinyRateMainPatcher.CalculateOddsDenominator(chance)),
            FormatPercent(chance),
            string.Create(CultureInfo.InvariantCulture, $"Writes {rollCount} PID rolls."));
    }

    private static SwShShinyRatePreset CreateAlwaysPreset()
    {
        return new SwShShinyRatePreset(
            "always",
            "Always Shiny",
            "always",
            RollCount: null,
            TargetDenominator: 1,
            IsEnabled: true,
            "1/1",
            FormatPercent(1),
            "Forces random shiny checks to resolve as shiny.");
    }

    private static SwShShinyRatePreset CreateDisabledPreset(
        string presetId,
        string label,
        int targetDenominator,
        string description)
    {
        var chance = 1d / targetDenominator;
        return new SwShShinyRatePreset(
            presetId,
            label,
            "unsupported",
            RollCount: null,
            targetDenominator,
            IsEnabled: false,
            FormatOdds(targetDenominator),
            FormatPercent(chance),
            description);
    }

    internal static string FormatOdds(int? denominator)
    {
        return denominator is null
            ? "Unknown"
            : string.Create(CultureInfo.InvariantCulture, $"1/{denominator.Value:N0}");
    }

    internal static string FormatPercent(double chance)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{chance * 100:0.000}%");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: SwShShinyRateEditSessionService.ShinyRateEditDomain,
            Expected: expected);
    }
}
