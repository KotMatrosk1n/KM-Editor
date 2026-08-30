/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import { calculatePayloadBytesSha256 } from '../utils/pendingPayloadHash';
import {
  RowClipboardError,
  rowClipboardEnvelopeSchemaVersion,
  rowClipboardExcludedFieldKinds,
  rowClipboardMaximumCanonicalPayloadBytes,
  rowClipboardMaximumDependencyCount,
  rowClipboardMaximumRowCount,
  rowClipboardMaximumTotalValueCount,
  rowClipboardMaximumValueCountPerRow,
  type RowClipboardAdapterRegistration,
  type RowClipboardDependencyReference,
  type RowClipboardEditorSchemaRef,
  type RowClipboardEnvelopeInputV1,
  type RowClipboardEnvelopePayloadV1,
  type RowClipboardEnvelopeScopeV1,
  type RowClipboardEnvelopeV1,
  type RowClipboardFieldPolicy,
  type RowClipboardGameFamily,
  type RowClipboardLogicalIdentity,
  type RowClipboardLogicalRowV1,
  type RowClipboardOwnedValue,
  type RowClipboardPasteMode,
  type RowClipboardScope,
  type RowClipboardSourceV1,
  type RowClipboardValue,
  type RowClipboardValueKind
} from './rowClipboardTypes';

const projectGames = ['sword', 'shield', 'scarlet', 'violet', 'za'] as const;
const valueKinds = [
  'boolean',
  'signedInteger',
  'unsignedInteger',
  'decimal',
  'string',
  'dependencyReference'
] as const;
const pasteModes = ['replace', 'insert', 'append', 'merge'] as const;
const signedIntegerMinimum = -(1n << 63n);
const signedIntegerMaximum = (1n << 63n) - 1n;
const unsignedIntegerMaximum = (1n << 64n) - 1n;
const canonicalSignedIntegerPattern = /^(?:0|-[1-9][0-9]*|[1-9][0-9]*)$/u;
const canonicalUnsignedIntegerPattern = /^(?:0|[1-9][0-9]*)$/u;
const canonicalDecimalPattern =
  /^(?:0|-?[1-9][0-9]*|-?(?:0|[1-9][0-9]*)\.[0-9]*[1-9])$/u;
const stableIdentifierPattern = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/u;
const checksumPattern = /^[A-F0-9]{64}$/u;

export type RowClipboardHashFunction = (bytes: Uint8Array) => Promise<string>;

export async function createRowClipboardEnvelopeV1(
  input: RowClipboardEnvelopeInputV1,
  registration: RowClipboardAdapterRegistration,
  hash: RowClipboardHashFunction = sha256UpperHex
): Promise<RowClipboardEnvelopeV1> {
  const normalizedRegistration = normalizeRowClipboardAdapterRegistration(registration);
  const payload = normalizeEnvelopePayload(
    {
      dependencies: input.dependencies,
      editor: input.editor,
      envelopeSchemaVersion: rowClipboardEnvelopeSchemaVersion,
      excludedFieldKinds: rowClipboardExcludedFieldKinds,
      producerVersion: input.producerVersion,
      rows: input.rows,
      scope: {
        ...input.scope,
        gameFamily: rowClipboardGameFamily(input.scope.game)
      },
      source: input.source
    },
    normalizedRegistration
  );
  const canonicalBytes = canonicalRowClipboardPayloadBytes(payload);
  const checksum = await computeChecksum(canonicalBytes, hash);
  return deepFreeze({ ...payload, checksum });
}

