/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { createReportableError, formatReportableErrorMessage } from './errorReporting';

describe('error reporting', () => {
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
});
