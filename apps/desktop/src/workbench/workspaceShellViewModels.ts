/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import type { WorkbenchLocation } from './workbenchLocation';

export type WorkspaceTargetViewModel = {
  description: string | null;
  id: string;
  label: string;
  labelIsRawData: boolean;
  location: WorkbenchLocation;
};

export type WorkspaceSavedViewViewModel = {
  description: string | null;
  id: string;
  name: string;
  target: WorkbenchLocation;
};

export type WorkspaceNoteViewModel = {
  entityLabel: string;
  isBusy: boolean;
  statusKey: string | null;
  text: string;
  updatedAtLabel: string | null;
};

export type WorkspaceOutputProfileViewModel = {
  description: string | null;
  id: string;
  isActive: boolean;
  name: string;
};

export type WorkspaceRecentProjectViewModel = {
  game: ProjectGame;
  id: string;
  isAvailable: boolean;
  name: string;
  unavailableReason: string | null;
};

export function mergeWorkspaceRecentTargetViewModels(
  sessionTargets: readonly WorkspaceTargetViewModel[],
  persistedTargets: readonly WorkspaceTargetViewModel[],
  maximumTargets: number
) {
  if (!Number.isSafeInteger(maximumTargets) || maximumTargets < 0) {
    throw new Error('The recent workspace target limit must be a non-negative safe integer.');
  }
  if (maximumTargets === 0) return [];
  const merged: WorkspaceTargetViewModel[] = [];
  const seenIds = new Set<string>();
  for (const target of [...sessionTargets, ...persistedTargets]) {
    if (seenIds.has(target.id)) continue;
    seenIds.add(target.id);
    merged.push(target);
    if (merged.length === maximumTargets) break;
  }
  return merged;
}
