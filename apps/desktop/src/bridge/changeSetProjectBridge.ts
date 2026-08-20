/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  captureChangeSetSessionResponseSchema,
  changeSetMaterializationSchema,
  changeSetWorkspaceSnapshotSchema,
  exportChangeSetsResponseSchema,
  importChangeSetsResponseSchema,
  type CaptureChangeSetSessionRequest,
  type CaptureChangeSetSessionResponse,
  type ChangeSetMaterialization,
  type ChangeSetWorkspaceSnapshot,
  type ExportChangeSetsRequest,
  type ExportChangeSetsResponse,
  type ImportChangeSetsRequest,
  type ImportChangeSetsResponse,
  type MaterializeChangeSetWorkspaceRequest,
  type MutateChangeSetWorkspaceRequest,
  type ReadChangeSetWorkspaceRequest
} from './changeSetContracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';

export type ChangeSetProjectBridgeApi = {
  captureChangeSetSession: (
    request: CaptureChangeSetSessionRequest
  ) => Promise<CaptureChangeSetSessionResponse>;
  exportChangeSets: (request: ExportChangeSetsRequest) => Promise<ExportChangeSetsResponse>;
  importChangeSets: (request: ImportChangeSetsRequest) => Promise<ImportChangeSetsResponse>;
  materializeChangeSets: (
    request: MaterializeChangeSetWorkspaceRequest
  ) => Promise<ChangeSetMaterialization>;
  mutateChangeSets: (
    request: MutateChangeSetWorkspaceRequest
  ) => Promise<ChangeSetWorkspaceSnapshot>;
  readChangeSets: (
    request: ReadChangeSetWorkspaceRequest
  ) => Promise<ChangeSetWorkspaceSnapshot>;
};

export function createChangeSetProjectBridgeApi(
  transport: ProjectBridgeTransport
): ChangeSetProjectBridgeApi {
  return {
    captureChangeSetSession: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.captureChangeSetSession,
      request,
      captureChangeSetSessionResponseSchema
    ),
    exportChangeSets: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.exportChangeSets,
      request,
      exportChangeSetsResponseSchema
    ),
    importChangeSets: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.importChangeSets,
      request,
      importChangeSetsResponseSchema
    ),
    materializeChangeSets: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.materializeChangeSets,
      request,
      changeSetMaterializationSchema
    ),
    mutateChangeSets: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.mutateChangeSets,
      request,
      changeSetWorkspaceSnapshotSchema
    ),
    readChangeSets: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.readChangeSets,
      request,
      changeSetWorkspaceSnapshotSchema
    )
  };
}
