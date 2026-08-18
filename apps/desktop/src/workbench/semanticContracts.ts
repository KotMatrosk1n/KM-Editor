/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';

export type JsonPrimitive = boolean | number | string | null;
export type JsonValue = JsonPrimitive | JsonValue[] | { [key: string]: JsonValue };

// These frontend contracts are wire-safe projections of KM.Core semantics, not
// implicit System.Text.Json output from the backend value-object types. In
// particular, 64-bit integers, decimal values, revisions, and byte offsets use
// strings to avoid JavaScript precision loss. Any bridge exposure must add an
// explicit, validated mapper between KM.Core value objects and these shapes.

export type GameFamily = 'swordShield' | 'scarletViolet' | 'legendsZA';

const semanticContractMaximumKeyLength = 128;
const semanticContractMaximumStableIdLength = 1024;
const semanticContractMaximumSchemaVersion = 2_147_483_647;
const semanticGameFamilies = new Set<GameFamily>([
  'swordShield',
  'scarletViolet',
  'legendsZA'
]);

export function projectGameToFamily(game: ProjectGame): GameFamily {
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

export type SemanticRecordKind = {
  key: string;
  schemaVersion: number;
};

export type SemanticRecordRef = {
  domain: string;
  gameFamily: GameFamily;
  recordId: string;
  recordKind: SemanticRecordKind;
  subrecordId: string | null;
};

export function validateSemanticRecordRef(record: SemanticRecordRef) {
  if (typeof record !== 'object' || record === null) {
    throw new Error('Semantic record reference is invalid.');
  }
  if (!semanticGameFamilies.has(record.gameFamily)) {
    throw new Error('Semantic record game family is invalid.');
  }
  validateSemanticContractKey(record.domain, 'semantic domain');
  if (typeof record.recordKind !== 'object' || record.recordKind === null) {
    throw new Error('Semantic record kind is invalid.');
  }
  validateSemanticContractKey(record.recordKind.key, 'semantic record kind');
  if (
    !Number.isSafeInteger(record.recordKind.schemaVersion) ||
    record.recordKind.schemaVersion < 1 ||
    record.recordKind.schemaVersion > semanticContractMaximumSchemaVersion
  ) {
    throw new Error('Semantic record kind schema version must be a positive 32-bit integer.');
  }
  validateSemanticStableId(record.recordId, 'semantic record id');
  if (record.subrecordId !== null) {
    validateSemanticStableId(record.subrecordId, 'semantic subrecord id');
  }
}

export type SemanticFieldRef = {
  fieldKey: string;
  record: SemanticRecordRef;
};

export type SemanticNullValue = { kind: 'null' };
export type SemanticBooleanValue = { kind: 'boolean'; value: boolean };
export type SemanticSignedIntegerValue = { kind: 'signedInteger'; value: string };
export type SemanticUnsignedIntegerValue = { kind: 'unsignedInteger'; value: string };
export type SemanticDecimalValue = { kind: 'decimal'; value: string };
export type SemanticFloat32Value = { kind: 'float32'; value: number };
export type SemanticFloat64Value = { kind: 'float64'; value: number };
export type SemanticTextValue = { kind: 'text'; value: string };
export type SemanticBinaryValue = { kind: 'binary'; value: readonly number[] };
export type SemanticEnumValue = {
  enumType: string;
  kind: 'enum';
  member: string;
  numericValue: string | null;
};
export type SemanticOrderedListValue = {
  items: readonly SemanticValue[];
  kind: 'orderedList';
};
export type SemanticStructuredValue = {
  fields: Readonly<Record<string, SemanticValue>>;
  kind: 'structured';
  schemaVersion: number;
  typeKey: string;
};

export type SemanticValue =
  | SemanticNullValue
  | SemanticBooleanValue
  | SemanticSignedIntegerValue
  | SemanticUnsignedIntegerValue
  | SemanticDecimalValue
  | SemanticFloat32Value
  | SemanticFloat64Value
  | SemanticTextValue
  | SemanticBinaryValue
  | SemanticEnumValue
  | SemanticOrderedListValue
  | SemanticStructuredValue;

export type SemanticPayload = {
  adapterId: string;
  canonicalFingerprint: string;
  root: SemanticValue;
  schemaVersion: number;
};

export type ProjectSourceRevision = {
  fingerprint: string;
  gameFamily: GameFamily;
  generation: string;
  projectId: string;
};

export type SourceLayerKind =
  | 'base'
  | 'layered'
  | 'pending'
  | 'changeSet'
  | 'comparedMod'
  | 'checkpoint';

export type SourceLayerRef = {
  instanceId: string | null;
  kind: SourceLayerKind;
};

export type SourceSnapshot = {
  fingerprint: string;
  layer: SourceLayerRef;
  revision: ProjectSourceRevision;
};

export type SemanticRecordMutationTarget = {
  kind: 'record';
  record: SemanticRecordRef;
};

export type SemanticFieldMutationTarget = {
  field: SemanticFieldRef;
  kind: 'field';
  record: SemanticRecordRef;
};

export type SemanticMutationTarget =
  | SemanticRecordMutationTarget
  | SemanticFieldMutationTarget;

export type SemanticOperationDescriptor = {
  adapterId: string;
  operationKind: string;
  schemaVersion: number;
};

export type SemanticBaselineState = 'present' | 'absent';

export type ExpectedSemanticBaseline = {
  expectedValue: SemanticValue | null;
  source: SourceSnapshot;
  state: SemanticBaselineState;
  targetFingerprint: string | null;
};

export type MutationProvenanceKind =
  | 'user'
  | 'import'
  | 'recipe'
  | 'generator'
  | 'extension'
  | 'migration'
  | 'system';

export type MutationProvenance = {
  createdAtUtc: string;
  kind: MutationProvenanceKind;
  originId: string | null;
  producerId: string;
};

export type SemanticMutation = {
  expectedBaseline: ExpectedSemanticBaseline;
  id: string;
  operation: SemanticOperationDescriptor;
  payload: SemanticPayload;
  provenance: MutationProvenance;
  target: SemanticMutationTarget;
};

export type ReferenceRelationshipKind = {
  key: string;
  schemaVersion: number;
};

export type ReferenceConfidence = 'unknown' | 'verified' | 'derived' | 'heuristic';

export type ReferenceEdge = {
  confidence: Exclude<ReferenceConfidence, 'unknown'>;
  providerId: string;
  relationship: ReferenceRelationshipKind;
  snapshot: SourceSnapshot;
  source: SemanticRecordRef;
  target: SemanticRecordRef;
};

export type ReferenceCoverageState = 'complete' | 'partial' | 'unavailable';

export type ReferenceCoverage = {
  confidence: ReferenceConfidence;
  coveredDomains: readonly string[];
  providerId: string;
  reasonCode: string | null;
  snapshot: SourceSnapshot;
  state: ReferenceCoverageState;
};

export type ReferenceQueryResult = {
  coverage: readonly ReferenceCoverage[];
  edges: readonly ReferenceEdge[];
  revision: ProjectSourceRevision;
};

export type OwnedByteRange = {
  length: string;
  offset: string;
};

export type OwnedTargetAddress = {
  archiveMember: string | null;
  byteRange: OwnedByteRange | null;
  file: string;
  record: SemanticRecordRef | null;
  scopeKind: 'file' | 'archiveMember' | 'record' | 'byteRange';
};

export type PreservationRuleDescriptor = {
  key: string;
  preservesUnownedData: boolean;
  requiresPreimage: boolean;
  schemaVersion: number;
};

export type OwnedTarget = {
  address: OwnedTargetAddress;
  gameFamily: GameFamily;
  ownerId: string;
  preservationRule: PreservationRuleDescriptor;
};

export type CapabilityKind =
  | 'navigation'
  | 'command'
  | 'semanticSearch'
  | 'comparison'
  | 'references'
  | 'impact'
  | 'bulkOperation'
  | 'analyzer'
  | 'recipeImport'
  | 'recipeExport'
  | 'outputOwnership'
  | 'recovery';

export type CapabilityMaturity =
  | 'editable'
  | 'readOnly'
  | 'analysisOnly'
  | 'research'
  | 'unavailable';

export type CapabilityAvailability = {
  gameFamily: GameFamily;
  isAvailable: boolean;
  maturity: CapabilityMaturity;
  reasonCode: string | null;
  supportedBuilds: readonly string[];
  supportedGames: readonly ProjectGame[];
  supportedOutputModes: readonly string[];
  supportedSourceLayers: readonly SourceLayerKind[];
};

export type CapabilityDescriptor = {
  availability: CapabilityAvailability;
  domain: string | null;
  id: string;
  kind: CapabilityKind;
};

// Display metadata is intentionally separate from canonical values.
export type SemanticValuePresentationViewModel = {
  description?: string;
  formattedValue: string;
  label: string;
};

export type SemanticPageWindow = {
  continuationToken: string | null;
  limit: number;
};

export type RevisionBoundResult<T> = {
  result: T;
  revision: ProjectSourceRevision;
};

export type RevisionBoundPage<T> = {
  continuationToken: string | null;
  items: readonly T[];
  revision: ProjectSourceRevision;
};

export type SemanticPageRequest = {
  continuationToken?: string;
  limit: number;
};

export type SemanticPage<T> = RevisionBoundPage<T>;

export type SemanticSearchQuery = SemanticPageRequest & {
  domains?: readonly string[];
  revision: ProjectSourceRevision;
  searchText: string;
};

export type SemanticSearchResultViewModel = {
  description?: string;
  displayName: string;
  record: SemanticRecordRef;
  sourceSnapshot: SourceSnapshot;
};

export type SemanticComparisonRequest = SemanticPageRequest & {
  left: SourceSnapshot;
  records?: readonly SemanticRecordRef[];
  right: SourceSnapshot;
};

export type SemanticDifferenceKind =
  | 'added'
  | 'removed'
  | 'reordered'
  | 'changed'
  | 'inherited'
  | 'unavailable'
  | 'undecodable';

export type SemanticDifferenceViewModel = {
  field: SemanticFieldRef | null;
  kind: SemanticDifferenceKind;
  left: SemanticValue | null;
  ownerId: string | null;
  record: SemanticRecordRef;
  right: SemanticValue | null;
};

export type ReferenceQuery = SemanticPageRequest & {
  direction: 'incoming' | 'outgoing';
  record: SemanticRecordRef;
  revision: ProjectSourceRevision;
};

export type SemanticReferencePage = SemanticPage<ReferenceEdge> & {
  coverage: readonly ReferenceCoverage[];
};

// Cross-cutting data stays lazy: consumers request bounded, revision-bound results.
export type SemanticApplicationApi = {
  compare: (
    request: SemanticComparisonRequest
  ) => Promise<SemanticPage<SemanticDifferenceViewModel>>;
  getReferences: (request: ReferenceQuery) => Promise<SemanticReferencePage>;
  search: (
    request: SemanticSearchQuery
  ) => Promise<SemanticPage<SemanticSearchResultViewModel>>;
};

export function semanticRecordRefKey(record: SemanticRecordRef) {
  // Validation keeps the null subrecord sentinel distinct from every accepted
  // subrecord id; in particular, an empty subrecord id cannot alias null.
  validateSemanticRecordRef(record);
  return [
    record.gameFamily,
    encodeURIComponent(record.domain),
    encodeURIComponent(record.recordKind.key),
    record.recordKind.schemaVersion,
    encodeURIComponent(record.recordId),
    encodeURIComponent(record.subrecordId ?? '')
  ].join(':');
}

export function semanticFieldRefKey(field: SemanticFieldRef) {
  return `${semanticRecordRefKey(field.record)}:${encodeURIComponent(field.fieldKey)}`;
}

function validateSemanticContractKey(value: unknown, label: string): asserts value is string {
  if (
    typeof value !== 'string' ||
    value.length === 0 ||
    value.length > semanticContractMaximumKeyLength ||
    !/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u.test(value)
  ) {
    throw new Error(`${label} is not a valid semantic contract key.`);
  }
}

function validateSemanticStableId(value: unknown, label: string): asserts value is string {
  if (
    typeof value !== 'string' ||
    value.length === 0 ||
    value.length > semanticContractMaximumStableIdLength ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f-\u009f]/u.test(value)
  ) {
    throw new Error(`${label} is not a valid bounded semantic id.`);
  }
}
