// SPDX-License-Identifier: GPL-3.0-only

import type { EncounterTableRecord } from './bridge/contracts';
import { formatSvEncounterFacetValue } from './svEncounterLabels';

type SvEncounterFacetKey = 'area' | 'version' | 'terrain' | 'time' | 'biome' | 'flag';

type SvEncounterFacets = Record<'location' | SvEncounterFacetKey, string>;

type SvEncounterFacetControl = {
  choices: Array<{ label: string; tableId: string; value: string }>;
  currentValue: string;
  disabled: boolean;
  key: SvEncounterFacetKey;
  label: string;
  title: string;
};

export type SvEncounterConditionRow = {
  area: string;
  biome: string;
  flag: string;
  gameVersion: string;
  label: string;
  location: string;
  slotCount: number;
  table: EncounterTableRecord;
  tableId: string;
  terrain: string;
  time: string;
  totalWeight: number;
};

export type SvEncounterLocationRow = {
  areaLabel: string;
  gameVersion: string;
  location: string;
  table: EncounterTableRecord;
  tableIds: string[];
  zoneKey: string;
};

export function isScarletVioletEncounterTable(table: EncounterTableRecord) {
  return (
    table.tableId.includes('|') ||
    table.provenance.sourceFile.includes('world/data/encount') ||
    table.gameVersion.includes('Scarlet') ||
    table.gameVersion.includes('Violet')
  );
}

export function parseSvEncounterFacets(
  table: EncounterTableRecord | null
): SvEncounterFacets | null {
  if (!table || !isScarletVioletEncounterTable(table)) {
    return null;
  }

  const parts = table.tableId.split('|');
  if (parts.length < 7) {
    return {
      area: table.area,
      biome: table.slots[0]?.weather ?? 'Any',
      flag: 'no-flag',
      location: table.location,
      terrain: table.encounterType,
      time: table.slots[0]?.timeOfDay ?? 'Any',
      version: table.gameVersion
    };
  }

  return {
    location: parts[0] || table.location,
    area: parts[1] || table.area,
    version: parts[2] || table.gameVersion,
    terrain: parts[3] || table.encounterType,
    time: parts[4] || (table.slots[0]?.timeOfDay ?? 'Any'),
    biome: parts[5] || (table.slots[0]?.weather ?? 'Any'),
    flag: parts[6] || 'no-flag'
  };
}

export function buildSvEncounterFacetControls(
  selectedTable: EncounterTableRecord,
  tables: EncounterTableRecord[]
): SvEncounterFacetControl[] {
  const selectedFacets = parseSvEncounterFacets(selectedTable);
  if (!selectedFacets) {
    return [];
  }

  const candidates = tables
    .map((table) => ({ facets: parseSvEncounterFacets(table), table }))
    .filter((candidate): candidate is { facets: SvEncounterFacets; table: EncounterTableRecord } =>
      candidate.facets !== null &&
      getSvEncounterLocationKey(candidate.table) === getSvEncounterLocationKey(selectedTable)
    );
  const definitions: Array<{ key: SvEncounterFacetKey; label: string }> = [
    { key: 'area', label: 'Area' },
    { key: 'version', label: 'Version' },
    { key: 'terrain', label: 'Terrain' },
    { key: 'time', label: 'Time' },
    { key: 'biome', label: 'Biome' },
    { key: 'flag', label: 'Flag' }
  ];

  return definitions
    .map((definition) => {
      const scopedCandidates = candidates.filter((candidate) =>
        isSvEncounterFacetChoiceInScope(selectedFacets, candidate.facets, definition.key)
      );
      const choicesByValue = new Map<
        string,
        { label: string; score: number; tableId: string; value: string }
      >();

      for (const candidate of scopedCandidates) {
        const value = candidate.facets[definition.key];
        const score = scoreSvEncounterFacetMatch(selectedFacets, candidate.facets, definition.key);
        const current = choicesByValue.get(value);
        if (!current || score > current.score) {
          choicesByValue.set(value, {
            label: formatSvEncounterFacetValue(value),
            score,
            tableId: candidate.table.tableId,
            value
          });
        }
      }

      choicesByValue.set(selectedFacets[definition.key], {
        label: formatSvEncounterFacetValue(selectedFacets[definition.key]),
        score: Number.MAX_SAFE_INTEGER,
        tableId: selectedTable.tableId,
        value: selectedFacets[definition.key]
      });

      const choices = [...choicesByValue.values()]
        .sort((left, right) =>
          left.value === selectedFacets[definition.key]
            ? -1
            : right.value === selectedFacets[definition.key]
              ? 1
              : left.label.localeCompare(right.label)
        )
        .map(({ label, tableId, value }) => ({ label, tableId, value }));

      return {
        choices,
        currentValue: selectedFacets[definition.key],
        disabled: choices.length <= 1,
        key: definition.key,
        label: definition.label,
        title:
          choices.length <= 1
            ? `Only one ${definition.label.toLocaleLowerCase()} is available for the current S/V location filter.`
            : `Choose a ${definition.label.toLocaleLowerCase()} available inside the current S/V location filter.`
      };
    });
}

