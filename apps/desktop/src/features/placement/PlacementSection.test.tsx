/* SPDX-License-Identifier: GPL-3.0-only */

import { type PlacementWorkflow } from '../../bridge/contracts';
import {
  buildPlacementObjectGroups,
  formatPlacementCoordinates,
  getPlacementInspectorPrimaryData,
  getPlacementObjectSubgroups,
  mergePlacementObjectUpdate,
  placementObjectChangesAreStaged
} from './placementUi';

it('keeps placement grouping, staging, and object summaries coherent', () => {
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

  const numberedWildZones = buildPlacementObjectGroups(
    [
      createPlacementObject(10, 'Wild Zone 10 Spawner 1', 'Wild Zone 10 - Bleu District, Sector 1'),
      createPlacementObject(500, 'Boss Battle Test', 'Boss Battles'),
      createPlacementObject(110, 'Wild Zone 1 Spawner 10', 'Wild Zone 1 - Vert District, Sector 2'),
      createPlacementObject(20, 'Wild Zone 2 Spawner 1', 'Wild Zone 2 - Magenta District, Sector 1'),
      createPlacementObject(102, 'Wild Zone 1 Spawner 2', 'Wild Zone 1 - Vert District, Sector 2'),
      createPlacementObject(101, 'Wild Zone 1 Spawner 1', 'Wild Zone 1 - Vert District, Sector 2'),
      createPlacementObject(111, 'Wild Zone 1 Spawner 11', 'Wild Zone 1 - Vert District, Sector 2')
    ],
    { groupPokemonSpawners: true }
  );
  expect(numberedWildZones.map((group) => group.label)).toEqual([
    'Wild Zone 1',
    'Boss Battle Test',
    'Wild Zone 2',
    'Wild Zone 10'
  ]);
  const wildZoneOneSubgroups = getPlacementObjectSubgroups(numberedWildZones[0]!);
  expect(wildZoneOneSubgroups.map((subgroup) => subgroup.label)).toEqual(['Spawners']);
  expect(wildZoneOneSubgroups[0]!.tabs.map((tab) => tab.label)).toEqual([
    'Spawner 1',
    'Spawner 2',
    'Spawner 10',
    'Spawner 11'
  ]);

  const trainer = {
    ...createPlacementObject(2, 'Trainer: Hop', 'Route 1'),
    categoryId: 'npcsTrainers',
    categoryLabel: 'NPCs & Trainers',
    fields: [
      createPlacementField(
        'raw.Trainer.Field_00.Field_00.LocationX',
        'X',
        'Transform',
        '1.235'
      ),
      createPlacementField(
        'raw.Trainer.Field_00.Field_04.LocationX',
        'Nested X',
        'Transform',
        '88'
      ),
      createPlacementField(
        'raw.Trainer.TrainerID',
        'Trainer Battle',
        'References',
        '0x0102030405060708',
        'Hop (0x0102030405060708)'
      )
    ],
    itemHash: '',
    itemName: '',
    label: 'Trainer: Hop',
    objectType: 'Trainer',
    scriptId: '0x0102030405060708',
    x: 1.23456789
  } satisfies PlacementWorkflow['objects'][number];

  expect(formatPlacementCoordinates(trainer)).toBe('1.234568, 0, 0');
  expect(getPlacementInspectorPrimaryData(trainer)).toEqual({
    isItem: false,
    label: 'Placement Data',
    value: 'Hop (0x0102030405060708)'
  });

  const changes = [
    { field: 'raw.Trainer.Field_00.Field_00.LocationX', value: '9.87654321' },
    { field: 'raw.Trainer.TrainerID', value: '0x1111111111111111' }
  ];

  expect(
    placementObjectChangesAreStaged(
      {
        pendingEdits: changes.map((change) => ({
          domain: 'workflow.placement',
          field: change.field,
          newValue: change.value,
          recordId: trainer.objectId
        }))
      },
      trainer,
      changes
    )
  ).toBe(true);
  expect(
    placementObjectChangesAreStaged(
      {
        pendingEdits: [
          {
            domain: 'workflow.placement',
            field: changes[0]!.field,
            newValue: changes[0]!.value,
            recordId: trainer.objectId
          }
        ]
      },
      trainer,
      changes
    )
  ).toBe(false);
  expect(
    placementObjectChangesAreStaged(
      {
        pendingEdits: changes.map((change) => ({
          domain: 'workflow.placement',
          field: change.field,
          newValue: change === changes[0] ? '1.5' : change.value,
          recordId: trainer.objectId
        }))
      },
      trainer,
      changes
    )
  ).toBe(false);
  expect(
    placementObjectChangesAreStaged(
      {
        pendingEdits: changes.map((change) => ({
          domain: 'workflow.placement',
          field: change.field,
          newValue: change === changes[0]
            ? Math.fround(Number(change.value)).toPrecision(9)
            : change.value,
          recordId: trainer.objectId
        }))
      },
      trainer,
      changes
    )
  ).toBe(true);

  const mergedTrainer = mergePlacementObjectUpdate(trainer, {
    ...trainer,
    fields: [
      createPlacementField(
        'raw.Trainer.Field_00.Field_00.LocationX',
        'X',
        'Transform',
        '9.5'
      )
    ],
    x: 9.5
  });
  expect(mergedTrainer.x).toBe(9.5);
  expect(mergedTrainer.fields).toHaveLength(trainer.fields.length);
  expect(mergedTrainer.fields?.find(
    (field) => field.field === 'raw.Trainer.TrainerID'
  )?.displayValue).toBe('Hop (0x0102030405060708)');

  const zaSpawner = {
    ...createPlacementObject(3, 'Wild Zone 1 Spawner', 'Wild Zone 1'),
    fields: [createPlacementField('point.positionX', 'X', 'Transform', '1.5')]
  };
  const zaChanges = [{ field: 'point.positionX', value: '9.87654321' }];
  expect(
    placementObjectChangesAreStaged(
      {
        pendingEdits: [{
          domain: 'workflow.placement',
          field: zaChanges[0]!.field,
          newValue: Math.fround(Number(zaChanges[0]!.value)).toString(),
          recordId: zaSpawner.objectId
        }]
      },
      zaSpawner,
      zaChanges
    )
  ).toBe(true);
});

