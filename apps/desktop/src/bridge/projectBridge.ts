/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke } from '@tauri-apps/api/core';
import { z, type ZodTypeAny } from 'zod';
import {
  type ApiError,
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
  type LoadGymUniformRemovalWorkflowRequest,
  type LoadGymUniformRemovalWorkflowResponse,
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
  type UpdatePokemonEvolutionRequest,
  type UpdatePokemonEvolutionResponse,
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
  type StageTypeChartRequest,
  type StageTypeChartResponse,
  type StageGymUniformRemovalInstallRequest,
  type StageGymUniformRemovalInstallResponse,
  type StageGymUniformRemovalUninstallRequest,
  type StageGymUniformRemovalUninstallResponse,
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
  type KmCommandName,
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
  type UpdateRentalPokemonFieldRequest,
  type UpdateRentalPokemonFieldResponse,
  type UpdateDynamaxAdventureFieldRequest,
  type UpdateDynamaxAdventureFieldResponse,
  type UpdateMoveFieldRequest,
  type UpdateMoveFieldResponse,
  type UpdateEncounterSlotFieldRequest,
  type UpdateEncounterSlotFieldResponse,
  type UpdatePlacementObjectFieldRequest,
  type UpdatePlacementObjectFieldResponse,
  type UpdateBehaviorEntryFieldRequest,
  type UpdateBehaviorEntryFieldResponse,
  type UpdateRaidBattleSlotFieldRequest,
  type UpdateRaidBattleSlotFieldResponse,
  type UpdateRaidRewardFieldRequest,
  type UpdateRaidRewardFieldResponse,
  type UpdateRaidBonusRewardFieldRequest,
  type UpdateRaidBonusRewardFieldResponse,
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
  createBridgeResponseSchema,
  createChangePlanResponseSchema,
  kmCommandNames,
  listWorkflowsResponseSchema,
  loadEncountersWorkflowResponseSchema,
  loadBagHookWorkflowResponseSchema,
  loadCatchCapWorkflowResponseSchema,
  loadHyperTrainingWorkflowResponseSchema,
  loadTypeChartWorkflowResponseSchema,
  loadGymUniformRemovalWorkflowResponseSchema,
  loadIvScreenWorkflowResponseSchema,
  loadExeFsPatchWorkflowResponseSchema,
  loadFlagworkSaveWorkflowResponseSchema,
  loadGiftPokemonWorkflowResponseSchema,
  loadTradePokemonWorkflowResponseSchema,
  loadStaticEncountersWorkflowResponseSchema,
  loadRentalPokemonWorkflowResponseSchema,
  loadDynamaxAdventuresWorkflowResponseSchema,
  loadItemsWorkflowResponseSchema,
  loadMovesWorkflowResponseSchema,
  loadPokemonWorkflowResponseSchema,
  loadBehaviorWorkflowResponseSchema,
  loadPlacementWorkflowResponseSchema,
  loadRaidBattlesWorkflowResponseSchema,
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
  stageGymUniformRemovalInstallResponseSchema,
  stageGymUniformRemovalUninstallResponseSchema,
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
  updateDynamaxAdventureFieldResponseSchema,
  updateMoveFieldResponseSchema,
  updatePokemonFieldResponseSchema,
  updatePokemonEvolutionResponseSchema,
  updatePokemonLearnsetResponseSchema,
  updateEncounterSlotFieldResponseSchema,
  updatePlacementObjectFieldResponseSchema,
  updateBehaviorEntryFieldResponseSchema,
  updateRaidBattleSlotFieldResponseSchema,
  updateRaidRewardFieldResponseSchema,
  updateRaidBonusRewardFieldResponseSchema,
  updateShopInventoryItemResponseSchema,
  updateTextEntryResponseSchema,
  updateTrainerFieldResponseSchema,
  validateEditSessionResponseSchema,
  validateProjectResponseSchema
} from './contracts';

