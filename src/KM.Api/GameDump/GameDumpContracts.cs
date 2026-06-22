// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;

namespace KM.Api.GameDump;

public enum GameDumpCategoryKindDto
{
    Table,
    Text,
    Raw,
}

public enum GameDumpFormatDto
{
    Tsv,
    Csv,
    Json,
    TsvAndJson,
    Txt,
    TxtAndJson,
    Raw,
    RawAndJson,
}

public sealed record LoadGameDumpWorkflowRequest(ProjectPathsDto Paths);

public sealed record RunGameDumpRequest(
    ProjectPathsDto Paths,
    string DestinationFolder,
    IReadOnlyList<GameDumpSelectionDto> Selections);

public sealed record GameDumpSelectionDto(
    string CategoryId,
    GameDumpFormatDto Format);

public sealed record GameDumpCategoryDto(
    string Id,
    string Label,
    string Description,
    GameDumpCategoryKindDto Kind,
    IReadOnlyList<GameDumpFormatDto> Formats,
    GameDumpFormatDto DefaultFormat,
    bool IsAvailable,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record GameDumpWorkflowDto(
    IReadOnlyList<GameDumpCategoryDto> Categories,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record GameDumpWrittenFileDto(
    string CategoryId,
    string RelativePath,
    long SizeBytes);

public sealed record GameDumpResultDto(
    string DestinationFolder,
    IReadOnlyList<GameDumpWrittenFileDto> WrittenFiles,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    bool Succeeded);

public sealed record LoadGameDumpWorkflowResponse(GameDumpWorkflowDto Workflow);

public sealed record RunGameDumpResponse(GameDumpResultDto Result);