it('groups title-only mission spawners without shortening their names', () => {
  const groups = buildPlacementObjectGroups(
    [
      createMissionPlacementObject(
        70,
        'Full Course of Battles: High Rolling',
        'Side Mission 73',
        'Battle 1 Spawn Point 001'
      ),
      createMissionPlacementObject(
        71,
        'Full Course of Battles: High Rolling',
        'Side Mission 73',
        'Battle 1 Spawn Point 002'
      ),
      createMissionPlacementObject(
        72,
        'Full Course of Battles: High Rolling',
        'Side Mission 73',
        'Battle 2 Spawn Point 001'
      ),
      createMissionPlacementObject(
        173,
        'Be a Defenseless Dodger!',
        'Side Mission 173',
        'Spawn Point 002'
      ),
      createMissionPlacementObject(
        174,
        'Be a Defenseless Dodger!',
        'Side Mission 173',
        'Spawn Point 002B'
      ),
      createMissionPlacementObject(
        64,
        'Let It Rain, Let It Pour',
        'Side Mission 64',
        'Spawn Point 001'
      ),
      createPlacementObject(
        704,
        'Rest Event 4 Battle 1 Spawn Point 001',
        'Rest Event 4'
      )
    ],
    { groupPokemonSpawners: true }
  );

  const restaurantGroup = groups.find(
    (group) => group.label === 'Full Course of Battles: High Rolling'
  );
  expect(restaurantGroup).toBeDefined();
  expect(restaurantGroup?.objects).toHaveLength(3);
  expect(restaurantGroup?.preview).toBe(
    'Event Spawners: Battle 1 Spawn Point 001, Battle 1 Spawn Point 002, Battle 2 Spawn Point 001'
  );
  const restaurantSubgroups = getPlacementObjectSubgroups(restaurantGroup!);
  expect(restaurantSubgroups.map((subgroup) => subgroup.label)).toEqual(['Event Spawners']);
  expect(restaurantSubgroups[0]?.tabs.map((tab) => tab.label)).toEqual([
    'Battle 1 Spawn Point 001',
    'Battle 1 Spawn Point 002',
    'Battle 2 Spawn Point 001'
  ]);

  const dodgerGroup = groups.find((group) => group.label === 'Be a Defenseless Dodger!');
  expect(dodgerGroup).toBeDefined();
  expect(getPlacementObjectSubgroups(dodgerGroup!)[0]?.tabs.map((tab) => tab.label)).toEqual([
    'Spawn Point 002',
    'Spawn Point 002B'
  ]);

  expect(groups.find((group) => group.label === 'Let It Rain, Let It Pour')).toBeDefined();
  expect(groups.some((group) => group.label === 'Let It Rain')).toBe(false);

  const legacyGroup = groups.find((group) => group.label === 'Rest Event 4');
  expect(legacyGroup).toBeDefined();
  expect(getPlacementObjectSubgroups(legacyGroup!)[0]?.tabs[0]?.label).toBe(
    'Battle 1 Spawn Point 001'
  );
});

function createPlacementField(
  field: string,
  label: string,
  group: string,
  value: string,
  displayValue = value
) {
  return {
    description: '',
    displayValue,
    field,
    group,
    isReadOnly: false,
    label,
    maximumValue: 1_000_000,
    minimumValue: -1_000_000,
    options: null,
    value,
    valueKind: field.endsWith('TrainerID') ? 'hash' : 'number'
  };
}

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

function createMissionPlacementObject(
  index: number,
  title: string,
  missionReference: string,
  tailLabel: string
): PlacementWorkflow['objects'][number] {
  return {
    ...createPlacementObject(index, `${title} ${tailLabel}`, title),
    fields: [{
      ...createPlacementField(
        'spawner.mission',
        'Mission',
        'Spawner Context',
        missionReference
      ),
      isReadOnly: true,
      valueKind: 'text'
    }]
  };
}
