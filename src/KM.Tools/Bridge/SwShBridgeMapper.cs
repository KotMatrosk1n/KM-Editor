// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Items;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.Flagwork;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Shops;
using KM.Api.Raids;
using KM.Api.RoyalCandy;
using KM.Api.Text;
using KM.Api.SpreadsheetImport;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.SwSh.Items;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.Flagwork;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Shops;
using KM.SwSh.Raids;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Text;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;

namespace KM.Tools.Bridge;

public static class SwShBridgeMapper
{
    public static ListWorkflowsResponse ToDto(SwShWorkflowList workflowList)
    {
        ArgumentNullException.ThrowIfNull(workflowList);

        return new ListWorkflowsResponse(workflowList.Workflows.Select(ToDto).ToArray());
    }

    public static LoadItemsWorkflowResponse ToDto(SwShItemsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadItemsWorkflowResponse(ToItemsWorkflowDto(workflow));
    }

    public static LoadPokemonWorkflowResponse ToDto(SwShPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPokemonWorkflowResponse(ToPokemonWorkflowDto(workflow));
    }

    public static LoadTextWorkflowResponse ToDto(SwShTextWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTextWorkflowResponse(ToTextWorkflowDto(workflow));
    }

    public static LoadTrainersWorkflowResponse ToDto(SwShTrainersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTrainersWorkflowResponse(ToTrainersWorkflowDto(workflow));
    }

    public static LoadShopsWorkflowResponse ToDto(SwShShopsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadShopsWorkflowResponse(ToShopsWorkflowDto(workflow));
    }

    public static LoadEncountersWorkflowResponse ToDto(SwShEncountersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadEncountersWorkflowResponse(ToEncountersWorkflowDto(workflow));
    }

    public static LoadRaidRewardsWorkflowResponse ToDto(SwShRaidRewardsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadRaidRewardsWorkflowResponse(ToRaidRewardsWorkflowDto(workflow));
    }

    public static LoadPlacementWorkflowResponse ToDto(SwShPlacementWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPlacementWorkflowResponse(ToPlacementWorkflowDto(workflow));
    }

    public static LoadFlagworkSaveWorkflowResponse ToDto(SwShFlagworkSaveWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadFlagworkSaveWorkflowResponse(ToFlagworkSaveWorkflowDto(workflow));
    }

    public static LoadExeFsPatchWorkflowResponse ToDto(SwShExeFsPatchWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadExeFsPatchWorkflowResponse(ToExeFsPatchWorkflowDto(workflow));
    }

