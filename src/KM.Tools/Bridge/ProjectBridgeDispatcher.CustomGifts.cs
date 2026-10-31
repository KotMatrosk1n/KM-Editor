// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.CustomGifts;
using KM.Api.Gifts;
using KM.Api.Editing;
using KM.SwSh.CustomGifts;

namespace KM.Tools.Bridge;

public sealed partial class ProjectBridgeDispatcher
{
    private string DispatchLoadCustomGifts(string requestJson)
    {
        var request = DeserializeRequest<LoadCustomGiftsRequest>(requestJson);
        return SerializeSuccess(new LoadCustomGiftsResponse(ToDto(customGiftService.Load(
            ProjectBridgeMapper.ToCore(request.Payload.Paths)))), request.RequestId);
    }

    private string DispatchStageCustomGifts(string requestJson)
    {
        var request = DeserializeRequest<StageCustomGiftsRequest>(requestJson);
        var result = customGiftService.Stage(ProjectBridgeMapper.ToCore(request.Payload.Paths), ToCore(request.Payload.Catalog),
            request.Payload.Session is null ? null : EditSessionBridgeMapper.ToCore(request.Payload.Session));
        return SerializeSuccess(new StageCustomGiftsResponse(ToDto(result.Workflow), EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray()), request.RequestId);
    }

    private static CustomGiftWorkflowDto ToDto(SwShCustomGiftWorkflow workflow) => new(workflow.CanEdit,
        workflow.InstalledMode, workflow.NeedsRepair, ToDto(workflow.Catalog), workflow.EditableFields.Select(field =>
            new GiftPokemonEditableFieldDto(field.Field, field.Label, field.ValueKind, field.MinimumValue, field.MaximumValue,
                field.Options.Select(option => new GiftPokemonEditableFieldOptionDto(option.Value, option.Label)).ToArray())).ToArray(),
        workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    private static CustomGiftCatalogDto ToDto(SwShCustomGiftCatalog catalog) => new(catalog.Version, catalog.Mode, catalog.NextClaimId,
        catalog.CodeGifts.Select(gift => new CodeGiftDto(gift.ClaimId, gift.Name, gift.Code, gift.OncePerSave, ToDto(gift.Pokemon))).ToArray(),
        catalog.RoyalPools.Select(pool => new RoyalGiftPoolDto(pool.Badges, pool.Pokemon.Select(ToDto).ToArray(),
            pool.Chances ?? SwShCustomGiftCatalogOdds.EqualChances(pool.Pokemon.Count))).ToArray());
    private static CustomGiftPokemonDto ToDto(SwShCustomGiftPokemon p) => new(p.SpeciesId, p.Form, p.Level, p.IsEgg,
        p.HeldItemId, p.BallItemId, p.Ability, p.Nature, p.Gender, p.ShinyLock, p.DynamaxLevel, p.CanGigantamax,
        p.SpecialMoveId, new(p.Ivs.HP, p.Ivs.Attack, p.Ivs.Defense, p.Ivs.SpecialAttack, p.Ivs.SpecialDefense, p.Ivs.Speed),
        p.FlawlessIvCount, p.GenderRatio, p.AbilityMask);
    private static SwShCustomGiftCatalog ToCore(CustomGiftCatalogDto catalog)
    {
        if (catalog is null || catalog.CodeGifts is null || catalog.RoyalPools is null
            || catalog.CodeGifts.Any(gift => gift is null || gift.Pokemon is null || gift.Pokemon.Ivs is null)
            || catalog.RoyalPools.Any(pool => pool is null || pool.Pokemon is null || pool.Chances is null
                || pool.Pokemon.Any(p => p is null || p.Ivs is null)))
            throw new System.Text.Json.JsonException("The custom gift catalog is incomplete.");
        return new(catalog.Version, catalog.Mode, catalog.NextClaimId,
            catalog.CodeGifts.Select(gift => new SwShCodeGift(gift.ClaimId, gift.Name, gift.Code, gift.OncePerSave, ToCore(gift.Pokemon))).ToArray(),
            catalog.RoyalPools.Select(pool => new SwShRoyalGiftPool(pool.Badges, pool.Pokemon.Select(ToCore).ToArray())
                { Chances = pool.Chances }).ToArray());
    }
    private static SwShCustomGiftPokemon ToCore(CustomGiftPokemonDto p) => new(p.SpeciesId, p.Form, p.Level, p.IsEgg,
        p.HeldItemId, p.BallItemId, p.Ability, p.Nature, p.Gender, p.ShinyLock, p.DynamaxLevel, p.CanGigantamax,
        p.SpecialMoveId, new(p.Ivs.HP, p.Ivs.Attack, p.Ivs.Defense, p.Ivs.SpecialAttack, p.Ivs.SpecialDefense, p.Ivs.Speed),
        p.FlawlessIvCount) { GenderRatio = p.GenderRatio, AbilityMask = p.AbilityMask };
}
