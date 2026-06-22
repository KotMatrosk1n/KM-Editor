/* SPDX-License-Identifier: GPL-3.0-only */

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { App, AppErrorBoundary } from './App';
import { installGlobalErrorHandlers } from './errorReporting';
import { LocalizationProvider } from './localization';
import { queryClient } from './queryClient';
import './styles.css';

installGlobalErrorHandlers();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <LocalizationProvider>
        <AppErrorBoundary>
          <App />
        </AppErrorBoundary>
      </LocalizationProvider>
    </QueryClientProvider>
  </StrictMode>
);
