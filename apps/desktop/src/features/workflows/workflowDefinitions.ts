/* SPDX-License-Identifier: GPL-3.0-only */

import {
  workflowDashboardRegistrations,
  type WorkbenchCapabilityRegistration
} from '../../workbench/capabilityRegistry';

export type WorkflowDefinition = Pick<
  WorkbenchCapabilityRegistration,
  'description' | 'icon' | 'id' | 'label'
>;

export const workflowDefinitions: readonly WorkflowDefinition[] =
  workflowDashboardRegistrations.map((registration) => ({
    description: registration.description,
    icon: registration.icon,
    id: registration.id,
    label: registration.workflowDashboardLabel ?? registration.label
  }));
