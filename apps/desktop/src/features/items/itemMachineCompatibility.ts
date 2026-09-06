/* SPDX-License-Identifier: GPL-3.0-only */

import type { ItemRecord, PokemonCompatibilityGroup, PokemonRecord, PokemonWorkflow } from '../../bridge/contracts';

type Family = 'swsh' | 'sv' | 'za';
export type ItemMachineCompatibilityRow = {
  entry: PokemonCompatibilityGroup['entries'][number];
  groupId: 'tm' | 'tr';
  pokemon: PokemonRecord;
};

export function itemMachineCompatibilityGroup(item: ItemRecord | null, family: Family): 'tm' | 'tr' | null {
  const slot = item?.metadata.machineSlot;
  if (slot === null || slot === undefined || !Number.isInteger(slot) || slot < 0) return null;
  if (family === 'swsh') return slot < 100 ? 'tm' : slot < 200 ? 'tr' : null;
  return 'tm';
}

export function getItemMachineCompatibilityRows(item: ItemRecord | null, workflow: PokemonWorkflow | null, family: Family): ItemMachineCompatibilityRow[] {
  const groupId = itemMachineCompatibilityGroup(item, family);
  const moveId = item?.metadata.machineMoveId;
  if (!groupId || !moveId || !workflow || !item) return [];
  // Sword/Shield stores separate TM/TR bits. Matching by move alone can select another machine.
  const slot = family === 'swsh' ? item.metadata.machineSlot! % 100 : null;
  return workflow.pokemon
    .filter(pokemon => pokemon.personalId !== 0 && pokemon.name.trim().toLowerCase() !== 'egg')
    .flatMap(pokemon => {
      const entry = pokemon.compatibility.find(group => group.groupId === groupId)?.entries.find(candidate =>
        candidate.moveId === moveId && (slot === null || candidate.slot === slot));
      return entry ? [{ entry, groupId, pokemon }] : [];
    })
    .sort((a, b) => a.pokemon.speciesId - b.pokemon.speciesId || a.pokemon.form - b.pokemon.form || a.pokemon.personalId - b.pokemon.personalId);
}

export function setItemMachineCompatibilityDrafts(current: Record<string, string>, personalIds: number[], enabled: boolean): Record<string, string> {
  const next = { ...current };
  for (const personalId of personalIds) next[personalId.toString()] = enabled ? '1' : '0';
  return next;
}