export async function validateRowClipboardEnvelopeV1(
  input: unknown,
  registration: RowClipboardAdapterRegistration,
  hash: RowClipboardHashFunction = sha256UpperHex
): Promise<RowClipboardEnvelopeV1> {
  const record = requireExactRecord(input, [
    'checksum',
    'dependencies',
    'editor',
    'envelopeSchemaVersion',
    'excludedFieldKinds',
    'producerVersion',
    'rows',
    'scope',
    'source'
  ]);
  const checksum = requireChecksum(record.checksum);
  const normalizedRegistration = normalizeRowClipboardAdapterRegistration(registration);
  const payload = normalizeEnvelopePayload(
    {
      dependencies: record.dependencies,
      editor: record.editor,
      envelopeSchemaVersion: record.envelopeSchemaVersion,
      excludedFieldKinds: record.excludedFieldKinds,
      producerVersion: record.producerVersion,
      rows: record.rows,
      scope: record.scope,
      source: record.source
    },
    normalizedRegistration
  );
  const canonicalBytes = canonicalRowClipboardPayloadBytes(payload);
  const expected = await computeChecksum(canonicalBytes, hash);
  if (checksum !== expected) {
    throw new RowClipboardError('checksum-mismatch');
  }
  return deepFreeze({ ...payload, checksum });
}

export function canonicalRowClipboardPayloadBytes(
  payload: RowClipboardEnvelopePayloadV1
): Uint8Array {
  const bytes = new TextEncoder().encode(canonicalJsonStringify(payload));
  if (bytes.byteLength > rowClipboardMaximumCanonicalPayloadBytes) {
    throw new RowClipboardError('payload-limit-exceeded');
  }
  return bytes;
}

export function canonicalJsonStringify(value: unknown): string {
  if (value === null || typeof value === 'boolean' || typeof value === 'string') {
    return JSON.stringify(value);
  }
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) {
      throw new RowClipboardError('invalid-envelope');
    }
    return JSON.stringify(value);
  }
  if (Array.isArray(value)) {
    return `[${value.map(canonicalJsonStringify).join(',')}]`;
  }
  if (!isPlainRecord(value)) {
    throw new RowClipboardError('invalid-envelope');
  }
  return `{${Object.keys(value)
    .sort()
    .map(
      (key) =>
        `${JSON.stringify(key)}:${canonicalJsonStringify(value[key])}`
    )
    .join(',')}}`;
}

export function normalizeRowClipboardScope(scope: RowClipboardScope): RowClipboardScope {
  const record = requireExactRecord(scope, ['game', 'profileId', 'projectId']);
  const game = requireProjectGame(record.game);
  return deepFreeze({
    game,
    profileId: requireBoundedIdentifier(record.profileId, 'invalid-scope'),
    projectId: requireBoundedText(record.projectId, 128, 'invalid-scope')
  });
}

