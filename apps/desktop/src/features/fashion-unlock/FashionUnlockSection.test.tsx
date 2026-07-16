/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { type EditSession } from '../../bridge/contracts';
import { type FashionUnlockWorkflow } from '../../bridge/fashionUnlockContracts';
import { type WorkflowPanelOutput } from '../../components/workflowPanels';
import {
  createSvFashionUnlockWorkflow,
  createSwShFashionUnlockWorkflow
} from '../../testSupport/fashionUnlockTestFixtures';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';
import { FashionUnlockSection } from './FashionUnlockSection';

const emptyPanelOutput: WorkflowPanelOutput = {
  actionDiagnostics: [],
  applyResult: null,
  changePlan: null
};

describe('FashionUnlockSection', () => {
  it('uses canonical staged truth and permits switching actions', () => {
    const workflow = createSwShFashionUnlockWorkflow('sword', true);
    const props = createProps(workflow);
    const { rerender } = render(
      <FashionUnlockSection {...props} editSession={createSession(workflow, 'install')} />
    );

    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeEnabled();
    expect(screen.getByText('Install or refresh')).toBeInTheDocument();

    rerender(
      <FashionUnlockSection {...props} editSession={createSession(workflow, 'uninstall')} />
    );
    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();
    expect(screen.getAllByText('Uninstall').length).toBeGreaterThan(0);
  });

  it('rejects malformed pending identity and blocks every action', () => {
    const workflow = createSwShFashionUnlockWorkflow('sword', true);
    const session = createSession(workflow, 'install');
    render(
      <FashionUnlockSection
        {...createProps(workflow)}
        editSession={{
          ...session,
          pendingEdits: [{ ...session.pendingEdits[0]!, newValue: 'TRUE' }]
        }}
      />
    );

    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    expect(screen.getByText('None')).toBeInTheDocument();
  });

  it('requires the workflow family and detected game to match the selected project', () => {
    const workflow = createSwShFashionUnlockWorkflow('shield', true);
    render(
      <FashionUnlockSection
        {...createProps(workflow)}
        editSession={null}
        selectedGame="sword"
      />
    );

    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();
  });

  it('uses backend uninstall truth instead of installed status alone', () => {
    const installedBase = {
      ...createSvFashionUnlockWorkflow('scarlet', true),
      canUninstall: false,
      provenance: {
        fileState: 'baseOnly' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'base' as const
      }
    };
    render(<FashionUnlockSection {...createProps(installedBase)} editSession={null} />);

    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();
  });

  it.each([
    ['install', 'Staging install'],
    ['uninstall', 'Staging uninstall']
  ] as const)('locks all actions and identifies a busy %s action', (stagingAction, label) => {
    const workflow = createSwShFashionUnlockWorkflow('sword', true);
    render(
      <FashionUnlockSection
        {...createProps(workflow)}
        editSession={createSession(workflow, 'install')}
        stagingAction={stagingAction}
      />
    );

    expect(screen.getByRole('button', { name: label })).toHaveAttribute('aria-busy', 'true');
    expect(screen.getAllByRole('button')).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ disabled: true }),
        expect.objectContaining({ disabled: true }),
        expect.objectContaining({ disabled: true }),
        expect.objectContaining({ disabled: true })
      ])
    );
    for (const button of screen.getAllByRole('button')) {
      expect(button).toBeDisabled();
    }
  });

  it('preserves Scarlet/Violet ownership copy and hides Sword/Shield getters', () => {
    const workflow = createSvFashionUnlockWorkflow('violet');
    render(<FashionUnlockSection {...createProps(workflow)} editSession={null} />);

    expect(screen.getByText('Pokemon Violet')).toBeInTheDocument();
    expect(screen.getAllByText('Ownership check').length).toBeGreaterThan(0);
    expect(screen.queryByText('Mapped getter')).not.toBeInTheDocument();
    expect(screen.getByText('Scarlet/Violet dress-up ownership check')).toBeInTheDocument();
  });

  it('marks informational table rows as static', () => {
    const workflow = createSwShFashionUnlockWorkflow();
    const { container } = render(
      <FashionUnlockSection {...createProps(workflow)} editSession={null} />
    );

    const bodyRows = container.querySelectorAll(
      '.exefs-row.iv-screen-range-row:not(.exefs-row-heading)'
    );
    expect(bodyRows.length).toBeGreaterThan(0);
    for (const row of bodyRows) {
      expect(row).toHaveClass('iv-screen-range-row-static');
    }
  });
});

function createProps(workflow: FashionUnlockWorkflow) {
  return {
    editSession: null,
    isChangePlanApplying: false,
    isChangePlanCreating: false,
    onApplyChangePlan: vi.fn(),
    onCreateChangePlan: vi.fn(),
    onStageInstall: vi.fn(),
    onStageUninstall: vi.fn(),
    panelOutput: emptyPanelOutput,
    selectedGame: workflow.detectedGame,
    stagingAction: null,
    workflow
  };
}

function createSession(
  workflow: FashionUnlockWorkflow,
  action: 'install' | 'uninstall'
): EditSession {
  const sourcePath = 'exefs/main';
  const swshSources = [
    { layer: 'base' as const, relativePath: sourcePath },
    ...(workflow.provenance.sourceLayer === 'layered'
      ? [{ layer: 'layered' as const, relativePath: sourcePath }]
      : []),
    {
      layer: 'pending' as const,
      relativePath: `pending/fashion-unlock/${action}/${calculatePendingPayloadSha256('true')}`
    }
  ];
  const svSources = action === 'install'
    ? [{ layer: workflow.provenance.sourceLayer, relativePath: sourcePath }]
    : [
        { layer: 'generated' as const, relativePath: sourcePath },
        { layer: 'base' as const, relativePath: sourcePath }
      ];
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.fashionUnlock',
        field: action,
        newValue: 'true',
        recordId: `fashion-unlock-v1-${action}`,
        sources: workflow.editorFamily === 'swsh' ? swshSources : svSources,
        summary: `Stage Fashion Unlock ${action}.`
      }
    ],
    sessionId: 'session-fashion-unlock'
  };
}
