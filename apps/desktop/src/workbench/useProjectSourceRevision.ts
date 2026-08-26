/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useRef, useState } from 'react';
import type { ProjectGame, ProjectPaths } from '../bridge/contracts';
import type { ProjectSourceRevisionProjectBridgeApi } from '../bridge/projectSourceRevisionProjectBridge';

export type ProjectSourceRevisionState = {
  error: string | null;
  fingerprint: string | null;
  sourceObservationToken: string | null;
  status: 'idle' | 'loading' | 'ready' | 'error';
};

export function projectSourceRevisionObservationIdentity(options: {
  game: ProjectGame | null;
  paths: ProjectPaths | null;
  projectId: string | null;
}) {
  return JSON.stringify({
    game: options.game,
    paths: options.paths
      ? {
          baseExeFsPath: options.paths.baseExeFsPath,
          baseRomFsPath: options.paths.baseRomFsPath,
          gameTextLanguage: options.paths.gameTextLanguage ?? null,
          outputRootPath: options.paths.outputRootPath,
          pokemonLegendsZASupportFolderPath:
            options.paths.pokemonLegendsZASupportFolderPath ?? null,
          saveFilePath: options.paths.saveFilePath,
          scarletVioletSupportFolderPath:
            options.paths.scarletVioletSupportFolderPath ?? null,
          selectedGame: options.paths.selectedGame
        }
      : null,
    projectId: options.projectId
  });
}

export function useProjectSourceRevision(options: {
  bridge: ProjectSourceRevisionProjectBridgeApi;
  game: ProjectGame | null;
  paths: ProjectPaths | null;
  projectId: string | null;
}) {
  const [refreshRevision, setRefreshRevision] = useState(0);
  const [state, setState] = useState<ProjectSourceRevisionState>({
    error: null,
    fingerprint: null,
    sourceObservationToken: null,
    status: 'idle'
  });
  const generationRef = useRef(0);
  const observationIdentity = projectSourceRevisionObservationIdentity(options);
  const refresh = useCallback(() => {
    generationRef.current += 1;
    setState({
      error: null,
      fingerprint: null,
      sourceObservationToken: null,
      status: 'loading'
    });
    setRefreshRevision((current) =>
      current === Number.MAX_SAFE_INTEGER ? 0 : current + 1
    );
  }, []);

  useEffect(() => {
    const generation = ++generationRef.current;
    if (!options.game || !options.paths || !options.projectId) {
      setState({
        error: null,
        fingerprint: null,
        sourceObservationToken: null,
        status: 'idle'
      });
      return;
    }

    setState({
      error: null,
      fingerprint: null,
      sourceObservationToken: null,
      status: 'loading'
    });
    void options.bridge
      .readProjectSourceRevision({
        paths: options.paths,
        projectId: options.projectId
      })
      .then(
        (response) => {
          if (generation !== generationRef.current) return;
          if (
            response.projectId !== options.projectId ||
            response.game !== options.game
          ) {
            setState({
              error: 'scope-mismatch',
              fingerprint: null,
              sourceObservationToken: null,
              status: 'error'
            });
            return;
          }
          setState({
            error: null,
            fingerprint: response.fingerprint,
            sourceObservationToken: response.sourceObservationToken,
            status: 'ready'
          });
        },
        () => {
          if (generation !== generationRef.current) return;
          setState({
            error: 'source-revision-unavailable',
            fingerprint: null,
            sourceObservationToken: null,
            status: 'error'
          });
        }
      );

    return () => {
      generationRef.current += 1;
    };
  }, [
    options.bridge,
    observationIdentity,
    refreshRevision
  ]);

  return { ...state, refresh };
}
