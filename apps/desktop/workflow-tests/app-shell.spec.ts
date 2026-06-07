/* SPDX-License-Identifier: GPL-3.0-only */

import { expect, test } from '@playwright/test';

test('loads the workbench shell and switches sections', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByText('KM Editor')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project Health' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project Paths' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Open Project' }).first()).toBeVisible();

  await page.getByRole('button', { name: 'Workflows' }).click();

  await expect(page.getByRole('heading', { name: 'Workflows' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Workflows' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('heading', { name: 'Items' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Text and Dialogue Map' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Trainers' })).toBeVisible();
  await expect(page.getByText('Disabled', { exact: true })).toHaveCount(3);
});
