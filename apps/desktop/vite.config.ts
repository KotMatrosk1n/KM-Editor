/* SPDX-License-Identifier: GPL-3.0-only */

import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { execFileSync } from 'node:child_process';
import { cp } from 'node:fs/promises';
import { relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const publicDir = fileURLToPath(new URL('./public', import.meta.url));
const downloadedArtwork = new Set(['sprites/gen5', 'sprites/ani', 'sprites/manifest.json']);
const usesDownloadedArtwork = Boolean(process.env.TAURI_ENV_PLATFORM);

const tauriDevHost = process.env.TAURI_DEV_HOST;
function buildIdentity() {
  try {
    const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8', timeout: 3000 }).trim();
    const modified = execFileSync('git', ['status', '--porcelain', '--untracked-files=normal'], { encoding: 'utf8', timeout: 3000 }).trim().length > 0;
    return { commit: /^[a-f0-9]{40}$/.test(commit) ? commit : null, modified };
  } catch { return { commit: null, modified: null }; }
}
const identity = buildIdentity();

export default defineConfig({
  base: './',
  clearScreen: false,
  build: { copyPublicDir: !usesDownloadedArtwork },
  plugins: [
    react(),
    usesDownloadedArtwork && {
      name: 'km-editor-public-assets',
      async writeBundle({ dir }) {
        if (!dir) return;
        await cp(publicDir, resolve(dir), {
          recursive: true,
          filter: (path) => !downloadedArtwork.has(relative(publicDir, path).replaceAll('\\', '/'))
        });
      }
    }
  ],
  define: {
    __KM_BUILD_COMMIT__: JSON.stringify(identity.commit),
    __KM_BUILD_MODIFIED__: JSON.stringify(identity.modified)
  },
  server: {
    // Tauri sets this when the dev server must bind to a specific host; otherwise keep Vite local-only.
    host: tauriDevHost ?? false,
    port: 5173,
    strictPort: true
  }
});
