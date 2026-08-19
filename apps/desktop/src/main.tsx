/* SPDX-License-Identifier: GPL-3.0-only */

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App, AppErrorBoundary } from './App';
import { installGlobalErrorHandlers } from './errorReporting';
import { LocalizationProvider } from './localization';
import {
  AppearancePreferencesProvider,
  applyStoredAppearancePreferences
} from './features/settings/AppearancePreferencesProvider';
import './styles.css';

installGlobalErrorHandlers();
applyStoredAppearancePreferences();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppearancePreferencesProvider>
      <LocalizationProvider>
        <AppErrorBoundary>
          <App />
        </AppErrorBoundary>
      </LocalizationProvider>
    </AppearancePreferencesProvider>
  </StrictMode>
);