export function normalizeRowClipboardAdapterRegistration(
  registration: RowClipboardAdapterRegistration
): RowClipboardAdapterRegistration {
  const record = requireExactRecord(registration, [
    'dependencyKinds',
    'editorId',
    'fieldPolicies',
    'games',
    'maximumRows',
    'maximumTotalValues',
    'maximumValuesPerRow',
    'pasteModes',
    'profileIds',
    'rowKind',
    'rowSchemaVersion'
  ]);
  const games = requireArray(record.games, 'invalid-adapter-registration').map(
    requireProjectGame
  );
  const normalizedGames = uniqueArray(
    games,
    'invalid-adapter-registration'
  ).sort(compareCanonicalText);
  if (normalizedGames.length === 0) {
    throw new RowClipboardError('invalid-adapter-registration');
  }
  const normalizedProfiles =
    record.profileIds === null
      ? null
      : uniqueArray(
          requireArray(record.profileIds, 'invalid-adapter-registration').map((value) =>
            requireBoundedIdentifier(value, 'invalid-adapter-registration')
          ),
          'invalid-adapter-registration'
        ).sort(compareCanonicalText);
  if (normalizedProfiles !== null && normalizedProfiles.length === 0) {
    throw new RowClipboardError('invalid-adapter-registration');
  }
  const normalizedModes = uniqueArray(
    requireArray(record.pasteModes, 'invalid-adapter-registration').map(
      requirePasteMode
    ),
    'invalid-adapter-registration'
  ).sort(compareCanonicalText);
  if (normalizedModes.length === 0) {
    throw new RowClipboardError('invalid-adapter-registration');
  }
  const dependencyKinds = uniqueArray(
    requireArray(record.dependencyKinds, 'invalid-adapter-registration').map((value) =>
      requireBoundedIdentifier(value, 'invalid-adapter-registration')
    ),
    'invalid-adapter-registration'
  ).sort();
  const fieldPolicies = requireArray(
    record.fieldPolicies,
    'invalid-adapter-registration'
  ).map(normalizeFieldPolicy);
  if (
    fieldPolicies.length === 0 ||
    fieldPolicies.length > rowClipboardMaximumValueCountPerRow ||
    new Set(fieldPolicies.map((policy) => policy.fieldKey)).size !==
      fieldPolicies.length
  ) {
    throw new RowClipboardError('invalid-adapter-registration');
  }
  fieldPolicies.sort((left, right) => compareCanonicalText(left.fieldKey, right.fieldKey));
  return deepFreeze({
    dependencyKinds,
    editorId: requireBoundedIdentifier(record.editorId, 'invalid-adapter-registration'),
    fieldPolicies,
    games: normalizedGames,
    maximumRows: requirePositiveBound(
      record.maximumRows,
      rowClipboardMaximumRowCount,
      'invalid-adapter-registration'
    ),
    maximumTotalValues: requirePositiveBound(
      record.maximumTotalValues,
      rowClipboardMaximumTotalValueCount,
      'invalid-adapter-registration'
    ),
    maximumValuesPerRow: requirePositiveBound(
      record.maximumValuesPerRow,
      rowClipboardMaximumValueCountPerRow,
      'invalid-adapter-registration'
    ),
    pasteModes: normalizedModes,
    profileIds: normalizedProfiles,
    rowKind: requireBoundedIdentifier(record.rowKind, 'invalid-adapter-registration'),
    rowSchemaVersion: requirePositiveBound(
      record.rowSchemaVersion,
      65_535,
      'invalid-adapter-registration'
    )
  });
}

export function rowClipboardAdapterRegistrationKey(
  editor: RowClipboardEditorSchemaRef
): string {
  return `${editor.editorId}\0${editor.rowKind}\0${editor.rowSchemaVersion}`;
}

export function rowClipboardAdapterRegistrationSignature(
  registration: RowClipboardAdapterRegistration
): string {
  return canonicalJsonStringify(normalizeRowClipboardAdapterRegistration(registration));
}

export function rowClipboardRegistrationSupportsScope(
  registration: RowClipboardAdapterRegistration,
  scope: RowClipboardScope
): boolean {
  return (
    registration.games.includes(scope.game) &&
    (registration.profileIds === null || registration.profileIds.includes(scope.profileId))
  );
}

export function rowClipboardGameFamily(game: ProjectGame): RowClipboardGameFamily {
  switch (game) {
    case 'sword':
    case 'shield':
      return 'swordShield';
    case 'scarlet':
    case 'violet':
      return 'scarletViolet';
    case 'za':
      return 'legendsZA';
  }
}

async function computeChecksum(
  bytes: Uint8Array,
  hash: RowClipboardHashFunction
): Promise<string> {
  let result: string;
  try {
    result = await hash(bytes);
  } catch (error) {
    if (error instanceof RowClipboardError) {
      throw error;
    }
    throw new RowClipboardError('checksum-unavailable');
  }
  return requireChecksum(result);
}

async function sha256UpperHex(bytes: Uint8Array): Promise<string> {
  return calculatePayloadBytesSha256(bytes);
}

