/* SPDX-License-Identifier: GPL-3.0-only */

import react from '@vitejs/plugin-react';
import { configDefaults, defineConfig } from 'vitest/config';

const tauriDevHost = process.env.TAURI_DEV_HOST;

export default defineConfig({
  clearScreen: false,
  plugins: [react()],
  server: {
    // Tauri sets this when the dev server must bind to a specific host; otherwise keep Vite local-only.
    host: tauriDevHost ?? false,
    port: 5173,
    strictPort: true
  },
  test: {
    css: true,
    environment: 'jsdom',
    // Playwright owns browser workflow specs; Vitest stays focused on unit and component tests.
    exclude: [...configDefaults.exclude, 'workflow-tests/**'],
    globals: true,
    setupFiles: './vitest.setup.ts'
  }
});
