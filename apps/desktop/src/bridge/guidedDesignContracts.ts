/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { apiDiagnosticSchema } from './contracts';
import { changeSetWorkspaceSnapshotSchema } from './changeSetContracts';
import {
  semanticExploreRecordRefSchema,
  semanticExploreRevisionSchema,
  semanticExploreScalarSchema,
  semanticExploreScopeSchema,
  semanticExploreSourceSnapshotSchema
} from './semanticExploreContracts';

export const guidedDesignSchemaVersion = 1 as const;
export const guidedDesignDefaultPageSize = 50;
export const guidedDesignMaximumPageSize = 100;
export const guidedDesignMaximumTargets = 128;
export const guidedDesignMaximumPins = 128;
export const guidedDesignExpectedMutations = 768;
export const guidedDesignProvisionedMutations = guidedDesignExpectedMutations * 4;
export const guidedDesignMaximumMutations = guidedDesignProvisionedMutations * 2;
export const guidedDesignExpectedAffectedRecords = 128;
export const guidedDesignProvisionedAffectedRecords = guidedDesignExpectedAffectedRecords * 4;
export const guidedDesignMaximumAffectedRecords = guidedDesignProvisionedAffectedRecords * 2;
export const guidedDesignExpectedEligibleTargetCount = 50_000;
export const guidedDesignProvisionedEligibleTargetCount =
  guidedDesignExpectedEligibleTargetCount * 4;
export const guidedDesignMaximumEligibleTargetCount =
  guidedDesignProvisionedEligibleTargetCount * 2;
export const guidedDesignMaximumEligibleTargetWindow = 500;
export const guidedDesignMaximumTargetSearchLength = 256;
export const guidedDesignExpectedFindings = 100;
export const guidedDesignProvisionedFindings = guidedDesignExpectedFindings * 4;
export const guidedDesignMaximumFindings = guidedDesignProvisionedFindings * 2;
export const guidedDesignMaximumAccumulatedResults =
  guidedDesignMaximumMutations + guidedDesignMaximumFindings;
export const guidedDesignMaximumFieldKeys = 32;
export const guidedDesignMaximumChangeSetNameLength = 128;
export const guidedDesignExpectedCanonicalExportBytes = 1 * 1_024 * 1_024;
export const guidedDesignProvisionedCanonicalExportBytes =
  guidedDesignExpectedCanonicalExportBytes * 4;
export const guidedDesignMaximumCanonicalExportBytes =
  guidedDesignProvisionedCanonicalExportBytes * 2;

const contractKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u);
const fieldKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[a-z][A-Za-z0-9]*$/u);
const stableIdSchema = z
  .string()
  .min(1)
  .max(1_024)
  .refine((value) => (
    value.trim() === value &&
    !/[\u0000-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/iu.test(value)
  ));
const displayTextSchema = z
  .string()
  .max(8_192)
  .refine((value) => (
    !/[\u0000-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/iu.test(value)
  ));
const fingerprintSchema = z.string().regex(/^[a-f0-9]{64}$/u);
const cursorSchema = z
  .string()
  .min(1)
  .max(2_048)
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value));
const seedSchema = z.string().regex(/^[0-9a-f]{32}$/u);
const canonicalIntegerSchema = z
  .string()
  .min(1)
  .max(20)
  .refine((value) => {
    try {
      const parsed = BigInt(value);
      return parsed >= -9_223_372_036_854_775_808n &&
        parsed <= 9_223_372_036_854_775_807n &&
        parsed.toString() === value;
    } catch {
      return false;
    }
  });
const distinct = <T>(values: readonly T[], key: (value: T) => string) =>
  new Set(values.map(key)).size === values.length;
const recordKey = (record: z.infer<typeof semanticExploreRecordRefSchema>) => JSON.stringify([
  record.gameFamily,
  record.domain,
  record.recordKind.key,
  record.recordKind.schemaVersion,
  record.recordId,
  record.subrecordId
]);
const pinOwnerRecordKey = (record: z.infer<typeof semanticExploreRecordRefSchema>) => (
  record.domain === 'workflow.pokemon' &&
  record.recordKind.key === 'pokemon-personal' &&
  record.recordKind.schemaVersion === 1 &&
  /^evolution-slot:(0|[1-9][0-9]*)$/u.test(record.subrecordId ?? '')
    ? recordKey({ ...record, subrecordId: null })
    : recordKey(record)
);

