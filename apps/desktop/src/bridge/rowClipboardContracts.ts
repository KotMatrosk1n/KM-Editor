/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  projectGameSchema,
  projectPathsSchema
} from './contracts';

const stableIdentifierSchema = z
  .string()
  .regex(/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/u);
const boundedText = (maximumLength: number) =>
  z
    .string()
    .min(1)
    .max(maximumLength)
    .refine((value) => value === value.trim() && !/\p{Cc}/u.test(value));
const upperSha256Schema = z.string().regex(/^[A-F0-9]{64}$/u);
const signedIntegerSchema = z
  .string()
  .regex(/^(?:0|-[1-9][0-9]*|[1-9][0-9]*)$/u)
  .refine((value) => {
    try {
      const parsed = BigInt(value);
      return parsed >= -(1n << 63n) && parsed <= (1n << 63n) - 1n;
    } catch {
      return false;
    }
  });
const unsignedIntegerSchema = z
  .string()
  .regex(/^(?:0|[1-9][0-9]*)$/u)
  .refine((value) => {
    try {
      const parsed = BigInt(value);
      return parsed >= 0n && parsed <= (1n << 64n) - 1n;
    } catch {
      return false;
    }
  });
const decimalSchema = z
  .string()
  .max(128)
  .regex(/^(?:0|-?[1-9][0-9]*|-?(?:0|[1-9][0-9]*)\.[0-9]*[1-9])$/u)
  .refine((value) => {
    const parsed = Number(value);
    return Number.isFinite(parsed) && (parsed !== 0 || value === '0');
  });

export const rowClipboardScopeDtoSchema = z
  .strictObject({
    game: projectGameSchema,
    gameFamily: z.enum(['swordShield', 'scarletViolet', 'legendsZA']),
    profileId: stableIdentifierSchema,
    projectId: boundedText(128)
  })
  .superRefine((scope, context) => {
    const expected =
      scope.game === 'sword' || scope.game === 'shield'
        ? 'swordShield'
        : scope.game === 'scarlet' || scope.game === 'violet'
          ? 'scarletViolet'
          : 'legendsZA';
    if (scope.gameFamily !== expected) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Logical-row game family does not match its game.',
        path: ['gameFamily']
      });
    }
  });

export const rowClipboardEditorSchemaDtoSchema = z.strictObject({
  editorId: stableIdentifierSchema,
  rowKind: stableIdentifierSchema,
  rowSchemaVersion: z.number().int().min(1).max(65_535)
});

export const rowClipboardLogicalIdentityDtoSchema = z.strictObject({
  key: boundedText(512),
  kind: stableIdentifierSchema
});

export const rowClipboardDependencyReferenceDtoSchema = z.strictObject({
  form: boundedText(128).nullable(),
  id: boundedText(128),
  kind: stableIdentifierSchema
});

export const rowClipboardValueDtoSchema = z.discriminatedUnion('kind', [
  z.strictObject({ kind: z.literal('boolean'), value: z.boolean() }),
  z.strictObject({ kind: z.literal('signedInteger'), value: signedIntegerSchema }),
  z.strictObject({ kind: z.literal('unsignedInteger'), value: unsignedIntegerSchema }),
  z.strictObject({ kind: z.literal('decimal'), value: decimalSchema }),
  z.strictObject({ kind: z.literal('string'), value: z.string() }),
  z.strictObject({
    kind: z.literal('dependencyReference'),
    value: rowClipboardDependencyReferenceDtoSchema
  })
]);

export const rowClipboardOwnedValueDtoSchema = z.strictObject({
  fieldKey: stableIdentifierSchema,
  value: rowClipboardValueDtoSchema
});

export const rowClipboardLogicalRowV1DtoSchema = z.strictObject({
  sourceIdentity: rowClipboardLogicalIdentityDtoSchema,
  values: z.array(rowClipboardOwnedValueDtoSchema).min(1).max(64)
});

export const rowClipboardSourceV1DtoSchema = z.strictObject({
  logicalIdentity: rowClipboardLogicalIdentityDtoSchema,
  projectRevision: boundedText(512)
});

export const rowClipboardEnvelopeV1DtoSchema = z
  .strictObject({
    checksum: upperSha256Schema,
    dependencies: z.array(rowClipboardDependencyReferenceDtoSchema).max(512),
    editor: rowClipboardEditorSchemaDtoSchema,
    envelopeSchemaVersion: z.literal(1),
    excludedFieldKinds: z.tuple([
      z.literal('identity'),
      z.literal('pointer'),
      z.literal('archiveOffset'),
      z.literal('unknown'),
      z.literal('presentation')
    ]),
    producerVersion: boundedText(64),
    rows: z.array(rowClipboardLogicalRowV1DtoSchema).min(1).max(128),
    scope: rowClipboardScopeDtoSchema,
    source: rowClipboardSourceV1DtoSchema
  })
  .superRefine((envelope, context) => {
    const totalValueCount = envelope.rows.reduce(
      (total, row) => total + row.values.length,
      0
    );
    if (totalValueCount > 4096) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Logical-row value count exceeds the supported bound.',
        path: ['rows']
      });
    }
  });

