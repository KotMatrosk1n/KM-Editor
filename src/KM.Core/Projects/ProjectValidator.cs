// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Files;

namespace KM.Core.Projects;

public sealed class ProjectValidator
{
    private const int NpdmTitleIdOffset = 0x290;
    private const int NpdmMinimumTitleIdLength = sizeof(ulong);

    private readonly ProjectFileGraphBuilder fileGraphBuilder;

    public ProjectValidator(ProjectFileGraphBuilder? fileGraphBuilder = null)
    {
        this.fileGraphBuilder = fileGraphBuilder ?? new ProjectFileGraphBuilder();
    }

    public ProjectHealth Validate(ProjectPaths paths)
    {
        return Validate(paths, graphPaths => fileGraphBuilder.Build(graphPaths).ToSummary());
    }

    internal ProjectHealth Validate(
        ProjectPaths paths,
        Func<ProjectPaths, ProjectFileGraphSummary> createFileGraphSummary)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(createFileGraphSummary);

        var baseRomFs = ValidateRequiredDirectory(ProjectPathRole.BaseRomFs, paths.BaseRomFsPath, "Base RomFS");
        var baseExeFs = ValidateRequiredDirectory(ProjectPathRole.BaseExeFs, paths.BaseExeFsPath, "Base ExeFS");
        var outputRoot = ValidateOptionalOutputRoot(paths.OutputRootPath);
        var saveFile = ValidateOptionalSaveFile(paths.SaveFilePath);
        var scarletVioletSupportFolder = ValidateOptionalScarletVioletSupportFolder(
            paths.ScarletVioletSupportFolderPath,
            paths.SelectedGame);
        var pokemonLegendsZASupportFolder = ValidateOptionalPokemonLegendsZASupportFolder(
            paths.PokemonLegendsZASupportFolderPath,
            paths.SelectedGame);

        AddBasePathSafetyDiagnostics(baseRomFs, baseExeFs);
        AddOutputRootSafetyDiagnostics(outputRoot, baseRomFs, baseExeFs);
        AddSelectedGameDiagnostics(baseRomFs, baseExeFs, outputRoot, paths.SelectedGame);

        var pathResults = new[]
        {
            baseRomFs.ToResult(),
            baseExeFs.ToResult(),
            outputRoot.ToResult(),
            saveFile.ToResult(),
            scarletVioletSupportFolder.ToResult(),
            pokemonLegendsZASupportFolder.ToResult(),
        };
        var diagnostics = pathResults.SelectMany(result => result.Diagnostics).ToArray();
        var state = ResolveHealthState(baseRomFs, baseExeFs, outputRoot);
        var graph = CreateFileGraphSummary(
            paths,
            baseRomFs,
            baseExeFs,
            outputRoot,
            createFileGraphSummary);

