/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { projectGameSchema, type ProjectGame } from '../bridge/contracts';
import {
  projectDraftKey,
  type ProjectDraft,
  type ProjectDraftAdapter,
  type ProjectDraftKey
} from '../workbench/draftRegistry';
import {
  projectGameToFamily,
  semanticFieldRefKey,
  validateSemanticRecordRef,
  type JsonValue
} from '../workbench/semanticContracts';
import {
  advancedAuthoringMaximumMutationCount,
  type AdvancedAuthoringDraftSnapshot
} from './advancedAuthoringTypes';

export const advancedAuthoringDraftAdapterSchemaVersion = 1 as const;

const associationIdSchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[A-Za-z0-9][A-Za-z0-9._-]*$/u);
const boundedProjectIdSchema = z
  .string()
  .min(1)
  .max(128)
  .refine((value) => value === value.trim() && !/\p{Cc}/u.test(value));
const semanticKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u);
const semanticFieldKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$/u);
const semanticStableIdSchema = z
  .string()
  .min(1)
  .max(1024)
  .refine(
    (value) => value === value.trim() && !/[\u0000-\u001f\u007f-\u009f]/u.test(value)
  );
const sha256FingerprintSchema = z.string().regex(/^[A-Fa-f0-9]{64}$/u);

const sourceBindingSchema = z
  .strictObject({
    outputMode: z
      .enum(['standalone', 'trinityModManager', 'trinityBypass'])
      .nullable(),
    outputProfileId: associationIdSchema.nullable(),
    outputRootFingerprint: sha256FingerprintSchema,
    projectId: boundedProjectIdSchema,
    selectedChangeSetIds: z.array(associationIdSchema).max(64),
    version: z.literal(1),
    workspaceETag: sha256FingerprintSchema,
    workspaceFingerprint: sha256FingerprintSchema,
    workspacePersonalStateETag: sha256FingerprintSchema.nullable()
  })
  .superRefine((binding, context) => {
    if (new Set(binding.selectedChangeSetIds).size !== binding.selectedChangeSetIds.length) {
      context.addIssue({
        code: 'custom',
        message: 'Selected change-set ids must be unique.',
        path: ['selectedChangeSetIds']
      });
    }
    if (
      binding.outputProfileId === null
        ? binding.workspacePersonalStateETag !== null
        : binding.workspacePersonalStateETag === null
    ) {
      context.addIssue({
        code: 'custom',
        message: 'The output profile and personal-state ETag must be bound together.',
        path: ['workspacePersonalStateETag']
      });
    }
  });

const semanticRecordRefSchema = z.strictObject({
  domain: semanticKeySchema,
  gameFamily: z.enum(['swordShield', 'scarletViolet', 'legendsZA']),
  recordId: semanticStableIdSchema,
  recordKind: z.strictObject({
    key: semanticKeySchema,
    schemaVersion: z.number().int().min(1).max(2_147_483_647)
  }),
  subrecordId: semanticStableIdSchema.nullable()
});

const advancedAuthoringDraftSnapshotSchema = z
  .strictObject({
    entries: z
      .array(
        z.strictObject({
          field: z.strictObject({
            fieldKey: semanticFieldKeySchema,
            record: semanticRecordRefSchema
          }),
          value: z.number().finite()
        })
      )
      .max(advancedAuthoringMaximumMutationCount),
    schemaVersion: z.literal(advancedAuthoringDraftAdapterSchemaVersion),
    scope: z.strictObject({
      activeChangeSetId: associationIdSchema,
      game: projectGameSchema,
      projectId: boundedProjectIdSchema,
      sourceBinding: sourceBindingSchema
    })
  })
  .superRefine((snapshot, context) => {
    if (snapshot.scope.sourceBinding.projectId !== snapshot.scope.projectId) {
      context.addIssue({
        code: 'custom',
        message: 'The draft source binding must match the project scope.',
        path: ['scope', 'sourceBinding', 'projectId']
      });
    }
    const fieldKeys = new Set<string>();
    snapshot.entries.forEach((entry, index) => {
      if (entry.field.record.gameFamily !== projectGameToFamily(snapshot.scope.game)) {
        context.addIssue({
          code: 'custom',
          message: 'Draft records must match the scoped game family.',
          path: ['entries', index, 'field', 'record', 'gameFamily']
        });
      }
      const key = semanticFieldRefKey(entry.field);
      if (fieldKeys.has(key)) {
        context.addIssue({
          code: 'custom',
          message: 'Advanced authoring draft fields must be unique.',
          path: ['entries', index, 'field']
        });
      }
      fieldKeys.add(key);
    });
  });

export const advancedAuthoringProjectDraftAdapter: ProjectDraftAdapter<AdvancedAuthoringDraftSnapshot> = {
  adapterId: 'advanced-authoring.drafts',
  parsePayload: (payload) => advancedAuthoringDraftSnapshotSchema.parse(payload),
  schemaVersion: advancedAuthoringDraftAdapterSchemaVersion,
  serializePayload: (draft) =>
    advancedAuthoringDraftSnapshotSchema.parse(draft) as JsonValue
};

export function advancedAuthoringProjectDraftMatchesCapture(
  current: ProjectDraft<AdvancedAuthoringDraftSnapshot>,
  captured: ProjectDraft<AdvancedAuthoringDraftSnapshot>
) {
  return (
    projectDraftKey(current.key) === projectDraftKey(captured.key) &&
    current.adapterId === captured.adapterId &&
    current.adapterSchemaVersion === captured.adapterSchemaVersion &&
    current.storedAdapterSchemaVersion === captured.storedAdapterSchemaVersion &&
    current.projectSourceRevisionFingerprint ===
      captured.projectSourceRevisionFingerprint &&
    current.updatedAtUtc === captured.updatedAtUtc &&
    JSON.stringify(advancedAuthoringProjectDraftAdapter.serializePayload(current.payload)) ===
      JSON.stringify(advancedAuthoringProjectDraftAdapter.serializePayload(captured.payload))
  );
}

export type CreateAdvancedAuthoringProjectDraftKeyOptions = {
  activeChangeSetId: string;
  game: ProjectGame;
  projectId: string;
};

export function createAdvancedAuthoringProjectDraftKey(
  options: CreateAdvancedAuthoringProjectDraftKeyOptions
): ProjectDraftKey {
  associationIdSchema.parse(options.activeChangeSetId);
  boundedProjectIdSchema.parse(options.projectId);
  const entity = {
    domain: 'workspace.changes',
    gameFamily: projectGameToFamily(options.game),
    recordId: options.activeChangeSetId,
    recordKind: { key: 'change-set', schemaVersion: 1 },
    subrecordId: null
  } as const;
  validateSemanticRecordRef(entity);
  return {
    changeSetId: options.activeChangeSetId,
    domain: entity.domain,
    entity,
    game: options.game,
    projectId: options.projectId,
    section: 'changes'
  };
}
