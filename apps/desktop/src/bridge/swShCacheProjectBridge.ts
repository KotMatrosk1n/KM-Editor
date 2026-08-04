/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';
import {
  type ClearSwShCacheRequest,
  type GetSwShCacheStatusRequest,
  type SwShCacheStatusResponse,
  type UpdateSwShCacheSettingsRequest,
  type WarmupSwShCacheStepRequest,
  swShCacheStatusResponseSchema
} from './swShCacheContracts';

export type SwShCacheProjectBridgeApi = {
  clearSwShCache: (request: ClearSwShCacheRequest) => Promise<SwShCacheStatusResponse>;
  getSwShCacheStatus: (
    request: GetSwShCacheStatusRequest
  ) => Promise<SwShCacheStatusResponse>;
  updateSwShCacheSettings: (
    request: UpdateSwShCacheSettingsRequest
  ) => Promise<SwShCacheStatusResponse>;
  warmupSwShCacheStep: (
    request: WarmupSwShCacheStepRequest
  ) => Promise<SwShCacheStatusResponse>;
};

export function createSwShCacheProjectBridgeApi(
  transport: ProjectBridgeTransport
): SwShCacheProjectBridgeApi {
  return {
    clearSwShCache: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.clearSwShCache,
        request,
        swShCacheStatusResponseSchema
      ),
    getSwShCacheStatus: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.getSwShCacheStatus,
        request,
        swShCacheStatusResponseSchema
      ),
    updateSwShCacheSettings: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updateSwShCacheSettings,
        request,
        swShCacheStatusResponseSchema
      ),
    warmupSwShCacheStep: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.warmupSwShCacheStep,
        request,
        swShCacheStatusResponseSchema
      )
  };
}