export const guidedDesignFeatureSchema = z.enum([
  'difficultyDesigner',
  'encounterPopulationDesigner',
  'economyRebalance',
  'evolutionAccessibility',
  'trainerArchetypes',
  'constraintRandomization',
  'plando',
  'seedInspector',
  'spoilerRaceExport'
]);

export const guidedDesignProposalKindSchema = z.enum([
  'trainerLevelAdjustment',
  'encounterLevelAdjustment',
  'encounterWeightScale',
  'economyPrimaryPriceScale',
  'evolutionLevelClamp',
  'trainerEvArchetype',
  'pokemonBaseStatShuffle'
]);

export const guidedDesignRoundingSchema = z.enum(['floor', 'nearest', 'ceiling']);
export const guidedDesignTrainerArchetypeSchema = z.enum([
  'physicalAttackSpeed',
  'specialAttackSpeed',
  'balanced'
]);
export const guidedDesignFindingSeveritySchema = z.enum(['info', 'warning', 'error']);
export const guidedDesignCapabilityStateSchema = z.enum(['complete', 'partial', 'unavailable']);
export const guidedDesignConfidenceSchema = z.enum(['verified', 'derived', 'unknown']);
export const guidedDesignSourceLayerSchema = z.literal('layered');

export const guidedDesignCapabilitySchema = z.strictObject({
  confidence: guidedDesignConfidenceSchema,
  feature: guidedDesignFeatureSchema,
  proposalKinds: z.array(guidedDesignProposalKindSchema).max(7).refine(
    (values) => new Set(values).size === values.length,
    { message: 'Guided Design proposal kinds must be unique.' }
  ),
  providerId: contractKeySchema,
  reasonCode: contractKeySchema.nullable(),
  sourceLayers: z.array(guidedDesignSourceLayerSchema).max(1).refine(
    (values) => new Set(values).size === values.length,
    { message: 'Guided Design source layers must be unique.' }
  ),
  state: guidedDesignCapabilityStateSchema
});

export const guidedDesignPinSchema = z.strictObject({
  canonicalValue: canonicalIntegerSchema,
  fieldKey: fieldKeySchema,
  record: semanticExploreRecordRefSchema
});

export const guidedDesignInputSchema = z.strictObject({
  archetype: guidedDesignTrainerArchetypeSchema.nullable(),
  delta: z.number().int().min(-100).max(100).nullable(),
  fieldKeys: z.array(fieldKeySchema).max(guidedDesignMaximumFieldKeys).refine(
    (values) => new Set(values).size === values.length,
    { message: 'Guided Design field keys must be unique.' }
  ),
  kind: guidedDesignProposalKindSchema,
  maximumValue: z.number().int().min(0).max(100).nullable(),
  minimumValue: z.number().int().min(0).max(100).nullable(),
  multiplierBasisPoints: z.number().int().min(0).max(100_000).nullable(),
  pins: z.array(guidedDesignPinSchema).max(guidedDesignMaximumPins).refine(
    (values) => distinct(values, (pin) => JSON.stringify([
      recordKey(pin.record),
      pin.fieldKey
    ])),
    { message: 'Guided Design pins must target unique fields.' }
  ),
  rounding: guidedDesignRoundingSchema.nullable(),
  seed: seedSchema.nullable(),
  targets: z.array(semanticExploreRecordRefSchema).max(guidedDesignMaximumTargets).refine(
    (values) => distinct(values, recordKey),
    { message: 'Guided Design targets must be unique.' }
  )
}).superRefine((input, context) => {
  if (
    input.minimumValue !== null &&
    input.maximumValue !== null &&
    input.minimumValue > input.maximumValue
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'The Guided Design minimum cannot exceed its maximum.',
      path: ['minimumValue']
    });
  }
  const targetKeys = new Set(input.targets.map(recordKey));
  input.pins.forEach((pin, index) => {
    if (!targetKeys.has(pinOwnerRecordKey(pin.record))) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'A Guided Design pin must belong to an exact selected target.',
        path: ['pins', index, 'record']
      });
    }
  });
});

