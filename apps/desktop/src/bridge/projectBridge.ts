/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke } from '@tauri-apps/api/core';
import {
  type ApplyChangePlanRequest,
  type ApplyChangePlanResponse,
  type CreateChangePlanRequest,
  type CreateChangePlanResponse,
  type ListWorkflowsRequest,
  type ListWorkflowsResponse,
  type LoadEncountersWorkflowRequest,
  type LoadEncountersWorkflowResponse,
  type LoadBagHookWorkflowRequest,
  type LoadBagHookWorkflowResponse,
  type LoadCatchCapWorkflowRequest,
  type LoadCatchCapWorkflowResponse,
  type LoadHyperTrainingWorkflowRequest,
  type LoadHyperTrainingWorkflowResponse,
  type LoadTypeChartWorkflowRequest,
  type LoadTypeChartWorkflowResponse,
  type LoadIvScreenWorkflowRequest,
  type LoadIvScreenWorkflowResponse,
  type LoadExeFsPatchWorkflowRequest,
  type LoadExeFsPatchWorkflowResponse,
  type LoadFlagworkSaveWorkflowRequest,
  type LoadFlagworkSaveWorkflowResponse,
  type LoadGiftPokemonWorkflowRequest,
  type LoadGiftPokemonWorkflowResponse,
  type LoadTradePokemonWorkflowRequest,
  type LoadTradePokemonWorkflowResponse,
  type LoadStaticEncountersWorkflowRequest,
  type LoadStaticEncountersWorkflowResponse,
  type LoadRentalPokemonWorkflowRequest,
  type LoadRentalPokemonWorkflowResponse,
  type LoadDynamaxAdventuresWorkflowRequest,
  type LoadDynamaxAdventuresWorkflowResponse,
  type PreviewDynamaxAdventureDefaultsRequest,
  type PreviewDynamaxAdventureDefaultsResponse,
  type StageDynamaxAdventureRepairRequest,
  type StageDynamaxAdventureRepairResponse,
  type StageDynamaxAdventureRestoreRequest,
  type StageDynamaxAdventureRestoreResponse,
  type LoadItemsWorkflowRequest,
  type LoadItemsWorkflowResponse,
  type LoadMovesWorkflowRequest,
  type LoadMovesWorkflowResponse,
  type LoadPokemonWorkflowRequest,
  type LoadPokemonWorkflowResponse,
  type LoadBehaviorWorkflowRequest,
  type LoadBehaviorWorkflowResponse,
  type LoadRaidBattlesWorkflowRequest,
  type LoadRaidBattlesWorkflowResponse,
  type LoadTeraRaidsWorkflowRequest,
  type LoadTeraRaidsWorkflowResponse,
  type UpdatePokemonEvolutionRequest,
  type UpdatePokemonEvolutionResponse,
  type SwapPokemonDexPlacementRequest,
  type SwapPokemonDexPlacementResponse,
  type UpdatePokemonFieldRequest,
  type UpdatePokemonFieldResponse,
  type UpdatePokemonLearnsetRequest,
  type UpdatePokemonLearnsetResponse,
  type LoadPlacementWorkflowRequest,
  type LoadPlacementWorkflowResponse,
  type LoadRaidRewardsWorkflowRequest,
  type LoadRaidRewardsWorkflowResponse,
  type LoadRaidBonusRewardsWorkflowRequest,
  type LoadRaidBonusRewardsWorkflowResponse,
  type LoadRoyalCandyWorkflowRequest,
  type LoadRoyalCandyWorkflowResponse,
  type LoadStartingItemsWorkflowRequest,
  type LoadStartingItemsWorkflowResponse,
  type StageBagHookInstallRequest,
  type StageBagHookInstallResponse,
  type StageBagHookUninstallRequest,
  type StageBagHookUninstallResponse,
  type StageCatchCapRequest,
  type StageCatchCapResponse,
  type StageCatchCapUninstallRequest,
  type StageCatchCapUninstallResponse,
  type StageHyperTrainingRequest,
  type StageHyperTrainingResponse,
  type StageTypeChartRequest, type StageTypeChartResponse,
  type StageTypeChartUninstallRequest, type StageTypeChartUninstallResponse,
  type StageIvScreenInstallRequest,
  type StageIvScreenInstallResponse,
  type StageIvScreenUninstallRequest,
  type StageIvScreenUninstallResponse,
  type StageExeFsPatchRequest,
  type StageExeFsPatchResponse,
  type StageRoyalCandyWorkflowRequest,
  type StageRoyalCandyWorkflowResponse,
  type StageStartingItemsRequest,
  type StageStartingItemsResponse,
  type LoadSpreadsheetImportWorkflowRequest,
  type LoadSpreadsheetImportWorkflowResponse,
  type PreviewSpreadsheetImportRequest,
  type PreviewSpreadsheetImportResponse,
  type LoadModMergerWorkflowRequest,
  type LoadModMergerWorkflowResponse,
  type StageModMergeRequest,
  type StageModMergeResponse,
  type ApplyModMergeRequest,
  type ApplyModMergeResponse,
  type LoadSvModMergerWorkflowRequest,
  type LoadSvModMergerWorkflowResponse,
  type StageSvModMergeRequest,
  type StageSvModMergeResponse,
  type ApplySvModMergeRequest,
  type ApplySvModMergeResponse,
  type LoadZaModMergerWorkflowRequest,
  type LoadZaModMergerWorkflowResponse,
  type StageZaModMergeRequest,
  type StageZaModMergeResponse,
  type ApplyZaModMergeRequest,
  type ApplyZaModMergeResponse,
  type ImportRandomizerSeedRequest,
  type ImportRandomizerSeedResponse,
  type ApplyRandomizerRequest,
  type ApplyRandomizerResponse,
  type RestoreRandomizerRequest,
  type RestoreRandomizerResponse,
  type LoadShopsWorkflowRequest,
  type LoadShopsWorkflowResponse,
  type LoadTextWorkflowRequest,
  type LoadTextWorkflowResponse,
  type LoadTrainersWorkflowRequest,
  type LoadTrainersWorkflowResponse,
  type OpenProjectRequest,
  type OpenProjectResponse,
  type RefreshFileGraphRequest,
  type RefreshFileGraphResponse,
  type StartEditSessionRequest,
  type StartEditSessionResponse,
  type UpdateItemFieldRequest,
  type UpdateItemFieldResponse,
  type UpdateGiftPokemonFieldRequest,
  type UpdateGiftPokemonFieldResponse,
  type UpdateTradePokemonFieldRequest,
  type UpdateTradePokemonFieldResponse,
  type UpdateStaticEncounterFieldRequest,
  type UpdateStaticEncounterFieldResponse,
  type UpdateStaticEncounterFieldsRequest,
  type UpdateRentalPokemonFieldRequest,
  type UpdateRentalPokemonFieldResponse,
  type UpdateDynamaxAdventureFieldRequest,
  type UpdateDynamaxAdventureFieldResponse,
  type UpdateMoveFieldRequest,
  type UpdateMoveFieldResponse,
  type UpdateBehaviorEntryFieldRequest,
  type UpdateBehaviorEntryFieldResponse,
  type UpdateBehaviorEntryFieldsRequest,
  type UpdateBehaviorEntryFieldsResponse,
  type UpdateRaidBattleSlotFieldRequest,
  type UpdateRaidBattleSlotFieldResponse,
  type UpdateRaidBattleSlotFieldsRequest,
  type UpdateRaidBattleSlotFieldsResponse,
  type UpdateTeraRaidFieldRequest,
  type UpdateTeraRaidFieldResponse,
  type UpdateTeraRaidFieldsRequest,
  type UpdateTeraRaidFieldsResponse,
  type UpdateRaidRewardFieldRequest,
  type UpdateRaidRewardFieldResponse,
  type UpdateRaidRewardFieldsRequest,
  type UpdateRaidRewardFieldsResponse,
  type UpdateRaidBonusRewardFieldRequest,
  type UpdateRaidBonusRewardFieldResponse,
  type UpdateRaidBonusRewardFieldsRequest,
  type UpdateRaidBonusRewardFieldsResponse,
  type UpdateShopInventoryItemRequest,
  type UpdateShopInventoryItemResponse,
  type UpdateTextEntryRequest,
  type UpdateTextEntryResponse,
  type UpdateTrainerFieldRequest,
  type UpdateTrainerFieldResponse,
  type ValidateEditSessionRequest,
  type ValidateEditSessionResponse,
  type ValidateProjectRequest,
  type ValidateProjectResponse,
  applyChangePlanResponseSchema,
  createChangePlanResponseSchema,
  kmCommandNames,
  listWorkflowsResponseSchema,
  loadEncountersWorkflowResponseSchema,
  loadBagHookWorkflowResponseSchema,
  loadCatchCapWorkflowResponseSchema,
  loadHyperTrainingWorkflowResponseSchema,
  loadTypeChartWorkflowResponseSchema,
  loadIvScreenWorkflowResponseSchema,
  loadExeFsPatchWorkflowResponseSchema,
  loadFlagworkSaveWorkflowResponseSchema,
  loadGiftPokemonWorkflowResponseSchema,
  loadTradePokemonWorkflowResponseSchema,
  loadStaticEncountersWorkflowResponseSchema,
  loadRentalPokemonWorkflowResponseSchema,
  loadDynamaxAdventuresWorkflowRequestSchema,
  loadDynamaxAdventuresWorkflowResponseSchema,
  previewDynamaxAdventureDefaultsRequestSchema,
  previewDynamaxAdventureDefaultsResponseSchema,
  stageDynamaxAdventureRepairRequestSchema,
  stageDynamaxAdventureRepairResponseSchema,
  stageDynamaxAdventureRestoreRequestSchema,
  stageDynamaxAdventureRestoreResponseSchema,
  loadItemsWorkflowResponseSchema,
  loadMovesWorkflowResponseSchema,
  loadPokemonWorkflowResponseSchema,
  loadBehaviorWorkflowResponseSchema,
  loadPlacementWorkflowResponseSchema,
  loadRaidBattlesWorkflowResponseSchema,
  loadTeraRaidsWorkflowResponseSchema,
  loadRaidRewardsWorkflowResponseSchema,
  loadRaidBonusRewardsWorkflowResponseSchema,
  loadRoyalCandyWorkflowResponseSchema,
  loadStartingItemsWorkflowResponseSchema,
  stageBagHookInstallResponseSchema,
  stageBagHookUninstallResponseSchema,
  stageCatchCapResponseSchema,
  stageCatchCapUninstallResponseSchema,
  stageHyperTrainingResponseSchema,
  stageTypeChartResponseSchema,
  stageTypeChartUninstallResponseSchema,
  stageIvScreenInstallResponseSchema,
  stageIvScreenUninstallResponseSchema,
  stageExeFsPatchResponseSchema,
  stageRoyalCandyWorkflowResponseSchema,
  stageStartingItemsResponseSchema,
  loadSpreadsheetImportWorkflowResponseSchema,
  previewSpreadsheetImportResponseSchema,
  loadModMergerWorkflowResponseSchema,
  stageModMergeResponseSchema,
  applyModMergeResponseSchema,
  loadSvModMergerWorkflowResponseSchema,
  stageSvModMergeResponseSchema,
  applySvModMergeResponseSchema,
  loadZaModMergerWorkflowResponseSchema,
  stageZaModMergeResponseSchema,
  applyZaModMergeResponseSchema,
  importRandomizerSeedResponseSchema,
  applyRandomizerResponseSchema,
  restoreRandomizerResponseSchema,
  loadShopsWorkflowResponseSchema,
  loadTextWorkflowResponseSchema,
  loadTrainersWorkflowResponseSchema,
  openProjectResponseSchema,
  refreshFileGraphResponseSchema,
  startEditSessionResponseSchema,
  updateItemFieldResponseSchema,
  updateGiftPokemonFieldResponseSchema,
  updateTradePokemonFieldResponseSchema,
  updateStaticEncounterFieldResponseSchema,
  updateRentalPokemonFieldResponseSchema,
  updateDynamaxAdventureFieldRequestSchema,
  updateDynamaxAdventureFieldResponseSchema,
  updateMoveFieldResponseSchema,
  updatePokemonFieldResponseSchema,
  updatePokemonEvolutionResponseSchema,
  swapPokemonDexPlacementResponseSchema,
  updatePokemonLearnsetResponseSchema,
  updateBehaviorEntryFieldResponseSchema,
  updateBehaviorEntryFieldsResponseSchema,
  updateRaidBattleSlotFieldResponseSchema,
  updateRaidBattleSlotFieldsResponseSchema,
  updateTeraRaidFieldResponseSchema,
  updateTeraRaidFieldsResponseSchema,
  updateRaidRewardFieldResponseSchema,
  updateRaidRewardFieldsResponseSchema,
  updateRaidBonusRewardFieldResponseSchema,
  updateRaidBonusRewardFieldsResponseSchema,
  updateShopInventoryItemResponseSchema,
  updateTextEntryResponseSchema,
  updateTrainerFieldResponseSchema,
  validateEditSessionResponseSchema,
  validateProjectResponseSchema
} from './contracts';
import {
  type LoadFashionUnlockWorkflowRequest,
  type LoadFashionUnlockWorkflowResponse,
  type StageFashionUnlockInstallRequest,
  type StageFashionUnlockInstallResponse,
  type StageFashionUnlockUninstallRequest,
  type StageFashionUnlockUninstallResponse,
  loadFashionUnlockWorkflowResponseSchema,
  stageFashionUnlockInstallResponseSchema,
  stageFashionUnlockUninstallResponseSchema
} from './fashionUnlockContracts';
import {
  type LoadGymUniformRemovalWorkflowRequest,
  type LoadGymUniformRemovalWorkflowResponse,
  type StageGymUniformRemovalInstallRequest,
  type StageGymUniformRemovalInstallResponse,
  type StageGymUniformRemovalUninstallRequest,
  type StageGymUniformRemovalUninstallResponse,
  loadGymUniformRemovalWorkflowResponseSchema,
  stageGymUniformRemovalInstallResponseSchema,
  stageGymUniformRemovalUninstallResponseSchema
} from './gymUniformRemovalContracts';
import {
  type ApplyFpsPatchRequest,
  type ApplyFpsPatchResponse,
  type LoadFpsPatchRequest,
  type LoadFpsPatchResponse,
  type RestoreFpsPatchRequest,
  type RestoreFpsPatchResponse,
  applyFpsPatchResponseSchema,
  loadFpsPatchResponseSchema,
  restoreFpsPatchResponseSchema
} from './fpsPatchContracts';
import {
  type ApplyProfanityFilterRequest,
  type ApplyProfanityFilterResponse,
  type LoadProfanityFilterRequest,
  type LoadProfanityFilterResponse,
  type RestoreProfanityFilterRequest,
  type RestoreProfanityFilterResponse,
  applyProfanityFilterResponseSchema,
  loadProfanityFilterResponseSchema,
  restoreProfanityFilterResponseSchema
} from './profanityFilterContracts';
import {
  type LoadNpcItemGiftWorkflowRequest,
  type LoadNpcItemGiftWorkflowResponse,
  type StageNpcItemGiftRequest,
  type StageNpcItemGiftResponse,
  loadNpcItemGiftWorkflowResponseSchema,
  stageNpcItemGiftResponseSchema
} from './npcItemGiftContracts';
import {
  type LoadFairyGymBoostsWorkflowRequest,
  type LoadFairyGymBoostsWorkflowResponse,
  type StageFairyGymBoostsRequest,
  type StageFairyGymBoostsResponse,
  loadFairyGymBoostsWorkflowResponseSchema,
  stageFairyGymBoostsResponseSchema
} from './fairyGymBoostsContracts';
import {
  type LoadAngeFightWorkflowRequest,
  type LoadAngeFightWorkflowResponse,
  type StageAngeFightRequest,
  type StageAngeFightResponse,
  type StageAngeFightUninstallRequest,
  type StageAngeFightUninstallResponse,
  loadAngeFightWorkflowResponseSchema,
  stageAngeFightResponseSchema,
  stageAngeFightUninstallResponseSchema
} from './angeFightContracts';
import {
  type LoadShinyRateWorkflowRequest,
  type LoadShinyRateWorkflowResponse,
  type StageShinyRateRequest,
  type StageShinyRateResponse,
  loadShinyRateWorkflowResponseSchema,
  stageShinyRateResponseSchema
} from './shinyRateContracts';
import {
  type LoadHyperspaceBypassWorkflowRequest,
  type LoadHyperspaceBypassWorkflowResponse,
  type StageHyperspaceBypassInstallRequest,
  type StageHyperspaceBypassInstallResponse,
  type StageHyperspaceBypassUninstallRequest,
  type StageHyperspaceBypassUninstallResponse,
  loadHyperspaceBypassWorkflowResponseSchema,
  stageHyperspaceBypassInstallResponseSchema,
  stageHyperspaceBypassUninstallResponseSchema
} from './hyperspaceBypassContracts';
export { ProjectBridgeError } from './projectBridgeError';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';
import { createSvBatchFieldProjectBridgeApi, type SvBatchFieldProjectBridgeApi } from './svBatchFieldProjectBridge';
import { createSvCacheProjectBridgeApi, type SvCacheProjectBridgeApi } from './svCacheProjectBridge';
import { createZaCacheProjectBridgeApi, type ZaCacheProjectBridgeApi } from './zaCacheProjectBridge';
import { createGameDumpProjectBridgeApi, type GameDumpProjectBridgeApi } from './gameDumpProjectBridge';

