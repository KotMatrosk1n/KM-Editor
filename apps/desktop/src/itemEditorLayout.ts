/* SPDX-License-Identifier: GPL-3.0-only */

import type { ItemRecord } from './bridge/contracts';

export const itemPriceFields = [
  ['buyPrice', 'Buy'], ['sellPrice', 'Sell'], ['wattsPrice', 'Watts'],
  ['alternatePrice', 'Alt'], ['price', 'Price'], ['megaShardPrice', 'Mega Shards'],
  ['colorfulScrewPrice', 'Colorful Screws'], ['meritPrice', 'Merit points']
] as const;

const effectPairs = [
  ['hpEvFlag', 'evHp'], ['attackEvFlag', 'evAttack'], ['defenseEvFlag', 'evDefense'],
  ['speedEvFlag', 'evSpeed'], ['specialAttackEvFlag', 'evSpecialAttack'],
  ['specialDefenseEvFlag', 'evSpecialDefense'], ['friendship1Flag', 'friendshipGain1'],
  ['friendship2Flag', 'friendshipGain2'], ['friendship3Flag', 'friendshipGain3']
] as const;

const boostLabels = [
  ['attackBoost', 'Attack'], ['defenseBoost', 'Defense'], ['specialAttackBoost', 'Sp. Atk'],
  ['specialDefenseBoost', 'Sp. Def'], ['speedBoost', 'Speed'], ['accuracyBoost', 'Accuracy'],
  ['criticalHitBoost', 'Crit']
] as const;
const useFlags1 = ['restorePpFlag', 'restoreAllPpFlag', 'restoreHpFlag', 'hpEvFlag', 'attackEvFlag',
  'defenseEvFlag', 'speedEvFlag', 'specialAttackEvFlag'];
const useFlags2 = ['specialDefenseEvFlag', 'evAbove100Flag', 'friendship1Flag', 'friendship2Flag', 'friendship3Flag'];
const cureFields = ['cureSleep', 'curePoison', 'cureBurn', 'cureFreeze', 'cureParalysis', 'cureParalyze',
  'cureConfusion', 'cureConfuse', 'cureInfatuation', 'guardSpec'];
const inventoryFields = ['pouch', 'pouchFlags', 'pocket', 'stackCap', 'maxQuantity', 'sortIndex', 'sortOrder'];
const codeFields = ['itemType', 'itemSprite', 'groupType', 'groupIndex'];
const machineFields = ['tmNumber', 'machineMoveId'];
const fieldUseFields = ['fieldUseType', 'canUseOnPokemon', 'canTargetFaintedPokemon', 'revivesWholeParty',
  'levelUpItem', 'evolutionItem', 'formChangeItem', 'maxUseLevel', 'autoHealPriority'];
const battleFields = ['battlePouch', 'flingPower', 'canUseInBattle', 'canNotHold', 'equipPower'];
const plainEffects = ['healAmount', 'healPower', 'healPercentage', 'revivalCount', 'revivePercentage',
  'ppGain', 'mintNature', 'expPointGain', 'swapIntoItem'];
const effectFields = [...effectPairs.flat(), ...plainEffects, ...boostLabels.map(([key]) => key), 'effectGuard'];
const flagFields = [...cureFields, ...useFlags1, ...useFlags2, 'ppUpFlag', 'ppMaxFlag']
  .filter(key => !effectPairs.some(([flag]) => flag === key));

export function isItemMachineField(key: string) {
  return machineFields.includes(key);
}

