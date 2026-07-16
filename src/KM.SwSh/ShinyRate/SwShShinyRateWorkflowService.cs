// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
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

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shiny Rate requires Pokemon Sword or Pokemon Shield to be selected before it can load.",
                    expected: "Selected Pokemon Sword or Pokemon Shield project"));
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
            var installMessage = project.Health.CanOpenReadOnlyWorkflows
                && !IsSupportedGame(project.Paths.SelectedGame)
                    ? "Shiny Rate cannot load until Pokemon Sword or Pokemon Shield is selected."
                    : "Shiny Rate cannot load until project paths validate.";
            return CreateWorkflow(
                summary,
                "disabled",
                installMessage,
                CreateUnavailableAnalysis(),
                source: null,
                sourceFileCount: 0,
                outputFileCount: 0,
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
                CreateUnavailableAnalysis(),
                CreateMissingSource(),
                sourceFileCount: 0,
                outputFileCount: 0,
                diagnostics);
        }

        var source = CreateSource(mainSource.Entry, "available");
        var outputFileCount = mainSource.Entry.LayeredFile is null ? 0 : 1;
        var basePath = ResolveBaseSourcePath(project.Paths, ExeFsMainPath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Base ExeFS main could not be resolved from the project graph.",
                file: ExeFsMainPath,
                expected: "Readable selected-game vanilla Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Shiny Rate cannot verify the selected-game vanilla base exefs/main.",
                CreateUnavailableAnalysis(),
                source,
                sourceFileCount: 1,
                outputFileCount,
                diagnostics);
        }

        SwShShinyRateMainAnalysis baseAnalysis;
        SwShShinyRateMainAnalysis effectiveAnalysis;
        byte[] baseBytes;
        byte[] effectiveBytes;
        try
        {
            baseBytes = File.ReadAllBytes(basePath);
            effectiveBytes = PathsReferToSameFile(basePath, mainSource.AbsolutePath)
                ? baseBytes
                : File.ReadAllBytes(mainSource.AbsolutePath);
            baseAnalysis = SwShShinyRateMainPatcher.Analyze(
                baseBytes,
                project.Paths.SelectedGame);
            effectiveAnalysis = PathsReferToSameFile(basePath, mainSource.AbsolutePath)
                ? baseAnalysis
                : SwShShinyRateMainPatcher.Analyze(
                    effectiveBytes,
                    project.Paths.SelectedGame);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main could not be read for Shiny Rate verification.",
                file: ExeFsMainPath,
                expected: "Readable selected-game base and effective Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Shiny Rate cannot inspect the reroll loop because an exefs/main source could not be read.",
                CreateUnavailableAnalysis(),
                source,
                sourceFileCount: 1,
                outputFileCount,
                diagnostics);
        }

        const int sourceFileCount = 1;
        if (baseAnalysis.Kind != SwShShinyRateMainKind.Default)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Base exefs/main is not a selected-game vanilla Shiny Rate source. {baseAnalysis.Message}",
                file: ExeFsMainPath,
                expected: "Selected-game Sword/Shield 1.3.2 base exefs/main with vanilla shiny reroll logic"));
        }

        AddEffectiveAnalysisDiagnostic(effectiveAnalysis, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateWorkflow(
                summary,
                "blocked",
                baseAnalysis.Kind != SwShShinyRateMainKind.Default
                    ? "Shiny Rate requires a verified selected-game vanilla base exefs/main before it can edit or restore the effective source."
                    : effectiveAnalysis.Message,
                effectiveAnalysis,
                source,
                sourceFileCount,
                outputFileCount,
                diagnostics);
        }

        try
        {
            SwShShinyRateMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, effectiveBytes);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Compatible selected-game base and effective Sword/Shield exefs/main NSOs"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Shiny Rate cannot edit because the base and effective exefs/main identities do not match.",
                effectiveAnalysis,
                source,
                sourceFileCount,
                outputFileCount,
                diagnostics);
        }

        var installStatus = effectiveAnalysis.Kind switch
        {
            SwShShinyRateMainKind.FixedRolls => "fixed",
            SwShShinyRateMainKind.AlwaysShiny => "always",
            _ => summary.Availability == SwShWorkflowAvailability.Available
                ? "available"
                : "readOnly",
        };

        return CreateWorkflow(
            summary,
            installStatus,
            effectiveAnalysis.Message,
            effectiveAnalysis,
            source,
            sourceFileCount,
            outputFileCount,
            diagnostics);
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

    internal static string? ResolveBaseSourcePath(ProjectPaths paths, string relativePath)
    {
        return SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, relativePath);
    }

    internal IReadOnlyList<ProjectFileReference> GetPlanSources(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var source = ResolveWorkflowFile(project, ExeFsMainPath);
        if (source is null)
        {
            return Array.Empty<ProjectFileReference>();
        }

        var sources = new List<ProjectFileReference>(2);
        if (source.Entry.BaseFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, ExeFsMainPath));
        }

        if (source.Entry.LayeredFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Layered, ExeFsMainPath));
        }

        return sources
            .Distinct()
            .OrderBy(candidate => candidate.Layer)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<SwShShinyRatePreset> PresetDefinitions => Presets;

    private static SwShShinyRateWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        SwShShinyRateMainAnalysis analysis,
        SwShShinyRateSourceRecord? source,
        int sourceFileCount,
        int outputFileCount,
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
            new SwShShinyRateWorkflowStats(sourceFileCount, outputFileCount, Presets.Length),
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
        var chance = analysis.Chance;
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
            chance is null ? null : chance.Value * 100,
            analysis.Kind == SwShShinyRateMainKind.Default ? "Dynamic" : FormatOdds(oddsDenominator),
            analysis.Kind == SwShShinyRateMainKind.Default ? "Variable" : FormatPercent(chance),
            analysis.Kind == SwShShinyRateMainKind.Default
                ? "Default restores the game's original runtime-dependent reroll count calculation."
                : analysis.Kind == SwShShinyRateMainKind.FixedRolls
                    ? "Fixed writes a global PID roll count for random shiny checks."
                    : analysis.Kind == SwShShinyRateMainKind.AlwaysShiny
                        ? "Always Shiny NOPs the loop break branch."
                        : "Runtime shiny rate is unavailable until exefs/main can be inspected.");
    }

    private static SwShShinyRateMainAnalysis CreateUnavailableAnalysis()
    {
        return new SwShShinyRateMainAnalysis(
            SwShShinyRateMainKind.Conflict,
            "Shiny Rate is unavailable until exefs/main can be inspected.",
            "unknown",
            "unknown",
            "unknown",
            "unknown",
            RollCount: null,
            Chance: null,
            OddsDenominator: null,
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
        return new SwShShinyRatePreset(
            "default",
            "Default",
            "default",
            RollCount: null,
            TargetDenominator: null,
            IsEnabled: true,
            "Dynamic",
            "Variable",
            "Restores the game's runtime-dependent shiny reroll logic.");
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

    internal static string FormatPercent(double? chance)
    {
        return chance is null
            ? "Unknown"
            : string.Create(CultureInfo.InvariantCulture, $"{chance.Value * 100:0.000}%");
    }

    private static void AddEffectiveAnalysisDiagnostic(
        SwShShinyRateMainAnalysis analysis,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (analysis.Kind is not (SwShShinyRateMainKind.UnsupportedBuild
            or SwShShinyRateMainKind.GameMismatch
            or SwShShinyRateMainKind.MissingFunction
            or SwShShinyRateMainKind.AmbiguousFunction
            or SwShShinyRateMainKind.Conflict))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            analysis.Message,
            file: ExeFsMainPath,
            expected: "Selected-game Sword/Shield 1.3.2 effective exefs/main with the verified shiny reroll loop"));
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    internal static bool IsSupportedGame(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
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