export type ProjectBridge = {
  applyChangePlan: (request: ApplyChangePlanRequest) => Promise<ApplyChangePlanResponse>;
  createChangePlan: (request: CreateChangePlanRequest) => Promise<CreateChangePlanResponse>;
  listWorkflows: (request: ListWorkflowsRequest) => Promise<ListWorkflowsResponse>;
  loadEncountersWorkflow: (request: LoadEncountersWorkflowRequest) => Promise<LoadEncountersWorkflowResponse>;
  loadExeFsPatchWorkflow: (
    request: LoadExeFsPatchWorkflowRequest
  ) => Promise<LoadExeFsPatchWorkflowResponse>;
  stageExeFsPatch: (request: StageExeFsPatchRequest) => Promise<StageExeFsPatchResponse>;
  loadFlagworkSaveWorkflow: (
    request: LoadFlagworkSaveWorkflowRequest
  ) => Promise<LoadFlagworkSaveWorkflowResponse>;
  loadGiftPokemonWorkflow: (
    request: LoadGiftPokemonWorkflowRequest
  ) => Promise<LoadGiftPokemonWorkflowResponse>;
  loadTradePokemonWorkflow: (
    request: LoadTradePokemonWorkflowRequest
  ) => Promise<LoadTradePokemonWorkflowResponse>;
  loadStaticEncountersWorkflow: (
    request: LoadStaticEncountersWorkflowRequest
  ) => Promise<LoadStaticEncountersWorkflowResponse>;
  loadRentalPokemonWorkflow: (
    request: LoadRentalPokemonWorkflowRequest
  ) => Promise<LoadRentalPokemonWorkflowResponse>;
  loadDynamaxAdventuresWorkflow: (
    request: LoadDynamaxAdventuresWorkflowRequest
  ) => Promise<LoadDynamaxAdventuresWorkflowResponse>;
  previewDynamaxAdventureDefaults: (
    request: PreviewDynamaxAdventureDefaultsRequest
  ) => Promise<PreviewDynamaxAdventureDefaultsResponse>;
  stageDynamaxAdventureRepair: (
    request: StageDynamaxAdventureRepairRequest
  ) => Promise<StageDynamaxAdventureRepairResponse>;
  stageDynamaxAdventureRestore: (
    request: StageDynamaxAdventureRestoreRequest
  ) => Promise<StageDynamaxAdventureRestoreResponse>;
  loadItemsWorkflow: (request: LoadItemsWorkflowRequest) => Promise<LoadItemsWorkflowResponse>;
  loadMovesWorkflow: (request: LoadMovesWorkflowRequest) => Promise<LoadMovesWorkflowResponse>;
  loadPokemonWorkflow: (
    request: LoadPokemonWorkflowRequest
  ) => Promise<LoadPokemonWorkflowResponse>;
  updatePokemonField: (
    request: UpdatePokemonFieldRequest
  ) => Promise<UpdatePokemonFieldResponse>;
  updatePokemonLearnset: (
    request: UpdatePokemonLearnsetRequest
  ) => Promise<UpdatePokemonLearnsetResponse>;
  updatePokemonEvolution: (
    request: UpdatePokemonEvolutionRequest
  ) => Promise<UpdatePokemonEvolutionResponse>;
  swapPokemonDexPlacement: (
    request: SwapPokemonDexPlacementRequest
  ) => Promise<SwapPokemonDexPlacementResponse>;
  loadPlacementWorkflow: (
    request: LoadPlacementWorkflowRequest
  ) => Promise<LoadPlacementWorkflowResponse>;
  loadBehaviorWorkflow: (
    request: LoadBehaviorWorkflowRequest
  ) => Promise<LoadBehaviorWorkflowResponse>;
  loadRaidBattlesWorkflow: (
    request: LoadRaidBattlesWorkflowRequest
  ) => Promise<LoadRaidBattlesWorkflowResponse>;
  loadTeraRaidsWorkflow: (
    request: LoadTeraRaidsWorkflowRequest
  ) => Promise<LoadTeraRaidsWorkflowResponse>;
  loadRaidRewardsWorkflow: (
    request: LoadRaidRewardsWorkflowRequest
  ) => Promise<LoadRaidRewardsWorkflowResponse>;
  loadRaidBonusRewardsWorkflow: (
    request: LoadRaidBonusRewardsWorkflowRequest
  ) => Promise<LoadRaidBonusRewardsWorkflowResponse>;
  loadBagHookWorkflow: (
    request: LoadBagHookWorkflowRequest
  ) => Promise<LoadBagHookWorkflowResponse>;
  stageBagHookInstall: (
    request: StageBagHookInstallRequest
  ) => Promise<StageBagHookInstallResponse>;
  stageBagHookUninstall: (
    request: StageBagHookUninstallRequest
  ) => Promise<StageBagHookUninstallResponse>;
  loadCatchCapWorkflow: (
    request: LoadCatchCapWorkflowRequest
  ) => Promise<LoadCatchCapWorkflowResponse>;
  stageCatchCap: (request: StageCatchCapRequest) => Promise<StageCatchCapResponse>;
  stageCatchCapUninstall: (
    request: StageCatchCapUninstallRequest
  ) => Promise<StageCatchCapUninstallResponse>;
  loadHyperTrainingWorkflow: (
    request: LoadHyperTrainingWorkflowRequest
  ) => Promise<LoadHyperTrainingWorkflowResponse>;
  stageHyperTraining: (
    request: StageHyperTrainingRequest
  ) => Promise<StageHyperTrainingResponse>;
  loadShinyRateWorkflow: (
    request: LoadShinyRateWorkflowRequest
  ) => Promise<LoadShinyRateWorkflowResponse>;
  stageShinyRate: (
    request: StageShinyRateRequest
  ) => Promise<StageShinyRateResponse>;
  loadTypeChartWorkflow: (
    request: LoadTypeChartWorkflowRequest
  ) => Promise<LoadTypeChartWorkflowResponse>;
  stageTypeChart: (
    request: StageTypeChartRequest
  ) => Promise<StageTypeChartResponse>;
  stageTypeChartUninstall: (request: StageTypeChartUninstallRequest) => Promise<StageTypeChartUninstallResponse>;
  loadAngeFightWorkflow: (
    request: LoadAngeFightWorkflowRequest
  ) => Promise<LoadAngeFightWorkflowResponse>;
  stageAngeFight: (
    request: StageAngeFightRequest
  ) => Promise<StageAngeFightResponse>;
  stageAngeFightUninstall: (
    request: StageAngeFightUninstallRequest
  ) => Promise<StageAngeFightUninstallResponse>;
  loadFairyGymBoostsWorkflow: (
    request: LoadFairyGymBoostsWorkflowRequest
  ) => Promise<LoadFairyGymBoostsWorkflowResponse>;
  stageFairyGymBoosts: (
    request: StageFairyGymBoostsRequest
  ) => Promise<StageFairyGymBoostsResponse>;
  loadFashionUnlockWorkflow: (
    request: LoadFashionUnlockWorkflowRequest
  ) => Promise<LoadFashionUnlockWorkflowResponse>;
  stageFashionUnlockInstall: (
    request: StageFashionUnlockInstallRequest
  ) => Promise<StageFashionUnlockInstallResponse>;
  stageFashionUnlockUninstall: (
    request: StageFashionUnlockUninstallRequest
  ) => Promise<StageFashionUnlockUninstallResponse>;
  loadGymUniformRemovalWorkflow: (
    request: LoadGymUniformRemovalWorkflowRequest
  ) => Promise<LoadGymUniformRemovalWorkflowResponse>;
  stageGymUniformRemovalInstall: (
    request: StageGymUniformRemovalInstallRequest
  ) => Promise<StageGymUniformRemovalInstallResponse>;
  stageGymUniformRemovalUninstall: (
    request: StageGymUniformRemovalUninstallRequest
  ) => Promise<StageGymUniformRemovalUninstallResponse>;
  loadHyperspaceBypassWorkflow: (
    request: LoadHyperspaceBypassWorkflowRequest
  ) => Promise<LoadHyperspaceBypassWorkflowResponse>;
  stageHyperspaceBypassInstall: (
    request: StageHyperspaceBypassInstallRequest
  ) => Promise<StageHyperspaceBypassInstallResponse>;
  stageHyperspaceBypassUninstall: (
    request: StageHyperspaceBypassUninstallRequest
  ) => Promise<StageHyperspaceBypassUninstallResponse>;
  loadIvScreenWorkflow: (
    request: LoadIvScreenWorkflowRequest
  ) => Promise<LoadIvScreenWorkflowResponse>;
  stageIvScreenInstall: (
    request: StageIvScreenInstallRequest
  ) => Promise<StageIvScreenInstallResponse>;
  stageIvScreenUninstall: (
    request: StageIvScreenUninstallRequest
  ) => Promise<StageIvScreenUninstallResponse>;
  loadRoyalCandyWorkflow: (
    request: LoadRoyalCandyWorkflowRequest
  ) => Promise<LoadRoyalCandyWorkflowResponse>;
  stageRoyalCandyWorkflow: (
    request: StageRoyalCandyWorkflowRequest
  ) => Promise<StageRoyalCandyWorkflowResponse>;
  loadStartingItemsWorkflow: (
    request: LoadStartingItemsWorkflowRequest
  ) => Promise<LoadStartingItemsWorkflowResponse>;
  stageStartingItems: (
    request: StageStartingItemsRequest
  ) => Promise<StageStartingItemsResponse>;
  loadNpcItemGiftWorkflow: (
    request: LoadNpcItemGiftWorkflowRequest
  ) => Promise<LoadNpcItemGiftWorkflowResponse>;
  stageNpcItemGift: (
    request: StageNpcItemGiftRequest
  ) => Promise<StageNpcItemGiftResponse>;
  loadSpreadsheetImportWorkflow: (
    request: LoadSpreadsheetImportWorkflowRequest
  ) => Promise<LoadSpreadsheetImportWorkflowResponse>;
  previewSpreadsheetImport: (
    request: PreviewSpreadsheetImportRequest
  ) => Promise<PreviewSpreadsheetImportResponse>;
  loadModMergerWorkflow: (
    request: LoadModMergerWorkflowRequest
  ) => Promise<LoadModMergerWorkflowResponse>;
  stageModMerge: (request: StageModMergeRequest) => Promise<StageModMergeResponse>;
  applyModMerge: (request: ApplyModMergeRequest) => Promise<ApplyModMergeResponse>;
  loadSvModMergerWorkflow: (
    request: LoadSvModMergerWorkflowRequest
  ) => Promise<LoadSvModMergerWorkflowResponse>;
  stageSvModMerge: (
    request: StageSvModMergeRequest
  ) => Promise<StageSvModMergeResponse>;
  applySvModMerge: (
    request: ApplySvModMergeRequest
  ) => Promise<ApplySvModMergeResponse>;
  loadZaModMergerWorkflow: (
    request: LoadZaModMergerWorkflowRequest
  ) => Promise<LoadZaModMergerWorkflowResponse>;
  stageZaModMerge: (
    request: StageZaModMergeRequest
  ) => Promise<StageZaModMergeResponse>;
  applyZaModMerge: (
    request: ApplyZaModMergeRequest
  ) => Promise<ApplyZaModMergeResponse>;
  loadFpsPatch: (request: LoadFpsPatchRequest) => Promise<LoadFpsPatchResponse>;
  applyFpsPatch: (request: ApplyFpsPatchRequest) => Promise<ApplyFpsPatchResponse>;
  restoreFpsPatch: (request: RestoreFpsPatchRequest) => Promise<RestoreFpsPatchResponse>;
  loadProfanityFilter: (request: LoadProfanityFilterRequest) => Promise<LoadProfanityFilterResponse>;
  applyProfanityFilter: (request: ApplyProfanityFilterRequest) => Promise<ApplyProfanityFilterResponse>;
  restoreProfanityFilter: (
    request: RestoreProfanityFilterRequest
  ) => Promise<RestoreProfanityFilterResponse>;
  importRandomizerSeed: (
    request: ImportRandomizerSeedRequest
  ) => Promise<ImportRandomizerSeedResponse>;
  applyRandomizer: (request: ApplyRandomizerRequest) => Promise<ApplyRandomizerResponse>;
  restoreRandomizer: (request: RestoreRandomizerRequest) => Promise<RestoreRandomizerResponse>;
  loadShopsWorkflow: (request: LoadShopsWorkflowRequest) => Promise<LoadShopsWorkflowResponse>;
  loadTextWorkflow: (request: LoadTextWorkflowRequest) => Promise<LoadTextWorkflowResponse>;
  loadTrainersWorkflow: (
    request: LoadTrainersWorkflowRequest
  ) => Promise<LoadTrainersWorkflowResponse>;
  openProject: (request: OpenProjectRequest) => Promise<OpenProjectResponse>;
  refreshFileGraph: (request: RefreshFileGraphRequest) => Promise<RefreshFileGraphResponse>;
  startEditSession: (request: StartEditSessionRequest) => Promise<StartEditSessionResponse>;
  updateItemField: (request: UpdateItemFieldRequest) => Promise<UpdateItemFieldResponse>;
  updateGiftPokemonField: (
    request: UpdateGiftPokemonFieldRequest
  ) => Promise<UpdateGiftPokemonFieldResponse>;
  updateTradePokemonField: (
    request: UpdateTradePokemonFieldRequest
  ) => Promise<UpdateTradePokemonFieldResponse>;
  updateStaticEncounterField: (
    request: UpdateStaticEncounterFieldRequest
  ) => Promise<UpdateStaticEncounterFieldResponse>;
  updateStaticEncounterFields: (
    request: UpdateStaticEncounterFieldsRequest
  ) => Promise<UpdateStaticEncounterFieldResponse>;
  updateRentalPokemonField: (
    request: UpdateRentalPokemonFieldRequest
  ) => Promise<UpdateRentalPokemonFieldResponse>;
  updateDynamaxAdventureField: (
    request: UpdateDynamaxAdventureFieldRequest
  ) => Promise<UpdateDynamaxAdventureFieldResponse>;
  updateMoveField: (request: UpdateMoveFieldRequest) => Promise<UpdateMoveFieldResponse>;
  updateRaidBattleSlotField: (
    request: UpdateRaidBattleSlotFieldRequest
  ) => Promise<UpdateRaidBattleSlotFieldResponse>;
  updateRaidBattleSlotFields: (
    request: UpdateRaidBattleSlotFieldsRequest
  ) => Promise<UpdateRaidBattleSlotFieldsResponse>;
  updateTeraRaidField: (
    request: UpdateTeraRaidFieldRequest
  ) => Promise<UpdateTeraRaidFieldResponse>;
  updateTeraRaidFields: (
    request: UpdateTeraRaidFieldsRequest
  ) => Promise<UpdateTeraRaidFieldsResponse>;
  updateRaidRewardField: (
    request: UpdateRaidRewardFieldRequest
  ) => Promise<UpdateRaidRewardFieldResponse>;
  updateRaidRewardFields: (
    request: UpdateRaidRewardFieldsRequest
  ) => Promise<UpdateRaidRewardFieldsResponse>;
  updateRaidBonusRewardField: (
    request: UpdateRaidBonusRewardFieldRequest
  ) => Promise<UpdateRaidBonusRewardFieldResponse>;
  updateRaidBonusRewardFields: (
    request: UpdateRaidBonusRewardFieldsRequest
  ) => Promise<UpdateRaidBonusRewardFieldsResponse>;
  updateBehaviorEntryField: (
    request: UpdateBehaviorEntryFieldRequest
  ) => Promise<UpdateBehaviorEntryFieldResponse>;
  updateBehaviorEntryFields: (
    request: UpdateBehaviorEntryFieldsRequest
  ) => Promise<UpdateBehaviorEntryFieldsResponse>;
  updateShopInventoryItem: (
    request: UpdateShopInventoryItemRequest
  ) => Promise<UpdateShopInventoryItemResponse>;
  updateTextEntry: (request: UpdateTextEntryRequest) => Promise<UpdateTextEntryResponse>;
  updateTrainerField: (
    request: UpdateTrainerFieldRequest
  ) => Promise<UpdateTrainerFieldResponse>;
  validateEditSession: (
    request: ValidateEditSessionRequest
  ) => Promise<ValidateEditSessionResponse>;
  validateProject: (request: ValidateProjectRequest) => Promise<ValidateProjectResponse>;
} & SvBatchFieldProjectBridgeApi & SvCacheProjectBridgeApi & ZaCacheProjectBridgeApi & GameDumpProjectBridgeApi;

