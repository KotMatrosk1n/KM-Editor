// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;

namespace KM.Api.Projects;

public sealed record ProjectPathsDto(
    string BaseRomFsPath,
    string BaseExeFsPath,
    string? OutputRootPath);

public sealed record OpenProjectRequest(ProjectPathsDto Paths);

public sealed record ProjectHealthDto(
    bool CanOpenEditableWorkflows,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record OpenProjectResponse(
    string ProjectId,
    ProjectHealthDto Health);
