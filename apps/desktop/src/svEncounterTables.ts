// SPDX-License-Identifier: GPL-3.0-only

import type { EncounterTableRecord } from './bridge/contracts';
import { formatSvEncounterFacetValue } from './svEncounterLabels';

type SvEncounterFacetKey = 'area' | 'version' | 'terrain' | 'time' | 'biome' | 'flag';

type SvEncounterFacets = Record<'location' | SvEncounterFacetKey, string>;

type SvEncounterFacetControl = {
  choices: Array<{ label: string; tableId: string; value: string }>;
  currentValue: string;
  key: SvEncounterFacetKey;
  label: string;
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
      candidate.facets !== null && candidate.facets.location === selectedFacets.location
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
      const choicesByValue = new Map<
        string,
        { label: string; score: number; tableId: string; value: string }
      >();

      for (const candidate of candidates) {
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
        key: definition.key,
        label: definition.label
      };
    })
    .filter((control) =>
      control.choices.length > 1 &&
      control.choices.some((choice) => choice.tableId === selectedTable.tableId)
    );
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
