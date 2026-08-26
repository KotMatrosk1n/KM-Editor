/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  EncounterSlotRecord,
  PokemonLearnsetMove,
  ProjectGame,
  TrainerPokemonRecord
} from '../bridge/contracts';
import {
  RowClipboardError,
  type RowClipboardAdapterRegistration,
  type RowClipboardEditorSchemaRef,
  type RowClipboardLogicalRowV1,
  type RowClipboardOwnedValue,
  type RowClipboardScope
} from './rowClipboardTypes';

export const rowClipboardProfileIds = {
  scarletViolet: '4.0.0',
  swordShield: '1.3.2',
  za: '2.0.2'
} as const;

export const rowClipboardEditorSchemas = {
  encounterSlot: {
    editorId: 'encounters.slots',
    rowKind: 'encounter.slot',
    rowSchemaVersion: 1
  },
  pokemonLearnset: {
    editorId: 'pokemon.learnset',
    rowKind: 'pokemon.learnset-row',
    rowSchemaVersion: 1
  },
  trainerParty: {
    editorId: 'trainers.party',
    rowKind: 'trainer.party-member',
    rowSchemaVersion: 1
  }
} as const satisfies Record<string, RowClipboardEditorSchemaRef>;

const commonTrainerFields = [
  'ability',
  'evAttack',
  'evDefense',
  'evHp',
  'evSpecialAttack',
  'evSpecialDefense',
  'evSpeed',
  'form',
  'gender',
  'heldItemId',
  'ivAttack',
  'ivDefense',
  'ivHp',
  'ivSpecialAttack',
  'ivSpecialDefense',
  'ivSpeed',
  'level',
  'move1Id',
  'move2Id',
  'move3Id',
  'move4Id',
  'nature',
  'speciesId'
] as const;
const zaEncounterFields = [
  'ability',
  'alphaChancePercent',
  'alphaLevelBonus',
  'appearanceMaxCount',
  'appearanceMinCount',
  'flawlessIvCount',
  'form',
  'gender',
  'heldItemId',
  'ivAttack',
  'ivDefense',
  'ivHp',
  'ivSpecialAttack',
  'ivSpecialDefense',
  'ivSpeed',
  'levelMax',
  'levelMin',
  'move1Id',
  'move2Id',
  'move3Id',
  'move4Id',
  'nature',
  'shinyLock',
  'slotMaxCount',
  'speciesId',
  'strengthenAttack',
  'strengthenDefense',
  'strengthenHp',
  'strengthenSpecialAttack',
  'strengthenSpecialDefense',
  'strengthenSpeed',
  'weight'
] as const;

const learnsetRegistrations = createFamilyRegistrations(
  rowClipboardEditorSchemas.pokemonLearnset,
  ['replace', 'append'],
  [unsignedField('level'), unsignedField('moveId')]
);
const basicEncounterRegistrations = [
  registration(
    rowClipboardEditorSchemas.encounterSlot,
    ['sword', 'shield'],
    rowClipboardProfileIds.swordShield,
    ['replace'],
    ['form', 'levelMax', 'levelMin', 'probability', 'speciesId'].map(signedField)
  ),
  registration(
    rowClipboardEditorSchemas.encounterSlot,
    ['scarlet', 'violet'],
    rowClipboardProfileIds.scarletViolet,
    ['replace'],
    ['form', 'levelMax', 'levelMin', 'probability', 'speciesId'].map(signedField)
  ),
  registration(
    rowClipboardEditorSchemas.encounterSlot,
    ['za'],
    rowClipboardProfileIds.za,
    ['replace'],
    zaEncounterFields.map(signedField)
  )
];
const trainerRegistrations = [
  registration(
    rowClipboardEditorSchemas.trainerParty,
    ['sword', 'shield'],
    rowClipboardProfileIds.swordShield,
    ['replace'],
    [
      ...commonTrainerFields.map(signedField),
      booleanField('shiny'),
      signedField('dynamaxLevel'),
      booleanField('canGigantamax'),
      booleanField('canDynamax')
    ],
    6
  ),
  registration(
    rowClipboardEditorSchemas.trainerParty,
    ['scarlet', 'violet'],
    rowClipboardProfileIds.scarletViolet,
    ['replace'],
    [...commonTrainerFields.map(signedField), booleanField('shiny'), signedField('teraType')],
    6
  ),
  registration(
    rowClipboardEditorSchemas.trainerParty,
    ['za'],
    rowClipboardProfileIds.za,
    ['replace'],
    [...commonTrainerFields.map(signedField), booleanField('shiny')],
    6
  )
];

