/* SPDX-License-Identifier: GPL-3.0-only */

import type { EditSession } from '../../bridge/contracts';
import type {
  TmMachineControlsWorkflow
} from '../../bridge/tmMachineControlsContracts';

export type TmMachineControlId = 'materialVisibility' | 'recipeAvailability';
export type TmMachineControlPolicy =
  | 'allAvailable'
  | 'alwaysVisible'
  | 'customized'
  | 'discoveryGated'
  | 'progressionGated'
  | 'unknown';
export type TmMachineControlStagingTarget =
  | 'materialDiscovery'
  | 'materialVisible'
  | 'recipeAvailable'
  | 'recipeProgression';

export type TmMachineControlStagingRequest = {
  control: TmMachineControlId;
  enabled: boolean;
  policy: Exclude<TmMachineControlPolicy, 'customized' | 'unknown'>;
};

export const tmMachineControlsEditDomain = 'workflow.tmMachineControls';

export function getEffectiveTmMachineControlPolicy(
  state: TmMachineControlsWorkflow['recipeAvailability']
): TmMachineControlPolicy {
  return state.stagedPolicy ?? state.policy;
}

export function getTmMachineControlPendingCount(editSession: EditSession | null) {
  return editSession?.pendingEdits.filter(
    (edit) => edit.domain === tmMachineControlsEditDomain
  ).length ?? 0;
}

export function getTmMachineControlSource(
  workflow: TmMachineControlsWorkflow,
  control: TmMachineControlId
) {
  return workflow.provenance.find((source) => source.control === control) ?? null;
}

export function isTmMachineControlTargetActive(
  state: TmMachineControlsWorkflow['recipeAvailability'],
  policy: TmMachineControlPolicy
) {
  return getEffectiveTmMachineControlPolicy(state) === policy;
}

export function getTmMachineControlStagingRequest(
  target: TmMachineControlStagingTarget
): TmMachineControlStagingRequest {
  switch (target) {
    case 'materialDiscovery':
      return { control: 'materialVisibility', enabled: false, policy: 'discoveryGated' };
    case 'materialVisible':
      return { control: 'materialVisibility', enabled: true, policy: 'alwaysVisible' };
    case 'recipeAvailable':
      return { control: 'recipeAvailability', enabled: true, policy: 'allAvailable' };
    case 'recipeProgression':
      return { control: 'recipeAvailability', enabled: false, policy: 'progressionGated' };
  }
}
