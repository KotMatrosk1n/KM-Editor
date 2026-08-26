/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';

export const rowClipboardEnvelopeSchemaVersion = 1 as const;
export const rowClipboardPreviewSchemaVersion = 1 as const;
export const rowClipboardMaximumRowCount = 128;
export const rowClipboardMaximumValueCountPerRow = 64;
export const rowClipboardMaximumTotalValueCount = 4096;
export const rowClipboardMaximumDependencyCount = 512;
export const rowClipboardMaximumCanonicalPayloadBytes = 256 * 1024;

export const rowClipboardExcludedFieldKinds = [
  'identity',
  'pointer',
  'archiveOffset',
  'unknown',
  'presentation'
] as const;

export type RowClipboardExcludedFieldKind =
  (typeof rowClipboardExcludedFieldKinds)[number];

export type RowClipboardGameFamily =
  | 'swordShield'
  | 'scarletViolet'
  | 'legendsZA';

export type RowClipboardPasteMode = 'replace' | 'insert' | 'append' | 'merge';

export type RowClipboardDependencyReference = Readonly<{
  form: string | null;
  id: string;
  kind: string;
}>;

export type RowClipboardBooleanValue = Readonly<{
  kind: 'boolean';
  value: boolean;
}>;

export type RowClipboardSignedIntegerValue = Readonly<{
  kind: 'signedInteger';
  value: string;
}>;

export type RowClipboardUnsignedIntegerValue = Readonly<{
  kind: 'unsignedInteger';
  value: string;
}>;

export type RowClipboardDecimalValue = Readonly<{
  kind: 'decimal';
  value: string;
}>;

export type RowClipboardStringValue = Readonly<{
  kind: 'string';
  value: string;
}>;

export type RowClipboardDependencyValue = Readonly<{
  kind: 'dependencyReference';
  value: RowClipboardDependencyReference;
}>;

export type RowClipboardValue =
  | RowClipboardBooleanValue
  | RowClipboardSignedIntegerValue
  | RowClipboardUnsignedIntegerValue
  | RowClipboardDecimalValue
  | RowClipboardStringValue
  | RowClipboardDependencyValue;

export type RowClipboardValueKind = RowClipboardValue['kind'];

export type RowClipboardOwnedValue = Readonly<{
  fieldKey: string;
  value: RowClipboardValue;
}>;

export type RowClipboardLogicalIdentity = Readonly<{
  key: string;
  kind: string;
}>;

export type RowClipboardLogicalRowV1 = Readonly<{
  sourceIdentity: RowClipboardLogicalIdentity;
  values: readonly RowClipboardOwnedValue[];
}>;

export type RowClipboardScope = Readonly<{
  game: ProjectGame;
  profileId: string;
  projectId: string;
}>;

export type RowClipboardEnvelopeScopeV1 = RowClipboardScope &
  Readonly<{
    gameFamily: RowClipboardGameFamily;
  }>;

export type RowClipboardEditorSchemaRef = Readonly<{
  editorId: string;
  rowKind: string;
  rowSchemaVersion: number;
}>;

export type RowClipboardSourceV1 = Readonly<{
  logicalIdentity: RowClipboardLogicalIdentity;
  projectRevision: string;
}>;

export type RowClipboardEnvelopePayloadV1 = Readonly<{
  dependencies: readonly RowClipboardDependencyReference[];
  editor: RowClipboardEditorSchemaRef;
  envelopeSchemaVersion: typeof rowClipboardEnvelopeSchemaVersion;
  excludedFieldKinds: readonly RowClipboardExcludedFieldKind[];
  producerVersion: string;
  rows: readonly RowClipboardLogicalRowV1[];
  scope: RowClipboardEnvelopeScopeV1;
  source: RowClipboardSourceV1;
}>;

export type RowClipboardEnvelopeV1 = RowClipboardEnvelopePayloadV1 &
  Readonly<{
    checksum: string;
  }>;

