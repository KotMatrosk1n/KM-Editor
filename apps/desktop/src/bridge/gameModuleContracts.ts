/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { apiDiagnosticSchema } from './contracts';
import {
  semanticExploreGameFamilySchema,
  semanticExploreRecordRefSchema,
  semanticExploreRevisionSchema,
  semanticExploreScalarSchema,
  semanticExploreScopeSchema,
  semanticExploreSourceSnapshotSchema
} from './semanticExploreContracts';

export const gameModuleDefaultPageSize = 50;
export const gameModuleMaximumPageSize = 100;
export const gameModuleMaximumAccumulatedRecords = 500;
export const gameModuleMaximumDiagnostics = 100;
export const gameModuleMaximumRecordCount = 400_000;

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
const recordKindSchema = z
  .string()
  .min(1)
  .max(64)
  .regex(/^[a-z][A-Za-z0-9]*$/u);
const unsafeTextPattern = /[\u0000-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/iu;
const stableIdSchema = z
  .string()
  .min(1)
  .max(1_024)
  .refine((value) => (
    value.trim() === value &&
    !unsafeTextPattern.test(value) &&
    !containsGameModuleLocalPathSignature(value)
  ));
const displayTextSchema = (maximumLength: number) => z
  .string()
  .min(1)
  .max(maximumLength)
  .refine((value) => (
    value.trim() === value &&
    !unsafeTextPattern.test(value) &&
    !containsGameModuleLocalPathSignature(value)
  ));
const fingerprintSchema = z.string().regex(/^[a-f0-9]{64}$/u);
const cursorSchema = z
  .string()
  .min(1)
  .max(2_048)
  .regex(/^[A-Za-z0-9_-]+$/u);

const gameModuleRecordRefSchema = semanticExploreRecordRefSchema.superRefine((record, context) => {
  if ([
    record.gameFamily,
    record.domain,
    record.recordKind.key,
    record.recordId,
    record.subrecordId
  ].some((component) => component !== null && containsGameModuleLocalPathSignature(component))) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'The game module record reference contains a local path.'
    });
  }
});
const gameModuleSnapshotSchema = semanticExploreSourceSnapshotSchema.superRefine(
  (snapshot, context) => {
    if (
      snapshot.layer.instanceId !== null &&
      containsGameModuleLocalPathSignature(snapshot.layer.instanceId)
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The game module source snapshot contains a local path.',
        path: ['layer', 'instanceId']
      });
    }
  }
);
const gameModuleScalarSchema = semanticExploreScalarSchema.superRefine((value, context) => {
  if (
    value.displayValue.trim() !== value.displayValue ||
    value.displayValue.length > 512 ||
    unsafeTextPattern.test(value.displayValue) ||
    containsGameModuleLocalPathSignature(value.displayValue) ||
    (value.kind === 'null') !== (value.canonicalValue === null) ||
    (value.canonicalValue !== null && (
      value.canonicalValue.trim() !== value.canonicalValue ||
      value.canonicalValue.length > 512 ||
      unsafeTextPattern.test(value.canonicalValue) ||
      containsGameModuleLocalPathSignature(value.canonicalValue)
    ))
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'The game module scalar exceeds its private bounded representation.'
    });
  }
});
const gameModuleDiagnosticSchema = apiDiagnosticSchema.superRefine((diagnostic, context) => {
  if ([
    diagnostic.domain,
    diagnostic.expected,
    diagnostic.field,
    diagnostic.file,
    diagnostic.message
  ].some((value) => value !== null && value !== undefined && (
    value.length > 1_024 || containsGameModuleLocalPathSignature(value)
  ))) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'The game module diagnostic is not private and bounded.'
    });
  }
});

export const gameModuleValues = [
  'swordShieldRewardEcosystem',
  'swordShieldExeFsCompatibility',
  'swordShieldDynamaxAdventures',
  'swordShieldRoyalCandyProgression',
  'swordShieldBattleCafeRewards',
  'swordShieldEventAssignments',
  'scarletVioletTeraRaidAnalysis',
  'scarletVioletPackedLooseComparison',
  'scarletVioletEventDataComparison',
  'scarletVioletScenePlacementEditing',
  'scarletVioletTypeEffectivenessState',
  'scarletVioletStellarBehavior',
  'legendsZaScriptedBossTimeline',
  'legendsZaTrainerArchetypes',
  'legendsZaWildSpawnExplorer',
  'legendsZaEncounterCompatibility',
  'legendsZaAlphaMoveDistribution',
  'legendsZaDexLayoutPlanning',
  'legendsZaMoveVariantComparison',
  'legendsZaTrainerPoolSwitching',
  'legendsZaTypeEffectivenessState'
] as const;

