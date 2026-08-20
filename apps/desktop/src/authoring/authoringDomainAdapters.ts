/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  ItemRecord,
  ItemsWorkflow,
  MovesWorkflow,
  PokemonRecord,
  PokemonWorkflow,
  ProjectGame,
  TrainerPokemonRecord,
  TrainersWorkflow
} from '../bridge/contracts';
import { getEditableMoveFieldValue } from '../movesEditor';
import {
  projectGameToFamily,
  semanticRecordRefKey,
  type SemanticRecordRef
} from '../workbench/semanticContracts';
import type {
  AdvancedAuthoringSourceBinding,
  AuthoringDomainWorkspace,
  AuthoringFieldDescriptor,
  AuthoringRecordSnapshot
} from './advancedAuthoringTypes';
import {
  getAdvancedAuthoringAdapter,
  type AdvancedAuthoringAdapterRegistration,
  type AuthoringFieldPolicy
} from './authoringAdapterRegistry';

type LiveEditableField = {
  field: string;
  isReadOnly?: boolean;
  label: string;
  maximumValue: number | null;
  minimumValue: number | null;
  options?: readonly { label: string; value: number }[];
  valueKind: string;
};

export type CreateAuthoringWorkspaceOptions<TWorkflow> = {
  game: ProjectGame;
  projectId: string;
  sourceBinding: AdvancedAuthoringSourceBinding | null;
  workflow: TWorkflow;
};

export function createItemsAuthoringWorkspace(
  options: CreateAuthoringWorkspaceOptions<ItemsWorkflow>
) {
  return createWorkspace({
    ...options,
    adapterId: 'items.scalar.v1',
    liveFields: options.workflow.editableFields,
    records: options.workflow.items,
    toRecord: (item, registration, fields) =>
      createRecordSnapshot(
        registration,
        createRecordRef(registration, options.game, item.itemId.toString(), null),
        item.name,
        fields,
        (fieldKey) => getItemFieldValue(item, fieldKey)
      )
  });
}

export function createPokemonAuthoringWorkspace(
  options: CreateAuthoringWorkspaceOptions<PokemonWorkflow>
) {
  return createWorkspace({
    ...options,
    adapterId: 'pokemon.personal.v1',
    liveFields: options.workflow.editableFields,
    records: options.workflow.pokemon,
    toRecord: (pokemon, registration, fields) =>
      createRecordSnapshot(
        registration,
        createRecordRef(registration, options.game, pokemon.personalId.toString(), null),
        pokemon.name,
        fields,
        (fieldKey) => getPokemonFieldValue(pokemon, fieldKey)
      )
  });
}

export function createMovesAuthoringWorkspace(
  options: CreateAuthoringWorkspaceOptions<MovesWorkflow>
) {
  return createWorkspace({
    ...options,
    adapterId: 'moves.core.v1',
    liveFields: options.workflow.editableFields,
    records: options.workflow.moves,
    toRecord: (move, registration, fields) =>
      createRecordSnapshot(
        registration,
        createRecordRef(registration, options.game, move.moveId.toString(), null),
        move.name,
        fields,
        (fieldKey) => getEditableMoveFieldValue(move, fieldKey)
      )
  });
}

export function createTrainerPartyAuthoringWorkspace(
  options: CreateAuthoringWorkspaceOptions<TrainersWorkflow>
) {
  const records = options.workflow.trainers.flatMap((trainer) =>
    trainer.team
      .filter(
        (pokemon) =>
          pokemon.speciesId > 0 &&
          Number.isSafeInteger(pokemon.slot) &&
          pokemon.slot >= 0 &&
          pokemon.slot <= 5
      )
      .map((pokemon) => ({ pokemon, trainer }))
  );

  return createWorkspace({
    ...options,
    adapterId: 'trainers.party.v1',
    liveFields: options.workflow.editableFields,
    records,
    toRecord: ({ pokemon, trainer }, registration, fields) =>
      createRecordSnapshot(
        registration,
        createRecordRef(
          registration,
          options.game,
          trainer.trainerId.toString(),
          `party-slot:${pokemon.slot}`
        ),
        `${trainer.name} / ${pokemon.species}`,
        fields,
        (fieldKey) => getTrainerPokemonFieldValue(pokemon, fieldKey)
      )
  });
}

