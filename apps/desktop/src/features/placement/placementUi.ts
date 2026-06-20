/* SPDX-License-Identifier: GPL-3.0-only */

import {
  type PlacedObjectRecord,
  type PlacementEditableField,
  type PlacementWorkflow
} from '../../bridge/contracts';

const placementLocationXFieldName = 'locationX';
const placementLocationYFieldName = 'locationY';
const placementLocationZFieldName = 'locationZ';
const placementRotationYFieldName = 'rotationY';
const placementItemIdFieldName = 'itemId';
const placementQuantityFieldName = 'quantity';
const placementChanceFieldName = 'chance';

export type PlacementFieldControl = {
  description?: string;
  displayValue: string;
  field: string;
  group: string;
  isReadOnly: boolean;
  label: string;
  maximumValue: number;
  minimumValue: number;
  options: Array<{ label: string; value: number }>;
  value: string;
  valueKind: string;
};

export function getPlacementCategories(workflow: PlacementWorkflow | null) {
  if (!workflow) return [];
  const hasStructuredCategoryIds = workflow.objects.some((object) => object.categoryId?.trim());
  if (workflow.categories?.length && hasStructuredCategoryIds) {
    return workflow.categories;
  }

  const counts = new Map<string, { description: string; id: string; label: string; objectCount: number }>();
  for (const object of workflow.objects) {
    const id = getPlacementCategoryId(object);
    const current = counts.get(id);
    if (current) current.objectCount += 1;
    else counts.set(id, {
      description: 'Placed object records.',
      id,
      label: getPlacementCategoryLabel(object, id),
      objectCount: 1
    });
  }

  return [...counts.values()];
}

export function getPlacementCategoryId(object: PlacedObjectRecord) {
  const categoryId = object.categoryId?.trim();
  return categoryId ? categoryId : getLegacyPlacementCategoryId(object);
}

export function getLegacyPlacementCategoryId(object: PlacedObjectRecord) {
  return object.objectType === 'HiddenItem' ? 'hiddenItems' : 'visibleItems';
}

export function getPlacementFieldControls(
  object: PlacedObjectRecord,
  editableFields: PlacementEditableField[]
): PlacementFieldControl[] {
  if (object.fields?.length) {
    return object.fields.map((value) => {
      const definition = editableFields.find((field) => field.field === value.field);
      const hasFieldMetadata =
        value.field.startsWith('raw.') ||
        value.valueKind !== 'text' ||
        value.minimumValue !== 0 ||
        value.maximumValue !== 0 ||
        value.description.trim().length > 0;
      return {
        description: value.description || definition?.description,
        displayValue: value.displayValue,
        field: value.field,
        group: value.group || definition?.group || 'Placement Data',
        isReadOnly: value.isReadOnly || definition?.isReadOnly === true,
        label: value.label || definition?.label || value.field,
        maximumValue: hasFieldMetadata
          ? value.maximumValue
          : definition?.maximumValue ?? Number.MAX_SAFE_INTEGER,
        minimumValue: hasFieldMetadata
          ? value.minimumValue
          : definition?.minimumValue ?? Number.MIN_SAFE_INTEGER,
        options: value.options ?? definition?.options ?? [],
        value: value.value,
        valueKind: hasFieldMetadata ? value.valueKind : definition?.valueKind ?? 'text'
      };
    });
  }

  return editableFields
    .filter((field) => isLegacyPlacementFieldVisible(object, field.field))
    .map((field) => {
      const value = getPlacementFieldValue(object, field.field) ?? '';
      return {
        description: field.description,
        displayValue: value,
        field: field.field,
        group: getLegacyPlacementFieldGroup(field),
        isReadOnly: field.isReadOnly === true,
        label: field.label,
        maximumValue: field.maximumValue,
        minimumValue: field.minimumValue,
        options: field.options ?? [],
        value,
        valueKind: field.valueKind
      };
    });
}

export function getPlacementFieldValue(object: PlacedObjectRecord, field: string) {
  const structuredField = object.fields?.find((candidate) => candidate.field === field);
  if (structuredField) return structuredField.value;

  switch (field) {
    case placementLocationXFieldName: return object.x.toString();
    case placementLocationYFieldName: return object.y.toString();
    case placementLocationZFieldName: return object.z.toString();
    case placementRotationYFieldName: return object.rotationY.toString();
    case placementItemIdFieldName: return object.itemId?.toString() ?? null;
    case placementQuantityFieldName: return object.quantity.toString();
    case placementChanceFieldName: return object.chance?.toString() ?? null;
    default: return null;
  }
}

