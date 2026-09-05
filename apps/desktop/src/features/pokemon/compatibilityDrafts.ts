/* SPDX-License-Identifier: GPL-3.0-only */

import type { PokemonCompatibilityGroup } from '../../bridge/contracts';

export function isMoveListCompatibility(group: PokemonCompatibilityGroup) {
  return group.groupId === 'egg' || group.groupId === 'reminder';
}

export function compatibilityGroupField(group: PokemonCompatibilityGroup) {
  return `compatibilityGroup:${group.groupId}`;
}

export function compatibilityGroupValue(group: PokemonCompatibilityGroup) {
  return group.entries.filter(entry => entry.canLearn).map(entry => entry.moveId).join(',');
}

export function compatibilityEntryEnabled(group: PokemonCompatibilityGroup, slot: number, drafts: Record<string, string>) {
  const entry = group.entries.find(candidate => candidate.slot === slot);
  if (!entry) return false;
  return isMoveListCompatibility(group)
    ? (drafts[compatibilityGroupField(group)] ?? compatibilityGroupValue(group)).split(',').includes(String(entry.moveId))
    : (drafts[`compatibility:${group.groupId}:${slot}`] ?? (entry.canLearn ? '1' : '0')) === '1';
}

export function compatibilityBulkValues(group: PokemonCompatibilityGroup, action: 'enable' | 'disable' | 'vanilla'): Record<string, string> | null {
  if (action === 'vanilla' && group.entries.some(entry => entry.vanillaCanLearn == null)) return null;
  if (isMoveListCompatibility(group)) {
    if (action === 'vanilla' && group.vanillaMoveIds == null) return null;
    return { [compatibilityGroupField(group)]: (action === 'vanilla' ? group.vanillaMoveIds!
      : action === 'enable' ? group.entries.map(entry => entry.moveId) : []).join(',') };
  }
  return Object.fromEntries(group.entries.map(entry => [`compatibility:${group.groupId}:${entry.slot}`,
    (action === 'vanilla' ? entry.vanillaCanLearn : action === 'enable') ? '1' : '0']));
}

export function compatibilityToggleValues(group: PokemonCompatibilityGroup, slot: number, enabled: boolean, drafts: Record<string, string>) {
  if (!isMoveListCompatibility(group)) return { [`compatibility:${group.groupId}:${slot}`]: enabled ? '1' : '0' };
  const entry = group.entries.find(candidate => candidate.slot === slot);
  if (!entry) return {};
  const moves = (drafts[compatibilityGroupField(group)] ?? compatibilityGroupValue(group)).split(',').filter(Boolean);
  const next = moves.filter(move => move !== String(entry.moveId));
  if (enabled) next.push(String(entry.moveId));
  return { [compatibilityGroupField(group)]: next.join(',') };
}