export const rowClipboardAdapterRegistrations = Object.freeze([
  ...learnsetRegistrations,
  ...basicEncounterRegistrations,
  ...trainerRegistrations
]);

export function resolveRowClipboardAdapterRegistration(
  editor: RowClipboardEditorSchemaRef,
  scope: RowClipboardScope
): RowClipboardAdapterRegistration {
  const matches = rowClipboardAdapterRegistrations.filter(
    (candidate) =>
      candidate.editorId === editor.editorId &&
      candidate.rowKind === editor.rowKind &&
      candidate.rowSchemaVersion === editor.rowSchemaVersion &&
      candidate.games.includes(scope.game) &&
      candidate.profileIds?.includes(scope.profileId) === true
  );
  if (matches.length !== 1) {
    throw new RowClipboardError('adapter-unavailable');
  }
  return matches[0];
}

export function createPokemonLearnsetClipboardRow(
  personalId: number,
  move: PokemonLearnsetMove
): RowClipboardLogicalRowV1 {
  return logicalRow(
    rowClipboardEditorSchemas.pokemonLearnset.rowKind,
    `personal:${requireNonNegativeInteger(personalId)}:slot:${move.slot}`,
    [unsigned('level', move.level), unsigned('moveId', move.moveId)]
  );
}

export function createPokemonLearnsetClipboardRowFromFieldValues(
  personalId: number,
  move: PokemonLearnsetMove,
  fieldValues: Readonly<Record<string, string>>
): RowClipboardLogicalRowV1 {
  return replaceClipboardRowFieldValues(
    createPokemonLearnsetClipboardRow(personalId, move),
    fieldValues
  );
}

export function applyPokemonLearnsetClipboardOwnedValues(
  move: PokemonLearnsetMove,
  values: readonly RowClipboardOwnedValue[]
): PokemonLearnsetMove {
  const byField = collectOwnedValues(values);
  const level = readOwnedInteger(byField, 'level', move.level, 'unsignedInteger');
  const moveId = readOwnedInteger(byField, 'moveId', move.moveId, 'unsignedInteger');
  return Object.freeze({
    ...move,
    level: level!,
    levelLabel: level === move.level ? move.levelLabel : null,
    moveId: moveId!
  });
}

export function createEncounterClipboardRow(
  game: ProjectGame,
  tableId: string,
  slot: EncounterSlotRecord
): RowClipboardLogicalRowV1 {
  const sourceKey = slot.encounterRecordId
    ? `record:${slot.encounterRecordId}`
    : slot.encounterDataId
      ? `data:${slot.encounterDataId}`
      : `${tableId}#${slot.slot}`;
  if (game !== 'za') {
    return logicalRow(rowClipboardEditorSchemas.encounterSlot.rowKind, sourceKey, [
      signed('speciesId', slot.speciesId),
      signed('form', slot.form),
      signed('levelMin', slot.levelMin),
      signed('levelMax', slot.levelMax),
      signed('probability', slot.weight)
    ]);
  }

  const values: RowClipboardOwnedValue[] = [
    signed('speciesId', slot.speciesId),
    signed('form', slot.form),
    signed('levelMin', slot.levelMin),
    signed('levelMax', slot.levelMax)
  ];
  addOptional(values, 'weight', slot.canEditWeight === true ? slot.weight : null);
  addOptional(
    values,
    'slotMaxCount',
    slot.canEditSlotMaxCount === true ? slot.slotMaxCount : null
  );
  if (slot.canEditAppearanceCounts === true) {
    addOptional(values, 'appearanceMinCount', slot.appearanceMinCount);
    addOptional(values, 'appearanceMaxCount', slot.appearanceMaxCount);
  }
  if (slot.hasAlphaChance === true) {
    addOptional(values, 'alphaChancePercent', slot.alphaChancePercent);
    addOptional(values, 'alphaLevelBonus', slot.alphaLevelBonus);
  }
  addOptional(values, 'heldItemId', slot.heldItemId);
  addOptional(values, 'ability', slot.ability);
  addOptional(values, 'nature', slot.nature);
  addOptional(values, 'gender', slot.gender);
  addOptional(values, 'shinyLock', slot.shinyMode);
  if (slot.moveIds) {
    const moves = [...slot.moveIds, 0, 0, 0, 0].slice(0, 4);
    addOptional(values, 'move1Id', moves[0]);
    addOptional(values, 'move2Id', moves[1]);
    addOptional(values, 'move3Id', moves[2]);
    addOptional(values, 'move4Id', moves[3]);
  }
  addOptional(values, 'flawlessIvCount', slot.flawlessIvCount);
  addOptional(values, 'ivHp', slot.ivHp);
  addOptional(values, 'ivAttack', slot.ivAttack);
  addOptional(values, 'ivDefense', slot.ivDefense);
  addOptional(values, 'ivSpecialAttack', slot.ivSpecialAttack);
  addOptional(values, 'ivSpecialDefense', slot.ivSpecialDefense);
  addOptional(values, 'ivSpeed', slot.ivSpeed);
  if (slot.canEditStrengthenValues === true) {
    addOptional(values, 'strengthenHp', slot.strengthenHp);
    addOptional(values, 'strengthenAttack', slot.strengthenAttack);
    addOptional(values, 'strengthenDefense', slot.strengthenDefense);
    addOptional(values, 'strengthenSpecialAttack', slot.strengthenSpecialAttack);
    addOptional(values, 'strengthenSpecialDefense', slot.strengthenSpecialDefense);
    addOptional(values, 'strengthenSpeed', slot.strengthenSpeed);
  }
  return logicalRow(rowClipboardEditorSchemas.encounterSlot.rowKind, sourceKey, values);
}

