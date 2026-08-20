/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import type { WorkbenchSection } from '../workbench/workbenchSections';
import type {
  AuthoringFieldValueKind,
  AuthoringPasteSpecialGroup,
  AuthoringRecordSnapshot,
  AuthoringRelativeTransformKind
} from './advancedAuthoringTypes';

export type AuthoringFieldPolicy = {
  fieldKey: string;
  supportedTransforms: readonly AuthoringRelativeTransformKind[];
  valueKind: AuthoringFieldValueKind;
};

export type AuthoringProjectionValidationContext = {
  changedFieldKeys: ReadonlySet<string>;
  projectedValues: Readonly<Record<string, number>>;
  record: AuthoringRecordSnapshot;
};

export type AdvancedAuthoringAdapterRegistration = {
  domain: string;
  fieldPolicies: readonly AuthoringFieldPolicy[];
  games: readonly ProjectGame[];
  id: string;
  pasteSpecialGroups: readonly AuthoringPasteSpecialGroup[];
  recordKind: string;
  recordKindSchemaVersion: number;
  section: WorkbenchSection;
  validateProjection?: (context: AuthoringProjectionValidationContext) => boolean;
};

const allGames = ['sword', 'shield', 'scarlet', 'violet', 'za'] as const;
const trainerBatchGames = ['scarlet', 'violet', 'za'] as const;
const numericTransforms = ['add', 'multiply', 'clamp'] as const;
const boundedOffsetTransforms = ['add', 'clamp'] as const;

const pokemonBaseStatFields = [
  'hp',
  'attack',
  'defense',
  'specialAttack',
  'specialDefense',
  'speed'
] as const;
const trainerEvFields = [
  'evHp',
  'evAttack',
  'evDefense',
  'evSpecialAttack',
  'evSpecialDefense',
  'evSpeed'
] as const;
const trainerIvFields = [
  'ivHp',
  'ivAttack',
  'ivDefense',
  'ivSpecialAttack',
  'ivSpecialDefense',
  'ivSpeed'
] as const;
const trainerMoveFields = ['move1Id', 'move2Id', 'move3Id', 'move4Id'] as const;

const registrations = [
  registration({
    domain: 'workflow.items',
    fieldPolicies: [
      numeric('flingPower'),
      numeric('healAmount'),
      numeric('ppGain'),
      numeric('friendshipGain1'),
      numeric('friendshipGain2'),
      numeric('friendshipGain3')
    ],
    games: allGames,
    id: 'items.scalar.v1',
    pasteSpecialGroups: [],
    recordKind: 'item',
    section: 'items'
  }),
  registration({
    domain: 'workflow.pokemon',
    fieldPolicies: [
      ...pokemonBaseStatFields.map(numeric),
      numeric('catchRate'),
      numeric('baseExperience'),
      numeric('baseFriendship'),
      numeric('height'),
      numeric('weight'),
      replaceOnly('genderRatio'),
      replaceOnly('type1', 'enum'),
      replaceOnly('type2', 'enum'),
      replaceOnly('ability1', 'enum'),
      replaceOnly('ability2', 'enum'),
      replaceOnly('hiddenAbility', 'enum')
    ],
    games: allGames,
    id: 'pokemon.personal.v1',
    pasteSpecialGroups: [
      group('base-stats', pokemonBaseStatFields),
      group('types', ['type1', 'type2']),
      group('abilities', ['ability1', 'ability2', 'hiddenAbility'])
    ],
    recordKind: 'pokemon-personal',
    section: 'pokemon'
  }),
  registration({
    domain: 'workflow.moves',
    fieldPolicies: [
      replaceOnly('type', 'enum'),
      replaceOnly('category', 'enum'),
      numeric('power'),
      numeric('accuracy'),
      numeric('pp'),
      offset('priority'),
      offset('critStage'),
      numeric('maxMovePower'),
      numeric('flinch')
    ],
    games: allGames,
    id: 'moves.core.v1',
    pasteSpecialGroups: [
      group('core-stats', ['type', 'category', 'power', 'accuracy', 'pp', 'priority']),
      group('effect-chances', ['accuracy', 'flinch'])
    ],
    recordKind: 'move',
    section: 'moves'
  }),
  registration({
    domain: 'workflow.trainers',
    fieldPolicies: [
      offset('level'),
      replaceOnly('heldItemId', 'enum'),
      ...trainerMoveFields.map((fieldKey) => replaceOnly(fieldKey, 'enum')),
      replaceOnly('gender', 'enum'),
      replaceOnly('ability', 'enum'),
      replaceOnly('nature', 'enum'),
      ...trainerEvFields.map(offset),
      ...trainerIvFields.map(offset)
    ],
    games: trainerBatchGames,
    id: 'trainers.party.v1',
    pasteSpecialGroups: [
      group('moves', trainerMoveFields),
      group('evs', trainerEvFields),
      group('ivs', trainerIvFields),
      group('held-item', ['heldItemId']),
      group('traits', ['gender', 'ability', 'nature'])
    ],
    recordKind: 'trainer',
    section: 'trainers',
    validateProjection: validateTrainerPartyProjection
  })
] as const satisfies readonly AdvancedAuthoringAdapterRegistration[];

