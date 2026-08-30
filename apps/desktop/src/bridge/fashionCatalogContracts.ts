/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  projectPathsSchema,
  workflowSummarySchema
} from './contracts';

const sha256Schema = z.string().regex(/^[a-f0-9]{64}$/);
const uint32Schema = z.number().int().min(0).max(4_294_967_295);
const int32Schema = z.number().int().min(-2_147_483_648).max(2_147_483_647);

export const fashionCatalogFileSchema = z.enum([
  'dressUpItems',
  'dressUpGroups',
  'hairAndMakeup',
  'dressUpLineups',
  'hairAndMakeupLineups'
]);

export const fashionCatalogRowBindingSchema = z.strictObject({
  physicalIndex: z.number().int().nonnegative().max(49_999),
  physicalRowId: z.string().min(1),
  rowRevision: sha256Schema,
  sourceRevision: sha256Schema
});

export const dressUpItemRecordSchema = z.strictObject({
  catalogGroupCode: uint32Schema,
  categoryCode: uint32Schema,
  colorVariantCode: uint32Schema,
  displayOrder: uint32Schema,
  itemId: uint32Schema,
  modelPart: z.string(),
  modelVariant: z.string(),
  physicalIndex: z.number().int().nonnegative().max(49_999),
  physicalRowId: z.string().min(1),
  primaryColorLabel: z.string(),
  rowRevision: sha256Schema,
  secondaryColorLabel: z.string(),
  variantOrder: uint32Schema
});

export const dressUpGroupRecordSchema = z.strictObject({
  displayLabel: z.string(),
  displayOrder: uint32Schema,
  modelPart: z.string(),
  physicalIndex: z.number().int().nonnegative().max(49_999),
  physicalRowId: z.string().min(1),
  rowRevision: sha256Schema
});

export const hairAndMakeupRecordSchema = z.strictObject({
  catalogTypeCode: uint32Schema,
  colorValue: z.string().nullable(),
  displayOrder: uint32Schema,
  groupCode: int32Schema,
  itemId: uint32Schema,
  labelKey: z.string().nullable(),
  modelKey: z.string(),
  physicalIndex: z.number().int().nonnegative().max(49_999),
  physicalRowId: z.string().min(1),
  rowRevision: sha256Schema,
  variantCode: int32Schema
});

export const fashionLineupEntryRecordSchema = z.strictObject({
  entryPhysicalIndex: z.number().int().nonnegative().max(49_999),
  itemId: uint32Schema,
  lineupId: z.string().min(1),
  lineupPhysicalIndex: z.number().int().nonnegative().max(49_999),
  physicalIndex: z.number().int().nonnegative().max(49_999),
  physicalRowId: z.string().min(1),
  rowRevision: sha256Schema,
  shopIds: z.array(z.string().min(1)).max(50_000)
});

export const fashionCatalogTextLabelSchema = z.strictObject({
  key: z.string().min(1),
  label: z.string().min(1)
});

