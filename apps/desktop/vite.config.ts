/* SPDX-License-Identifier: GPL-3.0-only */

import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const tauriDevHost = process.env.TAURI_DEV_HOST;

export default defineConfig({
  base: './',
  clearScreen: false,
  plugins: [react()],
  server: {
    // Tauri sets this when the dev server must bind to a specific host; otherwise keep Vite local-only.
    host: tauriDevHost ?? false,
    port: 5173,
    strictPort: true
  }
});