function normalizeEnvelopePayload(
  input: unknown,
  registration: RowClipboardAdapterRegistration
): RowClipboardEnvelopePayloadV1 {
  const record = requireExactRecord(input, [
    'dependencies',
    'editor',
    'envelopeSchemaVersion',
    'excludedFieldKinds',
    'producerVersion',
    'rows',
    'scope',
    'source'
  ]);
  if (record.envelopeSchemaVersion !== rowClipboardEnvelopeSchemaVersion) {
    throw new RowClipboardError('invalid-envelope');
  }
  const editor = normalizeEditorSchema(record.editor);
  if (
    editor.editorId !== registration.editorId ||
    editor.rowKind !== registration.rowKind ||
    editor.rowSchemaVersion !== registration.rowSchemaVersion
  ) {
    throw new RowClipboardError('schema-incompatible');
  }
  const scope = normalizeEnvelopeScope(record.scope);
  if (!rowClipboardRegistrationSupportsScope(registration, scope)) {
    throw new RowClipboardError('adapter-unavailable');
  }
  requireExactExcludedFieldKinds(record.excludedFieldKinds);
  const dependencies = normalizeDependencies(record.dependencies, registration);
  const dependencyKeys = new Set(dependencies.map(dependencyKey));
  const rows = normalizeRows(record.rows, registration, dependencyKeys);
  return deepFreeze({
    dependencies,
    editor,
    envelopeSchemaVersion: rowClipboardEnvelopeSchemaVersion,
    excludedFieldKinds: [...rowClipboardExcludedFieldKinds],
    producerVersion: requireBoundedText(
      record.producerVersion,
      64,
      'invalid-envelope'
    ),
    rows,
    scope,
    source: normalizeSource(record.source)
  });
}

function normalizeEnvelopeScope(input: unknown): RowClipboardEnvelopeScopeV1 {
  const record = requireExactRecord(input, [
    'game',
    'gameFamily',
    'profileId',
    'projectId'
  ]);
  const scope = normalizeRowClipboardScope({
    game: requireProjectGame(record.game),
    profileId: requireBoundedIdentifier(record.profileId, 'invalid-scope'),
    projectId: requireBoundedText(record.projectId, 128, 'invalid-scope')
  });
  if (record.gameFamily !== rowClipboardGameFamily(scope.game)) {
    throw new RowClipboardError('invalid-scope');
  }
  return deepFreeze({ ...scope, gameFamily: rowClipboardGameFamily(scope.game) });
}

function normalizeEditorSchema(input: unknown): RowClipboardEditorSchemaRef {
  const record = requireExactRecord(input, [
    'editorId',
    'rowKind',
    'rowSchemaVersion'
  ]);
  return deepFreeze({
    editorId: requireBoundedIdentifier(record.editorId, 'invalid-editor-schema'),
    rowKind: requireBoundedIdentifier(record.rowKind, 'invalid-editor-schema'),
    rowSchemaVersion: requirePositiveBound(
      record.rowSchemaVersion,
      65_535,
      'invalid-editor-schema'
    )
  });
}

function normalizeSource(input: unknown): RowClipboardSourceV1 {
  const record = requireExactRecord(input, ['logicalIdentity', 'projectRevision']);
  return deepFreeze({
    logicalIdentity: normalizeLogicalIdentity(record.logicalIdentity),
    projectRevision: requireBoundedText(record.projectRevision, 512, 'invalid-revision')
  });
}

function normalizeLogicalIdentity(input: unknown): RowClipboardLogicalIdentity {
  const record = requireExactRecord(input, ['key', 'kind']);
  return deepFreeze({
    key: requireBoundedText(record.key, 512, 'invalid-logical-identity'),
    kind: requireBoundedIdentifier(record.kind, 'invalid-logical-identity')
  });
}

function normalizeDependencies(
  input: unknown,
  registration: RowClipboardAdapterRegistration
): readonly RowClipboardDependencyReference[] {
  const values = requireArray(input, 'invalid-dependency');
  if (values.length > rowClipboardMaximumDependencyCount) {
    throw new RowClipboardError('dependency-limit-exceeded');
  }
  const dependencies = values.map(normalizeDependency);
  const keys = new Set<string>();
  for (const dependency of dependencies) {
    if (!registration.dependencyKinds.includes(dependency.kind)) {
      throw new RowClipboardError('invalid-dependency');
    }
    const key = dependencyKey(dependency);
    if (keys.has(key)) {
      throw new RowClipboardError('duplicate-dependency');
    }
    keys.add(key);
  }
  dependencies.sort(compareDependencies);
  return deepFreeze(dependencies);
}

