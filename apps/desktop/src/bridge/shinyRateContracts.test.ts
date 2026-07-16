/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import {
  shinyRateWorkflowSchema,
  stageShinyRateRequestSchema,
  stageShinyRateResponseSchema
} from './shinyRateContracts';
import {
  createShinyRateWorkflowFixture,
  createStageShinyRateFixtureResponse
} from '../testSupport/shinyRateTestFixtures';

const paths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

describe('Shiny Rate bridge contracts', () => {
  it('accepts the complete mapped workflow and nullable dynamic default rate', () => {
    const { shinyRateWorkflow } = createShinyRateWorkflowFixture(true);
    const parsed = shinyRateWorkflowSchema.parse(shinyRateWorkflow);

    expect(parsed.detectedGame).toBe('sword');
    expect(parsed.buildId).toBe('A3B75BCD3311385AEED67FBEEB79CBB7BF02F471');
    expect(parsed.functionOffsetHex).toBe('main.text+0x00D311C0');
    expect(parsed.compareOffsetHex).toBe('main.text+0x00D31488');
    expect(parsed.breakOffsetHex).toBe('main.text+0x00D3148C');
    expect(parsed.rateRule).toMatchObject({
      chancePercent: null,
      mode: 'default',
      oddsDenominator: null,
      oddsLabel: 'Dynamic',
      percentLabel: 'Variable',
      rollCount: null
    });
    expect(parsed.source).toMatchObject({
      relativePath: 'exefs/main',
      sourceId: 'exefs-main',
      status: 'available'
    });
    expect(parsed.stats).toMatchObject({
      outputFileCount: 0,
      presetCount: 6,
      sourceFileCount: 1
    });
  });

  it('rejects unknown discriminants, games, technical identities, and extra fields', () => {
    const { shinyRateWorkflow } = createShinyRateWorkflowFixture(true);
    const invalidWorkflows = [
      { ...shinyRateWorkflow, installStatus: 'installed' },
      { ...shinyRateWorkflow, detectedGame: 'scarlet' },
      { ...shinyRateWorkflow, buildId: shinyRateWorkflow.buildId.slice(0, 12) },
      { ...shinyRateWorkflow, compareOffsetHex: '0xD31488' },
      {
        ...shinyRateWorkflow,
        rateRule: { ...shinyRateWorkflow.rateRule, mode: 'future' }
      },
      {
        ...shinyRateWorkflow,
        presets: shinyRateWorkflow.presets.map((preset, index) =>
          index === 0 ? { ...preset, mode: 'future' } : preset
        )
      },
      {
        ...shinyRateWorkflow,
        source: shinyRateWorkflow.source
          ? { ...shinyRateWorkflow.source, status: 'optionalMissing' }
          : null
      },
      { ...shinyRateWorkflow, unexpected: true }
    ];

    for (const workflow of invalidWorkflows) {
      expect(shinyRateWorkflowSchema.safeParse(workflow).success).toBe(false);
    }
  });

  it('rejects impossible rule modes, constants, and derived fixed-rate values', () => {
    const { shinyRateWorkflow } = createShinyRateWorkflowFixture(true);
    const fixedChancePercent = (1 - Math.pow(4095 / 4096, 8)) * 100;
    const fixedRule = {
      ...shinyRateWorkflow.rateRule,
      chancePercent: fixedChancePercent,
      mode: 'fixed',
      oddsDenominator: 512,
      oddsLabel: '1/512',
      percentLabel: `${fixedChancePercent.toFixed(3)}%`,
      rollCount: 8,
      runtimeSummary: 'Fixed writes a global PID roll count for random shiny checks.'
    };

    expect(
      shinyRateWorkflowSchema.safeParse({
        ...shinyRateWorkflow,
        rateRule: fixedRule
      }).success
    ).toBe(true);

    const invalidRules = [
      { ...shinyRateWorkflow.rateRule, minimumRollCount: 2 },
      { ...shinyRateWorkflow.rateRule, rollCount: 1 },
      { ...shinyRateWorkflow.rateRule, chancePercent: 0 },
      { ...fixedRule, chancePercent: fixedChancePercent * 2 },
      { ...fixedRule, oddsDenominator: 511 },
      { ...fixedRule, oddsLabel: '1/511' },
      { ...fixedRule, percentLabel: '0.194%' },
      {
        ...fixedRule,
        chancePercent: 100,
        mode: 'always',
        oddsDenominator: 2,
        oddsLabel: '1/2',
        percentLabel: '100.000%',
        rollCount: null,
        runtimeSummary: 'Always Shiny NOPs the loop break branch.'
      },
      {
        ...shinyRateWorkflow.rateRule,
        mode: 'blocked',
        oddsLabel: 'Unknown',
        percentLabel: 'Unknown',
        rollCount: 1,
        runtimeSummary: 'Runtime shiny rate is unavailable until exefs/main can be inspected.'
      }
    ];

    for (const rateRule of invalidRules) {
      expect(
        shinyRateWorkflowSchema.safeParse({ ...shinyRateWorkflow, rateRule }).success
      ).toBe(false);
    }
  });

  it('rejects malformed, reordered, duplicated, and cross-mode presets', () => {
    const { shinyRateWorkflow } = createShinyRateWorkflowFixture(true);
    const replacePreset = (index: number, changes: Record<string, unknown>) =>
      shinyRateWorkflow.presets.map((preset, presetIndex) =>
        presetIndex === index ? { ...preset, ...changes } : preset
      );
    const invalidPresetCollections = [
      replacePreset(0, { isEnabled: true }),
      replacePreset(1, { targetDenominator: 4096 }),
      replacePreset(1, { description: "Restores the game's original shiny reroll logic." }),
      replacePreset(2, { rollCount: 4 }),
      replacePreset(4, { oddsLabel: '1/513' }),
      replacePreset(5, { rollCount: 1 }),
      [shinyRateWorkflow.presets[1], shinyRateWorkflow.presets[0], ...shinyRateWorkflow.presets.slice(2)],
      shinyRateWorkflow.presets.map((preset, index) =>
        index === 1 ? shinyRateWorkflow.presets[0] : preset
      ),
      shinyRateWorkflow.presets.slice(0, 5)
    ];

    for (const presets of invalidPresetCollections) {
      expect(
        shinyRateWorkflowSchema.safeParse({ ...shinyRateWorkflow, presets }).success
      ).toBe(false);
    }
  });

  it('enforces mode-specific request roll counts and backend numeric limits', () => {
    expect(
      stageShinyRateRequestSchema.safeParse({
        mode: 'fixed',
        paths,
        rollCount: 8,
        session: null
      }).success
    ).toBe(true);
    expect(
      stageShinyRateRequestSchema.safeParse({
        mode: 'fixed',
        paths,
        rollCount: null,
        session: null
      }).success
    ).toBe(false);
    expect(
      stageShinyRateRequestSchema.safeParse({
        mode: 'default',
        paths,
        rollCount: 1,
        session: null
      }).success
    ).toBe(false);
    expect(
      stageShinyRateRequestSchema.safeParse({
        mode: 'fixed',
        paths,
        rollCount: 4092,
        session: null
      }).success
    ).toBe(false);
  });

  it('accepts the canonical pending-only stage response and rejects forged sources', async () => {
    const { shinyRateWorkflow } = createShinyRateWorkflowFixture(true);
    const request = stageShinyRateRequestSchema.parse({
      mode: 'fixed',
      paths,
      rollCount: 8,
      session: null
    });
    const response = await createStageShinyRateFixtureResponse(request, shinyRateWorkflow);

    expect(stageShinyRateResponseSchema.safeParse(response).success).toBe(true);
    expect(response.workflow).toBe(shinyRateWorkflow);
    expect(response.session.pendingEdits[0]?.sources).toEqual([
      { layer: 'base', relativePath: 'exefs/main' },
      {
        layer: 'pending',
        relativePath:
          'pending/shiny-rate/rate/5831BD107E90A1F6A38691C03ED0602364ADA3C536A4E44A99C451A36FA8D874'
      }
    ]);

    const forged = {
      ...response,
      session: {
        ...response.session,
        pendingEdits: response.session.pendingEdits.map((edit) => ({
          ...edit,
          sources: [{ layer: 'future', relativePath: 'exefs/main' }]
        }))
      }
    };
    expect(stageShinyRateResponseSchema.safeParse(forged).success).toBe(false);
  });
});
