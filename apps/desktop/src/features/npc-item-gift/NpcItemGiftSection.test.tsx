/* SPDX-License-Identifier: GPL-3.0-only */

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { type EditSession } from '../../bridge/contracts';
import {
  type NpcItemGiftSelection,
  type NpcItemGiftWorkflow
} from '../../bridge/npcItemGiftContracts';
import { type WorkflowPanelOutput } from '../../components/workflowPanels';
import { createNpcItemGiftWorkflowFixture } from '../../testSupport/npcItemGiftTestFixtures';
import {
  NpcItemGiftSection,
  decodeNpcItemGiftPendingSelections
} from './NpcItemGiftSection';

const emptyPanelOutput: WorkflowPanelOutput = {
  actionDiagnostics: [],
  applyResult: null,
  changePlan: null
};

function createNpcItemGiftEditSession(
  newValue: string,
  field = 'gifts',
  recordId = 'npc-item-gift'
): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.npcItemGift',
        field,
        newValue,
        recordId,
        sources: [],
        summary: 'Stage NPC Item Gift changes.'
      }
    ],
    sessionId: 'npc-item-gift-test-session'
  };
}

function getSoniaPendingValue() {
  return [
    'sonia-stow-on-side-revive|2|item=28',
    'sonia-slumbering-weald-max-revive|3|item=29'
  ].join(';');
}

