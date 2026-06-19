// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace KM.SwSh.FpsPatch;

public sealed class SwShFpsPatchService
{
    private const string Domain = "tool.60fpsPatch";
    private const string ExeFsMainPath = SwShExeFsPatchWorkflowService.ExeFsMainPath;
    private const string ManifestRelativePath = ".km-editor/60fps-patch-manifest.json";
    private const string SequenceRootInsideRomFs = "bin/battle/waza/sequence";
    private const string SequenceRootRelativePath = "romfs/bin/battle/waza/sequence";
    private const int ExpectedManagedBseqFileCount = 1010;

    private static readonly string[] ManagedBseqPrefixes = ["eg", "es", "et", "ew"];
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShFpsPatchService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
    }

    public static bool IsManagedRomFsPath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (!normalized.StartsWith(SequenceRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(".bseq", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(normalized);
        return ManagedBseqPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsGeneratedRomFsOutput(ProjectPaths paths, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!IsManagedRomFsPath(relativePath) || string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return false;
        }

        var sourcePath = ResolveBaseRomFsPath(paths.BaseRomFsPath, relativePath);
        var outputPath = ResolveOutputPath(paths.OutputRootPath, relativePath);
        if (sourcePath is null || outputPath is null || !File.Exists(sourcePath) || !File.Exists(outputPath))
        {
            return false;
        }

        try
        {
            var generated = ConvertBseq(File.ReadAllBytes(sourcePath));
            var output = File.ReadAllBytes(outputPath);
            return output.SequenceEqual(generated);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public SwShFpsPatchStatus Load(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);

        var mainStatus = AnalyzeMain(paths, diagnostics);
        var bseqStatus = AnalyzeBseqOutputs(paths, diagnostics);

        return CreateStatus(mainStatus, bseqStatus, diagnostics);
    }

    public SwShFpsPatchApplyResult Apply(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);
        var writtenFiles = new List<ProjectFileReference>();

        var preparedMain = PrepareMainApply(paths, diagnostics);
        var preparedBseqFiles = PrepareBseqApply(paths, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        if (preparedMain is not null)
        {
            WriteOutputFile(paths.OutputRootPath!, ExeFsMainPath, preparedMain, diagnostics, writtenFiles);
        }

        foreach (var preparedFile in preparedBseqFiles)
        {
            WriteOutputFile(paths.OutputRootPath!, preparedFile.RelativePath, preparedFile.Contents, diagnostics, writtenFiles);
        }

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            RecordManifest(paths, preparedMain is not null, preparedBseqFiles, diagnostics);
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                writtenFiles.Count == 0
                    ? "60FPS Patch was already installed."
                    : string.Create(CultureInfo.InvariantCulture, $"60FPS Patch installed {writtenFiles.Count:N0} output file(s).")));
        }

        return CreateApplyResult(paths, writtenFiles, diagnostics);
    }

    public SwShFpsPatchApplyResult Restore(ProjectPaths paths)
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
                "60FPS Patch uninstall requires a configured Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory"));
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        RestoreMain(paths, diagnostics, writtenFiles);
        RestoreBseqFiles(paths, diagnostics, writtenFiles);
        DeleteManifest(paths, diagnostics);

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                writtenFiles.Count == 0
                    ? "60FPS Patch uninstall found no owned output files to remove."
                    : string.Create(CultureInfo.InvariantCulture, $"60FPS Patch uninstalled {writtenFiles.Count:N0} owned output file(s).")));
        }

        return CreateApplyResult(paths, writtenFiles, diagnostics);
    }

    private byte[]? PrepareMainApply(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base ExeFS and Output Root before it can install.",
                expected: "Readable Base ExeFS and writable Output Root"));
            return null;
        }

        var basePath = Path.Combine(paths.BaseExeFsPath, "main");
        if (!File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find base exefs/main.",
                file: ExeFsMainPath,
                expected: "Readable Sword/Shield 1.3.2 exefs/main"));
            return null;
        }

        var outputMainPath = ResolveOutputPath(paths.OutputRootPath, ExeFsMainPath);
        var sourcePath = outputMainPath is not null && File.Exists(outputMainPath)
            ? outputMainPath
            : basePath;

        try
        {
            var current = File.ReadAllBytes(sourcePath);
            var patched = SwShFpsMainPatcher.Apply(current, paths.SelectedGame);
            return patched.SequenceEqual(current) ? null : patched;
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Supported Sword/Shield 1.3.2 exefs/main with vanilla or KM 60FPS bytes"));
        }

        return null;
    }

    private IReadOnlyList<PreparedBseqFile> PrepareBseqApply(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var preparedFiles = new List<PreparedBseqFile>();
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base RomFS and Output Root before it can install.",
                expected: "Readable Base RomFS and writable Output Root"));
            return preparedFiles;
        }

        var sourceFiles = EnumerateManagedBseqFiles(paths.BaseRomFsPath, diagnostics);
        if (sourceFiles.Count != ExpectedManagedBseqFileCount)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"60FPS Patch expected {ExpectedManagedBseqFileCount:N0} managed move-effect BSEQ files, but found {sourceFiles.Count:N0}."),
                file: SequenceRootRelativePath,
                expected: "Complete Sword/Shield Base RomFS move-effect sequence folder"));
            return preparedFiles;
        }

        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                var generated = ConvertBseq(sourceBytes);
                var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
                if (targetPath is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "60FPS Patch target must stay inside Output Root.",
                        file: sourceFile.RelativePath,
                        expected: "Output-root-contained RomFS target"));
                    continue;
                }

                if (File.Exists(targetPath))
                {
                    var existing = File.ReadAllBytes(targetPath);
                    if (existing.SequenceEqual(generated))
                    {
                        continue;
                    }

                    if (!existing.SequenceEqual(sourceBytes))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "60FPS Patch found an existing non-60FPS move-effect sequence file and will not overwrite it.",
                            file: sourceFile.RelativePath,
                            expected: "No existing modded sequence file, or one already generated by 60FPS Patch"));
                        continue;
                    }
                }

                preparedFiles.Add(new PreparedBseqFile(sourceFile.RelativePath, generated, ComputeSha256(generated)));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"60FPS Patch could not read or stage a BSEQ file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Readable BSEQ source and target"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"60FPS Patch could not read or stage a BSEQ file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Readable BSEQ source and target"));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    exception.Message,
                    file: sourceFile.RelativePath,
                    expected: "Valid SwSh BSEQ sequence file"));
            }
        }

        return preparedFiles;
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
                "60FPS Patch uninstall requires Base ExeFS and Output Root.",
                expected: "Readable Base ExeFS and writable Output Root"));
            return;
        }

        var baseMainPath = Path.Combine(paths.BaseExeFsPath, "main");
        var outputMainPath = ResolveOutputPath(paths.OutputRootPath, ExeFsMainPath);
        if (outputMainPath is null || !File.Exists(outputMainPath))
        {
            return;
        }

        try
        {
            var current = File.ReadAllBytes(outputMainPath);
            var baseBytes = File.ReadAllBytes(baseMainPath);
            var restored = SwShFpsMainPatcher.RestoreFromBase(current, baseBytes, paths.SelectedGame);
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
                $"60FPS Patch could not restore exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable base and output exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not restore exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable base and output exefs/main"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Output exefs/main containing KM-owned 60FPS bytes"));
        }
    }

    private void RestoreBseqFiles(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch uninstall requires Base RomFS and Output Root.",
                expected: "Readable Base RomFS and writable Output Root"));
            return;
        }

        foreach (var sourceFile in EnumerateManagedBseqFiles(paths.BaseRomFsPath, diagnostics))
        {
            var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                var generated = ConvertBseq(sourceBytes);
                var outputBytes = File.ReadAllBytes(targetPath);
                if (!outputBytes.SequenceEqual(generated))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        "60FPS Patch left this sequence file in place because it no longer matches KM-owned 60FPS output.",
                        file: sourceFile.RelativePath,
                        expected: "Unmodified 60FPS Patch generated file"));
                    continue;
                }

                File.Delete(targetPath);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, sourceFile.RelativePath));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a generated BSEQ file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Deletable generated BSEQ file"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a generated BSEQ file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Deletable generated BSEQ file"));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    exception.Message,
                    file: sourceFile.RelativePath,
                    expected: "Valid SwSh BSEQ sequence file"));
            }
        }
    }

    private MainStatus AnalyzeMain(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base ExeFS.",
                field: "baseExeFsPath",
                expected: "Readable Base ExeFS folder"));
            return MainStatus.Empty;
        }

        var baseMainPath = Path.Combine(paths.BaseExeFsPath, "main");
        var outputMainPath = string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? null
            : ResolveOutputPath(paths.OutputRootPath, ExeFsMainPath);
        var sourcePath = outputMainPath is not null && File.Exists(outputMainPath)
            ? outputMainPath
            : baseMainPath;

        if (!File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not inspect exefs/main.",
                file: ExeFsMainPath,
                expected: "Readable base or output exefs/main"));
            return MainStatus.Empty;
        }

        try
        {
            var analysis = SwShFpsMainPatcher.Analyze(File.ReadAllBytes(sourcePath), paths.SelectedGame);
            if (analysis.Kind is SwShFpsPatchMainKind.UnsupportedBuild or SwShFpsPatchMainKind.GameMismatch or SwShFpsPatchMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    analysis.Kind == SwShFpsPatchMainKind.UnsupportedBuild ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    analysis.Message,
                    file: ExeFsMainPath,
                    expected: "Supported Sword/Shield 1.3.2 exefs/main"));
            }

            return new MainStatus(
                analysis.Kind,
                analysis.BuildId == "unknown" ? null : analysis.BuildId,
                analysis.DetectedGame,
                analysis.PatchedSiteCount,
                analysis.SiteCount);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }

        return MainStatus.Empty;
    }

    private BseqStatus AnalyzeBseqOutputs(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base RomFS.",
                field: "baseRomFsPath",
                expected: "Readable Base RomFS folder"));
            return BseqStatus.Empty;
        }

        var sourceFiles = EnumerateManagedBseqFiles(paths.BaseRomFsPath, diagnostics);
        var patchedCount = 0;
        var conflictingCount = 0;
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return new BseqStatus(sourceFiles.Count, patchedCount, conflictingCount);
        }

        foreach (var sourceFile in sourceFiles)
        {
            var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                var generated = ConvertBseq(sourceBytes);
                var outputBytes = File.ReadAllBytes(targetPath);
                if (outputBytes.SequenceEqual(generated))
                {
                    patchedCount++;
                }
                else if (!outputBytes.SequenceEqual(sourceBytes))
                {
                    conflictingCount++;
                }
            }
            catch (IOException)
            {
                conflictingCount++;
            }
            catch (UnauthorizedAccessException)
            {
                conflictingCount++;
            }
            catch (InvalidDataException)
            {
                conflictingCount++;
            }
        }

        return new BseqStatus(sourceFiles.Count, patchedCount, conflictingCount);
    }

    private SwShFpsPatchStatus CreateStatus(
        MainStatus mainStatus,
        BseqStatus bseqStatus,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var hasErrors = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string status;
        string message;

        if (hasErrors)
        {
            status = "blocked";
            message = "60FPS Patch has diagnostics that need attention.";
        }
        else if (mainStatus.Kind == SwShFpsPatchMainKind.UnsupportedBuild)
        {
            status = "unsupported";
            message = "60FPS Patch is not available for this exefs/main build.";
        }
        else if (mainStatus.PatchedSiteCount == mainStatus.SiteCount
            && mainStatus.SiteCount > 0
            && bseqStatus.ManagedFileCount == ExpectedManagedBseqFileCount
            && bseqStatus.PatchedFileCount == bseqStatus.ManagedFileCount
            && bseqStatus.ConflictingFileCount == 0)
        {
            status = "installed";
            message = "60FPS Patch is installed.";
        }
        else if (mainStatus.PatchedSiteCount == 0 && bseqStatus.PatchedFileCount == 0)
        {
            status = bseqStatus.ConflictingFileCount == 0 ? "notInstalled" : "blocked";
            message = bseqStatus.ConflictingFileCount == 0
                ? "60FPS Patch is not installed."
                : "60FPS Patch found move-effect sequence files owned by another mod.";
        }
        else
        {
            status = bseqStatus.ConflictingFileCount == 0 ? "partial" : "blocked";
            message = bseqStatus.ConflictingFileCount == 0
                ? "60FPS Patch is partially installed."
                : "60FPS Patch is partially installed and has move-effect sequence conflicts.";
        }

        return new SwShFpsPatchStatus(
            status,
            message,
            mainStatus.BuildId,
            mainStatus.DetectedGame,
            mainStatus.PatchedSiteCount,
            mainStatus.SiteCount,
            bseqStatus.PatchedFileCount,
            bseqStatus.ManagedFileCount,
            bseqStatus.ConflictingFileCount,
            diagnostics);
    }

    private SwShFpsPatchApplyResult CreateApplyResult(
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

        return new SwShFpsPatchApplyResult(status, applyResult);
    }

    private static IReadOnlyList<ManagedBseqFile> EnumerateManagedBseqFiles(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sequenceRoot = Path.Combine(baseRomFsPath, SequenceRootInsideRomFs);
        if (!Directory.Exists(sequenceRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find the move-effect sequence folder.",
                file: SequenceRootRelativePath,
                expected: "Sword/Shield Base RomFS move-effect sequence folder"));
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(sequenceRoot, "*.bseq", SearchOption.TopDirectoryOnly)
                .Where(path => IsManagedBseqFileName(Path.GetFileName(path)))
                .Select(path => new ManagedBseqFile(
                    Path.GetFullPath(path),
                    $"{SequenceRootRelativePath}/{Path.GetFileName(path).Replace('\\', '/')}"))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan BSEQ files: {exception.Message}",
                file: SequenceRootRelativePath,
                expected: "Readable move-effect sequence folder"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan BSEQ files: {exception.Message}",
                file: SequenceRootRelativePath,
                expected: "Readable move-effect sequence folder"));
            return [];
        }
    }

    private static bool IsManagedBseqFileName(string fileName)
    {
        return fileName.EndsWith(".bseq", StringComparison.OrdinalIgnoreCase)
            && ManagedBseqPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] ConvertBseq(byte[] sourceBytes)
    {
        return SwShFpsBseqPatcher.Convert(
            sourceBytes,
            SwShFpsBseqPatcher.MoveEffectTimelineScale,
            out _);
    }

    private static void ValidateEditableProject(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires valid base paths and a valid Output Root.",
                expected: "Editable project paths"));
        }
    }

    private static void WriteOutputFile(
        string outputRoot,
        string relativePath,
        byte[] contents,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        var targetPath = ResolveOutputPath(outputRoot, relativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch target must stay inside Output Root.",
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
                $"60FPS Patch could not write output: {exception.Message}",
                file: relativePath,
                expected: "Writable Output Root file"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not write output: {exception.Message}",
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

    private static string? ResolveOutputPath(string outputRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(outputRoot);
        var target = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(fullRoot, target);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : target;
    }

    private static string? ResolveBaseRomFsPath(string baseRomFsRoot, string relativePath)
    {
        if (!relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(
            baseRomFsRoot,
            relativePath["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static string ComputeSha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static void RecordManifest(
        ProjectPaths paths,
        bool wroteMain,
        IReadOnlyList<PreparedBseqFile> preparedBseqFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return;
        }

        var manifestPath = ResolveOutputPath(paths.OutputRootPath, ManifestRelativePath);
        if (manifestPath is null)
        {
            return;
        }

        try
        {
            var manifest = new FpsPatchManifest(
                Version: 1,
                CreatedAt: DateTimeOffset.UtcNow,
                ExeFsMainPatched: wroteMain,
                RomFsFiles: preparedBseqFiles
                    .Select(file => new FpsPatchManifestFile(file.RelativePath, file.Sha256))
                    .ToArray());
            var directory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ManifestJsonOptions));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be written: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Writable 60FPS Patch manifest"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be written: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Writable 60FPS Patch manifest"));
        }
    }

    private static void DeleteManifest(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return;
        }

        var manifestPath = ResolveOutputPath(paths.OutputRootPath, ManifestRelativePath);
        if (manifestPath is null || !File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            File.Delete(manifestPath);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be deleted: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Deletable 60FPS Patch manifest"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be deleted: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Deletable 60FPS Patch manifest"));
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

    private sealed record ManagedBseqFile(
        string SourcePath,
        string RelativePath);

    private sealed record PreparedBseqFile(
        string RelativePath,
        byte[] Contents,
        string Sha256);

    private sealed record MainStatus(
        SwShFpsPatchMainKind Kind,
        string? BuildId,
        ProjectGame? DetectedGame,
        int PatchedSiteCount,
        int SiteCount)
    {
        public static MainStatus Empty { get; } = new(SwShFpsPatchMainKind.Conflict, null, null, 0, 0);
    }

    private sealed record BseqStatus(
        int ManagedFileCount,
        int PatchedFileCount,
        int ConflictingFileCount)
    {
        public static BseqStatus Empty { get; } = new(0, 0, 0);
    }

    private sealed record FpsPatchManifest(
        int Version,
        DateTimeOffset CreatedAt,
        bool ExeFsMainPatched,
        IReadOnlyList<FpsPatchManifestFile> RomFsFiles);

    private sealed record FpsPatchManifestFile(
        string RelativePath,
        string Sha256);
}
