/* SPDX-License-Identifier: GPL-3.0-only */

import { type GymUniformRemovalWorkflow } from '../bridge/gymUniformRemovalContracts';

const identities = {
  sword: {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
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

export function createGymUniformRemovalWorkflow(
  game: 'sword' | 'shield' = 'sword',
  installed = false
): GymUniformRemovalWorkflow {
  const identity = identities[game];
  return {
    buildId: identity.buildId,
    canUninstall: installed,
    detectedGame: game,
    diagnostics: [],
    installMessage: installed
      ? 'Gym Uniform Removal IPS is installed.'
      : 'Gym Uniform Removal IPS is not installed.',
    installStatus: installed ? 'installed' : 'available',
    ipsArtifactState: installed ? 'current' : 'notPresent',
    mainHandlerState: 'vanilla',
    patchOffsetHex: identity.patchOffsetHex,
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: 'base'
    },
    reservedRegions: [identity.region],
    stats: {
      ownedByteCount: 8,
      reservedMainTextRegionCount: 1,
      sourceFileCount: installed ? 2 : 1
    },
    summary: {
      availability: 'available',
      description:
        'Independent ExeFS editor that keeps gym challenge and gym leader battle scripts from changing the player into the gym uniform.',
      diagnostics: [],
      id: 'gymUniformRemoval',
      label: 'Gym Uniform Removal'
    }
  };
}