export const guidedDesignMutationSchema = z.strictObject({
  after: semanticExploreScalarSchema,
  before: semanticExploreScalarSchema,
  fieldKey: fieldKeySchema,
  fieldLabel: displayTextSchema,
  mutationId: stableIdSchema,
  pinFieldKey: fieldKeySchema.nullable(),
  pinRecord: semanticExploreRecordRefSchema.nullable(),
  pinned: z.boolean(),
  providerId: contractKeySchema,
  record: semanticExploreRecordRefSchema,
  recordLabel: displayTextSchema,
  summary: displayTextSchema
}).superRefine((mutation, context) => {
  if ((mutation.pinRecord === null) !== (mutation.pinFieldKey === null)) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A Guided Design pin record and field must be supplied together.',
      path: ['pinRecord']
    });
  }
  if (mutation.pinned && mutation.pinRecord === null) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A pinned Guided Design mutation requires its exact pin identity.',
      path: ['pinned']
    });
  }
});

export const guidedDesignTargetOptionSchema = z.strictObject({
  record: semanticExploreRecordRefSchema,
  recordLabel: displayTextSchema
});

export const guidedDesignFindingSchema = z.strictObject({
  confidence: guidedDesignConfidenceSchema,
  findingId: stableIdSchema,
  record: semanticExploreRecordRefSchema.nullable(),
  relatedRecords: z.array(semanticExploreRecordRefSchema).max(guidedDesignMaximumAffectedRecords),
  ruleId: contractKeySchema,
  severity: guidedDesignFindingSeveritySchema,
  summary: displayTextSchema,
  title: displayTextSchema
});

export const guidedDesignCanonicalExportSchema = z.strictObject({
  content: z.string().refine(
    (value) => (
      new TextEncoder().encode(value).byteLength <= guidedDesignMaximumCanonicalExportBytes
    ),
    { message: 'Guided Design export content exceeds 8 MiB of UTF-8.' }
  ),
  kind: z.enum(['spoiler', 'race']),
  mediaType: z.literal('application/json'),
  schemaVersion: z.literal(guidedDesignSchemaVersion),
  sha256: fingerprintSchema,
  suggestedFileName: z
    .string()
    .min(1)
    .max(128)
    .refine((value) => (
      value.trim() === value &&
      /^[\x20-\x7e]+$/u.test(value) &&
      !/[\\/:*?"<>|\u0000-\u001f\u007f]/u.test(value) &&
      value !== '.' &&
      value !== '..'
    ), { message: 'Expected a safe ASCII export file name.' })
});

export const guidedDesignCapabilitiesRequestSchema = z.strictObject({
  scope: semanticExploreScopeSchema
});

export const guidedDesignCapabilitiesResponseSchema = z
  .strictObject({
    capabilities: z.array(guidedDesignCapabilitySchema).length(9),
    revision: semanticExploreRevisionSchema,
    snapshots: z.array(semanticExploreSourceSnapshotSchema).max(4)
  })
  .superRefine((response, context) => {
    if (!distinct(response.capabilities, (capability) => capability.feature)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Guided Design feature capabilities must be unique.',
        path: ['capabilities']
      });
    }
  });

export const guidedDesignPreviewRequestSchema = z
  .strictObject({
    cursor: cursorSchema.nullable(),
    expectedChangeSetETag: fingerprintSchema.nullable(),
    expectedRevision: semanticExploreRevisionSchema,
    input: guidedDesignInputSchema,
    layer: guidedDesignSourceLayerSchema,
    limit: z.number().int().min(1).max(guidedDesignMaximumPageSize),
    proposalFingerprint: fingerprintSchema.nullable(),
    proposalId: fingerprintSchema.nullable(),
    scope: semanticExploreScopeSchema,
    targetSearchText: z.string().min(1).max(guidedDesignMaximumTargetSearchLength).refine(
      (value) => value.trim() === value && value.normalize('NFC') === value,
      { message: 'Guided Design target search must be trimmed NFC text.' }
    ).nullable()
  })
  .superRefine((request, context) => {
    const isContinuation = request.cursor !== null;
    if (
      isContinuation !== (request.proposalId !== null) ||
      isContinuation !== (request.proposalFingerprint !== null)
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'A Guided Design continuation requires its exact proposal identity.',
        path: ['cursor']
      });
    }
    if (request.input.targets.length > 0 && request.targetSearchText !== null) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Guided Design target search is only valid before exact targets are selected.',
        path: ['targetSearchText']
      });
    }
  });

