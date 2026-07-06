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

export type PlacementObjectGroup = {
  key: string;
  label: string;
  map: string;
  objects: PlacedObjectRecord[];
  position: string;
  preview: string;
};

export type PlacementObjectGroupTab = {
  label: string;
  objectId: string;
  title: string;
};

export type PlacementObjectSubgroup = {
  key: string;
  label: string;
  objects: PlacedObjectRecord[];
  tabs: PlacementObjectGroupTab[];
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

  if (getPlacementCategoryId(object) === 'pokemonSpawners') {
    return object.label;
  }

  if (getPlacementCategoryId(object) === 'itemBallSpawners') {
    return object.itemName || object.label;
  }

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
  return categoryId === 'fixedSymbols' ||
    categoryId === 'coinSymbols' ||
    categoryId === 'pokemonEncounters' ||
    categoryId === 'pokemonSpawners';
}

export function buildPlacementObjectGroups(
  objects: PlacedObjectRecord[],
  options: { groupItemBallSpawners?: boolean; groupPokemonSpawners?: boolean } = {}
): PlacementObjectGroup[] {
  const groups = new Map<string, { label: string; map: string; objects: PlacedObjectRecord[] }>();

  for (const object of objects) {
    const groupInfo =
      options.groupPokemonSpawners && isZaPokemonSpawnerPlacementObject(object)
        ? getZaPokemonSpawnerGroupInfo(object)
        : options.groupItemBallSpawners && isZaItemBallPlacementObject(object)
          ? getZaItemBallGroupInfo(object)
          : null;
    const key = groupInfo?.key ?? `object:${object.objectId}`;
    const group = groups.get(key);
    if (group) {
      group.objects.push(object);
      continue;
    }

    groups.set(key, {
      label: groupInfo?.label ?? object.label,
      map: groupInfo?.map ?? object.map,
      objects: [object]
    });
  }

  return [...groups.entries()].map(([key, group]) => {
    const objects = [...group.objects].sort(comparePlacementObjectsForGroup);
    const objectGroup = {
      key,
      label: group.label,
      map: group.map,
      objects,
      position: formatPlacementGroupPosition(objects),
      preview: ''
    };
    return {
      ...objectGroup,
      preview: formatPlacementGroupPreview(objectGroup)
    };
  });
}

export function getPlacementObjectGroupTabs(
  group: Pick<PlacementObjectGroup, 'label' | 'map'> & Partial<Pick<PlacementObjectGroup, 'objects'>>,
  objects?: PlacedObjectRecord[],
  subgroupLabel = ''
): PlacementObjectGroupTab[] {
  const groupObjects = objects ?? group.objects ?? [];
  return groupObjects.map((object, index) => createPlacementObjectGroupTab(group, object, index, subgroupLabel));
}

