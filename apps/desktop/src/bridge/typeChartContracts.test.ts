/* SPDX-License-Identifier: GPL-3.0-only */

import { type ProjectGame, type TypeChartWorkflow } from './contracts';
import { typeChartWorkflowSchema } from './contracts';
import { createMockProjectBridge } from '../testSupport/appTestFixtures';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

const identities = [
  ['sword', 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471', 'main.ro+0x00743600'],
  ['shield', 'A16802625E7826BF83B6F9708E475B912A9AB7DF', 'main.ro+0x00743600'],
  ['scarlet', '421C5411B487EB4D049DD065FEC9547773E8E598', 'main.ro+0x0082286C'],
  ['violet', '709BFD66115298640155FCC4979DBA151C7CC79A', 'main.ro+0x0082286C'],
  ['za', 'B1F12FD919EAE86AB8A978317677E64BCE443D1F', 'main.ro+0x0019F2A4']
] satisfies Array<[ProjectGame, string, string]>;

describe('Type Chart bridge contracts', () => {
  it.each(identities)(
    'accepts the canonical %s build, offset, types, and complete 18x18 matrix',
    async (detectedGame, buildId, chartOffsetHex) => {
      const workflow = await createWorkflow();
      expect(
        typeChartWorkflowSchema.parse({
          ...workflow,
          buildId,
          chartOffsetHex,
          detectedGame
        })
      ).toMatchObject({ buildId, chartOffsetHex, detectedGame });
    }
  );

  it('rejects a build or chart offset mapped to the wrong detected game', async () => {
    const workflow = await createWorkflow();
    expect(() =>
      typeChartWorkflowSchema.parse({
        ...workflow,
        detectedGame: 'shield'
      })
    ).toThrow(/build ID does not match/i);
    expect(() =>
      typeChartWorkflowSchema.parse({
        ...workflow,
        chartOffsetHex: 'main.ro+0x0082286C'
      })
    ).toThrow(/offset does not match/i);
  });

  it.each([
    ['shield', 'A16802625E7826BF83B6F9708E475B912A9AB7DF'],
    ['scarlet', '421C5411B487EB4D049DD065FEC9547773E8E598'],
    ['za', 'B1F12FD919EAE86AB8A978317677E64BCE443D1F']
  ] satisfies Array<[ProjectGame, string]>)(
    'accepts a blocked %s mismatch or missing-chart analysis with an unknown offset',
    async (detectedGame, buildId) => {
      const workflow = await createWorkflow();
      expect(
        typeChartWorkflowSchema.parse({
          ...workflow,
          buildId,
          chartOffsetHex: 'unknown',
          detectedGame,
          installMessage: 'Type Chart could not verify the executable chart.',
          installStatus: 'blocked'
        })
      ).toMatchObject({
        buildId,
        chartOffsetHex: 'unknown',
        detectedGame,
        installStatus: 'blocked'
      });
    }
  );

  it('rejects an unknown chart offset for a usable workflow', async () => {
    const workflow = await createWorkflow();
    expect(() =>
      typeChartWorkflowSchema.parse({
        ...workflow,
        chartOffsetHex: 'unknown'
      })
    ).toThrow(/offset does not match/i);
  });

  it('rejects missing or duplicate matrix coordinates', async () => {
    const workflow = await createWorkflow();
    expect(() =>
      typeChartWorkflowSchema.parse({
        ...workflow,
        cells: workflow.cells.slice(0, -1)
      })
    ).toThrow();
    expect(() =>
      typeChartWorkflowSchema.parse({
        ...workflow,
        cells: workflow.cells.map((cell, index) =>
          index === workflow.cells.length - 1
            ? { ...workflow.cells[0]! }
            : cell
        )
      })
    ).toThrow(/every attack and defense coordinate/i);
  });

  it('rejects swapped type definitions and noncanonical source metadata', async () => {
    const workflow = await createWorkflow();
    expect(() =>
      typeChartWorkflowSchema.parse({
        ...workflow,
        types: workflow.types.map((type, index) =>
          index === 0 ? { ...type, label: 'Fire' } : type
        )
      })
    ).toThrow(/canonical display mapping/i);
    expect(() =>
      typeChartWorkflowSchema.parse({
        ...workflow,
        source: workflow.source
          ? {
              ...workflow.source,
              relativePath: 'exefs/subsdk0'
            }
          : null
      })
    ).toThrow();
  });

  it('accepts an honest unavailable workflow with no detected executable identity', async () => {
    const workflow = await createWorkflow();
    const unavailable: TypeChartWorkflow = {
      ...workflow,
      buildId: 'unknown',
      chartOffsetHex: 'unknown',
      detectedGame: null,
      installMessage: 'Type Chart cannot load until project paths validate.',
      installStatus: 'disabled',
      source: null,
      summary: {
        ...workflow.summary,
        availability: 'disabled'
      }
    };

    expect(typeChartWorkflowSchema.parse(unavailable)).toMatchObject({
      buildId: 'unknown',
      detectedGame: null,
      installStatus: 'disabled'
    });
  });
});

async function createWorkflow() {
  const bridge = createMockProjectBridge({}, true);
  return (await bridge.loadTypeChartWorkflow({ paths: projectPaths })).workflow;
}
