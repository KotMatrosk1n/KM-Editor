/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';
import {
  deleteWorkspaceProjectStateResponseSchema,
  readWorkspaceApplicationStateResponseSchema,
  readWorkspaceProjectStateResponseSchema,
  writeWorkspaceApplicationStateResponseSchema,
  writeWorkspaceProjectStateResponseSchema,
  type DeleteWorkspaceProjectStateRequest,
  type DeleteWorkspaceProjectStateResponse,
  type ReadWorkspaceApplicationStateRequest,
  type ReadWorkspaceApplicationStateResponse,
  type ReadWorkspaceProjectStateRequest,
  type ReadWorkspaceProjectStateResponse,
  type WriteWorkspaceApplicationStateRequest,
  type WriteWorkspaceApplicationStateResponse,
  type WriteWorkspaceProjectStateRequest,
  type WriteWorkspaceProjectStateResponse
} from './workspacePersonalStateContracts';

export type WorkspacePersonalStateProjectBridgeApi = {
  deleteWorkspaceProjectState: (
    request: DeleteWorkspaceProjectStateRequest
  ) => Promise<DeleteWorkspaceProjectStateResponse>;
  readWorkspaceApplicationState: (
    request?: ReadWorkspaceApplicationStateRequest
  ) => Promise<ReadWorkspaceApplicationStateResponse>;
  readWorkspaceProjectState: (
    request: ReadWorkspaceProjectStateRequest
  ) => Promise<ReadWorkspaceProjectStateResponse>;
  writeWorkspaceApplicationState: (
    request: WriteWorkspaceApplicationStateRequest
  ) => Promise<WriteWorkspaceApplicationStateResponse>;
  writeWorkspaceProjectState: (
    request: WriteWorkspaceProjectStateRequest
  ) => Promise<WriteWorkspaceProjectStateResponse>;
};

export function createWorkspacePersonalStateProjectBridgeApi(
  transport: ProjectBridgeTransport
): WorkspacePersonalStateProjectBridgeApi {
  return {
    deleteWorkspaceProjectState: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.deleteWorkspaceProjectState,
        request,
        deleteWorkspaceProjectStateResponseSchema
      ),
    readWorkspaceApplicationState: (request = {}) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.readWorkspaceApplicationState,
        request,
        readWorkspaceApplicationStateResponseSchema
      ),
    readWorkspaceProjectState: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.readWorkspaceProjectState,
        request,
        readWorkspaceProjectStateResponseSchema
      ),
    writeWorkspaceApplicationState: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.writeWorkspaceApplicationState,
        request,
        writeWorkspaceApplicationStateResponseSchema
      ),
    writeWorkspaceProjectState: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.writeWorkspaceProjectState,
        request,
        writeWorkspaceProjectStateResponseSchema
      )
  };
}
