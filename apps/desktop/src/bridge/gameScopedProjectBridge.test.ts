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

const zaPaths = {
  baseExeFsPath: 'za-exefs',
  baseRomFsPath: 'za-romfs',
  gameTextLanguage: 'en',
  outputRootPath: 'za-output',
  saveFilePath: null,
  selectedGame: 'za' as const
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

  it('rejects a response after the project generation changes even when paths change back', async () => {
    let currentGeneration = 4;
    const baseBridge = {
      updateRentalPokemonField: vi.fn(async () => ({
        diagnostics: [],
        session: { hasPendingChanges: true, pendingEdits: [], sessionId: 'stale-session' },
        workflow: { summary: { id: 'rentalPokemon' } }
      }))
    } as unknown as ProjectBridge;
    const scopedBridge = createGameScopedProjectBridge(
      baseBridge,
      () => swordPaths,
      () => currentGeneration
    );

    const response = scopedBridge.updateRentalPokemonField({
      field: 'level',
      paths: swordPaths,
      rentalIndex: 0,
      session: { hasPendingChanges: false, pendingEdits: [], sessionId: 'session-1' },
      value: '65'
    });
    currentGeneration += 2;

    await expect(response).rejects.toMatchObject({
      currentGeneration: 6,
      currentScope: createProjectScopeKey(swordPaths),
      requestGeneration: 4,
      requestScope: createProjectScopeKey(swordPaths)
    });
  });

  it('maps a rejected stale request to a stale project scope error', async () => {
    let currentPaths: ProjectScopePaths = swordPaths;
    let rejectRequest!: (reason: Error) => void;
    const baseBridge = {
      loadPlacementWorkflow: vi.fn(
        () =>
          new Promise((_resolve, reject) => {
            rejectRequest = reject;
          })
      )
    } as unknown as ProjectBridge;
    const scopedBridge = createGameScopedProjectBridge(baseBridge, () => currentPaths);

    const response = scopedBridge.loadPlacementWorkflow({ paths: swordPaths });
    currentPaths = violetPaths;
    rejectRequest(new Error('Old project failure'));

    await expect(response).rejects.toMatchObject({
      currentScope: createProjectScopeKey(violetPaths),
      message: 'Ignored a project bridge response for a game or project that is no longer selected.',
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

  it.each([
    [
      'S/V',
      { ...violetPaths, scarletVioletSupportFolderPath: 'sv-support-a' },
      { ...violetPaths, scarletVioletSupportFolderPath: 'sv-support-b' }
    ],
    [
      'Z-A',
      { ...zaPaths, pokemonLegendsZASupportFolderPath: 'za-support-a' },
      { ...zaPaths, pokemonLegendsZASupportFolderPath: 'za-support-b' }
    ]
  ])('uses the %s support folder as part of the scope', async (_game, requestPaths, nextPaths) => {
    let currentPaths: ProjectScopePaths = requestPaths;
    const baseBridge = {
      loadPokemonWorkflow: vi.fn(async () => ({ workflow: { summary: { id: 'pokemon' } } }))
    } as unknown as ProjectBridge;
    const scopedBridge = createGameScopedProjectBridge(baseBridge, () => currentPaths);

    const response = scopedBridge.loadPokemonWorkflow({ paths: requestPaths });
    currentPaths = nextPaths;

    await expect(response).rejects.toMatchObject({
      currentScope: createProjectScopeKey(nextPaths),
      requestScope: createProjectScopeKey(requestPaths)
    });
  });
});
