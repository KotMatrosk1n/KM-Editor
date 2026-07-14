/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(() => Promise.resolve(() => undefined))
}));

describe('responsive application shell', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.setState({
      activeSection: 'health',
      applyResult: null,
      changePlan: null,
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        pokemonLegendsZASupportFolderPath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: 'sword'
      },
      editSession: null,
      editValidationDiagnostics: [],
      itemsWorkflow: null,
      openProject: null,
      projectStatus: 'idle',
      selectedItemId: null,
      staticEncountersWorkflow: null,
      workflows: []
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('uses a temporary navigation overlay without replacing the stored preference', async () => {
    const user = userEvent.setup();
    const mediaQueryListeners = new Set<(event: MediaQueryListEvent) => void>();
    const constrainedMediaQuery = {
      addEventListener: vi.fn(
        (_eventName: string, listener: (event: MediaQueryListEvent) => void) => {
          mediaQueryListeners.add(listener);
        }
      ),
      addListener: vi.fn((listener: (event: MediaQueryListEvent) => void) => {
        mediaQueryListeners.add(listener);
      }),
      dispatchEvent: vi.fn(),
      matches: true,
      media: '(max-width: 1280px), (max-height: 720px)',
      onchange: null,
      removeEventListener: vi.fn(
        (_eventName: string, listener: (event: MediaQueryListEvent) => void) => {
          mediaQueryListeners.delete(listener);
        }
      ),
      removeListener: vi.fn((listener: (event: MediaQueryListEvent) => void) => {
        mediaQueryListeners.delete(listener);
      })
    } as MediaQueryList;
    vi.stubGlobal('matchMedia', vi.fn(() => constrainedMediaQuery));

    render(
      <LocalizationProvider>
        <App bridge={createMockProjectBridge({}, true)} />
      </LocalizationProvider>
    );

    const navigation = screen.getByRole('navigation', { name: 'Workspace' });
    const shell = navigation.closest('.app-shell');
    expect(shell).toHaveClass('sidebar-is-constrained', 'sidebar-is-compact');
    expect(navigation.querySelector('.sidebar-utility-nav')).toContainElement(
      within(navigation).getByRole('button', { name: 'Changes' })
    );
    expect(window.localStorage.getItem('km-editor.sidebar.compact.v1')).toBeNull();

    const expandSidebarButton = screen.getByRole('button', { name: 'Expand sidebar' });
    await user.click(expandSidebarButton);
    expect(shell).toHaveClass('sidebar-is-constrained', 'sidebar-overlay-open');
    expect(shell).not.toHaveClass('sidebar-is-compact');
    expect(shell?.querySelector('.workspace')).toHaveAttribute('inert');
    expect(window.localStorage.getItem('km-editor.sidebar.compact.v1')).toBeNull();

    await user.click(screen.getByRole('button', { name: 'Close sidebar' }));
    expect(shell).toHaveClass('sidebar-is-compact');
    expect(shell).not.toHaveClass('sidebar-overlay-open');
    expect(shell?.querySelector('.workspace')).not.toHaveAttribute('inert');
    expect(screen.getByRole('button', { name: 'Expand sidebar' })).toHaveFocus();
    expect(window.localStorage.getItem('km-editor.sidebar.compact.v1')).toBeNull();
  });
});
