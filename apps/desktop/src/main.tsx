/* SPDX-License-Identifier: GPL-3.0-only */

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App, AppErrorBoundary } from './App';
import { GlobalReportableErrorHost } from './components/ReportableErrorScreen';
import { installGlobalErrorHandlers } from './errorReporting';
import { LocalizationProvider } from './localization';
import {
  AppearancePreferencesProvider,
  applyStoredAppearancePreferences
} from './features/settings/AppearancePreferencesProvider';
import './styles.css';

const uninstallGlobalErrorHandlers = installGlobalErrorHandlers();
if (import.meta.hot) {
  import.meta.hot.dispose(uninstallGlobalErrorHandlers);
}
applyStoredAppearancePreferences();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppErrorBoundary>
      <AppearancePreferencesProvider>
        <LocalizationProvider>
          <GlobalReportableErrorHost>
            <AppErrorBoundary>
              <App />
            </AppErrorBoundary>
          </GlobalReportableErrorHost>
        </LocalizationProvider>
      </AppearancePreferencesProvider>
    </AppErrorBoundary>
  </StrictMode>
);
