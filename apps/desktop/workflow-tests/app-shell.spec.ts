/* SPDX-License-Identifier: GPL-3.0-only */

import { expect, test } from '@playwright/test';

test('loads the workbench shell and switches sections', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Which game are you using?' })).toBeVisible();
  await page.getByRole('button', { name: 'Pokemon Sword' }).click();

  await expect(page.getByText('KM Editor')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project Setup' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project Paths' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Health Summary' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Validate Paths' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Workflows' })).toHaveCount(0);

  await page.getByRole('button', { name: 'Changes', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Changes' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Changes', exact: true })).toHaveAttribute(
    'aria-current',
    'page'
  );

  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Settings', level: 1 })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Settings', exact: true })).toHaveAttribute(
    'aria-current',
    'page'
  );
});
