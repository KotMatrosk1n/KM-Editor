/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';
import {
  type LoadSwShPlacementObjectRequest,
  type LoadSwShPlacementObjectResponse,
  type OpenSwShPlacementCatalogRequest,
  type OpenSwShPlacementCatalogResponse,
  type QuerySwShPlacementCatalogRequest,
  type QuerySwShPlacementCatalogResponse,
  loadSwShPlacementObjectResponseSchema,
  openSwShPlacementCatalogResponseSchema,
  querySwShPlacementCatalogResponseSchema
} from './swShPlacementContracts';

export type SwShPlacementProjectBridgeApi = {
  openSwShPlacementCatalog: (
    request: OpenSwShPlacementCatalogRequest
  ) => Promise<OpenSwShPlacementCatalogResponse>;
  querySwShPlacementCatalog: (
    request: QuerySwShPlacementCatalogRequest
  ) => Promise<QuerySwShPlacementCatalogResponse>;
  loadSwShPlacementObject: (
    request: LoadSwShPlacementObjectRequest
  ) => Promise<LoadSwShPlacementObjectResponse>;
};

export function createSwShPlacementProjectBridgeApi(
  transport: ProjectBridgeTransport
): SwShPlacementProjectBridgeApi {
  return {
    openSwShPlacementCatalog: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.openSwShPlacementCatalog,
        request,
        openSwShPlacementCatalogResponseSchema
      ),
    querySwShPlacementCatalog: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.querySwShPlacementCatalog,
        request,
        querySwShPlacementCatalogResponseSchema
      ),
    loadSwShPlacementObject: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.loadSwShPlacementObject,
        request,
        loadSwShPlacementObjectResponseSchema
      )
  };
}
