/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  semanticExploreCapabilitiesRequestSchema,
  semanticExploreCapabilitiesSchema,
  semanticExploreChangesPageSchema,
  semanticExploreChangesRequestSchema,
  semanticExploreCompareRequestSchema,
  semanticExploreComparisonPageSchema,
  semanticExploreEntityRequestSchema,
  semanticExploreEntitySchema,
  semanticExploreExternalCompareRequestSchema,
  semanticExploreImpactPageSchema,
  semanticExploreImpactRequestSchema,
  semanticExploreOwnershipPageSchema,
  semanticExploreOwnershipRequestSchema,
  semanticExploreReferencesPageSchema,
  semanticExploreReferencesRequestSchema,
  semanticExploreSearchPageSchema,
  semanticExploreSearchRequestSchema,
  type SemanticExploreCapabilities,
  type SemanticExploreCapabilitiesRequest,
  type SemanticExploreChangesPage,
  type SemanticExploreChangesRequest,
  type SemanticExploreCompareRequest,
  type SemanticExploreComparisonPage,
  type SemanticExploreEntity,
  type SemanticExploreEntityRequest,
  type SemanticExploreExternalCompareRequest,
  type SemanticExploreImpactPage,
  type SemanticExploreImpactRequest,
  type SemanticExploreOwnershipPage,
  type SemanticExploreOwnershipRequest,
  type SemanticExploreReferencesPage,
  type SemanticExploreReferencesRequest,
  type SemanticExploreSearchPage,
  type SemanticExploreSearchRequest
} from './semanticExploreContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';
export type SemanticExploreProjectBridgeApi = {
  compare: (request: SemanticExploreCompareRequest) => Promise<SemanticExploreComparisonPage>;
  compareExternal: (
    request: SemanticExploreExternalCompareRequest
  ) => Promise<SemanticExploreComparisonPage>;
  getCapabilities: (
    request: SemanticExploreCapabilitiesRequest
  ) => Promise<SemanticExploreCapabilities>;
  getEntity: (request: SemanticExploreEntityRequest) => Promise<SemanticExploreEntity>;
  getImpact: (request: SemanticExploreImpactRequest) => Promise<SemanticExploreImpactPage>;
  getOwnership: (
    request: SemanticExploreOwnershipRequest
  ) => Promise<SemanticExploreOwnershipPage>;
  getReferences: (
    request: SemanticExploreReferencesRequest
  ) => Promise<SemanticExploreReferencesPage>;
  getSemanticChanges: (
    request: SemanticExploreChangesRequest
  ) => Promise<SemanticExploreChangesPage>;
  search: (request: SemanticExploreSearchRequest) => Promise<SemanticExploreSearchPage>;
};

export function createSemanticExploreProjectBridgeApi(
  transport: ProjectBridgeTransport
): SemanticExploreProjectBridgeApi {
  return {
    compare: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.compareSemantic,
      semanticExploreCompareRequestSchema.parse(request),
      semanticExploreComparisonPageSchema
    ),
    compareExternal: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.compareExternalSemantic,
      semanticExploreExternalCompareRequestSchema.parse(request),
      semanticExploreComparisonPageSchema
    ),
    getCapabilities: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.getSemanticCapabilities,
      semanticExploreCapabilitiesRequestSchema.parse(request),
      semanticExploreCapabilitiesSchema
    ),
    getEntity: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.getSemanticEntity,
      semanticExploreEntityRequestSchema.parse(request),
      semanticExploreEntitySchema
    ),
    getImpact: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.querySemanticImpact,
      semanticExploreImpactRequestSchema.parse(request),
      semanticExploreImpactPageSchema
    ),
    getOwnership: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.querySemanticOwnership,
      semanticExploreOwnershipRequestSchema.parse(request),
      semanticExploreOwnershipPageSchema
    ),
    getReferences: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.querySemanticReferences,
      semanticExploreReferencesRequestSchema.parse(request),
      semanticExploreReferencesPageSchema
    ),
    getSemanticChanges: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.querySemanticChanges,
      semanticExploreChangesRequestSchema.parse(request),
      semanticExploreChangesPageSchema
    ),
    search: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.searchSemantic,
      semanticExploreSearchRequestSchema.parse(request),
      semanticExploreSearchPageSchema
    )
  };
}
