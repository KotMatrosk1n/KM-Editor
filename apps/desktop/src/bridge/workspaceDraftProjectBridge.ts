/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';
import {
  deleteWorkspaceDraftsResponseSchema,
  readWorkspaceDraftsResponseSchema,
  writeWorkspaceDraftsResponseSchema,
  type DeleteWorkspaceDraftsRequest,
  type DeleteWorkspaceDraftsResponse,
  type ReadWorkspaceDraftsRequest,
  type ReadWorkspaceDraftsResponse,
  type WriteWorkspaceDraftsRequest,
  type WriteWorkspaceDraftsResponse
} from './workspaceDraftContracts';

export type WorkspaceDraftProjectBridgeApi = {
  deleteWorkspaceDrafts: (
    request: DeleteWorkspaceDraftsRequest
  ) => Promise<DeleteWorkspaceDraftsResponse>;
  readWorkspaceDrafts: (
    request: ReadWorkspaceDraftsRequest
  ) => Promise<ReadWorkspaceDraftsResponse>;
  writeWorkspaceDrafts: (
    request: WriteWorkspaceDraftsRequest
  ) => Promise<WriteWorkspaceDraftsResponse>;
};

export function createWorkspaceDraftProjectBridgeApi(
  transport: ProjectBridgeTransport
): WorkspaceDraftProjectBridgeApi {
  return {
    deleteWorkspaceDrafts: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.deleteWorkspaceDrafts,
        request,
        deleteWorkspaceDraftsResponseSchema
      ),
    readWorkspaceDrafts: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.readWorkspaceDrafts,
        request,
        readWorkspaceDraftsResponseSchema
      ),
    writeWorkspaceDrafts: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.writeWorkspaceDrafts,
        request,
        writeWorkspaceDraftsResponseSchema
      )
  };
}
