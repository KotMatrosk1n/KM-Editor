// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.SV.Workflows;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Security.Cryptography;
using System.Text.Json;

namespace KM.SV.ModMerger;

public sealed class SvModMergerWorkflowService
{
    private const string WorkflowId = "modMerger";
    private const string WorkflowDomain = "workflow.svModMerger";
    private const string RomFsPrefix = "romfs/";
    private const string DescriptorRelativePath = "romfs/" + SvTrinityDescriptorPatcher.DescriptorVirtualPath;
    private static readonly string ManifestRelativePath = Path.Combine(".km", "sv-mod-merger-manifest.json");
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SvModMergerWorkflowService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var availability = project.Health.CanOpenEditableWorkflows
            ? SvWorkflowAvailability.Available
            : SvWorkflowAvailability.Disabled;

        return new SvWorkflowSummary(
            WorkflowId,
            "S/V Mod Merger",
            "Smart merge ordered Scarlet/Violet RomFS mods from folders, zip files, or rar files and patch the Trinity descriptor for LayeredFS.",
            availability,
            []);
    }

    public SvModMergerWorkflow Load(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        var analysis = Analyze(project, modSources, diagnostics);

        return CreateWorkflow(project, analysis.Sources, analysis.OutputFiles, diagnostics);
    }

    public SvModMergerStageResult Stage(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        var analysis = Analyze(project, modSources, diagnostics);
        var plan = CreatePlan(project.Paths, analysis.OutputFiles, includeOutputs: false, diagnostics);
        var workflow = CreateWorkflow(project, analysis.Sources, analysis.OutputFiles, diagnostics);
        var preview = CreatePreview(project, plan.Files, diagnostics);

        return new SvModMergerStageResult(workflow, preview, diagnostics);
    }

    public SvModMergerApplyResult Apply(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        var analysis = Analyze(project, modSources, diagnostics);
        var plan = CreatePlan(project.Paths, analysis.OutputFiles, includeOutputs: true, diagnostics);
        var workflow = CreateWorkflow(project, analysis.Sources, analysis.OutputFiles, diagnostics);
        var preview = CreatePreview(project, plan.Files, diagnostics);
        var writtenFiles = new List<string>();

        if (!preview.CanApply)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "S/V Mod Merger cannot apply until the source list and project paths are valid.",
                expected: "At least one readable enabled mod source and valid editable project paths"));
            preview = CreatePreview(project, plan.Files, diagnostics);
            return new SvModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
        }

        var outputRoot = project.Paths.OutputRootPath;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "S/V Mod Merger apply requires an Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory"));
            preview = CreatePreview(project, plan.Files, diagnostics);
            return new SvModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
        }

        CleanPreviousOutputs(outputRoot, diagnostics);
        foreach (var output in plan.Outputs)
        {
            var targetPath = ResolveOutputPath(outputRoot, output.RelativePath, diagnostics);
            if (targetPath is null)
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllBytes(targetPath, output.Contents);
                writtenFiles.Add(output.RelativePath);
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Merged file could not be written: {exception.Message}",
                    file: output.RelativePath,
                    expected: "Writable Output Root file"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Merged file could not be written: {exception.Message}",
                    file: output.RelativePath,
                    expected: "Writable Output Root file"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            preview = CreatePreview(project, plan.Files, diagnostics);
            return new SvModMergerApplyResult(workflow, preview, writtenFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), diagnostics);
        }

        try
        {
            WritePatchedDescriptor(project.Paths, writtenFiles);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V Mod Merger could not write the patched Trinity descriptor: {exception.Message}",
                file: DescriptorRelativePath,
                expected: "Writable descriptor output"));
        }

        var manifest = CreateManifest(outputRoot, writtenFiles.Distinct(StringComparer.OrdinalIgnoreCase));
        SaveManifest(outputRoot, manifest, diagnostics);

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Applied {manifest.Entries.Count} Scarlet/Violet LayeredFS file{(manifest.Entries.Count == 1 ? string.Empty : "s")} from {analysis.EnabledSourceCount} enabled mod source{(analysis.EnabledSourceCount == 1 ? string.Empty : "s")}."));
        }

        preview = CreatePreview(project, plan.Files, diagnostics);
        return new SvModMergerApplyResult(workflow, preview, manifest.Entries.Select(entry => entry.RelativePath).ToArray(), diagnostics);
    }

    private static MergeAnalysis Analyze(
        OpenedProject project,
        IReadOnlyList<SvModMergerSourceRequest> requests,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sourceStates = requests
            .Select((request, index) => new SourceAnalysisState(index, request.Path, request.IsEnabled))
            .ToArray();
        var outputs = new Dictionary<string, OutputFileState>(StringComparer.OrdinalIgnoreCase);
        var enabledSourceCount = 0;
        var sourceFileCount = 0;
        var overrideCount = 0;

        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "S/V Mod Merger requires valid base paths and a valid Output Root.",
                expected: "Editable Scarlet/Violet project paths"));
        }

        foreach (var state in sourceStates)
        {
            if (!state.IsEnabled)
            {
                state.Status = "disabled";
                continue;
            }

            enabledSourceCount++;
            var files = EnumerateSourceFiles(new ModSourceSpec(state.SourceIndex, state.Path, state.IsEnabled), state.Diagnostics);
            state.FileCount = files.Count;
            state.Status = state.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                ? "error"
                : files.Count == 0
                    ? "empty"
                    : "ready";
            sourceFileCount += files.Count;

            foreach (var file in files)
            {
                if (string.Equals(file.RelativePath, DescriptorRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    state.Diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Info,
                        "The source descriptor was skipped. KM generates a patched data.trpfd from the vanilla descriptor after merging.",
                        file: DescriptorRelativePath));
                    continue;
                }

                if (!outputs.TryGetValue(file.RelativePath, out var output))
                {
                    output = new OutputFileState(file.RelativePath);
                    outputs[file.RelativePath] = output;
                }
                else
                {
                    state.OverrideCount++;
                    overrideCount++;
                }

                output.Sources.Add(new SourceOccurrenceState(
                    state.SourceIndex,
                    state.Name,
                    state.Path,
                    file.Size));
            }

            foreach (var diagnostic in state.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }
        }

        if (requests.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Add at least one folder, zip, or rar mod source before staging S/V Mod Merger.",
                expected: "One or more mod sources"));
        }

        if (enabledSourceCount > 0 && outputs.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "No RomFS files were found in the enabled S/V mod sources.",
                expected: "Files under romfs/... or direct RomFS paths"));
        }

        return new MergeAnalysis(
            sourceStates.Select(state => state.ToRecord()).ToArray(),
            outputs.Values.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            enabledSourceCount,
            sourceFileCount,
            overrideCount);
    }

    private static MergePlan CreatePlan(
        ProjectPaths paths,
        IReadOnlyList<OutputFileState> outputFiles,
        bool includeOutputs,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var files = new List<SvModMergerFilePreviewRecord>();
        var outputs = new List<MergeOutput>();
        using var baseReader = BaseFileReader.TryCreate(paths, diagnostics);

        foreach (var file in outputFiles)
        {
            var sourceBytes = ReadSourceBytes(file, diagnostics);
            if (sourceBytes.Count != file.Sources.Count)
            {
                files.Add(CreateErrorPreview(file, "KM could not read every source file needed for this output."));
                continue;
            }

            var result = MergeFile(file, sourceBytes, baseReader, diagnostics);
            files.Add(result.Preview);
            if (includeOutputs)
            {
                outputs.Add(new MergeOutput(file.RelativePath, result.Contents));
            }
        }

        return new MergePlan(files, outputs);
    }

    private static IReadOnlyList<SourceBytes> ReadSourceBytes(
        OutputFileState file,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var result = new List<SourceBytes>(file.Sources.Count);
        foreach (var source in file.Sources)
        {
            var bytes = TryReadSourceBytes(source, file.RelativePath, diagnostics);
            if (bytes is not null)
            {
                result.Add(new SourceBytes(source, bytes));
            }
        }

        return result;
    }

    private static SmartMergeResult MergeFile(
        OutputFileState file,
        IReadOnlyList<SourceBytes> sourceBytes,
        BaseFileReader? baseReader,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (sourceBytes.Count == 1)
        {
            var onlySource = sourceBytes[0];
            return new SmartMergeResult(
                onlySource.Bytes.ToArray(),
                CreatePreviewRecord(
                    file,
                    onlySource.Source,
                    "singleSource",
                    "Will write the only enabled mod source for this file."));
        }

        if (AllSourceBytesMatch(sourceBytes))
        {
            var winner = sourceBytes[^1];
            return new SmartMergeResult(
                winner.Bytes.ToArray(),
                CreatePreviewRecord(
                    file,
                    winner.Source,
                    "identical",
                    "All enabled sources provide identical bytes for this file."));
        }

        if (baseReader is null || !baseReader.TryRead(file.RelativePath, out var baseBytes))
        {
            return CreatePriorityFallback(
                file,
                sourceBytes,
                "priorityFallback",
                "Smart merge could not read the vanilla file, so the later enabled source in the mod order wins.",
                diagnostics);
        }

        if (sourceBytes.Any(source => source.Bytes.Length != baseBytes.Length))
        {
            return CreatePriorityFallback(
                file,
                sourceBytes,
                "priorityFallback",
                "Smart merge cannot combine files when one or more mods changed the file length, so the later enabled source in the mod order wins.",
                diagnostics);
        }

        var merged = baseBytes.ToArray();
        for (var index = 0; index < baseBytes.Length; index++)
        {
            byte? chosenValue = null;
            foreach (var source in sourceBytes)
            {
                var value = source.Bytes[index];
                if (value == baseBytes[index])
                {
                    continue;
                }

                if (chosenValue is null)
                {
                    chosenValue = value;
                    continue;
                }

                if (chosenValue.Value != value)
                {
                    return CreatePriorityFallback(
                        file,
                        sourceBytes,
                        "priorityFallback",
                        "Smart merge found overlapping byte edits with different values, so the later enabled source in the mod order wins.",
                        diagnostics);
                }
            }

            if (chosenValue is not null)
            {
                merged[index] = chosenValue.Value;
            }
        }

        var prioritySource = sourceBytes[^1].Source;
        return new SmartMergeResult(
            merged,
            CreatePreviewRecord(
                file,
                prioritySource,
                "smartMerge",
                $"Smart merge will combine non-overlapping edits from {sourceBytes.Count} enabled sources."));
    }

    private static SmartMergeResult CreatePriorityFallback(
        OutputFileState file,
        IReadOnlyList<SourceBytes> sourceBytes,
        string mergeKind,
        string summary,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var winner = sourceBytes[^1];
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Warning,
            summary,
            file: file.RelativePath,
            expected: "Non-overlapping same-length edits against vanilla bytes"));

        return new SmartMergeResult(
            winner.Bytes.ToArray(),
            CreatePreviewRecord(file, winner.Source, mergeKind, summary));
    }

    private static bool AllSourceBytesMatch(IReadOnlyList<SourceBytes> sourceBytes)
    {
        var first = sourceBytes[0].Bytes;
        return sourceBytes.Skip(1).All(source => source.Bytes.SequenceEqual(first));
    }

    private static SvModMergerFilePreviewRecord CreatePreviewRecord(
        OutputFileState file,
        SourceOccurrenceState winningSource,
        string mergeKind,
        string summary)
    {
        return new SvModMergerFilePreviewRecord(
            file.RelativePath,
            file.RelativePath,
            "Scarlet/Violet RomFS file",
            "ready",
            mergeKind,
            summary,
            winningSource.SourceIndex,
            winningSource.SourceName,
            Math.Max(0, file.Sources.Count - 1));
    }

    private static SvModMergerFilePreviewRecord CreateErrorPreview(
        OutputFileState file,
        string summary)
    {
        var fallbackSource = file.Sources.LastOrDefault();
        return new SvModMergerFilePreviewRecord(
            file.RelativePath,
            file.RelativePath,
            "Scarlet/Violet RomFS file",
            "error",
            "readError",
            summary,
            fallbackSource?.SourceIndex ?? -1,
            fallbackSource?.SourceName ?? string.Empty,
            Math.Max(0, file.Sources.Count - 1));
    }

    private static byte[]? TryReadSourceBytes(
        SourceOccurrenceState source,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            if (Directory.Exists(source.SourcePath))
            {
                return TryReadFolderBytes(source.SourcePath, relativePath);
            }

            if (File.Exists(source.SourcePath))
            {
                return TryReadArchiveBytes(source.SourcePath, relativePath, diagnostics);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArchiveException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Mod source file could not be read: {exception.Message}",
                file: $"{source.SourcePath}:{relativePath}",
                expected: "Readable source file"));
        }

        return null;
    }

    private static byte[]? TryReadFolderBytes(string sourcePath, string relativePath)
    {
        var root = ResolveFolderContentRoot(sourcePath);
        var pathInsideRomFs = StripRomFsPrefix(relativePath).Replace('/', Path.DirectorySeparatorChar);
        var targetPath = Path.GetFullPath(Path.Combine(root, pathInsideRomFs));
        var rootFullPath = Path.GetFullPath(root);
        var relativeToRoot = Path.GetRelativePath(rootFullPath, targetPath);
        if (relativeToRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeToRoot))
        {
            return null;
        }

        return File.Exists(targetPath) ? File.ReadAllBytes(targetPath) : null;
    }

    private static byte[]? TryReadArchiveBytes(
        string sourcePath,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        byte[]? result = null;
        using var stream = File.OpenRead(sourcePath);
        using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions());
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            var entryRelativePath = NormalizeEntryPath(reader.Entry.Key ?? string.Empty, diagnostics, sourcePath);
            if (!string.Equals(entryRelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = reader.OpenEntryStream();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            result = memory.ToArray();
        }

        return result;
    }

    private static IReadOnlyList<SourceFileRecord> EnumerateSourceFiles(
        ModSourceSpec source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "A mod source path is empty.",
                field: "modSources",
                expected: "Existing folder, zip, or rar file"));
            return [];
        }

        try
        {
            if (Directory.Exists(source.Path))
            {
                return EnumerateFolderFiles(source, diagnostics);
            }

            if (File.Exists(source.Path))
            {
                return EnumerateArchiveFiles(source, diagnostics);
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mod source path does not exist.",
                file: source.Path,
                expected: "Existing folder, zip, or rar file"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArchiveException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Mod source could not be read: {exception.Message}",
                file: source.Path,
                expected: "Readable folder, zip, or rar mod source"));
        }

        return [];
    }

    private static IReadOnlyList<SourceFileRecord> EnumerateFolderFiles(
        ModSourceSpec source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var root = ResolveFolderContentRoot(source.Path);
        var files = new List<SourceFileRecord>();
        foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var entryPath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
            var relativePath = NormalizeEntryPath(entryPath, diagnostics, source.Path);
            if (relativePath is null)
            {
                continue;
            }

            files.Add(new SourceFileRecord(relativePath, new FileInfo(filePath).Length));
        }

        return DeduplicateFiles(files);
    }

    private static IReadOnlyList<SourceFileRecord> EnumerateArchiveFiles(
        ModSourceSpec source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var files = new List<SourceFileRecord>();
        using var stream = File.OpenRead(source.Path);
        using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions());
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            if (entry.IsDirectory)
            {
                continue;
            }

            var relativePath = NormalizeEntryPath(entry.Key ?? string.Empty, diagnostics, source.Path);
            if (relativePath is null)
            {
                continue;
            }

            files.Add(new SourceFileRecord(relativePath, entry.Size));
        }

        return DeduplicateFiles(files);
    }

    private static IReadOnlyList<SourceFileRecord> DeduplicateFiles(IReadOnlyList<SourceFileRecord> files)
    {
        return files
            .GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveFolderContentRoot(string folderPath)
    {
        if (string.Equals(Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "romfs", StringComparison.OrdinalIgnoreCase))
        {
            return folderPath;
        }

        var directRomFs = Path.Combine(folderPath, "romfs");
        if (Directory.Exists(directRomFs))
        {
            return directRomFs;
        }

        return Directory
            .EnumerateDirectories(folderPath, "romfs", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault() ?? folderPath;
    }

    private static string? NormalizeEntryPath(
        string entryPath,
        ICollection<ValidationDiagnostic> diagnostics,
        string sourcePath)
    {
        var normalized = entryPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (segments.Length == 0)
        {
            return null;
        }

        if (segments.Any(segment => segment is "." or ".."))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "A mod source file path was skipped because it contains unsafe traversal segments.",
                file: $"{sourcePath}:{entryPath}",
                expected: "Relative RomFS file path"));
            return null;
        }

        if (segments.Any(segment => string.Equals(segment, "info.toml", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var romFsIndex = Array.FindIndex(
            segments,
            segment => string.Equals(segment, "romfs", StringComparison.OrdinalIgnoreCase));
        if (romFsIndex >= 0)
        {
            return romFsIndex == segments.Length - 1
                ? null
                : RomFsPrefix + string.Join('/', segments[(romFsIndex + 1)..]);
        }

        var first = segments[0];
        if (string.Equals(first, "exefs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(first, "atmosphere", StringComparison.OrdinalIgnoreCase)
            || string.Equals(first, "contents", StringComparison.OrdinalIgnoreCase)
            || string.Equals(first, "sdcard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(first, "switch", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "A non-RomFS mod source file was skipped.",
                file: $"{sourcePath}:{entryPath}",
                expected: "romfs/... file"));
            return null;
        }

        if (IsCommonMetadataFile(segments[^1]))
        {
            return null;
        }

        return RomFsPrefix + string.Join('/', segments);
    }

    private static bool IsCommonMetadataFile(string fileName)
    {
        return string.Equals(fileName, "settings.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, ".ds_store", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    private SvModMergerWorkflow CreateWorkflow(
        OpenedProject project,
        IReadOnlyList<SvModMergerSourceRecord> sources,
        IReadOnlyList<OutputFileState> outputFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SvModMergerWorkflow(
            CreateSummary(project),
            project.Paths.OutputRootPath,
            sources,
            new SvModMergerWorkflowStats(
                sources.Count,
                sources.Count(source => source.IsEnabled),
                sources.Sum(source => source.FileCount),
                outputFiles.Count,
                sources.Sum(source => source.OverrideCount)),
            diagnostics.ToArray());
    }

    private static SvModMergerPreview CreatePreview(
        OpenedProject project,
        IReadOnlyList<SvModMergerFilePreviewRecord> files,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var hasErrors = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || files.Any(file => string.Equals(file.Status, "error", StringComparison.OrdinalIgnoreCase));
        var canApply = project.Health.CanOpenEditableWorkflows && !hasErrors && files.Count > 0;
        var status = hasErrors
            ? "error"
            : files.Count == 0
                ? "empty"
                : files.Any(file => string.Equals(file.MergeKind, "priorityFallback", StringComparison.OrdinalIgnoreCase))
                    ? "priorityFallback"
                    : "ready";

        return new SvModMergerPreview(
            canApply,
            status,
            files.Count,
            files.Count(file => string.Equals(file.Status, "ready", StringComparison.OrdinalIgnoreCase)),
            0,
            0,
            files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics.ToArray());
    }

    private static void CleanPreviousOutputs(
        string outputRoot,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var manifest = LoadManifest(outputRoot, diagnostics);
        if (manifest is null)
        {
            return;
        }

        foreach (var entry in manifest.Entries)
        {
            var targetPath = ResolveOutputPath(outputRoot, entry.RelativePath, diagnostics);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            var actualHash = ComputeFileSha256(targetPath);
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "A previous S/V Mod Merger output was changed outside the merger, so KM left it in place.",
                    file: entry.RelativePath,
                    expected: "File hash matching previous merger manifest"));
                continue;
            }

            File.Delete(targetPath);
            DeleteEmptyParentDirectories(outputRoot, targetPath);
        }
    }

    private static void DeleteEmptyParentDirectories(string outputRoot, string deletedFilePath)
    {
        var root = Path.GetFullPath(outputRoot);
        var directory = Path.GetDirectoryName(deletedFilePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var fullDirectory = Path.GetFullPath(directory);
            if (string.Equals(fullDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var relativeToRoot = Path.GetRelativePath(root, fullDirectory);
            if (relativeToRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeToRoot))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(fullDirectory).Any())
            {
                return;
            }

            Directory.Delete(fullDirectory);
            directory = Path.GetDirectoryName(fullDirectory);
        }
    }

    private static void WritePatchedDescriptor(ProjectPaths paths, ICollection<string> writtenFiles)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException("S/V descriptor patching requires a base RomFS path.");
        }

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("S/V descriptor patching requires an Output Root.");
        }

        var descriptorBytes = SvTrinityDescriptorPatcher.CreateLayeredDescriptor(
            paths.BaseRomFsPath,
            paths.OutputRootPath);
        var descriptorPath = ResolveOutputPath(paths.OutputRootPath, DescriptorRelativePath, []);
        if (descriptorPath is null)
        {
            throw new IOException("S/V descriptor output path could not be resolved.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);
        File.WriteAllBytes(descriptorPath, descriptorBytes);
        writtenFiles.Add(DescriptorRelativePath);
    }

    private static string? ResolveOutputPath(
        string outputRootPath,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (Path.IsPathRooted(relativePath) || !relativePath.StartsWith(RomFsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Merged output path must be a RomFS relative path.",
                file: relativePath,
                expected: "romfs/... relative path"));
            return null;
        }

        var outputRoot = Path.GetFullPath(outputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relativeToOutputRoot = Path.GetRelativePath(outputRoot, targetPath);
        if (relativeToOutputRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeToOutputRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Merged output path escapes Output Root.",
                file: relativePath,
                expected: "Output path inside Output Root"));
            return null;
        }

        return targetPath;
    }

    private static SvModMergerManifest? LoadManifest(
        string outputRoot,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var manifestPath = Path.Combine(outputRoot, ManifestRelativePath);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SvModMergerManifest>(
                File.ReadAllText(manifestPath),
                ManifestJsonOptions);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Previous S/V Mod Merger manifest could not be read: {exception.Message}",
                file: ManifestRelativePath));
            return null;
        }
    }

    private static SvModMergerManifest CreateManifest(
        string outputRoot,
        IEnumerable<string> writtenFiles)
    {
        var entries = new List<SvModMergerManifestEntry>();
        foreach (var relativePath in writtenFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var targetPath = ResolveOutputPath(outputRoot, relativePath, []);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            entries.Add(new SvModMergerManifestEntry(relativePath, ComputeFileSha256(targetPath)));
        }

        return new SvModMergerManifest(entries);
    }

    private static void SaveManifest(
        string outputRoot,
        SvModMergerManifest manifest,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var manifestPath = Path.Combine(outputRoot, ManifestRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, ManifestJsonOptions));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"S/V Mod Merger manifest could not be written: {exception.Message}",
                file: ManifestRelativePath));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"S/V Mod Merger manifest could not be written: {exception.Message}",
                file: ManifestRelativePath));
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static string StripRomFsPrefix(string relativePath)
    {
        return relativePath.StartsWith(RomFsPrefix, StringComparison.OrdinalIgnoreCase)
            ? relativePath[RomFsPrefix.Length..]
            : relativePath;
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: WorkflowDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record ModSourceSpec(int SourceIndex, string Path, bool IsEnabled);

    private sealed record SourceFileRecord(string RelativePath, long Size);

    private sealed record SourceOccurrenceState(
        int SourceIndex,
        string SourceName,
        string SourcePath,
        long Size);

    private sealed record SourceBytes(SourceOccurrenceState Source, byte[] Bytes);

    private sealed record MergeOutput(string RelativePath, byte[] Contents);

    private sealed record MergePlan(
        IReadOnlyList<SvModMergerFilePreviewRecord> Files,
        IReadOnlyList<MergeOutput> Outputs);

    private sealed record SmartMergeResult(
        byte[] Contents,
        SvModMergerFilePreviewRecord Preview);

    private sealed record MergeAnalysis(
        IReadOnlyList<SvModMergerSourceRecord> Sources,
        IReadOnlyList<OutputFileState> OutputFiles,
        int EnabledSourceCount,
        int SourceFileCount,
        int OverrideCount);

    private sealed record SvModMergerManifest(IReadOnlyList<SvModMergerManifestEntry> Entries);

    private sealed record SvModMergerManifestEntry(string RelativePath, string Sha256);

    private sealed class OutputFileState
    {
        public OutputFileState(string relativePath)
        {
            RelativePath = relativePath;
        }

        public string RelativePath { get; }
        public List<SourceOccurrenceState> Sources { get; } = [];
    }

    private sealed class SourceAnalysisState
    {
        public SourceAnalysisState(int sourceIndex, string path, bool isEnabled)
        {
            SourceIndex = sourceIndex;
            Path = path;
            IsEnabled = isEnabled;
            Name = CreateSourceName(path, sourceIndex);
            Kind = Directory.Exists(path) ? "folder" : "archive";
        }

        public int SourceIndex { get; }
        public string Path { get; }
        public string Name { get; }
        public string Kind { get; }
        public bool IsEnabled { get; }
        public string Status { get; set; } = "pending";
        public int FileCount { get; set; }
        public int OverrideCount { get; set; }
        public List<ValidationDiagnostic> Diagnostics { get; } = [];

        public SvModMergerSourceRecord ToRecord()
        {
            return new SvModMergerSourceRecord(
                SourceIndex,
                Path,
                Name,
                Kind,
                IsEnabled,
                Status,
                FileCount,
                OverrideCount,
                Diagnostics.ToArray());
        }

        private static string CreateSourceName(string path, int sourceIndex)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return $"Source {sourceIndex + 1}";
            }

            var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Directory.Exists(path)
                ? new DirectoryInfo(trimmed).Name
                : System.IO.Path.GetFileNameWithoutExtension(trimmed);
            return string.IsNullOrWhiteSpace(name) ? $"Source {sourceIndex + 1}" : name;
        }
    }

    private sealed class BaseFileReader : IDisposable
    {
        private readonly string baseRomFsPath;
        private readonly string? supportFolderPath;
        private readonly string looseBaseRoot;
        private SvTrinityArchive? archive;

        private BaseFileReader(string baseRomFsPath, string? supportFolderPath)
        {
            this.baseRomFsPath = baseRomFsPath;
            this.supportFolderPath = supportFolderPath;
            looseBaseRoot = ResolveBaseRomFsRoot(baseRomFsPath);
        }

        public static BaseFileReader? TryCreate(
            ProjectPaths paths,
            ICollection<ValidationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "S/V smart merge cannot read vanilla files until Base RomFS is set.",
                    field: "baseRomFsPath",
                    expected: "Scarlet/Violet Base RomFS"));
                return null;
            }

            if (!SvCompressionRuntime.IsConfigured(paths.ScarletVioletSupportFolderPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "oo2core_8_win64.dll folder is not configured. Smart merge can still use loose base files, but packed vanilla comparisons may fall back to source priority.",
                    field: "scarletVioletSupportFolderPath",
                    expected: "Configured oo2core_8_win64.dll folder for packed vanilla comparisons"));
            }

            return new BaseFileReader(paths.BaseRomFsPath, paths.ScarletVioletSupportFolderPath);
        }

        public bool TryRead(string relativePath, out byte[] bytes)
        {
            var virtualPath = StripRomFsPrefix(relativePath);
            var loosePath = Path.GetFullPath(Path.Combine(looseBaseRoot, virtualPath.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(looseBaseRoot);
            var relativeToRoot = Path.GetRelativePath(root, loosePath);
            if (!relativeToRoot.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativeToRoot) && File.Exists(loosePath))
            {
                bytes = File.ReadAllBytes(loosePath);
                return true;
            }

            try
            {
                archive ??= SvTrinityArchive.Open(baseRomFsPath, supportFolderPath);
                return archive.TryReadFile(virtualPath, out bytes);
            }
            catch (Exception)
            {
                bytes = [];
                return false;
            }
        }

        public void Dispose()
        {
            archive?.Dispose();
        }

        private static string ResolveBaseRomFsRoot(string path)
        {
            if (File.Exists(Path.Combine(path, "arc", "data.trpfd")))
            {
                return path;
            }

            var nestedRomFsPath = Path.Combine(path, "romfs");
            return File.Exists(Path.Combine(nestedRomFsPath, "arc", "data.trpfd"))
                ? nestedRomFsPath
                : path;
        }
    }
}
