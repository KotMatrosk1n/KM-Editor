/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { type EditSession } from '../../bridge/contracts';
import {
  type GymUniformRemovalAction,
  type GymUniformRemovalWorkflow,
  getGymUniformRemovalIpsRelativePath
} from '../../bridge/gymUniformRemovalContracts';
import { type WorkflowPanelOutput } from '../../components/workflowPanels';
import { createGymUniformRemovalWorkflow } from '../../testSupport/gymUniformRemovalTestFixtures';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';
import { GymUniformRemovalSection } from './GymUniformRemovalSection';

const emptyPanelOutput: WorkflowPanelOutput = {
  actionDiagnostics: [],
  applyResult: null,
  changePlan: null
};

describe('GymUniformRemovalSection', () => {
  it('renders honest identity and artifact truth while gating every action from canonical state', () => {
    const shieldInstalled = createGymUniformRemovalWorkflow('shield', true);
    const { rerender } = render(
      <GymUniformRemovalSection {...createProps(shieldInstalled, 'shield')} />
    );

    expect(screen.getByText('Pokemon Shield')).toBeInTheDocument();
    expect(screen.getByText(shieldInstalled.buildId))
      .toHaveAttribute('data-localization-ignore', 'true');
    expect(screen.getAllByText('Current KM IPS')).toHaveLength(2);
    expect(screen.getByText('Vanilla handler')).toBeInTheDocument();
    expect(screen.getByText(getGymUniformRemovalIpsRelativePath('shield')))
      .toHaveAttribute('data-localization-ignore', 'true');
    expect(screen.getByText(shieldInstalled.reservedRegions[0]!.offsetLabel))
      .toHaveAttribute('data-localization-ignore', 'true');
    expect(screen.getByText('Gym Uniform Removal Shield uniform-change handler'))
      .toBeInTheDocument();

    const swordInstalled = createGymUniformRemovalWorkflow('sword', true);
    const mismatch: GymUniformRemovalWorkflow = {
      ...shieldInstalled,
      canUninstall: false,
      installStatus: 'blocked',
      ipsArtifactState: 'notInspected',
      mainHandlerState: 'gameMismatch',
      reservedRegions: swordInstalled.reservedRegions,
      stats: { ...shieldInstalled.stats, sourceFileCount: 0 }
    };
    rerender(<GymUniformRemovalSection {...createProps(mismatch, 'sword')} />);
    expect(screen.getByText(getGymUniformRemovalIpsRelativePath('sword')))
      .toHaveAttribute('data-localization-ignore', 'true');
    expect(screen.queryByText(getGymUniformRemovalIpsRelativePath('shield')))
      .not.toBeInTheDocument();

    rerender(
      <GymUniformRemovalSection
        {...createProps(swordInstalled, 'sword')}
        editSession={createSession(swordInstalled, 'install')}
      />
    );
    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();

    rerender(
      <GymUniformRemovalSection {...createProps(swordInstalled, 'shield')} />
    );
    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();

    const blockedWithOwnedIps: GymUniformRemovalWorkflow = {
      ...swordInstalled,
      installStatus: 'blocked',
      mainHandlerState: 'unreadable',
      provenance: {
        fileState: 'layeredOverride',
        sourceFile: 'exefs/main',
        sourceLayer: 'layered'
      },
      stats: { ...swordInstalled.stats, sourceFileCount: 2 }
    };
    rerender(
      <GymUniformRemovalSection {...createProps(blockedWithOwnedIps, 'sword')} />
    );
    expect(screen.getByRole('button', { name: 'Stage Install' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeEnabled();
    expect(screen.getByText('unreadable')).toBeInTheDocument();

    const available = createGymUniformRemovalWorkflow();
    rerender(
      <GymUniformRemovalSection
        {...createProps(available, 'sword')}
        stagingAction="install"
      />
    );
    expect(screen.getByRole('button', { name: 'Staging install' }))
      .toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();

    const forged = createSession(available, 'install');
    forged.pendingEdits[0] = {
      ...forged.pendingEdits[0]!,
      sources: forged.pendingEdits[0]!.sources.slice().reverse()
    };
    rerender(
      <GymUniformRemovalSection
        {...createProps(available, 'sword')}
        editSession={forged}
      />
    );
    expect(screen.getByRole('button', { name: 'Stage Install' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByText('None')).toBeInTheDocument();
  });
});

function createProps(
  workflow: GymUniformRemovalWorkflow,
  selectedGame: 'sword' | 'shield'
) {
  return {
    editSession: null,
    isChangePlanApplying: false,
    isChangePlanCreating: false,
    onApplyChangePlan: vi.fn(),
    onCreateChangePlan: vi.fn(),
    onStageInstall: vi.fn(),
    onStageUninstall: vi.fn(),
    panelOutput: emptyPanelOutput,
    selectedGame,
    stagingAction: null,
    workflow
  };
}

function createSession(
  workflow: GymUniformRemovalWorkflow,
  action: GymUniformRemovalAction
): EditSession {
  const game = workflow.detectedGame ?? 'sword';
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.gymUniformRemoval',
        field: action,
        newValue: 'true',
        recordId: `gym-uniform-removal-v1-${action}`,
        sources: [
          { layer: 'base', relativePath: 'exefs/main' },
          {
            layer: 'pending',
            relativePath:
              `pending/gym-uniform-removal/${action}/${calculatePendingPayloadSha256('true')}`
          },
          {
            layer: 'generated',
            relativePath: getGymUniformRemovalIpsRelativePath(game)
          }
        ],
        summary: `Stage Gym Uniform Removal ${action}.`
      }
    ],
    sessionId: 'session-gym-uniform-removal'
  };
}
