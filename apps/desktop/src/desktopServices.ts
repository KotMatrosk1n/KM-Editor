/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke } from '@tauri-apps/api/core';
import { open as openDialog } from '@tauri-apps/plugin-dialog';
import { open as openExternal } from '@tauri-apps/plugin-shell';
import type { DownloadEvent } from '@tauri-apps/plugin-updater';
import { desktopErrorCodes, type KmErrorCode } from './errorCodes';

export class DesktopServiceError extends Error {
  public constructor(
    public readonly code: KmErrorCode,
    message: string,
    cause?: unknown
  ) {
    super(message, { cause });
    this.name = 'DesktopServiceError';
  }
}

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
  cancelSupportFileSearch: () => Promise<void>;
  checkForNativeUpdate: () => Promise<NativeUpdate | null>;
  createDirectory: (path: string) => Promise<void>;
  exitApp: () => Promise<void>;
  findSupportFileFolder: () => Promise<string | null>;
  isAvailable: boolean;
  openExternalUrl: (url: string) => Promise<void>;
  openPath: (path: string) => Promise<void>;
  pickFile: (options: PickFolderOptions) => Promise<string | null>;
  pickFolder: (options: PickFolderOptions) => Promise<string | null>;
  recycleProjectBridge: () => Promise<void>;
  relaunchApp: () => Promise<void>;
  setCloseGuardEnabled: (enabled: boolean) => Promise<void>;
};

export const desktopServices: DesktopServices = {
  cancelSupportFileSearch: () =>
    invokeDesktopCommand<void>(
      'cancel_support_file_search',
      undefined,
      desktopErrorCodes.supportFileSearchCancelFailed,
      'Could not cancel the support file search.'
    ),
  checkForNativeUpdate: async () => {
    return withDesktopServiceError(
      desktopErrorCodes.updateCheckFailed,
      'Could not check for native updates.',
      async () => {
        ensureTauriRuntime();

        const { check } = await import('@tauri-apps/plugin-updater');
        const update = await check();

        if (!update) {
          return null;
        }

        return {
          body: update.body,
          close: () =>
            withDesktopServiceError(
              desktopErrorCodes.updateCloseFailed,
              'Could not close the native update session.',
              () => update.close()
            ),
          date: update.date,
          install: (onProgress) =>
            withDesktopServiceError(
              desktopErrorCodes.updateInstallFailed,
              'Could not install the native update.',
              () => update.downloadAndInstall(onProgress)
            ),
          version: update.version
        };
      }
    );
  },
  createDirectory: (path) =>
    invokeDesktopCommand<void>(
      'create_directory',
      { path },
      desktopErrorCodes.directoryCreateFailed,
      'Could not create the folder.'
    ),
  exitApp: () =>
    invokeDesktopCommand<void>(
      'exit_app',
      undefined,
      desktopErrorCodes.appExitFailed,
      'Could not close KM Editor.'
    ),
  findSupportFileFolder: async () => {
    try {
      return await invoke<string | null>('find_support_file_folder');
    } catch (error) {
      const message = toUnknownErrorMessage(error);
      if (message === 'Support file search was canceled.') {
        throw new DesktopServiceError(desktopErrorCodes.supportFileSearchCanceled, message);
      }

      throw createDesktopServiceError(
        desktopErrorCodes.supportFileSearchFailed,
        'Could not search for the support file.',
        error
      );
    }
  },
  isAvailable: hasTauriRuntime(),
  openExternalUrl: (url) =>
    withDesktopServiceError(
      desktopErrorCodes.externalUrlOpenFailed,
      'Could not open the external link.',
      () => openExternal(url)
    ),
  openPath: (path) =>
    invokeDesktopCommand<void>(
      'open_path',
      { path },
      desktopErrorCodes.pathOpenFailed,
      'Could not open the folder.'
    ),
  pickFile: ({ defaultPath, title }) =>
    withDesktopServiceError(
      desktopErrorCodes.filePickerFailed,
      'Could not open the file picker.',
      async () => {
        const selection = await openDialog({
          defaultPath,
          directory: false,
          multiple: false,
          title
        });

        return typeof selection === 'string' ? selection : null;
      }
    ),
  pickFolder: ({ defaultPath, title }) =>
    withDesktopServiceError(
      desktopErrorCodes.folderPickerFailed,
      'Could not open the folder picker.',
      async () => {
        const selection = await openDialog({
          defaultPath,
          directory: true,
          multiple: false,
          title
        });

        return typeof selection === 'string' ? selection : null;
      }
    ),
  recycleProjectBridge: () =>
    invokeDesktopCommand<void>(
      'recycle_project_bridge',
      undefined,
      desktopErrorCodes.bridgeRecycleFailed,
      'Could not restart the project bridge.'
    ),
  relaunchApp: async () => {
    return withDesktopServiceError(
      desktopErrorCodes.appRelaunchFailed,
      'Could not restart KM Editor.',
      async () => {
        ensureTauriRuntime();

        const { relaunch } = await import('@tauri-apps/plugin-process');
        await relaunch();
      }
    );
  },
  setCloseGuardEnabled: (enabled) =>
    invokeDesktopCommand<void>(
      'set_close_guard_enabled',
      { enabled },
      desktopErrorCodes.closeGuardUpdateFailed,
      'Could not update the desktop close guard.'
    )
};

async function invokeDesktopCommand<T>(
  command: string,
  args: Record<string, unknown> | undefined,
  code: KmErrorCode,
  fallbackMessage: string
) {
  return withDesktopServiceError(code, fallbackMessage, () => invoke<T>(command, args));
}

async function withDesktopServiceError<T>(
  code: KmErrorCode,
  fallbackMessage: string,
  operation: () => Promise<T>
) {
  try {
    return await operation();
  } catch (error) {
    throw createDesktopServiceError(code, fallbackMessage, error);
  }
}

function createDesktopServiceError(
  code: KmErrorCode,
  fallbackMessage: string,
  error: unknown
) {
  if (error instanceof DesktopServiceError) {
    return error;
  }

  const detail = toUnknownErrorMessage(error);
  const message =
    detail && detail !== fallbackMessage ? `${fallbackMessage} ${detail}` : fallbackMessage;
  return new DesktopServiceError(code, message, error);
}

function toUnknownErrorMessage(error: unknown) {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  if (typeof error === 'string' && error.trim().length > 0) {
    return error;
  }

  return null;
}

function hasTauriRuntime() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

function ensureTauriRuntime() {
  if (!hasTauriRuntime()) {
    throw new DesktopServiceError(
      desktopErrorCodes.runtimeUnavailable,
      'Native services are only available in the desktop app.'
    );
  }
}
