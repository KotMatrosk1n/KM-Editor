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
  type LoadExeFsPatchWorkflowRequest,
  type LoadExeFsPatchWorkflowResponse,
  type LoadFlagworkSaveWorkflowRequest,
  type LoadFlagworkSaveWorkflowResponse,
  type LoadItemsWorkflowRequest,
  type LoadItemsWorkflowResponse,
  type LoadPlacementWorkflowRequest,
  type LoadPlacementWorkflowResponse,
  type LoadRaidRewardsWorkflowRequest,
  type LoadRaidRewardsWorkflowResponse,
  type LoadRoyalCandyWorkflowRequest,
  type LoadRoyalCandyWorkflowResponse,
  type StageExeFsPatchRequest,
  type StageExeFsPatchResponse,
  type StageRoyalCandyWorkflowRequest,
  type StageRoyalCandyWorkflowResponse,
  type LoadSpreadsheetImportWorkflowRequest,
  type LoadSpreadsheetImportWorkflowResponse,
  type PreviewSpreadsheetImportRequest,
  type PreviewSpreadsheetImportResponse,
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
  type UpdateEncounterSlotFieldRequest,
  type UpdateEncounterSlotFieldResponse,
  type UpdatePlacementObjectFieldRequest,
  type UpdatePlacementObjectFieldResponse,
  type UpdateRaidRewardFieldRequest,
  type UpdateRaidRewardFieldResponse,
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
  loadExeFsPatchWorkflowResponseSchema,
  loadFlagworkSaveWorkflowResponseSchema,
  loadItemsWorkflowResponseSchema,
  loadPlacementWorkflowResponseSchema,
  loadRaidRewardsWorkflowResponseSchema,
  loadRoyalCandyWorkflowResponseSchema,
  stageExeFsPatchResponseSchema,
  stageRoyalCandyWorkflowResponseSchema,
  loadSpreadsheetImportWorkflowResponseSchema,
  previewSpreadsheetImportResponseSchema,
  loadShopsWorkflowResponseSchema,
  loadTextWorkflowResponseSchema,
  loadTrainersWorkflowResponseSchema,
  openProjectResponseSchema,
  refreshFileGraphResponseSchema,
  startEditSessionResponseSchema,
  updateItemFieldResponseSchema,
  updateEncounterSlotFieldResponseSchema,
  updatePlacementObjectFieldResponseSchema,
  updateRaidRewardFieldResponseSchema,
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
  loadItemsWorkflow: (request: LoadItemsWorkflowRequest) => Promise<LoadItemsWorkflowResponse>;
  loadPlacementWorkflow: (
    request: LoadPlacementWorkflowRequest
  ) => Promise<LoadPlacementWorkflowResponse>;
  loadRaidRewardsWorkflow: (
    request: LoadRaidRewardsWorkflowRequest
  ) => Promise<LoadRaidRewardsWorkflowResponse>;
  loadRoyalCandyWorkflow: (
    request: LoadRoyalCandyWorkflowRequest
  ) => Promise<LoadRoyalCandyWorkflowResponse>;
  stageRoyalCandyWorkflow: (
    request: StageRoyalCandyWorkflowRequest
  ) => Promise<StageRoyalCandyWorkflowResponse>;
  loadSpreadsheetImportWorkflow: (
    request: LoadSpreadsheetImportWorkflowRequest
  ) => Promise<LoadSpreadsheetImportWorkflowResponse>;
  previewSpreadsheetImport: (
    request: PreviewSpreadsheetImportRequest
  ) => Promise<PreviewSpreadsheetImportResponse>;
  loadShopsWorkflow: (request: LoadShopsWorkflowRequest) => Promise<LoadShopsWorkflowResponse>;
  loadTextWorkflow: (request: LoadTextWorkflowRequest) => Promise<LoadTextWorkflowResponse>;
  loadTrainersWorkflow: (
    request: LoadTrainersWorkflowRequest
  ) => Promise<LoadTrainersWorkflowResponse>;
  openProject: (request: OpenProjectRequest) => Promise<OpenProjectResponse>;
  refreshFileGraph: (request: RefreshFileGraphRequest) => Promise<RefreshFileGraphResponse>;
  startEditSession: (request: StartEditSessionRequest) => Promise<StartEditSessionResponse>;
  updateItemField: (request: UpdateItemFieldRequest) => Promise<UpdateItemFieldResponse>;
  updateEncounterSlotField: (
    request: UpdateEncounterSlotFieldRequest
  ) => Promise<UpdateEncounterSlotFieldResponse>;
  updateRaidRewardField: (
    request: UpdateRaidRewardFieldRequest
  ) => Promise<UpdateRaidRewardFieldResponse>;
  updatePlacementObjectField: (
    request: UpdatePlacementObjectFieldRequest
  ) => Promise<UpdatePlacementObjectFieldResponse>;
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
    loadItemsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadItemsWorkflow,
        request,
        loadItemsWorkflowResponseSchema
      ),
    loadPlacementWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadPlacementWorkflow,
        request,
        loadPlacementWorkflowResponseSchema
      ),
    loadRaidRewardsWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadRaidRewardsWorkflow,
        request,
        loadRaidRewardsWorkflowResponseSchema
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
    updateEncounterSlotField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateEncounterSlotField,
        request,
        updateEncounterSlotFieldResponseSchema
      ),
    updateRaidRewardField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRaidRewardField,
        request,
        updateRaidRewardFieldResponseSchema
      ),
    updatePlacementObjectField: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updatePlacementObjectField,
        request,
        updatePlacementObjectFieldResponseSchema
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
