/* SPDX-License-Identifier: GPL-3.0-only */

import { type EditSession } from '../../bridge/contracts';
import {
  type FashionUnlockAction,
  type FashionUnlockWorkflow
} from '../../bridge/fashionUnlockContracts';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';

const fashionUnlockDomain = 'workflow.fashionUnlock';
const fashionUnlockSourcePath = 'exefs/main';

const pendingDefinitions = {
  install: {
    field: 'install',
    recordId: 'fashion-unlock-v1-install',
    summary: 'Stage Fashion Unlock install.'
  },
  uninstall: {
    field: 'uninstall',
    recordId: 'fashion-unlock-v1-uninstall',
    summary: 'Stage Fashion Unlock uninstall.'
  }
} as const;

export function getCanonicalFashionUnlockPendingAction(
  editSession: EditSession | null,
  workflow: FashionUnlockWorkflow | null
): FashionUnlockAction | null {
  if (
    !workflow ||
    editSession?.sessionId.trim().length === 0 ||
    editSession?.hasPendingChanges !== true ||
    editSession.pendingEdits.length !== 1
  ) {
    return null;
  }

  const edit = editSession.pendingEdits[0];
  const action = getPendingAction(edit?.recordId);
  if (!edit || action === null) {
    return null;
  }

  const definition = pendingDefinitions[action];
  if (
    edit.domain !== fashionUnlockDomain ||
    edit.field !== definition.field ||
    edit.newValue !== 'true' ||
    edit.summary !== definition.summary ||
    !hasCanonicalSources(workflow, action, edit.sources)
  ) {
    return null;
  }

  return action;
}

function getPendingAction(recordId: string | null | undefined): FashionUnlockAction | null {
  if (recordId === pendingDefinitions.install.recordId) {
    return 'install';
  }

  if (recordId === pendingDefinitions.uninstall.recordId) {
    return 'uninstall';
  }

  return null;
}

function hasCanonicalSources(
  workflow: FashionUnlockWorkflow,
  action: FashionUnlockAction,
  sources: EditSession['pendingEdits'][number]['sources']
) {
  const expectedSources = workflow.editorFamily === 'swsh'
    ? getSwShSources(workflow, action)
    : getSvSources(workflow, action);

  return (
    expectedSources !== null &&
    sources.length === expectedSources.length &&
    sources.every(
      (source, index) =>
        source.layer === expectedSources[index]?.layer &&
        source.relativePath === expectedSources[index]?.relativePath
    )
  );
}

function getSwShSources(
  workflow: Extract<FashionUnlockWorkflow, { editorFamily: 'swsh' }>,
  action: FashionUnlockAction
) {
  if (
    workflow.provenance.sourceLayer !== 'base' &&
    workflow.provenance.sourceLayer !== 'layered'
  ) {
    return null;
  }

  return [
    { layer: 'base' as const, relativePath: fashionUnlockSourcePath },
    ...(workflow.provenance.sourceLayer === 'layered'
      ? [{ layer: 'layered' as const, relativePath: fashionUnlockSourcePath }]
      : []),
    {
      layer: 'pending' as const,
      relativePath: `pending/fashion-unlock/${action}/${calculatePendingPayloadSha256('true')}`
    }
  ];
}

function getSvSources(
  workflow: Extract<FashionUnlockWorkflow, { editorFamily: 'sv' }>,
  action: FashionUnlockAction
) {
  if (action === 'uninstall') {
    return [
      { layer: 'generated' as const, relativePath: fashionUnlockSourcePath },
      { layer: 'base' as const, relativePath: fashionUnlockSourcePath }
    ];
  }

  if (
    workflow.provenance.sourceLayer !== 'base' &&
    workflow.provenance.sourceLayer !== 'layered'
  ) {
    return null;
  }

  return [
    {
      layer: workflow.provenance.sourceLayer,
      relativePath: fashionUnlockSourcePath
    }
  ];
}
