// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using System.Globalization;

namespace KM.SwSh.NameFilter;

public sealed class SwShProfanityFilterService
{
    private const string Domain = "tool.profanityFilter";
    private const string ExeFsMainPath = SwShExeFsPatchWorkflowService.ExeFsMainPath;

    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShProfanityFilterService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
    }

    public SwShProfanityFilterStatus Load(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);

        var mainStatus = AnalyzeMain(paths, diagnostics);
        return CreateStatus(mainStatus, diagnostics);
    }

    public SwShProfanityFilterApplyResult Apply(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);
        var writtenFiles = new List<ProjectFileReference>();

        var preparedMain = PrepareMainApply(paths, diagnostics);
        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            && preparedMain is not null)
        {
            WriteOutputFile(paths, ExeFsMainPath, preparedMain, diagnostics, writtenFiles);
        }

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                writtenFiles.Count == 0
                    ? "Profanity Filter was already installed."
                    : string.Create(CultureInfo.InvariantCulture, $"Profanity Filter installed {writtenFiles.Count:N0} output file(s).")));
        }

        return CreateApplyResult(paths, writtenFiles, diagnostics);
    }

    public SwShProfanityFilterApplyResult Restore(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);
        var writtenFiles = new List<ProjectFileReference>();

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter uninstall requires a configured Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory"));
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        RestoreMain(paths, diagnostics, writtenFiles);

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                writtenFiles.Count == 0
                    ? "Profanity Filter uninstall found no owned output to remove."
                    : string.Create(CultureInfo.InvariantCulture, $"Profanity Filter uninstalled {writtenFiles.Count:N0} output file(s).")));
        }

        return CreateApplyResult(paths, writtenFiles, diagnostics);
    }

    private byte[]? PrepareMainApply(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter requires Base ExeFS and Output Root before it can install.",
                expected: "Readable Base ExeFS and writable Output Root"));
            return null;
        }

        var basePath = Path.Combine(paths.BaseExeFsPath, "main");
        if (!File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter could not find base exefs/main.",
                file: ExeFsMainPath,
                expected: "Readable Sword/Shield 1.3.2 exefs/main"));
            return null;
        }

        var outputMainPath = SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, ExeFsMainPath);
        var sourcePath = outputMainPath is not null && File.Exists(outputMainPath)
            ? outputMainPath
            : basePath;

        try
        {
            var current = File.ReadAllBytes(sourcePath);
            var patched = SwShNameFilterMainPatcher.Apply(current, paths.SelectedGame);
            return patched.SequenceEqual(current) ? null : patched;
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Profanity Filter could not read exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Profanity Filter could not read exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Supported Sword/Shield 1.3.2 exefs/main with vanilla or KM Profanity Filter bytes"));
        }

        return null;
    }

    private void RestoreMain(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter uninstall requires Base ExeFS and Output Root.",
                expected: "Readable Base ExeFS and writable Output Root"));
            return;
        }

        var baseMainPath = Path.Combine(paths.BaseExeFsPath, "main");
        if (!File.Exists(baseMainPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter could not find base exefs/main.",
                file: ExeFsMainPath,
                expected: "Readable Sword/Shield 1.3.2 exefs/main"));
            return;
        }

        var outputMainPath = SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, ExeFsMainPath);
        if (outputMainPath is null || !File.Exists(outputMainPath))
        {
            return;
        }

        try
        {
            var current = File.ReadAllBytes(outputMainPath);
            var currentAnalysis = SwShNameFilterMainPatcher.Analyze(current, paths.SelectedGame);
            if (currentAnalysis.Kind == SwShNameFilterMainKind.NotInstalled)
            {
                return;
            }

            var baseBytes = File.ReadAllBytes(baseMainPath);
            var restored = SwShNameFilterMainPatcher.RestoreFromBase(current, baseBytes, paths.SelectedGame);
            if (restored.SequenceEqual(current))
            {
                return;
            }

            if (restored.SequenceEqual(baseBytes))
            {
                File.Delete(outputMainPath);
            }
            else
            {
                WriteBytesAtomic(outputMainPath, restored);
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, ExeFsMainPath));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Profanity Filter could not restore exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable base and output exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Profanity Filter could not restore exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable base and output exefs/main"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Output exefs/main containing KM-owned Profanity Filter bytes"));
        }
    }

    private MainStatus AnalyzeMain(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter requires Base ExeFS.",
                field: "baseExeFsPath",
                expected: "Readable Base ExeFS folder"));
            return MainStatus.Empty;
        }

        var baseMainPath = Path.Combine(paths.BaseExeFsPath, "main");
        var outputMainPath = string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? null
            : SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, ExeFsMainPath);
        var sourcePath = outputMainPath is not null && File.Exists(outputMainPath)
            ? outputMainPath
            : baseMainPath;
        var sourceLayer = outputMainPath is not null && File.Exists(outputMainPath)
            ? "layered"
            : "base";

        if (!File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter could not inspect exefs/main.",
                file: ExeFsMainPath,
                expected: "Readable base or output exefs/main"));
            return MainStatus.Empty;
        }

        try
        {
            var analysis = SwShNameFilterMainPatcher.Analyze(File.ReadAllBytes(sourcePath), paths.SelectedGame);
            if (analysis.Kind is SwShNameFilterMainKind.UnsupportedBuild
                or SwShNameFilterMainKind.GameMismatch
                or SwShNameFilterMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    analysis.Kind == SwShNameFilterMainKind.UnsupportedBuild ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    analysis.Message,
                    file: ExeFsMainPath,
                    expected: "Supported Sword/Shield 1.3.2 exefs/main"));
            }

            return new MainStatus(
                analysis.Kind,
                analysis.BuildId == "unknown" ? null : analysis.BuildId,
                analysis.DetectedGame,
                analysis.PatchOffsetHex,
                analysis.PatchShape,
                sourceLayer);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Profanity Filter could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Profanity Filter could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }

        return MainStatus.Empty;
    }

    private static SwShProfanityFilterStatus CreateStatus(
        MainStatus mainStatus,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var hasErrors = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string status;
        string message;

        if (hasErrors)
        {
            status = "blocked";
            message = "Profanity Filter has diagnostics that need attention.";
        }
        else
        {
            (status, message) = mainStatus.Kind switch
            {
                SwShNameFilterMainKind.Installed => ("installed", "Profanity Filter is installed."),
                SwShNameFilterMainKind.InstalledCompatible => ("compatible", "A compatible Profanity Filter patch is installed."),
                SwShNameFilterMainKind.NotInstalled => ("notInstalled", "Profanity Filter is not installed."),
                SwShNameFilterMainKind.UnsupportedBuild => ("unsupported", "Profanity Filter is not available for this exefs/main build."),
                _ => ("blocked", "Profanity Filter could not determine a safe patch state."),
            };
        }

        return new SwShProfanityFilterStatus(
            status,
            message,
            mainStatus.BuildId,
            mainStatus.DetectedGame,
            mainStatus.PatchOffsetHex,
            mainStatus.PatchShape,
            mainStatus.SourceLayer,
            diagnostics);
    }

    private SwShProfanityFilterApplyResult CreateApplyResult(
        ProjectPaths paths,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var statusDiagnostics = diagnostics.ToList();
        var status = Load(paths);
        if (statusDiagnostics.Count > 0)
        {
            status = status with { Diagnostics = statusDiagnostics };
        }

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var applyResult = new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, Array.Empty<PlannedFileWrite>()),
            diagnostics);

        return new SwShProfanityFilterApplyResult(status, applyResult);
    }

    private static void ValidateEditableProject(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter requires valid base paths and a valid Output Root.",
                expected: "Editable project paths"));
        }
    }

    private static void WriteOutputFile(
        ProjectPaths paths,
        string relativePath,
        byte[] contents,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        var targetPath = SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, relativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Profanity Filter target must stay inside Output Root.",
                file: relativePath,
                expected: "Output-root-contained target"));
            return;
        }

        try
        {
            WriteBytesAtomic(targetPath, contents);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, relativePath));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Profanity Filter could not write output: {exception.Message}",
                file: relativePath,
                expected: "Writable Output Root file"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Profanity Filter could not write output: {exception.Message}",
                file: relativePath,
                expected: "Writable Output Root file"));
        }
    }

    private static void WriteBytesAtomic(string path, byte[] contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, contents);
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: Domain,
            Field: field,
            Expected: expected);
    }

    private sealed record MainStatus(
        SwShNameFilterMainKind Kind,
        string? BuildId,
        ProjectGame? DetectedGame,
        string PatchOffsetHex,
        string PatchShape,
        string SourceLayer)
    {
        public static MainStatus Empty { get; } = new(
            SwShNameFilterMainKind.Conflict,
            null,
            null,
            "unknown",
            "not inspected",
            "unknown");
    }
}
