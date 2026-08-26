/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  readProjectSourceRevisionResponseSchema,
  type ReadProjectSourceRevisionRequest,
  type ReadProjectSourceRevisionResponse
} from './projectSourceRevisionContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';

export type ProjectSourceRevisionProjectBridgeApi = {
  readProjectSourceRevision: (
    request: ReadProjectSourceRevisionRequest
  ) => Promise<ReadProjectSourceRevisionResponse>;
};

export function createProjectSourceRevisionProjectBridgeApi(
  transport: ProjectBridgeTransport
): ProjectSourceRevisionProjectBridgeApi {
  return {
    readProjectSourceRevision: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.readProjectSourceRevision,
        request,
        readProjectSourceRevisionResponseSchema
      )
  };
}
