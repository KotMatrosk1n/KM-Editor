/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { projectGameSchema } from './contracts';

export const workspaceDraftSchemaVersion = 1 as const;
export const workspaceDraftMaximumCount = 256;
export const workspaceDraftMaximumIdentifierLength = 256;
export const workspaceDraftMaximumStableIdLength = 1024;
export const workspaceDraftMaximumEntityIdLength = 4096;
export const workspaceDraftExpectedDocumentBytes = 3 * 1024 * 1024;
export const workspaceDraftProvisionedDocumentBytes = workspaceDraftExpectedDocumentBytes * 4;
export const workspaceDraftMaximumDocumentBytes = workspaceDraftProvisionedDocumentBytes * 2;
export const workspaceDraftExpectedPayloadBytes = 512 * 1024;
export const workspaceDraftProvisionedPayloadBytes = workspaceDraftExpectedPayloadBytes * 4;
export const workspaceDraftMaximumPayloadBytes = workspaceDraftProvisionedPayloadBytes * 2;

type JsonValue =
  | boolean
  | number
  | string
  | null
  | JsonValue[]
  | { [key: string]: JsonValue };

const jsonPrimitiveSchema = z.union([z.boolean(), z.number().finite(), z.string(), z.null()]);
const jsonValueSchema: z.ZodType<JsonValue> = z.lazy(() =>
  z.union([jsonPrimitiveSchema, z.array(jsonValueSchema), z.record(z.string(), jsonValueSchema)])
);
const boundedIdentifierSchema = z
  .string()
  .min(1)
  .max(workspaceDraftMaximumIdentifierLength)
  .refine((value) => value.trim() === value, {
    message: 'Identifiers cannot have surrounding whitespace.'
  })
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value), {
    message: 'Identifiers cannot contain control characters.'
  });
const boundedEntityIdSchema = z
  .string()
  .min(1)
  .max(workspaceDraftMaximumEntityIdLength)
  .refine((value) => value.trim() === value, {
    message: 'Entity IDs cannot have surrounding whitespace.'
  })
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value), {
    message: 'Entity IDs cannot contain control characters.'
  });
const boundedStableIdSchema = z
  .string()
  .min(1)
  .max(workspaceDraftMaximumStableIdLength)
  .refine((value) => value.trim() === value, {
    message: 'Stable IDs cannot have surrounding whitespace.'
  })
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value), {
    message: 'Stable IDs cannot contain control characters.'
  });
const dateTimeOffsetSchema = z.string().refine(
  (value) =>
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(value) &&
    Number.isFinite(Date.parse(value)),
  { message: 'Expected an ISO 8601 timestamp with an offset.' }
);
const sha256FingerprintSchema = z.string().regex(/^[a-fA-F0-9]{64}$/u);

export const workspaceDraftKeySchema = z.strictObject({
  changeSetId: boundedStableIdSchema,
  domain: boundedIdentifierSchema,
  entityId: boundedEntityIdSchema,
  game: projectGameSchema,
  section: boundedIdentifierSchema
});

export const workspaceDraftEntrySchema = z.strictObject({
  adapterId: boundedIdentifierSchema,
  adapterSchemaVersion: z.number().int().positive(),
  key: workspaceDraftKeySchema,
  payload: jsonValueSchema,
  projectSourceRevisionFingerprint: sha256FingerprintSchema.nullable().optional(),
  updatedAtUtc: dateTimeOffsetSchema
});

export const workspaceDraftDocumentSchema = z
  .strictObject({
    drafts: z.array(workspaceDraftEntrySchema).max(workspaceDraftMaximumCount),
    schemaVersion: z.literal(workspaceDraftSchemaVersion),
    updatedAtUtc: dateTimeOffsetSchema
  })
  .superRefine((document, context) => {
    const keys = new Set<string>();
    document.drafts.forEach((draft, index) => {
      const payloadBytes = new TextEncoder().encode(JSON.stringify(draft.payload)).byteLength;
      if (payloadBytes > workspaceDraftMaximumPayloadBytes) {
        context.addIssue({
          code: 'custom',
          message: `A workspace draft payload cannot exceed ${workspaceDraftMaximumPayloadBytes} bytes.`,
          path: ['drafts', index, 'payload']
        });
      }

      const key = JSON.stringify(draft.key);
      if (keys.has(key)) {
        context.addIssue({
          code: 'custom',
          message: 'Workspace draft keys must be unique.',
          path: ['drafts', index, 'key']
        });
      }
      keys.add(key);
    });

  });

export const readWorkspaceDraftsRequestSchema = z.strictObject({
  projectId: boundedIdentifierSchema
});
export const readWorkspaceDraftsResponseSchema = z
  .strictObject({
    document: workspaceDraftDocumentSchema.nullable(),
    etag: sha256FingerprintSchema.nullable(),
    exists: z.boolean()
  })
  .refine(
    (response) =>
      response.exists === (response.document !== null) &&
      response.exists === (response.etag !== null),
    { message: 'Workspace draft existence must match document and ETag presence.' }
  );

export const writeWorkspaceDraftsRequestSchema = z.strictObject({
  document: workspaceDraftDocumentSchema,
  expectedETag: sha256FingerprintSchema.nullable(),
  projectId: boundedIdentifierSchema
});
export const writeWorkspaceDraftsResponseSchema = z.strictObject({
  etag: sha256FingerprintSchema,
  writtenAtUtc: dateTimeOffsetSchema
});

export const deleteWorkspaceDraftsRequestSchema = z.strictObject({
  expectedETag: sha256FingerprintSchema.nullable(),
  projectId: boundedIdentifierSchema
});
export const deleteWorkspaceDraftsResponseSchema = z.strictObject({
  deleted: z.boolean()
});

export type WorkspaceDraftKey = z.infer<typeof workspaceDraftKeySchema>;
export type WorkspaceDraftEntry = z.infer<typeof workspaceDraftEntrySchema>;
export type WorkspaceDraftDocument = z.infer<typeof workspaceDraftDocumentSchema>;
export type ReadWorkspaceDraftsRequest = z.infer<typeof readWorkspaceDraftsRequestSchema>;
export type ReadWorkspaceDraftsResponse = z.infer<typeof readWorkspaceDraftsResponseSchema>;
export type WriteWorkspaceDraftsRequest = z.infer<typeof writeWorkspaceDraftsRequestSchema>;
export type WriteWorkspaceDraftsResponse = z.infer<typeof writeWorkspaceDraftsResponseSchema>;
export type DeleteWorkspaceDraftsRequest = z.infer<typeof deleteWorkspaceDraftsRequestSchema>;
export type DeleteWorkspaceDraftsResponse = z.infer<typeof deleteWorkspaceDraftsResponseSchema>;
