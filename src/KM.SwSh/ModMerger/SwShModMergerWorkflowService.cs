// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.FpsPatch;
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

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly Action<int, string>? beforeCommitOutput;

    public SwShModMergerWorkflowService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
    }

    internal SwShModMergerWorkflowService(Action<int, string> beforeCommitOutput)
        : this()
    {
        this.beforeCommitOutput = beforeCommitOutput
            ?? throw new ArgumentNullException(nameof(beforeCommitOutput));
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
        var reviewTokenBefore = ComputeReviewToken(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            normalizedMergeMode,
            resolutions,
            diagnostics);
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
        var reviewTokenAfter = ComputeReviewToken(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            normalizedMergeMode,
            resolutions,
            diagnostics);
        if ((reviewTokenBefore is not null || reviewTokenAfter is not null)
            && (reviewTokenBefore is null
                || reviewTokenAfter is null
                || !string.Equals(reviewTokenBefore, reviewTokenAfter, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mod Merger sources changed while the preview was being prepared. Stage the merge again.",
                expected: "Stable selected source files"));
        }

        var preview = CreatePreview(
            normalizedMergeMode,
            reviewTokenAfter ?? string.Empty,
            plan.Files,
            plan.Conflicts,
            diagnostics);

        return new SwShModMergerStageResult(workflow, preview, diagnostics);
    }

    [Obsolete("Call Stage, review the preview, then call ApplyReviewed with its ReviewToken.")]
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
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Direct Mod Merger apply is no longer supported. Call Stage, review the preview, then call ApplyReviewed with the preview ReviewToken.",
            expected: "Stage followed by ApplyReviewed with ReviewToken"));
        var preview = CreatePreview(
            NormalizeMergeMode(mergeMode),
            reviewToken: string.Empty,
            files: [],
            conflicts: [],
            diagnostics: diagnostics);

        return new SwShModMergerApplyResult(workflow, preview, [], diagnostics);
    }

    public SwShModMergerApplyResult ApplyReviewed(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<string> selectedDirectory1Files,
        IReadOnlyList<string> selectedDirectory2Files,
        IReadOnlyList<SwShModMergerConflictResolution> resolutions,
        string? mergeMode,
        string? reviewToken)
    {
        return ApplyCore(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            resolutions,
            mergeMode,
            reviewToken,
            requireReviewToken: true);
    }

    private SwShModMergerApplyResult ApplyCore(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<string> selectedDirectory1Files,
        IReadOnlyList<string> selectedDirectory2Files,
        IReadOnlyList<SwShModMergerConflictResolution> resolutions,
        string? mergeMode,
        string? reviewToken,
        bool requireReviewToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selectedDirectory1Files);
        ArgumentNullException.ThrowIfNull(selectedDirectory2Files);
        ArgumentNullException.ThrowIfNull(resolutions);

        var workflow = Load(paths, modDirectory1, modDirectory2);
        var diagnostics = workflow.Diagnostics.ToList();
        var normalizedMergeMode = NormalizeMergeMode(mergeMode);
        var currentTokenBefore = ComputeReviewToken(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            normalizedMergeMode,
            resolutions,
            diagnostics);
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
        var currentTokenAfter = ComputeReviewToken(
            paths,
            modDirectory1,
            modDirectory2,
            selectedDirectory1Files,
            selectedDirectory2Files,
            normalizedMergeMode,
            resolutions,
            diagnostics);
        if ((currentTokenBefore is not null || currentTokenAfter is not null)
            && (currentTokenBefore is null
                || currentTokenAfter is null
                || !string.Equals(currentTokenBefore, currentTokenAfter, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mod Merger sources changed while the merge was being prepared. Stage the merge again before applying.",
                expected: "Stable selected source files"));
        }

        if (requireReviewToken && string.IsNullOrWhiteSpace(reviewToken))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage and review the Mod Merger preview before applying.",
                expected: "Current staged Mod Merger review token"));
        }
        else if (reviewToken is not null
            && (currentTokenAfter is null
                || !string.Equals(reviewToken, currentTokenAfter, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mod Merger preview is stale because a selected source, project path, or output target changed. Stage the merge again before applying.",
                expected: "Selected sources, project paths, and output targets matching the staged preview"));
        }

        var activeReviewToken = currentTokenAfter ?? string.Empty;
        var preview = CreatePreview(
            normalizedMergeMode,
            activeReviewToken,
            plan.Files,
            plan.Conflicts,
            diagnostics);
        var writtenFiles = new List<string>();

        if (!preview.CanApply)
        {
            if (preview.UnresolvedConflictCount > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Resolve every Mod Merger conflict before applying the merge.",
                    expected: "Conflict-free merge preview"));
            }

            preview = CreatePreview(
                normalizedMergeMode,
                activeReviewToken,
                plan.Files,
                plan.Conflicts,
                diagnostics);
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
            preview = CreatePreview(normalizedMergeMode, activeReviewToken, plan.Files, plan.Conflicts, diagnostics);
            return new SwShModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
        }

        if (!ValidatePlanIntegrity(plan.Outputs, diagnostics))
        {
            preview = CreatePreview(normalizedMergeMode, activeReviewToken, plan.Files, plan.Conflicts, diagnostics);
            return new SwShModMergerApplyResult(workflow, preview, writtenFiles, diagnostics);
        }

        var expectedCommitToken = reviewToken ?? currentTokenAfter;
        writtenFiles.AddRange(WriteOutputsTransactionally(
            outputRoot,
            plan.Outputs,
            diagnostics,
            () =>
            {
                var preCommitToken = ComputeReviewToken(
                    paths,
                    modDirectory1,
                    modDirectory2,
                    selectedDirectory1Files,
                    selectedDirectory2Files,
                    normalizedMergeMode,
                    resolutions,
                    diagnostics);
                return expectedCommitToken is not null
                    && string.Equals(expectedCommitToken, preCommitToken, StringComparison.Ordinal);
            },
            beforeCommitOutput));

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            activeReviewToken = ComputeReviewToken(
                paths,
                modDirectory1,
                modDirectory2,
                selectedDirectory1Files,
                selectedDirectory2Files,
                normalizedMergeMode,
                resolutions,
                diagnostics) ?? string.Empty;
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Applied {writtenFiles.Count} merged RomFS file{(writtenFiles.Count == 1 ? string.Empty : "s")} to Output Root."));
        }

        preview = CreatePreview(normalizedMergeMode, activeReviewToken, plan.Files, plan.Conflicts, diagnostics);
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
        var selected = selected1
            .Union(selected2, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Select at least one RomFS file before staging a merge.",
                expected: "At least one selected RomFS file"));
            return new MergePlan(files, conflicts, outputs);
        }

        AddFpsPatchOverlapDiagnostics(selected, diagnostics);

        var resolutionMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resolution in resolutions.Where(candidate => !string.IsNullOrWhiteSpace(candidate.ConflictId)))
        {
            if (resolutionMap.TryAdd(resolution.ConflictId, resolution.Source))
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Mod Merger conflict resolution was submitted more than once.",
                file: resolution.ConflictId,
                expected: "One resolution per conflict"));
        }

        foreach (var relativePath in selected)
        {
            if (selected1.Contains(relativePath) && selected2.Contains(relativePath))
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
            else if (selected1.Contains(relativePath))
            {
                CopySingleSourceFile(root1, relativePath, "mod1", includeOutputs, files, outputs, diagnostics);
            }
            else
            {
                CopySingleSourceFile(root2, relativePath, "mod2", includeOutputs, files, outputs, diagnostics);
            }
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
        var modPath1 = ResolveModFilePath(root1, relativePath, diagnostics);
        var modPath2 = ResolveModFilePath(root2, relativePath, diagnostics);
        var basePath = ResolveBaseFilePath(paths.BaseRomFsPath, relativePath, diagnostics);
        if (modPath1 is null || modPath2 is null)
        {
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "error", CreateReadErrorSummary(), 0, 0, 0));
            return;
        }

        var hasMod1File = File.Exists(modPath1);
        var hasMod2File = File.Exists(modPath2);
        if (!hasMod1File || !hasMod2File)
        {
            if (hasMod1File)
            {
                CopySingleSourceFile(root1, relativePath, "mod1", includeOutputs, files, outputs, diagnostics);
            }
            else if (hasMod2File)
            {
                CopySingleSourceFile(root2, relativePath, "mod2", includeOutputs, files, outputs, diagnostics);
            }
            else
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Selected file '{relativePath}' could not be found in either mod directory.",
                    file: relativePath,
                    expected: "Readable selected RomFS file"));
                files.Add(CreateFilePreview(relativePath, supportKind, "error", "error", CreateReadErrorSummary(), 0, 0, 0));
            }

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

    private static void CopySingleSourceFile(
        string root,
        string relativePath,
        string source,
        bool includeOutputs,
        ICollection<SwShModMergerFilePreviewRecord> files,
        ICollection<MergeOutput> outputs,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var supportKind = GetSupportKind(relativePath);
        var modPath = ResolveModFilePath(root, relativePath, diagnostics);
        if (modPath is null)
        {
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "error", CreateReadErrorSummary(), 0, 0, 0));
            return;
        }

        if (!File.Exists(modPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Selected file '{relativePath}' could not be copied because it is missing from {FormatResolutionSource(source)}.",
                file: relativePath,
                expected: "Readable selected RomFS file"));
            files.Add(CreateFilePreview(relativePath, supportKind, "error", "error", CreateReadErrorSummary(), 0, 0, 0));
            return;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(modPath);
            if (includeOutputs)
            {
                outputs.Add(CreateMergeOutput(relativePath, sourceBytes));
            }

            files.Add(CreateFilePreview(
                relativePath,
                supportKind,
                "ready",
                "singleSource",
                CreateSingleSourceCopySummary(source),
                source == "mod1" ? 1 : 0,
                source == "mod2" ? 1 : 0,
                0));
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
                .EnumerateFiles(romFsRoot, "*", RecursiveEnumeration)
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

    private static string? ResolveRomFsRoot(string? directory)
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

    private static string? ResolveModFilePath(
        string romFsRoot,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var relativeInsideRomFs = GetRelativeInsideRomFs(relativePath);
        if (Path.IsPathRooted(relativeInsideRomFs))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Selected Mod Merger path is rooted outside the RomFS source.",
                file: relativePath,
                expected: "RomFS path contained by the selected mod directory"));
            return null;
        }

        var fullRoot = Path.GetFullPath(romFsRoot);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativeInsideRomFs));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(fullRoot, fullPath)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Selected Mod Merger path escapes the RomFS source.",
                file: relativePath,
                expected: "RomFS path contained by the selected mod directory"));
            return null;
        }

        if (TraversesReparsePointBelowRoot(fullRoot, fullPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Selected Mod Merger path traverses a symbolic link or junction below the RomFS source.",
                file: relativePath,
                expected: "Physical path contained by the selected mod directory"));
            return null;
        }

        return fullPath;
    }

    private static string? ResolveBaseFilePath(
        string? baseRomFsPath,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(baseRomFsPath))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(baseRomFsPath);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, GetRelativeInsideRomFs(relativePath)));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(fullRoot, fullPath)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Vanilla base file path escapes Base RomFS.",
                file: relativePath,
                expected: "Base file physically contained by Base RomFS"));
            return null;
        }

        if (TraversesReparsePointBelowRoot(fullRoot, fullPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Vanilla base file path traverses a symbolic link or junction below Base RomFS.",
                file: relativePath,
                expected: "Base file physically contained by Base RomFS"));
            return null;
        }

        return fullPath;
    }

    private static string GetRelativeInsideRomFs(string relativePath)
    {
        return relativePath[RomFsPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
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

            var relativeInsideRomFs = path[RomFsPrefix.Length..];
            var segments = relativeInsideRomFs.Split('/');
            if (string.IsNullOrWhiteSpace(relativeInsideRomFs)
                || segments.Any(segment => string.IsNullOrEmpty(segment)
                    || string.Equals(segment, ".", StringComparison.Ordinal)
                    || string.Equals(segment, "..", StringComparison.Ordinal)
                    || segment.Contains(':')
                    || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                || Path.IsPathRooted(relativeInsideRomFs.Replace('/', Path.DirectorySeparatorChar)))
            {
                continue;
            }

            normalized.Add(path);
        }

        return normalized;
    }

    private static string? ComputeReviewToken(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2,
        IReadOnlyList<string> selectedDirectory1Files,
        IReadOnlyList<string> selectedDirectory2Files,
        string mergeMode,
        IReadOnlyList<SwShModMergerConflictResolution> resolutions,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var root1 = ResolveRomFsRoot(modDirectory1);
        var root2 = ResolveRomFsRoot(modDirectory2);
        if (root1 is null || root2 is null)
        {
            return null;
        }

        var selected1 = NormalizeSelectedFiles(selectedDirectory1Files);
        var selected2 = NormalizeSelectedFiles(selectedDirectory2Files);
        var selected = selected1
            .Union(selected2, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
        {
            return null;
        }

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendHashText(hash, "swsh-mod-merger-review-v2\n");
            AppendHashText(hash, $"game:{paths.SelectedGame}\n");
            AppendHashText(hash, $"base-romfs:{NormalizeReviewPath(paths.BaseRomFsPath)}\n");
            AppendHashText(hash, $"base-exefs:{NormalizeReviewPath(paths.BaseExeFsPath)}\n");
            AppendHashText(hash, $"output-root:{NormalizeReviewPath(paths.OutputRootPath)}\n");
            AppendHashText(hash, $"mod1-root:{NormalizeReviewPath(root1)}\n");
            AppendHashText(hash, $"mod2-root:{NormalizeReviewPath(root2)}\n");
            AppendHashText(hash, $"mode:{mergeMode}\n");
            foreach (var resolution in resolutions
                .OrderBy(candidate => candidate.ConflictId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Source, StringComparer.Ordinal))
            {
                AppendHashText(hash, $"resolution:{resolution.ConflictId}:{resolution.Source}\n");
            }

            foreach (var relativePath in selected)
            {
                var fromMod1 = selected1.Contains(relativePath);
                var fromMod2 = selected2.Contains(relativePath);
                AppendHashText(hash, $"file:{relativePath}:mod1={fromMod1}:mod2={fromMod2}\n");
                if (fromMod1)
                {
                    AppendFileHash(hash, "mod1", ResolveModFilePath(root1, relativePath, diagnostics));
                }

                if (fromMod2)
                {
                    AppendFileHash(hash, "mod2", ResolveModFilePath(root2, relativePath, diagnostics));
                }

                if (fromMod1 && fromMod2)
                {
                    var basePath = ResolveBaseFilePath(paths.BaseRomFsPath, relativePath, diagnostics);
                    AppendFileHash(hash, "base", basePath);
                }

                var outputPath = string.IsNullOrWhiteSpace(paths.OutputRootPath)
                    ? null
                    : ResolveOutputPath(paths.OutputRootPath, relativePath, diagnostics);
                AppendFileHash(hash, "output", outputPath);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Mod Merger source fingerprint could not be read: {exception.Message}",
                expected: "Readable selected source files"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Mod Merger source fingerprint could not be read: {exception.Message}",
                expected: "Readable selected source files"));
        }

        return null;
    }

    private static void AppendFileHash(IncrementalHash hash, string label, string? filePath)
    {
        AppendHashText(hash, $"{label}:");
        if (filePath is null || !File.Exists(filePath))
        {
            AppendHashText(hash, "missing\n");
            return;
        }

        var fileInfo = new FileInfo(filePath);
        AppendHashText(hash, $"length={fileInfo.Length}\n");
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }

        AppendHashText(hash, "\n");
    }

    private static void AppendHashText(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
    }

    private static string NormalizeReviewPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "<missing>";
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
            .Replace('\\', '/');
        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    private static SwShModMergerPreview CreatePreview(
        string mergeMode,
        string reviewToken,
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
            reviewToken,
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

    private static string CreateSingleSourceCopySummary(string source)
    {
        return $"Only {FormatResolutionSource(source)} contains this file, so KM will copy that file to the output.";
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

    private static void AddFpsPatchOverlapDiagnostics(
        IReadOnlyList<string> selectedFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var fpsPatchFileCount = selectedFiles.Count(SwShFpsPatchService.IsManagedRomFsPath);
        if (fpsPatchFileCount == 0)
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Warning,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {fpsPatchFileCount:N0} ROMFS file(s) also managed by 60FPS Patch. Applying this merge can replace that 60FPS timing or animation overlay; reinstall 60FPS Patch after merging if the patched timing should win."),
            file: FpsPatchRelativePathForDiagnostics(),
            expected: "Reviewed overlap with 60FPS Patch ROMFS output"));
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
        if (SwShFpsPatchService.IsManagedRomFsPath(relativePath))
        {
            return "60FPS Patch ROMFS diff";
        }

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

    private static string FpsPatchRelativePathForDiagnostics()
    {
        return "romfs";
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

    private static IReadOnlyList<string> WriteOutputsTransactionally(
        string outputRoot,
        IReadOnlyList<MergeOutput> outputs,
        ICollection<ValidationDiagnostic> diagnostics,
        Func<bool>? canCommit = null,
        Action<int, string>? beforeCommitOutput = null)
    {
        var preparedOutputs = new List<PreparedMergeOutput>();
        var createdDirectories = new List<string>();
        var writtenFiles = new List<string>();
        MergeOutput? activeOutput = null;

        try
        {
            foreach (var output in outputs)
            {
                activeOutput = output;
                var targetPath = ResolveOutputPath(outputRoot, output.RelativePath, diagnostics);
                if (targetPath is null)
                {
                    return Array.Empty<string>();
                }

                preparedOutputs.Add(PrepareOutput(
                    outputRoot,
                    targetPath,
                    output,
                    createdDirectories,
                    diagnostics));
            }

            if (canCommit is not null && !canCommit())
            {
                throw new InvalidDataException(
                    "Selected Mod Merger sources or output targets changed after review.");
            }

            for (var index = 0; index < preparedOutputs.Count; index++)
            {
                var preparedOutput = preparedOutputs[index];
                activeOutput = preparedOutput.Output;
                beforeCommitOutput?.Invoke(index, preparedOutput.TargetPath);
                VerifyTargetMatchesPreimage(preparedOutput);
                File.Move(
                    preparedOutput.TempPath,
                    preparedOutput.TargetPath,
                    overwrite: preparedOutput.TargetPreimage.Exists);
                preparedOutput.IsCommitted = true;
                VerifyFileContents(preparedOutput.TargetPath, preparedOutput.Output);
                writtenFiles.Add(preparedOutput.Output.RelativePath);
            }

            return writtenFiles;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var rollbackFailures = RollBackPreparedOutputs(preparedOutputs, diagnostics);
            writtenFiles.Clear();
            writtenFiles.AddRange(rollbackFailures);
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                rollbackFailures.Count == 0
                    ? $"Merged output transaction failed and all output changes were rolled back: {exception.Message}"
                    : $"Merged output transaction failed and rollback was incomplete for {rollbackFailures.Count} output file(s): {exception.Message}",
                file: activeOutput?.RelativePath,
                expected: "All selected outputs written and verified together"));
            return writtenFiles;
        }
        finally
        {
            foreach (var preparedOutput in preparedOutputs)
            {
                TryDeleteTransactionFile(
                    preparedOutput.TempPath,
                    "temporary output",
                    preparedOutput.Output.RelativePath,
                    diagnostics);
                if (!preparedOutput.RollbackFailed)
                {
                    TryDeleteTransactionFile(
                        preparedOutput.BackupPath,
                        "rollback backup",
                        preparedOutput.Output.RelativePath,
                        diagnostics);
                }
            }

            TryDeleteCreatedDirectories(outputRoot, createdDirectories, diagnostics);

            foreach (var output in outputs)
            {
                ClearOutputContents(output);
            }
        }
    }

    private static PreparedMergeOutput PrepareOutput(
        string outputRoot,
        string targetPath,
        MergeOutput output,
        ICollection<string> createdDirectories,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        ValidateOutputContents(output);
        if (Directory.Exists(targetPath))
        {
            throw new IOException($"Output target '{output.RelativePath}' is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Output target directory could not be resolved.");
        CreateOutputDirectory(outputRoot, directory, createdDirectories);

        var nonce = Guid.NewGuid().ToString("N");
        var fileName = Path.GetFileName(targetPath);
        var tempPath = Path.Combine(directory, $".{fileName}.{nonce}.tmp");
        var backupPath = Path.Combine(directory, $".{fileName}.{nonce}.bak");
        var targetExisted = File.Exists(targetPath);

        try
        {
            File.WriteAllBytes(tempPath, output.Contents);
            VerifyFileContents(tempPath, output);
            OutputTargetPreimage targetPreimage;
            if (targetExisted)
            {
                File.Copy(targetPath, backupPath, overwrite: false);
                targetPreimage = CaptureExistingTargetPreimage(backupPath);
            }
            else
            {
                targetPreimage = OutputTargetPreimage.Missing;
            }

            return new PreparedMergeOutput(
                output,
                targetPath,
                tempPath,
                backupPath,
                targetPreimage);
        }
        catch
        {
            TryDeleteTransactionFile(
                tempPath,
                "temporary output",
                output.RelativePath,
                diagnostics);
            TryDeleteTransactionFile(
                backupPath,
                "rollback backup",
                output.RelativePath,
                diagnostics);
            throw;
        }
    }

    private static IReadOnlyList<string> RollBackPreparedOutputs(
        IReadOnlyList<PreparedMergeOutput> preparedOutputs,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var failures = new List<string>();
        foreach (var preparedOutput in preparedOutputs.Reverse())
        {
            if (!preparedOutput.IsCommitted)
            {
                continue;
            }

            try
            {
                if (preparedOutput.TargetPreimage.Exists)
                {
                    EnsureTargetStillContainsCommittedOutput(preparedOutput);
                    File.Copy(preparedOutput.BackupPath, preparedOutput.TargetPath, overwrite: true);
                    if (!FileMatchesFingerprint(
                        preparedOutput.TargetPath,
                        preparedOutput.TargetPreimage.Length,
                        preparedOutput.TargetPreimage.Sha256))
                    {
                        throw new IOException("The restored output does not match its rollback backup.");
                    }
                }
                else if (Directory.Exists(preparedOutput.TargetPath))
                {
                    throw new IOException("Rollback target is now a directory and was left untouched.");
                }
                else if (File.Exists(preparedOutput.TargetPath))
                {
                    EnsureTargetStillContainsCommittedOutput(preparedOutput);
                    File.Delete(preparedOutput.TargetPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                preparedOutput.RollbackFailed = true;
                failures.Add(preparedOutput.Output.RelativePath);
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Merged output rollback could not restore the original file without overwriting a concurrent change: {exception.Message}",
                    file: preparedOutput.Output.RelativePath,
                    expected: "Original output restored after a failed merge"));
            }
        }

        return failures;
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

    private static OutputTargetPreimage CaptureExistingTargetPreimage(string path)
    {
        var fileInfo = new FileInfo(path);
        return new OutputTargetPreimage(
            Exists: true,
            fileInfo.Length,
            ComputeFileSha256(path));
    }

    private static void VerifyTargetMatchesPreimage(PreparedMergeOutput preparedOutput)
    {
        var preimage = preparedOutput.TargetPreimage;
        if (!preimage.Exists)
        {
            if (File.Exists(preparedOutput.TargetPath) || Directory.Exists(preparedOutput.TargetPath))
            {
                throw new InvalidDataException(
                    $"Output target '{preparedOutput.Output.RelativePath}' was created after review.");
            }

            return;
        }

        if (!FileMatchesFingerprint(
            preparedOutput.TargetPath,
            preimage.Length,
            preimage.Sha256))
        {
            throw new InvalidDataException(
                $"Output target '{preparedOutput.Output.RelativePath}' changed after review.");
        }
    }

    private static void EnsureTargetStillContainsCommittedOutput(PreparedMergeOutput preparedOutput)
    {
        if (!FileMatchesFingerprint(
            preparedOutput.TargetPath,
            preparedOutput.Output.ExpectedLength,
            preparedOutput.Output.Sha256))
        {
            throw new IOException("Rollback target changed after commit and was left untouched.");
        }
    }

    private static bool FileMatchesFingerprint(string path, long expectedLength, string expectedSha256)
    {
        if (Directory.Exists(path) || !File.Exists(path))
        {
            return false;
        }

        var fileInfo = new FileInfo(path);
        return fileInfo.Length == expectedLength
            && string.Equals(
                ComputeFileSha256(path),
                expectedSha256,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateOutputDirectory(
        string outputRoot,
        string directory,
        ICollection<string> createdDirectories)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        var fullDirectory = Path.GetFullPath(directory);
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(fullRoot, fullDirectory)))
        {
            throw new IOException("Output target directory escapes Output Root.");
        }

        var current = fullDirectory;
        while (!PathsEqual(current, fullRoot) && !Directory.Exists(current))
        {
            createdDirectories.Add(current);
            current = Path.GetDirectoryName(current)
                ?? throw new IOException("Output target directory ancestry could not be resolved.");
        }

        Directory.CreateDirectory(fullDirectory);
    }

    private static void TryDeleteCreatedDirectories(
        string outputRoot,
        IEnumerable<string> createdDirectories,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var directory in createdDirectories
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.Exists(directory)
                    || Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    continue;
                }

                Directory.Delete(directory, recursive: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Mod Merger could not remove an empty transaction-created output directory: {exception.Message}",
                    file: Path.GetRelativePath(outputRoot, directory).Replace(Path.DirectorySeparatorChar, '/'),
                    expected: "No empty transaction-created output directories left behind"));
            }
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void TryDeleteTransactionFile(
        string path,
        string artifact,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Mod Merger could not remove its {artifact}: {exception.Message}",
                file: relativePath,
                expected: "Transaction artifacts removed after apply"));
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
        if (PathContainment.IsOutsideRoot(relativeToOutputRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Merged output path escapes Output Root.",
                file: relativePath,
                expected: "Output path inside Output Root"));
            return null;
        }

        if (TraversesReparsePointBelowRoot(outputRoot, targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Merged output path traverses a symbolic link or junction below Output Root.",
                file: relativePath,
                expected: "Physical output path inside Output Root"));
            return null;
        }

        return targetPath;
    }

    private static bool TraversesReparsePointBelowRoot(string fullRoot, string fullPath)
    {
        var relativePath = Path.GetRelativePath(fullRoot, fullPath);
        if (PathContainment.IsOutsideRoot(relativePath))
        {
            return true;
        }

        var currentPath = fullRoot;
        foreach (var segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            try
            {
                var attributes = File.GetAttributes(currentPath);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    FileSystemInfo entry = attributes.HasFlag(FileAttributes.Directory)
                        ? new DirectoryInfo(currentPath)
                        : new FileInfo(currentPath);
                    if (entry.LinkTarget is not null)
                    {
                        return true;
                    }
                }
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
        }

        return false;
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

    private sealed class PreparedMergeOutput
    {
        public PreparedMergeOutput(
            MergeOutput output,
            string targetPath,
            string tempPath,
            string backupPath,
            OutputTargetPreimage targetPreimage)
        {
            Output = output;
            TargetPath = targetPath;
            TempPath = tempPath;
            BackupPath = backupPath;
            TargetPreimage = targetPreimage;
        }

        public MergeOutput Output { get; }

        public string TargetPath { get; }

        public string TempPath { get; }

        public string BackupPath { get; }

        public OutputTargetPreimage TargetPreimage { get; }

        public bool IsCommitted { get; set; }

        public bool RollbackFailed { get; set; }
    }

    private sealed record OutputTargetPreimage(bool Exists, long Length, string Sha256)
    {
        public static OutputTargetPreimage Missing { get; } = new(false, 0, string.Empty);
    }
}
