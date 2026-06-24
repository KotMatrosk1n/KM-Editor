// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Shops;
using KM.Api.Workflows;
using KM.Api.ZaCache;
using KM.ZA.Items;
using KM.ZA.Moves;
using KM.ZA.Pokemon;
using KM.ZA.Shops;
using KM.ZA.Workflows;

namespace KM.Tools.Bridge;

public static class ZaBridgeMapper
{
    public static ZaCacheMode ToCore(ZaCacheModeDto mode)
    {
        return mode switch
        {
            ZaCacheModeDto.Minimal => ZaCacheMode.Minimal,
            ZaCacheModeDto.Balanced => ZaCacheMode.Balanced,
            ZaCacheModeDto.Performance => ZaCacheMode.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static ZaCacheStatusResponse ToDto(ZaCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new ZaCacheStatusResponse(new ZaCacheStatusDto(
            new ZaCacheSettingsDto(ToDto(status.Settings.Mode), status.Settings.MaxCacheSizeBytes),
            status.CacheSizeBytes,
            status.WarmupCompleted,
            status.WarmupTotal,
            status.ProgressPercent,
            status.Phase,
            status.Message,
            status.IsActiveProjectPreserved));
    }

    public static ZaOutputMode ToCore(ChangePlanOutputModeDto? outputMode)
    {
        return outputMode switch
        {
            null => ZaOutputMode.Standalone,
            ChangePlanOutputModeDto.Standalone => ZaOutputMode.Standalone,
            ChangePlanOutputModeDto.TrinityModManager => ZaOutputMode.TrinityModManager,
            ChangePlanOutputModeDto.TrinityBypass => ZaOutputMode.TrinityBypass,
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, null),
        };
    }

    public static ListWorkflowsResponse ToDto(ZaWorkflowList workflowList)
    {
        ArgumentNullException.ThrowIfNull(workflowList);

        return new ListWorkflowsResponse(workflowList.Workflows.Select(ToDto).ToArray());
    }

    public static LoadPokemonWorkflowResponse ToDto(ZaPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPokemonWorkflowResponse(ToPokemonWorkflowDto(workflow));
    }

    public static LoadItemsWorkflowResponse ToDto(ZaItemsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadItemsWorkflowResponse(ToItemsWorkflowDto(workflow));
    }

    public static LoadMovesWorkflowResponse ToDto(ZaMovesWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadMovesWorkflowResponse(ToMovesWorkflowDto(workflow));
    }

    public static LoadShopsWorkflowResponse ToDto(ZaShopsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadShopsWorkflowResponse(ToShopsWorkflowDto(workflow));
    }

    public static UpdatePokemonFieldResponse ToDto(ZaPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonFieldResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateItemFieldResponse ToDto(ZaItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateItemFieldsResponse ToItemFieldsDto(ZaItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldsResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateMoveFieldResponse ToDto(ZaMovesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateMoveFieldResponse(
            ToMovesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateMoveFieldsResponse ToMoveFieldsDto(ZaMovesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateMoveFieldsResponse(
            ToMovesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateShopInventoryItemResponse ToDto(ZaShopsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateShopInventoryItemResponse(
            ToShopsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonFieldsResponse ToPokemonFieldsDto(ZaPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonFieldsResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonLearnsetResponse ToDtoLearnsetUpdate(ZaPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonLearnsetResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonEvolutionResponse ToDtoEvolutionUpdate(ZaPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonEvolutionResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ValidateEditSessionResponse ToDto(ZaEditSessionValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new ValidateEditSessionResponse(
            EditSessionBridgeMapper.ToDto(validation.Session),
            validation.IsValid,
            validation.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static WorkflowSummaryDto ToDto(ZaWorkflowSummary summary)
    {
        return new WorkflowSummaryDto(
            summary.Id,
            summary.Label,
            summary.Description,
            ToDto(summary.Availability),
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static PokemonWorkflowDto ToPokemonWorkflowDto(ZaPokemonWorkflow workflow)
    {
        return new PokemonWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Pokemon.Select(ToDto).ToArray(),
            new PokemonWorkflowStatsDto(
                workflow.Stats.TotalPokemonCount,
                workflow.Stats.PresentPokemonCount,
                workflow.Stats.TotalEvolutionCount,
                workflow.Stats.TotalLearnsetMoveCount,
                workflow.Stats.SourceFileCount),
            workflow.EvolutionMethodOptions.Select(ToDto).ToArray(),
            workflow.LearnsetMoveOptions.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemsWorkflowDto ToItemsWorkflowDto(ZaItemsWorkflow workflow)
    {
        return new ItemsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Items.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new ItemsWorkflowStatsDto(
                workflow.Stats.TotalItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static MovesWorkflowDto ToMovesWorkflowDto(ZaMovesWorkflow workflow)
    {
        return new MovesWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Moves.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new MovesWorkflowStatsDto(
                workflow.Stats.TotalMoveCount,
                workflow.Stats.EnabledMoveCount,
                workflow.Stats.SourceFileCount,
                workflow.Stats.ActiveFlagCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ShopsWorkflowDto ToShopsWorkflowDto(ZaShopsWorkflow workflow)
    {
        return new ShopsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Shops.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new ShopsWorkflowStatsDto(
                workflow.Stats.TotalShopCount,
                workflow.Stats.TotalInventoryItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray())
        {
            EditorFamily = "za",
        };
    }

    private static PokemonRecordDto ToDto(ZaPokemonRecord pokemon)
    {
        return new PokemonRecordDto(
            pokemon.PersonalId,
            pokemon.SpeciesId,
            pokemon.Form,
            pokemon.Name,
            pokemon.FormLabel,
            pokemon.Type1,
            pokemon.Type2,
            new PokemonBaseStatsDto(
                pokemon.BaseStats.HP,
                pokemon.BaseStats.Attack,
                pokemon.BaseStats.Defense,
                pokemon.BaseStats.SpecialAttack,
                pokemon.BaseStats.SpecialDefense,
                pokemon.BaseStats.Speed,
                pokemon.BaseStats.Total),
            new PokemonAbilitySetDto(
                pokemon.Abilities.Ability1,
                pokemon.Abilities.Ability1Label,
                pokemon.Abilities.Ability2,
                pokemon.Abilities.Ability2Label,
                pokemon.Abilities.HiddenAbility,
                pokemon.Abilities.HiddenAbilityLabel),
            new PokemonDexPresenceDto(
                pokemon.DexPresence.IsPresentInGame,
                pokemon.DexPresence.IsInAnyDex,
                pokemon.DexPresence.RegionalDexIndex,
                pokemon.DexPresence.ArmorDexIndex,
                pokemon.DexPresence.CrownDexIndex),
            new PokemonPersonalDetailsDto(
                pokemon.Personal.Type1,
                pokemon.Personal.Type2,
                pokemon.Personal.CatchRate,
                pokemon.Personal.EvolutionStage,
                pokemon.Personal.EVYieldHP,
                pokemon.Personal.EVYieldAttack,
                pokemon.Personal.EVYieldDefense,
                pokemon.Personal.EVYieldSpecialAttack,
                pokemon.Personal.EVYieldSpecialDefense,
                pokemon.Personal.EVYieldSpeed,
                pokemon.Personal.HeldItem1,
                pokemon.Personal.HeldItem2,
                pokemon.Personal.HeldItem3,
                pokemon.Personal.GenderRatio,
                pokemon.Personal.HatchCycles,
                pokemon.Personal.BaseFriendship,
                pokemon.Personal.ExpGrowth,
                pokemon.Personal.EggGroup1,
                pokemon.Personal.EggGroup2,
                pokemon.Personal.FormStatsIndex,
                pokemon.Personal.FormCount,
                pokemon.Personal.Color,
                pokemon.Personal.IsPresentInGame,
                pokemon.Personal.HasSpriteForm,
                pokemon.Personal.ModelId,
                pokemon.Personal.HatchedSpecies,
                pokemon.Personal.LocalFormIndex,
                pokemon.Personal.IsRegionalForm,
                pokemon.Personal.CanNotDynamax,
                pokemon.Personal.Form),
            pokemon.CatchRate,
            pokemon.EvolutionStage,
            pokemon.GenderRatio,
            pokemon.GenderRatioLabel,
            pokemon.BaseExperience,
            pokemon.Height,
            pokemon.Weight,
            pokemon.Evolutions.Select(ToDto).ToArray(),
            pokemon.Learnset.Select(ToDto).ToArray(),
            pokemon.Compatibility.Select(ToDto).ToArray(),
            new PokemonProvenanceDto(
                pokemon.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(pokemon.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(pokemon.Provenance.FileState)));
    }

    private static ItemRecordDto ToDto(ZaItemRecord item)
    {
        return new ItemRecordDto(
            item.ItemId,
            item.Name,
            item.Category,
            item.BuyPrice,
            item.SellPrice,
            item.WattsPrice,
            item.AlternatePrice,
            ToDto(item.Metadata),
            item.SharedItemIds.ToArray(),
            item.DetailGroups.Select(ToDto).ToArray(),
            ToDto(item.Provenance));
    }

    private static ItemMetadataDto ToDto(ZaItemMetadata metadata)
    {
        return new ItemMetadataDto(
            metadata.Pouch,
            metadata.PouchFlags,
            metadata.FlingPower,
            metadata.FieldUseType,
            metadata.FieldFlags,
            metadata.CanUseOnPokemon,
            metadata.ItemType,
            metadata.SortIndex,
            metadata.ItemSprite,
            metadata.GroupType,
            metadata.GroupIndex,
            metadata.CureStatusFlags,
            metadata.Boost0,
            metadata.Boost1,
            metadata.Boost2,
            metadata.Boost3,
            metadata.UseFlags1,
            metadata.UseFlags2,
            metadata.EvHp,
            metadata.EvAttack,
            metadata.EvDefense,
            metadata.EvSpeed,
            metadata.EvSpecialAttack,
            metadata.EvSpecialDefense,
            metadata.HealAmount,
            metadata.PpGain,
            metadata.FriendshipGain1,
            metadata.FriendshipGain2,
            metadata.FriendshipGain3,
            metadata.MachineSlot,
            metadata.MachineMoveId,
            metadata.MachineMoveName);
    }

    private static ItemDetailGroupDto ToDto(ZaItemDetailGroup group)
    {
        return new ItemDetailGroupDto(
            group.Label,
            group.Details.Select(ToDto).ToArray());
    }

    private static ItemDetailDto ToDto(ZaItemDetail detail)
    {
        return new ItemDetailDto(detail.Label, detail.Value);
    }

    private static ItemProvenanceDto ToDto(ZaItemProvenance provenance)
    {
        return new ItemProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ItemEditableFieldDto ToDto(ZaItemEditableField field)
    {
        return new ItemEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static ItemEditableFieldOptionDto ToDto(ZaItemEditableFieldOption option)
    {
        return new ItemEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PokemonEditableFieldDto ToDto(ZaPokemonEditableField field)
    {
        return new PokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.Group,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static PokemonEditableFieldOptionDto ToDto(ZaPokemonEditableFieldOption option)
    {
        return new PokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PokemonEvolutionRecordDto ToDto(ZaPokemonEvolutionRecord evolution)
    {
        return new PokemonEvolutionRecordDto(
            evolution.Slot,
            evolution.Method,
            evolution.Argument,
            evolution.Species,
            evolution.Form,
            evolution.Level,
            evolution.MethodName,
            evolution.ArgumentKind,
            evolution.ArgumentLabel,
            evolution.ArgumentValue);
    }

    private static PokemonEvolutionMethodOptionDto ToDto(ZaPokemonEvolutionMethodOption option)
    {
        return new PokemonEvolutionMethodOptionDto(
            option.Value,
            option.Label,
            option.ArgumentKind,
            option.ArgumentLabel,
            option.ArgumentOptions.Select(ToDto).ToArray());
    }

    private static PokemonLearnsetMoveDto ToDto(ZaPokemonLearnsetMove learnsetMove)
    {
        return new PokemonLearnsetMoveDto(
            learnsetMove.Slot,
            learnsetMove.MoveId,
            learnsetMove.MoveName,
            learnsetMove.Level,
            learnsetMove.RawLevel == learnsetMove.Level ? null : learnsetMove.RawLevel,
            learnsetMove.LevelLabel);
    }

    private static PokemonCompatibilityGroupDto ToDto(ZaPokemonCompatibilityGroup group)
    {
        return new PokemonCompatibilityGroupDto(
            group.GroupId,
            group.Label,
            group.EnabledCount,
            group.Entries.Select(ToDto).ToArray());
    }

    private static PokemonCompatibilityEntryDto ToDto(ZaPokemonCompatibilityEntry entry)
    {
        return new PokemonCompatibilityEntryDto(
            entry.Slot,
            entry.MoveId,
            entry.MoveName,
            entry.Label,
            entry.CanLearn);
    }

    private static MoveRecordDto ToDto(ZaMoveRecord move)
    {
        return new MoveRecordDto(
            move.MoveId,
            move.Name,
            move.Description,
            move.Version,
            move.CanUseMove,
            move.Type,
            move.TypeName,
            move.Quality,
            move.Category,
            move.CategoryName,
            move.Power,
            move.Accuracy,
            move.PP,
            move.Priority,
            move.CritStage,
            move.MaxMovePower,
            move.Target,
            move.TargetName,
            move.HitMin,
            move.HitMax,
            move.TurnMin,
            move.TurnMax,
            move.Inflict,
            move.InflictName,
            move.InflictPercent,
            move.RawInflictCount,
            move.Flinch,
            move.EffectSequence,
            move.Recoil,
            move.RawHealing,
            move.StatChanges.Select(ToDto).ToArray(),
            move.Flags.Select(ToDto).ToArray(),
            ToDto(move.Provenance));
    }

    private static MoveEditableFieldDto ToDto(ZaMoveEditableField field)
    {
        return new MoveEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static MoveEditableFieldOptionDto ToDto(ZaMoveEditableFieldOption option)
    {
        return new MoveEditableFieldOptionDto(option.Value, option.Label);
    }

    private static MoveStatChangeRecordDto ToDto(ZaMoveStatChangeRecord statChange)
    {
        return new MoveStatChangeRecordDto(
            statChange.Slot,
            statChange.Stat,
            statChange.StatName,
            statChange.Stage,
            statChange.Percent);
    }

    private static MoveFlagRecordDto ToDto(ZaMoveFlagRecord flag)
    {
        return new MoveFlagRecordDto(
            flag.Field,
            flag.Label,
            flag.Enabled);
    }

    private static MoveProvenanceDto ToDto(ZaMoveProvenance provenance)
    {
        return new MoveProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShopRecordDto ToDto(ZaShopRecord shop)
    {
        return new ShopRecordDto(
            shop.ShopId,
            shop.Name,
            shop.Kind,
            shop.InventoryLabel,
            shop.InventoryIndex,
            shop.InventoryCount,
            shop.SourceHash,
            shop.InventorySummary,
            shop.Location,
            shop.Currency,
            shop.Inventory.Select(ToDto).ToArray(),
            ToDto(shop.Provenance))
        {
            EditorFamily = "za",
            CanEditInventoryOrder = shop.CanEditInventoryOrder,
        };
    }

    private static ShopInventoryRecordDto ToDto(ZaShopInventoryRecord inventoryItem)
    {
        return new ShopInventoryRecordDto(
            inventoryItem.Slot,
            inventoryItem.ItemId,
            inventoryItem.ItemName,
            inventoryItem.Price,
            inventoryItem.IsKnownItem,
            inventoryItem.StockLimit)
        {
            FieldValues = inventoryItem.FieldValues,
            FieldDisplayValues = inventoryItem.FieldDisplayValues,
            SupportedFields = inventoryItem.SupportedFields,
            PriceField = inventoryItem.PriceField,
            CanEditPrice = inventoryItem.CanEditPrice,
        };
    }

    private static ShopProvenanceDto ToDto(ZaShopProvenance provenance)
    {
        return new ShopProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShopEditableFieldDto ToDto(ZaShopEditableField field)
    {
        return new ShopEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static ShopEditableFieldOptionDto ToDto(ZaShopEditableFieldOption option)
    {
        return new ShopEditableFieldOptionDto(
            option.Value,
            option.Label,
            option.ItemName,
            option.Price);
    }

    private static ZaCacheModeDto ToDto(ZaCacheMode mode)
    {
        return mode switch
        {
            ZaCacheMode.Minimal => ZaCacheModeDto.Minimal,
            ZaCacheMode.Balanced => ZaCacheModeDto.Balanced,
            ZaCacheMode.Performance => ZaCacheModeDto.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private static WorkflowAvailabilityDto ToDto(ZaWorkflowAvailability availability)
    {
        return availability switch
        {
            ZaWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
            ZaWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
            ZaWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }
}
