/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import {
  apiDiagnosticSchema,
  applyResultSchema,
  projectGameSchema,
  projectPathsSchema
} from './contracts';

export const loadFpsPatchRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const applyFpsPatchRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const restoreFpsPatchRequestSchema = z.strictObject({ paths: projectPathsSchema });

export const fpsPatchRomFsCategoryStatusSchema = z.strictObject({
  category: z.string(),
  conflictingFileCount: z.number(),
  managedFileCount: z.number(),
  patchedFileCount: z.number(),
  staleOwnedFileCount: z.number()
});

export const fpsPatchStatusSchema = z.strictObject({
  buildId: z.string().nullable(),
  conflictingRomFsFiles: z.array(z.string()),
  conflictingRomFsFileCount: z.number(),
  detectedGame: projectGameSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  mainSiteCount: z.number(),
  managedRomFsFileCount: z.number(),
  message: z.string(),
  patchedMainSiteCount: z.number(),
  patchedRomFsFileCount: z.number(),
  romFsCategories: z.array(fpsPatchRomFsCategoryStatusSchema),
  staleOwnedRomFsFiles: z.array(z.string()),
  staleOwnedRomFsFileCount: z.number(),
  status: z.string()
});

export const loadFpsPatchResponseSchema = z.strictObject({ status: fpsPatchStatusSchema });
export const applyFpsPatchResponseSchema = z.strictObject({
  applyResult: applyResultSchema,
  status: fpsPatchStatusSchema
});
export const restoreFpsPatchResponseSchema = z.strictObject({
  applyResult: applyResultSchema,
  status: fpsPatchStatusSchema
});

export type FpsPatchStatus = z.infer<typeof fpsPatchStatusSchema>;
export type LoadFpsPatchRequest = z.infer<typeof loadFpsPatchRequestSchema>;
export type LoadFpsPatchResponse = z.infer<typeof loadFpsPatchResponseSchema>;
export type ApplyFpsPatchRequest = z.infer<typeof applyFpsPatchRequestSchema>;
export type ApplyFpsPatchResponse = z.infer<typeof applyFpsPatchResponseSchema>;
export type RestoreFpsPatchRequest = z.infer<typeof restoreFpsPatchRequestSchema>;
export type RestoreFpsPatchResponse = z.infer<typeof restoreFpsPatchResponseSchema>;