export const fashionCatalogWorkflowSchema = z
  .strictObject({
    canStage: z.boolean(),
    diagnostics: z.array(apiDiagnosticSchema),
    dressUpGroups: z.array(dressUpGroupRecordSchema).max(50_000),
    dressUpGroupsRevision: z.union([sha256Schema, z.literal('')]),
    dressUpItems: z.array(dressUpItemRecordSchema).max(50_000),
    dressUpItemsRevision: z.union([sha256Schema, z.literal('')]),
    dressUpLineups: z.array(fashionLineupEntryRecordSchema).max(50_000),
    dressUpLineupsRevision: z.union([sha256Schema, z.literal('')]),
    fashionShopsRevision: z.union([sha256Schema, z.literal('')]),
    hairAndMakeup: z.array(hairAndMakeupRecordSchema).max(50_000),
    hairAndMakeupLineups: z.array(fashionLineupEntryRecordSchema).max(50_000),
    hairAndMakeupLineupsRevision: z.union([sha256Schema, z.literal('')]),
    hairAndMakeupRevision: z.union([sha256Schema, z.literal('')]),
    sourceRevision: z.union([sha256Schema, z.literal('')]),
    stats: z.strictObject({
      dressUpGroupCount: z.number().int().nonnegative().max(50_000),
      dressUpItemCount: z.number().int().nonnegative().max(50_000),
      dressUpLineupEntryCount: z.number().int().nonnegative().max(50_000),
      hairAndMakeupCount: z.number().int().nonnegative().max(50_000),
      hairAndMakeupLineupEntryCount: z.number().int().nonnegative().max(50_000)
    }),
    summary: workflowSummarySchema,
    textLabels: z.array(fashionCatalogTextLabelSchema).max(5_000)
  })
  .superRefine((workflow, context) => {
    if (
      workflow.summary.id !== 'fashionCatalog' ||
      workflow.summary.label !== 'Fashion Catalog'
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Fashion Catalog workflow identity is not canonical.',
        path: ['summary']
      });
    }
    if (
      workflow.stats.dressUpItemCount !== workflow.dressUpItems.length ||
      workflow.stats.dressUpGroupCount !== workflow.dressUpGroups.length ||
      workflow.stats.hairAndMakeupCount !== workflow.hairAndMakeup.length ||
      workflow.stats.dressUpLineupEntryCount !== workflow.dressUpLineups.length ||
      workflow.stats.hairAndMakeupLineupEntryCount !== workflow.hairAndMakeupLineups.length
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Fashion Catalog statistics do not match the physical row arrays.',
        path: ['stats']
      });
    }
    if (workflow.canStage && workflow.sourceRevision.length !== 64) {
      context.addIssue({
        code: 'custom',
        message: 'Editable Fashion Catalog data must bind to an exact source revision.',
        path: ['sourceRevision']
      });
    }
    for (const [index, row] of workflow.dressUpItems.entries()) {
      if (row.physicalIndex !== index || row.physicalRowId !== `dress-up-item:${index}`) {
        context.addIssue({
          code: 'custom',
          message: 'Dress-up item physical identity is not canonical.',
          path: ['dressUpItems', index, 'physicalRowId']
        });
      }
    }
    for (const [index, row] of workflow.dressUpGroups.entries()) {
      if (row.physicalIndex !== index || row.physicalRowId !== `dress-up-group:${index}`) {
        context.addIssue({
          code: 'custom',
          message: 'Dress-up group physical identity is not canonical.',
          path: ['dressUpGroups', index, 'physicalRowId']
        });
      }
    }
    for (const [index, row] of workflow.hairAndMakeup.entries()) {
      if (row.physicalIndex !== index || row.physicalRowId !== `hair-and-makeup:${index}`) {
        context.addIssue({
          code: 'custom',
          message: 'Hair and makeup physical identity is not canonical.',
          path: ['hairAndMakeup', index, 'physicalRowId']
        });
      }
    }
    for (const [index, row] of workflow.dressUpLineups.entries()) {
      if (row.physicalIndex !== index || row.physicalRowId !== `dress-up-lineup-entry:${index}`) {
        context.addIssue({
          code: 'custom',
          message: 'Dress-up shop-lineup physical identity is not canonical.',
          path: ['dressUpLineups', index, 'physicalRowId']
        });
      }
    }
    for (const [index, row] of workflow.hairAndMakeupLineups.entries()) {
      if (
        row.physicalIndex !== index ||
        row.physicalRowId !== `hair-and-makeup-lineup-entry:${index}`
      ) {
        context.addIssue({
          code: 'custom',
          message: 'Hair and makeup shop-lineup physical identity is not canonical.',
          path: ['hairAndMakeupLineups', index, 'physicalRowId']
        });
      }
    }
    const labelKeys = new Set<string>();
    for (const [index, label] of workflow.textLabels.entries()) {
      if (labelKeys.has(label.key)) {
        context.addIssue({
          code: 'custom',
          message: 'Fashion Catalog text-label keys must be unique.',
          path: ['textLabels', index, 'key']
        });
      }
      labelKeys.add(label.key);
    }
  });

const zaPathsSchema = projectPathsSchema.superRefine((paths, context) => {
  if (paths.selectedGame !== 'za') {
    context.addIssue({
      code: 'custom',
      message: 'Fashion Catalog requires a Pokemon Legends Z-A project.',
      path: ['selectedGame']
    });
  }
});

export const loadFashionCatalogWorkflowRequestSchema = z.strictObject({
  paths: zaPathsSchema
});

export const loadFashionCatalogWorkflowResponseSchema = z.strictObject({
  workflow: fashionCatalogWorkflowSchema
});

export const stageFashionCatalogFieldEditRequestSchema = z
  .strictObject({
    binding: fashionCatalogRowBindingSchema,
    catalogFile: fashionCatalogFileSchema,
    clear: z.boolean(),
    field: z.string().min(1),
    paths: zaPathsSchema,
    session: editSessionSchema.nullable(),
    value: z.string().nullable()
  })
  .superRefine((request, context) => {
    if (request.clear && request.value !== null) {
      context.addIssue({
        code: 'custom',
        message: 'A clear operation cannot also carry a replacement value.',
        path: ['value']
      });
    }
    if (!request.clear && request.value === null) {
      context.addIssue({
        code: 'custom',
        message: 'A field edit requires a replacement value.',
        path: ['value']
      });
    }
  });

export const stageFashionCatalogFieldEditResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: fashionCatalogWorkflowSchema
});

export type FashionCatalogFile = z.infer<typeof fashionCatalogFileSchema>;
export type FashionCatalogRowBinding = z.infer<
  typeof fashionCatalogRowBindingSchema
>;
export type DressUpItemRecord = z.infer<typeof dressUpItemRecordSchema>;
export type DressUpGroupRecord = z.infer<typeof dressUpGroupRecordSchema>;
export type HairAndMakeupRecord = z.infer<typeof hairAndMakeupRecordSchema>;
export type FashionLineupEntryRecord = z.infer<typeof fashionLineupEntryRecordSchema>;
export type FashionCatalogTextLabel = z.infer<typeof fashionCatalogTextLabelSchema>;
export type FashionCatalogWorkflow = z.infer<typeof fashionCatalogWorkflowSchema>;
export type LoadFashionCatalogWorkflowRequest = z.infer<
  typeof loadFashionCatalogWorkflowRequestSchema
>;
export type LoadFashionCatalogWorkflowResponse = z.infer<
  typeof loadFashionCatalogWorkflowResponseSchema
>;
export type StageFashionCatalogFieldEditRequest = z.infer<
  typeof stageFashionCatalogFieldEditRequestSchema
>;
export type StageFashionCatalogFieldEditResponse = z.infer<
  typeof stageFashionCatalogFieldEditResponseSchema
>;