export function createEncounterClipboardRowFromFieldValues(
  game: ProjectGame,
  tableId: string,
  slot: EncounterSlotRecord,
  fieldValues: Readonly<Record<string, string>>
): RowClipboardLogicalRowV1 {
  return replaceClipboardRowFieldValues(
    createEncounterClipboardRow(game, tableId, slot),
    fieldValues
  );
}

export function applyEncounterClipboardOwnedValues(
  slot: EncounterSlotRecord,
  values: readonly RowClipboardOwnedValue[]
): EncounterSlotRecord {
  const byField = collectOwnedValues(values);
  const integer = (field: string, fallback: number | null | undefined) =>
    readOwnedInteger(byField, field, fallback ?? null, 'signedInteger');
  const moveFields = ['move1Id', 'move2Id', 'move3Id', 'move4Id'] as const;
  const hasOwnedMoves = moveFields.some((field) => byField.has(field));
  const currentMoves = [...(slot.moveIds ?? []), 0, 0, 0, 0].slice(0, 4);
  const hasOwnedStrengthenValues = [...byField.keys()].some((field) =>
    field.startsWith('strengthen')
  );
  return Object.freeze({
    ...slot,
    ability: integer('ability', slot.ability),
    alphaChancePercent: integer('alphaChancePercent', slot.alphaChancePercent),
    alphaLevelBonus: integer('alphaLevelBonus', slot.alphaLevelBonus),
    appearanceMaxCount: integer('appearanceMaxCount', slot.appearanceMaxCount),
    appearanceMinCount: integer('appearanceMinCount', slot.appearanceMinCount),
    flawlessIvCount: integer('flawlessIvCount', slot.flawlessIvCount),
    form: integer('form', slot.form)!,
    gender: integer('gender', slot.gender),
    heldItemId: integer('heldItemId', slot.heldItemId),
    ivAttack: integer('ivAttack', slot.ivAttack),
    ivDefense: integer('ivDefense', slot.ivDefense),
    ivHp: integer('ivHp', slot.ivHp),
    ivSpecialAttack: integer('ivSpecialAttack', slot.ivSpecialAttack),
    ivSpecialDefense: integer('ivSpecialDefense', slot.ivSpecialDefense),
    ivSpeed: integer('ivSpeed', slot.ivSpeed),
    levelMax: integer('levelMax', slot.levelMax)!,
    levelMin: integer('levelMin', slot.levelMin)!,
    moveIds: hasOwnedMoves
      ? moveFields.map((field, index) => integer(field, currentMoves[index])!)
      : slot.moveIds,
    nature: integer('nature', slot.nature),
    shinyMode: integer('shinyLock', slot.shinyMode),
    slotMaxCount: integer('slotMaxCount', slot.slotMaxCount),
    speciesId: integer('speciesId', slot.speciesId)!,
    strengthenAttack: integer('strengthenAttack', slot.strengthenAttack),
    strengthenDefense: integer('strengthenDefense', slot.strengthenDefense),
    strengthenHp: integer('strengthenHp', slot.strengthenHp),
    strengthenSpecialAttack: integer(
      'strengthenSpecialAttack',
      slot.strengthenSpecialAttack
    ),
    strengthenSpecialDefense: integer(
      'strengthenSpecialDefense',
      slot.strengthenSpecialDefense
    ),
    strengthenSpeed: integer('strengthenSpeed', slot.strengthenSpeed),
    strengthenValueSummary: hasOwnedStrengthenValues
      ? null
      : slot.strengthenValueSummary,
    weight: integer(
      byField.has('probability') ? 'probability' : 'weight',
      slot.weight
    )!
  });
}

