/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import {
  type EditSession,
  type ProjectGame,
  type TypeChartWorkflow
} from '../../bridge/contracts';
import { type WorkflowPanelOutput } from '../../components/workflowPanels';
import { TypeChartSection } from './TypeChartSection';

const emptyPanelOutput: WorkflowPanelOutput = {
  actionDiagnostics: [],
  applyResult: null,
  changePlan: null
};

describe('TypeChartSection', () => {
  it.each(
    [
      ['scarlet', 'sv-type-chart-v1-uninstall'],
      ['violet', 'sv-type-chart-v1-uninstall'],
      ['za', 'za-type-chart-v1-uninstall']
    ] satisfies Array<[ProjectGame, string]>
  )(
    'supports uninstall staging and review for %s',
    async (detectedGame, uninstallRecordId) => {
      const user = userEvent.setup();
      const onStageUninstall = vi.fn();
      const props = createProps(detectedGame, onStageUninstall);
      const { rerender } = render(<TypeChartSection {...props} />);

      const stageButton = screen.getByRole('button', { name: 'Stage Uninstall' });
      expect(stageButton).toBeEnabled();
      await user.click(stageButton);
      expect(onStageUninstall).toHaveBeenCalledOnce();

      rerender(
        <TypeChartSection
          {...props}
          editSession={createUninstallSession(uninstallRecordId)}
        />
      );

      expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();
    }
  );

  it('does not present synthetic vanilla cells when the executable source is blocked', () => {
    const workflow = createWorkflow('sword');
    render(
      <TypeChartSection
        {...createProps('sword', vi.fn())}
        workflow={{
          ...workflow,
          chartOffsetHex: 'unknown',
          installMessage: 'Type Chart could not verify exefs/main.',
          installStatus: 'blocked',
          source: workflow.source
            ? {
                ...workflow.source,
                provenance: {
                  ...workflow.source.provenance,
                  sourceLayer: 'generated'
                },
                status: 'missing'
              }
            : null
        }}
      />
    );

    expect(screen.getByText('Unavailable')).toBeInTheDocument();
    expect(screen.getByText('Type Chart could not verify exefs/main.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Type effectiveness chart')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Stage Type Chart' })).not.toBeInTheDocument();
  });
});

function createProps(detectedGame: ProjectGame, onStageUninstall: () => void) {
  return {
    editSession: null,
    isChangePlanApplying: false,
    isChangePlanCreating: false,
    isStaging: false,
    onApplyChangePlan: vi.fn(),
    onCreateChangePlan: vi.fn(),
    onDirtyChange: vi.fn(),
    onStageChart: vi.fn(),
    onStageUninstall,
    panelOutput: emptyPanelOutput,
    workflow: createWorkflow(detectedGame)
  };
}

function createWorkflow(detectedGame: ProjectGame): TypeChartWorkflow {
  return {
    buildId: 'test-build',
    cells: [],
    chartOffsetHex: 'main+0x1000',
    detectedGame,
    diagnostics: [],
    installMessage: 'Type Chart contains custom effectiveness values.',
    installStatus: 'modified',
    source: {
      label: 'ExeFS main',
      provenance: {
        fileState: 'layeredOverride',
        sourceFile: 'exefs/main',
        sourceLayer: 'layered'
      },
      relativePath: 'exefs/main',
      sourceId: 'exefs-main',
      status: 'available'
    },
    stats: {
      chartCellCount: 18 * 18,
      outputFileCount: 1,
      sourceFileCount: 1
    },
    summary: {
      availability: 'available',
      description: 'Type Chart editor',
      diagnostics: [],
      id: 'typeChart',
      label: 'Type Chart'
    },
    types: []
  };
}

function createUninstallSession(recordId: string): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.typeChart',
        field: 'uninstall',
        newValue: 'true',
        recordId,
        sources: [
          { layer: 'generated', relativePath: 'exefs/main' },
          { layer: 'base', relativePath: 'exefs/main' }
        ],
        summary: 'Stage Type Chart uninstall.'
      }
    ],
    sessionId: 'session-type-chart-uninstall'
  };
}
