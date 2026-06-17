/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { AppErrorBoundary } from './App';

describe('AppErrorBoundary', () => {
  it('shows a reportable error code when rendering crashes', () => {
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const BrokenSection = () => {
      throw new Error('render exploded');
    };

    try {
      render(
        <AppErrorBoundary>
          <BrokenSection />
        </AppErrorBoundary>
      );

      expect(screen.getByRole('alert')).toHaveTextContent('KM Editor hit a critical display error.');
      expect(screen.getByText(/^KM-UI-RENDER-/)).toBeInTheDocument();
      expect(screen.getByText(/Take a screenshot/)).toBeInTheDocument();
    } finally {
      consoleErrorSpy.mockRestore();
    }
  });
});
