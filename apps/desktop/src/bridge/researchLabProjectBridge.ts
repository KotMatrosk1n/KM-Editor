/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  closeResearchSourceRequestSchema,
  closeResearchSourceResponseSchema,
  compareResearchSourcesRequestSchema,
  compareResearchSourcesResponseSchema,
  mutateResearchAnnotationsRequestSchema,
  mutateResearchAnnotationsResponseSchema,
  openResearchSourceRequestSchema,
  openResearchSourceResponseSchema,
  readResearchAnnotationsRequestSchema,
  readResearchAnnotationsResponseSchema,
  readResearchByteWindowRequestSchema,
  readResearchByteWindowResponseSchema,
  readResearchLabCapabilitiesRequestSchema,
  readResearchLabCapabilitiesResponseSchema,
  type CloseResearchSourceRequest,
  type CloseResearchSourceResponse,
  type CompareResearchSourcesRequest,
  type CompareResearchSourcesResponse,
  type MutateResearchAnnotationsRequest,
  type MutateResearchAnnotationsResponse,
  type OpenResearchSourceRequest,
  type OpenResearchSourceResponse,
  type ReadResearchAnnotationsRequest,
  type ReadResearchAnnotationsResponse,
  type ReadResearchByteWindowRequest,
  type ReadResearchByteWindowResponse,
  type ReadResearchLabCapabilitiesRequest,
  type ReadResearchLabCapabilitiesResponse
} from './researchLabContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';

export type ResearchLabProjectBridgeApi = {
  closeResearchSource: (
    request: CloseResearchSourceRequest
  ) => Promise<CloseResearchSourceResponse>;
  compareResearchSources: (
    request: CompareResearchSourcesRequest
  ) => Promise<CompareResearchSourcesResponse>;
  getResearchLabCapabilities: (
    request: ReadResearchLabCapabilitiesRequest
  ) => Promise<ReadResearchLabCapabilitiesResponse>;
  mutateResearchAnnotations: (
    request: MutateResearchAnnotationsRequest
  ) => Promise<MutateResearchAnnotationsResponse>;
  openResearchSource: (
    request: OpenResearchSourceRequest
  ) => Promise<OpenResearchSourceResponse>;
  readResearchAnnotations: (
    request: ReadResearchAnnotationsRequest
  ) => Promise<ReadResearchAnnotationsResponse>;
  readResearchByteWindow: (
    request: ReadResearchByteWindowRequest
  ) => Promise<ReadResearchByteWindowResponse>;
};

export function createResearchLabProjectBridgeApi(
  transport: ProjectBridgeTransport
): ResearchLabProjectBridgeApi {
  return {
    closeResearchSource: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.closeResearchSource,
      closeResearchSourceRequestSchema.parse(request),
      closeResearchSourceResponseSchema
    ),
    compareResearchSources: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.compareResearchSources,
      compareResearchSourcesRequestSchema.parse(request),
      compareResearchSourcesResponseSchema
    ),
    getResearchLabCapabilities: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.getResearchLabCapabilities,
      readResearchLabCapabilitiesRequestSchema.parse(request),
      readResearchLabCapabilitiesResponseSchema
    ),
    mutateResearchAnnotations: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.mutateResearchAnnotations,
      mutateResearchAnnotationsRequestSchema.parse(request),
      mutateResearchAnnotationsResponseSchema
    ),
    openResearchSource: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.openResearchSource,
      openResearchSourceRequestSchema.parse(request),
      openResearchSourceResponseSchema
    ),
    readResearchAnnotations: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.readResearchAnnotations,
      readResearchAnnotationsRequestSchema.parse(request),
      readResearchAnnotationsResponseSchema
    ),
    readResearchByteWindow: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.readResearchByteWindow,
      readResearchByteWindowRequestSchema.parse(request),
      readResearchByteWindowResponseSchema
    )
  };
}
