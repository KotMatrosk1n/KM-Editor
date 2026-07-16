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

export const gymUniformRemovalInstallStatusSchema = z.enum([
  'disabled',
  'blocked',
  'foreign',
  'readOnly',
  'available',
  'installed'
]);

export const gymUniformRemovalMainHandlerStateSchema = z.enum([
  'notInspected',
  'vanilla',
  'kmReturnTrue',
  'compatibleReturnTrue',
  'foreign',
  'conflict',
  'unsupported',
  'gameMismatch',
  'unreadable'
]);

export const gymUniformRemovalIpsArtifactStateSchema = z.enum([
  'notInspected',
  'notPresent',
  'current',
  'legacy',
  'foreign',
  'invalid'
]);

export const gymUniformRemovalProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.literal('exefs/main'),
  sourceLayer: projectFileLayerSchema
});

export const gymUniformRemovalReservedRegionSchema = z.strictObject({
  label: z.string().min(1),
  length: z.number().int().positive().nullable(),
  offsetLabel: z.string().min(1),
  regionId: z.string().min(1),
  rule: z.string().min(1),
  startOffset: z.number().int().nonnegative().nullable()
});

export const gymUniformRemovalWorkflowStatsSchema = z.strictObject({
  ownedByteCount: z.number().int().nonnegative(),
  reservedMainTextRegionCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

const gymUniformRemovalWorkflowShapeSchema = z.strictObject({
  buildId: z.union([z.literal('unknown'), z.string().regex(/^[A-F0-9]{40}$/)]),
  canUninstall: z.boolean(),
  detectedGame: z.enum(['sword', 'shield']).nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  installMessage: z.string().min(1),
  installStatus: gymUniformRemovalInstallStatusSchema,
  ipsArtifactState: gymUniformRemovalIpsArtifactStateSchema,
  mainHandlerState: gymUniformRemovalMainHandlerStateSchema,
  patchOffsetHex: z.string().min(1),
  provenance: gymUniformRemovalProvenanceSchema,
  reservedRegions: z.array(gymUniformRemovalReservedRegionSchema),
  stats: gymUniformRemovalWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const gymUniformRemovalWorkflowSchema =
  gymUniformRemovalWorkflowShapeSchema.superRefine(validateGymUniformRemovalWorkflow);

export const loadGymUniformRemovalWorkflowRequestSchema = z
  .strictObject({ paths: projectPathsSchema })
  .superRefine(validateGymUniformRemovalRequestGame);

export const loadGymUniformRemovalWorkflowResponseSchema = z.strictObject({
  workflow: gymUniformRemovalWorkflowSchema
});

export const stageGymUniformRemovalInstallRequestSchema = z
  .strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable() })
  .superRefine(validateGymUniformRemovalRequestGame);

export const stageGymUniformRemovalInstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: gymUniformRemovalWorkflowSchema
});

export const stageGymUniformRemovalUninstallRequestSchema = z
  .strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable() })
  .superRefine(validateGymUniformRemovalRequestGame);

export const stageGymUniformRemovalUninstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: gymUniformRemovalWorkflowSchema
});

export type GymUniformRemovalAction = 'install' | 'uninstall';
export type GymUniformRemovalReservedRegion = z.infer<
  typeof gymUniformRemovalReservedRegionSchema
>;
export type GymUniformRemovalWorkflow = z.infer<typeof gymUniformRemovalWorkflowSchema>;
export type LoadGymUniformRemovalWorkflowRequest = z.infer<
  typeof loadGymUniformRemovalWorkflowRequestSchema
>;
export type LoadGymUniformRemovalWorkflowResponse = z.infer<
  typeof loadGymUniformRemovalWorkflowResponseSchema
>;
export type StageGymUniformRemovalInstallRequest = z.infer<
  typeof stageGymUniformRemovalInstallRequestSchema
>;
export type StageGymUniformRemovalInstallResponse = z.infer<
  typeof stageGymUniformRemovalInstallResponseSchema
>;
export type StageGymUniformRemovalUninstallRequest = z.infer<
  typeof stageGymUniformRemovalUninstallRequestSchema
>;
export type StageGymUniformRemovalUninstallResponse = z.infer<
  typeof stageGymUniformRemovalUninstallResponseSchema
>;

const identities = {
  sword: {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    ipsRelativePath: 'exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips',
    patchOffsetHex: 'main.text+0x01472600',
    region: {
      label: 'Gym Uniform Removal Sword uniform-change handler',
      length: 8,
      offsetLabel: 'text+0x1472600..0x1472607',
      regionId: 'gym-uniform-removal-sword-handler',
      rule: 'do-not-overwrite',
      startOffset: 0x01472600
    }
  },
  shield: {
    buildId: 'A16802625E7826BF83B6F9708E475B912A9AB7DF',
    ipsRelativePath: 'exefs/A16802625E7826BF83B6F9708E475B912A9AB7DF.ips',
    patchOffsetHex: 'main.text+0x01472630',
    region: {
      label: 'Gym Uniform Removal Shield uniform-change handler',
      length: 8,
      offsetLabel: 'text+0x1472630..0x1472637',
      regionId: 'gym-uniform-removal-shield-handler',
      rule: 'do-not-overwrite',
      startOffset: 0x01472630
    }
  }
} as const;

