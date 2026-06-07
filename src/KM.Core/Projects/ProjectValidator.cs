// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;

namespace KM.Core.Projects;

public sealed class ProjectValidator
{
    private readonly ProjectFileGraphBuilder fileGraphBuilder;

    public ProjectValidator(ProjectFileGraphBuilder? fileGraphBuilder = null)
    {
        this.fileGraphBuilder = fileGraphBuilder ?? new ProjectFileGraphBuilder();
    }

    public ProjectHealth Validate(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var baseRomFs = ValidateRequiredDirectory(ProjectPathRole.BaseRomFs, paths.BaseRomFsPath, "Base RomFS");
        var baseExeFs = ValidateRequiredDirectory(ProjectPathRole.BaseExeFs, paths.BaseExeFsPath, "Base ExeFS");
        var outputRoot = ValidateOptionalOutputRoot(paths.OutputRootPath);

        AddBasePathSafetyDiagnostics(baseRomFs, baseExeFs);
        AddOutputRootSafetyDiagnostics(outputRoot, baseRomFs, baseExeFs);

        var pathResults = new[]
        {
            baseRomFs.ToResult(),
            baseExeFs.ToResult(),
            outputRoot.ToResult(),
        };
        var diagnostics = pathResults.SelectMany(result => result.Diagnostics).ToArray();
        var state = ResolveHealthState(baseRomFs, baseExeFs, outputRoot);
        var graph = CreateFileGraphSummary(paths, baseRomFs, baseExeFs, outputRoot);

        return new ProjectHealth(state, pathResults, graph, diagnostics);
    }

    private ProjectFileGraphSummary CreateFileGraphSummary(
        ProjectPaths paths,
        PathValidationDraft baseRomFs,
        PathValidationDraft baseExeFs,
        PathValidationDraft outputRoot)
    {
        if (!baseRomFs.IsValid || !baseExeFs.IsValid)
        {
            return new ProjectFileGraphSummary(0, 0, 0, 0);
        }

        // Missing or unsafe output roots should not contribute LayeredFS entries to project health.
        var graphPaths = outputRoot.IsValid
            ? paths
            : paths with { OutputRootPath = null };

        return fileGraphBuilder.Build(graphPaths).ToSummary();
    }

    private static ProjectHealthState ResolveHealthState(
        PathValidationDraft baseRomFs,
        PathValidationDraft baseExeFs,
        PathValidationDraft outputRoot)
    {
        if (!baseRomFs.IsValid || !baseExeFs.IsValid)
        {
            return ProjectHealthState.NeedsPaths;
        }

        if (baseRomFs.HasBlockingError || baseExeFs.HasBlockingError || outputRoot.HasBlockingError)
        {
            return ProjectHealthState.Blocked;
        }

        return outputRoot.IsValid
            ? ProjectHealthState.EditableReady
            : ProjectHealthState.ReadOnlyReady;
    }

    private static PathValidationDraft ValidateRequiredDirectory(
        ProjectPathRole role,
        string? path,
        string label)
    {
        var draft = new PathValidationDraft(role, path, isRequired: true);

        if (string.IsNullOrWhiteSpace(path))
        {
            draft.Status = ProjectPathStatus.NotSet;
            draft.AddDiagnostic(
                DiagnosticSeverity.Error,
                $"{label} path is required.",
                expected: "Existing directory");
            return draft;
        }

        if (File.Exists(path))
        {
            draft.Status = ProjectPathStatus.WrongKind;
            draft.AddDiagnostic(
                DiagnosticSeverity.Error,
                $"{label} path must be a directory.",
                expected: "Directory");
            return draft;
        }

        if (!Directory.Exists(path))
        {
            draft.Status = ProjectPathStatus.Missing;
            draft.AddDiagnostic(
                DiagnosticSeverity.Error,
                $"{label} path does not exist.",
                expected: "Existing directory");
            return draft;
        }

        draft.Status = ProjectPathStatus.Valid;
        return draft;
    }

    private static PathValidationDraft ValidateOptionalOutputRoot(string? path)
    {
        var draft = new PathValidationDraft(ProjectPathRole.OutputRoot, path, isRequired: false);

        if (string.IsNullOrWhiteSpace(path))
        {
            draft.Status = ProjectPathStatus.NotSet;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "Output root is not configured; write actions are disabled.",
                expected: "Existing directory before applying output");
            return draft;
        }