export function formatPlacementPrimaryData(object: PlacedObjectRecord) {
  const fields = object.fields ?? [];
  const species =
    fields.find((field) => field.field.endsWith('.speciesId')) ??
    fields.find((field) => field.field.endsWith('.Species'));
  if (species) return species.displayValue || species.value;

  const table =
    fields.find((field) => field.field.endsWith('.tableKey')) ??
    fields.find((field) => field.field.endsWith('.label') && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.label.includes('Static Encounter') && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.label.includes('Symbol Encounter') && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.label.includes('Raid Table') && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.label.includes('Trainer Battle') && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.label.includes('Object Hash') && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.label.includes('Model Hash') && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.label.includes('Message Hash') && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.group === 'References' && hasUsefulPlacementDisplay(field));
  if (table) return getPlacementDisplayValue(table);

  const model =
    fields.find((field) => field.label === 'Model' && hasUsefulPlacementDisplay(field)) ??
    fields.find((field) => field.label === 'Model Hash' && hasUsefulPlacementDisplay(field));
  if (model) return getPlacementDisplayValue(model);

  if (object.scriptId) return object.scriptId;

  return object.itemId === null
    ? object.itemHash || object.itemName || object.objectType
    : `${object.itemName} (${object.itemId})`;
}

export function formatPlacementItem(object: PlacedObjectRecord) {
  if (isPokemonPlacementObject(object)) return formatPlacementPrimaryData(object);
  return object.itemId === null ? object.itemHash || object.itemName : `${object.itemName} (${object.itemId})`;
}

export function formatPlacementCoordinates(object: PlacedObjectRecord) {
  if (object.fields?.length) {
    const x = object.fields.find((field) => field.field === 'point.positionX');
    const y = object.fields.find((field) => field.field === 'point.positionY');
    const z = object.fields.find((field) => field.field === 'point.positionZ');
    if (x || y || z) return [x, y, z].map((field) => field?.displayValue || field?.value || 'Scene-only').join(', ');
  }

  return `${formatCoordinate(object.x)}, ${formatCoordinate(object.y)}, ${formatCoordinate(object.z)}`;
}

export function isPokemonPlacementObject(object: PlacedObjectRecord) {
  const categoryId = getPlacementCategoryId(object);
  return categoryId === 'fixedSymbols' || categoryId === 'coinSymbols' || categoryId === 'pokemonEncounters';
}

function getPlacementCategoryLabel(object: PlacedObjectRecord, categoryId: string) {
  const categoryLabel = object.categoryLabel?.trim();
  if (categoryLabel) return categoryLabel;
  if (categoryId === 'hiddenItems') return 'Hidden Items';
  if (categoryId === 'visibleItems') return 'Visible Items';
  return categoryId;
}

function isLegacyPlacementFieldVisible(object: PlacedObjectRecord, field: string) {
  if (field === placementChanceFieldName) return object.objectType === 'HiddenItem';
  if (field === placementItemIdFieldName) return object.itemId !== null || object.itemHash.length > 0;
  return [
    placementLocationXFieldName,
    placementLocationYFieldName,
    placementLocationZFieldName,
    placementRotationYFieldName,
    placementQuantityFieldName
  ].includes(field);
}

function getLegacyPlacementFieldGroup(field: PlacementEditableField) {
  if (field.group) return field.group;
  if ([placementItemIdFieldName, placementQuantityFieldName, placementChanceFieldName].includes(field.field)) {
    return 'Item';
  }

  if ([placementLocationXFieldName, placementLocationYFieldName, placementLocationZFieldName, placementRotationYFieldName].includes(field.field)) {
    return 'Position';
  }

  return 'Placement Data';
}

function formatCoordinate(value: number) {
  return Number.isInteger(value) ? value.toString() : value.toFixed(2);
}

function getPlacementDisplayValue(field: { displayValue: string; value: string }) {
  return field.displayValue || field.value;
}

function hasUsefulPlacementDisplay(field: { displayValue: string; value: string }) {
  const value = getPlacementDisplayValue(field).trim();
  return value.length > 0 &&
    value !== 'None' &&
    value !== 'None (empty hash)' &&
    value !== '0xCBF29CE484222645';
}
