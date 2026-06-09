/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke } from '@tauri-apps/api/core';
import { open as openDialog } from '@tauri-apps/plugin-dialog';

export type PickFolderOptions = {
  defaultPath?: string;
  title: string;
};

export type DesktopServices = {
  exitApp: () => Promise<void>;
  isAvailable: boolean;
  openPath: (path: string) => Promise<void>;
  pickFile: (options: PickFolderOptions) => Promise<string | null>;
  pickFolder: (options: PickFolderOptions) => Promise<string | null>;
  setCloseGuardEnabled: (enabled: boolean) => Promise<void>;
};

export const desktopServices: DesktopServices = {
  exitApp: () => invoke('exit_app'),
  isAvailable: hasTauriRuntime(),
  openPath: (path) => invoke('open_path', { path }),
  pickFile: async ({ defaultPath, title }) => {
    const selection = await openDialog({
      defaultPath,
      directory: false,
      multiple: false,
      title
    });

    return typeof selection === 'string' ? selection : null;
  },
  pickFolder: async ({ defaultPath, title }) => {
    const selection = await openDialog({
      defaultPath,
      directory: true,
      multiple: false,
      title
    });

    return typeof selection === 'string' ? selection : null;
  },
  setCloseGuardEnabled: (enabled) => invoke('set_close_guard_enabled', { enabled })
};

function hasTauriRuntime() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}
