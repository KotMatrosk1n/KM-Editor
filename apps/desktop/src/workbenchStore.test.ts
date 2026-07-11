/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { type PokemonWorkflow } from './bridge/contracts';
import { useWorkbenchStore } from './workbenchStore';

describe('workbench store', () => {
  it('clears every store-owned workflow when the project session resets', () => {
    const state = useWorkbenchStore.getState();
    const workflowKeys = Object.keys(state).filter(
      (key) => key.endsWith('Workflow') && !key.startsWith('set')
    );
    const loadedMarker = { loadedFromOldOutputRoot: true };
    const loadedWorkflows = Object.fromEntries(
      workflowKeys.map((key) => [key, loadedMarker])
    );

    useWorkbenchStore.setState(loadedWorkflows as never);
    useWorkbenchStore.getState().resetProjectSession();

    for (const key of workflowKeys) {
      expect(useWorkbenchStore.getState()[key as keyof ReturnType<typeof useWorkbenchStore.getState>])
        .toBeNull();
    }
  });

  it('defaults Pokemon selection to the first real Pokemon instead of Egg', () => {
    useWorkbenchStore.setState({
      activeSection: 'health',
      pokemonWorkflow: null,
      selectedPokemonPersonalId: null
    });

    useWorkbenchStore.getState().setPokemonWorkflow({
      diagnostics: [],
      editableFields: [],
      evolutionMethodOptions: [],
      learnsetMoveOptions: [],
      pokemon: [
        { name: 'Egg', personalId: 0 },
        { name: 'Bulbasaur', personalId: 1 }
      ],
      stats: {
        presentPokemonCount: 1,
        totalLearnsetMoveCount: 1,
        totalPokemonCount: 2
      },
      summary: {
        availability: 'available',
        description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
        diagnostics: [],
        id: 'pokemon',
        label: 'Pokemon'
      }
    } as unknown as PokemonWorkflow);

    expect(useWorkbenchStore.getState().selectedPokemonPersonalId).toBe(1);
  });
});
