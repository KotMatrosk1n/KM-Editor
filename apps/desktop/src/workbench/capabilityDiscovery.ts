/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  ProjectGame,
  ProjectHealth,
  WorkflowSummary
} from '../bridge/contracts';
import {
  getWorkbenchSectionDescriptionKey,
  getWorkbenchSectionLabelKey,
  isCapabilityRegisteredForGame,
  workbenchCapabilityRegistry
} from './capabilityRegistry';
import type { WorkbenchSection } from './workbenchSections';

export type CapabilityDiscoveryStatus = 'available' | 'blocked' | 'editable' | 'readOnly';

export type CapabilityDiscoveryViewModel = {
  capabilityKinds: readonly string[];
  descriptionKey: string;
  id: WorkbenchSection;
  labelKey: string;
  reason: string | null;
  reasonKey: string | null;
  status: CapabilityDiscoveryStatus;
  statusKey: string;
};

export function createCapabilityDiscoveryViewModels(options: {
  game: ProjectGame;
  health: ProjectHealth | null;
  workflows: readonly WorkflowSummary[];
}): CapabilityDiscoveryViewModel[] {
  const workflowsById = new Map(options.workflows.map((workflow) => [workflow.id, workflow]));
  return workbenchCapabilityRegistry
    .filter(
      (registration) =>
        registration.navigationKind !== 'internal' &&
        isCapabilityRegisteredForGame(registration.id, options.game)
    )
    .map((registration) => {
      const workflow = workflowsById.get(registration.id);
      const isWorkflow =
        registration.navigationKind === 'workflow' ||
        registration.navigationKind === 'hidden';
      const resolved = isWorkflow
        ? resolveWorkflowStatus(registration.maturity, workflow, options.health)
        : {
            reason: null,
            reasonKey: null,
            status: 'available' as const,
            statusKey: 'workbench.capability.status.available'
          };
      return {
        capabilityKinds: registration.capabilityKinds,
        descriptionKey: getWorkbenchSectionDescriptionKey(registration.id),
        id: registration.id,
        labelKey: getWorkbenchSectionLabelKey(registration.id),
        ...resolved
      };
    });
}

export function createCapabilityDiscoverySignature(
  capabilities: readonly CapabilityDiscoveryViewModel[]
) {
  return `v1:${capabilities
    .map((capability) => ({
      capabilityKinds: [...capability.capabilityKinds].sort(),
      id: capability.id,
      status: capability.status
    }))
    .sort((left, right) => (left.id < right.id ? -1 : left.id > right.id ? 1 : 0))
    .map(
      (capability) =>
        `${capability.id}=${capability.status}[${capability.capabilityKinds.join(',')}]`
    )
    .join(';')}`;
}

function resolveWorkflowStatus(
  maturity: 'editable' | 'readOnly' | 'mixed' | 'utility',
  workflow: WorkflowSummary | undefined,
  health: ProjectHealth | null
): Pick<
  CapabilityDiscoveryViewModel,
  'reason' | 'reasonKey' | 'status' | 'statusKey'
> {
  const reason = workflow?.diagnostics.find(
    (diagnostic) => diagnostic.severity === 'error'
  )?.message ?? workflow?.diagnostics.find(
    (diagnostic) => diagnostic.severity === 'warning'
  )?.message ?? workflow?.diagnostics[0]?.message ?? null;
  const readOnly = maturity === 'readOnly' || workflow?.availability === 'readOnly';
  const healthAllowsOpen = readOnly
    ? Boolean(health?.canOpenReadOnlyWorkflows)
    : Boolean(health?.canOpenEditableWorkflows);

  if (!workflow || workflow.availability === 'disabled' || !healthAllowsOpen) {
    return {
      reason,
      reasonKey: reason ? null : 'workbench.capability.reason.projectUnavailable',
      status: 'blocked',
      statusKey: 'workbench.capability.status.blocked'
    };
  }
  if (readOnly) {
    return {
      reason: null,
      reasonKey: null,
      status: 'readOnly',
      statusKey: 'workbench.capability.status.readOnly'
    };
  }
  return {
    reason: null,
    reasonKey: null,
    status: 'editable',
    statusKey: 'workbench.capability.status.editable'
  };
}
