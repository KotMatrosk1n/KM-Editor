/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  queryGameModuleRequestSchema,
  queryGameModuleResponseSchema,
  readGameModuleCapabilitiesRequestSchema,
  readGameModuleCapabilitiesResponseSchema,
  type QueryGameModuleRequest,
  type QueryGameModuleResponse,
  type ReadGameModuleCapabilitiesRequest,
  type ReadGameModuleCapabilitiesResponse
} from './gameModuleContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';

export type GameModuleProjectBridgeApi = {
  getGameModuleCapabilities: (
    request: ReadGameModuleCapabilitiesRequest
  ) => Promise<ReadGameModuleCapabilitiesResponse>;
  queryGameModule: (request: QueryGameModuleRequest) => Promise<QueryGameModuleResponse>;
};

export function createGameModuleProjectBridgeApi(
  transport: ProjectBridgeTransport
): GameModuleProjectBridgeApi {
  return {
    getGameModuleCapabilities: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.getGameModuleCapabilities,
      readGameModuleCapabilitiesRequestSchema.parse(request),
      readGameModuleCapabilitiesResponseSchema
    ),
    queryGameModule: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.queryGameModule,
      queryGameModuleRequestSchema.parse(request),
      queryGameModuleResponseSchema
    )
  };
}
