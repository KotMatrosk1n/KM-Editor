/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  type ClearRowClipboardAuthorizationsRequest,
  type ClearRowClipboardAuthorizationsResponse,
  type PrepareRowClipboardCopyRequest,
  type PrepareRowClipboardCopyResponse,
  type PreviewRowClipboardPasteRequest,
  type PreviewRowClipboardPasteResponse,
  type StageRowClipboardPasteRequest,
  type StageRowClipboardPasteResponse,
  clearRowClipboardAuthorizationsResponseSchema,
  prepareRowClipboardCopyResponseSchema,
  previewRowClipboardPasteResponseSchema,
  stageRowClipboardPasteResponseSchema
} from './rowClipboardContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';

export type RowClipboardProjectBridgeApi = {
  clearRowClipboardAuthorizations: (
    request: ClearRowClipboardAuthorizationsRequest
  ) => Promise<ClearRowClipboardAuthorizationsResponse>;
  prepareRowClipboardCopy: (
    request: PrepareRowClipboardCopyRequest
  ) => Promise<PrepareRowClipboardCopyResponse>;
  previewRowClipboardPaste: (
    request: PreviewRowClipboardPasteRequest
  ) => Promise<PreviewRowClipboardPasteResponse>;
  stageRowClipboardPaste: (
    request: StageRowClipboardPasteRequest
  ) => Promise<StageRowClipboardPasteResponse>;
};

export function createRowClipboardProjectBridgeApi(
  transport: ProjectBridgeTransport
): RowClipboardProjectBridgeApi {
  return {
    clearRowClipboardAuthorizations: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.clearRowClipboardAuthorizations,
        request,
        clearRowClipboardAuthorizationsResponseSchema
      ),
    prepareRowClipboardCopy: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.prepareRowClipboardCopy,
        request,
        prepareRowClipboardCopyResponseSchema
      ),
    previewRowClipboardPaste: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.previewRowClipboardPaste,
        request,
        previewRowClipboardPasteResponseSchema
      ),
    stageRowClipboardPaste: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.stageRowClipboardPaste,
        request,
        stageRowClipboardPasteResponseSchema
      )
  };
}
