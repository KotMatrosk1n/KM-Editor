/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  applyOutputCleanupResponseSchema,
  applyProjectRelocationResponseSchema,
  buildSupportReportResponseSchema,
  createOutputCheckpointResponseSchema,
  deleteOutputCheckpointResponseSchema,
  getOutputRecoveryStatusResponseSchema,
  listOutputCheckpointsResponseSchema,
  listOutputHistoryResponseSchema,
  previewOutputCheckpointRestoreResponseSchema,
  previewOutputCleanupResponseSchema,
  previewProjectRelocationResponseSchema,
  reconcileOutputRecoveryResponseSchema,
  restoreOutputCheckpointResponseSchema,
  scanOutputIntegrityResponseSchema,
  type ApplyOutputCleanupRequest,
  type ApplyOutputCleanupResponse,
  type ApplyProjectRelocationRequest,
  type ApplyProjectRelocationResponse,
  type BuildSupportReportRequest,
  type BuildSupportReportResponse,
  type CreateOutputCheckpointRequest,
  type CreateOutputCheckpointResponse,
  type DeleteOutputCheckpointRequest,
  type DeleteOutputCheckpointResponse,
  type GetOutputRecoveryStatusRequest,
  type GetOutputRecoveryStatusResponse,
  type ListOutputCheckpointsRequest,
  type ListOutputCheckpointsResponse,
  type ListOutputHistoryRequest,
  type ListOutputHistoryResponse,
  type PreviewOutputCheckpointRestoreRequest,
  type PreviewOutputCheckpointRestoreResponse,
  type PreviewOutputCleanupRequest,
  type PreviewOutputCleanupResponse,
  type PreviewProjectRelocationRequest,
  type PreviewProjectRelocationResponse,
  type ReconcileOutputRecoveryRequest,
  type ReconcileOutputRecoveryResponse,
  type RestoreOutputCheckpointRequest,
  type RestoreOutputCheckpointResponse,
  type ScanOutputIntegrityRequest,
  type ScanOutputIntegrityResponse
} from './outputSafetyContracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';

export type OutputSafetyProjectBridgeApi = {
  applyOutputCleanup: (
    request: ApplyOutputCleanupRequest
  ) => Promise<ApplyOutputCleanupResponse>;
  applyProjectRelocation: (
    request: ApplyProjectRelocationRequest
  ) => Promise<ApplyProjectRelocationResponse>;
  buildSupportReport: (
    request: BuildSupportReportRequest
  ) => Promise<BuildSupportReportResponse>;
  createOutputCheckpoint: (
    request: CreateOutputCheckpointRequest
  ) => Promise<CreateOutputCheckpointResponse>;
  deleteOutputCheckpoint: (
    request: DeleteOutputCheckpointRequest
  ) => Promise<DeleteOutputCheckpointResponse>;
  getOutputRecoveryStatus: (
    request: GetOutputRecoveryStatusRequest
  ) => Promise<GetOutputRecoveryStatusResponse>;
  listOutputCheckpoints: (
    request: ListOutputCheckpointsRequest
  ) => Promise<ListOutputCheckpointsResponse>;
  listOutputHistory: (
    request: ListOutputHistoryRequest
  ) => Promise<ListOutputHistoryResponse>;
  previewOutputCheckpointRestore: (
    request: PreviewOutputCheckpointRestoreRequest
  ) => Promise<PreviewOutputCheckpointRestoreResponse>;
  previewOutputCleanup: (
    request: PreviewOutputCleanupRequest
  ) => Promise<PreviewOutputCleanupResponse>;
  previewProjectRelocation: (
    request: PreviewProjectRelocationRequest
  ) => Promise<PreviewProjectRelocationResponse>;
  reconcileOutputRecovery: (
    request: ReconcileOutputRecoveryRequest
  ) => Promise<ReconcileOutputRecoveryResponse>;
  restoreOutputCheckpoint: (
    request: RestoreOutputCheckpointRequest
  ) => Promise<RestoreOutputCheckpointResponse>;
  scanOutputIntegrity: (
    request: ScanOutputIntegrityRequest
  ) => Promise<ScanOutputIntegrityResponse>;
};

export function createOutputSafetyProjectBridgeApi(
  transport: ProjectBridgeTransport
): OutputSafetyProjectBridgeApi {
  return {
    applyOutputCleanup: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyOutputCleanup,
        request,
        applyOutputCleanupResponseSchema
      ),
    applyProjectRelocation: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.applyProjectRelocation,
        request,
        applyProjectRelocationResponseSchema
      ),
    buildSupportReport: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.buildSupportReport,
        request,
        buildSupportReportResponseSchema
      ),
    createOutputCheckpoint: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.createOutputCheckpoint,
        request,
        createOutputCheckpointResponseSchema
      ),
    deleteOutputCheckpoint: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.deleteOutputCheckpoint,
        request,
        deleteOutputCheckpointResponseSchema
      ),
    getOutputRecoveryStatus: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.getOutputRecoveryStatus,
        request,
        getOutputRecoveryStatusResponseSchema
      ),
    listOutputCheckpoints: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.listOutputCheckpoints,
        request,
        listOutputCheckpointsResponseSchema
      ),
    listOutputHistory: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.listOutputHistory,
        request,
        listOutputHistoryResponseSchema
      ),
    previewOutputCheckpointRestore: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.previewOutputCheckpointRestore,
        request,
        previewOutputCheckpointRestoreResponseSchema
      ),
    previewOutputCleanup: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.previewOutputCleanup,
        request,
        previewOutputCleanupResponseSchema
      ),
    previewProjectRelocation: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.previewProjectRelocation,
        request,
        previewProjectRelocationResponseSchema
      ),
    reconcileOutputRecovery: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.reconcileOutputRecovery,
        request,
        reconcileOutputRecoveryResponseSchema
      ),
    restoreOutputCheckpoint: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.restoreOutputCheckpoint,
        request,
        restoreOutputCheckpointResponseSchema
      ),
    scanOutputIntegrity: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.scanOutputIntegrity,
        request,
        scanOutputIntegrityResponseSchema
      )
  };
}