const registrationsById = new Map<string, AdvancedAuthoringAdapterRegistration>();
for (const candidate of registrations) {
  if (registrationsById.has(candidate.id)) {
    throw new Error('Advanced authoring adapter ids must be unique.');
  }
  registrationsById.set(candidate.id, candidate);
}

export const advancedAuthoringAdapterRegistry: readonly AdvancedAuthoringAdapterRegistration[] =
  registrations;

export function getAdvancedAuthoringAdapter(adapterId: string) {
  return registrationsById.get(adapterId) ?? null;
}

export function getAdvancedAuthoringAdaptersForSection(
  section: WorkbenchSection,
  game: ProjectGame
) {
  return registrations.filter(
    (candidate) => candidate.section === section && candidate.games.includes(game)
  );
}

function registration(
  value: Omit<AdvancedAuthoringAdapterRegistration, 'recordKindSchemaVersion'>
): AdvancedAuthoringAdapterRegistration {
  return Object.freeze({
    ...value,
    fieldPolicies: Object.freeze([...value.fieldPolicies]),
    pasteSpecialGroups: Object.freeze([...value.pasteSpecialGroups]),
    recordKindSchemaVersion: 1
  });
}

function numeric(fieldKey: string): AuthoringFieldPolicy {
  return policy(fieldKey, 'integer', numericTransforms);
}

function offset(fieldKey: string): AuthoringFieldPolicy {
  return policy(fieldKey, 'integer', boundedOffsetTransforms);
}

function replaceOnly(
  fieldKey: string,
  valueKind: AuthoringFieldValueKind = 'integer'
): AuthoringFieldPolicy {
  return policy(fieldKey, valueKind, []);
}

function policy(
  fieldKey: string,
  valueKind: AuthoringFieldValueKind,
  supportedTransforms: readonly AuthoringRelativeTransformKind[]
): AuthoringFieldPolicy {
  return Object.freeze({ fieldKey, supportedTransforms, valueKind });
}

function group(id: string, fieldKeys: readonly string[]): AuthoringPasteSpecialGroup {
  return Object.freeze({ fieldKeys: Object.freeze([...fieldKeys]), id });
}

function validateTrainerPartyProjection(context: AuthoringProjectionValidationContext) {
  if (!trainerEvFields.some((fieldKey) => context.changedFieldKeys.has(fieldKey))) {
    return true;
  }

  const values = trainerEvFields.map((fieldKey) => context.projectedValues[fieldKey]);
  return (
    values.every((value) => value === undefined || Number.isSafeInteger(value)) &&
    values.reduce<number>((total, value) => total + Math.max(0, value ?? 0), 0) <= 510
  );
}
