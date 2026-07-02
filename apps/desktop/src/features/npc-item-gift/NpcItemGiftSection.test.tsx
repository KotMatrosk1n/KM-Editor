/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { type NpcItemGiftSelection } from '../../bridge/npcItemGiftContracts';
import { type WorkflowPanelOutput } from '../../components/workflowPanels';
import { createNpcItemGiftWorkflowFixture } from '../../testSupport/npcItemGiftTestFixtures';
import { NpcItemGiftSection } from './NpcItemGiftSection';

const emptyPanelOutput: WorkflowPanelOutput = {
  actionDiagnostics: [],
  applyResult: null,
  changePlan: null
};

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

    await user.click(screen.getByRole('tab', { name: 'Sonia' }));
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
});
