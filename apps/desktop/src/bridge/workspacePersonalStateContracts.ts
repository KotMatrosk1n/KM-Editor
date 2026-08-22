/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  changePlanOutputModeSchema,
  projectGameSchema,
  projectPathsSchema
} from './contracts';
import { isCapabilityRegisteredForGame } from '../workbench/capabilityRegistry';
import { workbenchSections } from '../workbench/workbenchSections';

export const workspacePersonalStateSchemaVersion = 1 as const;
export const workspaceApplicationStateExpectedBytes = 3 * 1024 * 1024;
export const workspaceApplicationStateProvisionedBytes =
  workspaceApplicationStateExpectedBytes * 4;
export const workspaceApplicationStateMaximumBytes =
  workspaceApplicationStateProvisionedBytes * 2;
export const workspaceProjectStateExpectedBytes = 2 * 1024 * 1024;
export const workspaceProjectStateProvisionedBytes = workspaceProjectStateExpectedBytes * 4;
export const workspaceProjectStateMaximumBytes = workspaceProjectStateProvisionedBytes * 2;
export const workspaceMaximumRecentProjects = 24;
export const workspaceMaximumShortcutOverrides = 128;
export const workspaceMaximumLocalePacks = 4;
export const workspaceExpectedLocalePackBytes = 512 * 1024;
export const workspaceProvisionedLocalePackBytes = workspaceExpectedLocalePackBytes * 4;
export const workspaceMaximumLocalePackBytes = workspaceProvisionedLocalePackBytes * 2;
export const workspaceExpectedLocalePackAggregateBytes = 2 * 1024 * 1024;
export const workspaceProvisionedLocalePackAggregateBytes =
  workspaceExpectedLocalePackAggregateBytes * 4;
export const workspaceMaximumLocalePackAggregateBytes =
  workspaceProvisionedLocalePackAggregateBytes * 2;
export const workspaceMaximumGameDumpDestinations = 5;
export const workspaceMaximumRecentTargets = 64;
export const workspaceMaximumBookmarks = 256;
export const workspaceMaximumNotes = 256;
export const workspaceExpectedNoteBytes = 32 * 1024;
export const workspaceProvisionedNoteBytes = workspaceExpectedNoteBytes * 4;
export const workspaceMaximumNoteBytes = workspaceProvisionedNoteBytes * 2;
export const workspaceExpectedAggregateNoteBytes = 1024 * 1024;
export const workspaceProvisionedAggregateNoteBytes = workspaceExpectedAggregateNoteBytes * 4;
export const workspaceMaximumAggregateNoteBytes = workspaceProvisionedAggregateNoteBytes * 2;
export const workspaceMaximumSavedViews = 128;
export const workspaceExpectedSavedViewPayloadBytes = 64 * 1024;
export const workspaceProvisionedSavedViewPayloadBytes = workspaceExpectedSavedViewPayloadBytes * 4;
export const workspaceMaximumSavedViewPayloadBytes = workspaceProvisionedSavedViewPayloadBytes * 2;
export const workspaceExpectedAggregateSavedViewPayloadBytes = 512 * 1024;
export const workspaceProvisionedAggregateSavedViewPayloadBytes =
  workspaceExpectedAggregateSavedViewPayloadBytes * 4;
export const workspaceMaximumAggregateSavedViewPayloadBytes =
  workspaceProvisionedAggregateSavedViewPayloadBytes * 2;
export const workspaceMaximumOutputProfiles = 32;

type JsonValue =
  | boolean
  | number
  | string
  | null
  | JsonValue[]
  | { [key: string]: JsonValue };

const textEncoder = new TextEncoder();
const dateTimeOffsetSchema = z.string().refine(
  (value) =>
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(value) &&
    Number.isFinite(Date.parse(value)),
  { message: 'Expected an ISO 8601 timestamp with an offset.' }
);
const sha256FingerprintSchema = z.string().regex(/^[a-fA-F0-9]{64}$/u);
const controlCharacterPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const disallowedTextControlPattern = /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/u;
const disallowedUnicodeFormatPattern = /[\u061c\u200b-\u200f\u202a-\u202e\u2060\u2066-\u2069\ufeff]/u;
export const workspaceProjectIdSchema = z.string().regex(/^km1_[a-f0-9]{64}$/u);
const stableIdSchema = z
  .string()
  .min(1)
  .max(1024)
  .refine((value) => value.trim() === value && !controlCharacterPattern.test(value));
