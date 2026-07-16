/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { type FairyGymBoostSelection } from '../../bridge/fairyGymBoostsContracts';
import { createFairyGymBoostsWorkflowFixture } from '../../testSupport/fairyGymBoostsTestFixtures';
import { FairyGymBoostsSection } from './FairyGymBoostsSection';

it('edits per-answer outcomes and stages guarded Fairy Gym boost selections', async () => {
  const user = userEvent.setup();
  const { fairyGymBoostsWorkflow } = createFairyGymBoostsWorkflowFixture(true);
  const onStageBoosts = vi.fn();

  render(
    <FairyGymBoostsSection
      editSession={null}
      isChangePlanApplying={false}
      isChangePlanCreating={false}
      isStaging={false}
      onApplyChangePlan={vi.fn()}
      onCreateChangePlan={vi.fn()}
      onDirtyChange={vi.fn()}
      onStageBoosts={onStageBoosts}
      panelOutput={{ actionDiagnostics: [], applyResult: null, changePlan: null }}
      workflow={fairyGymBoostsWorkflow}
    />
  );

  const annetteTab = screen.getByRole('tab', { name: 'Annette' });
  const teresaTab = screen.getByRole('tab', { name: 'Teresa' });
  const tabList = screen.getByRole('tablist', { name: 'Fairy Gym Boosts' });
  expect(tabList).toBeInTheDocument();
  expect(annetteTab).toHaveAttribute('aria-controls', 'fairy-gym-boost-panel');
  expect(teresaTab).toHaveAttribute('aria-controls', 'fairy-gym-boost-panel');
  annetteTab.focus();
  await user.keyboard('{ArrowRight}');
  expect(teresaTab).toHaveFocus();
  expect(teresaTab).toHaveAttribute('aria-selected', 'true');
  expect(screen.getByRole('tabpanel')).toHaveAttribute(
    'id',
    'fairy-gym-boost-panel'
  );
  expect(screen.getByRole('tabpanel')).toHaveAttribute(
    'aria-labelledby',
    teresaTab.id
  );

  await user.click(screen.getByRole('tab', { name: 'Opal' }));

  const magicUserCard = screen.getByText('The magic-user').closest('article');
  expect(magicUserCard).not.toBeNull();

  await user.selectOptions(
    within(magicUserCard!).getByLabelText('The magic-user outcome'),
    '0:none'
  );
  await user.click(screen.getByRole('button', { name: /Stage Fairy Gym Boosts/i }));

  expect(onStageBoosts).toHaveBeenCalledTimes(1);
  const stagedSelections = onStageBoosts.mock.calls[0][0] as FairyGymBoostSelection[];
  expect(
    stagedSelections.find((selection) => selection.boostId === 'opal-nickname-magic-user')
  ).toEqual({
    boostId: 'opal-nickname-magic-user',
    effectId: 0,
    resultKind: 'none'
  });
});