export type RowClipboardEnvelopeInputV1 = Readonly<{
  dependencies: readonly RowClipboardDependencyReference[];
  editor: RowClipboardEditorSchemaRef;
  producerVersion: string;
  rows: readonly RowClipboardLogicalRowV1[];
  scope: RowClipboardScope;
  source: RowClipboardSourceV1;
}>;

export type RowClipboardFieldPolicy = Readonly<{
  fieldKey: string;
  maximumUtf8Bytes: number | null;
  valueKinds: readonly RowClipboardValueKind[];
}>;

export type RowClipboardAdapterRegistration = Readonly<{
  dependencyKinds: readonly string[];
  editorId: string;
  fieldPolicies: readonly RowClipboardFieldPolicy[];
  games: readonly ProjectGame[];
  maximumRows: number;
  maximumTotalValues: number;
  maximumValuesPerRow: number;
  pasteModes: readonly RowClipboardPasteMode[];
  profileIds: readonly string[] | null;
  rowKind: string;
  rowSchemaVersion: number;
}>;

export type RowClipboardCopyRequestV1 = Omit<
  RowClipboardEnvelopeInputV1,
  'scope'
>;

export type RowClipboardPreviewRequestV1 = Readonly<{
  mode: RowClipboardPasteMode;
  targetIdentity: RowClipboardLogicalIdentity;
  targetRevision: string;
}>;

export type RowClipboardBoundPreviewV1 = Readonly<{
  atomicHistoryEvent: true;
  clipboardChecksum: string;
  editor: RowClipboardEditorSchemaRef;
  mode: RowClipboardPasteMode;
  operationCount: number;
  previewSchemaVersion: typeof rowClipboardPreviewSchemaVersion;
  scope: RowClipboardEnvelopeScopeV1;
  targetIdentity: RowClipboardLogicalIdentity;
  targetRevision: string;
}>;

export type RowClipboardCommitAuthorizationV1 = Readonly<{
  atomicHistoryEvent: true;
  envelope: RowClipboardEnvelopeV1;
  mode: RowClipboardPasteMode;
  operationCount: number;
  targetIdentity: RowClipboardLogicalIdentity;
  targetRevision: string;
}>;

export type RowClipboardControllerSnapshot = Readonly<{
  clipboard: RowClipboardEnvelopeV1 | null;
  preview: RowClipboardBoundPreviewV1 | null;
  scope: RowClipboardScope;
}>;

export type RowClipboardScopeUpdateResult =
  | Readonly<{ kind: 'preserved' }>
  | Readonly<{
      kind: 'reset';
      reason: 'project-changed' | 'game-changed' | 'profile-changed';
    }>;

export type RowClipboardRegistrationUpdateResult =
  | Readonly<{ kind: 'preserved' }>
  | Readonly<{
      kind: 'reset';
      reason: 'adapter-removed' | 'schema-incompatible';
    }>;

export type RowClipboardIssueCode =
  | 'adapter-unavailable'
  | 'checksum-mismatch'
  | 'checksum-unavailable'
  | 'clipboard-empty'
  | 'dependency-limit-exceeded'
  | 'duplicate-dependency'
  | 'duplicate-field-key'
  | 'invalid-adapter-registration'
  | 'invalid-checksum'
  | 'invalid-dependency'
  | 'invalid-editor-schema'
  | 'invalid-envelope'
  | 'invalid-logical-identity'
  | 'invalid-preview'
  | 'invalid-revision'
  | 'invalid-scope'
  | 'invalid-value'
  | 'operation-unavailable'
  | 'payload-limit-exceeded'
  | 'preview-mismatch'
  | 'row-limit-exceeded'
  | 'schema-incompatible'
  | 'scope-changed-during-copy'
  | 'target-revision-stale'
  | 'unknown-field'
  | 'value-limit-exceeded';

export class RowClipboardError extends Error {
  public readonly code: RowClipboardIssueCode;

  public constructor(code: RowClipboardIssueCode, message?: string) {
    super(message ?? code);
    this.name = 'RowClipboardError';
    this.code = code;
  }
}
