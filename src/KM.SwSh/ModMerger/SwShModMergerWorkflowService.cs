// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.ModMerger;

public sealed class SwShModMergerWorkflowService
{
    private const string WorkflowId = "modMerger";
    private const string WorkflowDomain = "workflow.modMerger";
    private const string RomFsPrefix = "romfs/";

    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShModMergerWorkflowService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var availability = project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.Disabled;

        return new SwShWorkflowSummary(
            WorkflowId,
            "Mod Merger",
            "Compare two RomFS mod folders, merge non-overlapping file edits, and write merged outputs directly to Output Root.",
            availability,
            []);
    }

    public SwShModMergerWorkflow Load(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        var directory1Files = ScanModDirectory(modDirectory1, "modDirectory1", diagnostics);
        var directory2Files = ScanModDirectory(modDirectory2, "modDirectory2", diagnostics);
        var matchingFileCount = directory1Files
            .Select(file => file.RelativePath)
            .Intersect(directory2Files.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase)
            .Count();

        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mod Merger requires valid base paths and a valid Output Root.",
                expected: "Editable project paths"));
        }

        return CreateWorkflow(
            project,
            modDirectory1,
            modDirectory2,
            directory1Files,
            directory2Files,
            matchingFileCount,
            paths.OutputRootPath,
            diagnostics);
    }

    public SwShModMergerStageResult Stage(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<string> selectedDirectory1Files,
        IReadOnlyList<string> selectedDirectory2Files,
        IReadOnlyList<SwShModMergerConflictResolution> resolutions,
        string? mergeMode = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selectedDirectory1Files);
        ArgumentNullException.ThrowIfNull(selectedDirectory2Files);
        ArgumentNullException.ThrowIfNull(resolutions);

        var workflow = Load(paths, modDirectory1, modDirectory2);
        var diagnostics = workflow.Diagnostics.ToList();
        var normalizedMergeMode = NormalizeMergeMode(mergeMode);
        var plan = BuildPlan(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            normalizedMergeMode,
            includeOutputs: false,
            resolutions,
            diagnostics);
        var preview = CreatePreview(normalizedMergeMode, plan.Files, plan.Conflicts, diagnostics);

        return new SwShModMergerStageResult(workflow, preview, diagnostics);
    }

    public SwShModMergerApplyResult Apply(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<string> selectedDirectory1Files,
        IReadOnlyList<string> selectedDirectory2Files,
        IReadOnlyList<SwShModMergerConflictResolution> resolutions,
        string? mergeMode = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selectedDirectory1Files);
        ArgumentNullException.ThrowIfNull(selectedDirectory2Files);
        ArgumentNullException.ThrowIfNull(resolutions);

        var workflow = Load(paths, modDirectory1, modDirectory2);
        var diagnostics = workflow.Diagnostics.ToList();
        var normalizedMergeMode = NormalizeMergeMode(mergeMode);
        var plan = BuildPlan(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            normalizedMergeMode,
            includeOutputs: true,
            resolutions,
            diagnostics);
        var preview = CreatePreview(normalizedMergeMode, plan.Files, plan.Conflicts, diagnostics);
        var writtenFiles = new List<string>();

        if (!preview.CanApply)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Resolve every Mod Merger conflict before applying the merge.",
                expected: "Conflict-free merge preview"));
            preview = CreatePreview(normalizedMergeMode, plan.Files, plan.Conflicts, diagnostics);
            return new SwShModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
        }

        var outputRoot = paths.OutputRootPath;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mod Merger apply requires an Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory"));
            preview = CreatePreview(normalizedMergeMode, plan.Files, plan.Conflicts, diagnostics);
            return new SwShModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
        }

        if (!ValidatePlanIntegrity(plan.Outputs, diagnostics))
        {
            preview = CreatePreview(normalizedMergeMode, plan.Files, plan.Conflicts, diagnostics);
            return new SwShModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
        }

        foreach (var output in plan.Outputs)
        {
            var targetPath = ResolveOutputPath(outputRoot, output.RelativePath, diagnostics);
            if (targetPath is null)
            {
                ClearOutputContents(output);
                continue;
            }

            try
            {
                WriteVerifiedOutput(targetPath, output);
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
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Merged file failed integrity verification: {exception.Message}",
                    file: output.RelativePath,
                    expected: "Verified output bytes matching the merge plan"));
            }
            finally
            {
                ClearOutputContents(output);
            }
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Applied {writtenFiles.Count} merged RomFS file{(writtenFiles.Count == 1 ? string.Empty : "s")} to Output Root."));
        }

        preview = CreatePreview(normalizedMergeMode, plan.Files, plan.Conflicts, diagnostics);
        return new SwShModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
    }

    private static MergePlan BuildPlan(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<string> selectedDirectory1Files,
        IReadOnlyList<string> selectedDirectory2Files,
        string mergeMode,
        bool includeOutputs,
        IReadOnlyList<SwShModMergerConflictResolution> resolutions,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var files = new List<SwShModMergerFilePreviewRecord>();
        var conflicts = new List<SwShModMergerConflictRecord>();
        var outputs = new List<MergeOutput>();

        if (string.IsNullOrWhiteSpace(modDirectory1) || string.IsNullOrWhiteSpace(modDirectory2))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Choose both mod directories before staging a merge.",
                expected: "Two RomFS mod directories"));
            return new MergePlan(files, conflicts, outputs);
        }

        var root1 = ResolveRomFsRoot(modDirectory1);
        var root2 = ResolveRomFsRoot(modDirectory2);
        if (root1 is null || root2 is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Both mod directories must exist and contain RomFS files.",
                expected: "Existing directory or romfs folder"));
            return new MergePlan(files, conflicts, outputs);
        }

        var selected1 = NormalizeSelectedFiles(selectedDirectory1Files);
        var selected2 = NormalizeSelectedFiles(selectedDirectory2Files);
        if (selected1.Count == 0 && selected2.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Select matching files from both mod directories before staging a merge.",
                expected: "At least one identical selected RomFS file"));
            return new MergePlan(files, conflicts, outputs);
        }

        var onlyIn1 = selected1.Except(selected2, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var onlyIn2 = selected2.Except(selected1, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (onlyIn1.Length > 0 || onlyIn2.Length > 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Files missing from one side were ignored for the merge. Directory 1 only: {FormatPathList(onlyIn1)}. Directory 2 only: {FormatPathList(onlyIn2)}.",
                expected: "Only files present in both selected mod directories are merged"));
        }

        var selectedBoth = selected1.Intersect(selected2, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selectedBoth.Length == 0)
        {
            return new MergePlan(files, conflicts, outputs);
        }

        var resolutionMap = resolutions
            .Where(resolution => !string.IsNullOrWhiteSpace(resolution.ConflictId))
            .ToDictionary(
                resolution => resolution.ConflictId,
                resolution => resolution.Source,
                StringComparer.Ordinal);

        foreach (var relativePath in selectedBoth)
        {
            MergeFile(
                paths,
                root1,
                root2,
                relativePath,
                mergeMode,
                includeOutputs,
                resolutionMap,
                files,
                conflicts,
                outputs,
                diagnostics);
        }

        return new MergePlan(files, conflicts, outputs);
    }

    private static void MergeFile(
        ProjectPaths paths,
        string root1,
        string root2,
        string relativePath,
        string mergeMode,
        bool includeOutputs,
        IReadOnlyDictionary<string, string> resolutionMap,
        ICollection<SwShModMergerFilePreviewRecord> files,
        ICollection<SwShModMergerConflictRecord> conflicts,
        ICollection<MergeOutput> outputs,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var supportKind = GetSupportKind(relativePath);
        var relativeInsideRomFs = relativePath[RomFsPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        var modPath1 = Path.Combine(root1, relativeInsideRomFs);
        var modPath2 = Path.Combine(root2, relativeInsideRomFs);
        var basePath = string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            ? null
            : Path.Combine(paths.BaseRomFsPath, relativeInsideRomFs);

        if (!File.Exists(modPath1) || !File.Exists(modPath2))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Selected file '{relativePath}' was ignored because it is missing from one mod directory.",
                file: relativePath,
                expected: "Only files present in both selected mod directories are merged"));
            return;
        }

        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Vanilla base file '{relativePath}' could not be found. Mod Merger needs vanilla bytes to identify safe changes.",
                file: relativePath,
                expected: "Matching file under Base RomFS"));
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "error", CreateMissingBaseSummary(), 0, 0, 0));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var mod1Bytes = File.ReadAllBytes(modPath1);
            var mod2Bytes = File.ReadAllBytes(modPath2);
            var directory1ChangeCount = CountChangedRanges(baseBytes, mod1Bytes);
            var directory2ChangeCount = CountChangedRanges(baseBytes, mod2Bytes);

            if (IsReplacementMode(mergeMode))
            {
                MergeFileByReplacementMode(
                    relativePath,
                    supportKind,
                    mergeMode,
                    baseBytes,
                    mod1Bytes,
                    mod2Bytes,
                    directory1ChangeCount,
                    directory2ChangeCount,
                    includeOutputs,
                    files,
                    outputs);
                return;
            }

            if (mod1Bytes.SequenceEqual(mod2Bytes))
            {
                if (includeOutputs)
                {
                    outputs.Add(CreateMergeOutput(relativePath, mod1Bytes));
                }

                files.Add(CreateFilePreview(
                    relativePath,
                    supportKind,
                    "ready",
                    "unchanged",
                    CreateIdenticalFileSummary(),
                    directory1ChangeCount,
                    directory2ChangeCount,
                    0));
                return;
            }

            if (mod1Bytes.SequenceEqual(baseBytes))
            {
                if (includeOutputs)
                {
                    outputs.Add(CreateMergeOutput(relativePath, mod2Bytes));
                }

                files.Add(CreateFilePreview(
                    relativePath,
                    supportKind,
                    "ready",
                    "singleSource",
                    CreateSingleSourceSummary("mod2"),
                    directory1ChangeCount,
                    directory2ChangeCount,
                    0));
                return;
            }

            if (mod2Bytes.SequenceEqual(baseBytes))
            {
                if (includeOutputs)
                {
                    outputs.Add(CreateMergeOutput(relativePath, mod1Bytes));
                }

                files.Add(CreateFilePreview(
                    relativePath,
                    supportKind,
                    "ready",
                    "singleSource",
                    CreateSingleSourceSummary("mod1"),
                    directory1ChangeCount,
                    directory2ChangeCount,
                    0));
                return;
            }

            if (baseBytes.Length != mod1Bytes.Length || baseBytes.Length != mod2Bytes.Length)
            {
                var conflict = CreateWholeFileConflict(relativePath, mod1Bytes, mod2Bytes, resolutionMap);
                conflicts.Add(conflict);
                var resolvedBytes = ResolveConflictBytes(conflict.Resolution, mod1Bytes, mod2Bytes);
                if (includeOutputs && resolvedBytes is not null)
                {
                    outputs.Add(CreateMergeOutput(relativePath, resolvedBytes));
                }

                files.Add(CreateFilePreview(
                    relativePath,
                    supportKind,
                    conflict.Resolution is null ? "needsResolution" : "ready",
                    conflict.Resolution is null ? "needsChoice" : "manualChoice",
                    conflict.Resolution is null
                        ? CreateLengthConflictSummary()
                        : CreateResolvedWholeFileSummary(conflict.Resolution),
                    directory1ChangeCount,
                    directory2ChangeCount,
                    conflict.Resolution is null ? 1 : 0));
                return;
            }

            var merged = includeOutputs ? baseBytes.ToArray() : null;
            var fileConflicts = new List<SwShModMergerConflictRecord>();
            var index = 0;
            while (index < baseBytes.Length)
            {
                var changed1 = mod1Bytes[index] != baseBytes[index];
                var changed2 = mod2Bytes[index] != baseBytes[index];

                if (changed1 && changed2 && mod1Bytes[index] != mod2Bytes[index])
                {
                    var start = index;
                    do
                    {
                        index++;
                    }
                    while (index < baseBytes.Length
                        && mod1Bytes[index] != baseBytes[index]
                        && mod2Bytes[index] != baseBytes[index]
                        && mod1Bytes[index] != mod2Bytes[index]);

                    var end = index - 1;
                    var conflict = CreateByteRangeConflict(relativePath, start, end, mod1Bytes, mod2Bytes, resolutionMap);
                    fileConflicts.Add(conflict);
                    var resolvedBytes = ResolveConflictBytes(conflict.Resolution, mod1Bytes, mod2Bytes);
                    if (merged is not null && resolvedBytes is not null)
                    {
                        Array.Copy(resolvedBytes, start, merged, start, end - start + 1);
                    }

                    continue;
                }

                if (merged is not null && changed1)
                {
                    merged[index] = mod1Bytes[index];
                }
                else if (merged is not null && changed2)
                {
                    merged[index] = mod2Bytes[index];
                }

                index++;
            }

            foreach (var conflict in fileConflicts)
            {
                conflicts.Add(conflict);
            }

            if (includeOutputs && merged is not null && fileConflicts.All(conflict => conflict.Resolution is not null))
            {
                outputs.Add(CreateMergeOutput(relativePath, merged));
            }

            var unresolvedCount = fileConflicts.Count(conflict => conflict.Resolution is null);
            var resolvedChoiceCount = fileConflicts.Count - unresolvedCount;
            files.Add(CreateFilePreview(
                relativePath,
                supportKind,
                unresolvedCount == 0 ? "ready" : "needsResolution",
                unresolvedCount > 0
                    ? "needsChoice"
                    : resolvedChoiceCount > 0
                        ? "manualChoice"
                        : GetReadyMergeKind(relativePath),
                unresolvedCount > 0
                    ? CreateNeedsChoiceSummary(relativePath, unresolvedCount)
                    : resolvedChoiceCount > 0
                        ? CreateResolvedOverlapSummary(relativePath, resolvedChoiceCount)
                        : CreateSmartMergeSummary(relativePath, baseBytes, mod1Bytes, mod2Bytes),
                directory1ChangeCount,
                directory2ChangeCount,
                unresolvedCount));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Selected file could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable RomFS file"));
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "error", CreateReadErrorSummary(), 0, 0, 0));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Selected file could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable RomFS file"));
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "error", CreateReadErrorSummary(), 0, 0, 0));
        }
    }

    private static void MergeFileByReplacementMode(
        string relativePath,
        string supportKind,
        string mergeMode,
        byte[] baseBytes,
        byte[] mod1Bytes,
        byte[] mod2Bytes,
        int directory1ChangeCount,
        int directory2ChangeCount,
        bool includeOutputs,
        ICollection<SwShModMergerFilePreviewRecord> files,
        ICollection<MergeOutput> outputs)
    {
        if (mod1Bytes.SequenceEqual(mod2Bytes))
        {
            if (includeOutputs)
            {
                outputs.Add(CreateMergeOutput(relativePath, mod1Bytes));
            }

            files.Add(CreateFilePreview(
                relativePath,
                supportKind,
                "ready",
                "unchanged",
                CreateIdenticalFileSummary(),
                directory1ChangeCount,
                directory2ChangeCount,
                0));
            return;
        }

        if (mod1Bytes.SequenceEqual(baseBytes) && !mod2Bytes.SequenceEqual(baseBytes))
        {
            if (includeOutputs)
            {
                outputs.Add(CreateMergeOutput(relativePath, mod2Bytes));
            }

            files.Add(CreateFilePreview(
                relativePath,
                supportKind,
                "ready",
                "singleSource",
                CreateSingleSourceSummary("mod2"),
                directory1ChangeCount,
                directory2ChangeCount,
                0));
            return;
        }

        if (mod2Bytes.SequenceEqual(baseBytes) && !mod1Bytes.SequenceEqual(baseBytes))
        {
            if (includeOutputs)
            {
                outputs.Add(CreateMergeOutput(relativePath, mod1Bytes));
            }

            files.Add(CreateFilePreview(
                relativePath,
                supportKind,
                "ready",
                "singleSource",
                CreateSingleSourceSummary("mod1"),
                directory1ChangeCount,
                directory2ChangeCount,
                0));
            return;
        }

        var selectedSource = mergeMode == SwShModMergerMergeModes.PreferMod1 ? "mod1" : "mod2";
        if (includeOutputs)
        {
            var selectedBytes = selectedSource == "mod1" ? mod1Bytes : mod2Bytes;
            outputs.Add(CreateMergeOutput(relativePath, selectedBytes));
        }

        files.Add(CreateFilePreview(
            relativePath,
            supportKind,
            "ready",
            "replacement",
            CreateReplacementSummary(selectedSource),
            directory1ChangeCount,
            directory2ChangeCount,
            0));
    }

    private static IReadOnlyList<SwShModMergerFileRecord> ScanModDirectory(
        string? directory,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return [];
        }

        var romFsRoot = ResolveRomFsRoot(directory);
        if (romFsRoot is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mod directory does not exist or does not contain a RomFS root.",
                field,
                expected: "Existing directory with a romfs folder or a selected romfs folder"));
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(romFsRoot, "*", SearchOption.AllDirectories)
                .Select(path => CreateFileRecord(romFsRoot, path))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Mod directory could not be scanned: {exception.Message}",
                field,
                expected: "Readable RomFS directory"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Mod directory could not be scanned: {exception.Message}",
                field,
                expected: "Readable RomFS directory"));
            return [];
        }
    }

    private static SwShModMergerFileRecord CreateFileRecord(string romFsRoot, string path)
    {
        var relativeInsideRomFs = Path.GetRelativePath(romFsRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        var relativePath = RomFsPrefix + relativeInsideRomFs;
        var fileInfo = new FileInfo(path);

        return new SwShModMergerFileRecord(
            relativePath,
            Path.GetFileName(path),
            fileInfo.Length,
            GetSupportKind(relativePath),
            "mergeable");
    }

    private static string? ResolveRomFsRoot(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        if (string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath)), "romfs", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var childRomFs = Path.Combine(fullPath, "romfs");
        return Directory.Exists(childRomFs) ? childRomFs : null;
    }

    private static SortedSet<string> NormalizeSelectedFiles(IReadOnlyList<string> selectedFiles)
    {
        var normalized = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectedFile in selectedFiles)
        {
            if (string.IsNullOrWhiteSpace(selectedFile))
            {
                continue;
            }

            var path = selectedFile.Replace('\\', '/').Trim();
            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                path = path.TrimStart('/');
            }

            if (!path.StartsWith(RomFsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (path.Contains("../", StringComparison.Ordinal) || path.Contains("/..", StringComparison.Ordinal))
            {
                continue;
            }

            normalized.Add(path);
        }

        return normalized;
    }

    private static SwShModMergerPreview CreatePreview(
        string mergeMode,
        IReadOnlyList<SwShModMergerFilePreviewRecord> files,
        IReadOnlyList<SwShModMergerConflictRecord> conflicts,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var unresolvedConflictCount = conflicts.Count(conflict => conflict.Resolution is null);
        var hasErrors = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var readyFileCount = files.Count(file => string.Equals(file.Status, "ready", StringComparison.Ordinal));
        var conflictFileCount = files.Count(file => string.Equals(file.Status, "needsResolution", StringComparison.Ordinal));
        var canApply = files.Count > 0 && unresolvedConflictCount == 0 && !hasErrors;
        var status = hasErrors
            ? "blocked"
            : unresolvedConflictCount > 0
                ? "needsResolution"
                : files.Count == 0
                    ? "empty"
                    : "ready";

        return new SwShModMergerPreview(
            canApply,
            status,
            mergeMode,
            files.Count,
            readyFileCount,
            conflictFileCount,
            unresolvedConflictCount,
            files,
            conflicts,
            diagnostics);
    }

    private static SwShModMergerWorkflow CreateWorkflow(
        OpenedProject project,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<SwShModMergerFileRecord> directory1Files,
        IReadOnlyList<SwShModMergerFileRecord> directory2Files,
        int matchingFileCount,
        string? outputRootPath,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var summary = new SwShWorkflowSummary(
            WorkflowId,
            "Mod Merger",
            "Merge matching RomFS files from two mod folders and write safe outputs directly to Output Root.",
            project.Health.CanOpenEditableWorkflows
                ? SwShWorkflowAvailability.Available
                : SwShWorkflowAvailability.Disabled,
            diagnostics);

        return new SwShModMergerWorkflow(
            summary,
            string.IsNullOrWhiteSpace(modDirectory1) ? null : modDirectory1,
            string.IsNullOrWhiteSpace(modDirectory2) ? null : modDirectory2,
            outputRootPath,
            directory1Files,
            directory2Files,
            new SwShModMergerWorkflowStats(directory1Files.Count, directory2Files.Count, matchingFileCount),
            diagnostics);
    }

    private static SwShModMergerFilePreviewRecord CreateFilePreview(
        string relativePath,
        string supportKind,
        string status,
        string mergeKind,
        string summary,
        int directory1ChangeCount,
        int directory2ChangeCount,
        int conflictCount)
    {
        return new SwShModMergerFilePreviewRecord(
            relativePath,
            relativePath,
            supportKind,
            status,
            mergeKind,
            summary,
            directory1ChangeCount,
            directory2ChangeCount,
            conflictCount);
    }

    private static SwShModMergerConflictRecord CreateWholeFileConflict(
        string relativePath,
        byte[] mod1Bytes,
        byte[] mod2Bytes,
        IReadOnlyDictionary<string, string> resolutionMap)
    {
        var conflictId = CreateConflictId(relativePath, "length");
        resolutionMap.TryGetValue(conflictId, out var resolution);

        return new SwShModMergerConflictRecord(
            conflictId,
            relativePath,
            "Whole file",
            CreateLengthConflictSummary(),
            $"{mod1Bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes",
            $"{mod2Bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes",
            NormalizeResolution(resolution));
    }

    private static SwShModMergerConflictRecord CreateByteRangeConflict(
        string relativePath,
        int start,
        int end,
        byte[] mod1Bytes,
        byte[] mod2Bytes,
        IReadOnlyDictionary<string, string> resolutionMap)
    {
        var conflictId = CreateConflictId(relativePath, $"{start}-{end}");
        resolutionMap.TryGetValue(conflictId, out var resolution);
        var label = DescribeRangePlain(relativePath, start, end);

        return new SwShModMergerConflictRecord(
            conflictId,
            relativePath,
            label,
            IsDataAwarePath(relativePath)
                ? $"Both mods changed {label} differently. Choose which mod should win for that value."
                : "KM cannot inspect this file type yet, and both mods changed the same byte range. Choose which mod should win for this overlap.",
            FormatByteRange(mod1Bytes, start, end),
            FormatByteRange(mod2Bytes, start, end),
            NormalizeResolution(resolution));
    }

    private static byte[]? ResolveConflictBytes(string? resolution, byte[] mod1Bytes, byte[] mod2Bytes)
    {
        return resolution switch
        {
            "mod1" => mod1Bytes,
            "mod2" => mod2Bytes,
            _ => null,
        };
    }

    private static string? NormalizeResolution(string? resolution)
    {
        return resolution switch
        {
            "mod1" => "mod1",
            "mod2" => "mod2",
            _ => null,
        };
    }

    private static string CreateConflictId(string relativePath, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{relativePath}|{key}"));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string FormatByteRange(byte[] data, int start, int end)
    {
        var length = Math.Min(end - start + 1, 16);
        var value = Convert.ToHexString(data.AsSpan(start, length).ToArray());
        var pairs = string.Join(" ", Enumerable.Range(0, value.Length / 2).Select(index => value.Substring(index * 2, 2)));
        return end - start + 1 > length ? $"{pairs} ..." : pairs;
    }

    private static string DescribeRange(string relativePath, int start, int end)
    {
        var offset = start == end
            ? $"0x{start:X}"
            : $"0x{start:X}-0x{end:X}";

        if (relativePath.StartsWith(SwShTrainerDataFile.TrainerDataRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{DescribeTrainerDataField(start)} ({offset})";
        }

        if (relativePath.StartsWith(SwShTrainerTeamFile.TrainerPokeRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            var slot = (start / SwShTrainerTeamFile.RowSize) + 1;
            var rowOffset = start % SwShTrainerTeamFile.RowSize;
            return $"Trainer party slot {slot} {DescribeTrainerPokemonField(rowOffset)} ({offset})";
        }

        if (string.Equals(relativePath, SwShPersonalTable.PersonalDataRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            var personalId = start / SwShPersonalTable.RecordSize;
            return $"Pokemon personal #{personalId} ({offset})";
        }

        if (string.Equals(relativePath, SwShItemTable.ItemDataRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return $"Item data bytes {offset}";
        }

        if (string.Equals(relativePath, "romfs/bin/shop_data.bin", StringComparison.OrdinalIgnoreCase))
        {
            return $"Shop data bytes {offset}";
        }

        return $"Bytes {offset}";
    }

    private static string DescribeTrainerDataField(int offset)
    {
        return offset switch
        {
            0x00 or 0x01 => "Trainer class",
            0x02 => "Battle type",
            0x03 => "Pokemon count",
            0x04 or 0x05 => "Trainer item 1",
            0x06 or 0x07 => "Trainer item 2",
            0x08 or 0x09 => "Trainer item 3",
            0x0A or 0x0B => "Trainer item 4",
            >= 0x0C and <= 0x0F => "AI flags",
            0x10 => "Heal flag",
            0x11 => "Prize money",
            0x12 or 0x13 => "Gift item",
            _ => "Trainer data bytes",
        };
    }

    private static string DescribeTrainerPokemonField(int rowOffset)
    {
        return rowOffset switch
        {
            0x00 => "gender/ability",
            0x01 => "nature",
            0x02 => "HP EV",
            0x03 => "Attack EV",
            0x04 => "Defense EV",
            0x05 => "Special Attack EV",
            0x06 => "Special Defense EV",
            0x07 => "Speed EV",
            0x08 => "Dynamax level",
            0x09 => "Gigantamax flag",
            0x0A or 0x0B => "level",
            0x0C or 0x0D => "species",
            0x0E or 0x0F => "form",
            0x10 or 0x11 => "held item",
            0x12 or 0x13 => "move 1",
            0x14 or 0x15 => "move 2",
            0x16 or 0x17 => "move 3",
            0x18 or 0x19 => "move 4",
            >= 0x1C and <= 0x1F => "IV/shiny/Dynamax flags",
            _ => "record bytes",
        };
    }

    private static string CreateMissingBaseSummary()
    {
        return "KM cannot merge this file because the matching vanilla RomFS file is missing. Add the base file, then stage the merge again.";
    }

    private static string CreateIdenticalFileSummary()
    {
        return "Both mods contain the same file contents, so KM will write that shared file to the output.";
    }

    private static string CreateSingleSourceSummary(string source)
    {
        return $"Only {FormatResolutionSource(source)} changed this file from vanilla, so KM will use that changed file.";
    }

    private static string CreateLengthConflictSummary()
    {
        return "Smart Merge cannot combine this file because one or both mods changed the file length. Choose which whole file should win.";
    }

    private static string CreateResolvedWholeFileSummary(string resolution)
    {
        return $"You chose {FormatResolutionSource(resolution)} for this whole-file conflict, so KM will write that mod's file to the output.";
    }

    private static string CreateNeedsChoiceSummary(string relativePath, int unresolvedCount)
    {
        var plural = unresolvedCount == 1 ? string.Empty : "s";
        if (IsDataAwarePath(relativePath))
        {
            return $"Smart Merge found {unresolvedCount} value overlap{plural} where both mods changed the same thing differently. Open Overlaps and choose which mod wins for each one.";
        }

        return $"KM cannot inspect this file type yet, and Smart Merge found {unresolvedCount} byte overlap{plural}. Open Overlaps and choose which mod wins for each one.";
    }

    private static string CreateResolvedOverlapSummary(string relativePath, int resolvedChoiceCount)
    {
        var plural = resolvedChoiceCount == 1 ? string.Empty : "s";
        if (IsDataAwarePath(relativePath))
        {
            return $"Choice applied for {resolvedChoiceCount} value overlap{plural}. KM will use your choice there and still combine the other compatible data-field changes.";
        }

        return $"Choice applied for {resolvedChoiceCount} byte overlap{plural}. KM will use your choice there and still combine other non-overlapping byte changes.";
    }

    private static string CreateReadErrorSummary()
    {
        return "KM could not read this selected file. Check that the file still exists and is not locked, then stage the merge again.";
    }

    private static string CreateReplacementSummary(string selectedSource)
    {
        return $"Replacement mode is active. Both mods changed this file, so KM will write the whole file from {FormatResolutionSource(selectedSource)}.";
    }

    private static string CreateSmartMergeSummary(
        string relativePath,
        byte[] baseBytes,
        byte[] mod1Bytes,
        byte[] mod2Bytes)
    {
        if (!IsDataAwarePath(relativePath))
        {
            return "Safe merge will combine the non-overlapping byte changes. KM cannot inspect this file type yet, so it cannot name the data fields inside it.";
        }

        var directory1Changes = FindChangedRangeDescriptions(relativePath, baseBytes, mod1Bytes);
        var directory2Changes = FindChangedRangeDescriptions(relativePath, baseBytes, mod2Bytes);

        return $"Smart Merge will combine Mod Directory 1 changes ({FormatChangeList(directory1Changes)}) with Mod Directory 2 changes ({FormatChangeList(directory2Changes)}).";
    }

    private static IReadOnlyList<string> FindChangedRangeDescriptions(
        string relativePath,
        byte[] baseBytes,
        byte[] modBytes)
    {
        var descriptions = new List<string>();
        var maxLength = Math.Max(baseBytes.Length, modBytes.Length);
        var index = 0;

        while (index < maxLength)
        {
            var changed = index >= baseBytes.Length
                || index >= modBytes.Length
                || baseBytes[index] != modBytes[index];
            if (!changed)
            {
                index++;
                continue;
            }

            var start = index;
            do
            {
                index++;
            }
            while (index < maxLength
                && (index >= baseBytes.Length
                    || index >= modBytes.Length
                    || baseBytes[index] != modBytes[index]));

            var end = index - 1;
            var description = DescribeRangePlain(relativePath, start, end);
            if (!descriptions.Contains(description, StringComparer.OrdinalIgnoreCase))
            {
                descriptions.Add(description);
            }
        }

        return descriptions;
    }

    private static string FormatChangeList(IReadOnlyList<string> changes)
    {
        return changes.Count switch
        {
            0 => "no data-field changes",
            1 => changes[0],
            2 => $"{changes[0]}, {changes[1]}",
            _ => $"{changes[0]}, {changes[1]}, and {changes.Count - 2} more",
        };
    }

    private static string GetReadyMergeKind(string relativePath)
    {
        return IsDataAwarePath(relativePath) ? "smartMerge" : "safeMerge";
    }

    private static string DescribeRangePlain(string relativePath, int start, int end)
    {
        if (relativePath.StartsWith(SwShTrainerDataFile.TrainerDataRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return DescribeTrainerDataField(start);
        }

        if (relativePath.StartsWith(SwShTrainerTeamFile.TrainerPokeRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            var slot = (start / SwShTrainerTeamFile.RowSize) + 1;
            var rowOffset = start % SwShTrainerTeamFile.RowSize;
            return $"Trainer party slot {slot} {DescribeTrainerPokemonField(rowOffset)}";
        }

        if (string.Equals(relativePath, SwShPersonalTable.PersonalDataRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            var personalId = start / SwShPersonalTable.RecordSize;
            return $"Pokemon personal record {personalId}";
        }

        if (string.Equals(relativePath, SwShItemTable.ItemDataRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return "item data";
        }

        if (string.Equals(relativePath, "romfs/bin/shop_data.bin", StringComparison.OrdinalIgnoreCase))
        {
            return "shop data";
        }

        return start == end ? "one byte" : "a byte range";
    }

    private static bool IsDataAwarePath(string relativePath)
    {
        return string.Equals(relativePath, "romfs/bin/shop_data.bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShItemTable.ItemDataRelativePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShPersonalTable.PersonalDataRelativePath, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(SwShTrainerDataFile.TrainerDataRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(SwShTrainerTeamFile.TrainerPokeRootRelativePath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountChangedRanges(byte[] baseBytes, byte[] modBytes)
    {
        var maxLength = Math.Max(baseBytes.Length, modBytes.Length);
        var count = 0;
        var inRange = false;

        for (var index = 0; index < maxLength; index++)
        {
            var changed = index >= baseBytes.Length
                || index >= modBytes.Length
                || baseBytes[index] != modBytes[index];
            if (changed && !inRange)
            {
                count++;
                inRange = true;
            }
            else if (!changed)
            {
                inRange = false;
            }
        }

        return count;
    }

    private static string GetSupportKind(string relativePath)
    {
        if (string.Equals(relativePath, "romfs/bin/shop_data.bin", StringComparison.OrdinalIgnoreCase))
        {
            return "Shop data binary diff";
        }

        if (string.Equals(relativePath, SwShItemTable.ItemDataRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return "Item data binary diff";
        }

        if (string.Equals(relativePath, SwShPersonalTable.PersonalDataRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return "Pokemon personal binary diff";
        }

        if (relativePath.StartsWith(SwShTrainerDataFile.TrainerDataRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "Trainer data binary diff";
        }

        if (relativePath.StartsWith(SwShTrainerTeamFile.TrainerPokeRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "Trainer party binary diff";
        }

        return "RomFS binary diff";
    }

    private static string NormalizeMergeMode(string? mergeMode)
    {
        return mergeMode switch
        {
            SwShModMergerMergeModes.PreferMod1 => SwShModMergerMergeModes.PreferMod1,
            SwShModMergerMergeModes.PreferMod2 => SwShModMergerMergeModes.PreferMod2,
            _ => SwShModMergerMergeModes.Smart,
        };
    }

    private static bool IsReplacementMode(string mergeMode)
    {
        return mergeMode is SwShModMergerMergeModes.PreferMod1 or SwShModMergerMergeModes.PreferMod2;
    }

    private static string FormatPathList(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", paths.Take(3)) + (paths.Count > 3 ? ", ..." : string.Empty);
    }

    private static string FormatResolutionSource(string resolution)
    {
        return resolution == "mod1" ? "Mod Directory 1" : "Mod Directory 2";
    }

    private static MergeOutput CreateMergeOutput(string relativePath, byte[] contents)
    {
        var plannedContents = contents.ToArray();
        return new MergeOutput(
            relativePath,
            plannedContents,
            plannedContents.LongLength,
            ComputeSha256(plannedContents));
    }

    private static bool ValidatePlanIntegrity(
        IReadOnlyList<MergeOutput> outputs,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var isValid = true;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs)
        {
            if (!seenPaths.Add(output.RelativePath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Merge plan contains the same output file more than once.",
                    file: output.RelativePath,
                    expected: "One planned write per RomFS output file"));
                isValid = false;
            }

            if (output.Contents.LongLength != output.ExpectedLength)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Merge plan output length changed before writing.",
                    file: output.RelativePath,
                    expected: $"{output.ExpectedLength.ToString(CultureInfo.InvariantCulture)} bytes"));
                isValid = false;
            }

            var actualSha256 = ComputeSha256(output.Contents);
            if (!string.Equals(actualSha256, output.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Merge plan output bytes changed before writing.",
                    file: output.RelativePath,
                    expected: "Planned bytes matching the original merge hash"));
                isValid = false;
            }
        }

        return isValid;
    }

    private static void WriteVerifiedOutput(string targetPath, MergeOutput output)
    {
        ValidateOutputContents(output);
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Output target directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, output.Contents);
            VerifyFileContents(tempPath, output);
            File.Move(tempPath, targetPath, overwrite: true);
            VerifyFileContents(targetPath, output);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static void ValidateOutputContents(MergeOutput output)
    {
        if (output.Contents.LongLength != output.ExpectedLength)
        {
            throw new InvalidDataException(
                $"Planned output length changed before write for '{output.RelativePath}'.");
        }

        var actualSha256 = ComputeSha256(output.Contents);
        if (!string.Equals(actualSha256, output.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Planned output hash changed before write for '{output.RelativePath}'.");
        }
    }

    private static void ClearOutputContents(MergeOutput output)
    {
        Array.Clear(output.Contents);
    }

    private static void VerifyFileContents(string path, MergeOutput output)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length != output.ExpectedLength)
        {
            throw new InvalidDataException(
                $"Expected {output.ExpectedLength.ToString(CultureInfo.InvariantCulture)} bytes but wrote {fileInfo.Length.ToString(CultureInfo.InvariantCulture)} bytes.");
        }

        var actualSha256 = ComputeFileSha256(path);
        if (!string.Equals(actualSha256, output.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Written file hash does not match the planned merge bytes.");
        }
    }

    private static string ComputeSha256(byte[] contents)
    {
        return Convert.ToHexString(SHA256.HashData(contents));
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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

    private sealed record MergePlan(
        IReadOnlyList<SwShModMergerFilePreviewRecord> Files,
        IReadOnlyList<SwShModMergerConflictRecord> Conflicts,
        IReadOnlyList<MergeOutput> Outputs);

    private sealed record MergeOutput(
        string RelativePath,
        byte[] Contents,
        long ExpectedLength,
        string Sha256);
}
