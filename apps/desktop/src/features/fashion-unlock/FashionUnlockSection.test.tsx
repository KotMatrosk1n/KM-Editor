/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { type EditSession, type FashionUnlockWorkflow } from '../../bridge/contracts';
import { type WorkflowPanelOutput } from '../../components/workflowPanels';
import { FashionUnlockSection } from './FashionUnlockSection';

const emptyPanelOutput: WorkflowPanelOutput = {
  actionDiagnostics: [],
  applyResult: null,
  changePlan: null
};

describe('FashionUnlockSection', () => {
  it('guards staged actions and applying state', () => {
    const props = createProps();
    const { rerender } = render(
      <FashionUnlockSection {...props} editSession={createSession('fashion-unlock-v1-install')} />
    );

    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeEnabled();

    rerender(
      <FashionUnlockSection {...props} editSession={createSession('fashion-unlock-v1-uninstall')} />
    );
    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();

    rerender(
      <FashionUnlockSection
        {...props}
        editSession={createSession('fashion-unlock-v1-install')}
        isChangePlanApplying
      />
    );
    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();
  });
});

function createProps() {
  return {
    editSession: null,
    isChangePlanApplying: false,
    isChangePlanCreating: false,
    isStaging: false,
    onApplyChangePlan: vi.fn(),
    onCreateChangePlan: vi.fn(),
    onStageInstall: vi.fn(),
    onStageUninstall: vi.fn(),
    panelOutput: emptyPanelOutput,
    workflow: createWorkflow()
  };
}

function createWorkflow(): FashionUnlockWorkflow {
  return {
    buildId: 'test-build',
    detectedGame: 'scarlet',
    diagnostics: [],
    directGetterOffsetHex: '',
    editorFamily: 'sv',
    installMessage: 'Fashion Unlock is installed.',
    installStatus: 'installed',
    mappedGetterOffsetHex: '',
    ownershipCheckOffsetHex: 'main.text+0x1000',
    provenance: {
      fileState: 'layeredOverride',
      sourceFile: 'exefs/main',
      sourceLayer: 'layered'
    },
    reservedRegions: [],
    stats: { reservedMainTextRegionCount: 1, sourceFileCount: 1 },
    stubKind: 'installed',
    summary: {
      availability: 'available',
      description: 'Fashion Unlock editor',
      diagnostics: [],
      id: 'fashionUnlock',
      label: 'Fashion Unlock'
    }
  };
}

function createSession(recordId: string): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.fashionUnlock',
        field: recordId.endsWith('-uninstall') ? 'uninstall' : 'install',
        newValue: 'true',
        recordId,
        sources: [],
        summary: 'Stage Fashion Unlock change.'
      }
    ],
    sessionId: 'session-fashion-unlock'
  };
}
