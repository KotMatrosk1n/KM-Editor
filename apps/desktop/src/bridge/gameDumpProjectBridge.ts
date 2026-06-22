/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  type LoadGameDumpWorkflowRequest,
  type LoadGameDumpWorkflowResponse,
  type RunGameDumpRequest,
  type RunGameDumpResponse,
  loadGameDumpWorkflowResponseSchema,
  runGameDumpResponseSchema
} from './gameDumpContracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';

export type GameDumpProjectBridgeApi = {
  loadGameDumpWorkflow: (
    request: LoadGameDumpWorkflowRequest
  ) => Promise<LoadGameDumpWorkflowResponse>;
  runGameDump: (request: RunGameDumpRequest) => Promise<RunGameDumpResponse>;
};

export function createGameDumpProjectBridgeApi(
  transport: ProjectBridgeTransport
): GameDumpProjectBridgeApi {
  return {
    loadGameDumpWorkflow: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadGameDumpWorkflow,
        request,
        loadGameDumpWorkflowResponseSchema
      ),
    runGameDump: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.runGameDump,
        request,
        runGameDumpResponseSchema
      )
  };
}
