// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.Bridge;

/// <summary>
/// Stable command names for the local UI/backend bridge.
/// </summary>
public static class KmCommandNames
{
    public const string OpenProject = "project.open";
    public const string ValidateProject = "project.validate";
    public const string RefreshFileGraph = "project.fileGraph.refresh";
    public const string ListWorkflows = "workflow.list";
    public const string LoadItemsWorkflow = "items.load";
    public const string UpdateItemField = "items.field.update";
    public const string LoadPokemonWorkflow = "pokemon.load";
    public const string UpdatePokemonField = "pokemon.field.update";
    public const string UpdatePokemonLearnset = "pokemon.learnset.update";
    public const string UpdatePokemonEvolution = "pokemon.evolution.update";
    public const string LoadMovesWorkflow = "moves.load";
    public const string UpdateMoveField = "moves.field.update";
    public const string LoadTextWorkflow = "text.load";
    public const string UpdateTextEntry = "text.entry.update";
    public const string LoadTrainersWorkflow = "trainers.load";
    public const string UpdateTrainerField = "trainers.field.update";
    public const string LoadGiftPokemonWorkflow = "giftPokemon.load";
    public const string UpdateGiftPokemonField = "giftPokemon.field.update";
    public const string LoadTradePokemonWorkflow = "tradePokemon.load";
    public const string UpdateTradePokemonField = "tradePokemon.field.update";
    public const string LoadStaticEncountersWorkflow = "staticEncounters.load";
    public const string UpdateStaticEncounterField = "staticEncounters.field.update";
    public const string LoadRentalPokemonWorkflow = "rentalPokemon.load";
    public const string UpdateRentalPokemonField = "rentalPokemon.field.update";
    public const string LoadShopsWorkflow = "shops.load";
    public const string UpdateShopInventoryItem = "shops.inventory.update";
    public const string LoadEncountersWorkflow = "encounters.load";
    public const string UpdateEncounterSlotField = "encounters.slot.update";
    public const string LoadRaidRewardsWorkflow = "raidRewards.load";
    public const string UpdateRaidRewardField = "raidRewards.reward.update";
    public const string LoadPlacementWorkflow = "placement.load";
    public const string UpdatePlacementObjectField = "placement.object.update";
    public const string LoadFlagworkSaveWorkflow = "flagworkSave.load";
    public const string LoadExeFsPatchWorkflow = "exefsPatches.load";
    public const string StageExeFsPatch = "exefsPatches.patch.stage";
    public const string LoadRoyalCandyWorkflow = "royalCandy.load";
    public const string StageRoyalCandyWorkflow = "royalCandy.workflow.stage";
    public const string LoadSpreadsheetImportWorkflow = "spreadsheetImport.load";
    public const string PreviewSpreadsheetImport = "spreadsheetImport.preview";
    public const string StartEditSession = "editSession.start";
    public const string GetEditSession = "editSession.get";
    public const string DiscardEditSession = "editSession.discard";
    public const string ValidateEditSession = "editSession.validate";
    public const string CreateChangePlan = "changePlan.create";
    public const string ApplyChangePlan = "changePlan.apply";
}
