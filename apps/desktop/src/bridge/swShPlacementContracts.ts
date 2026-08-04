/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  placedObjectRecordSchema,
  placementCategorySchema,
  placementEditableFieldSchema,
  placementWorkflowStatsSchema,
  projectPathsSchema,
  workflowSummarySchema
} from './contracts';

export const swShPlacementCatalogSchema = z.strictObject({
  revision: z.string().min(1),
  summary: workflowSummarySchema,
  editableFields: z.array(placementEditableFieldSchema),
  stats: placementWorkflowStatsSchema,
  diagnostics: z.array(apiDiagnosticSchema),
  categories: z.array(placementCategorySchema)
});

export const openSwShPlacementCatalogRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const openSwShPlacementCatalogResponseSchema = z.strictObject({
  catalog: swShPlacementCatalogSchema
});

export const querySwShPlacementCatalogRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  revision: z.string().min(1),
  categoryId: z.string().nullable().optional(),
  searchText: z.string().nullable().optional(),
  session: editSessionSchema.nullable().optional(),
  offset: z.number().int().nonnegative().optional(),
  limit: z.number().int().min(1).max(250).optional()
});

export const querySwShPlacementCatalogResponseSchema = z.strictObject({
  revision: z.string().min(1),
  objects: z.array(placedObjectRecordSchema),
  offset: z.number().int().nonnegative(),
  limit: z.number().int().min(1).max(250),
  totalCount: z.number().int().nonnegative()
});

export const loadSwShPlacementObjectRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  revision: z.string().min(1),
  objectId: z.string().min(1),
  session: editSessionSchema.nullable()
});

export const loadSwShPlacementObjectResponseSchema = z.strictObject({
  revision: z.string().min(1),
  object: placedObjectRecordSchema,
  diagnostics: z.array(apiDiagnosticSchema)
});

export type SwShPlacementCatalog = z.infer<typeof swShPlacementCatalogSchema>;
export type OpenSwShPlacementCatalogRequest = z.infer<
  typeof openSwShPlacementCatalogRequestSchema
>;
export type OpenSwShPlacementCatalogResponse = z.infer<
  typeof openSwShPlacementCatalogResponseSchema
>;
export type QuerySwShPlacementCatalogRequest = z.infer<
  typeof querySwShPlacementCatalogRequestSchema
>;
export type QuerySwShPlacementCatalogResponse = z.infer<
  typeof querySwShPlacementCatalogResponseSchema
>;
export type LoadSwShPlacementObjectRequest = z.infer<
  typeof loadSwShPlacementObjectRequestSchema
>;
export type LoadSwShPlacementObjectResponse = z.infer<
  typeof loadSwShPlacementObjectResponseSchema
>;
