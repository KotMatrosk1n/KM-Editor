/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { RandomizerSection } from './RandomizerSection';

describe('RandomizerSection', () => {
  it('locks configuration and distinguishes recovery warnings, failures, and no changes', async () => {
    const user = userEvent.setup();
    const restoredPath = 'romfs/bin/pml/personal/totalpersonal.bin';
    const onRestoreRandomizer = vi
      .fn()
      .mockResolvedValueOnce({
        applyResult: {
          applyId: 'restore-warning',
          diagnostics: [
            {
              message: 'Preserved a tracked Randomizer file because it changed after Randomizer apply.',
              severity: 'warning'
            }
          ],
          writtenFiles: [restoredPath]
        }
      })
      .mockRejectedValueOnce(new Error('Restore callback failed.'))
      .mockResolvedValueOnce({
        applyResult: {
          applyId: 'restore-no-changes',
          diagnostics: [
            {
              message: 'No Randomizer restore manifest was found.',
              severity: 'info'
            }
          ],
          writtenFiles: []
        }
      });
    const props = {
      canApply: true,
      isApplying: true,
      onApplyRandomizer: vi.fn(),
      onImportSeed: vi.fn(),
      onRestoreRandomizer
    };
    const { rerender } = render(<RandomizerSection {...props} />);

    expect(screen.getByRole('textbox', { name: 'Base Seed' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Randomize Base Stats' })).toBeDisabled();
    expect(screen.getByRole('textbox', { name: 'Shared Randomization Seed' })).toBeDisabled();
    expect(
      screen.getByText('Optional text used only when generating a new roll. It is not a complete replay seed.')
    ).toBeVisible();
    expect(screen.getByText('Selected categories')).toBeVisible();

    rerender(<RandomizerSection {...props} isApplying={false} />);
    await user.click(screen.getByRole('button', { name: 'Restore Vanilla Values' }));

    expect(
      screen.getByText(/Later changes are kept and reported\./)
    ).toBeVisible();
    await user.click(screen.getByRole('button', { name: 'Confirm Restore' }));

    expect(onRestoreRandomizer).toHaveBeenCalledOnce();
    expect(
      await screen.findByRole('heading', { name: 'Restore needs attention' })
    ).toBeVisible();
    expect(screen.getByText(restoredPath)).toBeVisible();
    expect(
      screen.getByText(/Diagnostics identify later changes that were preserved/)
    ).toBeVisible();

    await user.click(screen.getByRole('button', { name: 'Restore Vanilla Values' }));
    await user.click(screen.getByRole('button', { name: 'Confirm Restore' }));

    expect(await screen.findByText('Restore callback failed.')).toBeVisible();
    expect(screen.queryByText(restoredPath)).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Restore needs attention' })
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Restore Vanilla Values' }));
    await user.click(screen.getByRole('button', { name: 'Confirm Restore' }));

    expect(onRestoreRandomizer).toHaveBeenCalledTimes(3);
    expect(await screen.findByRole('heading', { name: 'No changes' })).toBeVisible();
    expect(screen.getAllByText('No changes').length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText(restoredPath)).not.toBeInTheDocument();
  });
});