export const gameModuleSchema = z.enum(gameModuleValues);
export const gameModuleMaximumCapabilities = gameModuleValues.length;
export const gameModuleMaturitySchema = z.enum(['product', 'readOnlyFirst', 'researchGated']);
export const gameModuleCoverageStateSchema = z.enum(['complete', 'partial', 'unavailable']);
export const gameModuleConfidenceSchema = z.enum(['unknown', 'verified', 'derived']);
export const gameModuleLayerSchema = z.literal('layered');
export const gameModuleReasonCodeSchema = z.enum([
  'unified-acquisition-provider-unavailable',
  'bounded-nso-decoder-unavailable',
  'bounded-route-analysis-provider-unavailable',
  'bounded-progression-provider-unavailable',
  'trainer-payout-and-runtime-acquisition-order-unavailable',
  'patch-interaction-and-unlisted-build-coverage-unavailable',
  'runtime-route-generation-and-unlisted-build-coverage-unavailable',
  'runtime-progression-evaluation-unavailable',
  'runtime-scene-availability-unavailable',
  'scene-script-assignments-and-runtime-audio-resolution-unavailable',
  'trainer-type-event-executable-build-unverified',
  'battle-cafe-source-unavailable',
  'battle-cafe-source-shape-unverified',
  'trainer-type-event-source-incomplete',
  'trainer-type-event-identity-ambiguous',
  'trainer-type-event-source-unavailable',
  'trainer-type-event-source-shape-unverified',
  'research-evidence-required',
  'progression-unlock-and-rotation-coverage-unavailable',
  'packed-loose-comparison-contract-missing',
  'source-content-decoding-outside-scope',
  'unmapped-event-fields-remain-opaque',
  'coordinates-rotations-naming-and-unowned-scene-fields-excluded',
  'stellar-and-runtime-effect-resolution-unavailable',
  'verified-event-comparison-provider-unavailable',
  'runtime-execution-order-unavailable',
  'class-and-presentation-semantics-research-gated',
  'placement-and-runtime-reachability-coverage-unavailable',
  'read-only-compatibility-projection-missing',
  'bounded-pokemon-projection-unavailable',
  'bounded-executable-observer-unavailable',
  'movement-proposals-and-per-species-mega-membership-unavailable',
  'runtime-city-behavior-and-unlisted-attachment-coverage-unavailable',
  'mapping-addition-and-runtime-selection-coverage-unavailable',
  'pool-resizing-and-runtime-selection-coverage-unavailable',
  'edit-proposals-and-runtime-effect-resolution-unavailable',
  'variant-consumer-coverage-unavailable',
  'verified-trainer-pool-provider-unavailable',
  'workflow-disabled',
  'workflow-source-invalid',
  'workflow-source-unavailable',
  'bounded-provider-limit-exceeded',
  'bounded-provider-unavailable'
]);

