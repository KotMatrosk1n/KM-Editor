/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { type EditSession } from '../../bridge/contracts';
import { type HyperspaceBypassWorkflow } from '../../bridge/hyperspaceBypassContracts';
import { type WorkflowPanelOutput } from '../../components/workflowPanels';
import { HyperspaceBypassSection } from './HyperspaceBypassSection';

const emptyPanelOutput: WorkflowPanelOutput = {
  actionDiagnostics: [],
  applyResult: null,
  changePlan: null
};

describe('HyperspaceBypassSection', () => {
  it('guards staged actions and applying state', () => {
    const props = createProps();
    const { rerender } = render(
      <HyperspaceBypassSection {...props} editSession={createSession('hyperspace-bypass-v1-install')} />
    );

    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeEnabled();

    rerender(
      <HyperspaceBypassSection {...props} editSession={createSession('hyperspace-bypass-v1-uninstall')} />
    );
    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();

    rerender(
      <HyperspaceBypassSection
        {...props}
        editSession={createSession('hyperspace-bypass-v1-install')}
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

function createWorkflow(): HyperspaceBypassWorkflow {
  return {
    buildId: 'test-build',
    detectedGame: 'scarlet',
    diagnostics: [],
    installMessage: 'Hyperspace Bypass is installed.',
    installStatus: 'installed',
    patchOffsetHex: 'main.text+0x1000',
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
      description: 'Hyperspace Bypass editor',
      diagnostics: [],
      id: 'hyperspaceBypass',
      label: 'Hyperspace Bypass'
    }
  };
}

function createSession(recordId: string): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.hyperspaceBypass',
        field: recordId.endsWith('-uninstall') ? 'uninstall' : 'install',
        newValue: 'true',
        recordId,
        sources: [],
        summary: 'Stage Hyperspace Bypass change.'
      }
    ],
    sessionId: 'session-hyperspace-bypass'
  };
}
