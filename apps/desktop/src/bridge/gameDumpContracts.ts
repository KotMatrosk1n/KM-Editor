/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { apiDiagnosticSchema, projectPathsSchema } from './contracts';

export const gameDumpCategoryKindSchema = z.enum(['table', 'text', 'raw']);
export const gameDumpFormatSchema = z.enum([
  'tsv',
  'csv',
  'json',
  'tsvAndJson',
  'txt',
  'txtAndJson',
  'raw',
  'rawAndJson'
]);

export const gameDumpSelectionSchema = z.strictObject({
  categoryId: z.string(),
  format: gameDumpFormatSchema
});

export const loadGameDumpWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const runGameDumpRequestSchema = z.strictObject({
  destinationFolder: z.string(),
  paths: projectPathsSchema,
  selections: z.array(gameDumpSelectionSchema)
});

export const gameDumpCategorySchema = z.strictObject({
  defaultFormat: gameDumpFormatSchema,
  description: z.string(),
  diagnostics: z.array(apiDiagnosticSchema),
  formats: z.array(gameDumpFormatSchema),
  id: z.string(),
  isAvailable: z.boolean(),
  kind: gameDumpCategoryKindSchema,
  label: z.string()
});

export const gameDumpWorkflowSchema = z.strictObject({
  categories: z.array(gameDumpCategorySchema),
  diagnostics: z.array(apiDiagnosticSchema)
});

export const gameDumpWrittenFileSchema = z.strictObject({
  categoryId: z.string(),
  relativePath: z.string(),
  sizeBytes: z.number().int().nonnegative()
});

export const gameDumpResultSchema = z.strictObject({
  destinationFolder: z.string(),
  diagnostics: z.array(apiDiagnosticSchema),
  succeeded: z.boolean(),
  writtenFiles: z.array(gameDumpWrittenFileSchema)
});

export const loadGameDumpWorkflowResponseSchema = z.strictObject({
  workflow: gameDumpWorkflowSchema
});

export const runGameDumpResponseSchema = z.strictObject({
  result: gameDumpResultSchema
});

export type GameDumpCategory = z.infer<typeof gameDumpCategorySchema>;
export type GameDumpCategoryKind = z.infer<typeof gameDumpCategoryKindSchema>;
export type GameDumpFormat = z.infer<typeof gameDumpFormatSchema>;
export type GameDumpResult = z.infer<typeof gameDumpResultSchema>;
export type GameDumpSelection = z.infer<typeof gameDumpSelectionSchema>;
export type GameDumpWorkflow = z.infer<typeof gameDumpWorkflowSchema>;
export type LoadGameDumpWorkflowRequest = z.infer<typeof loadGameDumpWorkflowRequestSchema>;
export type LoadGameDumpWorkflowResponse = z.infer<typeof loadGameDumpWorkflowResponseSchema>;
export type RunGameDumpRequest = z.infer<typeof runGameDumpRequestSchema>;
export type RunGameDumpResponse = z.infer<typeof runGameDumpResponseSchema>;
