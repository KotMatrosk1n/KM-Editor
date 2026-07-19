/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  projectFileGraphEntryStateSchema,
  projectFileLayerSchema,
  projectPathsSchema,
  workflowSummarySchema
} from './contracts';

const int32Max = 2_147_483_647;
const sha256Schema = z.string().regex(/^[0-9a-f]{64}$/);

const canonicalSources = [
  ['flowers', 'romfs/world/ik_data/field/field_wazagimmick_public/field_wazagimmick_public/field_wazagimmick_public.bin'],
  ['attacks', 'romfs/param_ai/data/ai/attack/attack_param/attack_param_array.bin'],
  ['bullets', 'romfs/param_ai/data/ai/bullet/bullet_param/bullet_param_array.bin']
] as const;

const canonicalFlowers = ['blue', 'red'] as const;

const canonicalAttacks = [
  ['standard-projectile', 2004, 2146],
  ['shockwave', 2005, 2147],
  ['blue-shine', 2006, 2148],
  ['beam', 2007, 2149],
  ['red-shockwave', 2008, 2150],
  ['explosion-impact', 2011, 2153],
  ['projectile-variant', 2012, 2154],
  ['large-projectile', 2013, 2155],
  ['fog-1', 2014, 2156],
  ['fog-2', 2015, 2157]
] as const;

export const angeFightDamageSchema = z.number().int().min(0).max(int32Max);
export const angeFightHpSchema = z.number().int().min(1).max(int32Max);

export const angeFightAttackSelectionSchema = z.strictObject({
  attackId: z.number().int(),
  damageToPlayer: angeFightDamageSchema,
  damageToPokemon: angeFightDamageSchema
});

export const angeFightProvenanceSchema = z.strictObject({
  relativePath: z.string(),
  sourceLayer: projectFileLayerSchema,
  state: projectFileGraphEntryStateSchema
});

export const angeFightSourceRecordSchema = z.strictObject({
  effectiveSha256: sha256Schema,
  id: z.string(),
  label: z.string(),
  provenance: angeFightProvenanceSchema,
  relativePath: z.string(),
  status: z.enum(['base', 'layered']),
  vanillaSha256: sha256Schema
});

export const angeFightFlowerRecordSchema = z.strictObject({
  flowerId: z.enum(canonicalFlowers),
  hp: angeFightHpSchema,
  label: z.string(),
  vanillaHp: angeFightHpSchema
});

export const angeFightAttackRecordSchema = z.strictObject({
  attackId: z.number().int(),
  bulletId: z.number().int(),
  canRepeatHit: z.boolean(),
  damageToPlayer: angeFightDamageSchema,
  damageToPokemon: angeFightDamageSchema,
  hitIntervalSeconds: z.number().positive(),
  label: z.string(),
  moveId: z.string(),
  sharedByMultipleActions: z.boolean(),
  usage: z.string(),
  vanillaDamageToPlayer: angeFightDamageSchema,
  vanillaDamageToPokemon: angeFightDamageSchema
});

