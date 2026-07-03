// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.ProfanityFilter;

public sealed record LoadProfanityFilterRequest(
    ProjectPathsDto Paths);

public sealed record ApplyProfanityFilterRequest(
    ProjectPathsDto Paths);

public sealed record RestoreProfanityFilterRequest(
    ProjectPathsDto Paths);

public sealed record ProfanityFilterStatusDto(
    string Status,
    string Message,
    string? BuildId,
    ProjectGameDto? DetectedGame,
    string PatchOffsetHex,
    string PatchShape,
    string SourceLayer,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadProfanityFilterResponse(
    ProfanityFilterStatusDto Status);

public sealed record ApplyProfanityFilterResponse(
    ProfanityFilterStatusDto Status,
    ApplyResultDto ApplyResult);

public sealed record RestoreProfanityFilterResponse(
    ProfanityFilterStatusDto Status,
    ApplyResultDto ApplyResult);