type CreateWorkspaceOptions<TRecord> = {
  adapterId: string;
  game: ProjectGame;
  liveFields: readonly LiveEditableField[];
  projectId: string;
  records: readonly TRecord[];
  sourceBinding: AdvancedAuthoringSourceBinding | null;
  toRecord: (
    record: TRecord,
    registration: AdvancedAuthoringAdapterRegistration,
    fields: readonly AuthoringFieldDescriptor[]
  ) => AuthoringRecordSnapshot | null;
};

function createWorkspace<TRecord>(
  options: CreateWorkspaceOptions<TRecord>
): AuthoringDomainWorkspace | null {
  const registration = getAdvancedAuthoringAdapter(options.adapterId);
  if (!registration || !registration.games.includes(options.game)) {
    return null;
  }
  if (!options.sourceBinding) {
    return null;
  }
  assertSourceBindingScope(options.projectId, options.sourceBinding);
  const fields = createAuthoringFields(registration, options.liveFields);
  if (fields.length === 0) {
    return null;
  }

  const records = options.records
    .map((record) => options.toRecord(record, registration, fields))
    .filter((record): record is AuthoringRecordSnapshot => record !== null);
  if (records.length === 0) {
    return null;
  }
  if (!recordsAreUnique(records)) {
    return null;
  }
  return {
    adapterId: registration.id,
    fields,
    game: options.game,
    pasteSpecialGroups: registration.pasteSpecialGroups
      .filter((group) =>
        group.fieldKeys.every((fieldKey) =>
          fields.some((field) => field.fieldKey === fieldKey)
        )
      )
      .map((group) => ({ fieldKeys: group.fieldKeys, id: group.id })),
    records,
    sourceBinding: options.sourceBinding
  };
}

function createAuthoringFields(
  registration: AdvancedAuthoringAdapterRegistration,
  liveFields: readonly LiveEditableField[]
) {
  const liveFieldsByKey = new Map(liveFields.map((field) => [field.field, field]));
  return registration.fieldPolicies.flatMap((policy) => {
    const liveField = liveFieldsByKey.get(policy.fieldKey);
    const field = liveField ? createAuthoringField(policy, liveField) : null;
    return field ? [field] : [];
  });
}

function createAuthoringField(
  policy: AuthoringFieldPolicy,
  liveField: LiveEditableField
): AuthoringFieldDescriptor | null {
  if (liveField.isReadOnly === true) {
    return null;
  }
  if (
    (liveField.minimumValue !== null && !Number.isFinite(liveField.minimumValue)) ||
    (liveField.maximumValue !== null && !Number.isFinite(liveField.maximumValue)) ||
    (liveField.minimumValue !== null &&
      liveField.maximumValue !== null &&
      liveField.minimumValue > liveField.maximumValue)
  ) {
    return null;
  }

  const options = liveField.options ?? [];
  const requiresIntegerValues =
    policy.valueKind === 'integer' ||
    policy.valueKind === 'enum' ||
    policy.valueKind === 'boolean';
  if (
    options.some((option) => !Number.isFinite(option.value)) ||
    (requiresIntegerValues &&
      options.some((option) => !Number.isSafeInteger(option.value))) ||
    (requiresIntegerValues &&
      liveField.minimumValue !== null &&
      !Number.isSafeInteger(liveField.minimumValue)) ||
    (requiresIntegerValues &&
      liveField.maximumValue !== null &&
      !Number.isSafeInteger(liveField.maximumValue)) ||
    new Set(options.map((option) => option.value)).size !== options.length ||
    (policy.valueKind === 'enum' && options.length === 0) ||
    (policy.valueKind === 'boolean' && liveField.valueKind !== 'boolean') ||
    ((policy.valueKind === 'integer' || policy.valueKind === 'enum') &&
      liveField.valueKind !== 'integer') ||
    (policy.valueKind === 'number' && liveField.valueKind !== 'decimal')
  ) {
    return null;
  }

  return {
    fieldKey: policy.fieldKey,
    label: liveField.label,
    maximumValue: liveField.maximumValue,
    minimumValue: liveField.minimumValue,
    options: options.map((option) => ({ label: option.label, value: option.value })),
    supportedTransforms: policy.supportedTransforms,
    valueKind: policy.valueKind
  };
}