export function buildSvEncounterConditionRows(
  selectedTable: EncounterTableRecord,
  tables: EncounterTableRecord[]
): SvEncounterConditionRow[] {
  const selectedZoneKey = getSvEncounterLocationKey(selectedTable);
  return tables
    .filter(
      (table) =>
        isScarletVioletEncounterTable(table) &&
        getSvEncounterLocationKey(table) === selectedZoneKey
    )
    .map((table) => {
      const facets = parseSvEncounterFacets(table);
      const area = formatSvEncounterFacetValue(facets?.area ?? table.area);
      const gameVersion = formatSvEncounterFacetValue(facets?.version ?? table.gameVersion);
      const terrain = formatSvEncounterFacetValue(facets?.terrain ?? table.encounterType);
      const time = formatSvEncounterFacetValue(
        facets?.time ?? table.slots[0]?.timeOfDay ?? 'Any'
      );
      const biome = formatSvEncounterFacetValue(facets?.biome ?? table.slots[0]?.weather ?? 'Any');
      const flag = formatSvEncounterFacetValue(facets?.flag ?? 'no-flag');
      const totalWeight = table.slots.reduce((total, slot) => total + slot.weight, 0);

      return {
        area,
        biome,
        flag,
        gameVersion,
        label: [area, gameVersion, terrain, time, biome, flag]
          .filter((part) => part.length > 0)
          .join(' / '),
        location: formatSvEncounterLocationLabel(table),
        slotCount: table.slots.length,
        table,
        tableId: table.tableId,
        terrain,
        time,
        totalWeight
      };
    })
    .sort(compareSvEncounterConditionRows);
}

export function buildSvEncounterLocationRows(
  tables: EncounterTableRecord[],
  selectedTable: EncounterTableRecord | null
): SvEncounterLocationRow[] {
  const groups = new Map<string, EncounterTableRecord[]>();

  for (const table of tables) {
    const zoneKey = getSvEncounterLocationKey(table);
    groups.set(zoneKey, [...(groups.get(zoneKey) ?? []), table]);
  }

  return Array.from(groups.entries())
    .map(([zoneKey, groupTables]) => {
      const selectedInGroup =
        selectedTable && getSvEncounterLocationKey(selectedTable) === zoneKey
          ? selectedTable
          : null;
      const table = selectedInGroup ?? getPreferredSvEncounterTable(groupTables);
      return {
        areaLabel: formatSvEncounterLocationListDetails(groupTables),
        gameVersion: formatSvEncounterLocationGameVersions(groupTables),
        location: formatSvEncounterLocationLabel(table),
        table,
        tableIds: groupTables.map((groupTable) => groupTable.tableId),
        zoneKey
      };
    })
    .sort((left, right) => left.location.localeCompare(right.location));
}

export function formatSvEncounterTableSummary(facets: SvEncounterFacets) {
  return [
    formatSvEncounterFacetValue(facets.version),
    formatSvEncounterFacetValue(facets.terrain),
    formatSvEncounterFacetValue(facets.time),
    formatSvEncounterFacetValue(facets.biome)
  ]
    .filter((part, index, parts) => part.length > 0 && parts.indexOf(part) === index)
    .join(' / ');
}

export function formatSvEncounterTableListDetails(table: EncounterTableRecord) {
  const facets = parseSvEncounterFacets(table);
  if (!facets) {
    return table.area;
  }

  return [
    formatSvEncounterFacetValue(facets.area),
    formatSvEncounterFacetValue(facets.terrain),
    formatSvEncounterFacetValue(facets.time),
    formatSvEncounterFacetValue(facets.biome)
  ]
    .filter((part, index, parts) => part.length > 0 && parts.indexOf(part) === index)
    .join(' / ');
}

export function getSvEncounterLocationKey(table: EncounterTableRecord) {
  return `sv:${formatSvEncounterLocationLabel(table).toLocaleLowerCase()}`;
}

