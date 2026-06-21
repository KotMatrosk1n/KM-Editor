/* SPDX-License-Identifier: GPL-3.0-only */
import { type ProjectBridge } from '../bridge/projectBridge';

const fpsPatchStatus = {
  buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
  conflictingRomFsFileCount: 0,
  detectedGame: 'sword' as const,
  diagnostics: [],
  mainSiteCount: 15,
  managedRomFsFileCount: 1106,
  message: '60FPS Patch is not installed.',
  patchedMainSiteCount: 0,
  patchedRomFsFileCount: 0,
  status: 'notInstalled'
};

const installedFpsPatchStatus = {
  ...fpsPatchStatus,
  message: '60FPS Patch is installed.',
  patchedMainSiteCount: 15,
  patchedRomFsFileCount: 1106,
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
          diagnostics: [{ message: '60FPS Patch installed 1,107 output file(s).', severity: 'info' }],
          writtenFiles: [
            'exefs/main',
            'romfs/bin/battle/waza/sequence/ew052.bseq',
            'romfs/bin/battle/waza/sequence/ee101.bseq',
            'romfs/bin/demo/sequence/d010.bseq',
            'romfs/bin/demo/sequence/d030.bseq',
            'romfs/bin/archive/demo/share/anime/a_pl0110.gfpak',
            'romfs/bin/archive/field/model/unit_obj_pc_recovery01.gfpak'
          ]
        },
        status: installedFpsPatchStatus
      }),
    restoreFpsPatch: () =>
      Promise.resolve({
        applyResult: {
          applyId: 'fps-patch-restore-1',
          diagnostics: [{ message: '60FPS Patch uninstalled 1,107 owned output file(s).', severity: 'info' }],
          writtenFiles: [
            'exefs/main',
            'romfs/bin/battle/waza/sequence/ew052.bseq',
            'romfs/bin/battle/waza/sequence/ee101.bseq',
            'romfs/bin/demo/sequence/d010.bseq',
            'romfs/bin/demo/sequence/d030.bseq',
            'romfs/bin/archive/demo/share/anime/a_pl0110.gfpak',
            'romfs/bin/archive/field/model/unit_obj_pc_recovery01.gfpak'
          ]
        },
        status: fpsPatchStatus
      })
  };
}
