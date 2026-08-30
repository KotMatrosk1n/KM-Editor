/* SPDX-License-Identifier: GPL-3.0-only */

import { reconcileSourceBackedDraft } from '../../components/localEditorDraftState';
import type { OutputSafetyScope } from '../../bridge/outputSafetyContracts';

export type ProjectRelocationCandidatePaths = OutputSafetyScope['paths'];
export type ProjectRelocationCandidatePathField = Exclude<
  keyof ProjectRelocationCandidatePaths,
  'gameTextLanguage' | 'selectedGame'
>;

export const projectRelocationCandidatePathFields = Object.freeze([
  'baseRomFsPath',
  'baseExeFsPath',
  'outputRootPath',
  'saveFilePath',
  'scarletVioletSupportFolderPath',
  'pokemonLegendsZASupportFolderPath'
] satisfies readonly ProjectRelocationCandidatePathField[]);

export function reconcileRelocationCandidatePaths(
  current: ProjectRelocationCandidatePaths | null,
  previousSource: OutputSafetyScope | null,
  nextSource: OutputSafetyScope | null
): ProjectRelocationCandidatePaths | null {
  if (!nextSource) {
    return null;
  }

  if (
    !current ||
    !previousSource ||
    previousSource.projectId !== nextSource.projectId ||
    previousSource.paths.selectedGame !== nextSource.paths.selectedGame
  ) {
    return { ...nextSource.paths };
  }

  const next = { ...nextSource.paths };
  for (const field of projectRelocationCandidatePathFields) {
    next[field] = reconcileSourceBackedDraft(
      current[field] ?? null,
      previousSource.paths[field] ?? null,
      nextSource.paths[field] ?? null,
      Object.is
    );
  }
  return next;
}