export function getPlacementObjectSubgroups(group: PlacementObjectGroup): PlacementObjectSubgroup[] {
  const subgroups = new Map<string, { label: string; objects: PlacedObjectRecord[] }>();

  for (const object of group.objects) {
    const subgroupInfo = getPlacementObjectSubgroupInfo(group, object);
    const current = subgroups.get(subgroupInfo.key);
    if (current) {
      current.objects.push(object);
      continue;
    }

    subgroups.set(subgroupInfo.key, {
      label: subgroupInfo.label,
      objects: [object]
    });
  }

  return [...subgroups.entries()]
    .map(([key, subgroup]) => {
      const objects = [...subgroup.objects].sort((left, right) =>
        comparePlacementObjectsWithinSubgroup(group, subgroup.label, left, right)
      );
      return {
        key,
        label: subgroup.label,
        objects,
        tabs: getPlacementObjectGroupTabs(group, objects, subgroup.label)
      };
    })
    .sort(comparePlacementSubgroups);
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

function isZaPokemonSpawnerPlacementObject(object: PlacedObjectRecord) {
  return getPlacementCategoryId(object) === 'pokemonSpawners' ||
    object.objectType.toLocaleLowerCase().includes('pokemon spawner');
}

export function isZaItemBallPlacementObject(object: PlacedObjectRecord) {
  return getPlacementCategoryId(object) === 'itemBallSpawners' ||
    object.objectType.toLocaleLowerCase().includes('item ball spawner');
}

function getZaPokemonSpawnerGroupInfo(object: PlacedObjectRecord) {
  const bossLabel = getZaBossPlacementBaseLabel(object.label) ??
    getZaBossPlacementBaseLabel(object.map);
  if (bossLabel) {
    return {
      key: `za:pokemon-spawner:boss:${normalizePlacementGroupKey(bossLabel)}`,
      label: bossLabel,
      map: 'Boss Battles'
    };
  }

  const dungeonInfo = getZaDimensionDungeonPlacementInfo(object);
  if (dungeonInfo) {
    return {
      key: `za:pokemon-spawner:dimension-dungeon:${dungeonInfo.dungeonNumber}`,
      label: dungeonInfo.dungeonLabel,
      map: object.categoryLabel || object.objectType
    };
  }

  const semanticLocation = getZaSemanticPlacementMajorLocation(object);
  if (semanticLocation) {
    return {
      key: `za:pokemon-spawner:location:${normalizePlacementGroupKey(semanticLocation)}`,
      label: semanticLocation,
      map: object.categoryLabel || object.objectType
    };
  }

  const map = object.map.trim();
  if (map && map !== object.categoryLabel) {
    const majorLocation = getZaPlacementMajorLocation(object);
    if (majorLocation && majorLocation !== map) {
      return {
        key: `za:pokemon-spawner:location:${normalizePlacementGroupKey(majorLocation)}`,
        label: majorLocation,
        map: object.categoryLabel || object.objectType
      };
    }

    return {
      key: `za:pokemon-spawner:map:${normalizePlacementGroupKey(map)}`,
      label: map,
      map
    };
  }

  const label = object.label.trim() || object.objectType;
  return {
    key: `za:pokemon-spawner:object:${normalizePlacementGroupKey(label)}`,
    label,
    map: object.categoryLabel || object.objectType
  };
}

function getZaItemBallGroupInfo(object: PlacedObjectRecord) {
  const semanticLocation = getZaSemanticPlacementMajorLocation(object);
  if (semanticLocation) {
    return {
      key: `za:item-ball-spawner:location:${normalizePlacementGroupKey(semanticLocation)}`,
      label: semanticLocation,
      map: object.categoryLabel || object.objectType
    };
  }

  const map = object.map.trim();
  if (map && map !== object.categoryLabel) {
    const majorLocation = getZaPlacementMajorLocation(object);
    if (majorLocation && majorLocation !== map) {
      return {
        key: `za:item-ball-spawner:location:${normalizePlacementGroupKey(majorLocation)}`,
        label: majorLocation,
        map: object.categoryLabel || object.objectType
      };
    }

    return {
      key: `za:item-ball-spawner:map:${normalizePlacementGroupKey(map)}`,
      label: map,
      map: object.categoryLabel || object.objectType
    };
  }

  const label = getZaItemBallBaseLabel(object.label) ?? object.objectType;
  return {
    key: `za:item-ball-spawner:object:${normalizePlacementGroupKey(label)}`,
    label,
    map: object.categoryLabel || object.objectType
  };
}

function getZaItemBallBaseLabel(label: string) {
  const trimmed = label.trim();
  const match = trimmed.match(/^(.*?)(?:\s+Item Ball(?:\s+\d+)?)(?::.*)?$/i);
  return match?.[1]?.trim() || null;
}

function getZaPlacementMajorLocation(object: PlacedObjectRecord) {
  for (const label of [object.map, object.label]) {
    const trimmed = label.trim();
    if (!trimmed || trimmed === object.categoryLabel || trimmed === object.objectType) {
      continue;
    }

    if (parseZaDimensionDungeonPlacement(label)) {
      continue;
    }

    const mainDungeon = parseZaMainDungeonPlacement(label);
    if (mainDungeon) {
      return mainDungeon.dungeonLabel;
    }

    const wildZone = parseZaWildZonePlacement(label);
    if (wildZone?.zoneLabel) {
      return wildZone.zoneLabel;
    }

    const event = parseZaEventPlacement(label);
    if (event?.eventLabel) {
      return event.eventLabel;
    }

    const testSpawner = parseZaTestSpawnerPlacement(label);
    if (testSpawner?.groupLabel) {
      return testSpawner.groupLabel;
    }

    const majorLocation = parseZaPlacementMajorLocation(label);
    if (majorLocation && majorLocation !== trimmed) {
      return majorLocation;
    }
  }

  return null;
}

function getZaSemanticPlacementMajorLocation(object: PlacedObjectRecord) {
  const majorLocation = getZaPlacementMajorLocation(object);
  const objectLabel = object.label.trim();
  const objectMap = object.map.trim();
  return majorLocation &&
    majorLocation !== objectLabel &&
    majorLocation !== objectMap &&
    majorLocation !== object.categoryLabel
    ? majorLocation
    : null;
}

function parseZaPlacementMajorLocation(label: string) {
  const trimmed = label.trim();
  if (!trimmed) {
    return null;
  }

  const districtMatch = trimmed.match(/^([^,]+ District)\b/i);
  if (districtMatch?.[1]) {
    return districtMatch[1].trim();
  }

  const wildZoneMatch = trimmed.match(/^(Wild Zone\s+\d+)\b/i);
  if (wildZoneMatch?.[1]) {
    return wildZoneMatch[1].trim();
  }

  const lumioseInteriorMatch = trimmed.match(/^(Lumiose City),\s*Interior Area\b/i);
  if (lumioseInteriorMatch?.[1]) {
    return `${lumioseInteriorMatch[1].trim()} Interiors`;
  }

  const commaIndex = trimmed.indexOf(',');
  if (commaIndex > 0) {
    return trimmed.slice(0, commaIndex).trim();
  }

  return trimmed;
}

function getZaDimensionDungeonPlacementInfo(object: PlacedObjectRecord) {
  for (const label of [object.map, object.label]) {
    const info = parseZaDimensionDungeonPlacement(label);
    if (info) {
      return info;
    }
  }

  return null;
}

function parseZaDimensionDungeonPlacement(label: string) {
  const normalized = label
    .trim()
    .replace(/_/g, ' ')
    .replace(/\s+/g, ' ');
  if (!normalized) {
    return null;
  }

  const formattedMatch = normalized.match(
    /^Dimension Dungeon\s+(\d+)\s+(Variant|Special Area|Pokemon Set)\s+(\d+)(?:\s+(.*))?$/i
  );
  if (formattedMatch?.[1] && formattedMatch[2] && formattedMatch[3]) {
    const variantKind = formatDimensionDungeonSectionKind(formattedMatch[2]);
    return {
      dungeonLabel: `Dimension Dungeon ${formatPlacementNumber(formattedMatch[1])}`,
      dungeonNumber: formatPlacementNumber(formattedMatch[1]),
      tailLabel: formatDimensionDungeonTail(formattedMatch[4] ?? ''),
      variantLabel: `${variantKind} ${formatPlacementNumber(formattedMatch[3])}`
    };
  }

  const rawMatch = normalized.match(
    /^(?:spn\s+)?(?:random\s+)?zdm(\d+)\s+(v|sp|poke)0*(\d+)(?:\s+(.*))?$/i
  );
  if (rawMatch?.[1] && rawMatch[2] && rawMatch[3]) {
    const variantKind = rawMatch[2].toLocaleLowerCase() === 'sp'
      ? 'Special Area'
      : rawMatch[2].toLocaleLowerCase() === 'poke'
        ? 'Pokemon Set'
        : 'Variant';
    return {
      dungeonLabel: `Dimension Dungeon ${formatPlacementNumber(rawMatch[1])}`,
      dungeonNumber: formatPlacementNumber(rawMatch[1]),
      tailLabel: formatDimensionDungeonTail(rawMatch[4] ?? ''),
      variantLabel: `${variantKind} ${formatPlacementNumber(rawMatch[3])}`
    };
  }

  return null;
}

function formatDimensionDungeonSectionKind(value: string) {
  const normalized = value.toLocaleLowerCase();
  if (normalized === 'special area') {
    return 'Special Area';
  }

  if (normalized === 'pokemon set') {
    return 'Pokemon Set';
  }

  return 'Variant';
}

function getZaMainDungeonPlacementInfo(object: PlacedObjectRecord) {
  for (const label of [object.map, object.label]) {
    const info = parseZaMainDungeonPlacement(label);
    if (info) {
      return info;
    }
  }

  return null;
}

function parseZaMainDungeonPlacement(label: string) {
  const normalized = label
    .trim()
    .replace(/_/g, ' ')
    .replace(/\s+/g, ' ');
  if (!normalized) {
    return null;
  }

  const formattedMatch = normalized.match(/^Dungeon\s+(\d+)(?:\s+Floor\s+(\d+))?(?:\s+(.*))?$/i);
  if (formattedMatch?.[1]) {
    const dungeonNumber = formatPlacementNumber(formattedMatch[1]);
    return {
      dungeonLabel: `Dungeon ${dungeonNumber}`,
      dungeonNumber,
      floorLabel: formattedMatch[2] ? `Floor ${formatPlacementNumber(formattedMatch[2])}` : 'Dungeon Items',
      tailLabel: formatMainDungeonTail(formattedMatch[3] ?? '')
    };
  }

  const rawMatch = normalized.match(/^(?:spn|itd)\s+d0*(\d+)(?:\s+0*(\d+))?(?:\s+(.*))?$/i);
  if (rawMatch?.[1]) {
    const dungeonNumber = formatPlacementNumber(rawMatch[1]);
    const floorNumber = rawMatch[2] ? formatPlacementNumber(rawMatch[2]) : '';
    return {
      dungeonLabel: `Dungeon ${dungeonNumber}`,
      dungeonNumber,
      floorLabel: floorNumber ? `Floor ${floorNumber}` : 'Dungeon Items',
      tailLabel: formatMainDungeonTail(rawMatch[3] ?? '')
    };
  }

  return null;
}

function formatMainDungeonTail(tail: string) {
  const normalized = tail.trim().replace(/_/g, ' ').replace(/\s+/g, ' ');
  if (!normalized) {
    return '';
  }

  if (/^Item\s+\d+/i.test(normalized) || /^Spawn Point\s+\w+/i.test(normalized)) {
    return normalized;
  }

  return normalized
    .split(' ')
    .map((token) => {
      if (/^\d+[a-z]?$/i.test(token)) {
        return `Spawn Point ${token.toUpperCase()}`;
      }

      if (/^ev$/i.test(token)) {
        return 'Event';
      }

      return formatDimensionDungeonTailToken(token);
    })
    .filter(Boolean)
    .join(' ');
}

function parseZaWildZonePlacement(label: string) {
  const normalized = label
    .trim()
    .replace(/_/g, ' ')
    .replace(/\s+/g, ' ');
  if (!normalized) {
    return null;
  }

  const formattedMatch = normalized.match(/^(Wild Zone\s+\d+)(?:\s+(.*))?$/i);
  if (formattedMatch?.[1]) {
    return {
      tailLabel: formatWildZonePlacementTail(formattedMatch[2] ?? ''),
      zoneLabel: formatPlacementTitleWords(formattedMatch[1])
    };
  }

  const rawMatch = normalized.match(/^(?:spn\s+)?a\d{4}\s+w\d+(?:\s+(.*))?$/i);
  if (rawMatch) {
    return {
      tailLabel: formatWildZonePlacementTail(rawMatch[1] ?? ''),
      zoneLabel: ''
    };
  }

  return null;
}

function formatWildZonePlacementTail(tail: string) {
  const normalized = tail.trim().replace(/_/g, ' ').replace(/\s+/g, ' ');
  if (!normalized) {
    return '';
  }

  if (/^Variant\s+\d+/i.test(normalized)) {
    return normalized;
  }

  return normalized
    .split(' ')
    .map((token) => {
      const variantMatch = token.match(/^v0*(\d+)$/i);
      if (variantMatch?.[1]) {
        return `Variant ${formatPlacementNumber(variantMatch[1])}`;
      }

      if (/^\d+[a-z]?$/i.test(token)) {
        return `Spawn Point ${token.toUpperCase()}`;
      }

      return formatDimensionDungeonTailToken(token);
    })
    .filter(Boolean)
    .join(' ');
}

function parseZaEventPlacement(label: string) {
  const normalized = label.trim().replace(/\s+/g, ' ');
  const match = normalized.match(
    /^(Story Chapter Event\s+\d+|Side Mission Event\s+\d+|Rest Event\s+\d+|DLC Event\s+\d+(?:\.\d+)?|Story Event\s+[A-Za-z ]+)(?:\s+(.*))?$/i
  );
  if (!match?.[1]) {
    return null;
  }

  return {
    eventLabel: formatPlacementTitleWords(match[1]),
    tailLabel: formatMainDungeonTail(match[2] ?? '')
  };
}

function parseZaTestSpawnerPlacement(label: string) {
  const normalized = label
    .trim()
    .replace(/_/g, ' ')
    .replace(/\s+/g, ' ');
  const match = normalized.match(/^test pokemon spawner(?:\s+(.*))?$/i);
  if (!match) {
    return null;
  }

  const tail = (match[1] ?? '').trim();
  return {
    groupLabel: 'Test Pokemon Spawners',
    subgroupLabel: 'Test Objects',
    tailLabel: tail ? formatPlacementTitleWords(tail) : 'Test Object'
  };
}

function formatPlacementTitleWords(value: string) {
  return value
    .split(' ')
    .filter(Boolean)
    .map((part) => (/^\d+(?:\.\d+)?$/.test(part) ? part : formatPlacementTitleToken(part)))
    .join(' ');
}

function formatDimensionDungeonTail(tail: string) {
  const normalized = tail.trim().replace(/_/g, ' ').replace(/\s+/g, ' ');
  if (!normalized) {
    return '';
  }

  if (
    /^(?:Alpha\s+)?Spawn Point\s+\d+/i.test(normalized) ||
    /^Follower(?:\s+\d+)?$/i.test(normalized)
  ) {
    return normalized;
  }

  const tokens = normalized.split(' ');
  return tokens.map(formatDimensionDungeonTailToken).filter(Boolean).join(' ');
}

function formatDimensionDungeonTailToken(token: string) {
  if (/^\d+$/.test(token)) {
    return `Spawn Point ${token}`;
  }

  const alphaMatch = token.match(/^a(\d+)$/i);
  if (alphaMatch?.[1]) {
    return `Alpha Spawn Point ${alphaMatch[1]}`;
  }

  if (/^follower\d*$/i.test(token)) {
    const followerNumber = token.replace(/^follower/i, '');
    return followerNumber ? `Follower ${followerNumber}` : 'Follower';
  }

  return token
    .split(/[-\s]/)
    .filter(Boolean)
    .map(formatPlacementTitleToken)
    .join(' ');
}

function formatPlacementNumber(value: string) {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed.toString() : value;
}

function formatPlacementTitleToken(value: string) {
  const lower = value.toLocaleLowerCase();
  return lower.charAt(0).toLocaleUpperCase() + lower.slice(1);
}

function getPlacementObjectSubgroupInfo(
  group: Pick<PlacementObjectGroup, 'label' | 'map'>,
  object: PlacedObjectRecord
) {
  if (group.map === 'Boss Battles') {
    return {
      key: 'subgroup:general',
      label: 'General'
    };
  }

  const subgroupLabel = formatPlacementSubgroupLabel(group, object);
  return {
    key: `subgroup:${normalizePlacementGroupKey(subgroupLabel)}`,
    label: subgroupLabel
  };
}

function formatPlacementSubgroupLabel(
  group: Pick<PlacementObjectGroup, 'label' | 'map'>,
  object: PlacedObjectRecord
) {
  const dungeonInfo = getZaDimensionDungeonPlacementInfo(object);
  if (dungeonInfo?.variantLabel) {
    return dungeonInfo.variantLabel;
  }

  const mainDungeonInfo = getZaMainDungeonPlacementInfo(object);
  if (mainDungeonInfo?.floorLabel) {
    return mainDungeonInfo.floorLabel;
  }

  const wildZoneInfo = parseZaWildZonePlacement(object.map) ?? parseZaWildZonePlacement(object.label);
  if (wildZoneInfo?.tailLabel) {
    const variantMatch = wildZoneInfo.tailLabel.match(/^(Variant\s+\d+)/i);
    return variantMatch?.[1] ?? 'Spawners';
  }

  const eventInfo = parseZaEventPlacement(object.map) ?? parseZaEventPlacement(object.label);
  if (eventInfo?.eventLabel) {
    return eventInfo.eventLabel === group.label ? 'Event Spawners' : eventInfo.eventLabel;
  }

  const testSpawnerInfo = parseZaTestSpawnerPlacement(object.map) ?? parseZaTestSpawnerPlacement(object.label);
  if (testSpawnerInfo?.subgroupLabel) {
    return testSpawnerInfo.subgroupLabel;
  }

  const map = object.map.trim();
  const label = object.label.trim();
  const strippedMap = stripPlacementGroupPrefix(map, group.label);
  if (strippedMap && strippedMap !== map) {
    return strippedMap;
  }

  const strippedLabel = stripPlacementGroupPrefix(label, group.label);
  const withoutItemBall = strippedLabel
    .replace(/\s+Item Ball(?:\s+\d+)?(?::.*)?$/i, '')
    .trim();
  const withoutSpawner = withoutItemBall
    .replace(/,\s*(?:Spawner|Transform)\s+\d+.*$/i, '')
    .replace(/\s+(?:Spawner|Transform)\s+\d+.*$/i, '')
    .trim();

  if (withoutSpawner && withoutSpawner !== label && withoutSpawner !== group.label) {
    return withoutSpawner;
  }

  if (map && map !== group.label && map !== group.map) {
    return map;
  }

  return 'General';
}

function getZaBossPlacementBaseLabel(label: string) {
  const trimmed = label.trim();
  if (!/^Boss Battle\b/i.test(trimmed)) {
    return null;
  }

  return trimmed
    .replace(
      /\s+(?:Phase\s+\d+|Rematch|Simulation(?:\s+\d+)?|Rush(?:\s+\d+)?|Dimension|Variant\s+[A-Z]|Y|Z)(?:\b.*)?$/i,
      ''
    )
    .trim();
}

function comparePlacementObjectsForGroup(left: PlacedObjectRecord, right: PlacedObjectRecord) {
  const leftRank = getPlacementObjectSortRank(left.label);
  const rightRank = getPlacementObjectSortRank(right.label);
  if (leftRank !== rightRank) {
    return leftRank - rightRank;
  }

  return left.label.localeCompare(right.label) ||
    left.objectIndex - right.objectIndex ||
    left.objectId.localeCompare(right.objectId);
}

function getPlacementObjectSortRank(label: string) {
  if (/\bPhase\s+\d+\b/i.test(label)) {
    return 10 + (parseFirstInteger(label) ?? 0);
  }

  if (/\bRematch\b/i.test(label)) {
    return 30;
  }

  if (/\bSimulation(?:\s+\d+)?\b/i.test(label)) {
    return 40 + (parseFirstInteger(label) ?? 0);
  }

  if (/\bRush(?:\s+\d+)?\b/i.test(label)) {
    return 50 + (parseFirstInteger(label) ?? 0);
  }

  if (/\bAlpha\b/i.test(label)) {
    return 60;
  }

  return parseFirstInteger(label) ?? 1000;
}

function formatPlacementGroupPreview(group: PlacementObjectGroup) {
  if (group.objects.length === 1) {
    const dungeonInfo = getZaDimensionDungeonPlacementInfo(group.objects[0]!);
    if (dungeonInfo) {
      return [dungeonInfo.variantLabel, dungeonInfo.tailLabel].filter(Boolean).join(': ');
    }

    const mainDungeonInfo = getZaMainDungeonPlacementInfo(group.objects[0]!);
    if (mainDungeonInfo) {
      return [mainDungeonInfo.floorLabel, mainDungeonInfo.tailLabel].filter(Boolean).join(': ');
    }

    const wildZoneInfo = parseZaWildZonePlacement(group.objects[0]!.map) ??
      parseZaWildZonePlacement(group.objects[0]!.label);
    if (wildZoneInfo?.tailLabel) {
      return wildZoneInfo.tailLabel;
    }

    const eventInfo = parseZaEventPlacement(group.objects[0]!.map) ??
      parseZaEventPlacement(group.objects[0]!.label);
    if (eventInfo?.tailLabel) {
      return eventInfo.tailLabel;
    }

    const testSpawnerInfo = parseZaTestSpawnerPlacement(group.objects[0]!.map) ??
      parseZaTestSpawnerPlacement(group.objects[0]!.label);
    if (testSpawnerInfo?.tailLabel) {
      return testSpawnerInfo.tailLabel;
    }

    return formatPlacementPrimaryData(group.objects[0]!);
  }

  const subgroups = getPlacementObjectSubgroups(group);
  if (subgroups.length > 1) {
    const preview = subgroups.slice(0, 3).map((subgroup) => subgroup.label).join(', ');
    const additionalCount = subgroups.length - 3;
    return additionalCount > 0 ? `${preview} + ${additionalCount} more` : preview;
  }

  const subgroup = subgroups[0];
  const tabs = (subgroup?.tabs ?? getPlacementObjectGroupTabs(group))
    .map((tab) => tab.label)
    .filter((label, index, labels) => labels.indexOf(label) === index);
  const preview = tabs.slice(0, 3).join(', ');
  const additionalCount = tabs.length - 3;
  const tabPreview = additionalCount > 0 ? `${preview} + ${additionalCount} more` : preview;
  return subgroup && subgroup.label !== 'General' && tabPreview
    ? `${subgroup.label}: ${tabPreview}`
    : tabPreview;
}

function comparePlacementObjectsWithinSubgroup(
  group: Pick<PlacementObjectGroup, 'label' | 'map'>,
  subgroupLabel: string,
  left: PlacedObjectRecord,
  right: PlacedObjectRecord
) {
  const leftDungeon = getZaDimensionDungeonPlacementInfo(left);
  const rightDungeon = getZaDimensionDungeonPlacementInfo(right);
  if (!leftDungeon && !rightDungeon) {
    return comparePlacementObjectsForGroup(left, right);
  }

  const leftLabel = formatPlacementObjectTabLabel(group, left, 0, subgroupLabel);
  const rightLabel = formatPlacementObjectTabLabel(group, right, 0, subgroupLabel);
  return comparePlacementNaturalLabels(leftLabel, rightLabel) ||
    comparePlacementObjectsForGroup(left, right);
}

function comparePlacementNaturalLabels(left: string, right: string) {
  return left.localeCompare(right, undefined, {
    numeric: true,
    sensitivity: 'base'
  });
}

function comparePlacementSubgroups(left: PlacementObjectSubgroup, right: PlacementObjectSubgroup) {
  return getPlacementSubgroupSortRank(left.label) - getPlacementSubgroupSortRank(right.label) ||
    comparePlacementNaturalLabels(left.label, right.label);
}

function getPlacementSubgroupSortRank(label: string) {
  if (/^Variant\s+\d+/i.test(label)) {
    return 10;
  }

  if (/^Special Area\s+\d+/i.test(label)) {
    return 20;
  }

  if (/^Pokemon Set\s+\d+/i.test(label)) {
    return 30;
  }

  if (/^Floor\s+\d+/i.test(label)) {
    return 40;
  }

  if (/^Sector\s+\d+/i.test(label)) {
    return 50;
  }

  if (/^Event/i.test(label)) {
    return 60;
  }

  if (/^Test/i.test(label)) {
    return 70;
  }

  if (label === 'General') {
    return 1000;
  }

  return 100;
}

function formatPlacementGroupPosition(objects: PlacedObjectRecord[]) {
  return objects.length === 1
    ? formatPlacementCoordinates(objects[0]!)
    : `${objects.length} positions`;
}

function createPlacementObjectGroupTab(
  group: Pick<PlacementObjectGroup, 'label' | 'map'>,
  object: PlacedObjectRecord,
  index: number,
  subgroupLabel = ''
) {
  return {
    label: formatPlacementObjectTabLabel(group, object, index, subgroupLabel),
    objectId: object.objectId,
    title: `${object.label} - ${formatPlacementCoordinates(object)}`
  };
}

function formatPlacementObjectTabLabel(
  group: Pick<PlacementObjectGroup, 'label' | 'map'>,
  object: PlacedObjectRecord,
  index: number,
  subgroupLabel = ''
) {
  const dungeonInfo = getZaDimensionDungeonPlacementInfo(object);
  if (dungeonInfo?.tailLabel) {
    return dungeonInfo.tailLabel;
  }

  const mainDungeonInfo = getZaMainDungeonPlacementInfo(object);
  if (mainDungeonInfo?.tailLabel) {
    return mainDungeonInfo.tailLabel;
  }

  const wildZoneInfo = parseZaWildZonePlacement(object.map) ?? parseZaWildZonePlacement(object.label);
  if (wildZoneInfo?.tailLabel) {
    return wildZoneInfo.tailLabel.replace(/^Variant\s+\d+\s*/i, '').trim() || wildZoneInfo.tailLabel;
  }

  const eventInfo = parseZaEventPlacement(object.map) ?? parseZaEventPlacement(object.label);
  if (eventInfo?.tailLabel) {
    return eventInfo.tailLabel;
  }

  const testSpawnerInfo = parseZaTestSpawnerPlacement(object.map) ?? parseZaTestSpawnerPlacement(object.label);
  if (testSpawnerInfo?.tailLabel) {
    return testSpawnerInfo.tailLabel;
  }

  const strippedFromLabel = stripPlacementGroupPrefix(object.label, group.label);
  const strippedFromSubgroup = stripPlacementGroupPrefix(strippedFromLabel || object.label, subgroupLabel);
  const strippedFromMap = stripPlacementGroupPrefix(strippedFromSubgroup || strippedFromLabel || object.label, group.map);
  const label = strippedFromMap || strippedFromLabel || object.label;
  return label === group.label || label === subgroupLabel || !label.trim()
    ? `Transform ${index + 1}`
    : label.trim();
}

function stripPlacementGroupPrefix(label: string, prefix: string) {
  const trimmed = label.trim();
  const normalizedPrefix = prefix.trim();
  if (!normalizedPrefix) {
    return trimmed;
  }

  if (trimmed === normalizedPrefix) {
    return '';
  }

  if (trimmed.startsWith(`${normalizedPrefix}, `)) {
    return trimmed.slice(normalizedPrefix.length + 2).trim();
  }

  if (trimmed.startsWith(`${normalizedPrefix} - `)) {
    return trimmed.slice(normalizedPrefix.length + 3).trim();
  }

  if (trimmed.startsWith(`${normalizedPrefix} `)) {
    return trimmed.slice(normalizedPrefix.length + 1).trim();
  }

  return trimmed;
}

function parseFirstInteger(label: string) {
  const match = label.match(/\d+/);
  return match ? Number.parseInt(match[0]!, 10) : null;
}

function normalizePlacementGroupKey(label: string) {
  return label.trim().toLocaleLowerCase().replace(/\s+/g, ' ');
}
