/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { workflowDefinitions } from './workflowDefinitions';

describe('workflow definitions', () => {
  it('publishes Rental Pokemon and does not promise unsupported shop stock limits', () => {
    expect(workflowDefinitions.find((definition) => definition.id === 'rentalPokemon')).toMatchObject({
      label: 'Rental Pokemon'
    });
    expect(workflowDefinitions.find((definition) => definition.id === 'shops')?.description)
      .toBe('Shop inventories, item metadata, and source provenance.');
  });
});
