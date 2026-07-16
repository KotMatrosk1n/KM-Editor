// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.HyperTraining;

public sealed class SwShHyperTrainingWorkflowService
{
    public const string ScriptPath = "romfs/bin/script/amx/hyper_training.amx";
    public const string EnglishDialoguePath = "romfs/bin/message/English/script/sub_event_007.dat";
    public const string ExeFsMainPath = "exefs/main";

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Hyper Training requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Hyper Training requires Pokemon Sword or Pokemon Shield to be selected.",
                    expected: "Pokemon Sword or Pokemon Shield"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShHyperTrainingWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var sources = new List<SwShHyperTrainingSourceRecord>();

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            var installMessage = project.Health.CanOpenReadOnlyWorkflows
                && !IsSupportedGame(project.Paths.SelectedGame)
                    ? "Hyper Training cannot load until Pokemon Sword or Pokemon Shield is selected."
                    : "Hyper Training cannot load until project paths validate.";
            return CreateWorkflow(
                summary,
                "disabled",
                installMessage,
                CreateDefaultAnalysis(),
                mainAnalysis: null,
                dialogueAnalysis: null,
                sources,
                outputFileCount: 0,
                diagnostics);
        }

        var scriptSource = AddScriptSource(project, sources, diagnostics);
        var dialogueSource = AddDialogueSource(project, sources, diagnostics);
        var mainSource = AddMainSource(project, sources, diagnostics);

        if (scriptSource is null || mainSource is null)
        {
            return CreateWorkflow(
                summary,
                "blocked",
                scriptSource is null
                    ? "Hyper Training cannot inspect the level check because hyper_training.amx is missing."
                    : "Hyper Training cannot inspect the picker cutoff because exefs/main is missing.",
                CreateDefaultAnalysis(),
                mainAnalysis: null,
                dialogueAnalysis: null,
                sources,
                outputFileCount: 0,
                diagnostics);
        }

        try
        {
            var baseScriptPath = ResolveBaseSourcePath(project.Paths, ScriptPath);
            var baseMainPath = ResolveBaseSourcePath(project.Paths, ExeFsMainPath);
            if (baseScriptPath is null || baseMainPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Hyper Training requires readable vanilla base script and executable sources.",
                    file: baseScriptPath is null ? ScriptPath : ExeFsMainPath,
                    expected: "Selected-game vanilla base source"));
                return CreateWorkflow(
                    summary,
                    "blocked",
                    "Hyper Training cannot edit because a required vanilla base source is missing.",
                    CreateDefaultAnalysis(),
                    mainAnalysis: null,
                    dialogueAnalysis: null,
                    sources,
                    outputFileCount: 0,
                    diagnostics);
            }

            var baseScriptAnalysis = SwShHyperTrainingAmxPatcher.Analyze(File.ReadAllBytes(baseScriptPath));
            var analysis = SwShHyperTrainingAmxPatcher.Analyze(File.ReadAllBytes(scriptSource.AbsolutePath));
            var baseMainBytes = File.ReadAllBytes(baseMainPath);
            var mainBytes = File.ReadAllBytes(mainSource.AbsolutePath);
            var baseMainAnalysis = SwShHyperTrainingMainPatcher.Analyze(
                baseMainBytes,
                project.Paths.SelectedGame);
            var mainAnalysis = SwShHyperTrainingMainPatcher.Analyze(
                mainBytes,
                project.Paths.SelectedGame);
            SwShHyperTrainingDialogueAnalysis? dialogueAnalysis = null;
            if (dialogueSource is not null)
            {
                dialogueAnalysis = SwShHyperTrainingDialoguePatcher.Analyze(
                    File.ReadAllBytes(dialogueSource.AbsolutePath));
            }

            var baseIsValid = true;
            if (baseScriptAnalysis.Kind != SwShHyperTrainingScriptKind.NotInstalled)
            {
                baseIsValid = false;
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Hyper Training base AMX is not the verified vanilla Lv.100 script.",
                    file: ScriptPath,
                    expected: "Vanilla Hyper Training AMX at Lv.100"));
            }

            if (baseMainAnalysis.Kind != SwShHyperTrainingMainKind.NotInstalled)
            {
                baseIsValid = false;
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Hyper Training base exefs/main is not a verified selected-game vanilla source: {baseMainAnalysis.Message}",
                    file: ExeFsMainPath,
                    expected: "Vanilla selected-game Sword/Shield 1.3.2 exefs/main"));
            }

            var baseDialoguePath = ResolveBaseSourcePath(project.Paths, EnglishDialoguePath);
            if (baseDialoguePath is not null
                && SwShHyperTrainingDialoguePatcher.Analyze(File.ReadAllBytes(baseDialoguePath)).Kind
                    != SwShHyperTrainingDialogueKind.NotInstalled)
            {
                baseIsValid = false;
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "English Hyper Training dialogue base is not the verified vanilla Lv.100 table.",
                    file: EnglishDialoguePath,
                    expected: "Vanilla English Hyper Training dialogue"));
            }

            if (baseIsValid)
            {
                try
                {
                    SwShHyperTrainingMainPatcher.EnsureCompatibleExecutableIdentity(baseMainBytes, mainBytes);
                }
                catch (InvalidDataException exception)
                {
                    baseIsValid = false;
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        exception.Message,
                        file: ExeFsMainPath,
                        expected: "Effective exefs/main matching the vanilla base build and layout"));
                }
            }

            if (analysis.Kind == SwShHyperTrainingScriptKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: scriptSource.Entry.RelativePath,
                    expected: "Dedicated Battle Tower hyper_training.amx with the known level check at AMX cell 2294"));
            }

            if (mainAnalysis.Kind is SwShHyperTrainingMainKind.UnsupportedBuild
                or SwShHyperTrainingMainKind.GameMismatch
                or SwShHyperTrainingMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    mainAnalysis.Message,
                    file: mainSource.Entry.RelativePath,
                    expected: "Selected-game Sword/Shield 1.3.2 exefs/main with known Hyper Training picker level checks"));
            }

            if (dialogueAnalysis?.Kind == SwShHyperTrainingDialogueKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    dialogueAnalysis.Message,
                    file: dialogueSource!.Entry.RelativePath,
                    expected: "English Hyper Training dialogue with one matching Lv. token in lines 0 and 3"));
            }

            var hasBlockingAnalysis =
                !baseIsValid
                || analysis.Kind == SwShHyperTrainingScriptKind.Conflict
                || mainAnalysis.Kind is SwShHyperTrainingMainKind.UnsupportedBuild
                    or SwShHyperTrainingMainKind.GameMismatch
                    or SwShHyperTrainingMainKind.Conflict
                || dialogueAnalysis?.Kind == SwShHyperTrainingDialogueKind.Conflict;
            var levelsMatch = analysis.MinimumLevel == mainAnalysis.MinimumLevel
                && (dialogueAnalysis is null
                    || dialogueAnalysis.MinimumLevel == analysis.MinimumLevel);
            if (!hasBlockingAnalysis && !levelsMatch)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Hyper Training is out of sync: {FormatCutoffState(analysis, mainAnalysis, dialogueAnalysis)} Apply this editor again to synchronize every available cutoff.",
                    file: ExeFsMainPath,
                    expected: "Script, picker, and available English dialogue cutoff levels match"));
            }

            var effectiveMinimumLevel = SelectEffectiveMinimumLevel(analysis, mainAnalysis);
            var installStatus = hasBlockingAnalysis
                ? "blocked"
                : effectiveMinimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel && levelsMatch
                    ? summary.Availability == SwShWorkflowAvailability.Available ? "available" : "readOnly"
                    : "installed";
            var installMessage = hasBlockingAnalysis
                ? "Hyper Training cannot edit because one or more required source mappings are unsupported or conflicting."
                : !levelsMatch
                    ? $"Hyper Training is out of sync: {FormatCutoffState(analysis, mainAnalysis, dialogueAnalysis)}"
                : effectiveMinimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                    ? "Hyper Training is using the vanilla Lv.100 minimum."
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Hyper Training currently accepts Pokemon at Lv.{effectiveMinimumLevel} or higher.");

            return CreateWorkflow(
                summary,
                installStatus,
                installMessage,
                analysis,
                mainAnalysis,
                dialogueAnalysis,
                sources,
                outputFileCount: dialogueSource is not null ? 3 : 2,
                diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training script could not be inspected: {exception.Message}",
                file: scriptSource.Entry.RelativePath,
                expected: "Readable Sword/Shield Hyper Training AMX script"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Hyper Training cannot inspect the level check because a required source file could not be read.",
                CreateDefaultAnalysis(),
                mainAnalysis: null,
                dialogueAnalysis: null,
                sources,
                outputFileCount: 0,
                diagnostics);
        }
    }

    internal static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var entry = FindEntry(project, relativePath);
        if (entry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(entry, sourcePath)
            : null;
    }

    internal static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
        }

        return null;
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var normalizedRelativePath = targetRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, normalizedRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);

        return PathContainment.IsWithinRoot(pathFromOutputRoot)
            ? targetPath
            : null;
    }

    private static SwShHyperTrainingWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        SwShHyperTrainingScriptAnalysis analysis,
        SwShHyperTrainingMainAnalysis? mainAnalysis,
        SwShHyperTrainingDialogueAnalysis? dialogueAnalysis,
        IReadOnlyList<SwShHyperTrainingSourceRecord> sources,
        int outputFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var effectiveMinimumLevel = mainAnalysis is null
            ? analysis.MinimumLevel
            : SelectEffectiveMinimumLevel(analysis, mainAnalysis);
        return new SwShHyperTrainingWorkflow(
            summary,
            installStatus,
            installMessage,
            mainAnalysis?.BuildId ?? "unknown",
            mainAnalysis?.DetectedGame,
            new SwShHyperTrainingLevelRule(
                effectiveMinimumLevel,
                analysis.MinimumLevel,
                mainAnalysis?.MinimumLevel ?? SwShHyperTrainingAmxPatcher.VanillaMinimumLevel,
                dialogueAnalysis?.Kind == SwShHyperTrainingDialogueKind.Conflict
                    ? null
                    : dialogueAnalysis?.MinimumLevel,
                mainAnalysis is not null
                    && analysis.MinimumLevel == mainAnalysis.MinimumLevel
                    && (dialogueAnalysis is null
                        || (dialogueAnalysis.Kind != SwShHyperTrainingDialogueKind.Conflict
                            && dialogueAnalysis.MinimumLevel == analysis.MinimumLevel)),
                SwShHyperTrainingAmxPatcher.VanillaMinimumLevel,
                SwShHyperTrainingAmxPatcher.MinimumAllowedLevel,
                SwShHyperTrainingAmxPatcher.MaximumAllowedLevel,
                analysis.ScriptCell,
                dialogueAnalysis is null
                    ? "English dialogue is unavailable, so no dialogue output will be staged."
                    : dialogueAnalysis.Kind == SwShHyperTrainingDialogueKind.Conflict
                        ? "English dialogue exists but its cutoff mapping could not be verified."
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"English dialogue lines {SwShHyperTrainingDialoguePatcher.IntroLineIndex} and {SwShHyperTrainingDialoguePatcher.LevelFailureLineIndex} use Lv.{dialogueAnalysis.MinimumLevel}."),
                mainAnalysis is null
                    ? "Runtime picker cutoff is unavailable until exefs/main can be inspected."
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Picker cutoff lives at {mainAnalysis.PatchOffsetHex} and related Hyper Training list/detail checks.")),
            sources,
            new SwShHyperTrainingWorkflowStats(sources.Count(source => source.Status == "available"), outputFileCount),
            diagnostics);
    }

    private static SwShHyperTrainingScriptAnalysis CreateDefaultAnalysis()
    {
        return new SwShHyperTrainingScriptAnalysis(
            SwShHyperTrainingScriptKind.NotInstalled,
            "Hyper Training is using the vanilla Lv.100 minimum.",
            SwShHyperTrainingAmxPatcher.VanillaMinimumLevel,
            SwShHyperTrainingAmxPatcher.LevelThresholdCellLabel);
    }

    private static WorkflowFileSource? AddScriptSource(
        OpenedProject project,
        ICollection<SwShHyperTrainingSourceRecord> sources,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var scriptSource = ResolveWorkflowFile(project, ScriptPath);
        if (scriptSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training script is missing.",
                file: ScriptPath,
                expected: "romfs/bin/script/amx/hyper_training.amx"));
            sources.Add(CreateMissingSource("script", "Hyper Training script", ScriptPath, required: true));
            return null;
        }

        sources.Add(CreateSource("script", "Hyper Training script", scriptSource.Entry, "available"));
        return scriptSource;
    }

    private static WorkflowFileSource? AddDialogueSource(
        OpenedProject project,
        ICollection<SwShHyperTrainingSourceRecord> sources,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var dialogueSource = ResolveWorkflowFile(project, EnglishDialoguePath);
        if (dialogueSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "English Hyper Training dialogue was not found. The level cutoff can still be patched, but NPC text will not be staged.",
                file: EnglishDialoguePath,
                expected: "romfs/bin/message/English/script/sub_event_007.dat"));
            sources.Add(CreateMissingSource("dialogue", "English Hyper Training dialogue", EnglishDialoguePath, required: false));
            return null;
        }

        sources.Add(CreateSource("dialogue", "English Hyper Training dialogue", dialogueSource.Entry, "available"));
        return dialogueSource;
    }

    internal static string? ResolveBaseSourcePath(ProjectPaths paths, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var rootPath = relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            ? paths.BaseRomFsPath
            : relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
                ? paths.BaseExeFsPath
                : null;
        var pathWithinRoot = relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            ? relativePath["romfs/".Length..]
            : relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
                ? relativePath["exefs/".Length..]
                : null;
        if (string.IsNullOrWhiteSpace(rootPath) || pathWithinRoot is null)
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(
            fullRoot,
            pathWithinRoot.Replace('/', Path.DirectorySeparatorChar)));
        return PathContainment.IsWithinRoot(Path.GetRelativePath(fullRoot, candidate))
            && File.Exists(candidate)
                ? candidate
                : null;
    }

    private static WorkflowFileSource? AddMainSource(
        OpenedProject project,
        ICollection<SwShHyperTrainingSourceRecord> sources,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var mainSource = ResolveWorkflowFile(project, ExeFsMainPath);
        if (mainSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main is missing. Hyper Training needs it to patch the party/box picker cutoff checks.",
                file: ExeFsMainPath,
                expected: "exefs/main"));
            sources.Add(CreateMissingSource("runtime", "Hyper Training picker runtime", ExeFsMainPath, required: true));
            return null;
        }

        sources.Add(CreateSource("runtime", "Hyper Training picker runtime", mainSource.Entry, "available"));
        return mainSource;
    }

    private static int SelectEffectiveMinimumLevel(
        SwShHyperTrainingScriptAnalysis scriptAnalysis,
        SwShHyperTrainingMainAnalysis mainAnalysis)
    {
        if (scriptAnalysis.Kind == SwShHyperTrainingScriptKind.CustomMinimumLevel)
        {
            return scriptAnalysis.MinimumLevel;
        }

        if (mainAnalysis.Kind == SwShHyperTrainingMainKind.CustomMinimumLevel)
        {
            return mainAnalysis.MinimumLevel;
        }

        return scriptAnalysis.MinimumLevel;
    }

    private static string FormatCutoffState(
        SwShHyperTrainingScriptAnalysis scriptAnalysis,
        SwShHyperTrainingMainAnalysis mainAnalysis,
        SwShHyperTrainingDialogueAnalysis? dialogueAnalysis)
    {
        var dialogue = dialogueAnalysis is null
            ? "English dialogue unavailable."
            : dialogueAnalysis.Kind == SwShHyperTrainingDialogueKind.Conflict
                ? "English dialogue unverified."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"English dialogue Lv.{dialogueAnalysis.MinimumLevel}.");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"NPC script Lv.{scriptAnalysis.MinimumLevel}, picker Lv.{mainAnalysis.MinimumLevel}, {dialogue}");
    }

    private static ProjectFileGraphEntry? FindEntry(OpenedProject project, string relativePath)
    {
        return project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShHyperTrainingSourceRecord CreateSource(
        string sourceId,
        string label,
        ProjectFileGraphEntry entry,
        string status)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShHyperTrainingSourceRecord(
            sourceId,
            label,
            entry.RelativePath,
            status,
            new SwShHyperTrainingProvenance(entry.RelativePath, sourceLayer, entry.State));
    }

    private static SwShHyperTrainingSourceRecord CreateMissingSource(
        string sourceId,
        string label,
        string relativePath,
        bool required)
    {
        return new SwShHyperTrainingSourceRecord(
            sourceId,
            label,
            relativePath,
            required ? "missing" : "optionalMissing",
            new SwShHyperTrainingProvenance(
                relativePath,
                ProjectFileLayer.Generated,
                ProjectFileGraphEntryState.BaseOnly));
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.HyperTraining,
            "Hyper Training",
            "Advanced editor for the Battle Tower Hyper Training NPC minimum level cutoff, matching English dialogue, and picker cutoff checks.",
            availability,
            diagnostics);
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
            Domain: SwShHyperTrainingEditSessionService.HyperTrainingEditDomain,
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry Entry,
        string AbsolutePath);
}