export function createTrainerPartyClipboardRow(
  game: ProjectGame,
  trainerId: number,
  member: TrainerPokemonRecord
): RowClipboardLogicalRowV1 {
  const moves = [...member.moveIds, 0, 0, 0, 0].slice(0, 4);
  const values: RowClipboardOwnedValue[] = [
    signed('ability', member.ability),
    signed('evAttack', member.evs.attack),
    signed('evDefense', member.evs.defense),
    signed('evHp', member.evs.hp),
    signed('evSpecialAttack', member.evs.specialAttack),
    signed('evSpecialDefense', member.evs.specialDefense),
    signed('evSpeed', member.evs.speed),
    signed('form', member.form),
    signed('gender', member.gender),
    signed('heldItemId', member.heldItemId),
    signed('ivAttack', member.ivs.attack),
    signed('ivDefense', member.ivs.defense),
    signed('ivHp', member.ivs.hp),
    signed('ivSpecialAttack', member.ivs.specialAttack),
    signed('ivSpecialDefense', member.ivs.specialDefense),
    signed('ivSpeed', member.ivs.speed),
    signed('level', member.level),
    signed('move1Id', moves[0]),
    signed('move2Id', moves[1]),
    signed('move3Id', moves[2]),
    signed('move4Id', moves[3]),
    signed('nature', member.nature),
    booleanValue('shiny', member.shiny),
    signed('speciesId', member.speciesId)
  ];
  if (game === 'sword' || game === 'shield') {
    addOptional(values, 'dynamaxLevel', member.dynamaxLevel);
    if (member.canGigantamax !== null) {
      values.push(booleanValue('canGigantamax', member.canGigantamax));
    }
    if (member.canDynamax !== null) {
      values.push(booleanValue('canDynamax', member.canDynamax));
    }
  } else if ((game === 'scarlet' || game === 'violet') && member.teraType !== null) {
    values.push(signed('teraType', member.teraType));
  }
  return logicalRow(
    rowClipboardEditorSchemas.trainerParty.rowKind,
    `trainer:${requireNonNegativeInteger(trainerId)}:slot:${member.slot}`,
    values
  );
}

export function createTrainerPartyClipboardRowFromFieldValues(
  game: ProjectGame,
  trainerId: number,
  member: TrainerPokemonRecord,
  fieldValues: Readonly<Record<string, string>>
): RowClipboardLogicalRowV1 {
  return replaceClipboardRowFieldValues(
    createTrainerPartyClipboardRow(game, trainerId, member),
    fieldValues
  );
}

