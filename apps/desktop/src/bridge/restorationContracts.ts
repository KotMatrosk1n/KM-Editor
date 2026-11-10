/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectPathsSchema } from './contracts';

export const restorationWorkflowSchema = z.strictObject({ canEdit: z.boolean(), revision: z.string(),
  entries: z.array(z.strictObject({ speciesId: z.number().int(), name: z.string(), definitionId: z.string().nullable(),
    form: z.number().int().min(0).max(255).default(0),
    plannedPersonalId: z.number().int().min(899).max(4095).nullable().default(null),
    formLabel: z.string().min(1).nullable().default(null),
    support: z.enum(['NotSupported', 'Testing', 'Ready']), installation: z.enum(['Disabled', 'Enabled', 'NeedsRepair', 'External']),
    assetsRemoved: z.string().nullable().default(null), removalStaged: z.boolean().default(false), updateAvailable: z.boolean().default(false),
    availableForAssignment: z.boolean().default(false),
    donor: z.enum(['ScarletViolet', 'LegendsZA', 'LegendsArceus']).nullable(), family: z.array(z.number().int()), preparedHash: z.string().nullable(), selected: z.boolean(), staged: z.boolean() })).max(1020),
  diagnostics: z.array(apiDiagnosticSchema) });
export const restorationPreparationSchema = z.strictObject({ id: z.string(), definitionId: z.string(),
  status: z.enum(['preparing', 'prepared', 'canceled', 'failed']), phase: z.string(), completedSpecies: z.number().int(),
  activity: z.string().default(''), species: z.number().int().nullable().default(null), completedUnits: z.number().int().nonnegative().default(0),
  totalUnits: z.number().int().positive().nullable().default(null), currentResource: z.string().nullable().default(null),
  recentResources: z.array(z.string()).max(24).default([]), elapsedMilliseconds: z.number().nonnegative().default(0),
  removalId: z.string().nullable().default(null),
  cancellationRequested: z.boolean().default(false), cacheMode: z.enum(['existing', 'temporary']).nullable().default(null),
  totalSpecies: z.number().int(), preparedHash: z.string().nullable(), unavailableSourceMoves: z.record(z.string(), z.array(z.number().int())),
  diagnostics: z.array(apiDiagnosticSchema) });
export const loadRestorationRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable().optional() });
export const loadRestorationResponseSchema = z.strictObject({ workflow: restorationWorkflowSchema });
export const prepareRestorationRequestSchema = z.strictObject({ paths: projectPathsSchema, definitionId: z.string(),
  donorRomFsPath: z.string().min(1), compressionSupportFolderPath: z.string().nullable() });
export const restorationPreparationRequestSchema = z.strictObject({ paths: projectPathsSchema, preparationId: z.string() });
export const restorationPreparationResponseSchema = z.strictObject({ preparation: restorationPreparationSchema });
const restorationSelectionFields = { definitionId: z.string(), preparedHash: z.string(), enabled: z.boolean(),
  removeAssets: z.boolean().optional(), updateAssets: z.boolean().optional(), adoptExternal: z.boolean().optional() };
const restorationStageFields = { paths: projectPathsSchema, session: editSessionSchema.nullable() };
export const stageRestorationRequestSchema = z.union([
  z.strictObject({ ...restorationStageFields, ...restorationSelectionFields }),
  z.strictObject({ ...restorationStageFields, selections: z.array(z.strictObject(restorationSelectionFields)).min(1).max(1020) }),
]);
export const stageRestorationResponseSchema = z.strictObject({ workflow: restorationWorkflowSchema, session: editSessionSchema,
  diagnostics: z.array(apiDiagnosticSchema) });
export const restorationStageProgressSchema = z.discriminatedUnion('phase', [
  z.strictObject({ phase: z.literal('staging'), completed: z.number().int().nonnegative(),
    total: z.number().int().positive().max(1020) }),
  z.strictObject({ phase: z.enum(['validating', 'refreshing']), completed: z.null(), total: z.null() }),
]).refine(progress => progress.phase !== 'staging' || progress.completed <= progress.total);
export type RestorationStageProgress = z.infer<typeof restorationStageProgressSchema>;
export type RestorationWorkflow = z.infer<typeof restorationWorkflowSchema>;
export type RestorationPreparation = z.infer<typeof restorationPreparationSchema>;
export type RestorationSelection = { definitionId: string; preparedHash: string; enabled: boolean; removeAssets?: boolean; updateAssets?: boolean; adoptExternal?: boolean };

export const inspectRestorationDonorRequestSchema = z.strictObject({ paths: projectPathsSchema, donor: z.enum(['ScarletViolet', 'LegendsZA']),
  donorRomFsPath: z.string().min(1), compressionSupportFolderPath: z.string().nullable() });
export const restorationDonorResponseSchema = z.strictObject({ mode: z.enum(['existing', 'temporary']) });