        if (File.Exists(path))
        {
            draft.Status = ProjectPathStatus.WrongKind;
            draft.AddDiagnostic(
                DiagnosticSeverity.Error,
                "Output root must be a directory.",
                expected: "Directory");
            return draft;
        }

        if (!Directory.Exists(path))
        {
            draft.Status = ProjectPathStatus.Missing;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "Output root does not exist; write actions are disabled until it is created or changed.",
                expected: "Existing directory before applying output");
            return draft;
        }

        draft.Status = ProjectPathStatus.Valid;
        return draft;
    }

    private static void AddBasePathSafetyDiagnostics(PathValidationDraft baseRomFs, PathValidationDraft baseExeFs)
    {
        if (!baseRomFs.IsValid || !baseExeFs.IsValid)
        {
            return;
        }

        if (!PathsOverlap(baseRomFs.Path, baseExeFs.Path))
        {
            return;
        }

        baseRomFs.Status = ProjectPathStatus.Unsafe;
        baseExeFs.Status = ProjectPathStatus.Unsafe;
        baseRomFs.AddDiagnostic(
            DiagnosticSeverity.Error,
            "Base RomFS and Base ExeFS must be different directories.",
            expected: "Separate source directories");
        baseExeFs.AddDiagnostic(
            DiagnosticSeverity.Error,
            "Base RomFS and Base ExeFS must be different directories.",
            expected: "Separate source directories");
    }

    private static void AddOutputRootSafetyDiagnostics(
        PathValidationDraft outputRoot,
        PathValidationDraft baseRomFs,
        PathValidationDraft baseExeFs)
    {
        if (!outputRoot.IsValid)
        {
            return;
        }

        var overlapsBaseRomFs = baseRomFs.IsValid && PathsOverlap(outputRoot.Path, baseRomFs.Path);
        var overlapsBaseExeFs = baseExeFs.IsValid && PathsOverlap(outputRoot.Path, baseExeFs.Path);

        if (!overlapsBaseRomFs && !overlapsBaseExeFs)
        {
            return;
        }

        outputRoot.Status = ProjectPathStatus.Unsafe;
        outputRoot.AddDiagnostic(
            DiagnosticSeverity.Error,
            "Output root must not overlap base RomFS or base ExeFS.",
            expected: "Separate LayeredFS output directory");
    }

    private static bool PathsOverlap(string? firstPath, string? secondPath)
    {
        var firstFullPath = TryNormalizePath(firstPath);
        var secondFullPath = TryNormalizePath(secondPath);

        if (firstFullPath is null || secondFullPath is null)
        {
            return false;
        }

        // Containment in either direction breaks the source/output boundary, so treat it as overlap.
        return firstFullPath.Equals(secondFullPath, StringComparison.OrdinalIgnoreCase)
            || IsDescendant(firstFullPath, secondFullPath)
            || IsDescendant(secondFullPath, firstFullPath);
    }

    private static bool IsDescendant(string candidatePath, string parentPath)
    {
        return candidatePath.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryNormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed class PathValidationDraft
    {
        private readonly List<ValidationDiagnostic> diagnostics = [];

        public PathValidationDraft(ProjectPathRole role, string? path, bool isRequired)
        {
            Role = role;
            Path = path;
            IsRequired = isRequired;
        }

        public ProjectPathRole Role { get; }

        public string? Path { get; }

        public bool IsRequired { get; }

        public ProjectPathStatus Status { get; set; }

        public bool IsValid => Status == ProjectPathStatus.Valid;

        public bool HasBlockingError => diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        public void AddDiagnostic(DiagnosticSeverity severity, string message, string? expected = null)
        {
            diagnostics.Add(new ValidationDiagnostic(
                severity,
                message,
                File: Path,
                Domain: "project",
                Expected: expected));
        }

        public ProjectPathValidation ToResult()
        {
            return new ProjectPathValidation(Role, Path, Status, IsRequired, diagnostics.ToArray());
        }
    }
}
