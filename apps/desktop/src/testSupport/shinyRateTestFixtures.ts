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
      preset(
        'gen3',
        'Gen 3',
        'unsupported',
        null,
        8192,
        false,
        '1/8,192',
        '0.012%',
        'Not available yet. The current patch can make odds more common, but 1/8192 needs a separate shiny-threshold patch.'
      ),
      preset(
        'default',
        'Default',
        'default',
        null,
        4096,
        true,
        '1/4,096',
        '0.024%',
        "Restores the game's original shiny reroll logic."
      ),
      preset('shinyCharm', 'Shiny Charm', 'fixed', 3, 1366, true, '1/1,366', '0.073%', 'Writes 3 PID rolls.'),
      preset('masuda', 'Masuda', 'fixed', 6, 683, true, '1/683', '0.146%', 'Writes 6 PID rolls.'),
      preset('masudaCharm', 'Masuda + Shiny Charm', 'fixed', 8, 512, true, '1/512', '0.195%', 'Writes 8 PID rolls.')
    ],
    rateRule: {
      chancePercent: 0.024,
      maximumCustomDenominator: 4096,
      maximumRollCount: 4091,
      minimumCustomDenominator: 2,
      minimumRollCount: 1,
      mode: 'default',
      oddsDenominator: 4096,
      oddsLabel: '1/4,096',
      percentLabel: '0.024%',
      rollCount: 1,
      runtimeSummary: "Default restores the game's original reroll count calculation."
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
      outputFileCount: 1,
      presetCount: 5,
      sourceFileCount: 1
    },
    summary: shinyRateWorkflowSummary
  };

  return { shinyRateWorkflow, shinyRateWorkflowSummary };
}

export function createStageShinyRateFixtureResponse(
  request: StageShinyRateRequest,
  shinyRateWorkflow: ShinyRateWorkflow
): StageShinyRateResponse {
  const nextRule =
    request.mode === 'fixed' && request.rollCount !== null
      ? {
          ...shinyRateWorkflow.rateRule,
          mode: 'fixed',
          oddsDenominator: calculateFixedOddsDenominator(request.rollCount),
          oddsLabel: formatFixedOddsLabel(request.rollCount),
          percentLabel: formatFixedPercentLabel(request.rollCount),
          rollCount: request.rollCount,
          runtimeSummary: 'Fixed writes a global PID roll count for random shiny checks.'
        }
      : request.mode === 'always'
        ? {
            ...shinyRateWorkflow.rateRule,
            chancePercent: 100,
            mode: 'always',
            oddsDenominator: 1,
            oddsLabel: '1/1',
            percentLabel: '100.000%',
            rollCount: null,
            runtimeSummary: 'Always Shiny NOPs the loop break branch.'
          }
        : shinyRateWorkflow.rateRule;
  const pendingValue =
    request.mode === 'fixed' && request.rollCount !== null
      ? `fixed:${request.rollCount}`
      : request.mode;

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
          sources: [{ layer: 'base', relativePath: 'exefs/main' }],
          summary: 'Stage Shiny Rate reroll settings.'
        }
      ],
      sessionId: request.session?.sessionId ?? 'session-shiny-rate'
    },
    workflow: {
      ...shinyRateWorkflow,
      installMessage: formatInstallMessage(request),
      installStatus:
        request.mode === 'fixed'
          ? 'fixed'
          : request.mode === 'always'
            ? 'always'
            : shinyRateWorkflow.installStatus,
      rateRule: nextRule
    }
  };
}

function preset(
  presetId: string,
  label: string,
  mode: string,
  rollCount: number | null,
  targetDenominator: number | null,
  isEnabled: boolean,
  oddsLabel: string,
  percentLabel: string,
  description: string
): ShinyRateWorkflow['presets'][number] {
  return {
    description,
    isEnabled,
    label,
    mode,
    oddsLabel,
    percentLabel,
    presetId,
    rollCount,
    targetDenominator
  };
}

function calculateFixedOddsDenominator(rollCount: number) {
  const chance = 1 - Math.pow((4096 - 1) / 4096, rollCount);
  return Math.max(1, Math.round(1 / chance));
}

function formatFixedOddsLabel(rollCount: number) {
  return {
    3: '1/1,366',
    6: '1/683',
    8: '1/512'
  }[rollCount] ?? `${rollCount} rolls`;
}

function formatFixedPercentLabel(rollCount: number) {
  return {
    3: '0.073%',
    6: '0.146%',
    8: '0.195%'
  }[rollCount] ?? '0.024%';
}

function formatInstallMessage(request: StageShinyRateRequest) {
  if (request.mode === 'fixed' && request.rollCount !== null) {
    return `Shiny Rate is fixed at ${request.rollCount} PID rolls.`;
  }

  if (request.mode === 'always') {
    return 'Shiny Rate is patched to always resolve random shiny checks as shiny.';
  }

  return "Shiny Rate is using the game's original shiny reroll logic.";
}
