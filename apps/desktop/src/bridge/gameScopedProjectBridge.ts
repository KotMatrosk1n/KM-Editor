/* SPDX-License-Identifier: GPL-3.0-only */

import { type OpenProjectRequest } from './contracts';
import { type ProjectBridge } from './projectBridge';

export type ProjectScopePaths = OpenProjectRequest['paths'];

type ProjectScopedRequest = {
  paths?: ProjectScopePaths | null;
};

export class StaleProjectScopeError extends Error {
  public readonly currentGeneration: number | null;
  public readonly currentScope: string;
  public readonly requestGeneration: number | null;
  public readonly requestScope: string;

  public constructor(
    requestScope: string,
    currentScope: string,
    requestGeneration: number | null = null,
    currentGeneration: number | null = null
  ) {
    super('Ignored a project bridge response for a game or project that is no longer selected.');
    this.name = 'StaleProjectScopeError';
    this.currentGeneration = currentGeneration;
    this.currentScope = currentScope;
    this.requestGeneration = requestGeneration;
    this.requestScope = requestScope;
  }
}

export function isStaleProjectScopeError(error: unknown) {
  return error instanceof StaleProjectScopeError;
}

export function createGameScopedProjectBridge(
  bridge: ProjectBridge,
  getCurrentPaths: () => ProjectScopePaths,
  getCurrentGeneration?: () => number
): ProjectBridge {
  return new Proxy(bridge, {
    get(target, property, receiver) {
      const value = Reflect.get(target, property, receiver);
      if (typeof value !== 'function') {
        return value;
      }

      return async (...args: unknown[]) => {
        const requestScope = getProjectScopeFromRequest(args[0]);
        const requestGeneration = requestScope ? (getCurrentGeneration?.() ?? null) : null;
        let response: unknown;

        try {
          response = await value.apply(target, args);
        } catch (error) {
          const staleScopeError = getStaleProjectScopeError(
            requestScope,
            requestGeneration,
            getCurrentPaths,
            getCurrentGeneration
          );
          if (staleScopeError) {
            throw staleScopeError;
          }

          throw error;
        }

        const staleScopeError = getStaleProjectScopeError(
          requestScope,
          requestGeneration,
          getCurrentPaths,
          getCurrentGeneration
        );
        if (staleScopeError) {
          throw staleScopeError;
        }

        return response;
      };
    }
  }) as ProjectBridge;
}

function getStaleProjectScopeError(
  requestScope: string | null,
  requestGeneration: number | null,
  getCurrentPaths: () => ProjectScopePaths,
  getCurrentGeneration?: () => number
) {
  if (!requestScope) {
    return null;
  }

  const currentScope = createProjectScopeKey(getCurrentPaths());
  const currentGeneration = getCurrentGeneration?.() ?? null;
  if (
    requestScope === currentScope &&
    (requestGeneration === null || requestGeneration === currentGeneration)
  ) {
    return null;
  }

  return new StaleProjectScopeError(
    requestScope,
    currentScope,
    requestGeneration,
    currentGeneration
  );
}

export function createProjectScopeKey(paths: ProjectScopePaths) {
  return JSON.stringify({
    selectedGame: paths.selectedGame,
    baseRomFsPath: paths.baseRomFsPath,
    baseExeFsPath: paths.baseExeFsPath,
    gameTextLanguage: paths.gameTextLanguage ?? null,
    outputRootPath: paths.outputRootPath,
    saveFilePath: paths.saveFilePath,
    scarletVioletSupportFolderPath: paths.scarletVioletSupportFolderPath ?? null,
    pokemonLegendsZASupportFolderPath: paths.pokemonLegendsZASupportFolderPath ?? null
  });
}

function getProjectScopeFromRequest(request: unknown) {
  if (!isProjectScopedRequest(request) || !request.paths) {
    return null;
  }

  return createProjectScopeKey(request.paths);
}

function isProjectScopedRequest(request: unknown): request is ProjectScopedRequest {
  return typeof request === 'object' && request !== null && 'paths' in request;
}
