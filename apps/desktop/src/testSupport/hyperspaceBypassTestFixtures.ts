/* SPDX-License-Identifier: GPL-3.0-only */

import { type WorkflowSummary } from '../bridge/contracts';
import { type HyperspaceBypassWorkflow } from '../bridge/hyperspaceBypassContracts';
import { type ProjectBridge } from '../bridge/projectBridge';

type HyperspaceBypassBridgeMethods = Pick<
  ProjectBridge,
  | 'loadHyperspaceBypassWorkflow'
  | 'stageHyperspaceBypassInstall'
  | 'stageHyperspaceBypassUninstall'
>;

export type HyperspaceBypassBridgeFixture = HyperspaceBypassBridgeMethods & {
  hyperspaceBypassWorkflow: HyperspaceBypassWorkflow;
  hyperspaceBypassWorkflowSummary: WorkflowSummary;
};

export function createHyperspaceBypassBridgeFixture(
  canEdit: boolean
): HyperspaceBypassBridgeFixture {
  const hyperspaceBypassWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Advanced S/V ExeFS editor that lets any Pokemon pass the Hyperspace Hole/Fury Hoopa runtime gate.',
    diagnostics: [],
    id: 'hyperspaceBypass',
    label: 'Hyperspace Bypass'
  };
  const hyperspaceBypassWorkflow: HyperspaceBypassWorkflow = {
    buildId: '421C5411B487EB4D049DD065FEC9547773E8E598',
    detectedGame: 'scarlet',
    diagnostics: [],
    installMessage: 'Hyperspace Bypass is not installed. Installing lets non-Hoopa and wrong-form users pass the Hyperspace runtime gate.',
    installStatus: canEdit ? 'available' : 'readOnly',
    patchOffsetHex: 'main.text+0x02873A50',
    provenance: { fileState: 'baseOnly', sourceFile: 'exefs/main', sourceLayer: 'base' },
    reservedRegions: [
      {
        label: 'Hyperspace Hole/Fury Hoopa runtime gate',
        length: 4,
        offsetLabel: 'text+0x2873A50..0x2873A53',
        regionId: 'hyperspace-hoopa-runtime-gate',
        rule: 'do-not-overwrite',
        startOffset: 0x02873a50
      }
    ],
    stats: { reservedMainTextRegionCount: 1, sourceFileCount: 1 },
    stubKind: 'vanilla Hoopa species compare',
    summary: hyperspaceBypassWorkflowSummary
  };

  return {
    hyperspaceBypassWorkflow,
    hyperspaceBypassWorkflowSummary,
    loadHyperspaceBypassWorkflow: () => Promise.resolve({ workflow: hyperspaceBypassWorkflow }),
    stageHyperspaceBypassInstall: (request) =>
      Promise.resolve({
        diagnostics: [{ message: 'Hyperspace Bypass install is staged for change-plan review.', severity: 'info' }],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.hyperspaceBypass',
              field: 'install',
              newValue: 'true',
              recordId: 'hyperspace-bypass-v1-install',
              sources: [{ layer: 'base', relativePath: 'exefs/main' }],
              summary: 'Stage Hyperspace Bypass install.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-hyperspace-bypass-install'
        },
        workflow: hyperspaceBypassWorkflow
      }),
    stageHyperspaceBypassUninstall: (request) =>
      Promise.resolve({
        diagnostics: [{ message: 'Hyperspace Bypass uninstall is staged for change-plan review.', severity: 'info' }],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.hyperspaceBypass',
              field: 'uninstall',
              newValue: 'true',
              recordId: 'hyperspace-bypass-v1-uninstall',
              sources: [
                { layer: 'generated', relativePath: 'exefs/main' },
                { layer: 'base', relativePath: 'exefs/main' }
              ],
              summary: 'Stage Hyperspace Bypass uninstall.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-hyperspace-bypass-uninstall'
        },
        workflow: {
          ...hyperspaceBypassWorkflow,
          installMessage:
            'Hyperspace Bypass is installed. Hyperspace Hole and Hyperspace Fury skip the Hoopa species/form gate while this ExeFS patch is active.',
          installStatus: 'installed',
          provenance: { fileState: 'layeredOverride', sourceFile: 'exefs/main', sourceLayer: 'layered' },
          stubKind: 'branch to existing success return'
        }
      })
  };
}
