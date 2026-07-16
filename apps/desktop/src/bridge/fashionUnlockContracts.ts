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

export const fashionUnlockInstallStatusSchema = z.enum([
  'disabled',
  'blocked',
  'available',
  'readOnly',
  'installed'
]);

export const fashionUnlockProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.literal('exefs/main'),
  sourceLayer: projectFileLayerSchema
});

export const fashionUnlockReservedRegionSchema = z.strictObject({
  label: z.string().min(1),
  length: z.number().int().positive().nullable(),
  offsetLabel: z.string().min(1),
  regionId: z.string().min(1),
  rule: z.string().min(1),
  startOffset: z.number().int().nonnegative().nullable()
});

export const fashionUnlockWorkflowStatsSchema = z.strictObject({
  ownedByteCount: z.number().int().nonnegative(),
  reservedMainTextRegionCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

const commonFashionUnlockWorkflowShape = {
  buildId: z.union([z.literal('unknown'), z.string().regex(/^[A-F0-9]{40}$/)]),
  canUninstall: z.boolean(),
  diagnostics: z.array(apiDiagnosticSchema),
  installMessage: z.string().min(1),
  installStatus: fashionUnlockInstallStatusSchema,
  provenance: fashionUnlockProvenanceSchema,
  reservedRegions: z.array(fashionUnlockReservedRegionSchema),
  stats: fashionUnlockWorkflowStatsSchema,
  summary: workflowSummarySchema
};

const swshFashionUnlockWorkflowSchema = z.strictObject({
  ...commonFashionUnlockWorkflowShape,
  detectedGame: z.enum(['sword', 'shield']).nullable(),
  directGetterOffsetHex: z.string(),
  editorFamily: z.literal('swsh'),
  mappedGetterOffsetHex: z.string(),
  ownershipCheckOffsetHex: z.literal(''),
  stubKind: z.enum([
    'not inspected',
    'unsupported',
    'unreadable',
    'game mismatch',
    'unknown bytes',
    'vanilla ownership getters',
    'return-true ownership stubs'
  ])
});

const svFashionUnlockWorkflowSchema = z.strictObject({
  ...commonFashionUnlockWorkflowShape,
  detectedGame: z.enum(['scarlet', 'violet']).nullable(),
  directGetterOffsetHex: z.literal(''),
  editorFamily: z.literal('sv'),
  mappedGetterOffsetHex: z.literal(''),
  ownershipCheckOffsetHex: z.string(),
  stubKind: z.enum([
    'not inspected',
    'unsupported',
    'unreadable',
    'game mismatch',
    'unknown bytes',
    'vanilla dress-up ownership check',
    'return-true dress-up ownership stub'
  ])
});

export const fashionUnlockWorkflowSchema = z
  .discriminatedUnion('editorFamily', [
    swshFashionUnlockWorkflowSchema,
    svFashionUnlockWorkflowSchema
  ])
  .superRefine(validateFashionUnlockWorkflow);

export const loadFashionUnlockWorkflowRequestSchema = z
  .strictObject({ paths: projectPathsSchema })
  .superRefine(validateFashionUnlockRequestGame);

export const loadFashionUnlockWorkflowResponseSchema = z.strictObject({
  workflow: fashionUnlockWorkflowSchema
});

export const stageFashionUnlockInstallRequestSchema = z
  .strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable() })
  .superRefine(validateFashionUnlockRequestGame);

export const stageFashionUnlockInstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: fashionUnlockWorkflowSchema
});

export const stageFashionUnlockUninstallRequestSchema = z
  .strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable() })
  .superRefine(validateFashionUnlockRequestGame);

export const stageFashionUnlockUninstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: fashionUnlockWorkflowSchema
});

export type FashionUnlockAction = 'install' | 'uninstall';
export type FashionUnlockReservedRegion = z.infer<typeof fashionUnlockReservedRegionSchema>;
export type FashionUnlockWorkflow = z.infer<typeof fashionUnlockWorkflowSchema>;
export type LoadFashionUnlockWorkflowRequest = z.infer<
  typeof loadFashionUnlockWorkflowRequestSchema
