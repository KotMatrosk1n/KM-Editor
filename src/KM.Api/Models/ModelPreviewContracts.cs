// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Projects;
using KM.Api.Editing;
namespace KM.Api.Models;
public sealed record ModelCatalogRequest(ProjectPathsDto Paths);
public sealed record ModelPrepareRequest(ProjectPathsDto Paths, string Id, string TransferId, string? Animation = null, ModelTextureChangeDto[]? TextureChanges = null, ModelAssetChangeDto[]? AssetChanges = null, int Resolution = 1);
public sealed record ModelTextureColorDto(string From, string To, int Tolerance);
public sealed record ModelTextureChangeDto(string Texture, string SourceHash, ModelTextureColorDto[] Changes);
public sealed record ModelTexturesRequest(ProjectPathsDto Paths, string Id);
public sealed record ModelTextureStageRequest(ProjectPathsDto Paths, string Id, ModelTextureChangeDto Change, EditSessionDto? Session);
public sealed record ModelMaterialChangeDto(string Key, double[] Values, string? Text = null);
public sealed record ModelAssetChangeDto(string Asset, string SourceHash, ModelMaterialChangeDto[] Changes, bool Restore = false);
public sealed record ModelAssetStageRequest(ProjectPathsDto Paths, string Id, ModelAssetChangeDto? Change, EditSessionDto? Session, bool RestoreVanilla = false);