const displayNameSchema = z
  .string()
  .min(1)
  .max(256)
  .refine((value) => value.trim() === value && !controlCharacterPattern.test(value));
const contractKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[A-Za-z0-9][A-Za-z0-9._-]*$/u);
const semanticContractKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u);
const boundedPathSchema = z
  .string()
  .min(1)
  .max(32767)
  .refine((value) => value.trim() === value && !controlCharacterPattern.test(value));
const fullyQualifiedPathSchema = boundedPathSchema.refine(
  (value) => /^(?:[A-Za-z]:[\\/]|\\\\|\/)/u.test(value),
  { message: 'Expected a fully qualified path.' }
);
const boundedJsonStringSchema = z
  .string()
  .max(4096)
  .refine((value) => !disallowedTextControlPattern.test(value));
const jsonObjectKeySchema = z
  .string()
  .min(1)
  .max(128)
  .refine((value) => !controlCharacterPattern.test(value));
const jsonPrimitiveSchema = z.union([
  z.boolean(),
  z.number().finite(),
  boundedJsonStringSchema,
  z.null()
]);
const jsonValueSchema: z.ZodType<JsonValue> = z.lazy(() =>
  z.union([
    jsonPrimitiveSchema,
    z.array(jsonValueSchema),
    z.record(jsonObjectKeySchema, jsonValueSchema)
  ])
);

export const workspaceSemanticRecordKindSchema = z.strictObject({
  key: semanticContractKeySchema,
  schemaVersion: z.number().int().positive().max(2_147_483_647)
});

export const workspaceSemanticRecordRefSchema = z.strictObject({
  domain: semanticContractKeySchema,
  gameFamily: z.enum(['swordShield', 'scarletViolet', 'legendsZA']),
  recordId: stableIdSchema.max(4096),
  recordKind: workspaceSemanticRecordKindSchema,
  subrecordId: stableIdSchema.max(4096).nullable()
});

const inspectorTabSchema = z.enum([
  'compare',
  'references',
  'impact',
  'history',
  'notes',
  'provenance'
]);

export const workspaceScopedLocationSchema = z
  .strictObject({
    changeSetId: stableIdSchema.nullable().optional(),
    entity: workspaceSemanticRecordRefSchema.nullable().optional(),
    game: projectGameSchema,
    inspectorTab: inspectorTabSchema.nullable().optional(),
    section: z.enum(workbenchSections),
    subcontext: z
      .record(contractKeySchema, jsonPrimitiveSchema)
      .refine((value) => Object.keys(value).length <= 32)
      .refine((value) =>
        Object.values(value).every(
          (entry) => typeof entry !== 'string' || entry.length <= 4096
        )
      )
      .nullable()
      .optional(),
    version: z.literal(1)
  })
  .superRefine((location, context) => {
    if (!isCapabilityRegisteredForGame(location.section, location.game)) {
      context.addIssue({ code: 'custom', message: 'Location section is unavailable for its game.' });
    }
    if (location.entity && location.entity.gameFamily !== projectGameFamily(location.game)) {
      context.addIssue({ code: 'custom', message: 'Location entity has the wrong game family.' });
    }
  });