const readinessReasonCodes = new Set([
  'workflow-disabled',
  'workflow-source-invalid',
  'workflow-source-unavailable',
  'bounded-provider-limit-exceeded',
  'bounded-provider-unavailable',
  'trainer-type-event-executable-build-unverified',
  'battle-cafe-source-unavailable',
  'battle-cafe-source-shape-unverified',
  'trainer-type-event-source-incomplete',
  'trainer-type-event-identity-ambiguous',
  'trainer-type-event-source-unavailable',
  'trainer-type-event-source-shape-unverified'
]);
const expectedCapabilityByModule = {
  swordShieldRewardEcosystem: ['readOnlyFirst', 'swsh.game-modules.reward-ecosystem', 'trainer-payout-and-runtime-acquisition-order-unavailable', true],
  swordShieldExeFsCompatibility: ['product', 'swsh.game-modules.exefs-compatibility', 'patch-interaction-and-unlisted-build-coverage-unavailable', true],
  swordShieldDynamaxAdventures: ['product', 'swsh.game-modules.dynamax-adventures', 'runtime-route-generation-and-unlisted-build-coverage-unavailable', true],
  swordShieldRoyalCandyProgression: ['product', 'swsh.game-modules.royal-candy-progression', 'runtime-progression-evaluation-unavailable', true],
  swordShieldBattleCafeRewards: ['readOnlyFirst', 'swsh.game-modules.battle-cafe-rewards', 'runtime-scene-availability-unavailable', true],
  swordShieldEventAssignments: ['readOnlyFirst', 'swsh.game-modules.event-assignments', 'scene-script-assignments-and-runtime-audio-resolution-unavailable', true],
  scarletVioletTeraRaidAnalysis: ['product', 'sv.game-modules.tera-raid-analysis', 'progression-unlock-and-rotation-coverage-unavailable', true],
  scarletVioletPackedLooseComparison: ['product', 'sv.game-modules.packed-loose-comparison', 'source-content-decoding-outside-scope', true],
  scarletVioletEventDataComparison: ['product', 'sv.game-modules.event-data-comparison', 'unmapped-event-fields-remain-opaque', true],
  scarletVioletScenePlacementEditing: ['readOnlyFirst', 'sv.game-modules.scene-placement', 'coordinates-rotations-naming-and-unowned-scene-fields-excluded', true],
  scarletVioletTypeEffectivenessState: ['readOnlyFirst', 'sv.game-modules.type-effectiveness-state', 'stellar-and-runtime-effect-resolution-unavailable', true],
  scarletVioletStellarBehavior: ['researchGated', 'sv.game-modules.stellar-behavior', 'research-evidence-required', false],
  legendsZaScriptedBossTimeline: ['readOnlyFirst', 'za.game-modules.scripted-boss-timeline', 'runtime-execution-order-unavailable', true],
  legendsZaTrainerArchetypes: ['product', 'za.game-modules.trainer-archetypes', 'class-and-presentation-semantics-research-gated', true],
  legendsZaWildSpawnExplorer: ['readOnlyFirst', 'za.game-modules.wild-spawn-explorer', 'placement-and-runtime-reachability-coverage-unavailable', true],
  legendsZaEncounterCompatibility: ['product', 'za.game-modules.encounter-compatibility', 'runtime-city-behavior-and-unlisted-attachment-coverage-unavailable', true],
  legendsZaAlphaMoveDistribution: ['product', 'za.game-modules.alpha-move-distribution', 'mapping-addition-and-runtime-selection-coverage-unavailable', true],
  legendsZaDexLayoutPlanning: ['product', 'za.game-modules.dex-layout-planning', 'movement-proposals-and-per-species-mega-membership-unavailable', true],
  legendsZaMoveVariantComparison: ['product', 'za.game-modules.move-variant-comparison', 'variant-consumer-coverage-unavailable', true],
  legendsZaTrainerPoolSwitching: ['product', 'za.game-modules.trainer-pool-switching', 'pool-resizing-and-runtime-selection-coverage-unavailable', true],
  legendsZaTypeEffectivenessState: ['readOnlyFirst', 'za.game-modules.type-effectiveness-state', 'edit-proposals-and-runtime-effect-resolution-unavailable', true]
} as const;

export const gameModuleCapabilitySchema = z
  .strictObject({
    canQuery: z.boolean(),
    confidence: gameModuleConfidenceSchema,
    family: semanticExploreGameFamilySchema,
    maturity: gameModuleMaturitySchema,
    module: gameModuleSchema,
    providerId: contractKeySchema,
    reasonCode: gameModuleReasonCodeSchema.nullable(),
    state: gameModuleCoverageStateSchema,
    supportedLayers: z.array(gameModuleLayerSchema).max(3)
  })
  .superRefine((capability, context) => {
    const unavailable = capability.state === 'unavailable';
    const expected = expectedCapabilityByModule[capability.module];
    const hasExpectedReason = capability.canQuery
      ? expected[3] && capability.reasonCode === expected[2]
      : expected[3]
        ? capability.reasonCode !== null && readinessReasonCodes.has(capability.reasonCode)
        : capability.reasonCode === expected[2];
    if (moduleFamily(capability.module) !== capability.family) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The game module belongs to another game family.',
        path: ['family']
      });
    }
    if (
      capability.maturity !== expected[0] ||
      capability.providerId !== expected[1] ||
      !hasExpectedReason ||
      capability.canQuery && !expected[3] ||
      capability.maturity === 'researchGated' && capability.canQuery ||
      capability.canQuery !== (capability.supportedLayers.length > 0) ||
      capability.canQuery === unavailable ||
      unavailable && capability.confidence !== 'unknown' ||
      capability.canQuery && (
        capability.state !== 'partial' ||
        capability.confidence !== 'verified' ||
        capability.supportedLayers.length !== 1 ||
        capability.supportedLayers[0] !== 'layered'
      ) ||
      new Set(capability.supportedLayers).size !== capability.supportedLayers.length
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The game module query boundary is inconsistent.',
        path: ['canQuery']
      });
    }
  });

