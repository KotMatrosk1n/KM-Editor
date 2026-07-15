/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import {
  type ApiDiagnostic,
  type BehaviorWorkflow,
  type EditSession
} from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { languageStorageKey, LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

const editSession: EditSession = {
  hasPendingChanges: true,
  pendingEdits: [
    {
      domain: 'workflow.items',
      field: 'buyPrice',
      newValue: '500',
      recordId: '1',
      sources: [{ layer: 'base', relativePath: 'romfs/bin/pml/item/item.dat' }],
      summary: 'Set Potion buy price to 500.'
    }
  ],
  sessionId: 'behavior-ui-session'
};

async function createBehaviorHarness(
  mutateWorkflow?: (workflow: BehaviorWorkflow) => BehaviorWorkflow
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadBehaviorWorkflow({ paths: projectPaths });
  const workflow = mutateWorkflow?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge({}, true);

  useWorkbenchStore.setState({
    activeSection: 'behavior',
    behaviorSearchText: '',
    behaviorWorkflow: workflow,
    draftPaths: projectPaths,
    editSession,
    editValidationDiagnostics: [],
    selectedBehaviorEntryId: workflow.entries[0]?.entryId ?? null
  });

  return { bridge, workflow };
}

function renderBehavior(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

function getPendingChangesMetric() {
  return screen.getByText('Pending changes').closest('.metric') as HTMLElement;
}

describe('Behavior UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('stages all changed fields atomically while preserving search and scoped pending counts', async () => {
    const user = userEvent.setup();
    const { bridge } = await createBehaviorHarness();
    const singularUpdate = vi.spyOn(bridge, 'updateBehaviorEntryField');
    const updateBehaviorEntryFields = vi.spyOn(bridge, 'updateBehaviorEntryFields');
    act(() => useWorkbenchStore.getState().setBehaviorSearchText('Pikachu'));
    renderBehavior(bridge);

    expect(within(getPendingChangesMetric()).getByText('0')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
    const modelAnchorInput = await screen.findByLabelText('Model Anchor');
    await user.selectOptions(modelAnchorInput, 'head');
    const hitboxRadiusInput = screen.getByLabelText('Hitbox Radius');
    await user.clear(hitboxRadiusInput);
    await user.type(hitboxRadiusInput, '2.5');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateBehaviorEntryFields).toHaveBeenCalledTimes(1));
    expect(singularUpdate).not.toHaveBeenCalled();
    expect(updateBehaviorEntryFields.mock.calls[0]?.[0].updates).toEqual([
      { entryId: '0', field: 'modelPart', value: 'head' },
      { entryId: '0', field: 'hitboxRadius', value: '2.5' }
    ]);
    expect(screen.getByRole('searchbox', { name: 'Search behavior' })).toHaveValue('Pikachu');
    await waitFor(() =>
      expect(within(getPendingChangesMetric()).getByText('2')).toBeInTheDocument()
    );
    expect(
      useWorkbenchStore
        .getState()
        .editSession?.pendingEdits.filter((edit) => edit.domain === 'workflow.behavior')
    ).toHaveLength(2);
  });

  it('retains every draft and the incoming session when an atomic batch is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.behavior',
      field: 'hitboxRadius',
      message: 'Behavior changes were not staged.',
      severity: 'error'
    };
    const harness = await createBehaviorHarness();
    const rejectedSession: EditSession = {
      hasPendingChanges: true,
      pendingEdits: [
        ...editSession.pendingEdits,
        {
          domain: 'workflow.behavior',
          field: 'modelPart',
          newValue: 'head',
          recordId: '0',
          sources: [
            {
              layer: 'base',
              relativePath:
                'romfs/bin/field/param/symbol_encount_mons_param/symbol_encount_mons_param.bin'
            }
          ],
          summary: 'Partially staged model anchor.'
        }
      ],
      sessionId: editSession.sessionId
    };
    const updateBehaviorEntryFields = vi.fn(
      async (): ReturnType<ProjectBridge['updateBehaviorEntryFields']> => ({
        diagnostics: [rejection],
        session: rejectedSession,
        workflow: harness.workflow
      })
    );
    harness.bridge.updateBehaviorEntryFields = updateBehaviorEntryFields;
    renderBehavior(harness.bridge);

    const modelAnchorInput = await screen.findByLabelText('Model Anchor');
    await user.selectOptions(modelAnchorInput, 'head');
    const hitboxRadiusInput = screen.getByLabelText('Hitbox Radius');
    await user.clear(hitboxRadiusInput);
    await user.type(hitboxRadiusInput, '2.5');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateBehaviorEntryFields).toHaveBeenCalledTimes(1));
    expect(modelAnchorInput).toHaveValue('head');
    expect(hitboxRadiusInput).toHaveValue(2.5);
    expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled();
    expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([rejection]);
    expect(useWorkbenchStore.getState().editSession).toEqual(editSession);
    expect(within(getPendingChangesMetric()).getByText('0')).toBeInTheDocument();
  });

  it('uses entry form metadata while keeping coordinated species and form edits possible', async () => {
    const user = userEvent.setup();
    const { bridge } = await createBehaviorHarness();
    const updateBehaviorEntryFields = vi.spyOn(bridge, 'updateBehaviorEntryFields');
    renderBehavior(bridge);

    const initialFormControl = await screen.findByLabelText('Form');
    expect(initialFormControl.tagName).toBe('SELECT');
    expect(within(initialFormControl).getByRole('option', { name: 'Original Cap' })).toHaveValue(
      '1'
    );

    await user.selectOptions(screen.getByLabelText('Species'), '133');
    const targetFormControl = screen.getByLabelText('Form');
    expect(targetFormControl).toHaveAttribute('type', 'number');
    await user.clear(targetFormControl);
    await user.type(targetFormControl, '1');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateBehaviorEntryFields).toHaveBeenCalledTimes(1));
    expect(updateBehaviorEntryFields.mock.calls[0]?.[0].updates).toEqual([
      { entryId: '0', field: 'speciesId', value: '133' },
      { entryId: '0', field: 'form', value: '1' }
    ]);
  });

  it('invalidates a custom form when the species draft returns to the original species', async () => {
    const user = userEvent.setup();
    const { bridge } = await createBehaviorHarness();
    const updateBehaviorEntryFields = vi.spyOn(bridge, 'updateBehaviorEntryFields');
    renderBehavior(bridge);

    const speciesControl = await screen.findByLabelText('Species');
    await user.selectOptions(speciesControl, '133');
    const customFormControl = screen.getByLabelText('Form');
    expect(customFormControl).toHaveAttribute('type', 'number');
    await user.clear(customFormControl);
    await user.type(customFormControl, '2');
    expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled();

    await user.selectOptions(speciesControl, '25');
    const revertedFormControl = screen.getByLabelText('Form');
    expect(revertedFormControl.tagName).toBe('SELECT');
    expect(revertedFormControl).toHaveValue('2');
    expect(revertedFormControl.closest('label')).toHaveClass('editable-field-invalid');
    expect(screen.getByText('Choose one of the available options.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
    expect(updateBehaviorEntryFields).not.toHaveBeenCalled();
  });

  it('rejects negative collision radius drafts before staging', async () => {
    const user = userEvent.setup();
    const { bridge } = await createBehaviorHarness();
    const updateBehaviorEntryFields = vi.spyOn(bridge, 'updateBehaviorEntryFields');
    renderBehavior(bridge);

    const hitboxRadiusInput = await screen.findByLabelText('Hitbox Radius');
    const grassShakeRadiusInput = screen.getByLabelText('Grass Shake Radius');
    expect(hitboxRadiusInput).toHaveAttribute('min', '0');
    expect(grassShakeRadiusInput).toHaveAttribute('min', '0');
    await user.clear(hitboxRadiusInput);
    await user.type(hitboxRadiusInput, '-1');
    await user.clear(grassShakeRadiusInput);
    await user.type(grassShakeRadiusInput, '-0.5');

    expect(hitboxRadiusInput.closest('label')).toHaveClass('editable-field-invalid');
    expect(grassShakeRadiusInput.closest('label')).toHaveClass('editable-field-invalid');
    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
    expect(updateBehaviorEntryFields).not.toHaveBeenCalled();
  });

  it('searches raw and localized details, selects only filtered rows, and localizes no matches', async () => {
    window.localStorage.setItem(languageStorageKey, 'es');
    const user = userEvent.setup();
    const { bridge } = await createBehaviorHarness((workflow) => ({
      ...workflow,
      entries: workflow.entries.map((entry) =>
        entry.entryId === '1'
          ? {
              ...entry,
              fields: entry.fields.map((field) =>
                field.field === 'modelPart' || field.field === 'internalSpeciesName'
                  ? { ...field, value: 'Common' }
                  : field
              ),
              internalSpeciesName: 'Common',
              modelPart: 'Common',
              speciesName: 'Common'
            }
          : entry
      )
    }));
    renderBehavior(bridge);

    const search = await screen.findByRole('searchbox', { name: 'Buscar comportamiento' });
    const table = screen.getByRole('table', { name: 'Entradas de comportamiento' });
    await user.type(search, '0000000000000004');

    const rawDetailMatch = within(table).getByRole('row', { name: /se mueve hacia/ });
    await waitFor(() => expect(rawDetailMatch).toHaveAttribute('aria-selected', 'true'));
    const cells = within(rawDetailMatch).getAllByRole('cell');
    expect(cells[0]).toHaveTextContent(/^Common$/);
    expect(cells[2]).toHaveTextContent(/^Common$/);
    const inspector = screen.getByLabelText(
      'Procedencia de la entrada de comportamiento seleccionada'
    );
    const internalName = within(inspector).getByText('Nombre interno').parentElement!;
    expect(internalName).toHaveTextContent('Common');
    expect(internalName).not.toHaveTextContent('Común');

    await user.clear(search);
    await user.type(search, 'persigue');
    expect(within(table).getByRole('row', { name: /se mueve hacia/ })).toHaveAttribute(
      'aria-selected',
      'true'
    );
    expect(within(table).queryByRole('row', { name: /Pikachu/ })).toBeNull();

    await user.clear(search);
    await user.type(search, 'sin coincidencias');
    expect(screen.getByRole('status')).toHaveTextContent(
      'No hay entradas de comportamiento coincidentes.'
    );
    expect(within(table).queryByRole('row', { selected: true })).toBeNull();
    expect(screen.getByText('No hay entrada de comportamiento seleccionada.')).toBeInTheDocument();
  });
});