export function applyTrainerPartyClipboardOwnedValues(
  member: TrainerPokemonRecord,
  values: readonly RowClipboardOwnedValue[]
): TrainerPokemonRecord {
  const byField = new Map<string, RowClipboardOwnedValue['value']>();
  for (const ownedValue of values) {
    if (byField.has(ownedValue.fieldKey)) {
      throw new RowClipboardError('duplicate-field-key');
    }
    byField.set(ownedValue.fieldKey, ownedValue.value);
  }

  const integer = (field: string, fallback: number | null) => {
    const ownedValue = byField.get(field);
    if (ownedValue === undefined) {
      return fallback;
    }
    if (ownedValue.kind !== 'signedInteger') {
      throw new RowClipboardError('invalid-value');
    }
    const parsed = Number(ownedValue.value);
    if (!Number.isSafeInteger(parsed)) {
      throw new RowClipboardError('invalid-value');
    }
    return parsed;
  };
  const boolean = (field: string, fallback: boolean | null) => {
    const ownedValue = byField.get(field);
    if (ownedValue === undefined) {
      return fallback;
    }
    if (ownedValue.kind !== 'boolean') {
      throw new RowClipboardError('invalid-value');
    }
    return ownedValue.value;
  };

  const moves = [...member.moveIds, 0, 0, 0, 0].slice(0, 4);
  return Object.freeze({
    ...member,
    ability: integer('ability', member.ability)!,
    canDynamax: boolean('canDynamax', member.canDynamax),
    canGigantamax: boolean('canGigantamax', member.canGigantamax),
    dynamaxLevel: integer('dynamaxLevel', member.dynamaxLevel),
    evs: Object.freeze({
      attack: integer('evAttack', member.evs.attack)!,
      defense: integer('evDefense', member.evs.defense)!,
      hp: integer('evHp', member.evs.hp)!,
      specialAttack: integer('evSpecialAttack', member.evs.specialAttack)!,
      specialDefense: integer('evSpecialDefense', member.evs.specialDefense)!,
      speed: integer('evSpeed', member.evs.speed)!
    }),
    form: integer('form', member.form)!,
    gender: integer('gender', member.gender)!,
    heldItemId: integer('heldItemId', member.heldItemId)!,
    ivs: Object.freeze({
      attack: integer('ivAttack', member.ivs.attack)!,
      defense: integer('ivDefense', member.ivs.defense)!,
      hp: integer('ivHp', member.ivs.hp)!,
      specialAttack: integer('ivSpecialAttack', member.ivs.specialAttack)!,
      specialDefense: integer('ivSpecialDefense', member.ivs.specialDefense)!,
      speed: integer('ivSpeed', member.ivs.speed)!
    }),
    level: integer('level', member.level)!,
    moveIds: [
      integer('move1Id', moves[0])!,
      integer('move2Id', moves[1])!,
      integer('move3Id', moves[2])!,
      integer('move4Id', moves[3])!
    ],
    nature: integer('nature', member.nature)!,
    shiny: boolean('shiny', member.shiny)!,
    speciesId: integer('speciesId', member.speciesId)!,
    teraType: integer('teraType', member.teraType)
  });
}

function createFamilyRegistrations(
  editor: RowClipboardEditorSchemaRef,
  pasteModes: readonly ('replace' | 'append')[],
  fields: RowClipboardAdapterRegistration['fieldPolicies']
) {
  return [
    registration(
      editor,
      ['sword', 'shield'],
      rowClipboardProfileIds.swordShield,
      pasteModes,
      fields
    ),
    registration(
      editor,
      ['scarlet', 'violet'],
      rowClipboardProfileIds.scarletViolet,
      pasteModes,
      fields
    ),
    registration(editor, ['za'], rowClipboardProfileIds.za, pasteModes, fields)
  ];
}

function registration(
  editor: RowClipboardEditorSchemaRef,
  games: readonly ProjectGame[],
  profileId: string,
  pasteModes: RowClipboardAdapterRegistration['pasteModes'],
  fieldPolicies: RowClipboardAdapterRegistration['fieldPolicies'],
  maximumRows = 128
): RowClipboardAdapterRegistration {
  return Object.freeze({
    dependencyKinds: [],
    editorId: editor.editorId,
    fieldPolicies: Object.freeze([...fieldPolicies]),
    games: Object.freeze([...games]),
    maximumRows,
    maximumTotalValues: 4096,
    maximumValuesPerRow: 64,
    pasteModes: Object.freeze([...pasteModes]),
    profileIds: Object.freeze([profileId]),
    rowKind: editor.rowKind,
    rowSchemaVersion: editor.rowSchemaVersion
  });
}

function signedField(fieldKey: string) {
  return Object.freeze({
    fieldKey,
    maximumUtf8Bytes: null,
    valueKinds: ['signedInteger'] as const
  });
}

function unsignedField(fieldKey: string) {
  return Object.freeze({
    fieldKey,
    maximumUtf8Bytes: null,
    valueKinds: ['unsignedInteger'] as const
  });
}

function booleanField(fieldKey: string) {
  return Object.freeze({
    fieldKey,
    maximumUtf8Bytes: null,
    valueKinds: ['boolean'] as const
  });
}

