/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import { useWorkbenchStore } from './workbenchStore';

describe('App', () => {
  beforeEach(() => {
    useWorkbenchStore.setState({ activeSection: 'health' });
  });

  it('renders the project workbench shell', () => {
    render(<App />);

    expect(screen.getByText('KM Editor')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Project Health' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open Project' })).toBeInTheDocument();
  });

  it('switches workbench sections', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByRole('heading', { name: 'Changes' })).toBeInTheDocument();
  });
});
