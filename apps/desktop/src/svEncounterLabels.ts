/* SPDX-License-Identifier: GPL-3.0-only */

const svEncounterAreaNames = new Map<string, string>([
  ['1', 'South Province (Area One)'],
  ['2', 'Mesagoza'],
  ['3', 'Pokemon League'],
  ['4', 'South Province (Area Two)'],
  ['5', 'South Province (Area Four)'],
  ['6', 'South Province (Area Six)'],
  ['7', 'South Province (Area Five)'],
  ['8', 'South Province (Area Three)'],
  ['9', 'West Province (Area One)'],
  ['10', 'Asado Desert'],
  ['11', 'West Province (Area Two)'],
  ['12', 'West Province (Area Three)'],
  ['13', 'Tagtree Thicket'],
  ['14', 'East Province (Area Three)'],
  ['15', 'East Province (Area One)'],
  ['16', 'East Province (Area Two)'],
  ['17', 'Glaseado Mountain'],
  ['18', 'Casseroya Lake'],
  ['19', 'Glaseado Mountain'],
  ['20', 'North Province (Area Three)'],
  ['21', 'North Province (Area One)'],
  ['22', 'North Province (Area Two)'],
  ['23', 'Great Crater of Paldea'],
  ['24', 'South Paldean Sea'],
  ['25', 'West Paldean Sea'],
  ['26', 'East Paldean Sea'],
  ['27', 'North Paldean Sea']
]);

const svEncounterLocationNames = new Map<string, string>([
  ['a_d1108', 'Alfornada Cavern'],
  ['a_d1202', 'Glaseado Cave (a_d1202)'],
  ['a_w23_d01', 'Area Zero Cave'],
  ['a_w23_d02', 'Area Zero Cave'],
  ['a_w23_d03', 'Area Zero Cave'],
  ['a_w23_d04', 'Area Zero Cave'],
  ['a_w23_field_1', 'Area Zero'],
  ['a_w23_field_2', 'Area Zero'],
  ['a_w23_field_3', 'Area Zero'],
  ['loc_desert_east', 'Asado Desert (East)'],
  ['loc_desert_west', 'Asado Desert (West)'],
  ['loc_lake_east', 'Casseroya Lake (East)'],
  ['loc_lake_south', 'Casseroya Lake (South)'],
  ['loc_snowymountain_01', 'Glaseado Mountain'],
  ['subarea_area18forest', 'Socarrat Trail']
]);

export function formatSvEncounterFacetValue(value: string) {
  if (value === 'area') {
    return 'Biome-based';
  }

  if (value === 'location') {
    return 'No explicit location';
  }

  if (value === 'no-flag') {
    return 'No flag';
  }

  if (value === 'voice:any') {
    return 'Any voice';
  }

  if (value === 'band:0:NONE:0:0') {
    return 'No band';
  }

  if (value.startsWith('height:')) {
    return value.replace('height:', 'Height ');
  }

  if (value.startsWith('outbreak:')) {
    const outbreak = value.replace('outbreak:', '');
    return outbreak === '0' ? 'No outbreak' : `Outbreak ${outbreak}`;
  }

  if (value.startsWith('band:')) {
    return value.replace(/:/g, ' ');
  }

  return formatSvEncounterTokenList(value) ?? value;
}

function formatSvEncounterTokenList(value: string) {
  if (value.includes('/') || value.includes(':')) {
    return null;
  }

  const tokens = value
    .split(',')
    .map((token) => token.trim())
    .filter((token) => token.length > 0);
  if (tokens.length === 0) {
    return null;
  }

  const labels = tokens.map(formatSvEncounterToken);
  if (labels.some((label) => label === null)) {
    return null;
  }

  return Array.from(new Set(labels as string[])).join(', ');
}

function formatSvEncounterToken(token: string) {
  return svEncounterAreaNames.get(token) ?? svEncounterLocationNames.get(token) ?? null;
}
