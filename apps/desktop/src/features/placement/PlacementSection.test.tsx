/* SPDX-License-Identifier: GPL-3.0-only */

import { type PlacementWorkflow } from '../../bridge/contracts';
import { buildPlacementObjectGroups, getPlacementObjectSubgroups } from './placementUi';

it('groups Z-A placement rows by district and location', () => {
  const groups = buildPlacementObjectGroups(
    [
      createPlacementObject(
        0,
        'Magenta District, Sector 1 Outside Wild Zone, Spawner 01',
        'Magenta District, Sector 1 Outside Wild Zone'
      ),
      createPlacementObject(
        1,
        'Magenta District, Sector 2 Outside Wild Zone, Spawner 01',
        'Magenta District, Sector 2 Outside Wild Zone'
      )
    ],
    { groupPokemonSpawners: true }
  );

  expect(groups).toHaveLength(1);
  expect(groups[0]?.label).toBe('Magenta District');
  expect(groups[0]?.preview).toBe('Sector 1 Outside Wild Zone, Sector 2 Outside Wild Zone');
  expect(getPlacementObjectSubgroups(groups[0]!).map((subgroup) => subgroup.label)).toEqual([
    'Sector 1 Outside Wild Zone',
    'Sector 2 Outside Wild Zone'
  ]);
});

function createPlacementObject(
  index: number,
  label: string,
  map: string
): PlacementWorkflow['objects'][number] {
  return {
    archiveMember:
      'romfs/world/ik_data/field/pokemon_spawner/pokemon_spawner_point/pokemon_spawner_point_array.bin',
    categoryId: 'pokemonSpawners',
    categoryLabel: 'Pokemon Spawners',
    chance: null,
    chanceIndex: null,
    fields: [],
    itemHash: `spawner_${index}`,
    itemId: null,
    itemName: `spawner_${index}`,
    label,
    map,
    objectId: `pokemonSpawners|${index}`,
    objectIndex: index,
    objectType: 'Pokemon Spawner',
    provenance: {
      fileState: 'baseOnly',
      sourceFile:
        'romfs/world/ik_data/field/pokemon_spawner/pokemon_spawner_point/pokemon_spawner_point_array.bin',
      sourceLayer: 'base'
    },
    quantity: 0,
    rotationY: 0,
    scriptId: `spawner_${index}`,
    x: index,
    y: 0,
    zoneIndex: 0,
    z: 0
  };
}