function isSvEncounterFacetChoiceInScope(
  selected: SvEncounterFacets,
  candidate: SvEncounterFacets,
  key: SvEncounterFacetKey
) {
  const cascade: SvEncounterFacetKey[] = ['area', 'version', 'terrain', 'time', 'biome', 'flag'];
  const keyIndex = cascade.indexOf(key);
  return cascade
    .slice(0, Math.max(0, keyIndex))
    .every((previousKey) => candidate[previousKey] === selected[previousKey]);
}

function getPreferredSvEncounterTable(tables: EncounterTableRecord[]) {
  return [...tables].sort((left, right) => {
    const leftFacets = parseSvEncounterFacets(left);
    const rightFacets = parseSvEncounterFacets(right);
    const areaCompare = (leftFacets?.area ?? left.area).localeCompare(rightFacets?.area ?? right.area);
    if (areaCompare !== 0) {
      return areaCompare;
    }

    const terrainCompare = (leftFacets?.terrain ?? left.encounterType).localeCompare(
      rightFacets?.terrain ?? right.encounterType
    );
    if (terrainCompare !== 0) {
      return terrainCompare;
    }

    return left.tableId.localeCompare(right.tableId);
  })[0]!;
}

function formatSvEncounterLocationListDetails(tables: EncounterTableRecord[]) {
  const labels = tables.map((table) => {
    const facets = parseSvEncounterFacets(table);
    return formatSvEncounterFacetValue(facets?.area ?? table.area);
  });

  return summarizeUniqueLabels(labels, 'area');
}

function formatSvEncounterLocationGameVersions(tables: EncounterTableRecord[]) {
  const versions = new Set(
    tables.map((table) => {
      const facets = parseSvEncounterFacets(table);
      return formatSvEncounterFacetValue(facets?.version ?? table.gameVersion);
    })
  );

  if (versions.has('Scarlet/Violet')) {
    return 'Scarlet/Violet';
  }

  return summarizeUniqueLabels([...versions], 'version', 2);
}

function formatSvEncounterLocationLabel(table: EncounterTableRecord) {
  const facets = parseSvEncounterFacets(table);
  const formattedLocation = table.location.trim();
  if (formattedLocation.length > 0 && formattedLocation !== 'Unknown Location') {
    return formattedLocation;
  }

  return formatSvEncounterFacetValue(facets?.location ?? table.location);
}

function summarizeUniqueLabels(labels: string[], noun: string, limit = 3) {
  const uniqueLabels = Array.from(new Set(labels.filter((label) => label.length > 0)));
  if (uniqueLabels.length <= limit) {
    return uniqueLabels.join(' / ');
  }

  return `${uniqueLabels.slice(0, limit).join(' / ')} + ${uniqueLabels.length - limit} ${noun}${
    uniqueLabels.length - limit === 1 ? '' : 's'
  }`;
}

function scoreSvEncounterFacetMatch(
  selected: SvEncounterFacets,
  candidate: SvEncounterFacets,
  ignoredKey: SvEncounterFacetKey
) {
  const weights: Array<[SvEncounterFacetKey, number]> = [
    ['area', 4],
    ['version', 3],
    ['terrain', 3],
    ['time', 2],
    ['biome', 2],
    ['flag', 1]
  ];

  return weights.reduce(
    (total, [key, weight]) =>
      key === ignoredKey || selected[key] !== candidate[key] ? total : total + weight,
    0
  );
}

function compareSvEncounterConditionRows(
  left: SvEncounterConditionRow,
  right: SvEncounterConditionRow
) {
  return (
    left.area.localeCompare(right.area, undefined, { numeric: true }) ||
    compareByRank(left.gameVersion, right.gameVersion, ['Scarlet/Violet', 'Scarlet', 'Violet']) ||
    compareByRank(left.terrain, right.terrain, ['Land', 'Water', 'Underwater', 'Air', 'Wild']) ||
    compareByRank(left.time, right.time, [
      'Any',
      'Morning, Noon, Evening, Night',
      'Morning',
      'Noon',
      'Evening',
      'Night'
    ]) ||
    left.biome.localeCompare(right.biome, undefined, { numeric: true }) ||
    left.flag.localeCompare(right.flag, undefined, { numeric: true }) ||
    left.tableId.localeCompare(right.tableId)
  );
}

function compareByRank(left: string, right: string, rankedValues: string[]) {
  const leftRank = rankedValues.indexOf(left);
  const rightRank = rankedValues.indexOf(right);
  if (leftRank >= 0 || rightRank >= 0) {
    return (
      (leftRank >= 0 ? leftRank : rankedValues.length) -
      (rightRank >= 0 ? rightRank : rankedValues.length)
    );
  }

  return left.localeCompare(right, undefined, { numeric: true });
}