export const readGameModuleCapabilitiesRequestSchema = z.strictObject({
  scope: semanticExploreScopeSchema
});

export const readGameModuleCapabilitiesResponseSchema = z
  .strictObject({
    capabilities: z.array(gameModuleCapabilitySchema).max(gameModuleMaximumCapabilities),
    revision: semanticExploreRevisionSchema,
    snapshots: z.array(gameModuleSnapshotSchema).max(4)
  })
  .superRefine((response, context) => {
    const modules = response.capabilities.map((capability) => capability.module);
    const snapshotLayers = response.snapshots.map((snapshot) => snapshot.layer.kind);
    if (
      response.capabilities.some((capability) => capability.family !== response.revision.gameFamily) ||
      new Set(modules).size !== modules.length ||
      new Set(snapshotLayers).size !== snapshotLayers.length
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The game module capability catalog is inconsistent.',
        path: ['capabilities']
      });
    }
    if (response.snapshots.some((snapshot) => (
      revisionIdentity(snapshot.revision) !== revisionIdentity(response.revision)
    ))) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'A game module snapshot belongs to another revision.',
        path: ['snapshots']
      });
    }
  });

export const gameModuleFactSchema = z
  .strictObject({
    confidence: gameModuleConfidenceSchema,
    evidence: z.array(gameModuleRecordRefSchema).max(16),
    factId: stableIdSchema,
    fieldKey: fieldKeySchema,
    label: displayTextSchema(128),
    providerId: contractKeySchema,
    unit: displayTextSchema(64).nullable(),
    value: gameModuleScalarSchema
  })
  .superRefine((fact, context) => {
    const evidence = fact.evidence.map(semanticRecordIdentity);
    if (
      new Set(evidence).size !== evidence.length
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The game module fact is not private or uniquely evidenced.'
      });
    }
  });

export const gameModuleRecordSchema = z
  .strictObject({
    confidence: gameModuleConfidenceSchema,
    coverage: gameModuleCoverageStateSchema,
    facts: z.array(gameModuleFactSchema).max(32),
    groupId: stableIdSchema.nullable(),
    parentRecordId: stableIdSchema.nullable(),
    recordId: stableIdSchema,
    recordKind: recordKindSchema,
    sortOrder: z.number().int().min(0).max(2_147_483_647),
    summary: displayTextSchema(1_024),
    target: gameModuleRecordRefSchema.nullable(),
    title: displayTextSchema(256)
  })
  .superRefine((record, context) => {
    const factIds = record.facts.map((fact) => fact.factId);
    const fieldKeys = record.facts.map((fact) => fact.fieldKey);
    if (
      new Set(factIds).size !== factIds.length ||
      new Set(fieldKeys).size !== fieldKeys.length
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The game module record contains duplicate facts.',
        path: ['facts']
      });
    }
  });

export const queryGameModuleRequestSchema = z.strictObject({
  cursor: cursorSchema.optional(),
  expectedRevision: semanticExploreRevisionSchema,
  layer: gameModuleLayerSchema,
  limit: z.number().int().min(1).max(gameModuleMaximumPageSize),
  module: gameModuleSchema,
  scope: semanticExploreScopeSchema
});

