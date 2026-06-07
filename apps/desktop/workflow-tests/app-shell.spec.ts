/* SPDX-License-Identifier: GPL-3.0-only */

import { expect, test } from '@playwright/test';

test('loads the workbench shell and switches sections', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByText('KM Editor')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project Health' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project Paths' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Open Project' }).first()).toBeVisible();

  await page.getByLabel('Base RomFS').fill('base-romfs');
  await page.getByLabel('Base ExeFS').fill('base-exefs');
  await page.getByRole('button', { name: 'Open Project' }).last().click();

  await expect(page.getByText('Read-only ready').first()).toBeVisible();

  await page.getByRole('button', { name: 'Workflows' }).click();

  await expect(page.getByRole('heading', { name: 'Workflows' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Workflows' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByText('Items')).toBeVisible();
  await expect(page.getByText('Read-only', { exact: true })).toBeVisible();
});