export const workspaceRecentProjectProfileSchema = z
  .strictObject({
    game: projectGameSchema,
    lastOpenedAtUtc: dateTimeOffsetSchema,
    name: displayNameSchema.nullable(),
    paths: projectPathsSchema,
    projectId: workspaceProjectIdSchema
  })
  .superRefine((profile, context) => {
    if (profile.paths.selectedGame !== profile.game) {
      context.addIssue({ code: 'custom', message: 'Recent project game must match its path scope.' });
    }
    for (const path of [profile.paths.baseRomFsPath, profile.paths.baseExeFsPath]) {
      if (!path || !fullyQualifiedPathSchema.safeParse(path).success) {
        context.addIssue({ code: 'custom', message: 'Recent project source paths must be fully qualified.' });
      }
    }
    for (const path of [
      profile.paths.outputRootPath,
      profile.paths.saveFilePath,
      profile.paths.scarletVioletSupportFolderPath,
      profile.paths.pokemonLegendsZASupportFolderPath
    ]) {
      if (path !== null && path !== undefined && !fullyQualifiedPathSchema.safeParse(path).success) {
        context.addIssue({ code: 'custom', message: 'Recent project paths must be fully qualified.' });
      }
    }
    if (
      profile.game !== 'scarlet' &&
      profile.game !== 'violet' &&
      profile.paths.scarletVioletSupportFolderPath
    ) {
      context.addIssue({ code: 'custom', message: 'Recent project has an unrelated support path.' });
    }
    if (profile.game !== 'za' && profile.paths.pokemonLegendsZASupportFolderPath) {
      context.addIssue({ code: 'custom', message: 'Recent project has an unrelated support path.' });
    }
    if (
      profile.paths.gameTextLanguage !== null &&
      profile.paths.gameTextLanguage !== undefined &&
      !displayNameSchema.max(64).safeParse(profile.paths.gameTextLanguage).success
    ) {
      context.addIssue({ code: 'custom', message: 'Recent project text language is invalid.' });
    }
  });

export const workspaceShortcutOverrideSchema = z.strictObject({
  commandId: contractKeySchema,
  shortcut: z
    .string()
    .min(1)
    .max(128)
    .refine((value) => value.trim() === value && !controlCharacterPattern.test(value)),
  updatedAtUtc: dateTimeOffsetSchema
});

export const workspaceGameDumpDestinationSchema = z.strictObject({
  destinationPath: fullyQualifiedPathSchema,
  game: projectGameSchema,
  updatedAtUtc: dateTimeOffsetSchema
});

const localeDictionarySchema = z
  .record(
    z.string().min(1).max(1024).refine(
      (value) =>
        value.normalize('NFC') === value &&
        !controlCharacterPattern.test(value) &&
        !disallowedUnicodeFormatPattern.test(value)
    ),
    z.string().max(8192).refine(
      (value) =>
        value.normalize('NFC') === value &&
        !controlCharacterPattern.test(value) &&
        !disallowedUnicodeFormatPattern.test(value)
    )
  );

export const workspaceLocalePackSchema = z
  .strictObject({
    direction: z.literal('ltr'),
    displayName: displayNameSchema.max(64).refine(
      (value) =>
        value.normalize('NFC') === value && !disallowedUnicodeFormatPattern.test(value)
    ),
    gameTextLanguage: z.enum(['en', 'es', 'fr', 'de', 'ru', 'uk', 'zh']),
    id: z.string().regex(/^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?$/u),
    keys: localeDictionarySchema,
    literals: localeDictionarySchema,
    localeTag: z
      .string()
      .min(2)
      .max(64)
      .regex(/^[A-Za-z0-9]{1,8}(?:-[A-Za-z0-9]{1,8}){0,7}$/u),
    schemaVersion: z.literal(1)
  })
  .superRefine((pack, context) => {
    if (Object.keys(pack.keys).length + Object.keys(pack.literals).length > 8192) {
      context.addIssue({ code: 'custom', message: 'Locale pack has too many entries.' });
    }
    if (utf8Bytes(pack) > workspaceMaximumLocalePackBytes) {
      context.addIssue({ code: 'custom', message: 'Locale pack exceeds its byte limit.' });
    }
  });

export const workspaceApplicationStateDocumentSchema = z
  .strictObject({
    gameDumpDestinations: z
      .array(workspaceGameDumpDestinationSchema)
      .max(workspaceMaximumGameDumpDestinations),
    localePacks: z.array(workspaceLocalePackSchema).max(workspaceMaximumLocalePacks),
    recentProjects: z
      .array(workspaceRecentProjectProfileSchema)
      .max(workspaceMaximumRecentProjects),
    schemaVersion: z.literal(workspacePersonalStateSchemaVersion),
    shortcutOverrides: z
      .array(workspaceShortcutOverrideSchema)
      .max(workspaceMaximumShortcutOverrides),
    updatedAtUtc: dateTimeOffsetSchema
  })
  .superRefine((document, context) => {
    requireUnique(document.recentProjects, (profile) => profile.projectId, context, ['recentProjects']);
    requireUnique(
      document.gameDumpDestinations,
      (destination) => destination.game,
      context,
      ['gameDumpDestinations']
    );
    requireUnique(
      document.shortcutOverrides,
      (shortcut) => shortcut.commandId,
      context,
      ['shortcutOverrides']
    );
    requireUnique(document.localePacks, (pack) => pack.id, context, ['localePacks']);
    requireUnique(
      document.localePacks,
      (pack) => pack.localeTag.toLowerCase(),
      context,
      ['localePacks']
    );
    const localeBytes = document.localePacks.reduce(
      (total, pack) => total + utf8Bytes(pack),
      0
    );
    if (localeBytes > workspaceMaximumLocalePackAggregateBytes) {
      context.addIssue({ code: 'custom', message: 'Locale packs exceed their aggregate byte limit.' });
    }
  });