function createRecordSnapshot(
  registration: AdvancedAuthoringAdapterRegistration,
  record: SemanticRecordRef,
  displayName: string,
  fields: readonly AuthoringFieldDescriptor[],
  getValue: (fieldKey: string) => number | null | undefined
): AuthoringRecordSnapshot | null {
  const fieldValues = Object.fromEntries(
    fields.flatMap((field) => {
      const value = getValue(field.fieldKey);
      return typeof value === 'number' && authoringFieldValueIsValid(field, value)
        ? [[field.fieldKey, value] as const]
        : [];
    })
  );
  if (Object.keys(fieldValues).length !== fields.length) {
    return null;
  }
  return { adapterId: registration.id, displayName, fieldValues, record };
}

function authoringFieldValueIsValid(
  field: AuthoringFieldDescriptor,
  value: number
) {
  return (
    Number.isFinite(value) &&
    ((field.valueKind !== 'integer' &&
      field.valueKind !== 'enum' &&
      field.valueKind !== 'boolean') ||
      Number.isSafeInteger(value)) &&
    (field.valueKind !== 'boolean' || value === 0 || value === 1) &&
    (field.options.length === 0 ||
      field.options.some((option) => option.value === value)) &&
    (field.minimumValue === null || value >= field.minimumValue) &&
    (field.maximumValue === null || value <= field.maximumValue)
  );
}

function createRecordRef(
  registration: AdvancedAuthoringAdapterRegistration,
  game: ProjectGame,
  recordId: string,
  subrecordId: string | null
): SemanticRecordRef {
  return {
    domain: registration.domain,
    gameFamily: projectGameToFamily(game),
    recordId,
    recordKind: {
      key: registration.recordKind,
      schemaVersion: registration.recordKindSchemaVersion
    },
    subrecordId
  };
}

function assertSourceBindingScope(
  projectId: string,
  binding: AdvancedAuthoringSourceBinding
) {
  if (
    binding.version !== 1 ||
    binding.projectId !== projectId ||
    projectId.length === 0 ||
    projectId.length > 128 ||
    projectId !== projectId.trim() ||
    /\p{Cc}/u.test(projectId) ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.workspaceETag) ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.workspaceFingerprint) ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.outputRootFingerprint) ||
    !validOutputMode(binding.outputMode) ||
    binding.selectedChangeSetIds.length > 64 ||
    new Set(binding.selectedChangeSetIds).size !==
      binding.selectedChangeSetIds.length ||
    binding.selectedChangeSetIds.some((id) => !isAssociationId(id)) ||
    (binding.outputProfileId !== null &&
      !isAssociationId(binding.outputProfileId)) ||
    (binding.outputProfileId === null
      ? binding.workspacePersonalStateETag !== null
      : !/^[A-Fa-f0-9]{64}$/u.test(binding.workspacePersonalStateETag ?? ''))
  ) {
    throw new Error('Authoring workspace binding is invalid.');
  }
}

function validOutputMode(value: AdvancedAuthoringSourceBinding['outputMode']) {
  return (
    value === null ||
    value === 'standalone' ||
    value === 'trinityModManager' ||
    value === 'trinityBypass'
  );
}