>;
export type LoadFashionUnlockWorkflowResponse = z.infer<
  typeof loadFashionUnlockWorkflowResponseSchema
>;
export type StageFashionUnlockInstallRequest = z.infer<
  typeof stageFashionUnlockInstallRequestSchema
>;
export type StageFashionUnlockInstallResponse = z.infer<
  typeof stageFashionUnlockInstallResponseSchema
>;
export type StageFashionUnlockUninstallRequest = z.infer<
  typeof stageFashionUnlockUninstallRequestSchema
>;
export type StageFashionUnlockUninstallResponse = z.infer<
  typeof stageFashionUnlockUninstallResponseSchema
>;

const swshIdentities = {
  sword: {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    directGetterOffsetHex: 'main.text+0x0143A2B0',
    mappedGetterOffsetHex: 'main.text+0x0143A300',
    regions: [
      {
        label: 'Fashion Unlock Sword direct ownership getter',
        length: 8,
        offsetLabel: 'text+0x143A2B0..0x143A2B7',
        regionId: 'fashion-unlock-sword-direct-owned-getter',
        rule: 'do-not-overwrite',
        startOffset: 0x0143a2b0
      },
      {
        label: 'Fashion Unlock Sword mapped ownership getter',
        length: 8,
        offsetLabel: 'text+0x143A300..0x143A307',
        regionId: 'fashion-unlock-sword-mapped-owned-getter',
        rule: 'do-not-overwrite',
        startOffset: 0x0143a300
      }
    ]
  },
  shield: {
    buildId: 'A16802625E7826BF83B6F9708E475B912A9AB7DF',
    directGetterOffsetHex: 'main.text+0x0143A2E0',
    mappedGetterOffsetHex: 'main.text+0x0143A330',
    regions: [
      {
        label: 'Fashion Unlock Shield direct ownership getter',
        length: 8,
        offsetLabel: 'text+0x143A2E0..0x143A2E7',
        regionId: 'fashion-unlock-shield-direct-owned-getter',
        rule: 'do-not-overwrite',
        startOffset: 0x0143a2e0
      },
      {
        label: 'Fashion Unlock Shield mapped ownership getter',
        length: 8,
        offsetLabel: 'text+0x143A330..0x143A337',
        regionId: 'fashion-unlock-shield-mapped-owned-getter',
        rule: 'do-not-overwrite',
        startOffset: 0x0143a330
      }
    ]
  }
} as const;

const svIdentities = {
  scarlet: {
    buildId: '421C5411B487EB4D049DD065FEC9547773E8E598',
    ownershipCheckOffsetHex: 'main.text+0x00EAE95C'
  },
  violet: {
    buildId: '709BFD66115298640155FCC4979DBA151C7CC79A',
    ownershipCheckOffsetHex: 'main.text+0x00EAE95C'
  }
} as const;

const svReservedRegion = {
  label: 'Scarlet/Violet dress-up ownership check',
  length: 8,
  offsetLabel: 'text+0xEAE95C..0xEAE963',
  regionId: 'fashion-unlock-sv-dressup-ownership-check',
  rule: 'do-not-overwrite',
  startOffset: 0x00eae95c
} as const;

