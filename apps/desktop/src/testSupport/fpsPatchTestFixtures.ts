/* SPDX-License-Identifier: GPL-3.0-only */
import { type ProjectBridge } from '../bridge/projectBridge';

const fpsPatchStatus = {
  buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
  conflictingRomFsFileCount: 0,
  detectedGame: 'sword' as const,
  diagnostics: [],
  mainSiteCount: 15,
  managedRomFsFileCount: 1010,
  message: '60FPS Patch is not installed.',
  patchedMainSiteCount: 0,
  patchedRomFsFileCount: 0,
  status: 'notInstalled'
};

const installedFpsPatchStatus = {
  ...fpsPatchStatus,
  message: '60FPS Patch is installed.',
  patchedMainSiteCount: 15,
  patchedRomFsFileCount: 1010,
  status: 'installed'
};

export function createFpsPatchBridgeFixture(): Pick<
  ProjectBridge,
  'loadFpsPatch' | 'applyFpsPatch' | 'restoreFpsPatch'
> {
  return {
    loadFpsPatch: () => Promise.resolve({ status: fpsPatchStatus }),
    applyFpsPatch: () =>
      Promise.resolve({
        applyResult: {
          applyId: 'fps-patch-apply-1',
          diagnostics: [{ message: '60FPS Patch installed 1,011 output file(s).', severity: 'info' }],
          writtenFiles: ['exefs/main', 'romfs/bin/battle/waza/sequence/ew052.bseq']
        },
        status: installedFpsPatchStatus
      }),
    restoreFpsPatch: () =>
      Promise.resolve({
        applyResult: {
          applyId: 'fps-patch-restore-1',
          diagnostics: [{ message: '60FPS Patch uninstalled 1,011 owned output file(s).', severity: 'info' }],
          writtenFiles: ['exefs/main', 'romfs/bin/battle/waza/sequence/ew052.bseq']
        },
        status: fpsPatchStatus
      })
  };
}
