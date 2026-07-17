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
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto('/');
  await page.getByRole('button', { name: 'Pokemon Sword' }).click();

  const sidebar = page.getByLabel('Application navigation');
  const workspace = page.locator('.workspace');
  const expandSidebarButton = page.getByRole('button', { name: 'Expand sidebar' });
  await expect(expandSidebarButton).toBeVisible();
  await expect(page.getByRole('button', { name: 'Changes', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Settings', exact: true })).toBeVisible();

  const collapsedBox = await sidebar.boundingBox();
  const collapsedWorkspaceBox = await workspace.boundingBox();
  expect(collapsedBox?.width).toBe(76);
  expect(collapsedWorkspaceBox?.x).toBe(76);
  expect(collapsedWorkspaceBox?.width).toBe(1204);

  await expandSidebarButton.click();
  const expandedBox = await sidebar.boundingBox();
  const expandedWorkspaceBox = await workspace.boundingBox();
  expect(expandedBox?.width).toBe(300);
  expect(expandedWorkspaceBox?.x).toBe(collapsedWorkspaceBox?.x);
  expect(expandedWorkspaceBox?.width).toBe(collapsedWorkspaceBox?.width);
  await expect(page.locator('.sidebar-scrim')).toBeVisible();
  await expect(workspace).toHaveAttribute('inert');
  await expect(page.getByRole('heading', { name: 'Project Setup' })).toBeVisible();
  const collapseSidebarButton = page.getByRole('button', { name: 'Collapse sidebar' });
  await expect(collapseSidebarButton).toBeVisible();

  const toggleBox = await collapseSidebarButton.boundingBox();
  expect(toggleBox?.width).toBeGreaterThanOrEqual(40);
  expect(toggleBox?.height).toBeGreaterThanOrEqual(40);
  expect((toggleBox?.x ?? 0) + (toggleBox?.width ?? 0)).toBeLessThanOrEqual(
    (expandedBox?.x ?? 0) + (expandedBox?.width ?? 0)
  );

  await collapseSidebarButton.click();
  await expect(page.locator('.sidebar-scrim')).toBeHidden();
  await expect(workspace).not.toHaveAttribute('inert');
  const restoredWorkspaceBox = await workspace.boundingBox();
  expect(restoredWorkspaceBox?.x).toBe(collapsedWorkspaceBox?.x);
  expect(restoredWorkspaceBox?.width).toBe(collapsedWorkspaceBox?.width);
  for (const viewport of [
    { width: 960, height: 540 },
    { width: 1024, height: 640 },
    { width: 1280, height: 720 },
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
      const brand = document.querySelector('.brand');
      const utilityNavigation = document
        .querySelector('.sidebar-utility-nav')
        ?.getBoundingClientRect();
      const navigation = document.querySelector('.section-nav')?.getBoundingClientRect();
      const toggle = document.querySelector('.sidebar-toggle')?.getBoundingClientRect();
      const sidebarBounds = document.querySelector('.sidebar')?.getBoundingClientRect();

      return {
        brandMinHeight: brand ? Number.parseFloat(getComputedStyle(brand).minHeight) : 0,
        horizontalOverflow:
          document.documentElement.scrollWidth - document.documentElement.clientWidth,
        navigationTop: navigation?.top ?? 0,
        shellHeight: shell?.height ?? 0,
        sidebarRight: sidebarBounds?.right ?? 0,
        toggleBottom: toggle?.bottom ?? 0,
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
    if (expectedSidebarWidth === 76) {
      expect(geometry.brandMinHeight).toBe(92);
      expect(geometry.navigationTop).toBeGreaterThanOrEqual(geometry.toggleBottom);
    }
  }
});

test('reserves compact header space at 150 percent display scaling', async ({ browser }) => {
  const context = await browser.newContext({
    baseURL: 'http://127.0.0.1:5173',
    deviceScaleFactor: 1.5,
    viewport: { width: 1024, height: 640 }
  });
  const page = await context.newPage();

  await page.goto('/');
  await page.getByRole('button', { name: 'Pokemon Sword' }).click();
  await expect(page.getByRole('button', { name: 'Expand sidebar' })).toBeVisible();

  const geometry = await page.evaluate(() => {
    const brand = document.querySelector('.brand');
    const navigation = document.querySelector('.section-nav')?.getBoundingClientRect();
    const toggle = document.querySelector('.sidebar-toggle')?.getBoundingClientRect();

    return {
      brandMinHeight: brand ? Number.parseFloat(getComputedStyle(brand).minHeight) : 0,
      navigationTop: navigation?.top ?? 0,
      toggleBottom: toggle?.bottom ?? 0
    };
  });

  expect(geometry.brandMinHeight).toBe(92);
  expect(geometry.navigationTop).toBeGreaterThanOrEqual(geometry.toggleBottom);
  await context.close();
});
