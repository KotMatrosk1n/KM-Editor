/* SPDX-License-Identifier: GPL-3.0-only */

import { type EditSession } from '../../bridge/contracts';
import {
  type GymUniformRemovalAction,
  type GymUniformRemovalWorkflow,
  getGymUniformRemovalIpsRelativePath
} from '../../bridge/gymUniformRemovalContracts';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';

const domain = 'workflow.gymUniformRemoval';
const mainRelativePath = 'exefs/main';

const pendingDefinitions = {
  install: {
    field: 'install',
    recordId: 'gym-uniform-removal-v1-install',
    summary: 'Stage Gym Uniform Removal install.'
  },
  uninstall: {
    field: 'uninstall',
    recordId: 'gym-uniform-removal-v1-uninstall',
    summary: 'Stage Gym Uniform Removal uninstall.'
  }
} as const;

export function getCanonicalGymUniformRemovalPendingAction(
  session: EditSession | null,
  workflow: GymUniformRemovalWorkflow | null
): GymUniformRemovalAction | null {
  if (
    !workflow ||
    session?.sessionId.trim().length === 0 ||
    session?.hasPendingChanges !== true ||
    session.pendingEdits.length !== 1
  ) {
    return null;
  }

  const edit = session.pendingEdits[0];
  const action = getPendingAction(edit?.recordId);
  if (!edit || action === null) {
    return null;
  }

  const definition = pendingDefinitions[action];
  return edit.domain === domain &&
    edit.field === definition.field &&
    edit.newValue === 'true' &&
    edit.summary === definition.summary &&
    hasCanonicalSources(workflow, action, edit.sources)
    ? action
    : null;
}

function getPendingAction(
  recordId: string | null | undefined
): GymUniformRemovalAction | null {
  if (recordId === pendingDefinitions.install.recordId) {
    return 'install';
  }

  if (recordId === pendingDefinitions.uninstall.recordId) {
    return 'uninstall';
  }

  return null;
}

function hasCanonicalSources(
  workflow: GymUniformRemovalWorkflow,
  action: GymUniformRemovalAction,
  sources: EditSession['pendingEdits'][number]['sources']
) {
  if (
    workflow.detectedGame === null ||
    (workflow.provenance.sourceLayer !== 'base' &&
      workflow.provenance.sourceLayer !== 'layered') ||
    workflow.provenance.fileState === 'layeredOnly'
  ) {
    return false;
  }

  const expectedSources = [
    { layer: 'base' as const, relativePath: mainRelativePath },
    ...(action === 'install' && workflow.provenance.sourceLayer === 'layered'
      ? [{ layer: 'layered' as const, relativePath: mainRelativePath }]
      : []),
    {
      layer: 'pending' as const,
      relativePath: `pending/gym-uniform-removal/${action}/${calculatePendingPayloadSha256('true')}`
    },
    {
      layer: 'generated' as const,
      relativePath: getGymUniformRemovalIpsRelativePath(workflow.detectedGame)
    }
  ];

  return sources.length === expectedSources.length &&
    sources.every(
      (source, index) =>
        source.layer === expectedSources[index]?.layer &&
        source.relativePath === expectedSources[index]?.relativePath
    );
}
