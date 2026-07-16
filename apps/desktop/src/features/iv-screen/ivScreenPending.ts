/* SPDX-License-Identifier: GPL-3.0-only */

import type { EditSession } from '../../bridge/contracts';

export type IvScreenPendingOperation = 'install' | 'uninstall';

const installSummary = 'Stage IV Screen install or refresh.';
const uninstallSummary = 'Stage IV Screen uninstall.';
const actionPayloadHash = 'B5BEA41B6C623F7C09F1BF24DCAE58EBAB3C0CDD90AD966BC43A45B44867E12B';

function hasCanonicalSources(
  sources: EditSession['pendingEdits'][number]['sources'],
  operation: IvScreenPendingOperation
) {
  const expectedSources = [
    { layer: 'base', relativePath: 'exefs/main' },
    ...(sources.length === 3 ? [{ layer: 'layered', relativePath: 'exefs/main' }] : []),
    {
      layer: 'pending',
      relativePath: `pending/iv-screen/${operation}/${actionPayloadHash}`
    }
  ] as const;

  return (
    sources.length === expectedSources.length &&
    sources.every(
      (source, index) =>
        source.layer === expectedSources[index]?.layer &&
        source.relativePath === expectedSources[index]?.relativePath
    )
  );
}

export function getIvScreenPendingOperation(
  editSession: EditSession | null
): IvScreenPendingOperation | null {
  if (editSession?.pendingEdits.length !== 1) {
    return null;
  }

  const edit = editSession.pendingEdits[0];
  if (edit?.domain !== 'workflow.ivScreen' || edit.newValue !== 'true') {
    return null;
  }

  if (
    edit.recordId === 'iv-screen-v1-install' &&
    edit.field === 'install' &&
    edit.summary === installSummary &&
    hasCanonicalSources(edit.sources, 'install')
  ) {
    return 'install';
  }

  if (
    edit.recordId === 'iv-screen-v1-uninstall' &&
    edit.field === 'uninstall' &&
    edit.summary === uninstallSummary &&
    hasCanonicalSources(edit.sources, 'uninstall')
  ) {
    return 'uninstall';
  }

  return null;
}
