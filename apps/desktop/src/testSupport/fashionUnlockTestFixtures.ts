/* SPDX-License-Identifier: GPL-3.0-only */

import { type WorkflowSummary } from '../bridge/contracts';
import { type FashionUnlockWorkflow } from '../bridge/fashionUnlockContracts';

type SwShFashionUnlockGame = 'sword' | 'shield';
type SvFashionUnlockGame = 'scarlet' | 'violet';

const swshIdentity = {
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

const svIdentity = {
  scarlet: { buildId: '421C5411B487EB4D049DD065FEC9547773E8E598' },
  violet: { buildId: '709BFD66115298640155FCC4979DBA151C7CC79A' }
} as const;

export function createSwShFashionUnlockWorkflow(
  game: SwShFashionUnlockGame = 'sword',
  installed = false
): Extract<FashionUnlockWorkflow, { editorFamily: 'swsh' }> {
  const identity = swshIdentity[game];
  return {
    buildId: identity.buildId,
    canUninstall: installed,
    detectedGame: game,
    diagnostics: [],
    directGetterOffsetHex: identity.directGetterOffsetHex,
    editorFamily: 'swsh',
    installMessage: installed
      ? 'Fashion Unlock is installed. Fashion ownership checks return unlocked while the ExeFS patch is active.'
      : 'Fashion Unlock is not installed. Installing makes clothing ownership checks return unlocked without editing the save file.',
    installStatus: installed ? 'installed' : 'available',
    mappedGetterOffsetHex: identity.mappedGetterOffsetHex,
    ownershipCheckOffsetHex: '',
    provenance: {
      fileState: installed ? 'layeredOverride' : 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: installed ? 'layered' : 'base'
    },
    reservedRegions: identity.regions.map((region) => ({ ...region })),
    stats: {
      ownedByteCount: 16,
      reservedMainTextRegionCount: 2,
      sourceFileCount: installed ? 2 : 1
    },
    stubKind: installed ? 'return-true ownership stubs' : 'vanilla ownership getters',
    summary: createSummary('Advanced ExeFS editor for Sword and Shield Fashion Unlock.')
  };
}

export function createSvFashionUnlockWorkflow(
  game: SvFashionUnlockGame = 'scarlet',
  installed = false
): Extract<FashionUnlockWorkflow, { editorFamily: 'sv' }> {
  return {
    buildId: svIdentity[game].buildId,
    canUninstall: installed,
    detectedGame: game,
    diagnostics: [],
    directGetterOffsetHex: '',
    editorFamily: 'sv',
    installMessage: installed
      ? 'Fashion Unlock is installed. Scarlet/Violet dress-up ownership checks return unlocked while this ExeFS patch is active.'
      : 'Fashion Unlock is not installed. Installing makes Scarlet/Violet dress-up ownership checks return unlocked without editing the save file.',
    installStatus: installed ? 'installed' : 'available',
    mappedGetterOffsetHex: '',
    ownershipCheckOffsetHex: 'main.text+0x00EAE95C',
    provenance: {
      fileState: installed ? 'layeredOverride' : 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: installed ? 'layered' : 'base'
    },
    reservedRegions: [
      {
        label: 'Scarlet/Violet dress-up ownership check',
        length: 8,
        offsetLabel: 'text+0xEAE95C..0xEAE963',
        regionId: 'fashion-unlock-sv-dressup-ownership-check',
        rule: 'do-not-overwrite',
        startOffset: 0x00eae95c
      }
    ],
    stats: { ownedByteCount: 8, reservedMainTextRegionCount: 1, sourceFileCount: 1 },
    stubKind: installed
      ? 'return-true dress-up ownership stub'
      : 'vanilla dress-up ownership check',
    summary: createSummary('Advanced ExeFS editor for Scarlet and Violet Fashion Unlock.')
  };
}

function createSummary(description: string): WorkflowSummary {
  return {
    availability: 'available',
    description,
    diagnostics: [],
    id: 'fashionUnlock',
    label: 'Fashion Unlock'
  };
}