function normalizeDependency(input: unknown): RowClipboardDependencyReference {
  const record = requireExactRecord(input, ['form', 'id', 'kind']);
  return deepFreeze({
    form:
      record.form === null
        ? null
        : requireBoundedText(record.form, 128, 'invalid-dependency'),
    id: requireBoundedText(record.id, 128, 'invalid-dependency'),
    kind: requireBoundedIdentifier(record.kind, 'invalid-dependency')
  });
}

function normalizeRows(
  input: unknown,
  registration: RowClipboardAdapterRegistration,
  dependencyKeys: ReadonlySet<string>
): readonly RowClipboardLogicalRowV1[] {
  const values = requireArray(input, 'invalid-envelope');
  if (values.length === 0 || values.length > registration.maximumRows) {
    throw new RowClipboardError('row-limit-exceeded');
  }
  let totalValues = 0;
  const rows = values.map((value) => {
    const record = requireExactRecord(value, ['sourceIdentity', 'values']);
    const ownedValues = requireArray(record.values, 'invalid-value');
    if (
      ownedValues.length === 0 ||
      ownedValues.length > registration.maximumValuesPerRow
    ) {
      throw new RowClipboardError('value-limit-exceeded');
    }
    totalValues += ownedValues.length;
    if (totalValues > registration.maximumTotalValues) {
      throw new RowClipboardError('value-limit-exceeded');
    }
    const normalizedValues = ownedValues.map((ownedValue) =>
      normalizeOwnedValue(ownedValue, registration, dependencyKeys)
    );
    if (
      new Set(normalizedValues.map((ownedValue) => ownedValue.fieldKey)).size !==
      normalizedValues.length
    ) {
      throw new RowClipboardError('duplicate-field-key');
    }
    normalizedValues.sort((left, right) =>
      compareCanonicalText(left.fieldKey, right.fieldKey)
    );
    return deepFreeze({
      sourceIdentity: normalizeLogicalIdentity(record.sourceIdentity),
      values: normalizedValues
    });
  });
  return deepFreeze(rows);
}

function normalizeOwnedValue(
  input: unknown,
  registration: RowClipboardAdapterRegistration,
  dependencyKeys: ReadonlySet<string>
): RowClipboardOwnedValue {
  const record = requireExactRecord(input, ['fieldKey', 'value']);
  const fieldKey = requireBoundedIdentifier(record.fieldKey, 'invalid-value');
  const policy = registration.fieldPolicies.find(
    (candidate) => candidate.fieldKey === fieldKey
  );
  if (!policy) {
    throw new RowClipboardError('unknown-field');
  }
  return deepFreeze({
    fieldKey,
    value: normalizeValue(record.value, policy, dependencyKeys)
  });
}

function normalizeValue(
  input: unknown,
  policy: RowClipboardFieldPolicy,
  dependencyKeys: ReadonlySet<string>
): RowClipboardValue {
  if (!isPlainRecord(input) || typeof input.kind !== 'string') {
    throw new RowClipboardError('invalid-value');
  }
  const kind = requireValueKind(input.kind);
  if (!policy.valueKinds.includes(kind)) {
    throw new RowClipboardError('invalid-value');
  }
  switch (kind) {
    case 'boolean': {
      const record = requireExactRecord(input, ['kind', 'value']);
      if (typeof record.value !== 'boolean') {
        throw new RowClipboardError('invalid-value');
      }
      return deepFreeze({ kind, value: record.value });
    }
    case 'signedInteger': {
      const record = requireExactRecord(input, ['kind', 'value']);
      const value = requireCanonicalInteger(record.value, true);
      return deepFreeze({ kind, value });
    }
    case 'unsignedInteger': {
      const record = requireExactRecord(input, ['kind', 'value']);
      const value = requireCanonicalInteger(record.value, false);
      return deepFreeze({ kind, value });
    }
    case 'decimal': {
      const record = requireExactRecord(input, ['kind', 'value']);
      const value = requireCanonicalDecimal(record.value);
      return deepFreeze({ kind, value });
    }
    case 'string': {
      const record = requireExactRecord(input, ['kind', 'value']);
      if (policy.maximumUtf8Bytes === null) {
        throw new RowClipboardError('invalid-adapter-registration');
      }
      const value = requireUtf8String(record.value, policy.maximumUtf8Bytes);
      return deepFreeze({ kind, value });
    }
    case 'dependencyReference': {
      const record = requireExactRecord(input, ['kind', 'value']);
      const value = normalizeDependency(record.value);
      if (!dependencyKeys.has(dependencyKey(value))) {
        throw new RowClipboardError('invalid-dependency');
      }
      return deepFreeze({ kind, value });
    }
  }
}

