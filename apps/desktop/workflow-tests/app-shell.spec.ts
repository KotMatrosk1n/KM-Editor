/* SPDX-License-Identifier: GPL-3.0-only */

import { expect, test } from '@playwright/test';

test('loads the workbench shell and switches sections', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Which game are you using?' })).toBeVisible();
  await page.getByRole('button', { name: 'Pokemon Sword' }).click();

  await expect(page.getByLabel('Application navigation')).toBeVisible();
  await expect(page.locator('.brand-logo')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project Setup' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project Paths' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Health Summary' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Validate Paths' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Workflows' })).toBeVisible();

  await page.getByRole('button', { name: 'Changes', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Changes' })).toBeVisible();
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

test('keeps navigation controls reachable at constrained desktop sizes', async ({ page }) => {
  await page.setViewportSize({ width: 960, height: 540 });
  await page.goto('/');
  await page.getByRole('button', { name: 'Pokemon Sword' }).click();

  const sidebar = page.getByLabel('Application navigation');
  const expandSidebarButton = page.getByRole('button', { name: 'Expand sidebar' });
  await expect(expandSidebarButton).toBeVisible();
  await expect(page.getByRole('button', { name: 'Changes', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Settings', exact: true })).toBeVisible();

  const collapsedBox = await sidebar.boundingBox();
  expect(collapsedBox?.width).toBe(76);

  await expandSidebarButton.click();
  const expandedBox = await sidebar.boundingBox();
  expect(expandedBox?.width).toBe(300);
  const collapseSidebarButton = page.getByRole('button', { name: 'Collapse sidebar' });
  await expect(collapseSidebarButton).toBeVisible();

  const toggleBox = await collapseSidebarButton.boundingBox();
  expect(toggleBox?.width).toBeGreaterThanOrEqual(40);
  expect(toggleBox?.height).toBeGreaterThanOrEqual(40);
  expect((toggleBox?.x ?? 0) + (toggleBox?.width ?? 0)).toBeLessThanOrEqual(
    (expandedBox?.x ?? 0) + (expandedBox?.width ?? 0)
  );

  await collapseSidebarButton.click();
  for (const viewport of [
    { width: 1280, height: 800 },
    { width: 1366, height: 768 },
    { width: 1440, height: 900 },
    { width: 1600, height: 900 },
    { width: 1920, height: 1080 },
    { width: 1920, height: 1200 },
    { width: 2560, height: 1440 },
    { width: 2560, height: 1600 },
    { width: 3440, height: 1440 },
    { width: 3840, height: 2160 },
    { width: 3840, height: 2400 }
  ]) {
    await page.setViewportSize(viewport);
    const expectedSidebarWidth = viewport.width <= 1280 || viewport.height <= 720 ? 76 : 260;
    await expect.poll(async () => (await sidebar.boundingBox())?.width).toBe(expectedSidebarWidth);

    const geometry = await page.evaluate(() => {
      const shell = document.querySelector('.app-shell')?.getBoundingClientRect();
      const utilityNavigation = document
        .querySelector('.sidebar-utility-nav')
        ?.getBoundingClientRect();
      const toggle = document.querySelector('.sidebar-toggle')?.getBoundingClientRect();
      const sidebarBounds = document.querySelector('.sidebar')?.getBoundingClientRect();

      return {
        horizontalOverflow:
          document.documentElement.scrollWidth - document.documentElement.clientWidth,
        shellHeight: shell?.height ?? 0,
        sidebarRight: sidebarBounds?.right ?? 0,
        toggleHeight: toggle?.height ?? 0,
        toggleRight: toggle?.right ?? 0,
        toggleWidth: toggle?.width ?? 0,
        utilityBottom: utilityNavigation?.bottom ?? 0,
        viewportHeight: window.innerHeight
      };
    });

    expect(geometry.horizontalOverflow).toBe(0);
    expect(geometry.shellHeight).toBeCloseTo(geometry.viewportHeight, 0);
    expect(geometry.utilityBottom).toBeLessThanOrEqual(geometry.viewportHeight + 1);
    expect(geometry.toggleWidth).toBeGreaterThanOrEqual(40);
    expect(geometry.toggleHeight).toBeGreaterThanOrEqual(40);
    expect(geometry.toggleRight).toBeLessThanOrEqual(geometry.sidebarRight + 1);
  }
});