function validateFashionUnlockWorkflow(
  workflow: z.infer<
    typeof swshFashionUnlockWorkflowSchema | typeof svFashionUnlockWorkflowSchema
  >,
  context: z.RefinementCtx
) {
  if (workflow.summary.id !== 'fashionUnlock' || workflow.summary.label !== 'Fashion Unlock') {
    addIssue(context, ['summary'], 'Fashion Unlock workflow identity is not canonical.');
  }

  if (!hasValidAvailabilityStatus(workflow)) {
    addIssue(
      context,
      ['installStatus'],
      'Fashion Unlock install status does not match workflow availability.'
    );
  }

  if (!hasValidProvenance(workflow)) {
    addIssue(
      context,
      ['provenance'],
      'Fashion Unlock provenance does not describe a possible exefs/main source.'
    );
  }

  if (
    workflow.summary.availability === 'readOnly' &&
    workflow.provenance.sourceLayer === 'layered'
  ) {
    addIssue(
      context,
      ['provenance', 'sourceLayer'],
      'Read-only Fashion Unlock cannot report a LayeredFS source.'
    );
  }

  if (
    workflow.provenance.sourceLayer === 'generated' &&
    workflow.stats.sourceFileCount !== 0
  ) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Generated Fashion Unlock provenance cannot report a verified source.'
    );
  }

  if (
    workflow.installStatus === 'disabled' &&
    (workflow.provenance.sourceLayer !== 'generated' ||
      workflow.provenance.fileState !== 'baseOnly' ||
      workflow.stats.sourceFileCount !== 0)
  ) {
    addIssue(
      context,
      ['provenance'],
      'Disabled Fashion Unlock must report missing generated provenance.'
    );
  }

  if (
    workflow.canUninstall &&
    (workflow.installStatus !== 'installed' ||
      workflow.provenance.sourceLayer !== 'layered')
  ) {
    addIssue(
      context,
      ['canUninstall'],
      'Fashion Unlock uninstall availability does not match the installed output source.'
    );
  }

  if (workflow.installStatus !== 'installed' && workflow.canUninstall) {
    addIssue(context, ['canUninstall'], 'Only installed Fashion Unlock can be uninstalled.');
  }

  if (workflow.installStatus === 'disabled' && workflow.stubKind !== 'not inspected') {
    addIssue(context, ['stubKind'], 'Disabled Fashion Unlock must report an uninspected stub.');
  }

  if (
    workflow.installStatus === 'disabled' &&
    (workflow.detectedGame !== null || workflow.buildId !== 'unknown')
  ) {
    addIssue(
      context,
      ['detectedGame'],
      'Disabled Fashion Unlock must not claim a detected executable build.'
    );
  }

  if (
    ['available', 'readOnly', 'installed'].includes(workflow.installStatus) &&
    workflow.detectedGame === null
  ) {
    addIssue(
      context,
      ['detectedGame'],
      'A loaded Fashion Unlock state requires a detected supported game.'
    );
  }

  if (workflow.editorFamily === 'swsh') {
    validateSwShFashionUnlockWorkflow(workflow, context);
  } else {
    validateSvFashionUnlockWorkflow(workflow, context);
  }
}

