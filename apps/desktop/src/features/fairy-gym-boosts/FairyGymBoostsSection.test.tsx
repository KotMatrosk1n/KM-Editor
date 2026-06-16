/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { FairyGymBoostsSection } from './FairyGymBoostsSection';
import { type FairyGymBoostSelection } from '../../bridge/fairyGymBoostsContracts';
import { createFairyGymBoostsWorkflowFixture } from '../../testSupport/fairyGymBoostsTestFixtures';

it('edits per-answer outcomes and stages guarded Fairy Gym boost selections', async () => {
  const user = userEvent.setup();
  const { fairyGymBoostsWorkflow } = createFairyGymBoostsWorkflowFixture(true);
  const onDirtyChange = vi.fn();
  const onStageBoosts = vi.fn();

  render(
    <FairyGymBoostsSection
      changePlan={null}
      editSession={null}
      isChangePlanApplying={false}
      isChangePlanCreating={false}
      isStaging={false}
      onApplyChangePlan={vi.fn()}
      onCreateChangePlan={vi.fn()}
      onDirtyChange={onDirtyChange}
      onStageBoosts={onStageBoosts}
      workflow={fairyGymBoostsWorkflow}
    />
  );

  expect(screen.getByRole('tab', { name: 'Annette' })).toBeInTheDocument();
  expect(screen.getByRole('tab', { name: 'Teresa' })).toBeInTheDocument();
  expect(screen.getByRole('tab', { name: 'Theodora' })).toBeInTheDocument();
  await user.click(screen.getByRole('tab', { name: 'Opal' }));

  const magicUserCard = screen.getByText('The magic-user').closest('article');
  const wizardCard = screen.getByText('The wizard').closest('article');
  expect(magicUserCard).not.toBeNull();
  expect(wizardCard).not.toBeNull();
  expect(within(magicUserCard!).getByText('Wrong answer')).toBeInTheDocument();
  expect(within(wizardCard!).getByText('Right answer')).toBeInTheDocument();

  await user.selectOptions(
    within(magicUserCard!).getByLabelText('The magic-user outcome'),
    '0:none'
  );
  await user.click(screen.getByRole('button', { name: /Stage Fairy Gym Boosts/i }));

  expect(onStageBoosts).toHaveBeenCalledTimes(1);
  const stagedSelections = onStageBoosts.mock.calls[0][0] as FairyGymBoostSelection[];
  expect(stagedSelections).toHaveLength(12);
  expect(
    stagedSelections.find(
      (selection: FairyGymBoostSelection) =>
        selection.boostId === 'opal-nickname-magic-user'
    )
  ).toEqual({
    boostId: 'opal-nickname-magic-user',
    effectId: 0,
    resultKind: 'none'
  });
  expect(
    stagedSelections.find(
      (selection: FairyGymBoostSelection) => selection.boostId === 'opal-nickname-wizard'
    )
  ).toEqual({
    boostId: 'opal-nickname-wizard',
    effectId: 6,
    resultKind: 'increase'
  });
});

it('restores Fairy Gym drafts to vanilla answer outcomes', async () => {
  const user = userEvent.setup();
  const fixture = createFairyGymBoostsWorkflowFixture(true);
  const fairyGymBoostsWorkflow = {
    ...fixture.fairyGymBoostsWorkflow,
    trainers: fixture.fairyGymBoostsWorkflow.trainers.map((trainer) => ({
      ...trainer,
      boosts: trainer.boosts.map((boost) =>
        boost.boostId === 'opal-nickname-magic-user'
          ? {
              ...boost,
              effectId: 0,
              resultKind: 'none' as const
            }
          : boost
      )
    }))
  };
  const onStageBoosts = vi.fn();

  render(
    <FairyGymBoostsSection
      changePlan={null}
      editSession={null}
      isChangePlanApplying={false}
      isChangePlanCreating={false}
      isStaging={false}
      onApplyChangePlan={vi.fn()}
      onCreateChangePlan={vi.fn()}
      onDirtyChange={vi.fn()}
      onStageBoosts={onStageBoosts}
      workflow={fairyGymBoostsWorkflow}
    />
  );

  await user.click(screen.getByRole('tab', { name: 'Opal' }));

  const magicUserCard = screen.getByText('The magic-user').closest('article');
  expect(magicUserCard).not.toBeNull();

  const outcomeSelect = within(magicUserCard!).getByLabelText('The magic-user outcome');
  expect(outcomeSelect).toHaveDisplayValue('No effect');

  const restoreButton = screen.getByRole('button', { name: 'Restore to Vanilla' });
  expect(restoreButton).toBeEnabled();
  await user.click(restoreButton);
  expect(outcomeSelect).toHaveDisplayValue('-2 Speed');

  await user.click(screen.getByRole('button', { name: /Stage Fairy Gym Boosts/i }));

  expect(onStageBoosts).toHaveBeenCalledTimes(1);
  const stagedSelections = onStageBoosts.mock.calls[0][0] as FairyGymBoostSelection[];
  expect(
    stagedSelections.find(
      (selection: FairyGymBoostSelection) =>
        selection.boostId === 'opal-nickname-magic-user'
    )
  ).toEqual({
    boostId: 'opal-nickname-magic-user',
    effectId: 6,
    resultKind: 'decrease'
  });
});
