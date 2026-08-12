/* SPDX-License-Identifier: GPL-3.0-only */

import type { ItemEditableField, ItemRecord } from './bridge/contracts';

export const zaKingsRockItemId = 221;
export const itemEquipPowerFieldName = 'equipPower';

export const zaKingsRockEquipPowerHelp =
  "Used only after a damaging hit that the target survives and when the move's native flinch chance is zero. " +
  'It does not add to, replace, or multiply a nonzero native chance.';

export const zaGenericEquipPowerHelp =
  'A handler-specific effect parameter whose meaning varies by item. Preserve it unless that item effect has been verified.';

export type ContextualItemEditableField = ItemEditableField & {
  group?: string;
  helpText?: string;
};

export function getContextualItemEditableFields(
  fields: ItemEditableField[],
  editorFamily: 'swsh' | 'sv' | 'za',
  item: ItemRecord | null
): ContextualItemEditableField[] {
  if (editorFamily !== 'za') {
    return fields;
  }

  return fields.map((field) => {
    if (field.field !== itemEquipPowerFieldName) {
      return field;
    }

    return item?.itemId === zaKingsRockItemId
      ? {
          ...field,
          group: 'Held Effect',
          helpText: zaKingsRockEquipPowerHelp,
          label: 'Added flinch chance (%)',
          maximumValue: 100,
          minimumValue: 0
        }
      : {
          ...field,
          group: 'Held Effect',
          helpText: zaGenericEquipPowerHelp,
          label: 'Effect power (handler-specific)'
        };
  });
}

export function getContextualItemEffectHelp(
  editorFamily: 'swsh' | 'sv' | 'za',
  item: ItemRecord | null
) {
  return editorFamily === 'za' && item?.itemId === zaKingsRockItemId
    ? zaKingsRockEquipPowerHelp
    : null;
}