export type ProjectBridge = {
  applyChangePlan: (request: ApplyChangePlanRequest) => Promise<ApplyChangePlanResponse>;
  createChangePlan: (request: CreateChangePlanRequest) => Promise<CreateChangePlanResponse>;
  listWorkflows: (request: ListWorkflowsRequest) => Promise<ListWorkflowsResponse>;
  loadEncountersWorkflow: (
    request: LoadEncountersWorkflowRequest
  ) => Promise<LoadEncountersWorkflowResponse>;
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
  loadPlacementWorkflow: (
    request: LoadPlacementWorkflowRequest
  ) => Promise<LoadPlacementWorkflowResponse>;
  loadBehaviorWorkflow: (
    request: LoadBehaviorWorkflowRequest
  ) => Promise<LoadBehaviorWorkflowResponse>;
  loadRaidBattlesWorkflow: (
    request: LoadRaidBattlesWorkflowRequest
  ) => Promise<LoadRaidBattlesWorkflowResponse>;
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
  loadTypeChartWorkflow: (
    request: LoadTypeChartWorkflowRequest
  ) => Promise<LoadTypeChartWorkflowResponse>;
  stageTypeChart: (
    request: StageTypeChartRequest
  ) => Promise<StageTypeChartResponse>;
  loadGymUniformRemovalWorkflow: (
    request: LoadGymUniformRemovalWorkflowRequest
  ) => Promise<LoadGymUniformRemovalWorkflowResponse>;
  stageGymUniformRemovalInstall: (
    request: StageGymUniformRemovalInstallRequest
  ) => Promise<StageGymUniformRemovalInstallResponse>;
  stageGymUniformRemovalUninstall: (
    request: StageGymUniformRemovalUninstallRequest
  ) => Promise<StageGymUniformRemovalUninstallResponse>;
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
  updateRentalPokemonField: (
    request: UpdateRentalPokemonFieldRequest
  ) => Promise<UpdateRentalPokemonFieldResponse>;
  updateDynamaxAdventureField: (
    request: UpdateDynamaxAdventureFieldRequest
  ) => Promise<UpdateDynamaxAdventureFieldResponse>;
  updateMoveField: (request: UpdateMoveFieldRequest) => Promise<UpdateMoveFieldResponse>;
  updateEncounterSlotField: (
    request: UpdateEncounterSlotFieldRequest
  ) => Promise<UpdateEncounterSlotFieldResponse>;
  updateRaidBattleSlotField: (
    request: UpdateRaidBattleSlotFieldRequest
  ) => Promise<UpdateRaidBattleSlotFieldResponse>;
  updateRaidRewardField: (
    request: UpdateRaidRewardFieldRequest
  ) => Promise<UpdateRaidRewardFieldResponse>;
  updateRaidBonusRewardField: (
    request: UpdateRaidBonusRewardFieldRequest
  ) => Promise<UpdateRaidBonusRewardFieldResponse>;
  updatePlacementObjectField: (
    request: UpdatePlacementObjectFieldRequest
  ) => Promise<UpdatePlacementObjectFieldResponse>;
  updateBehaviorEntryField: (
    request: UpdateBehaviorEntryFieldRequest
  ) => Promise<UpdateBehaviorEntryFieldResponse>;
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
};

export type ProjectBridgeTransport = (requestJson: string) => Promise<string>;

export class ProjectBridgeError extends Error {
  public readonly apiError: ApiError;

  public constructor(apiError: ApiError) {
    super(apiError.message);
    this.name = 'ProjectBridgeError';
    this.apiError = apiError;
  }
}

const tauriProjectBridgeTransport: ProjectBridgeTransport = (requestJson) => {
  if (!hasTauriRuntime()) {
    return Promise.reject(new Error('Project bridge is only available in the desktop app.'));
  }

  return invoke<string>('project_bridge_once', { requestJson });
};

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
    loadDynamaxAdventuresWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadDynamaxAdventuresWorkflow,
        request,
        loadDynamaxAdventuresWorkflowResponseSchema
      ),
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
    updateRentalPokemonField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRentalPokemonField,
        request,
        updateRentalPokemonFieldResponseSchema
      ),
    updateDynamaxAdventureField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateDynamaxAdventureField,
        request,
        updateDynamaxAdventureFieldResponseSchema
      ),
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
    updateEncounterSlotField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateEncounterSlotField,
        request,
        updateEncounterSlotFieldResponseSchema
      ),
    updateRaidBattleSlotField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidBattleSlotField,
        request,
        updateRaidBattleSlotFieldResponseSchema
      ),
    updateRaidRewardField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidRewardField,
        request,
        updateRaidRewardFieldResponseSchema
      ),
    updateRaidBonusRewardField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidBonusRewardField,
        request,
        updateRaidBonusRewardFieldResponseSchema
      ),
    updatePlacementObjectField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updatePlacementObjectField,
        request,
        updatePlacementObjectFieldResponseSchema
      ),
    updateBehaviorEntryField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateBehaviorEntryField,
        request,
        updateBehaviorEntryFieldResponseSchema
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

async function sendProjectBridgeRequest<TPayloadSchema extends ZodTypeAny>(
  transport: ProjectBridgeTransport,
  command: KmCommandName,
  payload: unknown,
  payloadSchema: TPayloadSchema
): Promise<z.infer<TPayloadSchema>> {
  const requestId = createRequestId(command);
  const responseJson = await transport(
    JSON.stringify({
      command,
      payload,
      requestId
    })
  );
  const response = createBridgeResponseSchema(payloadSchema).parse(JSON.parse(responseJson));

  if (response.error) {
    throw new ProjectBridgeError(response.error);
  }

  if (response.payload === null || response.payload === undefined) {
    throw new Error('Project bridge response did not include a payload.');
  }

  return response.payload;
}

function createRequestId(command: KmCommandName) {
  const randomValue = globalThis.crypto?.randomUUID?.() ?? Math.random().toString(36).slice(2);

  return `${command}:${randomValue}`;
}

function hasTauriRuntime() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}