export const rowClipboardPasteTargetDtoSchema = z.strictObject({
  kind: stableIdentifierSchema,
  personalId: z.number().int().nonnegative().nullable().optional(),
  slot: z.number().int().nonnegative().max(127).nullable().optional(),
  tableId: boundedText(384).nullable().optional(),
  trainerId: z.number().int().nonnegative().nullable().optional()
});

export const prepareRowClipboardCopyRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const prepareRowClipboardCopyResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  scope: rowClipboardScopeDtoSchema.nullable(),
  sourceRevision: z.string().max(512)
});

export const previewRowClipboardPasteRequestSchema = z.strictObject({
  envelope: rowClipboardEnvelopeV1DtoSchema,
  mode: z.enum(['replace', 'insert', 'append', 'merge']),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  target: rowClipboardPasteTargetDtoSchema
});

export const rowClipboardPreviewRowDtoSchema = z.strictObject({
  after: z.array(rowClipboardOwnedValueDtoSchema).max(64),
  before: z.array(rowClipboardOwnedValueDtoSchema).max(64),
  targetIdentity: rowClipboardLogicalIdentityDtoSchema
});

export const rowClipboardPastePreviewDtoSchema = z
  .strictObject({
    atomicHistoryEvent: z.literal(true),
    authorizationId: z.union([upperSha256Schema, z.literal('')]),
    canStage: z.boolean(),
    clipboardChecksum: upperSha256Schema,
    editor: rowClipboardEditorSchemaDtoSchema,
    mode: z.enum(['replace', 'insert', 'append', 'merge']),
    operationCount: z.number().int().min(1).max(128),
    previewSchemaVersion: z.literal(1),
    rows: z.array(rowClipboardPreviewRowDtoSchema).max(128),
    scope: rowClipboardScopeDtoSchema,
    targetIdentity: rowClipboardLogicalIdentityDtoSchema,
    targetRevision: upperSha256Schema
  })
  .superRefine((preview, context) => {
    if (preview.canStage !== (preview.authorizationId.length === 64)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Logical-row authorization state is inconsistent.',
        path: ['authorizationId']
      });
    }
  });

export const previewRowClipboardPasteResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  preview: rowClipboardPastePreviewDtoSchema.nullable()
});

export const stageRowClipboardPasteRequestSchema = z.strictObject({
  authorizationId: upperSha256Schema,
  envelope: rowClipboardEnvelopeV1DtoSchema,
  expectedTargetRevision: upperSha256Schema,
  mode: z.enum(['replace', 'insert', 'append', 'merge']),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  target: rowClipboardPasteTargetDtoSchema
});

export const rowClipboardStageReceiptDtoSchema = z.strictObject({
  atomicHistoryEvent: z.literal(true),
  clipboardChecksum: upperSha256Schema,
  historyEventId: z.string().regex(/^[a-f0-9]{32}$/u),
  operationCount: z.number().int().min(1).max(128),
  targetRevision: upperSha256Schema
});

export const stageRowClipboardPasteResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  receipt: rowClipboardStageReceiptDtoSchema.nullable(),
  session: editSessionSchema
});

export const clearRowClipboardAuthorizationsRequestSchema = z.strictObject({
  paths: projectPathsSchema.nullable()
});

export const clearRowClipboardAuthorizationsResponseSchema = z.strictObject({
  clearedCount: z.number().int().nonnegative().max(128)
});

export type RowClipboardScopeDto = z.infer<typeof rowClipboardScopeDtoSchema>;
export type RowClipboardEditorSchemaDto = z.infer<
  typeof rowClipboardEditorSchemaDtoSchema
>;
export type RowClipboardLogicalIdentityDto = z.infer<
  typeof rowClipboardLogicalIdentityDtoSchema
>;
export type RowClipboardOwnedValueDto = z.infer<typeof rowClipboardOwnedValueDtoSchema>;
export type RowClipboardEnvelopeV1Dto = z.infer<typeof rowClipboardEnvelopeV1DtoSchema>;
export type RowClipboardPasteTargetDto = z.infer<typeof rowClipboardPasteTargetDtoSchema>;
export type PrepareRowClipboardCopyRequest = z.infer<
  typeof prepareRowClipboardCopyRequestSchema
>;
export type PrepareRowClipboardCopyResponse = z.infer<
  typeof prepareRowClipboardCopyResponseSchema
>;
export type PreviewRowClipboardPasteRequest = z.infer<
  typeof previewRowClipboardPasteRequestSchema
>;
export type PreviewRowClipboardPasteResponse = z.infer<
  typeof previewRowClipboardPasteResponseSchema
>;
export type RowClipboardPastePreviewDto = z.infer<
  typeof rowClipboardPastePreviewDtoSchema
>;
export type StageRowClipboardPasteRequest = z.infer<
  typeof stageRowClipboardPasteRequestSchema
>;
export type StageRowClipboardPasteResponse = z.infer<
  typeof stageRowClipboardPasteResponseSchema
>;
export type ClearRowClipboardAuthorizationsRequest = z.infer<
  typeof clearRowClipboardAuthorizationsRequestSchema
>;
export type ClearRowClipboardAuthorizationsResponse = z.infer<
  typeof clearRowClipboardAuthorizationsResponseSchema
>;
