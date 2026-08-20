/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  kmRecipeExportRequestSchema,
  kmRecipeExportResponseSchema,
  kmRecipeImportRequestSchema,
  kmRecipeImportResponseSchema,
  kmRecipePreviewRequestSchema,
  kmRecipePreviewResponseSchema,
  kmRecipeValidateRequestSchema,
  kmRecipeValidateResponseSchema,
  semanticMergeCapabilitiesRequestSchema,
  semanticMergeCapabilitiesResponseSchema,
  semanticMergeImportRequestSchema,
  semanticMergeImportResponseSchema,
  semanticMergePreviewRequestSchema,
  semanticMergePreviewResponseSchema,
  semanticMergeSourceOpenRequestSchema,
  semanticMergeSourceOpenResponseSchema,
  type KmRecipeExportRequest,
  type KmRecipeExportResponse,
  type KmRecipeImportRequest,
  type KmRecipeImportResponse,
  type KmRecipePreviewRequest,
  type KmRecipePreviewResponse,
  type KmRecipeValidateRequest,
  type KmRecipeValidateResponse,
  type SemanticMergeCapabilitiesRequest,
  type SemanticMergeCapabilitiesResponse,
  type SemanticMergeImportRequest,
  type SemanticMergeImportResponse,
  type SemanticMergePreviewRequest,
  type SemanticMergePreviewResponse,
  type SemanticMergeSourceOpenRequest,
  type SemanticMergeSourceOpenResponse
} from './semanticMergeContracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';

export type SemanticMergeProjectBridgeApi = {
  exportKmRecipe: (request: KmRecipeExportRequest) => Promise<KmRecipeExportResponse>;
  getSemanticMergeCapabilities: (
    request: SemanticMergeCapabilitiesRequest
  ) => Promise<SemanticMergeCapabilitiesResponse>;
  importKmRecipe: (request: KmRecipeImportRequest) => Promise<KmRecipeImportResponse>;
  importSemanticMerge: (
    request: SemanticMergeImportRequest
  ) => Promise<SemanticMergeImportResponse>;
  openSemanticMergeSource: (
    request: SemanticMergeSourceOpenRequest
  ) => Promise<SemanticMergeSourceOpenResponse>;
  previewKmRecipe: (request: KmRecipePreviewRequest) => Promise<KmRecipePreviewResponse>;
  previewSemanticMerge: (
    request: SemanticMergePreviewRequest
  ) => Promise<SemanticMergePreviewResponse>;
  validateKmRecipe: (request: KmRecipeValidateRequest) => Promise<KmRecipeValidateResponse>;
};

export function createSemanticMergeProjectBridgeApi(
  transport: ProjectBridgeTransport
): SemanticMergeProjectBridgeApi {
  return {
    exportKmRecipe: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.exportKmRecipe,
      kmRecipeExportRequestSchema.parse(request),
      kmRecipeExportResponseSchema
    ),
    getSemanticMergeCapabilities: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.getSemanticMergeCapabilities,
      semanticMergeCapabilitiesRequestSchema.parse(request),
      semanticMergeCapabilitiesResponseSchema
    ),
    importKmRecipe: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.importKmRecipe,
      kmRecipeImportRequestSchema.parse(request),
      kmRecipeImportResponseSchema
    ),
    importSemanticMerge: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.importSemanticMerge,
      semanticMergeImportRequestSchema.parse(request),
      semanticMergeImportResponseSchema
    ),
    openSemanticMergeSource: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.openSemanticMergeSource,
      semanticMergeSourceOpenRequestSchema.parse(request),
      semanticMergeSourceOpenResponseSchema
    ),
    previewKmRecipe: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.previewKmRecipe,
      kmRecipePreviewRequestSchema.parse(request),
      kmRecipePreviewResponseSchema
    ),
    previewSemanticMerge: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.previewSemanticMerge,
      semanticMergePreviewRequestSchema.parse(request),
      semanticMergePreviewResponseSchema
    ),
    validateKmRecipe: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.validateKmRecipe,
      kmRecipeValidateRequestSchema.parse(request),
      kmRecipeValidateResponseSchema
    )
  };
}