function validateSwShFashionUnlockWorkflow(
  workflow: z.infer<typeof swshFashionUnlockWorkflowSchema>,
  context: z.RefinementCtx
) {
  const identity = workflow.detectedGame ? swshIdentities[workflow.detectedGame] : null;
  if (identity) {
    if (
      workflow.buildId !== identity.buildId ||
      workflow.directGetterOffsetHex !== identity.directGetterOffsetHex ||
      workflow.mappedGetterOffsetHex !== identity.mappedGetterOffsetHex
    ) {
      addIssue(
        context,
        ['detectedGame'],
        'Sword/Shield Fashion Unlock build and getter offsets do not match the detected game.'
      );
    }
  } else if (
    workflow.directGetterOffsetHex !== 'unknown' ||
    workflow.mappedGetterOffsetHex !== 'unknown'
  ) {
    addIssue(
      context,
      ['directGetterOffsetHex'],
      'Undetected Sword/Shield Fashion Unlock getter offsets must be unknown.'
    );
  }

  const regionsMatchDetectedGame =
    identity !== null && hasExactRegions(workflow.reservedRegions, identity.regions);
  const regionsMatchEitherGame = Object.values(swshIdentities).some((candidate) =>
    hasExactRegions(workflow.reservedRegions, candidate.regions)
  );
  const regionsMatchActiveGame = workflow.detectedGame === null
    ? workflow.stubKind !== 'game mismatch' && regionsMatchEitherGame
    : workflow.stubKind === 'game mismatch'
      ? hasExactRegions(
          workflow.reservedRegions,
          swshIdentities[workflow.detectedGame === 'sword' ? 'shield' : 'sword'].regions
        )
      : regionsMatchDetectedGame;
  const hasMappedOwnedRegions =
    workflow.reservedRegions.length === 2 &&
    workflow.stats.reservedMainTextRegionCount === 2 &&
    workflow.stats.ownedByteCount === 16 &&
    regionsMatchActiveGame;

  if (!hasMappedOwnedRegions) {
    addIssue(
      context,
      ['reservedRegions'],
      'Sword/Shield Fashion Unlock owned ranges and byte counts are not canonical.'
    );
  }

  if (workflow.stats.sourceFileCount > 2) {
    addIssue(context, ['stats', 'sourceFileCount'], 'Sword/Shield Fashion Unlock has at most two verified sources.');
  }

  if (
    workflow.installStatus === 'blocked' &&
    (workflow.stats.sourceFileCount > 1 ||
      (workflow.stats.sourceFileCount === 1 &&
        (workflow.provenance.sourceLayer !== 'layered' ||
          workflow.provenance.fileState !== 'layeredOverride')))
  ) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Blocked Sword/Shield Fashion Unlock source verification is not canonical.'
    );
  }

  const expectedVerifiedSourceCount = workflow.provenance.sourceLayer === 'layered' ? 2 : 1;
  if (
    workflow.installStatus !== 'blocked' &&
    workflow.installStatus !== 'disabled' &&
    workflow.stats.sourceFileCount !== expectedVerifiedSourceCount
  ) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Sword/Shield Fashion Unlock source count does not match its effective source layer.'
    );
  }

  if (workflow.installStatus === 'disabled' && workflow.stats.sourceFileCount !== 0) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Disabled Sword/Shield Fashion Unlock must not report verified sources.'
    );
  }

  if (
    workflow.installStatus !== 'blocked' &&
    workflow.installStatus !== 'disabled' &&
    workflow.provenance.sourceLayer === 'layered' &&
    workflow.provenance.fileState !== 'layeredOverride'
  ) {
    addIssue(
      context,
      ['provenance', 'fileState'],
      'Loaded layered Sword/Shield Fashion Unlock requires a verified base override.'
    );
  }

  if (
    workflow.installStatus === 'installed' &&
    (workflow.summary.availability !== 'available' ||
      workflow.provenance.sourceLayer !== 'layered' ||
      workflow.provenance.fileState !== 'layeredOverride' ||
      workflow.stats.sourceFileCount !== 2 ||
      !workflow.canUninstall)
  ) {
    addIssue(
      context,
      ['canUninstall'],
      'Installed Sword/Shield Fashion Unlock must be an editable removable layered override.'
    );
  }

  if (
    (workflow.installStatus === 'available' || workflow.installStatus === 'readOnly') &&
    workflow.stubKind !== 'vanilla ownership getters'
  ) {
    addIssue(context, ['stubKind'], 'Sword/Shield Fashion Unlock available state must report vanilla getters.');
  }

  if (workflow.installStatus === 'installed' && workflow.stubKind !== 'return-true ownership stubs') {
    addIssue(context, ['stubKind'], 'Installed Sword/Shield Fashion Unlock must report KM-owned stubs.');
  }
}

