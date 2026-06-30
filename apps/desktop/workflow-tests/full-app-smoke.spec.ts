/* SPDX-License-Identifier: GPL-3.0-only */

import { expect, type Page, test } from '@playwright/test';
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
      const runtimeIssues = await installMockRuntime(page);

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

      await openPrimarySection(page, 'Project Setup');
      await assertNoRuntimeIssues(page, runtimeIssues);
      await openPrimarySection(page, 'Changes');
      await assertNoRuntimeIssues(page, runtimeIssues);
      await openPrimarySection(page, 'Settings');
      await assertNoRuntimeIssues(page, runtimeIssues);
    });
  }
});

async function installMockRuntime(page: Page) {
  const bridge = createMockProjectBridge({}, true);
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
    if (command === 'project_bridge_once') {
      const requestJson = getProjectBridgeRequestJson(args);
      const request = JSON.parse(requestJson) as {
        command: string;
        payload: unknown;
        requestId?: string | null;
      };
      const methodName = commandMethodLookup.get(request.command);
      const handler = methodName
        ? (bridge as unknown as Record<string, ProjectBridge[keyof ProjectBridge]>)[methodName]
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

function getProjectBridgeRequestJson(args: unknown) {
  if (
    typeof args === 'object' &&
    args !== null &&
    'requestJson' in args &&
    typeof args.requestJson === 'string'
  ) {
    return args.requestJson;
  }

  throw new Error('project_bridge_once did not receive requestJson.');
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
  await expect(page.getByText(new RegExp(`Open ${escapeRegExp(workflowLabel)} from Workflows`))).toHaveCount(
    0
  );
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
