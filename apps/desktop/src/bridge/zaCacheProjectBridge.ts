/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';
import {
  type ClearZaCacheRequest,
  type GetZaCacheStatusRequest,
  type ZaCacheStatusResponse,
  type UpdateZaCacheSettingsRequest,
  type WarmupZaCacheStepRequest,
  zaCacheStatusResponseSchema
} from './zaCacheContracts';

export type ZaCacheProjectBridgeApi = {
  clearZaCache: (request: ClearZaCacheRequest) => Promise<ZaCacheStatusResponse>;
  getZaCacheStatus: (request: GetZaCacheStatusRequest) => Promise<ZaCacheStatusResponse>;
  updateZaCacheSettings: (
    request: UpdateZaCacheSettingsRequest
  ) => Promise<ZaCacheStatusResponse>;
  warmupZaCacheStep: (request: WarmupZaCacheStepRequest) => Promise<ZaCacheStatusResponse>;
};

export function createZaCacheProjectBridgeApi(
  transport: ProjectBridgeTransport
): ZaCacheProjectBridgeApi {
  return {
    clearZaCache: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.clearZaCache,
        request,
        zaCacheStatusResponseSchema
      ),
    getZaCacheStatus: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.getZaCacheStatus,
        request,
        zaCacheStatusResponseSchema
      ),
    updateZaCacheSettings: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateZaCacheSettings,
        request,
        zaCacheStatusResponseSchema
      ),
    warmupZaCacheStep: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.warmupZaCacheStep,
        request,
        zaCacheStatusResponseSchema
      )
  };
}