export const guidedDesignPreviewResponseSchema = z.strictObject({
  affectedRecords: z.array(semanticExploreRecordRefSchema).max(guidedDesignMaximumAffectedRecords),
  authoringContextFingerprint: fingerprintSchema,
  canImport: z.boolean(),
  capabilities: z.array(guidedDesignCapabilitySchema).length(9),
  diagnostics: z.array(apiDiagnosticSchema).max(512),
  eligibleTargetWindowCapped: z.boolean(),
  eligibleTargets: z.array(guidedDesignTargetOptionSchema).max(guidedDesignMaximumPageSize),
  exports: z.strictObject({
    race: guidedDesignCanonicalExportSchema.nullable(),
    spoiler: guidedDesignCanonicalExportSchema.nullable()
  }),
  findings: z.array(guidedDesignFindingSchema).max(guidedDesignMaximumFindings),
  mutations: z.array(guidedDesignMutationSchema).max(guidedDesignMaximumPageSize),
  nextCursor: cursorSchema.nullable(),
  normalizedInput: guidedDesignInputSchema,
  normalizedTargetSearchText: z.string().min(1).max(guidedDesignMaximumTargetSearchLength).refine(
    (value) => value.trim() === value && value.normalize('NFC') === value,
    { message: 'Guided Design normalized target search must be trimmed NFC text.' }
  ).nullable(),
  proposalFingerprint: fingerprintSchema,
  proposalId: fingerprintSchema,
  queryFingerprint: fingerprintSchema,
  revision: semanticExploreRevisionSchema,
  seed: seedSchema.nullable(),
  snapshot: semanticExploreSourceSnapshotSchema,
  selectionRequired: z.boolean(),
  totalEligibleTargetCount: z.number().int().min(0).max(guidedDesignMaximumEligibleTargetCount),
  totalFindingCount: z.number().int().min(0).max(guidedDesignMaximumFindings),
  totalMutationCount: z.number().int().min(0).max(guidedDesignMaximumMutations)
}).superRefine((response, context) => {
  const normalizedPins = new Map(response.normalizedInput.pins.map((pin) => [
    JSON.stringify([recordKey(pin.record), pin.fieldKey]),
    pin
  ]));
  response.mutations.forEach((mutation, index) => {
    if (!mutation.pinned) return;
    const pin = mutation.pinRecord && mutation.pinFieldKey
      ? normalizedPins.get(JSON.stringify([recordKey(mutation.pinRecord), mutation.pinFieldKey]))
      : null;
    if (!pin || pin.canonicalValue !== mutation.after.canonicalValue) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'A pinned Guided Design mutation must match its exact normalized constraint.',
        path: ['mutations', index, 'pinned']
      });
    }
  });
  if (response.exports.spoiler && response.exports.spoiler.kind !== 'spoiler') {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'The Guided Design spoiler property must contain a spoiler export.',
      path: ['exports', 'spoiler', 'kind']
    });
  }
  if (response.exports.race && response.exports.race.kind !== 'race') {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'The Guided Design race property must contain a race export.',
      path: ['exports', 'race', 'kind']
    });
  }
  if (response.exports.race !== null) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Guided Design race exports are unavailable without a reviewed replay contract.',
      path: ['exports', 'race']
    });
  }
  if ((response.exports.spoiler !== null) !== response.canImport) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A Guided Design spoiler export must exactly match proposal importability.',
      path: ['exports', 'spoiler']
    });
  }
  if (response.eligibleTargets.length > response.totalEligibleTargetCount) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Guided Design returned inconsistent eligible-target totals.',
      path: ['eligibleTargets']
    });
  }
  if (
    response.eligibleTargetWindowCapped !==
      (response.totalEligibleTargetCount > guidedDesignMaximumEligibleTargetWindow)
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Guided Design returned an inconsistent eligible-target window.',
      path: ['eligibleTargetWindowCapped']
    });
  }
  if (response.selectionRequired) {
    if (
      response.canImport ||
      response.affectedRecords.length > 0 ||
      response.mutations.length > 0 ||
      response.findings.length > 0 ||
      response.totalMutationCount > 0 ||
      response.totalFindingCount > 0 ||
      response.normalizedInput.targets.length > 0
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'A Guided Design target-selection page cannot contain an importable proposal.',
        path: ['selectionRequired']
      });
    }
  } else {
    if (response.normalizedInput.targets.length === 0) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'A generated Guided Design proposal must contain an exact target.',
        path: ['normalizedInput', 'targets']
      });
    }
    if (
      response.totalEligibleTargetCount !== 0 ||
      response.eligibleTargets.length > 0 ||
      response.normalizedTargetSearchText !== null ||
      response.eligibleTargetWindowCapped
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'An importable Guided Design proposal cannot contain target-selection rows.',
        path: ['eligibleTargets']
      });
    }
  }
});

