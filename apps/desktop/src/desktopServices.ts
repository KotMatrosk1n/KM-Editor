/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke } from '@tauri-apps/api/core';
import { open as openDialog } from '@tauri-apps/plugin-dialog';
import { open as openExternal } from '@tauri-apps/plugin-shell';
import type { DownloadEvent } from '@tauri-apps/plugin-updater';

export type PickFolderOptions = {
  defaultPath?: string;
  title: string;
};

export type NativeUpdate = {
  body?: string;
  close: () => Promise<void>;
  date?: string;
  install: (onProgress?: (event: DownloadEvent) => void) => Promise<void>;
  version: string;
};

export type DesktopServices = {
  checkForNativeUpdate: () => Promise<NativeUpdate | null>;
  createDirectory: (path: string) => Promise<void>;
  exitApp: () => Promise<void>;
  findScarletVioletSupportFolder: () => Promise<string | null>;
  isAvailable: boolean;
  openExternalUrl: (url: string) => Promise<void>;
  openPath: (path: string) => Promise<void>;
  pickFile: (options: PickFolderOptions) => Promise<string | null>;
  pickFolder: (options: PickFolderOptions) => Promise<string | null>;
  relaunchApp: () => Promise<void>;
  setCloseGuardEnabled: (enabled: boolean) => Promise<void>;
};

export const desktopServices: DesktopServices = {
  checkForNativeUpdate: async () => {
    ensureTauriRuntime();

    const { check } = await import('@tauri-apps/plugin-updater');
    const update = await check();

    if (!update) {
      return null;
    }

    return {
      body: update.body,
      close: () => update.close(),
      date: update.date,
      install: (onProgress) => update.downloadAndInstall(onProgress),
      version: update.version
    };
  },
  createDirectory: (path) => invoke('create_directory', { path }),
  exitApp: () => invoke('exit_app'),
  findScarletVioletSupportFolder: () => invoke('find_scarlet_violet_support_file'),
  isAvailable: hasTauriRuntime(),
  openExternalUrl: (url) => openExternal(url),
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
  relaunchApp: async () => {
    ensureTauriRuntime();

    const { relaunch } = await import('@tauri-apps/plugin-process');
    await relaunch();
  },
  setCloseGuardEnabled: (enabled) => invoke('set_close_guard_enabled', { enabled })
};

function hasTauriRuntime() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

function ensureTauriRuntime() {
  if (!hasTauriRuntime()) {
    throw new Error('Native updates are only available in the desktop app.');
  }
}