describe('NpcItemGiftSection', () => {
  it('stages the selected Sonia gift rows with edited item and amount selections', async () => {
    const user = userEvent.setup();
    const onDirtyChange = vi.fn();
    const onStageGifts = vi.fn();

    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={onDirtyChange}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={createNpcItemGiftWorkflowFixture()}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const stowItemInput = screen.getByLabelText('Sonia (Stow-on-Side) Revive');
    await user.clear(stowItemInput);
    await user.type(stowItemInput, 'Rare Candy');

    const stowAmountInput = screen.getByLabelText('Sonia (Stow-on-Side) amount');
    await user.clear(stowAmountInput);
    await user.type(stowAmountInput, '7');

    await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(true));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));

    expect(onStageGifts).toHaveBeenCalledWith([
      {
        giftId: 'sonia-stow-on-side-revive',
        items: [{ itemId: 5, slotId: 'item' }],
        quantity: 7
      },
      {
        giftId: 'sonia-slumbering-weald-max-revive',
        items: [{ itemId: 29, slotId: 'item' }],
        quantity: 3
      }
    ] satisfies NpcItemGiftSelection[]);
  });

  it('reselects the staged NPC on reopen so its review action remains reachable', async () => {
    render(
      <NpcItemGiftSection
        editSession={createNpcItemGiftEditSession(getSoniaPendingValue())}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={createNpcItemGiftWorkflowFixture()}
      />
    );

    const soniaButton = within(
      await screen.findByRole('group', { name: 'Important Characters NPCs' })
    ).getByRole('button', { name: /Sonia/ });
    expect(soniaButton).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText('Sonia (Stow-on-Side) amount')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();
  });

  it('fails closed when the NPC pending edit identity is not exact', async () => {
    render(
      <NpcItemGiftSection
        editSession={createNpcItemGiftEditSession(getSoniaPendingValue(), 'other-field')}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={createNpcItemGiftWorkflowFixture()}
      />
    );

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The staged NPC Item Gift entry is invalid.'
    );
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeDisabled();
  });

  it('fails closed when a staged NPC payload is not in canonical workflow order', async () => {
    render(
      <NpcItemGiftSection
        editSession={createNpcItemGiftEditSession(
          getSoniaPendingValue().split(';').reverse().join(';')
        )}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={createNpcItemGiftWorkflowFixture()}
      />
    );

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The staged NPC Item Gift entry is invalid.'
    );
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
  });

  it.each([
    { applying: false, creating: false, staging: true },
    { applying: false, creating: true, staging: false },
    { applying: true, creating: false, staging: false }
  ])('disables review during overlapping workflow operations %#', async (busyState) => {
    render(
      <NpcItemGiftSection
        editSession={createNpcItemGiftEditSession(getSoniaPendingValue())}
        isChangePlanApplying={busyState.applying}
        isChangePlanCreating={busyState.creating}
        isStaging={busyState.staging}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={createNpcItemGiftWorkflowFixture()}
      />
    );

    expect(await screen.findByRole('button', { name: /Review/ })).toBeDisabled();
  });

  it.each(['', '0', '1000', '1.5', '1e2', '01', '-1'])(
    'rejects noncanonical quantity input %j without silently changing the staged value',
    async (value) => {
      const user = userEvent.setup();
      const onDirtyChange = vi.fn();
      const onStageGifts = vi.fn();
      render(
        <NpcItemGiftSection
          editSession={null}
          isChangePlanApplying={false}
          isChangePlanCreating={false}
          isStaging={false}
          onApplyChangePlan={vi.fn()}
          onCreateChangePlan={vi.fn()}
          onDirtyChange={onDirtyChange}
          onStageGifts={onStageGifts}
          panelOutput={emptyPanelOutput}
          workflow={createNpcItemGiftWorkflowFixture()}
        />
      );

      await user.click(screen.getByRole('button', { name: 'Sonia' }));
      const quantityInput = screen.getByLabelText('Sonia (Stow-on-Side) amount');
      fireEvent.change(quantityInput, { target: { value } });

      expect(quantityInput).toHaveValue(value);
      expect(quantityInput).toHaveAttribute('aria-invalid', 'true');
      expect(screen.getByText('Enter a whole number from 1 to 999.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeDisabled();
      await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(true));
      expect(onStageGifts).not.toHaveBeenCalled();
    }
  );

  it('stages the exact visible canonical quantity at both valid bounds', async () => {
    const user = userEvent.setup();
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={createNpcItemGiftWorkflowFixture()}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const quantityInput = screen.getByLabelText('Sonia (Stow-on-Side) amount');
    fireEvent.change(quantityInput, { target: { value: '999' } });
    expect(quantityInput).toHaveAttribute('aria-invalid', 'false');
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts.mock.calls[0]?.[0][0]?.quantity).toBe(999);

    fireEvent.change(quantityInput, { target: { value: '1' } });
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts.mock.calls[1]?.[0][0]?.quantity).toBe(1);
  });

  it('preserves an unchanged legacy quantity above 999 after the field is touched', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    const sonia = workflow.npcs.find((npc) => npc.npcId === 'sonia')!;
    sonia.gifts[0] = { ...sonia.gifts[0]!, quantity: 1001 };
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const quantityInput = screen.getByLabelText('Sonia (Stow-on-Side) amount');
    fireEvent.change(quantityInput, { target: { value: '1001' } });
    fireEvent.blur(quantityInput);
    const itemInput = screen.getByLabelText('Sonia (Stow-on-Side) Revive');
    await user.clear(itemInput);
    await user.type(itemInput, 'Rare Candy{Enter}');

    expect(quantityInput).toHaveValue('1001');
    expect(quantityInput).not.toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts.mock.calls[0]?.[0][0]).toEqual({
      giftId: 'sonia-stow-on-side-revive',
      items: [{ itemId: 5, slotId: 'item' }],
      quantity: 1001
    });
  });

  it('allows a repairable gift to be staged without a manual field change', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    const sonia = workflow.npcs.find((npc) => npc.npcId === 'sonia')!;
    sonia.gifts[0] = { ...sonia.gifts[0]!, status: 'repairable' };
    workflow.sources = workflow.sources.map((source) =>
      source.relativePath.endsWith('main_event_1110.amx')
        ? { ...source, status: 'repairable' }
        : source
    );
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts).toHaveBeenCalledWith([
      expect.objectContaining({ giftId: 'sonia-stow-on-side-revive' }),
      expect.objectContaining({ giftId: 'sonia-slumbering-weald-max-revive' })
    ]);
  });

  it('keeps a read-only workflow disabled even when it has no error diagnostic', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    workflow.summary = { ...workflow.summary, availability: 'readOnly' };
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    expect(screen.getByLabelText('Sonia (Stow-on-Side) amount')).toBeDisabled();
    expect(screen.getByLabelText('Sonia (Stow-on-Side) Revive')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeDisabled();
    expect(
      screen.getByText('NPC Item Gift apply requires valid base paths and a valid output root.')
    ).toBeInTheDocument();
  });

  it('allows a quantity-only edit when item metadata cannot be loaded', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    workflow.itemOptions = [];
    workflow.stats = { ...workflow.stats, itemOptionCount: 0 };
    workflow.diagnostics = [
      {
        domain: 'workflow.npcItemGift',
        file: 'romfs/bin/pml/item/item.dat',
        message: 'NPC Item Gift could not load any selectable item metadata.',
        severity: 'error'
      }
    ];
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    fireEvent.change(screen.getByLabelText('Sonia (Stow-on-Side) amount'), {
      target: { value: '7' }
    });
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts.mock.calls[0]?.[0][0]).toEqual({
      giftId: 'sonia-stow-on-side-revive',
      items: [{ itemId: 28, slotId: 'item' }],
      quantity: 7
    });
  });

  it('does not replace the current item when Enter is pressed without a user query', async () => {
    const user = userEvent.setup();
    const onDirtyChange = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={onDirtyChange}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={createNpcItemGiftWorkflowFixture()}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const itemInput = screen.getByLabelText('Sonia (Stow-on-Side) Revive');
    await user.click(itemInput);
    const selectedOption = screen.getByRole('option', { name: /Revive \(#28\)/ });
    expect(selectedOption).toHaveAttribute('aria-selected', 'true');
    expect(selectedOption).toHaveAttribute('tabindex', '-1');
    expect(
      screen.getByRole('button', { name: 'Show Sonia (Stow-on-Side) Revive options' })
    ).toHaveAttribute('tabindex', '-1');
    await user.keyboard('{Enter}');

    expect(itemInput).toHaveValue('Revive (#28)');
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeDisabled();
    await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(false));
  });

  it('requires a unique item name but still accepts an exact item ID', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    workflow.itemOptions.push(
      { category: 'Key Items', isKeyItem: true, itemId: 40, name: 'Rotom Bike' },
      { category: 'Key Items', isKeyItem: true, itemId: 41, name: 'Rotom Bike' }
    );
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const itemInput = screen.getByLabelText('Sonia (Stow-on-Side) Revive');
    await user.clear(itemInput);
    await user.type(itemInput, 'Rotom Bike');
    await user.keyboard('{Enter}');
    expect(itemInput).toHaveValue('Revive (#28)');

    await user.clear(itemInput);
    await user.type(itemInput, '41');
    await user.keyboard('{Enter}');
    expect(itemInput).toHaveValue('Rotom Bike (#41) [Key]');
    expect(screen.getByLabelText('Sonia (Stow-on-Side) amount')).toBeDisabled();
    expect(screen.getByText('Key item amount')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts.mock.calls[0]?.[0][0]?.items[0]?.itemId).toBe(41);
    expect(onStageGifts.mock.calls[0]?.[0][0]?.quantity).toBe(1);
  });

  it('gates editing on only the selected NPC sources and gift statuses', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    workflow.sources.push({
      label: 'unrelated_missing.amx',
      provenance: {
        fileState: 'baseOnly',
        sourceFile: 'romfs/bin/script/amx/rigel02_unrelated_missing.amx',
        sourceLayer: 'base'
      },
      relativePath: 'romfs/bin/script/amx/rigel02_unrelated_missing.amx',
      sourceId: 'unrelated-missing',
      status: 'missing'
    });
    const { rerender } = render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    expect(screen.getByLabelText('Sonia (Stow-on-Side) Revive')).toBeEnabled();

    const blockedWorkflow: NpcItemGiftWorkflow = {
      ...workflow,
      sources: workflow.sources.map((source) =>
        source.relativePath.endsWith('main_event_1110.amx')
          ? { ...source, status: 'missing' }
          : source
      )
    };
    rerender(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={blockedWorkflow}
      />
    );

    expect(screen.getByLabelText('Sonia (Stow-on-Side) Revive')).toBeDisabled();
    expect(screen.getByText('main_event_1110.amx is missing.')).toBeInTheDocument();
  });

  it('stages a safe dirty gift while preserving an unchanged damaged sibling payload', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    const sonia = workflow.npcs.find((npc) => npc.npcId === 'sonia')!;
    const safeGift = sonia.gifts[0]!;
    const damagedGift = sonia.gifts[1]!;
    sonia.gifts[1] = {
      ...damagedGift,
      items: damagedGift.items.map((item) => ({
        ...item,
        itemId: -1,
        itemName: 'Legacy packed item'
      })),
      provenance: safeGift.provenance,
      quantity: -5,
      relativePath: safeGift.relativePath,
      status: 'damaged'
    };
    workflow.sources = workflow.sources.map((source) =>
      source.relativePath.endsWith('main_event_1110.amx')
        ? { ...source, status: 'damaged' }
        : source
    );
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const safeQuantity = screen.getByLabelText('Sonia (Stow-on-Side) amount');
    const damagedQuantity = screen.getByLabelText('Sonia (Slumbering Weald) amount');
    expect(safeQuantity).toBeEnabled();
    expect(damagedQuantity).toBeDisabled();
    fireEvent.change(safeQuantity, { target: { value: '7' } });
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));

    expect(onStageGifts).toHaveBeenCalledWith([
      expect.objectContaining({ giftId: 'sonia-stow-on-side-revive', quantity: 7 }),
      {
        giftId: 'sonia-slumbering-weald-max-revive',
        items: [{ itemId: -1, slotId: 'item' }],
        quantity: -5
      }
    ]);
  });

  it('allows verified repairable gifts and sources to stage through normalization', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    const sonia = workflow.npcs.find((npc) => npc.npcId === 'sonia')!;
    sonia.gifts = sonia.gifts.map((gift) => ({ ...gift, status: 'repairable' }));
    workflow.sources = workflow.sources.map((source) =>
      source.relativePath.endsWith('main_event_1110.amx') ||
      source.relativePath.endsWith('main_event_1820.amx')
        ? { ...source, status: 'repairable' }
        : source
    );
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const quantityInput = screen.getByLabelText('Sonia (Stow-on-Side) amount');
    expect(quantityInput).toBeEnabled();
    fireEvent.change(quantityInput, { target: { value: '7' } });
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts).toHaveBeenCalledTimes(1);
  });

  it('lists Leon with important characters instead of gym leaders', () => {
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={createNpcItemGiftWorkflowFixture()}
      />
    );

    expect(
      within(screen.getByRole('group', { name: 'Important Characters NPCs' })).getByRole(
        'button',
        { name: 'Leon' }
      )
    ).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Gym Leaders NPCs' })).toBeNull();
  });

  it('does not duplicate NPCs in the accessible main-game group name', () => {
    const workflow = createNpcItemGiftWorkflowFixture();
    const template = workflow.npcs.find((npc) => npc.npcId === 'mum')!;
    workflow.npcs.push({
      ...template,
      gifts: template.gifts.map((gift) => ({
        ...gift,
        giftId: `route-npc-${gift.giftId}`,
        npcId: 'route-npc',
        npcName: 'Route NPC'
      })),
      npcId: 'route-npc',
      npcName: 'Route NPC'
    });

    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={vi.fn()}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    expect(screen.getByRole('group', { name: 'Main Game NPCs' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Main Game NPCs NPCs' })).toBeNull();
  });

  it('splits a shared DLC scientist into the correct expansion categories and stage scope', async () => {
    const user = userEvent.setup();
    const baseWorkflow = createNpcItemGiftWorkflowFixture();
    const templateGift = baseWorkflow.npcs.find((npc) => npc.npcId === 'sonia')!.gifts[0]!;
    const islePath = 'romfs/bin/script/amx/rigel01_pokedex_scientist.amx';
    const crownPath = 'romfs/bin/script/amx/rigel02_pokedex_scientist.amx';
    const workflow: NpcItemGiftWorkflow = {
      ...baseWorkflow,
      npcs: [
        {
          displayOrder: 400,
          gifts: [
            {
              ...templateGift,
              displayOrder: 400,
              giftId: 'pokedex-scientist-isle',
              label: 'Isle Dex Reward',
              npcId: 'pokedex-scientist',
              npcName: 'Pokedex Scientist',
              provenance: { ...templateGift.provenance, sourceFile: islePath },
              relativePath: islePath
            },
            {
              ...templateGift,
              displayOrder: 500,
              giftId: 'pokedex-scientist-crown',
              label: 'Crown Dex Reward',
              npcId: 'pokedex-scientist',
              npcName: 'Pokedex Scientist',
              provenance: { ...templateGift.provenance, sourceFile: crownPath },
              relativePath: crownPath
            }
          ],
          npcId: 'pokedex-scientist',
          npcName: 'Pokedex Scientist'
        }
      ],
      sources: [
        {
          label: 'Isle scientist',
          provenance: { ...templateGift.provenance, sourceFile: islePath },
          relativePath: islePath,
          sourceId: 'isle-scientist',
          status: 'available'
        },
        {
          label: 'Crown scientist',
          provenance: { ...templateGift.provenance, sourceFile: crownPath },
          relativePath: crownPath,
          sourceId: 'crown-scientist',
          status: 'available'
        }
      ],
      stats: { ...baseWorkflow.stats, giftCount: 2, npcCount: 1, sourceFileCount: 2 }
    };
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    const isleGroup = screen.getByRole('group', { name: 'Isle of Armor NPCs' });
    const crownGroup = screen.getByRole('group', { name: 'Crown Tundra NPCs' });
    expect(within(isleGroup).getByRole('button', { name: 'Pokedex Scientist' })).toBeVisible();
    expect(within(crownGroup).getByRole('button', { name: 'Pokedex Scientist' })).toBeVisible();
    await user.click(within(isleGroup).getByRole('button', { name: 'Pokedex Scientist' }));
    expect(screen.getByRole('heading', { name: 'Isle Dex Reward' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Crown Dex Reward' })).toBeNull();

    const itemInput = screen.getByLabelText('Isle Dex Reward Revive');
    await user.clear(itemInput);
    await user.type(itemInput, '5');
    await user.keyboard('{Enter}');
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts).toHaveBeenCalledWith([
      expect.objectContaining({ giftId: 'pokedex-scientist-isle' })
    ]);
  });

  it('keeps backend-declared fixed quantities disabled while allowing item edits', async () => {
    const user = userEvent.setup();
    const workflow = createNpcItemGiftWorkflowFixture();
    const sonia = workflow.npcs.find((npc) => npc.npcId === 'sonia')!;
    sonia.gifts[0] = { ...sonia.gifts[0]!, canEditQuantity: false, quantityCell: null };
    const onStageGifts = vi.fn();
    render(
      <NpcItemGiftSection
        editSession={null}
        isChangePlanApplying={false}
        isChangePlanCreating={false}
        isStaging={false}
        onApplyChangePlan={vi.fn()}
        onCreateChangePlan={vi.fn()}
        onDirtyChange={vi.fn()}
        onStageGifts={onStageGifts}
        panelOutput={emptyPanelOutput}
        workflow={workflow}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    expect(screen.getByLabelText('Sonia (Stow-on-Side) amount')).toBeDisabled();
    expect(screen.getByText('Fixed amount')).toBeInTheDocument();
    const itemInput = screen.getByLabelText('Sonia (Stow-on-Side) Revive');
    await user.clear(itemInput);
    await user.type(itemInput, '1074');
    await user.keyboard('{Enter}');
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    expect(onStageGifts.mock.calls[0]?.[0][0]?.quantity).toBe(2);
  });

  it.each([
    '',
    'gift|1|item=1;',
    ';gift|1|item=1',
    'gift|1.5|item=1',
    'gift|1|item=2junk',
    'gift|01|item=1',
    'gift|-0|item=1',
    'gift|1|item=01',
    'gift|1|item=1,item=2',
    'gift|1|item=1|extra',
    'gift|1|item=1;gift|2|item=2'
  ])('rejects malformed pending payload %j', (value) => {
    expect(decodeNpcItemGiftPendingSelections(value)).toBeNull();
  });

  it('decodes canonical signed legacy values from unchanged staged siblings', () => {
    expect(decodeNpcItemGiftPendingSelections('legacy|1000|item=-1,other=0')).toEqual([
      {
        giftId: 'legacy',
        items: [
          { itemId: -1, slotId: 'item' },
          { itemId: 0, slotId: 'other' }
        ],
        quantity: 1000
      }
    ]);
  });

  it('decodes a canonical multi-gift pending payload', () => {
    expect(decodeNpcItemGiftPendingSelections(getSoniaPendingValue())).toEqual([
      {
        giftId: 'sonia-stow-on-side-revive',
        items: [{ itemId: 28, slotId: 'item' }],
        quantity: 2
      },
      {
        giftId: 'sonia-slumbering-weald-max-revive',
        items: [{ itemId: 29, slotId: 'item' }],
        quantity: 3
      }
    ]);
  });
});