function validateSvFashionUnlockWorkflow(
  workflow: z.infer<typeof svFashionUnlockWorkflowSchema>,
  context: z.RefinementCtx
) {
  const identity = workflow.detectedGame ? svIdentities[workflow.detectedGame] : null;
  if (identity) {
    if (
      workflow.buildId !== identity.buildId ||
      workflow.ownershipCheckOffsetHex !== identity.ownershipCheckOffsetHex
    ) {
      addIssue(
        context,
        ['detectedGame'],
        'Scarlet/Violet Fashion Unlock build and ownership offset do not match the detected game.'
      );
    }
  } else if (workflow.ownershipCheckOffsetHex !== 'unknown') {
    addIssue(
      context,
      ['ownershipCheckOffsetHex'],
      'Undetected Scarlet/Violet Fashion Unlock ownership offset must be unknown.'
    );
  }

  if (
    !hasExactRegions(workflow.reservedRegions, [svReservedRegion]) ||
    workflow.stats.reservedMainTextRegionCount !== 1 ||
    workflow.stats.ownedByteCount !== 8 ||
    workflow.stats.sourceFileCount > 1
  ) {
    addIssue(
      context,
      ['stats'],
      'Scarlet/Violet Fashion Unlock owned range and statistics are not canonical.'
    );
  }


  if (
    workflow.installStatus !== 'blocked' &&
    workflow.installStatus !== 'disabled' &&
    workflow.stats.sourceFileCount !== 1
  ) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Scarlet/Violet Fashion Unlock loaded state must report one effective source.'
    );
  }

  if (workflow.installStatus === 'disabled' && workflow.stats.sourceFileCount !== 0) {
    addIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Disabled Scarlet/Violet Fashion Unlock must not report an effective source.'
    );
  }

  const expectedCanUninstall =
    workflow.summary.availability === 'available' &&
    workflow.installStatus === 'installed' &&
    workflow.provenance.sourceLayer === 'layered' &&
    workflow.provenance.fileState === 'layeredOverride';
  if (workflow.canUninstall !== expectedCanUninstall) {
    addIssue(
      context,
      ['canUninstall'],
      'Scarlet/Violet Fashion Unlock uninstall capability does not match its effective source.'
    );
  }

  if (
    (workflow.installStatus === 'available' || workflow.installStatus === 'readOnly') &&
    workflow.stubKind !== 'vanilla dress-up ownership check'
  ) {
    addIssue(context, ['stubKind'], 'Scarlet/Violet Fashion Unlock available state must report the vanilla check.');
  }

  if (
    workflow.installStatus === 'installed' &&
    workflow.stubKind !== 'return-true dress-up ownership stub'
  ) {
    addIssue(context, ['stubKind'], 'Installed Scarlet/Violet Fashion Unlock must report the KM-owned stub.');
  }
}

function hasValidAvailabilityStatus(
  workflow: z.infer<
    typeof swshFashionUnlockWorkflowSchema | typeof svFashionUnlockWorkflowSchema
  >
) {
  switch (workflow.summary.availability) {
    case 'disabled':
      return workflow.installStatus === 'disabled';
    case 'readOnly':
      return ['readOnly', 'installed', 'blocked'].includes(workflow.installStatus);
    case 'available':
      return ['available', 'installed', 'blocked'].includes(workflow.installStatus);
  }
}

function hasValidProvenance(
  workflow: z.infer<
    typeof swshFashionUnlockWorkflowSchema | typeof svFashionUnlockWorkflowSchema
  >
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

function hasExactRegions(
  actual: readonly z.infer<typeof fashionUnlockReservedRegionSchema>[],
  expected: readonly (z.infer<typeof fashionUnlockReservedRegionSchema>)[]
) {
  return (
    actual.length === expected.length &&
    actual.every((region, index) => {
      const expectedRegion = expected[index];
      return (
        expectedRegion !== undefined &&
        region.label === expectedRegion.label &&
        region.length === expectedRegion.length &&
        region.offsetLabel === expectedRegion.offsetLabel &&
        region.regionId === expectedRegion.regionId &&
        region.rule === expectedRegion.rule &&
        region.startOffset === expectedRegion.startOffset
      );
    })
  );
}

function validateFashionUnlockRequestGame(
  request: { paths: { selectedGame: string | null } },
  context: z.RefinementCtx
) {
  if (
    request.paths.selectedGame === null ||
    !['sword', 'shield', 'scarlet', 'violet'].includes(request.paths.selectedGame)
  ) {
    addIssue(
      context,
      ['paths', 'selectedGame'],
      'Fashion Unlock requires Sword, Shield, Scarlet, or Violet.'
    );
  }
}

function addIssue(context: z.RefinementCtx, path: Array<string | number>, message: string) {
  context.addIssue({ code: 'custom', message, path });
}
