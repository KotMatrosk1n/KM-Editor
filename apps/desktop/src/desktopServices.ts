/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke } from '@tauri-apps/api/core';
import { open as openDialog } from '@tauri-apps/plugin-dialog';

export type PickFolderOptions = {
  defaultPath?: string;
  title: string;
};

export type DesktopServices = {
  isAvailable: boolean;
  openPath: (path: string) => Promise<void>;
  pickFolder: (options: PickFolderOptions) => Promise<string | null>;
};

export const desktopServices: DesktopServices = {
  isAvailable: hasTauriRuntime(),
  openPath: (path) => invoke('open_path', { path }),
  pickFolder: async ({ defaultPath, title }) => {
    const selection = await openDialog({
      defaultPath,
      directory: true,
      multiple: false,
      title
    });

    return typeof selection === 'string' ? selection : null;
  }
};

function hasTauriRuntime() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}
