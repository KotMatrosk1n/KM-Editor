/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  applyInGameSettingsPackageResponseSchema,
  inspectInGameSettingsPackageResponseSchema,
  previewInGameSettingsPackageResponseSchema,
  type ApplyInGameSettingsPackageRequest,
  type ApplyInGameSettingsPackageResponse,
  type InspectInGameSettingsPackageRequest,
  type InspectInGameSettingsPackageResponse,
  type PreviewInGameSettingsPackageRequest,
  type PreviewInGameSettingsPackageResponse
} from './inGameSettingsPackageContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';

export type InGameSettingsPackageProjectBridgeApi = {
  applyInGameSettingsPackage: (
    request: ApplyInGameSettingsPackageRequest
  ) => Promise<ApplyInGameSettingsPackageResponse>;
  inspectInGameSettingsPackage: (
    request: InspectInGameSettingsPackageRequest
  ) => Promise<InspectInGameSettingsPackageResponse>;
  previewInGameSettingsPackage: (
    request: PreviewInGameSettingsPackageRequest
  ) => Promise<PreviewInGameSettingsPackageResponse>;
};

export function createInGameSettingsPackageProjectBridgeApi(
  transport: ProjectBridgeTransport
): InGameSettingsPackageProjectBridgeApi {
  return {
    applyInGameSettingsPackage: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyInGameSettingsPackage,
        request,
        applyInGameSettingsPackageResponseSchema
      ),
    inspectInGameSettingsPackage: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.inspectInGameSettingsPackage,
        request,
        inspectInGameSettingsPackageResponseSchema
      ),
    previewInGameSettingsPackage: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.previewInGameSettingsPackage,
        request,
        previewInGameSettingsPackageResponseSchema
      )
  };
}
