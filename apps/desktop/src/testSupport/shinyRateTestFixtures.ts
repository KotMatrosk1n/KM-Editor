/* SPDX-License-Identifier: GPL-3.0-only */

import { type WorkflowSummary } from '../bridge/contracts';
import {
  type ShinyRateWorkflow,
  type StageShinyRateRequest,
  type StageShinyRateResponse
} from '../bridge/shinyRateContracts';

export function createShinyRateWorkflowFixture(canEdit: boolean): {
  shinyRateWorkflow: ShinyRateWorkflow;
  shinyRateWorkflowSummary: WorkflowSummary;
} {
  const shinyRateWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Advanced editor for the Sword/Shield shiny reroll count in exefs/main.',
    diagnostics: [],
    id: 'shinyRate',
    label: 'Shiny Rate'
  };
  const shinyRateWorkflow: ShinyRateWorkflow = {
    breakOffsetHex: 'main.text+0x00D3148C',
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    compareOffsetHex: 'main.text+0x00D31488',
    detectedGame: 'sword',
    diagnostics: [],
    functionOffsetHex: 'main.text+0x00D311C0',
    installMessage: "Shiny Rate is using the game's original shiny reroll logic.",
    installStatus: canEdit ? 'available' : 'readOnly',
    presets: [
      {
        description:
          'Not available yet. The current patch can make odds more common, but 1/8192 needs a separate shiny-threshold patch.',
        isEnabled: false,
        label: 'Gen 3',
        mode: 'unsupported',
        oddsLabel: '1/8,192',
        percentLabel: '0.012%',
        presetId: 'gen3',
        rollCount: null,
        targetDenominator: 8192
      },
      {
        description: "Restores the game's runtime-dependent shiny reroll logic.",
        isEnabled: true,
        label: 'Default',
        mode: 'default',
        oddsLabel: 'Dynamic',
        percentLabel: 'Variable',
        presetId: 'default',
        rollCount: null,
        targetDenominator: null
      },
      {
        description: 'Writes 3 PID rolls.',
        isEnabled: true,
        label: 'Shiny Charm',
        mode: 'fixed',
        oddsLabel: '1/1,366',
        percentLabel: '0.073%',
        presetId: 'shinyCharm',
        rollCount: 3,
        targetDenominator: 1366
      },
      {
        description: 'Writes 6 PID rolls.',
        isEnabled: true,
        label: 'Masuda',
        mode: 'fixed',
        oddsLabel: '1/683',
        percentLabel: '0.146%',
        presetId: 'masuda',
        rollCount: 6,
        targetDenominator: 683
      },
      {
        description: 'Writes 8 PID rolls.',
        isEnabled: true,
        label: 'Masuda + Shiny Charm',
        mode: 'fixed',
        oddsLabel: '1/512',
        percentLabel: '0.195%',
        presetId: 'masudaCharm',
        rollCount: 8,
        targetDenominator: 512
      },
      {
        description: 'Forces random shiny checks to resolve as shiny.',
        isEnabled: true,
        label: 'Always Shiny',
        mode: 'always',
        oddsLabel: '1/1',
        percentLabel: '100.000%',
        presetId: 'always',
        rollCount: null,
        targetDenominator: 1
      }
    ],
    rateRule: {
      chancePercent: null,
      maximumCustomDenominator: 4096,
      maximumRollCount: 4091,
      minimumCustomDenominator: 2,
      minimumRollCount: 1,
      mode: 'default',
      oddsDenominator: null,
      oddsLabel: 'Dynamic',
      percentLabel: 'Variable',
      rollCount: null,
      runtimeSummary: "Default restores the game's original runtime-dependent reroll count calculation."
    },
    source: {
      label: 'ExeFS main',
      provenance: {
        fileState: 'baseOnly',
        sourceFile: 'exefs/main',
        sourceLayer: 'base'
      },
      relativePath: 'exefs/main',
      sourceId: 'exefs-main',
      status: 'available'
    },
    stats: {
      outputFileCount: 0,
      presetCount: 6,
      sourceFileCount: 1
    },
    summary: shinyRateWorkflowSummary
  };

  return { shinyRateWorkflow, shinyRateWorkflowSummary };
}

export async function createStageShinyRateFixtureResponse(
  request: StageShinyRateRequest,
  shinyRateWorkflow: ShinyRateWorkflow
): Promise<StageShinyRateResponse> {
  const pendingValue =
    request.mode === 'fixed' && request.rollCount !== null
      ? `fixed:${request.rollCount}`
      : request.mode;
  const sourceReferences = [
    { layer: 'base' as const, relativePath: 'exefs/main' },
    ...(shinyRateWorkflow.source?.provenance.sourceLayer === 'layered'
      ? [{ layer: 'layered' as const, relativePath: 'exefs/main' }]
      : []),
    {
      layer: 'pending' as const,
      relativePath: `pending/shiny-rate/rate/${await calculateSha256(pendingValue)}`
    }
  ];

  return {
    diagnostics: [
      {
        message: 'Shiny Rate is staged for change-plan review.',
        severity: 'info'
      }
    ],
    session: {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.shinyRate',
          field: 'rate',
          newValue: pendingValue,
          recordId: 'shiny-rate',
          sources: sourceReferences,
          summary: formatPendingSummary(request)
        }
      ],
      sessionId: request.session?.sessionId ?? 'session-shiny-rate'
    },
    workflow: shinyRateWorkflow
  };
}

async function calculateSha256(value: string) {
  const digest = await globalThis.crypto.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(value)
  );
  return Array.from(new Uint8Array(digest), (byte) =>
    byte.toString(16).padStart(2, '0')
  )
    .join('')
    .toUpperCase();
}

function formatPendingSummary(request: StageShinyRateRequest) {
  if (request.mode === 'fixed' && request.rollCount !== null) {
    return `Stage Shiny Rate fixed ${request.rollCount} roll${request.rollCount === 1 ? '' : 's'}.`;
  }

  if (request.mode === 'always') {
    return 'Stage Shiny Rate always-shiny patch.';
  }

  return 'Stage Shiny Rate default reroll logic.';
}
