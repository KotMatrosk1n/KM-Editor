/* SPDX-License-Identifier: GPL-3.0-only */

import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { execFileSync } from 'node:child_process';

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
  plugins: [react()],
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
