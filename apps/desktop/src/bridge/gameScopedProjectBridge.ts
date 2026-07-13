/* SPDX-License-Identifier: GPL-3.0-only */

import { type OpenProjectRequest } from './contracts';
import { type ProjectBridge } from './projectBridge';

export type ProjectScopePaths = OpenProjectRequest['paths'];

type ProjectScopedRequest = {
  paths?: ProjectScopePaths | null;
};

export class StaleProjectScopeError extends Error {
  public readonly currentScope: string;
  public readonly requestScope: string;

  public constructor(requestScope: string, currentScope: string) {
    super('Ignored a project bridge response for a game or project that is no longer selected.');
    this.name = 'StaleProjectScopeError';
    this.currentScope = currentScope;
    this.requestScope = requestScope;
  }
}

export function isStaleProjectScopeError(error: unknown) {
  return error instanceof StaleProjectScopeError;
}

export function createGameScopedProjectBridge(
  bridge: ProjectBridge,
  getCurrentPaths: () => ProjectScopePaths
): ProjectBridge {
  return new Proxy(bridge, {
    get(target, property, receiver) {
      const value = Reflect.get(target, property, receiver);
      if (typeof value !== 'function') {
        return value;
      }

      return async (...args: unknown[]) => {
        const requestScope = getProjectScopeFromRequest(args[0]);
        const response = await value.apply(target, args);

        if (requestScope) {
          const currentScope = createProjectScopeKey(getCurrentPaths());
          if (requestScope !== currentScope) {
            throw new StaleProjectScopeError(requestScope, currentScope);
          }
        }

        return response;
      };
    }
  }) as ProjectBridge;
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
