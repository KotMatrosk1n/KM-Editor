/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';
import {
  type ClearSvCacheRequest,
  type GetSvCacheStatusRequest,
  type SvCacheStatusResponse,
  type UpdateSvCacheSettingsRequest,
  type WarmupSvCacheStepRequest,
  svCacheStatusResponseSchema
} from './svCacheContracts';

export type SvCacheProjectBridgeApi = {
  clearSvCache: (request: ClearSvCacheRequest) => Promise<SvCacheStatusResponse>;
  getSvCacheStatus: (request: GetSvCacheStatusRequest) => Promise<SvCacheStatusResponse>;
  updateSvCacheSettings: (
    request: UpdateSvCacheSettingsRequest
  ) => Promise<SvCacheStatusResponse>;
  warmupSvCacheStep: (request: WarmupSvCacheStepRequest) => Promise<SvCacheStatusResponse>;
};

export function createSvCacheProjectBridgeApi(
  transport: ProjectBridgeTransport
): SvCacheProjectBridgeApi {
  return {
    clearSvCache: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.clearSvCache,
        request,
        svCacheStatusResponseSchema
      ),
    getSvCacheStatus: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.getSvCacheStatus,
        request,
        svCacheStatusResponseSchema
      ),
    updateSvCacheSettings: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateSvCacheSettings,
        request,
        svCacheStatusResponseSchema
      ),
    warmupSvCacheStep: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.warmupSvCacheStep,
        request,
        svCacheStatusResponseSchema
      )
  };
}
