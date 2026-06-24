// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;
using KM.Api.Diagnostics;

namespace KM.Api.Projects;

public enum ProjectGameDto
{
    Sword,
    Shield,
    Scarlet,
    Violet,
}

public sealed record ProjectPathsDto
{
    [JsonConstructor]
    public ProjectPathsDto(
        string? BaseRomFsPath,
        string? BaseExeFsPath,
        string? OutputRootPath,
        string? SaveFilePath,
        string? ScarletVioletSupportFolderPath,
        ProjectGameDto? SelectedGame,
        string? GameTextLanguage = null)
    {
        this.BaseRomFsPath = BaseRomFsPath;
        this.BaseExeFsPath = BaseExeFsPath;
        this.OutputRootPath = OutputRootPath;
        this.SaveFilePath = SaveFilePath;
        this.ScarletVioletSupportFolderPath = ScarletVioletSupportFolderPath;
        this.SelectedGame = SelectedGame;
        this.GameTextLanguage = GameTextLanguage;
    }

    public ProjectPathsDto(
        string? BaseRomFsPath,
        string? BaseExeFsPath,
        string? OutputRootPath,
        string? SaveFilePath,
        ProjectGameDto? SelectedGame)
        : this(BaseRomFsPath, BaseExeFsPath, OutputRootPath, SaveFilePath, ScarletVioletSupportFolderPath: null, SelectedGame)
    {
    }

    public ProjectPathsDto(
        string? BaseRomFsPath,
        string? BaseExeFsPath,
        string? OutputRootPath,
        string? SaveFilePath)
        : this(BaseRomFsPath, BaseExeFsPath, OutputRootPath, SaveFilePath, SelectedGame: null)
    {
    }

    public ProjectPathsDto(
        string? BaseRomFsPath,
        string? BaseExeFsPath,
        string? OutputRootPath)
        : this(BaseRomFsPath, BaseExeFsPath, OutputRootPath, SaveFilePath: null)
    {
    }

    public string? BaseRomFsPath { get; init; }

    public string? BaseExeFsPath { get; init; }

    public string? OutputRootPath { get; init; }

    public string? SaveFilePath { get; init; }

    public string? ScarletVioletSupportFolderPath { get; init; }

    public ProjectGameDto? SelectedGame { get; init; }

    public string? GameTextLanguage { get; init; }
}

public sealed record OpenProjectRequest(ProjectPathsDto Paths);

public sealed record ValidateProjectRequest(ProjectPathsDto Paths);

public sealed record RefreshFileGraphRequest(ProjectPathsDto Paths);

public enum ProjectHealthStateDto
{
    NeedsPaths,
    ReadOnlyReady,
    EditableReady,
    Blocked,
}

public enum ProjectPathRoleDto
{
    BaseRomFs,
    BaseExeFs,
    OutputRoot,
    SaveFile,
    ScarletVioletSupportFolder,
}

public enum ProjectPathStatusDto
{
    NotSet,
    Missing,
    WrongKind,
    Valid,
    Unsafe,
}

public sealed record ProjectPathValidationDto(
    ProjectPathRoleDto Role,
    string? Path,
    ProjectPathStatusDto Status,
    bool IsRequired,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ProjectFileGraphSummaryDto(
    int BaseFileCount,
    int LayeredFileCount,
    int OverrideCount,
    int LayeredOnlyCount);

public enum ProjectFileGraphEntryStateDto
{
    BaseOnly,
    LayeredOverride,
    LayeredOnly,
}

public enum ProjectFileLayerDto
{
    Base,
    Layered,
    Pending,
    Generated,
}

public sealed record ProjectFileReferenceDto(
    ProjectFileLayerDto Layer,
    string RelativePath);

public sealed record ProjectFileGraphEntryDto(
    string RelativePath,
    ProjectFileReferenceDto? BaseFile,
    ProjectFileReferenceDto? LayeredFile,
    ProjectFileGraphEntryStateDto State);

public sealed record ProjectFileGraphDto(
    IReadOnlyList<ProjectFileGraphEntryDto> Entries,
    ProjectFileGraphSummaryDto Summary);

public sealed record ProjectHealthDto(
    ProjectHealthStateDto State,
    bool CanOpenReadOnlyWorkflows,
    bool CanOpenEditableWorkflows,
    IReadOnlyList<ProjectPathValidationDto> Paths,
    ProjectFileGraphSummaryDto FileGraph,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record OpenProjectResponse(
    string ProjectId,
    ProjectHealthDto Health,
    ProjectFileGraphDto FileGraph);

public sealed record ValidateProjectResponse(ProjectHealthDto Health);

public sealed record RefreshFileGraphResponse(ProjectFileGraphDto FileGraph);