export function getGymUniformRemovalIpsRelativePath(game: 'sword' | 'shield') {
  return identities[game].ipsRelativePath;
}

function validateGymUniformRemovalWorkflow(
  workflow: z.infer<typeof gymUniformRemovalWorkflowShapeSchema>,
  context: z.RefinementCtx
) {
  if (
    workflow.summary.id !== 'gymUniformRemoval' ||
    workflow.summary.label !== 'Gym Uniform Removal'
  ) {
    addIssue(context, ['summary'], 'Gym Uniform Removal workflow identity is not canonical.');
  }

  if (!hasValidAvailabilityStatus(workflow)) {
    addIssue(
      context,
      ['installStatus'],
      'Gym Uniform Removal install status does not match workflow availability.'
    );
  }

  if (!hasValidProvenance(workflow)) {
    addIssue(
      context,
      ['provenance'],
      'Gym Uniform Removal provenance does not describe a possible exefs/main source.'
    );
  }

  if (
    workflow.summary.availability === 'readOnly' &&
    workflow.provenance.sourceLayer === 'layered'
  ) {
    addIssue(
      context,
      ['provenance', 'sourceLayer'],
      'Read-only Gym Uniform Removal cannot report a LayeredFS source.'
    );
  }

  if (workflow.installStatus === 'disabled') {
    const hasSelectedGameRange = Object.values(identities).some((candidate) =>
      hasExactRegion(workflow.reservedRegions, candidate.region)
    );
    if (
      workflow.buildId !== 'unknown' ||
      workflow.detectedGame !== null ||
      workflow.patchOffsetHex !== 'unknown' ||
      workflow.mainHandlerState !== 'notInspected' ||
      workflow.ipsArtifactState !== 'notInspected' ||
      workflow.canUninstall ||
      !hasSelectedGameRange ||
      workflow.reservedRegions.length !== 1 ||
      workflow.stats.ownedByteCount !== 8 ||
      workflow.stats.reservedMainTextRegionCount !== 1 ||
      workflow.stats.sourceFileCount !== 0 ||
      workflow.provenance.sourceLayer !== 'generated' ||
      workflow.provenance.fileState !== 'baseOnly'
    ) {
      addIssue(
        context,
        ['installStatus'],
        'Disabled Gym Uniform Removal state is not canonical.'
      );
    }
    return;
  }

  const hasNoBaseProvenance =
    workflow.provenance.sourceLayer === 'generated' ||
    workflow.provenance.fileState === 'layeredOnly';
  if (
    hasNoBaseProvenance &&
    (workflow.buildId !== 'unknown' ||
      workflow.detectedGame !== null ||
      workflow.patchOffsetHex !== 'unknown' ||
      workflow.mainHandlerState !== 'notInspected' ||
      workflow.ipsArtifactState !== 'notInspected' ||
      workflow.canUninstall ||
      workflow.stats.sourceFileCount !== 0)
  ) {
    addIssue(
      context,
      ['provenance'],
      'Gym Uniform Removal without base provenance cannot claim executable or IPS inspection.'
    );
  }

  const identity = workflow.detectedGame ? identities[workflow.detectedGame] : null;
  if (identity) {
    if (
      workflow.buildId !== identity.buildId ||
      workflow.patchOffsetHex !== identity.patchOffsetHex
    ) {
      addIssue(
        context,
        ['detectedGame'],
        'Gym Uniform Removal build and patch offset do not match the detected game.'
      );
    }
  } else if (workflow.patchOffsetHex !== 'unknown') {
    addIssue(
      context,
      ['patchOffsetHex'],
      'Undetected Gym Uniform Removal patch offset must be unknown.'
    );
  }

  const expectedRegion = identity === null
    ? null
    : workflow.mainHandlerState === 'gameMismatch'
      ? identities[workflow.detectedGame === 'sword' ? 'shield' : 'sword'].region
      : identity.region;
  const hasActiveRegion = expectedRegion
    ? hasExactRegion(workflow.reservedRegions, expectedRegion)
    : Object.values(identities).some((candidate) =>
        hasExactRegion(workflow.reservedRegions, candidate.region)
      );

  if (
    !hasActiveRegion ||
    workflow.reservedRegions.length !== 1 ||
    workflow.stats.reservedMainTextRegionCount !== 1 ||
    workflow.stats.ownedByteCount !== 8
  ) {
    addIssue(
      context,
      ['reservedRegions'],
      'Gym Uniform Removal active range and owned byte count are not canonical.'
    );
  }

  if (workflow.stats.sourceFileCount > 3) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Gym Uniform Removal has at most three verified physical sources.'
    );
  }

  if (
    workflow.provenance.sourceLayer === 'generated' &&
    workflow.stats.sourceFileCount !== 0
  ) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Generated Gym Uniform Removal provenance cannot report verified sources.'
    );
  }

  if (
    ['available', 'readOnly', 'installed'].includes(workflow.installStatus) &&
    (identity === null || workflow.mainHandlerState === 'gameMismatch')
  ) {
    addIssue(
      context,
      ['detectedGame'],
      'A loaded Gym Uniform Removal state requires the selected supported game identity.'
    );
  }

  const verifiedMainSourceCounts = getVerifiedMainSourceCounts(workflow);
  const hasReadableIps = !['notInspected', 'notPresent'].includes(workflow.ipsArtifactState);
  const expectedVerifiedSourceCounts = verifiedMainSourceCounts === null
    ? null
    : verifiedMainSourceCounts.map((count) => count + (hasReadableIps ? 1 : 0));
  const sourceCountMatches =
    expectedVerifiedSourceCounts?.includes(workflow.stats.sourceFileCount) === true;
  if (
    expectedVerifiedSourceCounts !== null &&
    ['available', 'readOnly', 'installed', 'foreign'].includes(workflow.installStatus) &&
    !sourceCountMatches
  ) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Gym Uniform Removal source count does not match its verified main and IPS inputs.'
    );
  }

  if (
    workflow.installStatus === 'blocked' &&
    expectedVerifiedSourceCounts !== null &&
    !(workflow.ipsArtifactState === 'notInspected' && workflow.stats.sourceFileCount === 0) &&
    !sourceCountMatches
  ) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Blocked Gym Uniform Removal source count does not match its verified inputs.'
    );
  }

  if (
    workflow.provenance.sourceLayer === 'base' &&
    workflow.stats.sourceFileCount > 0 &&
    workflow.mainHandlerState !== 'vanilla'
  ) {
    addIssue(
      context,
      ['mainHandlerState'],
      'Verified base-only Gym Uniform Removal must retain the vanilla main handler.'
    );
  }

  if (
    ['notInspected', 'unsupported', 'gameMismatch'].includes(workflow.mainHandlerState) &&
    (workflow.ipsArtifactState !== 'notInspected' || workflow.stats.sourceFileCount !== 0)
  ) {
    addIssue(
      context,
      ['mainHandlerState'],
      'Pre-verification Gym Uniform Removal main state cannot claim inspected sources or IPS state.'
    );
  }

  if (
    (workflow.installStatus === 'available' || workflow.installStatus === 'readOnly') &&
    workflow.ipsArtifactState !== 'notPresent'
  ) {
    addIssue(
      context,
      ['ipsArtifactState'],
      'Available Gym Uniform Removal must report that its IPS artifact is not present.'
    );
  }

  const hasEditableMain = ['vanilla', 'kmReturnTrue', 'compatibleReturnTrue'].includes(
    workflow.mainHandlerState
  );
  if (
    ['available', 'readOnly', 'installed'].includes(workflow.installStatus) &&
    !hasEditableMain
  ) {
    addIssue(
      context,
      ['mainHandlerState'],
      'Loaded Gym Uniform Removal status requires editable recognized main handler bytes.'
    );
  }

  if (
    workflow.installStatus === 'blocked' &&
    hasEditableMain &&
    workflow.ipsArtifactState !== 'invalid' &&
    workflow.ipsArtifactState !== 'notInspected'
  ) {
    addIssue(
      context,
      ['installStatus'],
      'Blocked Gym Uniform Removal must identify a main or IPS artifact blocker.'
    );
  }

  if (
    workflow.installStatus === 'installed' &&
    !['current', 'legacy'].includes(workflow.ipsArtifactState)
  ) {
    addIssue(
      context,
      ['ipsArtifactState'],
      'Installed Gym Uniform Removal requires a recognized KM IPS artifact.'
    );
  }

  if (
    workflow.installStatus === 'foreign' &&
    !(
      (workflow.mainHandlerState === 'foreign' &&
        ['notPresent', 'foreign'].includes(workflow.ipsArtifactState)) ||
      (hasEditableMain && workflow.ipsArtifactState === 'foreign')
    )
  ) {
    addIssue(
      context,
      ['installStatus'],
      'Foreign Gym Uniform Removal must identify the foreign main or IPS artifact.'
    );
  }

  if (
    ['current', 'legacy'].includes(workflow.ipsArtifactState) &&
    workflow.installStatus !== 'installed' &&
    workflow.installStatus !== 'blocked'
  ) {
    addIssue(
      context,
      ['ipsArtifactState'],
      'Recognized Gym Uniform Removal IPS state must be installed or blocked.'
    );
  }

  if (
    workflow.ipsArtifactState === 'invalid' &&
    workflow.installStatus !== 'blocked'
  ) {
    addIssue(
      context,
      ['ipsArtifactState'],
      'Invalid Gym Uniform Removal IPS state must remain blocked.'
    );
  }

  if (
    workflow.ipsArtifactState === 'notInspected' &&
    workflow.installStatus !== 'blocked'
  ) {
    addIssue(
      context,
      ['ipsArtifactState'],
      'Uninspected Gym Uniform Removal IPS state must remain blocked after loading.'
    );
  }

  const hasSelectedIdentity =
    identity !== null &&
    workflow.mainHandlerState !== 'gameMismatch' &&
    hasExactRegion(workflow.reservedRegions, identity.region);
  const hasVerifiedBase = verifiedMainSourceCounts !== null;
  if (
    workflow.ipsArtifactState !== 'notInspected' &&
    (!hasSelectedIdentity || !hasVerifiedBase || !sourceCountMatches)
  ) {
    addIssue(
      context,
      ['ipsArtifactState'],
      'Inspected Gym Uniform Removal IPS state requires verified selected-game base identity and sources.'
    );
  }

  const expectedCanUninstall =
    workflow.summary.availability === 'available' &&
    hasSelectedIdentity &&
    hasVerifiedBase &&
    sourceCountMatches &&
    ['current', 'legacy'].includes(workflow.ipsArtifactState) &&
    (workflow.installStatus === 'installed' || workflow.installStatus === 'blocked');

  if (workflow.canUninstall !== expectedCanUninstall) {
    addIssue(
      context,
      ['canUninstall'],
      'Gym Uniform Removal uninstall capability does not match its verified IPS artifact.'
    );
  }
}