export function itemDataGroups<T>(fields: readonly T[], keyFor: (field: T) => string) {
  const orderedGroups = [
    { id: 'prices', label: 'Prices', keys: itemPriceFields.map(([key]) => key) },
    { id: 'inventory', label: 'Inventory', keys: inventoryFields },
    { id: 'codes', label: 'Item Codes', keys: codeFields },
    { id: 'use', label: 'Field & Battle', keys: [...fieldUseFields, ...battleFields] },
    { id: 'machine', label: 'Technical Machine', keys: machineFields },
    { id: 'flags', label: 'Use & Status Flags', keys: flagFields, disclosure: true },
    { id: 'effects', label: 'Pokemon Effects & Boosts', keys: effectFields, disclosure: true },
    { id: 'raw', label: 'Advanced / Raw', keys: [], disclosure: true }
  ];
  const keyGroups = new Map(orderedGroups.flatMap(group => group.keys.map(key => [key, group.id] as const)));
  return orderedGroups.flatMap(({ id, label, disclosure }) => {
    const members = fields.filter(field => (keyGroups.get(keyFor(field)) ?? 'raw') === id);
    const byKey = new Map(members.map(field => [keyFor(field), field]));
    const pairedKeys = new Set<string>(effectPairs.filter(([flag, gain]) => byKey.has(flag) && byKey.has(gain)).map(([flag]) => flag));
    const rows = members.flatMap(field => {
      const key = keyFor(field);
      if (pairedKeys.has(key)) return [];
      const pair = effectPairs.find(([, gain]) => gain === key);
      const flag = pair && byKey.get(pair[0]);
      return [{ fields: flag ? [flag, field] : [field] }];
    });
    return rows.length ? [{ id, label, disclosure, rows }] : [];
  });
}

export type ItemBehaviorValue = { key: string; label: string; value: string | number; literal?: boolean; enabled?: boolean };
export type ItemBehaviorGroup = { label: string; rows: ItemBehaviorValue[]; boosts?: ItemBehaviorValue[]; flags?: { key: string; label: string; flags: string[] }[] };

type BehaviorField = {
  field: string;
  label: string;
  valueKind: string;
  options: readonly { value: number; label: string }[];
};

