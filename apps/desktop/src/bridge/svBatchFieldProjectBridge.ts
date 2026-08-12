/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  type StageEncounterSlotVanillaRequest,
  type StageEncounterSlotVanillaResponse,
  type StageGiftPokemonVanillaRequest,
  type StageGiftPokemonVanillaResponse,
  type StageItemVanillaRequest,
  type StageItemVanillaResponse,
  type StageMoveVanillaRequest,
  type StageMoveVanillaResponse,
  type UpdateEncounterSlotFieldsRequest,
  type UpdateEncounterSlotFieldsResponse,
  type UpdateGiftPokemonFieldsRequest,
  type UpdateGiftPokemonFieldsResponse,
  type UpdateItemFieldsRequest,
  type UpdateItemFieldsResponse,
  type UpdateMoveFieldsRequest,
  type UpdateMoveFieldsResponse,
  type UpdatePlacementObjectFieldsRequest,
  type UpdatePlacementObjectFieldsResponse,
  type UpdatePokemonFieldsRequest,
  type UpdatePokemonFieldsResponse,
  type UpdateRentalPokemonFieldsRequest,
  type UpdateRentalPokemonFieldsResponse,
  type UpdateTradePokemonFieldsRequest,
  type UpdateTradePokemonFieldsResponse,
  type UpdateTrainerFieldsRequest,
  type UpdateTrainerFieldsResponse,
  stageEncounterSlotVanillaResponseSchema,
  stageGiftPokemonVanillaResponseSchema,
  stageItemVanillaResponseSchema,
  stageMoveVanillaResponseSchema,
  updateEncounterSlotFieldsResponseSchema,
  updateGiftPokemonFieldsResponseSchema,
  updateItemFieldsResponseSchema,
  updateMoveFieldsResponseSchema,
  updatePlacementObjectFieldsResponseSchema,
  updatePokemonFieldsResponseSchema,
  updateRentalPokemonFieldsResponseSchema,
  updateTradePokemonFieldsResponseSchema,
  updateTrainerFieldsResponseSchema
} from './svBatchFieldContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';

export type SvBatchFieldProjectBridgeApi = {
  stageEncounterSlotVanilla: (
    request: StageEncounterSlotVanillaRequest
  ) => Promise<StageEncounterSlotVanillaResponse>;
  stageGiftPokemonVanilla: (
    request: StageGiftPokemonVanillaRequest
  ) => Promise<StageGiftPokemonVanillaResponse>;
  stageItemVanilla: (request: StageItemVanillaRequest) => Promise<StageItemVanillaResponse>;
  stageMoveVanilla: (request: StageMoveVanillaRequest) => Promise<StageMoveVanillaResponse>;
  updateEncounterSlotFields: (
    request: UpdateEncounterSlotFieldsRequest
  ) => Promise<UpdateEncounterSlotFieldsResponse>;
  updateGiftPokemonFields: (
    request: UpdateGiftPokemonFieldsRequest
  ) => Promise<UpdateGiftPokemonFieldsResponse>;
  updateItemFields: (request: UpdateItemFieldsRequest) => Promise<UpdateItemFieldsResponse>;
  updateMoveFields: (request: UpdateMoveFieldsRequest) => Promise<UpdateMoveFieldsResponse>;
  updatePlacementObjectFields: (
    request: UpdatePlacementObjectFieldsRequest
  ) => Promise<UpdatePlacementObjectFieldsResponse>;
  updatePokemonFields: (
    request: UpdatePokemonFieldsRequest
  ) => Promise<UpdatePokemonFieldsResponse>;
  updateRentalPokemonFields: (
    request: UpdateRentalPokemonFieldsRequest
  ) => Promise<UpdateRentalPokemonFieldsResponse>;
  updateTradePokemonFields: (
    request: UpdateTradePokemonFieldsRequest
  ) => Promise<UpdateTradePokemonFieldsResponse>;
  updateTrainerFields: (
    request: UpdateTrainerFieldsRequest
  ) => Promise<UpdateTrainerFieldsResponse>;
};

export function createSvBatchFieldProjectBridgeApi(
  transport: ProjectBridgeTransport
): SvBatchFieldProjectBridgeApi {
  return {
    stageEncounterSlotVanilla: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageEncounterSlotVanilla,
        request,
        stageEncounterSlotVanillaResponseSchema
      ),
    stageGiftPokemonVanilla: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageGiftPokemonVanilla,
        request,
        stageGiftPokemonVanillaResponseSchema
      ),
    stageItemVanilla: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageItemVanilla,
        request,
        stageItemVanillaResponseSchema
      ),
    stageMoveVanilla: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageMoveVanilla,
        request,
        stageMoveVanillaResponseSchema
      ),
    updateEncounterSlotFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateEncounterSlotFields,
        request,
        updateEncounterSlotFieldsResponseSchema
      ),
    updateGiftPokemonFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateGiftPokemonFields,
        request,
        updateGiftPokemonFieldsResponseSchema
      ),
    updateItemFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateItemFields,
        request,
        updateItemFieldsResponseSchema
      ),
    updateMoveFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateMoveFields,
        request,
        updateMoveFieldsResponseSchema
      ),
    updatePlacementObjectFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updatePlacementObjectFields,
        request,
        updatePlacementObjectFieldsResponseSchema
      ),
    updatePokemonFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updatePokemonFields,
        request,
        updatePokemonFieldsResponseSchema
      ),
    updateRentalPokemonFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateRentalPokemonFields,
        request,
        updateRentalPokemonFieldsResponseSchema
      ),
    updateTradePokemonFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateTradePokemonFields,
        request,
        updateTradePokemonFieldsResponseSchema
      ),
    updateTrainerFields: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateTrainerFields,
        request,
        updateTrainerFieldsResponseSchema
      )
  };
}
