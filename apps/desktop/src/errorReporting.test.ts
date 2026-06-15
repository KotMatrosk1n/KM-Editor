/* SPDX-License-Identifier: GPL-3.0-only */

import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createReportableError,
  formatReportableErrorMessage,
  installGlobalErrorHandlers
} from './errorReporting';

describe('error reporting', () => {
  let cleanupGlobalErrorHandlers: (() => void) | null = null;

  afterEach(() => {
    cleanupGlobalErrorHandlers?.();
    cleanupGlobalErrorHandlers = null;
    vi.restoreAllMocks();
  });

  it('creates screenshot-friendly reportable error messages', () => {
    const report = createReportableError(new Error('Render failed'), {
      kind: 'render'
    });

    expect(report.code).toMatch(/^KM-UI-RENDER-[0-9A-Z]{6}$/);
    expect(formatReportableErrorMessage(report)).toContain(`Error code: ${report.code}`);
    expect(formatReportableErrorMessage(report)).toContain(
      'Take a screenshot of this message and report it in GitHub Issues.'
    );
  });

  it('uses fallback text when the thrown value has no useful message', () => {
    const report = createReportableError(null, {
      fallbackMessage: 'The background task failed.',
      kind: 'unhandledRejection'
    });

    expect(report.message).toBe('The background task failed.');
    expect(report.code).toMatch(/^KM-UI-PROMISE-[0-9A-Z]{6}$/);
  });

  it('installs global error handlers only once and exposes cleanup', () => {
    const addEventListener = vi.spyOn(window, 'addEventListener');
    const removeEventListener = vi.spyOn(window, 'removeEventListener');

    const firstCleanup = installGlobalErrorHandlers();
    cleanupGlobalErrorHandlers = firstCleanup;
    const secondCleanup = installGlobalErrorHandlers();

    expect(secondCleanup).toBe(firstCleanup);
    expect(addEventListener).toHaveBeenCalledTimes(2);
    expect(addEventListener.mock.calls.map(([eventName]) => eventName)).toEqual([
      'error',
      'unhandledrejection'
    ]);

    firstCleanup();
    cleanupGlobalErrorHandlers = null;

    expect(removeEventListener).toHaveBeenCalledTimes(2);
    expect(removeEventListener.mock.calls.map(([eventName]) => eventName)).toEqual([
      'error',
      'unhandledrejection'
    ]);
  });
});