function hasValidAvailabilityStatus(
  workflow: z.infer<typeof gymUniformRemovalWorkflowShapeSchema>
) {
  switch (workflow.summary.availability) {
    case 'disabled':
      return workflow.installStatus === 'disabled';
    case 'readOnly':
      return ['readOnly', 'installed', 'blocked', 'foreign'].includes(workflow.installStatus);
    case 'available':
      return ['available', 'installed', 'blocked', 'foreign'].includes(workflow.installStatus);
  }
}

function hasValidProvenance(
  workflow: z.infer<typeof gymUniformRemovalWorkflowShapeSchema>
) {
  const { fileState, sourceLayer } = workflow.provenance;
  if (sourceLayer === 'generated') {
    return fileState === 'baseOnly' && ['disabled', 'blocked'].includes(workflow.installStatus);
  }

  if (sourceLayer === 'base') {
    return fileState === 'baseOnly';
  }

  return (
    sourceLayer === 'layered' &&
    (fileState === 'layeredOverride' || fileState === 'layeredOnly')
  );
}

function getVerifiedMainSourceCounts(
  workflow: z.infer<typeof gymUniformRemovalWorkflowShapeSchema>
) {
  if (
    workflow.detectedGame === null ||
    workflow.mainHandlerState === 'gameMismatch' ||
    workflow.provenance.sourceLayer === 'generated' ||
    workflow.provenance.fileState === 'layeredOnly'
  ) {
    return null;
  }

  if (
    workflow.provenance.sourceLayer === 'layered' &&
    (workflow.mainHandlerState === 'vanilla' || workflow.mainHandlerState === 'unreadable')
  ) {
    return [1, 2];
  }

  return [workflow.provenance.sourceLayer === 'layered' ? 2 : 1];
}

function hasExactRegion(
  actual: readonly z.infer<typeof gymUniformRemovalReservedRegionSchema>[],
  expected: z.infer<typeof gymUniformRemovalReservedRegionSchema>
) {
  if (actual.length !== 1) {
    return false;
  }

  const region = actual[0];
  return (
    region?.label === expected.label &&
    region.length === expected.length &&
    region.offsetLabel === expected.offsetLabel &&
    region.regionId === expected.regionId &&
    region.rule === expected.rule &&
    region.startOffset === expected.startOffset
  );
}

function validateGymUniformRemovalRequestGame(
  request: { paths: { selectedGame: string | null } },
  context: z.RefinementCtx
) {
  if (
    request.paths.selectedGame === null ||
    !['sword', 'shield'].includes(request.paths.selectedGame)
  ) {
    addIssue(
      context,
      ['paths', 'selectedGame'],
      'Gym Uniform Removal requires Pokemon Sword or Pokemon Shield.'
    );
  }
}

function addIssue(context: z.RefinementCtx, path: Array<string | number>, message: string) {
  context.addIssue({ code: 'custom', message, path });
}