function normalizeFieldPolicy(input: unknown): RowClipboardFieldPolicy {
  const record = requireExactRecord(input, [
    'fieldKey',
    'maximumUtf8Bytes',
    'valueKinds'
  ]);
  const normalizedKinds = uniqueArray(
    requireArray(record.valueKinds, 'invalid-adapter-registration').map(
      requireValueKind
    ),
    'invalid-adapter-registration'
  ).sort(compareCanonicalText);
  if (normalizedKinds.length === 0) {
    throw new RowClipboardError('invalid-adapter-registration');
  }
  const maximumUtf8Bytes =
    record.maximumUtf8Bytes === null
      ? null
      : requirePositiveBound(
          record.maximumUtf8Bytes,
          rowClipboardMaximumCanonicalPayloadBytes,
          'invalid-adapter-registration'
        );
  if (normalizedKinds.includes('string') !== (maximumUtf8Bytes !== null)) {
    throw new RowClipboardError('invalid-adapter-registration');
  }
  return deepFreeze({
    fieldKey: requireBoundedIdentifier(record.fieldKey, 'invalid-adapter-registration'),
    maximumUtf8Bytes,
    valueKinds: normalizedKinds
  });
}

function requireCanonicalInteger(input: unknown, signed: boolean): string {
  if (
    typeof input !== 'string' ||
    !(signed ? canonicalSignedIntegerPattern : canonicalUnsignedIntegerPattern).test(
      input
    )
  ) {
    throw new RowClipboardError('invalid-value');
  }
  let value: bigint;
  try {
    value = BigInt(input);
  } catch {
    throw new RowClipboardError('invalid-value');
  }
  if (
    (signed && (value < signedIntegerMinimum || value > signedIntegerMaximum)) ||
    (!signed && (value < 0n || value > unsignedIntegerMaximum))
  ) {
    throw new RowClipboardError('invalid-value');
  }
  return input;
}

function requireCanonicalDecimal(input: unknown): string {
  if (
    typeof input !== 'string' ||
    input.length > 128 ||
    !canonicalDecimalPattern.test(input)
  ) {
    throw new RowClipboardError('invalid-value');
  }
  const numeric = Number(input);
  if (!Number.isFinite(numeric) || (numeric === 0 && input !== '0')) {
    throw new RowClipboardError('invalid-value');
  }
  return input;
}

function requireUtf8String(input: unknown, maximumUtf8Bytes: number): string {
  if (typeof input !== 'string' || !hasWellFormedUtf16(input)) {
    throw new RowClipboardError('invalid-value');
  }
  if (new TextEncoder().encode(input).byteLength > maximumUtf8Bytes) {
    throw new RowClipboardError('invalid-value');
  }
  return input;
}

function requireExactExcludedFieldKinds(input: unknown) {
  const values = requireArray(input, 'invalid-envelope');
  if (
    values.length !== rowClipboardExcludedFieldKinds.length ||
    !values.every((value, index) => value === rowClipboardExcludedFieldKinds[index])
  ) {
    throw new RowClipboardError('invalid-envelope');
  }
}

function requireValueKind(value: unknown): RowClipboardValueKind {
  if (!valueKinds.includes(value as RowClipboardValueKind)) {
    throw new RowClipboardError('invalid-value');
  }
  return value as RowClipboardValueKind;
}

