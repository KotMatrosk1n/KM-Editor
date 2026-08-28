/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useRef, useState } from 'react';
import type { ProjectGame, ProjectPaths } from '../bridge/contracts';
import type { ProjectSourceRevisionProjectBridgeApi } from '../bridge/projectSourceRevisionProjectBridge';
import {
  ProjectQueryEpoch,
  runIndependentProjectRead
} from '../utils/projectAsyncPolicy';

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
  const freshnessRef = useRef<ProjectQueryEpoch<'revision'> | null>(null);
  if (freshnessRef.current === null) {
    freshnessRef.current = new ProjectQueryEpoch<'revision'>();
  }
  const freshness = freshnessRef.current;
  const observationIdentity = projectSourceRevisionObservationIdentity(options);
  const requestKey = JSON.stringify([
    'project.sourceRevision.read',
    observationIdentity,
    refreshRevision
  ]);
  const refresh = useCallback(() => {
    freshness.invalidateAll();
    setState({
      error: null,
      fingerprint: null,
      sourceObservationToken: null,
      status: 'loading'
    });
    setRefreshRevision((current) =>
      current === Number.MAX_SAFE_INTEGER ? 0 : current + 1
    );
  }, [freshness]);

  useEffect(() => {
    const ticket = freshness.supersede('revision');
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
    void runIndependentProjectRead(
      'readProjectSourceRevision',
      options.bridge,
      requestKey,
      () => options.bridge.readProjectSourceRevision({
        paths: options.paths as ProjectPaths,
        projectId: options.projectId as string
      })
    )
      .then(
        (response) => {
          if (!freshness.isCurrent(ticket)) return;
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
          if (!freshness.isCurrent(ticket)) return;
          setState({
            error: 'source-revision-unavailable',
            fingerprint: null,
            sourceObservationToken: null,
            status: 'error'
          });
        }
      );

    return () => {
      freshness.supersede('revision');
    };
  }, [
    freshness,
    options.bridge,
    observationIdentity,
    requestKey
  ]);

  return { ...state, refresh };
}
