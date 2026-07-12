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
