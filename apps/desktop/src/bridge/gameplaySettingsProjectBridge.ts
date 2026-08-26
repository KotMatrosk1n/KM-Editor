/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from "./contracts";
import {
  applyGameplaySettingsUpdateResponseSchema,
  getGameplaySettingsResponseSchema,
  previewGameplaySettingsUpdateResponseSchema,
  type ApplyGameplaySettingsUpdateRequest,
  type ApplyGameplaySettingsUpdateResponse,
  type GetGameplaySettingsRequest,
  type GetGameplaySettingsResponse,
  type PreviewGameplaySettingsUpdateRequest,
  type PreviewGameplaySettingsUpdateResponse,
} from "./gameplaySettingsContracts";
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport,
} from "./projectBridgeRequest";

export type GameplaySettingsProjectBridgeApi = {
  applyGameplaySettingsUpdate: (
    request: ApplyGameplaySettingsUpdateRequest,
  ) => Promise<ApplyGameplaySettingsUpdateResponse>;
  getGameplaySettings: (
    request: GetGameplaySettingsRequest,
  ) => Promise<GetGameplaySettingsResponse>;
  previewGameplaySettingsUpdate: (
    request: PreviewGameplaySettingsUpdateRequest,
  ) => Promise<PreviewGameplaySettingsUpdateResponse>;
};

export function createGameplaySettingsProjectBridgeApi(
  transport: ProjectBridgeTransport,
): GameplaySettingsProjectBridgeApi {
  return {
    applyGameplaySettingsUpdate: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyGameplaySettingsUpdate,
        request,
        applyGameplaySettingsUpdateResponseSchema,
      ),
    getGameplaySettings: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.getGameplaySettings,
        request,
        getGameplaySettingsResponseSchema,
      ),
    previewGameplaySettingsUpdate: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.previewGameplaySettingsUpdate,
        request,
        previewGameplaySettingsUpdateResponseSchema,
      ),
  };
}