export const queryGameModuleResponseSchema = z
  .strictObject({
    capability: gameModuleCapabilitySchema,
    diagnostics: z.array(gameModuleDiagnosticSchema).max(gameModuleMaximumDiagnostics),
    nextCursor: cursorSchema.nullable(),
    queryFingerprint: fingerprintSchema,
    records: z.array(gameModuleRecordSchema).max(gameModuleMaximumPageSize),
    revision: semanticExploreRevisionSchema,
    snapshot: gameModuleSnapshotSchema,
    totalRecordCount: z.number().int().min(0).max(gameModuleMaximumRecordCount)
  })
  .superRefine((response, context) => {
    const recordIds = response.records.map((record) => record.recordId);
    const factIds = response.records.flatMap((record) => (
      record.facts.map((fact) => fact.factId)
    ));
    if (
      response.capability.family !== response.revision.gameFamily ||
      !response.capability.canQuery ||
      response.capability.state === 'unavailable' ||
      response.totalRecordCount < response.records.length ||
      revisionIdentity(response.snapshot.revision) !== revisionIdentity(response.revision) ||
      new Set(recordIds).size !== recordIds.length ||
      new Set(factIds).size !== factIds.length ||
      (response.nextCursor !== null && response.records.length === 0)
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The game module query response is internally inconsistent.'
      });
    }
  });

export type GameModule = z.infer<typeof gameModuleSchema>;
export type GameModuleMaturity = z.infer<typeof gameModuleMaturitySchema>;
export type GameModuleCoverageState = z.infer<typeof gameModuleCoverageStateSchema>;
export type GameModuleConfidence = z.infer<typeof gameModuleConfidenceSchema>;
export type GameModuleLayer = z.infer<typeof gameModuleLayerSchema>;
export type GameModuleReasonCode = z.infer<typeof gameModuleReasonCodeSchema>;
export type GameModuleCapability = z.infer<typeof gameModuleCapabilitySchema>;
export type ReadGameModuleCapabilitiesRequest = z.infer<
  typeof readGameModuleCapabilitiesRequestSchema
>;
export type ReadGameModuleCapabilitiesResponse = z.infer<
  typeof readGameModuleCapabilitiesResponseSchema
>;
export type GameModuleFact = z.infer<typeof gameModuleFactSchema>;
export type GameModuleRecord = z.infer<typeof gameModuleRecordSchema>;
export type QueryGameModuleRequest = z.infer<typeof queryGameModuleRequestSchema>;
export type QueryGameModuleResponse = z.infer<typeof queryGameModuleResponseSchema>;

export function containsGameModuleLocalPathSignature(value: string) {
  let candidate = value;
  for (let depth = 0; depth <= 3; depth += 1) {
    const isVirtualSourceIdentity = isSafeGameModuleVirtualSourceIdentity(candidate);
    if (
      candidate.includes('\\') ||
      (!isVirtualSourceIdentity && candidate.split('|').some((component) => (
        component.includes('/') && component !== 'Scarlet/Violet'
      ))) ||
      /(?:^|[^A-Za-z0-9])[A-Za-z]:/u.test(candidate) ||
      /(?:^|[^A-Za-z0-9])file:/iu.test(candidate) ||
      candidate.startsWith('~')
    ) return true;
    if (depth === 3 || !candidate.includes('%')) break;
    try {
      const decoded = decodeURIComponent(candidate);
      if (decoded === candidate) break;
      candidate = decoded;
    } catch {
      return true;
    }
  }
  return false;
}

function isSafeGameModuleVirtualSourceIdentity(value: string) {
  if (
    !value.startsWith('romfs/') ||
    value.length > 512 ||
    new TextEncoder().encode(value).length > 512 ||
    value.includes('\\') ||
    value.includes(':') ||
    unsafeTextPattern.test(value)
  ) return false;
  const virtualPath = value.slice('romfs/'.length);
  return virtualPath.length > 0 && virtualPath.split('/').every((segment) => (
    segment.trim().length > 0 && segment !== '.' && segment !== '..'
  ));
}

export function moduleFamily(module: GameModule) {
  if (module.startsWith('swordShield')) return 'swordShield' as const;
  if (module.startsWith('scarletViolet')) return 'scarletViolet' as const;
  return 'legendsZA' as const;
}

export function expectedModulesForFamily(
  family: z.infer<typeof semanticExploreGameFamilySchema>
): readonly GameModule[] {
  return gameModuleValues.filter((module) => moduleFamily(module) === family);
}

function revisionIdentity(revision: z.infer<typeof semanticExploreRevisionSchema>) {
  return JSON.stringify([
    revision.projectId,
    revision.gameFamily,
    revision.generation,
    revision.fingerprint
  ]);
}

function semanticRecordIdentity(record: z.infer<typeof semanticExploreRecordRefSchema>) {
  return JSON.stringify([
    record.gameFamily,
    record.domain,
    record.recordKind.key,
    record.recordKind.schemaVersion,
    record.recordId,
    record.subrecordId
  ]);
}