export function describeItemBehavior(
  item: ItemRecord,
  family: 'swsh' | 'sv' | 'za',
  fields: readonly BehaviorField[],
  getCurrent: (key: string) => number | null,
  drafts: readonly { field: string; value: string }[],
  getItemName: (id: number) => string | undefined
): ItemBehaviorGroup[] {
  const values = new Map(drafts.map(change => [change.field, Number(change.value)]));
  const value = (key: string) => values.get(key) ?? getCurrent(key);
  const definitions = new Map(fields.map(field => [field.field, field]));
  if (family === 'swsh') {
    // Each price setter writes the same underlying buy price.
    for (const change of drafts) {
      if (change.field === 'buyPrice' || change.field === 'sellPrice') {
        const buyPrice = Number(change.value) * (change.field === 'sellPrice' ? 2 : 1);
        values.set('buyPrice', buyPrice);
        values.set('sellPrice', Math.floor(buyPrice / 2));
      }
    }
  }
  if (family === 'sv') {
    // These controls are aliases for the same fields in the S/V item table.
    let fieldFunction = getCurrent('fieldUseType') ?? 0;
    for (const change of drafts) {
      if (change.field === 'fieldUseType') fieldFunction = Number(change.value);
      if (change.field === 'canUseOnPokemon') fieldFunction = change.value === '0' ? 0 : fieldFunction || 1;
      if (change.field === 'evHp' || change.field === 'healAmount') {
        values.set('evHp', Number(change.value));
        values.set('healAmount', Number(change.value));
      }
    }
    if (values.has('fieldUseType') || values.has('canUseOnPokemon')) {
      values.set('fieldUseType', fieldFunction);
      values.set('canUseOnPokemon', Number([1, 2, 6, 10, 12, 15].includes(fieldFunction)));
    }
  }
  if (family === 'za' && !item.metadata.isProjectedTechnicalMachineSlot) {
    const enableUse = [...cureFields.filter(key => key !== 'guardSpec'), 'healPower', 'healPercentage',
      'revivalCount', 'revivePercentage', 'expPointGain', 'evolutionItem', 'formChangeItem',
      ...effectPairs.slice(0, 6).map(([, key]) => key)];
    if (values.has('mintNature') || enableUse.some(key => values.has(key))) {
      values.set('canUseOnPokemon', Number((value('mintNature') ?? -1) >= 0 ||
        enableUse.some(key => (value(key) ?? 0) !== 0)));
    }
  }

  const row = (key: string, label: string, content: string | number, literal?: boolean): ItemBehaviorValue =>
    ({ key, label, value: content, literal });
  const visibleValue = (key: string, label?: string): ItemBehaviorValue[] => {
    const field = definitions.get(key);
    const amount = value(key);
    if (!field || amount === null) return [];
    const title = label ?? field.label;
    if (field.valueKind === 'boolean') return [row(key, title, amount ? 'Yes' : 'No', true)];
    if (family === 'swsh' && key === 'healAmount') {
      const sentinels: Record<number, string> = { 253: 'Quarter HP', 254: 'Half HP', 255: 'Full HP' };
      return [row(key, title, sentinels[amount] ?? `${amount} HP`, true)];
    }
    if (key === 'pouchFlags') return [row(key, title, `0x${amount.toString(16).toUpperCase()}`)];
    if (key === 'swapIntoItem') {
      if (amount === 0) return [row(key, title, 'None', true)];
      const name = getItemName(amount);
      return [row(key, title, name ? `${amount} ${name}` : amount)];
    }
    const option = field.options.find(entry => entry.value === amount);
    return [row(key, title, option?.label ?? amount, Boolean(option))];
  };
  const select = (keys: readonly string[]) => keys.flatMap(key => visibleValue(key));
  const extra = (label: string) => {
    const detail = item.detailGroups.flatMap(group => group.details).find(entry => entry.label === label);
    return detail?.value ? [row(label, label, detail.value)] : [];
  };
  const decoded = (keys: readonly string[], label: string, unknown: string[] = []) => {
    if (!keys.some(key => definitions.has(key) && value(key) !== null)) return [];
    const active = keys.flatMap(key => {
      const field = definitions.get(key);
      return field && value(key) === 1 ? [field.label] : [];
    });
    return [{ key: label, label, flags: [...active, ...unknown] }];
  };
  const flagRows = [
    ...decoded(useFlags1, 'Use flags 1 (decoded)'),
    ...decoded(useFlags2, 'Use flags 2 (decoded)', [5, 6, 7].filter(bit => item.metadata.useFlags2 & (1 << bit)).map(bit => `Unknown ${bit}`))
  ];
  const cures = decoded(cureFields, 'Cures status');
  const primaryPrice = value(family === 'za' ? 'price' : 'buyPrice');
  const sellEstimate = primaryPrice !== null && family !== 'swsh'
    ? [row('sellEstimate', 'Sell estimate', Math.floor(primaryPrice / 2))] : [];
  const gains = effectPairs.flatMap(([flag, gain]) => {
    const amounts = visibleValue(gain);
    if (!amounts.length) return visibleValue(flag);
    if (!definitions.has(flag) || value(flag) === null) return amounts;
    return amounts.map(amount => ({ ...amount, enabled: value(flag) === 1 }));
  });
  return [
    { label: 'Inventory', rows: [...select(itemPriceFields.map(([key]) => key)), ...sellEstimate,
      ...select([...inventoryFields, ...codeFields, ...machineFields]),
      ...extra('Internal token'), ...extra('Icon'),
      ...(family === 'swsh' && item.metadata.machineSlot !== null && value('machineMoveId') === null
        ? extra('Machine') : [])] },
    { label: 'Field Use', rows: select(fieldUseFields), flags: flagRows },
    { label: 'Battle', rows: [...select([...battleFields, 'ppUpFlag', 'ppMaxFlag', 'effectGuard']), ...extra('Battle function')],
      boosts: boostLabels.flatMap(([key, label]) => {
        const amount = value(key);
        return definitions.has(key) && amount !== null ? [row(key, label, amount)] : [];
      }), flags: cures },
    { label: 'Pokemon Effects', rows: [...select(plainEffects), ...gains] }
  ];
}
