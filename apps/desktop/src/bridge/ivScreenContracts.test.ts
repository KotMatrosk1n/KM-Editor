/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import {
  ivScreenInstallStatusSchema,
  ivScreenWorkflowSchema,
  loadIvScreenWorkflowResponseSchema,
  stageIvScreenInstallResponseSchema,
  stageIvScreenUninstallResponseSchema
} from './contracts';

function createWorkflow() {
  return {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    canUninstall: false,
    detectedGame: 'sword' as const,
    diagnostics: [],
    hyperTrainingWrapperOffsetHex: 'main.text+0x007790D0',
    installMessage: 'IV Screen can patch exefs/main.',
    installStatus: 'available' as const,
    marker: 'SWSH_IV_DISPLAY_V1',
    primaryValueSourceOffsetHex: 'main.text+0x0138A2B4',
    provenance: {
      fileState: 'baseOnly' as const,
      sourceFile: 'exefs/main',
      sourceLayer: 'base' as const
    },
    rawIvGetterOffsetHex: 'main.text+0x00779070',
    reservedRegions: [
      {
        label: 'IV Screen multi-chart HP text value source 01',
        length: 4,
        offsetLabel: 'text+0x138A2B4..0x138A2B7',
        regionId: 'iv-screen-multichart-text-hp-value-01',
        rule: 'do-not-overwrite',
        startOffset: 0x0138a2b4
      }
    ],
    stats: {
      reservedMainTextRegionCount: 1,
      sourceFileCount: 1
    },
    summary: {
      availability: 'available' as const,
      description: 'Installs the Pokemon Summary raw-IV screen hook.',
      diagnostics: [],
      id: 'ivScreen',
      label: 'IV Screen'
    },
    xToggleRefreshOffsetHex: 'main.text+0x0138B3AC'
  };
}

function createSession() {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.ivScreen',
        field: 'install',
        newValue: 'true',
        recordId: 'iv-screen-v1-install',
        sources: [
          { layer: 'base' as const, relativePath: 'exefs/main' },
          {
            layer: 'pending' as const,
            relativePath:
              'pending/iv-screen/install/B5BEA41B6C623F7C09F1BF24DCAE58EBAB3C0CDD90AD966BC43A45B44867E12B'
          }
        ],
        summary: 'Stage IV Screen install or refresh.'
      }
    ],
    sessionId: 'iv-screen-session'
  };
}

describe('IV Screen bridge contracts', () => {
  it('accepts every supported install state and exact game discriminants', () => {
    for (const status of [
      'disabled',
      'readOnly',
      'available',
      'installed',
      'blocked',
      'foreign'
    ]) {
      expect(ivScreenInstallStatusSchema.safeParse(status).success).toBe(true);
    }

    expect(ivScreenInstallStatusSchema.safeParse('legacy').success).toBe(false);
    expect(ivScreenWorkflowSchema.shape.detectedGame.safeParse('sword').success).toBe(true);
    expect(ivScreenWorkflowSchema.shape.detectedGame.safeParse('shield').success).toBe(true);
    expect(ivScreenWorkflowSchema.shape.detectedGame.safeParse(null).success).toBe(true);
    expect(ivScreenWorkflowSchema.shape.detectedGame.safeParse('scarlet').success).toBe(false);
  });

  it('requires the authoritative build, game, patch-site, and uninstall fields', () => {
    const workflow = createWorkflow();
    expect(ivScreenWorkflowSchema.parse(workflow)).toEqual(workflow);
    expect(
      ivScreenWorkflowSchema.safeParse({ ...workflow, hookSiteOffsetHex: 'stale' }).success
    ).toBe(false);

    for (const field of [
      'buildId',
      'canUninstall',
      'detectedGame',
      'primaryValueSourceOffsetHex',
      'xToggleRefreshOffsetHex'
    ]) {
      const missing = { ...workflow } as Record<string, unknown>;
      delete missing[field];
      expect(ivScreenWorkflowSchema.safeParse(missing).success, field).toBe(false);
    }
  });

  it('parses load and staging responses through the same strict workflow shape', () => {
    const workflow = createWorkflow();
    const response = {
      diagnostics: [],
      session: createSession(),
      workflow
    };

    expect(loadIvScreenWorkflowResponseSchema.safeParse({ workflow }).success).toBe(true);
    expect(stageIvScreenInstallResponseSchema.safeParse(response).success).toBe(true);
    expect(stageIvScreenUninstallResponseSchema.safeParse(response).success).toBe(true);
  });
});
