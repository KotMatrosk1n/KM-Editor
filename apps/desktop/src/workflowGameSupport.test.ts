/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import {
  canAccessWorkflowSectionForHealth,
  getGameScopedWorkflowSummaries,
  isWorkflowNavigationVisibleForGame,
  readOnlyViewerSectionIds,
  workflowNavigationGroups
} from './workflowGameSupport';
import type { WorkbenchSection } from './workbenchStore';

describe('workflow game support', () => {
  it('routes Rental Pokemon only for Sword and Shield', () => {
    const availableSections = new Set<WorkbenchSection>(['rentalPokemon']);
    const encounterGroup = workflowNavigationGroups.find(
      (group) => group.id === 'encountersPokemonSources'
    );

    expect(encounterGroup?.sectionIds).toContain('rentalPokemon');
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'sword', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'shield', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'scarlet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'violet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'za', availableSections)
    ).toBe(false);
  });

  it('hides the generic ExeFS patch manager even when a backend advertises it', () => {
    const availableSections = new Set<WorkbenchSection>(['exefsPatches', 'flagworkSave']);
    const advancedGroup = workflowNavigationGroups.find(
      (group) => group.id === 'advancedEditors'
    );

    expect(advancedGroup?.sectionIds).not.toContain('exefsPatches');
    expect(
      getGameScopedWorkflowSummaries(
        [
          {
            availability: 'readOnly',
            description: 'Internal executable patch diagnostics.',
            diagnostics: [],
            id: 'exefsPatches',
            label: 'ExeFS Patch Manager'
          }
        ],
        'sword'
      )
    ).toEqual([]);
    expect(
      isWorkflowNavigationVisibleForGame('exefsPatches', 'sword', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('exefsPatches', 'shield', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('exefsPatches', 'scarlet', availableSections)
    ).toBe(false);
  });

  it('identifies read-only viewers independently of hidden workflows', () => {
    expect(readOnlyViewerSectionIds).toEqual(new Set(['flagworkSave']));
    expect(canAccessWorkflowSectionForHealth('flagworkSave', true, false)).toBe(true);
    expect(canAccessWorkflowSectionForHealth('items', true, false)).toBe(false);
    expect(canAccessWorkflowSectionForHealth('items', true, true)).toBe(true);
  });

  it('routes Starting Items only for Sword and Shield', () => {
    const availableSections = new Set<WorkbenchSection>(['startingItems']);
    const advancedGroup = workflowNavigationGroups.find(
      (group) => group.id === 'advancedEditors'
    );

    expect(advancedGroup?.sectionIds).toContain('startingItems');
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'sword', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'shield', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'scarlet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'violet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'za', availableSections)
    ).toBe(false);
  });

  it('routes NPC Item Gift only for Sword and Shield', () => {
    const availableSections = new Set<WorkbenchSection>(['npcItemGift']);
    const advancedGroup = workflowNavigationGroups.find(
      (group) => group.id === 'advancedEditors'
    );

    expect(advancedGroup?.sectionIds).toContain('npcItemGift');
    expect(
      isWorkflowNavigationVisibleForGame('npcItemGift', 'sword', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('npcItemGift', 'shield', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('npcItemGift', 'scarlet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('npcItemGift', 'violet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('npcItemGift', 'za', availableSections)
    ).toBe(false);
  });
});
