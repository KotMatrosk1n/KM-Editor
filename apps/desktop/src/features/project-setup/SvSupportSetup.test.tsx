/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from '../../App';
import {
  createHealthForValidatedPaths,
  createMockDesktopServices,
  createMockProjectBridge
} from '../../testSupport/appTestFixtures';
import { useWorkbenchStore } from '../../workbenchStore';

const tauriEventMock = vi.hoisted(() => ({
  listen: vi.fn(() => Promise.resolve(() => undefined))
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen: tauriEventMock.listen
}));

describe('S/V support setup', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    useWorkbenchStore.setState({
      activeSection: 'health',
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: 'scarlet'
      },
      editSession: null,
      openProject: null,
      projectStatus: 'idle',
      workflows: []
    });
  });

  it('asks permission, finds the optional support folder, and refreshes paths', async () => {
    const user = userEvent.setup();
    const supportFolderPath = 'C:\\Support';
    const findScarletVioletSupportFolder = vi.fn(async () => supportFolderPath);
    const validateProject = vi.fn(async (request) => ({
      health: createHealthForValidatedPaths(
        request.paths.baseRomFsPath ?? '',
        request.paths.baseExeFsPath ?? '',
        request.paths.outputRootPath ?? '',
        request.paths.saveFilePath ?? null,
        request.paths.scarletVioletSupportFolderPath ?? null
      )
    }));
    const listWorkflows = vi.fn(async () => ({ workflows: [] }));

    render(
      <App
        bridge={createMockProjectBridge({ listWorkflows, validateProject })}
        desktopServices={createMockDesktopServices({ findScarletVioletSupportFolder })}
      />
    );

    expect(screen.getByLabelText('oo2core_8_win64.dll Folder (Optional)')).toHaveValue('');

    await user.click(screen.getByRole('button', { name: 'Find oo2core_8_win64.dll' }));
    expect(
      screen.getByRole('dialog', { name: 'Search for oo2core_8_win64.dll?' })
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Search' }));

    await waitFor(() =>
      expect(screen.getByLabelText('oo2core_8_win64.dll Folder (Optional)')).toHaveValue(
        supportFolderPath
      )
    );
    expect(findScarletVioletSupportFolder).toHaveBeenCalledTimes(1);
    expect(validateProject).toHaveBeenCalledWith({
      paths: {
        baseExeFsPath: null,
        baseRomFsPath: null,
        gameTextLanguage: 'en',
        outputRootPath: null,
        saveFilePath: null,
        scarletVioletSupportFolderPath: supportFolderPath,
        selectedGame: 'scarlet'
      }
    });
    expect(listWorkflows).toHaveBeenCalledTimes(1);
  });
});
