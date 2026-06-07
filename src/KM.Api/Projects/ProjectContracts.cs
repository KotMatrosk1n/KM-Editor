// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;

namespace KM.Api.Projects;

public sealed record ProjectPathsDto(
    string? BaseRomFsPath,
    string? BaseExeFsPath,
    string? OutputRootPath);

public sealed record OpenProjectRequest(ProjectPathsDto Paths);

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

public sealed record ProjectHealthDto(
    ProjectHealthStateDto State,
    bool CanOpenReadOnlyWorkflows,
    bool CanOpenEditableWorkflows,
    IReadOnlyList<ProjectPathValidationDto> Paths,
    ProjectFileGraphSummaryDto FileGraph,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record OpenProjectResponse(
    string ProjectId,
    ProjectHealthDto Health);
