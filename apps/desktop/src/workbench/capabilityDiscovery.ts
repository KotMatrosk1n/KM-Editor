/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  ApiDiagnostic,
  ProjectGame,
  ProjectHealth,
  WorkflowSummary
} from '../bridge/contracts';
import {
  getWorkbenchSectionDescriptionKey,
  getWorkbenchSectionLabelKey,
  isCapabilityRegisteredForGame,
  standaloneWorkflowSectionIds,
  workbenchCapabilityRegistry
} from './capabilityRegistry';
import { resolveWorkflowDataSection } from '../workflowGameSupport';
import { getDiagnosticLocalizationKey } from '../diagnostics';
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
      const workflow = workflowsById.get(resolveWorkflowDataSection(registration.id));
      const isWorkflow =
        registration.navigationKind === 'workflow' ||
        registration.navigationKind === 'hidden';
      const resolved = registration.id === 'gameplaySettings'
        ? {
            reason: null,
            reasonKey: null,
            status: options.health?.canOpenEditableWorkflows
              ? 'editable' as const
              : 'available' as const,
            statusKey: options.health?.canOpenEditableWorkflows
              ? 'workbench.capability.status.editable'
              : 'workbench.capability.status.available'
          }
        : isWorkflow
          ? resolveWorkflowStatus(
              registration.maturity,
              workflow,
              options.health,
              standaloneWorkflowSectionIds.has(registration.id)
            )
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

function resolveWorkflowStatus(
  maturity: 'editable' | 'readOnly' | 'mixed' | 'utility',
  workflow: WorkflowSummary | undefined,
  health: ProjectHealth | null,
  acceptsMissingWorkflowSummary: boolean
): Pick<
  CapabilityDiscoveryViewModel,
  'reason' | 'reasonKey' | 'status' | 'statusKey'
> {
  const workflowDiagnostic = firstDiagnostic(workflow?.diagnostics ?? []);
  const healthDiagnostic = firstDiagnostic(health?.diagnostics ?? []);
  const readOnly = maturity === 'readOnly' || workflow?.availability === 'readOnly';
  const supportsReadOnlyAccess =
    readOnly || maturity === 'mixed' || maturity === 'utility';
  const healthAllowsOpen = supportsReadOnlyAccess
    ? Boolean(health?.canOpenReadOnlyWorkflows)
    : Boolean(health?.canOpenEditableWorkflows);

  if (!healthAllowsOpen) {
    return {
      ...resolveCapabilityReason(
        healthDiagnostic ?? workflowDiagnostic,
        'workbench.capability.reason.projectUnavailable'
      ),
      status: 'blocked',
      statusKey: 'workbench.capability.status.blocked'
    };
  }
  if (
    workflow?.availability === 'disabled' ||
    (!workflow && !acceptsMissingWorkflowSummary)
  ) {
    return {
      ...resolveCapabilityReason(
        workflowDiagnostic,
        'workbench.capability.reason.projectUnavailable'
      ),
      status: 'blocked',
      statusKey: 'workbench.capability.status.blocked'
    };
  }
  if (readOnly || (maturity === 'mixed' && !health?.canOpenEditableWorkflows)) {
    return {
      reason: null,
      reasonKey: null,
      status: 'readOnly',
      statusKey: 'workbench.capability.status.readOnly'
    };
  }
  if (maturity === 'utility') {
    return {
      reason: null,
      reasonKey: null,
      status: 'available',
      statusKey: 'workbench.capability.status.available'
    };
  }
  return {
    reason: null,
    reasonKey: null,
    status: 'editable',
    statusKey: 'workbench.capability.status.editable'
  };
}

function firstDiagnostic(
  diagnostics: readonly ApiDiagnostic[]
) {
  return (
    diagnostics.find((diagnostic) => diagnostic.severity === 'error') ??
    diagnostics.find((diagnostic) => diagnostic.severity === 'warning') ??
    diagnostics[0] ??
    null
  );
}

function resolveCapabilityReason(
  diagnostic: ApiDiagnostic | null,
  fallbackKey: string
): Pick<CapabilityDiscoveryViewModel, 'reason' | 'reasonKey'> {
  if (!diagnostic) {
    return { reason: null, reasonKey: fallbackKey };
  }

  const reasonKey = diagnostic.code
    ? getDiagnosticLocalizationKey(diagnostic.code)
    : null;
  return reasonKey
    ? { reason: null, reasonKey }
    : { reason: diagnostic.message, reasonKey: null };
}