export const workspaceBookmarkSchema = z.strictObject({
  bookmarkId: stableIdSchema,
  createdAtUtc: dateTimeOffsetSchema,
  kind: z.enum(['pin', 'bookmark']),
  label: displayNameSchema.nullable(),
  location: workspaceScopedLocationSchema,
  updatedAtUtc: dateTimeOffsetSchema
}).refine((bookmark) => Date.parse(bookmark.updatedAtUtc) >= Date.parse(bookmark.createdAtUtc), {
  message: 'Bookmark update time cannot precede its creation time.'
});

export const workspaceProjectNoteSchema = z.strictObject({
  body: z.string()
    .refine((value) => !disallowedTextControlPattern.test(value))
    .refine((value) => textEncoder.encode(value).byteLength <= workspaceMaximumNoteBytes),
  location: workspaceScopedLocationSchema,
  noteId: stableIdSchema,
  updatedAtUtc: dateTimeOffsetSchema
});

export const workspaceSavedViewSchema = z
  .strictObject({
    adapterId: contractKeySchema,
    adapterSchemaVersion: z.number().int().positive().max(2_147_483_647),
    location: workspaceScopedLocationSchema,
    name: displayNameSchema,
    payload: jsonValueSchema,
    updatedAtUtc: dateTimeOffsetSchema,
    viewId: stableIdSchema
  })
  .superRefine((view, context) => {
    if (utf8Bytes(view.payload) > workspaceMaximumSavedViewPayloadBytes) {
      context.addIssue({ code: 'custom', message: 'Saved view payload exceeds its byte limit.' });
    }
    if (!jsonComplexityWithinBounds(view.payload)) {
      context.addIssue({ code: 'custom', message: 'Saved view payload is too complex.' });
    }
  });

export const workspaceRecentTargetSchema = z.strictObject({
  location: workspaceScopedLocationSchema,
  visitedAtUtc: dateTimeOffsetSchema
});

export const workspaceOutputProfileSchema = z.strictObject({
  name: displayNameSchema,
  outputMode: changePlanOutputModeSchema.nullable(),
  outputRootPath: fullyQualifiedPathSchema,
  profileId: stableIdSchema,
  updatedAtUtc: dateTimeOffsetSchema
});