        return new ProjectHealth(state, pathResults, graph, diagnostics);
    }

    private ProjectFileGraphSummary CreateFileGraphSummary(
        ProjectPaths paths,
        PathValidationDraft baseRomFs,
        PathValidationDraft baseExeFs,
        PathValidationDraft outputRoot,
        Func<ProjectPaths, ProjectFileGraphSummary> createFileGraphSummary)
    {
        if (!baseRomFs.IsValid || !baseExeFs.IsValid)
        {
            return new ProjectFileGraphSummary(0, 0, 0, 0);
        }

        // Missing or unsafe output roots should not contribute LayeredFS entries to project health.
        var graphPaths = outputRoot.IsValid
            ? paths
            : paths with { OutputRootPath = null };

        return createFileGraphSummary(graphPaths);
    }

    private static ProjectHealthState ResolveHealthState(
        PathValidationDraft baseRomFs,
        PathValidationDraft baseExeFs,
        PathValidationDraft outputRoot)
    {
        if (IsMissingRequiredBasePath(baseRomFs) || IsMissingRequiredBasePath(baseExeFs))
        {
            return ProjectHealthState.NeedsPaths;
        }

        if (baseRomFs.HasBlockingError
            || baseExeFs.HasBlockingError
            || outputRoot.HasBlockingError)
        {
            return ProjectHealthState.Blocked;
        }

        return outputRoot.IsValid
            ? ProjectHealthState.EditableReady
            : ProjectHealthState.ReadOnlyReady;
    }

    private static bool IsMissingRequiredBasePath(PathValidationDraft path)
    {
        return path.Status is ProjectPathStatus.NotSet
            or ProjectPathStatus.Missing
            or ProjectPathStatus.WrongKind;
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

    private static PathValidationDraft ValidateOptionalSaveFile(string? path)
    {
        var draft = new PathValidationDraft(ProjectPathRole.SaveFile, path, isRequired: false);

        if (string.IsNullOrWhiteSpace(path))
        {
            draft.Status = ProjectPathStatus.NotSet;
            return draft;
        }

        if (Directory.Exists(path))
        {
            draft.Status = ProjectPathStatus.WrongKind;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "Save file path must be a file.",
                expected: "Readable save file");
            return draft;
        }

        if (!File.Exists(path))
        {
            draft.Status = ProjectPathStatus.Missing;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "Save file does not exist; save-file inspection is disabled until it is created or changed.",
                expected: "Readable save file");
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

    private static PathValidationDraft ValidateOptionalScarletVioletSupportFolder(
        string? path,
        ProjectGame? selectedGame)
    {
        var draft = new PathValidationDraft(ProjectPathRole.ScarletVioletSupportFolder, path, isRequired: false);

        if (selectedGame is not ProjectGame.Scarlet and not ProjectGame.Violet)
        {
            draft.Status = string.IsNullOrWhiteSpace(path)
                ? ProjectPathStatus.NotSet
                : ProjectPathStatus.Valid;
            return draft;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            draft.Status = ProjectPathStatus.NotSet;
            return draft;
        }

        if (File.Exists(path))
        {
            draft.Status = ProjectPathStatus.WrongKind;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "S/V support path must be a folder.",
                expected: "Folder containing oo2core_8_win64.dll");
            return draft;
        }

        if (!Directory.Exists(path))
        {
            draft.Status = ProjectPathStatus.Missing;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "oo2core_8_win64.dll folder does not exist; S/V data editors are disabled until it is configured.",
                expected: "Existing oo2core_8_win64.dll folder");
            return draft;
        }

        var requiredFilePath = Path.Combine(path, CreateScarletVioletSupportFileName());
        if (!File.Exists(requiredFilePath))
        {
            draft.Status = ProjectPathStatus.Missing;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "oo2core_8_win64.dll was not found in the selected folder; S/V data editors are disabled until it is configured.",
                expected: "Folder containing oo2core_8_win64.dll");
            return draft;
        }

        draft.Status = ProjectPathStatus.Valid;
        return draft;
    }

    private static PathValidationDraft ValidateOptionalPokemonLegendsZASupportFolder(
        string? path,
        ProjectGame? selectedGame)
    {
        var draft = new PathValidationDraft(ProjectPathRole.PokemonLegendsZASupportFolder, path, isRequired: false);

        if (selectedGame is not ProjectGame.ZA)
        {
            draft.Status = string.IsNullOrWhiteSpace(path)
                ? ProjectPathStatus.NotSet
                : ProjectPathStatus.Valid;
            return draft;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            draft.Status = ProjectPathStatus.NotSet;
            return draft;
        }

        if (File.Exists(path))
        {
            draft.Status = ProjectPathStatus.WrongKind;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "Pokemon Legends Z-A support path must be a folder.",
                expected: "Folder containing oo2core_8_win64.dll");
            return draft;
        }

        if (!Directory.Exists(path))
        {
            draft.Status = ProjectPathStatus.Missing;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "oo2core_8_win64.dll folder does not exist; Z-A data editors are disabled until it is configured.",
                expected: "Existing oo2core_8_win64.dll folder");
            return draft;
        }

        var requiredFilePath = Path.Combine(path, CreateScarletVioletSupportFileName());
        if (!File.Exists(requiredFilePath))
        {
            draft.Status = ProjectPathStatus.Missing;
            draft.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "oo2core_8_win64.dll was not found in the selected folder; Z-A data editors are disabled until it is configured.",
                expected: "Folder containing oo2core_8_win64.dll");
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

    private static void AddSelectedGameDiagnostics(
        PathValidationDraft baseRomFs,
        PathValidationDraft baseExeFs,
        PathValidationDraft outputRoot,
        ProjectGame? selectedGame)
    {
        if (selectedGame is null)
        {
            return;
        }

        AddBaseRomFsGameDiagnostic(baseRomFs, selectedGame.Value);
        AddBaseExeFsGameDiagnostic(baseExeFs, selectedGame.Value);
        AddOutputRootGameDiagnostic(outputRoot, selectedGame.Value);
    }

    private static void AddBaseRomFsGameDiagnostic(PathValidationDraft baseRomFs, ProjectGame selectedGame)
    {
        if (!baseRomFs.IsValid)
        {
            return;
        }

        var gameInfo = ProjectGameMetadata.Get(selectedGame);
        if (!gameInfo.UsesTrinityRomFs)
        {
            return;
        }

        var trpfdPath = Path.Combine(baseRomFs.Path!, "arc", "data.trpfd");
        var trpfsPath = Path.Combine(baseRomFs.Path!, "arc", "data.trpfs");
        if (!File.Exists(trpfdPath) || !File.Exists(trpfsPath))
        {
            baseRomFs.Status = ProjectPathStatus.Unsafe;
            baseRomFs.AddDiagnostic(
                DiagnosticSeverity.Error,
                $"Base RomFS does not contain the Trinity archive required for {gameInfo.DisplayName}.",
                expected: "arc/data.trpfd and arc/data.trpfs");
            return;
        }

        baseRomFs.AddDiagnostic(
            DiagnosticSeverity.Info,
            $"Base RomFS contains the Trinity archive required for {gameInfo.DisplayName}.",
            expected: "arc/data.trpfd and arc/data.trpfs");
    }

    private static void AddBaseExeFsGameDiagnostic(PathValidationDraft baseExeFs, ProjectGame selectedGame)
    {
        if (!baseExeFs.IsValid)
        {
            return;
        }

        var npdmPath = Path.Combine(baseExeFs.Path!, "main.npdm");
        if (!File.Exists(npdmPath))
        {
            baseExeFs.Status = ProjectPathStatus.Unsafe;
            baseExeFs.AddDiagnostic(
                DiagnosticSeverity.Error,
                $"Base ExeFS game could not be verified for {FormatGame(selectedGame)} because main.npdm is missing.",
                expected: $"main.npdm with {FormatGame(selectedGame)} title id");
            return;
        }

        try
        {
            var npdm = File.ReadAllBytes(npdmPath);
            if (npdm.Length < NpdmMinimumTitleIdLength)
            {
                baseExeFs.Status = ProjectPathStatus.Unsafe;
                baseExeFs.AddDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Base ExeFS game could not be verified for {FormatGame(selectedGame)} because main.npdm is too small.",
                    expected: $"main.npdm with {FormatGame(selectedGame)} title id");
                return;
            }

            var (detectedGame, titleId) = DetectGameFromNpdm(npdm);
            if (detectedGame is null)
            {
                var titleIdLabel = titleId is null ? "not found" : $"0x{titleId.Value:X16}";
                baseExeFs.Status = ProjectPathStatus.Unsafe;
                baseExeFs.AddDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Base ExeFS title id {titleIdLabel} is not recognized as {ProjectGameMetadata.FormatRecognizedGameList()}.",
                    expected: $"0x{GetTitleId(selectedGame):X16} for {FormatGame(selectedGame)}");
                return;
            }

            if (detectedGame.Value != selectedGame)
            {
                baseExeFs.Status = ProjectPathStatus.Unsafe;
                baseExeFs.AddDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Selected {FormatGame(selectedGame)}, but Base ExeFS contains {FormatGame(detectedGame.Value)} title id 0x{titleId!.Value:X16}.",
                    expected: $"0x{GetTitleId(selectedGame):X16} for {FormatGame(selectedGame)}");
                return;
            }

            baseExeFs.AddDiagnostic(
                DiagnosticSeverity.Info,
                $"Base ExeFS matches selected {FormatGame(selectedGame)} title id 0x{titleId!.Value:X16}.",
                expected: $"0x{GetTitleId(selectedGame):X16} for {FormatGame(selectedGame)}");
        }
        catch (IOException exception)
        {
            baseExeFs.Status = ProjectPathStatus.Unsafe;
            baseExeFs.AddDiagnostic(
                DiagnosticSeverity.Error,
                $"Base ExeFS game could not be verified from main.npdm: {exception.Message}",
                expected: $"Readable main.npdm with {FormatGame(selectedGame)} title id");
        }
        catch (UnauthorizedAccessException exception)
        {
            baseExeFs.Status = ProjectPathStatus.Unsafe;
            baseExeFs.AddDiagnostic(
                DiagnosticSeverity.Error,
                $"Base ExeFS game could not be verified from main.npdm: {exception.Message}",
                expected: $"Readable main.npdm with {FormatGame(selectedGame)} title id");
        }
    }

    private static void AddOutputRootGameDiagnostic(PathValidationDraft outputRoot, ProjectGame selectedGame)
    {
        if (!outputRoot.IsValid || string.IsNullOrWhiteSpace(outputRoot.Path))
        {
            return;
        }

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(outputRoot.Path));
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var selectedTitleId = GetTitleId(selectedGame).ToString("X16");
        if (string.Equals(folderName, selectedTitleId, StringComparison.OrdinalIgnoreCase))
        {
            outputRoot.AddDiagnostic(
                DiagnosticSeverity.Info,
                $"Output root folder matches selected {FormatGame(selectedGame)} title id 0x{selectedTitleId}.",
                expected: $"LayeredFS output folder named {selectedTitleId}");
            return;
        }

        var otherGame = ProjectGameMetadata.All.FirstOrDefault(info =>
            info.Game != selectedGame
            && string.Equals(folderName, info.TitleId.ToString("X16"), StringComparison.OrdinalIgnoreCase));
        if (otherGame is not null)
        {
            outputRoot.Status = ProjectPathStatus.Unsafe;
            outputRoot.AddDiagnostic(
                DiagnosticSeverity.Error,
                $"Selected {FormatGame(selectedGame)}, but Output Root folder is the {otherGame.DisplayName} title id 0x{otherGame.TitleId:X16}.",
                expected: $"LayeredFS output folder named {selectedTitleId}");
        }
    }

    private static ProjectGame? DetectGame(ulong titleId)
    {
        return ProjectGameMetadata.DetectByTitleId(titleId);
    }

    private static (ProjectGame? Game, ulong? TitleId) DetectGameFromNpdm(byte[] npdm)
    {
        ulong? firstTitleId = null;
        if (npdm.Length >= NpdmTitleIdOffset + sizeof(ulong))
        {
            firstTitleId = BinaryPrimitives.ReadUInt64LittleEndian(
                npdm.AsSpan(NpdmTitleIdOffset, sizeof(ulong)));
            var legacyGame = DetectGame(firstTitleId.Value);
            if (legacyGame is not null)
            {
                return (legacyGame, firstTitleId.Value);
            }
        }

        for (var offset = 0; offset <= npdm.Length - sizeof(ulong); offset += 4)
        {
            var titleId = BinaryPrimitives.ReadUInt64LittleEndian(npdm.AsSpan(offset, sizeof(ulong)));
            var detectedGame = DetectGame(titleId);
            if (detectedGame is not null)
            {
                return (detectedGame, titleId);
            }
        }

        return (null, firstTitleId);
    }

    private static ulong GetTitleId(ProjectGame game)
    {
        return ProjectGameMetadata.Get(game).TitleId;
    }

    private static string FormatGame(ProjectGame game)
    {
        return ProjectGameMetadata.Get(game).DisplayName;
    }

    private static string CreateScarletVioletSupportFileName()
    {
        return string.Concat("oo2", "core", "_8_", "win", "64", ".dll");
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
