/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it, vi } from 'vitest';
import {
  StaleProjectScopeError,
  createGameScopedProjectBridge,
  createProjectScopeKey,
  type ProjectScopePaths
} from './gameScopedProjectBridge';
import { type ProjectBridge } from './projectBridge';

const swordPaths = {
  baseExeFsPath: 'sword-exefs',
  baseRomFsPath: 'sword-romfs',
  gameTextLanguage: 'en',
  outputRootPath: 'sword-output',
  saveFilePath: null,
  selectedGame: 'sword' as const
};

const violetPaths = {
  baseExeFsPath: 'violet-exefs',
  baseRomFsPath: 'violet-romfs',
  gameTextLanguage: 'en',
  outputRootPath: 'violet-output',
  saveFilePath: null,
  selectedGame: 'violet' as const
};

describe('createGameScopedProjectBridge', () => {
  it('returns responses when the selected project scope still matches', async () => {
    const baseBridge = {
      loadPlacementWorkflow: vi.fn(async () => ({ workflow: { summary: { id: 'placement' } } }))
    } as unknown as ProjectBridge;
    const scopedBridge = createGameScopedProjectBridge(baseBridge, () => swordPaths);

    await expect(scopedBridge.loadPlacementWorkflow({ paths: swordPaths })).resolves.toEqual({
      workflow: { summary: { id: 'placement' } }
    });
  });

  it('rejects responses when the selected game changes before the response returns', async () => {
    let currentPaths: ProjectScopePaths = swordPaths;
    const baseBridge = {
      loadPlacementWorkflow: vi.fn(async () => ({ workflow: { summary: { id: 'placement' } } }))
    } as unknown as ProjectBridge;
    const scopedBridge = createGameScopedProjectBridge(baseBridge, () => currentPaths);

    const response = scopedBridge.loadPlacementWorkflow({ paths: swordPaths });
    currentPaths = violetPaths;

    await expect(response).rejects.toBeInstanceOf(StaleProjectScopeError);
  });

  it('uses paths as part of the scope even when the selected game is unchanged', async () => {
    let currentPaths: ProjectScopePaths = swordPaths;
    const nextSwordPaths = {
      ...swordPaths,
      baseRomFsPath: 'other-sword-romfs'
    };
    const baseBridge = {
      loadPlacementWorkflow: vi.fn(async () => ({ workflow: { summary: { id: 'placement' } } }))
    } as unknown as ProjectBridge;
    const scopedBridge = createGameScopedProjectBridge(baseBridge, () => currentPaths);

    const response = scopedBridge.loadPlacementWorkflow({ paths: swordPaths });
    currentPaths = nextSwordPaths;

    await expect(response).rejects.toMatchObject({
      currentScope: createProjectScopeKey(nextSwordPaths),
      requestScope: createProjectScopeKey(swordPaths)
    });
  });

  it('uses game text language as part of the scope', async () => {
    let currentPaths: ProjectScopePaths = swordPaths;
    const chineseSwordPaths = {
      ...swordPaths,
      gameTextLanguage: 'zh'
    };
    const baseBridge = {
      loadPokemonWorkflow: vi.fn(async () => ({ workflow: { summary: { id: 'pokemon' } } }))
    } as unknown as ProjectBridge;
    const scopedBridge = createGameScopedProjectBridge(baseBridge, () => currentPaths);

    const response = scopedBridge.loadPokemonWorkflow({ paths: chineseSwordPaths });
    currentPaths = swordPaths;

    await expect(response).rejects.toMatchObject({
      currentScope: createProjectScopeKey(swordPaths),
      requestScope: createProjectScopeKey(chineseSwordPaths)
    });
  });
});
