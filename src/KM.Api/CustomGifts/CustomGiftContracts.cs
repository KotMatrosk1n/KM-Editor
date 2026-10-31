// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Gifts;
using KM.Api.Projects;
using System.Text.Json.Serialization;

namespace KM.Api.CustomGifts;

public sealed record CustomGiftPokemonDto(
    [property: JsonRequired] int SpeciesId, [property: JsonRequired] int Form,
    [property: JsonRequired] int Level, [property: JsonRequired] bool IsEgg,
    [property: JsonRequired] int HeldItemId, [property: JsonRequired] int BallItemId,
    [property: JsonRequired] int Ability, [property: JsonRequired] int Nature,
    [property: JsonRequired] int Gender, [property: JsonRequired] int ShinyLock,
    [property: JsonRequired] int DynamaxLevel, [property: JsonRequired] bool CanGigantamax,
    [property: JsonRequired] int SpecialMoveId, [property: JsonRequired] GiftPokemonIvsDto Ivs,
    [property: JsonRequired] int FlawlessIvCount, [property: JsonRequired] int GenderRatio,
    [property: JsonRequired] int AbilityMask);
public sealed record CodeGiftDto([property: JsonRequired] int ClaimId, [property: JsonRequired] string Name,
    [property: JsonRequired] string Code, [property: JsonRequired] bool OncePerSave, [property: JsonRequired] CustomGiftPokemonDto Pokemon);
public sealed record RoyalGiftPoolDto([property: JsonRequired] int Badges, [property: JsonRequired] IReadOnlyList<CustomGiftPokemonDto> Pokemon,
    [property: JsonRequired] IReadOnlyList<decimal> Chances);
public sealed record CustomGiftCatalogDto([property: JsonRequired] int Version, [property: JsonRequired] string Mode,
    [property: JsonRequired] int NextClaimId, [property: JsonRequired] IReadOnlyList<CodeGiftDto> CodeGifts,
    [property: JsonRequired] IReadOnlyList<RoyalGiftPoolDto> RoyalPools);
public sealed record LoadCustomGiftsRequest(ProjectPathsDto Paths);
public sealed record StageCustomGiftsRequest(ProjectPathsDto Paths, [property: JsonRequired] CustomGiftCatalogDto Catalog, EditSessionDto? Session);
public sealed record CustomGiftWorkflowDto(bool CanEdit, string InstalledMode, bool NeedsRepair, CustomGiftCatalogDto Catalog,
    IReadOnlyList<GiftPokemonEditableFieldDto> EditableFields, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadCustomGiftsResponse(CustomGiftWorkflowDto Workflow);
public sealed record StageCustomGiftsResponse(CustomGiftWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