    public static StageExeFsPatchResponse ToDto(SwShExeFsPatchEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageExeFsPatchResponse(
            ToExeFsPatchWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadRoyalCandyWorkflowResponse ToDto(SwShRoyalCandyWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadRoyalCandyWorkflowResponse(ToRoyalCandyWorkflowDto(workflow));
    }

    public static StageRoyalCandyWorkflowResponse ToDto(SwShRoyalCandyEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageRoyalCandyWorkflowResponse(
            ToRoyalCandyWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadSpreadsheetImportWorkflowResponse ToDto(SwShSpreadsheetImportWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadSpreadsheetImportWorkflowResponse(ToSpreadsheetImportWorkflowDto(workflow));
    }

    public static PreviewSpreadsheetImportResponse ToDto(SwShSpreadsheetImportExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new PreviewSpreadsheetImportResponse(
            ToSpreadsheetImportWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            ToDto(result.Preview),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateItemFieldResponse ToDto(SwShItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTextEntryResponse ToDto(SwShTextEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTextEntryResponse(
            ToTextWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTrainerFieldResponse ToDto(SwShTrainersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTrainerFieldResponse(
            ToTrainersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateShopInventoryItemResponse ToDto(SwShShopsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateShopInventoryItemResponse(
            ToShopsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateEncounterSlotFieldResponse ToDto(SwShEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateEncounterSlotFieldResponse(
            ToEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRaidRewardFieldResponse ToDto(SwShRaidRewardsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRaidRewardFieldResponse(
            ToRaidRewardsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePlacementObjectFieldResponse ToDto(SwShPlacementEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePlacementObjectFieldResponse(
            ToPlacementWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ValidateEditSessionResponse ToDto(SwShEditSessionValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new ValidateEditSessionResponse(
            EditSessionBridgeMapper.ToDto(validation.Session),
            validation.IsValid,
            validation.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemsWorkflowDto ToItemsWorkflowDto(SwShItemsWorkflow workflow)
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

    private static PokemonWorkflowDto ToPokemonWorkflowDto(SwShPokemonWorkflow workflow)
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
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TextWorkflowDto ToTextWorkflowDto(SwShTextWorkflow workflow)
    {
        return new TextWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Entries.Select(ToDto).ToArray(),
            workflow.DialogueReferences.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new TextWorkflowStatsDto(
                workflow.Stats.TotalTextEntryCount,
                workflow.Stats.DialogueReferenceCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TrainersWorkflowDto ToTrainersWorkflowDto(SwShTrainersWorkflow workflow)
    {
        return new TrainersWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Trainers.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new TrainersWorkflowStatsDto(
                workflow.Stats.TotalTrainerCount,
                workflow.Stats.TotalPokemonCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ShopsWorkflowDto ToShopsWorkflowDto(SwShShopsWorkflow workflow)
    {
        return new ShopsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Shops.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new ShopsWorkflowStatsDto(
                workflow.Stats.TotalShopCount,
                workflow.Stats.TotalInventoryItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static EncountersWorkflowDto ToEncountersWorkflowDto(SwShEncountersWorkflow workflow)
    {
        return new EncountersWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Tables.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new EncountersWorkflowStatsDto(
                workflow.Stats.TotalTableCount,
                workflow.Stats.TotalSlotCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static RaidRewardsWorkflowDto ToRaidRewardsWorkflowDto(SwShRaidRewardsWorkflow workflow)
    {
        return new RaidRewardsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Tables.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new RaidRewardsWorkflowStatsDto(
                workflow.Stats.TotalTableCount,
                workflow.Stats.TotalRewardItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static PlacementWorkflowDto ToPlacementWorkflowDto(SwShPlacementWorkflow workflow)
    {
        return new PlacementWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Objects.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new PlacementWorkflowStatsDto(
                workflow.Stats.TotalObjectCount,
                workflow.Stats.TotalAreaCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static FlagworkSaveWorkflowDto ToFlagworkSaveWorkflowDto(SwShFlagworkSaveWorkflow workflow)
    {
        return new FlagworkSaveWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Flags.Select(ToDto).ToArray(),
            workflow.SaveBlocks.Select(ToDto).ToArray(),
            workflow.SaveFile is null ? null : ToDto(workflow.SaveFile),
            new FlagworkSaveWorkflowStatsDto(
                workflow.Stats.TotalFlagCount,
                workflow.Stats.TotalSaveBlockCount,
                workflow.Stats.SourceFileCount,
                workflow.Stats.HasSaveFile),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ExeFsPatchWorkflowDto ToExeFsPatchWorkflowDto(SwShExeFsPatchWorkflow workflow)
    {
        return new ExeFsPatchWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Patches.Select(ToDto).ToArray(),
            workflow.Segments.Select(ToDto).ToArray(),
            workflow.Checks.Select(ToDto).ToArray(),
            new ExeFsPatchWorkflowStatsDto(
                workflow.Stats.TotalPatchCount,
                workflow.Stats.TotalCheckCount,
                workflow.Stats.PassCount,
                workflow.Stats.WarningCount,
                workflow.Stats.FailCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static RoyalCandyWorkflowDto ToRoyalCandyWorkflowDto(SwShRoyalCandyWorkflow workflow)
    {
        return new RoyalCandyWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Workflows.Select(ToDto).ToArray(),
            workflow.Checks.Select(ToDto).ToArray(),
            workflow.Outputs.Select(ToDto).ToArray(),
            new RoyalCandyWorkflowStatsDto(
                workflow.Stats.TotalWorkflowCount,
                workflow.Stats.TotalStepCount,
                workflow.Stats.TotalCheckCount,
                workflow.Stats.PassCount,
                workflow.Stats.WarningCount,
                workflow.Stats.FailCount,
                workflow.Stats.OutputCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportWorkflowDto ToSpreadsheetImportWorkflowDto(
        SwShSpreadsheetImportWorkflow workflow)
    {
        return new SpreadsheetImportWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Profiles.Select(ToDto).ToArray(),
            new SpreadsheetImportWorkflowStatsDto(
                workflow.Stats.TotalProfileCount,
                workflow.Stats.TotalColumnCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static WorkflowSummaryDto ToDto(SwShWorkflowSummary summary)
    {
        return new WorkflowSummaryDto(
            summary.Id,
            summary.Label,
            summary.Description,
            ToDto(summary.Availability),
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemRecordDto ToDto(SwShItemRecord item)
    {
        return new ItemRecordDto(
            item.ItemId,
            item.Name,
            item.Category,
            item.BuyPrice,
            item.SellPrice,
            item.WattsPrice,
            item.AlternatePrice,
            item.SharedItemIds,
            new ItemProvenanceDto(
                item.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(item.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(item.Provenance.FileState)));
    }

    private static ItemEditableFieldDto ToDto(SwShItemEditableField field)
    {
        return new ItemEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue);
    }

    private static PokemonRecordDto ToDto(SwShPokemonRecord pokemon)
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
                pokemon.Abilities.Ability2,
                pokemon.Abilities.HiddenAbility),
            new PokemonDexPresenceDto(
                pokemon.DexPresence.IsPresentInGame,
                pokemon.DexPresence.IsInAnyDex,
                pokemon.DexPresence.RegionalDexIndex,
                pokemon.DexPresence.ArmorDexIndex,
                pokemon.DexPresence.CrownDexIndex),
            pokemon.CatchRate,
            pokemon.EvolutionStage,
            pokemon.GenderRatio,
            pokemon.BaseExperience,
            pokemon.Height,
            pokemon.Weight,
            pokemon.Evolutions.Select(ToDto).ToArray(),
            pokemon.Learnset.Select(ToDto).ToArray(),
            new PokemonProvenanceDto(
                pokemon.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(pokemon.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(pokemon.Provenance.FileState)));
    }

    private static PokemonEvolutionRecordDto ToDto(SwShPokemonEvolutionRecord evolution)
    {
        return new PokemonEvolutionRecordDto(
            evolution.Method,
            evolution.Argument,
            evolution.Species,
            evolution.Form,
            evolution.Level);
    }

    private static PokemonLearnsetMoveDto ToDto(SwShPokemonLearnsetMove learnsetMove)
    {
        return new PokemonLearnsetMoveDto(
            learnsetMove.MoveId,
            learnsetMove.MoveName,
            learnsetMove.Level);
    }

    private static TextEntryRecordDto ToDto(SwShTextEntryRecord entry)
    {
        return new TextEntryRecordDto(
            entry.TextId,
            entry.TextKey,
            entry.Label,
            entry.Language,
            entry.SourceFile,
            entry.LineIndex,
            entry.Value,
            entry.CanEdit,
            entry.EditBlockedReason,
            ToDto(entry.Provenance));
    }

    private static TextEditableFieldDto ToDto(SwShTextEditableField field)
    {
        return new TextEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumLength,
            field.MaximumLength);
    }

    private static DialogueReferenceRecordDto ToDto(SwShDialogueReferenceRecord reference)
    {
        return new DialogueReferenceRecordDto(
            reference.DialogueId,
            reference.Label,
            reference.TextId,
            reference.Context,
            reference.Preview,
            ToDto(reference.Provenance));
    }

    private static TextProvenanceDto ToDto(SwShTextProvenance provenance)
    {
        return new TextProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TrainerRecordDto ToDto(SwShTrainerRecord trainer)
    {
        return new TrainerRecordDto(
            trainer.TrainerId,
            trainer.Name,
            trainer.TrainerClassId,
            trainer.TrainerClass,
            trainer.Location,
            trainer.BattleTypeValue,
            trainer.BattleType,
            trainer.Team.Select(ToDto).ToArray(),
            ToDto(trainer.Provenance));
    }

    private static TrainerPokemonRecordDto ToDto(SwShTrainerPokemonRecord pokemon)
    {
        return new TrainerPokemonRecordDto(
            pokemon.Slot,
            pokemon.SpeciesId,
            pokemon.Species,
            pokemon.Level,
            pokemon.HeldItemId,
            pokemon.HeldItem,
            pokemon.MoveIds,
            pokemon.Moves);
    }

    private static TrainerEditableFieldDto ToDto(SwShTrainerEditableField field)
    {
        return new TrainerEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue);
    }

    private static TrainerProvenanceDto ToDto(SwShTrainerProvenance provenance)
    {
        return new TrainerProvenanceDto(
            provenance.SourceFile,
            provenance.TeamSourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.TeamSourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState),
            ProjectBridgeMapper.ToDto(provenance.TeamFileState));
    }

    private static ShopRecordDto ToDto(SwShShopRecord shop)
    {
        return new ShopRecordDto(
            shop.ShopId,
            shop.Name,
            shop.Location,
            shop.Currency,
            shop.Inventory.Select(ToDto).ToArray(),
            ToDto(shop.Provenance));
    }

    private static ShopInventoryRecordDto ToDto(SwShShopInventoryRecord inventoryItem)
    {
        return new ShopInventoryRecordDto(
            inventoryItem.Slot,
            inventoryItem.ItemId,
            inventoryItem.ItemName,
            inventoryItem.Price,
            inventoryItem.StockLimit);
    }

    private static ShopProvenanceDto ToDto(SwShShopProvenance provenance)
    {
        return new ShopProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShopEditableFieldDto ToDto(SwShShopEditableField field)
    {
        return new ShopEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue);
    }

    private static EncounterTableRecordDto ToDto(SwShEncounterTableRecord table)
    {
        return new EncounterTableRecordDto(
            table.TableId,
            table.Location,
            table.Area,
            table.EncounterType,
            table.GameVersion,
            table.ArchiveMember,
            table.Slots.Select(ToDto).ToArray(),
            ToDto(table.Provenance));
    }

    private static EncounterSlotRecordDto ToDto(SwShEncounterSlotRecord slot)
    {
        return new EncounterSlotRecordDto(
            slot.Slot,
            slot.SpeciesId,
            slot.Species,
            slot.Form,
            slot.LevelMin,
            slot.LevelMax,
            slot.Weight,
            slot.TimeOfDay,
            slot.Weather);
    }

    private static EncounterEditableFieldDto ToDto(SwShEncounterEditableField field)
    {
        return new EncounterEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue);
    }

    private static EncounterProvenanceDto ToDto(SwShEncounterProvenance provenance)
    {
        return new EncounterProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static RaidRewardTableRecordDto ToDto(SwShRaidRewardTableRecord table)
    {
        return new RaidRewardTableRecordDto(
            table.TableId,
            table.DenId,
            table.Rank,
            table.GameVersion,
            table.RewardKind,
            table.RewardKindLabel,
            table.ArchiveMember,
            table.TableIndex,
            table.SourceTableHash,
            table.Rewards.Select(ToDto).ToArray(),
            ToDto(table.Provenance));
    }

    private static RaidRewardItemRecordDto ToDto(SwShRaidRewardItemRecord reward)
    {
        return new RaidRewardItemRecordDto(
            reward.Slot,
            reward.EntryId,
            reward.ItemId,
            reward.ItemName,
            reward.Quantity,
            reward.Weight,
            reward.Values);
    }

    private static RaidRewardEditableFieldDto ToDto(SwShRaidRewardEditableField field)
    {
        return new RaidRewardEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue);
    }

    private static RaidRewardProvenanceDto ToDto(SwShRaidRewardProvenance provenance)
    {
        return new RaidRewardProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static PlacedObjectRecordDto ToDto(SwShPlacedObjectRecord placedObject)
    {
        return new PlacedObjectRecordDto(
            placedObject.ObjectId,
            placedObject.ObjectType,
            placedObject.Label,
            placedObject.Map,
            placedObject.ArchiveMember,
            placedObject.ZoneIndex,
            placedObject.ObjectIndex,
            placedObject.ChanceIndex,
            placedObject.ItemId,
            placedObject.ItemName,
            placedObject.ItemHash,
            placedObject.Quantity,
            placedObject.Chance,
            placedObject.X,
            placedObject.Y,
            placedObject.Z,
            placedObject.RotationY,
            placedObject.ScriptId,
            ToDto(placedObject.Provenance));
    }

    private static PlacementEditableFieldDto ToDto(SwShPlacementEditableField field)
    {
        return new PlacementEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue);
    }

    private static PlacementProvenanceDto ToDto(SwShPlacementProvenance provenance)
    {
        return new PlacementProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static FlagRecordDto ToDto(SwShFlagRecord flag)
    {
        return new FlagRecordDto(
            flag.FlagId,
            flag.Name,
            flag.Category,
            flag.Kind,
            flag.ValueKind,
            flag.DefaultValue,
            flag.Description,
            flag.Table,
            flag.Index,
            flag.Hash,
            flag.Low32Key,
            ToDto(flag.Provenance));
    }

    private static SaveBlockRecordDto ToDto(SwShSaveBlockRecord saveBlock)
    {
        return new SaveBlockRecordDto(
            saveBlock.BlockId,
            saveBlock.Name,
            saveBlock.Key,
            saveBlock.Hash,
            saveBlock.Kind,
            saveBlock.ValueKind,
            saveBlock.Description,
            ToDto(saveBlock.Provenance));
    }

    private static SaveFileRecordDto ToDto(SwShSaveFileRecord saveFile)
    {
        return new SaveFileRecordDto(
            saveFile.FileName,
            saveFile.SizeBytes,
            saveFile.Sha256,
            saveFile.Status,
            saveFile.Description);
    }

    private static FlagworkSaveProvenanceDto ToDto(SwShFlagworkSaveProvenance provenance)
    {
        return new FlagworkSaveProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ExeFsPatchRecordDto ToDto(SwShExeFsPatchRecord patch)
    {
        return new ExeFsPatchRecordDto(
            patch.PatchId,
            patch.Name,
            patch.TargetFile,
            patch.PatchKind,
            patch.Status,
            patch.Description,
            patch.Details,
            ToDto(patch.Provenance));
    }

    private static ExeFsSegmentRecordDto ToDto(SwShExeFsSegmentRecord segment)
    {
        return new ExeFsSegmentRecordDto(
            segment.SegmentId,
            segment.Name,
            segment.FileOffset,
            segment.MemoryOffset,
            segment.DecompressedSize,
            segment.CompressedSize,
            segment.Sha256,
            segment.HashStatus,
            ToDto(segment.Provenance));
    }

    private static ExeFsPatchCheckRecordDto ToDto(SwShExeFsPatchCheckRecord check)
    {
        return new ExeFsPatchCheckRecordDto(
            check.CheckId,
            check.PatchId,
            check.Status,
            check.Area,
            check.Offset,
            check.Name,
            check.Expected,
            check.Actual,
            check.Notes,
            ToDto(check.Provenance));
    }

    private static ExeFsPatchProvenanceDto ToDto(SwShExeFsPatchProvenance provenance)
    {
        return new ExeFsPatchProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static RoyalCandyWorkflowRecordDto ToDto(SwShRoyalCandyWorkflowRecord workflow)
    {
        return new RoyalCandyWorkflowRecordDto(
            workflow.WorkflowId,
            workflow.Name,
            workflow.Category,
            workflow.Target,
            workflow.Mode,
            workflow.ItemId,
            workflow.TemplateItemId,
            workflow.Status,
            workflow.Description,
            workflow.Steps.Select(ToDto).ToArray(),
            ToDto(workflow.Provenance));
    }

    private static RoyalCandyWorkflowCheckRecordDto ToDto(SwShRoyalCandyWorkflowCheckRecord check)
    {
        return new RoyalCandyWorkflowCheckRecordDto(
            check.CheckId,
            check.WorkflowId,
            check.Status,
            check.Area,
            check.Target,
            check.Message,
            ToDto(check.Provenance));
    }

    private static RoyalCandyOutputRecordDto ToDto(SwShRoyalCandyOutputRecord output)
    {
        return new RoyalCandyOutputRecordDto(
            output.OutputId,
            output.WorkflowId,
            output.RelativePath,
            output.SourceFile,
            output.OutputKind,
            output.Status,
            output.Description,
            ToDto(output.Provenance));
    }

    private static RoyalCandyWorkflowStepRecordDto ToDto(SwShRoyalCandyWorkflowStepRecord step)
    {
        return new RoyalCandyWorkflowStepRecordDto(
            step.Step,
            step.Label,
            step.Description);
    }

    private static RoyalCandyProvenanceDto ToDto(SwShRoyalCandyProvenance provenance)
    {
        return new RoyalCandyProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static SpreadsheetImportProfileRecordDto ToDto(
        SwShSpreadsheetImportProfileRecord profile)
    {
        return new SpreadsheetImportProfileRecordDto(
            profile.ProfileId,
            profile.Name,
            profile.SourceKind,
            profile.TargetWorkflow,
            profile.Status,
            profile.Description,
            profile.Columns.Select(ToDto).ToArray(),
            ToDto(profile.Provenance));
    }

    private static SpreadsheetImportColumnRecordDto ToDto(
        SwShSpreadsheetImportColumnRecord column)
    {
        return new SpreadsheetImportColumnRecordDto(
            column.Column,
            column.Header,
            column.ValueKind,
            column.IsRequired,
            column.Description);
    }

    private static SpreadsheetImportProvenanceDto ToDto(SwShSpreadsheetImportProvenance provenance)
    {
        return new SpreadsheetImportProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static SpreadsheetImportPreviewDto ToDto(SwShSpreadsheetImportPreview preview)
    {
        return new SpreadsheetImportPreviewDto(
            preview.ProfileId,
            preview.SourcePath,
            preview.TotalRowCount,
            preview.AcceptedRowCount,
            preview.RejectedRowCount,
            preview.SkippedRowCount,
            preview.Rows.Select(ToDto).ToArray());
    }

    private static SpreadsheetImportRowPreviewRecordDto ToDto(
        SwShSpreadsheetImportRowPreviewRecord row)
    {
        return new SpreadsheetImportRowPreviewRecordDto(
            row.RowNumber,
            row.RecordId,
            row.Status,
            row.Summary,
            row.Cells.Select(ToDto).ToArray(),
            row.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportCellPreviewRecordDto ToDto(
        SwShSpreadsheetImportCellPreviewRecord cell)
    {
        return new SpreadsheetImportCellPreviewRecordDto(
            cell.Header,
            cell.Field,
            cell.Value,
            cell.Status,
            cell.Message);
    }

    private static WorkflowAvailabilityDto ToDto(SwShWorkflowAvailability availability)
    {
        return availability switch
        {
            SwShWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
            SwShWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
            SwShWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }
}
