/* SPDX-License-Identifier: GPL-3.0-only */
import { type ProjectBridge } from '../bridge/projectBridge';

const profanityFilterStatus = {
  buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
  detectedGame: 'sword' as const,
  diagnostics: [],
  message: 'Profanity Filter is not installed.',
  patchOffsetHex: 'main.text+0x00EF1228',
  patchShape: 'vanilla profanity-filter call',
  sourceLayer: 'base',
  status: 'notInstalled'
};

const installedProfanityFilterStatus = {
  ...profanityFilterStatus,
  message: 'Profanity Filter is installed.',
  patchShape: 'KM clean-result instruction',
  sourceLayer: 'layered',
  status: 'installed'
};

export function createProfanityFilterBridgeFixture(): Pick<
  ProjectBridge,
  'loadProfanityFilter' | 'applyProfanityFilter' | 'restoreProfanityFilter'
> {
  return {
    loadProfanityFilter: () => Promise.resolve({ status: profanityFilterStatus }),
    applyProfanityFilter: () =>
      Promise.resolve({
        applyResult: {
          applyId: 'profanity-filter-apply-1',
          diagnostics: [{ message: 'Profanity Filter installed 1 output file(s).', severity: 'info' }],
          writtenFiles: ['exefs/main']
        },
        status: installedProfanityFilterStatus
      }),
    restoreProfanityFilter: () =>
      Promise.resolve({
        applyResult: {
          applyId: 'profanity-filter-restore-1',
          diagnostics: [{ message: 'Profanity Filter uninstalled 1 output file(s).', severity: 'info' }],
          writtenFiles: ['exefs/main']
        },
        status: profanityFilterStatus
      })
  };
}