export const angeFightWorkflowStatsSchema = z.strictObject({
  attackCount: z.number().int().nonnegative(),
  editableValueCount: z.number().int().nonnegative(),
  flowerCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

const angeFightWorkflowBaseSchema = z.strictObject({
  attacks: z.array(angeFightAttackRecordSchema),
  canUninstall: z.boolean(),
  diagnostics: z.array(apiDiagnosticSchema),
  flowers: z.array(angeFightFlowerRecordSchema),
  installMessage: z.string(),
  installStatus: z.enum(['vanilla', 'modified', 'readOnly', 'blocked']),
  sources: z.array(angeFightSourceRecordSchema),
  stats: angeFightWorkflowStatsSchema,
  summary: workflowSummarySchema,
  uninstallMessage: z.string()
});

export const angeFightWorkflowSchema = angeFightWorkflowBaseSchema.superRefine(
  (workflow, context) => {
    if (workflow.summary.id !== 'angeFight' || workflow.summary.label !== 'Ange Fight') {
      context.addIssue({
        code: 'custom',
        message: 'Ange Fight workflow identity is invalid.',
        path: ['summary', 'id']
      });
    }

    if (workflow.installStatus === 'blocked') {
      return;
    }

    canonicalSources.forEach(([id, path], index) => {
      const source = workflow.sources[index];
      if (source?.id !== id || source.relativePath !== path) {
        context.addIssue({
          code: 'custom',
          message: 'Ange Fight sources must use the canonical three-member order.',
          path: ['sources', index]
        });
      }
    });

    canonicalFlowers.forEach((flowerId, index) => {
      if (workflow.flowers[index]?.flowerId !== flowerId) {
        context.addIssue({
          code: 'custom',
          message: 'Ange Fight must expose Blue and Red Flower HP in canonical order.',
          path: ['flowers', index, 'flowerId']
        });
      }
    });

    canonicalAttacks.forEach(([moveId, bulletId, attackId], index) => {
      const attack = workflow.attacks[index];
      if (
        attack?.moveId !== moveId ||
        attack.bulletId !== bulletId ||
        attack.attackId !== attackId
      ) {
        context.addIssue({
          code: 'custom',
          message: 'Ange Fight attacks must use the canonical non-Ember mapping.',
          path: ['attacks', index]
        });
      }
    });

    if (
      workflow.sources.length !== 3 ||
      workflow.flowers.length !== 2 ||
      workflow.attacks.length !== 10 ||
      workflow.stats.sourceFileCount !== 3 ||
      workflow.stats.flowerCount !== 2 ||
      workflow.stats.attackCount !== 10 ||
      workflow.stats.editableValueCount !== 22
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Ange Fight workflow counts do not match its canonical mapping.',
        path: ['stats']
      });
    }

    if (workflow.canUninstall && workflow.installStatus !== 'modified') {
      context.addIssue({
        code: 'custom',
        message: 'Ange Fight uninstall availability does not match its install status.',
        path: ['canUninstall']
      });
    }
  }
);

export const loadAngeFightWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
}).superRefine((request, context) => {
  if (request.paths.selectedGame !== 'za') {
    context.addIssue({
      code: 'custom',
      message: 'Ange Fight requires a Pokemon Legends Z-A project.',
      path: ['paths', 'selectedGame']
    });
  }
});

export const loadAngeFightWorkflowResponseSchema = z.strictObject({
  workflow: angeFightWorkflowSchema
});

export const stageAngeFightRequestSchema = z.strictObject({
  attacks: z.array(angeFightAttackSelectionSchema).length(10),
  blueFlowerHp: angeFightHpSchema,
  paths: projectPathsSchema,
  redFlowerHp: angeFightHpSchema,
  session: editSessionSchema.nullable()
}).superRefine((request, context) => {
  if (request.paths.selectedGame !== 'za') {
    context.addIssue({
      code: 'custom',
      message: 'Ange Fight requires a Pokemon Legends Z-A project.',
      path: ['paths', 'selectedGame']
    });
  }

  canonicalAttacks.forEach(([, , attackId], index) => {
    if (request.attacks[index]?.attackId !== attackId) {
      context.addIssue({
        code: 'custom',
        message: 'Ange Fight selections must include every canonical Attack ID in order.',
        path: ['attacks', index, 'attackId']
      });
    }
  });
});

export const stageAngeFightResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: angeFightWorkflowSchema
});

export const stageAngeFightUninstallRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
}).superRefine((request, context) => {
  if (request.paths.selectedGame !== 'za') {
    context.addIssue({
      code: 'custom',
      message: 'Ange Fight requires a Pokemon Legends Z-A project.',
      path: ['paths', 'selectedGame']
    });
  }
});

export const stageAngeFightUninstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: angeFightWorkflowSchema
});

export type AngeFightAttackRecord = z.infer<typeof angeFightAttackRecordSchema>;
export type AngeFightAttackSelection = z.infer<typeof angeFightAttackSelectionSchema>;
export type AngeFightFlowerRecord = z.infer<typeof angeFightFlowerRecordSchema>;
export type AngeFightWorkflow = z.infer<typeof angeFightWorkflowBaseSchema>;
export type LoadAngeFightWorkflowRequest = z.infer<typeof loadAngeFightWorkflowRequestSchema>;
export type LoadAngeFightWorkflowResponse = z.infer<typeof loadAngeFightWorkflowResponseSchema>;
export type StageAngeFightRequest = z.infer<typeof stageAngeFightRequestSchema>;
export type StageAngeFightResponse = z.infer<typeof stageAngeFightResponseSchema>;
export type StageAngeFightUninstallRequest = z.infer<
  typeof stageAngeFightUninstallRequestSchema
>;
export type StageAngeFightUninstallResponse = z.infer<
  typeof stageAngeFightUninstallResponseSchema
>;