function logicalRow(
  kind: string,
  key: string,
  values: readonly RowClipboardOwnedValue[]
): RowClipboardLogicalRowV1 {
  return Object.freeze({
    sourceIdentity: Object.freeze({ key, kind }),
    values: Object.freeze(
      [...values].sort((left, right) =>
        left.fieldKey < right.fieldKey ? -1 : left.fieldKey > right.fieldKey ? 1 : 0
      )
    )
  });
}

function replaceClipboardRowFieldValues(
  source: RowClipboardLogicalRowV1,
  fieldValues: Readonly<Record<string, string>>
): RowClipboardLogicalRowV1 {
  return logicalRow(
    source.sourceIdentity.kind,
    source.sourceIdentity.key,
    source.values.map((ownedValue) => {
      if (!Object.hasOwn(fieldValues, ownedValue.fieldKey)) {
        return ownedValue;
      }
      const draft = fieldValues[ownedValue.fieldKey];
      if (draft === undefined) {
        return ownedValue;
      }
      return Object.freeze({
        fieldKey: ownedValue.fieldKey,
        value: parseClipboardDraftValue(ownedValue.value.kind, draft)
      });
    })
  );
}

function parseClipboardDraftValue(
  kind: RowClipboardOwnedValue['value']['kind'],
  value: string
): RowClipboardOwnedValue['value'] {
  const normalized = value.trim();
  if (kind === 'boolean') {
    if (normalized === '1' || normalized === 'true') {
      return Object.freeze({ kind, value: true });
    }
    if (normalized === '0' || normalized === 'false') {
      return Object.freeze({ kind, value: false });
    }
    throw new RowClipboardError('invalid-value');
  }
  if (
    (kind !== 'signedInteger' && kind !== 'unsignedInteger') ||
    !/^-?[0-9]+$/u.test(normalized)
  ) {
    throw new RowClipboardError('invalid-value');
  }
  const parsed = Number(normalized);
  if (!Number.isSafeInteger(parsed) || (kind === 'unsignedInteger' && parsed < 0)) {
    throw new RowClipboardError('invalid-value');
  }
  return Object.freeze({ kind, value: String(parsed) });
}

function collectOwnedValues(values: readonly RowClipboardOwnedValue[]) {
  const byField = new Map<string, RowClipboardOwnedValue['value']>();
  for (const ownedValue of values) {
    if (byField.has(ownedValue.fieldKey)) {
      throw new RowClipboardError('duplicate-field-key');
    }
    byField.set(ownedValue.fieldKey, ownedValue.value);
  }
  return byField;
}

function readOwnedInteger(
  byField: ReadonlyMap<string, RowClipboardOwnedValue['value']>,
  field: string,
  fallback: number | null,
  expectedKind: 'signedInteger' | 'unsignedInteger'
) {
  const ownedValue = byField.get(field);
  if (ownedValue === undefined) {
    return fallback;
  }
  if (ownedValue.kind !== expectedKind) {
    throw new RowClipboardError('invalid-value');
  }
  const parsed = Number(ownedValue.value);
  if (
    !Number.isSafeInteger(parsed) ||
    (expectedKind === 'unsignedInteger' && parsed < 0)
  ) {
    throw new RowClipboardError('invalid-value');
  }
  return parsed;
}

function signed(fieldKey: string, value: number): RowClipboardOwnedValue {
  if (!Number.isSafeInteger(value)) {
    throw new RowClipboardError('invalid-value');
  }
  return Object.freeze({
    fieldKey,
    value: Object.freeze({ kind: 'signedInteger' as const, value: String(value) })
  });
}

function unsigned(fieldKey: string, value: number): RowClipboardOwnedValue {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new RowClipboardError('invalid-value');
  }
  return Object.freeze({
    fieldKey,
    value: Object.freeze({ kind: 'unsignedInteger' as const, value: String(value) })
  });
}

function booleanValue(fieldKey: string, value: boolean): RowClipboardOwnedValue {
  return Object.freeze({
    fieldKey,
    value: Object.freeze({ kind: 'boolean' as const, value })
  });
}

function addOptional(
  values: RowClipboardOwnedValue[],
  fieldKey: string,
  value: number | null | undefined
) {
  if (value !== null && value !== undefined) {
    values.push(signed(fieldKey, value));
  }
}

function requireNonNegativeInteger(value: number): number {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new RowClipboardError('invalid-logical-identity');
  }
  return value;
}