function isAssociationId(value: string) {
  return (
    value.length >= 1 &&
    value.length <= 128 &&
    /^[A-Za-z0-9][A-Za-z0-9._-]*$/u.test(value)
  );
}

function recordsAreUnique(records: readonly AuthoringRecordSnapshot[]) {
  const keys = records.map((record) => semanticRecordRefKey(record.record));
  return new Set(keys).size === keys.length;
}

function getItemFieldValue(item: ItemRecord, fieldKey: string) {
  const explicitValue = item.fieldValues?.[fieldKey];
  if (explicitValue !== undefined) {
    return explicitValue;
  }
  switch (fieldKey) {
    case 'flingPower':
      return item.metadata.flingPower;
    case 'healAmount':
      return item.metadata.healAmount;
    case 'ppGain':
      return item.metadata.ppGain;
    case 'friendshipGain1':
      return item.metadata.friendshipGain1;
    case 'friendshipGain2':
      return item.metadata.friendshipGain2;
    case 'friendshipGain3':
      return item.metadata.friendshipGain3;
    default:
      return null;
  }
}

function getPokemonFieldValue(pokemon: PokemonRecord, fieldKey: string) {
  switch (fieldKey) {
    case 'hp':
      return pokemon.baseStats.hp;
    case 'attack':
      return pokemon.baseStats.attack;
    case 'defense':
      return pokemon.baseStats.defense;
    case 'specialAttack':
      return pokemon.baseStats.specialAttack;
    case 'specialDefense':
      return pokemon.baseStats.specialDefense;
    case 'speed':
      return pokemon.baseStats.speed;
    case 'catchRate':
      return pokemon.catchRate;
    case 'baseExperience':
      return pokemon.baseExperience;
    case 'baseFriendship':
      return pokemon.personal.baseFriendship;
    case 'height':
      return pokemon.height;
    case 'weight':
      return pokemon.weight;
    case 'genderRatio':
      return pokemon.genderRatio;
    case 'type1':
      return pokemon.personal.type1;
    case 'type2':
      return pokemon.personal.type2;
    case 'ability1':
      return pokemon.abilities.ability1;
    case 'ability2':
      return pokemon.abilities.ability2;
    case 'hiddenAbility':
      return pokemon.abilities.hiddenAbility;
    default:
      return null;
  }
}

function getTrainerPokemonFieldValue(pokemon: TrainerPokemonRecord, fieldKey: string) {
  switch (fieldKey) {
    case 'level':
      return pokemon.level;
    case 'heldItemId':
      return pokemon.heldItemId;
    case 'move1Id':
      return pokemon.moveIds[0] ?? null;
    case 'move2Id':
      return pokemon.moveIds[1] ?? null;
    case 'move3Id':
      return pokemon.moveIds[2] ?? null;
    case 'move4Id':
      return pokemon.moveIds[3] ?? null;
    case 'gender':
      return pokemon.gender;
    case 'ability':
      return pokemon.ability;
    case 'nature':
      return pokemon.nature;
    case 'evHp':
      return pokemon.evs.hp;
    case 'evAttack':
      return pokemon.evs.attack;
    case 'evDefense':
      return pokemon.evs.defense;
    case 'evSpecialAttack':
      return pokemon.evs.specialAttack;
    case 'evSpecialDefense':
      return pokemon.evs.specialDefense;
    case 'evSpeed':
      return pokemon.evs.speed;
    case 'ivHp':
      return pokemon.ivs.hp;
    case 'ivAttack':
      return pokemon.ivs.attack;
    case 'ivDefense':
      return pokemon.ivs.defense;
    case 'ivSpecialAttack':
      return pokemon.ivs.specialAttack;
    case 'ivSpecialDefense':
      return pokemon.ivs.specialDefense;
    case 'ivSpeed':
      return pokemon.ivs.speed;
    default:
      return null;
  }
}
