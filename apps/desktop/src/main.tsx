/* SPDX-License-Identifier: GPL-3.0-only */

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App, AppErrorBoundary } from './App';
import { installGlobalErrorHandlers } from './errorReporting';
import { LocalizationProvider } from './localization';
import './styles.css';

installGlobalErrorHandlers();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LocalizationProvider>
      <AppErrorBoundary>
        <App />
      </AppErrorBoundary>
    </LocalizationProvider>
  </StrictMode>
);
