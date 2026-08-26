// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.Projects;

public sealed record ReadProjectSourceRevisionRequest(
    string ProjectId,
    ProjectPathsDto Paths);

public sealed record ReadProjectSourceRevisionResponse(
    string ProjectId,
    ProjectGameDto Game,
    string Fingerprint,
    string SourceObservationToken);