export const guidedDesignImportRequestSchema = z.strictObject({
  changeSetName: z.string().min(1).max(guidedDesignMaximumChangeSetNameLength).refine(
    (value) => value.trim() === value,
    { message: 'The Guided Design change-set name must be trimmed.' }
  ),
  expectedChangeSetETag: fingerprintSchema.nullable(),
  expectedRevision: semanticExploreRevisionSchema,
  input: guidedDesignInputSchema,
  proposalFingerprint: fingerprintSchema,
  proposalId: fingerprintSchema,
  scope: semanticExploreScopeSchema
}).superRefine((request, context) => {
  if (request.input.targets.length === 0) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A Guided Design import requires at least one exact target.',
      path: ['input', 'targets']
    });
  }
});

export const guidedDesignImportResponseSchema = z
  .strictObject({
    diagnostics: z.array(apiDiagnosticSchema).max(512),
    importedChangeSetId: stableIdSchema,
    proposalFingerprint: fingerprintSchema,
    proposalId: fingerprintSchema,
    revision: semanticExploreRevisionSchema,
    snapshot: changeSetWorkspaceSnapshotSchema
  })
  .superRefine((response, context) => {
    const imported = response.snapshot.document.changeSets.find(
      (changeSet) => changeSet.changeSetId === response.importedChangeSetId
    );
    if (!imported || imported.enabled || imported.archived) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The imported Guided Design proposal must be a current disabled change set.',
        path: ['importedChangeSetId']
      });
    }
    if (response.snapshot.document.activeChangeSetId === response.importedChangeSetId) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The imported Guided Design proposal cannot become the active staging target.',
        path: ['snapshot', 'document', 'activeChangeSetId']
      });
    }
  });

export type GuidedDesignFeature = z.infer<typeof guidedDesignFeatureSchema>;
export type GuidedDesignProposalKind = z.infer<typeof guidedDesignProposalKindSchema>;
export type GuidedDesignRounding = z.infer<typeof guidedDesignRoundingSchema>;
export type GuidedDesignTrainerArchetype = z.infer<typeof guidedDesignTrainerArchetypeSchema>;
export type GuidedDesignCapability = z.infer<typeof guidedDesignCapabilitySchema>;
export type GuidedDesignPin = z.infer<typeof guidedDesignPinSchema>;
export type GuidedDesignInput = z.infer<typeof guidedDesignInputSchema>;
export type GuidedDesignMutation = z.infer<typeof guidedDesignMutationSchema>;
export type GuidedDesignTargetOption = z.infer<typeof guidedDesignTargetOptionSchema>;
export type GuidedDesignFinding = z.infer<typeof guidedDesignFindingSchema>;
export type GuidedDesignCanonicalExport = z.infer<typeof guidedDesignCanonicalExportSchema>;
export type GuidedDesignCapabilitiesRequest = z.infer<
  typeof guidedDesignCapabilitiesRequestSchema
>;
export type GuidedDesignCapabilitiesResponse = z.infer<
  typeof guidedDesignCapabilitiesResponseSchema
>;
export type GuidedDesignPreviewRequest = z.infer<typeof guidedDesignPreviewRequestSchema>;
export type GuidedDesignPreviewResponse = z.infer<typeof guidedDesignPreviewResponseSchema>;
export type GuidedDesignImportRequest = z.infer<typeof guidedDesignImportRequestSchema>;
export type GuidedDesignImportResponse = z.infer<typeof guidedDesignImportResponseSchema>;
