/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { projectGameSchema, projectPathsSchema } from './contracts';
import { workspaceProjectIdSchema } from './workspacePersonalStateContracts';

const sourceRevisionFingerprintSchema = z.string().regex(/^[a-f0-9]{64}$/u);
const sourceObservationTokenSchema = z.string().regex(/^sob1_[a-f0-9]{64}$/u);

export const readProjectSourceRevisionRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  projectId: workspaceProjectIdSchema
});

export const readProjectSourceRevisionResponseSchema = z.strictObject({
  fingerprint: sourceRevisionFingerprintSchema,
  game: projectGameSchema,
  projectId: workspaceProjectIdSchema,
  sourceObservationToken: sourceObservationTokenSchema
});

export type ReadProjectSourceRevisionRequest = z.infer<
  typeof readProjectSourceRevisionRequestSchema
>;
export type ReadProjectSourceRevisionResponse = z.infer<
  typeof readProjectSourceRevisionResponseSchema
>;