const tauriProjectBridgeTransport: ProjectBridgeTransport = (requestJson) => {
  if (!hasTauriRuntime()) {
    return Promise.reject(new Error('Project bridge is only available in the desktop app.'));
  }

  return invoke<string>('project_bridge', { requestJson });
};

function validateDynamaxAdventureResponseGame<
  TResponse extends { workflow: { detectedGame: 'sword' | 'shield' | null } }
>(selectedGame: string | null, response: TResponse) {
  if (
    response.workflow.detectedGame !== null &&
    response.workflow.detectedGame !== selectedGame
  ) {
    throw new Error(
      `Dynamax Adventures response detected ${response.workflow.detectedGame}, but the request selected ${selectedGame}.`
    );
  }

  return response;
}

export function createProjectBridge(
  transport: ProjectBridgeTransport = tauriProjectBridgeTransport
): ProjectBridge {
  return {
    applyChangePlan: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyChangePlan,
        request,
        applyChangePlanResponseSchema
      ),
    createChangePlan: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.createChangePlan,
        request,
        createChangePlanResponseSchema
      ),
    listWorkflows: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.listWorkflows,
        request,
        listWorkflowsResponseSchema
      ),
    loadEncountersWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadEncountersWorkflow,
        request,
        loadEncountersWorkflowResponseSchema
      ),
    loadBagHookWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadBagHookWorkflow,
        request,
        loadBagHookWorkflowResponseSchema
      ),
    stageBagHookInstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageBagHookInstall,
        request,
        stageBagHookInstallResponseSchema
      ),
    stageBagHookUninstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageBagHookUninstall,
        request,
        stageBagHookUninstallResponseSchema
      ),
    loadCatchCapWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadCatchCapWorkflow,
        request,
        loadCatchCapWorkflowResponseSchema
      ),
    stageCatchCap: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageCatchCap,
        request,
        stageCatchCapResponseSchema
      ),
    stageCatchCapUninstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageCatchCapUninstall,
        request,
        stageCatchCapUninstallResponseSchema
      ),
    loadHyperTrainingWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadHyperTrainingWorkflow,
        request,
        loadHyperTrainingWorkflowResponseSchema
      ),
    stageHyperTraining: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageHyperTraining,
        request,
        stageHyperTrainingResponseSchema
      ),
    loadShinyRateWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadShinyRateWorkflow,
        request,
        loadShinyRateWorkflowResponseSchema
      ),
    stageShinyRate: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageShinyRate,
        request,
        stageShinyRateResponseSchema
      ),
    loadTypeChartWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadTypeChartWorkflow,
        request,
        loadTypeChartWorkflowResponseSchema
      ),
    stageTypeChart: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageTypeChart,
        request,
        stageTypeChartResponseSchema
      ),
    stageTypeChartUninstall: (request) => sendProjectBridgeRequest(transport, kmCommandNames.stageTypeChartUninstall, request, stageTypeChartUninstallResponseSchema),
    loadAngeFightWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadAngeFightWorkflow,
        request,
        loadAngeFightWorkflowResponseSchema
      ),
    stageAngeFight: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageAngeFight,
        request,
        stageAngeFightResponseSchema
      ),
    stageAngeFightUninstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageAngeFightUninstall,
        request,
        stageAngeFightUninstallResponseSchema
      ),
    loadFairyGymBoostsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadFairyGymBoostsWorkflow,
        request,
        loadFairyGymBoostsWorkflowResponseSchema
      ),
    stageFairyGymBoosts: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageFairyGymBoosts,
        request,
        stageFairyGymBoostsResponseSchema
      ),
    loadFashionUnlockWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadFashionUnlockWorkflow,
        request,
        loadFashionUnlockWorkflowResponseSchema
      ),
    stageFashionUnlockInstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageFashionUnlockInstall,
        request,
        stageFashionUnlockInstallResponseSchema
      ),
    stageFashionUnlockUninstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageFashionUnlockUninstall,
        request,
        stageFashionUnlockUninstallResponseSchema
      ),
    loadGymUniformRemovalWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadGymUniformRemovalWorkflow,
        request,
        loadGymUniformRemovalWorkflowResponseSchema
      ),
    stageGymUniformRemovalInstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageGymUniformRemovalInstall,
        request,
        stageGymUniformRemovalInstallResponseSchema
      ),
    stageGymUniformRemovalUninstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageGymUniformRemovalUninstall,
        request,
        stageGymUniformRemovalUninstallResponseSchema
      ),
    loadHyperspaceBypassWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadHyperspaceBypassWorkflow,
        request,
        loadHyperspaceBypassWorkflowResponseSchema
      ),
    stageHyperspaceBypassInstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageHyperspaceBypassInstall,
        request,
        stageHyperspaceBypassInstallResponseSchema
      ),
    stageHyperspaceBypassUninstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageHyperspaceBypassUninstall,
        request,
        stageHyperspaceBypassUninstallResponseSchema
      ),
    loadIvScreenWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadIvScreenWorkflow,
        request,
        loadIvScreenWorkflowResponseSchema
      ),
    stageIvScreenInstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageIvScreenInstall,
        request,
        stageIvScreenInstallResponseSchema
      ),
    stageIvScreenUninstall: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageIvScreenUninstall,
        request,
        stageIvScreenUninstallResponseSchema
      ),
    loadExeFsPatchWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadExeFsPatchWorkflow,
        request,
        loadExeFsPatchWorkflowResponseSchema
      ),
    stageExeFsPatch: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageExeFsPatch,
        request,
        stageExeFsPatchResponseSchema
      ),
    loadFlagworkSaveWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadFlagworkSaveWorkflow,
        request,
        loadFlagworkSaveWorkflowResponseSchema
      ),
    loadGiftPokemonWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadGiftPokemonWorkflow,
        request,
        loadGiftPokemonWorkflowResponseSchema
      ),
    loadTradePokemonWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadTradePokemonWorkflow,
        request,
        loadTradePokemonWorkflowResponseSchema
      ),
    loadStaticEncountersWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadStaticEncountersWorkflow,
        request,
        loadStaticEncountersWorkflowResponseSchema
      ),
    loadRentalPokemonWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadRentalPokemonWorkflow,
        request,
        loadRentalPokemonWorkflowResponseSchema
      ),
    loadDynamaxAdventuresWorkflow: async (request) => {
      const validatedRequest = loadDynamaxAdventuresWorkflowRequestSchema.parse(request);
      const response = await sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadDynamaxAdventuresWorkflow,
        validatedRequest,
        loadDynamaxAdventuresWorkflowResponseSchema
      );
      return validateDynamaxAdventureResponseGame(
        validatedRequest.paths.selectedGame,
        response
      );
    },
    previewDynamaxAdventureDefaults: async (request) => {
      const validatedRequest = previewDynamaxAdventureDefaultsRequestSchema.parse(request);
      return await sendProjectBridgeRequest(
        transport,
        kmCommandNames.previewDynamaxAdventureDefaults,
        validatedRequest,
        previewDynamaxAdventureDefaultsResponseSchema
      );
    },
    stageDynamaxAdventureRepair: async (request) => {
      const validatedRequest = stageDynamaxAdventureRepairRequestSchema.parse(request);
      const response = await sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageDynamaxAdventureRepair,
        validatedRequest,
        stageDynamaxAdventureRepairResponseSchema
      );
      return validateDynamaxAdventureResponseGame(
        validatedRequest.paths.selectedGame,
        response
      );
    },
    stageDynamaxAdventureRestore: async (request) => {
      const validatedRequest = stageDynamaxAdventureRestoreRequestSchema.parse(request);
      const response = await sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageDynamaxAdventureRestore,
        validatedRequest,
        stageDynamaxAdventureRestoreResponseSchema
      );
      return validateDynamaxAdventureResponseGame(
        validatedRequest.paths.selectedGame,
        response
      );
    },
    loadItemsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadItemsWorkflow,
        request,
        loadItemsWorkflowResponseSchema
      ),
    loadMovesWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadMovesWorkflow,
        request,
        loadMovesWorkflowResponseSchema
      ),
    loadPokemonWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadPokemonWorkflow,
        request,
        loadPokemonWorkflowResponseSchema
      ),
    loadPlacementWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadPlacementWorkflow,
        request,
        loadPlacementWorkflowResponseSchema
      ),
    loadBehaviorWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadBehaviorWorkflow,
        request,
        loadBehaviorWorkflowResponseSchema
      ),
    loadRaidBattlesWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadRaidBattlesWorkflow,
        request,
        loadRaidBattlesWorkflowResponseSchema
      ),
    loadTeraRaidsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadTeraRaidsWorkflow,
        request,
        loadTeraRaidsWorkflowResponseSchema
      ),
    loadRaidRewardsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadRaidRewardsWorkflow,
        request,
        loadRaidRewardsWorkflowResponseSchema
      ),
    loadRaidBonusRewardsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadRaidBonusRewardsWorkflow,
        request,
        loadRaidBonusRewardsWorkflowResponseSchema
      ),
    loadRoyalCandyWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadRoyalCandyWorkflow,
        request,
        loadRoyalCandyWorkflowResponseSchema
      ),
    stageRoyalCandyWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageRoyalCandyWorkflow,
        request,
        stageRoyalCandyWorkflowResponseSchema
      ),
    loadStartingItemsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadStartingItemsWorkflow,
        request,
        loadStartingItemsWorkflowResponseSchema
      ),
    stageStartingItems: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageStartingItems,
        request,
        stageStartingItemsResponseSchema
      ),
    loadNpcItemGiftWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadNpcItemGiftWorkflow,
        request,
        loadNpcItemGiftWorkflowResponseSchema
      ),
    stageNpcItemGift: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageNpcItemGift,
        request,
        stageNpcItemGiftResponseSchema
      ),
    loadSpreadsheetImportWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadSpreadsheetImportWorkflow,
        request,
        loadSpreadsheetImportWorkflowResponseSchema
      ),
    previewSpreadsheetImport: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.previewSpreadsheetImport,
        request,
        previewSpreadsheetImportResponseSchema
      ),
    loadModMergerWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadModMergerWorkflow,
        request,
        loadModMergerWorkflowResponseSchema
      ),
    stageModMerge: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageModMerge,
        request,
        stageModMergeResponseSchema
      ),
    applyModMerge: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyModMerge,
        request,
        applyModMergeResponseSchema
      ),
    loadSvModMergerWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadSvModMergerWorkflow,
        request,
        loadSvModMergerWorkflowResponseSchema
      ),
    stageSvModMerge: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageSvModMerge,
        request,
        stageSvModMergeResponseSchema
      ),
    applySvModMerge: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applySvModMerge,
        request,
        applySvModMergeResponseSchema
      ),
    loadZaModMergerWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadZaModMergerWorkflow,
        request,
        loadZaModMergerWorkflowResponseSchema
      ),
    stageZaModMerge: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageZaModMerge,
        request,
        stageZaModMergeResponseSchema
      ),
    applyZaModMerge: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyZaModMerge,
        request,
        applyZaModMergeResponseSchema
      ),
    loadFpsPatch: (request) =>
      sendProjectBridgeRequest(transport, kmCommandNames.loadFpsPatch, request, loadFpsPatchResponseSchema),
    applyFpsPatch: (request) =>
      sendProjectBridgeRequest(transport, kmCommandNames.applyFpsPatch, request, applyFpsPatchResponseSchema),
    restoreFpsPatch: (request) =>
      sendProjectBridgeRequest(transport, kmCommandNames.restoreFpsPatch, request, restoreFpsPatchResponseSchema),
    loadProfanityFilter: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadProfanityFilter,
        request,
        loadProfanityFilterResponseSchema
      ),
    applyProfanityFilter: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyProfanityFilter,
        request,
        applyProfanityFilterResponseSchema
      ),
    restoreProfanityFilter: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.restoreProfanityFilter,
        request,
        restoreProfanityFilterResponseSchema
      ),
    importRandomizerSeed: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.importRandomizerSeed,
        request,
        importRandomizerSeedResponseSchema
      ),
    applyRandomizer: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyRandomizer,
        request,
        applyRandomizerResponseSchema
      ),
    restoreRandomizer: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.restoreRandomizer,
        request,
        restoreRandomizerResponseSchema
      ),
    loadShopsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadShopsWorkflow,
        request,
        loadShopsWorkflowResponseSchema
      ),
    loadTextWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadTextWorkflow,
        request,
        loadTextWorkflowResponseSchema
      ),
    loadTrainersWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadTrainersWorkflow,
        request,
        loadTrainersWorkflowResponseSchema
      ),
    openProject: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.openProject,
        request,
        openProjectResponseSchema
      ),
    refreshFileGraph: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.refreshFileGraph,
        request,
        refreshFileGraphResponseSchema
      ),
    startEditSession: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.startEditSession,
        request,
        startEditSessionResponseSchema
      ),
    updateItemField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateItemField,
        request,
        updateItemFieldResponseSchema
      ),
    updateGiftPokemonField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateGiftPokemonField,
        request,
        updateGiftPokemonFieldResponseSchema
      ),
    updateTradePokemonField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateTradePokemonField,
        request,
        updateTradePokemonFieldResponseSchema
      ),
    updateStaticEncounterField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateStaticEncounterField,
        request,
        updateStaticEncounterFieldResponseSchema
      ),
    updateStaticEncounterFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateStaticEncounterFields,
        request,
        updateStaticEncounterFieldResponseSchema
      ),
    updateRentalPokemonField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRentalPokemonField,
        request,
        updateRentalPokemonFieldResponseSchema
      ),
    updateDynamaxAdventureField: async (request) => {
      const validatedRequest = updateDynamaxAdventureFieldRequestSchema.parse(request);
      const response = await sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateDynamaxAdventureField,
        validatedRequest,
        updateDynamaxAdventureFieldResponseSchema
      );
      return validateDynamaxAdventureResponseGame(
        validatedRequest.paths.selectedGame,
        response
      );
    },
    updateMoveField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateMoveField,
        request,
        updateMoveFieldResponseSchema
      ),
    updatePokemonField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updatePokemonField,
        request,
        updatePokemonFieldResponseSchema
      ),
    updatePokemonLearnset: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updatePokemonLearnset,
        request,
        updatePokemonLearnsetResponseSchema
      ),
    updatePokemonEvolution: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updatePokemonEvolution,
        request,
        updatePokemonEvolutionResponseSchema
      ),
    swapPokemonDexPlacement: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.swapPokemonDexPlacement,
        request,
        swapPokemonDexPlacementResponseSchema
      ),
    updateRaidBattleSlotField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidBattleSlotField,
        request,
        updateRaidBattleSlotFieldResponseSchema
      ),
    updateRaidBattleSlotFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidBattleSlotFields,
        request,
        updateRaidBattleSlotFieldsResponseSchema
      ),
    updateTeraRaidField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateTeraRaidField,
        request,
        updateTeraRaidFieldResponseSchema
      ),
    updateTeraRaidFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateTeraRaidFields,
        request,
        updateTeraRaidFieldsResponseSchema
      ),
    updateRaidRewardField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidRewardField,
        request,
        updateRaidRewardFieldResponseSchema
      ),
    updateRaidRewardFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidRewardFields,
        request,
        updateRaidRewardFieldsResponseSchema
      ),
    updateRaidBonusRewardField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidBonusRewardField,
        request,
        updateRaidBonusRewardFieldResponseSchema
      ),
    updateRaidBonusRewardFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidBonusRewardFields,
        request,
        updateRaidBonusRewardFieldsResponseSchema
      ),
    updateBehaviorEntryField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateBehaviorEntryField,
        request,
        updateBehaviorEntryFieldResponseSchema
      ),
    updateBehaviorEntryFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateBehaviorEntryFields,
        request,
        updateBehaviorEntryFieldsResponseSchema
      ),
    updateShopInventoryItem: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateShopInventoryItem,
        request,
        updateShopInventoryItemResponseSchema
      ),
    updateTextEntry: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateTextEntry,
        request,
        updateTextEntryResponseSchema
      ),
    updateTrainerField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateTrainerField,
        request,
        updateTrainerFieldResponseSchema
      ),
    ...createGameDumpProjectBridgeApi(transport),
    ...createSvBatchFieldProjectBridgeApi(transport),
    ...createSvCacheProjectBridgeApi(transport),
    ...createZaCacheProjectBridgeApi(transport),
    validateEditSession: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.validateEditSession,
        request,
        validateEditSessionResponseSchema
      ),
    validateProject: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.validateProject,
        request,
        validateProjectResponseSchema
      )
  };
}

export const projectBridge = createProjectBridge();

function hasTauriRuntime() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}