export const workspaceProjectPersonalStateDocumentSchema = z
  .strictObject({
    activeOutputProfileId: stableIdSchema.nullable(),
    bookmarks: z.array(workspaceBookmarkSchema).max(workspaceMaximumBookmarks),
    game: projectGameSchema,
    notes: z.array(workspaceProjectNoteSchema).max(workspaceMaximumNotes),
    outputProfiles: z.array(workspaceOutputProfileSchema).max(workspaceMaximumOutputProfiles),
    recentTargets: z.array(workspaceRecentTargetSchema).max(workspaceMaximumRecentTargets),
    savedViews: z.array(workspaceSavedViewSchema).max(workspaceMaximumSavedViews),
    schemaVersion: z.literal(workspacePersonalStateSchemaVersion),
    updatedAtUtc: dateTimeOffsetSchema
  })
  .superRefine((document, context) => {
    requireUnique(document.bookmarks, (entry) => entry.bookmarkId, context, ['bookmarks']);
    requireUnique(document.bookmarks, workspaceBookmarkTargetKey, context, ['bookmarks']);
    requireUnique(document.notes, (entry) => entry.noteId, context, ['notes']);
    requireUnique(
      document.notes,
      (entry) => workspaceScopedLocationKey(entry.location),
      context,
      ['notes']
    );
    requireUnique(document.savedViews, (entry) => entry.viewId, context, ['savedViews']);
    requireUnique(document.outputProfiles, (entry) => entry.profileId, context, ['outputProfiles']);
    requireUnique(
      document.recentTargets,
      (entry) => workspaceScopedLocationKey(entry.location),
      context,
      ['recentTargets']
    );
    for (const collection of [
      document.bookmarks.map((entry) => entry.location),
      document.notes.map((entry) => entry.location),
      document.savedViews.map((entry) => entry.location),
      document.recentTargets.map((entry) => entry.location)
    ]) {
      if (collection.some((location) => location.game !== document.game)) {
        context.addIssue({ code: 'custom', message: 'Saved location has the wrong project game.' });
      }
    }
    if (
      document.activeOutputProfileId !== null &&
      !document.outputProfiles.some((profile) => profile.profileId === document.activeOutputProfileId)
    ) {
      context.addIssue({ code: 'custom', message: 'Active output profile does not exist.' });
    }
    const noteBytes = document.notes.reduce(
      (total, note) => total + textEncoder.encode(note.body).byteLength,
      0
    );
    if (noteBytes > workspaceMaximumAggregateNoteBytes) {
      context.addIssue({ code: 'custom', message: 'Project notes exceed their aggregate byte limit.' });
    }
    const savedViewBytes = document.savedViews.reduce(
      (total, view) => total + utf8Bytes(view.payload),
      0
    );
    if (savedViewBytes > workspaceMaximumAggregateSavedViewPayloadBytes) {
      context.addIssue({
        code: 'custom',
        message: 'Saved view payloads exceed their aggregate byte limit.'
      });
    }
  });

export const readWorkspaceApplicationStateRequestSchema = z.strictObject({});
export const readWorkspaceApplicationStateResponseSchema = z
  .strictObject({
    document: workspaceApplicationStateDocumentSchema.nullable(),
    etag: sha256FingerprintSchema.nullable(),
    exists: z.boolean()
  })
  .refine((value) => value.exists === (value.document !== null) && value.exists === (value.etag !== null));
export const writeWorkspaceApplicationStateRequestSchema = z.strictObject({
  document: workspaceApplicationStateDocumentSchema,
  expectedETag: sha256FingerprintSchema.nullable()
});
export const writeWorkspaceApplicationStateResponseSchema = z.strictObject({
  etag: sha256FingerprintSchema,
  writtenAtUtc: dateTimeOffsetSchema
});

export const readWorkspaceProjectStateRequestSchema = z.strictObject({
  projectId: workspaceProjectIdSchema
});
export const readWorkspaceProjectStateResponseSchema = z
  .strictObject({
    document: workspaceProjectPersonalStateDocumentSchema.nullable(),
    etag: sha256FingerprintSchema.nullable(),
    exists: z.boolean()
  })
  .refine((value) => value.exists === (value.document !== null) && value.exists === (value.etag !== null));
export const writeWorkspaceProjectStateRequestSchema = z.strictObject({
  document: workspaceProjectPersonalStateDocumentSchema,
  expectedETag: sha256FingerprintSchema.nullable(),
  projectId: workspaceProjectIdSchema
});
export const writeWorkspaceProjectStateResponseSchema = writeWorkspaceApplicationStateResponseSchema;
export const deleteWorkspaceProjectStateRequestSchema = z.strictObject({
  expectedETag: sha256FingerprintSchema.nullable(),
  projectId: workspaceProjectIdSchema
});
export const deleteWorkspaceProjectStateResponseSchema = z.strictObject({ deleted: z.boolean() });

