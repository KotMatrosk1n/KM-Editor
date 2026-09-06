// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Projects;
namespace KM.Api.Models;
public sealed record ModelCatalogRequest(ProjectPathsDto Paths);
public sealed record ModelPrepareRequest(ProjectPathsDto Paths, string Id, string TransferId, string? Animation = null);
