/* SPDX-License-Identifier: GPL-3.0-only */

import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  fullyParallel: true,
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ],
  reporter: process.env.CI ? 'dot' : 'list',
  testDir: './workflow-tests',
  use: {
    baseURL: 'http://127.0.0.1:5173',
    trace: 'retain-on-failure'
  },
  webServer: {
    command: 'pnpm dev --host 127.0.0.1',
    // Local runs can reuse a manually started Vite server; CI should own its server process.
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    url: 'http://127.0.0.1:5173'
  }
});
