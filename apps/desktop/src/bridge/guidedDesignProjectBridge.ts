/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  guidedDesignCapabilitiesRequestSchema,
  guidedDesignCapabilitiesResponseSchema,
  guidedDesignImportRequestSchema,
  guidedDesignImportResponseSchema,
  guidedDesignPreviewRequestSchema,
  guidedDesignPreviewResponseSchema,
  type GuidedDesignCapabilitiesRequest,
  type GuidedDesignCapabilitiesResponse,
  type GuidedDesignImportRequest,
  type GuidedDesignImportResponse,
  type GuidedDesignPreviewRequest,
  type GuidedDesignPreviewResponse
} from './guidedDesignContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';

export type GuidedDesignProjectBridgeApi = {
  getGuidedDesignCapabilities: (
    request: GuidedDesignCapabilitiesRequest
  ) => Promise<GuidedDesignCapabilitiesResponse>;
  importGuidedDesignProposal: (
    request: GuidedDesignImportRequest
  ) => Promise<GuidedDesignImportResponse>;
  previewGuidedDesign: (
    request: GuidedDesignPreviewRequest
  ) => Promise<GuidedDesignPreviewResponse>;
};

export function createGuidedDesignProjectBridgeApi(
  transport: ProjectBridgeTransport
): GuidedDesignProjectBridgeApi {
  return {
    getGuidedDesignCapabilities: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.getGuidedDesignCapabilities,
      guidedDesignCapabilitiesRequestSchema.parse(request),
      guidedDesignCapabilitiesResponseSchema
    ),
    importGuidedDesignProposal: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.importGuidedDesignProposal,
      guidedDesignImportRequestSchema.parse(request),
      guidedDesignImportResponseSchema
    ),
    previewGuidedDesign: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.previewGuidedDesign,
      guidedDesignPreviewRequestSchema.parse(request),
      guidedDesignPreviewResponseSchema
    )
  };
}
