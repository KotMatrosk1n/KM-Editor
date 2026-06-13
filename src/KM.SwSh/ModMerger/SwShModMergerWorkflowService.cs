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
        IReadOnlyList<SwShModMergerConflictResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selectedDirectory1Files);
        ArgumentNullException.ThrowIfNull(selectedDirectory2Files);
        ArgumentNullException.ThrowIfNull(resolutions);

        var workflow = Load(paths, modDirectory1, modDirectory2);
        var diagnostics = workflow.Diagnostics.ToList();
        var plan = BuildPlan(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            resolutions,
            diagnostics);
        var preview = CreatePreview(plan.Files, plan.Conflicts, diagnostics);

        return new SwShModMergerStageResult(workflow, preview, diagnostics);
    }

    public SwShModMergerApplyResult Apply(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<string> selectedDirectory1Files,
        IReadOnlyList<string> selectedDirectory2Files,
        IReadOnlyList<SwShModMergerConflictResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selectedDirectory1Files);
        ArgumentNullException.ThrowIfNull(selectedDirectory2Files);
        ArgumentNullException.ThrowIfNull(resolutions);

        var workflow = Load(paths, modDirectory1, modDirectory2);
        var diagnostics = workflow.Diagnostics.ToList();
        var plan = BuildPlan(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            resolutions,
            diagnostics);
        var preview = CreatePreview(plan.Files, plan.Conflicts, diagnostics);
        var writtenFiles = new List<string>();

        if (!preview.CanApply)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Resolve every Mod Merger conflict before applying the merge.",
                expected: "Conflict-free merge preview"));
            preview = CreatePreview(plan.Files, plan.Conflicts, diagnostics);
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
            preview = CreatePreview(plan.Files, plan.Conflicts, diagnostics);
            return new SwShModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
        }

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

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Applied {writtenFiles.Count} merged RomFS file{(writtenFiles.Count == 1 ? string.Empty : "s")} to Output Root."));
        }

        preview = CreatePreview(plan.Files, plan.Conflicts, diagnostics);
        return new SwShModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
    }

    private static MergePlan BuildPlan(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<string> selectedDirectory1Files,
        IReadOnlyList<string> selectedDirectory2Files,
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
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "Vanilla base file is missing.", 0, 0, 0));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var mod1Bytes = File.ReadAllBytes(modPath1);
            var mod2Bytes = File.ReadAllBytes(modPath2);
            var directory1ChangeCount = CountChangedRanges(baseBytes, mod1Bytes);
            var directory2ChangeCount = CountChangedRanges(baseBytes, mod2Bytes);

            if (mod1Bytes.SequenceEqual(mod2Bytes))
            {
                outputs.Add(new MergeOutput(relativePath, mod1Bytes));
                files.Add(CreateFilePreview(
                    relativePath,
                    supportKind,
                    "ready",
                    "Both mod directories contain identical bytes for this file.",
                    directory1ChangeCount,
                    directory2ChangeCount,
                    0));
                return;
            }

            if (mod1Bytes.SequenceEqual(baseBytes))
            {
                outputs.Add(new MergeOutput(relativePath, mod2Bytes));
                files.Add(CreateFilePreview(
                    relativePath,
                    supportKind,
                    "ready",
                    "Only Mod Directory 2 changes this file.",
                    directory1ChangeCount,
                    directory2ChangeCount,
                    0));
                return;
            }

            if (mod2Bytes.SequenceEqual(baseBytes))
            {
                outputs.Add(new MergeOutput(relativePath, mod1Bytes));
                files.Add(CreateFilePreview(
                    relativePath,
                    supportKind,
                    "ready",
                    "Only Mod Directory 1 changes this file.",
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
                if (resolvedBytes is not null)
                {
                    outputs.Add(new MergeOutput(relativePath, resolvedBytes));
                }

                files.Add(CreateFilePreview(
                    relativePath,
                    supportKind,
                    conflict.Resolution is null ? "needsResolution" : "ready",
                    conflict.Resolution is null
                        ? "Both mods changed this file and at least one copy changed file length."
                        : $"Whole-file conflict resolved with {FormatResolutionSource(conflict.Resolution)}.",
                    directory1ChangeCount,
                    directory2ChangeCount,
                    conflict.Resolution is null ? 1 : 0));
                return;
            }

            var merged = baseBytes.ToArray();
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
                    if (resolvedBytes is not null)
                    {
                        Array.Copy(resolvedBytes, start, merged, start, end - start + 1);
                    }

                    continue;
                }

                if (changed1)
                {
                    merged[index] = mod1Bytes[index];
                }
                else if (changed2)
                {
                    merged[index] = mod2Bytes[index];
                }

                index++;
            }

            foreach (var conflict in fileConflicts)
            {
                conflicts.Add(conflict);
            }

            if (fileConflicts.All(conflict => conflict.Resolution is not null))
            {
                outputs.Add(new MergeOutput(relativePath, merged));
            }

            var unresolvedCount = fileConflicts.Count(conflict => conflict.Resolution is null);
            files.Add(CreateFilePreview(
                relativePath,
                supportKind,
                unresolvedCount == 0 ? "ready" : "needsResolution",
                unresolvedCount == 0
                    ? "Non-overlapping byte changes can be merged safely."
                    : $"{unresolvedCount} overlap{(unresolvedCount == 1 ? string.Empty : "s")} need a Mod Directory choice.",
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
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "Selected file could not be read.", 0, 0, 0));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Selected file could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable RomFS file"));
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "Selected file could not be read.", 0, 0, 0));
        }
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
            "Both mods changed this file and at least one copy changed file length. Choose which whole file should win.",
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
        var label = DescribeRange(relativePath, start, end);

        return new SwShModMergerConflictRecord(
            conflictId,
            relativePath,
            label,
            "Both mods changed this byte range differently from vanilla.",
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
        byte[] Contents);
}
