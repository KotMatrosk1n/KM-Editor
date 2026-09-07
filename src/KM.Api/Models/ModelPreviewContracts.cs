// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Projects;
using KM.Api.Editing;
namespace KM.Api.Models;
public sealed record ModelCatalogRequest(ProjectPathsDto Paths);
public sealed record ModelPrepareRequest(ProjectPathsDto Paths, string Id, string TransferId, string? Animation = null, ModelTextureChangeDto[]? TextureChanges = null);
public sealed record ModelTextureColorDto(string From, string To, int Tolerance);
public sealed record ModelTextureChangeDto(string Texture, string SourceHash, ModelTextureColorDto[] Changes);
public sealed record ModelTexturesRequest(ProjectPathsDto Paths, string Id);
public sealed record ModelTextureStageRequest(ProjectPathsDto Paths, string Id, ModelTextureChangeDto Change, EditSessionDto? Session);
