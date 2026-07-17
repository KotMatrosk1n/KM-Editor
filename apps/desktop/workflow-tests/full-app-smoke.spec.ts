/* SPDX-License-Identifier: GPL-3.0-only */

import { expect, type Locator, type Page, test } from '@playwright/test';
import { kmCommandNames } from '../src/bridge/contracts';
import { type ProjectBridge } from '../src/bridge/projectBridge';
import { createMockProjectBridge } from '../src/testSupport/appTestFixtures';

const gameSmokeCases = [
  {
    label: 'Pokemon Sword',
    expectedMinimumWorkflowCount: 20
  },
  {
    label: 'Pokemon Scarlet',
    expectedMinimumWorkflowCount: 14
  },
  {
    label: 'Pokemon Legends Z-A',
    expectedMinimumWorkflowCount: 11
  }
] as const;

test.describe('full app smoke pass', () => {
  for (const smokeCase of gameSmokeCases) {
    test(`opens every visible workflow for ${smokeCase.label}`, async ({ page }) => {
      const runtimeIssues = await installMockRuntime(
        page,
        smokeCase.label === 'Pokemon Sword'
          ? await createCanonicalDynamaxAdventureBridge()
          : undefined
      );

      await page.goto('/');
      await expect(page.getByRole('heading', { name: 'Which game are you using?' })).toBeVisible();
      await page.getByRole('button', { name: smokeCase.label }).click();
      await expect(page.getByRole('heading', { level: 1, name: 'Project Setup' })).toBeVisible();

      await fillProjectPathInputs(page);
      await page.getByRole('button', { name: 'Validate Paths' }).click();
      await expect(page.locator('.nav-group-button').first()).toBeVisible();
      await assertNoRuntimeIssues(page, runtimeIssues);

      const workflowLabels = await collectVisibleWorkflowLabels(page);
      expect(workflowLabels.length).toBeGreaterThanOrEqual(smokeCase.expectedMinimumWorkflowCount);

      for (const workflowLabel of workflowLabels) {
        await openWorkflow(page, workflowLabel);
        await assertNoRuntimeIssues(page, runtimeIssues);
      }

      await assertActiveWorkflowNavigationVisible(page);
      await page.getByRole('button', { name: 'Expand sidebar' }).click();
      await assertActiveWorkflowNavigationVisible(page);

      await openPrimarySection(page, 'Project Setup');
      await assertNoRuntimeIssues(page, runtimeIssues);
      await openPrimarySection(page, 'Changes');
      await assertNoRuntimeIssues(page, runtimeIssues);
      await openPrimarySection(page, 'Settings');
      await assertNoRuntimeIssues(page, runtimeIssues);
    });
  }

  test('keeps Pokemon learnset inline controls inside the row at scaled desktop sizes', async ({ page }) => {
    const runtimeIssues = await installMockRuntime(page);
    await page.setViewportSize({ width: 1024, height: 640 });

    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Which game are you using?' })).toBeVisible();
    await page.getByRole('button', { name: 'Pokemon Sword' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Project Setup' })).toBeVisible();

    await fillProjectPathInputs(page);
    await page.getByRole('button', { name: 'Validate Paths' }).click();
    await page.getByRole('button', { exact: true, name: 'Editors' }).click();
    await page.getByRole('button', { exact: true, name: 'Pokemon' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Pokemon' })).toBeVisible();
    await page.getByRole('button', { exact: true, name: 'Edit' }).click();

    const inlineRow = page.locator('.learnset-inline-row').first();
    await expect(inlineRow).toBeVisible();

    for (const viewport of [
      { width: 1024, height: 640 },
      { width: 1280, height: 800 },
      { width: 2560, height: 1600 }
    ]) {
      await page.setViewportSize(viewport);
      await expect(inlineRow).toBeVisible();
      await assertLearnsetInlineControlsStayInRow(page, inlineRow);
    }

    await assertNoRuntimeIssues(page, runtimeIssues);
  });

  test('keeps Dynamax Adventures browsing and editing balanced at desktop sizes', async ({ page }) => {
    const runtimeIssues = await installMockRuntime(
      page,
      await createCanonicalDynamaxAdventureBridge()
    );
    await page.setViewportSize({ width: 2560, height: 1600 });

    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Which game are you using?' })).toBeVisible();
    await page.getByRole('button', { name: 'Pokemon Sword' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Project Setup' })).toBeVisible();

    await fillProjectPathInputs(page);
    await page.getByRole('button', { name: 'Validate Paths' }).click();
    await page.getByRole('button', { exact: true, name: 'Advanced Editors' }).click();
    await openWorkflow(page, 'Dynamax Adventures');

    await expect(page.locator('.dynamax-adventures-layout')).toBeVisible();
    await expect(page.locator('.dynamax-adventure-technical-details')).not.toHaveAttribute(
      'open'
    );

    const wideGeometry = await readDynamaxAdventureGeometry(page);
    expect(wideGeometry.documentScrollWidth).toBeLessThanOrEqual(
      wideGeometry.documentClientWidth + 1
    );
    expect(Math.abs(wideGeometry.table.top - wideGeometry.summary.top)).toBeLessThanOrEqual(1);
    expect(wideGeometry.editor.top).toBeGreaterThanOrEqual(
      Math.max(wideGeometry.table.bottom, wideGeometry.summary.bottom) - 1
    );
    expect(Math.abs(wideGeometry.table.left - wideGeometry.editor.left)).toBeLessThanOrEqual(1);
    expect(Math.abs(wideGeometry.summary.right - wideGeometry.editor.right)).toBeLessThanOrEqual(1);
    expect(wideGeometry.fieldGroupColumns).toBe(4);

    await page.setViewportSize({ width: 1024, height: 800 });
    const compactGeometry = await readDynamaxAdventureGeometry(page);
    expect(compactGeometry.documentScrollWidth).toBeLessThanOrEqual(
      compactGeometry.documentClientWidth + 1
    );
    expect(compactGeometry.summary.top).toBeGreaterThanOrEqual(
      compactGeometry.table.bottom - 1
    );
    expect(compactGeometry.editor.top).toBeGreaterThanOrEqual(
      compactGeometry.summary.bottom - 1
    );
    expect(Math.abs(compactGeometry.table.left - compactGeometry.summary.left)).toBeLessThanOrEqual(
      1
    );
    expect(Math.abs(compactGeometry.table.left - compactGeometry.editor.left)).toBeLessThanOrEqual(
      1
    );
    expect(Math.abs(compactGeometry.table.right - compactGeometry.summary.right)).toBeLessThanOrEqual(
      1
    );
    expect(Math.abs(compactGeometry.table.right - compactGeometry.editor.right)).toBeLessThanOrEqual(
      1
    );
    expect(compactGeometry.fieldGroupColumns).toBe(2);

    await page.setViewportSize({ width: 820, height: 800 });
    const narrowGeometry = await readDynamaxAdventureGeometry(page);
    expect(narrowGeometry.documentScrollWidth).toBeLessThanOrEqual(
      narrowGeometry.documentClientWidth + 1
    );
    expect(narrowGeometry.summary.top).toBeGreaterThanOrEqual(
      narrowGeometry.table.bottom - 1
    );
    expect(narrowGeometry.editor.top).toBeGreaterThanOrEqual(
      narrowGeometry.summary.bottom - 1
    );
    expect(Math.abs(narrowGeometry.table.left - narrowGeometry.summary.left)).toBeLessThanOrEqual(
      1
    );
    expect(Math.abs(narrowGeometry.table.left - narrowGeometry.editor.left)).toBeLessThanOrEqual(
      1
    );
    expect(Math.abs(narrowGeometry.table.right - narrowGeometry.summary.right)).toBeLessThanOrEqual(
      1
    );
    expect(Math.abs(narrowGeometry.table.right - narrowGeometry.editor.right)).toBeLessThanOrEqual(
      1
    );
    expect(narrowGeometry.fieldGroupColumns).toBe(1);

    await assertNoRuntimeIssues(page, runtimeIssues);
  });
});

async function installMockRuntime(page: Page, bridge?: ProjectBridge) {
  const runtimeBridge = bridge ?? createMockProjectBridge({}, true);
  const commandMethodLookup = new Map(
    Object.entries(kmCommandNames).map(([methodName, command]) => [command, methodName])
  );
  const runtimeIssues: string[] = [];

  page.on('console', (message) => {
    if (message.type() === 'error') {
      runtimeIssues.push(`console error: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => {
    runtimeIssues.push(`page error: ${error.message}`);
  });

  await page.exposeBinding('kmEditorMockInvoke', async (_source, command: string, args?: unknown) => {
    if (command === 'project_bridge' || command === 'project_bridge_once') {
      const requestJson = getProjectBridgeRequestJson(args);
      const request = JSON.parse(requestJson) as {
        command: string;
        payload: unknown;
        requestId?: string | null;
      };
      const methodName = commandMethodLookup.get(request.command);
      const handler = methodName
        ? (runtimeBridge as unknown as Record<string, ProjectBridge[keyof ProjectBridge]>)[methodName]
        : undefined;

      if (typeof handler !== 'function') {
        throw new Error(`Unhandled project bridge command: ${request.command}`);
      }

      const payload = await (handler as (payload: unknown) => Promise<unknown>)(request.payload);
      return JSON.stringify({
        payload,
        requestId: request.requestId ?? null
      });
    }

    switch (command) {
      case 'create_directory':
      case 'exit_app':
      case 'open_path':
      case 'set_close_guard_enabled':
      case 'plugin:shell|open':
        return null;
      case 'find_support_file_folder':
        return 'mock-support-folder';
      case 'plugin:dialog|open':
        return null;
      default:
        throw new Error(`Unhandled native command: ${command}`);
    }
  });

  await page.addInitScript(() => {
    const callbacks = new Map<number, (data: unknown) => unknown>();
    const listeners = new Map<string, number[]>();

    function registerCallback(callback: (data: unknown) => unknown, once = false) {
      const identifier = window.crypto.getRandomValues(new Uint32Array(1))[0] ?? Date.now();
      callbacks.set(identifier, (data: unknown) => {
        if (once) {
          callbacks.delete(identifier);
        }

        return callback?.(data);
      });

      return identifier;
    }

    function unregisterCallback(identifier: number) {
      callbacks.delete(identifier);
    }

    function runCallback(identifier: number, data: unknown) {
      callbacks.get(identifier)?.(data);
    }

    function handleEventPlugin(command: string, args: { event?: string; eventId?: number; handler?: number }) {
      if (command === 'plugin:event|listen') {
        const eventName = args.event ?? '';
        const handlers = listeners.get(eventName) ?? [];
        const handler = args.handler ?? 0;
        listeners.set(eventName, [...handlers, handler]);
        return handler;
      }

      if (command === 'plugin:event|unlisten') {
        const eventName = args.event ?? '';
        const eventListeners = listeners.get(eventName) ?? [];
        listeners.set(
          eventName,
          eventListeners.filter((handler) => handler !== args.eventId)
        );
        unregisterCallback(args.eventId ?? 0);
        return null;
      }

      if (command === 'plugin:event|emit') {
        const eventName = args.event ?? '';
        for (const handler of listeners.get(eventName) ?? []) {
          runCallback(handler, args);
        }

        return null;
      }

      return null;
    }

    window.__TAURI_INTERNALS__ = {
      callbacks,
      convertFileSrc: (filePath: string) => filePath,
      invoke: (command: string, args?: unknown, options?: unknown) => {
        if (command.startsWith('plugin:event|')) {
          return Promise.resolve(handleEventPlugin(command, args as never));
        }

        return window.kmEditorMockInvoke(command, args, options);
      },
      runCallback,
      transformCallback: registerCallback,
      unregisterCallback
    };
    window.__TAURI_EVENT_PLUGIN_INTERNALS__ = {
      unregisterListener: (_event: string, identifier: number) => unregisterCallback(identifier)
    };
  });

  return runtimeIssues;
}

async function createCanonicalDynamaxAdventureBridge() {
  const fixtureBridge = createMockProjectBridge({}, true);
  const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
    paths: {
      baseExeFsPath: 'mock-exefs',
      baseRomFsPath: 'mock-romfs',
      outputRootPath: 'mock-output',
      pokemonLegendsZASupportFolderPath: '',
      saveFilePath: '',
      scarletVioletSupportFolderPath: '',
      selectedGame: 'sword'
    }
  });
  const template = workflow.encounters[0]!;
  const encounters = Array.from({ length: 273 }, (_, entryIndex) => {
    const encounter = {
      ...structuredClone(template),
      adventureIndex: entryIndex,
      entryIndex,
      label: `Adventure ${entryIndex.toString().padStart(3, '0')}`,
      singleCaptureFlagBlock: `0x${entryIndex
        .toString(16)
        .toUpperCase()
        .padStart(16, '0')}`,
      uiMessageId: `0x${(entryIndex + 0x1000)
        .toString(16)
        .toUpperCase()
        .padStart(16, '0')}`
    };
    if (entryIndex >= 226) {
      encounter.isEditable = false;
      encounter.layoutWritableFields = [];
      encounter.vanillaPokemon = {
        ability: encounter.ability,
        abilityLabel: encounter.abilityLabel,
        form: encounter.form,
        gigantamaxLabel: encounter.gigantamaxLabel,
        gigantamaxState: encounter.gigantamaxState,
        guaranteedPerfectIvs: encounter.guaranteedPerfectIvs,
        ivs: structuredClone(encounter.ivs),
        ivSummary: encounter.ivSummary,
        level: encounter.level,
        moves: structuredClone(encounter.moves),
        species: encounter.species,
        speciesId: encounter.speciesId
      };
    }
    return encounter;
  });
  const canonicalWorkflow = {
    ...workflow,
    encounters,
    stats: {
      guaranteedPerfectIvEncounterCount: encounters.length,
      singleCaptureCount: encounters.length,
      sourceFileCount: 1,
      storyGatedCount: 0,
      totalEncounterCount: encounters.length
    }
  };

  return createMockProjectBridge(
    {
      loadDynamaxAdventuresWorkflow: () =>
        Promise.resolve({ workflow: canonicalWorkflow })
    },
    true
  );
}

function getProjectBridgeRequestJson(args: unknown) {
  if (
    typeof args === 'object' &&
    args !== null &&
    'requestJson' in args &&
    typeof args.requestJson === 'string'
  ) {
    return args.requestJson;
  }

  throw new Error('project_bridge did not receive requestJson.');
}

async function fillProjectPathInputs(page: Page) {
  const projectPathInputs = page
    .locator('input:not([readonly]):not([disabled])')
    .filter({ hasNot: page.locator('[aria-hidden="true"]') });
  const inputCount = await projectPathInputs.count();

  for (let index = 0; index < inputCount; index += 1) {
    const input = projectPathInputs.nth(index);
    if (!(await input.isVisible())) {
      continue;
    }

    await input.fill(`mock-path-${index}`);
  }
}

async function collectVisibleWorkflowLabels(page: Page) {
  const groups = page.locator('.nav-group-button');
  const groupCount = await groups.count();

  for (let index = 0; index < groupCount; index += 1) {
    const group = groups.nth(index);
    if ((await group.getAttribute('aria-expanded')) !== 'true') {
      await group.click();
    }
  }

  const labels = await page.locator('.section-nav .nav-child-button').evaluateAll((buttons) =>
    buttons
      .map((button) => button.getAttribute('aria-label')?.trim() ?? button.textContent?.trim() ?? '')
      .filter((label, index, allLabels) => label.length > 0 && allLabels.indexOf(label) === index)
  );

  return labels;
}

async function openWorkflow(page: Page, workflowLabel: string) {
  const nav = page.getByRole('navigation', { name: 'Workspace' });
  await nav.getByRole('button', { exact: true, name: workflowLabel }).click();
  await expect(page.getByRole('heading', { level: 1, name: workflowLabel })).toBeVisible();
  await assertActiveWorkflowNavigationVisible(page);
  await expect(page.getByText(new RegExp(`Open ${escapeRegExp(workflowLabel)} from Workflows`))).toHaveCount(
    0
  );
}

async function assertActiveWorkflowNavigationVisible(page: Page) {
  const geometry = await page.locator('.sidebar-navigation-scroll').evaluate((scrollRegion) => {
    const activeItem = scrollRegion.querySelector<HTMLElement>('[aria-current="page"]');
    if (!activeItem) {
      return null;
    }

    const scrollRegionBounds = scrollRegion.getBoundingClientRect();
    const activeItemBounds = activeItem.getBoundingClientRect();
    return {
      activeBottom: activeItemBounds.bottom,
      activeTop: activeItemBounds.top,
      scrollLeft: scrollRegion.scrollLeft,
      scrollRegionBottom: scrollRegionBounds.bottom,
      scrollRegionTop: scrollRegionBounds.top
    };
  });

  expect(geometry).not.toBeNull();
  if (!geometry) {
    return;
  }

  const tolerance = 1;
  expect(geometry.scrollLeft).toBe(0);
  expect(geometry.activeTop).toBeGreaterThanOrEqual(geometry.scrollRegionTop - tolerance);
  expect(geometry.activeBottom).toBeLessThanOrEqual(geometry.scrollRegionBottom + tolerance);
}

async function openPrimarySection(page: Page, sectionLabel: string) {
  const nav = page.getByRole('navigation', { name: 'Workspace' });
  await nav.getByRole('button', { exact: true, name: sectionLabel }).click();
  await expect(page.getByRole('heading', { level: 1, name: sectionLabel })).toBeVisible();
}

async function assertNoRuntimeIssues(page: Page, runtimeIssues: string[]) {
  await expect(page.getByText('KM Editor hit an unexpected bridge error.')).toHaveCount(0);
  await expect(page.getByText(/^Error code:/)).toHaveCount(0);
  await expect(page.getByText(/^Unhandled exception/i)).toHaveCount(0);
  expect(runtimeIssues).toEqual([]);
}

async function assertLearnsetInlineControlsStayInRow(
  page: Page,
  inlineRow: Locator
) {
  const bounds = await inlineRow.evaluate((row) => {
    const rowBox = row.getBoundingClientRect();
    const actionBox = row.querySelector('.learnset-inline-actions')?.getBoundingClientRect();
    const boxes = Array.from(
      row.querySelectorAll<HTMLElement>(
        '.learnset-move-field, .learnset-level-field, .learnset-inline-metadata, .learnset-inline-actions'
      )
    ).map((element) => {
      const box = element.getBoundingClientRect();
      return {
        bottom: box.bottom,
        left: box.left,
        right: box.right,
        top: box.top
      };
    });

    return {
      actionBottom: actionBox?.bottom ?? 0,
      actionLeft: actionBox?.left ?? 0,
      actionRight: actionBox?.right ?? 0,
      actionTop: actionBox?.top ?? 0,
      boxes,
      rowBottom: rowBox.bottom,
      rowLeft: rowBox.left,
      rowRight: rowBox.right,
      rowTop: rowBox.top,
      scrollWidth: row.scrollWidth,
      width: row.clientWidth
    };
  });

  const tolerance = 1;
  expect(bounds.scrollWidth).toBeLessThanOrEqual(bounds.width + tolerance);
  expect(bounds.actionLeft).toBeGreaterThanOrEqual(bounds.rowLeft - tolerance);
  expect(bounds.actionRight).toBeLessThanOrEqual(bounds.rowRight + tolerance);
  expect(bounds.actionTop).toBeGreaterThanOrEqual(bounds.rowTop - tolerance);
  expect(bounds.actionBottom).toBeLessThanOrEqual(bounds.rowBottom + tolerance);
  for (const box of bounds.boxes) {
    expect(box.left).toBeGreaterThanOrEqual(bounds.rowLeft - tolerance);
    expect(box.right).toBeLessThanOrEqual(bounds.rowRight + tolerance);
    expect(box.top).toBeGreaterThanOrEqual(bounds.rowTop - tolerance);
    expect(box.bottom).toBeLessThanOrEqual(bounds.rowBottom + tolerance);
  }

  await expect(page.getByRole('button', { name: 'Move learnset row up' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Move learnset row down' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Remove learnset row' })).toBeVisible();
}

async function readDynamaxAdventureGeometry(page: Page) {
  return page.locator('.dynamax-adventures-layout').evaluate((layout) => {
    const readRect = (selector: string) => {
      const element = layout.querySelector<HTMLElement>(selector);
      if (!element) {
        throw new Error(`Missing Dynamax Adventures layout element: ${selector}`);
      }

      const bounds = element.getBoundingClientRect();
      return {
        bottom: bounds.bottom,
        left: bounds.left,
        right: bounds.right,
        top: bounds.top
      };
    };
    const fieldGroups = layout.querySelector<HTMLElement>(
      '.dynamax-adventure-field-groups'
    );
    if (!fieldGroups) {
      throw new Error('Missing Dynamax Adventures field groups.');
    }

    return {
      documentClientWidth: document.documentElement.clientWidth,
      documentScrollWidth: document.documentElement.scrollWidth,
      editor: readRect('.dynamax-adventure-editor'),
      fieldGroupColumns: getComputedStyle(fieldGroups).gridTemplateColumns.split(' ').length,
      summary: readRect('.dynamax-adventure-summary'),
      table: readRect('.dynamax-adventures-table')
    };
  });
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

declare global {
  interface Window {
    __TAURI_EVENT_PLUGIN_INTERNALS__: {
      unregisterListener: (event: string, identifier: number) => void;
    };
    __TAURI_INTERNALS__: {
      callbacks: Map<number, (data: unknown) => unknown>;
      convertFileSrc: (filePath: string, protocol?: string) => string;
      invoke: (command: string, args?: unknown, options?: unknown) => Promise<unknown>;
      runCallback: (identifier: number, data: unknown) => void;
      transformCallback: (callback: (data: unknown) => unknown, once?: boolean) => number;
      unregisterCallback: (identifier: number) => void;
    };
    kmEditorMockInvoke: (command: string, args?: unknown, options?: unknown) => Promise<unknown>;
  }
}