function requirePasteMode(value: unknown): RowClipboardPasteMode {
  if (!pasteModes.includes(value as RowClipboardPasteMode)) {
    throw new RowClipboardError('invalid-adapter-registration');
  }
  return value as RowClipboardPasteMode;
}

function requireProjectGame(value: unknown): ProjectGame {
  if (!projectGames.includes(value as ProjectGame)) {
    throw new RowClipboardError('invalid-scope');
  }
  return value as ProjectGame;
}

function requireChecksum(value: unknown): string {
  if (typeof value !== 'string' || !checksumPattern.test(value)) {
    throw new RowClipboardError('invalid-checksum');
  }
  return value;
}

function requireBoundedIdentifier(
  value: unknown,
  code:
    | 'invalid-adapter-registration'
    | 'invalid-dependency'
    | 'invalid-editor-schema'
    | 'invalid-logical-identity'
    | 'invalid-scope'
    | 'invalid-value'
): string {
  if (typeof value !== 'string' || !stableIdentifierPattern.test(value)) {
    throw new RowClipboardError(code);
  }
  return value;
}

function requireBoundedText(
  value: unknown,
  maximumLength: number,
  code:
    | 'invalid-dependency'
    | 'invalid-envelope'
    | 'invalid-logical-identity'
    | 'invalid-revision'
    | 'invalid-scope'
): string {
  if (
    typeof value !== 'string' ||
    value.length === 0 ||
    value.length > maximumLength ||
    value !== value.trim() ||
    /\p{Cc}/u.test(value) ||
    !hasWellFormedUtf16(value)
  ) {
    throw new RowClipboardError(code);
  }
  return value;
}

function requirePositiveBound(
  value: unknown,
  maximum: number,
  code: 'invalid-adapter-registration' | 'invalid-editor-schema'
): number {
  if (!Number.isSafeInteger(value) || (value as number) < 1 || (value as number) > maximum) {
    throw new RowClipboardError(code);
  }
  return value as number;
}

function requireArray(
  value: unknown,
  code:
    | 'invalid-adapter-registration'
    | 'invalid-dependency'
    | 'invalid-envelope'
    | 'invalid-value'
): unknown[] {
  if (!Array.isArray(value)) {
    throw new RowClipboardError(code);
  }
  return value;
}

function requireExactRecord(
  value: unknown,
  keys: readonly string[]
): Record<string, unknown> {
  if (!isPlainRecord(value)) {
    throw new RowClipboardError('invalid-envelope');
  }
  const actualKeys = Object.keys(value).sort();
  const expectedKeys = [...keys].sort();
  if (
    actualKeys.length !== expectedKeys.length ||
    !actualKeys.every((key, index) => key === expectedKeys[index])
  ) {
    throw new RowClipboardError('invalid-envelope');
  }
  return value;
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function uniqueArray<T>(values: T[], code: 'invalid-adapter-registration'): T[] {
  if (new Set(values).size !== values.length) {
    throw new RowClipboardError(code);
  }
  return values;
}

function dependencyKey(value: RowClipboardDependencyReference): string {
  return `${value.kind}\0${value.id}\0${value.form ?? ''}`;
}

function compareDependencies(
  left: RowClipboardDependencyReference,
  right: RowClipboardDependencyReference
) {
  return (
    compareCanonicalText(left.kind, right.kind) ||
    compareCanonicalText(left.id, right.id) ||
    compareCanonicalText(left.form ?? '', right.form ?? '')
  );
}

function compareCanonicalText(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function hasWellFormedUtf16(value: string): boolean {
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = value.charCodeAt(index + 1);
      if (next < 0xdc00 || next > 0xdfff) {
        return false;
      }
      index += 1;
    } else if (code >= 0xdc00 && code <= 0xdfff) {
      return false;
    }
  }
  return true;
}

function deepFreeze<T>(value: T): T {
  if (value !== null && typeof value === 'object' && !Object.isFrozen(value)) {
    Object.freeze(value);
    for (const child of Object.values(value)) {
      deepFreeze(child);
    }
  }
  return value;
}