export type WorkspaceScopedLocation = z.infer<typeof workspaceScopedLocationSchema>;
export type WorkspaceRecentProjectProfile = z.infer<typeof workspaceRecentProjectProfileSchema>;
export type WorkspaceShortcutOverride = z.infer<typeof workspaceShortcutOverrideSchema>;
export type WorkspaceGameDumpDestination = z.infer<typeof workspaceGameDumpDestinationSchema>;
export type WorkspaceLocalePack = z.infer<typeof workspaceLocalePackSchema>;
export type WorkspaceApplicationStateDocument = z.infer<typeof workspaceApplicationStateDocumentSchema>;
export type WorkspaceBookmark = z.infer<typeof workspaceBookmarkSchema>;
export type WorkspaceProjectNote = z.infer<typeof workspaceProjectNoteSchema>;
export type WorkspaceSavedView = z.infer<typeof workspaceSavedViewSchema>;
export type WorkspaceRecentTarget = z.infer<typeof workspaceRecentTargetSchema>;
export type WorkspaceOutputProfile = z.infer<typeof workspaceOutputProfileSchema>;
export type WorkspaceProjectPersonalStateDocument = z.infer<typeof workspaceProjectPersonalStateDocumentSchema>;
export type ReadWorkspaceApplicationStateRequest = z.infer<typeof readWorkspaceApplicationStateRequestSchema>;
export type ReadWorkspaceApplicationStateResponse = z.infer<typeof readWorkspaceApplicationStateResponseSchema>;
export type WriteWorkspaceApplicationStateRequest = z.infer<typeof writeWorkspaceApplicationStateRequestSchema>;
export type WriteWorkspaceApplicationStateResponse = z.infer<typeof writeWorkspaceApplicationStateResponseSchema>;
export type ReadWorkspaceProjectStateRequest = z.infer<typeof readWorkspaceProjectStateRequestSchema>;
export type ReadWorkspaceProjectStateResponse = z.infer<typeof readWorkspaceProjectStateResponseSchema>;
export type WriteWorkspaceProjectStateRequest = z.infer<typeof writeWorkspaceProjectStateRequestSchema>;
export type WriteWorkspaceProjectStateResponse = z.infer<typeof writeWorkspaceProjectStateResponseSchema>;
export type DeleteWorkspaceProjectStateRequest = z.infer<typeof deleteWorkspaceProjectStateRequestSchema>;
export type DeleteWorkspaceProjectStateResponse = z.infer<typeof deleteWorkspaceProjectStateResponseSchema>;

function projectGameFamily(game: z.infer<typeof projectGameSchema>) {
  if (game === 'sword' || game === 'shield') return 'swordShield';
  if (game === 'scarlet' || game === 'violet') return 'scarletViolet';
  return 'legendsZA';
}

function utf8Bytes(value: unknown) {
  return textEncoder.encode(JSON.stringify(value)).byteLength;
}

function requireUnique<T>(
  values: readonly T[],
  key: (value: T) => string,
  context: z.RefinementCtx,
  path: PropertyKey[]
) {
  const keys = new Set<string>();
  if (values.some((value) => {
    const identity = key(value);
    if (keys.has(identity)) return true;
    keys.add(identity);
    return false;
  })) {
    context.addIssue({ code: 'custom', message: 'Workspace entries must be unique.', path });
  }
}

function stableJson(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(stableJson).join(',')}]`;
  if (typeof value === 'object' && value !== null) {
    return `{${Object.entries(value)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, entry]) => `${JSON.stringify(key)}:${stableJson(entry)}`)
      .join(',')}}`;
  }
  return JSON.stringify(value);
}

export function workspaceScopedLocationKey(location: WorkspaceScopedLocation) {
  return stableJson({
    changeSetId: location.changeSetId ?? null,
    entity: location.entity ?? null,
    game: location.game,
    inspectorTab: location.inspectorTab ?? null,
    section: location.section,
    subcontext: location.subcontext ?? {},
    version: location.version
  });
}

export function workspaceBookmarkTargetKey(bookmark: WorkspaceBookmark) {
  return stableJson({
    kind: bookmark.kind,
    label: bookmark.kind === 'bookmark' ? bookmark.label : null,
    location: workspaceScopedLocationKey(bookmark.location)
  });
}

function jsonComplexityWithinBounds(value: JsonValue) {
  const pending: Array<{ depth: number; value: JsonValue }> = [{ depth: 1, value }];
  let nodes = 0;
  while (pending.length > 0) {
    const current = pending.pop()!;
    nodes += 1;
    if (nodes > 8192 || current.depth > 32) return false;
    if (Array.isArray(current.value)) {
      for (const entry of current.value) {
        pending.push({ depth: current.depth + 1, value: entry });
      }
    } else if (typeof current.value === 'object' && current.value !== null) {
      for (const entry of Object.values(current.value)) {
        pending.push({ depth: current.depth + 1, value: entry });
      }
    }
  }
  return true;
}
